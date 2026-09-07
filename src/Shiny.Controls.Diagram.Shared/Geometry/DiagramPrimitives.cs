namespace Shiny.Controls.Diagramming;

/// <summary>
/// A point in diagram space.
/// </summary>
/// <remarks>
/// Declared locally rather than taken from Microsoft.Maui.Graphics, for the same reason
/// <c>GanttPoint</c> is: this package is shared with the Blazor host, and pulling a MAUI graphics
/// dependency into a WebAssembly payload for the sake of two doubles is not a trade worth making.
/// </remarks>
public readonly record struct DiagramPoint(double X, double Y)
{
    /// <summary>The origin.</summary>
    public static readonly DiagramPoint Zero = new(0, 0);

    /// <summary>Straight-line distance to another point.</summary>
    public double DistanceTo(DiagramPoint other)
    {
        var dx = other.X - this.X;
        var dy = other.Y - this.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>The point moved by an offset.</summary>
    public DiagramPoint Offset(double dx, double dy) => new(this.X + dx, this.Y + dy);
}


/// <summary>A width and height in diagram space.</summary>
public readonly record struct DiagramSize(double Width, double Height)
{
    /// <summary>A zero size, which is what an unmeasured node reports.</summary>
    public static readonly DiagramSize Empty = new(0, 0);

    /// <summary>True when the size has no area.</summary>
    public bool IsEmpty => this.Width <= 0 || this.Height <= 0;
}


/// <summary>A rectangle in diagram space, which is the box a node occupies.</summary>
public readonly record struct DiagramRect(double X, double Y, double Width, double Height)
{
    /// <summary>An empty rectangle at the origin.</summary>
    public static readonly DiagramRect Empty = new(0, 0, 0, 0);

    /// <summary>Builds a rectangle from its centre rather than its top-left corner.</summary>
    /// <param name="centerX">Horizontal midpoint.</param>
    /// <param name="centerY">Vertical midpoint.</param>
    /// <param name="width">Full width.</param>
    /// <param name="height">Full height.</param>
    public static DiagramRect FromCenter(double centerX, double centerY, double width, double height) =>
        new(centerX - (width / 2), centerY - (height / 2), width, height);

    /// <summary>The right edge.</summary>
    public double Right => this.X + this.Width;

    /// <summary>The bottom edge.</summary>
    public double Bottom => this.Y + this.Height;

    /// <summary>Horizontal midpoint.</summary>
    public double CenterX => this.X + (this.Width / 2);

    /// <summary>Vertical midpoint.</summary>
    public double CenterY => this.Y + (this.Height / 2);

    /// <summary>The centre, as a point.</summary>
    public DiagramPoint Center => new(this.CenterX, this.CenterY);

    /// <summary>The top-left corner, as a point.</summary>
    public DiagramPoint Location => new(this.X, this.Y);

    /// <summary>The size, without the position.</summary>
    public DiagramSize Size => new(this.Width, this.Height);

    /// <summary>True when the rectangle has no area, which is what an unmeasured node reports.</summary>
    public bool IsEmpty => this.Width <= 0 || this.Height <= 0;

    /// <summary>Whether a point falls inside the rectangle, edges included.</summary>
    /// <param name="x">Horizontal position, in diagram space.</param>
    /// <param name="y">Vertical position, in diagram space.</param>
    public bool Contains(double x, double y) =>
        x >= this.X && x <= this.Right && y >= this.Y && y <= this.Bottom;

    /// <summary>Whether a point falls inside the rectangle, edges included.</summary>
    /// <param name="point">The point to test.</param>
    public bool Contains(DiagramPoint point) => this.Contains(point.X, point.Y);

    /// <summary>Whether another rectangle overlaps this one, which is how a marquee selects.</summary>
    /// <param name="other">The rectangle to test against.</param>
    public bool IntersectsWith(DiagramRect other) =>
        other.X <= this.Right && other.Right >= this.X &&
        other.Y <= this.Bottom && other.Bottom >= this.Y;

    /// <summary>Grows the rectangle on every side, for hit testing with a finger-sized tolerance.</summary>
    /// <param name="by">How far to grow each edge.</param>
    public DiagramRect Inflate(double by) =>
        new(this.X - by, this.Y - by, this.Width + (by * 2), this.Height + (by * 2));

    /// <summary>The rectangle moved by an offset, which is what dragging a node produces.</summary>
    /// <param name="dx">Horizontal movement.</param>
    /// <param name="dy">Vertical movement.</param>
    public DiagramRect Offset(double dx, double dy) =>
        new(this.X + dx, this.Y + dy, this.Width, this.Height);

    /// <summary>The smallest rectangle containing both this one and another.</summary>
    /// <param name="other">The rectangle to include.</param>
    public DiagramRect Union(DiagramRect other)
    {
        var left = Math.Min(this.X, other.X);
        var top = Math.Min(this.Y, other.Y);
        var right = Math.Max(this.Right, other.Right);
        var bottom = Math.Max(this.Bottom, other.Bottom);
        return new DiagramRect(left, top, right - left, bottom - top);
    }

    /// <summary>Builds a rectangle from two opposite corners, in any order - which is what a marquee drag gives.</summary>
    /// <param name="a">One corner.</param>
    /// <param name="b">The opposite corner.</param>
    public static DiagramRect FromCorners(DiagramPoint a, DiagramPoint b) =>
        new(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Abs(b.X - a.X),
            Math.Abs(b.Y - a.Y)
        );
}
