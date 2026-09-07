namespace Shiny.Controls.FloorPlan;

public enum FurnitureKind
{
    Desk,
    Chair,
    Table,
    Bookshelf,
    Sofa,
    FileCabinet
}

/// <summary>A piece of furniture. The kind picks both the silhouette and the default colour.</summary>
public class FurnitureElement : FloorPlanElement
{
    public FurnitureKind Kind { get; set; } = FurnitureKind.Desk;
    public float Width { get; set; } = 60;
    public float Height { get; set; } = 30;

    public override PlanRect GetBounds() =>
        new(this.Transform.X, this.Transform.Y, this.Width, this.Height);
}
