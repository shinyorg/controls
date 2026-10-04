namespace Shiny.Controls.RangePickers;

/// <summary>A selectable time of day, with the length of the range it would produce when it is an end time.</summary>
public readonly record struct TimeSlot(TimeOnly Time, TimeSpan? Duration = null);


/// <summary>The rules for picking a time range, and the start and end times they allow.</summary>
public sealed class TimeRangeConstraints
{
    /// <summary>The step between selectable times. 15 minutes by default.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>The earliest selectable time.</summary>
    public TimeOnly? MinTime { get; set; }

    /// <summary>The latest selectable time.</summary>
    public TimeOnly? MaxTime { get; set; }

    /// <summary>The shortest range. Defaults to one <see cref="Interval"/> - an empty range is never allowed.</summary>
    public TimeSpan? MinDuration { get; set; }

    /// <summary>The longest range.</summary>
    public TimeSpan? MaxDuration { get; set; }

    /// <summary>
    /// Whether the end may be earlier than the start, meaning the next day (a 22:00 – 06:00 shift).
    /// Off by default, and ignored when <see cref="MinTime"/> or <see cref="MaxTime"/> is set.
    /// </summary>
    public bool AllowOvernight { get; set; }

    TimeSpan Step => this.Interval <= TimeSpan.Zero ? TimeSpan.FromMinutes(15) : this.Interval;

    TimeSpan ShortestRange => this.MinDuration is { } min && min > TimeSpan.Zero ? min : this.Step;

    bool Overnight => this.AllowOvernight && this.MinTime is null && this.MaxTime is null;


    /// <summary>Every time on the interval grid between <see cref="MinTime"/> and <see cref="MaxTime"/>.</summary>
    public IReadOnlyList<TimeOnly> AllSlots()
    {
        var slots = new List<TimeOnly>();
        var min = (this.MinTime ?? TimeOnly.MinValue).ToTimeSpan();
        var max = this.MaxTime?.ToTimeSpan() ?? TimeSpan.FromDays(1) - TimeSpan.FromTicks(1);

        for (var t = TimeSpan.Zero; t < TimeSpan.FromDays(1); t += this.Step)
        {
            if (t >= min && t <= max)
                slots.Add(TimeOnly.FromTimeSpan(t));
        }
        return slots;
    }


    /// <summary>Times a range can start at - every slot that leaves room for the shortest range after it.</summary>
    public IReadOnlyList<TimeOnly> StartSlots()
        => [.. this.AllSlots().Where(s => this.EndSlotsFor(s).Count > 0)];


    /// <summary>The end times allowed after <paramref name="start"/>, each with the length it makes.</summary>
    public IReadOnlyList<TimeSlot> EndSlotsFor(TimeOnly start)
    {
        var result = new List<TimeSlot>();
        var shortest = this.ShortestRange;
        var longest = this.MaxDuration ?? TimeSpan.FromDays(1);

        if (this.Overnight)
        {
            // Walk forward from the start, wrapping past midnight, up to (not including) a full day.
            for (var d = this.Step; d < TimeSpan.FromDays(1); d += this.Step)
            {
                if (d < shortest || d > longest)
                    continue;
                result.Add(new TimeSlot(start.Add(d), d));
            }
            return result;
        }

        foreach (var slot in this.AllSlots())
        {
            if (slot <= start)
                continue;

            var d = slot - start;
            if (d >= shortest && d <= longest)
                result.Add(new TimeSlot(slot, d));
        }

        // Midnight as a closing time ("until the end of the day"). It is written as 00:00, which a
        // TimeRange reads as running into the next day - true, and it gives the right duration.
        if (this.MaxTime is null)
        {
            var toMidnight = TimeSpan.FromDays(1) - start.ToTimeSpan();
            if (toMidnight >= shortest && toMidnight <= longest && toMidnight.Ticks % this.Step.Ticks == 0)
                result.Add(new TimeSlot(TimeOnly.MinValue, toMidnight));
        }
        return result;
    }


    /// <summary>Whether <paramref name="range"/> satisfies every rule.</summary>
    public bool IsValid(TimeRange range)
    {
        // Ending at 00:00 is "until midnight", which every picker allows unless MaxTime says otherwise.
        var endsAtMidnight = range.End == TimeOnly.MinValue && range.Start != TimeOnly.MinValue && this.MaxTime is null;
        if (range.CrossesMidnight && !this.Overnight && !endsAtMidnight)
            return false;

        if (this.MinTime is { } min && (range.Start < min || (range.End < min && !endsAtMidnight)))
            return false;

        if (this.MaxTime is { } max && (range.Start > max || range.End > max))
            return false;

        var duration = range.Duration;

        if (duration < this.ShortestRange)
            return false;

        return this.MaxDuration is not { } longest || duration <= longest;
    }


    /// <summary>
    /// Where the end should go when the start moves: keep the length the range had, as calendar apps
    /// do, falling back to the nearest allowed end when that no longer fits.
    /// </summary>
    public TimeOnly? EndFor(TimeOnly start, TimeSpan? keepDuration)
    {
        var ends = this.EndSlotsFor(start);
        if (ends.Count == 0)
            return null;

        if (keepDuration is { } keep)
        {
            foreach (var end in ends)
            {
                if (end.Duration == keep)
                    return end.Time;
            }

            // Too long for the new start: the longest that fits.
            if (keep > ends[^1].Duration)
                return ends[^1].Time;
        }

        // The default length: an hour if allowed, else the shortest.
        foreach (var end in ends)
        {
            if (end.Duration == TimeSpan.FromHours(1))
                return end.Time;
        }
        return ends[0].Time;
    }


    /// <summary>Snaps a time down onto the interval grid.</summary>
    public TimeOnly Snap(TimeOnly time)
    {
        var ticks = time.Ticks - (time.Ticks % this.Step.Ticks);
        return new TimeOnly(ticks);
    }
}
