# Dialogs

A service-first dialog system that emulates the classic `alert`, `confirm`, `prompt`, and `action sheet` primitives with **owned (non-native), animated, themeable** dialogs on **both MAUI and Blazor**. Inject `IDialogService` and `await` a result — no markup per call. The dialog is always rendered by the library (the native platform alert/prompt is never used), so it looks identical across platforms and supports custom animations and templates.

> **Naming**: the interface is `IDialogService` (not `IDialogs`) to avoid colliding with `Shiny.Framework`'s `Shiny.IDialogs`. Namespaces: `Shiny.Maui.Controls.Dialogs` / `Shiny.Blazor.Controls.Dialogs`.

## Setup

### MAUI

Registered automatically by `UseShinyControls()`. The overlay auto-attaches to whichever page is current **at the time of each call** — no XAML or OverlayHost required, and dialogs keep working after navigating between pages.

```csharp
builder.UseMauiApp<App>().UseShinyControls();
```

### Blazor

```csharp
// Program.cs
builder.Services.AddShinyDialogs();
```

```razor
<!-- MainLayout.razor (once) -->
<DialogHost />
```

## Usage (identical surface on both hosts)

```csharp
// MAUI: inject into a ViewModel/service. Blazor: @inject IDialogService Dialogs
public class MyViewModel(IDialogService dialogs)
{
    // Alert — single button, awaits dismissal
    async Task SaveDone()
        => await dialogs.Alert("Heads up", "Your changes have been saved.", "Got it");

    // Confirm — returns bool
    async Task Delete()
    {
        var ok = await dialogs.Confirm("Delete item?", "This cannot be undone.", okText: "Delete", cancelText: "Cancel");
        if (ok)
            await DeleteItemAsync();
    }

    // Prompt — returns PromptResult (Ok + Value)
    async Task Rename()
    {
        var result = await dialogs.Prompt("Rename file", "Enter a new name.", placeholder: "report.pdf", okText: "Rename");
        if (result.Ok)
            await RenameAsync(result.Value);
    }

    // ActionSheet — returns the chosen option's text (null if cancelled); mark one option destructive (red)
    async Task PhotoOptions()
    {
        var choice = await dialogs.ActionSheet(
            "Photo",
            ["Take Photo", "Choose from Library", "Delete Photo"],
            cancelText: "Cancel",
            destructive: "Delete Photo");
        switch (choice)
        {
            case "Take Photo": /* … */ break;
            case "Delete Photo": /* … */ break;
            case null: /* cancelled */ break;
        }
    }
}
```

## API

| Method | Returns | Description |
|---|---|---|
| `Alert(title, message, okText = "OK", configure?)` | `Task` | Single dismiss button. |
| `Confirm(title, message, okText = "Yes", cancelText = "No", configure?)` | `Task<bool>` | `true` on confirm, `false` on cancel. |
| `Prompt(title, message, placeholder = null, okText = "OK", cancelText = "Cancel", initialValue = null, maxLength = null, keyboard/inputType = null, configure?)` | `Task<PromptResult>` | Text field + confirm/cancel. `initialValue` pre-fills the field, `maxLength` caps the length (`null` = no limit), and the keyboard arg picks the input mode — **MAUI** takes a `Keyboard` (`Keyboard.Email`, `Keyboard.Numeric`, …); **Blazor** takes an HTML `inputType` string (`"email"`, `"number"`, `"password"`, …). |
| `ActionSheet(title, options, cancelText = "Cancel", destructive = null, configure?)` | `Task<string?>` | One button per option + cancel. Returns the chosen option text, or `null` if cancelled. `destructive` (must match an option) renders that one in red. Defaults to a bottom slide-up, anchored to the bottom of the screen. |

`configure` is an optional `Action<DialogConfig>` for per-call animation and styling. `options` is an `IReadOnlyList<string>`.

**Hiding the cancel button**: `cancelText` is nullable on both `Prompt` and `ActionSheet`. Pass `cancelText: null` to drop the cancel button entirely (otherwise the owned ActionSheet always renders one — unlike a native iOS-style sheet, where a `null` cancel shows nothing). This makes `IDialogService` a clean target when adapting from `Shiny.Framework`'s `IDialogs`, whose `cancel: null` means "no cancel button".

### PromptResult

| Member | Type | Description |
|---|---|---|
| `Ok` | `bool` | True when the user confirmed. |
| `Cancelled` | `bool` | Inverse of `Ok`. |
| `Value` | `string?` | Entered text. `null` when cancelled. |

## Animations

Every dialog animates in and out with an owned, cross-platform animation (no native dialog). Set per-call via `configure`, or globally as the default.

`DialogAnimation`: `None`, `Fade`, `SlideTop`, `SlideBottom`, `SlideLeft`, `SlideRight`, `Zoom`, `Pop` (default).

```csharp
await dialogs.Confirm("Delete?", "This cannot be undone.", configure: c =>
{
    c.Animation = DialogAnimation.SlideBottom;
});
```

## Customization

Four levels, smallest to largest:

1. **Per-call config** — the `configure` delegate. Common `DialogConfig` members: `Animation`, `BackgroundColor`, `TitleColor`, `MessageColor`, `OkButtonColor`, `OkButtonTextColor`, `CancelButtonColor`, `CancelButtonTextColor`, `CornerRadius`, `BackdropOpacity`, `DismissOnBackdrop`, `MaxWidth` (MAUI), `DismissOnEscape`/`CssClass`/`InputType` (Blazor). Colors are MAUI `Color` on MAUI and CSS strings on Blazor.

   ```csharp
   await dialogs.Confirm("Custom", "Brand colors + zoom.", configure: c =>
   {
       c.Animation = DialogAnimation.Zoom;
       c.BackgroundColor = Color.FromArgb("#312E81");   // Blazor: "#312E81"
       c.OkButtonColor = Color.FromArgb("#22D3EE");
       c.CornerRadius = 24;
   });
   ```

2. **Global defaults**

   ```csharp
   // MAUI
   builder.UseMauiApp<App>().UseShinyControls(c => c.ConfigureDialogs(o =>
   {
       o.DefaultAnimation = DialogAnimation.Zoom;
       o.ConfigureDefaults = cfg => cfg.CornerRadius = 20;   // app-wide
   }));

   // Blazor
   builder.Services.AddShinyDialogs(o => o.DefaultAnimation = DialogAnimation.Zoom);
   ```

3. **Full template override** — replace the dialog card while the host still provides the dimmed backdrop and animation. The binding context / fragment context is a `DialogContext` (`Title`, `Message`, `OkText`, `CancelText`, `Placeholder`, `IsPrompt`, `IsActionSheet`, `HasCancel`, `Actions`, `DestructiveAction`, two-way `PromptValue`, `ConfirmCommand`/`CancelCommand`/`SelectCommand` on MAUI or `Confirm()`/`Cancel()`/`Select(option)` on Blazor).

   ```csharp
   // MAUI: a DataTemplate bound to DialogContext
   c.ConfigureDialogs(o => o.ContentTemplate = new DataTemplate(() => { /* build a View, bind to DialogContext */ }));
   ```

   ```razor
   @* Blazor: a RenderFragment<DialogContext> *@
   <DialogHost>
       <Template Context="ctx">
           <div class="my-card">
               <h3>@ctx.Title</h3>
               <p>@ctx.Message</p>
               @if (ctx.IsPrompt)
               {
                   <input @bind="ctx.PromptValue" />
               }
               <button @onclick="ctx.Cancel">@ctx.CancelText</button>
               <button @onclick="ctx.Confirm">@ctx.OkText</button>
           </div>
       </Template>
   </DialogHost>
   ```

4. **Replace the service** (MAUI) — `c.SetCustomDialogs<MyDialogService>()`.

## Behavior

- **Modal & queued**: dialogs show one at a time; awaiting several in a row displays them sequentially.
- **Backdrop / Escape**: tapping the dimmed backdrop cancels (configurable via `DismissOnBackdrop`); on Blazor `Escape` cancels and `Enter` confirms. On MAUI, a hardware-keyboard `Escape` cancels too, whenever the dialog has a Cancel button or `DismissOnBackdrop` is on.
- **Keyboard shortcuts**: an open MAUI dialog is a modal keyboard-shortcut scope (`KeyboardShortcuts.IsModal`), so page shortcuts are blocked until it closes (see `keyboard-shortcuts.md`).
- **Theming**: surface/text/outline/primary colors come from the theme tokens, so dialogs match light/dark automatically.

## Code Generation Guidance

- Inject `IDialogService` (not the concrete `DialogService`).
- Use `Alert` for acknowledgements, `Confirm` for a single destructive action (label OK with the verb, e.g. "Delete"), `Prompt` for single-line input, `ActionSheet` to pick from several options.
- Always check `result.Ok` before using `result.Value`; for `ActionSheet`, a `null` return means cancelled — branch on the returned option text (mark the dangerous one with `destructive:`).
- For `Prompt`, pass `initialValue`/`maxLength` directly and set the input mode with the keyboard arg (`keyboard: Keyboard.Email` on MAUI, `inputType: "email"` on Blazor) rather than reaching into `configure`.
- Pass `cancelText: null` to `Prompt` or `ActionSheet` when the caller wants no cancel button.
- MAUI: no setup beyond `UseShinyControls()`. Blazor: `AddShinyDialogs()` + a single `<DialogHost />`.
- Set the animation via `configure: c => c.Animation = DialogAnimation.X`, or globally with `ConfigureDialogs`/`AddShinyDialogs(o => ...)`.
