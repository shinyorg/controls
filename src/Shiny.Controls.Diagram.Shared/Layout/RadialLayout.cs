namespace Shiny.Controls.Diagramming;

/// <summary>
/// Root at the centre, each level of the hierarchy on a ring around it.
/// </summary>
/// <remarks>
/// <para>
/// Every node is given an angular sector, and it divides that sector among its children in proportion
/// to how many leaves each subtree ends in. Weighting by leaf count rather than by child count is
/// what stops a branch with one child and forty grandchildren being squeezed into the same wedge as
/// its leafless sibling.
/// </para>
/// <para>
/// Rings are evenly spaced by <see cref="DiagramLayoutOptions.RadialRingSpacing"/> rather than sized
/// from their contents. A ring wide enough for its widest node grows the whole diagram for one long
/// label, and unlike a row in a tree there is nowhere for that node to go instead.
/// </para>
/// </remarks>
public sealed class RadialLayout : IDiagramLayout
{
    /// <inheritdoc />
    public void Arrange(DiagramLayoutContext context)
    {
        context.EnsureSizes();

        if (context.Nodes.Count == 0)
            return;

        var options = context.Options;
        var roots = HierarchyBuilder.BuildForest(context.Nodes, context.Connections, options);

        if (roots.Count == 0)
            return;

        // Several roots means several separate wheels. They are laid out one after another along X
        // and separated by the component gap, rather than nested into one wheel, because a node with
        // no path to the centre has no meaningful angle in it.
        var offsetX = 0d;

        foreach (var root in roots)
        {
            var leaves = CountLeaves(root);
            Place(root, 0, Math.PI * 2, options, leaves);

            var (minX, maxX) = Extent(root);
            Shift(root, offsetX - minX, 0);
            offsetX += maxX - minX + options.ComponentSpacing;
        }

        Normalise(roots, options);
    }

    /// <summary>
    /// Leaf count per subtree, computed once bottom-up so the angular split is a lookup rather than a
    /// walk per node.
    /// </summary>
    static Dictionary<TreeNode, int> CountLeaves(TreeNode root)
    {
        var counts = new Dictionary<TreeNode, int>();
        var order = new List<TreeNode>();

        foreach (var node in TreeNode.Descendants(root))
            order.Add(node);

        // Descendants yields parents before children, so walking it backwards is a post-order.
        for (var i = order.Count - 1; i >= 0; i--)
        {
            var node = order[i];

            if (node.Children.Count == 0)
            {
                counts[node] = 1;
                continue;
            }

            var total = 0;
            foreach (var child in node.Children)
                total += counts[child];

            counts[node] = total;
        }

        return counts;
    }

    static void Place(
        TreeNode root,
        double from,
        double to,
        DiagramLayoutOptions options,
        Dictionary<TreeNode, int> leaves
    )
    {
        var stack = new Stack<(TreeNode Node, double From, double To)>();
        stack.Push((root, from, to));

        while (stack.Count > 0)
        {
            var (node, sectorFrom, sectorTo) = stack.Pop();

            var radius = node.Depth * options.RadialRingSpacing;
            var angle = (sectorFrom + sectorTo) / 2;

            // Depth zero puts the root exactly at the centre; every ring after is placed on its
            // sector's bisector.
            node.Across = radius * Math.Cos(angle);
            node.Depth1D = radius * Math.Sin(angle);

            if (node.Children.Count == 0)
                continue;

            var span = sectorTo - sectorFrom;
            var total = Math.Max(leaves[node], 1);
            var cursor = sectorFrom;

            // Pushed in reverse so children are popped in declaration order and the wheel reads
            // clockwise in the order the data was written.
            var slices = new List<(TreeNode Child, double From, double To)>(node.Children.Count);

            foreach (var child in node.Children)
            {
                var share = span * leaves[child] / total;
                slices.Add((child, cursor, cursor + share));
                cursor += share;
            }

            for (var i = slices.Count - 1; i >= 0; i--)
                stack.Push(slices[i]);
        }
    }

    static (double Min, double Max) Extent(TreeNode root)
    {
        var min = double.MaxValue;
        var max = double.MinValue;

        foreach (var node in TreeNode.Descendants(root))
        {
            min = Math.Min(min, node.Across - (node.Node.Width / 2));
            max = Math.Max(max, node.Across + (node.Node.Width / 2));
        }

        return min > max ? (0, 0) : (min, max);
    }

    static void Shift(TreeNode root, double dx, double dy)
    {
        foreach (var node in TreeNode.Descendants(root))
        {
            node.Across += dx;
            node.Depth1D += dy;
        }
    }

    static void Normalise(IReadOnlyList<TreeNode> roots, DiagramLayoutOptions options)
    {
        var minX = double.MaxValue;
        var minY = double.MaxValue;

        foreach (var root in roots)
        {
            foreach (var node in TreeNode.Descendants(root))
            {
                minX = Math.Min(minX, node.Across - (node.Node.Width / 2));
                minY = Math.Min(minY, node.Depth1D - (node.Node.Height / 2));
            }
        }

        if (minX == double.MaxValue)
            return;

        foreach (var root in roots)
        {
            foreach (var node in TreeNode.Descendants(root))
            {
                if (node.Node.IsPinned)
                    continue;

                // Both axes carry centres here, unlike the hierarchy layouts, so both are converted
                // back to a top-left corner.
                node.Node.X = node.Across - (node.Node.Width / 2) - minX + options.Margin;
                node.Node.Y = node.Depth1D - (node.Node.Height / 2) - minY + options.Margin;
                node.Node.Depth = node.Depth;
            }
        }
    }
}
