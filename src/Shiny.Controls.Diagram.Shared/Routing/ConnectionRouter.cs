namespace Shiny.Controls.Diagramming;

/// <summary>
/// Turns a pair of nodes into the polyline or curve the line between them follows.
/// </summary>
/// <remarks>
/// <para>
/// Both ends are anchored with <see cref="ShapeGeometry.Intersect"/>, against the real outline rather
/// than the bounding box. That single detail is most of what makes a decision tree look drawn rather
/// than approximated: with a box anchor, every arrow into a diamond stops short in mid-air at a
/// corner of an invisible rectangle.
/// </para>
/// <para>
/// The three routers produce the same contract - a point list on
/// <see cref="DiagramConnection.Points"/> - so a host draws all three the same way and only
/// <see cref="DiagramConnectionRouter.Bezier"/> needs to be told the points are control points rather
/// than vertices.
/// </para>
/// </remarks>
public static class ConnectionRouter
{
    /// <summary>
    /// Which axis an <see cref="DiagramPort.Auto"/> port is allowed to leave on.
    /// </summary>
    /// <remarks>
    /// A layout that flows in one direction wants its lines to leave and enter on that axis, and
    /// picking the geometrically nearest side instead is what makes an org chart's arrows arrive in
    /// the sides of the boxes rather than their tops. The side <i>within</i> the axis is still chosen
    /// from the relative positions, so a mindmap's left-hand branches leave the root's left edge and
    /// its right-hand ones leave the right.
    /// </remarks>
    public enum Axis
    {
        /// <summary>No preference - the nearest side wins. What a hand-placed diagram wants.</summary>
        Free,

        /// <summary>Lines leave and enter through the top and bottom edges.</summary>
        Vertical,

        /// <summary>Lines leave and enter through the left and right edges.</summary>
        Horizontal
    }

    /// <summary>How far a self-connection's loop stands off the node.</summary>
    const double SelfLoopSize = 36;

    /// <summary>How far a bezier's control points are pulled along the port normals, as a fraction of the span.</summary>
    const double BezierTension = 0.45;

    /// <summary>
    /// Routes one connection and writes the result onto it.
    /// </summary>
    /// <param name="connection">The connection to route. Its <see cref="DiagramConnection.Points"/> and
    /// <see cref="DiagramConnection.LabelPosition"/> are replaced.</param>
    /// <param name="source">The node the line leaves.</param>
    /// <param name="target">The node the line arrives at.</param>
    /// <param name="fallbackRouter">The router to use when the connection does not name one.</param>
    /// <param name="axis">Which axis an <see cref="DiagramPort.Auto"/> port may leave on.</param>
    public static void Route(
        DiagramConnection connection,
        DiagramNode source,
        DiagramNode target,
        DiagramConnectionRouter fallbackRouter,
        Axis axis = Axis.Free
    )
    {
        if (ReferenceEquals(source, target))
        {
            connection.Points = SelfLoop(source);
            connection.LabelPosition = new DiagramPoint(source.Bounds.Right + SelfLoopSize, source.Bounds.CenterY);
            return;
        }

        var router = connection.Router ?? fallbackRouter;
        var bends = connection.LayoutBends;

        var sourceBounds = source.Bounds;
        var targetBounds = target.Bounds;

        // The anchor faces the first bend rather than the far node, so a long edge threaded around
        // two intervening nodes leaves its source pointing the way it is actually going.
        var towardsSource = bends is { Count: > 0 } ? bends[0] : targetBounds.Center;
        var towardsTarget = bends is { Count: > 0 } ? bends[^1] : sourceBounds.Center;

        var sourcePort = Resolve(connection.SourcePort, source, towardsSource, axis);
        var targetPort = Resolve(connection.TargetPort, target, towardsTarget, axis);

        var start = Anchor(source, sourcePort, towardsSource);
        var end = Anchor(target, targetPort, towardsTarget);

        // Straight lines are happy with an unresolved Auto - the anchor is a ray intersection and
        // needs no side. The other two need a concrete edge to leave along: an elbow has to know
        // which way to turn first, and a bezier has no curve at all without a normal to pull its
        // control points down.
        var sourceEdge = sourcePort == DiagramPort.Auto
            ? ShapeGeometry.FacingPort(sourceBounds, towardsSource)
            : sourcePort;

        var targetEdge = targetPort == DiagramPort.Auto
            ? ShapeGeometry.FacingPort(targetBounds, towardsTarget)
            : targetPort;

        var points = router switch
        {
            DiagramConnectionRouter.Bezier => Bezier(start, end, sourceEdge, targetEdge, bends),
            DiagramConnectionRouter.Orthogonal => Orthogonal(start, end, sourceEdge, bends),
            _ => Straight(start, end, bends)
        };

        connection.Points = points;
        connection.LabelPosition = LabelPosition(points, router);
    }

    static DiagramPoint Anchor(DiagramNode node, DiagramPort port, DiagramPoint towards) =>
        port == DiagramPort.Auto
            ? ShapeGeometry.Intersect(node.Shape, node.Bounds, towards, node.CornerRadius ?? -1)
            : ShapeGeometry.PortPoint(node.Shape, node.Bounds, port, node.CornerRadius ?? -1);

    /// <summary>
    /// Turns an <see cref="DiagramPort.Auto"/> port into a concrete side, honouring the layout's axis
    /// when it has one.
    /// </summary>
    /// <remarks>
    /// Auto is left as Auto when the axis is free, because the anchor is then better computed by ray
    /// intersection than by snapping to one of four sides - that is what keeps a straight line to a
    /// node up and to the left leaving the corner rather than the middle of an edge.
    /// </remarks>
    static DiagramPort Resolve(DiagramPort port, DiagramNode node, DiagramPoint towards, Axis axis)
    {
        if (port != DiagramPort.Auto || axis == Axis.Free)
            return port;

        // The same rule at both ends: a port faces whatever it is pointing at. The source points at
        // the target and the target points back at the source, so a parent above its child leaves
        // through its own bottom and arrives through the child's top without either end needing to
        // know which one it is.
        if (axis == Axis.Vertical)
            return towards.Y >= node.Bounds.CenterY ? DiagramPort.Bottom : DiagramPort.Top;

        return towards.X >= node.Bounds.CenterX ? DiagramPort.Right : DiagramPort.Left;
    }

    static List<DiagramPoint> Straight(DiagramPoint start, DiagramPoint end, IReadOnlyList<DiagramPoint>? bends)
    {
        var points = new List<DiagramPoint>((bends?.Count ?? 0) + 2) { start };

        if (bends is not null)
            points.AddRange(bends);

        points.Add(end);
        return points;
    }

    /// <summary>
    /// Right-angled elbows - the org-chart look.
    /// </summary>
    /// <remarks>
    /// The line leaves and enters along its ports' normals and turns at the midpoint between them. It
    /// is a routing, not a pathfinding: it does not detour around unrelated nodes in the way, because
    /// the layouts that produce this look have already left a clear lane between a parent and its
    /// children, and a real obstacle-avoiding router costs an order of magnitude more per frame than
    /// the case it would fix.
    /// </remarks>
    static List<DiagramPoint> Orthogonal(
        DiagramPoint start,
        DiagramPoint end,
        DiagramPort sourcePort,
        IReadOnlyList<DiagramPoint>? bends
    )
    {
        var points = new List<DiagramPoint> { start };
        var vertical = sourcePort is DiagramPort.Top or DiagramPort.Bottom;

        var waypoints = new List<DiagramPoint>();
        if (bends is not null)
            waypoints.AddRange(bends);
        waypoints.Add(end);

        var current = start;

        foreach (var waypoint in waypoints)
        {
            // Turn halfway along the axis the line is currently travelling on, which is what puts the
            // shared horizontal run of an org chart's sibling arrows on one line.
            if (vertical)
            {
                var mid = (current.Y + waypoint.Y) / 2;
                AddIfMoved(points, new DiagramPoint(current.X, mid));
                AddIfMoved(points, new DiagramPoint(waypoint.X, mid));
            }
            else
            {
                var mid = (current.X + waypoint.X) / 2;
                AddIfMoved(points, new DiagramPoint(mid, current.Y));
                AddIfMoved(points, new DiagramPoint(mid, waypoint.Y));
            }

            AddIfMoved(points, waypoint);
            current = waypoint;
        }

        return points;
    }

    static void AddIfMoved(List<DiagramPoint> points, DiagramPoint point)
    {
        var last = points[^1];

        if (Math.Abs(last.X - point.X) < 1e-6 && Math.Abs(last.Y - point.Y) < 1e-6)
            return;

        points.Add(point);
    }

    /// <summary>
    /// A cubic bezier, with the control points pulled straight out of each port.
    /// </summary>
    /// <remarks>
    /// Four points: start, two controls, end. Handing a host the controls rather than a tessellated
    /// curve is what lets the MAUI side draw a real <c>PathF</c> curve and the Blazor side emit a
    /// single SVG <c>C</c> command, instead of both drawing forty short lines.
    /// </remarks>
    static List<DiagramPoint> Bezier(
        DiagramPoint start,
        DiagramPoint end,
        DiagramPort sourcePort,
        DiagramPort targetPort,
        IReadOnlyList<DiagramPoint>? bends
    )
    {
        _ = bends;
        var sourceNormal = ShapeGeometry.PortNormal(sourcePort);
        var targetNormal = ShapeGeometry.PortNormal(targetPort);

        var span = start.DistanceTo(end) * BezierTension;

        return
        [
            start,
            new DiagramPoint(start.X + (sourceNormal.X * span), start.Y + (sourceNormal.Y * span)),
            new DiagramPoint(end.X + (targetNormal.X * span), end.Y + (targetNormal.Y * span)),
            end
        ];
    }

    /// <summary>A lobe off the node's right side, for a connection that starts and ends on the same node.</summary>
    static List<DiagramPoint> SelfLoop(DiagramNode node)
    {
        var bounds = node.Bounds;
        var right = bounds.Right;
        var top = bounds.CenterY - (bounds.Height / 4);
        var bottom = bounds.CenterY + (bounds.Height / 4);
        var out1 = right + SelfLoopSize;

        return
        [
            new DiagramPoint(right, top),
            new DiagramPoint(out1, top),
            new DiagramPoint(out1, bottom),
            new DiagramPoint(right, bottom)
        ];
    }

    /// <summary>
    /// Where a connection's label sits: the midpoint of the route by arc length, not by index.
    /// </summary>
    /// <param name="points">The route.</param>
    /// <param name="router">Which router produced it, since a bezier's points are controls.</param>
    /// <remarks>
    /// By index is a one-liner and puts the label in the wrong place on any route whose segments are
    /// not all the same length - which is every orthogonal route with a short stub and a long run.
    /// </remarks>
    public static DiagramPoint LabelPosition(IReadOnlyList<DiagramPoint> points, DiagramConnectionRouter router)
    {
        if (points.Count == 0)
            return DiagramPoint.Zero;

        if (points.Count == 1)
            return points[0];

        if (router == DiagramConnectionRouter.Bezier && points.Count == 4)
            return CubicAt(points[0], points[1], points[2], points[3], 0.5);

        var total = 0d;
        for (var i = 1; i < points.Count; i++)
            total += points[i - 1].DistanceTo(points[i]);

        if (total <= 0)
            return points[0];

        var half = total / 2;
        var walked = 0d;

        for (var i = 1; i < points.Count; i++)
        {
            var segment = points[i - 1].DistanceTo(points[i]);

            if (walked + segment >= half)
            {
                var t = segment <= 0 ? 0 : (half - walked) / segment;
                return new DiagramPoint(
                    points[i - 1].X + ((points[i].X - points[i - 1].X) * t),
                    points[i - 1].Y + ((points[i].Y - points[i - 1].Y) * t)
                );
            }

            walked += segment;
        }

        return points[^1];
    }

    /// <summary>A point on a cubic bezier.</summary>
    /// <param name="p0">Start.</param>
    /// <param name="p1">First control point.</param>
    /// <param name="p2">Second control point.</param>
    /// <param name="p3">End.</param>
    /// <param name="t">Position along the curve, zero to one.</param>
    public static DiagramPoint CubicAt(DiagramPoint p0, DiagramPoint p1, DiagramPoint p2, DiagramPoint p3, double t)
    {
        var u = 1 - t;
        var a = u * u * u;
        var b = 3 * u * u * t;
        var c = 3 * u * t * t;
        var d = t * t * t;

        return new DiagramPoint(
            (a * p0.X) + (b * p1.X) + (c * p2.X) + (d * p3.X),
            (a * p0.Y) + (b * p1.Y) + (c * p2.Y) + (d * p3.Y)
        );
    }

    /// <summary>
    /// The direction a cap points at one end of a route.
    /// </summary>
    /// <param name="points">The route.</param>
    /// <param name="router">Which router produced it.</param>
    /// <param name="atEnd">True for the target end, false for the source end.</param>
    /// <returns>A unit vector pointing along the line, away from the node the cap sits on.</returns>
    public static DiagramPoint CapDirection(
        IReadOnlyList<DiagramPoint> points,
        DiagramConnectionRouter router,
        bool atEnd
    )
    {
        if (points.Count < 2)
            return new DiagramPoint(1, 0);

        DiagramPoint from, to;

        if (router == DiagramConnectionRouter.Bezier && points.Count == 4)
        {
            // The tangent at an endpoint runs to its own control point, and the control point is
            // where the curve is actually heading - unlike the other endpoint, which it may curve
            // right away from.
            (from, to) = atEnd ? (points[2], points[3]) : (points[1], points[0]);
        }
        else
        {
            (from, to) = atEnd ? (points[^2], points[^1]) : (points[1], points[0]);
        }

        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));

        return length < 1e-9 ? new DiagramPoint(1, 0) : new DiagramPoint(dx / length, dy / length);
    }
}
