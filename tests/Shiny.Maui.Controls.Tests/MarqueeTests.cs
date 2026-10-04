using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>The loop arithmetic, the angle maths, and how the marquee realizes and moves its items.</summary>
[Collection(ApplicationResourcesCollection.Name)]
public class MarqueeTests
{
    public MarqueeTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }


    // ---------------------------------------------------------------------------------------------
    // Geometry
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(90, 0, 1)]
    [InlineData(180, -1, 0)]
    [InlineData(270, 0, -1)]
    public void TheAxisIsExactAtRightAngles(double angle, double x, double y)
    {
        var axis = MarqueeGeometry.Axis(angle);

        axis.X.ShouldBe(x, 1e-9);
        axis.Y.ShouldBe(y, 1e-9);
    }


    [Fact]
    public void AnAngledAxisIsAUnitVector()
    {
        var (x, y) = MarqueeGeometry.Axis(-15);

        Math.Sqrt((x * x) + (y * y)).ShouldBe(1, 1e-9);
        x.ShouldBeGreaterThan(0);
        y.ShouldBeLessThan(0);
    }


    [Fact]
    public void AnUprightItemCoversItsProjectionAlongTheAxis()
    {
        var item = new Size(100, 40);

        MarqueeGeometry.ExtentAlong(item, 0, true).ShouldBe(100, 1e-9);
        MarqueeGeometry.ExtentAlong(item, 90, true).ShouldBe(40, 1e-9);
        MarqueeGeometry.ExtentAlong(item, 30, true).ShouldBe((100 * Math.Cos(Math.PI / 6)) + (40 * 0.5), 1e-9);

        // Tilted with the track, the item's own width is what lies along it.
        MarqueeGeometry.ExtentAlong(item, 30, false).ShouldBe(100);
    }


    [Fact]
    public void TheOffsetWrapsSeamlessly()
    {
        // 40s per 400-unit period is 10 units a second; 41s lands one second into the next loop.
        var offset = MarqueeGeometry.Advance(0, 400, TimeSpan.FromSeconds(41), TimeSpan.FromSeconds(40), 0, false);

        offset.ShouldBe(10, 1e-9);
    }


    [Fact]
    public void ReverseRunsBackwardsAndStillWraps()
    {
        var offset = MarqueeGeometry.Advance(0, 400, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(40), 0, true);

        offset.ShouldBe(390, 1e-9);
    }


    [Fact]
    public void SpeedOverridesDuration()
        => MarqueeGeometry.Advance(0, 1000, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(40), 50, false).ShouldBe(100, 1e-9);


    [Fact]
    public void EnoughCopiesAreMadeToCoverTheViewport()
    {
        // A 1000-wide viewport with a 150-long set: (1000 + 2*50) / 150 rounds up to 8, plus one.
        MarqueeGeometry.Copies(150, 1000, 50, 4).ShouldBe(9);

        // A long set still gets the minimum.
        MarqueeGeometry.Copies(2000, 300, 100, 4).ShouldBe(4);
    }


    [Fact]
    public void TheDiagonalViewportIsTheProjectedLength()
        => MarqueeGeometry.ViewportExtent(new Size(300, 400), 90).ShouldBe(400, 1e-9);


    // ---------------------------------------------------------------------------------------------
    // Control
    // ---------------------------------------------------------------------------------------------

    static Marquee Build(int items = 3, int repeat = 4) => new()
    {
        Repeat = repeat,
        ItemsSource = Enumerable.Range(0, items).Select(i => $"Item {i}").ToList(),
        ItemTemplate = new DataTemplate(() => new FixedView())
    };


    /// <summary>Reports a fixed size with no handler - a headless BoxView measures as nothing.</summary>
    sealed class FixedView : View
    {
        protected override Size MeasureOverride(double widthConstraint, double heightConstraint) => new(80, 30);
    }


    [Fact]
    public void EachCopyRealizesTheWholeSet()
    {
        var marquee = Build(items: 3, repeat: 4);

        marquee.GroupSize.ShouldBe(3);
        marquee.Copies.ShouldBe(4);
        marquee.Track.Children.Count.ShouldBe(12);
        ((View)marquee.Track.Children[4]).BindingContext.ShouldBe("Item 1");
    }


    [Fact]
    public void WithNoItemsSourceTheTemplateIsRealizedOncePerCopy()
    {
        var marquee = new Marquee
        {
            ItemTemplate = new DataTemplate(() => new Label { Text = "Hi" })
        };

        marquee.Track.Children.Count.ShouldBe(4);
    }


    [Fact]
    public void MeasuringFindsThePeriod()
    {
        var marquee = Build(items: 3);

        var size = marquee.MeasurePanel(500, double.PositiveInfinity);

        // Three 80-wide boxes, each followed by the 16 gap.
        marquee.Period.ShouldBe(3 * (80 + 16), 1e-6);
        size.Width.ShouldBe(500);
        size.Height.ShouldBe(30, 1e-6);
    }


    [Fact]
    public void AVerticalMarqueeIsAsTallAsItsSetAndAsWideAsItsWidestItem()
    {
        var marquee = Build(items: 3);
        marquee.Angle = 90;

        var size = marquee.MeasurePanel(double.PositiveInfinity, double.PositiveInfinity);

        size.Height.ShouldBe(3 * (30 + 16), 1e-6);
        size.Width.ShouldBe(80, 1e-6);
    }


    [Fact]
    public void AdvancingMovesTheTrackAgainstTheAxis()
    {
        var marquee = Build();
        marquee.MeasurePanel(500, 30);

        marquee.Advance(TimeSpan.FromSeconds(1));

        marquee.Offset.ShouldBeGreaterThan(0);
        marquee.Track.TranslationX.ShouldBe(-marquee.Offset, 1e-9);
        marquee.Track.TranslationY.ShouldBe(0, 1e-9);
    }


    [Fact]
    public void PausingStopsTheAdvance()
    {
        var marquee = Build();
        marquee.MeasurePanel(500, 30);
        marquee.IsRunning = false;

        marquee.Advance(TimeSpan.FromSeconds(1));

        marquee.IsPaused.ShouldBeTrue();
        marquee.Offset.ShouldBe(0);
    }


    [Fact]
    public void HoverPausesOnlyWhenAskedTo()
    {
        var marquee = Build();

        marquee.SetHovering(true);
        marquee.IsPaused.ShouldBeFalse();

        marquee.PauseOnHover = true;
        marquee.IsPaused.ShouldBeTrue();

        marquee.SetHovering(false);
        marquee.IsPaused.ShouldBeFalse();
    }


    [Fact]
    public void PressPausesWhenAskedTo()
    {
        var marquee = Build();
        marquee.PauseOnPress = true;

        marquee.SetPressing(true);
        marquee.IsPaused.ShouldBeTrue();
    }


    [Fact]
    public void ItemsTiltWithTheTrackOnlyWhenNotKeptUpright()
    {
        var marquee = Build();
        marquee.Angle = -15;

        marquee.Track.Children.Cast<VisualElement>().ShouldAllBe(v => v.Rotation == 0);

        marquee.KeepContentUpright = false;
        marquee.Track.Children.Cast<VisualElement>().ShouldAllBe(v => v.Rotation == -15);
    }


    [Fact]
    public void CopiesBeyondTheFirstAreHiddenFromScreenReaders()
    {
        var marquee = Build(items: 2);

        AutomationProperties.GetIsInAccessibleTree((BindableObject)marquee.Track.Children[0]).ShouldNotBe(false);
        AutomationProperties.GetIsInAccessibleTree((BindableObject)marquee.Track.Children[2]).ShouldBe(false);
    }


    [Fact]
    public void AWideViewportGrowsTheNumberOfCopies()
    {
        var marquee = Build(items: 3);
        marquee.MeasurePanel(2000, 30);

        marquee.ArrangePanel(new Rect(0, 0, 2000, 30));

        // Period 288, largest item 80: ceil((2000 + 160) / 288) + 1 = 9.
        marquee.Copies.ShouldBe(9);
        marquee.Track.Children.Count.ShouldBe(27);
    }


    [Fact]
    public void TheRunStartsBehindTheNearEdgeAndCoversTheFarEdgeAfterAFullPeriod()
    {
        var marquee = Build(items: 3);
        marquee.MeasurePanel(500, 30);
        marquee.ArrangePanel(new Rect(0, 0, 500, 30));

        var track = marquee.Track;
        var left = track.TranslationX + track.Placements.Min(r => r.Left);
        var right = track.Placements.Max(r => r.Right);
        var trackOrigin = -(500 / 2.0) - 80 + 250;   // where the first item starts, in panel space

        trackOrigin.ShouldBeLessThanOrEqualTo(0);
        (trackOrigin + right - marquee.Period).ShouldBeGreaterThanOrEqualTo(500);
        left.ShouldBe(0, 1e-9);
    }
}
