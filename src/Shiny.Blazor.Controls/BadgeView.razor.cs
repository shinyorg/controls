using System.Globalization;
using Microsoft.AspNetCore.Components;

using Shiny.Blazor.Controls.Theming;

namespace Shiny.Blazor.Controls;

public partial class BadgeView
{
    /// <summary>The view the badge is overlaid on top of.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Badge text. When empty/null and <see cref="IsDot"/> is false, the badge is hidden.</summary>
    [Parameter] public string? Text { get; set; }

    /// <summary>Corner of the wrapped content where the badge is anchored.</summary>
    [Parameter] public BadgePosition Position { get; set; } = BadgePosition.TopRight;

    /// <summary>Badge fill color (any valid CSS color).</summary>
    // Mirrored in BadgeView.razor.css; only a caller-chosen value inlines. See StyleDefaults.
    internal const string DefaultBadgeColor = "var(--shiny-color-error, #C20014)";
    internal const string DefaultBadgeTextColor = "var(--shiny-color-on-error, #FFFFFF)";
    internal const string DefaultBadgeBorderColor = "var(--shiny-color-surface, #F6FAFE)";
    internal const string DefaultFontWeight = "var(--shiny-type-label-small-weight, 700)";
    internal const string DefaultBadgePadding = "calc(2px * var(--shiny-density-scale, 1)) calc(6px * var(--shiny-density-scale, 1))";

    [Parameter] public string BadgeColor { get; set; } = DefaultBadgeColor;

    /// <summary>Badge text color (any valid CSS color).</summary>
    [Parameter] public string BadgeTextColor { get; set; } = DefaultBadgeTextColor;

    /// <summary>Badge border color (any valid CSS color). Defaults to white for clean separation from the wrapped content.</summary>
    [Parameter] public string BadgeBorderColor { get; set; } = DefaultBadgeBorderColor;

    /// <summary>Badge border thickness in px.</summary>
    /// <summary>Badge border thickness in px. The default, <c>-1</c>, follows the theme border scale.</summary>
    [Parameter] public double BadgeBorderThickness { get; set; } = -1;

    /// <summary>Badge text font size in px.</summary>
    /// <summary>Badge text size in px. The default, <c>-1</c>, follows the theme type scale.</summary>
    [Parameter] public double FontSize { get; set; } = -1;

    /// <summary>Badge text font weight (CSS value).</summary>
    [Parameter] public string FontWeight { get; set; } = DefaultFontWeight;

    /// <summary>Badge corner radius in px. Default is a fully rounded pill.</summary>
    /// <summary>Badge corner radius in px. The default, <c>-1</c>, follows the theme's full-round shape.</summary>
    [Parameter] public double CornerRadius { get; set; } = -1;

    /// <summary>Inner padding of the badge as a CSS value (e.g. "2px 6px").</summary>
    [Parameter] public string BadgePadding { get; set; } = DefaultBadgePadding;

    /// <summary>Horizontal nudge in px from the corner. Positive pushes outward (away from content).</summary>
    [Parameter] public double OffsetX { get; set; } = 4;

    /// <summary>Vertical nudge in px from the corner. Positive pushes downward; negative pushes upward.</summary>
    [Parameter] public double OffsetY { get; set; } = -4;

    /// <summary>When true, the badge is rendered as a small dot (text is ignored).</summary>
    [Parameter] public bool IsDot { get; set; }

    /// <summary>Diameter (px) of the badge in dot mode.</summary>
    [Parameter] public double DotSize { get; set; } = 10;

    /// <summary>When greater than zero and <see cref="Text"/> parses as a number above this limit, the badge displays "{MaxCount}+".</summary>
    [Parameter] public int MaxCount { get; set; }

    /// <summary>When true, the badge scales in/out as it appears or disappears.</summary>
    [Parameter] public bool IsAnimated { get; set; } = true;

    /// <summary>When true, the badge continuously pulses to draw attention.</summary>
    [Parameter] public bool IsPulsing { get; set; }

    [Parameter] public string? CssClass { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }

    bool ShouldShow => IsDot || !string.IsNullOrEmpty(Text);

    string DisplayText
    {
        get
        {
            var text = Text ?? string.Empty;
            if (MaxCount > 0 && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > MaxCount)
                return $"{MaxCount}+";
            return text;
        }
    }

    string PositionClass => Position switch
    {
        BadgePosition.TopLeft => "tl",
        BadgePosition.TopRight => "tr",
        BadgePosition.BottomLeft => "bl",
        BadgePosition.BottomRight => "br",
        _ => "tr"
    };

    string HostStyle => "position:relative; display:inline-block;";

    string BadgeStyle
    {
        get
        {
            var ci = CultureInfo.InvariantCulture;
            // The border is one shorthand, so a caller-chosen width or colour has to write the whole
            // declaration; leaving both alone leaves it to the stylesheet.
            var border = BadgeBorderThickness >= 0 || BadgeBorderColor != DefaultBadgeBorderColor
                ? $"border:{(BadgeBorderThickness >= 0 ? $"{BadgeBorderThickness.ToString(ci)}px" : "var(--shiny-border-thin, 1.5px)")} solid {BadgeBorderColor};"
                : "";

            var common =
                StyleDefaults.Override("background", BadgeColor, DefaultBadgeColor) +
                StyleDefaults.Override("color", BadgeTextColor, DefaultBadgeTextColor) +
                border +
                (CornerRadius >= 0 ? $"border-radius:{CornerRadius.ToString(ci)}px;" : "") +
                StyleDefaults.Override("font-weight", FontWeight, DefaultFontWeight);

            if (IsDot)
            {
                common +=
                    $"width:{DotSize.ToString(ci)}px;" +
                    $"height:{DotSize.ToString(ci)}px;" +
                    "padding:0;";
            }
            else
            {
                common +=
                    StyleDefaults.Override("padding", BadgePadding, DefaultBadgePadding) +
                    $"font-size:{(FontSize >= 0 ? $"{FontSize.ToString(ci)}px" : "calc(10px * var(--shiny-type-scale, 1))")};" +
                    "min-width:1em; line-height:1;";
            }

            // Set --offset variables consumed by the position-specific CSS classes.
            common +=
                $"--shiny-badge-offset-x:{OffsetX.ToString(ci)}px;" +
                $"--shiny-badge-offset-y:{OffsetY.ToString(ci)}px;";

            return common;
        }
    }
}
