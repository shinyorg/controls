using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Scheduler.Internal;

using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Scheduler;

public class SchedulerAgendaView : ContentView
{
    readonly Grid rootGrid;
    readonly AllDayEventsSection allDaySection;
    readonly DateCarouselPicker datePicker;
    readonly CalendarSheetPicker calendarSheetPicker;
    readonly Grid dayHeadersGrid;
    readonly ScrollView scrollView;
    readonly Grid columnsGrid;
    readonly ContentView loaderOverlay;
    readonly CurrentTimeIndicator timeIndicator;
    readonly List<AgendaTimelinePanel> panels = [];

    readonly List<SchedulerEvent> loadedTimedEvents = [];
    readonly AgendaDragController dragController;

    CancellationTokenSource? loadCts;
    IDispatcherTimer? timer;
    PinchGestureRecognizer? pinchGesture;
    PointerGestureRecognizer? pointerGesture;
    double startTimeSlotHeight;


    public static readonly BindableProperty ProviderProperty = BindableProperty.Create(
        nameof(Provider), typeof(ISchedulerEventProvider), typeof(SchedulerAgendaView),
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SchedulerAgendaView), () =>
            {
                ((SchedulerAgendaView)b).OnProviderChanged();
            }));

    public static readonly BindableProperty SelectedDateProperty = BindableProperty.Create(
        nameof(SelectedDate), typeof(DateOnly), typeof(SchedulerAgendaView),
        defaultValue: DateOnly.FromDateTime(DateTime.Today),
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SchedulerAgendaView), () =>
            {
                ((SchedulerAgendaView)b).OnSelectedDateChanged();
            }));

    public static readonly BindableProperty DaysToShowProperty = BindableProperty.Create(
        nameof(DaysToShow), typeof(int), typeof(SchedulerAgendaView), 1,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SchedulerAgendaView), () =>
            {
                ((SchedulerAgendaView)b).Rebuild();
            }));

    public static readonly BindableProperty ShowCarouselDatePickerProperty = BindableProperty.Create(
        nameof(ShowCarouselDatePicker), typeof(bool), typeof(SchedulerAgendaView), true,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(SchedulerAgendaView), () =>
            {
                ((SchedulerAgendaView)b).UpdateDatePickerVisibility();
            }));

    public static readonly BindableProperty DatePickerModeProperty = BindableProperty.Create(
        nameof(DatePickerMode), typeof(AgendaDatePickerMode), typeof(SchedulerAgendaView),
        AgendaDatePickerMode.Carousel,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SchedulerAgendaView), () =>
            {
                ((SchedulerAgendaView)b).UpdateDatePickerVisibility();
            }));

    public static readonly BindableProperty ShowCurrentTimeMarkerProperty = BindableProperty.Create(
        nameof(ShowCurrentTimeMarker), typeof(bool), typeof(SchedulerAgendaView), true);

    public static readonly BindableProperty EventItemTemplateProperty = BindableProperty.Create(
        nameof(EventItemTemplate), typeof(DataTemplate), typeof(SchedulerAgendaView));

    public static readonly BindableProperty DayPickerItemTemplateProperty = BindableProperty.Create(
        nameof(DayPickerItemTemplate), typeof(DataTemplate), typeof(SchedulerAgendaView),
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SchedulerAgendaView), () =>
            {
                ((SchedulerAgendaView)b).datePicker.ItemTemplate =
            ((SchedulerAgendaView)b).DayPickerItemTemplate;
            }));

    public static readonly BindableProperty LoaderTemplateProperty = BindableProperty.Create(
        nameof(LoaderTemplate), typeof(DataTemplate), typeof(SchedulerAgendaView));

    public static readonly BindableProperty CurrentTimeMarkerColorProperty = BindableProperty.Create(
        nameof(CurrentTimeMarkerColor), typeof(Color), typeof(SchedulerAgendaView), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(SchedulerAgendaView), () =>
            {
                ((SchedulerAgendaView)b).timeIndicator.MarkerColor = (Color?)n;
            }));

    public static readonly BindableProperty TimezoneColorProperty = BindableProperty.Create(
        nameof(TimezoneColor), typeof(Color), typeof(SchedulerAgendaView), null);

    public static readonly BindableProperty DefaultEventColorProperty = BindableProperty.Create(
        nameof(DefaultEventColor), typeof(Color), typeof(SchedulerAgendaView), null);

    public static readonly BindableProperty TimeSlotHeightProperty = BindableProperty.Create(
        nameof(TimeSlotHeight), typeof(double), typeof(SchedulerAgendaView), 60.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SchedulerAgendaView), () =>
            {
                ((SchedulerAgendaView)b).Rebuild();
            }));

    public static readonly BindableProperty MinDateProperty = BindableProperty.Create(
        nameof(MinDate), typeof(DateOnly?), typeof(SchedulerAgendaView));

    public static readonly BindableProperty MaxDateProperty = BindableProperty.Create(
        nameof(MaxDate), typeof(DateOnly?), typeof(SchedulerAgendaView));

    public static readonly BindableProperty AllowPanProperty = BindableProperty.Create(
        nameof(AllowPan), typeof(bool), typeof(SchedulerAgendaView), true,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(SchedulerAgendaView), () =>
            {
                ((SchedulerAgendaView)b).scrollView.Orientation =
            (bool)n ? ScrollOrientation.Vertical : ScrollOrientation.Neither;
            }));

    public static readonly BindableProperty AllowZoomProperty = BindableProperty.Create(
        nameof(AllowZoom), typeof(bool), typeof(SchedulerAgendaView), false,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SchedulerAgendaView), () =>
            {
                ((SchedulerAgendaView)b).UpdateZoomGesture();
            }));

    public static readonly BindableProperty Use24HourTimeProperty = BindableProperty.Create(
        nameof(Use24HourTime), typeof(bool), typeof(SchedulerAgendaView), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SchedulerAgendaView), () =>
            {
                ((SchedulerAgendaView)b).Rebuild();
            }));

    public static readonly BindableProperty SeparatorColorProperty = BindableProperty.Create(
        nameof(SeparatorColor), typeof(Color), typeof(SchedulerAgendaView), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SchedulerAgendaView), () =>
            {
                ((SchedulerAgendaView)b).Rebuild();
            }));

    public static readonly BindableProperty ShowAdditionalTimezonesProperty = BindableProperty.Create(
        nameof(ShowAdditionalTimezones), typeof(bool), typeof(SchedulerAgendaView), false,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SchedulerAgendaView), () =>
            {
                ((SchedulerAgendaView)b).Rebuild();
            }));

    public static readonly BindableProperty AllowEventDragProperty = BindableProperty.Create(
        nameof(AllowEventDrag), typeof(bool), typeof(SchedulerAgendaView), false,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SchedulerAgendaView), () =>
            {
                ((SchedulerAgendaView)b).UpdateDragGestures();
                ((SchedulerAgendaView)b).Rebuild();
            }));

    public static readonly BindableProperty AllowEventResizeProperty = BindableProperty.Create(
        nameof(AllowEventResize), typeof(bool), typeof(SchedulerAgendaView), false,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SchedulerAgendaView), () =>
            {
                ((SchedulerAgendaView)b).UpdateDragGestures();
                ((SchedulerAgendaView)b).Rebuild();
            }));

    public static readonly BindableProperty DragSnapMinutesProperty = BindableProperty.Create(
        nameof(DragSnapMinutes), typeof(int), typeof(SchedulerAgendaView), 15);

    public static readonly BindableProperty MinEventDurationProperty = BindableProperty.Create(
        nameof(MinEventDuration), typeof(TimeSpan), typeof(SchedulerAgendaView), TimeSpan.FromMinutes(15));

    public static readonly BindableProperty DragActivationDelayProperty = BindableProperty.Create(
        nameof(DragActivationDelay), typeof(TimeSpan), typeof(SchedulerAgendaView), TimeSpan.FromMilliseconds(350));

    public static readonly BindableProperty AllowCrossDayDragProperty = BindableProperty.Create(
        nameof(AllowCrossDayDrag), typeof(bool), typeof(SchedulerAgendaView), true);

    public static readonly BindableProperty DragSnapGuideColorProperty = BindableProperty.Create(
        nameof(DragSnapGuideColor), typeof(Color), typeof(SchedulerAgendaView), null);

    public ISchedulerEventProvider? Provider
    {
        get => (ISchedulerEventProvider?)GetValue(ProviderProperty);
        set => SetValue(ProviderProperty, value);
    }

    public DateOnly SelectedDate
    {
        get => (DateOnly)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public int DaysToShow
    {
        get => (int)GetValue(DaysToShowProperty);
        set => SetValue(DaysToShowProperty, Math.Clamp(value, 1, 7));
    }

    public bool ShowCarouselDatePicker
    {
        get => (bool)GetValue(ShowCarouselDatePickerProperty);
        set => SetValue(ShowCarouselDatePickerProperty, value);
    }

    public AgendaDatePickerMode DatePickerMode
    {
        get => (AgendaDatePickerMode)GetValue(DatePickerModeProperty);
        set => SetValue(DatePickerModeProperty, value);
    }

    public bool ShowCurrentTimeMarker
    {
        get => (bool)GetValue(ShowCurrentTimeMarkerProperty);
        set => SetValue(ShowCurrentTimeMarkerProperty, value);
    }

    public DataTemplate? EventItemTemplate
    {
        get => (DataTemplate?)GetValue(EventItemTemplateProperty);
        set => SetValue(EventItemTemplateProperty, value);
    }

    public DataTemplate? DayPickerItemTemplate
    {
        get => (DataTemplate?)GetValue(DayPickerItemTemplateProperty);
        set => SetValue(DayPickerItemTemplateProperty, value);
    }

    public DataTemplate? LoaderTemplate
    {
        get => (DataTemplate?)GetValue(LoaderTemplateProperty);
        set => SetValue(LoaderTemplateProperty, value);
    }

    public Color CurrentTimeMarkerColor
    {
        get => (Color)GetValue(CurrentTimeMarkerColorProperty);
        set => SetValue(CurrentTimeMarkerColorProperty, value);
    }

    public Color TimezoneColor
    {
        get => (Color)GetValue(TimezoneColorProperty);
        set => SetValue(TimezoneColorProperty, value);
    }

    public Color DefaultEventColor
    {
        get => (Color)GetValue(DefaultEventColorProperty);
        set => SetValue(DefaultEventColorProperty, value);
    }

    public double TimeSlotHeight
    {
        get => (double)GetValue(TimeSlotHeightProperty);
        set => SetValue(TimeSlotHeightProperty, value);
    }

    public DateOnly? MinDate
    {
        get => (DateOnly?)GetValue(MinDateProperty);
        set => SetValue(MinDateProperty, value);
    }

    public DateOnly? MaxDate
    {
        get => (DateOnly?)GetValue(MaxDateProperty);
        set => SetValue(MaxDateProperty, value);
    }

    public bool AllowPan
    {
        get => (bool)GetValue(AllowPanProperty);
        set => SetValue(AllowPanProperty, value);
    }

    public bool AllowZoom
    {
        get => (bool)GetValue(AllowZoomProperty);
        set => SetValue(AllowZoomProperty, value);
    }

    public bool Use24HourTime
    {
        get => (bool)GetValue(Use24HourTimeProperty);
        set => SetValue(Use24HourTimeProperty, value);
    }

    public Color SeparatorColor
    {
        get => (Color)GetValue(SeparatorColorProperty);
        set => SetValue(SeparatorColorProperty, value);
    }

    public bool ShowAdditionalTimezones
    {
        get => (bool)GetValue(ShowAdditionalTimezonesProperty);
        set => SetValue(ShowAdditionalTimezonesProperty, value);
    }

    public static readonly BindableProperty UseFeedbackProperty = BindableProperty.Create(
        nameof(UseFeedback), typeof(bool), typeof(SchedulerAgendaView), true);

    public bool UseFeedback
    {
        get => (bool)GetValue(UseFeedbackProperty);
        set => SetValue(UseFeedbackProperty, value);
    }

    /// <summary>Drag an event to a new time (and, when <see cref="DaysToShow"/> &gt; 1, another day).</summary>
    /// <remarks>
    /// Off by default, and the provider must also override
    /// <see cref="ISchedulerEventProvider.CanChangeEvent"/> - the opt-in is required on both sides.
    /// </remarks>
    public bool AllowEventDrag
    {
        get => (bool)GetValue(AllowEventDragProperty);
        set => SetValue(AllowEventDragProperty, value);
    }

    /// <summary>Drag the top/bottom edge of an event to change its duration.</summary>
    public bool AllowEventResize
    {
        get => (bool)GetValue(AllowEventResizeProperty);
        set => SetValue(AllowEventResizeProperty, value);
    }

    /// <summary>Snap granularity in minutes, clamped to 1-60.</summary>
    public int DragSnapMinutes
    {
        get => (int)GetValue(DragSnapMinutesProperty);
        set => SetValue(DragSnapMinutesProperty, value);
    }

    /// <summary>Resize floor. A move never changes duration, so this only gates resize.</summary>
    public TimeSpan MinEventDuration
    {
        get => (TimeSpan)GetValue(MinEventDurationProperty);
        set => SetValue(MinEventDurationProperty, value);
    }

    /// <summary>
    /// Long-press arming delay for touch - the window in which the scroller can still claim the
    /// gesture. <see cref="TimeSpan.Zero"/> arms immediately. Ignored for mouse input.
    /// </summary>
    public TimeSpan DragActivationDelay
    {
        get => (TimeSpan)GetValue(DragActivationDelayProperty);
        set => SetValue(DragActivationDelayProperty, value);
    }

    /// <summary>Only meaningful when <see cref="DaysToShow"/> &gt; 1.</summary>
    public bool AllowCrossDayDrag
    {
        get => (bool)GetValue(AllowCrossDayDragProperty);
        set => SetValue(AllowCrossDayDragProperty, value);
    }

    /// <summary>The horizontal guide line drawn at the snapped position while dragging.</summary>
    public Color? DragSnapGuideColor
    {
        get => (Color?)GetValue(DragSnapGuideColorProperty);
        set => SetValue(DragSnapGuideColorProperty, value);
    }

    /// <summary>
    /// Raised when <see cref="ISchedulerEventProvider.OnEventChanged"/> threw. The change has
    /// already been reverted; a revert with no explanation is the worst failure mode here.
    /// </summary>
    public event EventHandler<SchedulerEventChangeFailure>? EventChangeFailed;

    public IList<TimeZoneInfo> AdditionalTimezones { get; } = new List<TimeZoneInfo>();


    public SchedulerAgendaView()
    {
        allDaySection = new AllDayEventsSection();

        datePicker = new DateCarouselPicker();
        datePicker.DateSelected = date =>
        {
            if (MinDate.HasValue && date < MinDate.Value) return;
            if (MaxDate.HasValue && date > MaxDate.Value) return;
            SelectedDate = date;
        };

        calendarSheetPicker = new CalendarSheetPicker();
        calendarSheetPicker.IsVisible = false;
        calendarSheetPicker.DateSelected = date =>
        {
            if (MinDate.HasValue && date < MinDate.Value) return;
            if (MaxDate.HasValue && date > MaxDate.Value) return;
            SelectedDate = date;
        };

        timeIndicator = new CurrentTimeIndicator();
        dayHeadersGrid = new Grid();

        columnsGrid = new Grid { Padding = new Thickness(0, 10, 0, 10) };
        scrollView = new ScrollView
        {
            Content = columnsGrid,
            Orientation = ScrollOrientation.Vertical
        };

        dragController = new AgendaDragController(this, scrollView);

        loaderOverlay = new ContentView { IsVisible = false, Opacity = 0.8 };
        loaderOverlay.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);

        rootGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            RowSpacing = 0
        };

        rootGrid.Add(allDaySection, 0, 0);
        rootGrid.Add(datePicker, 0, 1);
        rootGrid.Add(calendarSheetPicker, 0, 1);
        rootGrid.Add(dayHeadersGrid, 0, 2);

        var contentGrid = new Grid();
        contentGrid.Add(scrollView);
        contentGrid.Add(loaderOverlay);
        rootGrid.Add(contentGrid, 0, 3);

        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        Content = rootGrid;
        Rebuild();
    


        // Last line: replays any styled property that was applied before the


        // children existed. See StyleGuard.


        StyleGuard.MarkReady(this, typeof(SchedulerAgendaView));

        // A handler is not disconnected when the view merely leaves the tree (a tab switch, a swapped
        // layout), so the repeating minute timer is tied to Loaded/Unloaded as well - left running it
        // is rooted by the platform timer and keeps the view and its page alive.
        Loaded += (_, _) =>
        {
            if (Handler != null)
                StartTimer();
        };
        Unloaded += (_, _) => StopTimer();


    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler != null)
            StartTimer();
        else
            StopTimer();
    }

    void StartTimer()
    {
        if (timer != null) return;
        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMinutes(1);
        timer.Tick += (_, _) => UpdateTimeMarker();
        timer.Start();
    }

    void StopTimer()
    {
        timer?.Stop();
        timer = null;
    }

    void UpdateTimeMarker()
    {
        if (!ShowCurrentTimeMarker) return;
        // Rebuild will reposition the marker
        Rebuild();
    }

    void UpdateZoomGesture()
    {
        if (pinchGesture != null)
            scrollView.GestureRecognizers.Remove(pinchGesture);

        if (AllowZoom)
        {
            pinchGesture = new PinchGestureRecognizer();
            pinchGesture.PinchUpdated += OnPinchUpdated;
            scrollView.GestureRecognizers.Add(pinchGesture);
        }
    }

    /// <summary>
    /// MAUI's PanGestureRecognizer does not report the input device, so this tracks whether a mouse
    /// has ever hovered the view - PointerEntered fires for mouse and never for touch. When it has,
    /// the drag arms immediately instead of waiting out <see cref="DragActivationDelay"/>.
    /// </summary>
    internal bool HasPointerDevice { get; private set; }

    void UpdateDragGestures()
    {
        var wanted = AllowEventDrag || AllowEventResize;

        if (pointerGesture != null)
        {
            GestureRecognizers.Remove(pointerGesture);
            pointerGesture = null;
        }
        if (!wanted) return;

        pointerGesture = new PointerGestureRecognizer();
        pointerGesture.PointerEntered += (_, _) => HasPointerDevice = true;
        GestureRecognizers.Add(pointerGesture);
    }

    void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        // A two-finger pinch that starts on an event must not also drag it.
        if (dragController.IsDragging)
        {
            dragController.Cancel();
            return;
        }

        switch (e.Status)
        {
            case GestureStatus.Started:
                startTimeSlotHeight = TimeSlotHeight;
                break;
            case GestureStatus.Running:
                TimeSlotHeight = Math.Clamp(startTimeSlotHeight * e.Scale, 20.0, 200.0);
                break;
        }
    }

    void OnProviderChanged() => LoadEvents();
    void OnSelectedDateChanged()
    {
        datePicker.SelectedDate = SelectedDate;
        calendarSheetPicker.SelectedDate = SelectedDate;
        Rebuild();
    }

    void Rebuild()
    {
        // BuildColumns discards every panel, which would orphan a view the drag is holding.
        dragController.Cancel(relayout: false);
        datePicker.SelectedDate = SelectedDate;
        datePicker.DaysToShow = DaysToShow;
        calendarSheetPicker.SelectedDate = SelectedDate;
        BuildDayHeaders();
        BuildColumns();
        LoadEvents();
    }

    int timeColumnCount;

    void BuildDayHeaders()
    {
        dayHeadersGrid.Children.Clear();
        dayHeadersGrid.ColumnDefinitions.Clear();

        var showTz = ShowAdditionalTimezones && AdditionalTimezones.Count > 0;
        var localTz = TimeZoneInfo.Local;
        var localAbbr = GetTimezoneAbbreviation(localTz);

        // Local tz header — only show label when additional timezones are visible
        dayHeadersGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(56)));
        if (showTz)
        {
            var localAbbrLabel = new Label
            {
                Text = localAbbr,
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                Padding = new Thickness(0, 4)
            };
            localAbbrLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
            dayHeadersGrid.Add(localAbbrLabel, 0);

            // Additional tz headers
            for (var t = 0; t < AdditionalTimezones.Count; t++)
            {
                dayHeadersGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(56)));
                var tz = AdditionalTimezones[t];
                var abbr = GetTimezoneAbbreviation(tz);
                var abbrLabel = new Label
                {
                    Text = abbr,
                    FontSize = 10,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                    Padding = new Thickness(0, 4)
                };
                abbrLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
                dayHeadersGrid.Add(abbrLabel, 1 + t);
            }
        }

        timeColumnCount = 1 + (showTz ? AdditionalTimezones.Count : 0);

        // Day headers
        for (var i = 0; i < DaysToShow; i++)
        {
            dayHeadersGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var date = SelectedDate.AddDays(i).ToDateTime(TimeOnly.MinValue);

            var headerLabel = new Label
            {
                Text = date.ToString("dddd, MMMM d, yyyy"),
                FontSize = DaysToShow > 1 ? 12 : 16,
                FontAttributes = FontAttributes.Bold,
                Padding = new Thickness(8, 8),
                HorizontalTextAlignment = DaysToShow > 1 ? TextAlignment.Center : TextAlignment.Start,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1
            };

            dayHeadersGrid.Add(headerLabel, timeColumnCount + i);
        }
    }

    void BuildColumns()
    {
        columnsGrid.Children.Clear();
        columnsGrid.ColumnDefinitions.Clear();
        columnsGrid.RowDefinitions.Clear();
        panels.Clear();

        var showTz = ShowAdditionalTimezones && AdditionalTimezones.Count > 0;
        var extraTzs = showTz ? AdditionalTimezones.ToList() : [];
        var localTz = TimeZoneInfo.Local;
        var timeFormat = Use24HourTime ? "HH:mm" : "h tt";

        // Match column structure: [local time] [extra tz...] [day panels...]
        columnsGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(56)));
        foreach (var _ in extraTzs)
            columnsGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(56)));
        for (var i = 0; i < DaysToShow; i++)
            columnsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        // 24 hour rows
        for (var hour = 0; hour < 24; hour++)
            columnsGrid.RowDefinitions.Add(new RowDefinition(new GridLength(TimeSlotHeight)));

        // Time labels
        var baseDate = SelectedDate.ToDateTime(TimeOnly.MinValue);
        for (var hour = 0; hour < 24; hour++)
        {
            var lbl = new Label
            {
                Text = new TimeOnly(hour, 0).ToString(timeFormat),
                TextColor = TimezoneColor,
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalTextAlignment = TextAlignment.End,
                Padding = new Thickness(0, 0, 8, 0),
                Margin = new Thickness(0, -8, 0, 0),
                VerticalOptions = LayoutOptions.Start,
                HeightRequest = 16
            }.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);
            columnsGrid.Add(lbl, 0, hour);

            for (var t = 0; t < extraTzs.Count; t++)
            {
                var localDt = new DateTime(baseDate.Year, baseDate.Month, baseDate.Day, hour, 0, 0, DateTimeKind.Local);
                var converted = TimeZoneInfo.ConvertTime(localDt, localTz, extraTzs[t]);
                var tzLbl = new Label
                {
                    Text = converted.ToString(timeFormat),
                    FontSize = 10,
                    VerticalTextAlignment = TextAlignment.Center,
                    HorizontalTextAlignment = TextAlignment.End,
                    Padding = new Thickness(0, 0, 8, 0),
                    Margin = new Thickness(0, -8, 0, 0),
                    VerticalOptions = LayoutOptions.Start,
                    HeightRequest = 16
                };
                tzLbl.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
                columnsGrid.Add(tzLbl, 1 + t, hour);
            }
        }

        // Day panels (events only)
        for (var i = 0; i < DaysToShow; i++)
        {
            var panel = new AgendaTimelinePanel
            {
                TimeSlotHeight = TimeSlotHeight,
                TimezoneColor = TimezoneColor,
                DefaultEventColor = DefaultEventColor,
                Use24HourTime = Use24HourTime,
                SeparatorColor = SeparatorColor,
                ShowTimeLabels = false,
                EventTemplate = EventItemTemplate,
                EventTapped = OnEventTapped,
                TimeSlotTapped = OnTimeSlotTapped,
                DragController = dragController,
                AllowEventDrag = AllowEventDrag,
                AllowEventResize = AllowEventResize
            };
            panels.Add(panel);
            var col = timeColumnCount + i;
            columnsGrid.Add(panel, col);
            Grid.SetRowSpan(panel, 24);
        }
    }

    static string GetTimezoneAbbreviation(TimeZoneInfo tz)
    {
        var offset = tz.BaseUtcOffset;
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var abs = offset.Duration();
        var offsetStr = abs.Minutes == 0
            ? $"UTC{sign}{abs.Hours}"
            : $"UTC{sign}{abs.Hours}:{abs.Minutes:D2}";

        // Try to get a short abbreviation from the ID
        var id = tz.Id;
        if (id.Contains('/'))
            id = id[(id.LastIndexOf('/') + 1)..].Replace("_", " ");

        return id.Length <= 10 ? $"{id}\n{offsetStr}" : offsetStr;
    }

    async void LoadEvents()
    {
        if (Provider == null) return;

        loadCts?.Cancel();
        loadCts = new CancellationTokenSource();
        var token = loadCts.Token;

        ShowLoader(true);

        try
        {
            var startDate = SelectedDate;
            var endDate = startDate.AddDays(DaysToShow);

            var start = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue));
            var end = new DateTimeOffset(endDate.ToDateTime(TimeOnly.MinValue));

            var events = await Provider.GetEvents(start, end);
            if (token.IsCancellationRequested) return;

            var allDayEvents = events.Where(e => e.IsAllDay).ToList();
            allDaySection.SetEvents(allDayEvents, EventItemTemplate, OnEventTapped);

            loadedTimedEvents.Clear();
            loadedTimedEvents.AddRange(events.Where(e => !e.IsAllDay));
            LayoutDays();

            ScrollToCurrentTime();
        }
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            // These loaders are `async void`, so anything escaping here is posted to the sync
            // context and terminates the process - including an exception from the consumer's
            // ISchedulerEventProvider. Surface it for debugging and leave the view usable rather
            // than taking the host app down.
            System.Diagnostics.Debug.WriteLine($"[Shiny.Scheduler] load failed: {ex}");
        }
        finally
        {
            if (!token.IsCancellationRequested)
                ShowLoader(false);
        }
    }

    void LayoutDays()
    {
        for (var i = 0; i < DaysToShow && i < panels.Count; i++)
        {
            var date = SelectedDate.AddDays(i);
            var dayEvents = loadedTimedEvents
                .Where(e => DateOnly.FromDateTime(e.Start.LocalDateTime) <= date &&
                            DateOnly.FromDateTime(e.End.LocalDateTime) >= date)
                .ToList();

            var indicator = i == 0 ? timeIndicator : null;
            panels[i].Build(date, dayEvents, indicator, ShowCurrentTimeMarker);
        }
    }

    // ------------- drag/resize plumbing for AgendaDragController -------------

    internal AgendaTimelinePanel? PanelAt(int index) =>
        index >= 0 && index < panels.Count ? panels[index] : null;

    internal int IndexOfPanel(AgendaTimelinePanel panel) => panels.IndexOf(panel);

    /// <summary>
    /// Re-runs the overlap clustering and rebuilds the visible days from the already-loaded events,
    /// without re-calling <see cref="ISchedulerEventProvider.GetEvents"/>. A cross-day move changes
    /// the clustering of both the source and destination day, so all visible days are relaid out.
    /// </summary>
    internal void RelayoutDays() => LayoutDays();

    internal void RaiseChangeFailed(SchedulerEventChange change, Exception ex) =>
        EventChangeFailed?.Invoke(this, new SchedulerEventChangeFailure(change, ex));

    async void ScrollToCurrentTime()
    {
        await Task.Delay(100);
        if (SelectedDate == DateOnly.FromDateTime(DateTime.Today))
        {
            var now = DateTime.Now.TimeOfDay.TotalMinutes;
            var scrollY = Math.Max(0, (now - 60) * TimeSlotHeight / 60.0);
            await scrollView.ScrollToAsync(0, scrollY, false);
        }
    }

    void ShowLoader(bool show)
    {
        if (show && loaderOverlay.Content == null)
        {
            var template = LoaderTemplate ?? DefaultTemplates.CreateLoaderTemplate();
            loaderOverlay.Content = (View)template.CreateContent();
        }
        loaderOverlay.IsVisible = show;
    }

    void OnEventTapped(SchedulerEvent evt)
    {
        if (UseFeedback)
            FeedbackHelper.Execute(this, "EventSelected");
        Provider?.OnEventSelected(evt);
    }

    void OnTimeSlotTapped(DateTimeOffset time)
    {
        if (Provider == null) return;
        if (!Provider.CanSelectAgendaTime(time)) return;

        if (UseFeedback)
            FeedbackHelper.Execute(this, "TimeSlotSelected");
        Provider.OnAgendaTimeSelected(time);
    }

    void UpdateDatePickerVisibility()
    {
        var show = ShowCarouselDatePicker;
        var mode = DatePickerMode;

        datePicker.IsVisible = show && mode == AgendaDatePickerMode.Carousel;
        calendarSheetPicker.IsVisible = show && mode == AgendaDatePickerMode.Calendar;
        // None hides both — user controls date externally
    }
}