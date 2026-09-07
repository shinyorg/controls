namespace Shiny.Controls.Diagramming;

/// <summary>
/// The knobs every layout shares.
/// </summary>
/// <remarks>
/// Sizes are in diagram units, which the hosts treat as device-independent pixels before zoom. A node
/// that carries its own non-zero <see cref="DiagramNode.Width"/>/<see cref="DiagramNode.Height"/>
/// keeps it; <see cref="NodeWidth"/> and <see cref="NodeHeight"/> are the fallback, and the only size
/// most diagrams ever specify.
/// </remarks>
public sealed class DiagramLayoutOptions
{
    /// <summary>Default node width, for nodes that do not set their own. Defaults to 140.</summary>
    public double NodeWidth { get; set; } = 140;

    /// <summary>Default node height, for nodes that do not set their own. Defaults to 56.</summary>
    public double NodeHeight { get; set; } = 56;

    /// <summary>Gap between two nodes side by side within a level. Defaults to 32.</summary>
    public double NodeSpacing { get; set; } = 32;

    /// <summary>Gap between one level of the hierarchy and the next. Defaults to 56.</summary>
    public double LevelSpacing { get; set; } = 56;

    /// <summary>Gap between two separate trees in a forest, or two disconnected components. Defaults to 64.</summary>
    public double ComponentSpacing { get; set; } = 64;

    /// <summary>Blank space left around the whole diagram. Defaults to 40.</summary>
    public double Margin { get; set; } = 40;

    /// <summary>Which way the hierarchy grows.</summary>
    public DiagramDirection Direction { get; set; } = DiagramDirection.TopToBottom;

    /// <summary>Whether children spread across the axis or stack and indent.</summary>
    public DiagramTreeStyle TreeStyle { get; set; } = DiagramTreeStyle.Normal;

    /// <summary>How far a <see cref="DiagramTreeStyle.TipOver"/> child indents from its parent. Defaults to 28.</summary>
    public double TipOverIndent { get; set; } = 28;

    /// <summary>
    /// Font size used to estimate a label's width when a node has no explicit width. Defaults to 14.
    /// </summary>
    /// <remarks>
    /// The estimate is deliberately crude - the engine has no font metrics and is not going to grow a
    /// dependency to get them. It exists so a node with a long label is not laid out at the same width
    /// as one with two characters; a host that can measure text properly should set
    /// <see cref="DiagramNode.Width"/> and skip this entirely.
    /// </remarks>
    public double FontSize { get; set; } = 14;

    /// <summary>Relaxation passes for <see cref="DiagramLayoutKind.ForceDirected"/>. Defaults to 300.</summary>
    public int ForceIterations { get; set; } = 300;

    /// <summary>Ring spacing for <see cref="DiagramLayoutKind.Radial"/>. Defaults to 120.</summary>
    public double RadialRingSpacing { get; set; } = 120;

    /// <summary>True when the layout runs along the vertical axis.</summary>
    public bool IsVertical =>
        this.Direction is DiagramDirection.TopToBottom or DiagramDirection.BottomToTop;

    /// <summary>Returns a copy, so a control can hand a layout its own snapshot.</summary>
    public DiagramLayoutOptions Clone() => new()
    {
        NodeWidth = this.NodeWidth,
        NodeHeight = this.NodeHeight,
        NodeSpacing = this.NodeSpacing,
        LevelSpacing = this.LevelSpacing,
        ComponentSpacing = this.ComponentSpacing,
        Margin = this.Margin,
        Direction = this.Direction,
        TreeStyle = this.TreeStyle,
        TipOverIndent = this.TipOverIndent,
        FontSize = this.FontSize,
        ForceIterations = this.ForceIterations,
        RadialRingSpacing = this.RadialRingSpacing
    };
}
