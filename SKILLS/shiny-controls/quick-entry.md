# Quick Entry

An assistant-style prompt summoned over whatever the user is looking at — `PromptView` in a popup, plus an optional Siri-style **screen-edge glow** on the same service.

Ships in the **core** packages on both hosts: `Shiny.Maui.Controls` (namespace `Shiny.Maui.Controls.QuickEntry`) and `Shiny.Blazor.Controls` (namespace `Shiny.Blazor.Controls.QuickEntry`).

## Two presentations, one API

| Presentation | What it is | Where |
|---|---|---|
| **In-app** | An overlay drawn over the current page | Everywhere — iOS, Android, Mac Catalyst, Windows, macOS, Linux, Blazor |
| **Desktop** | A borderless, always-on-top OS window that opens over *other applications* | Windows, macOS (AppKit), Linux — with the [`Shiny.Maui.Controls.Desktop`](./tray-icon.md) add-on |

`QuickEntryOptions.Presentation` picks between them:

- **`Auto`** (default) — the native window where one is available, the overlay everywhere else. One setting for a shared codebase, no platform checks at the call site.
- **`InApp`** — always the overlay, including on desktop. Right for a popup that should stay inside your app.
- **`Desktop`** — force the OS window. Where it isn't available it falls back to the overlay and logs why, rather than failing to open.

`IQuickEntryService.ResolvedPresentation` tells you which you actually got.

The in-app presentation is built on the library's own [`Overlay`](./overlay.md) control, so it shares
the page's `OverlayHost` backdrop with everything else that uses one — a popup opened over a floating
panel dims the page once, not twice — and inherits the blur, the close-on-backdrop-tap and the
show/hide worker. A page with no host gets one installed automatically.

> **macOS AppKit note.** The in-app overlay does not paint on the `net11.0-macos` head. Re-parenting a
> `ContentPage`'s `Content` at runtime — which every in-app overlay in this library does, Toast and
> Dialogs included — does not re-render there, so the page goes blank or the layer never appears. It
> is a pre-existing limitation of that head rather than anything specific to quick entry. `Auto`
> already picks the desktop window on macOS, so the default path is unaffected; only an explicit
> `Presentation = InApp` on the AppKit head hits it.


**iOS, Android, Mac Catalyst and Blazor are in-app only.** A phone has no second option and a browser cannot make an OS window — the Blazor options class has no `Presentation` member at all.

## Setup

**MAUI** — registered by `UseShinyControls()`, so this only changes the settings:

```csharp
using Shiny;
using Shiny.Maui.Controls.QuickEntry;

builder
    .UseMauiApp<App>()
    .UseShinyControls(cfg => cfg.ConfigureQuickEntry(o =>
    {
        o.Presentation = QuickEntryPresentation.Auto;
        o.Placement    = QuickEntryPlacement.TopCenter;
        o.ScreenGlow   = ScreenGlowTrigger.WhileBusy;
    }));
```

For a real desktop window — and global hotkeys — add the desktop package and one more call:

```bash
dotnet add package Shiny.Maui.Controls.Desktop
```

```csharp
builder
    .UseShinyControls(cfg => cfg.ConfigureQuickEntry(o => o.HotKey = "Ctrl+Alt+Space"))
    .UseShinyDesktop(cfg => cfg.UseQuickEntryWindow());
```

`cfg.UseQuickEntryWindow()` is opt-in because it changes behaviour: it adds the native always-on-top window and the whole-screen glow, claims `QuickEntryOptions.HotKey` at startup, and with `Presentation = Auto` takes over from the in-app popup. **Never emit `UseDesktopQuickEntry()`**: it was removed.

Safe to call unconditionally: on Mac Catalyst and anywhere else that isn't a desktop, the presenters report themselves unsupported and the core service quietly stays with the overlay.

**Blazor** — registered by `AddShinyControls()`, plus one host component in the root layout:

```csharp
services.AddShinyControls(cfg => cfg.ConfigureQuickEntry(o =>
{
    o.Placement  = QuickEntryPlacement.TopCenter;
    o.ScreenGlow = ScreenGlowTrigger.WhileBusy;
}));
```

```razor
@* MainLayout.razor *@
<QuickEntryHost />
```

⚠️ **`Shiny.Blazor.Controls.QuickEntry` must be in scope where you place the host** — in `_Imports.razor` or as an `@using` on the layout itself. Razor does not fail on a tag it cannot resolve: `<QuickEntryHost />` compiles to a literal `<quickentryhost>` element, renders nothing, and the service goes on reporting the popup as open. It looks exactly like a broken popup rather than a missing import.

## Driving the popup

```csharp
public class QuickEntryHost(IQuickEntryService quickEntry)
{
    public async Task StartAsync()
    {
        await quickEntry.PreloadAsync();   // optional: builds the native window so the first open is instant

        quickEntry.Opened += (_, _) => { };
        quickEntry.Closed += (_, _) => { };
    }

    public void OnTrayIconClicked() => quickEntry.Toggle();
}
```

| Member | Notes |
|---|---|
| `Show()` / `Hide()` / `Toggle()` | `Toggle` is what a hotkey or tray click binds to |
| `PreloadAsync()` | Creates the native window ahead of first use |
| `Resize(width, height)` | Manual sizing. Height is clamped to `MaxHeight` |
| `IsSupported` | False on MacCatalyst and non-desktop runtimes — check before `Show()` |
| `IsOpen` | True while the popup is on screen |
| `Content` | The hosted view. Cast to `PromptView` to wire it up |
| `Opened` / `Closed` | Raised however the popup was dismissed |

The popup is a real OS window, so it does **not** resize itself to its content the way an in-app overlay would. `Options.AutoSize` (on by default) measures the content and calls `Resize` for you, clamped between `CollapsedHeight` and `MaxHeight`; turn it off and call `Resize` yourself if your content measures expensively.

### QuickEntryOptions

| Property | Default | Notes |
|---|---|---|
| `HotKey` | `null` | **Desktop only.** Accelerator that toggles the popup, e.g. `"Ctrl+Alt+Space"` |
| `Width` | `720` | Device-independent pixels |
| `CollapsedHeight` | `76` | Height with just the prompt row |
| `MaxHeight` | `560` | Ceiling for auto-sizing |
| `Placement` | `TopCenter` | `TopCenter` / `BottomCenter` / `Center` / `NearCursor` / `Manual`. In-app, `NearCursor` reads as centred — a touch screen has no pointer. Blazor has only the first three |
| `TopMarginRatio` | `0.18` | Top edge as a fraction of the screen for `TopCenter` |
| `BottomMarginRatio` | `0.12` | Gap below the popup for `BottomCenter`, as a fraction of the working area |
| `X` / `Y` | `0` | Screen coordinates for `Manual` |
| `AutoSize` | `true` | Follow the content's measured height |
| `DismissOnFocusLost` | `true` | Close when another app takes focus |
| `DismissOnEscape` | `true` | Content gets first refusal — see below |
| `ActivateOnShow` | `true` | Take keyboard focus. Turn off for a passive HUD |
| `ShowInTaskbar` | `false` | Desktop only; applied when the native window is created |
| `JoinAllSpaces` | `true` | macOS desktop presentation: appear over full-screen apps |
| `ContentFactory` | `null` | Builds the content. Defaults to a new `PromptView` |
| `RecreateContentOnShow` | `false` | Off, so a half-typed prompt survives an accidental dismiss |
| `ScreenGlow` | `None` | `None` / `WhileOpen` / `WhileBusy` |
| `Glow` | — | The glow's appearance: thickness, palette, speed, intensity, blob count, layers, frame rate |
| `ScrimColor` | 35% black | In-app backdrop dimming the page. Transparent disables it |
| `DismissOnScrimTap` | `true` | In-app: the touch equivalent of `DismissOnFocusLost`, which needs a window manager |
| `WindowTitle` | `"Quick Entry"` | Desktop only. Never visible; shows in accessibility tooling |

Placement is always resolved against the screen under the mouse pointer, so the popup follows the user across a multi-monitor setup rather than pinning itself to the app's window.

## PromptView

The popup's default content: an animated orb, a single-line prompt, and an area beneath it that expands for suggestions and a response.

**It does no AI itself.** It raises `Submitted` and leaves the request to you.

```csharp
var prompt = (PromptView)quickEntry.Content!;

prompt.Suggestions = new List<PromptSuggestion>
{
    new("Summarise my clipboard", "Reads whatever you last copied", "📋"),
    new("Explain this error",     "Paste a stack trace",            "🐞"),
    new("Draft a reply",          "Bullet points in, message out",  "✉️")
};

prompt.Submitted += async (_, e) =>
{
    prompt.ResponseContent = null;
    prompt.IsBusy = true;

    var answer = await chatClient.GetResponseAsync(e.Text);

    prompt.IsBusy = false;
    prompt.ResponseContent = new MarkdownView { Markdown = answer.Text };
};

prompt.Cancelled += (_, _) => cts.Cancel();
```

It is an ordinary `ContentView`, so it also works inline on a normal page — including on iOS and Android, where the popup itself does not exist. That is the recommended way to demo or test it.

```xml
<ContentPage xmlns:qe="clr-namespace:Shiny.Maui.Controls.Desktop.QuickEntry;assembly=Shiny.Maui.Controls.Desktop">
    <qe:PromptView Placeholder="Ask anything…"
                     Text="{Binding Prompt}"
                     IsBusy="{Binding IsThinking}"
                     SubmitCommand="{Binding AskCommand}"
                     ShowMicrophone="True"
                     MicrophoneCommand="{Binding DictateCommand}" />
</ContentPage>
```

### Properties

| Property | Notes |
|---|---|
| `Text` | The prompt. Two-way by default |
| `Placeholder` | Default `"Ask anything…"` |
| `Icon` | An `ImageSource` in place of the built-in animated orb |
| `IconContent` | Any view in the leading slot. Wins over `Icon` |
| `ShowIcon` / `IconSize` | Hide the slot entirely, or resize the orb/image. Default true / 26 |
| `IsBusy` / `BusyText` | Spins the orb, shows a spinner, swaps submit for a stop button |
| `Suggestions` | `IEnumerable`. Honours `INotifyCollectionChanged`, so an `ObservableCollection` updated as the user types behaves like autocomplete |
| `SuggestionTemplate` | Render your own rows. Binding context is the item |
| `MaxVisibleSuggestions` | Default 6 — it is a HUD, not a list view |
| `DropdownContent` | Any view for the expanding area under the prompt — a command palette, recent items, your own list. Renders above `Suggestions`, so both can be used together |
| `DropdownHeight` | Unset (-1) sizes the dropdown to its content and the window follows. Set a value to pin it and scroll instead — right for a list that changes length as the user types |
| `Response` | The answer as plain text, rendered in a built-in label. This is what a read-aloud tool speaks |
| `ResponseContent` | Any `View`: a `Label`, a `MarkdownView`, a `ChatView`. Wins over `Response`; null collapses it |
| `Footer` | Optional bottom strip — a model picker, a keyboard legend |
| `LeadingTools` / `TrailingTools` | `IList<PromptTool>`, created for you. Leading sits beside the orb, trailing before the microphone and submit glyphs |
| `SubmitCommand` / `SuggestionCommand` / `MicrophoneCommand` | Command equivalents of the events |
| `ShowMicrophone` | Default false — there is no speech engine in this package |
| `ShowSubmitButton` | Default true |
| `ClearOnSubmit` | Default true |
| `AccentColor`, `SurfaceColor`, `OutlineColor`, `TextColor`, `PlaceholderColor`, `SubtleTextColor`, `HighlightColor`, `CornerRadius`, `PromptFontSize` | Appearance. The colours default to app-theme bindings, so they follow light/dark until you assign one |

Events: `Submitted` (`PromptSubmittedEventArgs` — `Text` and the chosen `Suggestion`, or null for a typed submit), `SuggestionSelected`, `Cancelled`, `ResponseChanged`.

`PromptSuggestion` is `Text`, `Description`, `Glyph` (any string — emoji or an icon-font character), and `Value` (anything you want carried to the handler).

### Tools

`LeadingTools` and `TrailingTools` are the prompt-bar equivalent of `TextEntry`'s tool slots. `PromptTool` derives from the same `IconTextTool` base, so `Text`, `Icon`, `ToolColor`, `FontSize`, `Command`/`CommandParameter` and `Clicked` all work the way they do on a `TextEntryTool`.

```xml
<qe:PromptView>
    <qe:PromptView.TrailingTools>
        <speech:PromptTextToSpeechTool AutoSpeak="True" />
        <qe:PromptTool Text="⚙" Command="{Binding SettingsCommand}" />
    </qe:PromptView.TrailingTools>
</qe:PromptView>
```

A tool that needs the prompt implements `IPromptAwareTool`:

```csharp
public class MyTool : PromptTool, IPromptAwareTool
{
    PromptView? prompt;

    void IPromptAwareTool.Attach(PromptView view)
    {
        this.prompt = view;
        this.prompt.ResponseChanged += this.OnResponse;   // drop it again in Detach
    }

    void IPromptAwareTool.Detach()
    {
        if (this.prompt is not null)
            this.prompt.ResponseChanged -= this.OnResponse;

        this.prompt = null;
    }
}
```

`Detach` runs when the tool leaves the collection *and* when the whole collection is replaced — the tool object outlives the prompt, so anything subscribed in `Attach` has to come off there.

#### Read aloud (`Shiny.Maui.Controls.SpeechAddins`)

`PromptTextToSpeechTool` speaks the answer through `Shiny.Speech`'s `ITextToSpeechService`. Hidden while there is nothing to read, a stop button while speaking.

| Property | Notes |
|---|---|
| `AutoSpeak` | Read the answer the moment it lands, instead of waiting for a tap. Default false |
| `HideWhenEmpty` | Hide the tool until there is something to read. Default true |
| `TextSelector` | `Func<PromptView, string?>`. Set this when the answer only lives in `ResponseContent` — a `View` has no text to speak |
| `SpeechRate` / `Pitch` / `Volume` / `VoiceName` / `Culture` | Passed through to `TextToSpeechOptions` |
| `SpeakingText` / `SpeakingColor` | The stop-state glyph and tint |

`Speak()`, `Stop()` and `StopAsync()` are callable directly. Register the engine with `AddSpeechServices()` (or `AddTextToSpeech()`) — the tool resolves it from DI and no-ops when nothing is registered.

`Shiny.Maui.Controls.SpeechAddins` targets iOS, Android, Mac Catalyst, macOS (AppKit) and Windows. There is no plain `net11.0` target, so the GTK/Linux head cannot reference it — `Shiny.Speech`'s `net11.0` assembly implements only the browser engine, so there would be nothing behind it. Resolve the tool by name if a page is shared with that head.

```csharp
prompt.TrailingTools!.Add(new PromptTextToSpeechTool { AutoSpeak = true });
prompt.Submitted += async (_, e) =>
{
    prompt.IsBusy = true;
    prompt.Response = await AskAsync(e.Text);   // Response, not ResponseContent - the tool needs text
    prompt.IsBusy = false;
};
```

### Keyboard

MAUI has no cross-platform key-down event, so the popup host reads keys off the native window and hands them to its content.

- **↑ / ↓** walk the suggestions, wrapping back to the prompt at either end
- **Enter** submits, or picks the highlighted suggestion
- **Escape** unwinds one layer of state at a time — cancel the request, drop the highlight, clear the response, clear the prompt — and only then falls through to the host, which closes the popup

Custom content joins in by implementing `IQuickEntryKeyHandler`:

```csharp
public class MyPopupContent : ContentView, IQuickEntryKeyHandler
{
    public bool HandleKey(QuickEntryKey key)
    {
        // return true to swallow the key; false lets the host act on it
        return key == QuickEntryKey.Tab && this.CycleSection();
    }
}
```

Three more optional hooks: `IQuickEntryPresentationAware` (`OnQuickEntryOpened` / `OnQuickEntryClosed` — focus an entry, cancel an in-flight request), `IQuickEntryBusyState` (lets the screen glow's `WhileBusy` trigger see your content's working state), and `IQuickEntryAutoSize` (`GetDesiredHeight(width)` + `DesiredHeightChanged`). `PromptView` implements all of them.

Implement `IQuickEntryAutoSize` on any content that **changes size**. The host would rather not need it, but the content lives inside the very window whose size it determines, and neither obvious signal survives that: a `ContentView` stretches to whatever space the layout offers, so its arranged height is the window's rather than its content's; and `Measure` keeps returning a desired size cached from that same constrained pass. A view that knows which of its children is the real content can simply say so. Content that does not implement it is measured instead — fine for a fixed height, unreliable for anything that grows.

## Blazor

Same control, same service, no OS window. `<PromptView>` takes ordinary parameters:

```razor
<PromptView Width="640"
            @bind-Text="prompt"
            Placeholder="Ask anything…"
            IsBusy="busy"
            Suggestions="suggestions"
            Response="@answer"
            Submitted="OnSubmitted" />
```

The popup's *own* prompt is configured through the service instead, because a service cannot hand a component parameters directly — the two meet at `PromptViewState`:

```razor
@inject IQuickEntryService QuickEntry

@code {
    protected override void OnInitialized() => QuickEntry.ConfigurePrompt(prompt =>
    {
        prompt.Suggestions = suggestions;
        prompt.Submitted += async (_, e) =>
        {
            prompt.IsBusy = true;
            prompt.Response = await AskAsync(e.Text);
            prompt.IsBusy = false;
        };
    });
}
```

Blazor tools are plain objects rather than components, so a tool can be built in a view model and handed over as a parameter — which is also the only way the popup's own prompt gets one:

```razor
<PromptView Response="@answer" TrailingTools="tools" />

@code {
    readonly List<PromptTool> tools = new() { new PromptTextToSpeechTool() };

    protected override void OnInitialized() => QuickEntry.ConfigurePrompt(prompt =>
        prompt.TrailingTools.Add(new PromptTextToSpeechTool()));
}
```

`PromptViewState.LeadingTools` / `TrailingTools` are `ObservableCollection<PromptTool>`, so a tool added after the popup has been built still shows up. Override `OnAttached` / `OnDetached` instead of `IPromptAwareTool`; both hand over `Prompt` and the app's `Services`, and `RefreshAsync()` re-renders after a tool changes its own glyph. `PromptTextToSpeechTool` ships in `Shiny.Blazor.Controls.SpeechAddins` and needs `AddTextToSpeech()` plus a WebAssembly host — the synthesiser is the browser's `speechSynthesis`, which a Blazor Server box does not have.

`IQuickEntryService.SetContent(RenderFragment)` replaces the built-in prompt with your own markup for every open; `Show(RenderFragment)` does it for one.

**Scoped, not singleton.** The popup's open state and the live options object are per-user, so a singleton would put one user's popup on every connected user's screen under Blazor Server. Under WebAssembly the two lifetimes are identical.

## Global hotkeys

**Desktop only**, and registered by every `UseShinyDesktop()` call (no `cfg` option needed) — a system-wide key grab does not exist on mobile or in a browser. Useful on its own:

```csharp
var registration = hotKeys.Register("Ctrl+Shift+K", () => ToggleRecording());
if (registration == null)
{
    // The combination could not be claimed. This is a normal outcome, not an exception:
    // unsupported platform, unparseable string, or another application already owns it.
}
```

The callback is marshalled to the UI thread, so it is safe to touch MAUI objects directly. Dispose the registration to release the key.

Accelerators use the same grammar as tray menu accelerators — modifiers joined with `+`, then the key: `"Ctrl+Alt+Space"`, `"Cmd+Shift+K"`, `"Ctrl+F12"`. Recognised modifiers are `Ctrl`/`Control`, `Alt`/`Option`/`Opt`, `Shift`, and `Cmd`/`Command`/`Meta`/`Win`/`Super`. **At least one modifier is required** — a bare key would hijack that key system-wide, and every backend refuses it.

| Platform | Mechanism | Notes |
|---|---|---|
| Windows | `RegisterHotKey` on a message-only window | Reliable. Fails if another process owns the combination |
| macOS (AppKit) | Carbon `RegisterEventHotKey` | No Accessibility permission prompt, unlike an `NSEvent` global monitor |
| Linux / X11 | `XGrabKey` on the root window | Grabbed with every lock-modifier combination, so Caps/Num Lock do not break it |
| Linux / Wayland | `org.freedesktop.portal.GlobalShortcuts` | GNOME 45+, KDE Plasma 6+ |
| MacCatalyst | — | Not supported |

## Screen glow

An animated colour wash around the edge of the display, in the style of Siri on iOS. Click-through and always-on-top, so it never interrupts anything.

```csharp
cfg.ConfigureQuickEntry(o =>
{
    o.Glow.Thickness = 130;    // how far the colour reaches in from the edge
    o.Glow.Palette   = new List<Color> { Colors.DeepSkyBlue, Colors.MediumPurple, Colors.HotPink };
    o.Glow.Speed     = 0.22;   // laps of the perimeter per second
    o.Glow.Intensity = 0.9;
    o.Glow.BlobCount = 5;      // *minimum* pools; enough to rim the screen are always drawn
    o.Glow.Layers    = 3;      // falloff passes: more is a deeper edge and costs more
    o.Glow.FrameRate = 30;
});
```

`Thickness` is the reach inward, and it is absolute — 110 is a band on a desktop display and most of the width of a phone. Turn it down for a tight rim on a small screen.

`BlobCount` is a floor rather than the count: enough colour pools to rim the screen without gaps between them are always drawn, which on a large display is well over five.

`UseQuickEntry()` calls `UseScreenGlow()` for you; call it directly to configure the appearance, or to use the glow without the popup.

```csharp
quickEntry.ShowGlow();
quickEntry.HideGlow();
await quickEntry.PulseGlowAsync(TimeSpan.FromSeconds(3));   // one-shot acknowledgement
```

Wire it to the popup with `QuickEntryOptions.ScreenGlow`:

- `None` — never (you can still drive it by hand)
- `WhileOpen` — the whole time the popup is up
- `WhileBusy` — only while the content reports itself working. This is the closest match to Siri, which lights the edge while listening and thinking rather than the whole time it is on screen. Works with `PromptView.IsBusy` out of the box; custom content implements `IQuickEntryBusyState`

It rims the **display** in desktop presentation and the **page** in-app — the same thing on a phone, and not the same thing on a desktop with your app in a window. Availability differs from the popup, so check `IsGlowSupported` separately:

- **In-app (every platform)** — an overlay layer on the current page
- **macOS / Linux (X11) desktop** — a transparent, click-through OS window
- **Windows desktop** — a WinUI 3 window has no per-pixel alpha, so the glow is rendered with GDI+ into four layered Win32 windows, one per screen edge. That is also why the Windows glow has square corners rather than following a rounded display
- **Blazor** — a CSS conic gradient masked to a band around the viewport
- **MacCatalyst / Linux Wayland desktop** — no whole-display glow; the in-app one is used instead

The glow is a full-screen animation. `FrameRate` is the first knob to turn down on an older GPU, then `Layers`.

## Wayland

Under Wayland a client is not allowed to position its own toplevel, raise itself above other windows, or grab the keyboard. GTK 4 dropped `gtk_window_move` and `set_keep_above` to match. So on a Wayland session:

- The popup is still undecorated and transparent, but the **compositor decides where it appears** and it is an ordinary window in the stack
- Hotkeys go through the desktop portal. Binding shows the user a **system confirmation dialog**, so the hotkey starts working asynchronously after startup — and the trigger you pass is a *preference*: the compositor may bind something else, and the user can rebind it. Never present the configured accelerator as fact on Wayland
- The screen glow is unavailable

Under X11 everything behaves as it does on Windows and macOS. Where `IGlobalHotKeyService.IsSupported` is false, fall back to a [tray icon](./tray-icon.md).

## Don't

- Don't force `Presentation = InApp` on the macOS AppKit head — see the note above.
- Don't reach for the Desktop package to get a popup on mobile or Blazor — the core in-app presentation already covers them.
- Don't set `Presentation = Desktop` and expect a window on iOS, Android, Mac Catalyst or Blazor. It falls back to the overlay.
- Don't treat a null return from `Register` as an error condition worth throwing on — it is the documented "could not claim" result.
- Don't expect `PromptView` to call an AI. Handle `Submitted`.
- Don't register a hotkey with no modifier.
- Don't assume the popup resizes itself if you turned `AutoSize` off.
- Don't expect measurement alone to size growing custom content — implement `IQuickEntryAutoSize`.

## Automation ids (MAUI)

Set `AutomationId` on the control to name its parts for UI tests (no id → no part ids). Never assign `AutomationId` twice in generated code; MAUI throws.

- `PromptView`: `<id>_Entry`, `<id>_Submit`, `<id>_Microphone`, `<id>_Busy`, `<id>_Response`, `<id>_Suggestion_<n>`, `<id>_LeadingTool_<n>` / `<id>_TrailingTool_<n>`. The service popup has no owner: set `QuickEntryOptions.AutomationId` to name the content it builds.
