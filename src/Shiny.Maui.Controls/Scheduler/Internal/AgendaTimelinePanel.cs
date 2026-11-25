using Microsoft.Maui.Layouts;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Scheduler.Internal;

class AgendaTimelinePanel : ContentView
{
    readonly AbsoluteLayout eventsLayer;
    readonly Grid timelineGrid;
    readonly Grid outerGrid;
    double timeSlotHeight = 60;
    // null => follow the active theme pack. A consumer-supplied colour wins.
    Color? timezoneColor;
    Color? defaultEventColor;
    DataTemplate? eventTemplate;
    bool use24HourTime = true;
    Color? separatorColor;
    IReadOnlyList<TimeZoneInfo>? additionalTimezones;
    bool showTimeLabels = true;
    TapGestureRecognizer? backgroundTap;
    readonly List<(View View, SchedulerEvent Event)> eventViews = [];
    DateOnly buildDate;

    public Action<SchedulerEvent>? EventTapped { get; set; }
    public Action<DateTimeOffset>? TimeSlotTapped { get; set; }

    /// <summary>Owner-coordinated drag: this panel detects and forwards, the view decides.</summary>
    public AgendaDragController? DragController { get; set; }
    public bool AllowEventDrag { get; set; }
    public bool AllowEventResize { get; set; }

    /// <summary>
    /// The provider's <see cref="ISchedulerEventProvider.CanChangeEvent"/>, asked at build time so a
    /// locked event never gets a grip or a pan. Null (no provider) locks every event, which is what
    /// the drag controller's own arm-time check does too.
    /// </summary>
    public Func<SchedulerEvent, bool>? CanChangeEvent { get; set; }

    /// <summary>Draws the lock glyph on the default event view of a locked event.</summary>
    public bool ShowLockedIndicator { get; set; } = true;

    /// <summary>The shortest block that gets resize grips; anything shorter is move-only.</summary>
    public double MinResizableEventHeight { get; set; } = DefaultMinResizableEventHeight;

    /// <summary>The absolute layer the drag controller repositions the dragged view inside.</summary>
    public AbsoluteLayout EventsLayer => eventsLayer;

    /// <summary>The day this panel last rendered - the drag controller's frame of reference.</summary>
    public DateOnly BuildDate => buildDate;

    /// <summary>Uses the explicit colour when one was supplied, otherwise binds to the theme token.</summary>
    static void Tint(Element target, BindableProperty property, Color? explicitColor, string themeKey)
    {
        if (explicitColor is null)
        {
            target.SetDynamicResource(property, themeKey);
        }
        else
        {
            target.RemoveDynamicResource(property);
            target.SetValue(property, explicitColor);
        }
    }

    public AgendaTimelinePanel()
    {
        timelineGrid = new Grid();
        eventsLayer = new AbsoluteLayout();

        // eventsLayer is parented ONCE, here, and never removed. Build() previously cleared the
        // timeline grid and re-added it on every rebuild; re-parenting a layout hands it a fresh
        // handler, whose SetVirtualView re-adds each child's platform view - and any child still
        // attached to the previous LayoutViewGroup makes Android throw "The specified child already
        // has a parent". The hour labels and separators live in timelineGrid, which is disposable and
        // can be cleared freely; the events overlay sits beside it in this outer grid so it is never
        // torn down. Both share the same column definitions, so they stay aligned.
        outerGrid = new Grid();
        outerGrid.Add(timelineGrid);
        outerGrid.Add(eventsLayer);

        Content = outerGrid;

        // Named by the agenda view after the day it shows; the events follow this panel's id.
        AutomationIds.Track(this, () => SchedulerAutomationIds.NameEvents(this, eventViews));
    }

    public double TimeSlotHeight
    {
        get => timeSlotHeight;
        set { timeSlotHeight = value; }
    }

    public Color? TimezoneColor
    {
        get => timezoneColor;
        set { timezoneColor = value; }
    }

    public Color? DefaultEventColor
    {
        get => defaultEventColor;
        set { defaultEventColor = value; }
    }

    public DataTemplate? EventTemplate
    {
        get => eventTemplate;
        set { eventTemplate = value; }
    }

    public bool Use24HourTime
    {
        get => use24HourTime;
        set { use24HourTime = value; }
    }

    public Color? SeparatorColor
    {
        get => separatorColor;
        set { separatorColor = value; }
    }

    public IReadOnlyList<TimeZoneInfo>? AdditionalTimezones
    {
        get => additionalTimezones;
        set { additionalTimezones = value; }
    }

    public bool ShowTimeLabels
    {
        get => showTimeLabels;
        set { showTimeLabels = value; }
    }

    public void Build(DateOnly date, IReadOnlyList<SchedulerEvent> timedEvents, CurrentTimeIndicator? timeIndicator, bool showTimeMarker)
    {
        timelineGrid.Children.Clear();
        timelineGrid.RowDefinitions.Clear();
        timelineGrid.ColumnDefinitions.Clear();

        var totalHeight = 24 * timeSlotHeight;
        var extraTzs = showTimeLabels ? (additionalTimezones ?? []) : [];
        var localTz = TimeZoneInfo.Local;

        // Column layout: [local time?] [extra tz?] ... [events]
        var eventsColumnIndex = 0;
        if (showTimeLabels)
        {
            timelineGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(56)));
            foreach (var _ in extraTzs)
                timelineGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(56)));
            eventsColumnIndex = 1 + extraTzs.Count;
        }
        timelineGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        for (var hour = 0; hour < 24; hour++)
            timelineGrid.RowDefinitions.Add(new RowDefinition(new GridLength(timeSlotHeight)));

        // Same columns on the outer grid so the (never-reparented) events overlay lines up with the
        // events column of the timeline. Redefining columns does not touch child parenting.
        outerGrid.ColumnDefinitions.Clear();
        foreach (var col in timelineGrid.ColumnDefinitions)
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition(col.Width));

        Grid.SetColumn(timelineGrid, 0);
        Grid.SetColumnSpan(timelineGrid, outerGrid.ColumnDefinitions.Count);
        Grid.SetColumn(eventsLayer, eventsColumnIndex);
        Grid.SetColumnSpan(eventsLayer, 1);

        var timeFormat = use24HourTime ? "HH:mm" : "h tt";
        var localDate = date.ToDateTime(TimeOnly.MinValue);

        for (var hour = 0; hour < 24; hour++)
        {
            if (showTimeLabels)
            {
                // local timezone label
                var lbl = new Label
                {
                    Text = new TimeOnly(hour, 0).ToString(timeFormat),
                    VerticalTextAlignment = TextAlignment.Center,
                    HorizontalTextAlignment = TextAlignment.End,
                    Padding = new Thickness(0, 0, 8, 0),
                    Margin = new Thickness(0, -8, 0, 0),
                    VerticalOptions = LayoutOptions.Start,
                    HeightRequest = 16
                }.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);
                Tint(lbl, Label.TextColorProperty, timezoneColor, ShinyThemeKeys.Color.OnSurfaceVariant);
                timelineGrid.Add(lbl, 0, hour);

                // additional timezone labels
                for (var t = 0; t < extraTzs.Count; t++)
                {
                    var localDt = new DateTime(localDate.Year, localDate.Month, localDate.Day, hour, 0, 0, DateTimeKind.Local);
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
                    Tint(tzLbl, Label.TextColorProperty, timezoneColor, ShinyThemeKeys.Color.OnSurfaceVariant);
                    timelineGrid.Add(tzLbl, 1 + t, hour);
                }
            }

            var separator = new BoxView
            {
                HeightRequest = 0.5,
                VerticalOptions = LayoutOptions.Start
            };
            Tint(separator, BoxView.ColorProperty, separatorColor, ShinyThemeKeys.Color.OutlineVariant);
            timelineGrid.Add(separator, eventsColumnIndex, hour);
        }

        // events overlay content (the layer itself is parented once, in the constructor)
        eventsLayer.Children.Clear();
        eventViews.Clear();
        eventsLayer.HeightRequest = totalHeight;

        var overlaps = DetectOverlaps(timedEvents);

        foreach (var (evt, column, totalColumns) in overlaps)
        {
            var startMinutes = evt.Start.LocalDateTime.TimeOfDay.TotalMinutes;
            var endMinutes = evt.End.LocalDateTime.TimeOfDay.TotalMinutes;
            if (DateOnly.FromDateTime(evt.Start.LocalDateTime) < date)
                startMinutes = 0;
            if (DateOnly.FromDateTime(evt.End.LocalDateTime) > date)
                endMinutes = 24 * 60;
            var duration = Math.Max(endMinutes - startMinutes, 15);

            var y = AgendaGeometry.MinutesToY(startMinutes, timeSlotHeight);
            var h = AgendaGeometry.MinutesToY(duration, timeSlotHeight);

            // Only asked when drag/resize is on, so a drag-disabled view never calls the provider here.
            var locked = DragEnabled && CanChangeEvent?.Invoke(evt) != true;

            View eventView;
            if (eventTemplate != null)
            {
                eventView = (View)eventTemplate.CreateContent();
                eventView.BindingContext = evt;
            }
            else
            {
                eventView = CreateDefaultEventView(evt, locked && ShowLockedIndicator);
            }

            // Set before the view is placed, so a template's bindings to it resolve on first layout.
            if (locked)
                eventView.SetValue(SchedulerAgendaView.IsEventLockedPropertyKey, true);

            var tap = new TapGestureRecognizer();
            var captured = evt;
            tap.Tapped += (_, _) =>
            {
                // Several platforms raise Tapped on the release that ends a pan, which would select
                // the event you just dropped.
                if (DragController?.ConsumedLastGesture == true) return;
                EventTapped?.Invoke(captured);
            };
            eventView.GestureRecognizers.Add(tap);
            // The tapped view is named, not the drag wrapper: that is what a UI test presses.
            eventViews.Add((eventView, captured));

            // When neither flag is on, the tree below is byte-for-byte what it has always been:
            // the templated view goes straight into the layer with no wrapper and no recognizers.
            // A locked event is placed the same way - nothing on it suggests it can be dragged.
            var placed = DragEnabled && !locked ? WrapForDrag(eventView, captured, h) : eventView;

            AbsoluteLayout.SetLayoutBounds(placed, new Rect(
                (double)column / totalColumns, y, 1.0 / totalColumns, h));
            AbsoluteLayout.SetLayoutFlags(placed,
                AbsoluteLayoutFlags.XProportional | AbsoluteLayoutFlags.WidthProportional);

            eventsLayer.Children.Add(placed);
        }

        SchedulerAutomationIds.NameEvents(this, eventViews);

        // time marker
        var time = ShinyTime.For(this);
        if (showTimeMarker && timeIndicator != null && date == DateOnly.FromDateTime(time.Today()))
        {
            var now = time.LocalNow().TimeOfDay.TotalMinutes;
            var markerY = now * timeSlotHeight / 60.0;

            timeIndicator.UpdateTime(use24HourTime);

            AbsoluteLayout.SetLayoutBounds(timeIndicator, new Rect(0, markerY, 1, 16));
            AbsoluteLayout.SetLayoutFlags(timeIndicator, AbsoluteLayoutFlags.WidthProportional);

            // The indicator is a single instance owned by SchedulerAgendaView, but BuildColumns()
            // discards its panels and creates new ones - so the *previous* panel's layer usually
            // still has it parented. Adopt it explicitly; adding a view that already has a parent is
            // what threw "The specified child already has a parent" on Android.
            if (timeIndicator.Parent is Layout previousParent && !ReferenceEquals(previousParent, eventsLayer))
                previousParent.Remove(timeIndicator);

            if (!eventsLayer.Children.Contains(timeIndicator))
                eventsLayer.Children.Add(timeIndicator);
        }

        // Tappable background. Attached once - eventsLayer is reused across Builds, so adding a
        // recognizer here every time stacked up a new one per rebuild and fired TimeSlotTapped
        // once per accumulated recognizer.
        this.buildDate = date;
        if (this.backgroundTap is null)
        {
            this.backgroundTap = new TapGestureRecognizer();
            this.backgroundTap.Tapped += (_, e) =>
            {
                if (TimeSlotTapped == null) return;
                if (DragController?.ConsumedLastGesture == true) return;
                var pos = e.GetPosition(eventsLayer);
                if (pos.HasValue)
                {
                    var minutes = pos.Value.Y / timeSlotHeight * 60.0;
                    var rounded = Math.Floor(minutes / 30.0) * 30.0;
                    var ts = TimeSpan.FromMinutes(rounded);
                    var dt = this.buildDate.ToDateTime(new TimeOnly(ts.Hours, ts.Minutes));
                    TimeSlotTapped(new DateTimeOffset(dt));
                }
            };
            eventsLayer.GestureRecognizers.Add(this.backgroundTap);
        }

    }

    // ------------- drag / resize -------------

    const double GripHeight = 16;
    internal const double DefaultMinResizableEventHeight = GripHeight * 3;

    bool DragEnabled => DragController != null && (AllowEventDrag || AllowEventResize);

    /// <summary>
    /// Puts the templated event view in a host grid so the resize grips can sit over its top and
    /// bottom edges. Which of Move/ResizeStart/ResizeEnd a gesture is, is decided by *which view*
    /// received it - the grips are separate views with their own recognizers, so there is no
    /// coordinate math and no ambiguity.
    /// </summary>
    View WrapForDrag(View eventView, SchedulerEvent evt, double height)
    {
        var host = new Grid();
        host.Add(eventView);

        // Attached to the inner view, not the host: the grips are siblings stacked above it, so a
        // touch in a grip goes to the grip alone.
        if (AllowEventDrag)
            AttachDrag(eventView, host, evt, SchedulerEventChangeKind.Move);

        // Too short to hold two grips and still be grabbable in the middle - move only.
        if (AllowEventResize && height >= MinResizableEventHeight)
        {
            host.Add(CreateGrip(host, evt, true));
            host.Add(CreateGrip(host, evt, false));
        }
        return host;
    }


    /// <summary>
    /// Two gesture sources for one drag: a pan for the deltas, and a native touch hook underneath
    /// it.
    /// </summary>
    /// <remarks>
    /// The hook is not optional. The pan alone cannot start this drag - the enclosing ScrollView
    /// claims the gesture on both mobile platforms before the pan ever reports anything usable, and
    /// a pan cannot time the long press either, because it does not begin until the finger has
    /// already moved. So the press is taken from the raw touch-down and the pan only supplies the
    /// travel once the drag has armed. See <see cref="DragTouchHook"/>.
    /// </remarks>
    void AttachDrag(View source, View host, SchedulerEvent evt, SchedulerEventChangeKind kind)
    {
        var hook = new DragTouchHook(source);
        hook.Pressed = () => DragController?.Press(this, host, evt, kind, hook);
        hook.Released = () => DragController?.Release();

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) => HandlePan(e, host, evt, kind, hook);
        source.GestureRecognizers.Add(pan);
    }


    /// <summary>
    /// A short centred pill, not an edge-to-edge rule: a line spanning the block reads as a border
    /// on it rather than as something you can grab.
    /// </summary>
    View CreateGrip(View host, SchedulerEvent evt, bool isStart)
    {
        var bar = new BoxView
        {
            WidthRequest = 26,
            HeightRequest = 3,
            CornerRadius = 1.5,
            HorizontalOptions = LayoutOptions.Center,
            // Just clear of the block's own padding, so it sits on the edge it resizes instead of
            // over the title.
            VerticalOptions = isStart ? LayoutOptions.Start : LayoutOptions.End,
            Margin = new Thickness(0, 3),
            InputTransparent = true
        };
        TintGrip(bar, evt);

        // A 3px visual bar inside a 16px touch target.
        var grip = new Grid
        {
            HeightRequest = GripHeight,
            BackgroundColor = Colors.Transparent,
            VerticalOptions = isStart ? LayoutOptions.Start : LayoutOptions.End,
            Children = { bar }
        };

        AttachDrag(grip, host, evt,
            isStart ? SchedulerEventChangeKind.ResizeStart : SchedulerEventChangeKind.ResizeEnd);
        return grip;
    }


    /// <summary>Whatever the block is filled with, the grip has to be visible against it.</summary>
    void TintGrip(BoxView bar, SchedulerEvent evt)
    {
        var fill = evt.Color ?? defaultEventColor;
        if (fill is null)
        {
            // Same fill the default event view falls back to, so the same foreground token applies.
            bar.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.OnPrimary);
            bar.Opacity = 0.85;
            return;
        }

        // WCAG relative luminance - a white bar on a pale event block is invisible.
        var luminance = 0.2126 * Linearize(fill.Red)
                      + 0.7152 * Linearize(fill.Green)
                      + 0.0722 * Linearize(fill.Blue);

        bar.Color = luminance > 0.4 ? Colors.Black : Colors.White;
        bar.Opacity = luminance > 0.4 ? 0.45 : 0.85;
    }

    static double Linearize(float channel)
        => channel <= 0.03928f ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);


    void HandlePan(PanUpdatedEventArgs e, View host, SchedulerEvent evt, SchedulerEventChangeKind kind, DragTouchHook hook)
    {
        var controller = DragController;
        if (controller == null) return;

        switch (e.StatusType)
        {
            // The hook has normally pressed already, on the touch-down this pan grew out of; the
            // call is idempotent and this is the fallback for hosts that have no hook.
            case GestureStatus.Started:
                controller.Press(this, host, evt, kind, hook);
                break;

            case GestureStatus.Running:
                controller.Update(e.TotalX, e.TotalY);
                break;

            // iOS reports the final totals on Completed, Android reports zeros - so the controller
            // commits from the last Running values it saw and this carries nothing.
            case GestureStatus.Completed:
                _ = controller.CompleteAsync();
                break;

            case GestureStatus.Canceled:
                controller.Cancel();
                break;
        }
    }


    View CreateDefaultEventView(SchedulerEvent evt, bool showLock)
    {
        var border = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerExtraSmallRadius),
            Stroke = Colors.Transparent,
            Padding = new Thickness(6, 4),
            Margin = new Thickness(1)
        };

        var title = new Label
        {
            Text = evt.Title,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation
        }.WithFontSize(ShinyThemeKeys.Type.BodySmallSize);

        var when = new Label
        {
            Text = use24HourTime
                ? $"{evt.Start.LocalDateTime:HH:mm} - {evt.End.LocalDateTime:HH:mm}"
                : $"{evt.Start.LocalDateTime:h:mm tt} - {evt.End.LocalDateTime:h:mm tt}",
            FontSize = 10
        };

        // The event's own colour wins; otherwise the block follows the theme accent. Text sits on
        // that fill, so it tracks whichever background actually got used.
        var fill = evt.Color ?? defaultEventColor;
        if (fill is null)
        {
            border.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Primary);
            title.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);
            when.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);
            when.Opacity = 0.8;
        }
        else
        {
            border.BackgroundColor = fill;
            title.TextColor = Colors.White;
            when.TextColor = Color.FromRgba(255, 255, 255, 200);
        }

        border.Content = new VerticalStackLayout { Children = { showLock ? WithLock(title, fill) : title, when } };
        return border;
    }

    /// <summary>
    /// The title row of a locked event: the title keeps its truncation and the lock sits at the end,
    /// in the title's colour so it reads on whatever the block is filled with.
    /// </summary>
    static View WithLock(Label title, Color? fill)
    {
        var glyph = new Label
        {
            Text = LockGlyph,
            FontSize = 10,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalOptions = LayoutOptions.Center,
            Opacity = 0.85,
            InputTransparent = true
        };
        if (fill is null)
            glyph.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);
        else
            glyph.TextColor = Colors.White;
        SemanticProperties.SetDescription(glyph, "Locked");

        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        row.Add(title, 0);
        row.Add(glyph, 1);
        return row;
    }

    internal const string LockGlyph = "🔒";

    static List<(SchedulerEvent Event, int Column, int TotalColumns)> DetectOverlaps(IReadOnlyList<SchedulerEvent> events)
    {
        if (events.Count == 0) return [];

        var sorted = events.OrderBy(e => e.Start).ThenBy(e => e.End).ToList();
        var result = new List<(SchedulerEvent Event, int Column, int TotalColumns)>();
        var groups = new List<List<SchedulerEvent>>();

        foreach (var evt in sorted)
        {
            var placed = false;
            foreach (var group in groups)
            {
                if (group.Any(g => Overlaps(g, evt)))
                {
                    group.Add(evt);
                    placed = true;
                    break;
                }
            }
            if (!placed)
                groups.Add([evt]);
        }

        foreach (var group in groups)
        {
            var columns = new List<List<SchedulerEvent>>();
            foreach (var evt in group.OrderBy(e => e.Start))
            {
                var placed = false;
                for (var c = 0; c < columns.Count; c++)
                {
                    if (!columns[c].Any(e => Overlaps(e, evt)))
                    {
                        columns[c].Add(evt);
                        result.Add((evt, c, 0));
                        placed = true;
                        break;
                    }
                }
                if (!placed)
                {
                    columns.Add([evt]);
                    result.Add((evt, columns.Count - 1, 0));
                }
            }

            var totalCols = columns.Count;
            for (var i = result.Count - group.Count; i < result.Count; i++)
                result[i] = (result[i].Event, result[i].Column, totalCols);
        }

        return result;
    }

    static bool Overlaps(SchedulerEvent a, SchedulerEvent b) =>
        a.Start < b.End && b.Start < a.End;
}