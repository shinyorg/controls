namespace Shiny.Controls.Gantt.Tests;

public class GanttCalendarTests
{
    static readonly DateTimeOffset Monday = Plan.Monday;
    static readonly GanttCalendar Weekdays = GanttCalendar.StandardDays;
    static readonly GanttCalendar NineToFive = GanttCalendar.StandardHours;


    [Fact]
    public void Continuous_ShortCircuits()
    {
        GanttCalendar.Continuous.IsContinuous.ShouldBeTrue();
        GanttCalendar.Continuous.IsWorkingTime(Monday.AddDays(5)).ShouldBeTrue();
        GanttCalendar.Continuous.WorkingTimeBetween(Monday, Monday.AddDays(10)).ShouldBe(TimeSpan.FromDays(10));
    }


    [Theory]
    [InlineData(0, true)]   // Monday
    [InlineData(4, true)]   // Friday
    [InlineData(5, false)]  // Saturday
    [InlineData(6, false)]  // Sunday
    [InlineData(7, true)]   // the next Monday
    public void WorkingDays_ExcludeTheWeekend(int dayOffset, bool expected) =>
        Weekdays.IsWorkingDay(DateOnly.FromDateTime(Monday.AddDays(dayOffset).DateTime)).ShouldBe(expected);


    [Fact]
    public void WorkingTimeBetween_SkipsTheWeekend()
    {
        // Monday to the following Monday is seven calendar days but only five worked ones.
        Weekdays.WorkingTimeBetween(Monday, Monday.AddDays(7)).ShouldBe(TimeSpan.FromDays(5));
    }


    [Fact]
    public void WorkingTimeBetween_IsSignedSoItComposesWithAdd()
    {
        var forward = Weekdays.WorkingTimeBetween(Monday, Monday.AddDays(7));
        var backward = Weekdays.WorkingTimeBetween(Monday.AddDays(7), Monday);

        backward.ShouldBe(-forward);
    }


    [Fact]
    public void AddWorkingTime_JumpsTheWeekend()
    {
        // Three working days from Thursday lands on the following Tuesday, not on Sunday.
        var thursday = Monday.AddDays(3);
        Weekdays.AddWorkingTime(thursday, TimeSpan.FromDays(3)).ShouldBe(Monday.AddDays(8));
    }


    [Fact]
    public void AddWorkingTime_ZeroStillNormalizesOntoAWorkingBoundary()
    {
        // This is what makes a milestone dropped on a Saturday land on Monday morning.
        var saturday = Monday.AddDays(5);
        Weekdays.AddWorkingTime(saturday, TimeSpan.Zero).ShouldBe(Monday.AddDays(7));
    }


    [Fact]
    public void SubtractWorkingTime_IsTheInverseOfAdd()
    {
        var from = Monday.AddDays(1).AddHours(10);
        foreach (var days in new[] { 0.5, 1, 3, 7.25, 20 })
        {
            var span = TimeSpan.FromDays(days);
            var forward = Weekdays.AddWorkingTime(from, span);
            Weekdays.WorkingTimeBetween(from, forward).ShouldBe(span);
            Weekdays.SubtractWorkingTime(forward, span).ShouldBe(from);
        }
    }


    [Fact]
    public void Shifts_BoundTheWorkingDay()
    {
        NineToFive.WorkingTimePerDay.ShouldBe(TimeSpan.FromHours(8));
        NineToFive.IsWorkingTime(Monday.AddHours(8)).ShouldBeFalse();
        NineToFive.IsWorkingTime(Monday.AddHours(9)).ShouldBeTrue();

        // Half open: the instant the shift ends belongs to the evening, not to the shift.
        NineToFive.IsWorkingTime(Monday.AddHours(17)).ShouldBeFalse();
    }


    [Fact]
    public void AddWorkingTime_SpillsIntoTheNextShift()
    {
        // Six hours from 14:00 Monday: three hours to 17:00, then three more from 09:00 Tuesday.
        var start = Monday.AddHours(14);
        NineToFive.AddWorkingTime(start, TimeSpan.FromHours(6)).ShouldBe(Monday.AddDays(1).AddHours(12));
    }


    [Fact]
    public void NextWorkingTime_MovesForwardOutOfAGap()
    {
        NineToFive.NextWorkingTime(Monday.AddHours(3)).ShouldBe(Monday.AddHours(9));
        NineToFive.NextWorkingTime(Monday.AddHours(20)).ShouldBe(Monday.AddDays(1).AddHours(9));
        NineToFive.NextWorkingTime(Monday.AddHours(11)).ShouldBe(Monday.AddHours(11));
    }


    [Fact]
    public void PreviousWorkingTime_TreatsTheValueAsAnExclusiveEnd()
    {
        // 17:00 is the finish of a working day, so it stays put rather than being pulled back.
        NineToFive.PreviousWorkingTime(Monday.AddHours(17)).ShouldBe(Monday.AddHours(17));
        NineToFive.PreviousWorkingTime(Monday.AddHours(20)).ShouldBe(Monday.AddHours(17));
        NineToFive.PreviousWorkingTime(Monday.AddDays(1).AddHours(2)).ShouldBe(Monday.AddHours(17));
    }


    [Fact]
    public void Holidays_AreNotWorked()
    {
        var wednesday = DateOnly.FromDateTime(Monday.AddDays(2).DateTime);
        var calendar = new GanttCalendar(Weekdays.WorkingDays, [GanttShift.FullDay], [wednesday]);

        calendar.IsWorkingDay(wednesday).ShouldBeFalse();
        calendar.WorkingTimeBetween(Monday, Monday.AddDays(5)).ShouldBe(TimeSpan.FromDays(4));
    }


    [Fact]
    public void AnException_CanTurnASaturdayIntoAWorkingDay()
    {
        var saturday = DateOnly.FromDateTime(Monday.AddDays(5).DateTime);
        var calendar = new GanttCalendar(
            Weekdays.WorkingDays,
            [GanttShift.FullDay],
            exceptions: [new KeyValuePair<DateOnly, IReadOnlyList<GanttShift>>(saturday, [GanttShift.FullDay])]
        );

        calendar.IsWorkingDay(saturday).ShouldBeTrue();
        calendar.WorkingTimeBetween(Monday, Monday.AddDays(7)).ShouldBe(TimeSpan.FromDays(6));
    }


    [Fact]
    public void AnEmptyException_TurnsAWeekdayOff()
    {
        var wednesday = DateOnly.FromDateTime(Monday.AddDays(2).DateTime);
        var calendar = new GanttCalendar(
            Weekdays.WorkingDays,
            [GanttShift.FullDay],
            exceptions: [new KeyValuePair<DateOnly, IReadOnlyList<GanttShift>>(wednesday, [])]
        );

        calendar.IsWorkingDay(wednesday).ShouldBeFalse();
    }


    [Fact]
    public void NonWorkingIntervals_MergeIntoOneWeekendBand()
    {
        // Friday evening, Saturday, Sunday and Monday morning are found a day at a time; the caller
        // must get one band or the seams show through a translucent fill.
        var intervals = NineToFive.NonWorkingIntervals(Monday.AddDays(4).AddHours(12), Monday.AddDays(7).AddHours(12));

        intervals.Count.ShouldBe(1);
        intervals[0].Start.ShouldBe(Monday.AddDays(4).AddHours(17));
        intervals[0].End.ShouldBe(Monday.AddDays(7).AddHours(9));
    }


    [Fact]
    public void NonWorkingIntervals_AreClippedToTheRequestedRange()
    {
        var from = Monday.AddDays(5).AddHours(6);
        var to = Monday.AddDays(6).AddHours(6);

        var intervals = Weekdays.NonWorkingIntervals(from, to);

        intervals.Count.ShouldBe(1);
        intervals[0].Start.ShouldBe(from);
        intervals[0].End.ShouldBe(to);
    }


    [Fact]
    public void NonWorkingIntervals_AreEmptyForAContinuousCalendar() =>
        GanttCalendar.Continuous.NonWorkingIntervals(Monday, Monday.AddDays(30)).ShouldBeEmpty();


    [Fact]
    public void OverlappingShifts_AreMerged()
    {
        var calendar = new GanttCalendar(
            null,
            [GanttShift.FromHours(9, 13), GanttShift.FromHours(12, 17)]
        );

        calendar.Shifts.Count.ShouldBe(1);
        calendar.Shifts[0].ShouldBe(GanttShift.FromHours(9, 17));
    }


    [Fact]
    public void ACalendarWithNoWorkingDaysClampsRatherThanHanging()
    {
        // Every walk is bounded on purpose: a control that freezes inside a layout pass is worse
        // than one that gives up and returns the input.
        var dead = new GanttCalendar([], [GanttShift.FullDay]);

        Should.CompleteIn(
            () => dead.AddWorkingTime(Monday, TimeSpan.FromDays(1)),
            TimeSpan.FromSeconds(5)
        );
    }
}
