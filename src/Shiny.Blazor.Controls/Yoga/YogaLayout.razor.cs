using System.Text;
using Microsoft.AspNetCore.Components;

namespace Shiny.Blazor.Controls;

/// <summary>
/// A Yoga node (<see href="https://www.yogalayout.dev/"/>): a flex container with Yoga's defaults, and a
/// flex item of its parent. Nest them the way React Native nests views. The same model ships for MAUI as
/// <c>Shiny.Maui.Controls.YogaLayout</c>, so one layout gives the same boxes on both hosts.
/// </summary>
/// <remarks>
/// The browser already implements flexbox, so this is CSS and not a layout engine. It emits Yoga's
/// defaults (column direction, shrink 0, align-content flex-start, minimum size 0, border-box, relative
/// position) and Yoga's extras (insets, percentages, aspect ratio, auto margins) as an inline style.
/// Lengths are strings: <c>Width="50%"</c>, <c>Width="120"</c> (points, rendered as px), <c>Width="auto"</c>.
/// </remarks>
public partial class YogaLayout : ComponentBase
{
    // ---------------------------------------------------------------- container

    /// <summary>The main axis. Defaults to <see cref="YogaFlexDirection.Column"/>, as in Yoga.</summary>
    [Parameter] public YogaFlexDirection FlexDirection { get; set; } = YogaFlexDirection.Column;

    [Parameter] public YogaJustify JustifyContent { get; set; } = YogaJustify.FlexStart;

    /// <summary>Cross-axis alignment of children. Defaults to <see cref="YogaAlign.Stretch"/>.</summary>
    [Parameter] public YogaAlign AlignItems { get; set; } = YogaAlign.Stretch;

    /// <summary>How wrapped lines are placed. Defaults to <see cref="YogaAlign.FlexStart"/>, as in Yoga.</summary>
    [Parameter] public YogaAlign AlignContent { get; set; } = YogaAlign.FlexStart;

    [Parameter] public YogaWrap FlexWrap { get; set; } = YogaWrap.NoWrap;

    /// <summary>Space between children and between lines, in pixels.</summary>
    [Parameter] public double Gap { get; set; }

    /// <summary>Vertical space between rows. Null uses <see cref="Gap"/>.</summary>
    [Parameter] public double? RowGap { get; set; }

    /// <summary>Horizontal space between columns. Null uses <see cref="Gap"/>.</summary>
    [Parameter] public double? ColumnGap { get; set; }

    /// <summary>CSS padding shorthand; bare numbers are pixels (<c>"16"</c>, <c>"8 16"</c>).</summary>
    [Parameter] public string? Padding { get; set; }

    /// <summary>Layout direction. <see cref="YogaDirection.Inherit"/> (the default) takes the parent's.</summary>
    [Parameter] public YogaDirection Direction { get; set; }


    // ---------------------------------------------------------------- as a child

    [Parameter] public double FlexGrow { get; set; }

    /// <summary>Defaults to 0, as in Yoga (CSS uses 1).</summary>
    [Parameter] public double FlexShrink { get; set; }

    [Parameter] public string? FlexBasis { get; set; }

    [Parameter] public YogaAlign AlignSelf { get; set; } = YogaAlign.Auto;

    [Parameter] public YogaPositionType PositionType { get; set; } = YogaPositionType.Relative;

    [Parameter] public YogaDisplay Display { get; set; } = YogaDisplay.Flex;

    /// <summary>Width divided by height.</summary>
    [Parameter] public double? AspectRatio { get; set; }

    [Parameter] public string? Left { get; set; }
    [Parameter] public string? Top { get; set; }
    [Parameter] public string? Right { get; set; }
    [Parameter] public string? Bottom { get; set; }

    /// <summary>Left inset in left-to-right flow, right inset in right-to-left.</summary>
    [Parameter] public string? Start { get; set; }
    [Parameter] public string? End { get; set; }

    [Parameter] public string? Width { get; set; }
    [Parameter] public string? Height { get; set; }
    [Parameter] public string? MinWidth { get; set; }
    [Parameter] public string? MinHeight { get; set; }
    [Parameter] public string? MaxWidth { get; set; }
    [Parameter] public string? MaxHeight { get; set; }

    /// <summary>CSS margin shorthand; bare numbers are pixels.</summary>
    [Parameter] public string? Margin { get; set; }

    /// <summary>Which margins are <c>auto</c>. They soak up free space, e.g. <c>Start</c> pushes the node to the end of its line.</summary>
    [Parameter] public YogaEdges AutoMargins { get; set; }


    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }


    protected IDictionary<string, object>? ExtraAttributes { get; private set; }
    protected string? UserClass { get; private set; }
    protected string NodeStyle { get; private set; } = string.Empty;


    protected override void OnParametersSet()
    {
        this.ExtraAttributes = LayoutAttributes.Split(this.AdditionalAttributes, out var userClass, out var userStyle);
        this.UserClass = userClass;
        this.NodeStyle = LayoutAttributes.Append(this.BuildStyle(), userStyle);
    }


    /// <summary>The inline style for the current parameters. Internal for tests.</summary>
    internal string BuildStyle()
    {
        var sb = new StringBuilder(256);

        sb.Append("display:").Append(this.Display == YogaDisplay.None ? "none" : "flex").Append(';')
            .Append("flex-direction:").Append(ToCss(this.FlexDirection)).Append(';')
            .Append("justify-content:").Append(ToCss(this.JustifyContent)).Append(';')
            .Append("align-items:").Append(ToCss(this.AlignItems == YogaAlign.Auto ? YogaAlign.Stretch : this.AlignItems)).Append(';')
            .Append("align-content:").Append(ToCss(this.AlignContent == YogaAlign.Auto ? YogaAlign.FlexStart : this.AlignContent)).Append(';')
            .Append("flex-wrap:").Append(ToCss(this.FlexWrap)).Append(';')
            .Append("box-sizing:border-box;")
            .Append("position:").Append(ToCss(this.PositionType)).Append(';')
            .Append("flex-grow:").Append(LayoutAttributes.Num(Math.Max(0, this.FlexGrow))).Append(';')
            .Append("flex-shrink:").Append(LayoutAttributes.Num(Math.Max(0, this.FlexShrink))).Append(';');

        Length(sb, "flex-basis", this.FlexBasis);

        if (this.AlignSelf != YogaAlign.Auto)
            sb.Append("align-self:").Append(ToCss(this.AlignSelf)).Append(';');

        var rowGap = this.RowGap ?? this.Gap;
        var columnGap = this.ColumnGap ?? this.Gap;
        if (rowGap > 0)
            sb.Append("row-gap:").Append(LayoutAttributes.Px(rowGap)).Append(';');
        if (columnGap > 0)
            sb.Append("column-gap:").Append(LayoutAttributes.Px(columnGap)).Append(';');

        if (!string.IsNullOrWhiteSpace(this.Padding))
            sb.Append("padding:").Append(LayoutAttributes.Spacing(this.Padding)).Append(';');

        if (this.Direction != YogaDirection.Inherit)
            sb.Append("direction:").Append(this.Direction == YogaDirection.RTL ? "rtl" : "ltr").Append(';');

        Length(sb, "width", this.Width);
        Length(sb, "height", this.Height);

        // Yoga's minimum size is 0, never CSS's content-based "auto".
        if (!Length(sb, "min-width", this.MinWidth))
            sb.Append("min-width:0;");
        if (!Length(sb, "min-height", this.MinHeight))
            sb.Append("min-height:0;");
        Length(sb, "max-width", this.MaxWidth);
        Length(sb, "max-height", this.MaxHeight);

        if (this.AspectRatio is > 0 and var ratio)
            sb.Append("aspect-ratio:").Append(LayoutAttributes.Num(ratio)).Append(';');

        // Static ignores insets, so they are only written for the positions that use them.
        if (this.PositionType != YogaPositionType.Static)
        {
            Length(sb, "left", this.Left);
            Length(sb, "top", this.Top);
            Length(sb, "right", this.Right);
            Length(sb, "bottom", this.Bottom);
            Length(sb, "inset-inline-start", this.Start);
            Length(sb, "inset-inline-end", this.End);
        }

        if (!string.IsNullOrWhiteSpace(this.Margin))
            sb.Append("margin:").Append(LayoutAttributes.Spacing(this.Margin)).Append(';');

        // After the shorthand, so an auto edge overrides it.
        var autos = this.AutoMargins;
        if ((autos & YogaEdges.Left) != 0)
            sb.Append("margin-left:auto;");
        if ((autos & YogaEdges.Top) != 0)
            sb.Append("margin-top:auto;");
        if ((autos & YogaEdges.Right) != 0)
            sb.Append("margin-right:auto;");
        if ((autos & YogaEdges.Bottom) != 0)
            sb.Append("margin-bottom:auto;");
        if ((autos & YogaEdges.Start) != 0)
            sb.Append("margin-inline-start:auto;");
        if ((autos & YogaEdges.End) != 0)
            sb.Append("margin-inline-end:auto;");

        return sb.ToString();
    }


    /// <summary>Appends a length declaration when the value is defined. Throws on text that is not a length.</summary>
    static bool Length(StringBuilder sb, string property, string? text)
    {
        var css = YogaValue.Parse(text).ToCss();
        if (css is null)
            return false;

        sb.Append(property).Append(':').Append(css).Append(';');
        return true;
    }


    static string ToCss(YogaFlexDirection direction) => direction switch
    {
        YogaFlexDirection.ColumnReverse => "column-reverse",
        YogaFlexDirection.Row => "row",
        YogaFlexDirection.RowReverse => "row-reverse",
        _ => "column"
    };

    static string ToCss(YogaJustify justify) => justify switch
    {
        YogaJustify.Center => "center",
        YogaJustify.FlexEnd => "flex-end",
        YogaJustify.SpaceBetween => "space-between",
        YogaJustify.SpaceAround => "space-around",
        YogaJustify.SpaceEvenly => "space-evenly",
        _ => "flex-start"
    };

    static string ToCss(YogaAlign align) => align switch
    {
        YogaAlign.Center => "center",
        YogaAlign.FlexEnd => "flex-end",
        YogaAlign.Stretch => "stretch",
        YogaAlign.Baseline => "baseline",
        YogaAlign.SpaceBetween => "space-between",
        YogaAlign.SpaceAround => "space-around",
        YogaAlign.SpaceEvenly => "space-evenly",
        _ => "flex-start"
    };

    static string ToCss(YogaWrap wrap) => wrap switch
    {
        YogaWrap.Wrap => "wrap",
        YogaWrap.WrapReverse => "wrap-reverse",
        _ => "nowrap"
    };

    static string ToCss(YogaPositionType position) => position switch
    {
        YogaPositionType.Absolute => "absolute",
        YogaPositionType.Static => "static",
        _ => "relative"
    };
}
