# Modal (Blazor only)

[← All Shiny Controls](../../README.md)

A **modal window** — a titled panel over a backdrop that owns the screen until it is dismissed. Where `Dialogs` is service-first (`await dialogs.Confirm(...)`, no markup), `ModalView` is declarative: you write the content and it hosts it. Reach for it when the content is a form, an editor or a picker.

There is **no MAUI equivalent** — use `FloatingPanel` / `ShinyContentPage` or `IDialogService` there. It needs no host component and no DI registration: it renders where you put it.

```razor
<ModalView @bind-IsOpen="showEdit"
           Title="Edit customer"
           Subtitle="Changes apply immediately"
           Size="ModalSize.Large"
           Buttons="@buttons"
           Closing="OnClosing"
           Closed="@(r => log = $"closed by {r}")">
    <EditForm Model="customer">
        <InputText @bind-Value="customer.Name" data-shiny-autofocus />
    </EditForm>
</ModalView>

@code {
    bool showEdit;
    readonly List<ModalButton> buttons = [];

    protected override void OnInitialized() => buttons.AddRange(
    [
        new("Cancel") { Type = ButtonType.Secondary, Appearance = ButtonAppearance.Text },
        new("Save") { ClosesModal = false, OnClick = SaveAsync }   // stays up while the save runs
    ]);

    // The dirty-form veto: every dismissal route goes through it.
    void OnClosing(ModalClosingEventArgs e)
        => e.Cancel = customer.IsDirty && e.Reason != ModalCloseReason.Button;
}
```

**Every region is optional and replaceable**

| Region | Built in | Yours | Off |
|---|---|---|---|
| Header | `Title`, `Subtitle`, `Icon` | `HeaderTemplate` (keeps the bar, so the close button still has a home) | `ShowHeader="false"` |
| Close | the ✕ button | `CloseButtonTemplate` (already wired) | `ShowCloseButton="false"` |
| Footer | `Buttons` (a list of `ModalButton`, rendered as `ShinyButton`) | `FooterTemplate` | neither set |

**State and events** — two-way `IsOpen`, plus `ShowAsync()` / `CloseAsync(reason)` / `ToggleAsync()` on the component reference. `Opened` fires once focus is inside; `Closing` is cancellable (`ModalClosingEventArgs.Cancel`); `Closed` carries a `ModalCloseReason` — `CloseButton`, `Backdrop`, `Escape`, `Button` or `Programmatic`. Binding `IsOpen` to false is the one path that skips the veto: the page has already decided.

**Sizing and chrome** — `Size` (`Small` 360 / `Medium` 520 / `Large` 760 / `ExtraLarge` 1080 / `Full`, all caps rather than fixed widths) or explicit `Width`/`Height`/`MaxWidth`/`MaxHeight`; `Placement` `Center`/`Top`/`Bottom`; `Animation` `None`/`Fade`/`Zoom`/`Pop`/`SlideTop`/`SlideBottom` with `AnimationDuration`; `ShowBackdrop`, `BackdropOpacity`, `BlurBackdrop`; `CornerRadius`, `Background`, `ContentPadding`, `CssClass`.

**Long content, and the footer that has to survive it** — the panel is a column of header, body and footer where only the body gives. `ScrollBody` (on by default) scrolls that body and leaves the header and footer where they are, so the buttons that answer the modal stay reachable however long the content runs. There are two ways to say where the scrolling starts: `MaxHeight` caps the whole panel (unset, that is the viewport less the layer's gutter), and `ContentMaxHeight` caps the scrolling region itself — the panel then shrink-wraps its content up to that point and scrolls beyond it, with no arithmetic over the chrome to work out a panel height. A `ContentMaxHeight` always scrolls, whatever `ScrollBody` says, because a capped region that does not scroll just hides the rest. `ScrollBody="false"` is the opposite trade: the panel is free to outgrow the viewport and the layer scrolls behind it, taking the header and footer with it.

**A window when you want one** — `Draggable` moves it by the header (header buttons stay clickable), `Resizable` gives it a corner grip, and `AllowMaximize` / `ShowMaximizeButton` / `MaximizeOnHeaderDoubleClick` maximise and restore it. Maximising drops any drag offset and resized size, so restoring lands where the stylesheet says. Neither gesture can put the chrome out of reach: the grip stops growing at the layer's box, the drag keeps the whole panel on screen, and shrinking the browser window pulls a panel that no longer fits back inside it. A panel taller than the viewport is the one exception — there the drag becomes a range between its own edges, which is how you read the rest of it.

**The modal contract is not optional** — focus moves into the panel (the first focusable, or whatever carries `data-shiny-autofocus`) and is trapped there, the page behind stops scrolling without lurching sideways, Escape and the backdrop dismiss, focus goes back where it came from, and the panel is announced as `role="dialog" aria-modal="true"` labelled by its own title. Modals stack: the newest sits on top, Escape only reaches the topmost, and the scrollbar returns only when the last one closes. A refused dismissal nudges the panel instead of doing nothing — both a backdrop click that does not close and any route `Closing` cancels.

Keep it at page level, out of any ancestor carrying `transform`, `filter` or `contain` — those create a containing block for `position: fixed` and would trap the panel inside that element.
