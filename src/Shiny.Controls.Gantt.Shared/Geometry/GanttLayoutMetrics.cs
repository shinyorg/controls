namespace Shiny.Controls.Gantt;

/// <summary>Vertical sizing for the timeline, kept together so the two hosts cannot disagree about it.</summary>
/// <param name="RowHeight">Full height of a row, including the gutter above and below the bar.</param>
/// <param name="BarHeight">Height of an ordinary task bar.</param>
/// <param name="SummaryHeight">Height of a summary bracket, usually shorter than a bar.</param>
/// <param name="MilestoneSize">Diagonal of the milestone diamond.</param>
/// <param name="BaselineHeight">Height of the thin baseline bar drawn beneath the live one.</param>
public readonly record struct GanttRowMetrics(
    double RowHeight = 32,
    double BarHeight = 18,
    double SummaryHeight = 10,
    double MilestoneSize = 14,
    double BaselineHeight = 4
);


/// <summary>
/// Everything positional about one rendered frame of a plan: where each bar sits, where its progress
/// fill ends, where a connector handle goes, and the polyline every dependency arrow follows.
/// </summary>
/// <remarks>
/// <para>
/// This is the piece that most obviously had to be shared. A MAUI <c>AbsoluteLayout</c> and a CSS
/// <c>position:absolute</c> want exactly the same rectangle, and a dependency arrow that routes one
/// way on a phone and another way in a browser is a bug nobody would ever find by reading either
/// host's code. Computing it once also makes it testable without a UI at all.
/// </para>
/// <para>
/// Instances are cheap and disposable — build one per layout pass rather than mutating one.
/// </para>
/// </remarks>
public sealed class GanttLayoutMetrics
{
    /// <summary>How far an arrow travels straight out of a bar before it is allowed to turn.</summary>
    public const double RouteStub = 12d;

    readonly Dictionary<string, int> rowIndexById;

    /// <summary>Builds the geometry for one layout pass.</summary>
    /// <param name="model">The plan being laid out.</param>
    /// <param name="origin">The instant sitting at x = 0.</param>
    /// <param name="pixelsPerDay">Horizontal zoom. Values at or below zero are clamped to 1.</param>
    /// <param name="rows">Row heights and bar insets. The default is used when omitted.</param>
    public GanttLayoutMetrics(
        GanttModel model,
        DateTimeOffset origin,
        double pixelsPerDay,
        GanttRowMetrics rows = default
    )
    {
        this.Model = model;
        this.Origin = origin;
        this.PixelsPerDay = pixelsPerDay <= 0 ? 1 : pixelsPerDay;
        this.Rows = rows == default ? new GanttRowMetrics() : rows;

        this.rowIndexById = new Dictionary<string, int>(model.Rows.Count, StringComparer.Ordinal);
        foreach (var row in model.Rows)
            this.rowIndexById[row.Task.Id] = row.Index;
    }

    /// <summary>The plan this geometry was built for.</summary>
    public GanttModel Model { get; }

    /// <summary>The instant at x = 0.</summary>
    public DateTimeOffset Origin { get; }

    /// <summary>Horizontal zoom, in pixels per day. Always greater than zero.</summary>
    public double PixelsPerDay { get; }

    /// <summary>Row heights and bar insets.</summary>
    public GanttRowMetrics Rows { get; }

    /// <summary>Total height of all visible rows.</summary>
    public double ContentHeight => this.Model.Rows.Count * this.Rows.RowHeight;


    /// <summary>Horizontal position of an instant.</summary>
    public double XOf(DateTimeOffset date) => GanttGeometry.DateToX(date, this.Origin, this.PixelsPerDay);

    /// <summary>The instant at a horizontal position.</summary>
    public DateTimeOffset DateAt(double x) => GanttGeometry.XToDate(x, this.Origin, this.PixelsPerDay);

    /// <summary>Top edge of a row.</summary>
    public double RowTop(int rowIndex) => rowIndex * this.Rows.RowHeight;

    /// <summary>Vertical centre of a row.</summary>
    public double RowCenter(int rowIndex) => (rowIndex * this.Rows.RowHeight) + (this.Rows.RowHeight / 2);

    /// <summary>The row a vertical position falls on, or -1 when it is past the last one.</summary>
    public int RowAt(double y)
    {
        if (y < 0 || this.Rows.RowHeight <= 0)
            return -1;

        var index = (int)(y / this.Rows.RowHeight);
        return index >= this.Model.Rows.Count ? -1 : index;
    }

    /// <summary>The visible row index of a task, or -1 when it is hidden inside a collapsed summary.</summary>
    public int RowIndexOf(string taskId) => this.rowIndexById.GetValueOrDefault(taskId, -1);


    /// <summary>
    /// The rectangle a task's bar occupies. Milestones get a square centred on their date — the host
    /// draws the diamond by rotating within it — and summaries a shorter bracket.
    /// </summary>
    public GanttRect BarOf(GanttTask task)
    {
        var row = this.RowIndexOf(task.Id);
        return row < 0 ? default : this.BarOf(task, row);
    }

    /// <summary>
    /// The rectangle a task's bar occupies on a row the caller has already resolved — the overload to
    /// use when walking rows in order, since it skips the id lookup.
    /// </summary>
    /// <param name="task">The task to measure.</param>
    /// <param name="rowIndex">The visible row the task sits on.</param>
    public GanttRect BarOf(GanttTask task, int rowIndex)
    {
        var center = this.RowCenter(rowIndex);

        if (task.Kind == GanttTaskKind.Milestone)
        {
            var size = this.Rows.MilestoneSize;
            return new GanttRect(this.XOf(task.Start) - (size / 2), center - (size / 2), size, size);
        }

        var height = task.IsRollup ? this.Rows.SummaryHeight : this.Rows.BarHeight;
        var x = this.XOf(task.Start);
        var width = GanttGeometry.SpanToWidth(task.Start, task.End, this.PixelsPerDay);

        // A task whose duration rounds below a pixel would otherwise vanish. One device-independent
        // pixel of width is the difference between "this task is very short" and "this task is gone".
        return new GanttRect(x, center - (height / 2), Math.Max(width, 1d), height);
    }


    /// <summary>The filled portion of a bar. Empty for a milestone, which has no width to fill.</summary>
    public GanttRect ProgressOf(GanttTask task)
    {
        if (task.Kind == GanttTaskKind.Milestone || task.Progress <= 0)
            return default;

        var bar = this.BarOf(task);
        return bar.IsEmpty ? default : bar with { Width = bar.Width * Math.Clamp(task.Progress, 0, 1) };
    }


    /// <summary>The thin baseline bar drawn under the live one, when the task carries a baseline.</summary>
    public GanttRect BaselineOf(GanttTask task)
    {
        if (task.BaselineStart is not { } start || task.BaselineEnd is not { } end)
            return default;

        var row = this.RowIndexOf(task.Id);
        if (row < 0)
            return default;

        var bar = this.BarOf(task, row);
        var height = this.Rows.BaselineHeight;

        // Sits below the live bar rather than behind it: overlapping the two makes a task that is
        // exactly on plan look like it has no baseline at all.
        return new GanttRect(
            this.XOf(start),
            bar.Bottom + 1,
            Math.Max(GanttGeometry.SpanToWidth(start, end, this.PixelsPerDay), 1d),
            height
        );
    }


    /// <summary>The two dots a drag-to-link gesture starts from, at the middle of each end of a bar.</summary>
    public (GanttPoint Start, GanttPoint End) ConnectorsOf(GanttTask task)
    {
        var bar = this.BarOf(task);
        return (new GanttPoint(bar.X, bar.CenterY), new GanttPoint(bar.Right, bar.CenterY));
    }


    /// <summary>
    /// The polyline an arrow follows, from the predecessor's edge to the successor's, or an empty
    /// array when either end is hidden inside a collapsed summary.
    /// </summary>
    /// <remarks>
    /// Two shapes, chosen by whether there is room. When the target is far enough past the source the
    /// arrow makes a single vertical jog — three segments, the shape a reader expects. When the target
    /// is behind or beside the source, going straight at it would draw the line back through the
    /// predecessor's own bar, so it detours through the gutter between the rows instead: out, across,
    /// and back in. That second case is not an edge case in real plans, it is what every
    /// finish-to-start link between two overlapping tasks looks like.
    /// </remarks>
    public IReadOnlyList<GanttPoint> RouteOf(GanttDependency dependency)
    {
        if (!this.Model.TryGetTask(dependency.PredecessorId, out var predecessor) ||
            !this.Model.TryGetTask(dependency.SuccessorId, out var successor))
        {
            return [];
        }

        var predecessorRow = this.RowIndexOf(predecessor.Id);
        var successorRow = this.RowIndexOf(successor.Id);

        if (predecessorRow < 0 || successorRow < 0)
            return [];

        var from = this.BarOf(predecessor, predecessorRow);
        var to = this.BarOf(successor, successorRow);

        // Which edge each end of the link attaches to. "Start" links leave and arrive at the left
        // edge, "finish" links at the right.
        var leavesFromFinish = dependency.Type is GanttDependencyType.FinishToStart or GanttDependencyType.FinishToFinish;
        var arrivesAtFinish = dependency.Type is GanttDependencyType.FinishToFinish or GanttDependencyType.StartToFinish;

        var source = new GanttPoint(leavesFromFinish ? from.Right : from.X, from.CenterY);
        var target = new GanttPoint(arrivesAtFinish ? to.Right : to.X, to.CenterY);

        var outward = leavesFromFinish ? 1 : -1;
        var approach = arrivesAtFinish ? 1 : -1;

        var exitX = source.X + (outward * RouteStub);
        var entryX = target.X + (approach * RouteStub);

        var direct = outward > 0 ? entryX >= exitX : entryX <= exitX;

        if (direct)
        {
            return
            [
                source,
                new GanttPoint(entryX, source.Y),
                new GanttPoint(entryX, target.Y),
                target
            ];
        }

        // Drop into the gutter between the two rows. Same row means the two bars overlap, so the
        // detour goes below the source's own row.
        var direction = successorRow >= predecessorRow ? 1 : -1;
        var gutter = source.Y + (direction * this.Rows.RowHeight / 2);

        return
        [
            source,
            new GanttPoint(exitX, source.Y),
            new GanttPoint(exitX, gutter),
            new GanttPoint(entryX, gutter),
            new GanttPoint(entryX, target.Y),
            target
        ];
    }


    /// <summary>
    /// Which part of a bar a point is over, and therefore what dragging from it would do. Returns
    /// <c>null</c> when the point is not on the task at all.
    /// </summary>
    /// <param name="task">The task to test.</param>
    /// <param name="x">Timeline-space horizontal position.</param>
    /// <param name="y">Timeline-space vertical position.</param>
    /// <param name="edgeGrip">Width of the resize grips at each end.</param>
    /// <param name="touchSlop">How far outside the bar still counts as a hit — a finger, not a mouse.</param>
    public GanttHitTarget? HitTest(GanttTask task, double x, double y, double edgeGrip = 8, double touchSlop = 4)
    {
        var bar = this.BarOf(task);
        if (bar.IsEmpty || !bar.Inflate(touchSlop).Contains(x, y))
            return null;

        if (task.Kind == GanttTaskKind.Milestone)
            return GanttHitTarget.Body;

        // The grips must not eat the whole bar. Below three grips' worth of width, a short task is
        // all body — resizing it is what zoom is for, and a bar you cannot drag at all is worse.
        if (bar.Width >= edgeGrip * 3)
        {
            if (x <= bar.X + edgeGrip)
                return GanttHitTarget.StartEdge;

            if (x >= bar.Right - edgeGrip)
                return GanttHitTarget.EndEdge;
        }

        if (task.Progress > 0 && Math.Abs(x - (bar.X + (bar.Width * task.Progress))) <= edgeGrip / 2)
            return GanttHitTarget.Progress;

        return GanttHitTarget.Body;
    }
}


/// <summary>What a point on a bar would drag. Mirrors the host-side enums, which add their own members.</summary>
public enum GanttHitTarget
{
    /// <summary>The middle of the bar — a drag here moves the whole task.</summary>
    Body,

    /// <summary>The leading edge — a drag here moves the start and holds the finish.</summary>
    StartEdge,

    /// <summary>The trailing edge — a drag here moves the finish and holds the start.</summary>
    EndEdge,

    /// <summary>The progress handle.</summary>
    Progress
}
