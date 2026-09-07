namespace Shiny.Controls.Diagramming;

/// <summary>
/// Turns a shape and its box into an outline, and answers the two questions a diagram keeps asking of
/// one: where does a line coming from over there meet this shape's edge, and is this click inside it.
/// </summary>
/// <remarks>
/// <para>
/// Both answers are computed against the <i>real</i> outline rather than the bounding box. That is
/// the difference between a decision tree that looks drawn and one that looks approximated: with a
/// bounding-box anchor, every arrow into a diamond stops short in mid-air at the corner of an
/// invisible rectangle, and a click in that same empty corner selects the node.
/// </para>
/// <para>
/// Every shape except the two round ones is reduced to a convex polygon - curves are tessellated -
/// so there is exactly one ray-intersection and one containment implementation to get right.
/// <see cref="DiagramNodeShape.Circle"/> and <see cref="DiagramNodeShape.Ellipse"/> are done
/// analytically instead, because a tessellated circle anchors arrows onto flat spots that are
/// visible at the zoom levels this control supports.
/// </para>
/// </remarks>
public static class ShapeGeometry
{
    /// <summary>Segments used to tessellate one rounded corner or one quarter of an ellipse.</summary>
    const int ArcSegments = 8;

    /// <summary>The default corner radius for the rounded shapes, when a node does not override it.</summary>
    public const double DefaultCornerRadius = 8;

    /// <summary>How far a parallelogram's top edge is inset, as a fraction of its width.</summary>
    /// <remarks>
    /// Shared with the renderers on both hosts. The anchoring here and the drawing there have to
    /// agree to the pixel, and the way that stops being true is one of them carrying its own copy of
    /// this number.
    /// </remarks>
    public const double ParallelogramInset = 0.2;

    /// <summary>How far a hexagon's flat top edge is inset from each side, as a fraction of its width.</summary>
    public const double HexagonInset = 0.25;

    /// <summary>How deep a cylinder's elliptical cap is, as a fraction of its height.</summary>
    public const double CylinderCap = 0.18;

    /// <summary>How deep a document's wavy bottom edge is, as a fraction of its height.</summary>
    public const double DocumentWave = 0.14;

    /// <summary>
    /// The shape's outline as a closed polygon, in diagram space.
    /// </summary>
    /// <param name="shape">The shape to trace.</param>
    /// <param name="bounds">The box the shape fills.</param>
    /// <param name="cornerRadius">Corner radius for the rounded shapes. Negative takes the default.</param>
    /// <returns>The outline vertices, in order. Empty when the box has no area.</returns>
    public static IReadOnlyList<DiagramPoint> Outline(
        DiagramNodeShape shape,
        DiagramRect bounds,
        double cornerRadius = -1
    )
    {
        if (bounds.IsEmpty)
            return [];

        var radius = cornerRadius < 0 ? DefaultCornerRadius : cornerRadius;

        return shape switch
        {
            DiagramNodeShape.Rectangle => Rect(bounds),
            DiagramNodeShape.RoundedRectangle => RoundedRect(bounds, radius),
            DiagramNodeShape.Stadium => RoundedRect(bounds, bounds.Height / 2),
            DiagramNodeShape.Circle => EllipseOutline(CircleBounds(bounds)),
            DiagramNodeShape.Ellipse => EllipseOutline(bounds),
            DiagramNodeShape.Diamond => Diamond(bounds),
            DiagramNodeShape.Parallelogram => Parallelogram(bounds),
            DiagramNodeShape.Hexagon => Hexagon(bounds),
            DiagramNodeShape.Triangle => Triangle(bounds),

            // A cylinder's silhouette is its box with the top and bottom bowed out, and a document's
            // is its box with a wave along the bottom. Both bulges are decoration a few pixels deep;
            // anchoring and hit testing against the box is within a pixel or two of the truth and
            // avoids two more special cases in every consumer.
            DiagramNodeShape.Cylinder => Rect(bounds),
            DiagramNodeShape.Document => Rect(bounds),
            _ => Rect(bounds)
        };
    }

    /// <summary>
    /// The box a <see cref="DiagramNodeShape.Circle"/> actually fills - square, centred, sized from
    /// the larger dimension so the label still fits.
    /// </summary>
    /// <param name="bounds">The node's box.</param>
    public static DiagramRect CircleBounds(DiagramRect bounds)
    {
        var diameter = Math.Max(bounds.Width, bounds.Height);
        return DiagramRect.FromCenter(bounds.CenterX, bounds.CenterY, diameter, diameter);
    }

    /// <summary>
    /// Where a line running between the shape's centre and <paramref name="towards"/> crosses the
    /// shape's outline.
    /// </summary>
    /// <param name="shape">The shape being left or entered.</param>
    /// <param name="bounds">The box the shape fills.</param>
    /// <param name="towards">The point the line heads for, in diagram space.</param>
    /// <param name="cornerRadius">Corner radius for the rounded shapes. Negative takes the default.</param>
    /// <returns>
    /// The point on the outline. Falls back to the centre when the box has no area, and to the bottom
    /// edge when <paramref name="towards"/> is the centre itself - a self-connection, which has no
    /// direction to work from.
    /// </returns>
    public static DiagramPoint Intersect(
        DiagramNodeShape shape,
        DiagramRect bounds,
        DiagramPoint towards,
        double cornerRadius = -1
    )
    {
        if (bounds.IsEmpty)
            return bounds.Center;

        var cx = bounds.CenterX;
        var cy = bounds.CenterY;
        var dx = towards.X - cx;
        var dy = towards.Y - cy;

        if (Math.Abs(dx) < Epsilon && Math.Abs(dy) < Epsilon)
            return new DiagramPoint(cx, bounds.Bottom);

        if (shape is DiagramNodeShape.Circle or DiagramNodeShape.Ellipse)
        {
            var box = shape == DiagramNodeShape.Circle ? CircleBounds(bounds) : bounds;
            return EllipseIntersect(box, dx, dy);
        }

        var outline = Outline(shape, bounds, cornerRadius);
        return PolygonIntersect(outline, new DiagramPoint(cx, cy), dx, dy) ?? new DiagramPoint(cx, bounds.Bottom);
    }

    /// <summary>
    /// Whether a point falls inside the shape's real outline, not merely inside its box.
    /// </summary>
    /// <param name="shape">The shape to test.</param>
    /// <param name="bounds">The box the shape fills.</param>
    /// <param name="point">The point to test, in diagram space.</param>
    /// <param name="cornerRadius">Corner radius for the rounded shapes. Negative takes the default.</param>
    public static bool Contains(
        DiagramNodeShape shape,
        DiagramRect bounds,
        DiagramPoint point,
        double cornerRadius = -1
    )
    {
        if (bounds.IsEmpty)
            return false;

        if (shape is DiagramNodeShape.Circle or DiagramNodeShape.Ellipse)
        {
            var box = shape == DiagramNodeShape.Circle ? CircleBounds(bounds) : bounds;
            var nx = (point.X - box.CenterX) / (box.Width / 2);
            var ny = (point.Y - box.CenterY) / (box.Height / 2);
            return (nx * nx) + (ny * ny) <= 1;
        }

        // Cheap reject first: every remaining outline is inside its own box, and most misses are far
        // outside it.
        if (!bounds.Contains(point))
            return false;

        return PolygonContains(Outline(shape, bounds, cornerRadius), point);
    }

    /// <summary>
    /// The point on the outline that a named port sits at.
    /// </summary>
    /// <param name="shape">The shape the port belongs to.</param>
    /// <param name="bounds">The box the shape fills.</param>
    /// <param name="port">Which side. <see cref="DiagramPort.Auto"/> resolves to the centre, which is
    /// not a usable anchor on its own - callers wanting Auto should use <see cref="Intersect"/>.</param>
    /// <param name="cornerRadius">Corner radius for the rounded shapes. Negative takes the default.</param>
    public static DiagramPoint PortPoint(
        DiagramNodeShape shape,
        DiagramRect bounds,
        DiagramPort port,
        double cornerRadius = -1
    )
    {
        if (bounds.IsEmpty)
            return bounds.Center;

        // Cast a ray from the centre straight out at the named side and take the outline crossing,
        // rather than using the box edge. On a diamond the left port belongs at the west vertex, not
        // at the middle of the bounding box's left edge, where there is nothing drawn.
        var (dx, dy) = port switch
        {
            DiagramPort.Top => (0d, -1d),
            DiagramPort.Right => (1d, 0d),
            DiagramPort.Bottom => (0d, 1d),
            DiagramPort.Left => (-1d, 0d),
            _ => (0d, 0d)
        };

        if (dx == 0 && dy == 0)
            return bounds.Center;

        return Intersect(shape, bounds, new DiagramPoint(bounds.CenterX + dx, bounds.CenterY + dy), cornerRadius);
    }

    /// <summary>The outward direction a port faces, as a unit vector. Used to pull bezier control points.</summary>
    /// <param name="port">The port. <see cref="DiagramPort.Auto"/> has no direction and returns zero.</param>
    public static DiagramPoint PortNormal(DiagramPort port) => port switch
    {
        DiagramPort.Top => new DiagramPoint(0, -1),
        DiagramPort.Right => new DiagramPoint(1, 0),
        DiagramPort.Bottom => new DiagramPoint(0, 1),
        DiagramPort.Left => new DiagramPoint(-1, 0),
        _ => DiagramPoint.Zero
    };

    /// <summary>
    /// Which side of <paramref name="bounds"/> faces <paramref name="towards"/>, for resolving
    /// <see cref="DiagramPort.Auto"/> into a concrete side.
    /// </summary>
    /// <param name="bounds">The box being left or entered.</param>
    /// <param name="towards">The point the line heads for.</param>
    /// <remarks>
    /// Compared against the box's own aspect ratio rather than at 45 degrees, so a wide, short node
    /// hands off from its left/right edges to its top/bottom ones where the corner actually is.
    /// </remarks>
    public static DiagramPort FacingPort(DiagramRect bounds, DiagramPoint towards)
    {
        var dx = towards.X - bounds.CenterX;
        var dy = towards.Y - bounds.CenterY;

        if (Math.Abs(dx) < Epsilon && Math.Abs(dy) < Epsilon)
            return DiagramPort.Bottom;

        var hw = Math.Max(bounds.Width / 2, Epsilon);
        var hh = Math.Max(bounds.Height / 2, Epsilon);

        if (Math.Abs(dx) * hh >= Math.Abs(dy) * hw)
            return dx >= 0 ? DiagramPort.Right : DiagramPort.Left;

        return dy >= 0 ? DiagramPort.Bottom : DiagramPort.Top;
    }

    const double Epsilon = 1e-9;

    static IReadOnlyList<DiagramPoint> Rect(DiagramRect b) =>
    [
        new(b.X, b.Y),
        new(b.Right, b.Y),
        new(b.Right, b.Bottom),
        new(b.X, b.Bottom)
    ];

    static IReadOnlyList<DiagramPoint> Diamond(DiagramRect b) =>
    [
        new(b.CenterX, b.Y),
        new(b.Right, b.CenterY),
        new(b.CenterX, b.Bottom),
        new(b.X, b.CenterY)
    ];

    static IReadOnlyList<DiagramPoint> Parallelogram(DiagramRect b)
    {
        var inset = Math.Min(b.Width * ParallelogramInset, b.Width / 2);
        return
        [
            new(b.X + inset, b.Y),
            new(b.Right, b.Y),
            new(b.Right - inset, b.Bottom),
            new(b.X, b.Bottom)
        ];
    }

    static IReadOnlyList<DiagramPoint> Hexagon(DiagramRect b)
    {
        var inset = Math.Min(b.Width * HexagonInset, b.Width / 2);
        return
        [
            new(b.X + inset, b.Y),
            new(b.Right - inset, b.Y),
            new(b.Right, b.CenterY),
            new(b.Right - inset, b.Bottom),
            new(b.X + inset, b.Bottom),
            new(b.X, b.CenterY)
        ];
    }

    static IReadOnlyList<DiagramPoint> Triangle(DiagramRect b) =>
    [
        new(b.CenterX, b.Y),
        new(b.Right, b.Bottom),
        new(b.X, b.Bottom)
    ];

    static IReadOnlyList<DiagramPoint> RoundedRect(DiagramRect b, double radius)
    {
        var r = Math.Min(radius, Math.Min(b.Width, b.Height) / 2);
        if (r <= Epsilon)
            return Rect(b);

        var points = new List<DiagramPoint>((ArcSegments + 1) * 4);

        // Corner centres, clockwise from top-left, with the sweep each one covers.
        AddArc(points, b.X + r, b.Y + r, r, Math.PI, Math.PI * 1.5);
        AddArc(points, b.Right - r, b.Y + r, r, Math.PI * 1.5, Math.PI * 2);
        AddArc(points, b.Right - r, b.Bottom - r, r, 0, Math.PI * 0.5);
        AddArc(points, b.X + r, b.Bottom - r, r, Math.PI * 0.5, Math.PI);

        return points;
    }

    static void AddArc(List<DiagramPoint> into, double cx, double cy, double r, double from, double to)
    {
        for (var i = 0; i <= ArcSegments; i++)
        {
            var angle = from + ((to - from) * i / ArcSegments);
            into.Add(new DiagramPoint(cx + (Math.Cos(angle) * r), cy + (Math.Sin(angle) * r)));
        }
    }

    static IReadOnlyList<DiagramPoint> EllipseOutline(DiagramRect b)
    {
        var count = ArcSegments * 4;
        var points = new List<DiagramPoint>(count);
        var rx = b.Width / 2;
        var ry = b.Height / 2;

        for (var i = 0; i < count; i++)
        {
            var angle = Math.PI * 2 * i / count;
            points.Add(new DiagramPoint(b.CenterX + (Math.Cos(angle) * rx), b.CenterY + (Math.Sin(angle) * ry)));
        }

        return points;
    }

    static DiagramPoint EllipseIntersect(DiagramRect box, double dx, double dy)
    {
        // Scale the direction into the unit circle, normalise there, and scale back out. One divide
        // per axis, and it is exact.
        var rx = Math.Max(box.Width / 2, Epsilon);
        var ry = Math.Max(box.Height / 2, Epsilon);

        var nx = dx / rx;
        var ny = dy / ry;
        var len = Math.Sqrt((nx * nx) + (ny * ny));

        if (len < Epsilon)
            return new DiagramPoint(box.CenterX, box.Bottom);

        return new DiagramPoint(
            box.CenterX + (nx / len * rx),
            box.CenterY + (ny / len * ry)
        );
    }

    /// <summary>
    /// Walks the polygon's edges and returns the nearest crossing of the ray from
    /// <paramref name="origin"/> in direction (<paramref name="dx"/>, <paramref name="dy"/>).
    /// </summary>
    static DiagramPoint? PolygonIntersect(
        IReadOnlyList<DiagramPoint> polygon,
        DiagramPoint origin,
        double dx,
        double dy
    )
    {
        if (polygon.Count < 2)
            return null;

        var best = double.MaxValue;
        DiagramPoint? hit = null;

        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];

            var ex = b.X - a.X;
            var ey = b.Y - a.Y;

            // Ray: origin + t*(dx,dy). Edge: a + u*(ex,ey). Solve the 2x2; a zero determinant means
            // the ray is parallel to this edge, which contributes no crossing worth taking.
            var det = (dx * ey) - (dy * ex);
            if (Math.Abs(det) < Epsilon)
                continue;

            var ox = a.X - origin.X;
            var oy = a.Y - origin.Y;

            var t = ((ox * ey) - (oy * ex)) / det;
            var u = ((ox * dy) - (oy * dx)) / det;

            if (t < 0 || u < 0 || u > 1)
                continue;

            if (t < best)
            {
                best = t;
                hit = new DiagramPoint(origin.X + (dx * t), origin.Y + (dy * t));
            }
        }

        return hit;
    }

    /// <summary>Even-odd containment by ray casting. The outlines here are convex, but this does not rely on it.</summary>
    static bool PolygonContains(IReadOnlyList<DiagramPoint> polygon, DiagramPoint point)
    {
        if (polygon.Count < 3)
            return false;

        var inside = false;

        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];

            if (a.Y > point.Y != b.Y > point.Y &&
                point.X < ((b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y)) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
