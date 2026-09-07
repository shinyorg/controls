using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Shiny.Controls.Diagramming;

namespace Shiny.Blazor.Controls.Diagram;

public sealed partial class DiagramView
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
    DiagramRect? marquee;
    string? pendingLink;

    DiagramNode? linkSource;
    DiagramPort linkSourcePort;
    DiagramNode? linkTarget;

    readonly List<DiagramNode> dragging = [];
    readonly List<(DiagramNode Node, double X, double Y)> dragOrigins = [];

    /// <summary>The nodes and connections currently selected.</summary>
    public DiagramSelection Selection { get; private set; } = DiagramSelection.Empty;

    /// <summary>
    /// Converts a pointer event into diagram space.
    /// </summary>
    /// <remarks>
    /// From <c>ClientX</c> and a measured origin, never from <c>OffsetX</c>. Inside an SVG the event
    /// target is whichever path is under the finger, so <c>OffsetX</c> silently reports coordinates
    /// in a child's space - and the resulting hit test is wrong by however far that child is from the
    /// surface, which looks like an intermittent selection bug rather than a coordinate one.
    /// </remarks>
    DiagramPoint At(PointerEventArgs e) =>
        this.ToDiagram(e.ClientX - this.originX, e.ClientY - this.originY);

    async Task OnPointerDown(PointerEventArgs e)
    {
        var point = this.At(e);

        this.gestureStart = point;
        this.gestureLast = point;

        if (this.module is not null)
            await this.module.InvokeVoidAsync("capture", this.surface, e.PointerId);

        // Ports first: a connector handle sits on the node's outline, so testing the node first would
        // make every handle unreachable.
        if (this.AllowConnectionEdit && this.PortUnder(point) is var (portNode, port))
        {
            this.gesture = Gesture.DrawLink;
            this.linkSource = portNode;
            this.linkSourcePort = port;
            return;
        }

        var node = DiagramHitTester.NodeAt(this.Model.VisibleNodes, point);

        if (node is not null)
        {
            this.HandleNodePressed(node, e);
            return;
        }

        var connection = DiagramHitTester.ConnectionAt(this.Model.VisibleConnections, point, this.HitTolerance);

        if (connection is not null)
        {
            if (this.AllowSelection)
                this.Select([], [connection], e.ShiftKey || e.CtrlKey || e.MetaKey);

            if (this.OnConnectionClick.HasDelegate)
                await this.OnConnectionClick.InvokeAsync(connection);

            return;
        }

        // Background.
        if (!e.ShiftKey && !e.CtrlKey && !e.MetaKey && this.AllowSelection)
            this.Select([], [], false);

        if (this.AllowMultiSelect && (e.ShiftKey || !this.AllowPan))
        {
            this.gesture = Gesture.Marquee;
            this.marquee = new DiagramRect(point.X, point.Y, 0, 0);
        }
        else if (this.AllowPan)
        {
            this.gesture = Gesture.Pan;
        }
    }

    void HandleNodePressed(DiagramNode node, PointerEventArgs e)
    {
        var additive = e.ShiftKey || e.CtrlKey || e.MetaKey;

        if (this.AllowSelection && (!node.IsSelected || additive))
            this.Select([node], [], additive);

        if (this.OnNodeClick.HasDelegate)
            _ = this.OnNodeClick.InvokeAsync(node);

        if (!this.AllowNodeDrag || !node.CanMove)
            return;

        this.gesture = Gesture.DragNodes;
        this.dragging.Clear();
        this.dragOrigins.Clear();

        // The whole selection moves together, which is what makes a marquee worth having. A node that
        // is not selected drags alone.
        var group = node.IsSelected && this.Selection.Nodes.Count > 0
            ? this.Selection.Nodes
            : [node];

        foreach (var candidate in group)
        {
            if (!candidate.CanMove)
                continue;

            this.dragging.Add(candidate);
            this.dragOrigins.Add((candidate, candidate.X, candidate.Y));
        }
    }

    Task OnPointerMove(PointerEventArgs e)
    {
        if (this.gesture == Gesture.None)
            return Task.CompletedTask;

        var point = this.At(e);

        switch (this.gesture)
        {
            case Gesture.Pan:
                this.panX -= point.X - this.gestureLast.X;
                this.panY -= point.Y - this.gestureLast.Y;

                // Recomputed rather than carried: the pan changed what this screen position maps to,
                // so the previous value is stale by exactly the amount just applied.
                this.gestureLast = this.ToDiagram(e.ClientX - this.originX, e.ClientY - this.originY);
                break;

            case Gesture.Marquee:
                this.marquee = DiagramRect.FromCorners(this.gestureStart, point);
                break;

            case Gesture.DragNodes:
                this.MoveDragged(point);
                break;

            case Gesture.DrawLink:
                this.TrackPendingLink(point);
                break;
        }

        this.StateHasChanged();
        return Task.CompletedTask;
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

            // Moved live rather than on release, so the drag is visible; the plan built on pointer-up
            // still carries the original positions, so a veto or an undo puts them back.
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

        this.pendingLink = ShapePathData.ForRoute([from, to], DiagramConnectionRouter.Straight);
    }

    async Task OnPointerUp(PointerEventArgs e)
    {
        var finished = this.gesture;
        this.gesture = Gesture.None;

        if (this.module is not null)
            await this.module.InvokeVoidAsync("release", this.surface, e.PointerId);

        switch (finished)
        {
            case Gesture.Marquee:
                this.CommitMarquee(e);
                break;

            case Gesture.DragNodes:
                await this.CommitDragAsync();
                break;

            case Gesture.DrawLink:
                await this.CommitLinkAsync();
                break;
        }

        this.marquee = null;
        this.pendingLink = null;
        this.linkSource = null;
        this.linkTarget = null;

        this.StateHasChanged();
    }

    void CommitMarquee(PointerEventArgs e)
    {
        if (this.marquee is not { } area)
            return;

        var hits = DiagramHitTester.NodesIn(this.Model.VisibleNodes, area);
        this.Select(hits, [], e.ShiftKey || e.CtrlKey || e.MetaKey);
    }

    async Task CommitDragAsync()
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

        this.dragging.Clear();
        this.dragOrigins.Clear();

        if (!moved)
            return;

        if (await this.VetoedAsync(plan))
        {
            this.ReRoute();
            return;
        }

        plan.ApplyMoves();
        this.Push(plan);
        this.ReRoute();

        if (this.OnEdited.HasDelegate)
            await this.OnEdited.InvokeAsync(plan);
    }

    async Task CommitLinkAsync()
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

        if (await this.VetoedAsync(plan))
        {
            plan.Revert();
            return;
        }

        this.Push(plan);
        this.Rebuild();

        if (this.OnEdited.HasDelegate)
            await this.OnEdited.InvokeAsync(plan);
    }

    async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is "Delete" or "Backspace" && this.AllowDelete)
        {
            await this.DeleteSelectionAsync();
            return;
        }

        if (e.Key is "z" or "Z" && (e.CtrlKey || e.MetaKey))
        {
            if (e.ShiftKey)
                this.Redo();
            else
                this.Undo();

            return;
        }

        if (e.Key is "a" or "A" && (e.CtrlKey || e.MetaKey) && this.AllowMultiSelect)
            this.Select(this.Model.VisibleNodes, this.Model.VisibleConnections, false);
    }

    /// <summary>
    /// Deletes the selection, taking the connections that touched a deleted node with it.
    /// </summary>
    public async Task DeleteSelectionAsync()
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

        if (await this.VetoedAsync(plan))
        {
            plan.Revert();
            return;
        }

        this.Select([], [], false);
        this.Push(plan);
        this.Rebuild();

        if (this.OnEdited.HasDelegate)
            await this.OnEdited.InvokeAsync(plan);
    }

    async Task<bool> VetoedAsync(DiagramEditPlan plan)
    {
        if (!this.OnEditing.HasDelegate)
            return false;

        var args = new DiagramEditingEventArgs(plan);
        await this.OnEditing.InvokeAsync(args);
        return args.Cancel;
    }

    void Push(DiagramEditPlan plan)
    {
        this.undo.Push(plan);

        // A new edit invalidates the redo branch, the same as every other editor.
        this.redo.Clear();
    }

    /// <summary>
    /// Re-routes without re-laying-out, which is what a drag needs on every pointer move.
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

        if (this.OnSelectionChanged.HasDelegate)
            _ = this.OnSelectionChanged.InvokeAsync(this.Selection);
    }

    /// <summary>How close a pointer has to be to a connection to hit it, widened as the zoom shrinks.</summary>
    double HitTolerance => DiagramHitTester.ConnectionTolerance / Math.Max(this.Zoom, 0.05);

    (DiagramNode Node, DiagramPort Port)? PortUnder(DiagramPoint point)
    {
        // Only the selected node shows handles, so only it can be dragged from - which is also what
        // stops a pointer-down anywhere near a crowded diagram grabbing a connector by accident.
        foreach (var node in this.PortHost())
        {
            if (DiagramHitTester.PortAt(node, point, DiagramHitTester.PortTolerance / Math.Max(this.Zoom, 0.05))
                is { } port)
            {
                return (node, port);
            }
        }

        return null;
    }

    /// <summary>The nodes currently showing connector handles.</summary>
    IEnumerable<DiagramNode> PortHost()
    {
        foreach (var node in this.Selection.Nodes)
        {
            if (node.CanConnect && !node.IsHidden && node.IsVisible)
                yield return node;
        }
    }

    IEnumerable<CapShape> Caps(DiagramConnection connection, DiagramConnectionRouter router)
    {
        if (connection.Points.Count < 2)
            yield break;

        if (connection.EndCap != DiagramConnectionCap.None)
        {
            var direction = ConnectionRouter.CapDirection(connection.Points, router, atEnd: true);
            yield return CapShape.Build(connection.EndCap, connection.Points[^1], direction);
        }

        if (connection.StartCap != DiagramConnectionCap.None)
        {
            var direction = ConnectionRouter.CapDirection(connection.Points, router, atEnd: false);
            yield return CapShape.Build(connection.StartCap, connection.Points[0], direction);
        }
    }

    IEnumerable<LabelLine> WrapLabel(DiagramNode node)
    {
        if (string.IsNullOrWhiteSpace(node.Text))
            yield break;

        var fontSize = node.FontSize ?? 14;
        var lineHeight = fontSize * 1.25;

        // A crude character-width estimate, the same one the layout sizes with - so a label that made
        // its node wider also wraps to that width rather than to some other guess.
        var perLine = Math.Max(1, (int)((node.Width - 16) / (fontSize * 0.6)));
        var lines = new List<string>();

        // A newline in the label is a break the author asked for, and it wins over the width wrap -
        // "Dana Whitfield\nCEO" is two lines because someone wrote it as two, not because it did not
        // fit.
        foreach (var paragraph in node.Text.Replace("\r\n", "\n").Split('\n'))
            lines.AddRange(Wrap(paragraph, perLine));

        var top = node.Bounds.CenterY - ((lines.Count - 1) * lineHeight / 2);

        for (var i = 0; i < lines.Count; i++)
            yield return new LabelLine(lines[i], top + (i * lineHeight));
    }

    static List<string> Wrap(string text, int perLine)
    {
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";

            if (candidate.Length <= perLine || current.Length == 0)
            {
                current = candidate;
                continue;
            }

            lines.Add(current);
            current = word;
        }

        if (current.Length > 0)
            lines.Add(current);

        // A single word longer than the line still has to be drawn; it overflows rather than being
        // cut, because a truncated node label is worse than a wide one. An empty paragraph keeps its
        // blank line, so a deliberate gap in a label survives.
        return lines.Count == 0 ? [text] : lines;
    }

    /// <summary>One line of a wrapped node label.</summary>
    /// <param name="Text">The line's text.</param>
    /// <param name="Y">Its baseline, in diagram space.</param>
    readonly record struct LabelLine(string Text, double Y);
}
