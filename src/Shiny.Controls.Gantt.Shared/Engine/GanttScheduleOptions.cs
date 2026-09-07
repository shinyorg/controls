namespace Shiny.Controls.Gantt;

/// <summary>How far an edit is allowed to ripple through the rest of the plan.</summary>
public enum GanttCascadeMode
{
    /// <summary>Nothing else moves. Dependencies still draw, and violations are still reported.</summary>
    None,

    /// <summary>
    /// Successors are pushed later when a link would otherwise be broken, but never pulled earlier.
    /// The default, and what most people mean by "auto-schedule": pulling a task earlier because its
    /// predecessor finished sooner is usually not what a planner wants to happen behind their back.
    /// </summary>
    PushOnly,

    /// <summary>
    /// Successors are moved to exactly where their links put them, earlier or later — the plan is
    /// re-derived from the dependency graph on every edit.
    /// </summary>
    Strict
}


/// <summary>Knobs for <see cref="GanttScheduler"/>.</summary>
public class GanttScheduleOptions
{
    /// <summary>Working-time rules. Defaults to <see cref="GanttCalendar.Continuous"/>.</summary>
    public GanttCalendar Calendar { get; set; } = GanttCalendar.Continuous;

    /// <summary>How far the edit ripples. See <see cref="GanttCascadeMode"/>.</summary>
    public GanttCascadeMode Cascade { get; set; } = GanttCascadeMode.PushOnly;

    /// <summary>Whether <see cref="GanttTask.Constraint"/> clamps the result.</summary>
    public bool EnforceConstraints { get; set; } = true;

    /// <summary>
    /// Whether edges are pulled onto a working boundary. With a continuous calendar this is free and
    /// does nothing.
    /// </summary>
    public bool SnapToWorkingTime { get; set; } = true;

    /// <summary>
    /// Whether a moved task keeps its <i>working</i> duration rather than its wall-clock one. A
    /// three-working-day task dragged across a weekend stays three working days and gets wider.
    /// </summary>
    public bool PreserveWorkingDuration { get; set; } = true;

    /// <summary>The shortest a task may be resized to. Zero lets a resize collapse a bar entirely.</summary>
    public TimeSpan MinimumDuration { get; set; } = TimeSpan.Zero;

    /// <summary>Nothing may be scheduled before this. Null for no limit.</summary>
    public DateTimeOffset? MinDate { get; set; }

    /// <summary>Nothing may be scheduled after this. Null for no limit.</summary>
    public DateTimeOffset? MaxDate { get; set; }

    /// <summary>
    /// Whether a summary's own bar can be resized. Off by default because a rolled-up summary's dates
    /// come from its children and the resize would be silently undone by the next rebuild.
    /// </summary>
    public bool AllowSummaryResize { get; set; }

    /// <summary>
    /// Whether dragging a summary moves everything beneath it. On by default — the alternative is a
    /// bar that snaps straight back, since a rollup has no dates of its own to keep.
    /// </summary>
    public bool MoveSubtreeWithSummary { get; set; } = true;

    /// <summary>
    /// Ceiling on how many tasks one edit may relocate. A guard rail, not a feature: it stops a
    /// pathological graph from turning a single drag into an unbounded walk.
    /// </summary>
    public int MaxCascadedTasks { get; set; } = 5_000;
}
