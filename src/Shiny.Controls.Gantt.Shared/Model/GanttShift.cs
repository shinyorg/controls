namespace Shiny.Controls.Gantt;

/// <summary>
/// A working period inside a day, as offsets from that day's midnight.
/// </summary>
/// <remarks>
/// <see cref="TimeSpan"/> rather than <see cref="TimeOnly"/> because a shift that runs to the end of
/// the day ends at 24:00, which <see cref="TimeOnly"/> cannot represent — it would have to be
/// written as 23:59:59.999 and every duration computed from it would be a millisecond short.
/// </remarks>
public readonly record struct GanttShift
{
    /// <summary>Midnight to midnight — a full 24-hour working day.</summary>
    public static readonly GanttShift FullDay = new(TimeSpan.Zero, TimeSpan.FromDays(1));

    /// <summary>09:00 to 17:00.</summary>
    public static readonly GanttShift NineToFive = new(TimeSpan.FromHours(9), TimeSpan.FromHours(17));

    /// <summary>Creates a shift from two offsets into the day.</summary>
    /// <param name="start">Offset from midnight at which the shift begins.</param>
    /// <param name="end">Offset from midnight at which the shift ends. May be exactly 24:00.</param>
    /// <exception cref="ArgumentOutOfRangeException">The shift starts or ends outside the day, or ends before it starts.</exception>
    public GanttShift(TimeSpan start, TimeSpan end)
    {
        if (start < TimeSpan.Zero || start > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(start), start, "A shift must start within the day.");

        if (end < start || end > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(end), end, "A shift must end at or after its start, and within the day.");

        this.Start = start;
        this.End = end;
    }

    /// <summary>Offset from midnight at which the shift begins.</summary>
    public TimeSpan Start { get; }

    /// <summary>Offset from midnight at which the shift ends. May be exactly 24:00.</summary>
    public TimeSpan End { get; }

    /// <summary>How long the shift lasts.</summary>
    public TimeSpan Duration => this.End - this.Start;

    /// <summary>Creates a shift from two hours-past-midnight, so 9 to 17.5 reads as it sounds.</summary>
    /// <param name="startHour">Hours past midnight at which the shift begins.</param>
    /// <param name="endHour">Hours past midnight at which the shift ends.</param>
    public static GanttShift FromHours(double startHour, double endHour) =>
        new(TimeSpan.FromHours(startHour), TimeSpan.FromHours(endHour));

    /// <summary>The shift as "hh:mm-hh:mm", for logs and debugger display.</summary>
    public override string ToString() => $"{this.Start:hh\\:mm}-{this.End:hh\\:mm}";
}
