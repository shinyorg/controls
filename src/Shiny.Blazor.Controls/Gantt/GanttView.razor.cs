using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Shiny.Controls.Gantt;

namespace Shiny.Blazor.Controls.Gantt;

/// <summary>
/// A project timeline: a task grid on the left, a scrolling time axis on the right, bars with
/// progress, milestones, summary rollups, baselines, deadlines, dependency arrows and a critical
/// path — all of it draggable.
/// </summary>
/// <remarks>
/// <para>
/// The scheduling lives in <c>Shiny.Controls.Gantt.Shared</c> and is shared verbatim with the MAUI
/// control: the same working calendars, the same dependency cascade, the same critical path, and the
/// same bar rectangles and arrow routes. This component is the DOM half of that arrangement, and
/// contains no date arithmetic of its own.
/// </para>
/// <para>
/// Bars are absolutely positioned <c>div</c>s rather than an SVG or a canvas, so a
/// <see cref="BarTemplate"/> can put arbitrary markup inside one and so the browser's own hit testing,
/// tooltips and accessibility tree work without being reimplemented. Only the dependency arrows are
/// SVG, because polylines are what SVG is for.
/// </para>
/// </remarks>
public partial class GanttView : ComponentBase, IAsyncDisposable
{
    [Inject] IJSRuntime JS { get; set; } = default!;

    ElementReference scrollElement;
    ElementReference canvasElement;
    ElementReference headerInner;
    ElementReference paneInner;

    IJSObjectReference? module;
    DotNetObjectReference<GanttView>? selfRef;
    bool attached;
    bool rendered;

    GanttLayoutMetrics? metrics;
    IReadOnlyList<GanttTick> upperTicks = [];
    IReadOnlyList<GanttTick> lowerTicks = [];
    IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> nonWorking = [];
    IReadOnlyList<GanttColumnDefinition> resolvedColumns = [];
    double contentWidth = 1;
    double contentHeight = 1;

    readonly List<INotifyPropertyChanged> observedItems = [];
    INotifyCollectionChanged? observedTasks;
    INotifyCollectionChanged? observedDependencies;

    // Drag state
    GanttTask? dragTask;
    GanttDragTarget dragTarget = GanttDragTarget.None;
    bool dragFromStartConnector;
    double dragOriginX;
    DateTimeOffset dragOriginalStart;
    DateTimeOffset dragOriginalEnd;
    double dragOriginalProgress;
    GanttSchedulePlan? preview;
    long activePointer = -1;

    GanttOrigin canvasOrigin = new();
    GanttTask? linkSource;
    GanttPoint linkFrom;
    GanttPoint linkTo;

    // Splitter state
    bool splitterActive;
    double splitterOriginX;
    double splitterOriginWidth;


    // =============================================================================================
    // Parameters — data
    // =============================================================================================

    /// <summary>
    /// The plan. Map your own type onto <see cref="GanttTask"/> and hang the original off
    /// <see cref="GanttTask.Item"/>. An observable collection is watched, as is each task.
    /// </summary>
    [Parameter] public IEnumerable<GanttTask>? Tasks { get; set; }

    /// <summary>The links between tasks.</summary>
    [Parameter] public IEnumerable<GanttDependency>? Dependencies { get; set; }

    /// <summary>Working-time rules. Null means <see cref="GanttCalendar.Continuous"/>.</summary>
    [Parameter] public GanttCalendar? Calendar { get; set; }

    /// <summary>Whether a summary's dates come from its children.</summary>
    [Parameter] public bool RollUpSummaries { get; set; } = true;

    /// <summary>Whether critical tasks and links are highlighted.</summary>
    [Parameter] public bool ShowCriticalPath { get; set; }

    /// <summary>How little slack still counts as critical.</summary>
    [Parameter] public TimeSpan CriticalSlackThreshold { get; set; }


    // =============================================================================================
    // Parameters — axis
    // =============================================================================================

    [Parameter] public DateTimeOffset? StartDate { get; set; }
    [Parameter] public DateTimeOffset? EndDate { get; set; }

    /// <summary>Header granularity. <see cref="GanttTimeScale.Auto"/> follows the zoom.</summary>
    [Parameter] public GanttTimeScale TimeScale { get; set; } = GanttTimeScale.Auto;

    /// <summary>The zoom level. Null derives it from <see cref="TimeScale"/>.</summary>
    [Parameter] public double? PixelsPerDay { get; set; }

    /// <summary>Breathing room added to each end of a derived date range.</summary>
    [Parameter] public double PaddingDays { get; set; } = 2;

    /// <summary>Culture for header labels and cell formatting.</summary>
    [Parameter] public CultureInfo? Culture { get; set; }


    // =============================================================================================
    // Parameters — metrics
    // =============================================================================================

    [Parameter] public double RowHeight { get; set; } = 32;
    [Parameter] public double BarHeight { get; set; } = 18;
    [Parameter] public double SummaryBarHeight { get; set; } = 10;
    [Parameter] public double MilestoneSize { get; set; } = 14;
    [Parameter] public double HeaderHeight { get; set; } = 48;
    [Parameter] public double TaskPaneWidth { get; set; } = 260;
    [Parameter] public bool ShowTaskPane { get; set; } = true;
    [Parameter] public bool IsTaskPaneResizable { get; set; } = true;

    /// <summary>Raised when the splitter is dragged, so the width can be bound two-way.</summary>
    [Parameter] public EventCallback<double> TaskPaneWidthChanged { get; set; }

    /// <summary>Task-pane columns. A sensible default set is used when none are given.</summary>
    [Parameter] public IReadOnlyList<GanttColumnDefinition>? Columns { get; set; }

    /// <summary>Shaded bands drawn behind the bars.</summary>
    [Parameter] public IReadOnlyList<GanttHighlightRange> HighlightRanges { get; set; } = [];


    // =============================================================================================
    // Parameters — display
    // =============================================================================================

    [Parameter] public bool ShowDependencies { get; set; } = true;
    [Parameter] public bool ShowBaselines { get; set; } = true;
    [Parameter] public bool ShowProgress { get; set; } = true;
    [Parameter] public bool ShowDeadlines { get; set; } = true;
    [Parameter] public bool ShowNonWorkingShading { get; set; } = true;
    [Parameter] public bool ShowTodayMarker { get; set; } = true;
    [Parameter] public bool ShowGridLines { get; set; } = true;
    [Parameter] public bool ShowRowSeparators { get; set; } = true;
    [Parameter] public GanttBarLabel BarLabel { get; set; } = GanttBarLabel.Right;

    /// <summary>Any CSS colour, or null to follow the theme token.</summary>
    [Parameter] public string? BarColor { get; set; }

    [Parameter] public string? SummaryColor { get; set; }
    [Parameter] public string? MilestoneColor { get; set; }
    [Parameter] public string? CriticalColor { get; set; }

    /// <summary>Extra classes on the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Extra inline style on the root element, appended after the component's own.</summary>
    [Parameter] public string? Style { get; set; }


    // =============================================================================================
    // Parameters — behaviour
    // =============================================================================================

    [Parameter] public bool IsReadOnly { get; set; }
    [Parameter] public bool AllowMove { get; set; } = true;
    [Parameter] public bool AllowResize { get; set; } = true;
    [Parameter] public bool AllowProgressChange { get; set; } = true;

    /// <summary>Whether dragging from a selected bar's connector dot draws a new link.</summary>
    [Parameter] public bool AllowDependencyEdit { get; set; }

    [Parameter] public bool AllowZoom { get; set; } = true;
    [Parameter] public GanttSnapMode SnapMode { get; set; } = GanttSnapMode.Scale;
    [Parameter] public TimeSpan SnapInterval { get; set; } = TimeSpan.FromHours(1);
    [Parameter] public GanttCascadeMode CascadeMode { get; set; } = GanttCascadeMode.PushOnly;
    [Parameter] public TimeSpan MinimumDuration { get; set; }
    [Parameter] public GanttSelectionMode SelectionMode { get; set; } = GanttSelectionMode.Single;

    [Parameter] public GanttTask? SelectedTask { get; set; }
    [Parameter] public EventCallback<GanttTask?> SelectedTaskChanged { get; set; }

    /// <summary>Every selected task in <see cref="GanttSelectionMode.Multiple"/>.</summary>
    public List<GanttTask> SelectedTasks { get; } = [];


    // =============================================================================================
    // Parameters — templates and callbacks
    // =============================================================================================

    /// <summary>Replaces the drawn bar with arbitrary markup, bound to the task.</summary>
    [Parameter] public RenderFragment<GanttTask>? BarTemplate { get; set; }

    /// <summary>Shown in place of the timeline when the plan has no tasks.</summary>
    [Parameter] public RenderFragment? EmptyTemplate { get; set; }

    /// <summary>Raised before a drag is committed. Set <see cref="GanttTaskChangingArgs.Cancel"/> to veto.</summary>
    [Parameter] public EventCallback<GanttTaskChangingArgs> OnTaskChanging { get; set; }

    /// <summary>Raised after a drag has been applied. Keep the plan to implement undo.</summary>
    [Parameter] public EventCallback<GanttTaskChangedArgs> OnTaskChanged { get; set; }

    [Parameter] public EventCallback<GanttTask> OnTaskClick { get; set; }
    [Parameter] public EventCallback<GanttTask> OnTaskDoubleClick { get; set; }
    [Parameter] public EventCallback<GanttDependencyArgs> OnDependencyCreated { get; set; }
    [Parameter] public EventCallback<GanttDependencyArgs> OnDependencyRemoved { get; set; }
    [Parameter] public EventCallback OnSelectionChanged { get; set; }

    /// <summary>
    /// Raised after every model rebuild. The place to read <see cref="GanttModel.Issues"/> — a plan
    /// that fails validation still renders, so nothing else tells you it was wrong.
    /// </summary>
    [Parameter] public EventCallback<GanttModel> OnPlanBuilt { get; set; }


    // =============================================================================================
    // Derived state
    // =============================================================================================

    /// <summary>The resolved plan.</summary>
    public GanttModel Model { get; private set; } = GanttModel.Empty;

    /// <summary>The visible rows.</summary>
    public IReadOnlyList<GanttRow> Rows => this.Model.Rows;

    /// <summary>The scale currently in use — the resolved one when <see cref="TimeScale"/> is Auto.</summary>
    public GanttTimeScale EffectiveScale { get; private set; } = GanttTimeScale.Day;

    /// <summary>The zoom currently in use.</summary>
    public double EffectivePixelsPerDay { get; private set; } = 40;

    public DateTimeOffset RangeStart { get; private set; }
    public DateTimeOffset RangeEnd { get; private set; }

    internal DateTimeOffset Now { get; private set; } = DateTimeOffset.Now;
    internal CultureInfo EffectiveCulture => this.Culture ?? CultureInfo.CurrentCulture;

    internal List<BarVisual> Bars { get; } = [];

    internal bool IsSelected(GanttTask task) =>
        ReferenceEquals(this.SelectedTask, task) || this.SelectedTasks.Contains(task);

    /// <summary>One bar's fully resolved appearance, computed once per render rather than in markup.</summary>
    internal sealed record BarVisual(
        GanttTask Task,
        GanttRect Rect,
        GanttRect Baseline,
        string Fill,
        string Tooltip,
        bool IsCritical,
        bool LabelFitsInside
    );


    // =============================================================================================
    // Lifecycle
    // =============================================================================================

    protected override void OnParametersSet()
    {
        this.HookCollections();
        this.Rebuild();
    }


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        this.rendered = true;

        if (this.attached)
            return;

        this.attached = true;
        this.selfRef = DotNetObjectReference.Create(this);

        this.module = await this.JS.InvokeAsync<IJSObjectReference>(
            "import", "./_content/Shiny.Blazor.Controls/gantt.js"
        );

        await this.module.InvokeVoidAsync("attach", this.scrollElement, this.headerInner, this.paneInner, this.selfRef);
    }


    void HookCollections()
    {
        if (!ReferenceEquals(this.observedTasks, this.Tasks as INotifyCollectionChanged))
        {
            if (this.observedTasks is not null)
                this.observedTasks.CollectionChanged -= this.OnSourceChanged;

            this.observedTasks = this.Tasks as INotifyCollectionChanged;

            if (this.observedTasks is not null)
                this.observedTasks.CollectionChanged += this.OnSourceChanged;
        }

        if (!ReferenceEquals(this.observedDependencies, this.Dependencies as INotifyCollectionChanged))
        {
            if (this.observedDependencies is not null)
                this.observedDependencies.CollectionChanged -= this.OnSourceChanged;

            this.observedDependencies = this.Dependencies as INotifyCollectionChanged;

            if (this.observedDependencies is not null)
                this.observedDependencies.CollectionChanged += this.OnSourceChanged;
        }
    }


    void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e) => this.Notify(this.Rebuild);


    void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A drag already re-renders every frame; rebuilding from its own writes would recurse.
        if (this.dragTask is not null)
            return;

        this.Notify(this.Rebuild);
    }


    /// <summary>
    /// Runs work on the renderer's thread and repaints — or, before the component has ever rendered,
    /// just runs it.
    /// </summary>
    /// <remarks>
    /// Both <see cref="ComponentBase.InvokeAsync(Action)"/> and
    /// <see cref="ComponentBase.StateHasChanged"/> throw "The render handle is not yet assigned"
    /// until the component is attached to a renderer. That is reachable ordinary usage, not a test
    /// artefact: a consumer who calls <see cref="SelectTask"/> or <see cref="Rebuild"/> from
    /// <c>OnInitialized</c>, or who drives the component from a unit test, hits it. There is nothing
    /// to repaint before the first render anyway, so the guard costs nothing.
    /// </remarks>
    void Notify(Action work)
    {
        if (!this.rendered)
        {
            work();
            return;
        }
        _ = this.InvokeAsync(() =>
        {
            work();
            this.StateHasChanged();
        });
    }


    /// <summary>Repaints, or does nothing when the component has yet to render. See <see cref="Notify"/>.</summary>
    internal void Refresh()
    {
        if (this.rendered)
            this.StateHasChanged();
    }


    void UnhookItems()
    {
        foreach (var item in this.observedItems)
            item.PropertyChanged -= this.OnItemChanged;

        this.observedItems.Clear();
    }


    // =============================================================================================
    // Build
    // =============================================================================================

    /// <summary>Rebuilds the plan and re-lays it out. Called automatically on every parameter change.</summary>
    public void Rebuild()
    {
        this.UnhookItems();
        this.Now = DateTimeOffset.Now;

        var calendar = this.Calendar ?? GanttCalendar.Continuous;

        this.Model = GanttModel.Build(this.Tasks, this.Dependencies, new GanttModelOptions
        {
            Calendar = calendar,
            RollUpSummaries = this.RollUpSummaries,
            ComputeCriticalPath = this.ShowCriticalPath,
            CriticalSlackThreshold = this.CriticalSlackThreshold
        });

        foreach (var task in this.Model.AllTasks)
        {
            task.PropertyChanged += this.OnItemChanged;
            this.observedItems.Add(task);
        }
        foreach (var dependency in this.Model.Dependencies)
        {
            dependency.PropertyChanged += this.OnItemChanged;
            this.observedItems.Add(dependency);
        }

        this.ResolveRange();
        this.ResolveScale();
        this.ResolveColumns();

        this.metrics = new GanttLayoutMetrics(
            this.Model,
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

        this.lowerTicks = GanttGeometry.GenerateTicks(
            this.RangeStart, this.RangeEnd, lower, this.RangeStart, this.EffectivePixelsPerDay,
            this.EffectiveCulture, calendar, this.Now
        );
        this.upperTicks = GanttGeometry.GenerateTicks(
            this.RangeStart, this.RangeEnd, GanttGeometry.UpperTierFor(lower), this.RangeStart,
            this.EffectivePixelsPerDay, this.EffectiveCulture, calendar, this.Now
        );

        this.nonWorking = this.ShowNonWorkingShading
            ? calendar.NonWorkingIntervals(this.RangeStart, this.RangeEnd)
            : [];

        this.contentWidth = Math.Max(1, GanttGeometry.SpanToWidth(this.RangeStart, this.RangeEnd, this.EffectivePixelsPerDay));
        this.contentHeight = Math.Max(this.RowHeight, this.metrics.ContentHeight);

        this.BuildBars();

        if (this.OnPlanBuilt.HasDelegate)
            _ = this.OnPlanBuilt.InvokeAsync(this.Model);
    }


    void BuildBars()
    {
        this.Bars.Clear();
        if (this.metrics is null)
            return;

        foreach (var row in this.Model.Rows)
        {
            var task = row.Task;
            var rect = this.metrics.BarOf(task, row.Index);
            if (rect.IsEmpty)
                continue;

            var critical = this.ShowCriticalPath && task.IsCritical;

            this.Bars.Add(new BarVisual(
                task,
                rect,
                this.ShowBaselines ? this.metrics.BaselineOf(task) : default,
                this.FillFor(task, critical),
                this.TooltipFor(task),
                critical,
                // Roughly seven pixels per character. Approximate on purpose: measuring text would
                // mean a JS round trip per bar per frame, to decide something a few pixels of slack
                // covers anyway.
                rect.Width > task.Name.Length * 7
            ));
        }
    }


    string FillFor(GanttTask task, bool critical)
    {
        if (!string.IsNullOrWhiteSpace(task.Color))
            return task.Color!;

        if (critical)
            return this.CriticalColor ?? "var(--shiny-color-error, #B3261E)";

        return task.Kind switch
        {
            GanttTaskKind.Milestone => this.MilestoneColor ?? "var(--shiny-color-tertiary, #7D5260)",
            GanttTaskKind.Summary or GanttTaskKind.Project => this.SummaryColor ?? "var(--shiny-color-on-surface-variant, #49454F)",
            _ => this.BarColor ?? "var(--shiny-color-primary, #6750A4)"
        };
    }


    string TooltipFor(GanttTask task)
    {
        var culture = this.EffectiveCulture;
        var end = task.Kind == GanttTaskKind.Milestone ? task.Start : task.End;

        var text = task.Kind == GanttTaskKind.Milestone
            ? $"{task.Name}\n{task.Start.ToString("g", culture)}"
            : $"{task.Name}\n{task.Start.ToString("g", culture)} – {end.ToString("g", culture)}\n{GanttFields.FormatDuration(task.Duration, culture)}";

        if (task.Progress > 0)
            text += $"\n{task.Progress.ToString("P0", culture)} complete";

        return text;
    }


    void ResolveRange()
    {
        var padding = TimeSpan.FromDays(Math.Max(0, this.PaddingDays));

        var start = this.StartDate ?? this.Model.Start - padding;
        var end = this.EndDate ?? this.Model.End + padding;

        // A plan of one milestone has zero extent, and a zero-width timeline draws nothing at all —
        // which looks exactly like a component that failed to bind.
        this.RangeStart = start;
        this.RangeEnd = end <= start ? start.AddDays(1) : end;
    }


    void ResolveScale()
    {
        var scale = this.TimeScale;

        if (this.PixelsPerDay is { } explicitZoom)
        {
            this.EffectivePixelsPerDay = Math.Clamp(explicitZoom, GanttGeometry.MinPixelsPerDay, GanttGeometry.MaxPixelsPerDay);
            scale = scale == GanttTimeScale.Auto ? GanttGeometry.ResolveScale(this.EffectivePixelsPerDay) : scale;
        }
        else
        {
            scale = scale == GanttTimeScale.Auto ? GanttTimeScale.Day : scale;
            this.EffectivePixelsPerDay = GanttGeometry.PixelsPerDayFor(scale, 40);
        }
        this.EffectiveScale = scale;
    }


    void ResolveColumns()
    {
        var columns = this.Columns is { Count: > 0 }
            ? this.Columns.ToList()
            :
            [
                new GanttColumnDefinition { Header = "Task", Field = GanttFields.Name, Width = 160, ShowHierarchy = true },
                new GanttColumnDefinition { Header = "Start", Field = GanttFields.Start, Width = 90 },
                new GanttColumnDefinition { Header = "Finish", Field = GanttFields.End, Width = 90 }
            ];

        // A tree with no expanders is unusable, and silently so.
        if (!columns.Any(x => x.ShowHierarchy))
            columns[0].ShowHierarchy = true;

        this.resolvedColumns = columns;
    }


    // =============================================================================================
    // Markup helpers
    // =============================================================================================

    internal double XOf(DateTimeOffset date) =>
        GanttGeometry.DateToX(date, this.RangeStart, this.EffectivePixelsPerDay);

    /// <summary>
    /// Formats a number for CSS. Always invariant: a browser parses "12.5" and never "12,5", so a
    /// German locale would otherwise emit styles the browser silently drops.
    /// </summary>
    internal static string Css(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    internal static string Box(GanttRect rect) =>
        $"left:{Css(rect.X)}px;top:{Css(rect.Y)}px;width:{Css(rect.Width)}px;height:{Css(rect.Height)}px";

    internal static string Points(IReadOnlyList<GanttPoint> route) =>
        string.Join(" ", route.Select(p => $"{Css(p.X)},{Css(p.Y)}"));

    /// <summary>
    /// The arrowhead triangle. Every route ends with a horizontal run into the bar's edge, so the
    /// head only ever points left or right and this stays a sign test rather than a rotation.
    /// </summary>
    internal static string ArrowPoints(IReadOnlyList<GanttPoint> route)
    {
        var to = route[^1];
        var from = route[^2];
        var direction = to.X >= from.X ? 1 : -1;
        const double size = 5;

        return $"{Css(to.X)},{Css(to.Y)} " +
               $"{Css(to.X - (direction * size))},{Css(to.Y - size)} " +
               $"{Css(to.X - (direction * size))},{Css(to.Y + size)}";
    }


    string RootStyle()
    {
        var pane = this.ShowTaskPane ? this.TaskPaneWidth : 0;
        var splitter = this.ShowTaskPane ? 6 : 0;

        var style =
            $"grid-template-columns:{Css(pane)}px {Css(splitter)}px 1fr;" +
            $"--gantt-row-height:{Css(this.RowHeight)}px;" +
            $"--gantt-header-height:{Css(this.HeaderHeight)}px;";

        // Appended, never replaced: a caller's style must be able to add a height without wiping the
        // custom properties every rule in the stylesheet reads.
        return this.Style is null ? style : style + this.Style;
    }


    public async ValueTask DisposeAsync()
    {
        this.UnhookItems();

        if (this.observedTasks is not null)
            this.observedTasks.CollectionChanged -= this.OnSourceChanged;

        if (this.observedDependencies is not null)
            this.observedDependencies.CollectionChanged -= this.OnSourceChanged;

        if (this.module is not null)
        {
            try
            {
                await this.module.InvokeVoidAsync("detach", this.scrollElement);
                await this.module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The circuit is already gone on Blazor Server; there is nothing left to detach from.
            }
        }
        this.selfRef?.Dispose();
        GC.SuppressFinalize(this);
    }
}
