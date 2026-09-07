using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Shiny.Controls.Diagramming;

namespace Shiny.Blazor.Controls.Diagram;

/// <summary>
/// An interactive diagram: shapes, connections, auto-layout, and a surface you can pan, zoom, select
/// on and edit.
/// </summary>
/// <remarks>
/// <para>
/// The graph, the layouts and the routing all live in <c>Shiny.Controls.Diagram.Shared</c> and are
/// shared verbatim with the MAUI control. This component is the Blazor half: it owns an SVG surface,
/// the pointer plumbing, and turning gestures into <see cref="DiagramEditPlan"/>s. It computes no
/// geometry of its own.
/// </para>
/// <para>
/// Pan and zoom are the SVG <c>viewBox</c> rather than a transform on the contents, so zooming costs
/// nothing at any node count and stroke widths stay honest. The component fills its parent and needs
/// a bounded height, the same as <c>GanttView</c>.
/// </para>
/// </remarks>
public sealed partial class DiagramView : ComponentBase, IAsyncDisposable
{
    readonly string gridId = $"shiny-diagram-grid-{Guid.NewGuid():N}";
    readonly List<INotifyPropertyChanged> observed = [];
    readonly Stack<DiagramEditPlan> undo = new();
    readonly Stack<DiagramEditPlan> redo = new();

    ElementReference surface;
    IJSObjectReference? module;
    DotNetObjectReference<DiagramView>? selfRef;

    INotifyCollectionChanged? observedNodes;
    INotifyCollectionChanged? observedConnections;

    double viewportWidth = 800;
    double viewportHeight = 600;
    double originX;
    double originY;
    double panX;
    double panY;

    bool disposed;
    bool rebuildQueued;
    bool rebuilding;

    // What the last rebuild was run against. A rebuild is only worth doing when one of these has
    // actually moved.
    object? builtNodes;
    object? builtConnections;
    (DiagramLayoutKind, DiagramDirection, DiagramTreeStyle, DiagramConnectionRouter, double, double, double, double)
        builtSettings;

    [Inject]
    IJSRuntime JS { get; set; } = null!;

    /// <summary>The built graph - node boxes, connection routes and validation issues.</summary>
    /// <remarks>
    /// Exposed because <see cref="DiagramModel.Issues"/> is the only thing that reports a dangling
    /// connection or a duplicate id, and nothing else will tell you.
    /// </remarks>
    public DiagramModel Model { get; } = new();

    /// <summary>The shapes to draw.</summary>
    /// <remarks>
    /// An <c>ObservableCollection</c> is watched for adds and removes, and every node is watched for
    /// property changes, so editing the source redraws without anything else being called.
    /// </remarks>
    [Parameter]
    public IEnumerable<DiagramNode>? Nodes { get; set; }

    /// <summary>The lines between them.</summary>
    [Parameter]
    public IEnumerable<DiagramConnection>? Connections { get; set; }

    /// <summary>
    /// Which auto-layout arranges the graph. Defaults to <see cref="DiagramLayoutKind.Tree"/>.
    /// </summary>
    /// <remarks>
    /// Named <c>LayoutKind</c> rather than <c>Layout</c> to match the MAUI control, where a property
    /// called <c>Layout</c> hides <c>VisualElement.Layout(Rect)</c>. Nothing forces the rename here,
    /// but two hosts with the same feature under two names is a trap for anyone writing against both.
    /// </remarks>
    [Parameter]
    public DiagramLayoutKind LayoutKind { get; set; } = DiagramLayoutKind.Tree;

    /// <summary>Which way a hierarchy or layered layout grows.</summary>
    [Parameter]
    public DiagramDirection Direction { get; set; } = DiagramDirection.TopToBottom;

    /// <summary>Whether tree children spread across the axis or stack and indent.</summary>
    [Parameter]
    public DiagramTreeStyle TreeStyle { get; set; } = DiagramTreeStyle.Normal;

    /// <summary>Gap between two nodes side by side within a level.</summary>
    [Parameter]
    public double NodeSpacing { get; set; } = 32;

    /// <summary>Gap between one level of the hierarchy and the next.</summary>
    [Parameter]
    public double LevelSpacing { get; set; } = 56;

    /// <summary>Default node width, for nodes that do not set their own.</summary>
    [Parameter]
    public double NodeWidth { get; set; } = 140;

    /// <summary>Default node height, for nodes that do not set their own.</summary>
    [Parameter]
    public double NodeHeight { get; set; } = 56;

    /// <summary>The router used by connections that do not name one.</summary>
    [Parameter]
    public DiagramConnectionRouter Router { get; set; } = DiagramConnectionRouter.Orthogonal;

    /// <summary>Current zoom. Two-way bindable.</summary>
    [Parameter]
    public double Zoom { get; set; } = 1;

    /// <summary>Raised when the zoom changes, so <c>@bind-Zoom</c> works.</summary>
    [Parameter]
    public EventCallback<double> ZoomChanged { get; set; }

    /// <summary>The smallest zoom a gesture can reach.</summary>
    [Parameter]
    public double MinZoom { get; set; } = 0.2;

    /// <summary>The largest zoom a gesture can reach.</summary>
    [Parameter]
    public double MaxZoom { get; set; } = 4;

    /// <summary>Whether dragging the background pans the surface.</summary>
    [Parameter]
    public bool AllowPan { get; set; } = true;

    /// <summary>Whether Ctrl/Cmd + wheel zooms.</summary>
    [Parameter]
    public bool AllowZoom { get; set; } = true;

    /// <summary>Whether clicking selects nodes and connections.</summary>
    [Parameter]
    public bool AllowSelection { get; set; } = true;

    /// <summary>Whether dragging the background draws a selection marquee.</summary>
    [Parameter]
    public bool AllowMultiSelect { get; set; } = true;

    /// <summary>Whether nodes can be dragged. Dragging a node pins it.</summary>
    [Parameter]
    public bool AllowNodeDrag { get; set; }

    /// <summary>Whether connections can be drawn from a node's ports, and rerouted.</summary>
    [Parameter]
    public bool AllowConnectionEdit { get; set; }

    /// <summary>Whether Delete and Backspace remove the selection.</summary>
    [Parameter]
    public bool AllowDelete { get; set; }

    /// <summary>Whether a dot grid is drawn behind the diagram.</summary>
    [Parameter]
    public bool ShowGrid { get; set; }

    /// <summary>The grid's pitch, and the step a dragged node snaps to when <see cref="SnapToGrid"/> is set.</summary>
    [Parameter]
    public double GridSize { get; set; } = 20;

    /// <summary>Whether a dragged node snaps to <see cref="GridSize"/>.</summary>
    [Parameter]
    public bool SnapToGrid { get; set; }

    /// <summary>
    /// Replaces the drawn shape with arbitrary content.
    /// </summary>
    /// <remarks>
    /// Templated nodes are real DOM in a layer over the canvas, so a node can hold a button, an image
    /// or an input. The cost is one element per node instead of one path, which is why it is off by
    /// default.
    /// </remarks>
    [Parameter]
    public RenderFragment<DiagramNode>? NodeTemplate { get; set; }

    /// <summary>Shown when there is nothing to draw.</summary>
    [Parameter]
    public RenderFragment? EmptyTemplate { get; set; }

    /// <summary>Extra classes for the root element.</summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>Extra inline style for the root element.</summary>
    [Parameter]
    public string? Style { get; set; }

    /// <summary>The accessible name of the surface.</summary>
    [Parameter]
    public string AriaLabel { get; set; } = "Diagram";

    /// <summary>Raised when the selection changes.</summary>
    [Parameter]
    public EventCallback<DiagramSelection> OnSelectionChanged { get; set; }

    /// <summary>Raised when a node is clicked.</summary>
    [Parameter]
    public EventCallback<DiagramNode> OnNodeClick { get; set; }

    /// <summary>Raised when a connection is clicked.</summary>
    [Parameter]
    public EventCallback<DiagramConnection> OnConnectionClick { get; set; }

    /// <summary>
    /// Raised before an edit is applied, with the whole plan and a <c>Cancel</c>.
    /// </summary>
    /// <remarks>
    /// The plan lists everything the gesture is about to do - deleting one node also lists the four
    /// connections that go with it - so a handler vetoes with the full consequence in front of it
    /// rather than after the fact.
    /// </remarks>
    [Parameter]
    public EventCallback<DiagramEditingEventArgs> OnEditing { get; set; }

    /// <summary>Raised after an edit has been applied.</summary>
    [Parameter]
    public EventCallback<DiagramEditPlan> OnEdited { get; set; }

    /// <summary>Raised after every rebuild, which is where <see cref="DiagramModel.Issues"/> is read from.</summary>
    [Parameter]
    public EventCallback<DiagramModel> OnBuilt { get; set; }

    /// <summary>True when there is an edit to undo.</summary>
    public bool CanUndo => this.undo.Count > 0;

    /// <summary>True when there is an undone edit to redo.</summary>
    public bool CanRedo => this.redo.Count > 0;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        this.Model.Layout = this.LayoutKind;
        this.Model.Router = this.Router;
        this.Model.Options.Direction = this.Direction;
        this.Model.Options.TreeStyle = this.TreeStyle;
        this.Model.Options.NodeSpacing = this.NodeSpacing;
        this.Model.Options.LevelSpacing = this.LevelSpacing;
        this.Model.Options.NodeWidth = this.NodeWidth;
        this.Model.Options.NodeHeight = this.NodeHeight;

        var settings = (
            this.LayoutKind, this.Direction, this.TreeStyle, this.Router,
            this.NodeSpacing, this.LevelSpacing, this.NodeWidth, this.NodeHeight
        );

        // Rebuilding unconditionally here is an infinite loop, and a subtle one. Raising OnBuilt
        // invokes an EventCallback, which re-renders the handling component; that re-render passes
        // parameters down again, which lands back here, which rebuilds, which raises OnBuilt. The
        // symptom is a renderer pegged at 100% and a page that never paints - it looks like the
        // browser has died rather than like a feedback loop.
        var changed =
            !ReferenceEquals(this.builtNodes, this.Nodes) ||
            !ReferenceEquals(this.builtConnections, this.Connections) ||
            !this.builtSettings.Equals(settings);

        if (!changed)
            return;

        this.builtSettings = settings;

        if (!ReferenceEquals(this.builtNodes, this.Nodes) ||
            !ReferenceEquals(this.builtConnections, this.Connections))
        {
            this.builtNodes = this.Nodes;
            this.builtConnections = this.Connections;
            this.Observe();
        }

        this.Rebuild();
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        this.selfRef = DotNetObjectReference.Create(this);

        this.module = await this.JS.InvokeAsync<IJSObjectReference>(
            "import",
            "./_content/Shiny.Blazor.Controls.Diagram/diagram.js"
        );

        await this.module.InvokeVoidAsync("attach", this.surface, this.selfRef);
    }

    /// <summary>
    /// Rebuilds the graph: validation, layout and routing.
    /// </summary>
    /// <remarks>
    /// Called for you whenever a parameter or a watched collection changes. Call it by hand only
    /// after mutating a node in a way that does not raise <c>PropertyChanged</c>.
    /// </remarks>
    public void Rebuild()
    {
        // The engine writes X, Y, Depth and the rest back onto the model, and every one of those
        // raises PropertyChanged. Without this flag a twenty-node diagram queues a hundred renders
        // per rebuild - none of which is wrong, all of which is wasted.
        this.rebuilding = true;

        try
        {
            this.Model.SetSource(this.Nodes, this.Connections);
            this.Model.Rebuild();
        }
        finally
        {
            this.rebuilding = false;
        }

        if (this.OnBuilt.HasDelegate)
            _ = this.OnBuilt.InvokeAsync(this.Model);
    }

    /// <summary>Fits the whole diagram in the viewport.</summary>
    public void ZoomToFit()
    {
        var content = this.Model.ContentBounds;

        if (content.IsEmpty || this.viewportWidth <= 0 || this.viewportHeight <= 0)
            return;

        var scale = Math.Min(this.viewportWidth / content.Width, this.viewportHeight / content.Height);
        this.SetZoom(Math.Clamp(scale, this.MinZoom, this.MaxZoom));

        this.panX = content.CenterX - (this.viewportWidth / this.Zoom / 2);
        this.panY = content.CenterY - (this.viewportHeight / this.Zoom / 2);

        this.SafeRender();
    }

    /// <summary>Moves the viewport so a node is in the middle of it.</summary>
    /// <param name="node">The node to centre on.</param>
    public void ScrollTo(DiagramNode node)
    {
        this.panX = node.Bounds.CenterX - (this.viewportWidth / this.Zoom / 2);
        this.panY = node.Bounds.CenterY - (this.viewportHeight / this.Zoom / 2);
        this.SafeRender();
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
        this.SafeRender();
    }

    /// <summary>Redoes the last undone edit.</summary>
    /// <remarks>
    /// Only moves are replayed. An add or a remove was applied by the gesture as it recorded it, and
    /// re-applying one would need the plan to know how to repeat a mutation rather than only how to
    /// invert it - which is a second mechanism for a case that has never come up.
    /// </remarks>
    public void Redo()
    {
        if (this.redo.Count == 0)
            return;

        var plan = this.redo.Pop();
        plan.ApplyMoves();
        this.undo.Push(plan);

        this.Rebuild();
        this.SafeRender();
    }

    /// <summary>The surface's size and screen origin, reported by the resize observer.</summary>
    /// <param name="left">Distance from the viewport's left edge.</param>
    /// <param name="top">Distance from the viewport's top edge.</param>
    /// <param name="width">The surface's width.</param>
    /// <param name="height">The surface's height.</param>
    [JSInvokable]
    public void OnSurfaceMeasured(double left, double top, double width, double height)
    {
        this.originX = left;
        this.originY = top;

        if (Math.Abs(this.viewportWidth - width) < 0.5 && Math.Abs(this.viewportHeight - height) < 0.5)
            return;

        this.viewportWidth = width;
        this.viewportHeight = height;
        this.SafeRender();
    }

    /// <summary>Ctrl/Cmd + wheel zoom, anchored on the pointer.</summary>
    /// <param name="deltaY">The wheel delta.</param>
    /// <param name="x">Pointer position within the surface.</param>
    /// <param name="y">Pointer position within the surface.</param>
    [JSInvokable]
    public void OnWheelZoom(double deltaY, double x, double y)
    {
        if (!this.AllowZoom)
            return;

        // Anchoring on the pointer rather than the centre: the point under the cursor has to stay
        // under it, or zooming in on a detail walks it off screen.
        var before = this.ToDiagram(x, y);
        this.SetZoom(Math.Clamp(this.Zoom * (deltaY < 0 ? 1.1 : 1 / 1.1), this.MinZoom, this.MaxZoom));
        var after = this.ToDiagram(x, y);

        this.panX += before.X - after.X;
        this.panY += before.Y - after.Y;

        this.SafeRender();
    }

    void SetZoom(double zoom)
    {
        if (Math.Abs(this.Zoom - zoom) < 1e-6)
            return;

        this.Zoom = zoom;

        if (this.ZoomChanged.HasDelegate)
            _ = this.ZoomChanged.InvokeAsync(zoom);
    }

    /// <summary>Converts a position within the surface into diagram space.</summary>
    DiagramPoint ToDiagram(double x, double y) =>
        new(this.panX + (x / this.Zoom), this.panY + (y / this.Zoom));

    string ViewBox() => string.Join(
        ' ',
        N(this.panX),
        N(this.panY),
        N(Math.Max(this.viewportWidth / this.Zoom, 1)),
        N(Math.Max(this.viewportHeight / this.Zoom, 1))
    );

    string RootStyle()
    {
        var builder = new StringBuilder();
        builder.Append("--shiny-diagram-zoom:").Append(N(this.Zoom)).Append(';');

        // The caller's style is appended rather than replaced: a splatted style attribute overwrites
        // the component's own, which would silently drop the custom property above.
        if (!string.IsNullOrWhiteSpace(this.Style))
            builder.Append(this.Style);

        return builder.ToString();
    }

    string TemplateLayerStyle() =>
        $"transform:scale({N(this.Zoom)}) translate({N(-this.panX)}px,{N(-this.panY)}px);";

    static string TemplateStyle(DiagramNode node) =>
        $"left:{N(node.X)}px;top:{N(node.Y)}px;width:{N(node.Width)}px;height:{N(node.Height)}px;";

    static string NodeStyle(DiagramNode node)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(node.Fill))
            builder.Append("fill:").Append(node.Fill).Append(';');

        if (!string.IsNullOrWhiteSpace(node.Stroke))
            builder.Append("stroke:").Append(node.Stroke).Append(';');

        if (node.StrokeThickness is { } thickness)
            builder.Append("stroke-width:").Append(N(thickness)).Append(';');

        return builder.ToString();
    }

    static string LabelStyle(DiagramNode node)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(node.TextColor))
            builder.Append("fill:").Append(node.TextColor).Append(';');

        if (node.FontSize is { } size)
            builder.Append("font-size:").Append(N(size)).Append("px;");

        return builder.ToString();
    }

    static string ConnectionStyle(DiagramConnection connection)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(connection.Stroke))
            builder.Append("stroke:").Append(connection.Stroke).Append(';');

        if (connection.StrokeThickness is { } thickness)
            builder.Append("stroke-width:").Append(N(thickness)).Append(';');

        return builder.ToString();
    }

    static string StrokeClass(DiagramConnection connection) => connection.StrokeStyle switch
    {
        DiagramConnectionStroke.Dashed => "is-dashed",
        DiagramConnectionStroke.Dotted => "is-dotted",
        _ => string.Empty
    };

    void Observe()
    {
        this.Unobserve();

        if (this.Nodes is INotifyCollectionChanged nodes)
        {
            this.observedNodes = nodes;
            nodes.CollectionChanged += this.OnSourceChanged;
        }

        if (this.Connections is INotifyCollectionChanged connections)
        {
            this.observedConnections = connections;
            connections.CollectionChanged += this.OnSourceChanged;
        }

        foreach (var node in this.Nodes ?? [])
            this.Watch(node);

        foreach (var connection in this.Connections ?? [])
            this.Watch(connection);
    }

    void Watch(INotifyPropertyChanged item)
    {
        item.PropertyChanged += this.OnItemChanged;
        this.observed.Add(item);
    }

    void Unobserve()
    {
        if (this.observedNodes is not null)
        {
            this.observedNodes.CollectionChanged -= this.OnSourceChanged;
            this.observedNodes = null;
        }

        if (this.observedConnections is not null)
        {
            this.observedConnections.CollectionChanged -= this.OnSourceChanged;
            this.observedConnections = null;
        }

        foreach (var item in this.observed)
            item.PropertyChanged -= this.OnItemChanged;

        this.observed.Clear();
    }

    void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        this.Observe();
        this.Rebuild();
        this.SafeRender();
    }

    void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Everything raised during a rebuild is the engine writing its own results back. The render
        // that follows the rebuild shows all of it.
        if (this.rebuilding)
            return;

        // The engine writes X, Y, Depth, IsHidden and IsReversed back onto the model as part of a
        // rebuild. Rebuilding in response to those would recurse forever.
        switch (e.PropertyName)
        {
            case nameof(DiagramNode.X):
            case nameof(DiagramNode.Y):
            case nameof(DiagramNode.Bounds):
            case nameof(DiagramNode.Depth):
            case nameof(DiagramNode.IsHidden):
            case nameof(DiagramConnection.IsReversed):
                this.SafeRender();
                return;

            case nameof(DiagramNode.IsSelected):
                this.SafeRender();
                return;
        }

        this.QueueRebuild();
    }

    void QueueRebuild()
    {
        // Coalesced: setting five properties on a node in a row is one gesture, and rebuilding the
        // whole layout five times for it is four wasted passes.
        if (this.rebuildQueued || this.disposed)
            return;

        this.rebuildQueued = true;

        _ = this.InvokeAsync(() =>
        {
            this.rebuildQueued = false;

            if (this.disposed)
                return;

            this.Rebuild();
            this.StateHasChanged();
        });
    }

    /// <summary>
    /// Re-renders, from any thread, and never before the component has a render handle.
    /// </summary>
    /// <remarks>
    /// The guard is the point: a public method called from a parent's <c>OnInitialized</c> - or from
    /// a test - otherwise throws "the render handle is not yet assigned", which reads like a bug in
    /// the caller.
    /// </remarks>
    void SafeRender()
    {
        if (this.disposed)
            return;

        try
        {
            _ = this.InvokeAsync(this.StateHasChanged);
        }
        catch (InvalidOperationException)
        {
            // Not rendered yet. The first render will show the current state anyway.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        this.Unobserve();

        if (this.module is not null)
        {
            try
            {
                await this.module.InvokeVoidAsync("detach", this.surface);
                await this.module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The circuit went away before the component did, which is an ordinary end to a
                // Blazor Server session rather than a failure.
            }
            catch (ObjectDisposedException)
            {
            }
        }

        this.selfRef?.Dispose();
    }
}
