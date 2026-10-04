namespace Shiny.Controls.RangePickers.Tests;

public class DateRangeSelectorTests
{
    static DateOnly D(int month, int day) => new(2026, month, day);

    [Fact]
    public void FirstTapStartsSecondTapCompletes()
    {
        var s = new DateRangeSelector();

        s.Tap(D(3, 3)).ShouldBe(DateRangeTapResult.Started);
        s.IsPickingEnd.ShouldBeTrue();
        s.Tap(D(3, 9)).ShouldBe(DateRangeTapResult.Completed);

        s.Range.ShouldBe(new DateRange(D(3, 3), D(3, 9)));
    }

    [Fact]
    public void TappingBeforeTheStartMovesTheStart()
    {
        var s = new DateRangeSelector();
        s.Tap(D(3, 10));

        s.Tap(D(3, 5)).ShouldBe(DateRangeTapResult.Started);

        s.Start.ShouldBe(D(3, 5));
        s.End.ShouldBeNull();
    }

    [Fact]
    public void ATapAfterACompleteRangeStartsOver()
    {
        var s = new DateRangeSelector();
        s.Tap(D(3, 3));
        s.Tap(D(3, 9));

        s.Tap(D(3, 20)).ShouldBe(DateRangeTapResult.Started);
        s.Range.ShouldBeNull();
    }

    [Fact]
    public void TappingTheStartAgainIsASingleDay()
    {
        var s = new DateRangeSelector();
        s.Tap(D(3, 3));

        s.Tap(D(3, 3)).ShouldBe(DateRangeTapResult.Completed);
        s.Range!.Value.IsSingleDay.ShouldBeTrue();
    }

    [Fact]
    public void SingleDayCanBeForbidden()
    {
        var s = new DateRangeSelector(new DateRangeConstraints { AllowSingleDay = false });
        s.Tap(D(3, 3));

        s.Tap(D(3, 3)).ShouldBe(DateRangeTapResult.Ignored);
    }

    [Fact]
    public void DisabledDaysCannotBeTapped()
    {
        var s = new DateRangeSelector(new DateRangeConstraints { MinDate = D(3, 5) });

        s.Tap(D(3, 4)).ShouldBe(DateRangeTapResult.Ignored);
        s.StateOf(D(3, 4)).HasFlag(CalendarDayState.Disabled).ShouldBeTrue();
    }

    [Fact]
    public void MinAndMaxDaysLimitTheEnd()
    {
        var s = new DateRangeSelector(new DateRangeConstraints { MinDays = 3, MaxDays = 7 });
        s.Tap(D(3, 1));

        s.CanEndAt(D(3, 2)).ShouldBeFalse();  // 2 days
        s.CanEndAt(D(3, 3)).ShouldBeTrue();   // 3 days
        s.CanEndAt(D(3, 7)).ShouldBeTrue();   // 7 days
        s.CanEndAt(D(3, 8)).ShouldBeFalse();  // 8 days
        s.StateOf(D(3, 8)).HasFlag(CalendarDayState.Unavailable).ShouldBeTrue();
        s.Tap(D(3, 8)).ShouldBe(DateRangeTapResult.Ignored);
    }

    [Fact]
    public void ARangeCannotCrossABlockedDayByDefault()
    {
        var s = new DateRangeSelector(new DateRangeConstraints { DisabledDates = [D(3, 5)] });
        s.Tap(D(3, 1));

        s.CanEndAt(D(3, 4)).ShouldBeTrue();
        s.CanEndAt(D(3, 6)).ShouldBeFalse();
    }

    [Fact]
    public void ARangeMayCrossBlockedDaysWhenAllowed()
    {
        var s = new DateRangeSelector(new DateRangeConstraints
        {
            DisabledDaysOfWeek = [DayOfWeek.Saturday, DayOfWeek.Sunday],
            AllowDisabledDatesInRange = true
        });
        s.Tap(new DateOnly(2026, 3, 6));   // a Friday

        s.CanEndAt(new DateOnly(2026, 3, 9)).ShouldBeTrue();   // the Monday after
        s.CanEndAt(new DateOnly(2026, 3, 7)).ShouldBeFalse();  // the Saturday itself
    }

    [Fact]
    public void MaxDaysOfOneCompletesOnTheFirstTap()
    {
        var s = new DateRangeSelector(new DateRangeConstraints { MaxDays = 1 });

        s.Tap(D(3, 3)).ShouldBe(DateRangeTapResult.Completed);
        s.Range.ShouldBe(new DateRange(D(3, 3), D(3, 3)));
    }

    [Fact]
    public void StatesDescribeTheRange()
    {
        var s = new DateRangeSelector { Today = D(3, 4) };
        s.SetRange(new DateRange(D(3, 3), D(3, 6)));

        s.StateOf(D(3, 3)).HasFlag(CalendarDayState.RangeStart).ShouldBeTrue();
        s.StateOf(D(3, 4)).HasFlag(CalendarDayState.InRange).ShouldBeTrue();
        s.StateOf(D(3, 4)).HasFlag(CalendarDayState.Today).ShouldBeTrue();
        s.StateOf(D(3, 6)).HasFlag(CalendarDayState.RangeEnd).ShouldBeTrue();
        s.StateOf(D(3, 7)).HasFlag(CalendarDayState.InRange).ShouldBeFalse();
        s.StateOf(D(2, 28), 2026, 3).HasFlag(CalendarDayState.OutsideMonth).ShouldBeTrue();
    }

    [Fact]
    public void HoverPreviewsTheEnd()
    {
        var s = new DateRangeSelector();
        s.Tap(D(3, 3));
        s.Hover = D(3, 6);

        s.StateOf(D(3, 4)).HasFlag(CalendarDayState.Preview).ShouldBeTrue();
        s.StateOf(D(3, 6)).HasFlag(CalendarDayState.PreviewEnd).ShouldBeTrue();
        s.StateOf(D(3, 7)).HasFlag(CalendarDayState.Preview).ShouldBeFalse();
    }

    [Fact]
    public void ValidateExplainsTheRule()
    {
        var c = new DateRangeConstraints { MaxDays = 3, DisabledDates = [D(3, 10)] };

        c.Validate(new DateRange(D(3, 1), D(3, 3))).ShouldBe(DateRangeError.None);
        c.Validate(new DateRange(D(3, 1), D(3, 4))).ShouldBe(DateRangeError.TooLong);
        c.Validate(new DateRange(D(3, 9), D(3, 11))).ShouldBe(DateRangeError.ContainsDisabledDate);
        c.Validate(new DateRange(D(3, 10), D(3, 11))).ShouldBe(DateRangeError.DisabledEndpoint);
    }
}


public class CalendarMathTests
{
    [Fact]
    public void AMonthIsSixWholeWeeks()
    {
        var cells = CalendarMath.MonthCells(2026, 3, DayOfWeek.Sunday);

        cells.Count.ShouldBe(42);
        cells[0].DayOfWeek.ShouldBe(DayOfWeek.Sunday);
        cells.ShouldContain(new DateOnly(2026, 3, 1));
        cells.ShouldContain(new DateOnly(2026, 3, 31));
    }

    [Fact]
    public void MondayFirstWeeks()
    {
        CalendarMath.MonthCells(2026, 3, DayOfWeek.Monday)[0].ShouldBe(new DateOnly(2026, 2, 23));
        CalendarMath.WeekdayOrder(DayOfWeek.Monday)[6].ShouldBe(DayOfWeek.Sunday);
    }
}


public class PresetTests
{
    static readonly DateOnly Today = new(2026, 3, 18); // a Wednesday

    [Fact]
    public void LastSevenDaysCountsToday()
        => DateRangePresets.Last7Days.Resolve(Today).ShouldBe(new DateRange(new DateOnly(2026, 3, 12), Today));

    [Fact]
    public void LastMonthIsTheWholePreviousMonth()
        => DateRangePresets.LastMonth.Resolve(Today).ShouldBe(new DateRange(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)));

    [Fact]
    public void ThisWeekFollowsTheFirstDayOfWeek()
    {
        DateRangePresets.ThisWeek(DayOfWeek.Monday).Resolve(Today).Start.ShouldBe(new DateOnly(2026, 3, 16));
        DateRangePresets.ThisWeek(DayOfWeek.Sunday).Resolve(Today).Start.ShouldBe(new DateOnly(2026, 3, 15));
    }

    [Fact]
    public void BookingPresetsNeverStartInThePast()
        => DateRangePresets.Booking(DayOfWeek.Sunday).ShouldAllBe(p => p.Resolve(Today).Start >= Today);
}
