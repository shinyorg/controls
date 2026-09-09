using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

/// <summary>
/// A modal window: a titled panel over a backdrop that owns the screen until it is dismissed.
/// </summary>
/// <remarks>
/// Every region is optional and replaceable — a title string or a whole <see cref="HeaderTemplate"/>,
/// the built-in close button or <see cref="CloseButtonTemplate"/> or neither, a
/// <see cref="FooterTemplate"/> or a list of <see cref="Buttons"/>. What it does not leave to the
/// caller is the modal contract itself: focus moves into the panel and is trapped there, the page
/// behind stops scrolling, Escape and the backdrop close it, focus goes back where it came from, and
/// the panel is announced as <c>role="dialog" aria-modal="true"</c>.
/// <para>
/// <see cref="Closing"/> is raised before any of that unwinds and can veto it, which is how a dirty
/// form refuses to disappear. Binding <c>IsOpen</c> to false is the one path that skips the veto: the
/// page has already decided.
/// </para>
/// <para>
/// Blazor-only. The MAUI half of the library reaches the same place with <c>FloatingPanel</c> and the
/// dialog service, which are built on native presentation rather than a positioned element.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// &lt;ModalView @bind-IsOpen="showEdit" Title="Edit customer" Size="ModalSize.Large"
///            Buttons="@buttons" Closed="OnClosed"&gt;
///     &lt;EditForm Model="customer"&gt;...&lt;/EditForm&gt;
/// &lt;/ModalView&gt;
/// </code>
/// </example>
public partial class ModalView : IAsyncDisposable
{
    readonly string instanceId = "shiny-modal-" + Guid.NewGuid().ToString("N");
    readonly string titleId = "shiny-modal-title-" + Guid.NewGuid().ToString("N");

    const string IconClose = "<svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round'><path d='M6 6l12 12M18 6L6 18'/></svg>";
    const string IconMaximize = "<svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linejoin='round'><rect x='4' y='4' width='16' height='16' rx='2'/></svg>";
    const string IconRestore = "<svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linejoin='round'><rect x='4' y='8' width='12' height='12' rx='2'/><path d='M8 8V6a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2h-2'/></svg>";

    Microsoft.AspNetCore.Components.ElementReference rootEl;
    Microsoft.AspNetCore.Components.ElementReference panelEl;
    IJSObjectReference? module;
    DotNetObjectReference<ModalView>? selfRef;

    bool hasRendered;

    /// <summary>The root is in the DOM: open, or still animating out.</summary>
    bool isRendered;

    /// <summary>The open class is on, which is what the entry and exit transitions run against.</summary>
    bool isVisible;

    bool jsAttached;

    /// <summary>Guards re-entry while an open or close sequence is mid-flight.</summary>
    bool inTransition;

    /// <summary>
    /// The panel has been rendered closed and is waiting for the frame that opens it. A flag rather
    /// than an inference from the state: mid-exit looks exactly like pre-entry — panel present, open
    /// class off — and reading it that way re-runs the open step on the way out.
    /// </summary>
    bool pendingOpen;


    // ---------------------------------------------------------------------------------------------
    // Content
    // ---------------------------------------------------------------------------------------------

    /// <summary>The modal's body.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Title text for the built-in header.</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Optional second line under <see cref="Title"/>.</summary>
    [Parameter] public string? Subtitle { get; set; }

    /// <summary>Inline SVG or a glyph shown before the title.</summary>
    [Parameter] public string? Icon { get; set; }

    /// <summary>
    /// A header of your own. Replaces <see cref="Title"/>, <see cref="Subtitle"/> and
    /// <see cref="Icon"/>, and sits inside the header bar beside the close button — so a custom
    /// header still drags, and still has somewhere to close from.
    /// </summary>
    [Parameter] public RenderFragment? HeaderTemplate { get; set; }

    /// <summary>
    /// Draw the header bar at all. False leaves the body flush to the top; give the modal an
    /// <see cref="AriaLabel"/> then, because there is no title to name it by.
    /// </summary>
    [Parameter] public bool ShowHeader { get; set; } = true;

    /// <summary>
    /// A footer of your own. Wins over <see cref="Buttons"/>; the footer is only drawn when one of
    /// the two is set.
    /// </summary>
    [Parameter] public RenderFragment? FooterTemplate { get; set; }

    /// <summary>The footer's actions, in the order they are rendered.</summary>
    [Parameter] public IReadOnlyList<ModalButton>? Buttons { get; set; }


    // ---------------------------------------------------------------------------------------------
    // Close affordances
    // ---------------------------------------------------------------------------------------------

    /// <summary>Draw the close button in the header.</summary>
    [Parameter] public bool ShowCloseButton { get; set; } = true;

    /// <summary>
    /// Your own close control instead of the built-in ✕. It is rendered inside the header's button
    /// row and wired to close the modal, so it does not need a click handler of its own.
    /// </summary>
    [Parameter] public RenderFragment? CloseButtonTemplate { get; set; }

    /// <summary>Accessible name for the close button.</summary>
    [Parameter] public string CloseButtonLabel { get; set; } = "Close";

    /// <summary>Accessible name for the maximise button.</summary>
    [Parameter] public string MaximizeButtonLabel { get; set; } = "Maximize";

    /// <summary>Accessible name for the maximise button once the panel is maximised.</summary>
    [Parameter] public string RestoreButtonLabel { get; set; } = "Restore";

    /// <summary>Close when the backdrop is clicked.</summary>
    [Parameter] public bool CloseOnBackdropClick { get; set; } = true;

    /// <summary>Close when Escape is pressed. Only the topmost modal answers.</summary>
    [Parameter] public bool CloseOnEscape { get; set; } = true;

    /// <summary>
    /// Nudge the panel when a dismissal is refused - a click on a backdrop that does not close, or
    /// any route <see cref="Closing"/> cancels - so a blocked dismissal reads as "not this one"
    /// rather than as a dead click.
    /// </summary>
    [Parameter] public bool NudgeOnBlockedDismiss { get; set; } = true;


    // ---------------------------------------------------------------------------------------------
    // State
    // ---------------------------------------------------------------------------------------------

    /// <summary>Whether the modal is showing. Two-way bindable.</summary>
    [Parameter] public bool IsOpen { get; set; }

    /// <summary>Two-way binding hook for <see cref="IsOpen"/>.</summary>
    [Parameter] public EventCallback<bool> IsOpenChanged { get; set; }

    /// <summary>Raised once the modal is on screen and focus has moved into it.</summary>
    [Parameter] public EventCallback Opened { get; set; }

    /// <summary>
    /// Raised before the modal closes. Set <see cref="ModalClosingEventArgs.Cancel"/> to keep it
    /// open. Skipped when the close came from <c>IsOpen</c> being bound to false.
    /// </summary>
    [Parameter] public EventCallback<ModalClosingEventArgs> Closing { get; set; }

    /// <summary>Raised after the modal has left the screen, with what closed it.</summary>
    [Parameter] public EventCallback<ModalCloseReason> Closed { get; set; }


    // ---------------------------------------------------------------------------------------------
    // Size and position
    // ---------------------------------------------------------------------------------------------

    /// <summary>How wide the panel may grow. Overridden by <see cref="Width"/>.</summary>
    [Parameter] public ModalSize Size { get; set; } = ModalSize.Medium;

    /// <summary>Where the panel sits in the viewport.</summary>
    [Parameter] public ModalPlacement Placement { get; set; } = ModalPlacement.Center;

    /// <summary>Exact panel width, as CSS. Unset lets <see cref="Size"/> decide.</summary>
    [Parameter] public string? Width { get; set; }

    /// <summary>Exact panel height, as CSS. Unset sizes to the content.</summary>
    [Parameter] public string? Height { get; set; }

    /// <summary>Cap on the panel's width, as CSS.</summary>
    [Parameter] public string? MaxWidth { get; set; }

    /// <summary>
    /// Cap on the panel's height, as CSS. Defaults to leaving a margin around the viewport, which is
    /// what keeps a long modal scrolling in its body instead of running off the screen.
    /// </summary>
    [Parameter] public string? MaxHeight { get; set; }

    /// <summary>
    /// Cap on the scrolling content region, as CSS - <c>MaxHeight</c> for the body rather than for the
    /// whole panel. The panel then shrink-wraps its content up to this point and scrolls beyond it,
    /// with the header and footer keeping their own height either way, so there is no arithmetic over
    /// the chrome to work out a panel height. Requires <see cref="ScrollBody"/>.
    /// </summary>
    [Parameter] public string? ContentMaxHeight { get; set; }

    /// <summary>
    /// Scroll the body when the content is taller than the panel, keeping the header and footer
    /// pinned - which is what guarantees the footer's buttons stay reachable however long the content
    /// runs. False instead lets the panel grow past the viewport and scrolls the layer behind it,
    /// taking the header and footer with it. Setting <see cref="ContentMaxHeight"/> overrides false,
    /// since a capped region that does not scroll only hides the rest.
    /// </summary>
    [Parameter] public bool ScrollBody { get; set; } = true;


    // ---------------------------------------------------------------------------------------------
    // Chrome
    // ---------------------------------------------------------------------------------------------

    /// <summary>Entry and exit motion.</summary>
    [Parameter] public ModalAnimation Animation { get; set; } = ModalAnimation.Pop;

    /// <summary>Animation length in milliseconds. Zero snaps.</summary>
    [Parameter] public int AnimationDuration { get; set; } = 200;

    /// <summary>Draw the dimmed backdrop.</summary>
    [Parameter] public bool ShowBackdrop { get; set; } = true;

    /// <summary>How dark the backdrop is, 0 to 1.</summary>
    [Parameter] public double BackdropOpacity { get; set; } = 0.45;

    /// <summary>Frost the page behind the backdrop as well as dimming it.</summary>
    [Parameter] public bool BlurBackdrop { get; set; }

    /// <summary>Panel corner radius, as CSS.</summary>
    [Parameter] public string? CornerRadius { get; set; }

    /// <summary>Panel fill, as CSS.</summary>
    [Parameter] public string? Background { get; set; }

    /// <summary>Body padding. Bare numbers are read as pixels, so <c>"16 24"</c> works.</summary>
    [Parameter] public string? ContentPadding { get; set; }

    /// <summary>Extra classes for the panel.</summary>
    [Parameter] public string? CssClass { get; set; }


    // ---------------------------------------------------------------------------------------------
    // Window behaviour
    // ---------------------------------------------------------------------------------------------

    /// <summary>Let the user move the panel by dragging its header.</summary>
    [Parameter] public bool Draggable { get; set; }

    /// <summary>Let the user resize the panel from its bottom-right corner.</summary>
    [Parameter] public bool Resizable { get; set; }

    /// <summary>
    /// Whether the panel can be maximised at all. Implied by <see cref="ShowMaximizeButton"/>; set it
    /// on its own for a window that maximises on a header double-click but carries no button.
    /// </summary>
    [Parameter] public bool AllowMaximize { get; set; }

    /// <summary>Show a maximise/restore button in the header. Implies <see cref="AllowMaximize"/>.</summary>
    [Parameter] public bool ShowMaximizeButton { get; set; }

    /// <summary>
    /// Double-clicking the header maximises and restores, the way a desktop window does. Only acts
    /// when maximising is allowed, so it costs nothing to leave on.
    /// </summary>
    [Parameter] public bool MaximizeOnHeaderDoubleClick { get; set; } = true;

    /// <summary>Whether the panel currently fills the viewport. Two-way bindable.</summary>
    [Parameter] public bool IsMaximized { get; set; }

    /// <summary>Two-way binding hook for <see cref="IsMaximized"/>.</summary>
    [Parameter] public EventCallback<bool> IsMaximizedChanged { get; set; }


    // ---------------------------------------------------------------------------------------------
    // Accessibility
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Accessible name for the panel. Unset uses <see cref="Title"/> — set it when the header is a
    /// template, or when there is no header at all.
    /// </summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Id of an element inside the modal that describes it, for <c>aria-describedby</c>.</summary>
    [Parameter] public string? AriaDescribedBy { get; set; }

    /// <summary>
    /// Move focus into the panel when it opens — the first focusable element, or whatever carries
    /// <c>data-shiny-autofocus</c>.
    /// </summary>
    [Parameter] public bool AutoFocus { get; set; } = true;

    /// <summary>Keep Tab inside the panel while it is open.</summary>
    [Parameter] public bool TrapFocus { get; set; } = true;

    /// <summary>Put focus back on whatever had it when the modal opened.</summary>
    [Parameter] public bool RestoreFocus { get; set; } = true;

    /// <summary>Stop the page behind the modal from scrolling.</summary>
    [Parameter] public bool LockScroll { get; set; } = true;


    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }

    IDictionary<string, object>? ExtraAttributes { get; set; }
    string? UserClass { get; set; }
    string? UserStyle { get; set; }

    [Inject] IJSRuntime JS { get; set; } = default!;


    // ---------------------------------------------------------------------------------------------
    // Public surface
    // ---------------------------------------------------------------------------------------------

    /// <summary>Open the modal. A no-op when it is already open.</summary>
    public async Task ShowAsync()
    {
        if (this.IsOpen)
            return;

        this.IsOpen = true;
        await this.IsOpenChanged.InvokeAsync(true);
        await this.BeginOpenAsync();
    }


    /// <summary>
    /// Close the modal, giving <see cref="Closing"/> its chance to veto.
    /// </summary>
    /// <returns>False when the modal was already closed, or a handler cancelled the close.</returns>
    public async Task<bool> CloseAsync(ModalCloseReason reason = ModalCloseReason.Programmatic)
    {
        if (!this.IsOpen || this.inTransition)
            return false;

        if (this.Closing.HasDelegate)
        {
            var args = new ModalClosingEventArgs(reason);
            await this.Closing.InvokeAsync(args);
            if (args.Cancel)
            {
                // A veto is a blocked dismissal like any other, and without this it is a dead click:
                // whatever the handler wants to say about the refusal is behind the panel it just
                // refused to move.
                await this.NudgeAsync();
                return false;
            }
        }

        // Claimed before the binding round-trip: invoking IsOpenChanged re-renders the parent, whose
        // parameters land back here with IsOpen already false - and OnParametersSetAsync would start a
        // second close on top of this one. BeginCloseAsync owns the flag from here and clears it.
        this.inTransition = true;
        this.IsOpen = false;
        await this.IsOpenChanged.InvokeAsync(false);
        await this.BeginCloseAsync(reason);
        return true;
    }


    /// <summary>Open a closed modal, close an open one.</summary>
    public Task ToggleAsync() => this.IsOpen ? this.CloseAsync() : this.ShowAsync();


    /// <summary>Fill the viewport, or go back to the sized panel.</summary>
    public async Task SetMaximizedAsync(bool maximized)
    {
        if (this.IsMaximized == maximized)
            return;

        this.IsMaximized = maximized;

        // A panel that was dragged or resized keeps those in inline styles, and they would fight the
        // maximised layout - and be gone on restore, which is worse. Clearing both is the only state
        // either transition can leave that reads correctly.
        if (this.module is not null)
            await this.module.InvokeVoidAsync("resetGeometry", this.instanceId);

        await this.IsMaximizedChanged.InvokeAsync(maximized);
        this.Repaint();
    }


    // ---------------------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------------------

    protected override void OnParametersSet()
    {
        this.ExtraAttributes = LayoutAttributes.Split(this.AdditionalAttributes, out var userClass, out var userStyle);
        this.UserClass = userClass;
        this.UserStyle = userStyle;
    }


    protected override async Task OnParametersSetAsync()
    {
        // The binding moved without going through ShowAsync/CloseAsync - the page set IsOpen itself.
        if (this.IsOpen && !this.isRendered && !this.inTransition)
            await this.BeginOpenAsync();
        else if (!this.IsOpen && this.isRendered && !this.inTransition)
            await this.BeginCloseAsync(ModalCloseReason.Programmatic);
    }


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            this.hasRendered = true;

        // The panel is rendered closed: wire the browser side up, then flip the class in a second
        // pass so the entry transition has two states to run between.
        if (this.pendingOpen)
        {
            this.pendingOpen = false;
            await this.AttachAsync();
            this.isVisible = true;
            this.StateHasChanged();
            await this.Opened.InvokeAsync();
        }
    }


    public async ValueTask DisposeAsync()
    {
        try
        {
            await this.DetachAsync();
            if (this.module is not null)
                await this.module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit went away before the modal did. Nothing left to unwind on the browser side.
        }
        catch (ObjectDisposedException)
        {
        }

        this.selfRef?.Dispose();
    }


    // ---------------------------------------------------------------------------------------------
    // Transitions
    // ---------------------------------------------------------------------------------------------

    Task BeginOpenAsync()
    {
        // The parent's re-render may have got here first, through OnParametersSetAsync.
        if (this.isRendered)
            return Task.CompletedTask;

        this.isRendered = true;
        this.isVisible = false;
        this.pendingOpen = true;
        this.Repaint();

        // The rest happens in OnAfterRenderAsync: the panel has to exist before focus can move into
        // it, and the open class has to land in a later frame than the panel itself.
        return Task.CompletedTask;
    }


    async Task BeginCloseAsync(ModalCloseReason reason)
    {
        this.inTransition = true;
        if (!this.isRendered)
        {
            this.inTransition = false;
            return;
        }

        try
        {
            this.isVisible = false;
            this.pendingOpen = false;
            this.Repaint();

            // Only wait for motion that is actually running - a headless test has no frames to wait
            // for, and Animation.None has nothing to wait on.
            if (this.hasRendered && this.Animation != ModalAnimation.None && this.AnimationDuration > 0)
                await Task.Delay(this.AnimationDuration);

            await this.DetachAsync();

            this.isRendered = false;
            this.Repaint();
        }
        finally
        {
            this.inTransition = false;
        }

        await this.Closed.InvokeAsync(reason);
    }


    /// <summary>
    /// Re-render, but only once there is something to re-render. These components are driven straight
    /// from their public methods in tests, where there is no render handle to ask.
    /// </summary>
    void Repaint()
    {
        if (this.hasRendered)
            this.StateHasChanged();
    }


    // ---------------------------------------------------------------------------------------------
    // Browser side
    // ---------------------------------------------------------------------------------------------

    async Task AttachAsync()
    {
        if (this.jsAttached)
            return;

        try
        {
            this.module ??= await this.JS.InvokeAsync<IJSObjectReference>(
                "import", "./_content/Shiny.Blazor.Controls/modal.js");

            this.selfRef ??= DotNetObjectReference.Create(this);

            await this.module.InvokeVoidAsync(
                "attach",
                this.instanceId,
                this.rootEl,
                this.panelEl,
                this.selfRef,
                new ModalJsOptions(
                    this.CloseOnEscape,
                    this.TrapFocus,
                    this.AutoFocus,
                    this.RestoreFocus,
                    this.LockScroll,
                    this.Draggable && this.ShowHeader,
                    this.Resizable
                )
            );
            this.jsAttached = true;
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
            // Prerendering, or a host with no module support. The modal still renders, closes on the
            // backdrop and on its buttons - it just does without the focus trap and the scroll lock.
        }
    }


    async Task DetachAsync()
    {
        if (!this.jsAttached || this.module is null)
            return;

        this.jsAttached = false;
        await this.module.InvokeVoidAsync("detach", this.instanceId);
    }


    /// <summary>Escape, from the browser. Only the topmost modal is asked.</summary>
    [JSInvokable]
    public Task OnEscapeJs() => this.CloseOnEscape ? this.CloseAsync(ModalCloseReason.Escape) : Task.FromResult(false);


    // ---------------------------------------------------------------------------------------------
    // Interaction
    // ---------------------------------------------------------------------------------------------

    internal async Task OnBackdropClick()
    {
        if (this.CloseOnBackdropClick)
        {
            await this.CloseAsync(ModalCloseReason.Backdrop);
            return;
        }

        await this.NudgeAsync();
    }


    /// <summary>Shake the panel, when the modal has refused to go away.</summary>
    async Task NudgeAsync()
    {
        if (this.NudgeOnBlockedDismiss && this.module is not null)
            await this.module.InvokeVoidAsync("nudge", this.instanceId);
    }


    internal Task OnCloseButtonClick() => this.CloseAsync(ModalCloseReason.CloseButton);

    internal Task OnMaximizeClick() => this.SetMaximizedAsync(!this.IsMaximized);

    internal Task OnHeaderDoubleClick()
        => this.CanMaximize && this.MaximizeOnHeaderDoubleClick
            ? this.SetMaximizedAsync(!this.IsMaximized)
            : Task.CompletedTask;


    internal async Task OnFooterButtonClick(ModalButton button)
    {
        if (button.Disabled)
            return;

        if (button.OnClick is not null)
            await button.OnClick();

        if (button.ClosesModal)
            await this.CloseAsync(ModalCloseReason.Button);
        else
            this.Repaint();
    }


    // ---------------------------------------------------------------------------------------------
    // Rendering
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Asking for a <see cref="ContentMaxHeight"/> is asking for the content to stop there, so it
    /// turns the body's scrolling on whatever <see cref="ScrollBody"/> says. The alternative reading -
    /// cap the region and let it overflow - only ever clips the rest away silently.
    /// </summary>
    bool EffectiveScrollBody => this.ScrollBody || !String.IsNullOrWhiteSpace(this.ContentMaxHeight);

    bool HasFooter => this.FooterTemplate is not null || this.Buttons is { Count: > 0 };

    /// <summary>Maximising is available - by the button, by double-click, or through code.</summary>
    internal bool CanMaximize => this.AllowMaximize || this.ShowMaximizeButton;

    bool HasHeader => this.ShowHeader &&
        (this.HeaderTemplate is not null
         || !String.IsNullOrWhiteSpace(this.Title)
         || this.ShowCloseButton
         || this.CloseButtonTemplate is not null
         || this.ShowMaximizeButton);

    string? EffectiveAriaLabel => this.AriaLabel ?? this.Title;

    /// <summary>
    /// The built-in title names the dialog when there is one - pointing at the visible heading beats
    /// a duplicate label, and a screen reader then reads the same words the page shows.
    /// </summary>
    string? LabelledBy => this.HasHeader && this.HeaderTemplate is null && !String.IsNullOrWhiteSpace(this.Title)
        ? this.titleId
        : null;

    internal string RootClasses
    {
        get
        {
            var builder = new StringBuilder("shiny-modal-root");
            builder.Append(this.isVisible ? " is-open" : " is-closed");
            builder.Append(this.Placement switch
            {
                ModalPlacement.Top => " place-top",
                ModalPlacement.Bottom => " place-bottom",
                _ => " place-center"
            });

            // The layer, not the panel, is what has to scroll when the panel is free to outgrow it.
            if (!this.EffectiveScrollBody)
                builder.Append(" grow-body");

            return builder.ToString();
        }
    }

    internal string PanelClasses
    {
        get
        {
            var builder = new StringBuilder("shiny-modal");
            builder.Append(this.Animation switch
            {
                ModalAnimation.None => " anim-none",
                ModalAnimation.Fade => " anim-fade",
                ModalAnimation.Zoom => " anim-zoom",
                ModalAnimation.SlideTop => " anim-slide-top",
                ModalAnimation.SlideBottom => " anim-slide-bottom",
                _ => " anim-pop"
            });
            builder.Append(this.Size switch
            {
                ModalSize.Small => " size-small",
                ModalSize.Large => " size-large",
                ModalSize.ExtraLarge => " size-xlarge",
                ModalSize.Full => " size-full",
                _ => " size-medium"
            });

            if (this.IsMaximized)
                builder.Append(" is-maximized");
            if (this.EffectiveScrollBody)
                builder.Append(" scroll-body");
            if (this.Draggable && this.ShowHeader)
                builder.Append(" is-draggable");
            if (!String.IsNullOrWhiteSpace(this.CssClass))
                builder.Append(' ').Append(this.CssClass);
            if (!String.IsNullOrWhiteSpace(this.UserClass))
                builder.Append(' ').Append(this.UserClass);

            return builder.ToString();
        }
    }

    string RootStyle
    {
        get
        {
            var style = new StringBuilder();
            style.Append("--shiny-modal-duration:").Append(Css.Ms(this.AnimationDuration)).Append(';');
            style.Append("--shiny-modal-backdrop-opacity:").Append(Css.Number(this.BackdropOpacity)).Append(';');
            return style.ToString();
        }
    }

    internal string PanelStyle
    {
        get
        {
            var style = new StringBuilder();
            Css.Append(style, "width", this.Width);
            Css.Append(style, "height", this.Height);
            Css.Append(style, "max-width", this.MaxWidth);
            Css.Append(style, "max-height", this.MaxHeight);
            Css.Append(style, "border-radius", this.CornerRadius);
            Css.Append(style, "background", this.Background);
            Css.Append(style, "--shiny-modal-content-padding", LayoutAttributes.Spacing(this.ContentPadding));
            Css.Append(style, "--shiny-modal-content-max-height", this.ContentMaxHeight);

            return LayoutAttributes.Append(style.ToString(), this.UserStyle);
        }
    }

    /// <summary>Options handed to modal.js. A named type: an anonymous one does not survive trimming.</summary>
    sealed record ModalJsOptions(
        bool CloseOnEscape,
        bool TrapFocus,
        bool AutoFocus,
        bool RestoreFocus,
        bool LockScroll,
        bool Draggable,
        bool Resizable
    );

    static class Css
    {
        public static void Append(StringBuilder style, string property, string? value)
        {
            if (!String.IsNullOrWhiteSpace(value))
                style.Append(property).Append(':').Append(value).Append(';');
        }

        public static string Ms(int value) => value.ToString(CultureInfo.InvariantCulture) + "ms";

        public static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
