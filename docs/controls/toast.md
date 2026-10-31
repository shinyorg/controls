# Toast

[← All Shiny Controls](../../README.md)

A service-first toast notification system — inject `IToaster` (registered by `UseShinyControls()`) and call from code. No XAML or OverlayHost required. The overlay auto-attaches to the current page on first use.

```csharp
using Shiny.Maui.Controls.Toast;

public class MyViewModel(IToaster toaster)
{
    // Simple
    await toaster.ShowAsync("Item saved!");

    // With spinner + manual dismiss
    IDisposable toast = await toaster.ShowAsync("Uploading...", cfg =>
    {
        cfg.Spinner = ToastSpinnerPosition.Left;
        cfg.Duration = TimeSpan.Zero;
    });
    // Later: toast.Dispose();
}
```

**Themed methods** — colors from MAUI Styles or built-in defaults:

```csharp
await toaster.InfoAsync("Update available");        // Blue
await toaster.SuccessAsync("File saved");           // Green
await toaster.WarningAsync("Storage almost full");  // Amber
await toaster.DangerAsync("Save failed");           // Orange
await toaster.CriticalAsync("System error");        // Red
```

**Your own toaster** — implement `IToaster` and register it with `UseShinyControls(c => c.SetCustomToaster<MyToaster>())`; it replaces the built-in `Toaster` for everything that injects `IToaster`. (Before 1.7.0 this registered the built-in `Toaster` regardless of `T` — #29.)

```razor
<!-- Blazor: register AddShinyToast() in DI, place <ToastHost /> in layout -->
@inject IToastService ToastService

await ToastService.ShowAsync("Saved!", cfg =>
{
    cfg.Duration = TimeSpan.FromSeconds(3);
    cfg.ShowProgressBar = true;
});

// Blazor themed methods also available:
await ToastService.InfoAsync("Update available");
await ToastService.SuccessAsync("File saved");
```

| Property | Type | Default | Description |
|---|---|---|---|
| Text | string | (required) | Toast message |
| Duration | TimeSpan | 3s | Auto-dismiss. Zero = manual only |
| Position | ToastPosition | Bottom | Top or Bottom |
| DisplayMode | ToastDisplayMode | Pill | Pill (rounded) or FillHorizontal (full width) |
| DismissOnTap | bool | true | Tap to dismiss |
| QueueMode | ToastQueueMode | Queue | Queue (sequential) or Stack (multiple visible) |
| Spinner | ToastSpinnerPosition | None | None, Left, or Right |
| ShowProgressBar | bool | false | Countdown drain bar |
| Icon | ImageSource? | null | Optional icon (MAUI) |
| TapCommand | ICommand? | null | Tap action (MAUI) |
| UseFeedback | bool | true | Feedback on show/dismiss |
| BackgroundColor | Color? | dark gray | Background fill |
| TextColor | Color? | white | Text color |
| BorderColor | Color? | null | Border stroke |
| CornerRadius | double | 20 | Corner radius (pill mode) |
| TextOverflow | ToastTextOverflow | Ellipsis | Ellipsis, MultiLine, or Marquee |
| MarqueeSpeedPixelsPerSecond | double | 40 | Scroll speed for marquee mode |

**Text Overflow modes:**
- `Ellipsis` — truncates long text with "…" (default)
- `MultiLine` — wraps text to multiple lines
- `Marquee` — scrolling ticker animation (configure speed via `MarqueeSpeedPixelsPerSecond`)

## Safe area

On MAUI, toasts keep clear of the status bar, notch and home indicator on every platform. The toast
layer applies the page's safe area, and `Offset` is measured from inside it. iOS used to add the
window's inset a second time, so a `Top` toast sat about a notch's height lower than it does on Android.

## Foldables

With a fold posture source registered, toasts sit in the larger segment of a book. In tabletop posture, `Bottom` toasts go below the fold and `Top` toasts above it. Blazor `ToastHost` does the book case in CSS, using the first segment. See [Foldables](foldables.md).

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
