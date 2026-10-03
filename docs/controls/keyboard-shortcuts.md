# Keyboard Shortcuts

[← All Shiny Controls](../../README.md)

In-app keyboard shortcuts declared on a page, a view or a dialog: `Primary+S`, `F5`, `?`, a two-step chord like `Ctrl+K, Ctrl+C`, or a push-to-talk key that reports when it is let go. They fire while your window has focus. For system-wide hotkeys that fire while the app is in the background, see `IGlobalHotKeyService` in the [Desktop add-on](desktop.md).

| Host | Package | Key source |
| --- | --- | --- |
| MAUI — Windows | `Shiny.Maui.Controls` | WinUI `PreviewKeyDown` on the window root |
| MAUI — Android | `Shiny.Maui.Controls` | Hardware keyboards (Chromebook, DeX, Bluetooth) through the activity's key events |
| MAUI — iOS / iPadOS | `Shiny.Maui.Controls` | Hardware keyboards through GameController's `GCKeyboard` (observes; see below) |
| MAUI — macOS (AppKit) | `Shiny.Maui.Controls.Desktop` | A local `NSEvent` monitor |
| MAUI — Linux (GTK4) | `Shiny.Maui.Controls.Desktop` | A capture-phase `GtkEventControllerKey` on the window |
| MAUI — Mac Catalyst | `Shiny.Maui.Controls.Desktop` | `GCKeyboard`, as on iOS |
| Blazor — WebAssembly / Server / Hybrid | `Shiny.Blazor.Controls` | One capture-phase listener on `window`, matched in JS |

Both hosts share one engine, `Shiny.Controls.Keyboard.Shared`, for gesture parsing, display, layout-aware matching, scopes, chords, repeats and releases. A shortcut means the same keys on every host.

## Gestures

```
Primary+S            Ctrl+S on Windows/Linux/Android, ⌘S on Apple platforms
Ctrl+Shift+A         modifiers joined with +, key last; case-insensitive
Alt+F4  Esc  Up      named keys and aliases (Esc, Return, Del, PgDn, Up/Down/Left/Right…)
?   /   Plus         a single typed character; Shift is ignored because it is how the character is typed
Ctrl+Slash           a punctuation key by its US position
Ctrl+K, Ctrl+C       a sequence: press one chord, then the next
Primary+,            a comma right after + is the key, not a separator
```

- **Modifiers:** `Ctrl`/`Control`, `Alt`/`Option`, `Shift`, `Meta`/`Cmd`/`Win`/`Super`, and **`Primary`** (also `Mod`/`CmdOrCtrl`). Use `Primary` in cross-platform code. Writing `Control` gives Mac users ⌃S, which no Mac app uses.
- **Modifiers match exactly.** `Ctrl+B` does not fire for `Ctrl+Shift+B`, and leaving a modifier out means it must not be held.
- **Keyboard layouts:**
  - Letters follow the key's printed letter, so `Ctrl+Z` on AZERTY is the key labelled Z.
  - On non-Latin layouts (Cyrillic, Greek, Hebrew) letters fall back to the key's position.
  - Digits match by position, so AZERTY can reach `Ctrl+1` even though its top row types `&`.
- **Display:** gestures are written the platform's way: `Ctrl+Shift+S` on Windows, `⇧⌘S` on a Mac.

## MAUI

```csharp
builder
    .UseMauiApp<App>()
    .UseShinyControls()                 // Windows, Android, iOS: nothing else needed
    .UseDesktopKeyboardShortcuts();     // Shiny.Maui.Controls.Desktop — AppKit, GTK4, Catalyst
```

```xml
<ContentPage xmlns:shiny="http://shiny.net/maui/controls">
    <shiny:KeyboardShortcuts.Shortcuts>
        <shiny:KeyboardShortcut Gesture="Primary+S" Command="{Binding SaveCommand}" Description="Save" />
        <shiny:KeyboardShortcut Key="F5" Command="{Binding RefreshCommand}" />
        <shiny:KeyboardShortcut Key="S" Modifiers="Primary,Shift" Command="{Binding SaveAsCommand}" />
        <shiny:KeyboardShortcut Gesture="Ctrl+K, Ctrl+C" Command="{Binding CommentCommand}" />
        <shiny:KeyboardShortcut Key="Right" AllowRepeat="True" Command="{Binding NudgeCommand}" CommandParameter="right" />
        <shiny:KeyboardShortcut Key="Space" TextInput="Never"
                                Command="{Binding StartTalkingCommand}"
                                ReleasedCommand="{Binding StopTalkingCommand}" />
    </shiny:KeyboardShortcuts.Shortcuts>
    ...
</ContentPage>
```

### `KeyboardShortcut`

| Property | Default | |
| --- | --- | --- |
| `Gesture` | | Gesture syntax. Wins over `Key`/`Modifiers` when both are set. |
| `Key` / `Modifiers` | | The alternative to `Gesture`: `Key="S" Modifiers="Primary,Shift"`. |
| `Command` / `CommandParameter` | | Executed on press. While `CanExecute` is false the shortcut stands aside and the key reaches the focused control. |
| `ReleasedCommand` | | Executed when the key comes back up. |
| `IsEnabled` | `true` | A disabled shortcut never matches. |
| `AllowRepeat` | `false` | Fire again for each auto-repeat while held. Repeats are swallowed either way, so holding `Ctrl+B` never starts typing b's. |
| `TextInput` | `Auto` | Whether it fires while a text field has focus — see below. |
| `Description` / `Category` | | For a cheat sheet. |
| `DisplayText` | (read-only) | The gesture as this platform writes it. Bind a tooltip to it. |
| `Pressed` / `Released` events | | Raised before the commands. In `Pressed`, set `e.Handled = false` to let the key through. |

Bindings resolve against the element's `BindingContext`, the same as anything else on the page.

### Where shortcuts are live

- **On a page:** from `Appearing` to `Disappearing`. A page pushed on top silences the one beneath it.
- **On any other element:** while it is in a window, `IsVisible`, and its page is showing.
- **Precedence:** a view beats its page, and between unrelated elements the most recently shown wins.
- **`KeyboardShortcuts.IsModal="True"`** on an element blocks every shortcut outside it while it is live, including app-wide ones. Shiny's own in-app dialogs do this automatically, and Escape cancels them.

```xml
<Grid IsVisible="{Binding IsEditing}" shiny:KeyboardShortcuts.IsModal="True">
    <shiny:KeyboardShortcuts.Shortcuts>
        <shiny:KeyboardShortcut Key="Escape" Command="{Binding CancelCommand}" />
    </shiny:KeyboardShortcuts.Shortcuts>
</Grid>
```

### From code — `IKeyboardShortcutService`

```csharp
public ShellViewModel(IKeyboardShortcutService shortcuts)
{
    // App-wide, lowest precedence. Dispose to remove.
    this.palette = shortcuts.Register("Primary+Shift+P", _ => this.OpenPalette(), b => b.Description = "Command palette");

    shortcuts.ChordStateChanged += (_, _) =>
        this.Hint = shortcuts.IsChordPending
            ? $"{String.Join(" ", shortcuts.PendingChords.Select(c => c.ToDisplayString(shortcuts.Platform)))} — waiting for the next key"
            : "";

    var sheet = shortcuts.GetActiveShortcuts()      // highest precedence first
        .Where(x => !x.IsShadowed)
        .Select(x => $"{shortcuts.Format(x.Binding.Gesture)}  {x.Binding.Description}");
}
```

| Member | |
| --- | --- |
| `IsSupported` | False where no key source exists (AppKit/GTK/Catalyst without `UseDesktopKeyboardShortcuts()`). |
| `Platform` | What `Primary` means and how gestures are written. |
| `Register(gesture, pressed, configure?, window?)` | App-wide by default; pass a `Window` to limit it to one. |
| `GetActiveShortcuts(window?)` | Everything reachable now, for a "press ? for shortcuts" sheet. `IsShadowed` marks a shortcut hidden by a higher-precedence one. |
| `Format(gesture)` | `Ctrl+Shift+P` or `⇧⌘P`. |
| `ChordTimeout` | How long a sequence waits for its next chord. Two seconds by default; `TimeSpan.Zero` waits indefinitely. |
| `IsChordPending` / `PendingChords` / `ChordStateChanged` | For a "⌘K was pressed…" hint. |
| `ShortcutInvoked` | After any shortcut fires. |

## Blazor

`AddShinyControls()` covers the registration (or `AddShinyKeyboardShortcuts()` on its own). Add `@using Shiny.Controls.Keyboard` to `_Imports.razor` for `KeyModifiers` and `TextInputBehavior`.

```razor
<KeyboardShortcuts>
    <KeyboardShortcut Gesture="Primary+S" OnPressed="Save" Description="Save" />
    <KeyboardShortcut Key="F5" OnPressed="Refresh" />
    <KeyboardShortcut Key="A" Modifiers="KeyModifiers.Control | KeyModifiers.Shift" OnPressed="SelectAll" />
    <KeyboardShortcut Gesture="Ctrl+K, Ctrl+C" OnPressed="Comment" />
    <KeyboardShortcut Key="Space" TextInput="TextInputBehavior.Never" OnPressed="StartTalking" OnReleased="StopTalking" />
</KeyboardShortcuts>

@* Only while focus is inside the content: *@
<KeyboardShortcuts Scope="KeyboardShortcutScopeKind.Element">
    <KeyboardShortcut Gesture="Primary+B" OnPressed="Bold" />
    <textarea @bind="text" />
</KeyboardShortcuts>

@* A dialog: nothing outside it fires while it is rendered. *@
<KeyboardShortcuts IsModal="true">
    <KeyboardShortcut Key="Escape" OnPressed="Close" />
</KeyboardShortcuts>

<KeyboardShortcutHint Gesture="Primary+Shift+P" />   @* <kbd>⇧⌘P</kbd> on a Mac, Ctrl+Shift+P elsewhere *@
```

- **`<KeyboardShortcuts>`:** `Scope` (`Document` or `Element`), `IsModal`, `IsEnabled`, `Name`. Groups nest: an inner group beats the group around it, and the group rendered last beats an unrelated earlier one. Shortcuts and ordinary content can share its `ChildContent`.
- **`<KeyboardShortcut>`:** the same parameters as MAUI, with `OnPressed`/`OnReleased` event callbacks in place of commands. It adds `PreventDefault` (default `true`), which stops the browser and the focused element from acting on the key. On its own, outside any group, it applies to the whole page.
- **`IKeyboardShortcutService`:** the same surface as MAUI, plus `StartAsync()` and `PlatformChanged`. Any shortcut component starts the listener after its first render. If every shortcut is registered in code, call `StartAsync()` from `OnAfterRenderAsync` yourself.
- **Matching runs in JavaScript.** `preventDefault` must be decided before the browser acts on the key, and Blazor Server cannot reach .NET synchronously, so only a match crosses to .NET. Typing costs no round trips. For the same reason a `CanExecute` on a code-registered binding can only skip the handler; it cannot hand the key back. Use `IsEnabled` for that.

## Typing in a text field

`TextInput="Auto"` (the default) fires only for gestures that cannot be ordinary typing:

| Gesture | Field focused, `Auto` |
| --- | --- |
| `Ctrl+B`, `⌘S`, `Alt+F` | fires |
| `Escape`, `F1`–`F24` | fires |
| `J`, `Shift+J`, `?`, `Space`, arrows | stays with the field |

Use `Always` or `Never` to override. A focused web view (`BlazorWebView`, `WKWebView`, WebKitGTK) counts as a text field on the native hosts, because the page may be typing.

AltGr is excluded as well: while typing, `Ctrl+Alt` shortcuts never fire for an AltGr stroke. Windows reports AltGr as Ctrl+Alt, so on a German layout `AltGr+E` (€) would otherwise press a `Ctrl+Alt+E` shortcut.

## Chords, repeats and releases

- **Sequences** wait `ChordTimeout` (two seconds) for their next chord. Holding the modifier between chords is fine. A key that does not continue the sequence abandons it and is then treated as a fresh key, not eaten. If both `Ctrl+K` and `Ctrl+K, Ctrl+C` exist in the same scope, the sequence wins.
- **Auto-repeat** fires again only with `AllowRepeat`, but repeats are always swallowed.
- **Release** pairs with the key that went down, even if the modifier was let go first. If the window loses focus while a key is held, `Released` fires straight away, so a push-to-talk can't stay stuck on.

## Platform notes

- **macOS (AppKit):** the local monitor runs before the main menu's key equivalents, so a shortcut can take a key the menu bar would otherwise have handled.
- **iOS, iPadOS and Mac Catalyst:**
  - UIKit only delivers keys to responders MAUI owns, so the key source is GameController. It **observes**: a shortcut fires, but the focused control still receives the key.
  - The system sends no auto-repeat.
  - Character gestures (`?`) assume a US layout there, because GameController reports positions only.
- **Android:** the activity only sees keys the focused view declines. An `EditText` keeps typing and its own editing keys (`Ctrl+A/C/V/X/Z`).
- **Windows:** keys typed into a `WebView2` (`BlazorWebView`) never reach the XAML tree. Put Blazor `<KeyboardShortcuts>` inside the web content for those. WebView2 also handles browser accelerators (F5, Ctrl+P, Ctrl+F) unless `AreBrowserAcceleratorKeysEnabled` is false.
- **Browsers:** some keys (`Ctrl+W`, `Ctrl+T`, `Ctrl+N`, `Ctrl+Tab`) belong to the browser and no page can claim them. Keys are ignored while an IME is composing.
