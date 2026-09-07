namespace Shiny.Controls.Gantt;

/// <summary>Knobs for <see cref="GanttModel.Build"/>.</summary>
public class GanttModelOptions
{
    /// <summary>Working-time rules. Defaults to <see cref="GanttCalendar.Continuous"/>.</summary>
    public GanttCalendar Calendar { get; set; } = GanttCalendar.Continuous;

    /// <summary>
    /// Whether a summary's dates and progress are recomputed from its children. On by default; turn
    /// it off when the parent rows carry meaningful dates of their own — a phase with a contractual
    /// window wider than the work inside it, say.
    /// </summary>
    public bool RollUpSummaries { get; set; } = true;

    /// <summary>Whether to run the critical-path pass. Off skips it and leaves every task non-critical.</summary>
    public bool ComputeCriticalPath { get; set; } = true;

    /// <summary>
    /// How little slack still counts as critical. Zero is the strict definition; a day is the usual
    /// practical one, because a plan built from whole days rarely lands on exact zero.
    /// </summary>
    public TimeSpan CriticalSlackThreshold { get; set; } = TimeSpan.Zero;
}
