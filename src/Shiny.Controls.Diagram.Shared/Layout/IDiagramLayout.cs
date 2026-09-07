namespace Shiny.Controls.Diagramming;

/// <summary>
/// Arranges a graph by writing <see cref="DiagramNode.X"/> and <see cref="DiagramNode.Y"/>.
/// </summary>
/// <remarks>
/// Implementations must be deterministic: the same graph and the same options have to produce the
/// same coordinates every time, on both hosts. A layout that seeded itself from
/// <c>Random.Shared</c> or iterated a dictionary's natural order would let the MAUI and Blazor
/// controls disagree about where a node goes, which is a bug neither host's own tests would notice.
/// </remarks>
public interface IDiagramLayout
{
    /// <summary>Positions every node the context offers, leaving pinned ones where they are.</summary>
    /// <param name="context">The nodes, connections and options to arrange.</param>
    void Arrange(DiagramLayoutContext context);
}


/// <summary>What a layout is given to work with.</summary>
/// <param name="Nodes">The nodes to place - already filtered to the visible, non-collapsed ones, in a stable order.</param>
/// <param name="Connections">The connections between them - already filtered to the ones with both ends present.</param>
/// <param name="Options">Spacing, direction and default sizes.</param>
public sealed record DiagramLayoutContext(
    IReadOnlyList<DiagramNode> Nodes,
    IReadOnlyList<DiagramConnection> Connections,
    DiagramLayoutOptions Options
)
{
    /// <summary>
    /// Applies the fallback size to any node that has not been given one, so a layout can rely on
    /// every node having a real width and height.
    /// </summary>
    /// <remarks>
    /// Width is estimated from the label when the node did not set one - see
    /// <see cref="DiagramLayoutOptions.FontSize"/> for why that estimate is as rough as it is.
    /// </remarks>
    public void EnsureSizes()
    {
        foreach (var node in this.Nodes)
        {
            if (node.Width <= 0)
                node.Width = EstimateWidth(node, this.Options);

            if (node.Height <= 0)
                node.Height = this.Options.NodeHeight;
        }
    }

    static double EstimateWidth(DiagramNode node, DiagramLayoutOptions options)
    {
        var fontSize = node.FontSize ?? options.FontSize;

        // The longest line, not the whole string: a label written as three short lines needs the
        // width of its widest one, and sizing it to the total makes the node three times too wide.
        var longest = 0;

        foreach (var line in node.Text.Replace("\r\n", "\n").Split('\n'))
            longest = Math.Max(longest, line.Length);

        var text = longest * fontSize * 0.6;
        var width = Math.Max(options.NodeWidth, text + 32);

        // A diamond wastes most of its bounding box: a label that fits a rectangle of this width
        // overruns the sloped sides, so the box has to grow for the text to sit inside the shape.
        return node.Shape == DiagramNodeShape.Diamond ? width * 1.4 : width;
    }
}
