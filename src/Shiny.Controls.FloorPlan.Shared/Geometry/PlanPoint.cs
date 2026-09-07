namespace Shiny.Controls.FloorPlan;

/// <summary>
/// A point in plan (document) coordinates, in plan units.
/// </summary>
/// <remarks>
/// Plan units are whatever the document says they are - the engine never assumes pixels, inches or
/// millimetres. The camera is the only thing that turns them into screen coordinates.
/// </remarks>
public readonly record struct PlanPoint(float X, float Y)
{
    public static PlanPoint Zero => new(0, 0);

    public float DistanceTo(PlanPoint other)
    {
        var dx = this.X - other.X;
        var dy = this.Y - other.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public PlanPoint Offset(float dx, float dy) => new(this.X + dx, this.Y + dy);
}
