namespace Shiny.Controls.Diagramming;

/// <summary>
/// Tidy hierarchy - the org chart and decision tree layout.
/// </summary>
/// <remarks>
/// <para>
/// The packing itself is <see cref="TidyTree"/>; this class is what feeds it - resolving the
/// hierarchy, packing each tree of a forest and shifting it clear of the one before, placing the
/// levels, and mapping the result onto real coordinates.
/// </para>
/// <para>
/// The hierarchy comes from <see cref="DiagramNode.Parent"/> when any node has one, and is otherwise
/// derived from the connections as a breadth-first spanning forest. That second path is what makes a
/// diagram declared purely as shapes and connections - the shape most people reach for first - lay
/// out as a tree without being restructured.
/// </para>
/// </remarks>
public sealed class TreeLayout : IDiagramLayout
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

        if (options.TreeStyle == DiagramTreeStyle.TipOver)
        {
            TipOverArranger.Arrange(roots, options);
            return;
        }

        // Every tree in the forest is packed independently and then shifted clear of the one before
        // it, so a graph with three roots draws three trees side by side rather than one overlapping
        // mess.
        var acrossOffset = 0d;

        foreach (var root in roots)
        {
            TidyTree.Pack(root, options);

            var (min, max) = AcrossExtent(root);
            var shift = acrossOffset - min;
            ShiftTree(root, shift);

            acrossOffset += max - min + options.ComponentSpacing;
        }

        AssignDepths(roots, options);
        LayoutAxis.Apply(roots, options);
    }

    static (double Min, double Max) AcrossExtent(TreeNode root)
    {
        var min = double.MaxValue;
        var max = double.MinValue;

        foreach (var node in TreeNode.Descendants(root))
        {
            min = Math.Min(min, node.Across - (node.AcrossSize / 2));
            max = Math.Max(max, node.Across + (node.AcrossSize / 2));
        }

        return min > max ? (0, 0) : (min, max);
    }

    static void ShiftTree(TreeNode root, double by)
    {
        if (Math.Abs(by) < 1e-9)
            return;

        foreach (var node in TreeNode.Descendants(root))
            node.Across += by;
    }

    /// <summary>
    /// Places each level along the growth axis, centring a node within its level so a tall node in one
    /// level does not push a short one in the same level off its own baseline.
    /// </summary>
    static void AssignDepths(IReadOnlyList<TreeNode> roots, DiagramLayoutOptions options)
    {
        var levelSize = new List<double>();

        foreach (var root in roots)
        {
            foreach (var node in TreeNode.Descendants(root))
            {
                while (levelSize.Count <= node.Depth)
                    levelSize.Add(0);

                levelSize[node.Depth] = Math.Max(levelSize[node.Depth], node.DepthSize);
            }
        }

        var offsets = new double[levelSize.Count];
        var running = 0d;

        for (var i = 0; i < levelSize.Count; i++)
        {
            offsets[i] = running;
            running += levelSize[i] + options.LevelSpacing;
        }

        foreach (var root in roots)
        {
            foreach (var node in TreeNode.Descendants(root))
                node.Depth1D = offsets[node.Depth] + ((levelSize[node.Depth] - node.DepthSize) / 2);
        }
    }
}
