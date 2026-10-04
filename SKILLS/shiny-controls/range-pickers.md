# Range Pickers

Date range, time range and date/time range pickers on both hosts (core packages). Rules come from the shared engine `Shiny.Controls.RangePickers.Shared` (namespace `Shiny.Controls.RangePickers`; XAML resolves it under the `shiny:` prefix; Blazor's `_Imports` already has it).

## Controls

| Control | MAUI values | Blazor values |
|---|---|---|
| `DateRangeCalendar` (inline) | `StartDate`/`EndDate` `DateTime?` (TwoWay) | `@bind-StartDate`/`@bind-EndDate` `DateOnly?` |
| `DateRangePicker` | `StartDate`/`EndDate` `DateTime?` | `DateOnly?` |
| `TimeRangePicker` | `StartTime`/`EndTime` `TimeSpan?` | `TimeOnly?` |
| `DateTimeRangePicker` | `StartDateTime`/`EndDateTime` `DateTime?` | `DateTime?` |

Events:
- MAUI: `RangeSelected` (`DateRangeSelectedEventArgs` / `TimeRangeSelectedEventArgs` / `DateTimeRangeSelectedEventArgs`, `Range` null when cleared) and `RangeSelectedCommand` (parameter: the range struct).
- Blazor: `RangeChanged` (`EventCallback<DateRange?>`, `<TimeRange?>`, `<DateTimeRange?>`).

## MAUI

```xml
<shiny:DateRangePicker StartDate="{Binding CheckIn}" EndDate="{Binding CheckOut}"
                       MinDate="{Binding Today}" MinDays="2" MaxDays="14" AllowSingleDay="False"
                       DisabledDates="{Binding BookedNights}" AutoApply="True"
                       Placeholder="Check-in – check-out" />

<shiny:TimeRangePicker StartTime="{Binding From}" EndTime="{Binding To}"
                       Interval="0:30:00" MinTime="7:00:00" MaxTime="20:00:00" />

<shiny:DateTimeRangePicker StartDateTime="{Binding Start}" EndDateTime="{Binding End}" MinDuration="0:30:00" />
```

Presets from code: `picker.Presets = [.. DateRangePresets.Reporting(CalendarMath.FirstDayOfWeek())];`

The popup goes in the page overlay. Any `ContentPage` works, with no `ShinyContentPage`/`OverlayHost` needed.

## Blazor

```razor
<DateRangePicker @bind-StartDate="from" @bind-EndDate="to" Presets="presets" AutoApply="true" />
<DateRangeCalendar @bind-StartDate="from" @bind-EndDate="to" Months="2" />
<TimeRangePicker @bind-StartTime="start" @bind-EndTime="end" AllowOvernight="true" />
<DateTimeRangePicker @bind-StartDateTime="s" @bind-EndDateTime="e" />

@code {
    IReadOnlyList<DateRangePreset> presets = DateRangePresets.Reporting(DayOfWeek.Monday);
}
```

## Parameters

### Date constraints (calendar, DateRangePicker, DateTimeRangePicker)

| Parameter | Notes |
|---|---|
| `MinDate`, `MaxDate` | The selectable window |
| `MinDays`, `MaxDays` | Inclusive span, counting both ends |
| `AllowSingleDay` | Default true |
| `DisabledDates`, `DisabledDaysOfWeek`, `IsDateDisabled` | Blocked days |
| `AllowDisabledDatesInRange` | Default false: a range can't cross a blocked day |
| `FirstDayOfWeek` | Null follows the culture |
| `Presets` | Shortcut chips |
| `Months` | 1; the Blazor picker defaults to 2 |
| `DisplayMonth` | The first visible month |

### Time constraints

- `TimeRangePicker`: `Interval` (15 min), `MinTime`, `MaxTime`, `MinDuration` (defaults to one interval), `MaxDuration`, `AllowOvernight`. An end of 00:00 means "until midnight".
- `DateTimeRangePicker`: `Interval`, `MinDuration`, `MaxDuration` (for the whole range), `DefaultStartTime` 09:00, `DefaultEndTime` 17:00.

### Common to all three pickers

`Placeholder`, `Format`, `Separator`, `Culture`, `Title`, `ApplyText`, `CancelText`, `ClearText`, `ShowClear`, `IsOpen` (two-way). `AutoApply` is `DateRangePicker` only.

## Engine (no UI)

- **Value types:** `DateRange` (`Days`, `Contains`, `EachDay`, `ToDateTimeRange()` midnight-to-midnight), `TimeRange` (`CrossesMidnight`, `Duration`, `On(date)`) and `DateTimeRange`.
- **Formatting:** `RangeFormatter.Format(range, culture?, format?, separator?)` gives "Mar 3 – 9, 2026"; `RangeFormatter.FormatDuration(span)` gives "1 hr 30 min".
- **Selection and constraints:** `DateRangeSelector` (the tap state machine) and `DateRangeConstraints.Validate(range)`, which returns a `DateRangeError`.
- **Times and calendars:** `TimeRangeConstraints.EndSlotsFor(start)` / `EndFor(start, keepDuration)`, `CalendarMath.MonthCells/FirstDayOfWeek`, and `DateRangePresets`.

## Guidance

- **Inline or popup:** use `DateRangeCalendar` when the calendar is the page (booking screens), and a picker inside forms and filter bars.
- **Value types:** on MAUI, bind `DateTime?`/`TimeSpan?` view-model properties; on Blazor, `DateOnly?`/`TimeOnly?`. Don't mix them up.
- **Hotel-style nights:** use `AllowSingleDay="False"` with `MinDays="2"`. Days counts both ends, so 2 days is one night.
- **Validating on the server:** call `new DateRangeConstraints { … }.Validate(range)`. It applies the same rules as the UI.
