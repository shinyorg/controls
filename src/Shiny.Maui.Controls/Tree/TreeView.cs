using System.Collections;
using System.Collections.Specialized;
using Shiny.Maui.Controls.Tree.Internal;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Tree;

public partial class TreeView : ContentView
{
    /// <summary>Appended to the tree's <see cref="Element.AutomationId"/>, with the row's index in visible order, to name each node's row (the selection tap target).</summary>
    public const string NodeAutomationIdSuffix = "_Node";

    /// <summary>Appended to the tree's <see cref="Element.AutomationId"/>, with the row's index, to name each node's expand/collapse chevron.</summary>
    public const string ExpandAutomationIdSuffix = "_Expand";

    /// <summary>
    /// Appended to the tree's <see cref="Element.AutomationId"/>, with the row's index, to name each node's
    /// multi-select checkbox. Without a tree id every checkbox keeps the fixed <c>ShinyTreeNodeCheckBox</c>.
    /// </summary>
    public const string CheckBoxAutomationIdSuffix = "_CheckBox";

    /// <summary>Appended to the tree's <see cref="Element.AutomationId"/> to name the root loading indicator.</summary>
    public const string LoadingAutomationIdSuffix = "_Loading";

    /// <summary>Appended to the tree's <see cref="Element.AutomationId"/> to name the label shown when the root load fails.</summary>
    public const string ErrorAutomationIdSuffix = "_Error";

    readonly ScrollView scrollView;
    internal readonly VerticalStackLayout rowLayout;
    internal readonly VirtualRowPanel virtualPanel;
    readonly ActivityIndicator rootLoadingIndicator;
    readonly Label rootErrorLabel;
    readonly Grid root;

    readonly List<TreeNode> rootNodes = new();
    readonly Dictionary<TreeNode, TreeNodeView> nodeViews = new();
    readonly List<TreeNode> flatNodes = new();

    IDisposable? sourceSubscription;
    bool isRebuilding;
    Exception? rootLoadError;
    bool rootLoaderInvoked;

    public TreeView()
    {
        rowLayout = new VerticalStackLayout { Spacing = 0 };
        virtualPanel = new VirtualRowPanel();
        scrollView = new ScrollView { Content = rowLayout };
        scrollView.Scrolled += (_, _) => OnViewportChanged();
        scrollView.SizeChanged += (_, _) => OnViewportChanged();

        rootLoadingIndicator = new ActivityIndicator
        {
            IsRunning = false,
            IsVisible = false,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };
        rootErrorLabel = new Label
        {
            IsVisible = false,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            Padding = 16
        };
        rootErrorLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        root = new Grid { Children = { scrollView, rootLoadingIndicator, rootErrorLabel } };
        Content = root;

        AutomationIds.Link(this, rootLoadingIndicator, LoadingAutomationIdSuffix);
        AutomationIds.Link(this, rootErrorLabel, ErrorAutomationIdSuffix);
        // Rows are rebuilt on every change and named as they are built; this renames the current ones.
        AutomationIds.Track(this, NameRows);

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(TreeView));
    }

    // ------------- Source management -------------
    void OnItemsSourceChanged()
    {
        sourceSubscription?.Dispose();
        sourceSubscription = null;

        if (RootLoader != null)
        {
            // RootLoader takes precedence; leave nodes as-is until loader runs.
            return;
        }

        // Weak: ItemsSource is usually a view model's collection, which outlives the page.
        sourceSubscription = WeakEventSubscription.CollectionChanged(ItemsSource, OnSourceCollectionChanged);

        BuildRootNodes();
        Rebuild();
    }

    void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        BuildRootNodes();
        Rebuild();
    }

    void BuildRootNodes()
    {
        rootNodes.Clear();
        if (ItemsSource == null)
            return;

        foreach (var item in ItemsSource)
        {
            if (item != null)
                rootNodes.Add(new TreeNode(item, parent: null, depth: 0));
        }
    }

    async void OnRootLoaderChanged()
    {
        rootLoaderInvoked = false;
        await EnsureRootLoadedAsync();
    }

    async Task EnsureRootLoadedAsync()
    {
        if (RootLoader == null || rootLoaderInvoked)
            return;

        rootLoaderInvoked = true;
        rootLoadError = null;
        rootLoadingIndicator.IsVisible = true;
        rootLoadingIndicator.IsRunning = true;
        rootErrorLabel.IsVisible = false;
        scrollView.IsVisible = false;

        try
        {
            var items = await RootLoader();
            rootNodes.Clear();
            foreach (var item in items)
            {
                if (item != null)
                    rootNodes.Add(new TreeNode(item, parent: null, depth: 0));
            }
        }
        catch (Exception ex)
        {
            rootLoadError = ex;
            rootErrorLabel.Text = $"Failed to load: {ex.Message}\nTap to retry.";
            rootErrorLabel.IsVisible = true;
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
            {
                rootLoaderInvoked = false;
                await EnsureRootLoadedAsync();
            };
            rootErrorLabel.GestureRecognizers.Clear();
            rootErrorLabel.GestureRecognizers.Add(tap);
        }
        finally
        {
            rootLoadingIndicator.IsRunning = false;
            rootLoadingIndicator.IsVisible = false;
            scrollView.IsVisible = rootLoadError == null;
        }

        Rebuild();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler != null && RootLoader != null && !rootLoaderInvoked)
            _ = EnsureRootLoadedAsync();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        foreach (var node in flatNodes)
        {
            if (nodeViews.TryGetValue(node, out var view) && view.BindingContext is null)
                view.BindingContext = node.Item;
        }
    }

    // ------------- Predicates -------------
    internal bool HasChildren(object item)
    {
        if (HasChildrenSelector != null)
            return HasChildrenSelector(item);
        if (ChildrenLoader != null)
            return true;
        var kids = ChildrenSelector?.Invoke(item);
        return kids != null && kids.Any();
    }

    internal bool CanExpand(object item)
    {
        if (CanExpandSelector != null)
            return CanExpandSelector(item);
        return true;
    }

    internal bool CanSelect(object item)
    {
        if (SelectionMode == TreeSelectionMode.None)
            return false;
        if (CanSelectSelector != null)
            return CanSelectSelector(item);
        return true;
    }

    // ------------- Flatten + render -------------

    /// <summary>Rows realized above and below the viewport while <see cref="IsVirtualized"/>.</summary>
    internal const int VirtualOverscan = 6;

    /// <summary>How many row views exist right now: every visible node, or only the realized window when <see cref="IsVirtualized"/>.</summary>
    public int RealizedRowCount => nodeViews.Count;

    /// <summary>The rows currently in the tree: every visible node, or only the realized window when virtualized.</summary>
    internal IEnumerable<TreeNodeView> RowViews =>
        (IsVirtualized ? virtualPanel.Children : rowLayout.Children).OfType<TreeNodeView>();

    /// <summary>
    /// Re-flattens the visible nodes and re-renders. <paramref name="keepViews"/> is for changes that
    /// only move rows (expand / collapse): a virtualized tree then keeps the rows it already built for
    /// nodes still in range instead of rebuilding them. Anything that changes how a row looks
    /// (template, indent, selection mode...) rebuilds every row.
    /// </summary>
    internal void Rebuild(bool keepViews = false)
    {
        if (isRebuilding)
            return;
        isRebuilding = true;
        try
        {
            flatNodes.Clear();
            foreach (var n in rootNodes)
                Flatten(n);

            if (IsVirtualized)
            {
                if (!keepViews)
                    ClearVirtualRows();
                virtualPanel.RowHeight = RowHeight;
                virtualPanel.RowCount = flatNodes.Count;
                RealizeVisibleRows();
                return;
            }

            foreach (var view in nodeViews.Values)
                view.Detach();
            rowLayout.Children.Clear();
            var newViews = new Dictionary<TreeNode, TreeNodeView>();
            for (var i = 0; i < flatNodes.Count; i++)
            {
                var node = flatNodes[i];
                var view = new TreeNodeView(this, node);
                view.ApplyAutomationIds(i);
                newViews[node] = view;
                rowLayout.Children.Add(view);
            }
            nodeViews.Clear();
            foreach (var kv in newViews)
                nodeViews[kv.Key] = kv.Value;
        }
        finally
        {
            isRebuilding = false;
        }
    }

    void OnIsVirtualizedChanged()
    {
        // Each mode owns its own panel; empty the one being left so its rows unhook from their nodes.
        foreach (var view in nodeViews.Values)
            view.Detach();
        nodeViews.Clear();
        rowLayout.Children.Clear();
        virtualPanel.Children.Clear();

        scrollView.Content = IsVirtualized ? virtualPanel : rowLayout;
        Rebuild();
    }

    void OnViewportChanged()
    {
        if (IsVirtualized && !isRebuilding)
            RealizeVisibleRows();
    }

    void ClearVirtualRows()
    {
        foreach (var view in nodeViews.Values)
            view.Detach();
        nodeViews.Clear();
        virtualPanel.Children.Clear();
    }

    /// <summary>
    /// Brings the virtualized panel in line with the viewport: drops rows that left the window
    /// (plus overscan), builds the ones that entered it, and re-indexes the rows it keeps.
    /// </summary>
    internal void RealizeVisibleRows() =>
        RealizeVisibleRows(scrollView.ScrollY, scrollView.Height);

    internal void RealizeVisibleRows(double scrollY, double viewportHeight)
    {
        var (first, last) = VirtualRowPanel.VisibleRange(scrollY, viewportHeight, RowHeight, flatNodes.Count, VirtualOverscan);

        var wanted = new Dictionary<TreeNode, int>();
        for (var i = first; i <= last; i++)
            wanted[flatNodes[i]] = i;

        // A row being dragged stays realized even when it scrolls out of range - the pan
        // gesture belongs to it.
        var dragging = pointerDragSource?.Node;
        var stale = nodeViews
            .Where(kv => !wanted.ContainsKey(kv.Key) && !ReferenceEquals(kv.Key, dragging))
            .ToList();
        foreach (var kv in stale)
        {
            kv.Value.Detach();
            virtualPanel.Children.Remove(kv.Value);
            nodeViews.Remove(kv.Key);
        }

        foreach (var (node, index) in wanted)
        {
            if (nodeViews.TryGetValue(node, out var existing))
            {
                existing.ApplyAutomationIds(index);
            }
            else
            {
                var view = new TreeNodeView(this, node) { HeightRequest = RowHeight };
                view.ApplyAutomationIds(index);
                nodeViews[node] = view;
                virtualPanel.Children.Add(view);
            }
        }

        virtualPanel.InvalidateMeasure();
    }

    void NameRows()
    {
        foreach (var row in RowViews)
            row.ApplyAutomationIds(row.RowIndex);
    }

    void Flatten(TreeNode node)
    {
        flatNodes.Add(node);
        if (node.IsExpanded && node.Children != null)
        {
            foreach (var c in node.Children)
                Flatten(c);
        }
    }

    internal void RefreshChevrons()
    {
        foreach (var v in nodeViews.Values)
            v.RefreshChevron();
    }

    internal void RefreshSelectionVisuals()
    {
        foreach (var v in nodeViews.Values)
            v.RefreshSelection();
    }

    // ------------- Expand / collapse -------------
    internal async void ToggleExpand(TreeNode node)
    {
        if (node.IsExpanded)
        {
            node.IsExpanded = false;
            Rebuild(keepViews: true);
            RaiseCollapsed(node);
            return;
        }

        if (node.Children == null)
        {
            // Selector takes precedence; loader is a fallback for items the selector
            // can't provide (returns null). This lets a single tree mix sync and lazy
            // branches.
            var syncKids = ChildrenSelector?.Invoke(node.Item);
            if (syncKids != null)
            {
                node.Children = new System.Collections.ObjectModel.ObservableCollection<TreeNode>();
                foreach (var item in syncKids)
                {
                    if (item != null)
                        node.Children.Add(new TreeNode(item, node, node.Depth + 1));
                }
                node.LoadState = TreeLoadState.Loaded;
            }
            else if (ChildrenLoader != null)
            {
                node.LoadState = TreeLoadState.Loading;
                try
                {
                    var children = await ChildrenLoader(node.Item);
                    node.Children = new System.Collections.ObjectModel.ObservableCollection<TreeNode>();
                    foreach (var item in children)
                    {
                        if (item != null)
                            node.Children.Add(new TreeNode(item, node, node.Depth + 1));
                    }
                    node.LoadState = TreeLoadState.Loaded;
                }
                catch (Exception ex)
                {
                    node.LoadError = ex;
                    node.LoadState = TreeLoadState.Error;
                    RaiseLoadFailed(node, ex);
                    return;
                }
            }
            else
            {
                node.Children = new System.Collections.ObjectModel.ObservableCollection<TreeNode>();
                node.LoadState = TreeLoadState.Loaded;
            }
        }

        node.IsExpanded = true;
        Rebuild(keepViews: true);
        RaiseExpanded(node);
    }

    internal async void RetryLoad(TreeNode node)
    {
        node.LoadError = null;
        node.Children = null;
        node.LoadState = TreeLoadState.NotLoaded;
        await Task.Yield();
        ToggleExpand(node); // will trigger a load attempt
    }

    // ------------- Selection -------------
    internal void HandleRowTapped(TreeNode node)
    {
        if (!CanSelect(node.Item))
        {
            // Still raise selection-attempt? No — keep semantics tight.
            return;
        }

        switch (SelectionMode)
        {
            case TreeSelectionMode.None:
                return;

            case TreeSelectionMode.Single:
                // All nodes, not just the visible ones — a selection inside a collapsed
                // branch has to clear too.
                foreach (var n in EnumerateAllNodes())
                    n.IsSelected = ReferenceEquals(n, node);
                SetValueFromInternal(SelectedItemProperty, node.Item);
                break;

            case TreeSelectionMode.Multiple:
                node.IsSelected = !node.IsSelected;
                EnsureSelectedItemsCollection();
                if (node.IsSelected && !SelectedItems!.Contains(node.Item))
                    SelectedItems.Add(node.Item);
                else if (!node.IsSelected && SelectedItems!.Contains(node.Item))
                    SelectedItems.Remove(node.Item);
                SetValueFromInternal(SelectedItemProperty, node.IsSelected ? node.Item : null);
                break;
        }

        RaiseSelected(node);
    }

    void EnsureSelectedItemsCollection()
    {
        if (SelectedItems == null)
            SetValueFromInternal(SelectedItemsProperty, new System.Collections.ObjectModel.ObservableCollection<object>());
    }

    bool suppressSelectedItemSync;

    void OnSelectedItemPropertyChanged(object? newValue)
    {
        if (suppressSelectedItemSync || SelectionMode == TreeSelectionMode.None)
            return;

        foreach (var n in EnumerateAllNodes())
            n.IsSelected = n.Item == newValue;
    }

    bool selectionModeApplied;

    void OnSelectionModeChanged()
    {
        if (!selectionModeApplied)
        {
            // First application (e.g. SelectionMode="Multiple" in XAML) — there is nothing
            // to drop, and clearing here would wipe a SelectedItems collection the caller
            // set before the mode.
            selectionModeApplied = true;
            Rebuild();
            return;
        }

        // Switching modes drops the current selection: leftover multi-selection would
        // otherwise linger as several highlighted rows in Single mode.
        foreach (var n in EnumerateAllNodes())
            n.IsSelected = false;
        SetValueFromInternal(SelectedItemProperty, null);

        if (SelectionMode == TreeSelectionMode.None)
            SetValueFromInternal(SelectedItemsProperty, null);
        else
            SelectedItems?.Clear(); // keep the caller's bound collection instance

        // Rows are built per-mode (the checkbox column only exists in Multiple).
        Rebuild();
    }

    void SetValueFromInternal(BindableProperty prop, object? value)
    {
        suppressSelectedItemSync = true;
        try { SetValue(prop, value); }
        finally { suppressSelectedItemSync = false; }
    }

    // ------------- Pointer-based drag (Catalyst / AppKit / GTK4 fallback) -------------
    TreeNodeView? pointerDragSource;
    TreeNodeView? pointerDropTarget;
    TreeDropPosition pointerDropZone;

    internal void BeginPointerDrag(TreeNodeView source)
    {
        pointerDragSource = source;
        source.SetDragging(true);
    }

    internal void UpdatePointerDrag(TreeNodeView source, double totalY)
    {
        if (!ReferenceEquals(pointerDragSource, source))
            return;

        source.TranslationY = totalY;

        // Pan deltas are relative to the grab point; approximate the pointer with the
        // row's vertical centre shifted by the pan amount.
        var pointerY = source.Frame.Y + (source.Frame.Height / 2) + totalY;
        var target = FindDropTarget(source, pointerY, out var zone);

        if (!ReferenceEquals(target, pointerDropTarget))
            pointerDropTarget?.HideDropIndicators();

        pointerDropTarget = target;
        pointerDropZone = zone;
        target?.ShowDropIndicator(zone);
    }

    internal void CompletePointerDrag(TreeNodeView source)
    {
        var target = pointerDropTarget;
        var zone = pointerDropZone;
        CancelPointerDrag(source);
        if (target != null)
            HandleDrop(source.Node, target.Node, zone);
    }

    internal void CancelPointerDrag(TreeNodeView source)
    {
        source.SetDragging(false);
        pointerDropTarget?.HideDropIndicators();
        pointerDragSource = null;
        pointerDropTarget = null;
    }

    TreeNodeView? FindDropTarget(TreeNodeView source, double pointerY, out TreeDropPosition zone)
    {
        zone = TreeDropPosition.Below;
        TreeNodeView? first = null, last = null, hit = null;

        foreach (var row in RowViews.Where(r => !ReferenceEquals(r, source) && !IsDescendantOf(source.Node, r.Node)))
        {

            first ??= row;
            last = row;
            var f = row.Frame;
            if (hit == null && pointerY >= f.Y && pointerY < f.Y + f.Height)
                hit = row;
        }

        if (hit != null)
        {
            var f = hit.Frame;
            var rel = (pointerY - f.Y) / Math.Max(f.Height, 1);
            if (HasChildren(hit.Node.Item) && rel is >= 0.25 and <= 0.75)
                zone = TreeDropPosition.Into;
            else
                zone = rel < 0.5 ? TreeDropPosition.Above : TreeDropPosition.Below;
            return hit;
        }

        if (first != null && pointerY < first.Frame.Y)
        {
            zone = TreeDropPosition.Above;
            return first;
        }
        if (last != null && pointerY >= last.Frame.Y + last.Frame.Height)
        {
            zone = TreeDropPosition.Below;
            return last;
        }
        return null;
    }

    static bool IsDescendantOf(TreeNode source, TreeNode candidate)
    {
        var probe = candidate.Parent;
        while (probe != null)
        {
            if (ReferenceEquals(probe, source))
                return true;
            probe = probe.Parent;
        }
        return false;
    }

    // ------------- Drag/drop dispatch -------------
    internal void HandleDrop(TreeNode source, TreeNode target, TreeDropPosition position)
    {
        if (ReferenceEquals(source, target))
            return;
        // Don't allow dropping a node onto its own descendant
        var probe = target;
        while (probe != null)
        {
            if (ReferenceEquals(probe, source))
                return;
            probe = probe.Parent;
        }
        RaiseDropped(new TreeItemDroppedEventArgs(source, target, position));
    }
}
