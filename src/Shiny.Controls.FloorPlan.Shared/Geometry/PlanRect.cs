namespace Shiny.Controls.FloorPlan;

/// <summary>
/// An axis-aligned rectangle in plan coordinates.
/// </summary>
public readonly record struct PlanRect(float X, float Y, float Width, float Height)
{
    public float Left => this.X;
    public float Top => this.Y;
    public float Right => this.X + this.Width;
    public float Bottom => this.Y + this.Height;
    public PlanPoint Center => new(this.X + this.Width / 2, this.Y + this.Height / 2);

    public bool Contains(PlanPoint point) =>
        point.X >= this.Left && point.X <= this.Right &&
        point.Y >= this.Top && point.Y <= this.Bottom;

    public bool IntersectsWith(PlanRect other) =>
        this.Left < other.Right && this.Right > other.Left &&
        this.Top < other.Bottom && this.Bottom > other.Top;

    public PlanRect Inflate(float amount) =>
        new(this.X - amount, this.Y - amount, this.Width + 2 * amount, this.Height + 2 * amount);
}
