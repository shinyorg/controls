using System.Globalization;

namespace Shiny.Controls.RangePickers;

/// <summary>
/// Writes ranges the way people do: "Mar 3 – 9, 2026" rather than "3/3/2026 – 3/9/2026", dropping
/// whatever the two ends share.
/// </summary>
/// <remarks>
/// The compact forms are built from the culture's own month-day and year-month patterns, so the order
/// and punctuation follow the culture (German gets "3.–9. März 2026"-style ordering, not an English
/// sentence with German month names). A custom <c>format</c> skips all of this and formats both ends
/// with it.
/// </remarks>
public static class RangeFormatter
{
    /// <summary>The separator between the two ends - an en dash with spaces.</summary>
    public const string DefaultSeparator = " – ";


    public static string Format(DateRange range, CultureInfo? culture = null, string? format = null, string separator = DefaultSeparator)
    {
        culture ??= CultureInfo.CurrentCulture;

        if (!string.IsNullOrEmpty(format))
        {
            return range.IsSingleDay
                ? range.Start.ToString(format, culture)
                : range.Start.ToString(format, culture) + separator + range.End.ToString(format, culture);
        }

        var full = FullDatePattern(culture);
        if (range.IsSingleDay)
            return range.Start.ToString(full, culture);

        var monthDay = MonthDayPattern(culture);
        if (range.Start.Year != range.End.Year)
            return range.Start.ToString(full, culture) + separator + range.End.ToString(full, culture);

        // Same month: the month once. Only attempted for simple patterns - a quoted literal ("d 'de'
        // MMM") would need the literal's grammar to drop correctly, and the plain form is still right.
        if (range.Start.Month == range.End.Month && !full.Contains('\'') && CountOf(full, "MMM") == 1)
        {
            var monthFirst = full.IndexOf("MMM", StringComparison.Ordinal) < full.IndexOf('d');
            if (monthFirst)
                return range.Start.ToString(monthDay, culture) + separator + range.End.ToString(Without(full, "MMM"), culture);

            return range.Start.ToString(Without(monthDay, "MMM"), culture) + separator + range.End.ToString(full, culture);
        }

        // Same year: only the end carries it.
        return range.Start.ToString(monthDay, culture) + separator + range.End.ToString(full, culture);
    }


    public static string Format(TimeRange range, CultureInfo? culture = null, string? format = null, string separator = DefaultSeparator)
    {
        culture ??= CultureInfo.CurrentCulture;
        format = string.IsNullOrEmpty(format) ? culture.DateTimeFormat.ShortTimePattern : format;
        return range.Start.ToString(format, culture) + separator + range.End.ToString(format, culture);
    }


    public static string Format(DateTimeRange range, CultureInfo? culture = null, string? format = null, string separator = DefaultSeparator)
    {
        culture ??= CultureInfo.CurrentCulture;

        if (!string.IsNullOrEmpty(format))
            return range.Start.ToString(format, culture) + separator + range.End.ToString(format, culture);

        var time = culture.DateTimeFormat.ShortTimePattern;
        var full = FullDatePattern(culture);

        // Same day: the date once, then the two times.
        if (range.Start.Date == range.End.Date)
            return $"{range.Start.ToString(full, culture)}, {range.Start.ToString(time, culture)}{separator}{range.End.ToString(time, culture)}";

        var startDate = range.Start.Year == range.End.Year ? MonthDayPattern(culture) : full;
        return $"{range.Start.ToString(startDate, culture)}, {range.Start.ToString(time, culture)}{separator}{range.End.ToString(full, culture)}, {range.End.ToString(time, culture)}";
    }


    /// <summary>"1 hr 30 min", "2 days 4 hr", "45 min".</summary>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return "0 min";

        var parts = new List<string>(3);
        if (duration.Days > 0)
            parts.Add(duration.Days == 1 ? "1 day" : $"{duration.Days} days");
        if (duration.Hours > 0)
            parts.Add($"{duration.Hours} hr");
        if (duration.Minutes > 0 && duration.Days == 0)
            parts.Add($"{duration.Minutes} min");

        return parts.Count == 0 ? "0 min" : string.Join(' ', parts);
    }


    /// <summary>The culture's month-day pattern with an abbreviated month: "MMM d" for en-US, "d. MMM" for de-DE.</summary>
    internal static string MonthDayPattern(CultureInfo culture)
        => Abbreviate(culture.DateTimeFormat.MonthDayPattern);


    /// <summary>The month-day pattern plus the year, in the culture's order: "MMM d, yyyy" for en-US.</summary>
    internal static string FullDatePattern(CultureInfo culture)
    {
        // The long date pattern carries the culture's ordering of day, month and year; strip the
        // weekday out of it and abbreviate the month.
        var pattern = culture.DateTimeFormat.LongDatePattern;
        pattern = pattern.Replace("dddd", "").Replace("ddd", "");
        pattern = Abbreviate(pattern).Trim(' ', ',', '.');
        while (pattern.Contains("  "))
            pattern = pattern.Replace("  ", " ");
        return pattern.TrimStart(',', ' ');
    }


    static int CountOf(string text, string token)
    {
        var count = 0;
        for (var i = text.IndexOf(token, StringComparison.Ordinal); i >= 0; i = text.IndexOf(token, i + token.Length, StringComparison.Ordinal))
            count++;
        return count;
    }


    static string Without(string pattern, string token)
    {
        var result = pattern.Replace(token, "").Trim();
        while (result.Contains("  "))
            result = result.Replace("  ", " ");
        return result;
    }


    static string Abbreviate(string pattern)
        => pattern.Contains("MMMM") ? pattern.Replace("MMMM", "MMM") : pattern;
}
