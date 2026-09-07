namespace Shiny.Controls.Gantt.Tests;

/// <summary>
/// Terse plan construction for the tests. Dates are expressed as day offsets from a fixed Monday, so
/// a test reads as "A runs day 0 to 2" rather than as a wall of DateTimeOffset literals — and every
/// weekend lands in a predictable place for the working-calendar tests.
/// </summary>
static class Plan
{
    /// <summary>Monday, 5 January 2026, midnight UTC.</summary>
    public static readonly DateTimeOffset Monday = new(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

    public static DateTimeOffset Day(double offset) => Monday.AddDays(offset);

    public static GanttTask Task(
        string id,
        double start,
        double end,
        string? parent = null,
        GanttTaskKind kind = GanttTaskKind.Task,
        double progress = 0
    ) => new()
    {
        Id = id,
        Name = id,
        ParentId = parent,
        Start = Day(start),
        End = Day(end),
        Kind = kind,
        Progress = progress
    };

    public static GanttTask Milestone(string id, double at, string? parent = null) => new()
    {
        Id = id,
        Name = id,
        ParentId = parent,
        Start = Day(at),
        End = Day(at),
        Kind = GanttTaskKind.Milestone
    };

    public static GanttDependency Link(
        string from,
        string to,
        GanttDependencyType type = GanttDependencyType.FinishToStart,
        double lagDays = 0
    ) => new(from, to, type, TimeSpan.FromDays(lagDays));

    /// <summary>Day offset of an instant, so assertions read in the same units the plan was written in.</summary>
    public static double Offset(this DateTimeOffset value) => (value - Monday).TotalDays;
}
