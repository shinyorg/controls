using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

using Shiny.Blazor.Controls.Theming;

namespace Shiny.Blazor.Controls;

public partial class TreeView<TItem> : IAsyncDisposable
{
    readonly List<BlazorTreeNode<TItem>> rootNodes = new();
    BlazorTreeNode<TItem>? focusedNode;
    ElementReference rootElement;
    IJSObjectReference? dragModule;
    DotNetObjectReference<TreeView<TItem>>? selfRef;
    bool disposed;
    IEnumerable<TItem>? lastItemsSource;
    BlazorTreeSelectionMode? selectionMode;
    bool isLoadingRoot;
    bool rootLoaderInvoked;
    Exception? rootError;

    [Inject] IJSRuntime JS { get; set; } = null!;

    // ------------- Data -------------
    [Parameter] public IEnumerable<TItem>? ItemsSource { get; set; }
    [Parameter] public Func<Task<IEnumerable<TItem>>>? RootLoader { get; set; }
    [Parameter] public Func<TItem, IEnumerable<TItem>?>? ChildrenSelector { get; set; }
    [Parameter] public Func<TItem, Task<IEnumerable<TItem>>>? ChildrenLoader { get; set; }
    [Parameter] public Func<TItem, bool>? HasChildrenSelector { get; set; }
    [Parameter] public Func<TItem, bool>? CanExpandSelector { get; set; }
    [Parameter] public Func<TItem, bool>? CanSelectSelector { get; set; }

    // ------------- Templates -------------
    [Parameter] public RenderFragment<TItem>? ItemTemplate { get; set; }
    [Parameter] public RenderFragment? ExpandedIcon { get; set; }
    [Parameter] public RenderFragment? CollapsedIcon { get; set; }
    [Parameter] public RenderFragment? RetryIcon { get; set; }
    [Parameter] public RenderFragment? LoadingTemplate { get; set; }

    // ------------- Selection -------------
    [Parameter] public BlazorTreeSelectionMode SelectionMode { get; set; } = BlazorTreeSelectionMode.Single;

    /// <summary>
    /// Show a checkbox on each row while <see cref="SelectionMode"/> is
    /// <see cref="BlazorTreeSelectionMode.Multiple"/>. Defaults to true.
    /// </summary>
    [Parameter] public bool ShowSelectionCheckBoxes { get; set; } = true;

    [Parameter] public TItem? SelectedItem { get; set; }
    [Parameter] public EventCallback<TItem?> SelectedItemChanged { get; set; }
    [Parameter] public IList<TItem>? SelectedItems { get; set; }
    [Parameter] public EventCallback<IList<TItem>> SelectedItemsChanged { get; set; }

    // ------------- Events -------------
    [Parameter] public EventCallback<TreeItemEventArgs<TItem>> ItemSelected { get; set; }
    [Parameter] public EventCallback<TreeItemEventArgs<TItem>> ItemExpanded { get; set; }
    [Parameter] public EventCallback<TreeItemEventArgs<TItem>> ItemCollapsed { get; set; }
    [Parameter] public EventCallback<TreeLoadFailedEventArgs<TItem>> LoadFailed { get; set; }
    [Parameter] public EventCallback<TreeItemDroppedEventArgs<TItem>> ItemDropped { get; set; }

    // ------------- Layout / visuals -------------
    [Parameter] public double IndentSize { get; set; } = 20;
    [Parameter] public double ChevronSize { get; set; } = 14;
    // Mirrored in the component stylesheet; only a caller-chosen value inlines. See StyleDefaults.
    internal const string DefaultChevronColor = "var(--shiny-color-on-surface-variant, #3B475B)";

    [Parameter] public string ChevronColor { get; set; } = DefaultChevronColor;
    [Parameter] public bool ShowGuideLines { get; set; } = false;
    [Parameter] public bool EnableDragDrop { get; set; } = false;
    [Parameter] public string? CssClass { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        // Null on the first parameter set: nothing to drop yet, and clearing would wipe a
        // SelectedItems collection the caller pre-populated.
        if (selectionMode is null)
        {
            selectionMode = SelectionMode;
        }
        else if (selectionMode != SelectionMode)
        {
            // Switching modes drops the current selection: leftover multi-selection would
            // otherwise linger as several highlighted rows in Single mode.
            var hadSelection = EnumerateAll(rootNodes).Any(n => n.IsSelected);
            selectionMode = SelectionMode;
            foreach (var n in EnumerateAll(rootNodes))
                n.IsSelected = false;
            SelectedItems?.Clear();
            if (hadSelection)
            {
                SelectedItem = default;
                await SelectedItemChanged.InvokeAsync(default);
            }
        }

        if (RootLoader != null)
        {
            if (!rootLoaderInvoked)
                await EnsureRootLoadedAsync();
        }
        // only rebuild when the source actually changes — parameters re-set on
        // every parent render (lambdas are fresh instances), and rebuilding
        // recreates the nodes, wiping all expansion/selection state
        else if (!ReferenceEquals(ItemsSource, lastItemsSource))
        {
            lastItemsSource = ItemsSource;
            RebuildRootNodes();
        }
    }

    void RebuildRootNodes()
    {
        rootNodes.Clear();
        focusedNode = null;
        if (ItemsSource == null) return;
        foreach (var item in ItemsSource)
            rootNodes.Add(new BlazorTreeNode<TItem>(item, null, 0));
    }

    async Task EnsureRootLoadedAsync()
    {
        if (RootLoader == null || rootLoaderInvoked) return;
        rootLoaderInvoked = true;
        isLoadingRoot = true;
        rootError = null;
        StateHasChanged();
        try
        {
            var items = await RootLoader();
            rootNodes.Clear();
            foreach (var item in items)
                rootNodes.Add(new BlazorTreeNode<TItem>(item, null, 0));
        }
        catch (Exception ex)
        {
            rootError = ex;
        }
        finally
        {
            isLoadingRoot = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Rebuilds the tree from the current data, preserving expansion and selection
    /// state for items that still exist (matched with the default equality comparer).
    /// </summary>
    public async Task ReloadAsync()
    {
        var expanded = EnumerateAll(rootNodes).Where(n => n.IsExpanded).Select(n => n.Item).ToList();
        var selected = EnumerateAll(rootNodes).Where(n => n.IsSelected).Select(n => n.Item).ToList();

        if (RootLoader != null)
        {
            rootLoaderInvoked = false;
            await EnsureRootLoadedAsync();
        }
        else
        {
            RebuildRootNodes();
        }

        await RestoreStateAsync(expanded, selected);
        StateHasChanged();
    }

    async Task RestoreStateAsync(List<TItem> expanded, List<TItem> selected)
    {
        // Children are built lazily, so an item nested under a not-yet-expanded
        // node can't be found until its ancestors expand — keep sweeping until a
        // pass makes no progress.
        var progress = true;
        while (progress && expanded.Count > 0)
        {
            progress = false;
            for (var i = expanded.Count - 1; i >= 0; i--)
            {
                var node = FindNode(expanded[i]);
                if (node == null)
                    continue;

                expanded.RemoveAt(i);
                progress = true;
                if (!node.IsExpanded && HasChildren(node.Item) && CanExpand(node.Item))
                    await ExpandNodeAsync(node, raiseEvents: false);
            }
        }

        foreach (var item in selected)
        {
            var node = FindNode(item);
            if (node != null)
                node.IsSelected = true;
        }
    }

    // ------------- Predicates -------------
    bool HasChildren(TItem item)
    {
        if (HasChildrenSelector != null) return HasChildrenSelector(item);
        if (ChildrenLoader != null) return true;
        var kids = ChildrenSelector?.Invoke(item);
        return kids != null && kids.Any();
    }

    bool CanExpand(TItem item) => CanExpandSelector?.Invoke(item) ?? true;

    bool CanSelect(TItem item)
    {
        if (SelectionMode == BlazorTreeSelectionMode.None) return false;
        return CanSelectSelector?.Invoke(item) ?? true;
    }

    // ------------- Expansion -------------
    async Task OnChevronClick(MouseEventArgs e, BlazorTreeNode<TItem> node, bool hasChildren, bool canExpand)
    {
        if (node.LoadState == BlazorTreeLoadState.Error)
        {
            await RetryAsync(node);
            return;
        }
        if (!hasChildren || !canExpand) return;
        await ToggleExpandAsync(node);
    }

    async Task ToggleExpandAsync(BlazorTreeNode<TItem> node)
    {
        if (node.IsExpanded)
        {
            node.IsExpanded = false;
            await ItemCollapsed.InvokeAsync(new TreeItemEventArgs<TItem>(node));
            StateHasChanged();
            return;
        }

        await ExpandNodeAsync(node, raiseEvents: true);
    }

    async Task<bool> ExpandNodeAsync(BlazorTreeNode<TItem> node, bool raiseEvents)
    {
        if (node.Children == null)
        {
            var syncKids = ChildrenSelector?.Invoke(node.Item);
            if (syncKids != null)
            {
                node.Children = new List<BlazorTreeNode<TItem>>();
                foreach (var item in syncKids)
                    node.Children.Add(new BlazorTreeNode<TItem>(item, node, node.Depth + 1));
                node.LoadState = BlazorTreeLoadState.Loaded;
            }
            else if (ChildrenLoader != null)
            {
                node.LoadState = BlazorTreeLoadState.Loading;
                StateHasChanged();
                try
                {
                    var kids = await ChildrenLoader(node.Item);
                    node.Children = new List<BlazorTreeNode<TItem>>();
                    foreach (var item in kids)
                        node.Children.Add(new BlazorTreeNode<TItem>(item, node, node.Depth + 1));
                    node.LoadState = BlazorTreeLoadState.Loaded;
                }
                catch (Exception ex)
                {
                    node.LoadError = ex;
                    node.LoadState = BlazorTreeLoadState.Error;
                    await LoadFailed.InvokeAsync(new TreeLoadFailedEventArgs<TItem>(node, ex));
                    StateHasChanged();
                    return false;
                }
            }
            else
            {
                node.Children = new List<BlazorTreeNode<TItem>>();
                node.LoadState = BlazorTreeLoadState.Loaded;
            }
        }

        node.IsExpanded = true;
        if (raiseEvents)
            await ItemExpanded.InvokeAsync(new TreeItemEventArgs<TItem>(node));
        StateHasChanged();
        return true;
    }

    async Task RetryAsync(BlazorTreeNode<TItem> node)
    {
        node.LoadState = BlazorTreeLoadState.NotLoaded;
        node.Children = null;
        node.LoadError = null;
        await ToggleExpandAsync(node);
    }

    public async Task ExpandAsync(TItem item)
    {
        var n = FindNode(item);
        if (n != null && !n.IsExpanded) await ToggleExpandAsync(n);
    }

    public async Task CollapseAsync(TItem item)
    {
        var n = FindNode(item);
        if (n != null && n.IsExpanded) await ToggleExpandAsync(n);
    }

    /// <summary>
    /// Default depth cap for <see cref="ExpandAllAsync(int)"/>. Stops a self-referencing
    /// (or endlessly lazy-loaded) hierarchy from expanding forever.
    /// </summary>
    public const int DefaultExpandAllMaxDepth = 32;

    /// <summary>
    /// Expand every node, awaiting <see cref="ChildrenLoader"/> for branches that need it.
    /// </summary>
    /// <param name="maxDepth">Deepest node depth to expand. Guards against cyclic hierarchies.</param>
    public async Task ExpandAllAsync(int maxDepth = DefaultExpandAllMaxDepth)
    {
        foreach (var n in rootNodes) await ExpandRecursive(n, maxDepth);
        StateHasChanged();
    }

    async Task ExpandRecursive(BlazorTreeNode<TItem> node, int maxDepth)
    {
        if (node.Depth >= maxDepth) return;
        if (!HasChildren(node.Item) || !CanExpand(node.Item)) return;
        if (!node.IsExpanded) await ExpandNodeAsync(node, raiseEvents: false);
        if (node.Children != null)
            foreach (var c in node.Children)
                await ExpandRecursive(c, maxDepth);
    }

    public void CollapseAll()
    {
        foreach (var n in rootNodes) CollapseRecursive(n);
        StateHasChanged();
    }

    void CollapseRecursive(BlazorTreeNode<TItem> node)
    {
        node.IsExpanded = false;
        if (node.Children != null)
            foreach (var c in node.Children) CollapseRecursive(c);
    }

    public async Task RefreshAsync(TItem item)
    {
        var node = FindNode(item);
        if (node == null) return;
        var wasExpanded = node.IsExpanded;
        node.IsExpanded = false;
        node.Children = null;
        node.LoadState = BlazorTreeLoadState.NotLoaded;
        node.LoadError = null;
        if (wasExpanded) await ToggleExpandAsync(node);
        else StateHasChanged();
    }

    BlazorTreeNode<TItem>? FindNode(TItem item)
    {
        foreach (var n in rootNodes)
        {
            var f = FindRecursive(n, item);
            if (f != null) return f;
        }
        return null;
    }

    static BlazorTreeNode<TItem>? FindRecursive(BlazorTreeNode<TItem> node, TItem item)
    {
        if (EqualityComparer<TItem>.Default.Equals(node.Item, item)) return node;
        if (node.Children == null) return null;
        foreach (var c in node.Children)
        {
            var f = FindRecursive(c, item);
            if (f != null) return f;
        }
        return null;
    }

    // ------------- Selection -------------
    async Task OnRowClick(MouseEventArgs e, BlazorTreeNode<TItem> node, bool canSelect)
    {
        focusedNode = node;
        if (!canSelect) return;

        switch (SelectionMode)
        {
            case BlazorTreeSelectionMode.None:
                return;
            case BlazorTreeSelectionMode.Single:
                foreach (var n in EnumerateAll(rootNodes)) n.IsSelected = false;
                node.IsSelected = true;
                SelectedItem = node.Item;
                await SelectedItemChanged.InvokeAsync(node.Item);
                break;
            case BlazorTreeSelectionMode.Multiple:
                await ApplyMultiSelectAsync(node, !node.IsSelected);
                break;
        }
        await ItemSelected.InvokeAsync(new TreeItemEventArgs<TItem>(node));
    }

    async Task OnCheckChanged(BlazorTreeNode<TItem> node, bool canSelect, ChangeEventArgs e)
    {
        if (!canSelect || SelectionMode != BlazorTreeSelectionMode.Multiple)
            return;

        focusedNode = node;
        await ApplyMultiSelectAsync(node, e.Value is true or "true" or "True");
        await ItemSelected.InvokeAsync(new TreeItemEventArgs<TItem>(node));
        StateHasChanged();
    }

    async Task ApplyMultiSelectAsync(BlazorTreeNode<TItem> node, bool selected)
    {
        node.IsSelected = selected;
        SelectedItems ??= new List<TItem>();
        if (selected && !SelectedItems.Contains(node.Item))
            SelectedItems.Add(node.Item);
        else if (!selected && SelectedItems.Contains(node.Item))
            SelectedItems.Remove(node.Item);
        await SelectedItemsChanged.InvokeAsync(SelectedItems);
        if (selected)
        {
            SelectedItem = node.Item;
            await SelectedItemChanged.InvokeAsync(node.Item);
        }
    }

    /// <summary>
    /// Check every selectable node, whether or not its branch is expanded. Only meaningful in
    /// <see cref="BlazorTreeSelectionMode.Multiple"/>; branches that have never been loaded
    /// aren't included (call <see cref="ExpandAllAsync(int)"/> first to materialize them).
    /// </summary>
    public async Task SelectAllAsync()
    {
        if (SelectionMode != BlazorTreeSelectionMode.Multiple)
            return;

        SelectedItems ??= new List<TItem>();
        foreach (var node in EnumerateAll(rootNodes))
        {
            if (!CanSelect(node.Item))
                continue;
            node.IsSelected = true;
            if (!SelectedItems.Contains(node.Item))
                SelectedItems.Add(node.Item);
        }
        await SelectedItemsChanged.InvokeAsync(SelectedItems);
        StateHasChanged();
    }

    /// <summary>
    /// Clear the selection in any mode: unchecks every node and resets
    /// <see cref="SelectedItem"/> / <see cref="SelectedItems"/>.
    /// </summary>
    public async Task DeselectAllAsync()
    {
        foreach (var node in EnumerateAll(rootNodes))
            node.IsSelected = false;

        SelectedItems?.Clear();
        if (SelectedItems != null)
            await SelectedItemsChanged.InvokeAsync(SelectedItems);

        SelectedItem = default;
        await SelectedItemChanged.InvokeAsync(default);
        StateHasChanged();
    }

    /// <summary>
    /// Check (or uncheck) an item and every materialized descendant beneath it.
    /// Only meaningful in <see cref="BlazorTreeSelectionMode.Multiple"/>.
    /// </summary>
    public async Task SetBranchSelectedAsync(TItem item, bool selected)
    {
        if (SelectionMode != BlazorTreeSelectionMode.Multiple)
            return;

        var node = FindNode(item);
        if (node == null)
            return;

        SelectedItems ??= new List<TItem>();
        foreach (var n in EnumerateAll(new[] { node }))
        {
            if (!CanSelect(n.Item))
                continue;
            n.IsSelected = selected;
            if (selected && !SelectedItems.Contains(n.Item))
                SelectedItems.Add(n.Item);
            else if (!selected)
                SelectedItems.Remove(n.Item);
        }
        await SelectedItemsChanged.InvokeAsync(SelectedItems);
        StateHasChanged();
    }

    IEnumerable<BlazorTreeNode<TItem>> EnumerateAll(IEnumerable<BlazorTreeNode<TItem>> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            if (n.Children != null)
                foreach (var c in EnumerateAll(n.Children))
                    yield return c;
        }
    }

    // ------------- Drag/drop -------------
    // Native DOM listeners in tree-view.js drive the drag (Blazor's synthetic drag
    // events can't call dataTransfer.setData(), which Safari/Firefox require to
    // start a drag at all) and report the finished drop back here.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (EnableDragDrop && dragModule == null && !disposed)
        {
            try
            {
                var localRef = DotNetObjectReference.Create(this);
                selfRef = localRef;
                var module = await JS.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./_content/Shiny.Blazor.Controls/tree-view.js");
                if (disposed)
                {
                    try { await module.DisposeAsync(); } catch { }
                    localRef.Dispose();
                    return;
                }
                dragModule = module;
                await dragModule.InvokeVoidAsync("init", rootElement, localRef);
            }
            catch (ObjectDisposedException) { }
            catch (JSDisconnectedException) { }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
        }
    }

    [JSInvokable]
    public async Task OnJsDrop(string sourceId, string targetId, string zone)
    {
        if (!EnableDragDrop || disposed) return;

        var all = EnumerateAll(rootNodes).ToList();
        var source = all.FirstOrDefault(n => n.Id == sourceId);
        var target = all.FirstOrDefault(n => n.Id == targetId);
        if (source == null || target == null || ReferenceEquals(source, target))
            return;

        // Disallow dropping onto descendants
        var probe = target;
        while (probe != null)
        {
            if (ReferenceEquals(probe, source)) return;
            probe = probe.Parent;
        }

        var position = zone switch
        {
            "above" => BlazorTreeDropPosition.Above,
            "into" => BlazorTreeDropPosition.Into,
            _ => BlazorTreeDropPosition.Below
        };
        await ItemDropped.InvokeAsync(new TreeItemDroppedEventArgs<TItem>(source, target, position));
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        try
        {
            if (dragModule != null)
            {
                await dragModule.InvokeVoidAsync("dispose", rootElement);
                await dragModule.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
        catch (OperationCanceledException) { }
        catch (JSException) { }
        selfRef?.Dispose();
    }

    // ------------- Keyboard nav -------------
    async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (focusedNode == null)
        {
            focusedNode = rootNodes.FirstOrDefault();
            if (focusedNode == null) return;
            StateHasChanged();
            return;
        }

        var flat = EnumerateAll(rootNodes).Where(n => IsVisible(n)).ToList();
        var idx = flat.IndexOf(focusedNode);

        switch (e.Key)
        {
            case "ArrowDown":
                if (idx < flat.Count - 1) { focusedNode = flat[idx + 1]; StateHasChanged(); }
                break;
            case "ArrowUp":
                if (idx > 0) { focusedNode = flat[idx - 1]; StateHasChanged(); }
                break;
            case "ArrowRight":
                if (!focusedNode.IsExpanded && HasChildren(focusedNode.Item) && CanExpand(focusedNode.Item))
                    await ToggleExpandAsync(focusedNode);
                else if (focusedNode.IsExpanded && focusedNode.Children?.Count > 0)
                { focusedNode = focusedNode.Children[0]; StateHasChanged(); }
                break;
            case "ArrowLeft":
                if (focusedNode.IsExpanded)
                    await ToggleExpandAsync(focusedNode);
                else if (focusedNode.Parent != null)
                { focusedNode = focusedNode.Parent; StateHasChanged(); }
                break;
            case "Enter":
            case " ":
                await OnRowClick(new MouseEventArgs(), focusedNode, CanSelect(focusedNode.Item));
                break;
            case "Home":
                if (flat.Count > 0) { focusedNode = flat[0]; StateHasChanged(); }
                break;
            case "End":
                if (flat.Count > 0) { focusedNode = flat[^1]; StateHasChanged(); }
                break;
        }
    }

    static bool IsVisible(BlazorTreeNode<TItem> n)
    {
        var p = n.Parent;
        while (p != null)
        {
            if (!p.IsExpanded) return false;
            p = p.Parent;
        }
        return true;
    }
}
