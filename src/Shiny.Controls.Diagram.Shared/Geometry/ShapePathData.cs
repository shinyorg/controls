using System.Globalization;
using System.Text;

namespace Shiny.Controls.Diagramming;

/// <summary>
/// Renders a shape as SVG path data.
/// </summary>
/// <remarks>
/// <para>
/// Used by the Blazor control, which draws every node as one <c>&lt;path&gt;</c>. It is here rather
/// than there so the outline a line is anchored to and the outline that is drawn come from one
/// definition - the proportions live on <see cref="ShapeGeometry"/> as shared constants and neither
/// side gets its own copy to drift.
/// </para>
/// <para>
/// True arcs, not the tessellated polygon <see cref="ShapeGeometry.Outline"/> returns. That polygon
/// is precise enough to anchor and hit-test against and visibly faceted when a circle is zoomed to
/// fill a screen, which is a difference worth thirty lines.
/// </para>
/// </remarks>
public static class ShapePathData
{
    /// <summary>
    /// The shape's outline as an SVG path.
    /// </summary>
    /// <param name="shape">The shape to trace.</param>
    /// <param name="bounds">The box the shape fills.</param>
    /// <param name="cornerRadius">Corner radius for the rounded shapes. Negative takes the default.</param>
    /// <returns>A path suitable for an SVG <c>d</c> attribute. Empty when the box has no area.</returns>
    public static string For(DiagramNodeShape shape, DiagramRect bounds, double cornerRadius = -1)
    {
        if (bounds.IsEmpty)
            return string.Empty;

        var radius = cornerRadius < 0 ? ShapeGeometry.DefaultCornerRadius : cornerRadius;

        return shape switch
        {
            DiagramNodeShape.RoundedRectangle => RoundedRect(bounds, radius),
            DiagramNodeShape.Stadium => RoundedRect(bounds, bounds.Height / 2),
            DiagramNodeShape.Circle => Ellipse(ShapeGeometry.CircleBounds(bounds)),
            DiagramNodeShape.Ellipse => Ellipse(bounds),
            DiagramNodeShape.Cylinder => Cylinder(bounds),
            DiagramNodeShape.Document => Document(bounds),
            _ => Polygon(ShapeGeometry.Outline(shape, bounds, radius))
        };
    }

    /// <summary>
    /// A connection's route as an SVG path.
    /// </summary>
    /// <param name="points">The route, as the engine produced it.</param>
    /// <param name="router">Which router produced it, since a bezier's points are control points.</param>
    public static string ForRoute(IReadOnlyList<DiagramPoint> points, DiagramConnectionRouter router)
    {
        if (points.Count < 2)
            return string.Empty;

        var builder = new StringBuilder();
        builder.Append("M ").Append(N(points[0].X)).Append(' ').Append(N(points[0].Y));

        if (router == DiagramConnectionRouter.Bezier && points.Count == 4)
        {
            builder
                .Append(" C ").Append(N(points[1].X)).Append(' ').Append(N(points[1].Y))
                .Append(' ').Append(N(points[2].X)).Append(' ').Append(N(points[2].Y))
                .Append(' ').Append(N(points[3].X)).Append(' ').Append(N(points[3].Y));

            return builder.ToString();
        }

        for (var i = 1; i < points.Count; i++)
            builder.Append(" L ").Append(N(points[i].X)).Append(' ').Append(N(points[i].Y));

        return builder.ToString();
    }

    static string Polygon(IReadOnlyList<DiagramPoint> points)
    {
        if (points.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        builder.Append("M ").Append(N(points[0].X)).Append(' ').Append(N(points[0].Y));

        for (var i = 1; i < points.Count; i++)
            builder.Append(" L ").Append(N(points[i].X)).Append(' ').Append(N(points[i].Y));

        return builder.Append(" Z").ToString();
    }

    static string RoundedRect(DiagramRect b, double radius)
    {
        var r = Math.Min(radius, Math.Min(b.Width, b.Height) / 2);

        if (r <= 0)
            return Polygon(ShapeGeometry.Outline(DiagramNodeShape.Rectangle, b));

        return new StringBuilder()
            .Append("M ").Append(N(b.X + r)).Append(' ').Append(N(b.Y))
            .Append(" H ").Append(N(b.Right - r))
            .Append(" A ").Append(N(r)).Append(' ').Append(N(r)).Append(" 0 0 1 ")
            .Append(N(b.Right)).Append(' ').Append(N(b.Y + r))
            .Append(" V ").Append(N(b.Bottom - r))
            .Append(" A ").Append(N(r)).Append(' ').Append(N(r)).Append(" 0 0 1 ")
            .Append(N(b.Right - r)).Append(' ').Append(N(b.Bottom))
            .Append(" H ").Append(N(b.X + r))
            .Append(" A ").Append(N(r)).Append(' ').Append(N(r)).Append(" 0 0 1 ")
            .Append(N(b.X)).Append(' ').Append(N(b.Bottom - r))
            .Append(" V ").Append(N(b.Y + r))
            .Append(" A ").Append(N(r)).Append(' ').Append(N(r)).Append(" 0 0 1 ")
            .Append(N(b.X + r)).Append(' ').Append(N(b.Y))
            .Append(" Z")
            .ToString();
    }

    static string Ellipse(DiagramRect b)
    {
        var rx = b.Width / 2;
        var ry = b.Height / 2;

        // Two half-arcs, because a single arc from a point back to itself is a degenerate case SVG
        // renderers are entitled to draw as nothing.
        return new StringBuilder()
            .Append("M ").Append(N(b.X)).Append(' ').Append(N(b.CenterY))
            .Append(" A ").Append(N(rx)).Append(' ').Append(N(ry)).Append(" 0 1 0 ")
            .Append(N(b.Right)).Append(' ').Append(N(b.CenterY))
            .Append(" A ").Append(N(rx)).Append(' ').Append(N(ry)).Append(" 0 1 0 ")
            .Append(N(b.X)).Append(' ').Append(N(b.CenterY))
            .Append(" Z")
            .ToString();
    }

    static string Cylinder(DiagramRect b)
    {
        var cap = b.Height * ShapeGeometry.CylinderCap;
        var rx = b.Width / 2;

        // The silhouette only - the ellipse across the top that makes it read as a cylinder is drawn
        // separately by the host, since it is a second sub-path that must not be filled over the body.
        return new StringBuilder()
            .Append("M ").Append(N(b.X)).Append(' ').Append(N(b.Y + cap))
            .Append(" A ").Append(N(rx)).Append(' ').Append(N(cap)).Append(" 0 0 1 ")
            .Append(N(b.Right)).Append(' ').Append(N(b.Y + cap))
            .Append(" V ").Append(N(b.Bottom - cap))
            .Append(" A ").Append(N(rx)).Append(' ').Append(N(cap)).Append(" 0 0 1 ")
            .Append(N(b.X)).Append(' ').Append(N(b.Bottom - cap))
            .Append(" Z")
            .ToString();
    }

    /// <summary>The lid ellipse a cylinder needs drawn over its body, or empty for any other shape.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="bounds">The box the shape fills.</param>
    public static string CylinderLid(DiagramNodeShape shape, DiagramRect bounds)
    {
        if (shape != DiagramNodeShape.Cylinder || bounds.IsEmpty)
            return string.Empty;

        var cap = bounds.Height * ShapeGeometry.CylinderCap;
        var rx = bounds.Width / 2;

        return new StringBuilder()
            .Append("M ").Append(N(bounds.X)).Append(' ').Append(N(bounds.Y + cap))
            .Append(" A ").Append(N(rx)).Append(' ').Append(N(cap)).Append(" 0 0 0 ")
            .Append(N(bounds.Right)).Append(' ').Append(N(bounds.Y + cap))
            .ToString();
    }

    static string Document(DiagramRect b)
    {
        var wave = b.Height * ShapeGeometry.DocumentWave;
        var quarter = b.Width / 4;

        return new StringBuilder()
            .Append("M ").Append(N(b.X)).Append(' ').Append(N(b.Y))
            .Append(" H ").Append(N(b.Right))
            .Append(" V ").Append(N(b.Bottom - wave))
            .Append(" C ").Append(N(b.Right - quarter)).Append(' ').Append(N(b.Bottom - (wave * 2)))
            .Append(' ').Append(N(b.X + quarter)).Append(' ').Append(N(b.Bottom))
            .Append(' ').Append(N(b.X)).Append(' ').Append(N(b.Bottom - wave))
            .Append(" Z")
            .ToString();
    }

    /// <summary>
    /// Formats a number for a path.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <remarks>
    /// Invariant culture, always. An SVG path built under a comma-decimal locale is not merely
    /// mis-scaled - "12,5 30" is a valid path meaning something else entirely, so the shape silently
    /// draws wrong rather than failing.
    /// </remarks>
    static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
