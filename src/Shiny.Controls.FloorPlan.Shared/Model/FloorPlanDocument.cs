namespace Shiny.Controls.FloorPlan;

/// <summary>
/// One floor plan: its extents, its grid, everything on it, and the stencils its custom elements
/// point at.
/// </summary>
public class FloorPlanDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Plan width, in plan units.</summary>
    public float Width { get; set; } = 2000;

    /// <summary>Plan height, in plan units.</summary>
    public float Height { get; set; } = 1500;

    /// <summary>Spacing of the drawn grid, and the step everything snaps to.</summary>
    public float GridSize { get; set; } = 20;

    public List<FloorPlanElement> Elements { get; set; } = new();

    public List<ShapeDefinition> CustomShapes { get; set; } = new();
}
