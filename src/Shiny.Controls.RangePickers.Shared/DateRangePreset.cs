namespace Shiny.Controls.RangePickers;

/// <summary>
/// A named shortcut ("Last 7 days") the picker lists beside the calendar. Resolved against today
/// each time it is chosen, so a picker left open overnight still means what it says.
/// </summary>
public sealed class DateRangePreset(string label, Func<DateOnly, DateRange> resolve)
{
    /// <summary>The text shown in the preset list.</summary>
    public string Label { get; } = label;

    /// <summary>The range this preset means on <paramref name="today"/>.</summary>
    public DateRange Resolve(DateOnly today) => resolve(today);

    /// <summary>The <see cref="Label"/>.</summary>
    public override string ToString() => this.Label;
}


/// <summary>
/// The usual presets. Labels are English; build your own <see cref="DateRangePreset"/> with the same
/// resolver to translate one.
/// </summary>
public static class DateRangePresets
{
    /// <summary>Just today.</summary>
    public static DateRangePreset Today { get; } = new("Today", t => new(t, t));
    /// <summary>Just yesterday.</summary>
    public static DateRangePreset Yesterday { get; } = new("Yesterday", t => new(t.AddDays(-1), t.AddDays(-1)));
    /// <summary>The last 7 days, counting today.</summary>
    public static DateRangePreset Last7Days { get; } = LastDays(7);
    /// <summary>The last 14 days, counting today.</summary>
    public static DateRangePreset Last14Days { get; } = LastDays(14);
    /// <summary>The last 30 days, counting today.</summary>
    public static DateRangePreset Last30Days { get; } = LastDays(30);
    /// <summary>The last 90 days, counting today.</summary>
    public static DateRangePreset Last90Days { get; } = LastDays(90);
    /// <summary>The next 7 days, counting today.</summary>
    public static DateRangePreset Next7Days { get; } = NextDays(7);
    /// <summary>The next 30 days, counting today.</summary>
    public static DateRangePreset Next30Days { get; } = NextDays(30);
    /// <summary>The whole of the current month.</summary>
    public static DateRangePreset ThisMonth { get; } = new("This month", t => new(CalendarMath.StartOfMonth(t), CalendarMath.StartOfMonth(t).AddMonths(1).AddDays(-1)));
    /// <summary>The whole of the previous month.</summary>
    public static DateRangePreset LastMonth { get; } = new("Last month", t =>
    {
        var start = CalendarMath.StartOfMonth(t).AddMonths(-1);
        return new(start, start.AddMonths(1).AddDays(-1));
    });
    /// <summary>The 1st of the current month through today.</summary>
    public static DateRangePreset MonthToDate { get; } = new("Month to date", t => new(CalendarMath.StartOfMonth(t), t));
    /// <summary>The whole of the current year.</summary>
    public static DateRangePreset ThisYear { get; } = new("This year", t => new(new DateOnly(t.Year, 1, 1), new DateOnly(t.Year, 12, 31)));
    /// <summary>The whole of the previous year.</summary>
    public static DateRangePreset LastYear { get; } = new("Last year", t => new(new DateOnly(t.Year - 1, 1, 1), new DateOnly(t.Year - 1, 12, 31)));
    /// <summary>January 1st of the current year through today.</summary>
    public static DateRangePreset YearToDate { get; } = new("Year to date", t => new(new DateOnly(t.Year, 1, 1), t));

    /// <summary>"Last N days", counting today.</summary>
    public static DateRangePreset LastDays(int days, string? label = null)
        => new(label ?? $"Last {days} days", t => new(t.AddDays(-(days - 1)), t));

    /// <summary>"Next N days", counting today.</summary>
    public static DateRangePreset NextDays(int days, string? label = null)
        => new(label ?? $"Next {days} days", t => new(t, t.AddDays(days - 1)));

    /// <summary>The whole of the current week, starting on <paramref name="firstDayOfWeek"/>.</summary>
    public static DateRangePreset ThisWeek(DayOfWeek firstDayOfWeek, string label = "This week")
        => new(label, t =>
        {
            var start = CalendarMath.StartOfWeek(t, firstDayOfWeek);
            return new(start, start.AddDays(6));
        });

    /// <summary>The whole of the previous week, starting on <paramref name="firstDayOfWeek"/>.</summary>
    public static DateRangePreset LastWeek(DayOfWeek firstDayOfWeek, string label = "Last week")
        => new(label, t =>
        {
            var start = CalendarMath.StartOfWeek(t, firstDayOfWeek).AddDays(-7);
            return new(start, start.AddDays(6));
        });

    /// <summary>A sensible set for reporting screens - looking backwards from today.</summary>
    public static IReadOnlyList<DateRangePreset> Reporting(DayOfWeek firstDayOfWeek) =>
    [
        Today, Yesterday, Last7Days, Last30Days, ThisWeek(firstDayOfWeek), LastWeek(firstDayOfWeek), ThisMonth, LastMonth, YearToDate
    ];

    /// <summary>A sensible set for booking screens - looking forwards from today.</summary>
    public static IReadOnlyList<DateRangePreset> Booking(DayOfWeek firstDayOfWeek) =>
    [
        Today, ThisWeek(firstDayOfWeek, "Rest of this week").Clip(), Next7Days, Next30Days
    ];

    /// <summary>Trims a preset so it never starts before today.</summary>
    static DateRangePreset Clip(this DateRangePreset preset)
        => new(preset.Label, t =>
        {
            var r = preset.Resolve(t);
            return new(r.Start < t ? t : r.Start, r.End < t ? t : r.End);
        });
}
