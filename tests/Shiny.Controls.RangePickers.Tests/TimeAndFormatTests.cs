using System.Globalization;

namespace Shiny.Controls.RangePickers.Tests;

public class TimeRangeConstraintsTests
{
    static TimeOnly T(int h, int m = 0) => new(h, m);

    [Fact]
    public void EndsAreAfterTheStartWithTheirDuration()
    {
        var c = new TimeRangeConstraints { Interval = TimeSpan.FromMinutes(30) };

        var ends = c.EndSlotsFor(T(9));

        ends[0].ShouldBe(new TimeSlot(T(9, 30), TimeSpan.FromMinutes(30)));
        ends.ShouldAllBe(e => e.Time > T(9) || e.Time == TimeOnly.MinValue);
        ends[^1].ShouldBe(new TimeSlot(TimeOnly.MinValue, TimeSpan.FromHours(15)));  // until midnight
    }

    [Fact]
    public void DurationLimitsTrimTheEnds()
    {
        var c = new TimeRangeConstraints { MinDuration = TimeSpan.FromHours(1), MaxDuration = TimeSpan.FromHours(2) };

        var ends = c.EndSlotsFor(T(9));

        ends.First().Time.ShouldBe(T(10));
        ends.Last().Time.ShouldBe(T(11));
    }

    [Fact]
    public void MinAndMaxTimeBoundTheDay()
    {
        var c = new TimeRangeConstraints { MinTime = T(8), MaxTime = T(18), Interval = TimeSpan.FromHours(1) };

        c.StartSlots().First().ShouldBe(T(8));
        c.StartSlots().Last().ShouldBe(T(17));   // 18:00 leaves no room for an end
        c.EndSlotsFor(T(17)).Single().Time.ShouldBe(T(18));
    }

    [Fact]
    public void OvernightEndsWrapPastMidnight()
    {
        var c = new TimeRangeConstraints { AllowOvernight = true, Interval = TimeSpan.FromHours(1), MaxDuration = TimeSpan.FromHours(8) };

        var ends = c.EndSlotsFor(T(22));

        ends.Last().ShouldBe(new TimeSlot(T(6), TimeSpan.FromHours(8)));
        c.IsValid(new TimeRange(T(22), T(6))).ShouldBeTrue();
    }

    [Fact]
    public void OvernightIsRejectedUnlessAllowed()
    {
        var c = new TimeRangeConstraints();

        c.IsValid(new TimeRange(T(22), T(6))).ShouldBeFalse();
        c.IsValid(new TimeRange(T(22), TimeOnly.MinValue)).ShouldBeTrue();  // until midnight
    }

    [Fact]
    public void MovingTheStartKeepsTheLength()
    {
        var c = new TimeRangeConstraints();

        c.EndFor(T(10), TimeSpan.FromMinutes(90)).ShouldBe(T(11, 30));
        c.EndFor(T(10), null).ShouldBe(T(11));
    }

    [Fact]
    public void RangeDurationAcrossMidnight()
        => new TimeRange(T(22), T(6)).Duration.ShouldBe(TimeSpan.FromHours(8));
}


public class RangeFormatterTests
{
    static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    // ICU puts a narrow no-break space before AM/PM; what matters is the shape, not which space.
    static string Plain(string s) => s.Replace('\u202F', ' ').Replace('\u00A0', ' ');

    [Fact]
    public void SameMonthSharesTheYear()
        => RangeFormatter.Format(new DateRange(new(2026, 3, 3), new(2026, 3, 9)), En).ShouldBe("Mar 3 – 9, 2026");

    [Fact]
    public void AcrossMonthsKeepsBothMonths()
        => RangeFormatter.Format(new DateRange(new(2026, 3, 28), new(2026, 4, 2)), En).ShouldBe("Mar 28 – Apr 2, 2026");

    [Fact]
    public void AcrossYearsCarriesBothYears()
        => RangeFormatter.Format(new DateRange(new(2025, 12, 28), new(2026, 1, 3)), En).ShouldBe("Dec 28, 2025 – Jan 3, 2026");

    [Fact]
    public void ASingleDayIsOneDate()
        => RangeFormatter.Format(new DateRange(new(2026, 3, 3), new(2026, 3, 3)), En).ShouldBe("Mar 3, 2026");

    [Fact]
    public void TimesUseTheCulturesClock()
    {
        Plain(RangeFormatter.Format(new TimeRange(new(9, 0), new(17, 30)), En)).ShouldBe("9:00 AM – 5:30 PM");
        RangeFormatter.Format(new TimeRange(new(9, 0), new(17, 30)), CultureInfo.GetCultureInfo("de-DE")).ShouldBe("09:00 – 17:30");
    }

    [Fact]
    public void SameDayDateTimesShowTheDateOnce()
        => Plain(RangeFormatter.Format(new DateTimeRange(new(2026, 3, 3, 9, 0, 0), new(2026, 3, 3, 17, 0, 0)), En))
            .ShouldBe("Mar 3, 2026, 9:00 AM – 5:00 PM");

    [Fact]
    public void GermanOrdering()
        => RangeFormatter.Format(new DateRange(new(2026, 3, 3), new(2026, 3, 9)), CultureInfo.GetCultureInfo("de-DE")).ShouldBe("3. – 9. März 2026");

    [Fact]
    public void Durations()
    {
        RangeFormatter.FormatDuration(TimeSpan.FromMinutes(90)).ShouldBe("1 hr 30 min");
        RangeFormatter.FormatDuration(TimeSpan.FromMinutes(45)).ShouldBe("45 min");
        RangeFormatter.FormatDuration(TimeSpan.FromHours(52)).ShouldBe("2 days 4 hr");
    }

    [Fact]
    public void AWholeDayRangeEndsAtTheNextMidnight()
    {
        var r = new DateRange(new(2026, 3, 3), new(2026, 3, 4)).ToDateTimeRange();
        r.End.ShouldBe(new DateTime(2026, 3, 5));
    }
}
