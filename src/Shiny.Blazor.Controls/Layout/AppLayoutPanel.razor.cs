using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

/// <summary>
/// A left- or right-docked panel of an <see cref="AppLayout"/>. It collapses between
/// <see cref="PanelState.Hidden"/>, a narrow <see cref="PanelState.Toolbar"/> rail and the full
/// <see cref="PanelState.Shown"/> width, is drag-resizable between <c>MinSize</c> and
/// <c>MaxSize</c>, and always scrolls its own body.
/// </summary>
public partial class AppLayoutPanel : IAsyncDisposable
{
    ElementReference panelRef;
    IJSObjectReference? module;
    bool disposed;
    DotNetObjectReference<AppLayoutPanel>? selfRef;

    PanelState currentState;
    PanelState lastState;
    double currentSize;
    double lastSize;
    bool compact;
    PanelState? restoreState;
    bool initialized;

    /// <summary>The shell this panel belongs to. Must be public — Blazor only matches cascading
    /// values onto public properties, and a private one is skipped silently.</summary>
    [CascadingParameter] public AppLayout? Layout { get; set; }
    [Inject] IJSRuntime JS { get; set; } = null!;

    /// <summary>Which edge of the layout the panel docks to.</summary>
    [Parameter] public PanelSide Side { get; set; } = PanelSide.Left;

    /// <summary>How much of the panel is showing. Supports <c>@bind-State</c>.</summary>
    [Parameter] public PanelState State { get; set; } = PanelState.Shown;
    [Parameter] public EventCallback<PanelState> StateChanged { get; set; }

    /// <summary>Expanded width in pixels. Supports <c>@bind-Size</c> and is updated after a drag.</summary>
    [Parameter] public double Size { get; set; } = 260;
    [Parameter] public EventCallback<double> SizeChanged { get; set; }

    /// <summary>Smallest width a drag can produce, in pixels.</summary>
    [Parameter] public double MinSize { get; set; } = 140;

    /// <summary>Largest width a drag can produce, in pixels.</summary>
    [Parameter] public double MaxSize { get; set; } = 640;

    /// <summary>Show the drag handle on the panel's inner edge. Defaults to true.</summary>
    [Parameter] public bool Resizable { get; set; } = true;

    /// <summary>Width of the rail in <see cref="PanelState.Toolbar"/>, in pixels.</summary>
    [Parameter] public double ToolbarSize { get; set; } = 56;

    /// <summary>
    /// Layout width, in pixels, under which the panel compacts. An expanded panel drops to
    /// <see cref="CollapsedState"/>, and re-expanding it there floats it over the content as a
    /// drawer. Zero (the default) disables the behaviour.
    /// </summary>
    [Parameter] public double CollapseBelow { get; set; }

    /// <summary>What an expanded panel collapses to under <see cref="CollapseBelow"/>.</summary>
    [Parameter] public PanelState CollapsedState { get; set; } = PanelState.Toolbar;

    /// <summary>Persist state and width to localStorage under this key and restore them on load.</summary>
    [Parameter] public string? PersistKey { get; set; }

    /// <summary>Give the panel body its own scroll region. Defaults to true.</summary>
    [Parameter] public bool Scrollable { get; set; } = true;

    /// <summary>Pinned above the scrolling body — a title, a close button, a search box.</summary>
    [Parameter] public RenderFragment? HeaderContent { get; set; }

    /// <summary>Pinned below the scrolling body.</summary>
    [Parameter] public RenderFragment? FooterContent { get; set; }

    /// <summary>Rendered instead of the body in <see cref="PanelState.Toolbar"/>.</summary>
    [Parameter] public RenderFragment? ToolbarContent { get; set; }

    /// <summary>The panel's current state, which may differ from <see cref="State"/> after a collapse.</summary>
    public PanelState CurrentState => this.currentState;

    /// <summary>The panel's current width in pixels, which may differ from <see cref="Size"/> after a drag.</summary>
    public double CurrentSize => this.currentSize;

    /// <summary>True while the panel is floating over the content as a compact drawer.</summary>
    public bool IsOverlay => this.compact && this.currentState == PanelState.Shown;

    string SideKey => this.Side == PanelSide.Left ? "left" : "right";
    string StateClass => "is-" + this.currentState.ToString().ToLowerInvariant();
    string StoreKey => "shiny.applayout." + this.PersistKey;

    protected override void OnInitialized()
        => this.Layout?.Register(this);

    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        // Only take the parameter back when the consumer actually changed it — otherwise an
        // unbound State/Size would undo every state change the panel makes itself.
        if (!this.initialized || this.State != this.lastState)
        {
            this.currentState = this.State;
            this.lastState = this.State;
        }

        if (!this.initialized || Math.Abs(this.Size - this.lastSize) > 0.01)
        {
            this.currentSize = this.Clamp(this.Size);
            this.lastSize = this.Size;
        }

        this.initialized = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        var loaded = await this.JS.InvokeAsync<IJSObjectReference>(
            "import",
            "./_content/Shiny.Blazor.Controls/app-layout.js"
        );
        if (this.disposed) { await loaded.ReleaseLateAsync(); return; }
        this.module = loaded;
        this.selfRef = DotNetObjectReference.Create(this);
        await this.module.InvokeVoidAsync("initPanel", this.panelRef, this.selfRef);

        await this.RestoreAsync();
    }

    /// <summary>Moves the panel to a state, raising <see cref="StateChanged"/>.</summary>
    public Task SetStateAsync(PanelState state)
        => this.SetStateAsync(state, persist: true);

    /// <summary>Toggles between <see cref="PanelState.Shown"/> and <see cref="CollapsedState"/>.</summary>
    public Task ToggleAsync()
        => this.SetStateAsync(
            this.currentState == PanelState.Shown ? this.CollapsedState : PanelState.Shown
        );

    async Task SetStateAsync(PanelState state, bool persist)
    {
        if (this.currentState == state)
            return;

        this.currentState = state;
        if (this.StateChanged.HasDelegate)
            await this.StateChanged.InvokeAsync(state);

        if (persist)
            await this.PersistAsync();

        this.StateHasChanged();
    }

    Task DismissOverlayAsync()
        => this.SetStateAsync(this.CollapsedState, persist: false);

    internal async Task OnHostWidthChangedAsync(double width)
    {
        var nowCompact = this.CollapseBelow > 0 && width > 0 && width < this.CollapseBelow;
        if (nowCompact == this.compact)
            return;

        this.compact = nowCompact;

        if (nowCompact && this.currentState == PanelState.Shown)
        {
            this.restoreState = PanelState.Shown;
            // not persisted: compacting is a response to the viewport, not a user preference
            await this.SetStateAsync(this.CollapsedState, persist: false);
        }
        else if (!nowCompact && this.restoreState is { } restore)
        {
            this.restoreState = null;
            await this.SetStateAsync(restore, persist: false);
        }
        else
        {
            this.StateHasChanged();
        }
    }

    [JSInvokable]
    public async Task OnResizedJs(double width)
    {
        var clamped = this.Clamp(width);
        if (Math.Abs(clamped - this.currentSize) < 0.01)
            return;

        this.currentSize = clamped;
        if (this.SizeChanged.HasDelegate)
            await this.SizeChanged.InvokeAsync(clamped);

        await this.PersistAsync();
        this.StateHasChanged();
    }

    double Clamp(double value)
        => Math.Clamp(value, this.MinSize, Math.Max(this.MinSize, this.MaxSize));

    async Task PersistAsync()
    {
        if (this.module is null || string.IsNullOrWhiteSpace(this.PersistKey))
            return;

        var payload = $"{(int)this.currentState}|{LayoutAttributes.Num(this.currentSize)}";
        try
        {
            await this.module.InvokeVoidAsync("save", this.StoreKey, payload);
        }
        catch (JSDisconnectedException) { }
    }

    async Task RestoreAsync()
    {
        if (this.module is null || string.IsNullOrWhiteSpace(this.PersistKey))
            return;

        var raw = await this.module.InvokeAsync<string?>("load", this.StoreKey);
        if (!TryParse(raw, out var state, out var size))
            return;

        var changed = false;

        if (state != this.currentState)
        {
            this.currentState = state;
            changed = true;
            if (this.StateChanged.HasDelegate)
                await this.StateChanged.InvokeAsync(state);
        }

        var clamped = this.Clamp(size);
        if (Math.Abs(clamped - this.currentSize) > 0.01)
        {
            this.currentSize = clamped;
            changed = true;
            if (this.SizeChanged.HasDelegate)
                await this.SizeChanged.InvokeAsync(clamped);
        }

        if (changed)
            this.StateHasChanged();
    }

    // "<state>|<width>" rather than JSON — nothing to serialize, nothing for the trimmer to lose.
    static bool TryParse(string? raw, out PanelState state, out double size)
    {
        state = PanelState.Shown;
        size = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var parts = raw.Split('|');
        if (parts.Length != 2)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
            return false;

        if (!Enum.IsDefined(typeof(PanelState), s))
            return false;

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out size))
            return false;

        state = (PanelState)s;
        return true;
    }

    string PanelStyle
    {
        get
        {
            var hidden = this.currentState == PanelState.Hidden;
            var width = this.currentState switch
            {
                PanelState.Hidden => 0,
                PanelState.Toolbar => this.ToolbarSize,
                _ => this.currentSize
            };

            var css = $"width:{LayoutAttributes.Px(width)};";

            // the divider always sits on the edge facing the content
            var edge = this.Side == PanelSide.Left ? "right" : "left";

            // A hidden panel is zero-wide, but the border still paints — leaving a 1px sliver that
            // butts straight up against whatever divider is next to it and reads as one 2px line.
            // It has to be dropped here rather than in the stylesheet: this style is inline, so a
            // `.is-hidden { border-width: 0 }` rule would never win.
            css += hidden ? $"border-{edge}:none;" : this.BorderCss(edge);

            if (!string.IsNullOrWhiteSpace(this.Background))
                css += $"background:{this.Background};";

            if (this.IsOverlay)
                css += "max-width:90%;";

            return LayoutAttributes.Append(css, this.UserStyle);
        }
    }

    // Padding belongs to the scrolling body, not the panel, so a pinned header/footer sits flush.
    string? BodyStyle
        => string.IsNullOrWhiteSpace(this.Padding) ? null : $"padding:{LayoutAttributes.Spacing(this.Padding)};";

    public async ValueTask DisposeAsync()
    {
        this.disposed = true;
        this.Layout?.Unregister(this);

        if (this.module is not null)
        {
            try
            {
                await this.module.InvokeVoidAsync("disposePanel", this.panelRef);
                await this.module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
        }

        this.selfRef?.Dispose();
    }
}
