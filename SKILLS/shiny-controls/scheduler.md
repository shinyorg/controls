# Scheduler Views

Three views for calendar/scheduling UI, all sharing a common `ISchedulerEventProvider` interface. Built programmatically (no XAML internals), using AOT-safe lambda bindings, with custom DataTemplate support.

## Core Interface: ISchedulerEventProvider

```csharp
public interface ISchedulerEventProvider
{
    Task<IReadOnlyList<SchedulerEvent>> GetEvents(DateTimeOffset start, DateTimeOffset end);
    void OnEventSelected(SchedulerEvent selectedEvent);
    bool CanCalendarSelect(DateOnly selectedDate);
    void OnCalendarDateSelected(DateOnly selectedDate);
    void OnAgendaTimeSelected(DateTimeOffset selectedTime);
    bool CanSelectAgendaTime(DateTimeOffset selectedTime);

    // ---- drag/resize on SchedulerAgendaView (default interface methods - override only what you need) ----

    // Gates whether this event can be dragged/resized at all. Asked when the agenda lays out events
    // (while drag/resize is on - a false hides the grips/drag affordance and marks the event locked)
    // and again when a gesture arms. Keep it cheap.
    // DEFAULTS TO FALSE: opting in requires both AllowEventDrag on the view AND this override.
    bool CanChangeEvent(SchedulerEvent evt) => false;

    // Called as the event is dragged, before commit. Return false to reject the position.
    // Must be cheap - it runs on every snap boundary crossed.
    bool CanChangeEventTo(SchedulerEventChange change) => true;

    // Called once when the gesture completes; the change is ALREADY applied optimistically.
    // Return true to keep it, false to revert. Throwing reverts and raises EventChangeFailed.
    Task<bool> OnEventChanged(SchedulerEventChange change) => Task.FromResult(false);
}
```

## SchedulerEvent Model

```csharp
public class SchedulerEvent
{
    public string Identifier { get; set; }   // Default: new Guid
    public string Title { get; set; }
    public string? Description { get; set; }
    public Color? Color { get; set; }
    public bool IsAllDay { get; set; }
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
}
```

## SchedulerEventChange Model

Produced by an agenda drag or resize and handed to `CanChangeEventTo` / `OnEventChanged`.

```csharp
public class SchedulerEventChange
{
    public required SchedulerEvent Event { get; init; }
    public required DateTimeOffset OriginalStart { get; init; }   // before the gesture
    public required DateTimeOffset OriginalEnd { get; init; }
    public required DateTimeOffset NewStart { get; init; }        // already snapped
    public required DateTimeOffset NewEnd { get; init; }          // never closer than MinEventDuration
    public required SchedulerEventChangeKind Kind { get; init; }
}

public enum SchedulerEventChangeKind { Move, ResizeStart, ResizeEnd }

// Surfaced by SchedulerAgendaView.EventChangeFailed (MAUI event / Blazor EventCallback)
public class SchedulerEventChangeFailure
{
    public SchedulerEventChange Change { get; }
    public Exception Exception { get; }
}
```

`OriginalStart`/`OriginalEnd` are the event's *true* times, not the clipped position the timeline
drew - a multi-day event that visually starts at 00:00 still reports the day it really began on.

## Supporting Models

### CalendarListDayGroup
Used for custom `DayHeaderTemplate` bindings in `SchedulerCalendarListView`:

```csharp
public class CalendarListDayGroup : List<SchedulerEvent>
{
    public DateOnly Date { get; }
    public string DateDisplay { get; }       // "dddd, MMMM d, yyyy"
    public bool IsToday { get; }
    public string EventCountDisplay { get; } // "3 events"
}
```

### AgendaDatePickerMode
Controls which date picker is shown on the `SchedulerAgendaView`:

```csharp
public enum AgendaDatePickerMode
{
    None,      // No picker — control date externally
    Carousel,  // Horizontal day picker (Apple Calendar-style, default)
    Calendar   // Collapsible month calendar sheet with pull-to-expand
}
```

### DatePickerItemContext
Used for custom `DayPickerItemTemplate` bindings in `SchedulerAgendaView`:

```csharp
public class DatePickerItemContext
{
    public DateOnly Date { get; set; }
    public string DayNumber { get; set; }   // "30"
    public string DayName { get; set; }     // "MON"
    public string MonthName { get; set; }   // "MAR"
    public bool IsSelected { get; set; }
    public bool IsToday { get; set; }
}
```

### CalendarOverflowContext
Used for custom `OverflowItemTemplate` bindings in `SchedulerCalendarView`:

```csharp
public class CalendarOverflowContext
{
    public int EventCount { get; set; }
    public DateOnly Date { get; set; }
}
```

## SchedulerCalendarView

Monthly calendar grid with swipe navigation, event display per cell, and pinch-to-zoom.

```xml
<shiny:SchedulerCalendarView
    Provider="{Binding Provider}"
    SelectedDate="{Binding SelectedDate}"
    DisplayMonth="{Binding DisplayMonth}"
    MaxEventsPerCell="3"
    CurrentDayColor="DodgerBlue" />
```

### SchedulerCalendarView Properties

| Property | Type | Default |
|----------|------|---------|
| `Provider` | `ISchedulerEventProvider?` | `null` |
| `SelectedDate` | `DateOnly` | Today (TwoWay) |
| `DisplayMonth` | `DateOnly` | Today (TwoWay) |
| `MinDate` | `DateOnly?` | `null` |
| `MaxDate` | `DateOnly?` | `null` |
| `ShowCalendarCellEventCountOnly` | `bool` | `false` |
| `EventItemTemplate` | `DataTemplate?` | `null` |
| `OverflowItemTemplate` | `DataTemplate?` | `null` |
| `LoaderTemplate` | `DataTemplate?` | `null` |
| `MaxEventsPerCell` | `int` | `3` |
| `CalendarCellColor` | `Color` | `White` |
| `CalendarCellSelectedColor` | `Color` | `LightBlue` |
| `CurrentDayColor` | `Color` | `DodgerBlue` |
| `FirstDayOfWeek` | `DayOfWeek` | `Sunday` |
| `AllowPan` | `bool` | `true` |
| `AllowZoom` | `bool` | `false` |
| `UseFeedback` | `bool` | `true` |

## SchedulerAgendaView

Day/multi-day timeline with overlap detection, Apple Calendar-style date picker, current time marker, and timezone support.

```xml
<shiny:SchedulerAgendaView
    Provider="{Binding Provider}"
    SelectedDate="{Binding SelectedDate}"
    DaysToShow="{Binding DaysToShow}"
    Use24HourTime="False"
    ShowCurrentTimeMarker="True"
    ShowCarouselDatePicker="True"
    TimeSlotHeight="60" />
```

### SchedulerAgendaView Properties

| Property | Type | Default |
|----------|------|---------|
| `Provider` | `ISchedulerEventProvider?` | `null` |
| `SelectedDate` | `DateOnly` | Today (TwoWay) |
| `MinDate` | `DateOnly?` | `null` |
| `MaxDate` | `DateOnly?` | `null` |
| `DaysToShow` | `int` | `1` (clamped 1-7) |
| `ShowCarouselDatePicker` | `bool` | `true` |
| `DatePickerMode` | `AgendaDatePickerMode` | `Carousel` |
| `ShowCurrentTimeMarker` | `bool` | `true` |
| `Use24HourTime` | `bool` | `true` |
| `EventItemTemplate` | `DataTemplate?` | `null` |
| `LoaderTemplate` | `DataTemplate?` | `null` |
| `DayPickerItemTemplate` | `DataTemplate?` | `null` |
| `CurrentTimeMarkerColor` | `Color` | `Red` |
| `TimezoneColor` | `Color` | `Gray` |
| `SeparatorColor` | `Color` | Light gray |
| `DefaultEventColor` | `Color` | `CornflowerBlue` |
| `TimeSlotHeight` | `double` | `60.0` |
| `AllowPan` | `bool` | `true` |
| `AllowZoom` | `bool` | `false` |
| `ShowAdditionalTimezones` | `bool` | `false` |
| `AdditionalTimezones` | `IList<TimeZoneInfo>` | empty |
| `UseFeedback` | `bool` | `true` |
| `AllowEventDrag` | `bool` | `false` |
| `AllowEventResize` | `bool` | `false` |
| `DragSnapMinutes` | `int` | `15` (clamped 1-60) |
| `MinEventDuration` | `TimeSpan` | 15 min |
| `DragActivationDelay` | `TimeSpan` | 350 ms |
| `AllowCrossDayDrag` | `bool` | `true` |
| `DragSnapGuideColor` | `Color?` | `null` (separator colour) |
| `MinResizableEventHeight` | `double` | `48` (shorter blocks are move-only) |
| `ShowLockedIndicator` | `bool` | `true` (🔒 on the default event view of a locked event) |
| `IsEventLocked` (attached, read-only) | `bool` | set on each event's root view - see Locked events |

`EventChangeFailed` is an `EventHandler<SchedulerEventChangeFailure>` on the view.

### Drag & resize (agenda timeline only)

```xml
<shiny:SchedulerAgendaView
    Provider="{Binding Provider}"
    AllowEventDrag="True"
    AllowEventResize="True"
    DragSnapMinutes="15"
    MinEventDuration="00:15:00"
    AllowCrossDayDrag="True" />
```

```csharp
public class MyEventProvider : ISchedulerEventProvider
{
    // ... GetEvents / OnEventSelected / etc ...

    public bool CanChangeEvent(SchedulerEvent evt)
        => !evt.IsAllDay && evt.Start > DateTimeOffset.Now;   // no editing the past

    public bool CanChangeEventTo(SchedulerEventChange change)
        => change.NewStart.LocalDateTime.TimeOfDay >= TimeSpan.FromHours(8);   // office hours only

    public async Task<bool> OnEventChanged(SchedulerEventChange change)
    {
        try
        {
            await this.api.Reschedule(change.Event.Identifier, change.NewStart, change.NewEnd);
            return true;   // keep the optimistic change
        }
        catch (HttpRequestException)
        {
            return false;  // the view reverts to OriginalStart/OriginalEnd
        }
    }
}
```

Behaviour to know:

- On touch the drag arms on a **long press** (`DragActivationDelay`); moving before it elapses hands
  the gesture back to the scroller so the timeline still scrolls. A mouse drag starts immediately.
  The press is timed from the raw touch-down, not from the pan (a pan does not begin until the
  finger has already moved), and arming disables the enclosing scroller natively for that one
  gesture. A press that arms and then never travels is still a tap: nothing is saved and the event
  is still selected.
- The change is applied **optimistically** and reverted if `OnEventChanged` returns false or throws.
- A move never changes duration; a resize clamps at `MinEventDuration` from both directions rather
  than flipping the event inside out.
- A move clamps to the day it started on rather than spilling past midnight - except on a cross-day
  drag, where the day changes instead. An event that already spilled (overnight) is never yanked back.
- Times are computed in **wall-clock** space, so a drag across a DST transition lands where the finger
  was rather than an hour off.
- All-day events are not draggable, and timed ↔ all-day conversion is not supported.
- Resize grips only appear on blocks at least `MinResizableEventHeight` tall (default 48); shorter
  events are move-only so the middle stays grabbable.
- `SchedulerCalendarView` (month grid) and `SchedulerCalendarListView` have no drag editing.

### Locked events

While `AllowEventDrag` or `AllowEventResize` is on, an event whose `CanChangeEvent` returns false is
**locked**: no grips, no pan (MAUI), no `data-draggable` / grab cursor (Blazor). It is still tappable.
The default event view draws a small 🔒 after the title (`ShowLockedIndicator="False"` hides it).
Lock state is re-read on every layout (reload, date change, drop). With drag/resize off nothing is
asked and nothing is marked.

For a custom `EventItemTemplate` on MAUI, react to the read-only attached property
`SchedulerAgendaView.IsEventLocked`, set once on the template's root view right after it is created
(AOT-safe - no string binding):

```csharp
agenda.EventItemTemplate = new DataTemplate(() =>
{
    var title = new Label();
    title.SetBinding(Label.TextProperty, static (SchedulerEvent e) => e.Title);
    var badge = new Label { Text = "Read-only", IsVisible = false };

    var root = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
    root.Add(title, 0);
    root.Add(badge, 1);

    root.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName == SchedulerAgendaView.IsEventLockedProperty.PropertyName)
            badge.IsVisible = SchedulerAgendaView.GetIsEventLocked(root);
    };
    return root;
});
```

On Blazor, locked events carry the `shiny-agenda-event--locked` class (the built-in lock is a
`.shiny-agenda-event-lock` span inside the title).

## SchedulerCalendarListView

Vertically scrolling event list grouped by day with infinite scroll forward/backward.

```xml
<shiny:SchedulerCalendarListView
    Provider="{Binding Provider}"
    SelectedDate="{Binding SelectedDate}"
    DaysPerPage="30"
    DefaultEventColor="CornflowerBlue" />
```

### SchedulerCalendarListView Properties

| Property | Type | Default |
|----------|------|---------|
| `Provider` | `ISchedulerEventProvider?` | `null` |
| `SelectedDate` | `DateOnly` | Today (TwoWay) |
| `MinDate` | `DateOnly?` | `null` |
| `MaxDate` | `DateOnly?` | `null` |
| `EventItemTemplate` | `DataTemplate?` | `null` |
| `DayHeaderTemplate` | `DataTemplate?` | `null` |
| `LoaderTemplate` | `DataTemplate?` | `null` |
| `DaysPerPage` | `int` | `30` |
| `DefaultEventColor` | `Color` | `CornflowerBlue` |
| `DayHeaderBackgroundColor` | `Color` | `Transparent` |
| `DayHeaderTextColor` | `Color` | `Black` |
| `StickyDayHeaders` | `bool` | `true` |
| `AllowPan` | `bool` | `true` |
| `AllowZoom` | `bool` | `false` |

`StickyDayHeaders` pins the current day group's header to the top of the list while scrolling (input-transparent — scroll gestures pass through it). Disable it for a plain grouped list. `EventItemTemplate` and `DayHeaderTemplate` re-apply immediately when changed at runtime.

## Default Templates

The `DefaultTemplates` static class provides reusable AOT-safe templates:

| Method | Binds To |
|--------|----------|
| `CreateEventItemTemplate()` | `SchedulerEvent` — Color bar + title |
| `CreateOverflowTemplate()` | `CalendarOverflowContext` — "+N more" |
| `CreateLoaderTemplate()` | None — ActivityIndicator + "Loading..." |
| `CreateCalendarListDayHeaderTemplate()` | `CalendarListDayGroup` — Accent bar + today dot + bold date + event count |
| `CreateCalendarListEventItemTemplate()` | `SchedulerEvent` — Card with color bar, title, description, time range |
| `CreateAppleCalendarDayPickerTemplate()` | `DatePickerItemContext` — Apple Calendar-style day picker |

## Event Provider Implementation

```csharp
public class MyEventProvider : ISchedulerEventProvider
{
    public async Task<IReadOnlyList<SchedulerEvent>> GetEvents(DateTimeOffset start, DateTimeOffset end)
    {
        // Fetch events from your data source for the given range
        // Multi-day events should be included if they overlap the range at all
        return events;
    }

    public void OnEventSelected(SchedulerEvent selectedEvent)
    {
        // Handle event taps — navigate to detail, show dialog, etc.
    }

    public bool CanCalendarSelect(DateOnly selectedDate) => true;
    public void OnCalendarDateSelected(DateOnly selectedDate) { }
    public void OnAgendaTimeSelected(DateTimeOffset selectedTime) { }
    public bool CanSelectAgendaTime(DateTimeOffset selectedTime) => true;
}
```

## Scheduler ViewModel Pattern

```csharp
public class MySchedulerViewModel : INotifyPropertyChanged
{
    DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);

    public MySchedulerViewModel(ISchedulerEventProvider provider)
    {
        Provider = provider;
    }

    public ISchedulerEventProvider Provider { get; }

    public DateOnly SelectedDate
    {
        get => _selectedDate;
        set { _selectedDate = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

Register in DI:
```csharp
builder.Services.AddSingleton<ISchedulerEventProvider, MyEventProvider>();
```

## Scheduler Page Pattern

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:shiny="http://shiny.net/maui/controls"
             xmlns:vm="clr-namespace:MyApp.ViewModels"
             x:Class="MyApp.Pages.MyCalendarPage"
             x:DataType="vm:MySchedulerViewModel"
             Title="Calendar">

    <shiny:SchedulerCalendarListView
        Provider="{Binding Provider}"
        SelectedDate="{Binding SelectedDate}" />
</ContentPage>
```

Code-behind with constructor injection:
```csharp
public partial class MyCalendarPage : ContentPage
{
    public MyCalendarPage(MySchedulerViewModel viewModel)
    {
        BindingContext = viewModel;
        InitializeComponent();
    }
}
```

## Agenda with Calendar Picker Example

```xml
<shiny:SchedulerAgendaView
    Provider="{Binding Provider}"
    SelectedDate="{Binding SelectedDate}"
    DaysToShow="{Binding DaysToShow}"
    DatePickerMode="Calendar"
    ShowCarouselDatePicker="True"
    ShowCurrentTimeMarker="True"
    CurrentTimeMarkerColor="Red"
    DefaultEventColor="CornflowerBlue"
    TimeSlotHeight="60"
    Use24HourTime="False" />
```

The calendar picker starts collapsed showing only the week of the selected date. Pull the handle down or tap it to expand to a full month view with navigation arrows. Tapping a date selects it and updates the agenda. The picker auto-navigates months when a date outside the current display month is selected.

## Custom Template Example (AOT-Safe)

```csharp
var myEventTemplate = new DataTemplate(() =>
{
    var grid = new Grid
    {
        ColumnDefinitions =
        {
            new ColumnDefinition(new GridLength(4)),
            new ColumnDefinition(GridLength.Star)
        },
        Padding = new Thickness(8),
        ColumnSpacing = 8
    };

    var colorBar = new BoxView { CornerRadius = 2 };
    colorBar.SetBinding(BoxView.ColorProperty, static (SchedulerEvent e) => e.Color);

    var title = new Label { FontSize = 14 };
    title.SetBinding(Label.TextProperty, static (SchedulerEvent e) => e.Title);

    grid.Add(colorBar, 0);
    grid.Add(title, 1);
    return grid;
});
```

## Blazor

All three views ship as Razor components in `Shiny.Blazor.Controls` (namespace `Shiny.Blazor.Controls.Scheduler`) with the same `ISchedulerEventProvider` interface and models. Differences: colors are CSS strings, `SelectedDate` binds via `@bind-SelectedDate` (`SelectedDateChanged` callback), and there are no `DataTemplate` properties.

The Blazor `SchedulerAgendaView` has full feature parity with MAUI:

```razor
<SchedulerAgendaView Provider="provider"
                     @bind-SelectedDate="selectedDate"
                     DaysToShow="3"
                     DatePickerMode="AgendaDatePickerMode.Calendar"
                     ShowAdditionalTimezones="true"
                     AdditionalTimezones="additionalTimezones"
                     ShowCurrentTimeMarker="true"
                     CurrentTimeMarkerColor="#EF4444"
                     DefaultEventColor="#6366F1"
                     TimeSlotHeight="60"
                     Use24HourTime="false" />
```

Blazor agenda parameters: `Provider`, `SelectedDate` (+`SelectedDateChanged`), `DaysToShow` (1–7, clamped), `ShowCarouselDatePicker`, `DatePickerMode` (`None` / `Carousel` / `Calendar` — `Calendar` renders a full month picker with ‹/› navigation and today highlight), `ShowAdditionalTimezones`, `AdditionalTimezones` (`IList<TimeZoneInfo>` — extra time columns with UTC offset labels), `ShowCurrentTimeMarker`, `CurrentTimeMarkerColor` (default `#EF4444`), `DefaultEventColor` (default `#6366F1`), `TimeSlotHeight`, `MinDate`, `MaxDate`, `Use24HourTime`. Overlapping events lay out side-by-side with proportional widths; the current time marker refreshes itself every minute without re-rendering the component.

Blazor `SchedulerAgendaView` and `SchedulerCalendarListView` scroll themselves (the agenda has a 500px,
the list a 400px `min-height`), so place them where they get a real height: a full-height flex column with
`flex: 1; min-height: 0`, never a fixed-height box inside a scrolling page (two scrollers on one axis).
Don't wrap any of the three views in another bordered card flush to their edge either: each draws its
own outer border.

Blazor agenda drag/resize mirrors MAUI - `AllowEventDrag`, `AllowEventResize`, `DragSnapMinutes`,
`MinEventDuration`, `DragActivationDelay`, `AllowCrossDayDrag`, `DragSnapGuideColor` (a CSS colour
string), `MinResizableEventHeight` (px, default 48), `ShowLockedIndicator` (default true), plus `EventChangeFailed` as an `EventCallback<SchedulerEventChangeFailure>` and one
Blazor-only parameter:

| Parameter | Type | Default | Notes |
|-----------|------|---------|-------|
| `DragValidationMode` | `AgendaDragValidationMode` | `OnCommit` | `OnCommit` never round-trips to .NET while the pointer moves. `PerPosition` calls `CanChangeEventTo` on every snap boundary so a rejected position shows live - one interop hop per boundary, visibly slower on WASM. |

```razor
<SchedulerAgendaView Provider="provider"
                     @bind-SelectedDate="selectedDate"
                     AllowEventDrag="true"
                     AllowEventResize="true"
                     DragSnapMinutes="15"
                     MinEventDuration="TimeSpan.FromMinutes(15)"
                     DragValidationMode="AgendaDragValidationMode.PerPosition"
                     EventChangeFailed="OnChangeFailed" />
```

The drag uses Pointer Events (not HTML5 drag-and-drop), so it works with mouse, touch and pen. It sets
`touch-action: none` on draggable events, which means a touch drag that *starts on an event* no longer
scrolls the timeline - drag from the empty background to scroll.

Blazor `SchedulerCalendarListView` parameters: `Provider`, `SelectedDate` (+`SelectedDateChanged`), `DaysPerPage`, `DefaultEventColor`, `DayHeaderBackgroundColor`, `DayHeaderTextColor`, `MinDate`, `MaxDate`.

## Scheduler Important Notes

- All three views extend `ContentView` — place anywhere in your XAML layout
- `SelectedDate` is `TwoWay` on all views — changes propagate back to the ViewModel
- All views support `MinDate`/`MaxDate` to constrain navigation and selection
- All views support `AllowPan` and `AllowZoom` to control gesture interactions
- All views support `LoaderTemplate` for custom loading indicators
- The provider's `GetEvents()` may be called with any date range — implement it to handle arbitrary ranges
- Multi-day events should span the full range (the views handle per-day duplication internally)
- `SchedulerCalendarListView` skips empty days (only days with events are shown)
- `SchedulerAgendaView` supports three date picker modes via `DatePickerMode`: `Carousel` (Apple Calendar-style day picker, default), `Calendar` (collapsible month calendar sheet with pull-to-expand), or `None` (hides both pickers for external date control)
- When using `DatePickerMode="Carousel"`, override with `DayPickerItemTemplate` using `DatePickerItemContext`
- `SchedulerAgendaView` supports 24-hour and 12-hour (AM/PM) time via `Use24HourTime`
- `SchedulerAgendaView` supports multiple timezone columns via `AdditionalTimezones` + `ShowAdditionalTimezones`
- All-day events always sort to top within their day group
- Custom templates must use AOT-safe static lambda bindings, never string-based bindings
- `AdditionalTimezones` is an `IList<TimeZoneInfo>` — add timezones in code-behind
- **`SchedulerEvent.Identifier` must be unique when drag/resize is enabled on Blazor.** It defaults to
  a `Guid`, so this is safe unless you assign your own. Blazor matches the dragged event across the JS
  boundary by identifier; a duplicate makes the drag a silent no-op (traced to `Debug`) rather than
  moving the wrong event. MAUI matches by object reference and is unaffected.
- Drag/resize is **off by default and additive**: with `AllowEventDrag`/`AllowEventResize` unset, no
  gesture recognizers are attached on MAUI, no JS module is imported on Blazor, and the rendered tree
  is unchanged. The three new provider members are default interface methods, so existing
  `ISchedulerEventProvider` implementations compile and behave exactly as before.
- **Let `GetEvents()` throw and it is now handled, not fatal.** The views load through `async void`, so
  before this an exception from your provider (failed request, malformed data) escaped to the
  synchronization context and terminated the app. It is now caught, written to `Debug`, and the view is
  left usable — but you still get no user-facing error, so surface failures from inside your provider
  (return an empty list and raise your own state) rather than relying on the throw.
- Leave the Scheduler's colour properties unset to follow the active theme — `CalendarCellColor`,
  `CalendarCellSelectedColor`, `CurrentDayColor`, `TimezoneColor`, `DefaultEventColor`,
  `SeparatorColor` and `CurrentTimeMarkerColor` all default to `null` and resolve from theme tokens.
  A `SchedulerEvent.Color` (or a user's `BubbleColor`) still wins for that item, since per-item colour
  is data.

### Dark mode

Leave `CalendarCellColor`, `CalendarCellSelectedColor`, `CurrentDayColor` and the day-header colours
unset — they default to theme tokens. Event chip colours are yours to pick per event; the label ink
is derived from each chip's colour automatically, so do not set a text colour on them.

## Automation ids (MAUI)

Set `AutomationId` on the control and its parts are named after it, for UI tests (Appium, UI Automation, DevFlow). With no id on the control, the parts get none.

| Part | Id |
|---|---|
| Calendar view: each day cell, by date | `<id>_Day_20261019` |
| Calendar view: an event in a day, by `SchedulerEvent.Identifier` | `<id>_Day_20261019_Event_<identifier>` |
| Calendar view: a day's "+N more" | `<id>_Day_20261019_More` |
| Calendar view: previous / next month, month title | `<id>_PreviousMonth / <id>_NextMonth / <id>_MonthTitle` |
| Agenda view: carousel date picker, and its days | `<id>_DatePicker → <id>_DatePicker_Day_20261019` |
| Agenda view: calendar sheet picker, its days, month buttons, title | `<id>_CalendarPicker → <id>_CalendarPicker_Day_20261019, …_PreviousMonth, …_NextMonth, …_MonthTitle` |
| Agenda view: the sheet picker's expand handle | `<id>_CalendarPicker_Expand` |
| Agenda view: all-day strip, and its events | `<id>_AllDay → <id>_AllDay_Event_<identifier>` |
| Agenda view: each day's timeline (the time-slot tap target), and its events | `<id>_DayColumn_20261019 → <id>_DayColumn_20261019_Event_<identifier>` |
| Agenda view: each day's header | `<id>_DayHeader_20261019` |
| Calendar list view: the list | `<id>_List` |

Days are keyed by date and events by `Identifier` (set it to something stable in tests; it defaults to a new GUID). The calendar list view's rows are recycled template rows, so they carry whatever id your `EventItemTemplate` gives them.
