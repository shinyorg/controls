namespace Shiny.Controls.Diagramming;

/// <summary>
/// Layered directed-graph layout - the Sugiyama pipeline.
/// </summary>
/// <remarks>
/// <para>
/// This is the layout for a flowchart rather than a tree: a decision whose branches rejoin later, a
/// process with a loop back to an earlier step, a graph where a node has two parents. Feeding any of
/// those to <see cref="TreeLayout"/> works, but only by throwing away every edge that is not part of
/// the spanning tree, so the drawing stops matching the graph.
/// </para>
/// <para>
/// Five passes, in the usual order: break cycles by reversing back edges, assign layers by longest
/// path, thread long edges through dummy nodes, reorder within layers to cut crossings, then place.
/// The pipeline is adapted from the one already in <c>Shiny.Maui.Controls.MermaidDiagrams</c>, with
/// three changes that mattered here - the depth-first pass that finds back edges runs on an explicit
/// stack, placement uses each node's real width instead of a fixed slot per column, and there is a
/// straightening pass afterwards. <b>The two copies are independent</b>; the mermaid control has its
/// own parser and themes wrapped around its copy and is deliberately left alone, so a fix made here
/// does not reach it.
/// </para>
/// </remarks>
public sealed class LayeredLayout : IDiagramLayout
{
    /// <summary>Barycenter sweeps. Four is where the crossing count stops improving on real graphs.</summary>
    const int OrderingPasses = 4;

    /// <summary>Straightening sweeps run after ordering is fixed.</summary>
    const int StraighteningPasses = 4;

    /// <inheritdoc />
    public void Arrange(DiagramLayoutContext context)
    {
        context.EnsureSizes();

        if (context.Nodes.Count == 0)
            return;

        var options = context.Options;
        var graph = LayeredGraph.Build(context.Nodes, context.Connections, options);

        if (graph.Order.Count == 0)
            return;

        AssignLayers(graph);
        graph.InsertDummies();
        MinimiseCrossings(graph);
        Place(graph, options);
        graph.WriteBack(options);
    }

    /// <summary>
    /// Longest path from every source. Nodes with nothing pointing at them start at layer zero and
    /// each edge pushes its target at least one layer further down.
    /// </summary>
    static void AssignLayers(LayeredGraph graph)
    {
        var inDegree = new Dictionary<string, int>(graph.Order.Count, StringComparer.Ordinal);

        foreach (var id in graph.Order)
            inDegree[id] = 0;

        foreach (var (_, target) in graph.Edges)
            inDegree[target]++;

        var queue = new Queue<string>();

        foreach (var id in graph.Order)
        {
            if (inDegree[id] == 0)
            {
                graph.Layers[id] = 0;
                queue.Enqueue(id);
            }
        }

        // Every node sitting on a cycle the reversal pass could not open. Seeding the first one keeps
        // the graph laid out instead of stacked at the origin.
        if (queue.Count == 0)
        {
            graph.Layers[graph.Order[0]] = 0;
            queue.Enqueue(graph.Order[0]);
        }

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            var layer = graph.Layers[id];

            foreach (var target in graph.Successors(id))
            {
                if (!graph.Layers.TryGetValue(target, out var existing) || existing < layer + 1)
                    graph.Layers[target] = layer + 1;

                if (--inDegree[target] <= 0)
                    queue.Enqueue(target);
            }
        }

        foreach (var id in graph.Order)
            graph.Layers.TryAdd(id, 0);
    }

    /// <summary>
    /// Barycenter heuristic, sweeping down then up. Each node moves to the average position of its
    /// neighbours in the adjacent layer, which is a cheap and famously effective proxy for cutting
    /// edge crossings.
    /// </summary>
    static void MinimiseCrossings(LayeredGraph graph)
    {
        for (var pass = 0; pass < OrderingPasses; pass++)
        {
            for (var layer = 1; layer < graph.LayerLists.Count; layer++)
                SortByBarycenter(graph.LayerLists[layer], graph.LayerLists[layer - 1], graph.Predecessors);

            for (var layer = graph.LayerLists.Count - 2; layer >= 0; layer--)
                SortByBarycenter(graph.LayerLists[layer], graph.LayerLists[layer + 1], graph.SuccessorMap);
        }
    }

    static void SortByBarycenter(
        List<string> current,
        List<string> reference,
        Dictionary<string, List<string>> neighbours
    )
    {
        if (current.Count < 2)
            return;

        var positions = new Dictionary<string, int>(reference.Count, StringComparer.Ordinal);
        for (var i = 0; i < reference.Count; i++)
            positions[reference[i]] = i;

        var keys = new Dictionary<string, double>(current.Count, StringComparer.Ordinal);
        var order = new Dictionary<string, int>(current.Count, StringComparer.Ordinal);

        for (var i = 0; i < current.Count; i++)
        {
            var id = current[i];
            order[id] = i;

            var sum = 0d;
            var count = 0;

            if (neighbours.TryGetValue(id, out var list))
            {
                foreach (var neighbour in list)
                {
                    if (positions.TryGetValue(neighbour, out var p))
                    {
                        sum += p;
                        count++;
                    }
                }
            }

            // A node with no neighbour in the reference layer has no opinion, so it keeps its current
            // position rather than being swept to one end.
            keys[id] = count > 0 ? sum / count : i;
        }

        // Ties broken by the existing index, which is what makes this stable and therefore identical
        // on both hosts. List.Sort is introsort and is not stable on its own.
        current.Sort((a, b) =>
        {
            var compare = keys[a].CompareTo(keys[b]);
            return compare != 0 ? compare : order[a].CompareTo(order[b]);
        });
    }

    /// <summary>
    /// Places each layer, then straightens. Placement respects the ordering the previous pass
    /// produced and each node's real size; straightening pulls nodes toward the average of their
    /// neighbours without ever letting two swap or overlap.
    /// </summary>
    static void Place(LayeredGraph graph, DiagramLayoutOptions options)
    {
        foreach (var layer in graph.LayerLists)
        {
            var cursor = 0d;

            foreach (var id in layer)
            {
                var size = graph.AcrossSize(id);
                graph.Across[id] = cursor + (size / 2);
                cursor += size + options.NodeSpacing;
            }
        }

        for (var pass = 0; pass < StraighteningPasses; pass++)
        {
            for (var layer = 1; layer < graph.LayerLists.Count; layer++)
                Straighten(graph, graph.LayerLists[layer], graph.Predecessors, options);

            for (var layer = graph.LayerLists.Count - 2; layer >= 0; layer--)
                Straighten(graph, graph.LayerLists[layer], graph.SuccessorMap, options);
        }

        AssignDepths(graph, options);
    }

    static void Straighten(
        LayeredGraph graph,
        List<string> layer,
        Dictionary<string, List<string>> neighbours,
        DiagramLayoutOptions options
    )
    {
        if (layer.Count == 0)
            return;

        var desired = new double[layer.Count];

        for (var i = 0; i < layer.Count; i++)
        {
            var id = layer[i];
            var sum = 0d;
            var count = 0;

            if (neighbours.TryGetValue(id, out var list))
            {
                foreach (var neighbour in list)
                {
                    if (graph.Across.TryGetValue(neighbour, out var across))
                    {
                        sum += across;
                        count++;
                    }
                }
            }

            desired[i] = count > 0 ? sum / count : graph.Across[layer[i]];
        }

        // Left to right, honouring the minimum gap; then right to left, which lets a node that was
        // pushed right by its left neighbour settle back if there is room on its other side. Running
        // only the first pass biases the whole layer rightwards.
        var cursor = double.MinValue;

        for (var i = 0; i < layer.Count; i++)
        {
            var half = graph.AcrossSize(layer[i]) / 2;
            var position = Math.Max(desired[i], cursor + half);
            graph.Across[layer[i]] = position;
            cursor = position + half + options.NodeSpacing;
        }

        cursor = double.MaxValue;

        for (var i = layer.Count - 1; i >= 0; i--)
        {
            var half = graph.AcrossSize(layer[i]) / 2;
            var position = Math.Min(Math.Max(graph.Across[layer[i]], desired[i]), cursor - half);
            graph.Across[layer[i]] = position;
            cursor = position - half - options.NodeSpacing;
        }
    }

    static void AssignDepths(LayeredGraph graph, DiagramLayoutOptions options)
    {
        var sizes = new double[graph.LayerLists.Count];

        for (var i = 0; i < graph.LayerLists.Count; i++)
        {
            foreach (var id in graph.LayerLists[i])
                sizes[i] = Math.Max(sizes[i], graph.DepthSize(id));
        }

        var running = 0d;

        for (var i = 0; i < graph.LayerLists.Count; i++)
        {
            foreach (var id in graph.LayerLists[i])
                graph.Depth[id] = running + ((sizes[i] - graph.DepthSize(id)) / 2);

            running += sizes[i] + options.LevelSpacing;
        }
    }
}
