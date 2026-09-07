using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Shiny.Controls.Gantt;

namespace Shiny.Blazor.Controls.Gantt;

public partial class GanttView
{
    /// <summary>How close a press must be to a connector dot to start drawing a link.</summary>
    const double ConnectorRadius = 12;


    // =============================================================================================
    // Hit testing
    // =============================================================================================

    /// <summary>
    /// What is under a point in timeline space: a part of a bar, or one of its connector dots.
    /// </summary>
    /// <remarks>
    /// The row index falls straight out of the y coordinate, so this never walks the plan — a
    /// component that tested every bar would get slower with every task the user added.
    /// </remarks>
    internal (GanttTask Task, GanttDragTarget Target)? FindHit(double x, double y)
    {
        if (this.metrics is null)
            return null;

        var rowIndex = this.metrics.RowAt(y);
        if (rowIndex < 0)
            return null;

        var task = this.Model.Rows[rowIndex].Task;

        if (this.metrics.HitTest(task, x, y) is { } target)
        {
            return (task, target switch
            {
                GanttHitTarget.StartEdge => GanttDragTarget.StartEdge,
                GanttHitTarget.EndEdge => GanttDragTarget.EndEdge,
                GanttHitTarget.Progress => GanttDragTarget.Progress,
                _ => GanttDragTarget.Body
            });
        }

        // Connectors sit just outside the bar, so they are only reached once the bar itself has
        // declined the hit. That ordering is what stops them stealing the resize grips.
        if (this.AllowDependencyEdit && !this.IsReadOnly && this.IsSelected(task))
        {
            var (start, end) = this.metrics.ConnectorsOf(task);

            if (Near(x, y, end))
            {
                this.dragFromStartConnector = false;
                return (task, GanttDragTarget.Connector);
            }

            if (Near(x, y, start))
            {
                this.dragFromStartConnector = true;
                return (task, GanttDragTarget.Connector);
            }
        }
        return null;

        static bool Near(double x, double y, GanttPoint point) =>
            Math.Abs(x - point.X) <= ConnectorRadius && Math.Abs(y - point.Y) <= ConnectorRadius;
    }


    // =============================================================================================
    // Pointer
    // =============================================================================================

    async Task OnCanvasPointerDown(PointerEventArgs e)
    {
        // Must come first: every coordinate below is measured against it.
        if (this.module is not null)
            this.canvasOrigin = await this.module.InvokeAsync<GanttOrigin>("origin", this.canvasElement);

        var (x, y) = this.PointOf(e);
        var hit = this.FindHit(x, y);
        if (hit is null)
            return;

        this.SelectTask(hit.Value.Task);

        if (this.IsReadOnly)
            return;

        this.dragTask = hit.Value.Task;
        this.dragTarget = hit.Value.Target;
        this.dragOriginX = x;
        this.dragOriginalStart = this.dragTask.Start;
        this.dragOriginalEnd = this.dragTask.End;
        this.dragOriginalProgress = this.dragTask.Progress;
        this.activePointer = e.PointerId;

        if (this.dragTarget == GanttDragTarget.Connector && this.metrics is not null)
        {
            var (start, end) = this.metrics.ConnectorsOf(this.dragTask);
            this.linkSource = this.dragTask;
            this.linkFrom = this.dragFromStartConnector ? start : end;
            this.linkTo = new GanttPoint(x, y);
        }

        // Without capture, dragging past the edge of the canvas drops the gesture and the bar freezes
        // mid-drag with no pointerup ever arriving.
        if (this.module is not null)
            await this.module.InvokeVoidAsync("capture", this.canvasElement, e.PointerId);
    }


    void OnCanvasPointerMove(PointerEventArgs e)
    {
        if (this.dragTask is null || e.PointerId != this.activePointer)
            return;

        var (x, y) = this.PointOf(e);

        if (this.dragTarget == GanttDragTarget.Connector)
        {
            this.linkTo = new GanttPoint(x, y);
            this.Refresh();
            return;
        }

        // Each frame starts from the original dates, not from the last preview — accumulating deltas
        // makes a snapped drag creep, because every frame re-snaps a value that was already snapped.
        this.preview?.Revert();
        this.preview = null;

        var plan = this.BuildDragPlan(this.dragTask, this.dragTarget, x - this.dragOriginX);
        if (plan is null)
            return;

        // Applied immediately so the bars follow the pointer. Reverted before the next frame and once
        // more before the change callback, so a handler that cancels never sees a mutated task.
        plan.Apply();
        this.preview = plan;

        this.BuildBars();
        this.Refresh();
    }


    async Task OnCanvasPointerUp(PointerEventArgs e)
    {
        if (this.dragTask is null)
            return;

        if (this.module is not null)
            await this.module.InvokeVoidAsync("release", this.canvasElement, e.PointerId);

        await this.CommitDrag(this.PointOf(e).X, cancelled: false);
    }


    async Task OnCanvasPointerCancel(PointerEventArgs e) =>
        await this.CommitDrag(this.PointOf(e).X, cancelled: true);


    /// <summary>
    /// A pointer event in timeline coordinates.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>OffsetX</c>/<c>OffsetY</c>. Those are relative to the event's <i>target</i>,
    /// and a press on a bar targets the bar, not the canvas the handler is bound to — so a drag
    /// started on a bar would hit-test in the bar's own coordinate space and, in practice, do nothing
    /// at all. Blazor does not expose <c>event.target</c>, so the honest fix is to work from client
    /// space and subtract the canvas origin measured when the gesture began.
    /// </remarks>
    (double X, double Y) PointOf(MouseEventArgs e) =>
        (e.ClientX - this.canvasOrigin.Left, e.ClientY - this.canvasOrigin.Top);


    async Task OnCanvasDoubleClick(MouseEventArgs e)
    {
        if (this.module is not null)
            this.canvasOrigin = await this.module.InvokeAsync<GanttOrigin>("origin", this.canvasElement);

        var (x, y) = this.PointOf(e);
        var hit = this.FindHit(x, y);
        if (hit is not null && this.OnTaskDoubleClick.HasDelegate)
            await this.OnTaskDoubleClick.InvokeAsync(hit.Value.Task);
    }


    /// <summary>
    /// The plan a drag of <paramref name="dx"/> pixels would produce.
    /// </summary>
    /// <remarks>
    /// Takes the task and target as arguments rather than reading the drag fields, because the commit
    /// path has to clear those <i>before</i> it builds the final plan — clearing them first is what
    /// stops a re-entrant pointer event restarting the gesture. Reading them here instead produced a
    /// drag that tracked the pointer perfectly and then snapped back on release, since the committed
    /// plan was always null.
    /// </remarks>
    GanttSchedulePlan? BuildDragPlan(GanttTask? task, GanttDragTarget target, double dx)
    {
        if (task is null || this.metrics is null)
            return null;

        var options = this.BuildScheduleOptions();

        if (target == GanttDragTarget.Progress)
        {
            if (!this.AllowProgressChange || !task.CanChangeProgress)
                return null;

            var bar = this.metrics.BarOf(task);
            if (bar.Width <= 0)
                return null;

            return GanttScheduler.SetProgress(task, ((bar.Width * this.dragOriginalProgress) + dx) / bar.Width);
        }

        var delta = GanttGeometry.XToSpan(dx, this.EffectivePixelsPerDay);

        return target switch
        {
            GanttDragTarget.Body when this.AllowMove =>
                GanttScheduler.Move(this.Model, task, this.Snap(this.dragOriginalStart + delta), options),

            GanttDragTarget.StartEdge when this.AllowResize =>
                GanttScheduler.Resize(this.Model, task, this.Snap(this.dragOriginalStart + delta), this.dragOriginalEnd, options),

            GanttDragTarget.EndEdge when this.AllowResize =>
                GanttScheduler.Resize(this.Model, task, this.dragOriginalStart, this.Snap(this.dragOriginalEnd + delta), options),

            _ => null
        };
    }


    async Task CommitDrag(double x, bool cancelled)
    {
        var task = this.dragTask;
        var target = this.dragTarget;

        this.dragTask = null;
        this.dragTarget = GanttDragTarget.None;
        this.activePointer = -1;

        // Put everything back before anyone is told about it. The preview existed to move pixels; the
        // authoritative change is the one built here and passed through OnTaskChanging.
        this.preview?.Revert();
        this.preview = null;

        if (task is null || cancelled)
        {
            this.linkSource = null;
            this.Rebuild();
            this.Refresh();
            return;
        }

        if (target == GanttDragTarget.Connector)
        {
            await this.CommitLink(task);
            return;
        }

        var plan = this.BuildDragPlan(task, target, x - this.dragOriginX);

        if (plan is null || !plan.IsValid || !plan.HasChanges)
        {
            this.Rebuild();
            this.Refresh();
            return;
        }

        if (this.OnTaskChanging.HasDelegate)
        {
            var changing = new GanttTaskChangingArgs(plan);
            await this.OnTaskChanging.InvokeAsync(changing);

            if (changing.Cancel)
            {
                this.Rebuild();
                this.Refresh();
                return;
            }
        }

        plan.Apply();
        this.Rebuild();
        this.Refresh();

        if (this.OnTaskChanged.HasDelegate)
            await this.OnTaskChanged.InvokeAsync(new GanttTaskChangedArgs(plan));
    }


    async Task CommitLink(GanttTask source)
    {
        var hit = this.FindHitIncludingUnselected(this.linkTo.X, this.linkTo.Y);
        this.linkSource = null;

        if (hit is null || ReferenceEquals(hit.Value.Task, source) || this.Dependencies is not IList<GanttDependency> list)
        {
            this.Rebuild();
            this.Refresh();
            return;
        }

        // Which edges the link joins falls out of which dot it was dragged from and which end it was
        // dropped on — the user draws the shape rather than picking a type from a menu.
        var droppedOnStart = hit.Value.Target != GanttDragTarget.EndEdge;

        var type = (this.dragFromStartConnector, droppedOnStart) switch
        {
            (false, true) => GanttDependencyType.FinishToStart,
            (true, true) => GanttDependencyType.StartToStart,
            (false, false) => GanttDependencyType.FinishToFinish,
            (true, false) => GanttDependencyType.StartToFinish
        };

        var dependency = new GanttDependency(source.Id, hit.Value.Task.Id, type);
        var plan = GanttScheduler.AddDependency(this.Model, dependency, this.BuildScheduleOptions());

        if (!plan.IsValid)
        {
            this.Rebuild();
            this.Refresh();
            return;
        }

        if (this.OnDependencyCreated.HasDelegate)
        {
            var args = new GanttDependencyArgs(dependency);
            await this.OnDependencyCreated.InvokeAsync(args);

            if (args.Cancel)
            {
                this.Rebuild();
                this.Refresh();
                return;
            }
        }

        list.Add(dependency);
        plan.Apply();
        this.Rebuild();
        this.Refresh();
    }


    /// <summary>
    /// A link is dropped on whatever bar is under the pointer, whether or not it happens to be
    /// selected — <see cref="FindHit"/> only offers connectors on the selected task, which is right
    /// for starting a drag and wrong for finishing one.
    /// </summary>
    (GanttTask Task, GanttDragTarget Target)? FindHitIncludingUnselected(double x, double y)
    {
        if (this.metrics is null)
            return null;

        var rowIndex = this.metrics.RowAt(y);
        if (rowIndex < 0)
            return null;

        var task = this.Model.Rows[rowIndex].Task;

        return this.metrics.HitTest(task, x, y, touchSlop: ConnectorRadius) is { } target
            ? (task, target == GanttHitTarget.EndEdge ? GanttDragTarget.EndEdge : GanttDragTarget.Body)
            : null;
    }


    /// <summary>Removes a link and cascades the result. Returns whether it was there to remove.</summary>
    public async Task<bool> RemoveDependency(GanttDependency dependency)
    {
        if (this.Dependencies is not IList<GanttDependency> list || !list.Contains(dependency))
            return false;

        if (this.OnDependencyRemoved.HasDelegate)
        {
            var args = new GanttDependencyArgs(dependency);
            await this.OnDependencyRemoved.InvokeAsync(args);

            if (args.Cancel)
                return false;
        }

        list.Remove(dependency);
        this.Rebuild();
        this.Refresh();
        return true;
    }


    internal GanttScheduleOptions BuildScheduleOptions() => new()
    {
        Calendar = this.Calendar ?? GanttCalendar.Continuous,
        Cascade = this.CascadeMode,
        MinimumDuration = this.MinimumDuration,
        SnapToWorkingTime = this.SnapMode == GanttSnapMode.WorkingTime,
        PreserveWorkingDuration = this.Calendar is not null,
        MinDate = this.StartDate,
        MaxDate = this.EndDate
    };


    internal DateTimeOffset Snap(DateTimeOffset value) =>
        GanttGeometry.Snap(value, this.SnapMode, this.EffectiveScale, this.SnapInterval, this.Calendar);


    // =============================================================================================
    // Selection and expansion
    // =============================================================================================

    internal async Task HandleRowClick(GanttTask task)
    {
        this.SelectTask(task);

        if (this.OnTaskClick.HasDelegate)
            await this.OnTaskClick.InvokeAsync(task);
    }


    /// <summary>Selects a task, honouring <see cref="SelectionMode"/>. Passing null clears the selection.</summary>
    public void SelectTask(GanttTask? task)
    {
        switch (this.SelectionMode)
        {
            case GanttSelectionMode.None:
                return;

            case GanttSelectionMode.Multiple when task is not null:
                if (!this.SelectedTasks.Remove(task))
                    this.SelectedTasks.Add(task);

                this.SelectedTask = this.SelectedTasks.Count > 0 ? this.SelectedTasks[^1] : null;
                break;

            default:
                this.SelectedTask = task;
                this.SelectedTasks.Clear();

                if (task is not null)
                    this.SelectedTasks.Add(task);

                break;
        }

        _ = this.SelectedTaskChanged.InvokeAsync(this.SelectedTask);
        _ = this.OnSelectionChanged.InvokeAsync();
        this.Refresh();
    }


    /// <summary>Expands or collapses a summary.</summary>
    public void ToggleExpand(GanttTask task)
    {
        task.IsExpanded = !task.IsExpanded;
        this.Rebuild();
        this.Refresh();
    }

    /// <summary>Expands every summary in the plan.</summary>
    public void ExpandAll() => this.SetExpansion(true);

    /// <summary>Collapses every summary in the plan.</summary>
    public void CollapseAll() => this.SetExpansion(false);

    void SetExpansion(bool expanded)
    {
        foreach (var task in this.Model.AllTasks)
            task.IsExpanded = expanded;

        this.Rebuild();
        this.Refresh();
    }


    // =============================================================================================
    // Zoom and scroll
    // =============================================================================================

    /// <summary>Called from the JS wheel handler when the user holds ctrl or cmd.</summary>
    [JSInvokable]
    public async Task OnWheelZoom(double deltaY, double anchorX)
    {
        if (!this.AllowZoom)
            return;

        // One notch is one step, whatever the browser reports for delta — trackpads emit a stream of
        // small deltas and a mouse wheel one big one, and scaling by the raw value makes the two feel
        // like different features.
        await this.SetZoom(this.EffectivePixelsPerDay * (deltaY < 0 ? 1.2 : 1 / 1.2), anchorX);
    }


    /// <summary>Zooms in one step, about the middle of the viewport.</summary>
    public async Task ZoomIn() => await this.ZoomAboutCentre(1.6);

    /// <summary>Zooms out one step, about the middle of the viewport.</summary>
    public async Task ZoomOut() => await this.ZoomAboutCentre(1 / 1.6);

    async Task ZoomAboutCentre(double factor)
    {
        var viewport = await this.Viewport();
        await this.SetZoom(this.EffectivePixelsPerDay * factor, viewport.ScrollLeft + (viewport.Width / 2));
    }


    /// <summary>Sets the zoom, keeping the instant at <paramref name="anchorX"/> under that position.</summary>
    public async Task SetZoom(double pixelsPerDay, double anchorX)
    {
        if (!this.AllowZoom)
            return;

        var clamped = Math.Clamp(pixelsPerDay, GanttGeometry.MinPixelsPerDay, GanttGeometry.MaxPixelsPerDay);
        if (Math.Abs(clamped - this.EffectivePixelsPerDay) < 0.0001)
            return;

        var anchorDate = GanttGeometry.XToDate(anchorX, this.RangeStart, this.EffectivePixelsPerDay);
        var viewport = await this.Viewport();
        var anchorOffset = anchorX - viewport.ScrollLeft;

        this.PixelsPerDay = clamped;
        this.Rebuild();
        this.Refresh();

        var target = GanttGeometry.DateToX(anchorDate, this.RangeStart, clamped) - anchorOffset;
        await this.ScrollTo(Math.Max(0, target), null, false);
    }


    /// <summary>Picks the zoom that fits the whole plan in the visible width.</summary>
    public async Task ZoomToFit()
    {
        var viewport = await this.Viewport();
        if (viewport.Width <= 0)
            return;

        this.PixelsPerDay = GanttGeometry.FitToWidth(this.RangeStart, this.RangeEnd, viewport.Width);
        this.Rebuild();
        this.Refresh();

        await this.ScrollTo(0, null, false);
    }


    /// <summary>Scrolls so that a date is at the left edge of the timeline.</summary>
    public Task ScrollToDate(DateTimeOffset date, bool smooth = true) =>
        this.ScrollTo(Math.Max(0, this.XOf(date)), null, smooth);


    /// <summary>
    /// Scrolls a task into view both ways, expanding whatever it is hidden inside first — scrolling
    /// to a row that a collapsed parent is hiding would silently do nothing.
    /// </summary>
    public async Task ScrollToTask(GanttTask task, bool smooth = true)
    {
        var changed = false;
        foreach (var ancestor in task.Ancestors())
        {
            if (!ancestor.IsExpanded)
            {
                ancestor.IsExpanded = true;
                changed = true;
            }
        }

        if (changed)
        {
            this.Rebuild();
            this.Refresh();
        }

        if (this.metrics is null)
            return;

        var row = this.metrics.RowIndexOf(task.Id);
        if (row < 0)
            return;

        await this.ScrollTo(
            Math.Max(0, this.XOf(task.Start) - 40),
            Math.Max(0, this.metrics.RowTop(row) - this.RowHeight),
            smooth
        );
    }


    async Task ScrollTo(double x, double? y, bool smooth)
    {
        if (this.module is null)
            return;

        await this.module.InvokeVoidAsync("scrollTo", this.scrollElement, x, y, smooth);
    }


    async Task<GanttViewport> Viewport()
    {
        if (this.module is null)
            return new GanttViewport();

        return await this.module.InvokeAsync<GanttViewport>("viewport", this.scrollElement);
    }


    // =============================================================================================
    // Splitter
    // =============================================================================================

    void OnSplitterDown(PointerEventArgs e)
    {
        if (!this.IsTaskPaneResizable || !this.ShowTaskPane)
            return;

        this.splitterActive = true;
        this.splitterOriginX = e.ClientX;
        this.splitterOriginWidth = this.TaskPaneWidth;
    }


    void OnSplitterMove(PointerEventArgs e)
    {
        if (!this.splitterActive)
            return;

        this.TaskPaneWidth = Math.Clamp(this.splitterOriginWidth + (e.ClientX - this.splitterOriginX), 80, 900);
        _ = this.TaskPaneWidthChanged.InvokeAsync(this.TaskPaneWidth);
        this.Refresh();
    }


    void OnSplitterUp(PointerEventArgs e) => this.splitterActive = false;
}


/// <summary>
/// The canvas's position in client space, returned from JS. A named type for the same reason
/// <see cref="GanttViewport"/> is one.
/// </summary>
public class GanttOrigin
{
    public double Left { get; set; }
    public double Top { get; set; }
}


/// <summary>
/// The scroller's measurements, returned from JS.
/// </summary>
/// <remarks>
/// A named type with settable properties, not an anonymous one and not a positional record: an
/// anonymous type over interop fails on trimmed WebAssembly with ConstructorContainsNullParameterNames,
/// and the failure only ever shows up in a published build.
/// </remarks>
public class GanttViewport
{
    public double Width { get; set; }
    public double Height { get; set; }
    public double ScrollLeft { get; set; }
    public double ScrollTop { get; set; }
}
