using System.Collections;
using Shiny.Controls.Gantt;
using Shiny.Maui.Controls.Gantt.Internal;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Gantt;

public partial class GanttView
{
    /// <summary>How close a press must be to a connector dot to start drawing a link.</summary>
    const double ConnectorRadius = 12;

    DragTouchHook? touchHook;
    Point pressPoint;
    GanttTask? dragTask;
    GanttDragTarget dragTarget = GanttDragTarget.None;
    bool dragFromStartConnector;
    DateTimeOffset dragOriginalStart;
    DateTimeOffset dragOriginalEnd;
    double dragOriginalProgress;
    GanttSchedulePlan? preview;
    double lastDragDx;
    double pinchStartPixelsPerDay;

    /// <summary>True while a bar is being dragged. Suppresses the rebuild a date change would trigger.</summary>
    internal bool IsDragging => this.dragTask is not null;

    /// <summary>The task a link is currently being drawn from, for the drawable to render the rubber band.</summary>
    internal GanttTask? LinkSource => this.dragTarget == GanttDragTarget.Connector ? this.dragTask : null;

    /// <summary>Where the link rubber band currently ends.</summary>
    internal GanttPoint LinkTarget { get; private set; }


    void WireGestures()
    {
        var pointer = new PointerGestureRecognizer();
        pointer.PointerPressed += this.OnPointerPressed;
        pointer.PointerMoved += this.OnPointerMoved;
        this.timelineContent.GestureRecognizers.Add(pointer);

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += this.OnTimelinePan;
        this.timelineContent.GestureRecognizers.Add(pan);

        this.timelineContent.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(this.CommitTap)
        });

        this.timelineContent.GestureRecognizers.Add(new TapGestureRecognizer
        {
            NumberOfTapsRequired = 2,
            Command = new Command(this.CommitDoubleTap)
        });

        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += this.OnPinch;
        this.timelineContent.GestureRecognizers.Add(pinch);

        var splitterPan = new PanGestureRecognizer();
        splitterPan.PanUpdated += this.OnSplitterPan;
        this.splitter.GestureRecognizers.Add(splitterPan);

        this.touchHook = new DragTouchHook(this.timelineContent);
    }


    // =============================================================================================
    // Hit testing
    // =============================================================================================

    void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(this.timelineContent);
        if (point is null)
            return;

        this.pressPoint = point.Value;

        var hit = this.FindHit(point.Value.X, point.Value.Y);
        this.dragTask = hit?.Task;
        this.dragTarget = hit?.Target ?? GanttDragTarget.None;

        if (this.dragTask is not null)
        {
            this.dragOriginalStart = this.dragTask.Start;
            this.dragOriginalEnd = this.dragTask.End;
            this.dragOriginalProgress = this.dragTask.Progress;
        }
    }


    void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        // The rubber band follows the pointer, not the pan delta: a link is drawn to wherever the
        // finger is, which is not the same thing as the offset from where it started.
        if (this.dragTarget != GanttDragTarget.Connector)
            return;

        if (e.GetPosition(this.timelineContent) is { } point)
        {
            this.LinkTarget = new GanttPoint(point.X, point.Y);
            this.Repaint();
        }
    }


    /// <summary>
    /// What is under a point: a part of a bar, or one of its connector dots.
    /// </summary>
    /// <remarks>
    /// Only one row can possibly be hit, and the row index falls straight out of the y coordinate, so
    /// this never walks the plan. A control that tested every bar would get slower with every task
    /// the user added, which is precisely backwards.
    /// </remarks>
    internal (GanttTask Task, GanttDragTarget Target)? FindHit(double x, double y)
    {
        var metrics = this.Metrics;
        var model = this.PlanModel;

        if (metrics is null || model is null)
            return null;

        var rowIndex = metrics.RowAt(y);
        if (rowIndex < 0)
            return null;

        var task = model.Rows[rowIndex].Task;

        if (metrics.HitTest(task, x, y) is { } target)
        {
            return (task, target switch
            {
                GanttHitTarget.StartEdge => GanttDragTarget.StartEdge,
                GanttHitTarget.EndEdge => GanttDragTarget.EndEdge,
                GanttHitTarget.Progress => GanttDragTarget.Progress,
                _ => GanttDragTarget.Body
            });
        }

        // Connectors sit just outside the bar, so they are only ever reached once the bar itself has
        // declined the hit. That ordering is what stops them stealing the resize grips.
        if (this.AllowDependencyEdit && !this.IsReadOnly)
        {
            var (start, end) = metrics.ConnectorsOf(task);

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
    // Drag
    // =============================================================================================

    void OnTimelinePan(object? sender, PanUpdatedEventArgs e)
    {
        if (this.dragTask is null || this.dragTarget == GanttDragTarget.None)
            return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                this.lastDragDx = 0;
                this.touchHook?.LockScroller(true);
                break;

            case GestureStatus.Running:
                this.UpdateDrag(e.TotalX, e.TotalY);
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                this.touchHook?.LockScroller(false);
                this.CommitDrag(e.StatusType == GestureStatus.Canceled);
                break;
        }
    }


    void UpdateDrag(double dx, double dy)
    {
        if (this.dragTask is null || this.Metrics is null || this.PlanModel is null)
            return;

        // Each frame starts from the original dates, not from the last preview — accumulating deltas
        // makes a snapped drag creep, because every frame re-snaps a value that was already snapped.
        this.preview?.Revert();
        this.preview = null;

        if (this.dragTarget == GanttDragTarget.Connector)
        {
            this.LinkTarget = new GanttPoint(this.pressPoint.X + dx, this.pressPoint.Y + dy);
            this.Repaint();
            return;
        }

        this.lastDragDx = dx;

        var plan = this.BuildDragPlan(this.dragTask, this.dragTarget, dx);
        if (plan is null)
            return;

        // Applied immediately so the bars follow the finger. It is reverted before the next frame and
        // once more before the change event, so a handler that cancels never sees a mutated task.
        plan.Apply();
        this.preview = plan;
        this.Repaint();
    }


    /// <summary>
    /// The plan a drag of <paramref name="dx"/> pixels would produce.
    /// </summary>
    /// <remarks>
    /// Takes the task and target as arguments rather than reading the drag fields, because the commit
    /// path has to clear those <i>before</i> it builds the final plan — clearing them first is what
    /// stops a re-entrant gesture update restarting the drag. Reading them here instead produced a
    /// drag that tracked the finger perfectly and then snapped back on release, since the committed
    /// plan was always null.
    /// </remarks>
    GanttSchedulePlan? BuildDragPlan(GanttTask? task, GanttDragTarget target, double dx)
    {
        if (task is null || this.Metrics is null || this.PlanModel is null)
            return null;

        var metrics = this.Metrics;
        var model = this.PlanModel;
        var options = this.BuildScheduleOptions();

        if (target == GanttDragTarget.Progress)
        {
            if (this.IsReadOnly || !this.AllowProgressChange || !task.CanChangeProgress)
                return null;

            var bar = metrics.BarOf(task);
            if (bar.Width <= 0)
                return null;

            var progress = ((bar.Width * this.dragOriginalProgress) + dx) / bar.Width;
            return GanttScheduler.SetProgress(task, progress);
        }

        var delta = GanttGeometry.XToSpan(dx, this.EffectivePixelsPerDay);

        switch (target)
        {
            case GanttDragTarget.Body:
                if (this.IsReadOnly || !this.AllowMove)
                    return null;

                return GanttScheduler.Move(model, task, this.Snap(this.dragOriginalStart + delta), options);

            case GanttDragTarget.StartEdge:
                if (this.IsReadOnly || !this.AllowResize)
                    return null;

                return GanttScheduler.Resize(model, task, this.Snap(this.dragOriginalStart + delta), this.dragOriginalEnd, options);

            case GanttDragTarget.EndEdge:
                if (this.IsReadOnly || !this.AllowResize)
                    return null;

                return GanttScheduler.Resize(model, task, this.dragOriginalStart, this.Snap(this.dragOriginalEnd + delta), options);

            default:
                return null;
        }
    }


    void CommitDrag(bool cancelled)
    {
        var task = this.dragTask;
        var target = this.dragTarget;

        this.dragTask = null;
        this.dragTarget = GanttDragTarget.None;

        // Put everything back before anyone is told about it. The preview existed to move pixels; the
        // authoritative change is the one built here and passed through TaskChanging.
        this.preview?.Revert();
        this.preview = null;

        if (task is null || cancelled)
        {
            this.Repaint();
            return;
        }

        if (target == GanttDragTarget.Connector)
        {
            this.CommitLink(task);
            return;
        }

        var plan = this.BuildDragPlan(task, target, this.lastDragDx);

        if (plan is null || !plan.IsValid || !plan.HasChanges)
        {
            this.RelayoutTimeline();
            return;
        }

        var changing = new GanttTaskChangingEventArgs(plan);
        this.TaskChanging?.Invoke(this, changing);

        if (changing.Cancel)
        {
            this.RelayoutTimeline();
            return;
        }

        plan.Apply();
        this.RebuildModel();

        this.TaskChanged?.Invoke(this, new GanttTaskChangedEventArgs(plan));

        if (this.TaskChangedCommand?.CanExecute(plan) == true)
            this.TaskChangedCommand.Execute(plan);
    }


    void CommitLink(GanttTask source)
    {
        var model = this.PlanModel;
        var metrics = this.Metrics;

        if (model is null || metrics is null || this.Dependencies is not IList list)
        {
            this.Repaint();
            return;
        }

        var hit = this.FindHit(this.LinkTarget.X, this.LinkTarget.Y);
        if (hit is null || ReferenceEquals(hit.Value.Task, source))
        {
            this.Repaint();
            return;
        }

        // Which edges the link joins falls out of which dot it was dragged from and which end it was
        // dropped on — the user draws the shape rather than picking a type from a menu.
        var targetTask = hit.Value.Task;
        var droppedOnStart = hit.Value.Target != GanttDragTarget.EndEdge;

        var type = (this.dragFromStartConnector, droppedOnStart) switch
        {
            (false, true) => GanttDependencyType.FinishToStart,
            (true, true) => GanttDependencyType.StartToStart,
            (false, false) => GanttDependencyType.FinishToFinish,
            (true, false) => GanttDependencyType.StartToFinish
        };

        var dependency = new GanttDependency(source.Id, targetTask.Id, type);
        var plan = GanttScheduler.AddDependency(model, dependency, this.BuildScheduleOptions());

        if (!plan.IsValid)
        {
            this.Repaint();
            return;
        }

        var args = new GanttDependencyEventArgs(dependency);
        this.DependencyCreated?.Invoke(this, args);

        if (args.Cancel)
        {
            this.Repaint();
            return;
        }

        list.Add(dependency);
        plan.Apply();
        this.RebuildModel();

        if (this.DependencyCreatedCommand?.CanExecute(dependency) == true)
            this.DependencyCreatedCommand.Execute(dependency);
    }


    /// <summary>Removes a link and cascades the result. Returns whether it was there to remove.</summary>
    public bool RemoveDependency(GanttDependency dependency)
    {
        if (this.Dependencies is not IList list || !list.Contains(dependency))
            return false;

        var args = new GanttDependencyEventArgs(dependency);
        this.DependencyRemoved?.Invoke(this, args);

        if (args.Cancel)
            return false;

        list.Remove(dependency);
        this.RebuildModel();
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


    internal DateTimeOffset Snap(DateTimeOffset value) => GanttGeometry.Snap(
        value,
        this.SnapMode,
        this.EffectiveScale,
        this.SnapInterval,
        this.Calendar
    );


    // =============================================================================================
    // Tap and selection
    // =============================================================================================

    void CommitTap()
    {
        var hit = this.FindHit(this.pressPoint.X, this.pressPoint.Y);
        if (hit is null)
            return;

        this.HandleRowTapped(hit.Value.Task);
    }


    void CommitDoubleTap()
    {
        var hit = this.FindHit(this.pressPoint.X, this.pressPoint.Y);
        if (hit is not null)
            this.TaskDoubleTapped?.Invoke(this, new GanttTaskEventArgs(hit.Value.Task));
    }


    internal void HandleRowTapped(GanttTask task)
    {
        this.SelectTask(task);

        this.TaskTapped?.Invoke(this, new GanttTaskEventArgs(task));

        if (this.TaskTappedCommand?.CanExecute(task) == true)
            this.TaskTappedCommand.Execute(task);
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
                break;
        }

        this.RefreshSelectionVisuals();
        this.SelectionChanged?.Invoke(this, EventArgs.Empty);
    }


    void OnSelectedTaskChanged()
    {
        if (this.SelectionMode == GanttSelectionMode.Single)
        {
            this.SelectedTasks.Clear();
            if (this.SelectedTask is not null)
                this.SelectedTasks.Add(this.SelectedTask);
        }
        this.RefreshSelectionVisuals();
    }


    void RefreshSelectionVisuals()
    {
        // The task pane is views, so it updates in place; the timeline is drawn, so it repaints. Both
        // beat rebuilding, which would drop scroll position on every click.
        foreach (var child in this.taskPaneRows.Children)
        {
            if (child is GanttTaskPaneRow row)
                row.UpdateSelection();
        }
        this.Repaint();
    }


    // =============================================================================================
    // Expansion
    // =============================================================================================

    /// <summary>Expands or collapses a summary.</summary>
    public void ToggleExpand(GanttTask task)
    {
        task.IsExpanded = !task.IsExpanded;
        this.RebuildModel();
    }

    /// <summary>Expands every summary in the plan.</summary>
    public void ExpandAll() => this.SetExpansion(true);

    /// <summary>Collapses every summary in the plan.</summary>
    public void CollapseAll() => this.SetExpansion(false);

    void SetExpansion(bool expanded)
    {
        if (this.PlanModel is null)
            return;

        foreach (var task in this.PlanModel.AllTasks)
            task.IsExpanded = expanded;

        this.RebuildModel();
    }


    // =============================================================================================
    // Zoom and scroll
    // =============================================================================================

    void OnPinch(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (!this.AllowZoom)
            return;

        if (e.Status == GestureStatus.Started)
        {
            this.pinchStartPixelsPerDay = this.EffectivePixelsPerDay;
            return;
        }

        if (e.Status != GestureStatus.Running)
            return;

        var anchorX = e.ScaleOrigin.X * this.timelineContent.Width;
        this.SetZoom(this.pinchStartPixelsPerDay * e.Scale, anchorX);
    }


    /// <summary>Zooms in one step, about the middle of the viewport.</summary>
    public void ZoomIn() => this.SetZoom(this.EffectivePixelsPerDay * 1.6, this.timelineScroll.ScrollX + (this.timelineScroll.Width / 2));

    /// <summary>Zooms out one step, about the middle of the viewport.</summary>
    public void ZoomOut() => this.SetZoom(this.EffectivePixelsPerDay / 1.6, this.timelineScroll.ScrollX + (this.timelineScroll.Width / 2));


    /// <summary>Sets the zoom, keeping the instant at <paramref name="anchorX"/> under that position.</summary>
    public void SetZoom(double pixelsPerDay, double anchorX)
    {
        if (!this.AllowZoom)
            return;

        var clamped = Math.Clamp(pixelsPerDay, GanttGeometry.MinPixelsPerDay, GanttGeometry.MaxPixelsPerDay);
        if (Math.Abs(clamped - this.EffectivePixelsPerDay) < 0.0001)
            return;

        // Where the anchored instant ends up is a scroll offset, not a new origin: the range is what
        // the consumer asked for and zoom must not quietly rewrite it.
        var anchorDate = GanttGeometry.XToDate(anchorX, this.RangeStart, this.EffectivePixelsPerDay);

        this.PixelsPerDay = clamped;
        this.RelayoutTimeline();

        var target = GanttGeometry.DateToX(anchorDate, this.RangeStart, clamped) - (anchorX - this.timelineScroll.ScrollX);
        _ = this.timelineScroll.ScrollToAsync(Math.Max(0, target), this.timelineScroll.ScrollY, false);
    }


    /// <summary>Picks the zoom that fits the whole plan in the visible width.</summary>
    public void ZoomToFit()
    {
        if (this.PlanModel is null || this.timelineScroll.Width <= 0)
            return;

        this.PixelsPerDay = GanttGeometry.FitToWidth(this.RangeStart, this.RangeEnd, this.timelineScroll.Width);
        this.RelayoutTimeline();
    }


    /// <summary>Scrolls so that a date is at the left edge of the timeline.</summary>
    public Task ScrollToDate(DateTimeOffset date, bool animated = true)
    {
        if (this.Metrics is null)
            return Task.CompletedTask;

        var x = Math.Max(0, this.Metrics.XOf(date));
        return this.timelineScroll.ScrollToAsync(x, this.timelineScroll.ScrollY, animated);
    }


    /// <summary>
    /// Scrolls a task into view both ways, and expands whatever it is hidden inside first — scrolling
    /// to a row that a collapsed parent is hiding would silently do nothing.
    /// </summary>
    public async Task ScrollToTask(GanttTask task, bool animated = true)
    {
        if (this.PlanModel is null)
            return;

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
            this.RebuildModel();

        if (this.Metrics is null)
            return;

        var row = this.Metrics.RowIndexOf(task.Id);
        if (row < 0)
            return;

        var x = Math.Max(0, this.Metrics.XOf(task.Start) - 40);
        var y = Math.Max(0, this.Metrics.RowTop(row) - this.RowHeight);

        await this.timelineScroll.ScrollToAsync(x, y, animated);
    }


    void OnSplitterPan(object? sender, PanUpdatedEventArgs e)
    {
        if (!this.IsTaskPaneResizable || !this.ShowTaskPane)
            return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                this.splitterStartWidth = this.TaskPaneWidth;
                break;

            case GestureStatus.Running:
                this.TaskPaneWidth = Math.Clamp(this.splitterStartWidth + e.TotalX, 80, 900);
                break;
        }
    }

    double splitterStartWidth;
}
