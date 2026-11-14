# SecurityPin

A PIN/OTP entry control with individually rendered cells. Input is captured by a hidden `Entry` so the native keyboard appears when any cell is tapped. Digits are visible by default and can optionally be masked with any character for password-style entry.

## Basic Usage

```xml
<!-- 4-digit visible numeric PIN -->
<shiny:SecurityPin Length="4"
                   Value="{Binding Pin}"
                   Keyboard="Numeric" />

<!-- 6-digit masked PIN -->
<shiny:SecurityPin Length="6"
                   HideCharacter="*"
                   Value="{Binding Pin}"
                   Keyboard="Numeric" />
```

## SecurityPin Properties

| Property | Type | Default | Binding Mode | Description |
|---|---|---|---|---|
| `Length` | `int` | `4` | OneWay | Number of PIN cells |
| `Value` | `string` | `""` | TwoWay | Current entered value |
| `Keyboard` | `Keyboard` | `Numeric` | OneWay | Keyboard type applied to the hidden Entry |
| `HideCharacter` | `string?` | `null` | OneWay | When set, masks entered characters; when null/empty, shows the actual value |
| `CellSize` | `double` | `50` | OneWay | Width and height of each cell |
| `CellSpacing` | `double` | `8` | OneWay | Horizontal spacing between cells |
| `CellCornerRadius` | `double` | `8` | OneWay | Corner radius of the cell border |
| `CellBorderColor` | `Color?` | `null` | OneWay | Border stroke color (unset → theme `outline`) |
| `CellFocusedBorderColor` | `Color?` | `null` | OneWay | Border stroke color for the currently active cell (unset → theme `primary`, or `CellBorderColor` if only that is set) |
| `CellBackgroundColor` | `Color?` | `null` | OneWay | Cell fill color (unset → theme `surface-container-low`) |
| `CellFocusedBackgroundColor` | `Color?` | `null` | OneWay | Background color for the currently active cell (unset → `CellBackgroundColor`) |
| `CellTextColor` | `Color?` | `null` | OneWay | Color for the rendered digit/character (unset → theme `on-surface`) |
| `CellFocusedTextColor` | `Color?` | `null` | OneWay | Character color for the currently active cell (unset → `CellTextColor`) |
| `CellBorderThickness` | `double` | unset (theme thin, 1) | OneWay | Cell stroke width |
| `CellFocusedBorderThickness` | `double` | unset | OneWay | Stroke width for the currently active cell (unset → `CellBorderThickness`) |
| `CellBorderStyle` | `SecurityPinBorderStyle` | `Solid` | OneWay | `Solid`, `Dashed`, `Dotted` or `None` |
| `CellFocusedBorderStyle` | `SecurityPinBorderStyle?` | `null` | OneWay | Stroke style for the currently active cell (unset → `CellBorderStyle`) |
| `HighlightCurrentCell` | `bool` | `true` | OneWay | When false, the active cell is drawn like the others |
| `FontSize` | `double` | `24` | OneWay | Font size for the rendered character |
| `UseFeedback` | `bool` | `true` | OneWay | Haptic click on digit entry, long press on completion |

## Events

| Event | Args | Description |
|---|---|---|
| `Completed` | `SecurityPinCompletedEventArgs(string Value)` | Fires when the entered value reaches `Length` |

## Methods

| Method | Description |
|---|---|
| `Focus()` | Focus the hidden Entry and show the keyboard |
| `Unfocus()` | Dismiss the keyboard |
| `Clear()` | Reset `Value` to an empty string |

## Code Generation Guidance

- When the user asks for a PIN/OTP/passcode entry field, use `SecurityPin` — do not stitch together multiple `Entry` controls
- Default `Length` to 4 unless the user specifies otherwise (common alternatives: 6 for OTP codes)
- Keep `Keyboard="Numeric"` for numeric PINs; only switch to `Default` when the user asks for alphanumeric entry
- Set `HideCharacter="*"` (or `"●"`) only when the user wants masked/hidden entry — leave null for visible digits
- Wire `Completed` when the caller wants to react automatically after the last digit is entered
- Bind `Value` TwoWay to the ViewModel so the PIN is observable and `Clear()` / reassignment works as expected

## SecurityPin ViewModel Pattern

```csharp
public partial class LoginViewModel : ObservableObject
{
    [ObservableProperty]
    string pin = string.Empty;

    [RelayCommand]
    void ClearPin() => Pin = string.Empty;
}
```

```xml
<shiny:SecurityPin Length="4"
                   HideCharacter="*"
                   Value="{Binding Pin}"
                   Completed="OnPinCompleted" />
```

```csharp
void OnPinCompleted(object? sender, SecurityPinCompletedEventArgs e)
{
    // e.Value contains the full entered PIN
    _ = ((LoginViewModel)BindingContext).VerifyAsync(e.Value);
}
```

## Styled Example

```xml
<shiny:SecurityPin Length="5"
                   HideCharacter="●"
                   CellSize="48"
                   CellSpacing="10"
                   CellCornerRadius="12"
                   CellBorderColor="LightGray"
                   CellFocusedBorderColor="DodgerBlue"
                   CellBackgroundColor="#F5F5F5"
                   CellFocusedBackgroundColor="#E0EDFF"
                   CellTextColor="Black"
                   FontSize="22"
                   Value="{Binding Pin}" />
```

## Current-Cell Highlight

The "active cell" is the one waiting for the next character: the first empty cell, or the last cell once the pin is full. It only shows while the pin has focus. Each `CellFocused…` property left unset falls back to its `Cell…` twin, except `CellFocusedBorderColor`, which falls back to theme `primary`. Only set it when you want something other than `primary`.

```xml
<shiny:SecurityPin Length="4"
                   CellBorderStyle="Dashed"
                   CellFocusedBorderStyle="Solid"
                   CellFocusedBorderThickness="3"
                   CellFocusedBorderColor="DarkOrange"
                   CellFocusedBackgroundColor="#FFF3E0"
                   CellFocusedTextColor="DarkOrange" />
```

Blazor takes the same parameters (colors as CSS strings, `CellBorderStyle="SecurityPinBorderStyle.Dashed"`, `CellFocusedBorderThickness` is `double?`). The Blazor active cell also draws a focus ring, set by the `--shiny-pin-focus-ring` CSS custom property (`none` removes it).

- When the user wants the active cell to stand out, use the `CellFocused…` properties. Never swap styles from the `Focused`/`TextChanged` events.
- Use `HighlightCurrentCell="False"` when the user wants every cell to look the same.

## Behavior Notes

- Tapping any cell focuses the hidden Entry; the native keyboard appears
- The hidden Entry's `MaxLength` is synced with `Length`
- Changing `Length` rebuilds the cells; a longer `Value` is truncated to fit
- When `HideCharacter` is null or empty, the entered character is shown verbatim
- `Completed` fires whenever the value length reaches `Length` — including via programmatic assignment

### Dark mode

Leave `CellBackgroundColor` unset — it defaults to `surface-container-low` and follows the scheme.
If you pin it, pin `CellTextColor` too.

## Automation ids (MAUI)

Set `AutomationId` on the control to name its parts for UI tests (no id → no part ids). Never assign `AutomationId` twice in generated code; MAUI throws.

- `SecurityPin`: `<id>_Entry` (hidden input), `<id>_Cells` (tap target), cells `<id>_Cell_<n>`.
