using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

/// <summary>
/// The desktop ribbon: a strip of tabs over a body of titled command groups.
/// </summary>
/// <remarks>
/// <para>
/// A desktop control, and only nominally a responsive one. It wants a pointer to hover with and enough
/// width for three rows of small commands; on a phone viewport use <see cref="ShinyToolbar"/> or
/// <see cref="ShinyTabBar"/> instead.
/// </para>
/// <para>
/// Groups collapse to a single button when the showing tab is wider than the bar, worst
/// <see cref="RibbonGroup.Priority"/> first. That decision needs real measured widths, so it is made in
/// <c>ribbon.js</c> and handed back here — which is also why it degrades cleanly: with no JS module
/// (prerendering, a locked-down host) every group simply stays open and the body scrolls.
/// </para>
/// </remarks>
public partial class Ribbon : ComponentBase, IAsyncDisposable
{
    readonly List<RibbonTab> tabs = new();
    readonly List<RibbonGroup> groups = new();
    readonly HashSet<string> collapsed = new();

    [Inject] IJSRuntime JS { get; set; } = default!;

    ElementReference rootElement;
    IJSObjectReference? module;
    bool disposed;
    DotNetObjectReference<Ribbon>? selfReference;

    string? activeKey;
    bool peeking;
    bool placeMenus;

    string? menuOwner;
    List<RibbonMenuEntry>? menuEntries;
    readonly List<RibbonMenuEntry> menuPath = new();
    RibbonGroup? popupGroup;


    // ---------------------------------------------------------------------------------------------
    // Parameters
    // ---------------------------------------------------------------------------------------------

    /// <summary>The <see cref="RibbonTab"/> children.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// Small icon-only commands pinned to the trailing end of the tab strip — save, undo, redo. They
    /// stay reachable whichever tab is showing, and while the ribbon is collapsed.
    /// </summary>
    [Parameter] public RenderFragment? QuickAccess { get; set; }

    /// <summary>
    /// Text for the accented button at the head of the strip — "File" in most apps. Null (the default)
    /// leaves it out entirely.
    /// </summary>
    [Parameter] public string? ApplicationButtonText { get; set; }

    /// <summary>Raised when the application button is pressed.</summary>
    [Parameter] public EventCallback ApplicationButtonClicked { get; set; }

    /// <summary>
    /// The showing tab's <see cref="RibbonTab.Key"/>. Two-way bindable; leave it unset and the ribbon
    /// opens on the first selectable tab.
    /// </summary>
    [Parameter] public string? SelectedKey { get; set; }

    [Parameter] public EventCallback<string?> SelectedKeyChanged { get; set; }

    /// <summary>Raised after the ribbon moves to a different tab, whatever moved it.</summary>
    [Parameter] public EventCallback<RibbonTabChangedEventArgs> TabChanged { get; set; }

    /// <summary>Expanded, collapsed to the tab strip, or the one-row simplified layout. Two-way bindable.</summary>
    [Parameter] public RibbonDisplayMode DisplayMode { get; set; } = RibbonDisplayMode.Expanded;

    [Parameter] public EventCallback<RibbonDisplayMode> DisplayModeChanged { get; set; }

    /// <summary>
    /// Offers the chevron that collapses the ribbon, and makes a second click on the showing tab do the
    /// same. Turn it off for chrome that has to stay put; <see cref="DisplayMode"/> still works.
    /// </summary>
    [Parameter] public bool AllowCollapse { get; set; } = true;

    /// <summary>
    /// Lets groups collapse to a single button when the tab is wider than the bar. Turn it off and the
    /// body scrolls horizontally instead, which is the better answer when every group is small.
    /// </summary>
    [Parameter] public bool AllowGroupCollapse { get; set; } = true;

    /// <summary>Draws each group's caption under it. Always false in the simplified layout.</summary>
    [Parameter] public bool ShowGroupTitles { get; set; } = true;

    /// <summary>
    /// Draws the tab strip. Set false for a single-tab ribbon that is really a toolbar — the tab's
    /// groups still show, without a strip above them saying so.
    /// </summary>
    [Parameter] public bool ShowTabStrip { get; set; } = true;

    /// <summary>
    /// How many <see cref="RibbonItemSize.Small"/> items stack in a column before a new one starts.
    /// Three is the ribbon convention and what the body's height is sized from.
    /// </summary>
    [Parameter] public int SmallItemRows { get; set; } = 3;

    /// <summary>The selected tab's underline and the application button's fill. Any CSS colour; falls back to the theme.</summary>
    [Parameter] public string? AccentColor { get; set; }

    /// <summary>Fill behind the tab strip. Any CSS colour; falls back to the theme.</summary>
    [Parameter] public string? HeaderBackgroundColor { get; set; }

    /// <summary>
    /// Ink for the tab strip, when <see cref="HeaderBackgroundColor"/> is something the theme's own
    /// text colour would not be legible on.
    /// </summary>
    /// <remarks>
    /// Needed the moment the header is painted a saturated colour rather than a surface tint: the tab
    /// labels inherit the ribbon's <c>on-surface</c> ink, which would vanish into a Word-blue or
    /// Excel-green band. Null leaves them on the theme.
    /// </remarks>
    [Parameter] public string? HeaderForegroundColor { get; set; }

    /// <summary>Fill behind the groups. Any CSS colour; falls back to the theme.</summary>
    [Parameter] public string? BodyBackgroundColor { get; set; }

    /// <summary>Raised when a dropdown line is picked, after that line's own callback.</summary>
    [Parameter] public EventCallback<RibbonMenuEntry> MenuEntrySelected { get; set; }

    [Parameter] public string? CssClass { get; set; }

    [Parameter] public string? Style { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }


    // ---------------------------------------------------------------------------------------------
    // Registration
    // ---------------------------------------------------------------------------------------------

    /*
        Registration and the overflow report are split into a decision and a render, because both
        arrive from somewhere that cannot assume a renderer: a child's OnInitialized, and JS. Keeping
        the decision on its own is also what lets it be tested without standing up a renderer to hold
        a component that has no markup worth asserting on.
    */

    internal void Register(RibbonTab tab)
    {
        if (this.AddTab(tab))
            this.StateHasChanged();
    }


    internal bool AddTab(RibbonTab tab)
    {
        if (this.tabs.Contains(tab))
            return false;

        this.tabs.Add(tab);
        this.EnsureSelection();
        return true;
    }


    internal void Unregister(RibbonTab tab)
    {
        if (this.RemoveTab(tab))
            this.StateHasChanged();
    }


    internal bool RemoveTab(RibbonTab tab)
    {
        if (!this.tabs.Remove(tab))
            return false;

        this.EnsureSelection();
        return true;
    }


    /// <summary>
    /// A tab's own parameters changed in a way that alters the strip. Kept separate from
    /// <see cref="Register(RibbonTab)"/> so it can be called from a tab's <c>OnParametersSet</c> only
    /// when something actually changed — notifying unconditionally spins the renderer forever.
    /// </summary>
    internal void NotifyTabChanged()
    {
        this.EnsureSelection();
        this.StateHasChanged();
    }


    internal void Register(RibbonGroup group)
    {
        if (!this.groups.Contains(group))
            this.groups.Add(group);
    }


    internal void Unregister(RibbonGroup group)
    {
        this.groups.Remove(group);
        this.collapsed.Remove(group.Id);
    }


    /// <summary>Whether the overflow pass has folded this group down to a button.</summary>
    internal bool IsGroupCollapsed(RibbonGroup group) => this.collapsed.Contains(group.Id);


    /// <summary>Whether this tab's groups should be in the DOM.</summary>
    internal bool IsActive(RibbonTab tab) => tab.EffectiveKey == this.activeKey;


    // ---------------------------------------------------------------------------------------------
    // Selection
    // ---------------------------------------------------------------------------------------------

    /// <summary>The tabs the strip actually draws.</summary>
    internal IReadOnlyList<RibbonTab> VisibleTabs => this.tabs.Where(x => x.Visible).ToList();


    protected override void OnParametersSet() => this.SyncSelection(this.SelectedKey);


    /// <summary>Takes a requested key, then makes sure the ribbon is on a tab that can actually show.</summary>
    internal void SyncSelection(string? key)
    {
        if (key is not null && key != this.activeKey)
            this.activeKey = key;

        this.EnsureSelection();
    }


    /// <summary>
    /// Makes sure the ribbon is on a tab that exists and can be selected.
    /// </summary>
    /// <remarks>
    /// This is what makes contextual tabs work without the host managing them: a tab bound to "is a
    /// table selected" simply disappears, and the ribbon lands on a real tab instead of showing an
    /// empty body.
    /// </remarks>
    internal void EnsureSelection()
    {
        var selectable = this.tabs.Where(x => x.IsSelectable).ToList();
        if (selectable.Count == 0)
        {
            this.activeKey = null;
            return;
        }

        if (this.activeKey is not null && selectable.Any(x => x.EffectiveKey == this.activeKey))
            return;

        this.Move(selectable[0].EffectiveKey, RibbonTabChangeReason.Fallback);
    }


    async Task SelectAsync(RibbonTab tab)
    {
        if (!tab.IsSelectable)
            return;

        if (tab.EffectiveKey == this.activeKey)
        {
            // A second click on the open tab puts the ribbon away, which is the gesture every ribbon
            // has. Handled here rather than with a dblclick, which would fight the click that selects.
            if (this.DisplayMode == RibbonDisplayMode.Collapsed)
                this.peeking = !this.peeking;
            else if (this.AllowCollapse)
                await this.SetDisplayModeAsync(RibbonDisplayMode.Collapsed).ConfigureAwait(false);

            return;
        }

        this.Move(tab.EffectiveKey, RibbonTabChangeReason.User);

        if (this.DisplayMode == RibbonDisplayMode.Collapsed)
            this.peeking = true;

        await this.SelectedKeyChanged.InvokeAsync(this.activeKey).ConfigureAwait(false);
        await this.TabChanged
            .InvokeAsync(new RibbonTabChangedEventArgs(this.activeKey, RibbonTabChangeReason.User))
            .ConfigureAwait(false);
    }


    void Move(string? key, RibbonTabChangeReason reason)
    {
        this.activeKey = key;
        this.collapsed.Clear();   // widths are per tab; the next measure pass fills this in again
        this.placeMenus = false;

        if (reason != RibbonTabChangeReason.User)
        {
            // Fire-and-forget would race the render; these two are safe to leave to the next cycle.
            _ = this.SelectedKeyChanged.InvokeAsync(key);
            _ = this.TabChanged.InvokeAsync(new RibbonTabChangedEventArgs(key, reason));
        }
    }


    // ---------------------------------------------------------------------------------------------
    // Display mode
    // ---------------------------------------------------------------------------------------------

    /// <summary>The mode to come back to. Collapsed is a hidden body, not a third layout.</summary>
    /// <remarks>
    /// Without this, collapsing a <see cref="RibbonDisplayMode.Simplified"/> bar and re-opening it
    /// returned <see cref="RibbonDisplayMode.Expanded"/> - the dense single row could be put away but
    /// never got back, so on a narrow window the chevron was a one-way trip to a bar three times the
    /// height of the one you collapsed.
    /// </remarks>
    RibbonDisplayMode restoreMode = RibbonDisplayMode.Expanded;

    /// <summary>Collapses an expanded ribbon and restores a collapsed one to the mode it had.</summary>
    public Task ToggleCollapsedAsync()
    {
        if (!this.AllowCollapse)
            return Task.CompletedTask;

        if (this.DisplayMode == RibbonDisplayMode.Collapsed)
            return this.SetDisplayModeAsync(this.restoreMode);

        this.restoreMode = this.DisplayMode;
        return this.SetDisplayModeAsync(RibbonDisplayMode.Collapsed);
    }


    async Task SetDisplayModeAsync(RibbonDisplayMode mode)
    {
        // A mode set any other way - a binding, a host's width rule - is also the one to come back to.
        if (mode != RibbonDisplayMode.Collapsed)
            this.restoreMode = mode;

        this.DisplayMode = mode;
        this.peeking = false;
        await this.DisplayModeChanged.InvokeAsync(mode).ConfigureAwait(false);
    }


    /// <summary>Whether the group body is in the DOM at all.</summary>
    bool BodyVisible => this.DisplayMode != RibbonDisplayMode.Collapsed || this.peeking;


    // ---------------------------------------------------------------------------------------------
    // Menus
    // ---------------------------------------------------------------------------------------------

    /// <summary>Whether a dropdown or a collapsed group's popup is open.</summary>
    public bool IsMenuOpen => this.menuEntries is not null || this.popupGroup is not null;


    /// <summary>Closes any open dropdown. Safe to call when nothing is open.</summary>
    public void CloseMenu()
    {
        if (!this.IsMenuOpen)
            return;

        this.SetMenu(null, null, null);
        this.StateHasChanged();
    }


    internal void OpenMenu(string ownerId, List<RibbonMenuEntry> entries)
    {
        this.SetMenu(ownerId, entries, null);
        this.StateHasChanged();
    }


    /// <summary>
    /// The one place the open panel is recorded, so a dropdown and a popped-open group cannot both be
    /// showing — they are anchored to different buttons and would need two backdrops to dismiss.
    /// </summary>
    internal void SetMenu(string? ownerId, List<RibbonMenuEntry>? entries, RibbonGroup? group)
    {
        this.menuOwner = ownerId;
        this.menuEntries = entries;
        this.popupGroup = group;
        this.menuPath.Clear();
        this.placeMenus = ownerId is not null;
    }


    /// <summary>
    /// Pops a collapsed group open.
    /// </summary>
    /// <remarks>
    /// The state lives here so that only one panel on the bar is ever open, but the markup is the
    /// group's own — re-rendering someone else's <c>ChildContent</c> from here would build a second
    /// set of item components with none of the group's own layout around them.
    /// </remarks>
    internal void OpenGroupPopup(RibbonGroup group)
    {
        this.SetMenu(group.Id, null, group);
        this.StateHasChanged();
    }


    /// <summary>Whether this collapsed group is the one currently popped open.</summary>
    internal bool IsGroupPopupOpen(RibbonGroup group) => ReferenceEquals(this.popupGroup, group);


    void ToggleSubmenu(RibbonMenuEntry entry, int depth)
    {
        // Everything below this level closes: a chain that kept its deeper panels open would leave
        // them pointing at a row that is no longer expanded.
        while (this.menuPath.Count > depth)
            this.menuPath.RemoveAt(this.menuPath.Count - 1);

        this.menuPath.Add(entry);
        this.placeMenus = true;
    }


    async Task PickAsync(RibbonMenuEntry entry)
    {
        if (entry.IsDisabled || entry.IsSeparator)
            return;

        this.CloseMenu();

        if (entry.OnClick.HasDelegate)
            await entry.OnClick.InvokeAsync().ConfigureAwait(false);

        await this.MenuEntrySelected.InvokeAsync(entry).ConfigureAwait(false);
        this.NotifyInvoked();
    }


    /// <summary>
    /// An item on the bar was pressed. A command run off a peeking ribbon closes it again, which is the
    /// whole point of having collapsed it.
    /// </summary>
    internal void NotifyInvoked()
    {
        if (!this.peeking)
            return;

        this.peeking = false;
        this.StateHasChanged();
    }


    // ---------------------------------------------------------------------------------------------
    // Markup helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>The showing tab, when it is one of a contextual set — the band above the strip captions it.</summary>
    RibbonTab? ContextualTab
        => this.tabs.FirstOrDefault(x => this.IsActive(x) && x.IsContextual);

    string RootCss
        => string.Join(
            ' ',
            new[]
            {
                "shiny-ribbon",
                this.DisplayMode == RibbonDisplayMode.Simplified ? "is-simplified" : null,
                this.BodyVisible ? null : "is-collapsed",
                this.CssClass
            }.Where(x => !string.IsNullOrWhiteSpace(x))
        );

    /// <summary>
    /// The row count reaches the CSS as a custom property rather than a class, because the group's grid
    /// needs it in two places — the track template and the row span of a large item — and they have to
    /// agree or a large item overhangs its column.
    /// </summary>
    string RootStyle
    {
        get
        {
            // The simplified layout is one row by definition, whatever SmallItemRows says: it is the
            // shape you pick when the bar has to be short, and three rows of small buttons is not that.
            var rows = this.DisplayMode == RibbonDisplayMode.Simplified ? 1 : Math.Max(1, this.SmallItemRows);
            var css = $"--shiny-ribbon-rows:{rows};";

            if (!string.IsNullOrWhiteSpace(this.AccentColor))
                css += $"--shiny-ribbon-accent:{this.AccentColor};";

            if (!string.IsNullOrWhiteSpace(this.HeaderBackgroundColor))
                css += $"--shiny-ribbon-header-bg:{this.HeaderBackgroundColor};";

            if (!string.IsNullOrWhiteSpace(this.HeaderForegroundColor))
                css += $"--shiny-ribbon-header-ink:{this.HeaderForegroundColor};";

            return css + this.Style;
        }
    }

    string? BodyStyle
        => string.IsNullOrWhiteSpace(this.BodyBackgroundColor) ? null : $"background:{this.BodyBackgroundColor};";

    string? ContextStyle
        => this.ContextualTab?.ContextColor is { Length: > 0 } color
            ? $"--shiny-ribbon-context:{color};"
            : null;

    /// <summary>
    /// A contextual tab underlines in its own colour, so that it reads as a different kind of thing
    /// rather than as the selected one of the same kind.
    /// </summary>
    string? TabStyle(RibbonTab tab, bool active)
        => active && tab.IsContextual && !string.IsNullOrWhiteSpace(tab.ContextColor)
            ? $"--shiny-ribbon-accent:{tab.ContextColor};"
            : null;


    async Task OnApplicationButtonAsync()
    {
        this.CloseMenu();
        await this.ApplicationButtonClicked.InvokeAsync().ConfigureAwait(false);
    }


    async Task OnMenuEntryAsync(RibbonMenuEntry entry, int depth)
    {
        if (entry.HasChildren)
        {
            this.ToggleSubmenu(entry, depth);
            return;
        }

        await this.PickAsync(entry).ConfigureAwait(false);
    }


    // ---------------------------------------------------------------------------------------------
    // JS
    // ---------------------------------------------------------------------------------------------

    protected override async Task OnAfterRenderAsync(bool first)
    {
        if (first)
        {
            this.selfReference = DotNetObjectReference.Create(this);
            var loaded = await this.JS
                .InvokeAsync<IJSObjectReference>("import", "./_content/Shiny.Blazor.Controls/ribbon.js")
                .ConfigureAwait(false);
            if (this.disposed) { await loaded.ReleaseLateAsync().ConfigureAwait(false); return; }
            this.module = loaded;

            await this.module
                .InvokeVoidAsync("init", this.rootElement, this.selfReference)
                .ConfigureAwait(false);
        }

        if (this.module is null)
            return;

        if (this.placeMenus)
        {
            this.placeMenus = false;
            await this.module.InvokeVoidAsync("placeMenus", this.rootElement).ConfigureAwait(false);
        }

        // The observer only fires on a size change of the bar, so a render that changed which items are
        // in a group has to ask for the widths to be taken again.
        await this.module.InvokeVoidAsync("remeasure", this.rootElement).ConfigureAwait(false);
    }


    /// <summary>
    /// Called from <c>ribbon.js</c> when Escape is pressed while a panel is open.
    /// </summary>
    /// <remarks>
    /// The panels are <c>popover=manual</c> so that the browser's light dismiss does not fight the
    /// ribbon's own backdrop — which also opts them out of the browser's Escape handling, so it comes
    /// back through here.
    /// </remarks>
    [JSInvokable]
    public void OnDismiss() => this.CloseMenu();


    /// <summary>
    /// Called from <c>ribbon.js</c> with the ids of the groups that do not fit.
    /// </summary>
    [JSInvokable]
    public void OnOverflow(string[] collapsedIds)
    {
        if (this.ApplyOverflow(collapsedIds))
            this.StateHasChanged();
    }


    internal bool ApplyOverflow(IReadOnlyCollection<string> collapsedIds)
    {
        if (this.collapsed.SetEquals(collapsedIds))
            return false;

        this.collapsed.Clear();
        foreach (var id in collapsedIds)
            this.collapsed.Add(id);

        return true;
    }


    public async ValueTask DisposeAsync()
    {
        this.disposed = true;
        if (this.module is not null)
        {
            try
            {
                await this.module.InvokeVoidAsync("dispose", this.rootElement).ConfigureAwait(false);
                await this.module.DisposeAsync().ConfigureAwait(false);
            }
            catch (JSDisconnectedException)
            {
                // The circuit went away first. Nothing to clean up on a browser that is not there.
            }
            catch (ObjectDisposedException)
            {
            }
        }

        this.selfReference?.Dispose();
    }
}


/// <summary>Which tab the ribbon moved to, and what moved it.</summary>
public class RibbonTabChangedEventArgs(string? key, RibbonTabChangeReason reason) : EventArgs
{
    /// <summary>The <see cref="RibbonTab.Key"/> now showing, or null when no tab is selectable.</summary>
    public string? Key { get; } = key;

    public RibbonTabChangeReason Reason { get; } = reason;
}
