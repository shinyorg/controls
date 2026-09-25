using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// The Blazor YogaLayout is CSS, so what matters is the declarations: Yoga's defaults where CSS's
/// differ, and each Yoga extra mapped onto the CSS property that does the same thing.
/// </summary>
public class YogaLayoutTests
{
    [Fact]
    public void EmitsYogaDefaultsWhereCssDiffers()
    {
        var style = new YogaLayout().BuildStyle();

        style.ShouldContain("display:flex;");
        style.ShouldContain("flex-direction:column;");
        style.ShouldContain("align-content:flex-start;");
        style.ShouldContain("flex-shrink:0;");
        style.ShouldContain("min-width:0;");
        style.ShouldContain("min-height:0;");
        style.ShouldContain("position:relative;");
        style.ShouldContain("box-sizing:border-box;");
    }


    [Fact]
    public void LengthsAcceptPointsPercentAndAuto()
    {
        var style = new YogaLayout { Width = "50%", Height = "120", FlexBasis = "auto", MaxWidth = "300px" }.BuildStyle();

        style.ShouldContain("width:50%;");
        style.ShouldContain("height:120px;");
        style.ShouldContain("flex-basis:auto;");
        style.ShouldContain("max-width:300px;");
    }


    [Fact]
    public void AnExplicitMinimumReplacesTheZeroDefault()
    {
        var style = new YogaLayout { MinWidth = "40" }.BuildStyle();

        style.ShouldContain("min-width:40px;");
        style.ShouldNotContain("min-width:0;");
    }


    [Fact]
    public void AbsoluteWithInsets()
    {
        var style = new YogaLayout { PositionType = YogaPositionType.Absolute, Right = "10", Bottom = "10%", Start = "4" }.BuildStyle();

        style.ShouldContain("position:absolute;");
        style.ShouldContain("right:10px;");
        style.ShouldContain("bottom:10%;");
        style.ShouldContain("inset-inline-start:4px;");
    }


    [Fact]
    public void StaticIgnoresInsets()
    {
        var style = new YogaLayout { PositionType = YogaPositionType.Static, Left = "10" }.BuildStyle();

        style.ShouldContain("position:static;");
        style.ShouldNotContain("left:");
    }


    [Fact]
    public void AutoMarginsOverrideTheShorthand()
    {
        var style = new YogaLayout { Margin = "8", AutoMargins = YogaEdges.Start | YogaEdges.Vertical }.BuildStyle();

        style.IndexOf("margin:8px;").ShouldBeLessThan(style.IndexOf("margin-inline-start:auto;"));
        style.ShouldContain("margin-top:auto;");
        style.ShouldContain("margin-bottom:auto;");
    }


    [Fact]
    public void GapsFallBackToGap()
    {
        var style = new YogaLayout { Gap = 8, RowGap = 2 }.BuildStyle();

        style.ShouldContain("row-gap:2px;");
        style.ShouldContain("column-gap:8px;");
    }


    [Fact]
    public void ContainerAndItemProperties()
    {
        var style = new YogaLayout
        {
            FlexDirection = YogaFlexDirection.RowReverse,
            JustifyContent = YogaJustify.SpaceEvenly,
            AlignItems = YogaAlign.Baseline,
            AlignContent = YogaAlign.SpaceBetween,
            FlexWrap = YogaWrap.WrapReverse,
            FlexGrow = 2,
            FlexShrink = 1,
            AlignSelf = YogaAlign.Center,
            AspectRatio = 1.5,
            Direction = YogaDirection.RTL,
            Padding = "8 16"
        }.BuildStyle();

        style.ShouldContain("flex-direction:row-reverse;");
        style.ShouldContain("justify-content:space-evenly;");
        style.ShouldContain("align-items:baseline;");
        style.ShouldContain("align-content:space-between;");
        style.ShouldContain("flex-wrap:wrap-reverse;");
        style.ShouldContain("flex-grow:2;");
        style.ShouldContain("flex-shrink:1;");
        style.ShouldContain("align-self:center;");
        style.ShouldContain("aspect-ratio:1.5;");
        style.ShouldContain("direction:rtl;");
        style.ShouldContain("padding:8px 16px;");
    }


    [Fact]
    public void DisplayNone()
        => new YogaLayout { Display = YogaDisplay.None }.BuildStyle().ShouldStartWith("display:none;");


    [Fact]
    public void GarbageLengthThrows()
        => Should.Throw<FormatException>(() => new YogaLayout { Width = "wide" }.BuildStyle());
}
