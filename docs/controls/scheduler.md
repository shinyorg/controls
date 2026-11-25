# Scheduler

[← All Shiny Controls](../../README.md)

Calendar and agenda views for displaying events and appointments, powered by `ISchedulerEventProvider`.

| Calendar | Agenda | Event List |
|:---:|:---:|:---:|
| ![Calendar](../../assets/scheduler1.png) | ![Agenda](../../assets/scheduler2.png) | ![Event List](../../assets/scheduler3.png) |

**SchedulerCalendarView** - Month calendar grid with event indicators, swipe navigation, and date selection.

```xml
<shiny:SchedulerCalendarView
    Provider="{Binding Provider}"
    SelectedDate="{Binding SelectedDate}"
    DisplayMonth="{Binding DisplayMonth}" />
```

**SchedulerAgendaView** - Day/multi-day timeline with time slots, overlapping event layout, current time marker, optional timezone columns, and switchable date picker modes (carousel, calendar sheet, or none).

```xml
<shiny:SchedulerAgendaView
    Provider="{Binding Provider}"
    SelectedDate="{Binding SelectedDate}"
    DaysToShow="{Binding DaysToShow}"
    DatePickerMode="Calendar"
    ShowAdditionalTimezones="{Binding ShowAdditionalTimezones}" />
```

**DatePickerMode** options: `Carousel` (default horizontal day picker), `Calendar` (collapsible month calendar with pull-to-expand), `None` (no picker).

**Drag & drop event editing** (agenda timeline only) - drag an event to a new time, across day columns when `DaysToShow > 1`, or drag its top/bottom grip to change its duration. Off by default and additive: with `AllowEventDrag`/`AllowEventResize` unset, no gesture recognizers are attached (MAUI), no JS module is imported (Blazor), and the rendered tree is unchanged.

```xml
<shiny:SchedulerAgendaView
    Provider="{Binding Provider}"
    AllowEventDrag="True"
    AllowEventResize="True"
    DragSnapMinutes="15"
    MinEventDuration="00:15:00"
    AllowCrossDayDrag="True" />
```

| Property | Default | Notes |
| --- | --- | --- |
| `AllowEventDrag` | `false` | Move an event to a new time (and, when `DaysToShow > 1`, another day). |
| `AllowEventResize` | `false` | Drag the top/bottom edge to change duration. |
| `DragSnapMinutes` | `15` | Snap granularity, clamped to 1-60. |
| `MinEventDuration` | 15 min | Resize floor. A move never changes duration. |
| `DragActivationDelay` | 350 ms | Long-press arming delay for touch; mouse never waits. `Zero` arms immediately. |
| `AllowCrossDayDrag` | `true` | Only meaningful when `DaysToShow > 1`. |
| `DragSnapGuideColor` | separator colour | The guide line drawn at the snapped position. |
| `MinResizableEventHeight` | `48` | Shortest event block (DIPs / px) that gets resize grips; shorter events are move-only. |
| `ShowLockedIndicator` | `true` | Draws a small 🔒 on the default event block of a locked event. |

On touch the drag arms on a long press, so a vertical swipe still scrolls the timeline; with a mouse it starts immediately. The long press is measured from the touch itself, and arming disables the enclosing scroller natively for that one gesture — a press that arms and then never moves is still a tap, and still selects the event. The change is committed optimistically and reverted if the provider says no. All-day events are not draggable, and timed ↔ all-day conversion is not supported.

**Locked events** - while `AllowEventDrag` or `AllowEventResize` is on, the view asks the provider's `CanChangeEvent` for each event as it lays them out. An event it refuses is *locked*: it gets no grips, no move gesture (MAUI) and no `data-draggable` / grab cursor (Blazor), so nothing suggests it can be dragged - it is still tappable and still selects. The default event view shows a small 🔒 after the title (turn it off with `ShowLockedIndicator="False"`). For a custom `EventItemTemplate` on MAUI, read the read-only attached property `SchedulerAgendaView.IsEventLocked`, which is set on the template's root view; on Blazor, locked events carry the `shiny-agenda-event--locked` CSS class. The answer is re-read whenever the view lays out again (a reload, a date change, a drop), and `CanChangeEvent` is still checked again when a gesture arms. With drag/resize off nothing is asked and nothing is marked.

```csharp
agenda.EventItemTemplate = new DataTemplate(() =>
{
    var title = new Label();
    title.SetBinding(Label.TextProperty, static (SchedulerEvent e) => e.Title);
    var badge = new Label { Text = "Read-only", IsVisible = false };

    var root = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
    root.Add(title, 0);
    root.Add(badge, 1);

    // Set on the template root once, right after it is created and before it is laid out.
    root.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName == SchedulerAgendaView.IsEventLockedProperty.PropertyName)
            badge.IsVisible = SchedulerAgendaView.GetIsEventLocked(root);
    };
    return root;
});
```

**SchedulerCalendarListView** - Scrollable event list grouped by day with infinite scroll loading and sticky day headers (`StickyDayHeaders`, on by default, pins the current day's header to the top while scrolling).

```xml
<shiny:SchedulerCalendarListView
    Provider="{Binding Provider}"
    SelectedDate="{Binding SelectedDate}" />
```

The Blazor `SchedulerAgendaView` has the same feature set — `DaysToShow` (1–7 day columns), `DatePickerMode` (`Carousel` / `Calendar` / `None`), `ShowAdditionalTimezones` + `AdditionalTimezones` side-by-side timezone columns, overlap-aware event layout, and an auto-updating current time marker — using CSS color strings instead of `Color`.

On Blazor all three views draw one outer border, and the cells and columns inside never draw a line
along it: the calendar's last column and last week, the agenda's last day column and last hour, and the
list's day headers (which draw only their bottom line) sit flush against the frame. The agenda and the
list scroll themselves, so give them a real height (fill a full-height column with `flex: 1; min-height: 0`)
rather than a fixed height inside a scrolling page.

**ISchedulerEventProvider** - Implement this interface to supply event data:

```csharp
public class MyEventProvider : ISchedulerEventProvider
{
    public Task<IReadOnlyList<SchedulerEvent>> GetEvents(DateTimeOffset start, DateTimeOffset end) { ... }
    public void OnEventSelected(SchedulerEvent selectedEvent) { ... }
    public bool CanCalendarSelect(DateOnly selectedDate) => true;
    public void OnCalendarDateSelected(DateOnly selectedDate) { }
    public bool CanSelectAgendaTime(DateTimeOffset selectedTime) => true;
    public void OnAgendaTimeSelected(DateTimeOffset selectedTime) { }

    // drag/drop - all three are default interface methods, so existing providers still compile
    public bool CanChangeEvent(SchedulerEvent evt) => true;                    // defaults to false
    public bool CanChangeEventTo(SchedulerEventChange change) => true;
    public async Task<bool> OnEventChanged(SchedulerEventChange change) { ... } // defaults to false
}
```

`CanChangeEvent` defaults to `false`, so a provider that ignores drag/drop can never have its events moved even if an app sets `AllowEventDrag` - the opt-in is required on both the view and the provider (and such a provider's events all show as locked). It is asked at layout time as well as when a gesture arms, so keep it cheap. `SchedulerEventChange` carries the event, its original `Start`/`End`, the proposed (already snapped) `NewStart`/`NewEnd`, and a `Kind` of `Move` / `ResizeStart` / `ResizeEnd`. Returning `false` from `OnEventChanged` reverts; throwing reverts and raises `EventChangeFailed` on the view.

On Blazor, events are matched across the JS boundary by `SchedulerEvent.Identifier` (a `Guid` by default) - duplicate identifiers make a drag a no-op rather than move the wrong event. Blazor also has `DragValidationMode`: `OnCommit` (default, no interop while the pointer moves) or `PerPosition` (`CanChangeEventTo` per snap boundary, which is visibly slower on WASM).

## Dark mode

`CalendarCellColor`, `CalendarCellSelectedColor`, `CurrentDayColor` and the day-header colours default
to theme tokens rather than literals, so the month grid follows the app's scheme.

Event chips are the exception: their background is a colour the app picks per event, so no single ink
works for all of them — a fixed white disappears on a pale amber and a fixed near-black disappears on
a deep indigo. The label colour is derived from each chip's own colour instead, which holds in both
schemes. See [Styling & theming](styling.md#dark-mode).

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
