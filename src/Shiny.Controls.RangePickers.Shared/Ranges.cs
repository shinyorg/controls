namespace Shiny.Controls.RangePickers;

/// <summary>An inclusive span of whole days. <see cref="Start"/> is never after <see cref="End"/>.</summary>
public readonly record struct DateRange
{
    public DateRange(DateOnly start, DateOnly end)
    {
        if (end < start)
            (start, end) = (end, start);

        this.Start = start;
        this.End = end;
    }

    public DateOnly Start { get; }
    public DateOnly End { get; }

    /// <summary>How many days the range covers, counting both ends - a single day is 1.</summary>
    public int Days => this.End.DayNumber - this.Start.DayNumber + 1;

    public bool IsSingleDay => this.Start == this.End;

    public bool Contains(DateOnly date) => date >= this.Start && date <= this.End;

    public bool Overlaps(DateRange other) => this.Start <= other.End && other.Start <= this.End;

    public IEnumerable<DateOnly> EachDay()
    {
        for (var d = this.Start; d <= this.End; d = d.AddDays(1))
            yield return d;
    }

    /// <summary>
    /// The range as instants: midnight at the start through midnight after the end, so the last day
    /// is included in full. The usual shape for a query's <c>&gt;= start AND &lt; end</c>.
    /// </summary>
    public DateTimeRange ToDateTimeRange(DateTimeKind kind = DateTimeKind.Unspecified) => new(
        DateTime.SpecifyKind(this.Start.ToDateTime(TimeOnly.MinValue), kind),
        DateTime.SpecifyKind(this.End.AddDays(1).ToDateTime(TimeOnly.MinValue), kind)
    );

    public override string ToString() => RangeFormatter.Format(this);
}


/// <summary>
/// A span of the clock. <see cref="End"/> before <see cref="Start"/> means the range runs over
/// midnight (a 22:00 – 06:00 night shift), which only a picker with overnight ranges allowed produces.
/// </summary>
public readonly record struct TimeRange(TimeOnly Start, TimeOnly End)
{
    public bool CrossesMidnight => this.End < this.Start;

    public TimeSpan Duration => this.CrossesMidnight
        ? this.End.ToTimeSpan() + TimeSpan.FromDays(1) - this.Start.ToTimeSpan()
        : this.End - this.Start;

    public bool Contains(TimeOnly time) => this.CrossesMidnight
        ? time >= this.Start || time <= this.End
        : time >= this.Start && time <= this.End;

    /// <summary>The range on a given day. An overnight range ends on the following day.</summary>
    public DateTimeRange On(DateOnly date) => new(
        date.ToDateTime(this.Start),
        (this.CrossesMidnight ? date.AddDays(1) : date).ToDateTime(this.End)
    );

    public override string ToString() => RangeFormatter.Format(this);
}


/// <summary>A span between two instants. <see cref="Start"/> is never after <see cref="End"/>.</summary>
public readonly record struct DateTimeRange
{
    public DateTimeRange(DateTime start, DateTime end)
    {
        if (end < start)
            (start, end) = (end, start);

        this.Start = start;
        this.End = end;
    }

    public DateTime Start { get; }
    public DateTime End { get; }

    public TimeSpan Duration => this.End - this.Start;

    /// <summary>The calendar days the range touches.</summary>
    public DateRange Dates => new(DateOnly.FromDateTime(this.Start), DateOnly.FromDateTime(this.End));

    public bool Contains(DateTime instant) => instant >= this.Start && instant <= this.End;

    public bool Overlaps(DateTimeRange other) => this.Start < other.End && other.Start < this.End;

    public override string ToString() => RangeFormatter.Format(this);
}
