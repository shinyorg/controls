using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls.Docking;

public partial class DockHost : ComponentBase, IDockHost, IAsyncDisposable
{
    DockRoot? layout;
    string? pristineJson;
    bool loadAttempted;
    bool lastLocked;
    DockableContentRegistry? registry;
    ElementReference hostRef;
    IJSObjectReference? module;
    bool disposed;
    DotNetObjectReference<DockHost>? dotnetRef;
    CancellationTokenSource? saveCts;
    readonly Dictionary<string, RenderFragment> fragments = new();
    readonly DockEventsImpl events = new();
    readonly DockCommandScopeImpl commandScope = new();
    readonly Dictionary<DockWindowState, int> floatOrder = new(ReferenceEqualityComparer.Instance);
    int floatZ;

    [Inject] IServiceProvider Services { get; set; } = null!;
    [Inject] IJSRuntime JS { get; set; } = null!;
    [Inject] ILogger<IDockHost> Logger { get; set; } = null!;

    [Parameter] public DockRoot? InitialLayout { get; set; }
    [Parameter] public bool IsLocked { get; set; }
    [Parameter] public IDockLayoutStore? LayoutStore { get; set; }

    [Parameter] public string? BackgroundColor { get; set; }

    string HostStyle => string.IsNullOrEmpty(BackgroundColor)
        ? string.Empty
        : $"--shiny-dock-host-bg: {BackgroundColor};";

    public IDockEvents Events => events;
    public IDockCommandScope CommandScope => commandScope;

    protected override void OnInitialized()
        => registry = Services.GetService<DockableContentRegistry>();

    protected override async Task OnParametersSetAsync()
    {
        if (layout is null && !loadAttempted)
        {
            loadAttempted = true;
            DockRoot? stored = null;
            if (LayoutStore is not null)
            {
                try { stored = await LayoutStore.LoadAsync(); }
                catch { /* fall back to InitialLayout on any store failure */ }
            }
            var root = stored ?? InitialLayout;
            if (root is not null)
                await LoadCoreAsync(root, captureAsPristine: stored is null, CancellationToken.None);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            if (firstRender)
            {
                lastLocked = IsLocked;
                var loaded = await JS.InvokeAsync<IJSObjectReference>(
                    "import", "./_content/Shiny.Blazor.Controls/docking.js");
                if (disposed) { await loaded.ReleaseLateAsync(); return; }
                module = loaded;
                dotnetRef = DotNetObjectReference.Create(this);
                // primitives only over interop — anonymous types break trimmed/AOT publish
                await module.InvokeVoidAsync("init", hostRef, dotnetRef, IsLocked);
            }
            else if (module is not null)
            {
                if (lastLocked != IsLocked)
                {
                    lastLocked = IsLocked;
                    await module.InvokeVoidAsync("setLocked", hostRef, IsLocked);
                }
            }
        }
        catch (JSDisconnectedException) { }
        catch (JSException ex)
        {
            // a stale build/server can 404 the script asset — render still works,
            // only drag/resize interactions are unavailable
            module = null;
            Logger.LogError(ex,
                "Failed to load the docking interaction script (_content/Shiny.Blazor.Controls/docking.js). " +
                "Splitter/tab dragging is disabled. Rebuild the app and hard-refresh the browser — " +
                "this usually means the running server or browser cache predates the script asset.");
        }
    }

    // ----------------------------------------------------------------- IDockHost
    public Task LoadAsync(DockRoot root, CancellationToken ct = default)
    {
        loadAttempted = true;
        return LoadCoreAsync(root, captureAsPristine: true, ct);
    }

    async Task LoadCoreAsync(DockRoot root, bool captureAsPristine, CancellationToken ct)
    {
        root = Migrate(root);
        var json = DockSerialization.Serialize(root);
        if (captureAsPristine || pristineJson is null)
            pristineJson = InitialLayout is not null ? DockSerialization.Serialize(Migrate(InitialLayout)) : json;
        layout = DockSerialization.Deserialize(json)!;
        ConvertLegacyCollapsedRails();
        fragments.Clear();
        floatOrder.Clear();
        await ResolveFragmentsAsync(ct);
        events.RaiseLayoutChanged(new LayoutChangedEventArgs { Snapshot = Snapshot(), Reason = "load" });
        StateHasChanged();
    }

    // older layouts collapsed whole rails via CollapsedRails — convert to per-panel entries
    void ConvertLegacyCollapsedRails()
    {
        var win = layout!.MainWindow;
        foreach (var area in win.CollapsedRails.ToList())
        {
            var node = area switch
            {
                DockArea.Top => win.TopRail,
                DockArea.Right => win.RightRail,
                DockArea.Bottom => win.BottomRail,
                _ => win.LeftRail
            };
            foreach (var tab in GroupsIn(node).SelectMany(g => g.Tabs))
                win.CollapsedTabs.Add(new DockCollapsedPanel { Area = area, Tab = tab });
            switch (area)
            {
                case DockArea.Top: win.TopRail = null; break;
                case DockArea.Right: win.RightRail = null; break;
                case DockArea.Bottom: win.BottomRail = null; break;
                default: win.LeftRail = null; break;
            }
        }
        win.CollapsedRails.Clear();
    }

    DockRoot Migrate(DockRoot root)
    {
        var migrators = Services.GetService<IEnumerable<IDockLayoutMigrator>>()?.ToList();
        if (migrators is null || migrators.Count == 0) return root;
        while (root.SchemaVersion < DockRoot.CurrentSchemaVersion)
        {
            var step = migrators.FirstOrDefault(m => m.FromVersion == root.SchemaVersion);
            if (step is null) break;
            root = step.Migrate(root);
            root.SchemaVersion = step.ToVersion;
        }
        return root;
    }

    public DockRoot Snapshot()
    {
        var current = layout ?? InitialLayout;
        if (current is null) return new DockRoot();
        return DockSerialization.Deserialize(DockSerialization.Serialize(current))!;
    }

    public async Task ShowPanelAsync(string panelTypeId, DockArea preferredArea = DockArea.Left, CancellationToken ct = default)
    {
        layout ??= new DockRoot();

        var existing = AllGroups()
            .SelectMany(g => g.Tabs)
            .FirstOrDefault(t => t.PanelTypeId == panelTypeId);
        if (existing is not null)
        {
            await ActivatePanelAsync(existing.PanelInstanceId, ct);
            return;
        }

        var collapsedExisting = layout.MainWindow.CollapsedTabs
            .FirstOrDefault(c => c.Tab.PanelTypeId == panelTypeId);
        if (collapsedExisting is not null)
        {
            await RestoreCollapsedAsync(collapsedExisting);
            return;
        }

        var tab = new DockTab { PanelTypeId = panelTypeId };
        await ResolveFragmentAsync(tab, ct);

        DockIntoRail(preferredArea, tab);
        layout.MainWindow.ActivePanelId = tab.PanelInstanceId;
        OnLayoutMutated("show-panel");
        events.RaisePanelActivated(new PanelActivatedEventArgs
        {
            PanelInstanceId = tab.PanelInstanceId,
            PanelTypeId = tab.PanelTypeId
        });
        StateHasChanged();
    }

    void DockIntoRail(DockArea area, DockTab tab) => DockLayoutOps.DockIntoRail(layout!, area, tab);

    public Task HidePanelAsync(string panelInstanceId, CancellationToken ct = default)
    {
        if (layout is null) return Task.CompletedTask;

        // a panel registered as not closable stays put however it is asked to go - the tab hides
        // its own close button too, but that is the affordance rather than the rule
        if (FindTab(panelInstanceId) is { } found && !CanCloseTab(found))
            return Task.CompletedTask;

        var collapsedEntry = layout.MainWindow.CollapsedTabs
            .FirstOrDefault(c => c.Tab.PanelInstanceId == panelInstanceId);
        if (collapsedEntry is not null)
        {
            layout.MainWindow.CollapsedTabs.Remove(collapsedEntry);
            fragments.Remove(panelInstanceId);
            OnLayoutMutated("hide-panel");
            StateHasChanged();
            return Task.CompletedTask;
        }

        foreach (var group in AllGroups())
        {
            var tab = group.Tabs.FirstOrDefault(t => t.PanelInstanceId == panelInstanceId);
            if (tab is null) continue;

            group.Tabs.Remove(tab);
            group.ActiveTabIndex = Math.Clamp(group.ActiveTabIndex, 0, Math.Max(0, group.Tabs.Count - 1));
            fragments.Remove(panelInstanceId);
            SimplifyAll();
            if (layout.MainWindow.ActivePanelId == panelInstanceId)
                layout.MainWindow.ActivePanelId = null;

            OnLayoutMutated("hide-panel");
            StateHasChanged();
            return Task.CompletedTask;
        }
        return Task.CompletedTask;
    }

    public Task ActivatePanelAsync(string panelInstanceId, CancellationToken ct = default)
    {
        foreach (var group in AllGroups())
        {
            var idx = group.Tabs.FindIndex(t => t.PanelInstanceId == panelInstanceId);
            if (idx < 0) continue;
            ActivateTab(group, idx);
            return Task.CompletedTask;
        }
        return Task.CompletedTask;
    }

    public async Task ResetLayoutAsync(CancellationToken ct = default)
    {
        if (pristineJson is null) return;
        layout = DockSerialization.Deserialize(pristineJson)!;
        fragments.Clear();
        floatOrder.Clear();
        await ResolveFragmentsAsync(ct);
        OnLayoutMutated("reset");
        StateHasChanged();
    }

    /// <summary>Collapse or restore every panel on a rail at once. Individual panels
    /// collapse via their tab-strip button; this is the bulk/programmatic form.</summary>
    public async Task SetRailCollapsedAsync(DockArea area, bool collapsed, CancellationToken ct = default)
    {
        if (layout is null) return;
        var win = layout.MainWindow;

        if (collapsed)
        {
            var node = area switch
            {
                DockArea.Top => win.TopRail,
                DockArea.Right => win.RightRail,
                DockArea.Bottom => win.BottomRail,
                _ => win.LeftRail
            };
            var tabs = GroupsIn(node).SelectMany(g => g.Tabs).ToList();
            if (tabs.Count == 0) return;
            foreach (var tab in tabs)
                win.CollapsedTabs.Add(new DockCollapsedPanel { Area = area, Tab = tab });
            switch (area)
            {
                case DockArea.Top: win.TopRail = null; break;
                case DockArea.Right: win.RightRail = null; break;
                case DockArea.Bottom: win.BottomRail = null; break;
                default: win.LeftRail = null; break;
            }
            OnLayoutMutated("rail-collapse");
            StateHasChanged();
        }
        else
        {
            var items = CollapsedFor(area);
            if (items.Count == 0) return;
            foreach (var item in items)
            {
                win.CollapsedTabs.Remove(item);
                await ResolveFragmentAsync(item.Tab, ct);
                DockIntoRail(area, item.Tab);
            }
            OnLayoutMutated("rail-expand");
            StateHasChanged();
        }
    }

    Task CollapseActiveTabAsync(DockArea area, DockGroup group)
    {
        if (layout is null || group.Tabs.Count == 0) return Task.CompletedTask;
        var idx = Math.Clamp(group.ActiveTabIndex, 0, group.Tabs.Count - 1);
        var tab = group.Tabs[idx];
        group.Tabs.RemoveAt(idx);
        // the next tab becomes active; the group stays expanded while tabs remain
        group.ActiveTabIndex = Math.Clamp(idx, 0, Math.Max(0, group.Tabs.Count - 1));
        layout.MainWindow.CollapsedTabs.Add(new DockCollapsedPanel { Area = area, Tab = tab });
        SimplifyAll();
        OnLayoutMutated("panel-collapse");
        StateHasChanged();
        return Task.CompletedTask;
    }

    async Task RestoreCollapsedAsync(DockCollapsedPanel item)
    {
        if (layout is null) return;
        layout.MainWindow.CollapsedTabs.Remove(item);
        await ResolveFragmentAsync(item.Tab, CancellationToken.None);
        DockIntoRail(item.Area, item.Tab);
        OnLayoutMutated("panel-expand");
        await ActivatePanelAsync(item.Tab.PanelInstanceId);
        StateHasChanged();
    }

    List<DockCollapsedPanel> CollapsedFor(DockArea area)
        => layout?.MainWindow.CollapsedTabs.Where(c => c.Area == area).ToList() ?? new();

    bool HasCollapsed(DockArea area)
        => layout?.MainWindow.CollapsedTabs.Any(c => c.Area == area) == true;

    public Task SetGroupCollapsedAsync(string groupId, bool collapsed, CancellationToken ct = default)
    {
        var group = AllGroups().FirstOrDefault(g => g.GroupId == groupId);
        if (group is null || group.IsCollapsed == collapsed) return Task.CompletedTask;
        group.IsCollapsed = collapsed;
        OnLayoutMutated(collapsed ? "group-collapse" : "group-expand");
        StateHasChanged();
        return Task.CompletedTask;
    }

    Task ToggleGroupCollapsedAsync(DockGroup group)
        => SetGroupCollapsedAsync(group.GroupId, !group.IsCollapsed);

    double RailSize(DockArea area)
    {
        var win = layout!.MainWindow;
        return area switch
        {
            DockArea.Left => win.LeftRailSize ?? 230,
            DockArea.Right => win.RightRailSize ?? 230,
            DockArea.Top => win.TopRailSize ?? 170,
            _ => win.BottomRailSize ?? 180
        };
    }

    string RailSizeStyle(DockArea area)
    {
        var size = RailSize(area).ToString("0.##", CultureInfo.InvariantCulture);
        return area is DockArea.Left or DockArea.Right
            ? $"width:{size}px;"
            : $"height:{size}px;";
    }

    [JSInvokable]
    public void OnRailResizedJs(string areaName, double size)
    {
        if (IsLocked || layout is null) return;
        if (!Enum.TryParse<DockArea>(areaName, ignoreCase: true, out var area)) return;
        size = Math.Clamp(size, 80, 1200);
        var win = layout.MainWindow;
        switch (area)
        {
            case DockArea.Left: win.LeftRailSize = size; break;
            case DockArea.Right: win.RightRailSize = size; break;
            case DockArea.Top: win.TopRailSize = size; break;
            default: win.BottomRailSize = size; break;
        }
        OnLayoutMutated("rail-resize");
        StateHasChanged();
    }

    // ----------------------------------------------------------------- JS callbacks
    [JSInvokable]
    public void OnSplitterRatioChangedJs(string splitId, double ratio)
    {
        if (IsLocked || layout is null) return;
        var split = AllSplits().FirstOrDefault(s => s.RuntimeId == splitId);
        if (split is null) return;
        split.Ratio = Math.Clamp(ratio, 0.08, 0.92);
        OnLayoutMutated("splitter");
        StateHasChanged();
    }

    [JSInvokable]
    public void OnDragStartedJs(string instanceId)
    {
        if (IsLocked) return;
        var tab = FindTab(instanceId);
        if (tab is null) return;
        events.RaiseDragStarted(new DockDragEventArgs { SourcePanelInstanceId = instanceId });
    }

    [JSInvokable]
    public void OnDragCancelledJs(string instanceId)
        => events.RaiseDragCancelled(new DockDragEventArgs { SourcePanelInstanceId = instanceId });

    [JSInvokable]
    public async Task OnTabDroppedJs(string instanceId, string? targetGroupId, string zoneName, int index, double x, double y, double width, double height)
    {
        if (IsLocked || layout is null) return;
        if (!Enum.TryParse<DockZone>(zoneName, out var zone)) return;

        var targetGroup = FindGroup(targetGroupId);
        var bounds = new DockRect(x, y,
            width > 0 ? width : DockLayoutOps.DefaultFloatWidth,
            height > 0 ? height : DockLayoutOps.DefaultFloatHeight);
        if (!DockLayoutOps.DropTab(layout, instanceId, targetGroup, zone, index, bounds))
        {
            events.RaiseDragCancelled(new DockDragEventArgs { SourcePanelInstanceId = instanceId });
            StateHasChanged();
            return;
        }

        if (zone == DockZone.TearOff)
            BringFloatingToFront(layout.FloatingWindows.Count - 1);
        OnLayoutMutated("drag-drop");
        events.RaiseDragCompleted(new DockDragEventArgs
        {
            SourcePanelInstanceId = instanceId,
            TargetGroupId = targetGroupId,
            TargetZone = zone
        });
        await ActivatePanelAsync(instanceId);
        StateHasChanged();
    }

    /// <summary>A whole floating window dragged by its title bar and released on a docking guide.</summary>
    [JSInvokable]
    public async Task OnFloatingDroppedJs(int windowIndex, string? targetGroupId, string zoneName, int index)
    {
        if (IsLocked || layout is null) return;
        if (!Enum.TryParse<DockZone>(zoneName, out var zone)) return;
        if (windowIndex < 0 || windowIndex >= layout.FloatingWindows.Count) return;

        var fw = layout.FloatingWindows[windowIndex];
        var active = ActiveTabOf(fw);
        if (!DockLayoutOps.DropFloatingWindow(layout, windowIndex, FindGroup(targetGroupId), zone, index))
        {
            StateHasChanged();
            return;
        }
        floatOrder.Remove(fw);

        OnLayoutMutated("float-dock");
        if (active is not null)
        {
            events.RaiseDragCompleted(new DockDragEventArgs
            {
                SourcePanelInstanceId = active.PanelInstanceId,
                TargetGroupId = targetGroupId,
                TargetZone = zone
            });
            await ActivatePanelAsync(active.PanelInstanceId);
        }
        StateHasChanged();
    }

    [JSInvokable]
    public void OnFloatingMovedJs(int index, double x, double y)
    {
        if (IsLocked || layout is null || index < 0 || index >= layout.FloatingWindows.Count) return;
        var fw = layout.FloatingWindows[index];
        var b = fw.Bounds ?? new DockRect(x, y, DockLayoutOps.DefaultFloatWidth, DockLayoutOps.DefaultFloatHeight);
        // no-change guard: a click on the title bar must not churn LayoutChanged / the debounced save
        if (Math.Abs(b.X - x) < 0.5 && Math.Abs(b.Y - y) < 0.5) return;
        fw.Bounds = DockLayoutOps.ClampFloat(b with { X = x, Y = y });
        OnLayoutMutated("float-move");
        StateHasChanged();
    }

    [JSInvokable]
    public void OnFloatingBoundsChangedJs(int index, double x, double y, double width, double height)
    {
        if (IsLocked || layout is null || index < 0 || index >= layout.FloatingWindows.Count) return;
        var fw = layout.FloatingWindows[index];
        var next = DockLayoutOps.ClampFloat(new DockRect(x, y, width, height));
        if (fw.Bounds is { } b
            && Math.Abs(b.X - next.X) < 0.5 && Math.Abs(b.Y - next.Y) < 0.5
            && Math.Abs(b.Width - next.Width) < 0.5 && Math.Abs(b.Height - next.Height) < 0.5)
            return;
        fw.Bounds = next;
        OnLayoutMutated("float-resize");
        StateHasChanged();
    }

    /// <summary>Raises a floating window above its siblings. Z-order is session state, never persisted.</summary>
    [JSInvokable]
    public void OnFloatingFocusedJs(int index)
    {
        if (layout is null || index < 0 || index >= layout.FloatingWindows.Count) return;
        if (BringFloatingToFront(index))
            StateHasChanged();
    }

    // ----------------------------------------------------------------- floating
    async Task DockFloatingAsync(int index)
    {
        if (IsLocked || layout is null || index < 0 || index >= layout.FloatingWindows.Count) return;
        floatOrder.Remove(layout.FloatingWindows[index]);
        var tabs = DockLayoutOps.DockFloatingBack(layout, index);

        OnLayoutMutated("dock-floating");
        if (tabs.Count > 0)
            await ActivatePanelAsync(tabs[^1].PanelInstanceId);
        StateHasChanged();
    }

    Task CloseFloatingAsync(int index)
    {
        if (IsLocked || layout is null || index < 0 || index >= layout.FloatingWindows.Count) return Task.CompletedTask;
        var fw = layout.FloatingWindows[index];
        if (!CanCloseFloating(fw)) return Task.CompletedTask;
        foreach (var t in DockLayoutOps.GroupsIn(fw.DocumentArea).SelectMany(g => g.Tabs))
            fragments.Remove(t.PanelInstanceId);
        layout.FloatingWindows.RemoveAt(index);
        floatOrder.Remove(fw);
        OnLayoutMutated("close-floating");
        StateHasChanged();
        return Task.CompletedTask;
    }

    public async Task FloatPanelAsync(string panelInstanceId, CancellationToken ct = default)
    {
        if (IsLocked || layout is null) return;
        var n = layout.FloatingWindows.Count;
        var bounds = new DockRect(60 + n * 28, 48 + n * 28, DockLayoutOps.DefaultFloatWidth, DockLayoutOps.DefaultFloatHeight);
        var before = layout.FloatingWindows.Count;
        var fw = DockLayoutOps.FloatTab(layout, panelInstanceId, bounds);
        if (fw is null || layout.FloatingWindows.Count == before) return;

        BringFloatingToFront(layout.FloatingWindows.IndexOf(fw));
        OnLayoutMutated("float-panel");
        await ActivatePanelAsync(panelInstanceId, ct);
        StateHasChanged();
    }

    /// <summary>Double-clicking a tab floats it; inside a floating window it docks the window back home.</summary>
    Task OnTabDoubleClickAsync(DockTab tab)
    {
        if (IsLocked || layout is null) return Task.CompletedTask;
        var group = DockLayoutOps.AllGroups(layout).FirstOrDefault(g => g.Tabs.Contains(tab));
        if (group is not null && DockLayoutOps.FloatingWindowOf(layout, group) is { } owner)
            return DockFloatingAsync(layout.FloatingWindows.IndexOf(owner));
        return FloatPanelAsync(tab.PanelInstanceId);
    }

    bool BringFloatingToFront(int index)
    {
        if (layout is null || index < 0 || index >= layout.FloatingWindows.Count) return false;
        var fw = layout.FloatingWindows[index];
        if (floatOrder.TryGetValue(fw, out var z) && z == floatZ) return false;
        floatOrder[fw] = ++floatZ;
        return true;
    }

    int FloatZ(DockWindowState fw)
        => floatOrder.TryGetValue(fw, out var z) ? z : 0;

    bool CanCloseFloating(DockWindowState fw)
        => DockLayoutOps.GroupsIn(fw.DocumentArea).SelectMany(g => g.Tabs).All(CanCloseTab);

    DockTab? ActiveTabOf(DockWindowState fw)
    {
        var group = DockLayoutOps.GroupsIn(fw.DocumentArea).FirstOrDefault();
        if (group is null || group.Tabs.Count == 0) return null;
        return group.Tabs[Math.Clamp(group.ActiveTabIndex, 0, group.Tabs.Count - 1)];
    }

    bool IsFloatingActive(DockWindowState fw)
        => layout?.MainWindow.ActivePanelId is { } id
           && DockLayoutOps.GroupsIn(fw.DocumentArea).Any(g => g.Tabs.Any(t => t.PanelInstanceId == id));

    bool IsGroupFocused(DockGroup group)
        => layout?.MainWindow.ActivePanelId is { } id && group.Tabs.Any(t => t.PanelInstanceId == id);

    DockGroup? FindGroup(string? groupId)
        => groupId is null ? null : AllGroups().FirstOrDefault(g => g.GroupId == groupId);

    string FloatTitle(DockWindowState fw)
        => ActiveTabOf(fw) is { } active ? GetTabTitle(active) : "Floating";

    string? FloatIcon(DockWindowState fw)
        => ActiveTabOf(fw) is { } active ? GetTabIcon(active) : null;

    /// <summary>One group holding one tab: the title bar already names it, so the tab strip is hidden.</summary>
    static bool IsSingleTabFloat(DockWindowState fw)
        => fw.DocumentArea is DockGroup { Tabs.Count: 1 };

    string FloatStyle(DockWindowState fw, DockRect b) => string.Create(CultureInfo.InvariantCulture,
        $"left:{b.X:0.##}px;top:{b.Y:0.##}px;width:{b.Width:0.##}px;height:{b.Height:0.##}px;z-index:{50 + FloatZ(fw)};");

    // ----------------------------------------------------------------- internals
    void OnLayoutMutated(string reason)
    {
        events.RaiseLayoutChanged(new LayoutChangedEventArgs { Snapshot = Snapshot(), Reason = reason });
        QueueSave();
    }

    void QueueSave()
    {
        if (LayoutStore is null || layout is null) return;
        saveCts?.Cancel();
        var snapshot = Snapshot();
        var debounce = Math.Max(0, LayoutStore.SaveDebounceMs);
        if (debounce == 0)
        {
            _ = SaveSafeAsync(snapshot, CancellationToken.None);
            return;
        }
        saveCts = new CancellationTokenSource();
        var token = saveCts.Token;
        _ = DebouncedSaveAsync(snapshot, debounce, token);
    }

    async Task DebouncedSaveAsync(DockRoot snapshot, int debounce, CancellationToken token)
    {
        try
        {
            await Task.Delay(debounce, token);
            await SaveSafeAsync(snapshot, token);
        }
        catch (OperationCanceledException) { }
    }

    async Task SaveSafeAsync(DockRoot snapshot, CancellationToken ct)
    {
        try { await LayoutStore!.SaveAsync(snapshot, ct); }
        catch { /* persistence must never take the host down */ }
    }

    void ActivateTab(DockGroup group, int index)
    {
        if (index < 0 || index >= group.Tabs.Count) return;
        if (group.IsCollapsed)
        {
            group.IsCollapsed = false;
            OnLayoutMutated("group-expand");
        }
        group.ActiveTabIndex = index;
        group.FocusHistory.Remove(index);
        group.FocusHistory.Add(index);

        var tab = group.Tabs[index];
        if (layout is not null)
            layout.MainWindow.ActivePanelId = tab.PanelInstanceId;

        commandScope.IsInScope = true;
        commandScope.ActiveGroupId = group.GroupId;
        commandScope.ActivePanelInstanceId = tab.PanelInstanceId;

        events.RaisePanelActivated(new PanelActivatedEventArgs
        {
            PanelInstanceId = tab.PanelInstanceId,
            PanelTypeId = tab.PanelTypeId
        });
        StateHasChanged();
    }

    /// <summary>The tab with this instance id, wherever it currently sits - docked or collapsed.</summary>
    DockTab? FindTab(string panelInstanceId)
    {
        var collapsed = layout?.MainWindow.CollapsedTabs
            .FirstOrDefault(c => c.Tab.PanelInstanceId == panelInstanceId);

        if (collapsed is not null)
            return collapsed.Tab;

        return AllGroups()
            .SelectMany(g => g.Tabs)
            .FirstOrDefault(t => t.PanelInstanceId == panelInstanceId);
    }

    string GetTabTitle(DockTab tab)
        => registry?.Resolve(tab.PanelTypeId)?.DisplayName ?? tab.PanelTypeId;

    string? GetTabIcon(DockTab tab)
        => registry?.Resolve(tab.PanelTypeId)?.Icon;

    /// <summary>
    /// Whether a tab may be closed. Unknown panel types are closable: a layout naming a panel this
    /// app no longer registers is one the user needs to be able to get rid of.
    /// </summary>
    bool CanCloseTab(DockTab tab)
        => registry?.Resolve(tab.PanelTypeId)?.CanClose ?? true;

    static string FlexStyle(double ratio)
        => $"flex:{ratio.ToString("0.####", CultureInfo.InvariantCulture)} 1 0%;";

    async Task ResolveFragmentsAsync(CancellationToken ct)
    {
        foreach (var group in AllGroups())
            foreach (var tab in group.Tabs)
                await ResolveFragmentAsync(tab, ct);
    }

    async Task ResolveFragmentAsync(DockTab tab, CancellationToken ct)
    {
        if (fragments.ContainsKey(tab.PanelInstanceId)) return;
        var factory = registry?.Resolve(tab.PanelTypeId);
        if (factory is null) return; // renders the "unknown panel" placeholder
        fragments[tab.PanelInstanceId] = await factory.CreateAsync(tab.PanelInstanceId, ct);
    }

    IEnumerable<DockGroup> AllGroups()
        => layout is null ? Enumerable.Empty<DockGroup>() : DockLayoutOps.AllGroups(layout);

    IEnumerable<DockSplit> AllSplits()
    {
        if (layout is null) yield break;
        var win = layout.MainWindow;
        var roots = new List<DockNode?> { win.DocumentArea, win.LeftRail, win.TopRail, win.RightRail, win.BottomRail };
        roots.AddRange(layout.FloatingWindows.Select(w => (DockNode?)w.DocumentArea));
        foreach (var node in roots)
            foreach (var split in DockLayoutOps.SplitsIn(node))
                yield return split;
    }

    static IEnumerable<DockGroup> GroupsIn(DockNode? node) => DockLayoutOps.GroupsIn(node);

    void SimplifyAll()
    {
        if (layout is not null)
            DockLayoutOps.Simplify(layout);
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        saveCts?.Cancel();
        try
        {
            if (module is not null)
            {
                await module.InvokeVoidAsync("dispose", hostRef);
                await module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { }
        dotnetRef?.Dispose();
    }

    sealed class DockEventsImpl : IDockEvents
    {
        public event EventHandler<LayoutChangedEventArgs>? LayoutChanged;
        public event EventHandler<PanelActivatedEventArgs>? PanelActivated;
        public event EventHandler<DockDragEventArgs>? DragStarted;
        public event EventHandler<DockDragEventArgs>? DragCompleted;
        public event EventHandler<DockDragEventArgs>? DragCancelled;

        internal void RaiseLayoutChanged(LayoutChangedEventArgs e) => LayoutChanged?.Invoke(this, e);
        internal void RaisePanelActivated(PanelActivatedEventArgs e) => PanelActivated?.Invoke(this, e);
        internal void RaiseDragStarted(DockDragEventArgs e) => DragStarted?.Invoke(this, e);
        internal void RaiseDragCompleted(DockDragEventArgs e) => DragCompleted?.Invoke(this, e);
        internal void RaiseDragCancelled(DockDragEventArgs e) => DragCancelled?.Invoke(this, e);
    }

    sealed class DockCommandScopeImpl : IDockCommandScope
    {
        public bool IsInScope { get; internal set; }
        public string? ActiveGroupId { get; internal set; }
        public string? ActivePanelInstanceId { get; internal set; }
    }
}
