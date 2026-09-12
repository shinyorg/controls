using Microsoft.AspNetCore.Components;

using Shiny.Blazor.Controls.Theming;

namespace Shiny.Blazor.Controls;

public partial class SkeletonView
{
    /// <summary>When true, the content is hidden and animated shimmer placeholders are shown.</summary>
    [Parameter] public bool IsBusy { get; set; }

    /// <summary>The real content shown when <see cref="IsBusy"/> is false.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Optional custom placeholder markup. Apply the <c>shiny-skeleton__shape</c> class to shimmer your own shapes.</summary>
    [Parameter] public RenderFragment? SkeletonContent { get; set; }

    /// <summary>Number of placeholder lines in the built-in skeleton (ignored when SkeletonContent is set).</summary>
    [Parameter] public int ItemCount { get; set; } = 3;

    /// <summary>Height in px of each built-in placeholder line.</summary>
    [Parameter] public double ItemHeight { get; set; } = 16;

    /// <summary>Vertical spacing in px between built-in placeholder lines.</summary>
    [Parameter] public double ItemSpacing { get; set; } = 12;

    /// <summary>
    /// Corner radius in px of the built-in placeholder shapes. The default, <c>-1</c>, follows the
    /// theme's shape scale.
    /// </summary>
    [Parameter] public double CornerRadius { get; set; } = -1;

    string RadiusCss => this.CornerRadius >= 0
        ? FormattableString.Invariant($"{this.CornerRadius}px")
        : "var(--shiny-shape-corner-small, 6px)";

    /// <summary>Base fill color of placeholder shapes.</summary>
    // Mirrored in SkeletonView.razor.css; only a caller-chosen value inlines. See StyleDefaults.
    internal const string DefaultBaseColor = "var(--shiny-color-surface-container-high, #E2E6EA)";
    internal const string DefaultHighlightColor = "color-mix(in srgb, var(--shiny-color-on-surface, #161C23) 22%, transparent)";

    [Parameter] public string BaseColor { get; set; } = DefaultBaseColor;

    /// <summary>Color of the sweeping shimmer highlight.</summary>
    // A flat white sweep blows out against a dark base; keyed off on-surface it stays a soft
    // lift of whatever the base is in either scheme.
    [Parameter] public string HighlightColor { get; set; } = DefaultHighlightColor;

    /// <summary>Duration in seconds of a single shimmer sweep.</summary>
    [Parameter] public double AnimationDuration { get; set; } = 1.4;

    /// <summary>When false, placeholders are shown statically with no sweeping animation.</summary>
    [Parameter] public bool ShimmerEnabled { get; set; } = true;

    [Parameter] public string? CssClass { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }

    string RootStyle =>
        StyleDefaults.Override("--shiny-skeleton-base", BaseColor, DefaultBaseColor) +
        StyleDefaults.Override("--shiny-skeleton-highlight", HighlightColor, DefaultHighlightColor) +
        $" --shiny-skeleton-duration: {AnimationDuration.ToString(System.Globalization.CultureInfo.InvariantCulture)}s;" +
        $" --shiny-skeleton-gap: {ItemSpacing.ToString(System.Globalization.CultureInfo.InvariantCulture)}px;";
}
