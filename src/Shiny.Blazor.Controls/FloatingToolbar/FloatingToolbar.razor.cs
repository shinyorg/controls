using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

/// <summary>What was clicked, and on whose behalf.</summary>
/// <param name="Item">The item invoked — always a leaf, never a menu button.</param>
/// <param name="TargetIndex">Which of the bound targets the bar was anchored to, for a shared bar.</param>
public record FloatingToolbarItemEventArgs(ToolbarItem Item, int TargetIndex);


/// <summary>
/// A toolbar that floats over a control the way a tooltip does — icons, labels, badges, dropdown
/// menus and an overflow, laid out across or down, anchored to whichever control triggered it.
/// </summary>
/// <remarks>
/// Placement, the top layer and the reposition-on-scroll watcher all come from <c>tooltip.js</c>:
/// anchoring a bar to a control is the same problem as anchoring a bubble to one, and a second
/// implementation would only drift. What is added here is the toolbar's own half — orientation,
/// dropdowns, overflow, and the grace period that lets the pointer cross onto the bar.
/// <para>
/// One instance can serve many controls. Give <see cref="Target"/> a selector that matches a whole
/// list and the bar re-anchors to whichever element was triggered, reporting the index.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// &lt;FloatingToolbar Target=".card" Trigger="TooltipTrigger.Hover"
///                  Orientation="ToolbarOrientation.Vertical"
///                  Items="@actions" ItemClicked="OnAction" /&gt;
/// </code>
/// </example>
public partial class FloatingToolbar : IAsyncDisposable
{
    sealed class MenuState
    {
        public required List<ToolbarItem> Items { get; init; }
        public required int Depth { get; init; }
        public required ToolbarItem Owner { get; init; }
        public ElementReference Element { get; set; }
        public bool Placed { get; set; }
    }

    readonly string instanceId = "shiny-fbar-" + Guid.NewGuid().ToString("N");
    readonly List<MenuState> menus = new();

    ElementReference barEl;
    ElementReference anchorEl;
    IJSObjectReference? module;
    DotNetObjectReference<FloatingToolbar>? selfRef;

    CancellationTokenSource? showCts;
    CancellationTokenSource? hideCts;
    CancellationTokenSource? dismissCts;

    bool isRendered;
    bool isOpen;

    /// <summary>The class the bar carries once it has been placed — what the CSS transitions to.</summary>
    bool isEntered;

    /// <summary>The side the placer actually chose, which may not be the side asked for.</summary>
    string resolvedSide = "top";
    bool pendingPlace;
    int currentIndex;
    int boundCount;
    int fitCount;
    string? boundSelector;
    TooltipTrigger boundTrigger;

    [Inject] IJSRuntime JS { get; set; } = default!;


    // ---------------------------------------------------------------------------------------------
    // Parameters
    // ---------------------------------------------------------------------------------------------

    /// <summary>The actions on the bar. Items with <c>Children</c> open a dropdown instead of acting.</summary>
    [Parameter] public List<ToolbarItem>? Items { get; set; }

    /// <summary>Wrap the target inline instead of naming it with <see cref="Target"/>.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// CSS selector for the target. A selector matching many elements binds all of them: one bar
    /// serves the whole list and re-anchors to whichever was triggered.
    /// </summary>
    [Parameter] public string? Target { get; set; }

    /// <summary>What opens the bar.</summary>
    [Parameter] public TooltipTrigger Trigger { get; set; } = TooltipTrigger.Click;

    /// <summary>Whether the bar is up. Two-way bindable.</summary>
    [Parameter] public bool IsOpen { get; set; }

    /// <summary>Two-way binding hook for <see cref="IsOpen"/>.</summary>
    [Parameter] public EventCallback<bool> IsOpenChanged { get; set; }

    /// <summary>Across or down. Also decides whether overflow is measured on width or on height.</summary>
    [Parameter] public ToolbarOrientation Orientation { get; set; } = ToolbarOrientation.Horizontal;

    /// <summary>Which side of the target the bar sits on. <c>Auto</c> picks the side with room.</summary>
    [Parameter] public TooltipPlacement Placement { get; set; } = TooltipPlacement.Top;

    /// <summary>Gap between the target and the bar.</summary>
    [Parameter] public double Offset { get; set; } = 8;

    /// <summary>Space kept clear at the viewport edges.</summary>
    [Parameter] public double ScreenMargin { get; set; } = 12;

    /// <summary>Draw each item's text beside its icon.</summary>
    [Parameter] public bool ShowLabels { get; set; }

    /// <summary>Fold items that do not fit into a "⋯" dropdown rather than letting them run off.</summary>
    [Parameter] public bool OverflowEnabled { get; set; } = true;

    /// <summary>
    /// Cap on items drawn on the bar. Zero measures what actually fits. The cap
    /// <b>counts the overflow button</b>.
    /// </summary>
    [Parameter] public int MaxVisibleItems { get; set; }

    /// <summary>Milliseconds a trigger has to persist before the bar opens.</summary>
    [Parameter] public int ShowDelay { get; set; }

    /// <summary>
    /// Milliseconds the bar lingers after the pointer leaves. This is what makes a hover toolbar
    /// usable at all: without it the bar vanishes in the gap between the target and the buttons, and
    /// none of them can ever be reached.
    /// </summary>
    [Parameter] public int HideDelay { get; set; } = 250;

    /// <summary>How long a press is held before <see cref="TooltipTrigger.LongPress"/> opens the bar.</summary>
    [Parameter] public int LongPressDelay { get; set; } = 450;

    /// <summary>Milliseconds before the bar closes itself. Zero leaves it up.</summary>
    [Parameter] public int AutoDismissDelay { get; set; }

    /// <summary>Close the bar once an item is invoked.</summary>
    [Parameter] public bool DismissOnItemClick { get; set; } = true;

    /// <summary>Raised for the item actually invoked — a bar button, or a leaf inside a dropdown.</summary>
    [Parameter] public EventCallback<FloatingToolbarItemEventArgs> ItemClicked { get; set; }

    /// <summary>Raised once the bar is on screen.</summary>
    [Parameter] public EventCallback Opened { get; set; }

    /// <summary>Raised once it has gone.</summary>
    [Parameter] public EventCallback Closed { get; set; }

    /// <summary>Accessible name for the bar.</summary>
    [Parameter] public string AriaLabel { get; set; } = "Actions";

    /// <summary>The bar's surface.</summary>
    [Parameter] public string? BarColor { get; set; }

    /// <summary>Foreground for items that do not override it.</summary>
    [Parameter] public string? ForegroundColor { get; set; }

    /// <summary>Corner radius of the bar and its menus.</summary>
    [Parameter] public string CornerRadius { get; set; } = "10px";

    /// <summary>How the bar enters and leaves.</summary>
    [Parameter] public TooltipAnimation Animation { get; set; } = TooltipAnimation.Scale;

    /// <summary>Length of the enter and exit animations in milliseconds. Zero snaps.</summary>
    [Parameter] public int AnimationLength { get; set; } = 140;

    /// <summary>Extra classes on the bar.</summary>
    [Parameter] public string? CssClass { get; set; }


    /// <summary>Which of the bound targets the bar is anchored to right now.</summary>
    public int CurrentTargetIndex => this.currentIndex;


    // ---------------------------------------------------------------------------------------------
    // Public surface
    // ---------------------------------------------------------------------------------------------

    /// <summary>Open the bar against a bound target.</summary>
    public Task ShowAsync(int targetIndex = 0) => this.SetOpenAsync(true, targetIndex);

    /// <summary>Close the bar.</summary>
    public Task HideAsync() => this.SetOpenAsync(false, this.currentIndex);


    // ---------------------------------------------------------------------------------------------
    // Items and overflow
    // ---------------------------------------------------------------------------------------------

    List<ToolbarItem> AllItems => this.Items ?? [];

    /// <summary>The "⋯" button. Its children are whatever did not fit.</summary>
    ToolbarItem OverflowItem { get; } = new() { Text = "More", Tooltip = "More actions", Children = [] };

    int EffectiveCap
    {
        get
        {
            if (this.MaxVisibleItems > 0)
                return this.MaxVisibleItems;

            // Zero until JS has measured; until then everything is drawn, which is also what the
            // measure pass needs in order to have something to measure.
            return this.fitCount;
        }
    }

    bool HasOverflow => this.OverflowEnabled && this.EffectiveCap > 0 && this.AllItems.Count > this.EffectiveCap;

    IEnumerable<ToolbarItem> VisibleItems
    {
        get
        {
            if (!this.HasOverflow)
                return this.AllItems;

            // The cap counts the overflow button: keeping cap items and then adding "⋯" would put
            // the bar straight back over the width that caused the overflow.
            var keep = Math.Max(0, this.EffectiveCap - 1);
            this.OverflowItem.Children = this.AllItems.Skip(keep).ToList();
            return this.AllItems.Take(keep);
        }
    }


    // ---------------------------------------------------------------------------------------------
    // Styling
    // ---------------------------------------------------------------------------------------------

    string OrientationClass => this.Orientation == ToolbarOrientation.Vertical ? "is-vertical" : "is-horizontal";

    string BarClasses
    {
        get
        {
            var builder = new StringBuilder("shiny-fbar ").Append(this.OrientationClass);

            builder.Append(this.Animation switch
            {
                TooltipAnimation.None => " anim-none",
                TooltipAnimation.Scale => " anim-scale",
                TooltipAnimation.Slide => " anim-slide",
                _ => " anim-fade"
            });

            // The side the placer chose decides which way a slide travels and which edge a scale
            // grows out of, so it has to reach the stylesheet.
            builder.Append(" from-").Append(this.resolvedSide);
            builder.Append(this.isEntered ? " is-entered" : " is-entering");

            if (this.ShowLabels)
                builder.Append(" has-labels");
            if (!String.IsNullOrWhiteSpace(this.CssClass))
                builder.Append(' ').Append(this.CssClass);

            return builder.ToString();
        }
    }

    string BarStyle
    {
        get
        {
            var style = new StringBuilder();
            style.Append("--shiny-fbar-radius:").Append(this.CornerRadius).Append(';');
            style.Append("--shiny-fbar-anim:").Append(Math.Max(0, this.AnimationLength)).Append("ms;");

            if (!String.IsNullOrWhiteSpace(this.BarColor))
                style.Append("--shiny-fbar-bg:").Append(this.BarColor).Append(';');
            if (!String.IsNullOrWhiteSpace(this.ForegroundColor))
                style.Append("--shiny-fbar-fg:").Append(this.ForegroundColor).Append(';');

            return style.ToString();
        }
    }
}
