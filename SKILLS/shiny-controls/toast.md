# Toast

A service-first toast notification system. Unlike other controls, Toast is invoked via code — no XAML placement required. Shows lightweight, transient messages with auto-dismiss, manual dismiss via `IDisposable`, stacking/queuing, and full styling control.

## MAUI Usage

`IToaster` is registered automatically by `UseShinyControls()`. Inject it via constructor injection:

```csharp
using Shiny.Maui.Controls.Toast;

public class MyViewModel(IToaster toaster)
{
    // Simple
    await toaster.ShowAsync("Item saved!");

    // With configuration — returns IDisposable for manual dismiss
    IDisposable toast = await toaster.ShowAsync("Uploading...", cfg =>
    {
        cfg.Spinner = ToastSpinnerPosition.Left;
        cfg.Duration = TimeSpan.Zero; // manual dismiss only
        cfg.DismissOnTap = false;
    });
    // Later: toast.Dispose();

    // Full configuration
    await toaster.ShowAsync("Connection lost", cfg =>
    {
        cfg.Duration = TimeSpan.FromSeconds(5);
        cfg.Position = ToastPosition.Bottom;
        cfg.DisplayMode = ToastDisplayMode.FillHorizontal;
        cfg.DismissOnTap = true;
        cfg.QueueMode = ToastQueueMode.Stack;
        cfg.Offset = new Thickness(12);
        cfg.UseFeedback = true;
        cfg.ShowProgressBar = true;
        cfg.BackgroundColor = Colors.Red;
        cfg.TextColor = Colors.White;
        cfg.BorderColor = Colors.DarkRed;
        cfg.BorderThickness = 1;
        cfg.CornerRadius = 20;
        cfg.Icon = ImageSource.FromFile("warning.png");
        cfg.TapCommand = new Command(() => { });
        cfg.AnnounceToScreenReader = true;
    });
}
```

No `OverlayHost` or base class required. The toast overlay auto-attaches to the current page.

## Themed Methods (MAUI)

```csharp
// Themed convenience methods — colors from MAUI Styles or built-in defaults
await toaster.InfoAsync("Update available");        // Blue
await toaster.SuccessAsync("File saved");           // Green
await toaster.WarningAsync("Storage almost full");  // Amber
await toaster.DangerAsync("Save failed");           // Orange
await toaster.CriticalAsync("System error");        // Red

// Override after theme defaults are applied
await toaster.SuccessAsync("Done!", cfg =>
{
    cfg.Duration = TimeSpan.FromSeconds(5);
    cfg.ShowProgressBar = true;
});
```

### Customizing Theme Colors (MAUI)

Define `ToastTypeStyle` resources in `App.xaml` to override the defaults:

```xml
<Application.Resources>
    <shiny:ToastTypeStyle x:Key="ShinyToastSuccessStyle"
                          BackgroundColor="#065F46"
                          TextColor="White"
                          BorderColor="#10B981" />
    <shiny:ToastTypeStyle x:Key="ShinyToastCriticalStyle"
                          BackgroundColor="#7F1D1D"
                          TextColor="White"
                          BorderColor="#DC2626"
                          BorderThickness="2" />
</Application.Resources>
```

**Style keys:** `ShinyToastInfoStyle`, `ShinyToastSuccessStyle`, `ShinyToastWarningStyle`, `ShinyToastDangerStyle`, `ShinyToastCriticalStyle`

## Blazor Usage

```csharp
// Program.cs
builder.Services.AddShinyToast();
```

```razor
<!-- MainLayout.razor (once) -->
<ToastHost />
```

```razor
@inject IToastService ToastService

<button @onclick="ShowToast">Show Toast</button>

@code {
    async Task ShowToast()
    {
        await ToastService.ShowAsync("Saved!", cfg =>
        {
            cfg.Position = ToastPosition.Bottom;
            cfg.Duration = TimeSpan.FromSeconds(3);
            cfg.TapCallback = () => { /* handle tap */ };
        });
    }
}
```

### Themed Methods (Blazor)

```csharp
await ToastService.InfoAsync("Update available");
await ToastService.SuccessAsync("File saved");
await ToastService.WarningAsync("Storage almost full");
await ToastService.DangerAsync("Save failed");
await ToastService.CriticalAsync("System error");
```

## ToastConfig Properties

| Property | Type (MAUI / Blazor) | Default | Description |
|---|---|---|---|
| `Text` | `string` | (required) | Toast message text |
| `Duration` | `TimeSpan` | `3s` | Auto-dismiss duration. `TimeSpan.Zero` = manual only |
| `Position` | `ToastPosition` | `Bottom` | `Top` or `Bottom` |
| `DisplayMode` | `ToastDisplayMode` | `Pill` | `Pill` (rounded, offset from edges) or `FillHorizontal` (flush, full width) |
| `DismissOnTap` | `bool` | `true` | Tap to dismiss |
| `QueueMode` | `ToastQueueMode` | `Queue` | `Queue` (one at a time) or `Stack` (multiple visible) |
| `Offset` | `Thickness` / `double` | `12` | Margin from edges (pill mode only; fill mode ignores this) |
| `Spinner` | `ToastSpinnerPosition` | `None` | `None`, `Left`, or `Right` — shows indeterminate loading spinner |
| `UseFeedback` | `bool` | `true` | Feedback on show/dismiss (MAUI only) |
| `ShowProgressBar` | `bool` | `false` | Countdown progress bar draining over duration |
| `BackgroundColor` | `Color?` / `string?` | dark gray | Background fill |
| `TextColor` | `Color?` / `string?` | white | Text color |
| `BorderColor` | `Color?` / `string?` | none | Border stroke |
| `BorderThickness` | `double` | `0` | Border width |
| `CornerRadius` | `double` | `20` | Corner radius (pill mode) |
| `Icon` | `ImageSource?` / — | `null` | Optional icon (MAUI) |
| `IconHtml` | — / `string?` | `null` | Optional HTML/SVG icon (Blazor) |
| `TapCommand` | `ICommand?` / — | `null` | Command on tap (MAUI) |
| `TapCallback` | — / `Action?` | `null` | Callback on tap (Blazor) |
| `TextOverflow` | `ToastTextOverflow` | `Ellipsis` | `Ellipsis` (truncate with …), `MultiLine` (word wrap), or `Marquee` (scrolling ticker) |
| `MarqueeSpeedPixelsPerSecond` | `double` | `40` | Scroll speed for marquee mode (pixels per second) |
| `AnnounceToScreenReader` | `bool` / — | `true` | Announce via SemanticScreenReader (MAUI) |

## Text Overflow

Controls how long text is displayed when it exceeds the toast width:

```csharp
// Ellipsis (default) — truncates with "..."
await toaster.ShowAsync("Very long message that will be truncated...", cfg =>
{
    cfg.TextOverflow = ToastTextOverflow.Ellipsis;
});

// Multi-line — wraps text to multiple lines
await toaster.ShowAsync("Very long message that will wrap to the next line", cfg =>
{
    cfg.TextOverflow = ToastTextOverflow.MultiLine;
});

// Marquee — scrolling ticker animation
await toaster.ShowAsync("Very long message that scrolls continuously", cfg =>
{
    cfg.TextOverflow = ToastTextOverflow.Marquee;
    cfg.MarqueeSpeedPixelsPerSecond = 80; // faster scroll (default: 40)
    cfg.Duration = TimeSpan.FromSeconds(10); // give time to read
});
```

## Behavior

- **Queue mode** (default): Toasts appear one at a time. Next toast shows after current dismisses.
- **Stack mode**: Multiple toasts visible simultaneously, stacked from the edge inward. Max 5 visible; oldest auto-dismissed when exceeded.
- **Auto-dismiss**: Toast disappears after `Duration`. Set `Duration = TimeSpan.Zero` for manual-only dismiss.
- **Manual dismiss**: `ShowAsync` returns `IDisposable`. Call `Dispose()` to dismiss programmatically.
- **Tap dismiss**: When `DismissOnTap = true`, tapping the toast dismisses it. `TapCommand`/`TapCallback` fires before dismiss.
- **Safe area**: On MAUI, toasts stay clear of the status bar, notch and home indicator on every platform (the toast layer applies the safe area once; `Offset` is measured inside it). Fill mode handles it too. Don't add your own safe-area margin.
- **Feedback**: Fires via `IFeedbackService` on show (MAUI, configurable via `UseFeedback`).
- **Accessibility**: `SemanticScreenReader.Announce()` on show (MAUI). `role="alert"` + `aria-live="polite"` (Blazor).
- **Progress bar**: Optional thin bar that drains linearly over the duration.
- **Animations**: Slide in from edge + fade (250ms). Slide out + fade on dismiss (200ms).

## Code Generation Guidance

- Use `toaster.ShowAsync(text)` for fire-and-forget notifications (inject `IToaster` via constructor)
- To replace the toaster, implement `IToaster` and call `UseShinyControls(c => c.SetCustomToaster<MyToaster>())` — do not also `AddSingleton<IToaster, …>` yourself
- Use `var handle = await toaster.ShowAsync(text, cfg => { cfg.Duration = TimeSpan.Zero; })` when the caller needs to control dismiss timing
- Default to `Pill` mode for brief notifications, `FillHorizontal` for connectivity/status bars
- Use `Spinner` for ongoing operations (uploads, syncing)
- Use `ShowProgressBar` when the user should see remaining time
- Use `TextOverflow = MultiLine` for important messages that shouldn't be truncated
- Use `TextOverflow = Marquee` for long messages in constrained pill toasts; increase `Duration` to give time to read
- Stack mode is best for rapid-fire notifications; queue mode for sequential alerts

## ViewModel Pattern

```csharp
public partial class MyViewModel(IToaster toaster) : ObservableObject
{
    IDisposable? uploadToast;

    [RelayCommand]
    async Task Upload()
    {
        uploadToast = await toaster.ShowAsync("Uploading...", cfg =>
        {
            cfg.Spinner = ToastSpinnerPosition.Left;
            cfg.Duration = TimeSpan.Zero;
        });

        await DoUploadAsync();
        uploadToast.Dispose();

        await toaster.ShowAsync("Upload complete!");
    }
}
```

## Automation ids (MAUI)

A toast has no element of its own to find it under, so name it through the config. The id goes on the toast's tappable chrome:

```csharp
await toaster.ShowAsync("Saved", c => c.AutomationId = "SavedToast");
```

| Part | Id |
|---|---|
| The toast (tap target) | `<id>` |
| Text label | `<id>_Text` |

Unset (the default), the toast carries no ids.

## Foldables

Toasts move into one segment of a fold automatically: the larger half of a book, and on a tabletop below or above the fold depending on `Position`. There is nothing to configure. See [foldables.md](./foldables.md).
