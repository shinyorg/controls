using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Shiny.Controls.Gantt;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Gantt;

public partial class GanttView
{
    // Every property that changes what is on screen routes through one of three verbs, so the
    // rebuild story stays legible: Model rebuilds the plan, Layout re-measures and re-places, and
    // Paint just invalidates the drawables. Getting that wrong is how a control ends up rebuilding
    // its entire task graph because someone changed a grid line colour.
    static BindableProperty Model<T>(string name, T? defaultValue = default) =>
        Declare(name, defaultValue, static v => v.RebuildModel());

    static BindableProperty Layout<T>(string name, T? defaultValue = default) =>
        Declare(name, defaultValue, static v => v.RelayoutTimeline());

    static BindableProperty Paint<T>(string name, T? defaultValue = default) =>
        Declare(name, defaultValue, static v => v.Repaint());

    static BindableProperty Inert<T>(string name, T? defaultValue = default) =>
        Declare<T>(name, defaultValue, null);

    static BindableProperty Declare<T>(string name, T? defaultValue, Action<GanttView>? changed) =>
        BindableProperty.Create(
            name,
            typeof(T),
            typeof(GanttView),
            defaultValue,
            propertyChanged: changed is null
                ? null
                : (b, _, _) => StyleGuard.WhenReady(b, typeof(GanttView), () => changed((GanttView)b))
        );


    // =============================================================================================
    // Data
    // =============================================================================================

    public static readonly BindableProperty TasksProperty = BindableProperty.Create(
        nameof(Tasks), typeof(IEnumerable), typeof(GanttView),
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(GanttView), () => ((GanttView)b).OnItemsChanged(o, n)));

    public static readonly BindableProperty DependenciesProperty = BindableProperty.Create(
        nameof(Dependencies), typeof(IEnumerable), typeof(GanttView),
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(GanttView), () => ((GanttView)b).OnItemsChanged(o, n)));

    public static readonly BindableProperty CalendarProperty = Model<GanttCalendar>(nameof(Calendar));
    public static readonly BindableProperty RollUpSummariesProperty = Model(nameof(RollUpSummaries), true);
    public static readonly BindableProperty ShowCriticalPathProperty = Model(nameof(ShowCriticalPath), false);
    public static readonly BindableProperty CriticalSlackThresholdProperty = Model(nameof(CriticalSlackThreshold), TimeSpan.Zero);

    /// <summary>
    /// The plan. Items must be <see cref="GanttTask"/>; map your own type onto one and hang the
    /// original off <see cref="GanttTask.Item"/>. An <see cref="ObservableCollection{T}"/> is watched
    /// for changes, as is each task's own <c>PropertyChanged</c>.
    /// </summary>
    public IEnumerable? Tasks
    {
        get => (IEnumerable?)this.GetValue(TasksProperty);
        set => this.SetValue(TasksProperty, value);
    }

    /// <summary>The links between tasks. Items must be <see cref="GanttDependency"/>.</summary>
    public IEnumerable? Dependencies
    {
        get => (IEnumerable?)this.GetValue(DependenciesProperty);
        set => this.SetValue(DependenciesProperty, value);
    }

    /// <summary>Working-time rules. Null means <see cref="GanttCalendar.Continuous"/> — no weekends, no shifts.</summary>
    public GanttCalendar? Calendar
    {
        get => (GanttCalendar?)this.GetValue(CalendarProperty);
        set => this.SetValue(CalendarProperty, value);
    }

    /// <summary>Whether a summary's dates come from its children. On by default.</summary>
    public bool RollUpSummaries
    {
        get => (bool)this.GetValue(RollUpSummariesProperty);
        set => this.SetValue(RollUpSummariesProperty, value);
    }

    /// <summary>Whether critical tasks and links are highlighted. Off by default; computing it is not free.</summary>
    public bool ShowCriticalPath
    {
        get => (bool)this.GetValue(ShowCriticalPathProperty);
        set => this.SetValue(ShowCriticalPathProperty, value);
    }

    /// <summary>How little slack still counts as critical. Zero is the strict definition.</summary>
    public TimeSpan CriticalSlackThreshold
    {
        get => (TimeSpan)this.GetValue(CriticalSlackThresholdProperty);
        set => this.SetValue(CriticalSlackThresholdProperty, value);
    }

    /// <summary>The resolved plan, rebuilt on every data change. Null before the first build.</summary>
    public GanttModel? PlanModel { get; private set; }


    // =============================================================================================
    // Time axis
    // =============================================================================================

    public static readonly BindableProperty StartDateProperty = Layout<DateTimeOffset?>(nameof(StartDate));
    public static readonly BindableProperty EndDateProperty = Layout<DateTimeOffset?>(nameof(EndDate));
    public static readonly BindableProperty TimeScaleProperty = Layout(nameof(TimeScale), GanttTimeScale.Auto);
    public static readonly BindableProperty PixelsPerDayProperty = Layout(nameof(PixelsPerDay), ThemeTokens.Unset);
    public static readonly BindableProperty PaddingDaysProperty = Layout(nameof(PaddingDays), 2d);
    public static readonly BindableProperty CultureProperty = Layout<CultureInfo>(nameof(Culture));

    /// <summary>First instant on the timeline. Null derives it from the plan, less <see cref="PaddingDays"/>.</summary>
    public DateTimeOffset? StartDate
    {
        get => (DateTimeOffset?)this.GetValue(StartDateProperty);
        set => this.SetValue(StartDateProperty, value);
    }

    /// <summary>Last instant on the timeline. Null derives it from the plan, plus <see cref="PaddingDays"/>.</summary>
    public DateTimeOffset? EndDate
    {
        get => (DateTimeOffset?)this.GetValue(EndDateProperty);
        set => this.SetValue(EndDateProperty, value);
    }

    /// <summary>
    /// Header granularity. <see cref="GanttTimeScale.Auto"/> follows the zoom level, which is what
    /// makes pinching feel like a real timeline rather than a stretched image.
    /// </summary>
    public GanttTimeScale TimeScale
    {
        get => (GanttTimeScale)this.GetValue(TimeScaleProperty);
        set => this.SetValue(TimeScaleProperty, value);
    }

    /// <summary>The zoom level. Left unset it is derived from <see cref="TimeScale"/>.</summary>
    public double PixelsPerDay
    {
        get => (double)this.GetValue(PixelsPerDayProperty);
        set => this.SetValue(PixelsPerDayProperty, value);
    }

    /// <summary>Breathing room added to each end of a derived date range.</summary>
    public double PaddingDays
    {
        get => (double)this.GetValue(PaddingDaysProperty);
        set => this.SetValue(PaddingDaysProperty, value);
    }

    /// <summary>Culture for header labels and cell formatting. Null uses the current culture.</summary>
    public CultureInfo? Culture
    {
        get => (CultureInfo?)this.GetValue(CultureProperty);
        set => this.SetValue(CultureProperty, value);
    }


    // =============================================================================================
    // Metrics
    // =============================================================================================

    public static readonly BindableProperty RowHeightProperty = Layout(nameof(RowHeight), 32d);
    public static readonly BindableProperty BarHeightProperty = Layout(nameof(BarHeight), 18d);
    public static readonly BindableProperty SummaryBarHeightProperty = Layout(nameof(SummaryBarHeight), 10d);
    public static readonly BindableProperty MilestoneSizeProperty = Layout(nameof(MilestoneSize), 14d);
    public static readonly BindableProperty HeaderHeightProperty = Layout(nameof(HeaderHeight), 48d);
    public static readonly BindableProperty TaskPaneWidthProperty = Layout(nameof(TaskPaneWidth), 260d);
    public static readonly BindableProperty ShowTaskPaneProperty = Layout(nameof(ShowTaskPane), true);
    public static readonly BindableProperty IsTaskPaneResizableProperty = Layout(nameof(IsTaskPaneResizable), true);

    public double RowHeight
    {
        get => (double)this.GetValue(RowHeightProperty);
        set => this.SetValue(RowHeightProperty, value);
    }

    public double BarHeight
    {
        get => (double)this.GetValue(BarHeightProperty);
        set => this.SetValue(BarHeightProperty, value);
    }

    /// <summary>Height of the summary bracket. Shorter than a bar on purpose — it is chrome, not work.</summary>
    public double SummaryBarHeight
    {
        get => (double)this.GetValue(SummaryBarHeightProperty);
        set => this.SetValue(SummaryBarHeightProperty, value);
    }

    public double MilestoneSize
    {
        get => (double)this.GetValue(MilestoneSizeProperty);
        set => this.SetValue(MilestoneSizeProperty, value);
    }

    /// <summary>Total height of the two-tier timeline header.</summary>
    public double HeaderHeight
    {
        get => (double)this.GetValue(HeaderHeightProperty);
        set => this.SetValue(HeaderHeightProperty, value);
    }

    /// <summary>Width of the task grid on the left.</summary>
    public double TaskPaneWidth
    {
        get => (double)this.GetValue(TaskPaneWidthProperty);
        set => this.SetValue(TaskPaneWidthProperty, value);
    }

    /// <summary>Whether the task grid is shown at all. Off gives a bare timeline.</summary>
    public bool ShowTaskPane
    {
        get => (bool)this.GetValue(ShowTaskPaneProperty);
        set => this.SetValue(ShowTaskPaneProperty, value);
    }

    /// <summary>Whether the splitter between the two panes can be dragged.</summary>
    public bool IsTaskPaneResizable
    {
        get => (bool)this.GetValue(IsTaskPaneResizableProperty);
        set => this.SetValue(IsTaskPaneResizableProperty, value);
    }

    /// <summary>The task pane's columns. The control seeds a sensible default set when left empty.</summary>
    public IList<GanttColumn> Columns { get; } = new ObservableCollection<GanttColumn>();

    /// <summary>Shaded bands drawn behind the bars — a sprint, a freeze window, a release train.</summary>
    public IList<GanttHighlightRange> HighlightRanges { get; } = new ObservableCollection<GanttHighlightRange>();


    // =============================================================================================
    // Display
    // =============================================================================================

    public static readonly BindableProperty ShowDependenciesProperty = Paint(nameof(ShowDependencies), true);
    public static readonly BindableProperty ShowBaselinesProperty = Paint(nameof(ShowBaselines), true);
    public static readonly BindableProperty ShowProgressProperty = Paint(nameof(ShowProgress), true);
    public static readonly BindableProperty ShowDeadlinesProperty = Paint(nameof(ShowDeadlines), true);
    public static readonly BindableProperty ShowNonWorkingShadingProperty = Paint(nameof(ShowNonWorkingShading), true);
    public static readonly BindableProperty ShowTodayMarkerProperty = Paint(nameof(ShowTodayMarker), true);
    public static readonly BindableProperty ShowGridLinesProperty = Paint(nameof(ShowGridLines), true);
    public static readonly BindableProperty ShowRowSeparatorsProperty = Paint(nameof(ShowRowSeparators), true);
    public static readonly BindableProperty BarLabelProperty = Paint(nameof(BarLabel), GanttBarLabel.Right);

    public bool ShowDependencies
    {
        get => (bool)this.GetValue(ShowDependenciesProperty);
        set => this.SetValue(ShowDependenciesProperty, value);
    }

    public bool ShowBaselines
    {
        get => (bool)this.GetValue(ShowBaselinesProperty);
        set => this.SetValue(ShowBaselinesProperty, value);
    }

    public bool ShowProgress
    {
        get => (bool)this.GetValue(ShowProgressProperty);
        set => this.SetValue(ShowProgressProperty, value);
    }

    public bool ShowDeadlines
    {
        get => (bool)this.GetValue(ShowDeadlinesProperty);
        set => this.SetValue(ShowDeadlinesProperty, value);
    }

    /// <summary>Whether weekends and holidays are shaded. Needs a <see cref="Calendar"/> to show anything.</summary>
    public bool ShowNonWorkingShading
    {
        get => (bool)this.GetValue(ShowNonWorkingShadingProperty);
        set => this.SetValue(ShowNonWorkingShadingProperty, value);
    }

    public bool ShowTodayMarker
    {
        get => (bool)this.GetValue(ShowTodayMarkerProperty);
        set => this.SetValue(ShowTodayMarkerProperty, value);
    }

    public bool ShowGridLines
    {
        get => (bool)this.GetValue(ShowGridLinesProperty);
        set => this.SetValue(ShowGridLinesProperty, value);
    }

    public bool ShowRowSeparators
    {
        get => (bool)this.GetValue(ShowRowSeparatorsProperty);
        set => this.SetValue(ShowRowSeparatorsProperty, value);
    }

    /// <summary>Where a bar's name is drawn, if anywhere.</summary>
    public GanttBarLabel BarLabel
    {
        get => (GanttBarLabel)this.GetValue(BarLabelProperty);
        set => this.SetValue(BarLabelProperty, value);
    }


    // =============================================================================================
    // Colours. All null by default, which means "follow the theme".
    // =============================================================================================

    public static readonly BindableProperty BarColorProperty = Paint<Color>(nameof(BarColor));
    public static readonly BindableProperty SummaryColorProperty = Paint<Color>(nameof(SummaryColor));
    public static readonly BindableProperty MilestoneColorProperty = Paint<Color>(nameof(MilestoneColor));
    public static readonly BindableProperty CriticalColorProperty = Paint<Color>(nameof(CriticalColor));
    public static readonly BindableProperty ProgressColorProperty = Paint<Color>(nameof(ProgressColor));
    public static readonly BindableProperty DependencyColorProperty = Paint<Color>(nameof(DependencyColor));
    public static readonly BindableProperty TodayColorProperty = Paint<Color>(nameof(TodayColor));
    public static readonly BindableProperty NonWorkingColorProperty = Paint<Color>(nameof(NonWorkingColor));
    public static readonly BindableProperty GridLineColorProperty = Paint<Color>(nameof(GridLineColor));
    public static readonly BindableProperty SelectionColorProperty = Paint<Color>(nameof(SelectionColor));
    public static readonly BindableProperty BaselineColorProperty = Paint<Color>(nameof(BaselineColor));
    public static readonly BindableProperty DeadlineColorProperty = Paint<Color>(nameof(DeadlineColor));

    public Color? BarColor
    {
        get => (Color?)this.GetValue(BarColorProperty);
        set => this.SetValue(BarColorProperty, value);
    }

    public Color? SummaryColor
    {
        get => (Color?)this.GetValue(SummaryColorProperty);
        set => this.SetValue(SummaryColorProperty, value);
    }

    public Color? MilestoneColor
    {
        get => (Color?)this.GetValue(MilestoneColorProperty);
        set => this.SetValue(MilestoneColorProperty, value);
    }

    public Color? CriticalColor
    {
        get => (Color?)this.GetValue(CriticalColorProperty);
        set => this.SetValue(CriticalColorProperty, value);
    }

    public Color? ProgressColor
    {
        get => (Color?)this.GetValue(ProgressColorProperty);
        set => this.SetValue(ProgressColorProperty, value);
    }

    public Color? DependencyColor
    {
        get => (Color?)this.GetValue(DependencyColorProperty);
        set => this.SetValue(DependencyColorProperty, value);
    }

    public Color? TodayColor
    {
        get => (Color?)this.GetValue(TodayColorProperty);
        set => this.SetValue(TodayColorProperty, value);
    }

    public Color? NonWorkingColor
    {
        get => (Color?)this.GetValue(NonWorkingColorProperty);
        set => this.SetValue(NonWorkingColorProperty, value);
    }

    public Color? GridLineColor
    {
        get => (Color?)this.GetValue(GridLineColorProperty);
        set => this.SetValue(GridLineColorProperty, value);
    }

    public Color? SelectionColor
    {
        get => (Color?)this.GetValue(SelectionColorProperty);
        set => this.SetValue(SelectionColorProperty, value);
    }

    public Color? BaselineColor
    {
        get => (Color?)this.GetValue(BaselineColorProperty);
        set => this.SetValue(BaselineColorProperty, value);
    }

    public Color? DeadlineColor
    {
        get => (Color?)this.GetValue(DeadlineColorProperty);
        set => this.SetValue(DeadlineColorProperty, value);
    }


    // =============================================================================================
    // Behaviour
    // =============================================================================================

    public static readonly BindableProperty IsReadOnlyProperty = Inert(nameof(IsReadOnly), false);
    public static readonly BindableProperty AllowMoveProperty = Inert(nameof(AllowMove), true);
    public static readonly BindableProperty AllowResizeProperty = Inert(nameof(AllowResize), true);
    public static readonly BindableProperty AllowProgressChangeProperty = Inert(nameof(AllowProgressChange), true);
    public static readonly BindableProperty AllowDependencyEditProperty = Inert(nameof(AllowDependencyEdit), false);
    public static readonly BindableProperty AllowZoomProperty = Inert(nameof(AllowZoom), true);
    public static readonly BindableProperty SnapModeProperty = Inert(nameof(SnapMode), GanttSnapMode.Scale);
    public static readonly BindableProperty SnapIntervalProperty = Inert(nameof(SnapInterval), TimeSpan.FromHours(1));
    public static readonly BindableProperty CascadeModeProperty = Inert(nameof(CascadeMode), GanttCascadeMode.PushOnly);
    public static readonly BindableProperty MinimumDurationProperty = Inert(nameof(MinimumDuration), TimeSpan.Zero);

    public static readonly BindableProperty SelectionModeProperty = Paint(nameof(SelectionMode), GanttSelectionMode.Single);

    public static readonly BindableProperty SelectedTaskProperty = BindableProperty.Create(
        nameof(SelectedTask), typeof(GanttTask), typeof(GanttView), null,
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(GanttView), () => ((GanttView)b).OnSelectedTaskChanged()));

    /// <summary>Blocks every edit gesture in one switch, whatever the individual Allow flags say.</summary>
    public bool IsReadOnly
    {
        get => (bool)this.GetValue(IsReadOnlyProperty);
        set => this.SetValue(IsReadOnlyProperty, value);
    }

    public bool AllowMove
    {
        get => (bool)this.GetValue(AllowMoveProperty);
        set => this.SetValue(AllowMoveProperty, value);
    }

    public bool AllowResize
    {
        get => (bool)this.GetValue(AllowResizeProperty);
        set => this.SetValue(AllowResizeProperty, value);
    }

    public bool AllowProgressChange
    {
        get => (bool)this.GetValue(AllowProgressChangeProperty);
        set => this.SetValue(AllowProgressChangeProperty, value);
    }

    /// <summary>
    /// Whether dragging from a bar's connector dot draws a new link. Off by default: on a phone the
    /// dots compete with the resize grips for the same few pixels.
    /// </summary>
    public bool AllowDependencyEdit
    {
        get => (bool)this.GetValue(AllowDependencyEditProperty);
        set => this.SetValue(AllowDependencyEditProperty, value);
    }

    /// <summary>Whether pinch and the zoom methods change the scale.</summary>
    public bool AllowZoom
    {
        get => (bool)this.GetValue(AllowZoomProperty);
        set => this.SetValue(AllowZoomProperty, value);
    }

    /// <summary>What a drag rounds to. Defaults to the current header scale.</summary>
    public GanttSnapMode SnapMode
    {
        get => (GanttSnapMode)this.GetValue(SnapModeProperty);
        set => this.SetValue(SnapModeProperty, value);
    }

    /// <summary>The unit for <see cref="GanttSnapMode.Interval"/>.</summary>
    public TimeSpan SnapInterval
    {
        get => (TimeSpan)this.GetValue(SnapIntervalProperty);
        set => this.SetValue(SnapIntervalProperty, value);
    }

    /// <summary>How far an edit ripples through dependent tasks.</summary>
    public GanttCascadeMode CascadeMode
    {
        get => (GanttCascadeMode)this.GetValue(CascadeModeProperty);
        set => this.SetValue(CascadeModeProperty, value);
    }

    /// <summary>The shortest a task may be resized to.</summary>
    public TimeSpan MinimumDuration
    {
        get => (TimeSpan)this.GetValue(MinimumDurationProperty);
        set => this.SetValue(MinimumDurationProperty, value);
    }

    public GanttSelectionMode SelectionMode
    {
        get => (GanttSelectionMode)this.GetValue(SelectionModeProperty);
        set => this.SetValue(SelectionModeProperty, value);
    }

    /// <summary>The selected task in <see cref="GanttSelectionMode.Single"/>. Two-way.</summary>
    public GanttTask? SelectedTask
    {
        get => (GanttTask?)this.GetValue(SelectedTaskProperty);
        set => this.SetValue(SelectedTaskProperty, value);
    }

    /// <summary>Every selected task in <see cref="GanttSelectionMode.Multiple"/>.</summary>
    public IList<GanttTask> SelectedTasks { get; } = new ObservableCollection<GanttTask>();


    // =============================================================================================
    // Templates and commands
    // =============================================================================================

    public static readonly BindableProperty BarTemplateProperty = Layout<DataTemplate>(nameof(BarTemplate));
    public static readonly BindableProperty EmptyViewProperty = Layout<View>(nameof(EmptyView));

    public static readonly BindableProperty TaskTappedCommandProperty = Inert<ICommand>(nameof(TaskTappedCommand));
    public static readonly BindableProperty TaskChangedCommandProperty = Inert<ICommand>(nameof(TaskChangedCommand));
    public static readonly BindableProperty DependencyCreatedCommandProperty = Inert<ICommand>(nameof(DependencyCreatedCommand));

    /// <summary>
    /// Replaces the drawn bar with a view per task, bound to the <see cref="GanttTask"/>.
    /// </summary>
    /// <remarks>
    /// Left null the control draws every bar onto one canvas, which is dramatically cheaper for a
    /// plan of any size and is what gives milestones and summary brackets their shapes. Setting a
    /// template switches to realizing a view per visible row instead — reach for it when a bar needs
    /// something a drawing cannot give you, such as an embedded avatar or a nested control.
    /// </remarks>
    public DataTemplate? BarTemplate
    {
        get => (DataTemplate?)this.GetValue(BarTemplateProperty);
        set => this.SetValue(BarTemplateProperty, value);
    }

    /// <summary>Shown in place of the timeline when the plan has no tasks.</summary>
    public View? EmptyView
    {
        get => (View?)this.GetValue(EmptyViewProperty);
        set => this.SetValue(EmptyViewProperty, value);
    }

    public ICommand? TaskTappedCommand
    {
        get => (ICommand?)this.GetValue(TaskTappedCommandProperty);
        set => this.SetValue(TaskTappedCommandProperty, value);
    }

    public ICommand? TaskChangedCommand
    {
        get => (ICommand?)this.GetValue(TaskChangedCommandProperty);
        set => this.SetValue(TaskChangedCommandProperty, value);
    }

    public ICommand? DependencyCreatedCommand
    {
        get => (ICommand?)this.GetValue(DependencyCreatedCommandProperty);
        set => this.SetValue(DependencyCreatedCommandProperty, value);
    }


    // =============================================================================================
    // Events
    // =============================================================================================

    /// <summary>
    /// Raised before a drag is committed, with the whole cascade in hand. Cancel it to abandon the
    /// edit, or amend <see cref="GanttSchedulePlan"/> before it lands.
    /// </summary>
    public event EventHandler<GanttTaskChangingEventArgs>? TaskChanging;

    /// <summary>Raised after a drag has been applied. Keep the plan to implement undo.</summary>
    public event EventHandler<GanttTaskChangedEventArgs>? TaskChanged;

    public event EventHandler<GanttTaskEventArgs>? TaskTapped;
    public event EventHandler<GanttTaskEventArgs>? TaskDoubleTapped;

    /// <summary>Raised when a link is drawn. Cancel it to refuse.</summary>
    public event EventHandler<GanttDependencyEventArgs>? DependencyCreated;

    public event EventHandler<GanttDependencyEventArgs>? DependencyRemoved;
    public event EventHandler? SelectionChanged;

    /// <summary>Raised when zooming crosses into a different header scale.</summary>
    public event EventHandler? ScaleChanged;

    /// <summary>
    /// Raised after every model rebuild, before the first paint. The place to read
    /// <see cref="GanttModel.Issues"/> — a plan that fails validation still renders, so nothing else
    /// tells you it was wrong.
    /// </summary>
    public event EventHandler? PlanBuilt;
}
