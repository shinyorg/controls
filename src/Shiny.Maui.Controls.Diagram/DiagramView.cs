using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Maui.Layouts;
using Shiny.Controls.Diagramming;
using Shiny.Maui.Controls.Diagram.Internal;

namespace Shiny.Maui.Controls.Diagram;

/// <summary>
/// An interactive diagram: shapes, connections, auto-layout, and a surface you can pan, zoom, select
/// on and edit.
/// </summary>
/// <remarks>
/// <para>
/// The graph, the layouts and the routing all live in <c>Shiny.Controls.Diagram.Shared</c> and are
/// shared verbatim with the Blazor control. This class is the MAUI half: it owns the visual tree, the
/// pan and zoom, and turning gestures into <see cref="DiagramEditPlan"/>s. It computes no geometry of
/// its own.
/// </para>
/// <para>
/// The whole diagram is painted into one <see cref="GraphicsView"/> rather than composed from a view
/// per node, for the reason a virtualized list exists: two hundred nodes is two hundred native views,
/// each with a handler and a layout pass. Setting <see cref="NodeTemplate"/> opts into real views, in
/// an <see cref="AbsoluteLayout"/> over the canvas.
/// </para>
/// </remarks>
public partial class DiagramView : ContentView, IDisposable
{
    readonly Grid root;
    readonly GraphicsView canvas;
    readonly AbsoluteLayout templateLayer;
    readonly DiagramDrawable drawable;

    readonly Dictionary<DiagramNode, View> templateViews = [];
    readonly List<INotifyPropertyChanged> observedItems = [];
    readonly Stack<DiagramEditPlan> undo = new();
    readonly Stack<DiagramEditPlan> redo = new();

    INotifyCollectionChanged? observedNodes;
    INotifyCollectionChanged? observedConnections;

    bool disposed;
    bool rebuilding;

    /// <summary>Creates the control.</summary>
    public DiagramView()
    {
        this.root = new Grid();

        this.Palette = new DiagramPalette(this.root);
        this.Palette.Changed += (_, _) => this.Repaint();

        this.drawable = new DiagramDrawable(this);
        this.canvas = new GraphicsView { Drawable = this.drawable };

        // Sits over the canvas and is only populated when a NodeTemplate is set. It is always in the
        // tree rather than added on demand, because on the AppKit head a child added after the page
        // has been laid out is never realized and simply never paints.
        this.templateLayer = new AbsoluteLayout { InputTransparent = true };

        this.root.Add(this.canvas);
        this.root.Add(this.templateLayer);

        this.Content = this.root;

        this.SetUpGestures();
        this.SizeChanged += this.OnSizeChanged;
    }

    /// <summary>The built graph - node boxes, connection routes and validation issues.</summary>
    /// <remarks>
    /// Exposed because <see cref="DiagramModel.Issues"/> is the only thing that reports a dangling
    /// connection or a duplicate id, and nothing else will tell you.
    /// </remarks>
    public DiagramModel Model { get; } = new();

    /// <summary>How far the viewport has been panned, in diagram units.</summary>
    public double PanX { get; private set; }

    /// <summary>How far the viewport has been panned, in diagram units.</summary>
    public double PanY { get; private set; }

    /// <summary>True when there is an edit to undo.</summary>
    public bool CanUndo => this.undo.Count > 0;

    /// <summary>True when there is an undone edit to redo.</summary>
    public bool CanRedo => this.redo.Count > 0;

    /// <summary>Raised when the selection changes.</summary>
    public event EventHandler<DiagramSelectionEventArgs>? SelectionChanged;

    /// <summary>Raised when a node is tapped.</summary>
    public event EventHandler<DiagramNodeEventArgs>? NodeTapped;

    /// <summary>Raised when a connection is tapped.</summary>
    public event EventHandler<DiagramConnectionEventArgs>? ConnectionTapped;

    /// <summary>
    /// Raised before an edit is applied, with the whole plan and a <c>Cancel</c>.
    /// </summary>
    /// <remarks>
    /// The plan lists everything the gesture is about to do - deleting one node also lists the
    /// connections that go with it - so a handler vetoes with the full consequence in front of it
    /// rather than after the fact.
    /// </remarks>
    public event EventHandler<DiagramEditingEventArgs>? Editing;

    /// <summary>Raised after an edit has been applied.</summary>
    public event EventHandler<DiagramEditedEventArgs>? Edited;

    /// <summary>Raised after every rebuild, which is where <see cref="DiagramModel.Issues"/> is read from.</summary>
    public event EventHandler<DiagramBuiltEventArgs>? Built;

    internal DiagramPalette Palette { get; }

    /// <summary>
    /// Rebuilds the graph: validation, layout and routing, then a repaint.
    /// </summary>
    /// <remarks>
    /// Called for you whenever a property or a watched collection changes. Call it by hand only after
    /// mutating a node in a way that does not raise <c>PropertyChanged</c>.
    /// </remarks>
    public void Rebuild()
    {
        if (this.disposed)
            return;

        this.Model.Layout = this.LayoutKind;
        this.Model.Router = this.Router;
        this.Model.Options.Direction = this.Direction;
        this.Model.Options.TreeStyle = this.TreeStyle;
        this.Model.Options.NodeSpacing = this.NodeSpacing;
        this.Model.Options.LevelSpacing = this.LevelSpacing;
        this.Model.Options.NodeWidth = this.NodeWidth;
        this.Model.Options.NodeHeight = this.NodeHeight;
        this.Model.Options.FontSize = this.NodeFontSize;

        // The engine writes X, Y, Depth and the rest back onto the model, and every one of those
        // raises PropertyChanged. Without this flag a twenty-node diagram queues a rebuild per write.
        this.rebuilding = true;

        try
        {
            this.Model.SetSource(Cast<DiagramNode>(this.Nodes), Cast<DiagramConnection>(this.Connections));
            this.Model.Rebuild();
        }
        finally
        {
            this.rebuilding = false;
        }

        this.RebuildTemplateLayer();
        this.Repaint();

        this.Built?.Invoke(this, new DiagramBuiltEventArgs(this.Model));
    }

    /// <summary>Queues a repaint of the canvas.</summary>
    public void Repaint()
    {
        if (this.disposed)
            return;

        this.PositionTemplateViews();
        this.canvas.Invalidate();
    }

    /// <summary>Fits the whole diagram in the viewport.</summary>
    public void ZoomToFit()
    {
        var content = this.Model.ContentBounds;

        if (content.IsEmpty || this.canvas.Width <= 0 || this.canvas.Height <= 0)
            return;

        var scale = Math.Min(this.canvas.Width / content.Width, this.canvas.Height / content.Height);
        this.Zoom = Math.Clamp(scale, this.MinZoom, this.MaxZoom);

        this.PanX = content.CenterX - (this.canvas.Width / this.Zoom / 2);
        this.PanY = content.CenterY - (this.canvas.Height / this.Zoom / 2);

        this.Repaint();
    }

    /// <summary>Moves the viewport so a node is in the middle of it.</summary>
    /// <param name="node">The node to centre on.</param>
    public void ScrollTo(DiagramNode node)
    {
        if (this.canvas.Width <= 0)
            return;

        this.PanX = node.Bounds.CenterX - (this.canvas.Width / this.Zoom / 2);
        this.PanY = node.Bounds.CenterY - (this.canvas.Height / this.Zoom / 2);
        this.Repaint();
    }

    /// <summary>Undoes the last edit.</summary>
    public void Undo()
    {
        if (this.undo.Count == 0)
            return;

        var plan = this.undo.Pop();
        plan.Revert();
        this.redo.Push(plan);

        this.Rebuild();
    }

    /// <summary>
    /// Redoes the last undone edit.
    /// </summary>
    /// <remarks>
    /// Only moves are replayed. An add or a remove was applied by the gesture as it recorded it, and
    /// re-applying one would need the plan to know how to repeat a mutation rather than only how to
    /// invert it - a second mechanism for a case that has never come up.
    /// </remarks>
    public void Redo()
    {
        if (this.redo.Count == 0)
            return;

        var plan = this.redo.Pop();
        plan.ApplyMoves();
        this.undo.Push(plan);

        this.Rebuild();
    }

    static IEnumerable<T>? Cast<T>(IEnumerable? source)
    {
        if (source is null)
            return null;

        if (source is IEnumerable<T> typed)
            return typed;

        var list = new List<T>();

        foreach (var item in source)
        {
            if (item is T value)
                list.Add(value);
        }

        return list;
    }

    void OnSizeChanged(object? sender, EventArgs e) => this.Repaint();

    void OnSourceReplaced(object? oldValue, object? newValue)
    {
        this.Unobserve();
        this.Observe();
        this.Rebuild();
    }

    void Observe()
    {
        if (this.Nodes is INotifyCollectionChanged nodes)
        {
            this.observedNodes = nodes;
            nodes.CollectionChanged += this.OnSourceCollectionChanged;
        }

        if (this.Connections is INotifyCollectionChanged connections)
        {
            this.observedConnections = connections;
            connections.CollectionChanged += this.OnSourceCollectionChanged;
        }

        foreach (var item in Cast<DiagramNode>(this.Nodes) ?? [])
            this.Watch(item);

        foreach (var item in Cast<DiagramConnection>(this.Connections) ?? [])
            this.Watch(item);
    }

    void Watch(INotifyPropertyChanged item)
    {
        item.PropertyChanged += this.OnItemChanged;
        this.observedItems.Add(item);
    }

    void Unobserve()
    {
        if (this.observedNodes is not null)
        {
            this.observedNodes.CollectionChanged -= this.OnSourceCollectionChanged;
            this.observedNodes = null;
        }

        if (this.observedConnections is not null)
        {
            this.observedConnections.CollectionChanged -= this.OnSourceCollectionChanged;
            this.observedConnections = null;
        }

        foreach (var item in this.observedItems)
            item.PropertyChanged -= this.OnItemChanged;

        this.observedItems.Clear();
    }

    void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        this.Unobserve();
        this.Observe();
        this.Rebuild();
    }

    void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Everything raised during a rebuild is the engine writing its own results back; the repaint
        // that follows shows all of it.
        if (this.rebuilding)
            return;

        switch (e.PropertyName)
        {
            // Position and selection change the picture but not the layout, so they repaint without
            // paying for a rebuild - which is what makes a drag smooth.
            case nameof(DiagramNode.X):
            case nameof(DiagramNode.Y):
            case nameof(DiagramNode.Bounds):
            case nameof(DiagramNode.Depth):
            case nameof(DiagramNode.IsSelected):
            case nameof(DiagramNode.IsHidden):
            case nameof(DiagramConnection.IsReversed):
                this.Repaint();
                return;
        }

        this.Rebuild();
    }

    /// <summary>
    /// Creates or discards the real views a <see cref="NodeTemplate"/> asks for.
    /// </summary>
    /// <remarks>
    /// Views are cached per node and reused across rebuilds. Recreating them would rebuild every
    /// native view under the layer on each layout pass, and would take focus off anything the user
    /// was typing into inside a node.
    /// </remarks>
    void RebuildTemplateLayer()
    {
        var template = this.NodeTemplate;
        var selector = this.NodeTemplateSelector;

        if (template is null && selector is null)
        {
            if (this.templateViews.Count > 0)
            {
                this.templateLayer.Clear();
                this.templateViews.Clear();
            }

            return;
        }

        var live = new HashSet<DiagramNode>();

        foreach (var node in this.Model.VisibleNodes)
        {
            live.Add(node);

            if (this.templateViews.ContainsKey(node))
                continue;

            var chosen = selector?.SelectTemplate(node, this) ?? template;

            if (chosen?.CreateContent() is not View created)
                continue;

            created.BindingContext = node;
            this.templateViews[node] = created;
            this.templateLayer.Add(created);
        }

        foreach (var node in this.templateViews.Keys.ToList())
        {
            if (live.Contains(node))
                continue;

            this.templateLayer.Remove(this.templateViews[node]);
            this.templateViews.Remove(node);
        }

        // The layer only takes input when there is something in it to take input; left hit-testable
        // while empty it would swallow every gesture meant for the canvas underneath.
        this.templateLayer.InputTransparent = this.templateViews.Count == 0;
    }

    void PositionTemplateViews()
    {
        if (this.templateViews.Count == 0)
            return;

        foreach (var (node, view) in this.templateViews)
        {
            var bounds = node.Bounds;

            AbsoluteLayout.SetLayoutBounds(view, new Rect(
                (bounds.X - this.PanX) * this.Zoom,
                (bounds.Y - this.PanY) * this.Zoom,
                bounds.Width * this.Zoom,
                bounds.Height * this.Zoom
            ));

            AbsoluteLayout.SetLayoutFlags(view, AbsoluteLayoutFlags.None);
            view.IsVisible = node.IsVisible && !node.IsHidden;
        }
    }

    void OnSelectedNodeSet(DiagramNode? node)
    {
        if (node is null)
        {
            if (this.Selection.Nodes.Count > 0)
                this.Select([], [], false);

            return;
        }

        if (!node.IsSelected)
            this.Select([node], [], false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Unhooks everything the control subscribed to.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (this.disposed || !disposing)
            return;

        this.disposed = true;

        this.SizeChanged -= this.OnSizeChanged;
        this.Unobserve();
        this.TearDownGestures();
        this.Palette.Dispose();
    }
}
