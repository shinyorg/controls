using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Microsoft.Maui.Layouts;
using Shiny.Controls.Gantt;
using Shiny.Maui.Controls.Gantt.Internal;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Gantt;

/// <summary>
/// A project timeline: a task grid on the left, a scrolling time axis on the right, bars with
/// progress, milestones, summary rollups, baselines, deadlines, dependency arrows and a critical
/// path — all of it draggable.
/// </summary>
/// <remarks>
/// <para>
/// The scheduling itself lives in <c>Shiny.Controls.Gantt.Shared</c> and is shared verbatim with the
/// Blazor control. This class is the MAUI half: it owns the visual tree, the scroll and zoom
/// plumbing, and turning gestures into <see cref="GanttSchedulePlan"/>s. It deliberately contains no
/// date arithmetic of its own.
/// </para>
/// <para>
/// The layout is a three-column grid — task pane, splitter, timeline — with the timeline's header and
/// the task pane's rows kept in step with the one scroller by translation rather than by nesting
/// scrollers inside each other, which is the arrangement <c>DataGrid</c>'s frozen columns already
/// use and the only one that behaves the same on all six platforms.
/// </para>
/// </remarks>
public partial class GanttView : ContentView, IDisposable
{
    readonly Grid root;
    readonly Grid taskPaneHeader;
    readonly VerticalStackLayout taskPaneRows;
    readonly ScrollView taskPaneViewport;
    readonly ContentView headerViewport;
    readonly GraphicsView headerGraphics;
    readonly ScrollView timelineScroll;
    readonly Grid timelineContent;
    readonly GraphicsView timelineGraphics;
    readonly AbsoluteLayout barLayer;
    readonly Border splitter;
    readonly ContentView emptyHost;
    readonly GanttPalette palette;
    readonly Border taskPaneBorder;

    INotifyCollectionChanged? observedTasks;
    INotifyCollectionChanged? observedDependencies;
    readonly List<INotifyPropertyChanged> observedItems = [];

    IDispatcherTimer? nowTimer;
    bool syncingScroll;
    bool disposed;

    public GanttView()
    {
        this.root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            RowSpacing = 0,
            ColumnSpacing = 0
        };

        this.palette = new GanttPalette(this.root);
        this.palette.Changed += (_, _) => this.Repaint();

        // ---- task pane ----
        this.taskPaneHeader = new Grid { ColumnSpacing = 0 };
        this.taskPaneRows = new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Start };

        // A ScrollView rather than a clipped panel: the task pane has to be able to scroll on its own
        // when the timeline is hidden, and syncing two scrollers is less trouble than special-casing
        // that mode. Its own scrollbar is suppressed - the timeline's is the one that means anything.
        this.taskPaneViewport = new ScrollView
        {
            Content = this.taskPaneRows,
            Orientation = ScrollOrientation.Vertical,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never
        };

        this.taskPaneBorder = new Border
        {
            Content = this.taskPaneViewport,
            StrokeThickness = 0,
            Padding = 0
        };

        this.splitter = new Border
        {
            WidthRequest = 6,
            StrokeThickness = 0,
            Padding = 0,
            Content = new BoxView { WidthRequest = 1, HorizontalOptions = LayoutOptions.Center }
        };

        // ---- timeline ----
        this.headerGraphics = new GraphicsView { Drawable = new GanttHeaderDrawable(this) };
        this.headerViewport = new ContentView
        {
            Content = this.headerGraphics,
            HorizontalOptions = LayoutOptions.Fill
        };

        // IsClippedToBounds alone does not hold the header in: scrolled right, the translated
        // GraphicsView painted its earlier columns straight over the task pane's own header. An
        // explicit Clip geometry does clip, but it has to be resized with the viewport - hence the
        // handler rather than a one-off assignment.
        this.headerViewport.SizeChanged += (_, _) => this.UpdateHeaderClip();

        this.timelineGraphics = new GraphicsView { Drawable = new GanttTimelineDrawable(this) };
        this.barLayer = new AbsoluteLayout { InputTransparent = true };

        // Start, not the default Fill. A ScrollView centres content smaller than its viewport, so a
        // plan with fewer rows than fit on screen had its whole timeline pushed down by half the
        // slack — bars sitting a row and a half below the task-pane rows they belong to. The same
        // thing happens horizontally the moment ZoomToFit makes the content narrower than the
        // viewport, which is why both axes are pinned rather than just the one that was caught.
        this.timelineContent = new Grid
        {
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start
        };
        this.timelineContent.Add(this.timelineGraphics);
        this.timelineContent.Add(this.barLayer);

        this.timelineScroll = new ScrollView
        {
            Content = this.timelineContent,
            Orientation = ScrollOrientation.Both
        };
        this.timelineScroll.Scrolled += this.OnTimelineScrolled;
        this.taskPaneViewport.Scrolled += this.OnTaskPaneScrolled;

        this.emptyHost = new ContentView { IsVisible = false, InputTransparent = true };

        this.root.Add(this.taskPaneHeader, 0, 0);
        this.root.Add(this.taskPaneBorder, 0, 1);
        this.root.Add(this.splitter, 1, 0);
        Grid.SetRowSpan(this.splitter, 2);
        this.root.Add(this.headerViewport, 2, 0);
        this.root.Add(this.timelineScroll, 2, 1);
        this.root.Add(this.emptyHost, 2, 1);

        this.Content = this.root;

        this.ApplyThemeChrome();
        this.WireGestures();

        ((INotifyCollectionChanged)this.Columns).CollectionChanged += (_, _) => this.RelayoutTimeline();
        ((INotifyCollectionChanged)this.HighlightRanges).CollectionChanged += (_, _) => this.Repaint();
        ((INotifyCollectionChanged)this.SelectedTasks).CollectionChanged += (_, _) => this.Repaint();

        StyleGuard.MarkReady(this, typeof(GanttView));
        this.RebuildModel();
    }


    // =============================================================================================
    // State the drawables read
    // =============================================================================================

    internal GanttLayoutMetrics? Metrics { get; private set; }
    internal GanttPalette? Palette => this.palette;
    internal IReadOnlyList<GanttTick> UpperTicks { get; private set; } = [];
    internal IReadOnlyList<GanttTick> LowerTicks { get; private set; } = [];
    internal IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> NonWorkingIntervals { get; private set; } = [];
    internal DateTimeOffset Now { get; private set; } = DateTimeOffset.Now;

    /// <summary>How far the timeline is scrolled, so the header can pin a partly-scrolled label.</summary>
    internal double ViewportScrollX { get; private set; }

    /// <summary>Visible width of the timeline, paired with <see cref="ViewportScrollX"/>.</summary>
    internal double ViewportWidth => this.timelineScroll.Width;

    /// <summary>The scale currently in use — the resolved one when <see cref="TimeScale"/> is Auto.</summary>
    public GanttTimeScale EffectiveScale { get; private set; } = GanttTimeScale.Day;

    /// <summary>The zoom currently in use, whether it was set explicitly or derived from the scale.</summary>
    public double EffectivePixelsPerDay { get; private set; } = 40;

    /// <summary>The first instant on the axis, derived from the plan when <see cref="StartDate"/> is null.</summary>
    public DateTimeOffset RangeStart { get; private set; }

    /// <summary>The last instant on the axis.</summary>
    public DateTimeOffset RangeEnd { get; private set; }

    internal CultureInfo EffectiveCulture => this.Culture ?? CultureInfo.CurrentCulture;

    internal bool IsSelected(GanttTask task) =>
        ReferenceEquals(this.SelectedTask, task) || this.SelectedTasks.Contains(task);


    // =============================================================================================
    // Model
    // =============================================================================================

    void OnItemsChanged(object? oldValue, object? newValue)
    {
        if (oldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= this.OnSourceCollectionChanged;

            if (ReferenceEquals(oldCollection, this.observedTasks))
                this.observedTasks = null;

            if (ReferenceEquals(oldCollection, this.observedDependencies))
                this.observedDependencies = null;
        }

        if (newValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += this.OnSourceCollectionChanged;

            if (ReferenceEquals(newValue, this.Tasks))
                this.observedTasks = newCollection;
            else
                this.observedDependencies = newCollection;
        }
        this.RebuildModel();
    }


    void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => this.RebuildModel();


    /// <summary>
    /// Rebuilds the plan from the bound collections and re-lays out. Called automatically on every
    /// data change; call it by hand after mutating a task in a way the control cannot observe.
    /// </summary>
    public void RebuildModel()
    {
        this.UnhookItems();

        var tasks = this.Tasks?.OfType<GanttTask>().ToList() ?? [];
        var dependencies = this.Dependencies?.OfType<GanttDependency>().ToList() ?? [];

        this.PlanModel = GanttModel.Build(tasks, dependencies, new GanttModelOptions
        {
            Calendar = this.Calendar ?? GanttCalendar.Continuous,
            RollUpSummaries = this.RollUpSummaries,
            ComputeCriticalPath = this.ShowCriticalPath,
            CriticalSlackThreshold = this.CriticalSlackThreshold
        });

        // Watch each task, not just the collection: a view model that edits a date in place raises
        // PropertyChanged and nothing else, and without this the bar simply would not move.
        foreach (var task in this.PlanModel.AllTasks)
        {
            task.PropertyChanged += this.OnItemPropertyChanged;
            this.observedItems.Add(task);
        }
        foreach (var dependency in this.PlanModel.Dependencies)
        {
            dependency.PropertyChanged += this.OnItemPropertyChanged;
            this.observedItems.Add(dependency);
        }

        this.PlanBuilt?.Invoke(this, EventArgs.Empty);
        this.RelayoutTimeline();
    }


    void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Only the properties that move something. Progress and colour repaint; anything structural
        // - dates, expansion, kind - has to re-roll the summaries above it, so it rebuilds.
        switch (e.PropertyName)
        {
            case nameof(GanttTask.Progress):
            // GanttDependency.Color is the same string, so it is covered by this arm too.
            case nameof(GanttTask.Color):
                this.Repaint();
                break;

            case nameof(GanttTask.Start):
            case nameof(GanttTask.End):
            case nameof(GanttTask.Kind):
            case nameof(GanttTask.IsExpanded):
            case nameof(GanttTask.ParentId):
            case nameof(GanttTask.Name):
            case nameof(GanttTask.Deadline):
            case nameof(GanttDependency.Type):
            case nameof(GanttDependency.Lag):
                if (!this.IsDragging)
                    this.RebuildModel();
                break;
        }
    }


    void UnhookItems()
    {
        foreach (var item in this.observedItems)
            item.PropertyChanged -= this.OnItemPropertyChanged;

        this.observedItems.Clear();
    }


    // =============================================================================================
    // Layout
    // =============================================================================================

    /// <summary>Recomputes the axis, the ticks and every bar position, then repaints.</summary>
    public void RelayoutTimeline()
    {
        var model = this.PlanModel;
        if (model is null)
            return;

        this.Now = DateTimeOffset.Now;
        this.ResolveRange(model);
        this.ResolveScale();

        var calendar = this.Calendar ?? GanttCalendar.Continuous;

        this.Metrics = new GanttLayoutMetrics(
            model,
            this.RangeStart,
            this.EffectivePixelsPerDay,
            new GanttRowMetrics(
                this.RowHeight,
                this.BarHeight,
                this.SummaryBarHeight,
                this.MilestoneSize,
                Math.Max(2, this.BarHeight / 4)
            )
        );

        var lower = this.EffectiveScale;
        var upper = GanttGeometry.UpperTierFor(lower);

        this.LowerTicks = GanttGeometry.GenerateTicks(
            this.RangeStart, this.RangeEnd, lower, this.RangeStart, this.EffectivePixelsPerDay,
            this.EffectiveCulture, calendar, this.Now
        );
        this.UpperTicks = GanttGeometry.GenerateTicks(
            this.RangeStart, this.RangeEnd, upper, this.RangeStart, this.EffectivePixelsPerDay,
            this.EffectiveCulture, calendar, this.Now
        );

        this.NonWorkingIntervals = this.ShowNonWorkingShading
            ? calendar.NonWorkingIntervals(this.RangeStart, this.RangeEnd)
            : [];

        var contentWidth = Math.Max(1, GanttGeometry.SpanToWidth(this.RangeStart, this.RangeEnd, this.EffectivePixelsPerDay));
        var contentHeight = Math.Max(this.RowHeight, this.Metrics.ContentHeight);

        this.timelineContent.WidthRequest = contentWidth;
        this.timelineContent.HeightRequest = contentHeight;
        this.timelineGraphics.WidthRequest = contentWidth;
        this.timelineGraphics.HeightRequest = contentHeight;
        this.barLayer.WidthRequest = contentWidth;
        this.barLayer.HeightRequest = contentHeight;

        this.headerGraphics.WidthRequest = contentWidth;
        this.headerGraphics.HeightRequest = this.HeaderHeight;
        this.headerGraphics.HorizontalOptions = LayoutOptions.Start;
        this.headerViewport.HeightRequest = this.HeaderHeight;
        this.UpdateHeaderClip();

        this.taskPaneHeader.HeightRequest = this.HeaderHeight;
        this.taskPaneBorder.WidthRequest = this.ShowTaskPane ? this.TaskPaneWidth : 0;
        this.taskPaneHeader.WidthRequest = this.ShowTaskPane ? this.TaskPaneWidth : 0;
        this.taskPaneBorder.IsVisible = this.ShowTaskPane;
        this.taskPaneHeader.IsVisible = this.ShowTaskPane;
        this.splitter.IsVisible = this.ShowTaskPane;

        this.BuildTaskPane(model);
        this.BuildTemplatedBars(model);

        var empty = model.Rows.Count == 0;
        this.emptyHost.Content = this.EmptyView;
        this.emptyHost.IsVisible = empty && this.EmptyView is not null;
        this.timelineScroll.IsVisible = !this.emptyHost.IsVisible;

        this.Repaint();
    }


    /// <summary>Invalidates the drawn surfaces without recomputing anything.</summary>
    public void Repaint()
    {
        this.timelineGraphics.Invalidate();
        this.headerGraphics.Invalidate();
    }


    void ResolveRange(GanttModel model)
    {
        var padding = TimeSpan.FromDays(Math.Max(0, this.PaddingDays));

        var start = this.StartDate ?? model.Start - padding;
        var end = this.EndDate ?? model.End + padding;

        // A plan of one milestone has zero extent, and a zero-width timeline draws nothing at all —
        // which looks exactly like a control that failed to bind.
        if (end <= start)
            end = start.AddDays(1);

        this.RangeStart = start;
        this.RangeEnd = end;
    }


    void ResolveScale()
    {
        var scale = this.TimeScale;

        if (ThemeTokens.IsSet(this.PixelsPerDay))
        {
            this.EffectivePixelsPerDay = Math.Clamp(this.PixelsPerDay, GanttGeometry.MinPixelsPerDay, GanttGeometry.MaxPixelsPerDay);
            scale = scale == GanttTimeScale.Auto ? GanttGeometry.ResolveScale(this.EffectivePixelsPerDay) : scale;
        }
        else if (scale == GanttTimeScale.Auto)
        {
            scale = GanttTimeScale.Day;
            this.EffectivePixelsPerDay = GanttGeometry.PixelsPerDayFor(scale, 40);
        }
        else
        {
            this.EffectivePixelsPerDay = GanttGeometry.PixelsPerDayFor(scale, 40);
        }

        if (scale != this.EffectiveScale)
        {
            this.EffectiveScale = scale;
            this.ScaleChanged?.Invoke(this, EventArgs.Empty);
        }
    }


    // =============================================================================================
    // Task pane
    // =============================================================================================

    void BuildTaskPane(GanttModel model)
    {
        if (!this.ShowTaskPane)
        {
            this.taskPaneRows.Clear();
            this.taskPaneHeader.Clear();
            return;
        }

        var columns = this.ResolveColumns();

        this.taskPaneHeader.Clear();
        this.taskPaneHeader.ColumnDefinitions.Clear();
        this.taskPaneRows.Clear();

        for (var i = 0; i < columns.Count; i++)
        {
            this.taskPaneHeader.ColumnDefinitions.Add(new ColumnDefinition(columns[i].Width));

            var label = new Label
            {
                Text = string.IsNullOrEmpty(columns[i].Header) ? columns[i].Field : columns[i].Header,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center,
                Padding = new Thickness(8, 0),
                LineBreakMode = LineBreakMode.TailTruncation
            };
            label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
            label.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);

            this.taskPaneHeader.Add(label, i, 0);
        }

        foreach (var row in model.Rows)
            this.taskPaneRows.Add(new GanttTaskPaneRow(this, row, columns));
    }


    /// <summary>
    /// The columns to draw. An unconfigured control still has to show something useful, so it seeds
    /// a name/start/finish set rather than rendering an empty pane.
    /// </summary>
    internal IReadOnlyList<GanttColumn> ResolveColumns()
    {
        if (this.Columns.Count > 0)
        {
            // Exactly one column carries the indent and the chevron. If nobody claimed it, the first
            // one gets it — a tree with no expanders is unusable and silently so.
            if (!this.Columns.Any(x => x.ShowHierarchy))
                this.Columns[0].ShowHierarchy = true;

            return (IReadOnlyList<GanttColumn>)this.Columns;
        }

        return
        [
            new GanttColumn { Header = "Task", Field = GanttColumn.NameField, Width = 160, ShowHierarchy = true },
            new GanttColumn { Header = "Start", Field = GanttColumn.StartField, Width = 90 },
            new GanttColumn { Header = "Finish", Field = GanttColumn.EndField, Width = 90 }
        ];
    }


    // =============================================================================================
    // Templated bars
    // =============================================================================================

    void BuildTemplatedBars(GanttModel model)
    {
        this.barLayer.Clear();

        if (this.BarTemplate is null || this.Metrics is null)
            return;

        foreach (var row in model.Rows)
        {
            var rect = this.Metrics.BarOf(row.Task, row.Index);
            if (rect.IsEmpty)
                continue;

            if (this.BarTemplate.CreateContent() is not View view)
                continue;

            view.BindingContext = row.Task;
            AbsoluteLayout.SetLayoutFlags(view, AbsoluteLayoutFlags.None);
            AbsoluteLayout.SetLayoutBounds(view, new Rect(rect.X, rect.Y, rect.Width, rect.Height));
            this.barLayer.Add(view);
        }
    }


    // =============================================================================================
    // Scroll sync
    // =============================================================================================

    void OnTimelineScrolled(object? sender, ScrolledEventArgs e)
    {
        // The header is translated rather than scrolled: giving it a scroller of its own means two
        // scrollers fighting over momentum, and the header visibly lags the bars on iOS.
        this.headerGraphics.TranslationX = -e.ScrollX;

        // The upper tier pins the month/year label of whichever cell the left edge is inside, so
        // scrolling into the middle of September does not leave the row blank.
        this.ViewportScrollX = e.ScrollX;
        this.headerGraphics.Invalidate();

        if (this.syncingScroll)
            return;

        this.syncingScroll = true;
        try
        {
            _ = this.taskPaneViewport.ScrollToAsync(0, e.ScrollY, false);
        }
        finally
        {
            this.syncingScroll = false;
        }
    }


    void OnTaskPaneScrolled(object? sender, ScrolledEventArgs e)
    {
        if (this.syncingScroll)
            return;

        this.syncingScroll = true;
        try
        {
            _ = this.timelineScroll.ScrollToAsync(this.timelineScroll.ScrollX, e.ScrollY, false);
        }
        finally
        {
            this.syncingScroll = false;
        }
    }


    // =============================================================================================
    // Theme chrome
    // =============================================================================================

    void UpdateHeaderClip()
    {
        if (this.headerViewport.Width <= 0 || this.headerViewport.Height <= 0)
            return;

        this.headerViewport.Clip = new Microsoft.Maui.Controls.Shapes.RectangleGeometry(
            new Rect(0, 0, this.headerViewport.Width, this.headerViewport.Height)
        );
    }


    void ApplyThemeChrome()
    {
        this.taskPaneHeader.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);
        this.taskPaneBorder.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);
        this.splitter.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainer);
        this.timelineContent.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);

        if (this.splitter.Content is BoxView line)
            line.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);
    }


    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (this.Handler is null)
        {
            this.StopNowTimer();
            return;
        }

        this.StartNowTimer();

        // The theme dictionary merges after construction on the alternate app heads, so the palette's
        // probes only resolve now. Repainting here is what stops the first frame being unthemed.
        this.Repaint();
    }


    void StartNowTimer()
    {
        if (this.nowTimer is not null || this.Dispatcher is null)
            return;

        this.nowTimer = this.Dispatcher.CreateTimer();
        this.nowTimer.Interval = TimeSpan.FromMinutes(1);
        this.nowTimer.Tick += (_, _) =>
        {
            this.Now = DateTimeOffset.Now;
            this.Repaint();
        };
        this.nowTimer.Start();
    }


    void StopNowTimer()
    {
        this.nowTimer?.Stop();
        this.nowTimer = null;
    }


    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        this.StopNowTimer();
        this.UnhookItems();
        this.palette.Dispose();

        if (this.observedTasks is not null)
            this.observedTasks.CollectionChanged -= this.OnSourceCollectionChanged;

        if (this.observedDependencies is not null)
            this.observedDependencies.CollectionChanged -= this.OnSourceCollectionChanged;

        GC.SuppressFinalize(this);
    }
}
