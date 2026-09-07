using System.Globalization;
using System.Text;
using Shiny.Controls.Diagramming;

namespace Shiny.Blazor.Controls.Diagram;

/// <summary>What is currently selected on a diagram.</summary>
/// <param name="Nodes">The selected nodes.</param>
/// <param name="Connections">The selected connections.</param>
public sealed record DiagramSelection(
    IReadOnlyList<DiagramNode> Nodes,
    IReadOnlyList<DiagramConnection> Connections
)
{
    /// <summary>Nothing selected.</summary>
    public static readonly DiagramSelection Empty = new([], []);

    /// <summary>True when nothing is selected.</summary>
    public bool IsEmpty => this.Nodes.Count == 0 && this.Connections.Count == 0;
}


/// <summary>
/// An edit about to be applied, and the chance to stop it.
/// </summary>
/// <remarks>
/// The plan describes the whole gesture, not the one thing that was clicked - deleting a node lists
/// the connections going with it - so a handler decides with the full consequence in front of it.
/// </remarks>
/// <param name="Plan">What the gesture is about to do.</param>
public sealed record DiagramEditingEventArgs(DiagramEditPlan Plan)
{
    /// <summary>Set to true to abandon the edit. Everything it had already applied is reverted.</summary>
    public bool Cancel { get; set; }
}


/// <summary>The arrowhead or terminator drawn at one end of a connection.</summary>
/// <param name="Path">SVG path data for the cap.</param>
/// <param name="Filled">Whether it is filled or only stroked.</param>
readonly record struct CapShape(string Path, bool Filled)
{
    /// <summary>How long an arrowhead is, in diagram units.</summary>
    const double Length = 10;

    /// <summary>How wide an arrowhead is at its base.</summary>
    const double Width = 7;

    /// <summary>Builds the cap for one end of a route.</summary>
    /// <param name="cap">Which cap to draw.</param>
    /// <param name="tip">Where the line meets the node.</param>
    /// <param name="direction">A unit vector pointing along the line, away from the node.</param>
    public static CapShape Build(DiagramConnectionCap cap, DiagramPoint tip, DiagramPoint direction)
    {
        // The perpendicular, for the two base corners.
        var px = -direction.Y;
        var py = direction.X;

        var baseX = tip.X - (direction.X * Length);
        var baseY = tip.Y - (direction.Y * Length);

        return cap switch
        {
            DiagramConnectionCap.Arrow => new CapShape(
                Poly(
                    (baseX + (px * Width / 2), baseY + (py * Width / 2)),
                    (tip.X, tip.Y),
                    (baseX - (px * Width / 2), baseY - (py * Width / 2))
                ),
                false
            ),

            DiagramConnectionCap.FilledArrow => new CapShape(
                Poly(
                    (tip.X, tip.Y),
                    (baseX + (px * Width / 2), baseY + (py * Width / 2)),
                    (baseX - (px * Width / 2), baseY - (py * Width / 2))
                ) + " Z",
                true
            ),

            DiagramConnectionCap.Circle => new CapShape(Circle(tip, direction, Width / 2), true),

            DiagramConnectionCap.Diamond => new CapShape(
                Poly(
                    (tip.X, tip.Y),
                    (baseX + (px * Width / 2), baseY + (py * Width / 2)),
                    (tip.X - (direction.X * Length * 2), tip.Y - (direction.Y * Length * 2)),
                    (baseX - (px * Width / 2), baseY - (py * Width / 2))
                ) + " Z",
                true
            ),

            _ => new CapShape(string.Empty, false)
        };
    }

    static string Circle(DiagramPoint tip, DiagramPoint direction, double radius)
    {
        // Sat back along the line by its own radius, so the dot ends where the line ends rather than
        // half of it hanging inside the node.
        var cx = tip.X - (direction.X * radius);
        var cy = tip.Y - (direction.Y * radius);

        return new StringBuilder()
            .Append("M ").Append(N(cx - radius)).Append(' ').Append(N(cy))
            .Append(" a ").Append(N(radius)).Append(' ').Append(N(radius)).Append(" 0 1 0 ")
            .Append(N(radius * 2)).Append(" 0")
            .Append(" a ").Append(N(radius)).Append(' ').Append(N(radius)).Append(" 0 1 0 ")
            .Append(N(-radius * 2)).Append(" 0")
            .ToString();
    }

    static string Poly(params (double X, double Y)[] points)
    {
        var builder = new StringBuilder();

        for (var i = 0; i < points.Length; i++)
        {
            builder
                .Append(i == 0 ? "M " : " L ")
                .Append(N(points[i].X)).Append(' ').Append(N(points[i].Y));
        }

        return builder.ToString();
    }

    static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
