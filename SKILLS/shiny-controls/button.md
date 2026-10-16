# ShinyButton

A button with a leading and a trailing icon slot, a real working state, and success/error states.
`ShinyButton` on both hosts — the name avoids colliding with `Microsoft.Maui.Controls.Button`.

Reach for it instead of a plain `Button` whenever the tap starts work that takes time, or whenever the
button needs an icon. Use `Fab` for a floating primary action and `IconTextTool` for the small tools
docked inside a `TextEntry`.

## Basic Usage

```xml
<!-- Nothing here binds IsBusy. SaveCommand is an AsyncRelayCommand; the button drives its own
     busy state for exactly as long as the command runs. -->
<shiny:ShinyButton Text="Save"
                   BusyText="Saving..."
                   LeftMotionIcon="download"
                   Command="{Binding SaveCommand}" />

<!-- Submit, spin, tick -->
<shiny:ShinyButton Text="Submit"
                   State="{Binding SubmitState}"
                   BusyText="Submitting..."
                   SuccessText="Submitted"
                   ErrorText="Failed"
                   StateRevertDelay="0:0:2"
                   Command="{Binding SubmitCommand}" />

<!-- Appearance (emphasis) and Type (meaning) are orthogonal -->
<shiny:ShinyButton Text="Delete" Appearance="Outlined" Type="Critical" LeftMotionIcon="trash" />
<shiny:ShinyButton Text="Cancel" Appearance="Text" />
<shiny:ShinyButton Text="Brand"  ButtonBackgroundColor="#E91E63" TextColor="White" />

<!-- Icon-only: give it a description, since there is no label to read -->
<shiny:ShinyButton LeftMotionIcon="settings"
                   CornerRadius="22"
                   ContentPadding="11"
                   SemanticProperties.Description="Settings"
                   Command="{Binding SettingsCommand}" />
```

## State

`ButtonState` is `Normal` / `Busy` / `Success` / `Error`.

- `State` is TwoWay by default, so an auto-revert reports itself back to the view model.
- `IsBusy` is a shorthand projection for view models that only have an `IsSaving` flag. Setting it
  true enters `Busy`; setting it false returns to `Normal` **only if currently busy**, so it cannot
  cut a `Success` or `Error` short — which matters, because a view model clearing its flag in a
  `finally` is exactly when the outcome is on screen.
- `Success` and `Error` revert to `Normal` after `StateRevertDelay` (default 1.5s).
  `TimeSpan.Zero` holds the state indefinitely.
- Motion playback waits for the button to be loaded, so a styled-in `State="Busy"` does not start a
  ticker on a view with no window.

Bind `State` when the view model owns the outcome; bind `IsBusy` when it only owns a flag; bind
neither and let the command drive it (see below).

## Command state (MAUI only)

Two separate behaviours, both on by default:

1. **`CanExecute`** — the button follows the command's `CanExecuteChanged`. It does this through
   MAUI's own `IsEnabledCore` rather than writing `IsEnabled`, so a button with an explicit
   `IsEnabled="False"` (or a binding) **stays** disabled when the command becomes executable again.
2. **`AutoBusy`** — if the command exposes an `ExecutionTask` or an `IsRunning`/`IsExecuting` flag
   (MVVM Toolkit's `AsyncRelayCommand`, Prism's, ReactiveUI's, most hand-rolled ones), the button
   enters `Busy` on tap and leaves it when the work finishes — `Error` if it faulted and
   `ShowErrorOnFault` is set. A command that sets `State` itself has the last word: the button only
   unwinds if it is still the one holding `Busy`.

   A command that can fault must not rethrow through `async void`: MVVM Toolkit's
   `AsyncRelayCommand.Execute` does exactly that by default, which is an unhandled exception that
   can take a MAUI app down. Generate `[RelayCommand(FlowExceptionsToTaskScheduler = true)]` on any
   command whose failure should just show `Error` — the button still sees the faulted `ExecutionTask`.

   Detection is by cached reflection, so no MVVM package is referenced. For full trimming /
   NativeAOT, set `AutoBusy="False"` and drive `State` from the view model.

```csharp
public partial class OrderViewModel : ObservableObject
{
    [ObservableProperty]
    ButtonState submitState = ButtonState.Normal;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    bool isValid;

    // AutoBusy handles the spinner. Do not add an IsBusy property for this.
    [RelayCommand(CanExecute = nameof(IsValid))]
    async Task SaveAsync() => await this.api.SaveAsync();

    // Reports its own outcome; the button respects the Success rather than resetting to Normal.
    [RelayCommand]
    async Task SubmitAsync()
    {
        await this.api.SubmitAsync();
        this.SubmitState = ButtonState.Success;
    }
}
```

## Busy modes

| Mode | Behaviour |
|---|---|
| `ReplaceLeftIcon` (default) | Indicator takes the left icon's place; the text stays. Both are `IconSize` square, so the button cannot change width. |
| `ReplaceContent` | Content fades to opacity 0 — keeping its layout space, so the button holds the width it had — and a centred indicator takes over. |
| `KeepContent` | Indicator appears after the right icon; nothing else moves. |

The indicator is, in order: `BusyIconView` if set → a motion icon if `BusyMotionIcon` is set
(default `loader`) → a platform `ActivityIndicator` when `BusyMotionIcon` is cleared.

## Icon slots

Each side takes three forms, in ascending precedence:

1. `LeftIcon` / `RightIcon` — an `ImageSource`.
2. `LeftMotionIcon` / `RightMotionIcon` — the name of a motion icon (see `motion-icons.md`). The
   button colours it from its own foreground and plays one cycle on tap
   (`MotionIconPlayOnClick`).
3. `LeftIconView` / `RightIconView` — any `View`: a `BadgeView`, an avatar, a control of your own.
   Consumer-supplied views are not resized by the button.

`ContentLayout` is `Sides` (default), `Top` or `Bottom` — named for where the **icons** sit relative
to the text.

Prefer motion icons: they are in-box, follow the foreground through disabled and theme changes, and
animate on tap. A MAUI `Image` cannot be tinted, so `IconColor` reaches a `FontImageSource` glyph and
a motion icon but leaves a PNG the colour it was drawn.

The success/error glyphs play once and then return to their **resting** artwork, not their final
frame (motion icons use `FillMode.None` on both hosts). Every built-in icon's motion ends at its
resting value, so `check` and `warning` look the same either way — but if you point
`SuccessMotionIcon` at your own artwork, author its motion to end where it rests or the glyph will
snap when the cycle finishes.

## Appearance × Type

`Appearance` is emphasis, `Type` is meaning; they are independent so a destructive action can be loud
(`Filled` + `Critical`) or quiet (`Text` + `Critical`) without an enum member per pair.

| Appearance | Background | Foreground | Stroke |
|---|---|---|---|
| `Filled` (default) | `{Type}` | `On{Type}` | none |
| `Tonal` | `{Type}Container` | `On{Type}Container` | none |
| `Outlined` | transparent | `{Type}` | `Outline`, 1px |
| `Text` | transparent | `{Type}` | none |
| `Elevated` | `SurfaceContainerLow` | `{Type}` | none, plus shadow |

`Type` is `Primary` (default) / `Secondary` / `Success` / `Warning` / `Critical` / `Info`, resolved
from the theme tokens via `SetDynamicResource`, so `ShinyThemeManager.SetTheme` restyles live.

Every explicit colour property (`ButtonBackgroundColor`, `TextColor`, `BorderColor`, `IconColor`)
short-circuits its token. **Leave them unset unless you mean to pin the colour** — an explicit colour
survives every theme swap.

## Properties (MAUI)

**Text** — `Text`, `TextColor?`, `FontSize` (15), `FontFamily?`, `FontAttributes`,
`CharacterSpacing`, `LineBreakMode` (`NoWrap`).

**Surface** — `Appearance` (`Filled`), `Type` (`Primary`), `ButtonBackgroundColor?`, `BorderColor?`,
`BorderThickness` (`-1` = appearance decides), `CornerRadius` (10), `ContentPadding`
(`16,10`), `HasShadow` (`bool?`, null = appearance decides), `DisabledOpacity` (0.38),
`PressedOpacity` (0.6).

**Icons** — `LeftIcon?`, `RightIcon?`, `LeftMotionIcon?`, `RightMotionIcon?`, `LeftIconView?`,
`RightIconView?`, `IconSize` (20), `IconColor?`, `IconSpacing` (8), `ContentLayout` (`Sides`),
`MotionIconPlayOnClick` (`true`), `MotionIconStrokeWidth` (2).

**State** — `State` (TwoWay), `IsBusy` (TwoWay), `BusyMode` (`ReplaceLeftIcon`), `BusyText?`,
`SuccessText?`, `ErrorText?`, `BusyMotionIcon` (`"loader"`), `BusyIconView?`, `SuccessMotionIcon`
(`"check"`), `ErrorMotionIcon` (`"warning"`), `SuccessIcon?`, `ErrorIcon?`, `StateRevertDelay`
(1.5s), `DisableWhileBusy` (`true`), `AutoBusy` (`true`), `ShowErrorOnFault` (`true`).

**Command** — `Command?`, `CommandParameter?`, `UseFeedback` (`true`).

Events: `Clicked`, `StateChanged(ButtonStateChangedEventArgs)` with `From` and `To`.

`ContentPadding` is named apart from `Padding` deliberately: `ContentView.Padding` sits *outside* the
painted surface, which is not what a button's padding means.

There are no `Pressed`/`Released` events. A `ContentView` has no portable touch-down signal —
`TapGestureRecognizer` only reports the release — so press feedback is a flash to `PressedOpacity`
and back rather than a held state. Set `PressedOpacity="1"` to turn it off.

## Blazor

Parameters mirror MAUI one-for-one. Renders a real `<button type="button">`, so keyboard, focus ring
and `disabled` come free.

There is no `ICommand` on the web, so the command-state work is **MAUI-only**. Its equivalent is that
`Clicked` is awaited: with `AutoBusy` (default) an `async` handler holds the button busy for exactly
as long as it runs, and a synchronous handler never flickers because the task is checked for
completion before any state change. A handler that throws sets `Error` and the exception is still
rethrown — and an exception escaping a Blazor event handler shows the error UI (WASM) or kills the
circuit (Server). So generated handlers should **catch their own failures** and report them through a
bound `State`:

```razor
<ShinyButton Text="Upload" ErrorText="Failed" @bind-State="uploadState" Clicked="UploadAsync" />

@code {
    ButtonState uploadState;

    async Task UploadAsync()
    {
        try { await api.UploadAsync(); }
        catch (Exception) { uploadState = ButtonState.Error; }
    }
}
```

```razor
@using Shiny.Blazor.Controls

<ShinyButton Text="Save" BusyText="Saving..." LeftMotionIcon="download" Clicked="SaveAsync" />

<ShinyButton Text="Submit"
             @bind-State="submitState"
             BusyText="Submitting..."
             SuccessText="Submitted"
             StateRevertDelay="@TimeSpan.FromSeconds(2)"
             Clicked="SubmitAsync" />

<ShinyButton Text="Delete"
             Appearance="ButtonAppearance.Outlined"
             Type="ButtonType.Critical"
             LeftMotionIcon="trash"
             Clicked="DeleteAsync" />

@code {
    ButtonState submitState = ButtonState.Normal;

    async Task SaveAsync() => await http.PostAsJsonAsync("/api/save", model);

    async Task SubmitAsync()
    {
        await http.PostAsJsonAsync("/api/submit", model);
        submitState = ButtonState.Success;
    }
}
```

Blazor-side differences worth knowing:

- Colours come from `--shiny-color-*` custom properties via `shiny-btn--{appearance}` /
  `shiny-btn--{type}` classes, not inline styles. Explicit colour parameters emit inline custom
  properties, which win with no specificity fight.
- Motion icons default to `currentColor`, so they follow the button through hover and disabled for
  free — do not set `IconColor` unless you want to break that.
- The button plays its slot icons itself, exactly as MAUI does: they are on `MotionTrigger.Manual`
  and the button calls `Play()` from its own click, so a click anywhere on the button animates them
  rather than only one that lands on the 20px glyph. `MotionIconPlayOnClick="false"` turns it off.
- `ContentPadding` is a CSS `padding` string (`"10px 16px"`), not a `Thickness`.
- `FullWidth` stretches the button to its container; `HasShadow` is a `bool?`.
- `LeftIcon` accepts an image URL **or** raw SVG/HTML markup; `LeftIconContent` is a
  `RenderFragment`.
- Clearing `BusyMotionIcon` falls back to a CSS spinner rather than an `ActivityIndicator`.
- Hover and press are a `filter: brightness()` for the filled appearances and a `color-mix` state
  layer for the transparent ones, so they work over any background including a caller's own colour.
- Everything honours `prefers-reduced-motion`.

## Code Generation Guidance

- **Do not add an `IsBusy` property to the view model for an async command.** `AutoBusy` already
  covers it on MAUI, and awaiting `Clicked` covers it on Blazor. Only bind `IsBusy` when the busy
  state comes from somewhere other than this button's own command.
- Do not set `IsEnabled` from a `CanExecute`-shaped property either — wire `CanExecute` on the
  command and let the button follow it.
- Leave colour properties unset so the theme carries through; reach for `Appearance` and `Type`
  first, explicit colours only for brand.
- One `Filled` primary button per screen region; `Tonal` or `Outlined` for secondary actions, `Text`
  for tertiary ones like Cancel.
- Use `Critical` for destructive actions, and prefer `Outlined`/`Text` there so the loud colour is
  not also the largest area.
- `BusyText` is worth setting whenever the work takes more than a moment — "Saving..." tells the user
  more than a spinner beside an unchanged label.
- Give icon-only buttons a `SemanticProperties.Description` (MAUI) or `aria-label` (Blazor). The
  button keeps one stable `AutomationId` across state changes and moves only the description, since
  MAUI's `AutomationId` is set-once. A description you set, style or bind is left alone — even a
  binding whose value arrives after the text; without one, the button mirrors its state text.
- `ReplaceLeftIcon` is the default for a reason — pick `ReplaceContent` only when the label would be
  misleading mid-flight, and pin nothing: the content keeps its layout space either way.
