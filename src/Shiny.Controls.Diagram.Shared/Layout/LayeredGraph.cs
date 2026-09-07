namespace Shiny.Controls.Diagramming;

/// <summary>
/// The scratch graph a <see cref="LayeredLayout"/> pass works on: an acyclic edge list, a layer per
/// node, dummy nodes threading the long edges, and the two abstract axes everything is placed in.
/// </summary>
sealed class LayeredGraph
{
    /// <summary>An edge as the layout sees it, which may be a reversal of the connection it came from.</summary>
    /// <param name="Source">Where the edge starts, after any reversal.</param>
    /// <param name="Target">Where the edge ends, after any reversal.</param>
    /// <param name="Connection">The connection it came from.</param>
    /// <param name="Reversed">True when the pair was swapped to break a cycle.</param>
    internal readonly record struct LayeredEdge(
        string Source,
        string Target,
        DiagramConnection Connection,
        bool Reversed
    );

    /// <summary>A long edge's dummy chain, kept so the route can be read back off it.</summary>
    /// <param name="Chain">The dummy ids, in layer order along the reversed-or-not edge.</param>
    /// <param name="Reversed">True when the chain runs from the connection's target to its source.</param>
    /// <param name="Connection">The connection to hand the bends to.</param>
    readonly record struct DummyChain(List<string> Chain, bool Reversed, DiagramConnection Connection);

    readonly Dictionary<string, DiagramNode> nodes = new(StringComparer.Ordinal);
    readonly List<LayeredEdge> sourceEdges = [];
    readonly List<string> dummyOrder = [];
    readonly List<DummyChain> dummyChains = [];

    bool vertical = true;

    /// <summary>Real node ids, in the order they were supplied.</summary>
    public List<string> Order { get; } = [];

    /// <summary>Edges after cycle breaking, before dummies are threaded through them.</summary>
    public List<(string Source, string Target)> Edges { get; } = [];

    /// <summary>Which layer each node - real or dummy - sits in.</summary>
    public Dictionary<string, int> Layers { get; } = new(StringComparer.Ordinal);

    /// <summary>Nodes grouped by layer, in the order they are drawn within it.</summary>
    public List<List<string>> LayerLists { get; } = [];

    /// <summary>Outgoing neighbours, after dummies.</summary>
    public Dictionary<string, List<string>> SuccessorMap { get; } = new(StringComparer.Ordinal);

    /// <summary>Incoming neighbours, after dummies.</summary>
    public Dictionary<string, List<string>> Predecessors { get; } = new(StringComparer.Ordinal);

    /// <summary>Position along the sibling axis, as a centre.</summary>
    public Dictionary<string, double> Across { get; } = new(StringComparer.Ordinal);

    /// <summary>Position along the growth axis, as a leading edge.</summary>
    public Dictionary<string, double> Depth { get; } = new(StringComparer.Ordinal);

    /// <summary>Builds the graph and breaks any cycles in it.</summary>
    /// <param name="source">The nodes to arrange.</param>
    /// <param name="connections">The connections between them.</param>
    /// <param name="options">Supplies the growth direction.</param>
    public static LayeredGraph Build(
        IReadOnlyList<DiagramNode> source,
        IReadOnlyList<DiagramConnection> connections,
        DiagramLayoutOptions options
    )
    {
        var graph = new LayeredGraph { vertical = options.IsVertical };

        foreach (var node in source)
        {
            if (graph.nodes.TryAdd(node.Id, node))
                graph.Order.Add(node.Id);
        }

        var raw = new List<LayeredEdge>();

        foreach (var connection in connections)
        {
            // A self-loop has no layer to span and would hold the cycle detector on its own node
            // forever. The router draws it as a lobe off one side instead.
            if (string.Equals(connection.SourceId, connection.TargetId, StringComparison.Ordinal))
                continue;

            if (!graph.nodes.ContainsKey(connection.SourceId) || !graph.nodes.ContainsKey(connection.TargetId))
                continue;

            raw.Add(new LayeredEdge(connection.SourceId, connection.TargetId, connection, false));
        }

        var backEdges = FindBackEdges(graph.Order, raw);

        foreach (var edge in raw)
        {
            var reversed = backEdges.Contains((edge.Source, edge.Target));
            edge.Connection.IsReversed = reversed;

            var resolved = reversed
                ? edge with { Source = edge.Target, Target = edge.Source, Reversed = true }
                : edge;

            graph.sourceEdges.Add(resolved);
            graph.Edges.Add((resolved.Source, resolved.Target));
        }

        return graph;
    }

    /// <summary>
    /// Depth-first back-edge detection, on an explicit stack.
    /// </summary>
    /// <remarks>
    /// Explicit rather than recursive because a ten-thousand-node chain is a perfectly ordinary
    /// import, and a recursive walk over one overflows the stack - which cannot be caught and takes
    /// the process with it. The colouring is the standard one: an edge into a node still on the
    /// current path is a back edge, and reversing it opens the cycle.
    /// </remarks>
    static HashSet<(string, string)> FindBackEdges(List<string> order, List<LayeredEdge> edges)
    {
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var id in order)
            adjacency[id] = [];

        foreach (var edge in edges)
            adjacency[edge.Source].Add(edge.Target);

        var back = new HashSet<(string, string)>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var onPath = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<(string Node, int Index)>();

        foreach (var start in order)
        {
            if (!visited.Add(start))
                continue;

            onPath.Add(start);
            stack.Push((start, 0));

            while (stack.Count > 0)
            {
                var (node, index) = stack.Pop();
                var neighbours = adjacency[node];

                if (index >= neighbours.Count)
                {
                    onPath.Remove(node);
                    continue;
                }

                stack.Push((node, index + 1));
                var next = neighbours[index];

                if (onPath.Contains(next))
                    back.Add((node, next));
                else if (visited.Add(next))
                {
                    onPath.Add(next);
                    stack.Push((next, 0));
                }
            }
        }

        return back;
    }

    /// <summary>Outgoing neighbours of a node in the pre-dummy edge list.</summary>
    /// <param name="id">The node to walk from.</param>
    public IEnumerable<string> Successors(string id)
    {
        foreach (var (source, target) in this.Edges)
        {
            if (string.Equals(source, id, StringComparison.Ordinal))
                yield return target;
        }
    }

    /// <summary>
    /// Threads every edge spanning more than one layer through dummy nodes, then groups everything
    /// into layers and builds the neighbour maps the ordering pass needs.
    /// </summary>
    /// <remarks>
    /// The dummies are what stop a long edge being drawn straight through the nodes it passes. They
    /// are ordered and placed like any other node and then read back as bend points.
    /// </remarks>
    public void InsertDummies()
    {
        var threaded = new List<(string Source, string Target)>();
        var counter = 0;

        foreach (var edge in this.sourceEdges)
        {
            if (!this.Layers.TryGetValue(edge.Source, out var from) ||
                !this.Layers.TryGetValue(edge.Target, out var to))
            {
                continue;
            }

            var span = to - from;

            if (span <= 1)
            {
                threaded.Add((edge.Source, edge.Target));
                continue;
            }

            var chain = new List<string>(span - 1);
            var previous = edge.Source;

            for (var i = 1; i < span; i++)
            {
                // A space cannot appear in a caller's node id without them going out of their way, so
                // this cannot collide with one.
                var id = $"dummy {counter++}";
                this.dummyOrder.Add(id);
                this.Layers[id] = from + i;
                chain.Add(id);
                threaded.Add((previous, id));
                previous = id;
            }

            threaded.Add((previous, edge.Target));
            this.dummyChains.Add(new DummyChain(chain, edge.Reversed, edge.Connection));
        }

        var maxLayer = 0;
        foreach (var layer in this.Layers.Values)
            maxLayer = Math.Max(maxLayer, layer);

        for (var i = 0; i <= maxLayer; i++)
            this.LayerLists.Add([]);

        // Real nodes first, in their supplied order, then dummies in creation order - so the starting
        // order is stable and the barycenter passes begin from the same place on both hosts.
        foreach (var id in this.Order)
            this.LayerLists[this.Layers[id]].Add(id);

        foreach (var id in this.dummyOrder)
            this.LayerLists[this.Layers[id]].Add(id);

        foreach (var (source, target) in threaded)
        {
            if (!this.SuccessorMap.TryGetValue(source, out var successors))
                this.SuccessorMap[source] = successors = [];

            successors.Add(target);

            if (!this.Predecessors.TryGetValue(target, out var predecessors))
                this.Predecessors[target] = predecessors = [];

            predecessors.Add(source);
        }
    }

    /// <summary>The node's extent along the sibling axis. A dummy has none - it is a bend, not a box.</summary>
    /// <param name="id">The node or dummy id.</param>
    public double AcrossSize(string id) =>
        this.nodes.TryGetValue(id, out var node) ? (this.vertical ? node.Width : node.Height) : 0;

    /// <summary>The node's extent along the growth axis. A dummy has none.</summary>
    /// <param name="id">The node or dummy id.</param>
    public double DepthSize(string id) =>
        this.nodes.TryGetValue(id, out var node) ? (this.vertical ? node.Height : node.Width) : 0;

    /// <summary>
    /// Maps the two abstract axes onto real coordinates and hands each long connection the bend
    /// points its dummy chain traced.
    /// </summary>
    /// <param name="options">Supplies direction and margin.</param>
    public void WriteBack(DiagramLayoutOptions options)
    {
        var acrossMin = double.MaxValue;
        var depthExtent = 0d;

        foreach (var id in this.Across.Keys)
        {
            acrossMin = Math.Min(acrossMin, this.Across[id] - (this.AcrossSize(id) / 2));
            depthExtent = Math.Max(depthExtent, this.Depth[id] + this.DepthSize(id));
        }

        if (acrossMin == double.MaxValue)
            return;

        var flip = options.Direction is DiagramDirection.BottomToTop or DiagramDirection.RightToLeft;

        DiagramPoint Resolve(string id)
        {
            var across = this.Across[id] - (this.AcrossSize(id) / 2) - acrossMin + options.Margin;
            var depth = flip
                ? depthExtent - this.Depth[id] - this.DepthSize(id) + options.Margin
                : this.Depth[id] + options.Margin;

            return this.vertical ? new DiagramPoint(across, depth) : new DiagramPoint(depth, across);
        }

        foreach (var id in this.Order)
        {
            var node = this.nodes[id];

            // A node the user dragged keeps its place; the layout still reserved its space above, so
            // its neighbours do not close over the gap.
            if (node.IsPinned)
                continue;

            var point = Resolve(id);
            node.X = point.X;
            node.Y = point.Y;
            node.Depth = this.Layers[id];
        }

        foreach (var (chain, reversed, connection) in this.dummyChains)
        {
            if (chain.Count == 0)
                continue;

            var bends = new List<DiagramPoint>(chain.Count);

            // A dummy has no size, so the leading edge Resolve returns already is the centre of the
            // bend.
            foreach (var dummy in chain)
                bends.Add(Resolve(dummy));

            // The chain was traced along the reversed edge, so it runs from the connection's target
            // back to its source. The router reads bends in the connection's own direction.
            if (reversed)
                bends.Reverse();

            connection.LayoutBends = bends;
        }
    }
}
