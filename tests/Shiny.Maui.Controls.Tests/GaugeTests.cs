using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Gauge;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The MAUI gauge. A headless host has no dispatcher to tick, so these drive the needle through
/// <see cref="Gauge.Advance"/> - the same call the frame clock makes.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class GaugeTests
{
    static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    public GaugeTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }


    static void Settle(Gauge gauge, int maxFrames = 2000)
    {
        for (var i = 0; i < maxFrames && gauge.IsNeedleMoving; i++)
            gauge.Advance(Frame);
    }


    [Fact]
    public void TheNeedleAnimatesRatherThanJumping()
    {
        var gauge = new Gauge();
        gauge.Value = 80;

        gauge.NeedleValue.ShouldBe(0);
        gauge.IsNeedleMoving.ShouldBeTrue();

        gauge.Advance(Frame);
        gauge.NeedleValue.ShouldBeInRange(0.0001, 79.9);

        Settle(gauge);
        gauge.NeedleValue.ShouldBe(80);
    }


    [Fact]
    public void AnimationNoneSnaps()
    {
        var gauge = new Gauge { Animation = GaugeAnimation.None };
        gauge.Value = 42;
        gauge.NeedleValue.ShouldBe(42);
        gauge.IsNeedleMoving.ShouldBeFalse();
    }


    [Fact]
    public void TurningAnimationOffMidFlightLandsTheNeedle()
    {
        var gauge = new Gauge { Value = 90 };
        gauge.Advance(Frame);

        gauge.Animation = GaugeAnimation.None;

        gauge.NeedleValue.ShouldBe(90);
    }


    [Fact]
    public void SettlingRaisesNeedleSettledOnce()
    {
        var gauge = new Gauge { Value = 30 };
        var raised = 0;
        gauge.NeedleSettled += (_, _) => raised++;

        Settle(gauge);
        gauge.Advance(Frame);

        raised.ShouldBe(1);
    }


    [Fact]
    public void ValuesAreClampedToTheScale()
    {
        var gauge = new Gauge { Animation = GaugeAnimation.None, Maximum = 8 };
        gauge.Value = 12;
        gauge.NeedleValue.ShouldBe(8);
    }


    [Fact]
    public void ShrinkingTheScalePullsTheNeedleIn()
    {
        var gauge = new Gauge { Animation = GaugeAnimation.None, Value = 90 };
        gauge.Maximum = 50;
        gauge.NeedleValue.ShouldBe(50);
    }


    [Fact]
    public void PeakTracksTheHighestReadingNotTheOvershoot()
    {
        var gauge = new Gauge { SpringDamping = 0.3 };
        gauge.Value = 60;
        gauge.Value = 40;
        Settle(gauge);

        gauge.PeakValue.ShouldBe(60);

        gauge.ResetPeak();
        gauge.PeakValue.ShouldBe(40);
    }


    [Fact]
    public void SweepTouchesTheMaximumAndComesBack()
    {
        var gauge = new Gauge { Maximum = 8, Value = 1 };
        Settle(gauge);

        gauge.Sweep();
        var highest = 0.0;
        for (var i = 0; i < 2000 && gauge.IsNeedleMoving; i++)
        {
            gauge.Advance(Frame);
            highest = Math.Max(highest, gauge.NeedleValue);
        }

        highest.ShouldBeGreaterThan(7.9);
        gauge.NeedleValue.ShouldBe(1);
    }


    [Fact]
    public void ArcTakesTheColourOfTheRangeTheNeedleIsIn()
    {
        var gauge = new Gauge { Maximum = 8 };
        gauge.Ranges.Add(new GaugeRange { Start = 0, End = 6, Color = Colors.Green });
        gauge.Ranges.Add(new GaugeRange { Start = 6.5, End = 8, Color = Colors.Red });

        gauge.RangeAt(3)!.Color.ShouldBe(Colors.Green);
        gauge.RangeAt(7)!.Color.ShouldBe(Colors.Red);
        gauge.RangeAt(6.2).ShouldBeNull();
    }


    [Fact]
    public void RangesInheritTheBindingContext()
    {
        var context = new object();
        var gauge = new Gauge();
        var range = new GaugeRange();

        gauge.BindingContext = context;
        gauge.Ranges.Add(range);

        range.BindingContext.ShouldBe(context);
    }


    [Fact]
    public void TheDescriptionReadsTheValue()
    {
        var gauge = new Gauge { Animation = GaugeAnimation.None, Title = "Speed", Units = "km/h" };
        gauge.Value = 88;
        SemanticProperties.GetDescription(gauge).ShouldBe("Speed 88 km/h");
    }


    [Fact]
    public void ABadTickFormatFallsBack()
    {
        var gauge = new Gauge { TickLabelFormat = "{0:0" };
        gauge.FormatTickLabel(5).ShouldBe("5");
    }


    [Theory]
    [InlineData(GaugeNeedleStyle.Tapered, -135, 270)]
    [InlineData(GaugeNeedleStyle.Line, -90, 180)]
    [InlineData(GaugeNeedleStyle.None, 0, 360)]
    public void EveryLayoutDrawsWithoutThrowing(GaugeNeedleStyle style, double start, double sweep)
    {
        var gauge = new Gauge
        {
            NeedleStyle = style,
            StartAngle = start,
            SweepAngle = sweep,
            ShowPeak = true,
            FaceColor = Colors.Black,
            Title = "RPM",
            Units = "x1000",
            Value = 50
        };
        gauge.Ranges.Add(new GaugeRange { Start = 80, End = 100 });
        gauge.Advance(Frame);

        var canvas = new PictureCanvas(0, 0, 300, 200);
        Should.NotThrow(() => gauge.Drawable.Draw(canvas, new RectF(0, 0, 300, 200)));
        Should.NotThrow(() => gauge.Drawable.Draw(canvas, new RectF(0, 0, 0, 0)));
    }
}
