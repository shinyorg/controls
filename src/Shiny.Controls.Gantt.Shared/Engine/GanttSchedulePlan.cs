namespace Shiny.Controls.Gantt;

/// <summary>One task's before-and-after inside a <see cref="GanttSchedulePlan"/>.</summary>
public sealed class GanttTaskSchedule
{
    internal GanttTaskSchedule(GanttTask task, bool isCascade)
    {
        this.Task = task;
        this.IsCascade = isCascade;
        this.OldStart = this.NewStart = task.Start;
        this.OldEnd = this.NewEnd = task.End;
        this.OldProgress = this.NewProgress = task.Progress;
    }

    /// <summary>The task this entry is about.</summary>
    public GanttTask Task { get; }

    /// <summary>Where the task started before the edit.</summary>
    public DateTimeOffset OldStart { get; internal set; }

    /// <summary>Where the task finished before the edit.</summary>
    public DateTimeOffset OldEnd { get; internal set; }

    /// <summary>Where the task starts once the plan is applied.</summary>
    public DateTimeOffset NewStart { get; internal set; }

    /// <summary>Where the task finishes once the plan is applied.</summary>
    public DateTimeOffset NewEnd { get; internal set; }

    /// <summary>Completion, 0-1, before the edit.</summary>
    public double OldProgress { get; internal set; }

    /// <summary>Completion, 0-1, once the plan is applied.</summary>
    public double NewProgress { get; internal set; }

    /// <summary>True when the auto-scheduler moved this task rather than the user dragging it.</summary>
    public bool IsCascade { get; }

    /// <summary>Whether anything actually differs.</summary>
    public bool HasChange =>
        this.NewStart != this.OldStart ||
        this.NewEnd != this.OldEnd ||
        Math.Abs(this.NewProgress - this.OldProgress) > 0.0001d;

    /// <summary>How far the task moved.</summary>
    public TimeSpan Delta => this.NewStart - this.OldStart;

    /// <summary>The move in one line, for logs and debugger display.</summary>
    public override string ToString() =>
        $"{this.Task.Name}: {this.OldStart:g}-{this.OldEnd:g} -> {this.NewStart:g}-{this.NewEnd:g}{(this.IsCascade ? " (cascade)" : "")}";
}


/// <summary>
/// The full consequence of one edit, computed but not yet applied.
/// </summary>
/// <remarks>
/// <para>
/// Separating "what would happen" from "make it happen" is the whole point. It lets the control raise
/// a cancellable event carrying the complete picture — including every task the cascade would drag
/// along — before a single date changes, and it hands the consumer undo for nothing:
/// <see cref="Revert"/> puts every task back exactly where it was, because the plan recorded the old
/// values rather than trying to recompute them.
/// </para>
/// </remarks>
public sealed class GanttSchedulePlan
{
    readonly List<GanttTaskSchedule> changes;
    readonly List<GanttValidationIssue> issues;
    bool applied;

    internal GanttSchedulePlan(
        GanttChangeKind kind,
        GanttTask? task,
        List<GanttTaskSchedule> changes,
        List<GanttValidationIssue> issues,
        GanttDependency? dependency = null
    )
    {
        this.Kind = kind;
        this.Task = task;
        this.Dependency = dependency;
        this.changes = changes;
        this.issues = issues;
    }

    /// <summary>What kind of edit produced this plan.</summary>
    public GanttChangeKind Kind { get; }

    /// <summary>The task the user edited, or null for a dependency-only change.</summary>
    public GanttTask? Task { get; }

    /// <summary>The dependency added or removed, for those two kinds.</summary>
    public GanttDependency? Dependency { get; }

    /// <summary>Every task that would move, the edited one first.</summary>
    public IReadOnlyList<GanttTaskSchedule> Changes => this.changes;

    /// <summary>Just the tasks the auto-scheduler dragged along.</summary>
    public IEnumerable<GanttTaskSchedule> Cascade => this.changes.Where(x => x.IsCascade);

    /// <summary>Anything the engine wants to say about the edit, blocking or not.</summary>
    public IReadOnlyList<GanttValidationIssue> Issues => this.issues;

    /// <summary>False when something stopped the edit outright; <see cref="Apply"/> then does nothing.</summary>
    public bool IsValid => !this.issues.Any(x => x.IsBlocking);

    /// <summary>Whether any task would actually move.</summary>
    public bool HasChanges => this.changes.Any(x => x.HasChange);

    /// <summary>Whether <see cref="Apply"/> has run and not been reverted.</summary>
    public bool IsApplied => this.applied;


    /// <summary>
    /// Writes the new dates onto the tasks. A no-op when <see cref="IsValid"/> is false, or when
    /// already applied. Returns whether anything changed.
    /// </summary>
    public bool Apply()
    {
        if (this.applied || !this.IsValid)
            return false;

        foreach (var change in this.changes)
        {
            change.Task.Start = change.NewStart;
            change.Task.End = change.NewEnd;
            change.Task.Progress = change.NewProgress;
        }
        this.applied = true;
        return true;
    }


    /// <summary>Puts every task back where it was. A no-op unless <see cref="Apply"/> has run.</summary>
    public bool Revert()
    {
        if (!this.applied)
            return false;

        // Reverse order so a task whose new dates were derived from another's lands back on the
        // values it actually held, not on an intermediate state.
        for (var i = this.changes.Count - 1; i >= 0; i--)
        {
            var change = this.changes[i];
            change.Task.Start = change.OldStart;
            change.Task.End = change.OldEnd;
            change.Task.Progress = change.OldProgress;
        }
        this.applied = false;
        return true;
    }


    /// <summary>A plan that does nothing, for the paths where an edit is rejected before it starts.</summary>
    internal static GanttSchedulePlan Blocked(
        GanttChangeKind kind,
        GanttTask? task,
        GanttValidationCode code,
        string message,
        GanttDependency? dependency = null
    ) => new(
        kind,
        task,
        [],
        [new GanttValidationIssue(code, message, task?.Id, null, true)],
        dependency
    );
}
