namespace Shiny.Controls.Gantt;

/// <summary>
/// Turns a gesture — a bar dragged, an edge pulled, a link drawn — into a <see cref="GanttSchedulePlan"/>:
/// the complete set of tasks that would move, validated, but not yet applied.
/// </summary>
/// <remarks>
/// Nothing here mutates a task. The separation is what lets a control raise a cancellable event with
/// the whole consequence in hand, and it is why the drag preview a user sees and the dates that
/// eventually land can be computed by the same code instead of two implementations that drift.
/// </remarks>
public static class GanttScheduler
{
    /// <summary>
    /// The rollup/cascade loop is run to a fixed point rather than once, because the two passes feed
    /// each other: moving a task changes its summary's rolled-up dates, and a summary is itself a
    /// legal predecessor. Three passes settle every plan shape encountered in practice; the ceiling
    /// exists so a hostile one terminates rather than freezing a layout pass.
    /// </summary>
    const int MaxSettlePasses = 8;

    readonly record struct Span(DateTimeOffset Start, DateTimeOffset End);


    /// <summary>Drags a bar to a new start, keeping its duration.</summary>
    public static GanttSchedulePlan Move(
        GanttModel model,
        GanttTask task,
        DateTimeOffset newStart,
        GanttScheduleOptions? options = null
    )
    {
        var opts = options ?? new GanttScheduleOptions();

        if (!task.CanMove)
            return GanttSchedulePlan.Blocked(GanttChangeKind.Move, task, GanttValidationCode.ConstraintViolated, $"Task '{task.Name}' cannot be moved.");

        if (task.HasChildren && !opts.MoveSubtreeWithSummary)
            return GanttSchedulePlan.Blocked(GanttChangeKind.Move, task, GanttValidationCode.ConstraintViolated, $"Summary '{task.Name}' takes its dates from its children.");

        var issues = new List<GanttValidationIssue>();
        var dates = Snapshot(model);
        var seeds = new HashSet<string>(StringComparer.Ordinal);

        var delta = newStart - task.Start;

        // A summary has no dates of its own to keep, so the gesture means "shift everything under
        // this". Applying the same wall-clock delta to each descendant — rather than rescheduling
        // each one from the parent's new start — is what preserves the internal shape of the phase.
        foreach (var member in Subtree(model, task))
        {
            var current = dates[member.Id];
            var shifted = Place(member, current.Start + delta, WorkingDuration(member, current, opts), opts, issues);
            dates[member.Id] = shifted;
            seeds.Add(member.Id);
        }

        return BuildPlan(GanttChangeKind.Move, task, model, opts, dates, seeds, issues);
    }


    /// <summary>Drags one edge of a bar, holding the other.</summary>
    public static GanttSchedulePlan Resize(
        GanttModel model,
        GanttTask task,
        DateTimeOffset newStart,
        DateTimeOffset newEnd,
        GanttScheduleOptions? options = null
    )
    {
        var opts = options ?? new GanttScheduleOptions();

        if (!task.CanResize)
            return GanttSchedulePlan.Blocked(GanttChangeKind.ResizeEnd, task, GanttValidationCode.ConstraintViolated, $"Task '{task.Name}' cannot be resized.");

        if (task.HasChildren && !opts.AllowSummaryResize)
            return GanttSchedulePlan.Blocked(GanttChangeKind.ResizeEnd, task, GanttValidationCode.ConstraintViolated, $"Summary '{task.Name}' takes its dates from its children.");

        var kind = newStart != task.Start ? GanttChangeKind.ResizeStart : GanttChangeKind.ResizeEnd;
        var issues = new List<GanttValidationIssue>();

        if (newEnd < newStart)
        {
            // A drag that crosses the far edge is a slip of the hand, not an instruction to invert
            // the bar. Collapsing it to zero is the behaviour every drag handle in the repo has.
            (newStart, newEnd) = kind == GanttChangeKind.ResizeStart ? (newEnd, newEnd) : (newStart, newStart);
        }

        if (opts.MinimumDuration > TimeSpan.Zero && task.Kind != GanttTaskKind.Milestone)
        {
            var duration = opts.PreserveWorkingDuration
                ? opts.Calendar.WorkingTimeBetween(newStart, newEnd)
                : newEnd - newStart;

            if (duration < opts.MinimumDuration)
            {
                if (kind == GanttChangeKind.ResizeStart)
                    newStart = Back(newEnd, opts.MinimumDuration, opts);
                else
                    newEnd = Forward(newStart, opts.MinimumDuration, opts);
            }
        }

        var dates = Snapshot(model);
        var placed = Place(task, newStart, PlainDuration(task, new Span(newStart, newEnd), opts), opts, issues);
        dates[task.Id] = placed;

        return BuildPlan(kind, task, model, opts, dates, Seeds(task.Id), issues);
    }


    /// <summary>Sets completion. Never cascades — progress does not move dates.</summary>
    public static GanttSchedulePlan SetProgress(GanttTask task, double progress)
    {
        if (!task.CanChangeProgress)
            return GanttSchedulePlan.Blocked(GanttChangeKind.Progress, task, GanttValidationCode.ConstraintViolated, $"Task '{task.Name}' progress is read-only.");

        var change = new GanttTaskSchedule(task, isCascade: false)
        {
            NewProgress = double.IsNaN(progress) ? 0d : Math.Clamp(progress, 0d, 1d)
        };
        return new GanttSchedulePlan(GanttChangeKind.Progress, task, [change], []);
    }


    /// <summary>
    /// Works out what adding a link would do, refusing outright if it would close a cycle. The refusal
    /// is the one case that genuinely has to block: a cyclic graph has no schedule to compute.
    /// </summary>
    public static GanttSchedulePlan AddDependency(
        GanttModel model,
        GanttDependency dependency,
        GanttScheduleOptions? options = null
    )
    {
        var opts = options ?? new GanttScheduleOptions();

        if (!model.TryGetTask(dependency.PredecessorId, out var predecessor) ||
            !model.TryGetTask(dependency.SuccessorId, out _))
        {
            return GanttSchedulePlan.Blocked(
                GanttChangeKind.DependencyAdded, null, GanttValidationCode.UnknownTaskReference,
                $"Dependency {dependency} names a task that is not in the plan.", dependency
            );
        }

        if (dependency.PredecessorId == dependency.SuccessorId ||
            WouldCreateCycle(model, dependency.PredecessorId, dependency.SuccessorId))
        {
            return GanttSchedulePlan.Blocked(
                GanttChangeKind.DependencyAdded, predecessor, GanttValidationCode.CircularDependency,
                $"Linking '{dependency.PredecessorId}' to '{dependency.SuccessorId}' would create a circular dependency.",
                dependency
            );
        }

        var issues = new List<GanttValidationIssue>();
        var dates = Snapshot(model);

        // The link itself is not in the model yet, so seed the cascade with the successor's bound
        // computed from this one dependency, then let the ordinary settle pass carry it onward.
        var successor = model[dependency.SuccessorId]!;
        var bound = RequiredStart(model, dependency, dates, successor, opts);

        if (bound is { } required && (opts.Cascade == GanttCascadeMode.Strict || required > dates[successor.Id].Start))
        {
            var current = dates[successor.Id];
            dates[successor.Id] = Place(successor, required, WorkingDuration(successor, current, opts), opts, issues);
        }

        return BuildPlan(GanttChangeKind.DependencyAdded, predecessor, model, opts, dates, Seeds(successor.Id), issues, dependency);
    }


    /// <summary>Re-derives every task's dates from the dependency graph, as if each had just been edited.</summary>
    public static GanttSchedulePlan Reschedule(GanttModel model, GanttScheduleOptions? options = null)
    {
        var opts = options ?? new GanttScheduleOptions();
        var issues = new List<GanttValidationIssue>();
        return BuildPlan(GanttChangeKind.Cascade, null, model, opts, Snapshot(model), Seeds(), issues);
    }


    /// <summary>
    /// Whether a link from <paramref name="predecessorId"/> to <paramref name="successorId"/> would
    /// close a loop — that is, whether the predecessor is already reachable from the successor.
    /// </summary>
    public static bool WouldCreateCycle(GanttModel model, string predecessorId, string successorId)
    {
        if (predecessorId == successorId)
            return true;

        var seen = new HashSet<string>(StringComparer.Ordinal) { successorId };
        var stack = new Stack<string>();
        stack.Push(successorId);

        while (stack.Count > 0)
        {
            foreach (var dep in model.SuccessorsOf(stack.Pop()))
            {
                if (dep.SuccessorId == predecessorId)
                    return true;

                if (seen.Add(dep.SuccessorId))
                    stack.Push(dep.SuccessorId);
            }
        }
        return false;
    }


    // =============================================================================================
    // Cascade
    // =============================================================================================

    static GanttSchedulePlan BuildPlan(
        GanttChangeKind kind,
        GanttTask? task,
        GanttModel model,
        GanttScheduleOptions opts,
        Dictionary<string, Span> dates,
        IReadOnlySet<string> seeds,
        List<GanttValidationIssue> issues,
        GanttDependency? dependency = null
    )
    {
        if (opts.Cascade != GanttCascadeMode.None)
            Settle(model, opts, dates, seeds, issues);

        // Rollups are recomputed either way: even with cascading off, dragging a child has to move
        // the summary bar drawn above it or the two disagree until the next rebuild.
        RollUp(model, dates);

        var changes = new List<GanttTaskSchedule>();

        // The edited task leads, so a consumer reading Changes[0] gets what the user actually did.
        if (task is not null && dates.TryGetValue(task.Id, out var edited))
            changes.Add(Record(task, edited, isCascade: false));

        foreach (var candidate in model.AllTasks)
        {
            if (task is not null && ReferenceEquals(candidate, task))
                continue;

            if (!dates.TryGetValue(candidate.Id, out var span))
                continue;

            var record = Record(candidate, span, isCascade: !seeds.Contains(candidate.Id));
            if (record.HasChange)
                changes.Add(record);
        }

        if (changes.Count > opts.MaxCascadedTasks)
        {
            issues.Add(new GanttValidationIssue(
                GanttValidationCode.OutOfRange,
                $"The edit would move {changes.Count} tasks, past the {opts.MaxCascadedTasks} limit; it was not applied.",
                task?.Id, null, IsBlocking: true
            ));
        }

        ValidateLinks(model, dates, issues);
        return new GanttSchedulePlan(kind, task, changes, issues, dependency);

        static GanttTaskSchedule Record(GanttTask t, Span span, bool isCascade) =>
            new(t, isCascade) { NewStart = span.Start, NewEnd = span.End };
    }


    /// <summary>Alternates the link cascade and the summary rollup until neither moves anything.</summary>
    static void Settle(
        GanttModel model,
        GanttScheduleOptions opts,
        Dictionary<string, Span> dates,
        IReadOnlySet<string> seeds,
        List<GanttValidationIssue> issues
    )
    {
        for (var pass = 0; pass < MaxSettlePasses; pass++)
        {
            var moved = CascadePass(model, opts, dates, seeds, issues);
            moved |= RollUp(model, dates);

            if (!moved)
                return;
        }
    }


    static bool CascadePass(
        GanttModel model,
        GanttScheduleOptions opts,
        Dictionary<string, Span> dates,
        IReadOnlySet<string> seeds,
        List<GanttValidationIssue> issues
    )
    {
        var moved = false;

        // TopologicalOrder guarantees a predecessor is visited before its successor, so one sweep
        // propagates a change the full depth of the graph. Tasks caught in a cycle are absent from
        // the order, which is exactly the "leave them alone" behaviour we want.
        foreach (var id in model.TopologicalOrder)
        {
            if (seeds.Contains(id))
                continue;

            var task = model[id]!;
            if (task.ManuallyScheduled || task.HasChildren)
                continue;

            var predecessors = model.PredecessorsOf(id);
            if (predecessors.Count == 0)
                continue;

            DateTimeOffset? required = null;
            foreach (var dep in predecessors)
            {
                if (RequiredStart(model, dep, dates, task, opts) is not { } candidate)
                    continue;

                if (required is null || candidate > required)
                    required = candidate;
            }

            if (required is null)
                continue;

            var current = dates[id];
            var target = opts.Cascade == GanttCascadeMode.Strict
                ? required.Value
                : (required.Value > current.Start ? required.Value : current.Start);

            if (target == current.Start)
                continue;

            var placed = Place(task, target, WorkingDuration(task, current, opts), opts, issues);
            if (placed.Start == current.Start && placed.End == current.End)
                continue;

            dates[id] = placed;
            moved = true;
        }
        return moved;
    }


    /// <summary>The earliest start one dependency permits its successor, or null when it imposes none.</summary>
    static DateTimeOffset? RequiredStart(
        GanttModel model,
        GanttDependency dep,
        Dictionary<string, Span> dates,
        GanttTask successor,
        GanttScheduleOptions opts
    )
    {
        if (!dates.TryGetValue(dep.PredecessorId, out var predecessor))
            return null;

        var duration = WorkingDuration(successor, dates[successor.Id], opts);

        return dep.Type switch
        {
            GanttDependencyType.FinishToStart => Lagged(predecessor.End),
            GanttDependencyType.StartToStart => Lagged(predecessor.Start),

            // These two bound the finish, so convert back to a start by removing the duration.
            GanttDependencyType.FinishToFinish => Back(Lagged(predecessor.End), duration, opts),
            GanttDependencyType.StartToFinish => Back(Lagged(predecessor.Start), duration, opts),

            _ => null
        };

        DateTimeOffset Lagged(DateTimeOffset value) =>
            dep.Lag >= TimeSpan.Zero ? Forward(value, dep.Lag, opts) : Back(value, -dep.Lag, opts);
    }


    /// <summary>Rewrites every summary's span from its children. Returns whether anything changed.</summary>
    static bool RollUp(GanttModel model, Dictionary<string, Span> dates)
    {
        var moved = false;

        // AllTasks is pre-order, so backwards visits every child before its parent.
        for (var i = model.AllTasks.Count - 1; i >= 0; i--)
        {
            var task = model.AllTasks[i];
            var children = model.ChildrenOf(task.Id);
            if (children.Count == 0)
                continue;

            var start = DateTimeOffset.MaxValue;
            var end = DateTimeOffset.MinValue;

            foreach (var child in children)
            {
                if (!dates.TryGetValue(child.Id, out var span))
                    continue;

                var childEnd = child.Kind == GanttTaskKind.Milestone ? span.Start : span.End;

                if (span.Start < start)
                    start = span.Start;

                if (childEnd > end)
                    end = childEnd;
            }

            if (start == DateTimeOffset.MaxValue)
                continue;

            var rolled = new Span(start, end);
            if (dates.TryGetValue(task.Id, out var existing) && existing == rolled)
                continue;

            dates[task.Id] = rolled;
            moved = true;
        }
        return moved;
    }


    /// <summary>Reports links the settled dates still break, without blocking the edit.</summary>
    static void ValidateLinks(GanttModel model, Dictionary<string, Span> dates, List<GanttValidationIssue> issues)
    {
        foreach (var dep in model.Dependencies)
        {
            if (!dates.TryGetValue(dep.PredecessorId, out var p) || !dates.TryGetValue(dep.SuccessorId, out var s))
                continue;

            var satisfied = dep.Type switch
            {
                GanttDependencyType.FinishToStart => s.Start >= p.End + dep.Lag,
                GanttDependencyType.StartToStart => s.Start >= p.Start + dep.Lag,
                GanttDependencyType.FinishToFinish => s.End >= p.End + dep.Lag,
                GanttDependencyType.StartToFinish => s.End >= p.Start + dep.Lag,
                _ => true
            };

            if (!satisfied)
            {
                issues.Add(new GanttValidationIssue(
                    GanttValidationCode.DependencyViolated,
                    $"'{dep.SuccessorId}' still breaks its {dep.Type} link from '{dep.PredecessorId}'; a constraint or a manual schedule is holding it there.",
                    dep.SuccessorId,
                    dep.PredecessorId
                ));
            }
        }
    }


    // =============================================================================================
    // Placement
    // =============================================================================================

    /// <summary>
    /// Puts a task at a start with a duration, then applies — in this order — working-time snapping,
    /// the task's own constraint, and the plan-wide date bounds. The order matters: a constraint is a
    /// statement about the calendar date, so it must win over a working-time nudge, and the plan
    /// bounds are absolute so they win over everything.
    /// </summary>
    static Span Place(
        GanttTask task,
        DateTimeOffset start,
        TimeSpan duration,
        GanttScheduleOptions opts,
        List<GanttValidationIssue> issues
    )
    {
        if (task.Kind == GanttTaskKind.Milestone)
            duration = TimeSpan.Zero;

        if (opts.SnapToWorkingTime && !opts.Calendar.IsContinuous)
            start = opts.Calendar.NextWorkingTime(start);

        var end = Forward(start, duration, opts);

        if (opts.EnforceConstraints && task.Constraint != GanttConstraintType.None && task.ConstraintDate is { } cd)
        {
            var before = start;

            switch (task.Constraint)
            {
                case GanttConstraintType.MustStartOn:
                    start = cd;
                    end = Forward(start, duration, opts);
                    break;

                case GanttConstraintType.MustFinishOn:
                    end = cd;
                    start = Back(end, duration, opts);
                    break;

                case GanttConstraintType.StartNoEarlierThan when start < cd:
                    start = cd;
                    end = Forward(start, duration, opts);
                    break;

                case GanttConstraintType.StartNoLaterThan when start > cd:
                    start = cd;
                    end = Forward(start, duration, opts);
                    break;

                case GanttConstraintType.FinishNoEarlierThan when end < cd:
                    end = cd;
                    start = Back(end, duration, opts);
                    break;

                case GanttConstraintType.FinishNoLaterThan when end > cd:
                    end = cd;
                    start = Back(end, duration, opts);
                    break;
            }

            if (start != before)
            {
                issues.Add(new GanttValidationIssue(
                    GanttValidationCode.ConstraintViolated,
                    $"'{task.Name}' was held at {start:g} by its {task.Constraint} constraint.",
                    task.Id
                ));
            }
        }

        if (opts.MinDate is { } min && start < min)
        {
            start = min;
            end = Forward(start, duration, opts);
            issues.Add(new GanttValidationIssue(GanttValidationCode.OutOfRange, $"'{task.Name}' was clamped to the plan's earliest date.", task.Id));
        }

        if (opts.MaxDate is { } max && end > max)
        {
            end = max;
            start = Back(end, duration, opts);
            issues.Add(new GanttValidationIssue(GanttValidationCode.OutOfRange, $"'{task.Name}' was clamped to the plan's latest date.", task.Id));
        }

        return new Span(start, end < start ? start : end);
    }


    static HashSet<string> Seeds(params string[] ids) => new(ids, StringComparer.Ordinal);


    static Dictionary<string, Span> Snapshot(GanttModel model)
    {
        var dates = new Dictionary<string, Span>(model.AllTasks.Count, StringComparer.Ordinal);
        foreach (var task in model.AllTasks)
            dates[task.Id] = new Span(task.Start, task.Kind == GanttTaskKind.Milestone ? task.Start : task.End);

        return dates;
    }


    static IEnumerable<GanttTask> Subtree(GanttModel model, GanttTask root)
    {
        var stack = new Stack<GanttTask>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var task = stack.Pop();
            yield return task;

            foreach (var child in model.ChildrenOf(task.Id))
                stack.Push(child);
        }
    }


    static TimeSpan WorkingDuration(GanttTask task, Span span, GanttScheduleOptions opts) =>
        task.Kind == GanttTaskKind.Milestone
            ? TimeSpan.Zero
            : opts.PreserveWorkingDuration
                ? opts.Calendar.WorkingTimeBetween(span.Start, span.End)
                : span.End - span.Start;

    static TimeSpan PlainDuration(GanttTask task, Span span, GanttScheduleOptions opts) =>
        WorkingDuration(task, span, opts);

    static DateTimeOffset Forward(DateTimeOffset from, TimeSpan by, GanttScheduleOptions opts) =>
        opts.PreserveWorkingDuration ? opts.Calendar.AddWorkingTime(from, by) : from + by;

    static DateTimeOffset Back(DateTimeOffset from, TimeSpan by, GanttScheduleOptions opts) =>
        opts.PreserveWorkingDuration ? opts.Calendar.SubtractWorkingTime(from, by) : from - by;
}
