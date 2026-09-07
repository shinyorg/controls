using Shiny.Controls.Diagramming;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Diagram;

public partial class DiagramView
{
    enum Gesture
    {
        None,
        Pan,
        Marquee,
        DragNodes,
        DrawLink
    }

    Gesture gesture;
    DiagramPoint gestureStart;
    DiagramPoint gestureLast;

    DiagramNode? linkSource;
    DiagramPort linkSourcePort;
    DiagramNode? linkTarget;

    readonly List<(DiagramNode Node, double X, double Y)> dragOrigins = [];

    PinchGestureRecognizer? pinch;
    DragTouchHook? touchHook;
    double pinchStartZoom = 1;
    bool movedDuringGesture;

    /// <summary>The nodes and connections currently selected.</summary>
    public DiagramSelection Selection { get; private set; } = DiagramSelection.Empty;

    /// <summary>The marquee currently being dragged, in diagram space. Null when there is none.</summary>
    internal DiagramRect? Marquee { get; private set; }

    /// <summary>The connection currently being drawn, in diagram space. Null when there is none.</summary>
    internal (DiagramPoint From, DiagramPoint To)? PendingLink { get; private set; }

    void SetUpGestures()
    {
        // GraphicsView's interaction events rather than a PanGestureRecognizer: they carry the touch
        // point, which a pan does not - it reports only how far the finger has travelled, and a
        // diagram has to know what was under it when it went down.
        this.canvas.StartInteraction += this.OnStartInteraction;
        this.canvas.DragInteraction += this.OnDragInteraction;
        this.canvas.EndInteraction += this.OnEndInteraction;
        this.canvas.CancelInteraction += this.OnCancelInteraction;

        // Only used for its scroller lock. A diagram dropped inside a ScrollView otherwise loses every
        // drag to the scroller the moment the finger crosses the touch slop.
        this.touchHook = new DragTouchHook(this.canvas);

        this.UpdateGestures();
    }

    void UpdateGestures()
    {
        if (this.pinch is not null)
        {
            this.pinch.PinchUpdated -= this.OnPinch;
            this.canvas.GestureRecognizers.Remove(this.pinch);
            this.pinch = null;
        }

        if (!this.AllowZoom)
            return;

        this.pinch = new PinchGestureRecognizer();
        this.pinch.PinchUpdated += this.OnPinch;
        this.canvas.GestureRecognizers.Add(this.pinch);
    }

    void TearDownGestures()
    {
        this.canvas.StartInteraction -= this.OnStartInteraction;
        this.canvas.DragInteraction -= this.OnDragInteraction;
        this.canvas.EndInteraction -= this.OnEndInteraction;
        this.canvas.CancelInteraction -= this.OnCancelInteraction;

        if (this.pinch is not null)
        {
            this.pinch.PinchUpdated -= this.OnPinch;
            this.canvas.GestureRecognizers.Remove(this.pinch);
            this.pinch = null;
        }

        this.touchHook?.LockScroller(false);
        this.touchHook = null;
    }

    /// <summary>Converts a touch point on the canvas into diagram space.</summary>
    DiagramPoint At(PointF point) =>
        new(this.PanX + (point.X / this.Zoom), this.PanY + (point.Y / this.Zoom));

    void OnStartInteraction(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0)
            return;

        var point = this.At(e.Touches[0]);

        this.gestureStart = point;
        this.gestureLast = point;
        this.movedDuringGesture = false;

        // Ports first: a connector handle sits on the node's outline, so testing the node first would
        // make every handle unreachable.
        if (this.AllowConnectionEdit && this.PortUnder(point) is var (portNode, port))
        {
            this.gesture = Gesture.DrawLink;
            this.linkSource = portNode;
            this.linkSourcePort = port;
            this.touchHook?.LockScroller(true);
            return;
        }

        var node = DiagramHitTester.NodeAt(this.Model.VisibleNodes, point);

        if (node is not null)
        {
            this.PressNode(node);
            return;
        }

        var connection = DiagramHitTester.ConnectionAt(this.Model.VisibleConnections, point, this.HitTolerance);

        if (connection is not null)
        {
            if (this.AllowSelection)
                this.Select([], [connection], false);

            this.ConnectionTapped?.Invoke(this, new DiagramConnectionEventArgs(connection));
            return;
        }

        if (this.AllowSelection)
            this.Select([], [], false);

        // A finger has no shift key, so panning and marquee-selecting are the same gesture on the
        // background and only one of them can have it. Panning wins when it is on, because getting
        // around a diagram is the more common need than selecting several nodes at once.
        if (this.AllowPan)
        {
            this.gesture = Gesture.Pan;
            this.touchHook?.LockScroller(true);
        }
        else if (this.AllowMultiSelect)
        {
            this.gesture = Gesture.Marquee;
            this.Marquee = new DiagramRect(point.X, point.Y, 0, 0);
            this.touchHook?.LockScroller(true);
        }
    }

    void PressNode(DiagramNode node)
    {
        if (this.AllowSelection && !node.IsSelected)
            this.Select([node], [], false);

        this.NodeTapped?.Invoke(this, new DiagramNodeEventArgs(node));

        if (!this.AllowNodeDrag || !node.CanMove)
            return;

        this.gesture = Gesture.DragNodes;
        this.dragOrigins.Clear();

        // The whole selection moves together, which is what makes a marquee worth having. A node that
        // is not selected drags alone.
        var group = node.IsSelected && this.Selection.Nodes.Count > 0
            ? this.Selection.Nodes
            : [node];

        foreach (var candidate in group)
        {
            if (candidate.CanMove)
                this.dragOrigins.Add((candidate, candidate.X, candidate.Y));
        }

        this.touchHook?.LockScroller(true);
    }

    void OnDragInteraction(object? sender, TouchEventArgs e)
    {
        if (this.gesture == Gesture.None || e.Touches.Length == 0)
            return;

        var point = this.At(e.Touches[0]);
        this.movedDuringGesture = true;

        switch (this.gesture)
        {
            case Gesture.Pan:
                this.PanX -= point.X - this.gestureLast.X;
                this.PanY -= point.Y - this.gestureLast.Y;

                // Recomputed rather than carried: the pan changed what this screen position maps to,
                // so the previous value is stale by exactly the amount just applied.
                this.gestureLast = this.At(e.Touches[0]);
                break;

            case Gesture.Marquee:
                this.Marquee = DiagramRect.FromCorners(this.gestureStart, point);
                break;

            case Gesture.DragNodes:
                this.MoveDragged(point);
                break;

            case Gesture.DrawLink:
                this.TrackPendingLink(point);
                break;
        }

        this.Repaint();
    }

    void MoveDragged(DiagramPoint point)
    {
        var dx = point.X - this.gestureStart.X;
        var dy = point.Y - this.gestureStart.Y;

        foreach (var (node, originX, originY) in this.dragOrigins)
        {
            var x = originX + dx;
            var y = originY + dy;

            if (this.SnapToGrid && this.GridSize > 0)
            {
                x = Math.Round(x / this.GridSize) * this.GridSize;
                y = Math.Round(y / this.GridSize) * this.GridSize;
            }

            // Moved live so the drag is visible; the plan built on release still carries the original
            // positions, so a veto or an undo puts them back.
            node.X = x;
            node.Y = y;
        }

        this.ReRoute();
    }

    void TrackPendingLink(DiagramPoint point)
    {
        if (this.linkSource is null)
            return;

        this.linkTarget = DiagramHitTester.NodeAt(this.Model.VisibleNodes, point);

        if (ReferenceEquals(this.linkTarget, this.linkSource) || this.linkTarget?.CanConnect == false)
            this.linkTarget = null;

        var from = ShapeGeometry.PortPoint(
            this.linkSource.Shape,
            this.linkSource.Bounds,
            this.linkSourcePort,
            this.linkSource.CornerRadius ?? -1
        );

        var to = this.linkTarget is null
            ? point
            : ShapeGeometry.Intersect(
                this.linkTarget.Shape,
                this.linkTarget.Bounds,
                from,
                this.linkTarget.CornerRadius ?? -1
            );

        this.PendingLink = (from, to);
    }

    void OnEndInteraction(object? sender, TouchEventArgs e)
    {
        var finished = this.gesture;
        this.gesture = Gesture.None;
        this.touchHook?.LockScroller(false);

        switch (finished)
        {
            case Gesture.Marquee:
                this.CommitMarquee();
                break;

            case Gesture.DragNodes:
                this.CommitDrag();
                break;

            case Gesture.DrawLink:
                this.CommitLink();
                break;
        }

        this.Marquee = null;
        this.PendingLink = null;
        this.linkSource = null;
        this.linkTarget = null;

        this.Repaint();
    }

    void OnCancelInteraction(object? sender, EventArgs e)
    {
        // A cancelled drag has to put the nodes back itself: no plan was built, so nothing else will.
        if (this.gesture == Gesture.DragNodes)
        {
            foreach (var (node, originX, originY) in this.dragOrigins)
            {
                node.X = originX;
                node.Y = originY;
            }
        }

        this.gesture = Gesture.None;
        this.dragOrigins.Clear();
        this.Marquee = null;
        this.PendingLink = null;
        this.linkSource = null;
        this.linkTarget = null;

        this.touchHook?.LockScroller(false);
        this.ReRoute();
        this.Repaint();
    }

    void CommitMarquee()
    {
        if (this.Marquee is not { } area || !this.movedDuringGesture)
            return;

        this.Select(DiagramHitTester.NodesIn(this.Model.VisibleNodes, area), [], false);
    }

    void CommitDrag()
    {
        if (this.dragOrigins.Count == 0)
            return;

        var plan = new DiagramEditPlan(DiagramEditKind.Move, this.Nodes as IList<DiagramNode>);
        var moved = false;

        foreach (var (node, originX, originY) in this.dragOrigins)
        {
            if (Math.Abs(node.X - originX) < 0.01 && Math.Abs(node.Y - originY) < 0.01)
                continue;

            var x = node.X;
            var y = node.Y;

            // Rewound before recording, so the plan holds the true before-and-after rather than two
            // copies of the after - which is what makes Revert able to undo a live drag.
            node.X = originX;
            node.Y = originY;

            plan.RecordMove(node, x, y);
            moved = true;
        }

        this.dragOrigins.Clear();

        if (!moved)
            return;

        if (this.Vetoed(plan))
        {
            this.ReRoute();
            return;
        }

        plan.ApplyMoves();
        this.Push(plan);
        this.ReRoute();

        this.Edited?.Invoke(this, new DiagramEditedEventArgs(plan));
    }

    void CommitLink()
    {
        if (this.linkSource is null || this.linkTarget is null)
            return;

        if (this.Connections is not IList<DiagramConnection> list)
            return;

        var connection = new DiagramConnection(this.linkSource.Id, this.linkTarget.Id)
        {
            SourcePort = this.linkSourcePort
        };

        var plan = new DiagramEditPlan(DiagramEditKind.AddConnection, this.Nodes as IList<DiagramNode>, list);

        list.Add(connection);
        plan.RecordConnectionAdded(connection, list.Count - 1);

        if (this.Vetoed(plan))
        {
            plan.Revert();
            return;
        }

        this.Push(plan);
        this.Rebuild();

        this.Edited?.Invoke(this, new DiagramEditedEventArgs(plan));
    }

    /// <summary>
    /// Deletes the selection, taking the connections that touched a deleted node with it.
    /// </summary>
    /// <remarks>
    /// A method rather than a key handler: MAUI has no reliable cross-platform key event on a
    /// content view, and a phone has no Delete key at all - so this is what a toolbar button, a
    /// context menu or a desktop shortcut calls.
    /// </remarks>
    public void DeleteSelection()
    {
        if (this.Nodes is not IList<DiagramNode> nodeList || this.Connections is not IList<DiagramConnection> linkList)
            return;

        if (this.Selection.IsEmpty)
            return;

        var plan = new DiagramEditPlan(DiagramEditKind.RemoveNode, nodeList, linkList);
        var doomedNodes = this.Selection.Nodes.ToList();

        // A connection whose node is going has to go too, or the next rebuild reports it as dangling
        // and the user is left with an error where they expected a delete.
        var doomedLinks = linkList
            .Where(c =>
                this.Selection.Connections.Contains(c) ||
                doomedNodes.Any(n =>
                    string.Equals(n.Id, c.SourceId, StringComparison.Ordinal) ||
                    string.Equals(n.Id, c.TargetId, StringComparison.Ordinal)))
            .Distinct()
            .ToList();

        foreach (var connection in doomedLinks)
        {
            plan.RecordConnectionRemoved(connection, linkList.IndexOf(connection));
            linkList.Remove(connection);
        }

        foreach (var node in doomedNodes)
        {
            plan.RecordNodeRemoved(node, nodeList.IndexOf(node));
            nodeList.Remove(node);
        }

        if (plan.IsEmpty)
            return;

        if (this.Vetoed(plan))
        {
            plan.Revert();
            return;
        }

        this.Select([], [], false);
        this.Push(plan);
        this.Rebuild();

        this.Edited?.Invoke(this, new DiagramEditedEventArgs(plan));
    }

    bool Vetoed(DiagramEditPlan plan)
    {
        if (this.Editing is null)
            return false;

        var args = new DiagramEditingEventArgs(plan);
        this.Editing.Invoke(this, args);
        return args.Cancel;
    }

    void Push(DiagramEditPlan plan)
    {
        this.undo.Push(plan);

        // A new edit invalidates the redo branch, the same as every other editor.
        this.redo.Clear();
    }

    void OnPinch(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (!this.AllowZoom)
            return;

        switch (e.Status)
        {
            case GestureStatus.Started:
                this.pinchStartZoom = this.Zoom;
                break;

            case GestureStatus.Running:
            {
                // e.Scale is cumulative from the start of the gesture, so it multiplies the zoom the
                // gesture began at rather than the current one.
                var before = this.PinchAnchor(e);
                this.Zoom = Math.Clamp(this.pinchStartZoom * e.Scale, this.MinZoom, this.MaxZoom);
                var after = this.PinchAnchor(e);

                // Whatever was under the pinch stays under it.
                this.PanX += before.X - after.X;
                this.PanY += before.Y - after.Y;

                this.Repaint();
                break;
            }
        }
    }

    DiagramPoint PinchAnchor(PinchGestureUpdatedEventArgs e) =>
        new(
            this.PanX + (e.ScaleOrigin.X * this.canvas.Width / this.Zoom),
            this.PanY + (e.ScaleOrigin.Y * this.canvas.Height / this.Zoom)
        );

    /// <summary>
    /// Re-routes without re-laying-out, which is what a drag needs on every touch move.
    /// </summary>
    /// <remarks>
    /// Running the full rebuild here would re-run the layout and drag the node straight back out from
    /// under the finger on every frame.
    /// </remarks>
    void ReRoute()
    {
        var byId = new Dictionary<string, DiagramNode>(StringComparer.Ordinal);

        foreach (var node in this.Model.VisibleNodes)
            byId.TryAdd(node.Id, node);

        foreach (var connection in this.Model.VisibleConnections)
        {
            if (byId.TryGetValue(connection.SourceId, out var source) &&
                byId.TryGetValue(connection.TargetId, out var target))
            {
                ConnectionRouter.Route(connection, source, target, this.Router);
            }
        }
    }

    /// <summary>Replaces or extends the selection and raises the change.</summary>
    /// <param name="nodes">The nodes to select.</param>
    /// <param name="connections">The connections to select.</param>
    /// <param name="additive">True to add to the current selection rather than replace it.</param>
    public void Select(
        IReadOnlyList<DiagramNode> nodes,
        IReadOnlyList<DiagramConnection> connections,
        bool additive
    )
    {
        if (!additive)
        {
            foreach (var node in this.Model.Nodes)
                node.IsSelected = false;

            foreach (var connection in this.Model.Connections)
                connection.IsSelected = false;
        }

        foreach (var node in nodes)
            node.IsSelected = true;

        foreach (var connection in connections)
            connection.IsSelected = true;

        var selectedNodes = this.Model.Nodes.Where(n => n.IsSelected).ToList();
        var selectedLinks = this.Model.Connections.Where(c => c.IsSelected).ToList();

        this.Selection = new DiagramSelection(selectedNodes, selectedLinks);
        this.SetValue(SelectedNodeProperty, selectedNodes.Count > 0 ? selectedNodes[^1] : null);

        this.SelectionChanged?.Invoke(this, new DiagramSelectionEventArgs(this.Selection));
        this.Repaint();
    }

    /// <summary>How close a touch has to be to a connection to hit it, widened as the zoom shrinks.</summary>
    double HitTolerance => DiagramHitTester.ConnectionTolerance / Math.Max(this.Zoom, 0.05);

    (DiagramNode Node, DiagramPort Port)? PortUnder(DiagramPoint point)
    {
        // Only a selected node shows handles, so only it can be dragged from - which is also what
        // stops a touch anywhere near a crowded diagram grabbing a connector by accident.
        foreach (var node in this.Selection.Nodes)
        {
            if (!node.CanConnect || node.IsHidden || !node.IsVisible)
                continue;

            var tolerance = DiagramHitTester.PortTolerance / Math.Max(this.Zoom, 0.05);

            if (DiagramHitTester.PortAt(node, point, tolerance) is { } port)
                return (node, port);
        }

        return null;
    }
}
