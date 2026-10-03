# Dialogs

[← All Shiny Controls](../../README.md)

A service-first dialog system that emulates the classic `alert`, `confirm`, `prompt`, and `action sheet` primitives — with **owned (non-native), animated, themeable** dialogs on **both MAUI and Blazor**. Inject `IDialogService` and `await` a result — no markup per call. Calls are queued, so awaiting several in a row shows them one at a time.

- **MAUI**: registered by `UseShinyControls()`. The overlay auto-attaches to whichever page is current **at the time of each call** (no XAML or OverlayHost required), so dialogs keep working across navigation.
- **Blazor**: register `AddShinyDialogs()` in DI and place a single `<DialogHost />` in your layout.

```csharp
// MAUI — inject IDialogService (e.g. into a ViewModel)
public class MyViewModel(IDialogService dialogs)
{
    await dialogs.Alert("Heads up", "Your changes have been saved.", "Got it");

    var ok = await dialogs.Confirm("Delete item?", "This cannot be undone.", okText: "Delete", cancelText: "Cancel");

    var result = await dialogs.Prompt("What's your name?", "We'll personalize your experience.", placeholder: "e.g. Allan");
    if (result.Ok)
        Console.WriteLine(result.Value);
}
```

```razor
@* Blazor — same surface *@
@inject IDialogService Dialogs

await Dialogs.Alert("Heads up", "Your changes have been saved.", "Got it");
var ok = await Dialogs.Confirm("Delete item?", "This cannot be undone.", okText: "Delete", cancelText: "Cancel");
var result = await Dialogs.Prompt("Your name?", "Personalize things.", placeholder: "e.g. Allan");

// action sheet — returns the chosen option's text (or null if cancelled); mark one option destructive (red)
var choice = await Dialogs.ActionSheet("Photo", ["Take Photo", "Choose from Library", "Delete Photo"], destructive: "Delete Photo");
```

| Method | Returns | Buttons |
|---|---|---|
| `Alert(title, message, okText, configure?)` | `Task` | OK |
| `Confirm(title, message, okText, cancelText, configure?)` | `Task<bool>` | confirm + cancel |
| `Prompt(title, message, placeholder, okText, cancelText, initialValue?, maxLength?, keyboard?/inputType?, configure?)` | `Task<PromptResult>` | confirm + cancel + text field |
| `ActionSheet(title, options, cancelText, destructive?, configure?)` | `Task<string?>` | one button per option + cancel (returns the chosen option, or `null` if cancelled) |

`Prompt` forwards `initialValue`, `maxLength`, and the keyboard directly (MAUI takes a `Keyboard`; Blazor takes an HTML `inputType` string). Pass `cancelText: null` to `Prompt` or `ActionSheet` to **hide the cancel button** entirely (the ActionSheet otherwise always renders one).

**Animations** — every call takes an optional `configure` delegate to set the entry/exit animation and styling. `DialogAnimation` values: `None`, `Fade`, `SlideTop`, `SlideBottom`, `SlideLeft`, `SlideRight`, `Zoom`, `Pop` (default).

```csharp
await dialogs.Confirm("Delete?", "This cannot be undone.", configure: c =>
{
    c.Animation = DialogAnimation.SlideBottom;
    c.BackgroundColor = Color.FromArgb("#312E81");   // MAUI Color (Blazor: CSS string)
    c.OkButtonColor = Color.FromArgb("#22D3EE");
    c.CornerRadius = 24;
});
```

**Customization**
- **Per-call**: the `configure` delegate (animation, colors, corner radius, backdrop opacity, dismiss behavior).
- **Global defaults**: MAUI `UseShinyControls(c => c.ConfigureDialogs(o => o.DefaultAnimation = DialogAnimation.Zoom))`; Blazor `AddShinyDialogs(o => o.DefaultAnimation = DialogAnimation.Zoom)`.
- **Full template override**: MAUI `DialogOptions.ContentTemplate` (a `DataTemplate` bound to `DialogContext`); Blazor `<DialogHost Template="...">` (a `RenderFragment<DialogContext>`). The host still supplies the dimmed backdrop and animation.
- **Replace the service**: MAUI `c.SetCustomDialogs<T>()`.

Tapping the backdrop or pressing `Escape` cancels (`Confirm` → `false`, `Prompt` → `Cancelled`); `Enter` confirms on Blazor. On MAUI, `Escape` comes from a hardware keyboard and works whenever the dialog has a Cancel button or `DismissOnBackdrop` is on. While a MAUI dialog is open it is a modal [keyboard-shortcut](keyboard-shortcuts.md) scope, so the page behind it ignores its shortcuts. Colors follow the theme tokens (`--shiny-color-surface` / `Shiny.Color.Surface`, `Primary`, …) so dialogs match light/dark automatically.
