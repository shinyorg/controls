using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// <see cref="YogaLayout"/> is checked against the boxes Yoga itself produces for the same styles
/// (yogalayout.dev playground), including the places Yoga departs from CSS: column by default,
/// shrink 0, align-content flex-start, absolute children against the padding box.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class YogaLayoutTests
{
    public YogaLayoutTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }


    /// <summary>A view with a fixed natural size that counts its measures.</summary>
    sealed class Probe(double width, double height) : View
    {
        public int Measures;

        protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
        {
            this.Measures++;
            var margin = this.Margin;
            var size = new Size(width + margin.HorizontalThickness, height + margin.VerticalThickness);
            this.DesiredSize = size;
            return size;
        }
    }


    static Size Layout(YogaLayout layout, double width, double height)
    {
        var l = (Microsoft.Maui.ILayout)layout;
        var size = l.CrossPlatformMeasure(width, height);
        l.CrossPlatformArrange(new Rect(0, 0, width, height));
        return size;
    }

    static YogaLayout Yoga(params View[] children)
    {
        var yoga = new YogaLayout();
        foreach (var child in children)
            yoga.Children.Add(child);
        return yoga;
    }

    static void ShouldBe(Rect actual, double x, double y, double w, double h)
    {
        actual.X.ShouldBe(x, 0.01, $"X of {actual}");
        actual.Y.ShouldBe(y, 0.01, $"Y of {actual}");
        actual.Width.ShouldBe(w, 0.01, $"Width of {actual}");
        actual.Height.ShouldBe(h, 0.01, $"Height of {actual}");
    }


    [Fact]
    public void DefaultsToAColumnThatStretchesChildrenAcross()
    {
        var a = new Probe(50, 20);
        var b = new Probe(30, 40);
        var size = Layout(Yoga(a, b), 300, 200);

        ShouldBe(a.Frame, 0, 0, 300, 20);
        ShouldBe(b.Frame, 0, 20, 300, 40);
        size.Height.ShouldBe(60);
    }


    [Fact]
    public void GrowSharesTheFreeSpace()
    {
        var a = new Probe(50, 20);
        var b = new Probe(30, 20);
        YogaLayout.SetFlexGrow(a, 1);
        var yoga = Yoga(a, b);
        yoga.FlexDirection = YogaFlexDirection.Row;

        Layout(yoga, 300, 100);

        ShouldBe(a.Frame, 0, 0, 270, 100);
        ShouldBe(b.Frame, 270, 0, 30, 100);
    }


    [Fact]
    public void ChildrenDoNotShrinkByDefault()
    {
        var a = new Probe(200, 20);
        var b = new Probe(200, 20);
        var yoga = Yoga(a, b);
        yoga.FlexDirection = YogaFlexDirection.Row;
        yoga.AlignItems = YogaAlign.FlexStart;

        Layout(yoga, 300, 100);

        // Yoga's FlexShrink is 0, so the row overflows rather than squeezing.
        ShouldBe(a.Frame, 0, 0, 200, 20);
        ShouldBe(b.Frame, 200, 0, 200, 20);
    }


    [Fact]
    public void ShrinkIsWeightedByBasis()
    {
        var a = new Probe(300, 20);
        var b = new Probe(100, 20);
        YogaLayout.SetFlexShrink(a, 1);
        YogaLayout.SetFlexShrink(b, 1);
        var yoga = Yoga(a, b);
        yoga.FlexDirection = YogaFlexDirection.Row;

        Layout(yoga, 300, 100);

        // 100 over; a gives 3/4 of it, b 1/4.
        a.Frame.Width.ShouldBe(225, 0.01);
        b.Frame.Width.ShouldBe(75, 0.01);
        b.Frame.X.ShouldBe(225, 0.01);
    }


    [Fact]
    public void PercentSizesAreTakenOfTheContentBox()
    {
        var a = new Probe(10, 10);
        var b = new Probe(10, 10);
        YogaLayout.SetNodeWidth(a, YogaValue.Percent(50));
        YogaLayout.SetNodeHeight(b, YogaValue.Percent(25));
        var yoga = Yoga(a, b);
        yoga.Padding = new Thickness(10);
        yoga.AlignItems = YogaAlign.FlexStart;

        Layout(yoga, 220, 220);

        ShouldBe(a.Frame, 10, 10, 100, 10);
        ShouldBe(b.Frame, 10, 20, 10, 50);
    }


    [Fact]
    public void FlexBasisPercentOverridesTheMeasuredSize()
    {
        var a = new Probe(10, 10);
        YogaLayout.SetFlexBasis(a, YogaValue.Percent(40));
        var yoga = Yoga(a);
        yoga.FlexDirection = YogaFlexDirection.Row;

        Layout(yoga, 200, 50);

        a.Frame.Width.ShouldBe(80, 0.01);
    }


    [Fact]
    public void AbsoluteChildIsPlacedAgainstThePaddingBoxAndLeavesTheFlow()
    {
        var flow = new Probe(50, 20);
        var overlay = new Probe(40, 40);
        YogaLayout.SetPositionType(overlay, YogaPositionType.Absolute);
        YogaLayout.SetLeft(overlay, 0);
        YogaLayout.SetTop(overlay, 5);
        YogaLayout.SetRight(overlay, 30);
        var yoga = Yoga(overlay, flow);
        yoga.Padding = new Thickness(10);

        var size = Layout(yoga, 300, 200);

        // Left and Right both set: the width follows from them.
        ShouldBe(overlay.Frame, 0, 5, 270, 40);
        // The flow child ignores the absolute sibling and sits at the top of the content box.
        ShouldBe(flow.Frame, 10, 10, 280, 20);
        size.Height.ShouldBe(40); // 20 + padding; the absolute child adds nothing
    }


    [Fact]
    public void AbsoluteChildAnchoredBottomRight()
    {
        var badge = new Probe(50, 20);
        YogaLayout.SetPositionType(badge, YogaPositionType.Absolute);
        YogaLayout.SetRight(badge, 10);
        YogaLayout.SetBottom(badge, YogaValue.Percent(10));

        Layout(Yoga(badge), 300, 200);

        ShouldBe(badge.Frame, 240, 160, 50, 20);
    }


    [Fact]
    public void AbsoluteChildWithoutInsetsUsesJustifyAndAlign()
    {
        var badge = new Probe(50, 20);
        YogaLayout.SetPositionType(badge, YogaPositionType.Absolute);
        var yoga = Yoga(badge);
        yoga.JustifyContent = YogaJustify.Center;
        yoga.AlignItems = YogaAlign.FlexEnd;

        Layout(yoga, 300, 200);

        ShouldBe(badge.Frame, 250, 90, 50, 20);
    }


    [Fact]
    public void AspectRatioDerivesTheMissingDimension()
    {
        var a = new Probe(10, 10);
        YogaLayout.SetNodeWidth(a, 100);
        YogaLayout.SetAspectRatio(a, 2);
        var b = new Probe(10, 10);
        YogaLayout.SetFlexGrow(b, 1);
        YogaLayout.SetAspectRatio(b, 1);
        var yoga = Yoga(a, b);
        yoga.FlexDirection = YogaFlexDirection.Row;
        // Not stretched: a stretched row child would take its width from the row's height instead (next test).
        yoga.AlignItems = YogaAlign.FlexStart;

        Layout(yoga, 300, 300);

        ShouldBe(a.Frame, 0, 0, 100, 50);
        // b grows to 200 wide, and a square follows.
        ShouldBe(b.Frame, 100, 0, 200, 200);
    }


    [Fact]
    public void StretchedChildWithAnAspectRatioTakesItsHeightFromTheStretchedWidth()
    {
        var image = new Probe(40, 40) { Margin = new Thickness(10, 0) };
        YogaLayout.SetAspectRatio(image, 2);
        var below = new Probe(50, 20);

        Layout(Yoga(image, below), 300, 400);

        ShouldBe(image.Frame, 10, 0, 280, 140);
        below.Frame.Y.ShouldBe(140, 0.01);
    }


    [Fact]
    public void AutoMarginPushesAChildToTheEnd()
    {
        var a = new Probe(50, 20);
        var b = new Probe(50, 20);
        YogaLayout.SetAutoMargins(b, YogaEdges.Start);
        var yoga = Yoga(a, b);
        yoga.FlexDirection = YogaFlexDirection.Row;
        yoga.JustifyContent = YogaJustify.Center; // auto margins win over justify

        Layout(yoga, 300, 100);

        a.Frame.X.ShouldBe(0, 0.01);
        b.Frame.X.ShouldBe(250, 0.01);
    }


    [Fact]
    public void AutoCrossMarginsCentre()
    {
        var a = new Probe(50, 20);
        YogaLayout.SetAutoMargins(a, YogaEdges.Vertical);
        var yoga = Yoga(a);
        yoga.FlexDirection = YogaFlexDirection.Row;

        Layout(yoga, 300, 100);

        ShouldBe(a.Frame, 0, 40, 50, 20);
    }


    [Fact]
    public void WrapUsesGapsAndPacksLinesAtTheStart()
    {
        var a = new Probe(40, 20);
        var b = new Probe(40, 20);
        var c = new Probe(40, 20);
        var yoga = Yoga(a, b, c);
        yoga.FlexDirection = YogaFlexDirection.Row;
        yoga.FlexWrap = YogaWrap.Wrap;
        yoga.Gap = 10;

        Layout(yoga, 100, 200);

        ShouldBe(a.Frame, 0, 0, 40, 20);
        ShouldBe(b.Frame, 50, 0, 40, 20);
        // AlignContent is FlexStart in Yoga, so the second line sits right under the first.
        ShouldBe(c.Frame, 0, 30, 40, 20);
    }


    [Fact]
    public void RowGapAndColumnGapOverrideGap()
    {
        var a = new Probe(40, 20);
        var b = new Probe(40, 20);
        var yoga = Yoga(a, b);
        yoga.Gap = 100;
        yoga.RowGap = 7;

        Layout(yoga, 100, 200);

        b.Frame.Y.ShouldBe(27, 0.01);
    }


    [Fact]
    public void JustifySpaceBetween()
    {
        var a = new Probe(50, 20);
        var b = new Probe(50, 20);
        var c = new Probe(50, 20);
        var yoga = Yoga(a, b, c);
        yoga.FlexDirection = YogaFlexDirection.Row;
        yoga.JustifyContent = YogaJustify.SpaceBetween;

        Layout(yoga, 300, 100);

        a.Frame.X.ShouldBe(0, 0.01);
        b.Frame.X.ShouldBe(125, 0.01);
        c.Frame.X.ShouldBe(250, 0.01);
    }


    [Fact]
    public void ColumnReverseStartsAtTheBottom()
    {
        var a = new Probe(50, 20);
        var b = new Probe(50, 30);
        var yoga = Yoga(a, b);
        yoga.FlexDirection = YogaFlexDirection.ColumnReverse;

        Layout(yoga, 100, 200);

        a.Frame.Y.ShouldBe(180, 0.01);
        b.Frame.Y.ShouldBe(150, 0.01);
    }


    [Fact]
    public void InheritedRightToLeftRowStartsAtTheRightAndKeepsPhysicalMargins()
    {
        var a = new Probe(50, 20) { Margin = new Thickness(5, 0, 0, 0) };
        var b = new Probe(50, 20);
        var yoga = Yoga(a, b);
        yoga.FlexDirection = YogaFlexDirection.Row;
        _ = new ContentPage { FlowDirection = FlowDirection.RightToLeft, Content = yoga };

        Layout(yoga, 300, 100);

        // Margins stay physical (MAUI's ComputeFrame does not swap them), so a's left margin sits between it and b.
        a.Frame.X.ShouldBe(250, 0.01);
        b.Frame.X.ShouldBe(195, 0.01);
    }


    [Fact]
    public void RelativeInsetsNudgeWithoutMovingSiblings()
    {
        var a = new Probe(50, 20);
        var b = new Probe(50, 20);
        YogaLayout.SetLeft(a, 5);
        YogaLayout.SetBottom(a, 3);
        var yoga = Yoga(a, b);
        yoga.AlignItems = YogaAlign.FlexStart;

        Layout(yoga, 300, 200);

        ShouldBe(a.Frame, 5, -3, 50, 20);
        ShouldBe(b.Frame, 0, 20, 50, 20);
    }


    [Fact]
    public void StaticPositionIgnoresInsets()
    {
        var a = new Probe(50, 20);
        YogaLayout.SetPositionType(a, YogaPositionType.Static);
        YogaLayout.SetLeft(a, 40);
        var yoga = Yoga(a);
        yoga.AlignItems = YogaAlign.FlexStart;

        Layout(yoga, 300, 200);

        a.Frame.X.ShouldBe(0, 0.01);
    }


    [Fact]
    public void DisplayNoneTakesNoSpace()
    {
        var a = new Probe(50, 20);
        var hidden = new Probe(50, 500);
        var b = new Probe(50, 20);
        YogaLayout.SetDisplay(hidden, YogaDisplay.None);

        Layout(Yoga(a, hidden, b), 300, 200);

        b.Frame.Y.ShouldBe(20, 0.01);
        hidden.Frame.Size.ShouldBe(Size.Zero);
    }


    [Fact]
    public void MaxWidthFreezesAGrowerAndTheRestGoesToItsSibling()
    {
        var a = new Probe(0, 20);
        var b = new Probe(0, 20);
        YogaLayout.SetFlexGrow(a, 1);
        YogaLayout.SetFlexGrow(b, 1);
        YogaLayout.SetMaxWidth(a, 100);
        var yoga = Yoga(a, b);
        yoga.FlexDirection = YogaFlexDirection.Row;

        Layout(yoga, 400, 50);

        a.Frame.Width.ShouldBe(100, 0.01);
        b.Frame.Width.ShouldBe(300, 0.01);
    }


    [Fact]
    public void MarginsAreOutsideTheBox()
    {
        var a = new Probe(50, 20) { Margin = new Thickness(10, 5) };
        var b = new Probe(50, 20);
        var yoga = Yoga(a, b);

        var size = Layout(yoga, 300, 200);

        ShouldBe(a.Frame, 10, 5, 280, 20);
        b.Frame.Y.ShouldBe(30, 0.01);
        size.Height.ShouldBe(50);
    }


    [Fact]
    public void AlignSelfOverridesAlignItems()
    {
        var a = new Probe(50, 20);
        YogaLayout.SetAlignSelf(a, YogaAlign.Center);
        Layout(Yoga(a), 300, 200);

        ShouldBe(a.Frame, 125, 0, 50, 20);
    }


    [Fact]
    public void MeasuresToContentWhenUnbounded()
    {
        var a = new Probe(50, 20);
        var b = new Probe(70, 30);
        var yoga = Yoga(a, b);
        yoga.FlexDirection = YogaFlexDirection.Row;
        yoga.Gap = 4;
        yoga.Padding = new Thickness(2);

        var size = ((Microsoft.Maui.ILayout)yoga).CrossPlatformMeasure(double.PositiveInfinity, double.PositiveInfinity);

        size.ShouldBe(new Size(128, 34));
    }


    [Fact]
    public void ArrangeAfterMeasureAtTheSameSizeResolvesOnce()
    {
        var a = new Probe(50, 20);
        var yoga = Yoga(a);

        Layout(yoga, 300, 200);

        yoga.ResolveCount.ShouldBe(1);
    }


    [Fact]
    public void ArrangeAtANewSizeResolvesAgain()
    {
        var a = new Probe(50, 20);
        YogaLayout.SetFlexGrow(a, 1);
        var yoga = Yoga(a);
        yoga.FlexDirection = YogaFlexDirection.Row;
        var l = (Microsoft.Maui.ILayout)yoga;

        l.CrossPlatformMeasure(300, 100);
        l.CrossPlatformArrange(new Rect(0, 0, 200, 100));

        a.Frame.Width.ShouldBe(200, 0.01);
    }


    [Theory]
    [InlineData("50%", 50, YogaUnit.Percent)]
    [InlineData("12", 12, YogaUnit.Point)]
    [InlineData("12.5px", 12.5, YogaUnit.Point)]
    [InlineData(" auto ", double.NaN, YogaUnit.Auto)]
    [InlineData("", double.NaN, YogaUnit.Undefined)]
    public void YogaValueParses(string text, double value, YogaUnit unit)
    {
        var parsed = YogaValue.Parse(text);
        parsed.Unit.ShouldBe(unit);
        if (!double.IsNaN(value))
            parsed.Value.ShouldBe(value);
    }


    [Fact]
    public void YogaValueRejectsGarbage()
        => Should.Throw<FormatException>(() => YogaValue.Parse("wide"));


    [Fact]
    public void PercentOfAnUnboundedAxisIsAuto()
        => YogaValue.Percent(50).Resolve(double.PositiveInfinity).ShouldBe(double.NaN);
}
