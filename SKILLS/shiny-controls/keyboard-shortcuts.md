# Keyboard Shortcuts

In-app keyboard shortcuts on **both hosts, in the core packages**. Shortcuts are declared on a page, a view or a dialog, or registered in code. The engine (`Shiny.Controls.Keyboard.Shared`: gesture parsing, layout-aware matching, scopes, chords, repeat/release) is shared verbatim.

These are **not** global hotkeys. For keys that fire while the app is in the background, use `IGlobalHotKeyService` from `Shiny.Maui.Controls.Desktop` (see `quick-entry.md`).

## Rules for generated code

- **Use `Primary`** for the platform shortcut modifier (⌘ on Apple, Ctrl elsewhere): `Gesture="Primary+S"`, never `Control` for Save/Copy-style commands.
- **Modifiers match exactly.** Never emit `Alt="false"` style flags; leaving a modifier out already means "not held".
- On MAUI, emit `<shiny:KeyboardShortcuts.Shortcuts>` with `<shiny:KeyboardShortcut>` children. There is **no** `<KeyboardEvents>` and **no** `Keyboard.Shortcuts`; MAUI's own `Microsoft.Maui.Keyboard` class would clash.
- On MAUI, bind `Command` (+ `CommandParameter`); `ReleasedCommand` is for key-up. On Blazor, use the `OnPressed` / `OnReleased` **EventCallbacks**, never an `Action` parameter.
- AppKit (`net10.0-macos`), Linux GTK4 and Mac Catalyst need `.UseDesktopKeyboardShortcuts()` from `Shiny.Maui.Controls.Desktop` **in addition to** `UseShinyControls()`. Without it, shortcuts are declared but never fire (`IKeyboardShortcutService.IsSupported` is false). Windows, Android and iOS need nothing beyond core.
- Blazor razor files need `@using Shiny.Controls.Keyboard` for `KeyModifiers` / `TextInputBehavior`.
- Plain-key shortcuts (`J`, `?`, `Space`, arrows) are **not** delivered while a text field has focus (`TextInput="Auto"`). Only set `TextInput="Always"` when the user really wants that.

## Gesture syntax

`"Primary+Shift+P"`, `"Ctrl+S"`, `"Alt+F4"`, `"F5"`, `"Esc"`, `"Up"`, `"PageDown"`, `"Numpad5"`, `"?"` (a typed character; Shift ignored), `"Ctrl+Slash"` (a physical key), `"Ctrl+Plus"` / `"Ctrl++"`, `"Primary+,"`. A sequence is written `"Ctrl+K, Ctrl+C"`.

Modifier names: `Ctrl`/`Control`, `Alt`/`Option`/`Opt`, `Shift`, `Meta`/`Cmd`/`Command`/`Win`/`Super`, `Primary`/`Mod`/`CmdOrCtrl`.

## MAUI

```csharp
builder
    .UseMauiApp<App>()
    .UseShinyControls()
    .UseDesktopKeyboardShortcuts();   // Shiny.Maui.Controls.Desktop — AppKit / GTK4 / Catalyst
```

```xml
<ContentPage xmlns:shiny="http://shiny.net/maui/controls">
    <shiny:KeyboardShortcuts.Shortcuts>
        <shiny:KeyboardShortcut Gesture="Primary+S" Command="{Binding SaveCommand}" Description="Save" Category="File" />
        <shiny:KeyboardShortcut Key="S" Modifiers="Primary,Shift" Command="{Binding SaveAsCommand}" />
        <shiny:KeyboardShortcut Gesture="Ctrl+K, Ctrl+C" Command="{Binding CommentCommand}" />
        <shiny:KeyboardShortcut Gesture="?" Command="{Binding ShowShortcutsCommand}" />
        <shiny:KeyboardShortcut Key="Right" AllowRepeat="True" Command="{Binding NudgeCommand}" CommandParameter="right" />
        <shiny:KeyboardShortcut Key="Space" TextInput="Never"
                                Command="{Binding StartTalkingCommand}"
                                ReleasedCommand="{Binding StopTalkingCommand}" />
    </shiny:KeyboardShortcuts.Shortcuts>
    <!-- content -->
</ContentPage>
```

`KeyboardShortcut` (BindableObject) properties:

- `Gesture`, or `Key` + `Modifiers` (`Gesture` wins when both are set).
- `Command`, `CommandParameter`, `ReleasedCommand`.
- `IsEnabled` (default true).
- `AllowRepeat` (default false; repeats are swallowed regardless).
- `TextInput` (`Auto` / `Always` / `Never`).
- `Description`, `Category` (for cheat sheets).
- Read-only `DisplayText` (`Ctrl+Shift+S` / `⇧⌘S`; bind tooltips to it) and `ParsedGesture`.
- Events `Pressed` / `Released` (`KeyboardShortcutEventArgs`; set `Handled = false` in `Pressed` to let the key through).
- A `Command` whose `CanExecute` is false makes the shortcut stand aside; the key then reaches the focused control.

Bindings resolve against the element's `BindingContext`.

When shortcuts are live:

- **On a page:** from `Appearing` to `Disappearing`.
- **On any other `VisualElement`:** while it is in a window, `IsVisible`, and its page is showing.
- **Precedence:** a view beats its page, and the most recently shown unrelated element wins.
- **`shiny:KeyboardShortcuts.IsModal="True"`** blocks everything outside the element while it is live, including app-wide registrations. Shiny's in-app dialogs (`IDialogService`) set it themselves, and Escape cancels them.

Code — `IKeyboardShortcutService` (singleton, registered by `UseShinyControls()`):

```csharp
IDisposable reg = shortcuts.Register("Primary+Shift+P", e => OpenPalette(), b => { b.Description = "Command palette"; b.Released = e => { }; }, window: null);
shortcuts.GetActiveShortcuts();          // highest precedence first; skip x.IsShadowed for a cheat sheet
shortcuts.Format("Primary+S");           // "Ctrl+S" or "⌘S"
shortcuts.IsSupported; shortcuts.Platform; shortcuts.ChordTimeout = TimeSpan.FromSeconds(2);
shortcuts.IsChordPending; shortcuts.PendingChords; shortcuts.ChordStateChanged; shortcuts.ShortcutInvoked;
```

## Blazor

`AddShinyControls()` registers it (or `AddShinyKeyboardShortcuts()`), **Scoped**.

```razor
<KeyboardShortcuts>
    <KeyboardShortcut Gesture="Primary+S" OnPressed="Save" Description="Save" />
    <KeyboardShortcut Key="A" Modifiers="KeyModifiers.Control | KeyModifiers.Shift" OnPressed="SelectAll" />
    <KeyboardShortcut Key="Space" TextInput="TextInputBehavior.Never" OnPressed="StartTalking" OnReleased="StopTalking" />
</KeyboardShortcuts>

<KeyboardShortcuts Scope="KeyboardShortcutScopeKind.Element">   @* only while focus is inside *@
    <KeyboardShortcut Gesture="Primary+B" OnPressed="Bold" />
    <textarea @bind="text" />
</KeyboardShortcuts>

<KeyboardShortcuts IsModal="true">                              @* a dialog *@
    <KeyboardShortcut Key="Escape" OnPressed="Close" />
</KeyboardShortcuts>

<KeyboardShortcutHint Gesture="Primary+Shift+P" />            @* <kbd>⇧⌘P</kbd> / Ctrl+Shift+P *@
```

- **`<KeyboardShortcuts>`:** `Scope` (`Document` | `Element`), `IsModal`, `IsEnabled`, `Name`, `ChildContent` (shortcuts and content can be mixed).
- **`<KeyboardShortcut>`:** same parameters as MAUI, plus `PreventDefault` (default true). On its own, outside a group, it is page-wide.
- **`IKeyboardShortcutService`:** the MAUI surface plus `StartAsync()`, `IsRunning` and `PlatformChanged`. Shortcut components start it after their first render. If everything is registered in code, call `StartAsync()` in `OnAfterRenderAsync`.
- Matching happens in JS (`keyboard-shortcuts.js`), so `preventDefault` is decided synchronously and only matches cross to .NET. A `CanExecute` on a code binding can therefore only skip the handler; use `IsEnabled` to let a key through.

## Platform caveats to mention when relevant

- **iOS, iPadOS and Mac Catalyst:**
  - The source is GameController's `GCKeyboard`, so shortcuts **observe**: the focused control also gets the key.
  - There is no auto-repeat.
  - Character gestures (`?`) assume a US layout.
- **Android:** only keys the focused view declines reach the shortcuts. An `EditText` keeps typing and `Ctrl+A/C/V/X/Z`.
- **Windows:** keys inside a `WebView2` / `BlazorWebView` never reach XAML. Use Blazor `<KeyboardShortcuts>` inside the web content.
- **Browsers:** `Ctrl+W`, `Ctrl+T`, `Ctrl+N` and `Ctrl+Tab` cannot be claimed by a page.
