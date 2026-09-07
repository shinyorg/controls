namespace Shiny.Controls.Gantt;

/// <summary>
/// A plan, resolved. Takes the tasks and dependencies a consumer supplied — in whatever shape they
/// supplied them — and works out the hierarchy, the rolled-up summary dates, the visible row order,
/// the dependency graph and the critical path.
/// </summary>
/// <remarks>
/// <para>
/// The model never reshapes the consumer's collections. A flat list stays a flat list and a nested
/// tree stays nested; the parent/child relationships it resolves live in the model's own indexes and
/// in the engine-owned properties on <see cref="GanttTask"/>. That matters because both hosts bind
/// to the very collections being read, and quietly re-parenting an <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>
/// mid-build would raise change notifications into a layout pass that is already running.
/// </para>
/// <para>
/// Building is cheap enough to do on every change: it is a handful of linear passes plus a
/// topological sort, with no allocation per row beyond the row list itself.
/// </para>
/// </remarks>
public sealed class GanttModel
{
    /// <summary>A plan with nothing in it. Every collection is empty; the extent is a zero-length span at the epoch.</summary>
    public static GanttModel Empty { get; } = Build(null, null);

    readonly Dictionary<string, GanttTask> byId;
    readonly Dictionary<string, List<GanttTask>> childrenOf;
    readonly Dictionary<string, List<GanttDependency>> successorsOf;
    readonly Dictionary<string, List<GanttDependency>> predecessorsOf;
    readonly Dictionary<string, DateTimeOffset> latestFinish;
    readonly List<GanttValidationIssue> issues;

    GanttModel(
        Dictionary<string, GanttTask> byId,
        Dictionary<string, List<GanttTask>> childrenOf,
        Dictionary<string, List<GanttDependency>> successorsOf,
        Dictionary<string, List<GanttDependency>> predecessorsOf,
        Dictionary<string, DateTimeOffset> latestFinish,
        List<GanttValidationIssue> issues,
        List<GanttTask> allTasks,
        List<GanttTask> roots,
        List<GanttRow> rows,
        List<GanttDependency> dependencies,
        IReadOnlyList<string> topologicalOrder,
        GanttCalendar calendar,
        DateTimeOffset start,
        DateTimeOffset end
    )
    {
        this.byId = byId;
        this.childrenOf = childrenOf;
        this.successorsOf = successorsOf;
        this.predecessorsOf = predecessorsOf;
        this.latestFinish = latestFinish;
        this.issues = issues;
        this.AllTasks = allTasks;
        this.Roots = roots;
        this.Rows = rows;
        this.Dependencies = dependencies;
        this.TopologicalOrder = topologicalOrder;
        this.Calendar = calendar;
        this.Start = start;
        this.End = end;
    }

    /// <summary>Every task, depth first, in row order — including ones hidden inside a collapsed summary.</summary>
    public IReadOnlyList<GanttTask> AllTasks { get; }

    /// <summary>Tasks with no parent, in the order they were supplied.</summary>
    public IReadOnlyList<GanttTask> Roots { get; }

    /// <summary>The visible rows: <see cref="AllTasks"/> minus anything inside a collapsed summary.</summary>
    public IReadOnlyList<GanttRow> Rows { get; }

    /// <summary>The dependencies whose two ends both resolved. Dangling ones are dropped and reported.</summary>
    public IReadOnlyList<GanttDependency> Dependencies { get; }

    /// <summary>
    /// Task ids in dependency order, predecessors first. Tasks caught in a cycle are omitted, which
    /// is how the schedulers know to leave them alone.
    /// </summary>
    public IReadOnlyList<string> TopologicalOrder { get; }

    /// <summary>The working-time rules this model was built against.</summary>
    public GanttCalendar Calendar { get; }

    /// <summary>Everything wrong with the plan. Empty for a clean one.</summary>
    public IReadOnlyList<GanttValidationIssue> Issues => this.issues;

    /// <summary>True when the dependency graph contains a cycle.</summary>
    public bool HasCycle => this.TopologicalOrder.Count < this.byId.Count && this.issues.Any(x => x.Code == GanttValidationCode.CircularDependency);

    /// <summary>Earliest start across the plan. Equals <see cref="End"/> when the plan is empty.</summary>
    public DateTimeOffset Start { get; }

    /// <summary>Latest finish across the plan.</summary>
    public DateTimeOffset End { get; }

    /// <summary>The plan's total span.</summary>
    public TimeSpan Duration => this.End - this.Start;

    /// <summary>How many rows are currently visible.</summary>
    public int RowCount => this.Rows.Count;


    public bool TryGetTask(string? id, out GanttTask task)
    {
        if (id is not null)
            return this.byId.TryGetValue(id, out task!);

        task = null!;
        return false;
    }

    /// <summary>The task with this id, or null.</summary>
    public GanttTask? this[string id] => this.byId.GetValueOrDefault(id);

    /// <summary>The resolved children of a task, nested and flat sources combined.</summary>
    public IReadOnlyList<GanttTask> ChildrenOf(string id) =>
        this.childrenOf.TryGetValue(id, out var list) ? list : [];

    /// <summary>Dependencies where this task is the predecessor — the arrows leaving it.</summary>
    public IReadOnlyList<GanttDependency> SuccessorsOf(string id) =>
        this.successorsOf.TryGetValue(id, out var list) ? list : [];

    /// <summary>Dependencies where this task is the successor — the arrows arriving at it.</summary>
    public IReadOnlyList<GanttDependency> PredecessorsOf(string id) =>
        this.predecessorsOf.TryGetValue(id, out var list) ? list : [];

    /// <summary>
    /// The latest this task could finish without pushing the project finish out. Absent when the
    /// critical path was not computed, or the task sits on a dependency cycle.
    /// </summary>
    public bool TryGetLatestFinish(string id, out DateTimeOffset value) =>
        this.latestFinish.TryGetValue(id, out value);

    /// <summary>The visible row index of a task, or -1 when it is hidden inside a collapsed summary.</summary>
    public int RowIndexOf(string id)
    {
        for (var i = 0; i < this.Rows.Count; i++)
        {
            if (this.Rows[i].Task.Id == id)
                return i;
        }
        return -1;
    }

    /// <summary>Whether <paramref name="ancestorId"/> is at or above <paramref name="taskId"/> in the hierarchy.</summary>
    public bool IsAncestorOf(string ancestorId, string taskId)
    {
        if (!this.byId.TryGetValue(taskId, out var task))
            return false;

        if (ancestorId == taskId)
            return true;

        foreach (var a in task.Ancestors())
        {
            if (a.Id == ancestorId)
                return true;
        }
        return false;
    }

    /// <summary>The task's duration in working time, zero for a milestone.</summary>
    public TimeSpan WorkingDurationOf(GanttTask task) =>
        task.Kind == GanttTaskKind.Milestone
            ? TimeSpan.Zero
            : this.Calendar.WorkingTimeBetween(task.Start, task.End);


    // =============================================================================================
    // Build
    // =============================================================================================

    /// <summary>
    /// Resolves a plan. Safe to call with nulls, which is what an unbound control does on first
    /// layout.
    /// </summary>
    public static GanttModel Build(
        IEnumerable<GanttTask>? tasks,
        IEnumerable<GanttDependency>? dependencies = null,
        GanttModelOptions? options = null
    )
    {
        var opts = options ?? new GanttModelOptions();
        var issues = new List<GanttValidationIssue>();

        var byId = new Dictionary<string, GanttTask>(StringComparer.Ordinal);
        var all = Flatten(tasks, byId, issues);

        ResolveParents(all, byId, issues);
        var childrenOf = IndexChildren(all);
        AssignDepthAndKind(all, childrenOf);

        if (opts.RollUpSummaries)
            RollUp(all, childrenOf, opts.Calendar);

        var roots = all.Where(x => x.Parent is null).ToList();
        var rows = BuildRows(roots, childrenOf, all);

        var deps = IndexDependencies(dependencies, byId, issues, out var successorsOf, out var predecessorsOf);
        var topo = TopologicalSort(all, successorsOf, predecessorsOf, issues);

        var latestFinish = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var (start, end) = Extent(all);

        if (opts.ComputeCriticalPath)
            ComputeCriticalPath(all, topo, byId, successorsOf, deps, latestFinish, opts, end);
        else
            ClearCriticalPath(all, deps);

        ReportDeadlines(all, issues);

        return new GanttModel(
            byId, childrenOf, successorsOf, predecessorsOf, latestFinish, issues,
            all, roots, rows, deps, topo, opts.Calendar, start, end
        );
    }


    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Walks the supplied tasks and everything nested under them into one pre-order list. Uses an
    /// explicit stack rather than recursion so a pathologically deep plan cannot blow the stack
    /// inside a layout pass, and lets the id dictionary double as the cycle guard.
    /// </summary>
    static List<GanttTask> Flatten(
        IEnumerable<GanttTask>? tasks,
        Dictionary<string, GanttTask> byId,
        List<GanttValidationIssue> issues
    )
    {
        var all = new List<GanttTask>();
        if (tasks is null)
            return all;

        var stack = new Stack<GanttTask>();
        foreach (var t in tasks.Reverse())
            stack.Push(t);

        while (stack.Count > 0)
        {
            var task = stack.Pop();

            if (!byId.TryAdd(task.Id, task))
            {
                // Either two distinct tasks share an id, or Children loops back on itself. The
                // second is not worth a separate code: both mean "this id already named something".
                if (!ReferenceEquals(byId[task.Id], task))
                {
                    issues.Add(new GanttValidationIssue(
                        GanttValidationCode.DuplicateId,
                        $"More than one task uses the id '{task.Id}'; only the first was kept.",
                        task.Id
                    ));
                }
                continue;
            }

            task.ResolvedChildCount = 0;
            all.Add(task);

            for (var i = task.Children.Count - 1; i >= 0; i--)
                stack.Push(task.Children[i]);
        }
        return all;
    }


    static void ResolveParents(List<GanttTask> all, Dictionary<string, GanttTask> byId, List<GanttValidationIssue> issues)
    {
        foreach (var task in all)
        {
            GanttTask? parent = null;

            if (task.ParentId is { Length: > 0 } pid &&
                byId.TryGetValue(pid, out var found) &&
                !ReferenceEquals(found, task))
            {
                parent = found;
            }
            else if (task.Parent is not null && byId.ContainsKey(task.Parent.Id) && !ReferenceEquals(task.Parent, task))
            {
                // Nested under something whose ParentId was never written — keep the nesting.
                parent = task.Parent;
            }

            task.Parent = parent;
        }

        // Now that every link is resolved, break any that closes a loop. Doing it in a second pass
        // matters: a cycle A->B->A cannot be spotted while the second link is still being assigned.
        foreach (var task in all)
        {
            var slow = task.Parent;
            var fast = task.Parent?.Parent;

            while (slow is not null && fast is not null)
            {
                if (ReferenceEquals(slow, fast) || ReferenceEquals(fast, task))
                {
                    issues.Add(new GanttValidationIssue(
                        GanttValidationCode.CircularHierarchy,
                        $"Task '{task.Id}' is its own ancestor; the parent link was dropped so the rest of the plan still resolves.",
                        task.Id,
                        task.Parent?.Id
                    ));
                    task.Parent = null;
                    break;
                }
                slow = slow.Parent;
                fast = fast.Parent?.Parent;
            }
        }
    }


    static Dictionary<string, List<GanttTask>> IndexChildren(List<GanttTask> all)
    {
        var childrenOf = new Dictionary<string, List<GanttTask>>(StringComparer.Ordinal);

        // `all` is already in pre-order, so appending as we go preserves both the nested order and
        // the supplied order of a flat list without a sort.
        foreach (var task in all)
        {
            if (task.Parent is null)
                continue;

            if (!childrenOf.TryGetValue(task.Parent.Id, out var list))
                childrenOf[task.Parent.Id] = list = [];

            list.Add(task);
        }

        foreach (var (id, list) in childrenOf)
        {
            if (all.Find(x => x.Id == id) is { } parent)
                parent.ResolvedChildCount = list.Count;
        }
        return childrenOf;
    }


    static void AssignDepthAndKind(List<GanttTask> all, Dictionary<string, List<GanttTask>> childrenOf)
    {
        foreach (var task in all)
        {
            var depth = 0;
            foreach (var _ in task.Ancestors())
                depth++;

            task.Depth = depth;

            // A parent draws as a summary bracket whether or not the consumer said so. Project and
            // Milestone are left alone: the first is a deliberate styling choice for the root, and a
            // milestone with children is a modelling mistake the control should show, not paper over.
            if (childrenOf.ContainsKey(task.Id) && task.Kind == GanttTaskKind.Task)
                task.Kind = GanttTaskKind.Summary;
        }

        // Visibility needs parents settled first, so it gets its own pass over the pre-ordered list —
        // where a parent is always seen before its children.
        foreach (var task in all)
            task.IsVisible = task.Parent is null || (task.Parent.IsVisible && task.Parent.IsExpanded);
    }


    /// <summary>
    /// Recomputes every summary's span and progress from its children, deepest first so a three-level
    /// plan rolls all the way up in one pass.
    /// </summary>
    static void RollUp(List<GanttTask> all, Dictionary<string, List<GanttTask>> childrenOf, GanttCalendar calendar)
    {
        for (var i = all.Count - 1; i >= 0; i--)
        {
            var task = all[i];
            if (!childrenOf.TryGetValue(task.Id, out var children) || children.Count == 0)
                continue;

            var start = DateTimeOffset.MaxValue;
            var end = DateTimeOffset.MinValue;
            var weighted = 0d;
            var weight = 0d;
            var completed = 0d;

            foreach (var child in children)
            {
                var childEnd = child.Kind == GanttTaskKind.Milestone ? child.Start : child.End;

                if (child.Start < start)
                    start = child.Start;

                if (childEnd > end)
                    end = childEnd;

                // Weighting by working duration is what stops a two-hour task at 100% from dragging a
                // six-week task at 0% up to "half done".
                var w = calendar.WorkingTimeBetween(child.Start, childEnd).TotalMinutes;
                weight += w;
                weighted += child.Progress * w;
                completed += child.Progress;
            }

            if (start == DateTimeOffset.MaxValue)
                continue;

            task.Start = start;
            task.End = end;

            // All-milestone or all-zero-duration groups have no weight to average by; fall back to a
            // plain mean so the parent still shows something truthful.
            task.Progress = weight > 0d ? weighted / weight : completed / children.Count;
        }

        // `all` is pre-order, so iterating it backwards visits every child before its parent — which
        // is exactly the post-order the rollup needs, without building a second list.
    }


    static List<GanttRow> BuildRows(
        List<GanttTask> roots,
        Dictionary<string, List<GanttTask>> childrenOf,
        List<GanttTask> all
    )
    {
        var rows = new List<GanttRow>(all.Count);
        var stack = new Stack<GanttTask>();

        for (var i = roots.Count - 1; i >= 0; i--)
            stack.Push(roots[i]);

        while (stack.Count > 0)
        {
            var task = stack.Pop();
            var children = childrenOf.TryGetValue(task.Id, out var list) ? list : [];

            rows.Add(new GanttRow(task, rows.Count, task.Depth, children.Count > 0, task.IsExpanded));

            if (!task.IsExpanded)
                continue;

            for (var i = children.Count - 1; i >= 0; i--)
                stack.Push(children[i]);
        }
        return rows;
    }


    static List<GanttDependency> IndexDependencies(
        IEnumerable<GanttDependency>? source,
        Dictionary<string, GanttTask> byId,
        List<GanttValidationIssue> issues,
        out Dictionary<string, List<GanttDependency>> successorsOf,
        out Dictionary<string, List<GanttDependency>> predecessorsOf
    )
    {
        var deps = new List<GanttDependency>();
        successorsOf = new Dictionary<string, List<GanttDependency>>(StringComparer.Ordinal);
        predecessorsOf = new Dictionary<string, List<GanttDependency>>(StringComparer.Ordinal);

        if (source is null)
            return deps;

        foreach (var dep in source)
        {
            var hasPredecessor = byId.ContainsKey(dep.PredecessorId);
            var hasSuccessor = byId.ContainsKey(dep.SuccessorId);

            if (!hasPredecessor || !hasSuccessor)
            {
                issues.Add(new GanttValidationIssue(
                    GanttValidationCode.UnknownTaskReference,
                    $"Dependency {dep} names a task that is not in the plan; it was not drawn.",
                    hasPredecessor ? dep.PredecessorId : dep.SuccessorId,
                    hasPredecessor ? dep.SuccessorId : dep.PredecessorId
                ));
                dep.IsCritical = false;
                continue;
            }

            if (dep.PredecessorId == dep.SuccessorId)
            {
                issues.Add(new GanttValidationIssue(
                    GanttValidationCode.CircularDependency,
                    $"Task '{dep.PredecessorId}' depends on itself.",
                    dep.PredecessorId,
                    dep.SuccessorId
                ));
                dep.IsCritical = false;
                continue;
            }

            deps.Add(dep);
            Add(successorsOf, dep.PredecessorId, dep);
            Add(predecessorsOf, dep.SuccessorId, dep);
        }
        return deps;

        static void Add(Dictionary<string, List<GanttDependency>> index, string key, GanttDependency dep)
        {
            if (!index.TryGetValue(key, out var list))
                index[key] = list = [];

            list.Add(dep);
        }
    }


    /// <summary>
    /// Kahn's algorithm. Anything still holding an in-degree when the queue empties is on a cycle;
    /// those ids are reported and left out of the order, which is the signal every later pass uses
    /// to skip them rather than loop forever.
    /// </summary>
    static List<string> TopologicalSort(
        List<GanttTask> all,
        Dictionary<string, List<GanttDependency>> successorsOf,
        Dictionary<string, List<GanttDependency>> predecessorsOf,
        List<GanttValidationIssue> issues
    )
    {
        var inDegree = new Dictionary<string, int>(all.Count, StringComparer.Ordinal);
        foreach (var task in all)
            inDegree[task.Id] = predecessorsOf.TryGetValue(task.Id, out var preds) ? preds.Count : 0;

        var queue = new Queue<string>();
        foreach (var task in all)
        {
            if (inDegree[task.Id] == 0)
                queue.Enqueue(task.Id);
        }

        var order = new List<string>(all.Count);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            order.Add(id);

            foreach (var dep in successorsOf.TryGetValue(id, out var succs) ? succs : [])
            {
                if (--inDegree[dep.SuccessorId] == 0)
                    queue.Enqueue(dep.SuccessorId);
            }
        }

        if (order.Count != all.Count)
        {
            foreach (var (id, degree) in inDegree)
            {
                if (degree > 0)
                {
                    issues.Add(new GanttValidationIssue(
                        GanttValidationCode.CircularDependency,
                        $"Task '{id}' sits on a circular dependency; it was left out of scheduling and critical-path analysis.",
                        id
                    ));
                }
            }
        }
        return order;
    }


    /// <summary>
    /// A backward pass only. The forward pass a textbook CPM starts with derives earliest dates from
    /// durations — but a Gantt control is handed a plan that is <i>already scheduled</i>, and those
    /// dates are the answer, not something to recompute. What is still unknown is how much each task
    /// could slip before the finish moves, and that comes from walking the graph in reverse.
    /// </summary>
    static void ComputeCriticalPath(
        List<GanttTask> all,
        IReadOnlyList<string> topo,
        Dictionary<string, GanttTask> byId,
        Dictionary<string, List<GanttDependency>> successorsOf,
        List<GanttDependency> deps,
        Dictionary<string, DateTimeOffset> latestFinish,
        GanttModelOptions opts,
        DateTimeOffset projectFinish
    )
    {
        var calendar = opts.Calendar;

        foreach (var task in all)
        {
            task.IsCritical = false;
            task.TotalSlack = TimeSpan.Zero;
        }

        for (var i = topo.Count - 1; i >= 0; i--)
        {
            var task = byId[topo[i]];
            var duration = task.Kind == GanttTaskKind.Milestone
                ? TimeSpan.Zero
                : calendar.WorkingTimeBetween(task.Start, task.End);

            var lf = projectFinish;
            var successors = successorsOf.TryGetValue(task.Id, out var list) ? list : [];

            foreach (var dep in successors)
            {
                if (!latestFinish.TryGetValue(dep.SuccessorId, out var successorFinish))
                    continue; // on a cycle, so it contributes no bound

                var successor = byId[dep.SuccessorId];
                var successorDuration = successor.Kind == GanttTaskKind.Milestone
                    ? TimeSpan.Zero
                    : calendar.WorkingTimeBetween(successor.Start, successor.End);

                var successorStart = calendar.SubtractWorkingTime(successorFinish, successorDuration);

                var bound = dep.Type switch
                {
                    // The successor cannot start until we finish: our finish is bounded by its start.
                    GanttDependencyType.FinishToStart => Shift(successorStart, -dep.Lag),

                    // Bounds our *start*; convert to a finish by adding our own duration back on.
                    GanttDependencyType.StartToStart =>
                        calendar.AddWorkingTime(Shift(successorStart, -dep.Lag), duration),

                    GanttDependencyType.FinishToFinish => Shift(successorFinish, -dep.Lag),

                    GanttDependencyType.StartToFinish =>
                        calendar.AddWorkingTime(Shift(successorFinish, -dep.Lag), duration),

                    _ => lf
                };

                if (bound < lf)
                    lf = bound;
            }

            latestFinish[task.Id] = lf;

            var actualFinish = task.Kind == GanttTaskKind.Milestone ? task.Start : task.End;
            var slack = calendar.WorkingTimeBetween(actualFinish, lf);

            task.TotalSlack = slack;
            task.IsCritical = slack <= opts.CriticalSlackThreshold;
        }

        // A link is critical when it is the reason its successor cannot move — both ends critical.
        // Highlighting a link whose successor merely happens to be critical would draw a red arrow
        // across slack that genuinely exists.
        foreach (var dep in deps)
        {
            dep.IsCritical =
                byId.TryGetValue(dep.PredecessorId, out var p) && p.IsCritical &&
                byId.TryGetValue(dep.SuccessorId, out var s) && s.IsCritical;
        }

        DateTimeOffset Shift(DateTimeOffset value, TimeSpan by) =>
            by >= TimeSpan.Zero ? calendar.AddWorkingTime(value, by) : calendar.SubtractWorkingTime(value, -by);
    }


    static void ClearCriticalPath(List<GanttTask> all, List<GanttDependency> deps)
    {
        foreach (var task in all)
        {
            task.IsCritical = false;
            task.TotalSlack = TimeSpan.Zero;
        }
        foreach (var dep in deps)
            dep.IsCritical = false;
    }


    static void ReportDeadlines(List<GanttTask> all, List<GanttValidationIssue> issues)
    {
        foreach (var task in all)
        {
            if (task.IsOverdue)
            {
                issues.Add(new GanttValidationIssue(
                    GanttValidationCode.DeadlineExceeded,
                    $"Task '{task.Name}' finishes after its deadline of {task.Deadline:d}.",
                    task.Id
                ));
            }
        }
    }


    static (DateTimeOffset Start, DateTimeOffset End) Extent(List<GanttTask> all)
    {
        if (all.Count == 0)
        {
            // DateTime.Today is Kind.Local, so pairing it with an explicit offset throws. The
            // single-argument overload takes the machine's own offset, which is the right default
            // for an unbound control anyway.
            var epoch = new DateTimeOffset(DateTime.Today);
            return (epoch, epoch);
        }

        var start = DateTimeOffset.MaxValue;
        var end = DateTimeOffset.MinValue;

        foreach (var task in all)
        {
            var taskEnd = task.Kind == GanttTaskKind.Milestone ? task.Start : task.End;

            if (task.Start < start)
                start = task.Start;

            if (taskEnd > end)
                end = taskEnd;

            if (task.BaselineStart is { } bs && bs < start)
                start = bs;

            if (task.BaselineEnd is { } be && be > end)
                end = be;

            if (task.Deadline is { } dl && dl > end)
                end = dl;
        }
        return end < start ? (start, start) : (start, end);
    }
}
