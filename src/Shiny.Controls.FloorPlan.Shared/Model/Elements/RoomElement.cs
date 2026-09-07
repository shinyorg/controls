namespace Shiny.Controls.FloorPlan;

/// <summary>A rectangular room, optionally labelled.</summary>
public class RoomElement : FloorPlanElement
{
    public float Width { get; set; } = 200;
    public float Height { get; set; } = 150;

    /// <summary>
    /// A free-form outline. Reserved: the renderer draws the rectangle today, and a polygon room is
    /// the next thing this type grows.
    /// </summary>
    public List<PlanPoint>? Polygon { get; set; }

    /// <summary>Text drawn in the middle of the room. Distinct from <see cref="FloorPlanElement.Name"/>,
    /// which is what a tap reports - a room can be called "Room 3.14" and read "Boardroom".</summary>
    public string? Label { get; set; }

    public override PlanRect GetBounds() =>
        new(this.Transform.X, this.Transform.Y, this.Width, this.Height);
}
