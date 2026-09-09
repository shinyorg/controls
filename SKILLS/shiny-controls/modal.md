# Modal (Blazor only)

`ModalView` is a **modal window**: a titled panel over a backdrop that owns the screen until it is dismissed. It ships in the core `Shiny.Blazor.Controls` package — **there is no MAUI equivalent, never emit `<ModalView>` in XAML**. The MAUI answer to the same problem is `FloatingPanel` (a sheet on a `ShinyContentPage`) or the dialog service.

> **Not the same thing as `Dialogs`.** `IDialogService` is service-first — you `await dialogs.Confirm(...)` and get a `bool`, with no markup. `ModalView` is declarative: you write the content, it hosts it. Reach for the dialog service for alert/confirm/prompt; reach for `ModalView` when the content is a form, an editor, a picker — anything with its own markup.

## Usage

```razor
@* Every region is optional: title or HeaderTemplate, close button or CloseButtonTemplate or neither,
   Buttons or FooterTemplate or nothing. *@
<ModalView @bind-IsOpen="showEdit"
           Title="Edit customer"
           Subtitle="Changes apply immediately"
           Size="ModalSize.Large"
           Buttons="@buttons"
           Opened="OnOpened"
           Closing="OnClosing"
           Closed="OnClosed">
    <EditForm Model="customer">
        <InputText @bind-Value="customer.Name" data-shiny-autofocus />
    </EditForm>
</ModalView>

@code {
    bool showEdit;

    // Fields, not an expression: a ModalButton carries state (State, Disabled) that a list rebuilt
    // on every render would throw away.
    readonly List<ModalButton> buttons = [];

    protected override void OnInitialized() => this.buttons.AddRange(
    [
        new("Cancel") { Type = ButtonType.Secondary, Appearance = ButtonAppearance.Text },
        new("Save") { ClosesModal = false, OnClick = SaveAsync }
    ]);

    async Task SaveAsync()
    {
        var save = this.buttons[1];
        save.State = ButtonState.Busy;
        StateHasChanged();

        await this.api.SaveAsync(this.customer);

        save.State = ButtonState.Normal;
        this.showEdit = false;   // close it yourself when ClosesModal is false
    }

    void OnClosing(ModalClosingEventArgs e)
        => e.Cancel = this.customer.IsDirty && e.Reason != ModalCloseReason.Button;
}
```

Open and close from code with the component reference instead of the binding when you want the result:

```razor
<ModalView @ref="modal" Title="Pick a date">…</ModalView>

@code {
    ModalView? modal;

    async Task Go()
    {
        await this.modal!.ShowAsync();
        // CloseAsync returns false when Closing vetoed it (or it was already closed).
        var closed = await this.modal.CloseAsync();
        await this.modal.ToggleAsync();
        await this.modal.SetMaximizedAsync(true);
    }
}
```

## What you get for free

Focus moves into the panel on open (the first focusable, or whatever carries `data-shiny-autofocus`) and is **trapped** there; the page behind stops scrolling without the layout jumping sideways; Escape and a backdrop click dismiss; focus returns to whatever had it; the panel is announced as `role="dialog" aria-modal="true"`, labelled by its own title. Modals **stack** — the newest sits on top, Escape only reaches the top one, and the scrollbar comes back only when the last one closes.

## Parameters

### Content
| Parameter | Type | Default | Notes |
|---|---|---|---|
| `ChildContent` | `RenderFragment?` | — | The body. |
| `Title` / `Subtitle` | `string?` | — | The built-in header. `Title` also becomes the accessible name. |
| `Icon` | `string?` | — | Inline SVG or a glyph before the title. |
| `HeaderTemplate` | `RenderFragment?` | — | Replaces the title block, **keeps** the header bar (so the close button still has a home). |
| `ShowHeader` | `bool` | `true` | False removes the bar entirely — set `AriaLabel` then. |
| `FooterTemplate` | `RenderFragment?` | — | Replaces the button row. Wins over `Buttons`. |
| `Buttons` | `IReadOnlyList<ModalButton>?` | — | Footer actions, rendered as `ShinyButton`. |

### Closing
| Parameter | Type | Default | Notes |
|---|---|---|---|
| `ShowCloseButton` | `bool` | `true` | The header ✕. |
| `CloseButtonTemplate` | `RenderFragment?` | — | Your own close control; already wired, no handler needed. |
| `CloseButtonLabel` | `string` | `"Close"` | Accessible name. |
| `CloseOnBackdropClick` | `bool` | `true` | |
| `CloseOnEscape` | `bool` | `true` | Only the topmost modal answers. |
| `NudgeOnBlockedDismiss` | `bool` | `true` | A refused dismissal shoves the panel instead of doing nothing — a backdrop click that does not close, and any route `Closing` cancels. |

### State and events
| Member | Type | Notes |
|---|---|---|
| `IsOpen` / `IsOpenChanged` | `bool` | Two-way bindable. |
| `Opened` | `EventCallback` | After the panel is up and focus is inside. |
| `Closing` | `EventCallback<ModalClosingEventArgs>` | Cancellable. **Skipped** when `IsOpen` is bound to false — the page already decided. |
| `Closed` | `EventCallback<ModalCloseReason>` | `Programmatic`, `CloseButton`, `Backdrop`, `Escape`, `Button`. |
| `ShowAsync()` / `CloseAsync(reason)` / `ToggleAsync()` | | `CloseAsync` returns `false` when it was vetoed or already closed. |

### Size, placement, chrome
| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Size` | `ModalSize` | `Medium` | `Small` 360 / `Medium` 520 / `Large` 760 / `ExtraLarge` 1080 / `Full`. Caps, not fixed widths. |
| `Placement` | `ModalPlacement` | `Center` | `Center`, `Top`, `Bottom`. |
| `Width` / `Height` / `MaxWidth` / `MaxHeight` | `string?` | — | CSS. `Width` beats `Size`. `MaxHeight` caps the whole panel; unset it is the viewport less the layer's gutter. |
| `ContentMaxHeight` | `string?` | — | CSS. Caps the **scrolling region**, not the panel — the panel shrink-wraps its content up to here, then scrolls. Use this instead of doing header + footer arithmetic to land on a `MaxHeight`. Always scrolls, whatever `ScrollBody` says. |
| `ScrollBody` | `bool` | `true` | Body scrolls, header and footer stay pinned — this is what keeps the footer buttons reachable. `false` lets the panel outgrow the viewport and scrolls the layer instead, taking the chrome with it. |
| `Animation` | `ModalAnimation` | `Pop` | `None`, `Fade`, `Zoom`, `Pop`, `SlideTop`, `SlideBottom`. |
| `AnimationDuration` | `int` | `200` | Milliseconds. |
| `ShowBackdrop` / `BackdropOpacity` / `BlurBackdrop` | `bool` / `double` / `bool` | `true` / `0.45` / `false` | With no backdrop the click surface stays, invisible. |
| `CornerRadius` / `Background` / `ContentPadding` / `CssClass` | `string?` | — | CSS; bare numbers in padding are read as pixels. |

### Window behaviour
| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Draggable` | `bool` | `false` | Move by the header. Header buttons are excluded from the drag surface. |
| `Resizable` | `bool` | `false` | Bottom-right grip. |
| `AllowMaximize` | `bool` | `false` | The capability on its own — for a double-click-only window. |
| `ShowMaximizeButton` | `bool` | `false` | The header button. Implies `AllowMaximize`. |
| `MaximizeOnHeaderDoubleClick` | `bool` | `true` | Only acts when maximising is allowed. |
| `IsMaximized` / `IsMaximizedChanged` | `bool` | `false` | Two-way bindable. Maximising drops any drag offset and resized size. |

Neither gesture can push the header or footer off screen: the grip stops at the layer's box, the drag keeps the whole panel inside the viewport, and shrinking the browser window pulls an overhanging panel back in. A panel taller than the viewport is the exception — the drag there is a range between its own edges, so the rest of it can still be read.

### Accessibility
`AriaLabel`, `AriaDescribedBy`, `AutoFocus` (default true), `TrapFocus` (default true), `RestoreFocus` (default true), `LockScroll` (default true).

## ModalButton

```csharp
new ModalButton("Save")
{
    Type = ButtonType.Primary,          // Primary/Secondary/Success/Warning/Critical/Info
    Appearance = ButtonAppearance.Filled, // Filled/Tonal/Outlined/Text/Elevated
    Icon = "<svg …>",
    Disabled = false,
    State = ButtonState.Normal,          // flip to Busy from OnClick
    ClosesModal = true,                  // false keeps it up while work runs
    OnClick = SaveAsync,                 // awaited before the close
    Tag = someContext
}
```

## Rules

- **Blazor only.** There is no `ModalView` on MAUI. On MAUI use `FloatingPanel` / `ShinyContentPage`, or `IDialogService`.
- **No host component.** Unlike `Toast`, `Dialogs` or `ProgressLine`, `ModalView` needs no `<ModalHost />` and no `AddShinyModal()` — it renders where you put it.
- Put it at page level, not inside a container with `transform`/`filter`/`contain`. Those create a containing block for `position: fixed` and would trap the panel inside that element.
- `Closing` is the only veto. A footer button's `OnClick` runs **before** it, so "save then close" works without touching `IsOpen`.
- A vetoed close shakes the panel (`NudgeOnBlockedDismiss`). Say *why* it was refused from inside the panel — anything the handler writes to the page is behind the modal that just refused to move.
- Set `AriaLabel` whenever there is no `Title` (a `HeaderTemplate` or `ShowHeader="false"`), or the dialog is announced unnamed.
- Mutating a `ModalButton` in place (`State`, `Disabled`) needs a `StateHasChanged` unless the click handler that changed it is the thing returning.
