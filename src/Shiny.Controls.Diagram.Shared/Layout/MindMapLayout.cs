namespace Shiny.Controls.Diagramming;

/// <summary>
/// Root in the middle, branches fanned out to both sides.
/// </summary>
/// <remarks>
/// <para>
/// The same tidy packing as <see cref="TreeLayout"/>, run once per branch: the root's children are
/// split into two halves and each half grows away from the centre in its own direction. That is the
/// whole difference, and it is worth a separate layout because it halves the width of a wide first
/// level - which is exactly the level a mindmap has most of its nodes on.
/// </para>
/// <para>
/// The split is by count rather than alternating, so the first half of the children stay together on
/// one side in the order they were declared. Alternating reads better in the abstract and worse in
/// practice: a run of siblings that belong together ends up interleaved across the centre line.
/// </para>
/// </remarks>
public sealed class MindMapLayout : IDiagramLayout
{
    /// <inheritdoc />
    public void Arrange(DiagramLayoutContext context)
    {
        context.EnsureSizes();

        if (context.Nodes.Count == 0)
            return;

        var options = context.Options;

        // A mindmap grows sideways from the centre whatever direction was asked for, so the sibling
        // axis is the vertical one and the packing options say so.
        var packing = options.Clone();
        packing.Direction = DiagramDirection.LeftToRight;

        var roots = HierarchyBuilder.BuildForest(context.Nodes, context.Connections, packing);
        if (roots.Count == 0)
            return;

        var top = 0d;

        foreach (var root in roots)
            top = ArrangeOne(root, options, top) + options.ComponentSpacing;

        Normalise(roots, options);
    }

    /// <summary>Places one mindmap starting at <paramref name="top"/>, and returns the bottom it reached.</summary>
    static double ArrangeOne(TreeNode root, DiagramLayoutOptions options, double top = 0)
    {
        var children = root.Children;

        if (children.Count == 0)
        {
            root.Across = top + (root.AcrossSize / 2);
            root.Depth1D = 0;
            return top + root.AcrossSize;
        }

        var rightCount = (children.Count + 1) / 2;
        var right = children.GetRange(0, rightCount);
        var left = children.GetRange(rightCount, children.Count - rightCount);

        var rightHeight = PackSide(right, options);
        var leftHeight = PackSide(left, options);
        var height = Math.Max(Math.Max(rightHeight, leftHeight), root.AcrossSize);

        // Each side is centred against the taller one, and the root against both, so a map with two
        // branches on one side and six on the other still has its root on the centre line.
        Offset(right, top + ((height - rightHeight) / 2));
        Offset(left, top + ((height - leftHeight) / 2));

        root.Across = top + (height / 2);
        AssignDepths(root, right, left, options);

        return top + height;
    }

    /// <summary>
    /// Packs each branch on one side and stacks them, returning the total sibling-axis extent used.
    /// </summary>
    static double PackSide(List<TreeNode> branches, DiagramLayoutOptions options)
    {
        var used = 0d;

        foreach (var branch in branches)
        {
            // Packed as a root in its own right. Its sibling link points at a branch on the other
            // side of the map, and leaving it in place would make the packing reserve room for a
            // subtree that is not going to be drawn anywhere near it.
            var sibling = branch.LeftSibling;
            var parent = branch.Parent;
            branch.LeftSibling = null;
            branch.Parent = null;

            TidyTree.Pack(branch, options);

            branch.LeftSibling = sibling;
            branch.Parent = parent;

            var min = double.MaxValue;
            var max = double.MinValue;

            foreach (var node in TreeNode.Descendants(branch))
            {
                min = Math.Min(min, node.Across - (node.AcrossSize / 2));
                max = Math.Max(max, node.Across + (node.AcrossSize / 2));
            }

            if (min > max)
                continue;

            var shift = used - min;

            foreach (var node in TreeNode.Descendants(branch))
                node.Across += shift;

            used += max - min + options.NodeSpacing;
        }

        return used <= 0 ? 0 : used - options.NodeSpacing;
    }

    static void Offset(List<TreeNode> branches, double by)
    {
        if (Math.Abs(by) < 1e-9)
            return;

        foreach (var branch in branches)
        {
            foreach (var node in TreeNode.Descendants(branch))
                node.Across += by;
        }
    }

    /// <summary>
    /// Puts every level at a fixed distance from the root, one column per level on each side.
    /// </summary>
    /// <remarks>
    /// Columns are sized from the deepest node at that level on <i>either</i> side, so the two halves
    /// stay symmetrical. Sizing them independently makes level three sit further out on the left than
    /// on the right, which reads as a mistake rather than as a fact about the data.
    /// </remarks>
    static void AssignDepths(
        TreeNode root,
        List<TreeNode> right,
        List<TreeNode> left,
        DiagramLayoutOptions options
    )
    {
        var levelSize = new List<double>();

        foreach (var branch in right.Concat(left))
        {
            foreach (var node in TreeNode.Descendants(branch))
            {
                while (levelSize.Count <= node.Depth)
                    levelSize.Add(0);

                levelSize[node.Depth] = Math.Max(levelSize[node.Depth], node.DepthSize);
            }
        }

        root.Depth1D = 0;

        // Level 1 clears the root's own box; every level after clears the one before it.
        var outward = root.DepthSize + options.LevelSpacing;
        var inward = -options.LevelSpacing;

        var starts = new double[levelSize.Count];
        var ends = new double[levelSize.Count];

        for (var depth = 1; depth < levelSize.Count; depth++)
        {
            starts[depth] = outward;
            ends[depth] = inward;

            outward += levelSize[depth] + options.LevelSpacing;
            inward -= levelSize[depth] + options.LevelSpacing;
        }

        Place(right, starts, levelSize, forward: true);
        Place(left, ends, levelSize, forward: false);
    }

    static void Place(List<TreeNode> branches, double[] edges, List<double> levelSize, bool forward)
    {
        foreach (var branch in branches)
        {
            foreach (var node in TreeNode.Descendants(branch))
            {
                if (node.Depth >= edges.Length)
                    continue;

                // Centred within its column, so a short node in a column sized by a tall one does not
                // sit against the column's leading edge.
                var slack = (levelSize[node.Depth] - node.DepthSize) / 2;

                node.Depth1D = forward
                    ? edges[node.Depth] + slack
                    : edges[node.Depth] - levelSize[node.Depth] + slack;
            }
        }
    }

    /// <summary>Writes the packed axes onto the nodes, shifted clear of the origin by the margin.</summary>
    static void Normalise(IReadOnlyList<TreeNode> roots, DiagramLayoutOptions options)
    {
        var minAcross = double.MaxValue;
        var minDepth = double.MaxValue;

        foreach (var root in roots)
        {
            foreach (var node in TreeNode.Descendants(root))
            {
                minAcross = Math.Min(minAcross, node.Across - (node.AcrossSize / 2));
                minDepth = Math.Min(minDepth, node.Depth1D);
            }
        }

        if (minAcross == double.MaxValue)
            return;

        foreach (var root in roots)
        {
            foreach (var node in TreeNode.Descendants(root))
            {
                if (node.Node.IsPinned)
                    continue;

                node.Node.X = node.Depth1D - minDepth + options.Margin;
                node.Node.Y = node.Across - (node.AcrossSize / 2) - minAcross + options.Margin;
                node.Node.Depth = node.Depth;
            }
        }
    }
}
