using Microsoft.AspNetCore.Components;

namespace Shiny.Blazor.Controls;

/// <summary>
/// Shared background / padding / attribute plumbing for the regions of an <see cref="AppLayout"/>.
/// </summary>
public abstract class AppLayoutRegionBase : ComponentBase
{
    /// <summary>CSS background shorthand for the region.</summary>
    [Parameter] public string? Background { get; set; }

    /// <summary>CSS padding shorthand for the region.</summary>
    [Parameter] public string? Padding { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }

    protected IDictionary<string, object>? ExtraAttributes { get; private set; }
    protected string? UserClass { get; private set; }
    protected string? UserStyle { get; private set; }

    protected override void OnParametersSet()
    {
        this.ExtraAttributes = LayoutAttributes.Split(this.AdditionalAttributes, out var userClass, out var userStyle);
        this.UserClass = userClass;
        this.UserStyle = userStyle;
    }

    protected string CommonCss()
    {
        var css = string.Empty;
        if (!string.IsNullOrWhiteSpace(this.Background))
            css += $"background:{this.Background};";
        if (!string.IsNullOrWhiteSpace(this.Padding))
            css += $"padding:{LayoutAttributes.Spacing(this.Padding)};";

        return css;
    }
}

/// <summary>
/// A region that draws a divider between itself and the content.
/// </summary>
public abstract class AppLayoutBorderedRegionBase : AppLayoutRegionBase
{
    /// <summary>Draw the divider between this region and the content. Defaults to true.</summary>
    [Parameter] public bool Border { get; set; } = true;

    /// <summary>Border width in pixels. Falls back to the layout's <c>BorderWidth</c>.</summary>
    [Parameter] public double? BorderWidth { get; set; }

    /// <summary>Border colour. Falls back to the layout's <c>BorderColor</c>, then the theme outline token.</summary>
    [Parameter] public string? BorderColor { get; set; }

    /// <summary>
    /// Border shorthand for one edge. An unset width/colour resolves through the layout's CSS
    /// variables, so a layout-level default still reaches a region that never saw the value in C#.
    /// </summary>
    protected string BorderCss(string edge)
    {
        if (!this.Border)
            return $"border-{edge}:none;";

        var width = this.BorderWidth is null
            ? "var(--shiny-layout-border-width,1px)"
            : LayoutAttributes.Px(this.BorderWidth.Value);

        var color = this.BorderColor
            ?? "var(--shiny-layout-border-color,var(--shiny-color-outline-variant, #BBC7DF))";

        return $"border-{edge}:{width} solid {color};";
    }
}
