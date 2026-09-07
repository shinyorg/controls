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
    public double Right => this.X + this.Width;
    public double Bottom => this.Y + this.Height;
    public double CenterX => this.X + (this.Width / 2);
    public double CenterY => this.Y + (this.Height / 2);
    public bool IsEmpty => this.Width <= 0 || this.Height <= 0;

    public bool Contains(double x, double y) =>
        x >= this.X && x <= this.Right && y >= this.Y && y <= this.Bottom;

    /// <summary>Grows the rectangle on every side, for hit testing with a finger-sized tolerance.</summary>
    public GanttRect Inflate(double by) => new(this.X - by, this.Y - by, this.Width + (by * 2), this.Height + (by * 2));
}
