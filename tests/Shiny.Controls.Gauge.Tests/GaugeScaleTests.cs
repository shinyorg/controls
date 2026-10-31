namespace Shiny.Controls.Gauge.Tests;

public class GaugeScaleTests
{
    [Theory]
    [InlineData(0, -135)]
    [InlineData(50, 0)]
    [InlineData(100, 135)]
    public void AngleOf_MapsOntoTheDefaultDial(double value, double expected)
    {
        var scale = new GaugeScale(0, 100, GaugeScale.DefaultStartAngle, GaugeScale.DefaultSweepAngle);
        scale.AngleOf(value).ShouldBe(expected, 1e-9);
    }


    [Fact]
    public void Normalized_RepairsAnInvertedRangeAndAnOversizedSweep()
    {
        var scale = new GaugeScale(10, 5, 0, 720).Normalized();
        scale.Maximum.ShouldBe(11);
        scale.SweepAngle.ShouldBe(360);
    }


    [Theory]
    [InlineData(0, 0, -1)]
    [InlineData(90, 1, 0)]
    [InlineData(180, 0, 1)]
    [InlineData(270, -1, 0)]
    public void PointAt_IsCompassAngleInYDownSpace(double angle, double x, double y)
    {
        var point = GaugeScale.PointAt(0, 0, 1, angle);
        point.X.ShouldBe(x, 1e-9);
        point.Y.ShouldBe(y, 1e-9);
    }


    [Fact]
    public void Clamp_TreatsNaNAsTheMinimum()
        => new GaugeScale(20, 40, 0, 180).Clamp(double.NaN).ShouldBe(20);
}


public class GaugeTicksTests
{
    [Theory]
    [InlineData(8, 1)]
    [InlineData(240, 20)]
    [InlineData(7000, 1000)]
    [InlineData(100, 10)]
    [InlineData(1, 0.1)]
    [InlineData(160, 20)]
    [InlineData(300, 50)]
    public void NiceInterval_PicksAPrintableStep(double range, double expected)
        => GaugeTicks.NiceInterval(range).ShouldBe(expected, 1e-9);


    [Fact]
    public void Generate_InterleavesMinorTicks()
    {
        var ticks = GaugeTicks.Generate(new GaugeScale(0, 8, -135, 270), 1, 4);

        ticks.Count.ShouldBe(41);
        ticks.Count(t => t.IsMajor).ShouldBe(9);
        ticks[0].ShouldBe(new GaugeTick(0, true));
        ticks[1].IsMajor.ShouldBeFalse();
        ticks[^1].ShouldBe(new GaugeTick(8, true));
    }


    [Fact]
    public void Generate_AlwaysLabelsTheMaximum()
    {
        var ticks = GaugeTicks.Generate(new GaugeScale(0, 7, -135, 270), 2, 0);
        ticks.Select(t => t.Value).ShouldBe([0, 2, 4, 6, 7]);
        ticks.ShouldAllBe(t => t.IsMajor);
    }


    [Fact]
    public void Generate_DoesNotDropTheLastTickToFloatingPoint()
    {
        var ticks = GaugeTicks.Generate(new GaugeScale(0, 0.3, -135, 270), 0.1, 0);
        ticks.Count.ShouldBe(4);
        ticks[^1].Value.ShouldBe(0.3);
    }


    [Fact]
    public void Generate_RefusesToDrawAMillionTicks()
    {
        var ticks = GaugeTicks.Generate(new GaugeScale(0, 1_000_000, -135, 270), 1, 9);
        ticks.Count.ShouldBeLessThanOrEqualTo(GaugeTicks.MaximumTickCount + 1);
    }
}


public class GaugeLayoutTests
{
    [Fact]
    public void Fit_SemicircleUsesTheWidthOfAWideBox()
    {
        // A half disc is twice as wide as it is tall, so a 200x100 box is a near-perfect fit.
        var layout = GaugeLayout.Fit(200, 112, -90, 180, 0, reserveReadout: false);

        layout.Radius.ShouldBe(100, 1e-9);
        layout.CenterX.ShouldBe(100, 1e-9);
        // The pivot sits near the bottom, leaving just the hub's allowance below it.
        layout.CenterY.ShouldBe(100, 1e-9);
    }


    [Fact]
    public void Fit_FullDialIsCentred()
    {
        var layout = GaugeLayout.Fit(300, 300, 0, 360, 10, reserveReadout: true);

        layout.CenterX.ShouldBe(150, 1e-9);
        layout.CenterY.ShouldBe(150, 1e-9);
        layout.Radius.ShouldBe(140, 1e-9);
    }


    [Fact]
    public void Fit_ReservingTheReadoutShrinksASemicircle()
    {
        var without = GaugeLayout.Fit(200, 120, -90, 180, 0, reserveReadout: false);
        var with = GaugeLayout.Fit(200, 120, -90, 180, 0, reserveReadout: true);
        with.Radius.ShouldBeLessThan(without.Radius);
    }


    [Fact]
    public void Fit_AFaceKeepsTheWholeDisc()
    {
        // A speedometer's arc stops short of the bottom, so fitting the arc alone clips the face flat.
        var (_, _, _, arcOnly) = GaugeLayout.UnitBounds(-135, 270, reserveReadout: false);
        var (minX, minY, maxX, maxY) = GaugeLayout.UnitBounds(-135, 270, reserveReadout: false, includeFace: true);

        arcOnly.ShouldBeLessThan(1);
        (minX, minY, maxX, maxY).ShouldBe((-1d, -1d, 1d, 1d));
    }


    [Fact]
    public void Fit_EmptyBoxHasNoRadius()
        => GaugeLayout.Fit(0, 100, -135, 270, 0, true).Radius.ShouldBe(0);


    [Fact]
    public void ReadoutAngle_IsOppositeTheMiddleOfTheArc()
        => GaugeLayout.ReadoutAngle(-135, 270).ShouldBe(180);
}
