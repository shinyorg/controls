namespace Shiny.Controls.Diagramming;

/// <summary>
/// The built diagram: a validated graph, laid out, with every connection routed.
/// </summary>
/// <remarks>
/// <para>
/// This is what both controls drive. Hand it nodes and connections, call <see cref="Rebuild"/>, and
/// read back node boxes and connection routes - the hosts add a canvas, gestures and a theme, and
/// nothing else. It is perfectly usable with no UI at all, which is what makes the layouts and the
/// routing testable without a rendering surface.
/// </para>
/// <para>
/// Validation reports, it does not throw. See <see cref="Issues"/>.
/// </para>
/// </remarks>
public sealed class DiagramModel
{
    readonly List<DiagramNode> nodes = [];
    readonly List<DiagramConnection> connections = [];
    readonly List<DiagramNode> visibleNodes = [];
    readonly List<DiagramConnection> visibleConnections = [];
    readonly List<DiagramValidationIssue> issues = [];
    readonly List<DiagramConnection> implicitConnections = [];

    /// <summary>Every node handed to the model, in supply order.</summary>
    public IReadOnlyList<DiagramNode> Nodes => this.nodes;

    /// <summary>Every connection handed to the model, in supply order.</summary>
    public IReadOnlyList<DiagramConnection> Connections => this.connections;

    /// <summary>The nodes actually drawn - visible, and not behind a collapsed ancestor.</summary>
    public IReadOnlyList<DiagramNode> VisibleNodes => this.visibleNodes;

    /// <summary>The connections actually drawn - visible, with both ends drawn.</summary>
    public IReadOnlyList<DiagramConnection> VisibleConnections => this.visibleConnections;

    /// <summary>
    /// What was wrong with the graph. Empty when nothing was.
    /// </summary>
    /// <remarks>
    /// Rebuilt from scratch on every <see cref="Rebuild"/>, so this always describes the current
    /// picture rather than accumulating.
    /// </remarks>
    public IReadOnlyList<DiagramValidationIssue> Issues => this.issues;

    /// <summary>The box every drawn node and route fits inside, including the margin.</summary>
    public DiagramRect ContentBounds { get; private set; }

    /// <summary>Which layout arranges the graph.</summary>
    public DiagramLayoutKind Layout { get; set; } = DiagramLayoutKind.Tree;

    /// <summary>Spacing, direction and default sizes.</summary>
    public DiagramLayoutOptions Options { get; set; } = new();

    /// <summary>The router used by connections that do not name one.</summary>
    public DiagramConnectionRouter Router { get; set; } = DiagramConnectionRouter.Orthogonal;

    /// <summary>
    /// Whether a parent/child link with no connection of its own is drawn anyway. On by default.
    /// </summary>
    /// <remarks>
    /// An org chart handed over as nested nodes has a hierarchy and no connections, and without this
    /// it would draw as a tidy grid of boxes joined by nothing - which looks like the control failed
    /// rather than like a deliberate absence. Declaring an edge between the same two nodes replaces
    /// the implicit one, so a consumer who wants a label or a different cap just says so.
    /// </remarks>
    public bool ShowHierarchyConnections { get; set; } = true;

    /// <summary>Replaces the graph. Does not rebuild - call <see cref="Rebuild"/> when the source has settled.</summary>
    /// <param name="sourceNodes">The nodes to draw.</param>
    /// <param name="sourceConnections">The connections between them.</param>
    public void SetSource(IEnumerable<DiagramNode>? sourceNodes, IEnumerable<DiagramConnection>? sourceConnections)
    {
        this.nodes.Clear();
        this.connections.Clear();

        if (sourceNodes is not null)
            this.nodes.AddRange(sourceNodes);

        if (sourceConnections is not null)
            this.connections.AddRange(sourceConnections);
    }

    /// <summary>
    /// Validates, resolves the hierarchy, runs the layout and routes every connection.
    /// </summary>
    /// <remarks>
    /// Cheap enough to call on any change. The expensive pass is the layout, and
    /// <see cref="DiagramLayoutKind.None"/> skips it entirely - which is what a diagram being dragged
    /// around by hand should be using.
    /// </remarks>
    public void Rebuild()
    {
        this.issues.Clear();

        var byId = this.Index();
        this.ResolveHierarchy(byId);
        this.ResolveVisibility(byId);
        this.RunLayout();
        this.RouteConnections(byId);
        this.MeasureContent();
    }

    /// <summary>Indexes the nodes by id, reporting any duplicates rather than letting the later one win silently.</summary>
    Dictionary<string, DiagramNode> Index()
    {
        var byId = new Dictionary<string, DiagramNode>(this.nodes.Count, StringComparer.Ordinal);

        foreach (var node in this.nodes)
        {
            if (byId.TryAdd(node.Id, node))
                continue;

            this.issues.Add(new DiagramValidationIssue(
                DiagramIssueKind.DuplicateId,
                node.Id,
                $"More than one node uses the id '{node.Id}'. Only the first is drawn; connections naming it resolve to that one."
            ));
        }

        var connectionIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var connection in this.connections)
        {
            if (connectionIds.Add(connection.Id))
                continue;

            this.issues.Add(new DiagramValidationIssue(
                DiagramIssueKind.DuplicateId,
                connection.Id,
                $"More than one connection uses the id '{connection.Id}'."
            ));
        }

        return byId;
    }

    /// <summary>
    /// Turns <see cref="DiagramNode.ParentId"/> values into real <see cref="DiagramNode.Parent"/>
    /// links, so a flat source and a nested one end up in the same shape.
    /// </summary>
    void ResolveHierarchy(Dictionary<string, DiagramNode> byId)
    {
        foreach (var node in this.nodes)
        {
            if (node.ParentId is null)
            {
                // A node nested inside another already has its parent; only a genuinely rootless node
                // gets its link cleared.
                if (node.Parent is not null && !this.nodes.Contains(node.Parent))
                    node.Parent = null;

                continue;
            }

            if (node.Parent is not null && string.Equals(node.Parent.Id, node.ParentId, StringComparison.Ordinal))
                continue;

            if (!byId.TryGetValue(node.ParentId, out var parent) || ReferenceEquals(parent, node))
                continue;

            node.Parent = parent;

            if (!parent.Children.Contains(node))
                parent.Children.Add(node);
        }

        // Depth doubles as the cycle check: a chain that never terminates within the bound is one.
        foreach (var node in this.nodes)
        {
            var depth = 0;
            var current = node.Parent;
            var cyclic = false;

            while (current is not null)
            {
                if (++depth > 512)
                {
                    cyclic = true;
                    break;
                }

                current = current.Parent;
            }

            if (!cyclic)
            {
                node.Depth = depth;
                continue;
            }

            this.issues.Add(new DiagramValidationIssue(
                DiagramIssueKind.ParentCycle,
                node.Id,
                $"Node '{node.Id}' has a parent chain that loops back to itself. It is drawn as a root."
            ));

            node.Parent?.Children.Remove(node);
            node.Parent = null;
            node.ParentId = null;
            node.Depth = 0;
        }
    }

    /// <summary>
    /// Works out what is actually drawn: hidden nodes, nodes behind a collapsed ancestor, and the
    /// connections whose ends went with them.
    /// </summary>
    void ResolveVisibility(Dictionary<string, DiagramNode> byId)
    {
        this.visibleNodes.Clear();
        this.visibleConnections.Clear();

        foreach (var node in this.nodes)
        {
            var hidden = false;
            var ancestor = node.Parent;
            var steps = 0;

            while (ancestor is not null && steps++ < 512)
            {
                if (!ancestor.IsExpanded || !ancestor.IsVisible)
                {
                    hidden = true;
                    break;
                }

                ancestor = ancestor.Parent;
            }

            node.IsHidden = hidden;

            if (!hidden && node.IsVisible && ReferenceEquals(byId.GetValueOrDefault(node.Id), node))
                this.visibleNodes.Add(node);
        }

        this.AddHierarchyConnections(byId);

        foreach (var connection in this.connections.Concat(this.implicitConnections))
        {
            var hasSource = byId.TryGetValue(connection.SourceId, out var source);
            var hasTarget = byId.TryGetValue(connection.TargetId, out var target);

            if (!hasSource || !hasTarget)
            {
                connection.IsHidden = true;

                var missing = !hasSource ? connection.SourceId : connection.TargetId;
                this.issues.Add(new DiagramValidationIssue(
                    DiagramIssueKind.DanglingConnection,
                    connection.Id,
                    $"Connection '{connection.Id}' names node '{missing}', which is not in the diagram. It is not drawn."
                ));
                continue;
            }

            if (string.Equals(connection.SourceId, connection.TargetId, StringComparison.Ordinal))
            {
                this.issues.Add(new DiagramValidationIssue(
                    DiagramIssueKind.SelfConnection,
                    connection.Id,
                    $"Connection '{connection.Id}' joins node '{connection.SourceId}' to itself. It is drawn as a loop."
                ));
            }

            var drawn = source!.IsVisible && !source.IsHidden && target!.IsVisible && !target.IsHidden;
            connection.IsHidden = !drawn;

            if (drawn && connection.IsVisible)
                this.visibleConnections.Add(connection);
        }
    }

    /// <summary>
    /// Creates a line for every parent/child link the consumer did not declare one for.
    /// </summary>
    /// <remarks>
    /// The instances are cached by node pair and reused across rebuilds. Recreating them would throw
    /// away the route computed last time on every frame of a drag, and would hand a host a different
    /// object identity each render - which is enough to defeat any keyed rendering over them.
    /// </remarks>
    void AddHierarchyConnections(Dictionary<string, DiagramNode> byId)
    {
        this.implicitConnections.Clear();

        if (!this.ShowHierarchyConnections)
            return;

        var declared = new HashSet<(string, string)>();

        foreach (var connection in this.connections)
            declared.Add((connection.SourceId, connection.TargetId));

        foreach (var node in this.nodes)
        {
            if (node.Parent is null)
                continue;

            var pair = (node.Parent.Id, node.Id);

            if (declared.Contains(pair) || !byId.ContainsKey(node.Parent.Id) || !byId.ContainsKey(node.Id))
                continue;

            if (!this.hierarchyCache.TryGetValue(pair, out var connection))
            {
                connection = new DiagramConnection(node.Parent.Id, node.Id)
                {
                    Id = $"hierarchy {node.Parent.Id} {node.Id}",
                    IsImplicit = true,
                    CanEdit = false
                };

                this.hierarchyCache[pair] = connection;
            }

            this.implicitConnections.Add(connection);
        }
    }

    readonly Dictionary<(string, string), DiagramConnection> hierarchyCache = [];

    void RunLayout()
    {
        // Bends belong to the layout that produced them. Leaving a previous run's chain in place is
        // how a diagram switched from Layered to Tree ends up with arrows detouring around nodes that
        // are no longer in the way.
        foreach (var connection in this.connections.Concat(this.implicitConnections))
            connection.LayoutBends = null;

        if (this.Layout == DiagramLayoutKind.None)
        {
            var context = new DiagramLayoutContext(this.visibleNodes, this.visibleConnections, this.Options);
            context.EnsureSizes();
            return;
        }

        IDiagramLayout layout = this.Layout switch
        {
            DiagramLayoutKind.Layered => new LayeredLayout(),
            DiagramLayoutKind.MindMap => new MindMapLayout(),
            DiagramLayoutKind.Radial => new RadialLayout(),
            DiagramLayoutKind.ForceDirected => new ForceDirectedLayout(),
            _ => new TreeLayout()
        };

        layout.Arrange(new DiagramLayoutContext(this.visibleNodes, this.visibleConnections, this.Options));
    }

    void RouteConnections(Dictionary<string, DiagramNode> byId)
    {
        var axis = this.RoutingAxis();

        foreach (var connection in this.visibleConnections)
        {
            if (!byId.TryGetValue(connection.SourceId, out var source) ||
                !byId.TryGetValue(connection.TargetId, out var target))
            {
                continue;
            }

            ConnectionRouter.Route(connection, source, target, this.Router, axis);
        }
    }

    /// <summary>
    /// The axis auto ports snap to, taken from the layout.
    /// </summary>
    /// <remarks>
    /// A layout that flows one way wants its lines on that axis - an org chart's arrows belong in the
    /// tops of its boxes, not their sides. The layouts with no single flow direction get
    /// <see cref="ConnectionRouter.Axis.Free"/> and fall back to the nearest edge, because on a radial
    /// wheel or a force-directed cloud there is no axis to prefer and forcing one would make every
    /// line leave in a direction unrelated to where it is going.
    /// </remarks>
    ConnectionRouter.Axis RoutingAxis() => this.Layout switch
    {
        DiagramLayoutKind.Tree or DiagramLayoutKind.Layered =>
            this.Options.IsVertical ? ConnectionRouter.Axis.Vertical : ConnectionRouter.Axis.Horizontal,

        DiagramLayoutKind.MindMap => ConnectionRouter.Axis.Horizontal,

        _ => ConnectionRouter.Axis.Free
    };

    void MeasureContent()
    {
        var bounds = DiagramRect.Empty;
        var first = true;

        foreach (var node in this.visibleNodes)
        {
            bounds = first ? node.Bounds : bounds.Union(node.Bounds);
            first = false;
        }

        // Routes are included because an orthogonal elbow, and a self-loop especially, can reach
        // outside every node box - and a content size that excluded them would clip the drawing at
        // exactly the edge the user needs to scroll to.
        foreach (var connection in this.visibleConnections)
        {
            foreach (var point in connection.Points)
            {
                var dot = new DiagramRect(point.X, point.Y, 0, 0);
                bounds = first ? dot : bounds.Union(dot);
                first = false;
            }
        }

        this.ContentBounds = first
            ? DiagramRect.Empty
            : new DiagramRect(
                0,
                0,
                bounds.Right + this.Options.Margin,
                bounds.Bottom + this.Options.Margin
            );
    }
}
