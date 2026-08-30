# DurationPicker

A standalone duration picker control that opens a FloatingPanel for selection with hour/minute pickers and "hr"/"min" labels.

**Requirement:** Pages must use `ShinyContentPage` (or have an `OverlayHost` in the visual tree) for the floating panel to render.

## Usage

```xml
<shiny:DurationPicker Duration="{Binding SelectedDuration, Mode=TwoWay}"
                      MinDuration="0:15:00"
                      MaxDuration="8:00:00"
                      MinuteInterval="5"
                      Placeholder="Choose duration" />
```

| Property | Type | Default | Description |
|---|---|---|---|
| `Duration` | `TimeSpan?` | `null` | Selected duration (TwoWay bindable) |
| `MinDuration` | `TimeSpan` | `0:00:00` | Minimum allowed duration |
| `MaxDuration` | `TimeSpan` | `24:00:00` | Maximum allowed duration |
| `MinuteInterval` | `int` | `5` | Minute increment step |
| `Format` | `string` | `@"h\:mm"` | Display format string |
| `Placeholder` | `string` | `"Select duration"` | Text shown when no duration selected |
| `PlaceholderColor` | `Color` | `Gray` | Placeholder text color |
| `TextColor` | `Color?` | `null` | Selected duration text color |
| `FontSize` | `double` | `16` | Display font size |
| `Title` | `string` | `"Select Duration"` | Heading shown at the top of the picker dialog |
| `HourUnitText` | `string` | `"hr"` | Unit label next to the hours picker |
| `MinuteUnitText` | `string` | `"min"` | Unit label next to the minutes picker |
| `HoursPickerTitle` | `string` | `"Hours"` | Title of the hours `Picker` control |
| `MinutesPickerTitle` | `string` | `"Minutes"` | Title of the minutes `Picker` control |
| `DoneText` | `string` | `"Done"` | Text of the dialog's confirm button |
| `CancelText` | `string` | `"Cancel"` | Text of the dialog's cancel button |

**Event:** `DurationSelected` — fires with the selected `TimeSpan` value.

## TableView DurationPickerCell

The `DurationPickerCell` uses the same FloatingPanel-based picker internally.

| Property | Type | Default | Description |
|---|---|---|---|
| `Duration` | `TimeSpan?` | `null` | Selected duration (TwoWay bindable) |
| `MinDuration` | `TimeSpan` | `0:00:00` | Minimum allowed duration |
| `MaxDuration` | `TimeSpan` | `24:00:00` | Maximum allowed duration |
| `MinuteInterval` | `int` | `5` | Minute increment step |
| `Format` | `string` | `@"h\:mm"` | Display format string |
| `PickerTitle` | `string` | `"Select Duration"` | Heading shown at the top of the picker dialog |
| `ValueTextColor` | `Color?` | `null` | Selected duration text color, falls back to the parent TableView's `CellValueTextColor` |
| `SelectedCommand` | `ICommand?` | `null` | Executed with the new `Duration` when a value is confirmed |
| `DoneText` | `string` | `"Done"` | Text of the dialog's confirm button |
| `CancelText` | `string` | `"Cancel"` | Text of the dialog's cancel button |

**Note:** When using `DurationPickerCell` in a TableView, the page must use `ShinyContentPage` so the floating panel has an overlay host to render into.

## Width (MAUI)

`FontPickerButton`, `FontSizePickerButton` and `ColorPickerButton` shrink-wrap their trigger by
default. Setting `WidthRequest` makes the trigger fill that width, so a toolbar can size a row of
pickers to a common width without leaving a gap beside each one.
