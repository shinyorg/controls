# SecurityPin

[← All Shiny Controls](../../README.md)

A PIN entry control with individually rendered cells that captures input through a hidden Entry. Digits remain visible by default and can optionally be masked with any character.

![SecurityPin](../../assets/securitypin.png)

```xml
<shiny:SecurityPin Length="4"
                   HideCharacter="*"
                   Value="{Binding Pin}"
                   Keyboard="Numeric"
                   Completed="OnPinCompleted" />
```

| Property | Type | Default | Description |
|---|---|---|---|
| Length | int | 4 | Number of PIN cells |
| Value | string | "" | Current PIN value (TwoWay) |
| Keyboard | Keyboard | Numeric | Keyboard type for input |
| HideCharacter | string? | null | When set, masks entered characters; when null/empty, shows actual values |
| CellSize | double | 50 | Width/height of each cell |
| CellSpacing | double | 8 | Space between cells |
| CellCornerRadius | double | 8 | Border corner radius |
| CellBorderColor | Color? | null | Cell border color; unset follows the theme's `outline` |
| CellFocusedBorderColor | Color? | null | Border color for the active cell; unset uses `primary` (or `CellBorderColor` when only that is set) |
| CellBackgroundColor | Color? | null | Cell fill color; unset follows `surface-container-low` |
| CellFocusedBackgroundColor | Color? | null | Fill of the active cell; unset keeps `CellBackgroundColor` |
| CellTextColor | Color? | null | Entered character color; unset follows `on-surface` |
| CellFocusedTextColor | Color? | null | Character color of the active cell; unset keeps `CellTextColor` |
| CellBorderThickness | double | theme thin (1) | Cell stroke width |
| CellFocusedBorderThickness | double | unset | Stroke width of the active cell; unset keeps `CellBorderThickness` |
| CellBorderStyle | SecurityPinBorderStyle | Solid | `Solid`, `Dashed`, `Dotted` or `None` |
| CellFocusedBorderStyle | SecurityPinBorderStyle? | null | Stroke style of the active cell; unset keeps `CellBorderStyle` |
| HighlightCurrentCell | bool | true | Turn off to draw the active cell like the others |
| FontSize | double | 24 | Character font size |
| UseFeedback | bool | true | Enable/disable feedback on digit entry (click) and completion (long press) |

Events: `Completed` fires with a `SecurityPinCompletedEventArgs` once the entered value reaches `Length`.

Methods: `Focus()`, `Unfocus()`, `Clear()`.

## Highlighting the current cell

While the pin has focus, the cell waiting for the next character (the first empty one, or the last
cell once the pin is full) takes the `CellFocused…` appearance. Each `CellFocused…` property left
unset falls back to its plain `Cell…` twin, except the border color, which falls back to the theme's
`primary`. Set `HighlightCurrentCell="False"` to draw every cell the same.

```xml
<shiny:SecurityPin Length="4"
                   CellBorderStyle="Dashed"
                   CellFocusedBorderStyle="Solid"
                   CellFocusedBorderThickness="3"
                   CellFocusedBorderColor="DarkOrange"
                   CellFocusedBackgroundColor="#FFF3E0"
                   CellFocusedTextColor="DarkOrange" />
```

The Blazor component takes the same parameters, with colors as CSS strings and
`CellBorderStyle="SecurityPinBorderStyle.Dashed"`. On Blazor the active cell also gets a focus ring.
Override `--shiny-pin-focus-ring` with any `box-shadow` value, or `none`, to change or drop it.

## Dark mode

`CellBackgroundColor` defaults to `surface-container-low` rather than a literal white, so the cells
follow the app's scheme. Pass your own to pin it — and pass `CellTextColor` alongside it, or the
theme's ink may not read against your fill. See [Styling & theming](styling.md#dark-mode).

### Automation ids (MAUI)

Set `AutomationId` on the control and its parts are named after it, for UI tests (Appium, UI Automation, DevFlow). Without an id, the parts get none.

| Part | Id |
|---|---|
| The hidden entry that takes keystrokes | `<id>_Entry` |
| The row of cells (tap to raise the keyboard) | `<id>_Cells` |
| Each cell, by position | `<id>_Cell_0` |
