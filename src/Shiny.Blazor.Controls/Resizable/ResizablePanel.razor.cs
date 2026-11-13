using System.Globalization;
using Microsoft.AspNetCore.Components;
using Shiny.Controls.Resizable;

namespace Shiny.Blazor.Controls;

/// <summary>
/// One resizable region of a <see cref="ResizablePanelGroup"/>. Sizes are percentages of the group,
/// after the handles have taken their share.
/// </summary>
public partial class ResizablePanel : IDisposable
{
    static int nextId;
    ResizablePanelLimits? lastLimits;
    double? lastDefault;
    bool lastIsCollapsed;
    bool settledCollapsed;

    [CascadingParameter] ResizablePanelGroup? Group { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// The starting size in percent. Panels without one share whatever the others leave. Ignored once
    /// the group has sizes of its own (dragged, or set through <see cref="ResizablePanelGroup.Sizes"/>).
    /// </summary>
    [Parameter] public double? DefaultSize { get; set; }

    /// <summary>The smallest the panel can be dragged, in percent. 0 by default.</summary>
    [Parameter] public double MinSize { get; set; }

    /// <summary>The largest the panel can be dragged, in percent. 100 by default.</summary>
    [Parameter] public double MaxSize { get; set; } = 100;

    /// <summary>
    /// Lets the panel snap shut to <see cref="CollapsedSize"/> when it is dragged more than halfway past
    /// its <see cref="MinSize"/>, and snap open again the same way. Enter on a focused handle toggles it.
    /// </summary>
    [Parameter] public bool Collapsible { get; set; }

    /// <summary>The size of a collapsed panel, in percent - 0 hides it, a few percent leaves a rail.</summary>
    [Parameter] public double CollapsedSize { get; set; }

    /// <summary>
    /// Whether the panel is at its collapsed size. Bindable (<c>@bind-IsCollapsed</c>): follows drags, and
    /// setting it collapses or expands the panel. Only means anything with <see cref="Collapsible"/> on.
    /// </summary>
    [Parameter] public bool IsCollapsed { get; set; }

    [Parameter] public EventCallback<bool> IsCollapsedChanged { get; set; }

    /// <summary>The panel collapsed - dragged shut, or through <see cref="IsCollapsed"/> / <see cref="CollapseAsync"/>.</summary>
    [Parameter] public EventCallback Collapsed { get; set; }

    /// <summary>The panel opened from collapsed.</summary>
    [Parameter] public EventCallback Expanded { get; set; }

    /// <summary>
    /// The panel's place among its siblings, for a panel that is rendered conditionally - one that appears
    /// after the others would otherwise be put last. Panels without one keep their render order.
    /// </summary>
    [Parameter] public int? Order { get; set; }

    /// <summary>The element id; a handle points its <c>aria-controls</c> at it. Generated when unset.</summary>
    [Parameter] public string Id { get; set; } = $"shiny-resizable-panel-{Interlocked.Increment(ref nextId)}";

    [Parameter] public string? Class { get; set; }

    [Parameter] public string? Style { get; set; }

    /// <summary>The panel's current size in percent of the group. Updated when a resize settles.</summary>
    public double Size { get; private set; }

    /// <summary>Collapses the panel. Needs <see cref="Collapsible"/>.</summary>
    public Task CollapseAsync() => this.Group?.CollapseAsync(this) ?? Task.CompletedTask;

    /// <summary>Opens a collapsed panel to the size it had before.</summary>
    public Task ExpandAsync() => this.Group?.ExpandAsync(this) ?? Task.CompletedTask;

    internal ResizablePanelLimits Limits => new(
        Math.Clamp(this.MinSize, 0, 100),
        Math.Clamp(this.MaxSize, 0, 100),
        this.Collapsible,
        Math.Clamp(this.CollapsedSize, 0, 100));

    /// <summary>The size to come back to when the panel is expanded: what it was before it collapsed.</summary>
    internal double? ExpandedSize { get; set; }

    string PanelStyle
    {
        get
        {
            // Before the group has counted its panels, CSS shares the space: a panel with a default
            // takes that percentage, the rest split what is left.
            var size = this.Group?.SizeOf(this);
            var flex = size is { } s
                ? $"flex:{Invariant(s)} 1 0px;"
                : this.DefaultSize is { } d
                    ? $"flex:0 1 {Invariant(d)}%;"
                    : "flex:1 1 0px;";
            return flex + this.Style;
        }
    }

    static string Invariant(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    protected override void OnInitialized()
    {
        if (this.Group is null)
            throw new InvalidOperationException($"{nameof(ResizablePanel)} must be placed inside a {nameof(ResizablePanelGroup)}.");

        this.lastLimits = this.Limits;
        this.lastDefault = this.DefaultSize;
        this.lastIsCollapsed = this.IsCollapsed;
        // Declared collapsed is where it starts, not a change to announce.
        this.settledCollapsed = this.IsCollapsed;
        this.Group.Register(this);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (this.Group is null || this.lastLimits is null)
            return;

        var limitsChanged = this.lastLimits != this.Limits;
        var defaultsChanged = this.lastDefault != this.DefaultSize;
        this.lastLimits = this.Limits;
        this.lastDefault = this.DefaultSize;
        if (limitsChanged || defaultsChanged)
            this.Group.OnPanelChanged(this, limitsChanged, defaultsChanged);

        if (this.IsCollapsed != this.lastIsCollapsed)
        {
            this.lastIsCollapsed = this.IsCollapsed;
            await this.Group.OnPanelIsCollapsedChanged(this, this.IsCollapsed);
        }
    }

    /// <summary>Takes a settled size from the group, re-renders, and raises Collapsed / Expanded on a change.</summary>
    internal async Task SettleAsync(double size, bool collapsed)
    {
        this.Size = size;
        if (!collapsed)
            this.ExpandedSize = size;

        this.StateHasChanged();

        // Put IsCollapsed back to the truth even when nothing changed - a collapse that could not make
        // room leaves it set by the parent but not done.
        if (this.IsCollapsed != collapsed)
        {
            this.IsCollapsed = collapsed;
            this.lastIsCollapsed = collapsed;
            await this.IsCollapsedChanged.InvokeAsync(collapsed);
        }

        if (this.settledCollapsed == collapsed)
            return;

        this.settledCollapsed = collapsed;
        await (collapsed ? this.Collapsed : this.Expanded).InvokeAsync();
    }

    public void Dispose() => this.Group?.Unregister(this);
}
