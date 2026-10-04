# Range Pickers

[← All Shiny Controls](../../README.md)

Date range, time range and date/time range pickers for both hosts, in the **core** packages. The rules live in one dependency-free engine, `Shiny.Controls.RangePickers.Shared`, so MAUI and Blazor pick exactly the same ranges and format them the same way. The engine covers which day a tap selects, whether a span crosses a blocked day, which end times a start time allows, and how "Mar 28 – Apr 2" is written.

| Control | What it is |
|---|---|
| `DateRangeCalendar` | An inline calendar: tap the first day, then the last. A third tap starts over, and tapping before the start moves the start. |
| `DateRangePicker` | A field ("Mar 3 – 9, 2026") that opens the calendar in a popup, with optional presets. |
| `TimeRangePicker` | A field ("9:00 AM – 5:30 PM") that opens two columns of times. The end column shows how long each end would make the range, and moving the start keeps that length. Ranges can run overnight. |
| `DateTimeRangePicker` | A field that opens a range calendar with a start and an end time under it. |

Each picker edits a **draft**. Only **Apply** writes the bound properties; **Cancel** or a tap outside leaves them untouched. With `AutoApply` (`DateRangePicker` only), the picker applies and closes as soon as a range is complete.

## MAUI

```xml
<shiny:DateRangeCalendar StartDate="{Binding From}" EndDate="{Binding To}" Months="1" />

<shiny:DateRangePicker StartDate="{Binding From}"
                       EndDate="{Binding To}"
                       MinDays="2" MaxDays="14" AllowSingleDay="False"
                       Presets="{Binding Presets}" />

<shiny:TimeRangePicker StartTime="{Binding ShiftStart}" EndTime="{Binding ShiftEnd}"
                       Interval="0:30:00" AllowOvernight="True" MaxDuration="12:00:00" />

<shiny:DateTimeRangePicker StartDateTime="{Binding EventStart}" EndDateTime="{Binding EventEnd}"
                           MinDuration="0:30:00" />
```

On MAUI the values follow MAUI's own pickers:

- Dates are `DateTime?` (the time part is ignored).
- Times are `TimeSpan?`.
- Date/times are `DateTime?`.

The popup is a card over a scrim in the page's overlay layer, so any `ContentPage` works; no `ShinyContentPage` or `OverlayHost` is needed. Android's back button cancels it.

## Blazor

```razor
<DateRangeCalendar @bind-StartDate="from" @bind-EndDate="to" Months="2" Presets="presets" />

<DateRangePicker @bind-StartDate="from" @bind-EndDate="to" MinDate="today" MinDays="2" MaxDays="14"
                 DisabledDates="booked" RangeChanged="OnRange" />

<TimeRangePicker @bind-StartTime="start" @bind-EndTime="end" Interval="TimeSpan.FromMinutes(30)" />

<DateTimeRangePicker @bind-StartDateTime="eventStart" @bind-EndDateTime="eventEnd" />
```

On Blazor the values are `DateOnly?`, `TimeOnly?` and `DateTime?`.

- **Popover:** an anchored dropdown that becomes a centred sheet under 600px. `DateRangePicker` shows two months and hides the second under 640px.
- **Inline calendar:** the hover preview and keyboard navigation are built in: arrows, PageUp/PageDown, Home/End, Enter.

## Shared parameters

### Date constraints

`DateRangeCalendar`, `DateRangePicker` and `DateTimeRangePicker` all take:

| Property | Default | Description |
|---|---|---|
| MinDate / MaxDate | null | The selectable window |
| MinDays / MaxDays | null | The shortest and longest span, counting both ends |
| AllowSingleDay | true | Whether a range may start and end on the same day |
| DisabledDates | null | Specific blocked days |
| DisabledDaysOfWeek | null | Blocked weekdays |
| IsDateDisabled | null | `Func<DateOnly, bool>` for anything else |
| AllowDisabledDatesInRange | false | Off: a range can't run across a blocked day (a hotel stay can't straddle a booked night). On: blocked days only can't be an end |
| FirstDayOfWeek | culture | The first calendar column |
| Presets | null | Shortcut chips, such as `DateRangePresets.Reporting(firstDay)` or `.Booking(firstDay)`, or your own `DateRangePreset` |
| Months | 1 (Blazor picker: 2) | Months shown side by side |

While the end is being picked, days that can't end the range are greyed out: too short, too long, or past a blocked day. They don't accept a tap and then refuse it.

### Time constraints

`TimeRangePicker` takes:

| Property | Default | Description |
|---|---|---|
| Interval | 15 min | The step between times |
| MinTime / MaxTime | null | The selectable part of the day |
| MinDuration / MaxDuration | one interval / null | The shortest and longest range |
| AllowOvernight | false | Lets the end wrap past midnight (22:00 – 06:00) |

An end of 00:00 always means "until midnight".

`DateTimeRangePicker` adds `Interval`, `MinDuration` and `MaxDuration` for the whole range, plus `DefaultStartTime` (09:00) and `DefaultEndTime` (17:00).

### Common picker properties

`Placeholder`, `Format` (a .NET format for both ends; null uses the compact form), `Separator` (" – "), `Culture`, `Title`, `ApplyText`/`CancelText`/`ClearText`, `ShowClear` and a two-way `IsOpen`.

- Each picker raises `RangeSelected` (MAUI, with a command too) or `RangeChanged` (Blazor) when a range is applied or cleared.

## Formatting

The field text comes from `RangeFormatter`, which drops whatever the two ends share. It follows the culture's own patterns, so German gets "3. – 9. März 2026".

| Range | Text |
|---|---|
| Same month | Mar 3 – 9, 2026 |
| Across months | Mar 28 – Apr 2, 2026 |
| Across years | Dec 28, 2025 – Jan 3, 2026 |
| Same day, with times | Mar 3, 2026, 9:00 AM – 5:00 PM |

`RangeFormatter.FormatDuration` writes "1 hr 30 min".

## The engine on its own

`Shiny.Controls.RangePickers` works with no UI at all:

- **Value types:** `DateRange` (inclusive days; `ToDateTimeRange()` gives a midnight-to-midnight span for queries), `TimeRange` (`CrossesMidnight`, `On(date)`) and `DateTimeRange`.
- **Selection:** `DateRangeSelector` (the tap state machine and per-day `CalendarDayState`), `DateRangeConstraints.Validate` and `TimeRangeConstraints` (start and end slots, `EndFor` keeping a length).
- **Calendar helpers:** `CalendarMath` (month grids) and `DateRangePresets`.
