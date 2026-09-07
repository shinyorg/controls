using System.Globalization;

namespace Shiny.Controls.Gantt;

/// <summary>
/// The dates-to-pixels arithmetic both hosts draw with, plus the header tick generation that has to
/// agree with it exactly. Deliberately free of any UI type: a bar's position is a number, and the
/// only way to keep a MAUI layout pass and a CSS grid drawing the same plan is for neither of them
/// to own the maths.
/// </summary>
public static class GanttGeometry
{
    /// <summary>Below this the header text stops fitting, so <see cref="GanttTimeScale.Auto"/> steps up a scale.</summary>
    public const double DefaultMinTickWidth = 28d;

    /// <summary>Guard rail on zoom, in both directions. A year is roughly 0.3px/day at the bottom.</summary>
    public const double MinPixelsPerDay = 0.1d;

    /// <summary>At the top, a minute is about a pixel and a half.</summary>
    public const double MaxPixelsPerDay = 200_000d;


    // ---------------------------------------------------------------------------------------------
    // Position
    // ---------------------------------------------------------------------------------------------

    /// <summary>Horizontal offset of an instant from the timeline origin.</summary>
    public static double DateToX(DateTimeOffset date, DateTimeOffset origin, double pixelsPerDay) =>
        (date - origin).TotalDays * pixelsPerDay;

    /// <summary>The instant at a horizontal offset. The inverse of <see cref="DateToX"/>.</summary>
    public static DateTimeOffset XToDate(double x, DateTimeOffset origin, double pixelsPerDay) =>
        pixelsPerDay <= 0d ? origin : origin.AddDays(x / pixelsPerDay);

    /// <summary>Width of a span. Never negative — a reversed pair yields zero rather than a bar drawn backwards.</summary>
    public static double SpanToWidth(DateTimeOffset start, DateTimeOffset end, double pixelsPerDay) =>
        Math.Max(0d, (end - start).TotalDays * pixelsPerDay);

    /// <summary>A pixel delta as a time delta, for turning a drag into a schedule change.</summary>
    public static TimeSpan XToSpan(double dx, double pixelsPerDay) =>
        pixelsPerDay <= 0d ? TimeSpan.Zero : TimeSpan.FromDays(dx / pixelsPerDay);


    // ---------------------------------------------------------------------------------------------
    // Zoom
    // ---------------------------------------------------------------------------------------------

    /// <summary>The pixels-per-day that gives a scale's ticks the requested width.</summary>
    public static double PixelsPerDayFor(GanttTimeScale scale, double tickWidth) =>
        Math.Clamp(tickWidth / Math.Max(0.000001d, ApproximateTickDays(scale)), MinPixelsPerDay, MaxPixelsPerDay);

    /// <summary>
    /// The finest scale whose ticks are still at least <paramref name="minTickWidth"/> wide, which is
    /// what <see cref="GanttTimeScale.Auto"/> resolves to.
    /// </summary>
    public static GanttTimeScale ResolveScale(double pixelsPerDay, double minTickWidth = DefaultMinTickWidth)
    {
        // Ordered finest first: the first one wide enough wins, and Year is the floor.
        foreach (var scale in Scales)
        {
            if (ApproximateTickDays(scale) * pixelsPerDay >= minTickWidth)
                return scale;
        }
        return GanttTimeScale.Year;
    }

    /// <summary>The pixels-per-day that fits a date range into an available width.</summary>
    public static double FitToWidth(DateTimeOffset from, DateTimeOffset to, double availableWidth, double padding = 0d)
    {
        var days = (to - from).TotalDays;
        var usable = availableWidth - padding * 2;

        if (days <= 0d || usable <= 0d)
            return PixelsPerDayFor(GanttTimeScale.Day, DefaultMinTickWidth);

        return Math.Clamp(usable / days, MinPixelsPerDay, MaxPixelsPerDay);
    }

    /// <summary>
    /// Zooms about a fixed point, returning the new origin that keeps the instant under
    /// <paramref name="anchorX"/> under it afterwards. Without this a pinch or wheel zoom drifts the
    /// content out from under the pointer.
    /// </summary>
    public static DateTimeOffset ZoomAnchored(
        DateTimeOffset origin,
        double anchorX,
        double oldPixelsPerDay,
        double newPixelsPerDay
    )
    {
        if (oldPixelsPerDay <= 0d || newPixelsPerDay <= 0d)
            return origin;

        var anchorDate = XToDate(anchorX, origin, oldPixelsPerDay);
        return anchorDate.AddDays(-anchorX / newPixelsPerDay);
    }


    // ---------------------------------------------------------------------------------------------
    // Snapping
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Rounds a dragged instant to the mode's boundary. <see cref="GanttSnapMode.WorkingTime"/> pushes
    /// forward out of a gap rather than rounding to the nearest edge, because a bar dropped on a
    /// Saturday means "the following Monday", not "back to Friday".
    /// </summary>
    public static DateTimeOffset Snap(
        DateTimeOffset value,
        GanttSnapMode mode,
        GanttTimeScale scale,
        TimeSpan interval,
        GanttCalendar? calendar = null
    ) => mode switch
    {
        GanttSnapMode.None => value,
        GanttSnapMode.Interval => SnapToInterval(value, interval),
        GanttSnapMode.WorkingTime => (calendar ?? GanttCalendar.Continuous).NextWorkingTime(SnapToScale(value, scale)),
        _ => SnapToScale(value, scale)
    };

    /// <summary>Rounds to the nearest whole multiple of <paramref name="interval"/> since the epoch.</summary>
    public static DateTimeOffset SnapToInterval(DateTimeOffset value, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
            return value;

        var ticks = interval.Ticks;
        var rounded = (long)Math.Round((double)value.UtcTicks / ticks, MidpointRounding.AwayFromZero) * ticks;
        return new DateTimeOffset(rounded, TimeSpan.Zero).ToOffset(value.Offset);
    }

    /// <summary>Rounds to the nearest boundary of the scale's own unit.</summary>
    public static DateTimeOffset SnapToScale(DateTimeOffset value, GanttTimeScale scale)
    {
        var floor = FloorTo(value, scale);
        var ceiling = NextTick(floor, scale);
        return (value - floor) < (ceiling - value) ? floor : ceiling;
    }

    /// <summary>The start of the scale unit containing <paramref name="value"/>.</summary>
    public static DateTimeOffset FloorTo(DateTimeOffset value, GanttTimeScale scale, CultureInfo? culture = null)
    {
        var offset = value.Offset;
        var d = value.DateTime;

        switch (scale)
        {
            case GanttTimeScale.Minute:
                return new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0, offset);

            case GanttTimeScale.QuarterHour:
                return new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, d.Minute / 15 * 15, 0, offset);

            case GanttTimeScale.Hour:
                return new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, 0, 0, offset);

            case GanttTimeScale.Week:
            {
                var first = (culture ?? CultureInfo.CurrentCulture).DateTimeFormat.FirstDayOfWeek;
                var day = new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, offset);
                var back = ((int)day.DayOfWeek - (int)first + 7) % 7;
                return day.AddDays(-back);
            }

            case GanttTimeScale.Month:
                return new DateTimeOffset(d.Year, d.Month, 1, 0, 0, 0, offset);

            case GanttTimeScale.Quarter:
                return new DateTimeOffset(d.Year, (d.Month - 1) / 3 * 3 + 1, 1, 0, 0, 0, offset);

            case GanttTimeScale.Year:
                return new DateTimeOffset(d.Year, 1, 1, 0, 0, 0, offset);

            default:
                return new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, offset);
        }
    }

    /// <summary>
    /// The start of the unit after <paramref name="tickStart"/>. Calendar units are stepped with
    /// <see cref="DateTimeOffset.AddMonths"/> rather than by adding a nominal number of days, so a
    /// 28-day February and a 31-day March each get exactly one column.
    /// </summary>
    public static DateTimeOffset NextTick(DateTimeOffset tickStart, GanttTimeScale scale) => scale switch
    {
        GanttTimeScale.Minute => tickStart.AddMinutes(1),
        GanttTimeScale.QuarterHour => tickStart.AddMinutes(15),
        GanttTimeScale.Hour => tickStart.AddHours(1),
        GanttTimeScale.Week => tickStart.AddDays(7),
        GanttTimeScale.Month => tickStart.AddMonths(1),
        GanttTimeScale.Quarter => tickStart.AddMonths(3),
        GanttTimeScale.Year => tickStart.AddYears(1),
        _ => tickStart.AddDays(1)
    };


    // ---------------------------------------------------------------------------------------------
    // Header tiers
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The coarser scale drawn above a given one. A single-tier header reads as an undifferentiated
    /// run of numbers, so the control always draws two and this decides the top one.
    /// </summary>
    public static GanttTimeScale UpperTierFor(GanttTimeScale scale) => scale switch
    {
        GanttTimeScale.Minute or GanttTimeScale.QuarterHour => GanttTimeScale.Hour,
        GanttTimeScale.Hour => GanttTimeScale.Day,
        GanttTimeScale.Day => GanttTimeScale.Month,
        GanttTimeScale.Week => GanttTimeScale.Month,
        GanttTimeScale.Month => GanttTimeScale.Year,
        GanttTimeScale.Quarter => GanttTimeScale.Year,
        _ => GanttTimeScale.Year
    };

    /// <summary>
    /// Builds one tier of header cells covering <paramref name="from"/> to <paramref name="to"/>,
    /// clipped to that range so a month column at the edge is drawn at its true visible width.
    /// </summary>
    public static IReadOnlyList<GanttTick> GenerateTicks(
        DateTimeOffset from,
        DateTimeOffset to,
        GanttTimeScale scale,
        DateTimeOffset origin,
        double pixelsPerDay,
        CultureInfo? culture = null,
        GanttCalendar? calendar = null,
        DateTimeOffset? now = null
    )
    {
        var ticks = new List<GanttTick>();
        if (to <= from || pixelsPerDay <= 0d || scale == GanttTimeScale.Auto)
            return ticks;

        culture ??= CultureInfo.CurrentCulture;
        var today = now ?? DateTimeOffset.Now;

        var cursor = FloorTo(from, scale, culture);

        // Bounded for the same reason the calendar walks are: a caller who zooms all the way out and
        // asks for minute ticks across a decade must get a clamped list, not a frozen UI.
        for (var i = 0; cursor < to && i < 5_000; i++)
        {
            var next = NextTick(cursor, scale);
            if (next <= cursor)
                break;

            var visibleStart = cursor > from ? cursor : from;
            var visibleEnd = next < to ? next : to;

            ticks.Add(new GanttTick(
                cursor,
                next,
                FormatTick(cursor, scale, culture),
                DateToX(visibleStart, origin, pixelsPerDay),
                SpanToWidth(visibleStart, visibleEnd, pixelsPerDay),
                IsNonWorkingTick(cursor, next, scale, calendar),
                today >= cursor && today < next
            ));

            cursor = next;
        }
        return ticks;
    }

    /// <summary>The label for a tick, in the requested culture.</summary>
    public static string FormatTick(DateTimeOffset tick, GanttTimeScale scale, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;

        return scale switch
        {
            GanttTimeScale.Minute or GanttTimeScale.QuarterHour => tick.ToString("HH:mm", culture),
            GanttTimeScale.Hour => tick.ToString("HH:mm", culture),
            GanttTimeScale.Day => tick.Day.ToString(culture),
            GanttTimeScale.Week => "W" + WeekOfYear(tick, culture).ToString(culture),
            GanttTimeScale.Month => tick.ToString("MMM yyyy", culture),
            GanttTimeScale.Quarter => "Q" + ((tick.Month - 1) / 3 + 1).ToString(culture) + " " + tick.Year.ToString(culture),
            GanttTimeScale.Year => tick.Year.ToString(culture),
            _ => tick.ToString("d", culture)
        };
    }

    /// <summary>ISO-style week number resolved through the culture's own week rule.</summary>
    public static int WeekOfYear(DateTimeOffset value, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return culture.Calendar.GetWeekOfYear(
            value.DateTime,
            culture.DateTimeFormat.CalendarWeekRule,
            culture.DateTimeFormat.FirstDayOfWeek
        );
    }


    // ---------------------------------------------------------------------------------------------
    // Internals
    // ---------------------------------------------------------------------------------------------

    // Finest to coarsest. ResolveScale walks this in order.
    static readonly GanttTimeScale[] Scales =
    [
        GanttTimeScale.Minute,
        GanttTimeScale.QuarterHour,
        GanttTimeScale.Hour,
        GanttTimeScale.Day,
        GanttTimeScale.Week,
        GanttTimeScale.Month,
        GanttTimeScale.Quarter,
        GanttTimeScale.Year
    ];

    /// <summary>
    /// A nominal tick width in days, used only for zoom decisions. Months and years vary in length
    /// and that is fine here — nothing is positioned from these, they only pick a scale.
    /// </summary>
    internal static double ApproximateTickDays(GanttTimeScale scale) => scale switch
    {
        GanttTimeScale.Minute => 1d / 1440d,
        GanttTimeScale.QuarterHour => 1d / 96d,
        GanttTimeScale.Hour => 1d / 24d,
        GanttTimeScale.Day => 1d,
        GanttTimeScale.Week => 7d,
        GanttTimeScale.Month => 30.4375d,
        GanttTimeScale.Quarter => 91.3125d,
        GanttTimeScale.Year => 365.25d,
        _ => 1d
    };

    static bool IsNonWorkingTick(DateTimeOffset start, DateTimeOffset end, GanttTimeScale scale, GanttCalendar? calendar)
    {
        if (calendar is null || calendar.IsContinuous)
            return false;

        // Only the day-and-finer scales get shaded. A week column containing one holiday is not a
        // non-working week, and shading it would misrepresent the plan far more loudly than leaving
        // it plain does.
        if (scale is GanttTimeScale.Week or GanttTimeScale.Month or GanttTimeScale.Quarter or GanttTimeScale.Year)
            return false;

        if (scale == GanttTimeScale.Day)
            return !calendar.IsWorkingDay(DateOnly.FromDateTime(start.DateTime));

        return calendar.WorkingTimeBetween(start, end) == TimeSpan.Zero;
    }
}
