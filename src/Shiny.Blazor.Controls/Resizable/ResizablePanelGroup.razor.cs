using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Shiny.Controls.Resizable;

namespace Shiny.Blazor.Controls;

/// <summary>
/// Panels laid side by side (or stacked) with draggable handles between them, sized in percent of the
/// group.
/// </summary>
/// <example>
/// <code>
/// &lt;ResizablePanelGroup Direction="ResizableDirection.Horizontal" Style="height:400px"&gt;
///     &lt;ResizablePanel DefaultSize="25" MinSize="15" Collapsible&gt;Sidebar&lt;/ResizablePanel&gt;
///     &lt;ResizableHandle WithHandle /&gt;
///     &lt;ResizablePanel&gt;
///         &lt;ResizablePanelGroup Direction="ResizableDirection.Vertical"&gt;
///             &lt;ResizablePanel DefaultSize="70"&gt;Editor&lt;/ResizablePanel&gt;
///             &lt;ResizableHandle /&gt;
///             &lt;ResizablePanel&gt;Terminal&lt;/ResizablePanel&gt;
///         &lt;/ResizablePanelGroup&gt;
///     &lt;/ResizablePanel&gt;
/// &lt;/ResizablePanelGroup&gt;
/// </code>
/// </example>
/// <remarks>
/// <para>
/// The group fills its parent (<c>width:100%; height:100%</c>), so give that parent a real size; a
/// nested group fills its panel. Panels are flex items sized by percentage, and the handles' one-pixel
/// lines come off the top first. A panel's content scrolls inside the panel - the group itself never
/// scrolls.
/// </para>
/// <para>
/// Dragging and the keyboard (arrow keys, Home, End, Enter to collapse) are handled by a small script
/// that moves the panels live and reports back once the change settles; <see cref="Sizes"/> and
/// <see cref="LayoutChanged"/> follow then, which makes them the place to save and restore a layout.
/// </para>
/// </remarks>
public partial class ResizablePanelGroup : IAsyncDisposable
{
    readonly List<(ResizablePanel Panel, int Sequence)> panels = new();
    readonly List<ResizableHandle> handles = new();
    ElementReference groupEl;
    IJSObjectReference? module;
    DotNetObjectReference<ResizablePanelGroup>? selfRef;
    double[]? sizes;
    IReadOnlyList<double>? lastSizesParameter;
    int sequence;
    bool disposed;
    // A drag, key press or method has settled the layout at least once; defaults no longer apply.
    bool hasSettled;
    ResizableDirection lastDirection;

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Side by side (the default) or stacked.</summary>
    [Parameter] public ResizableDirection Direction { get; set; } = ResizableDirection.Horizontal;

    /// <summary>
    /// Each panel's size in percent, in panel order. Bindable (<c>@bind-Sizes</c>): updated when a resize
    /// settles, and setting it (a saved layout) resizes the panels - held to their limits and scaled to
    /// add up to 100. Wins over <see cref="ResizablePanel.DefaultSize"/> when its count matches.
    /// </summary>
    [Parameter] public IReadOnlyList<double>? Sizes { get; set; }

    [Parameter] public EventCallback<IReadOnlyList<double>> SizesChanged { get; set; }

    /// <summary>The panels settled on new sizes: the end of a drag, a key press, a collapse or expand, or <see cref="SetSizesAsync"/>.</summary>
    [Parameter] public EventCallback<IReadOnlyList<double>> LayoutChanged { get; set; }

    /// <summary>How far one arrow-key press on a focused handle moves it, in percent. 10 by default.</summary>
    [Parameter] public double KeyboardStep { get; set; } = 10;

    [Parameter] public string? Class { get; set; }

    [Parameter] public string? Style { get; set; }


    /// <summary>The panels, in order.</summary>
    public IReadOnlyList<ResizablePanel> Panels => this.Ordered();

    /// <summary>Resizes every panel at once (percent, in panel order), within their limits.</summary>
    public Task SetSizesAsync(params double[] values)
    {
        var limits = this.Limits();
        return limits.Count == 0
            ? Task.CompletedTask
            : this.CommitAsync(ResizableLayout.Validate(values, limits), raise: true);
    }

    /// <summary>Collapses <paramref name="panel"/>. Needs <see cref="ResizablePanel.Collapsible"/>.</summary>
    public async Task CollapseAsync(ResizablePanel panel)
    {
        var index = this.IndexOf(panel);
        if (index < 0 || !panel.Collapsible || this.EnsureSizes() is not { } current)
            return;

        var limits = this.Limits();
        var next = ResizableLayout.Collapse(current, limits, index);
        if (ResizableLayout.IsCollapsed(next, limits, index))
            await this.CommitAsync(next, raise: true);
        else
            await this.SettleAsync(current);
    }

    /// <summary>Opens a collapsed <paramref name="panel"/> to the size it had before it collapsed.</summary>
    public async Task ExpandAsync(ResizablePanel panel)
    {
        var index = this.IndexOf(panel);
        if (index < 0 || this.EnsureSizes() is not { } current)
            return;

        await this.CommitAsync(ResizableLayout.Expand(current, this.Limits(), index, panel.ExpandedSize), raise: true);
    }


    // ---------------------------------------------------------------------------------------------
    // Children
    // ---------------------------------------------------------------------------------------------

    internal void Register(ResizablePanel panel)
    {
        this.panels.Add((panel, this.sequence++));
        this.sizes = null;
        this.RefreshIfLive();
    }

    internal void Unregister(ResizablePanel panel)
    {
        this.panels.RemoveAll(x => ReferenceEquals(x.Panel, panel));
        this.sizes = null;
        this.RefreshIfLive();
    }

    /// <returns>The handle's place in render order, which says which panels it sits between.</returns>
    internal int Register(ResizableHandle handle)
    {
        this.handles.Add(handle);
        return this.sequence++;
    }

    internal void Unregister(ResizableHandle handle) => this.handles.Remove(handle);

    /// <summary>The panel's size in percent, or null until the group has worked the sizes out (the first render).</summary>
    internal double? SizeOf(ResizablePanel panel)
    {
        var index = this.IndexOf(panel);
        return this.sizes is { } s && index >= 0 && index < s.Length ? s[index] : null;
    }

    /// <summary>The panel a handle resizes - the one rendered just before it.</summary>
    internal ResizablePanel? PanelBefore(ResizableHandle handle)
    {
        // Panels and handles draw from one sequence as they first render, so a handle's number falls
        // between the panels either side of it.
        var before = this.panels
            .Where(x => x.Sequence < handle.Sequence)
            .OrderBy(x => x.Sequence)
            .Select(x => x.Panel)
            .LastOrDefault();

        var ordered = this.Ordered();
        return before is not null && ordered.IndexOf(before) < ordered.Count - 1 ? before : null;
    }

    internal void OnPanelChanged(ResizablePanel panel, bool limitsChanged, bool defaultsChanged)
    {
        if (defaultsChanged && this.Sizes is null && this.sizes is not null && !this.hasSettled)
        {
            this.sizes = null;
            this.RefreshIfLive();
            return;
        }

        if (limitsChanged && this.sizes is { } current)
            _ = this.CommitAsync(ResizableLayout.Validate(current, this.Limits()), raise: false);
    }

    internal Task OnPanelIsCollapsedChanged(ResizablePanel panel, bool collapsed)
        => collapsed ? this.CollapseAsync(panel) : this.ExpandAsync(panel);


    // ---------------------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------------------

    protected override async Task OnParametersSetAsync()
    {
        // Handles take no parameters from here, so they do not re-render with the group by themselves.
        if (this.Direction != this.lastDirection)
        {
            this.lastDirection = this.Direction;
            foreach (var handle in this.handles)
                handle.Refresh();
        }

        if (ReferenceEquals(this.Sizes, this.lastSizesParameter))
            return;

        this.lastSizesParameter = this.Sizes;
        if (this.Sizes is { } given && this.panels.Count > 0 && given.Count == this.panels.Count && this.sizes is not null
            && !given.SequenceEqual(this.sizes))
        {
            await this.CommitAsync(ResizableLayout.Validate(given, this.Limits()), raise: false, echo: false);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var loaded = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Shiny.Blazor.Controls/resizable.js");
            if (this.disposed)
            {
                await loaded.ReleaseLateAsync();
                return;
            }

            this.module = loaded;
            this.selfRef = DotNetObjectReference.Create(this);
            await this.module.InvokeVoidAsync("init", this.groupEl, this.selfRef);
        }

        // The first render lays panels out by their defaults in CSS (it cannot know how many panels
        // there are until they have all registered); from here on they carry exact sizes.
        if (this.sizes is null && this.panels.Count > 0 && this.EnsureSizes() is { } initial)
            await this.SettleAsync(initial);
    }

    [JSInvokable]
    public Task OnLayoutCommittedJs(double[] values)
    {
        var limits = this.Limits();
        return values.Length != limits.Count
            ? Task.CompletedTask
            : this.CommitAsync(ResizableLayout.Validate(values, limits), raise: true);
    }

    public async ValueTask DisposeAsync()
    {
        this.disposed = true;
        if (this.module is not null)
        {
            try
            {
                await this.module.InvokeVoidAsync("dispose", this.groupEl);
                await this.module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
        }
        this.selfRef?.Dispose();
    }


    // ---------------------------------------------------------------------------------------------
    // Internals
    // ---------------------------------------------------------------------------------------------

    List<ResizablePanel> Ordered()
        => this.panels
            .OrderBy(x => x.Panel.Order ?? Int32.MaxValue)
            .ThenBy(x => x.Sequence)
            .Select(x => x.Panel)
            .ToList();

    int IndexOf(ResizablePanel panel) => this.Ordered().IndexOf(panel);

    List<ResizablePanelLimits> Limits() => this.Ordered().Select(x => x.Limits).ToList();

    double[]? EnsureSizes()
    {
        var ordered = this.Ordered();
        if (ordered.Count == 0)
            return null;

        if (this.sizes is { } current && current.Length == ordered.Count)
            return current;

        var limits = ordered.Select(x => x.Limits).ToList();
        var initial = this.Sizes is { } given && given.Count == ordered.Count
            ? ResizableLayout.Validate(given, limits)
            : ResizableLayout.Initial(ordered.Select(x => x.DefaultSize).ToList(), limits);

        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].IsCollapsed && ordered[i].Collapsible && !limits[i].IsCollapsed(initial[i]))
            {
                ordered[i].ExpandedSize = initial[i];
                initial = ResizableLayout.Collapse(initial, limits, i);
            }
        }

        this.sizes = initial;
        return initial;
    }

    async Task CommitAsync(double[] next, bool raise, bool echo = true)
    {
        this.sizes = next;
        this.hasSettled = true;
        await this.SettleAsync(next);

        if (echo)
        {
            this.Sizes = next;
            this.lastSizesParameter = next;
            await this.SizesChanged.InvokeAsync(next);
        }

        if (raise)
            await this.LayoutChanged.InvokeAsync(next);
    }

    /// <summary>Re-renders every panel and handle at the given sizes and tells panels whether they are collapsed.</summary>
    async Task SettleAsync(double[] values)
    {
        var ordered = this.Ordered();
        var limits = ordered.Select(x => x.Limits).ToList();
        for (var i = 0; i < ordered.Count && i < values.Length; i++)
            await ordered[i].SettleAsync(values[i], limits[i].IsCollapsed(values[i]));

        foreach (var handle in this.handles)
            handle.Refresh();
    }

    void RefreshIfLive()
    {
        if (this.module is not null && !this.disposed)
            this.InvokeAsync(this.StateHasChanged);
    }
}
