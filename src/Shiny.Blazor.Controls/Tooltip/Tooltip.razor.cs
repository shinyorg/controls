using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

/// <summary>
/// A themed tooltip bubble that points at a target.
/// </summary>
/// <remarks>
/// <para>
/// Wrap the thing it describes, and the tooltip finds its own target:
/// </para>
/// <code>
/// &lt;Tooltip Text="Saves without closing" Placement="TooltipPlacement.Top"&gt;
///     &lt;button&gt;Apply&lt;/button&gt;
/// &lt;/Tooltip&gt;
/// </code>
/// <para>
/// Or leave it empty and point it at a selector, which is what a bound, view-model-driven tooltip
/// wants — it does not have to sit anywhere near its target in the markup:
/// </para>
/// <code>
/// &lt;Tooltip Target="#save" Text="Nothing to save yet" @@bind-IsOpen="showHint" /&gt;
/// </code>
/// <para>
/// The bubble is put in the browser's top layer with the popover API, so it is never clipped by an
/// <c>overflow: hidden</c> ancestor and never loses a z-index argument — the two things that make
/// in-tree popovers unusable. Browsers without the API fall back to fixed positioning.
/// </para>
/// </remarks>
public partial class Tooltip
{
    readonly string bubbleId = $"shiny-tt-{Guid.NewGuid():N}";

    IJSObjectReference? module;
    bool disposed;
    DotNetObjectReference<Tooltip>? selfRef;
    ElementReference anchorRef;
    ElementReference bubbleRef;

    bool open;
    bool observing;
    bool hovering;
    bool focused;
    string placement = "bottom";
    double tailOffset;
    CancellationTokenSource? showDelay;
    CancellationTokenSource? dismissDelay;
    CancellationTokenSource? longPress;


    /// <summary>The control the tooltip describes. Leave it out and use <see cref="Target"/> instead.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Markup inside the bubble, below the title and text.</summary>
    [Parameter] public RenderFragment? BubbleContent { get; set; }

    /// <summary>The tooltip body.</summary>
    [Parameter] public string? Text { get; set; }

    /// <summary>Optional heading above <see cref="Text"/>.</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>
    /// A CSS selector for the element to point at, when the tooltip is not wrapping it. Resolved each
    /// time the bubble opens, so it works for content that comes and goes.
    /// </summary>
    [Parameter] public string? Target { get; set; }

    /// <summary>Which side to prefer. <see cref="TooltipPlacement.Auto"/> picks the roomiest.</summary>
    [Parameter] public TooltipPlacement Placement { get; set; } = TooltipPlacement.Auto;

    /// <summary>Draw the pointer back at the target. Always off for <see cref="TooltipPlacement.Center"/>.</summary>
    [Parameter] public bool ShowTail { get; set; } = true;

    /// <summary>What opens the tooltip.</summary>
    [Parameter] public TooltipTrigger Trigger { get; set; } = TooltipTrigger.HoverOrFocus;

    /// <summary>
    /// Whether the bubble is showing. Two-way, so a trigger writes it back and a view-model can drive
    /// it directly.
    /// </summary>
    [Parameter] public bool IsOpen { get; set; }

    [Parameter] public EventCallback<bool> IsOpenChanged { get; set; }

    /// <summary>Milliseconds a trigger has to persist before the bubble appears.</summary>
    [Parameter] public int ShowDelay { get; set; } = 120;

    /// <summary>Milliseconds before the bubble closes itself. Zero leaves it up.</summary>
    [Parameter] public int AutoDismissDelay { get; set; }

    /// <summary>How long a press has to be held to count, for <see cref="TooltipTrigger.LongPress"/>.</summary>
    [Parameter] public int LongPressDelay { get; set; } = 450;

    /// <summary>Clicking the bubble closes it. Turn off when the bubble carries its own controls.</summary>
    [Parameter] public bool DismissOnClick { get; set; } = true;

    /// <summary>Gap between the target and the bubble's tail, in pixels.</summary>
    [Parameter] public double Offset { get; set; } = 8;

    /// <summary>How close to the viewport edges the bubble is allowed to get.</summary>
    [Parameter] public double ScreenMargin { get; set; } = 12;

    /// <summary>Ceiling on the bubble's width, so long text wraps rather than spanning the screen.</summary>
    [Parameter] public string MaxWidth { get; set; } = "280px";

    /// <summary>Overrides the bubble's background. Any CSS colour.</summary>
    [Parameter] public string? BubbleColor { get; set; }

    /// <summary>Overrides the bubble's text colour.</summary>
    [Parameter] public string? TextColor { get; set; }

    [Parameter] public TooltipAnimation Animation { get; set; } = TooltipAnimation.Scale;

    /// <summary>Runs when the bubble is clicked, before <see cref="DismissOnClick"/> acts.</summary>
    [Parameter] public EventCallback Clicked { get; set; }

    [Parameter] public EventCallback Opened { get; set; }

    [Parameter] public EventCallback Closed { get; set; }

    /// <summary>Extra classes for the bubble.</summary>
    [Parameter] public string? CssClass { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }


    string PlacementClass => $"shiny-tooltip--{this.placement}";

    string StateClass
    {
        get
        {
            var css = $"shiny-tooltip--{this.Animation.ToString().ToLowerInvariant()}";

            // Only the no-popover fallback needs this: with the API present, `:popover-open` is what
            // drives visibility and an extra class would fight it.
            return this.open ? css + " shiny-tooltip--open" : css;
        }
    }

    bool HasTail => this.placement != "center";

    /// <summary>
    /// Written as one attribute rather than several declarations, because a caller's splatted
    /// <c>style</c> would replace a literal one outright and silently drop these custom properties.
    /// </summary>
    string RootStyle
    {
        get
        {
            var style = $"--shiny-tooltip-max-width:{this.MaxWidth};--shiny-tooltip-tail-offset:{this.tailOffset.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}px;";

            if (!string.IsNullOrWhiteSpace(this.BubbleColor))
                style += $"--shiny-tooltip-bg:{this.BubbleColor};";

            if (!string.IsNullOrWhiteSpace(this.TextColor))
                style += $"--shiny-tooltip-fg:{this.TextColor};";

            if (this.AdditionalAttributes is not null
                && this.AdditionalAttributes.TryGetValue("style", out var extra)
                && extra is string extraStyle
                && !string.IsNullOrWhiteSpace(extraStyle))
            {
                style += extraStyle.TrimEnd().TrimEnd(';') + ";";
            }

            return style;
        }
    }


    /// <summary>Opens the tooltip.</summary>
    public Task ShowAsync() => this.SetOpenAsync(true);

    /// <summary>Closes the tooltip.</summary>
    public Task HideAsync() => this.SetOpenAsync(false);

    public Task ToggleAsync() => this.SetOpenAsync(!this.open);


    protected override async Task OnParametersSetAsync()
    {
        if (this.IsOpen != this.open)
            await this.SetOpenAsync(this.IsOpen, notify: false);
    }


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        try
        {
            var loaded = await this.JS.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/Shiny.Blazor.Controls/tooltip.js"
            );
            if (this.disposed) { await loaded.ReleaseLateAsync(); return; }
            this.module = loaded;

            // A tooltip whose IsOpen bound true before the module loaded has nothing to place against yet.
            if (this.open)
                await this.PresentAsync();
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
        catch (JSException) { }
    }


    // ---------------------------------------------------------------------------------------------
    // Triggers
    // ---------------------------------------------------------------------------------------------

    bool OpensOnHover => this.Trigger is TooltipTrigger.Hover or TooltipTrigger.HoverOrFocus;

    bool OpensOnFocus => this.Trigger is TooltipTrigger.Focus or TooltipTrigger.HoverOrFocus;


    async Task OnPointerEnterAsync(PointerEventArgs e)
    {
        if (!this.OpensOnHover)
            return;

        // A touch "pointerenter" fires on tap and would open a hover tooltip that then has no way to
        // close. Touch is served by LongPress instead.
        if (string.Equals(e.PointerType, "touch", StringComparison.OrdinalIgnoreCase))
            return;

        this.hovering = true;
        await this.OpenAfterDelayAsync();
    }


    async Task OnPointerLeaveAsync(PointerEventArgs e)
    {
        this.hovering = false;
        this.CancelLongPress();

        if (this.OpensOnHover && !this.focused)
            await this.SetOpenAsync(false);
    }


    async Task OnPointerDownAsync(PointerEventArgs e)
    {
        if (this.Trigger != TooltipTrigger.LongPress)
            return;

        this.CancelLongPress();
        this.longPress = new CancellationTokenSource();
        var token = this.longPress.Token;

        try
        {
            await Task.Delay(Math.Max(1, this.LongPressDelay), token);
            await this.SetOpenAsync(true);
        }
        catch (OperationCanceledException)
        {
            // The finger came up (or moved off) before the hold was long enough.
        }
    }


    Task OnPointerUpAsync(PointerEventArgs e)
    {
        this.CancelLongPress();
        return Task.CompletedTask;
    }


    async Task OnAnchorClickAsync(MouseEventArgs e)
    {
        if (this.Trigger == TooltipTrigger.Click)
            await this.ToggleAsync();
    }


    async Task OnFocusInAsync(FocusEventArgs e)
    {
        if (!this.OpensOnFocus)
            return;

        this.focused = true;
        await this.OpenAfterDelayAsync();
    }


    async Task OnFocusOutAsync(FocusEventArgs e)
    {
        this.focused = false;

        if (this.OpensOnFocus && !this.hovering)
            await this.SetOpenAsync(false);
    }


    async Task OnBubbleClickAsync(MouseEventArgs e)
    {
        if (this.Clicked.HasDelegate)
            await this.Clicked.InvokeAsync();

        if (this.DismissOnClick)
            await this.SetOpenAsync(false);
    }


    void CancelLongPress()
    {
        this.longPress?.Cancel();
        this.longPress?.Dispose();
        this.longPress = null;
    }


    async Task OpenAfterDelayAsync()
    {
        this.showDelay?.Cancel();
        this.showDelay?.Dispose();

        if (this.ShowDelay <= 0)
        {
            await this.SetOpenAsync(true);
            return;
        }

        this.showDelay = new CancellationTokenSource();
        var token = this.showDelay.Token;

        try
        {
            await Task.Delay(this.ShowDelay, token);

            // The pointer may have left again while the delay ran.
            if (this.hovering || this.focused)
                await this.SetOpenAsync(true);
        }
        catch (OperationCanceledException)
        {
        }
    }


    // ---------------------------------------------------------------------------------------------
    // Open / close
    // ---------------------------------------------------------------------------------------------

    async Task SetOpenAsync(bool value, bool notify = true)
    {
        if (this.open == value)
            return;

        this.open = value;
        this.IsOpen = value;

        if (notify && this.IsOpenChanged.HasDelegate)
            await this.IsOpenChanged.InvokeAsync(value);

        if (value)
            await this.PresentAsync();
        else
            await this.DismissAsync();

        this.StateHasChanged();
    }


    async Task PresentAsync()
    {
        if (this.module is null)
            return;

        try
        {
            await this.module.InvokeAsync<bool>("open", this.bubbleRef);
            await this.PlaceAsync();

            this.selfRef ??= DotNetObjectReference.Create(this);
            if (!this.observing)
            {
                await this.module.InvokeVoidAsync("observe", this.bubbleId, this.selfRef);
                this.observing = true;
            }

            this.StartAutoDismiss();

            if (this.Opened.HasDelegate)
                await this.Opened.InvokeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
    }


    async Task DismissAsync()
    {
        this.dismissDelay?.Cancel();
        this.dismissDelay?.Dispose();
        this.dismissDelay = null;

        if (this.module is null)
            return;

        try
        {
            if (this.observing)
            {
                await this.module.InvokeVoidAsync("unobserve", this.bubbleId);
                this.observing = false;
            }

            await this.module.InvokeVoidAsync("close", this.bubbleRef);

            if (this.Closed.HasDelegate)
                await this.Closed.InvokeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
    }


    async Task PlaceAsync()
    {
        if (this.module is null)
            return;

        // The wrapper when it is wrapping, the selector when it is not. Passed as-is: the JS side takes
        // either an element reference or a selector string.
        object? target = this.ChildContent is not null
            ? this.anchorRef
            : this.Target;

        if (target is null)
            return;

        try
        {
            var result = await this.module.InvokeAsync<TooltipPlacementResult?>(
                "place",
                this.bubbleRef,
                target,
                this.Placement.ToString().ToLowerInvariant(),
                this.Offset,
                this.ScreenMargin,
                16d
            );

            if (result is null)
                return;

            var moved = this.placement != (result.Placement ?? "bottom")
                || Math.Abs(this.tailOffset - result.TailOffset) > 0.5;

            this.placement = result.Placement ?? "bottom";
            this.tailOffset = result.TailOffset;

            if (moved)
            {
                // The tail moves to a different edge, which is a class change rather than something JS can
                // write, so the bubble is re-rendered and then re-placed against its new size.
                this.StateHasChanged();
                await Task.Yield();
                await this.module.InvokeAsync<TooltipPlacementResult?>(
                    "place",
                    this.bubbleRef,
                    target,
                    this.placement,
                    this.Offset,
                    this.ScreenMargin,
                    16d
                );
            }
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
        catch (JSException) { }
    }


    void StartAutoDismiss()
    {
        if (this.AutoDismissDelay <= 0)
            return;

        this.dismissDelay = new CancellationTokenSource();
        var token = this.dismissDelay.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(this.AutoDismissDelay, token);
                await this.InvokeAsync(() => this.SetOpenAsync(false));
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }


    /// <summary>Called from JS when the page scrolls or resizes under an open bubble.</summary>
    [JSInvokable]
    public async Task OnViewportChangedJs()
    {
        if (this.open)
            await this.PlaceAsync();
    }


    public async ValueTask DisposeAsync()
    {
        this.disposed = true;
        this.showDelay?.Cancel();
        this.showDelay?.Dispose();
        this.dismissDelay?.Cancel();
        this.dismissDelay?.Dispose();
        this.CancelLongPress();

        if (this.module is not null)
        {
            try
            {
                if (this.observing)
                    await this.module.InvokeVoidAsync("unobserve", this.bubbleId);

                await this.module.InvokeVoidAsync("close", this.bubbleRef);
                await this.module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
            catch (JSException) { }
        }

        this.selfRef?.Dispose();
        GC.SuppressFinalize(this);
    }
}
