using System.Globalization;

namespace Shiny.Controls.RangePickers;

/// <summary>Month grids and week arithmetic.</summary>
public static class CalendarMath
{
    /// <summary>
    /// The days a month view shows: whole weeks from the one containing the 1st, padded with the
    /// neighbouring months. Always six weeks (42 days) by default, so paging between months never
    /// makes the calendar change height.
    /// </summary>
    public static IReadOnlyList<DateOnly> MonthCells(int year, int month, DayOfWeek firstDayOfWeek, bool fixedSixWeeks = true)
    {
        var first = new DateOnly(year, month, 1);
        var start = StartOfWeek(first, firstDayOfWeek);
        var last = first.AddMonths(1).AddDays(-1);

        var weeks = fixedSixWeeks
            ? 6
            : (int)Math.Ceiling((last.DayNumber - start.DayNumber + 1) / 7.0);

        var cells = new DateOnly[weeks * 7];
        for (var i = 0; i < cells.Length; i++)
            cells[i] = start.AddDays(i);

        return cells;
    }


    /// <summary>The seven weekdays in display order.</summary>
    public static IReadOnlyList<DayOfWeek> WeekdayOrder(DayOfWeek firstDayOfWeek)
    {
        var days = new DayOfWeek[7];
        for (var i = 0; i < 7; i++)
            days[i] = (DayOfWeek)(((int)firstDayOfWeek + i) % 7);
        return days;
    }


    public static DateOnly StartOfWeek(DateOnly date, DayOfWeek firstDayOfWeek)
    {
        var diff = ((int)date.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        return date.AddDays(-diff);
    }


    public static DateOnly StartOfMonth(DateOnly date) => new(date.Year, date.Month, 1);


    /// <summary>The culture's first day of the week - Sunday in the US, Monday in most of Europe.</summary>
    public static DayOfWeek FirstDayOfWeek(CultureInfo? culture = null)
        => (culture ?? CultureInfo.CurrentCulture).DateTimeFormat.FirstDayOfWeek;


    /// <summary>"Mo", "Tu"... in the culture's language, for column headers.</summary>
    public static string ShortDayName(DayOfWeek day, CultureInfo? culture = null)
        => (culture ?? CultureInfo.CurrentCulture).DateTimeFormat.GetShortestDayName(day);


    /// <summary>"March 2026" in the culture's own year-month pattern.</summary>
    public static string MonthTitle(int year, int month, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return new DateTime(year, month, 1).ToString(culture.DateTimeFormat.YearMonthPattern, culture);
    }
}
