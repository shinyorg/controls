namespace Shiny.Controls.RangePickers;

/// <summary>The rules a selectable date range has to satisfy.</summary>
public sealed class DateRangeConstraints
{
    /// <summary>The earliest selectable day.</summary>
    public DateOnly? MinDate { get; set; }

    /// <summary>The latest selectable day.</summary>
    public DateOnly? MaxDate { get; set; }

    /// <summary>The shortest range, in days, counting both ends. Null or 1 allows a single day.</summary>
    public int? MinDays { get; set; }

    /// <summary>The longest range, in days, counting both ends.</summary>
    public int? MaxDays { get; set; }

    /// <summary>Whether a range can start and end on the same day. On by default.</summary>
    public bool AllowSingleDay { get; set; } = true;

    /// <summary>Days of the week that can never be picked (weekends for a booking form, say).</summary>
    public ICollection<DayOfWeek>? DisabledDaysOfWeek { get; set; }

    /// <summary>Specific days that can never be picked - holidays, fully booked dates.</summary>
    public ICollection<DateOnly>? DisabledDates { get; set; }

    /// <summary>Anything else that rules a day out.</summary>
    public Func<DateOnly, bool>? IsDateDisabled { get; set; }

    /// <summary>
    /// Whether a range may run across disabled days. Off by default: a hotel stay cannot straddle a
    /// night that is already booked. Turn it on when disabled days only stop a range starting or ending
    /// on them (weekends inside a "working days" report range).
    /// </summary>
    public bool AllowDisabledDatesInRange { get; set; }


    /// <summary>Whether <paramref name="date"/> itself can never be picked.</summary>
    public bool IsDisabled(DateOnly date)
    {
        if (this.MinDate is { } min && date < min)
            return true;

        if (this.MaxDate is { } max && date > max)
            return true;

        if (this.DisabledDaysOfWeek is { Count: > 0 } days && days.Contains(date.DayOfWeek))
            return true;

        if (this.DisabledDates is { Count: > 0 } dates && dates.Contains(date))
            return true;

        return this.IsDateDisabled?.Invoke(date) ?? false;
    }


    /// <summary>Why a range breaks the rules, or <see cref="DateRangeError.None"/> when it is fine.</summary>
    public DateRangeError Validate(DateRange range)
    {
        if (this.IsDisabled(range.Start) || this.IsDisabled(range.End))
            return DateRangeError.DisabledEndpoint;

        if (range.IsSingleDay && !this.AllowSingleDay)
            return DateRangeError.TooShort;

        if (this.MinDays is { } minDays && range.Days < minDays)
            return DateRangeError.TooShort;

        if (this.MaxDays is { } maxDays && range.Days > maxDays)
            return DateRangeError.TooLong;

        if (!this.AllowDisabledDatesInRange && this.FirstDisabledBetween(range.Start, range.End) is not null)
            return DateRangeError.ContainsDisabledDate;

        return DateRangeError.None;
    }


    /// <summary>The first disabled day strictly between two days, or null.</summary>
    internal DateOnly? FirstDisabledBetween(DateOnly start, DateOnly end)
    {
        for (var d = start.AddDays(1); d < end; d = d.AddDays(1))
        {
            if (this.IsDisabled(d))
                return d;
        }
        return null;
    }
}


/// <summary>Why a date range fails its <see cref="DateRangeConstraints"/>.</summary>
public enum DateRangeError
{
    /// <summary>The range satisfies every rule.</summary>
    None,
    /// <summary>The start or end day is disabled.</summary>
    DisabledEndpoint,
    /// <summary>Shorter than <see cref="DateRangeConstraints.MinDays"/>, or a single day when those are not allowed.</summary>
    TooShort,
    /// <summary>Longer than <see cref="DateRangeConstraints.MaxDays"/>.</summary>
    TooLong,
    /// <summary>Runs across a disabled day while <see cref="DateRangeConstraints.AllowDisabledDatesInRange"/> is off.</summary>
    ContainsDisabledDate
}
