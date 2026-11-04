# Toast

[← All Shiny Controls](../../README.md)

A service-first toast notification system — inject `IToaster` (registered by `UseShinyControls()`) and call from code. No XAML or OverlayHost required. The overlay auto-attaches to the current page. On MAUI every `ContentPage` gets the wrapper it draws into as the page is handed its native view, so the first toast on a page that is already showing does not move the page's content (which used to rebuild its native views, reset bound lists and, on Android, shift the page for a frame).

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
| DisplayMode | ToastDisplayMode | Pill | Pill (rounded), FillHorizontal (full width) or Snackbar (rectangular, max 600) |
| DismissOnTap | bool | true | Tap to dismiss |
| QueueMode | ToastQueueMode | Queue | Queue (sequential), Stack (multiple visible) or Replace (dismiss the current one and jump the queue) |
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

## Snackbars

A snackbar is a toast with an action. It goes through the same service and the same queue, and every `ToastConfig` setting still applies. `SnackbarConfig` derives from `ToastConfig` and changes the defaults: `DisplayMode = Snackbar` (rectangular, 4px corners, full width on a phone, capped at 600 and centred on a wider screen), `TextOverflow = MultiLine`, `QueueMode = Replace` and `PauseOnHover = true`.

```csharp
// MAUI - IToaster
var result = await toaster.SnackbarAsync("Message deleted", "Undo");
if (result == SnackbarResult.ActionInvoked)
    Restore();

// Body tap and action button are separate targets
await toaster.SnackbarAsync("New reply from Sam", "Mark read", cfg =>
{
    cfg.TapCommand = OpenReplyCommand;     // tapping the message
    cfg.ActionCommand = MarkReadCommand;   // tapping the button - never fires TapCommand
});

// Loading: a line along the top edge, open until you dispose the handle
using var loading = await toaster.LoadingSnackbarAsync("Uploading...", cfg =>
{
    cfg.ActionText = "Cancel";
    cfg.ActionCommand = new Command(cts.Cancel);
});
loading.Progress = 0.4;              // null (default) = indeterminate
loading.Text = "Uploading... 40%";   // updates in place
```

```razor
@inject IToastService ToastService

var result = await ToastService.SnackbarAsync("Message deleted", "Undo");

await ToastService.SnackbarAsync("New reply", "Mark read", cfg =>
{
    cfg.TapCallback = OpenReply;
    cfg.ActionCallback = MarkRead;
});

using var loading = await ToastService.LoadingSnackbarAsync("Uploading...");
loading.Progress = 0.4;
```

**`SnackbarAsync(text, actionText?, configure?, cancellationToken)`** returns `Task<SnackbarResult>`, which completes when the snackbar closes:

| Result | When |
|---|---|
| `ActionInvoked` | The action button was pressed |
| `TimedOut` | The duration ran out |
| `Dismissed` | Body tap (`DismissOnTap`), or the token was cancelled (a queued snackbar is dropped without showing) |
| `Replaced` | A newer toast took its place (`QueueMode.Replace`, or a full stack) |

**`LoadingSnackbarAsync(text, configure?)`** returns `ILoadingSnackbar` (`Text`, `Progress`, `Closed`, `Dispose`) once it is on screen. It has `Duration = Zero` and `DismissOnTap = false`, so it stays until you dispose it or the user presses its action.

| SnackbarConfig | Type | Default | Description |
|---|---|---|---|
| ActionText | string? | null | Button label. Null = no button. Passing `actionText` makes the default duration 8s instead of 4s; `configure` can still change it |
| ActionCommand (MAUI) / ActionCallback (Blazor) | ICommand? / Action? | null | Runs when the button is pressed, before it closes |
| ActionTextColor | Color? / string? | inverse-primary token | Button label colour |
| IsLoading | bool | false | Top-edge progress line. Set for you by `LoadingSnackbarAsync` |
| Progress | double? | null | 0-1 for the loading line. Null = indeterminate |

New on `ToastConfig`, so plain toasts can use them too: `ToastDisplayMode.Snackbar`, `ToastQueueMode.Replace` (dismiss the queued toast that is showing and jump the queue), and `PauseOnHover` (holds the countdown and its progress bar while the pointer is over it or, on Blazor, focus is inside it; on MAUI desktop only, since a pointer recognizer on a phone takes real taps). Marquee, spinner, icon and colours work on a snackbar as they do on a toast. With `ShowProgressBar`, a snackbar's countdown bar runs along the top edge rather than the bottom.

Accessibility: the screen-reader announcement includes the action ("Message deleted. Undo"). The button is a real `Button` / `<button>` at least 40 tall and 48 wide. A snackbar with an action defaults to 8 seconds, and you can set `Duration = TimeSpan.Zero` to keep an important action up until the user responds.

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
