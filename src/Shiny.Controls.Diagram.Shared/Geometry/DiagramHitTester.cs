namespace Shiny.Controls.Diagramming;

/// <summary>
/// Answers "what is under this point" for a built model.
/// </summary>
/// <remarks>
/// Shared rather than written twice because the answer has to match what was drawn, and what was
/// drawn came from this package. A host that hit-tested against its own idea of the geometry would
/// drift from the picture the moment either side changed.
/// </remarks>
public static class DiagramHitTester
{
    /// <summary>How close a point has to be to a connection to count as on it, in diagram units.</summary>
    public const double ConnectionTolerance = 6;

    /// <summary>How far outside a node's outline its connector handles sit.</summary>
    public const double PortTolerance = 10;

    /// <summary>
    /// The topmost node under a point, or null.
    /// </summary>
    /// <param name="nodes">The nodes to test, in draw order.</param>
    /// <param name="point">The point, in diagram space.</param>
    /// <remarks>
    /// Walked backwards, because the last node drawn is the one on top and therefore the one a click
    /// belongs to.
    /// </remarks>
    public static DiagramNode? NodeAt(IReadOnlyList<DiagramNode> nodes, DiagramPoint point)
    {
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            var node = nodes[i];

            if (!node.IsVisible || node.IsHidden)
                continue;

            if (ShapeGeometry.Contains(node.Shape, node.Bounds, point, node.CornerRadius ?? -1))
                return node;
        }

        return null;
    }

    /// <summary>
    /// The connection under a point, or null.
    /// </summary>
    /// <param name="connections">The connections to test, in draw order.</param>
    /// <param name="point">The point, in diagram space.</param>
    /// <param name="tolerance">How close counts as a hit. Defaults to <see cref="ConnectionTolerance"/>.</param>
    /// <remarks>
    /// A line is one pixel wide and a finger is not, so this measures distance to the route rather
    /// than containment. A bezier is tested against its four points as a polyline, which is close
    /// enough at this tolerance and avoids flattening the curve on every pointer move.
    /// </remarks>
    public static DiagramConnection? ConnectionAt(
        IReadOnlyList<DiagramConnection> connections,
        DiagramPoint point,
        double tolerance = ConnectionTolerance
    )
    {
        for (var i = connections.Count - 1; i >= 0; i--)
        {
            var connection = connections[i];

            if (!connection.IsVisible || connection.IsHidden)
                continue;

            if (DistanceToRoute(connection.Points, point) <= tolerance)
                return connection;
        }

        return null;
    }

    /// <summary>
    /// Which of a node's four connector handles is under a point, or null.
    /// </summary>
    /// <param name="node">The node to test.</param>
    /// <param name="point">The point, in diagram space.</param>
    /// <param name="tolerance">How close counts as a hit. Defaults to <see cref="PortTolerance"/>.</param>
    public static DiagramPort? PortAt(DiagramNode node, DiagramPoint point, double tolerance = PortTolerance)
    {
        if (!node.IsVisible || node.IsHidden || !node.CanConnect)
            return null;

        DiagramPort? best = null;
        var bestDistance = tolerance;

        foreach (var port in Ports)
        {
            var anchor = ShapeGeometry.PortPoint(node.Shape, node.Bounds, port, node.CornerRadius ?? -1);
            var distance = anchor.DistanceTo(point);

            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = port;
            }
        }

        return best;
    }

    /// <summary>The four real ports, in clockwise order from the top.</summary>
    public static readonly IReadOnlyList<DiagramPort> Ports =
        [DiagramPort.Top, DiagramPort.Right, DiagramPort.Bottom, DiagramPort.Left];

    /// <summary>Every node whose box overlaps a rectangle, which is what a marquee drag selects.</summary>
    /// <param name="nodes">The nodes to test.</param>
    /// <param name="area">The marquee, in diagram space.</param>
    public static IReadOnlyList<DiagramNode> NodesIn(IReadOnlyList<DiagramNode> nodes, DiagramRect area)
    {
        var hits = new List<DiagramNode>();

        foreach (var node in nodes)
        {
            if (!node.IsVisible || node.IsHidden)
                continue;

            if (area.IntersectsWith(node.Bounds))
                hits.Add(node);
        }

        return hits;
    }

    /// <summary>Shortest distance from a point to a polyline.</summary>
    /// <param name="route">The polyline.</param>
    /// <param name="point">The point.</param>
    public static double DistanceToRoute(IReadOnlyList<DiagramPoint> route, DiagramPoint point)
    {
        if (route.Count == 0)
            return double.MaxValue;

        if (route.Count == 1)
            return route[0].DistanceTo(point);

        var best = double.MaxValue;

        for (var i = 1; i < route.Count; i++)
            best = Math.Min(best, DistanceToSegment(route[i - 1], route[i], point));

        return best;
    }

    static double DistanceToSegment(DiagramPoint a, DiagramPoint b, DiagramPoint p)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = (dx * dx) + (dy * dy);

        if (lengthSquared < 1e-12)
            return a.DistanceTo(p);

        // Project onto the segment and clamp, so a point beyond either end measures to that end
        // rather than to the infinite line.
        var t = (((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / lengthSquared;
        t = Math.Clamp(t, 0, 1);

        return new DiagramPoint(a.X + (dx * t), a.Y + (dy * t)).DistanceTo(p);
    }
}
