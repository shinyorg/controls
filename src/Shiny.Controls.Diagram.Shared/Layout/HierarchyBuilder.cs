namespace Shiny.Controls.Diagramming;

/// <summary>
/// Turns a set of nodes into the forest the hierarchy layouts arrange.
/// </summary>
/// <remarks>
/// <para>
/// Two sources, in priority order. If any node has a <see cref="DiagramNode.Parent"/> - set by
/// nesting, or resolved from <see cref="DiagramNode.ParentId"/> when the model was built - that is
/// the hierarchy, full stop. Otherwise one is derived from the connections as a breadth-first
/// spanning forest.
/// </para>
/// <para>
/// The derived path is what lets a diagram declared as nothing but shapes and connections lay out as
/// a tree. Without it, the most natural way to write an org chart - a list of nodes and a list of
/// "A reports to B" edges - would have no hierarchy at all and every node would land at the origin.
/// </para>
/// </remarks>
static class HierarchyBuilder
{
    /// <summary>Builds the forest, sized for the options' growth direction.</summary>
    /// <param name="nodes">The nodes to arrange.</param>
    /// <param name="connections">The connections, used only when no parent links exist.</param>
    /// <param name="options">Supplies the direction, which decides which axis a node's width is on.</param>
    public static IReadOnlyList<TreeNode> BuildForest(
        IReadOnlyList<DiagramNode> nodes,
        IReadOnlyList<DiagramConnection> connections,
        DiagramLayoutOptions? options = null
    )
    {
        if (nodes.Count == 0)
            return [];

        var wrappers = new Dictionary<string, TreeNode>(nodes.Count, StringComparer.Ordinal);
        var ordered = new List<TreeNode>(nodes.Count);

        foreach (var node in nodes)
        {
            var wrapper = new TreeNode(node);
            ordered.Add(wrapper);
            wrappers.TryAdd(node.Id, wrapper);
        }

        var usingParents = false;
        foreach (var node in nodes)
        {
            if (node.Parent is not null)
            {
                usingParents = true;
                break;
            }
        }

        if (usingParents)
            LinkByParent(nodes, wrappers);
        else
            LinkByConnections(ordered, wrappers, connections);

        var roots = new List<TreeNode>();
        foreach (var wrapper in ordered)
        {
            if (wrapper.Parent is null)
                roots.Add(wrapper);
        }

        Finalise(roots, options);
        return roots;
    }

    static void LinkByParent(IReadOnlyList<DiagramNode> nodes, Dictionary<string, TreeNode> wrappers)
    {
        foreach (var node in nodes)
        {
            if (node.Parent is null)
                continue;

            if (!wrappers.TryGetValue(node.Id, out var child))
                continue;

            // The parent may not be in the visible set - it can be filtered out, or simply absent -
            // in which case the child is promoted to a root rather than dropped.
            if (!wrappers.TryGetValue(node.Parent.Id, out var parent) || ReferenceEquals(parent, child))
                continue;

            child.Parent = parent;
            parent.Children.Add(child);
        }

        BreakCycles(wrappers.Values);
    }

    static void LinkByConnections(
        List<TreeNode> ordered,
        Dictionary<string, TreeNode> wrappers,
        IReadOnlyList<DiagramConnection> connections
    )
    {
        var outgoing = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var hasIncoming = new HashSet<string>(StringComparer.Ordinal);

        foreach (var connection in connections)
        {
            if (connection.SourceId == connection.TargetId)
                continue;

            if (!wrappers.ContainsKey(connection.SourceId) || !wrappers.ContainsKey(connection.TargetId))
                continue;

            if (!outgoing.TryGetValue(connection.SourceId, out var list))
                outgoing[connection.SourceId] = list = [];

            list.Add(connection.TargetId);
            hasIncoming.Add(connection.TargetId);
        }

        // Breadth-first from the nodes nothing points at. A node claimed by an earlier root keeps
        // that parent, so the first edge into a node wins and a diamond-shaped graph becomes a tree
        // by dropping the second path rather than by duplicating the node.
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<TreeNode>();

        foreach (var wrapper in ordered)
        {
            if (!hasIncoming.Contains(wrapper.Node.Id))
            {
                claimed.Add(wrapper.Node.Id);
                queue.Enqueue(wrapper);
            }
        }

        // A graph that is one big cycle has no such node. Seeding from the first node keeps it laid
        // out rather than collapsed at the origin.
        if (queue.Count == 0)
        {
            claimed.Add(ordered[0].Node.Id);
            queue.Enqueue(ordered[0]);
        }

        while (true)
        {
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                if (!outgoing.TryGetValue(current.Node.Id, out var targets))
                    continue;

                foreach (var targetId in targets)
                {
                    if (!claimed.Add(targetId))
                        continue;

                    var child = wrappers[targetId];
                    child.Parent = current;
                    current.Children.Add(child);
                    queue.Enqueue(child);
                }
            }

            // Anything left is in a component the roots could not reach - a disconnected island, or a
            // cycle with no entry point. Seed the next one and keep going.
            TreeNode? next = null;
            foreach (var wrapper in ordered)
            {
                if (!claimed.Contains(wrapper.Node.Id))
                {
                    next = wrapper;
                    break;
                }
            }

            if (next is null)
                return;

            claimed.Add(next.Node.Id);
            queue.Enqueue(next);
        }
    }

    /// <summary>
    /// Cuts any node whose parent chain loops back to it loose as a root.
    /// </summary>
    /// <remarks>
    /// A cyclic parent chain is not exotic: it is what a flat list of rows produces the moment two
    /// records name each other. Left in place it makes the depth walk below run forever.
    /// </remarks>
    static void BreakCycles(IEnumerable<TreeNode> all)
    {
        foreach (var start in all)
        {
            var slow = start;
            var fast = start;

            while (true)
            {
                fast = fast.Parent;
                if (fast is null)
                    break;

                fast = fast.Parent;
                if (fast is null)
                    break;

                slow = slow.Parent!;

                if (!ReferenceEquals(slow, fast))
                    continue;

                // Cut at the node that closed the loop.
                var parent = slow.Parent;
                parent?.Children.Remove(slow);
                slow.Parent = null;
                break;
            }
        }
    }

    static void Finalise(IReadOnlyList<TreeNode> roots, DiagramLayoutOptions? options)
    {
        var vertical = options?.IsVertical ?? true;

        foreach (var root in roots)
        {
            var stack = new Stack<TreeNode>();
            root.Depth = 0;
            stack.Push(root);

            while (stack.Count > 0)
            {
                var node = stack.Pop();

                node.AcrossSize = vertical ? node.Node.Width : node.Node.Height;
                node.DepthSize = vertical ? node.Node.Height : node.Node.Width;
                node.Ancestor = node;

                for (var i = 0; i < node.Children.Count; i++)
                {
                    var child = node.Children[i];
                    child.Depth = node.Depth + 1;
                    child.Number = i + 1;
                    child.LeftSibling = i > 0 ? node.Children[i - 1] : null;
                    stack.Push(child);
                }
            }
        }
    }
}
