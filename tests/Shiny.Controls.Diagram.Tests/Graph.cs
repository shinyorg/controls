namespace Shiny.Controls.Diagram.Tests;

/// <summary>Builders for the graphs the tests keep needing.</summary>
static class Graph
{
    /// <summary>A model over a flat node list and a set of "source&gt;target" edges.</summary>
    public static DiagramModel Model(
        IEnumerable<DiagramNode> nodes,
        IEnumerable<DiagramConnection>? connections = null,
        DiagramLayoutKind layout = DiagramLayoutKind.Tree,
        Action<DiagramLayoutOptions>? configure = null
    )
    {
        var model = new DiagramModel { Layout = layout };
        configure?.Invoke(model.Options);
        model.SetSource(nodes, connections ?? []);
        model.Rebuild();
        return model;
    }

    /// <summary>A node with a fixed size, so a test asserting on geometry is not asserting on the width estimate.</summary>
    public static DiagramNode Node(string id, double width = 100, double height = 50) =>
        new(id, id) { Width = width, Height = height };

    /// <summary>Connections from a compact "a>b" spelling.</summary>
    public static List<DiagramConnection> Edges(params string[] pairs)
    {
        var list = new List<DiagramConnection>();

        foreach (var pair in pairs)
        {
            var parts = pair.Split('>');
            list.Add(new DiagramConnection(parts[0], parts[1]) { Id = pair });
        }

        return list;
    }

    /// <summary>A parent with children attached, for the nested-hierarchy path.</summary>
    public static DiagramNode Parent(string id, params DiagramNode[] children)
    {
        var node = Node(id);

        foreach (var child in children)
            node.Children.Add(child);

        return node;
    }

    /// <summary>Every node in a nested hierarchy, parents before children.</summary>
    public static List<DiagramNode> Flatten(params DiagramNode[] roots)
    {
        var all = new List<DiagramNode>();
        var stack = new Stack<DiagramNode>();

        for (var i = roots.Length - 1; i >= 0; i--)
            stack.Push(roots[i]);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            all.Add(node);

            for (var i = node.Children.Count - 1; i >= 0; i--)
                stack.Push(node.Children[i]);
        }

        return all;
    }

    /// <summary>True when no two of the nodes overlap, which is the property every layout owes.</summary>
    public static bool NoOverlaps(IReadOnlyList<DiagramNode> nodes)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            for (var j = i + 1; j < nodes.Count; j++)
            {
                // Touching edges are fine; sharing area is not. Deflating by a hair keeps a shared
                // boundary from reading as an overlap.
                if (nodes[i].Bounds.Inflate(-0.01).IntersectsWith(nodes[j].Bounds.Inflate(-0.01)))
                    return false;
            }
        }

        return true;
    }
}
