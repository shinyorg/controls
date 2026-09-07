namespace Shiny.Controls.FloorPlan;

/// <summary>
/// Where an element sits on the plan, and how it is turned and scaled there.
/// </summary>
/// <remarks>
/// A class rather than a struct, and mutable: the tools move an element by writing
/// <see cref="X"/> and <see cref="Y"/> on the transform the element already holds. A value type here
/// would mean every drag went through a copy-back on the element, and a missed one would silently
/// drop the move.
/// </remarks>
public class PlanTransform
{
    public float X { get; set; }
    public float Y { get; set; }

    /// <summary>Clockwise rotation in degrees, about the element's own anchor.</summary>
    public float Rotation { get; set; }

    public float ScaleX { get; set; } = 1;
    public float ScaleY { get; set; } = 1;
}
