namespace Shiny.Controls.RangePickers;

/// <summary>How a calendar day should be drawn. Several can apply at once.</summary>
[Flags]
public enum CalendarDayState
{
    None = 0,
    Today = 1 << 0,
    /// <summary>Belongs to the previous or next month and is only filling out the grid.</summary>
    OutsideMonth = 1 << 1,
    /// <summary>Can never be picked.</summary>
    Disabled = 1 << 2,
    /// <summary>Selectable in general, but not as the end of the range that is half picked (too short, too long, or across a blocked day).</summary>
    Unavailable = 1 << 3,
    RangeStart = 1 << 4,
    RangeEnd = 1 << 5,
    /// <summary>Strictly between the start and end.</summary>
    InRange = 1 << 6,
    /// <summary>Between the start and the hovered day, while the end is still being chosen.</summary>
    Preview = 1 << 7,
    /// <summary>The day the pointer is over while the end is still being chosen.</summary>
    PreviewEnd = 1 << 8,
    Weekend = 1 << 9
}


/// <summary>What a tap on a day did.</summary>
public enum DateRangeTapResult
{
    /// <summary>Nothing - the day cannot be picked right now.</summary>
    Ignored,
    /// <summary>The day became the start of a new range; the end is still to be picked.</summary>
    Started,
    /// <summary>The day completed the range.</summary>
    Completed
}


/// <summary>
/// Picking a date range by tapping days: the first tap sets the start, the second the end, and a third
/// starts over. Tapping before the start moves the start rather than producing a backwards range.
/// </summary>
/// <remarks>
/// While the end is being chosen, days that cannot end the range - shorter than
/// <see cref="DateRangeConstraints.MinDays"/>, longer than <see cref="DateRangeConstraints.MaxDays"/>,
/// or past a blocked day - report <see cref="CalendarDayState.Unavailable"/>, so the calendar can grey
/// them out rather than accept a tap and then refuse it.
/// </remarks>
public sealed class DateRangeSelector
{
    DateOnly? blockedAfterStart;
    DateOnly? blockedFor;
    bool blockedComputed;

    public DateRangeSelector(DateRangeConstraints? constraints = null)
        => this.Constraints = constraints ?? new DateRangeConstraints();

    /// <summary>The rules. Call <see cref="InvalidateConstraints"/> after changing it in place.</summary>
    public DateRangeConstraints Constraints { get; set; }

    public DateOnly? Start { get; private set; }

    public DateOnly? End { get; private set; }

    /// <summary>The day under the pointer, for the hover preview. Hosts without a pointer leave it null.</summary>
    public DateOnly? Hover { get; set; }

    /// <summary>Today, for <see cref="CalendarDayState.Today"/>. Settable so tests and time-zone-aware hosts can pin it.</summary>
    public DateOnly Today { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>The completed range, or null while nothing or only a start is picked.</summary>
    public DateRange? Range => this.Start is { } s && this.End is { } e ? new DateRange(s, e) : null;

    /// <summary>A start is picked and the end is not.</summary>
    public bool IsPickingEnd => this.Start is not null && this.End is null;


    public void InvalidateConstraints() => this.blockedComputed = false;


    /// <summary>Replaces the selection outright - a bound value arriving, or a preset.</summary>
    public void SetRange(DateRange? range)
    {
        this.Start = range?.Start;
        this.End = range?.End;
        this.blockedComputed = false;
    }


    /// <summary>Puts a lone start down, as if it had just been tapped.</summary>
    public void SetStart(DateOnly? start)
    {
        this.Start = start;
        this.End = null;
        this.blockedComputed = false;
    }


    public void Clear() => this.SetRange(null);


    public DateRangeTapResult Tap(DateOnly date)
    {
        if (this.Constraints.IsDisabled(date))
            return DateRangeTapResult.Ignored;

        if (this.Start is not { } start || this.End is not null || date < start)
        {
            this.SetStart(date);

            // A range that may only be one day long, or must be exactly one, finishes on the first tap.
            if (this.Constraints.MaxDays == 1 && this.Constraints.AllowSingleDay)
            {
                this.End = date;
                return DateRangeTapResult.Completed;
            }
            return DateRangeTapResult.Started;
        }

        if (!this.CanEndAt(date))
            return DateRangeTapResult.Ignored;

        this.End = date;
        return DateRangeTapResult.Completed;
    }


    /// <summary>Whether <paramref name="date"/> could end the half-picked range.</summary>
    public bool CanEndAt(DateOnly date)
    {
        if (this.Start is not { } start || date < start || this.Constraints.IsDisabled(date))
            return false;

        var days = date.DayNumber - start.DayNumber + 1;
        if (days == 1 && !this.Constraints.AllowSingleDay)
            return false;

        if (this.Constraints.MinDays is { } min && days < min)
            return false;

        if (this.Constraints.MaxDays is { } max && days > max)
            return false;

        if (!this.Constraints.AllowDisabledDatesInRange && this.BlockedAfterStart() is { } blocked && date > blocked)
            return false;

        return true;
    }


    public CalendarDayState StateOf(DateOnly date, int displayYear = 0, int displayMonth = 0)
    {
        var state = CalendarDayState.None;

        if (date == this.Today)
            state |= CalendarDayState.Today;

        if (displayMonth != 0 && (date.Month != displayMonth || date.Year != displayYear))
            state |= CalendarDayState.OutsideMonth;

        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            state |= CalendarDayState.Weekend;

        if (this.Constraints.IsDisabled(date))
            state |= CalendarDayState.Disabled;

        if (this.Start is not { } start)
            return state;

        if (date == start)
            state |= CalendarDayState.RangeStart;

        if (this.End is { } end)
        {
            if (date == end)
                state |= CalendarDayState.RangeEnd;
            else if (date > start && date < end)
                state |= CalendarDayState.InRange;

            return state;
        }

        // Picking the end.
        if (date > start && !this.CanEndAt(date) && (state & CalendarDayState.Disabled) == 0)
            state |= CalendarDayState.Unavailable;

        if (this.Hover is { } hover && hover > start && this.CanEndAt(hover))
        {
            if (date == hover)
                state |= CalendarDayState.PreviewEnd;
            else if (date > start && date < hover)
                state |= CalendarDayState.Preview;
        }

        return state;
    }


    DateOnly? BlockedAfterStart()
    {
        if (this.blockedComputed && this.blockedFor == this.Start)
            return this.blockedAfterStart;

        this.blockedFor = this.Start;
        this.blockedComputed = true;
        this.blockedAfterStart = null;

        if (this.Start is not { } start)
            return null;

        // Bounded: nothing past MaxDays or MaxDate can be picked anyway, and an unconstrained calendar
        // never needs to know about a blocked day more than a few years out.
        var limit = start.AddDays(this.Constraints.MaxDays ?? 3660);
        if (this.Constraints.MaxDate is { } maxDate && maxDate < limit)
            limit = maxDate;

        for (var d = start.AddDays(1); d <= limit; d = d.AddDays(1))
        {
            if (this.Constraints.IsDisabled(d))
            {
                this.blockedAfterStart = d;
                break;
            }
        }
        return this.blockedAfterStart;
    }
}
