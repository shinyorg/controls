# ShinyButton

[← All Shiny Controls](../../README.md)

A button that knows what it is doing: a leading and a trailing icon slot, a real working state, and
success/error states, all wired to the theme and — on MAUI — to its `Command`.

`Microsoft.Maui.Controls.Button` renders text and one image and nothing else, so "submit, spin, tick"
ends up hand-assembled on every page out of a `Grid`, an `ActivityIndicator` and a swapped label.
This is that assembly, done once and at parity on both hosts.

- **State machine** — `ButtonState.Normal` / `Busy` / `Success` / `Error`, with `BusyText` /
  `SuccessText` / `ErrorText` standing in for `Text`, per-state icons, and a `StateRevertDelay` that
  drops Success and Error back to Normal on their own (`TimeSpan.Zero` holds).
- **`IsBusy` shorthand** for the common view model that only has an `IsSaving` flag. Clearing it
  unwinds Busy but will *not* cut a Success or Error short — a view model clearing its flag in a
  `finally` is exactly when the outcome is on screen.
- **Three busy modes** — `ReplaceLeftIcon` (the default; the spinner and the icon are the same size,
  so the button cannot change width), `ReplaceContent` (content fades but keeps its layout space, so
  the button holds the width it had), and `KeepContent`.
- **Icon slots, three ways each** — `LeftIcon` (`ImageSource`), `LeftMotionIcon` (the name of a
  [motion icon](motion-icons.md), coloured and played by the button), or `LeftIconView` for any `View`
  at all. Same trio on the right.
- **Command state (MAUI)** — the button follows its command's `CanExecuteChanged`, and if the command
  is an async one (`AsyncRelayCommand` and friends) it drives its own `Busy` for exactly as long as
  the command runs. Nothing binds `IsBusy`. It does this through MAUI's own `IsEnabledCore`, so a
  button you explicitly set `IsEnabled="False"` on **stays** disabled when the command becomes
  executable again. A command that can fault should be declared
  `[RelayCommand(FlowExceptionsToTaskScheduler = true)]` (MVVM Toolkit): by default
  `AsyncRelayCommand.Execute` rethrows the fault through `async void`, an unhandled exception that can
  take the app down, rather than just leaving the button in `Error`.
- **Appearance × Type** — `Filled` / `Tonal` / `Outlined` / `Text` / `Elevated` crossed with
  `Primary` / `Secondary` / `Success` / `Warning` / `Critical` / `Info`, all resolved from the theme
  tokens. Any explicit colour property wins over both.

```xml
<!-- The whole point: nothing here binds IsBusy. SaveCommand is an AsyncRelayCommand. -->
<shiny:ShinyButton Text="Save"
                   BusyText="Saving..."
                   LeftMotionIcon="download"
                   Command="{Binding SaveCommand}" />

<!-- Submit, spin, tick. The command sets State=Success itself; the button respects it. -->
<shiny:ShinyButton Text="Submit"
                   State="{Binding SubmitState}"
                   BusyText="Submitting..."
                   SuccessText="Submitted"
                   StateRevertDelay="0:0:2"
                   Command="{Binding SubmitCommand}" />

<!-- Appearance and type are orthogonal; explicit colours still win -->
<shiny:ShinyButton Text="Delete" Appearance="Outlined" Type="Critical" LeftMotionIcon="trash" />
<shiny:ShinyButton Text="Cancel" Appearance="Text" />
<shiny:ShinyButton Text="Brand"  ButtonBackgroundColor="#E91E63" TextColor="White" />
```

Blazor mirrors the parameters one-for-one. There is no `ICommand` on the web, so the command-state
integration is MAUI-only; its equivalent is that `Clicked` is awaited — an `async` handler holds the
button busy for as long as it runs, and a synchronous one never flickers. A handler that throws puts the button into `Error` (`ShowErrorOnFault`) and the exception is still rethrown — the button reflects a fault, it does not swallow it. An exception escaping a Blazor event handler raises the error UI on WebAssembly and ends the circuit on Blazor Server, so catch failures in the handler and set `State="ButtonState.Error"` (bound with `@bind-State`) yourself.

```razor
<ShinyButton Text="Save" BusyText="Saving..." LeftMotionIcon="download" Clicked="SaveAsync" />

<ShinyButton Text="Delete" Appearance="ButtonAppearance.Outlined"
             Type="ButtonType.Critical" LeftMotionIcon="trash" Clicked="DeleteAsync" />

@code {
    async Task SaveAsync() => await http.PostAsJsonAsync("/api/save", model);
}
```

Motion icons in the slots play a cycle on tap and take their colour from the button's foreground, so
a disabled or hovered button carries its icons with it. The button owns that playback on both hosts —
the icons sit on the `Manual` trigger and the button plays them from its own tap — so a tap anywhere
on the button animates them, not only one that lands on the glyph. Clearing `BusyMotionIcon` falls
back to a platform `ActivityIndicator` on MAUI and a CSS spinner on Blazor.

The text doubles as the accessible name: while `SemanticProperties.Description` is unset, the button
keeps it in step with the state text. A description you set, style or bind is yours and is never
overwritten — a binding counts from the moment it is set, even before its value arrives.
