namespace Shiny.Controls.FloorPlan;

/// <summary>
/// A workstation: a partitioned bay with a desk in it, and optionally someone sitting there.
/// </summary>
/// <remarks>
/// The reason the viewer mode exists. A seating chart is a floor plan whose cubicles carry names, and
/// tapping one is the whole interaction.
/// </remarks>
public class CubicleElement : FloorPlanElement
{
    public float Width { get; set; } = 80;
    public float Height { get; set; } = 80;

    /// <summary>Who sits here. Drawn inside the bay when set.</summary>
    public string? Occupant { get; set; }

    public override PlanRect GetBounds() =>
        new(this.Transform.X, this.Transform.Y, this.Width, this.Height);
}
