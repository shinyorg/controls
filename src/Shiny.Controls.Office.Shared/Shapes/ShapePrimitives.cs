using Shiny.Controls.Office.Spreadsheet;

namespace Shiny.Controls.Office.Shapes;

/// <summary>The preset geometries the office controls draw natively.</summary>
/// <remarks>
/// <para>
/// DrawingML defines roughly 190 preset shapes, each as a parameterised path with its own guide
/// formulas. Rather than half-implement that, the common ones are drawn properly and everything else
/// falls back to <see cref="Rectangle"/> — a wrong-but-present shape reads better than a hole, and the
/// unsupported sink says which ones were substituted.
/// </para>
/// <para>
/// Shared by the slide and document sides on purpose. A <c>a:prstGeom</c> means the same thing in a
/// <c>p:sp</c> on a slide and in a <c>wps:wsp</c> inside a Word drawing, so one enum, one path builder
/// and one painter path serve both — the shape a user draws in a deck and the one they drop into a
/// document are the same shape.
/// </para>
/// </remarks>
public enum ShapeGeometry
{
    Rectangle,
    RoundedRectangle,
    Ellipse,
    Triangle,
    RightTriangle,
    Diamond,
    Line,
    RightArrow,
    LeftArrow,
    UpArrow,
    DownArrow,
    Pentagon,
    Hexagon,
    Star5,
    Chevron,
    Parallelogram,
    Trapezoid,
    Plus,
    Can,
    Cloud,
    None
}

public sealed record ShapeFill
{
    public static readonly ShapeFill None = new();

    public ArgbColor? Solid { get; init; }

    /// <summary>Gradient stops, ordered by position. Rendered as a linear gradient.</summary>
    public IReadOnlyList<(double Position, ArgbColor Color)> GradientStops { get; init; } = [];

    /// <summary>Gradient direction in degrees, clockwise from the positive x axis.</summary>
    public double GradientAngle { get; init; }

    /// <summary>
    /// An encoded picture stretched over the area, for a <c>a:blipFill</c> — a slide background set
    /// from a photograph, say. Null for every other fill.
    /// </summary>
    public byte[]? Image { get; init; }

    public bool IsEmpty => this.Solid is null && this.GradientStops.Count == 0 && this.Image is null;
}

/// <summary>The dash patterns DrawingML's <c>a:prstDash</c> offers that the editor writes.</summary>
public enum LineDash
{
    Solid,
    Dot,
    Dash,
    LargeDash,
    DashDot,
    LongDashDot,
    LongDashDotDot,
    SystemDash,
    SystemDot
}

public sealed record ShapeOutline(ArgbColor Color, double Width, bool Dashed = false)
{
    /// <summary>
    /// The exact dash pattern. <see cref="Dashed"/> is kept for the painters that only care whether a
    /// line is broken; this is what the slide painter and the ribbon read.
    /// </summary>
    public LineDash Dash { get; init; } = Dashed ? LineDash.Dash : LineDash.Solid;
}

/// <summary>
/// A drop shadow behind a shape — DrawingML's <c>a:outerShdw</c>, which is what PowerPoint's Shape
/// Effects ▸ Shadow presets write.
/// </summary>
/// <param name="Color">Shadow colour, alpha included.</param>
/// <param name="Blur">Blur radius, in pixels.</param>
/// <param name="Distance">How far the shadow is offset, in pixels.</param>
/// <param name="Direction">Direction of the offset in degrees, clockwise from the positive x axis.</param>
public sealed record ShapeShadow(ArgbColor Color, double Blur, double Distance, double Direction)
{
    public double OffsetX => this.Distance * Math.Cos(this.Direction * Math.PI / 180);

    public double OffsetY => this.Distance * Math.Sin(this.Direction * Math.PI / 180);
}
