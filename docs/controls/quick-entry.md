# Quick Entry

[← All Shiny Controls](../../README.md)

An assistant-style prompt summoned over whatever the user is looking at — `PromptView` in a popup, plus an optional Siri-style glow around the screen edge. Ships in the **core** packages on both hosts.

It is presented one of two ways, and the API is identical either way:

| Presentation | What it is | Where |
|---|---|---|
| **In-app** | An overlay drawn over the current page | Everywhere — iOS, Android, Mac Catalyst, Windows, macOS, Linux, Blazor |
| **Desktop** | A borderless, always-on-top OS window opening over *other applications* | Windows, macOS (AppKit), Linux — with the `Shiny.Maui.Controls.Desktop` add-on and `UseShinyDesktop(cfg => cfg.UseQuickEntryWindow())` |

`QuickEntryOptions.Presentation` defaults to `Auto`: the native window where one is available, the overlay everywhere else. So a shared codebase configures this once, with no platform checks at the call site. `InApp` and `Desktop` force it either way; `Desktop` where it isn't available falls back to the overlay and logs why.

**MAUI**

```csharp
using Shiny;
using Shiny.Maui.Controls.QuickEntry;

builder.UseShinyControls(cfg => cfg.ConfigureQuickEntry(o =>
{
    o.Presentation = QuickEntryPresentation.Auto;
    o.Placement    = QuickEntryPlacement.TopCenter;   // or BottomCenter / Center / NearCursor / Manual
    o.ScreenGlow   = ScreenGlowTrigger.WhileBusy;
}));
```

Then wire the prompt to your AI of choice:

```csharp
public class QuickEntryHost(IQuickEntryService quickEntry, IChatClient chat)
{
    public async Task StartAsync()
    {
        await quickEntry.PreloadAsync();          // optional — also how you reach Content before first open

        var prompt = (PromptView)quickEntry.Content!;
        prompt.Suggestions = new List<PromptSuggestion>
        {
            new("Summarise my clipboard", "Reads whatever you last copied", "📋"),
            new("Explain this error",     "Paste a stack trace",            "🐞")
        };

        prompt.Submitted += async (_, e) =>
        {
            prompt.IsBusy = true;
            var answer = await chat.GetResponseAsync(e.Text);
            prompt.IsBusy = false;
            prompt.ResponseContent = new MarkdownView { Markdown = answer.Text };
        };
    }
}
```

**Blazor** — in-app only, since a web page cannot make an OS window. Place one `<QuickEntryHost />` in your root layout, then drive `IQuickEntryService` from anywhere. Import `Shiny.Blazor.Controls.QuickEntry` where you place it: Razor renders a tag it cannot resolve as a literal element rather than failing the build, so a missing `@using` gives you a popup that never appears while the service still reports it open.

```razor
@inject IQuickEntryService QuickEntry

<button @onclick="QuickEntry.Toggle">Ask</button>

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

| Member | Notes |
|---|---|
| `Show()` / `Hide()` / `Toggle()` | `Toggle` is what a hotkey, tray click or button binds to |
| `PreloadAsync()` (MAUI) | Builds the popup ahead of first use |
| `Resize(width, height)` (MAUI) | Manual sizing; content implementing `IQuickEntryAutoSize` drives it for you |
| `IsOpen` / `Content` / `ResolvedPresentation` | `ResolvedPresentation` has `Auto` and any fallback already applied |
| `ShowGlow()` / `HideGlow()` / `PulseGlowAsync()` | The glow is on the same service — the two are almost always used together |
| `Opened` / `Closed` | Fire however the popup was dismissed |

`QuickEntryOptions` covers `Presentation`, `Placement`, `Width`, `CollapsedHeight`, `MaxHeight`, `TopMarginRatio` / `BottomMarginRatio`, `AutoSize`, `DismissOnFocusLost`, `DismissOnScrimTap`, `DismissOnEscape`, `ActivateOnShow`, `ScrimColor`, `ContentFactory`, `RecreateContentOnShow`, `ScreenGlow`, and `Glow` (thickness, palette, speed, intensity, blob count, layers, frame rate). `HotKey`, `ShowInTaskbar` and `JoinAllSpaces` only apply to the desktop presentation.

## PromptView

The popup's default content, and an ordinary control in its own right — put it on a page and it works there too, on every platform.

`Text`, `Placeholder`, `IsBusy`, `BusyText`, the leading icon (`Icon` for an image, `IconContent` for any view, `ShowIcon`, `IconSize` — leave them alone for the built-in animated orb), the dropdown (`Suggestions` + `SuggestionTemplate`, `MaxVisibleSuggestions`, `DropdownContent` to replace that area entirely, `DropdownHeight` to pin it), the response (`Response` for plain text, `ResponseContent` for any view/markup — the latter wins when both are set), `Footer`, tools (`LeadingTools` / `TrailingTools`), `SubmitCommand` / `SuggestionCommand` / `MicrophoneCommand`, `ShowMicrophone`, `ShowSubmitButton`, `ClearOnSubmit`, and a full colour surface (`AccentColor`, `SurfaceColor`, `OutlineColor`, `TextColor`, `PlaceholderColor`, `SubtleTextColor`, `HighlightColor`, `CornerRadius`, `PromptFontSize`) that follows the theme tokens until you assign one. Events: `Submitted`, `SuggestionSelected`, `Cancelled`, `ResponseChanged`.

**It does no AI itself.** Handle `Submitted`, set `IsBusy`, assign the response.

## Prompt tools

`LeadingTools` (beside the orb) and `TrailingTools` (before the microphone and submit glyphs) take `PromptTool`s — the prompt-bar equivalent of `TextEntry`'s tool slots. A tool that needs to read or drive the prompt implements `IPromptAwareTool` on MAUI (`Attach`/`Detach`), or overrides `OnAttached`/`OnDetached` on Blazor, where it is also handed the app's `IServiceProvider` so it can resolve what it needs without the hosting page wiring it.

`PromptTextToSpeechTool` ships in **`Shiny.Maui.Controls.SpeechAddins`** and **`Shiny.Blazor.Controls.SpeechAddins`** and reads the answer aloud through `Shiny.Speech`. It hides itself until there is something to read, turns into a stop button while speaking, and takes `AutoSpeak`, `SpeechRate`, `Pitch`, `Volume`, `VoiceName`, `Culture` and `HideWhenEmpty`.

```xml
<qe:PromptView>
    <qe:PromptView.TrailingTools>
        <speech:PromptTextToSpeechTool AutoSpeak="True" Culture="en-US" />
    </qe:PromptView.TrailingTools>
</qe:PromptView>
```

```razor
<PromptView Response="@answer" TrailingTools="tools" />

@code {
    readonly List<PromptTool> tools = new() { new PromptTextToSpeechTool() };
}
```

It speaks `Response`, the plain-text half of the answer. A rich answer set through `ResponseContent` is a view with no text to read, so give the tool a `TextSelector` to pull the words out of what you rendered.

MAUI needs `AddSpeechServices()` (or `AddTextToSpeech()`) registered — the tool resolves its engine from DI and no-ops otherwise. The package targets iOS, Android, Mac Catalyst, macOS (AppKit) and Windows; there is no plain `net11.0` target, so the GTK/Linux head cannot reference it (`Shiny.Speech`'s `net11.0` assembly implements only the browser engine). Blazor needs nothing registered but does need a WebAssembly host — the synthesiser is the browser's own.

The dropdown sizes itself to whatever is in it — the popup grows and shrinks to match — unless you set `DropdownHeight`, which pins it and scrolls instead (what you want for a list that changes length as the user types and would otherwise make the popup jump under the pointer).

Keyboard: ↑ / ↓ walk the suggestions, Enter submits (or picks the highlighted row), and Escape unwinds one layer of state at a time — cancel the request, drop the highlight, clear the response, clear the prompt — and only closes the popup once there is nothing left to back out of. Host your own content and implement `IQuickEntryKeyHandler` to take part; content that changes size should also implement `IQuickEntryAutoSize` so a desktop window can follow it.

## Screen glow

An animated colour wash around the edge, click-through and always-on-top. `ScreenGlowTrigger.WhileBusy` is the Siri-like one — it lights only while your content reports itself working, rather than the whole time the popup is up. Custom content joins in via `IQuickEntryBusyState`; `PromptView` already does.

```csharp
await quickEntry.PulseGlowAsync(TimeSpan.FromSeconds(3));   // one-shot acknowledgement
```

It rims the **display** in desktop presentation and the **page** in-app — the same thing on a phone, and not the same thing on a desktop with your app in a window. On macOS and Linux/X11 the desktop glow is a transparent click-through window; on Windows a WinUI 3 window has no per-pixel alpha, so it is rendered with GDI+ into four layered Win32 windows, one per screen edge, which is why the Windows glow has square corners.

`Glow.Thickness` is how far the colour reaches inward, and it is absolute — 110 is a band around a desktop display and most of the width of a phone, so turn it down for a tight rim on a small screen. `Glow.BlobCount` is a *floor*, not the count: enough colour pools to rim the screen without unlit gaps between them are always drawn, which on a large display is well over five.

### Automation ids (MAUI)

Set `AutomationId` on the control and its parts are named after it, for UI tests (Appium, UI Automation, DevFlow). Without an id, the parts get none.

For `PromptView`:

| Part | Id |
|---|---|
| The prompt field | `<id>_Entry` |
| Submit / stop button | `<id>_Submit` |
| Microphone button | `<id>_Microphone` |
| Busy status line | `<id>_Busy` |
| Built-in response label | `<id>_Response` |
| Each suggestion row, by position | `<id>_Suggestion_0` |
| Each leading / trailing tool, by position | `<id>_LeadingTool_0` / `<id>_TrailingTool_0` |

The popup has no owner element, so set `QuickEntryOptions.AutomationId` and the service gives it to the content it builds (custom `ContentFactory` content that already has an id keeps it). A tool or `SuggestionTemplate` root with its own id keeps it.
