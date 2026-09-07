namespace Shiny.Controls.Gantt;

/// <summary>
/// How a successor's schedule is tied to its predecessor's. The names are the standard project
/// management ones and the semantics match them exactly, so a plan imported from MS Project or
/// Primavera keeps its meaning.
/// </summary>
public enum GanttDependencyType
{
    /// <summary>The successor may not start until the predecessor finishes. The overwhelming default.</summary>
    FinishToStart,

    /// <summary>The successor may not start until the predecessor starts.</summary>
    StartToStart,

    /// <summary>The successor may not finish until the predecessor finishes.</summary>
    FinishToFinish,

    /// <summary>The successor may not finish until the predecessor starts. Rare, but plans do use it.</summary>
    StartToFinish
}


/// <summary>What a row <i>is</i>, which decides how it draws and whether the engine schedules it.</summary>
public enum GanttTaskKind
{
    /// <summary>An ordinary bar with a duration.</summary>
    Task,

    /// <summary>A zero-duration marker, drawn as a diamond. <see cref="GanttTask.End"/> is ignored.</summary>
    Milestone,

    /// <summary>
    /// A parent whose dates are rolled up from its children rather than set directly. A task with
    /// children becomes one automatically; setting it explicitly on a childless task is allowed and
    /// simply draws the summary bracket.
    /// </summary>
    Summary,

    /// <summary>The single root of a plan. Draws like a summary but is styled as the project bar.</summary>
    Project
}


/// <summary>
/// A hard date restriction on a task, applied after dependencies and honoured by the auto-scheduler.
/// </summary>
public enum GanttConstraintType
{
    /// <summary>No restriction; dependencies alone decide the dates.</summary>
    None,

    /// <summary>Pinned: the task starts exactly on the constraint date.</summary>
    MustStartOn,

    /// <summary>Pinned: the task finishes exactly on the constraint date.</summary>
    MustFinishOn,

    /// <summary>The task may be pushed later but never earlier than the constraint date.</summary>
    StartNoEarlierThan,

    /// <summary>The task may be pulled earlier but never later than the constraint date.</summary>
    StartNoLaterThan,

    /// <summary>The finish may move later but never earlier than the constraint date.</summary>
    FinishNoEarlierThan,

    /// <summary>The finish may move earlier but never later than the constraint date.</summary>
    FinishNoLaterThan
}


/// <summary>The tick width of the timeline header, which also sets the default drag snap.</summary>
public enum GanttTimeScale
{
    /// <summary>Picks the finest scale whose ticks stay wider than the minimum tick width. </summary>
    Auto,

    /// <summary>One tick per minute.</summary>
    Minute,

    /// <summary>One tick per fifteen minutes.</summary>
    QuarterHour,

    /// <summary>One tick per hour.</summary>
    Hour,

    /// <summary>One tick per day.</summary>
    Day,

    /// <summary>One tick per week.</summary>
    Week,

    /// <summary>One tick per month.</summary>
    Month,

    /// <summary>One tick per quarter.</summary>
    Quarter,

    /// <summary>One tick per year.</summary>
    Year
}


/// <summary>What a drag rounds to.</summary>
public enum GanttSnapMode
{
    /// <summary>Free positioning, to the pixel.</summary>
    None,

    /// <summary>Round to the boundaries of the current lower header tier. The default.</summary>
    Scale,

    /// <summary>Round to the boundaries set by the host control's <c>SnapInterval</c>.</summary>
    Interval,

    /// <summary>
    /// Round to the nearest working-time boundary in the calendar — a bar dropped on a Saturday
    /// lands on Monday morning.
    /// </summary>
    WorkingTime
}


/// <summary>Which edit produced a <see cref="GanttSchedulePlan"/>.</summary>
public enum GanttChangeKind
{
    /// <summary>Both edges moved by the same delta.</summary>
    Move,

    /// <summary>The start edge moved; the finish held.</summary>
    ResizeStart,

    /// <summary>The finish edge moved; the start held.</summary>
    ResizeEnd,

    /// <summary>The progress handle moved.</summary>
    Progress,

    /// <summary>A dependency was drawn between two tasks.</summary>
    DependencyAdded,

    /// <summary>A dependency was deleted.</summary>
    DependencyRemoved,

    /// <summary>A row was dragged to a new position or a new parent in the task pane.</summary>
    Reorder,

    /// <summary>
    /// The task was not edited directly — the auto-scheduler moved it because something it depends
    /// on moved. Never raised on its own; it only ever appears in <see cref="GanttSchedulePlan.Cascade"/>.
    /// </summary>
    Cascade
}


/// <summary>Why the engine refused, or would refuse, a scheduling change.</summary>
public enum GanttValidationCode
{
    /// <summary>A dependency names a predecessor or successor that is not in the task list.</summary>
    UnknownTaskReference,

    /// <summary>The dependency graph contains a cycle; the tasks on it cannot be ordered.</summary>
    CircularDependency,

    /// <summary>A task is its own ancestor.</summary>
    CircularHierarchy,

    /// <summary>Two tasks share an <see cref="GanttTask.Id"/>.</summary>
    DuplicateId,

    /// <summary>The task finishes before it starts.</summary>
    NegativeDuration,

    /// <summary>The requested dates break a dependency that could not be resolved by cascading.</summary>
    DependencyViolated,

    /// <summary>The requested dates break the task's own <see cref="GanttTask.Constraint"/>.</summary>
    ConstraintViolated,

    /// <summary>The task would finish after its <see cref="GanttTask.Deadline"/>.</summary>
    DeadlineExceeded,

    /// <summary>The requested dates fall outside <see cref="GanttScheduleOptions.MinDate"/>/<see cref="GanttScheduleOptions.MaxDate"/>.</summary>
    OutOfRange
}
