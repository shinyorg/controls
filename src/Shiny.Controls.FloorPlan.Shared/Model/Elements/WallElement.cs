namespace Shiny.Controls.FloorPlan;

/// <summary>A straight wall segment of a given thickness.</summary>
/// <remarks>
/// <see cref="Start"/> and <see cref="End"/> are relative to the transform, so a wall moves the way
/// everything else does - by writing the transform - without the tools having to know it is stored as
/// two points rather than a box.
/// </remarks>
public class WallElement : FloorPlanElement
{
    public PlanPoint Start { get; set; }
    public PlanPoint End { get; set; }
    public float Thickness { get; set; } = 10;

    public override PlanRect GetBounds()
    {
        var x = MathF.Min(this.Start.X, this.End.X) + this.Transform.X;
        var y = MathF.Min(this.Start.Y, this.End.Y) + this.Transform.Y;
        var w = MathF.Abs(this.End.X - this.Start.X);
        var h = MathF.Abs(this.End.Y - this.Start.Y);

        // A perfectly horizontal or vertical wall has zero extent on one axis. Widening it to the
        // thickness is what gives it something to hit-test and something to draw handles around.
        return new PlanRect(x, y, MathF.Max(w, this.Thickness), MathF.Max(h, this.Thickness));
    }
}
