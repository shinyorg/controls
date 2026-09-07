using System.Globalization;

namespace Shiny.Controls.Gantt.Tests;

public class GanttGeometryTests
{
    static readonly DateTimeOffset Origin = Plan.Monday;
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;


    [Fact]
    public void DateToX_AndBack_RoundTrip()
    {
        foreach (var ppd in new[] { 0.5, 1, 24, 400d })
        {
            var date = Origin.AddDays(3).AddHours(7);
            var x = GanttGeometry.DateToX(date, Origin, ppd);

            GanttGeometry.XToDate(x, Origin, ppd).ShouldBe(date, TimeSpan.FromSeconds(1));
        }
    }


    [Fact]
    public void SpanToWidth_NeverGoesNegative() =>
        GanttGeometry.SpanToWidth(Origin.AddDays(5), Origin, 30).ShouldBe(0);


    [Fact]
    public void XToDate_IsSafeAtZeroZoom() =>
        GanttGeometry.XToDate(500, Origin, 0).ShouldBe(Origin);


    [Theory]
    [InlineData(50_000, GanttTimeScale.Minute)]
    [InlineData(3000, GanttTimeScale.QuarterHour)]
    [InlineData(800, GanttTimeScale.Hour)]
    [InlineData(40, GanttTimeScale.Day)]
    [InlineData(6, GanttTimeScale.Week)]
    [InlineData(1.5, GanttTimeScale.Month)]
    [InlineData(0.05, GanttTimeScale.Year)]
    public void ResolveScale_PicksTheFinestThatStillFits(double pixelsPerDay, GanttTimeScale expected) =>
        GanttGeometry.ResolveScale(pixelsPerDay).ShouldBe(expected);


    [Fact]
    public void PixelsPerDayFor_RoundTripsThroughResolveScale()
    {
        foreach (var scale in new[] { GanttTimeScale.Hour, GanttTimeScale.Day, GanttTimeScale.Week, GanttTimeScale.Month })
        {
            var ppd = GanttGeometry.PixelsPerDayFor(scale, GanttGeometry.DefaultMinTickWidth);
            GanttGeometry.ResolveScale(ppd).ShouldBe(scale);
        }
    }


    [Fact]
    public void FitToWidth_FillsTheAvailableSpace()
    {
        var ppd = GanttGeometry.FitToWidth(Origin, Origin.AddDays(10), 500);
        GanttGeometry.SpanToWidth(Origin, Origin.AddDays(10), ppd).ShouldBe(500, 0.01);
    }


    [Fact]
    public void ZoomAnchored_KeepsTheInstantUnderThePointer()
    {
        const double anchorX = 320;
        var before = GanttGeometry.XToDate(anchorX, Origin, 40);

        var newOrigin = GanttGeometry.ZoomAnchored(Origin, anchorX, 40, 120);
        var after = GanttGeometry.XToDate(anchorX, newOrigin, 120);

        after.ShouldBe(before, TimeSpan.FromSeconds(1));
    }


    [Fact]
    public void FloorTo_Day_DropsTheTime() =>
        GanttGeometry.FloorTo(Origin.AddHours(17.5), GanttTimeScale.Day).ShouldBe(Origin);


    [Fact]
    public void FloorTo_Month_AndQuarter()
    {
        var may = new DateTimeOffset(2026, 5, 18, 13, 0, 0, TimeSpan.Zero);

        GanttGeometry.FloorTo(may, GanttTimeScale.Month).ShouldBe(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
        GanttGeometry.FloorTo(may, GanttTimeScale.Quarter).ShouldBe(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));
        GanttGeometry.FloorTo(may, GanttTimeScale.Year).ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }


    [Fact]
    public void FloorTo_Week_UsesTheCulturesFirstDay()
    {
        var wednesday = Origin.AddDays(2);

        // Invariant starts its week on Sunday, so a Wednesday floors back past the Monday origin.
        GanttGeometry.FloorTo(wednesday, GanttTimeScale.Week, Invariant).ShouldBe(Origin.AddDays(-1));

        var german = CultureInfo.GetCultureInfo("de-DE");
        GanttGeometry.FloorTo(wednesday, GanttTimeScale.Week, german).ShouldBe(Origin);
    }


    [Fact]
    public void NextTick_StepsCalendarUnitsNotNominalDays()
    {
        // February is 28 days and March is 31; both must get exactly one column.
        var feb = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        GanttGeometry.NextTick(feb, GanttTimeScale.Month).ShouldBe(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
        GanttGeometry.NextTick(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), GanttTimeScale.Month)
            .ShouldBe(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));
    }


    [Fact]
    public void SnapToScale_RoundsToTheNearestBoundary()
    {
        GanttGeometry.SnapToScale(Origin.AddHours(5), GanttTimeScale.Day).ShouldBe(Origin);
        GanttGeometry.SnapToScale(Origin.AddHours(20), GanttTimeScale.Day).ShouldBe(Origin.AddDays(1));
    }


    [Fact]
    public void SnapToInterval_RoundsToWholeMultiples()
    {
        var snapped = GanttGeometry.SnapToInterval(Origin.AddMinutes(23), TimeSpan.FromMinutes(15));
        snapped.ShouldBe(Origin.AddMinutes(30));
    }


    [Fact]
    public void SnapToInterval_IsANoOpForANonPositiveInterval() =>
        GanttGeometry.SnapToInterval(Origin.AddMinutes(23), TimeSpan.Zero).ShouldBe(Origin.AddMinutes(23));


    [Fact]
    public void Snap_WorkingTime_PushesForwardOutOfTheWeekend()
    {
        // A bar dropped on a Saturday means the following Monday, not back to Friday.
        var saturday = Origin.AddDays(5).AddHours(10);

        var snapped = GanttGeometry.Snap(
            saturday, GanttSnapMode.WorkingTime, GanttTimeScale.Day, TimeSpan.Zero, GanttCalendar.StandardDays
        );

        snapped.ShouldBe(Origin.AddDays(7));
    }


    [Fact]
    public void Snap_None_LeavesTheValueAlone() =>
        GanttGeometry.Snap(Origin.AddHours(5), GanttSnapMode.None, GanttTimeScale.Day, TimeSpan.Zero).ShouldBe(Origin.AddHours(5));


    [Fact]
    public void GenerateTicks_CoversTheRangeContiguously()
    {
        var ticks = GanttGeometry.GenerateTicks(Origin, Origin.AddDays(7), GanttTimeScale.Day, Origin, 40, Invariant);

        ticks.Count.ShouldBe(7);
        ticks[0].X.ShouldBe(0);
        for (var i = 1; i < ticks.Count; i++)
            ticks[i].X.ShouldBe(ticks[i - 1].X + ticks[i - 1].Width, 0.001);
    }


    [Fact]
    public void GenerateTicks_ClipsTheEdgeCellsToTheVisibleRange()
    {
        // Starting mid-month, the first month column must be drawn at its visible width, not its full one.
        var from = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);
        var ticks = GanttGeometry.GenerateTicks(from, from.AddDays(30), GanttTimeScale.Month, from, 10, Invariant);

        ticks[0].Start.Day.ShouldBe(1);
        ticks[0].X.ShouldBe(0);
        ticks[0].Width.ShouldBe(12 * 10, 0.001); // 20 Jan to 1 Feb is 12 days
    }


    [Fact]
    public void GenerateTicks_ShadesNonWorkingDays()
    {
        var ticks = GanttGeometry.GenerateTicks(
            Origin, Origin.AddDays(7), GanttTimeScale.Day, Origin, 40, Invariant, GanttCalendar.StandardDays
        );

        ticks.Select(x => x.IsNonWorking).ShouldBe([false, false, false, false, false, true, true]);
    }


    [Fact]
    public void GenerateTicks_DoesNotShadeCoarseScales()
    {
        // A week containing one holiday is not a non-working week, and shading it would misrepresent
        // the plan far more loudly than leaving it plain.
        var ticks = GanttGeometry.GenerateTicks(
            Origin, Origin.AddDays(28), GanttTimeScale.Week, Origin, 6, Invariant, GanttCalendar.StandardDays
        );

        ticks.ShouldAllBe(x => !x.IsNonWorking);
    }


    [Fact]
    public void GenerateTicks_MarksTheCellContainingNow()
    {
        var now = Origin.AddDays(2).AddHours(9);
        var ticks = GanttGeometry.GenerateTicks(Origin, Origin.AddDays(7), GanttTimeScale.Day, Origin, 40, Invariant, null, now);

        ticks.Count(x => x.IsToday).ShouldBe(1);
        ticks.Single(x => x.IsToday).Start.ShouldBe(Origin.AddDays(2));
    }


    [Fact]
    public void GenerateTicks_IsEmptyForAnInvertedOrZeroRange()
    {
        GanttGeometry.GenerateTicks(Origin.AddDays(5), Origin, GanttTimeScale.Day, Origin, 40).ShouldBeEmpty();
        GanttGeometry.GenerateTicks(Origin, Origin.AddDays(5), GanttTimeScale.Auto, Origin, 40).ShouldBeEmpty();
    }


    [Fact]
    public void GenerateTicks_IsBoundedRatherThanUnlimited()
    {
        // Minute ticks across a decade would be five million cells; the caller gets a clamped list
        // instead of a frozen UI.
        var ticks = GanttGeometry.GenerateTicks(Origin, Origin.AddYears(10), GanttTimeScale.Minute, Origin, 40);
        ticks.Count.ShouldBeLessThanOrEqualTo(5_000);
    }


    [Fact]
    public void UpperTierFor_IsAlwaysCoarser()
    {
        foreach (var scale in new[] { GanttTimeScale.Hour, GanttTimeScale.Day, GanttTimeScale.Week, GanttTimeScale.Month })
        {
            var upper = GanttGeometry.UpperTierFor(scale);
            GanttGeometry.ApproximateTickDays(upper).ShouldBeGreaterThan(GanttGeometry.ApproximateTickDays(scale));
        }
    }


    [Fact]
    public void FormatTick_UsesTheRequestedCulture()
    {
        GanttGeometry.FormatTick(Origin, GanttTimeScale.Day, Invariant).ShouldBe("5");
        GanttGeometry.FormatTick(Origin, GanttTimeScale.Month, Invariant).ShouldBe("Jan 2026");
        GanttGeometry.FormatTick(Origin, GanttTimeScale.Quarter, Invariant).ShouldBe("Q1 2026");
        GanttGeometry.FormatTick(Origin, GanttTimeScale.Year, Invariant).ShouldBe("2026");
    }
}
