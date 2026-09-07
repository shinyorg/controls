namespace Shiny.Controls.Gantt;

/// <summary>
/// A point in timeline space.
/// </summary>
/// <remarks>
/// Declared locally rather than taken from Microsoft.Maui.Graphics: this package is shared with the
/// Blazor host, and pulling a MAUI graphics dependency into a WebAssembly payload for the sake of two
/// doubles is not a trade worth making.
/// </remarks>
public readonly record struct GanttPoint(double X, double Y);


/// <summary>A rectangle in timeline space.</summary>
public readonly record struct GanttRect(double X, double Y, double Width, double Height)
{
    /// <summary>The right edge.</summary>
    public double Right => this.X + this.Width;

    /// <summary>The bottom edge.</summary>
    public double Bottom => this.Y + this.Height;

    /// <summary>Horizontal midpoint, where a dependency arrow leaves a milestone.</summary>
    public double CenterX => this.X + (this.Width / 2);

    /// <summary>Vertical midpoint, where a dependency arrow enters and leaves a bar.</summary>
    public double CenterY => this.Y + (this.Height / 2);

    /// <summary>True when the rectangle has no area, which is what an unresolved task measures to.</summary>
    public bool IsEmpty => this.Width <= 0 || this.Height <= 0;

    /// <summary>Whether a point falls inside the rectangle, edges included.</summary>
    /// <param name="x">Horizontal position, in timeline space.</param>
    /// <param name="y">Vertical position, in timeline space.</param>
    public bool Contains(double x, double y) =>
        x >= this.X && x <= this.Right && y >= this.Y && y <= this.Bottom;

    /// <summary>Grows the rectangle on every side, for hit testing with a finger-sized tolerance.</summary>
    public GanttRect Inflate(double by) => new(this.X - by, this.Y - by, this.Width + (by * 2), this.Height + (by * 2));
}
