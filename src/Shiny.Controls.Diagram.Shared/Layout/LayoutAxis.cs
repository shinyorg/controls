namespace Shiny.Controls.Diagramming;

/// <summary>
/// Maps a hierarchy layout's two abstract axes onto real X and Y.
/// </summary>
/// <remarks>
/// The tree algorithms work in "across" (the sibling axis) and "depth" (the growth axis) and know
/// nothing about direction. Turning that into coordinates - and flipping it for
/// <see cref="DiagramDirection.BottomToTop"/> and <see cref="DiagramDirection.RightToLeft"/> - happens
/// once, here, rather than as four cases threaded through every algorithm. That is also what keeps a
/// direction change from being able to break the packing: the packing never sees it.
/// </remarks>
static class LayoutAxis
{
    /// <summary>Writes final coordinates onto every node in the forest.</summary>
    /// <param name="roots">The forest to place.</param>
    /// <param name="options">Supplies direction and margin.</param>
    public static void Apply(IReadOnlyList<TreeNode> roots, DiagramLayoutOptions options)
    {
        var depthExtent = 0d;
        var acrossMin = double.MaxValue;

        foreach (var root in roots)
        {
            foreach (var node in TreeNode.Descendants(root))
            {
                depthExtent = Math.Max(depthExtent, node.Depth1D + node.DepthSize);
                acrossMin = Math.Min(acrossMin, node.Across - (node.AcrossSize / 2));
            }
        }

        if (acrossMin == double.MaxValue)
            return;

        var margin = options.Margin;
        var vertical = options.IsVertical;

        foreach (var root in roots)
        {
            foreach (var node in TreeNode.Descendants(root))
            {
                // A node the user dragged keeps its place. The space it occupies was still reserved
                // by the packing above, so its neighbours do not close over it.
                if (node.Node.IsPinned)
                    continue;

                var across = node.Across - (node.AcrossSize / 2) - acrossMin + margin;
                var depth = node.Depth1D + margin;

                if (options.Direction is DiagramDirection.BottomToTop or DiagramDirection.RightToLeft)
                    depth = depthExtent - node.Depth1D - node.DepthSize + margin;

                if (vertical)
                {
                    node.Node.X = across;
                    node.Node.Y = depth;
                }
                else
                {
                    node.Node.X = depth;
                    node.Node.Y = across;
                }

                node.Node.Depth = node.Depth;
            }
        }
    }
}


/// <summary>
/// The stacked, indented hierarchy - a file tree rather than an org chart.
/// </summary>
/// <remarks>
/// Worth having as its own arrangement rather than a tuning of the tidy one: a manager with twelve
/// reports is twelve node-widths across under <see cref="DiagramTreeStyle.Normal"/> and one node-width
/// across here, which is the difference between a deep chart fitting on a phone and not.
/// </remarks>
static class TipOverArranger
{
    /// <summary>Stacks the forest along the growth axis, indenting one step per level.</summary>
    /// <param name="roots">The forest to place.</param>
    /// <param name="options">Supplies indent, spacing and direction.</param>
    public static void Arrange(IReadOnlyList<TreeNode> roots, DiagramLayoutOptions options)
    {
        var cursor = 0d;

        foreach (var root in roots)
        {
            var stack = new Stack<(TreeNode Node, double Leading)>();
            stack.Push((root, 0));

            while (stack.Count > 0)
            {
                var (node, leading) = stack.Pop();

                node.Depth1D = cursor;
                node.Across = leading + (node.AcrossSize / 2);
                cursor += node.DepthSize + options.LevelSpacing;

                for (var i = node.Children.Count - 1; i >= 0; i--)
                    stack.Push((node.Children[i], leading + options.TipOverIndent));
            }

            cursor += options.ComponentSpacing;
        }

        LayoutAxis.Apply(roots, options);
    }
}
