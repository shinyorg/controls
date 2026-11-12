using System.Diagnostics;
using Shiny.Controls.AgentUI.Json;

namespace Shiny.Maui.Controls.Agents;

/// <summary>
/// One drawn component: its slot in the parent layout, the children it drew, and the data
/// watchers that keep it current. Rebuilding a node swaps the slot's content and throws away
/// everything under it.
/// </summary>
sealed class A2UINode(A2UISurfaceRenderer owner, A2UINode? parent, string id, A2UIScope scope)
{
    public A2UISurfaceRenderer Owner { get; } = owner;
    public A2UINode? Parent { get; } = parent;
    public string Id { get; } = id;
    public A2UIScope Scope { get; } = scope;

    // the parent positions the slot, so a rebuild never touches the parent's layout
    public ContentView Slot { get; } = new() { Padding = 0 };

    public List<A2UINode> Children { get; } = [];
    public List<A2UIWatcher> Watchers { get; } = [];
    public HashSet<string> Consumed { get; } = new(StringComparer.Ordinal);
    public List<Action> Disposers { get; } = [];
    public List<InputView> Holds { get; } = [];

    public bool InsideCard { get; init; }
    public bool ChildrenInsideCard { get; set; }
    public Action? Trigger { get; init; }
    public bool Touched { get; set; }
    public bool IsDisposed { get; private set; }
    public bool PendingRebuild { get; set; }

    public void AddWatcher(Action<A2UIScope> update)
    {
        var watcher = new A2UIWatcher(this, update);
        this.Watchers.Add(watcher);
        watcher.Run();
    }

    public void Dispose()
    {
        if (this.IsDisposed)
            return;
        this.IsDisposed = true;
        this.Reset();
    }

    // drops everything the last build made, keeping the node itself
    public void Reset()
    {
        foreach (var child in this.Children)
            child.Dispose();
        this.Children.Clear();
        this.Watchers.Clear();
        this.Consumed.Clear();
        this.Holds.Clear();
        foreach (var dispose in this.Disposers)
        {
            try { dispose(); }
            catch (Exception ex) { Debug.WriteLine($"[A2UI] dispose failed: {ex}"); }
        }
        this.Disposers.Clear();
        this.ChildrenInsideCard = false;
    }

    public bool HasFocusedInput()
        => this.Holds.Any(h => h.IsFocused) || this.Children.Any(c => c.HasFocusedInput());

    public IEnumerable<InputView> AllHolds()
        => this.Holds.Concat(this.Children.SelectMany(c => c.AllHolds()));
}

/// <summary>One data binding: an update and the paths it read last time it ran.</summary>
sealed class A2UIWatcher(A2UINode node, Action<A2UIScope> update)
{
    readonly HashSet<string> dependencies = new(StringComparer.Ordinal);

    public A2UINode Node { get; } = node;

    public bool DependsOn(string path) => this.dependencies.Any(d => JsonPointer.Overlaps(d, path));

    public void Run()
    {
        if (this.Node.IsDisposed)
            return;

        this.dependencies.Clear();
        try
        {
            update(this.Node.Scope.Tracking(this.dependencies));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[A2UI] '{this.Node.Id}' failed to update: {ex}");
        }
    }
}

/// <summary>
/// Draws one surface and keeps it current: builds the component tree from <c>root</c>, re-runs only
/// the watchers a data change touches, and rebuilds a component's subtree when the agent re-sends it.
/// </summary>
sealed class A2UISurfaceRenderer : IDisposable
{
    A2UINode? root;

    public A2UISurfaceRenderer(A2UISurfaceView view, A2UISurface surface, A2UIRendererRegistry renderers)
    {
        this.View = view;
        this.Surface = surface;
        this.Renderers = renderers;
        this.Container = new ContentView { Padding = 0 };
        this.Surface.Changed += this.OnSurfaceChanged;
        this.BuildRoot();
    }

    public A2UISurfaceView View { get; }
    public A2UISurface Surface { get; }
    public A2UIRendererRegistry Renderers { get; }

    /// <summary>What the surface view puts on screen for this surface.</summary>
    public ContentView Container { get; }

    public void Dispose()
    {
        this.Surface.Changed -= this.OnSurfaceChanged;
        this.root?.Dispose();
        this.root = null;
        this.Container.Content = null;
    }

    void BuildRoot()
    {
        if (this.root is not null || this.Surface.IsDeleted || this.Surface.Root is null)
            return;

        this.root = new A2UINode(this, null, "root", this.Surface.RootScope);
        this.Build(this.root);
        this.Container.Content = this.root.Slot;
    }

    // ── tree ─────────────────────────────────────────────────────────────────────

    public A2UINode CreateChild(A2UINode parent, A2UIChild child, Action? trigger = null)
    {
        var node = new A2UINode(this, parent, child.Id, child.Scope)
        {
            InsideCard = parent.InsideCard || parent.ChildrenInsideCard,
            Trigger = trigger
        };
        parent.Children.Add(node);
        this.Build(node);
        return node;
    }

    public void RemoveChild(A2UINode parent, A2UINode child)
    {
        child.Dispose();
        parent.Children.Remove(child);
    }

    void Build(A2UINode node)
    {
        var component = this.Surface.GetComponent(node.Id);
        View view;
        if (component is null)
        {
            view = A2UIPlaceholders.Missing();
        }
        else if (!this.Renderers.TryGet(component.Type, out var renderer) || this.Surface.GetDefinition(component) is null)
        {
            view = A2UIPlaceholders.Unsupported(component.Type);
        }
        else
        {
            var context = new A2UIRenderContext(node, component);
            try
            {
                view = renderer(context);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[A2UI] renderer for '{component.Type}' ({component.Id}) threw: {ex}");
                view = A2UIPlaceholders.Unsupported(component.Type + " (failed)");
            }

            if (component["@accessibility"] is not null)
                node.AddWatcher(scope => A2UIAccessibilityMapper.Apply(view, this.Surface.GetAccessibility(component, scope)));
        }
        node.Slot.Content = view;
    }

    void Rebuild(A2UINode node)
    {
        if (node.IsDisposed)
            return;

        // never replace a field the user is typing in; redraw once it loses focus
        if (node.HasFocusedInput())
        {
            if (node.PendingRebuild)
                return;
            node.PendingRebuild = true;
            foreach (var input in node.AllHolds().Where(h => h.IsFocused))
            {
                void OnUnfocused(object? sender, FocusEventArgs e)
                {
                    input.Unfocused -= OnUnfocused;
                    this.View.Dispatcher.Dispatch(() =>
                    {
                        if (node.PendingRebuild)
                            this.Rebuild(node);
                    });
                }
                input.Unfocused += OnUnfocused;
            }
            return;
        }

        node.PendingRebuild = false;
        node.Reset();
        this.Build(node);
    }

    // ── changes ──────────────────────────────────────────────────────────────────

    void OnSurfaceChanged(object? sender, A2UISurfaceChange change)
    {
        switch (change.Kind)
        {
            case A2UISurfaceChangeKind.Components:
                if (this.root is null)
                {
                    this.BuildRoot();
                    return;
                }
                var ids = new HashSet<string>(change.ComponentIds ?? [], StringComparer.Ordinal);
                var targets = new List<A2UINode>();
                CollectRebuilds(this.root, ids, targets);
                foreach (var node in targets)
                    this.Rebuild(node);
                break;

            case A2UISurfaceChangeKind.DataModel when change.Path is { } path:
                this.RunWatchers(w => w.DependsOn(path));
                break;

            case A2UISurfaceChangeKind.FunctionResults:
                this.RunWatchers(_ => true);
                break;

            case A2UISurfaceChangeKind.Deleted:
                this.root?.Dispose();
                this.root = null;
                this.Container.Content = null;
                break;
        }
    }

    static void CollectRebuilds(A2UINode node, HashSet<string> ids, List<A2UINode> targets)
    {
        if (ids.Contains(node.Id) || node.Consumed.Overlaps(ids))
        {
            targets.Add(node);
            return; // the rebuild redraws everything below
        }
        foreach (var child in node.Children)
            CollectRebuilds(child, ids, targets);
    }

    void RunWatchers(Func<A2UIWatcher, bool> predicate)
    {
        if (this.root is null)
            return;

        // collect first: running a watcher can rebuild children (and drop their watchers)
        var due = new List<A2UIWatcher>();
        Collect(this.root);
        foreach (var watcher in due)
            watcher.Run();

        void Collect(A2UINode node)
        {
            foreach (var watcher in node.Watchers)
            {
                if (predicate(watcher))
                    due.Add(watcher);
            }
            foreach (var child in node.Children)
                Collect(child);
        }
    }
}

/// <summary>Maps <c>@accessibility</c> to MAUI semantics. Explicit agent labels win over what a renderer derived.</summary>
static class A2UIAccessibilityMapper
{
    public static void Apply(View view, A2UIAccessibility a11y)
    {
        if (a11y.Label is { } label)
            SemanticProperties.SetDescription(view, label);
        if (a11y.Description is { } description)
            SemanticProperties.SetHint(view, description);
        AutomationProperties.SetExcludedWithChildren(view, a11y.Hidden ? true : null);
    }
}
