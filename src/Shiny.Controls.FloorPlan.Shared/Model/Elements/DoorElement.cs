namespace Shiny.Controls.FloorPlan;

public enum DoorType
{
    Single,
    Double,
    Sliding
}

/// <summary>A door in a wall, drawn with its swing arc.</summary>
public class DoorElement : FloorPlanElement
{
    /// <summary>Width of the opening, which is also the radius of the swing.</summary>
    public float Width { get; set; } = 40;

    /// <summary>How far the leaf opens, in degrees.</summary>
    public float SwingAngle { get; set; } = 90;

    public DoorType DoorType { get; set; } = DoorType.Single;

    public override PlanRect GetBounds() =>
        new(this.Transform.X, this.Transform.Y, this.Width, this.Width);
}
