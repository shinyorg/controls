namespace Shiny.Blazor.Controls;

/// <summary>
/// Data record describing a menu item used by <see cref="FabMenu"/>.
/// <para>
/// Renders as one capsule ("pill"): the label lives inside the capsule with a tinted circular icon
/// chip on the edge nearest the main FAB. An item with no <see cref="Text"/> collapses to a plain
/// circle of <see cref="Size"/>.
/// </para>
/// </summary>
public class FabMenuItem
{
    public string? Icon { get; set; }
    public string? Text { get; set; }

    /// <summary>Fill of the circular icon chip - and of the whole pill when the item has no <see cref="Text"/>.</summary>
    public string FabBackgroundColor { get; set; } = "var(--shiny-color-primary, #0055D9)";

    public string TextColor { get; set; } = "var(--shiny-color-on-surface, #161C23)";

    /// <summary>Fill of the pill body behind the label.</summary>
    public string LabelBackgroundColor { get; set; } = "var(--shiny-color-surface-container-high, #E2E6EA)";

    /// <summary>Outline stroke of the pill. Defaults to the theme outline-variant hairline.</summary>
    public string? BorderColor { get; set; }

    /// <summary>Outline thickness of the pill. Set to 0 for a borderless pill.</summary>
    public double BorderThickness { get; set; } = 1;

    /// <summary>Pill height - and the diameter when the item has no <see cref="Text"/>.</summary>
    public double Size { get; set; } = 44;

    public double IconSize { get; set; } = 20;
    public double FontSize { get; set; } = 13;
    public bool HasShadow { get; set; } = true;
    public object? Tag { get; set; }
}
