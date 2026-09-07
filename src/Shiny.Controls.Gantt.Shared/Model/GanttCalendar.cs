namespace Shiny.Controls.Gantt;

/// <summary>
/// The working-time rules a plan is scheduled against: which days of the week are worked, which
/// hours within them, which dates are holidays, and any per-date overrides.
/// </summary>
/// <remarks>
/// <para>
/// Everything here operates on the wall clock carried by the <see cref="DateTimeOffset"/> it is
/// given, and every result is returned with that same offset. The calendar deliberately does not own
/// a <see cref="TimeZoneInfo"/>: a plan's working day is "nine in the morning wherever this project
/// happens", and re-projecting it through a zone on every arithmetic step is both slower and a
/// source of off-by-an-hour bugs twice a year. Callers that need a zone convert at the boundary.
/// </para>
/// <para>
/// <see cref="Continuous"/> short-circuits every method to plain arithmetic. It is the default, so a
/// consumer who never mentions working time pays nothing for the machinery.
/// </para>
/// </remarks>
public class GanttCalendar
{
    // Every walk over days is bounded. A calendar with no working days at all - Holidays covering
    // the range, or an empty WorkingDays set - would otherwise spin forever inside a layout pass,
    // and a control that hangs is worse than one that clamps and reports it.
    internal const int MaxDaysWalked = 20_000;

    readonly HashSet<DayOfWeek> workingDays;
    readonly List<GanttShift> shifts;
    readonly HashSet<DateOnly> holidays;
    readonly Dictionary<DateOnly, IReadOnlyList<GanttShift>> exceptions;

    /// <summary>24/7. No day is a holiday and every instant is working time.</summary>
    public static GanttCalendar Continuous { get; } = new(
        Enum.GetValues<DayOfWeek>(),
        [GanttShift.FullDay]
    );

    /// <summary>Monday to Friday, full days. The usual choice for a plan measured in days.</summary>
    public static GanttCalendar StandardDays { get; } = new(
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
        [GanttShift.FullDay]
    );

    /// <summary>Monday to Friday, 09:00-17:00. The usual choice for a plan measured in hours.</summary>
    public static GanttCalendar StandardHours { get; } = new(
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
        [GanttShift.NineToFive]
    );

    /// <summary>Builds a working-time calendar.</summary>
    /// <param name="workingDays">Which days of the week are worked. Null means every day.</param>
    /// <param name="shifts">The working periods inside each of those days. Null means the full day.</param>
    /// <param name="holidays">Dates that are never worked, whatever the day of the week.</param>
    /// <param name="exceptions">Per-date shift overrides, for a half day or a one-off weekend.</param>
    public GanttCalendar(
        IEnumerable<DayOfWeek>? workingDays = null,
        IEnumerable<GanttShift>? shifts = null,
        IEnumerable<DateOnly>? holidays = null,
        IEnumerable<KeyValuePair<DateOnly, IReadOnlyList<GanttShift>>>? exceptions = null
    )
    {
        this.workingDays = workingDays is null
            ? [.. Enum.GetValues<DayOfWeek>()]
            : [.. workingDays];

        this.shifts = Normalize(shifts ?? [GanttShift.FullDay]);
        this.holidays = holidays is null ? [] : [.. holidays];
        this.exceptions = exceptions is null
            ? []
            : exceptions.ToDictionary(x => x.Key, x => Normalize(x.Value) as IReadOnlyList<GanttShift>);

        this.IsContinuous =
            this.workingDays.Count == 7 &&
            this.shifts.Count == 1 &&
            this.shifts[0] == GanttShift.FullDay &&
            this.holidays.Count == 0 &&
            this.exceptions.Count == 0;
    }

    /// <summary>Days of the week that are worked at all.</summary>
    public IReadOnlySet<DayOfWeek> WorkingDays => this.workingDays;

    /// <summary>The shifts worked on an ordinary working day, sorted and merged.</summary>
    public IReadOnlyList<GanttShift> Shifts => this.shifts;

    /// <summary>Dates that are never worked, whatever their day of week.</summary>
    public IReadOnlySet<DateOnly> Holidays => this.holidays;

    /// <summary>
    /// Per-date shift overrides. An entry with an empty list makes that date non-working; an entry
    /// with shifts makes it working even if its day of week or the holiday list says otherwise,
    /// which is how a worked Saturday is expressed.
    /// </summary>
    public IReadOnlyDictionary<DateOnly, IReadOnlyList<GanttShift>> Exceptions => this.exceptions;

    /// <summary>True when this is a 24/7 calendar and working time equals wall-clock time.</summary>
    public bool IsContinuous { get; }

    /// <summary>
    /// Working time in an ordinary working day, from the shifts. Used to turn a duration expressed
    /// in days into one expressed in hours.
    /// </summary>
    public TimeSpan WorkingTimePerDay
    {
        get
        {
            var total = TimeSpan.Zero;
            foreach (var shift in this.shifts)
                total += shift.Duration;
            return total;
        }
    }


    /// <summary>The shifts worked on a given date, empty when the date is not worked at all.</summary>
    public IReadOnlyList<GanttShift> ShiftsOn(DateOnly date)
    {
        if (this.exceptions.TryGetValue(date, out var over))
            return over;

        if (this.holidays.Contains(date) || !this.workingDays.Contains(date.DayOfWeek))
            return [];

        return this.shifts;
    }


    /// <summary>Whether any work happens on the date.</summary>
    public bool IsWorkingDay(DateOnly date) => this.ShiftsOn(date).Count > 0;


    /// <summary>Whether the instant falls inside a shift.</summary>
    public bool IsWorkingTime(DateTimeOffset value)
    {
        if (this.IsContinuous)
            return true;

        var date = DateOnly.FromDateTime(value.DateTime);
        var time = value.DateTime.TimeOfDay;

        foreach (var shift in this.ShiftsOn(date))
        {
            // Half-open: the instant a shift ends already belongs to the gap after it, so
            // 17:00 on a 9-5 day is not working time and NextWorkingTime moves it to tomorrow.
            if (time >= shift.Start && time < shift.End)
                return true;
        }
        return false;
    }


    /// <summary>
    /// The first working instant at or after <paramref name="value"/>. Returns the input unchanged
    /// when it is already working time.
    /// </summary>
    public DateTimeOffset NextWorkingTime(DateTimeOffset value)
    {
        if (this.IsContinuous)
            return value;

        var offset = value.Offset;
        var date = DateOnly.FromDateTime(value.DateTime);
        var time = value.DateTime.TimeOfDay;

        for (var i = 0; i < MaxDaysWalked; i++)
        {
            foreach (var shift in this.ShiftsOn(date))
            {
                if (time < shift.Start)
                    return At(date, shift.Start, offset);

                if (time < shift.End)
                    return At(date, time, offset);
            }
            date = date.AddDays(1);
            time = TimeSpan.Zero;
        }
        return value;
    }


    /// <summary>
    /// The last working instant at or before <paramref name="value"/>, treating the value as the
    /// exclusive end of a period. A task finishing at 17:00 on a 9-5 Friday stays at 17:00 rather
    /// than being dragged back into Friday afternoon.
    /// </summary>
    public DateTimeOffset PreviousWorkingTime(DateTimeOffset value)
    {
        if (this.IsContinuous)
            return value;

        var offset = value.Offset;
        var date = DateOnly.FromDateTime(value.DateTime);
        var time = value.DateTime.TimeOfDay;

        for (var i = 0; i < MaxDaysWalked; i++)
        {
            var shiftsOn = this.ShiftsOn(date);
            for (var s = shiftsOn.Count - 1; s >= 0; s--)
            {
                var shift = shiftsOn[s];
                if (time > shift.End)
                    return At(date, shift.End, offset);

                if (time > shift.Start)
                    return At(date, time, offset);
            }
            date = date.AddDays(-1);
            time = TimeSpan.FromDays(1);
        }
        return value;
    }


    /// <summary>
    /// How much working time lies between two instants. Negative when <paramref name="end"/> precedes
    /// <paramref name="start"/>, so it composes with <see cref="AddWorkingTime"/> in both directions.
    /// </summary>
    public TimeSpan WorkingTimeBetween(DateTimeOffset start, DateTimeOffset end)
    {
        if (this.IsContinuous)
            return end - start;

        if (end < start)
            return -this.WorkingTimeBetween(end, start);

        var from = start.DateTime;
        var to = end.DateTime;
        var date = DateOnly.FromDateTime(from);
        var last = DateOnly.FromDateTime(to);
        var total = TimeSpan.Zero;

        for (var i = 0; date <= last && i < MaxDaysWalked; i++)
        {
            var dayStart = date.ToDateTime(TimeOnly.MinValue);

            foreach (var shift in this.ShiftsOn(date))
            {
                var shiftStart = dayStart + shift.Start;
                var shiftEnd = dayStart + shift.End;

                var lo = shiftStart > from ? shiftStart : from;
                var hi = shiftEnd < to ? shiftEnd : to;

                if (hi > lo)
                    total += hi - lo;
            }
            date = date.AddDays(1);
        }
        return total;
    }


    /// <summary>
    /// Advances <paramref name="from"/> by <paramref name="working"/> of working time. Negative spans
    /// walk backwards. A zero span still normalizes onto a working boundary, which is what makes a
    /// milestone dropped on a Sunday land on Monday.
    /// </summary>
    public DateTimeOffset AddWorkingTime(DateTimeOffset from, TimeSpan working)
    {
        if (this.IsContinuous)
            return from + working;

        if (working < TimeSpan.Zero)
            return this.SubtractWorkingTime(from, -working);

        var cursor = this.NextWorkingTime(from);
        if (working == TimeSpan.Zero)
            return cursor;

        var offset = cursor.Offset;
        var date = DateOnly.FromDateTime(cursor.DateTime);
        var time = cursor.DateTime.TimeOfDay;
        var remaining = working;

        for (var i = 0; i < MaxDaysWalked; i++)
        {
            foreach (var shift in this.ShiftsOn(date))
            {
                if (time >= shift.End)
                    continue;

                var segmentStart = time > shift.Start ? time : shift.Start;
                var available = shift.End - segmentStart;

                if (available >= remaining)
                    return At(date, segmentStart + remaining, offset);

                remaining -= available;
            }
            date = date.AddDays(1);
            time = TimeSpan.Zero;
        }
        return cursor;
    }


    /// <summary>Walks backwards by <paramref name="working"/> of working time.</summary>
    public DateTimeOffset SubtractWorkingTime(DateTimeOffset from, TimeSpan working)
    {
        if (this.IsContinuous)
            return from - working;

        if (working < TimeSpan.Zero)
            return this.AddWorkingTime(from, -working);

        var cursor = this.PreviousWorkingTime(from);
        if (working == TimeSpan.Zero)
            return cursor;

        var offset = cursor.Offset;
        var date = DateOnly.FromDateTime(cursor.DateTime);
        var time = cursor.DateTime.TimeOfDay;
        var remaining = working;

        for (var i = 0; i < MaxDaysWalked; i++)
        {
            var shiftsOn = this.ShiftsOn(date);
            for (var s = shiftsOn.Count - 1; s >= 0; s--)
            {
                var shift = shiftsOn[s];
                if (time <= shift.Start)
                    continue;

                var segmentEnd = time < shift.End ? time : shift.End;
                var available = segmentEnd - shift.Start;

                if (available >= remaining)
                    return At(date, segmentEnd - remaining, offset);

                remaining -= available;
            }
            date = date.AddDays(-1);
            time = TimeSpan.FromDays(1);
        }
        return cursor;
    }


    /// <summary>
    /// The non-working spans that overlap a range, for the timeline's weekend/holiday shading.
    /// Merged and clipped to the range; empty for a continuous calendar.
    /// </summary>
    public IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> NonWorkingIntervals(
        DateTimeOffset start,
        DateTimeOffset end
    )
    {
        var result = new List<(DateTimeOffset, DateTimeOffset)>();
        if (this.IsContinuous || end <= start)
            return result;

        var offset = start.Offset;
        var date = DateOnly.FromDateTime(start.DateTime);
        var last = DateOnly.FromDateTime(end.DateTime);
        var cursor = start;

        for (var i = 0; date <= last && i < MaxDaysWalked; i++)
        {
            var dayStart = date.ToDateTime(TimeOnly.MinValue);

            foreach (var shift in this.ShiftsOn(date))
            {
                var shiftStart = new DateTimeOffset(dayStart + shift.Start, offset);
                if (shiftStart > cursor)
                    Add(cursor, shiftStart);

                var shiftEnd = new DateTimeOffset(dayStart + shift.End, offset);
                if (shiftEnd > cursor)
                    cursor = shiftEnd;
            }
            date = date.AddDays(1);
        }

        if (cursor < end)
            Add(cursor, end);

        return result;

        void Add(DateTimeOffset from, DateTimeOffset to)
        {
            var lo = from > start ? from : start;
            var hi = to < end ? to : end;
            if (hi <= lo)
                return;

            // Contiguous gaps - Friday evening, all of Saturday and Sunday, Monday morning - arrive
            // as separate pieces because they are found a day at a time. Merging here is what lets
            // the host draw one weekend band instead of three abutting rectangles whose seams show
            // through a translucent fill.
            if (result.Count > 0 && result[^1].Item2 >= lo)
                result[^1] = (result[^1].Item1, hi > result[^1].Item2 ? hi : result[^1].Item2);
            else
                result.Add((lo, hi));
        }
    }


    static DateTimeOffset At(DateOnly date, TimeSpan time, TimeSpan offset) =>
        new(date.ToDateTime(TimeOnly.MinValue) + time, offset);


    /// <summary>Sorts shifts and merges any that touch or overlap, so the walks above can assume order.</summary>
    static List<GanttShift> Normalize(IEnumerable<GanttShift> source)
    {
        var sorted = source.Where(x => x.Duration > TimeSpan.Zero).OrderBy(x => x.Start).ToList();
        var merged = new List<GanttShift>(sorted.Count);

        foreach (var shift in sorted)
        {
            if (merged.Count > 0 && shift.Start <= merged[^1].End)
            {
                var previous = merged[^1];
                if (shift.End > previous.End)
                    merged[^1] = new GanttShift(previous.Start, shift.End);
            }
            else
            {
                merged.Add(shift);
            }
        }
        return merged;
    }
}
