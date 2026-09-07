namespace Shiny.Controls.FloorPlan;

/// <summary>An instance of one of the document's <see cref="ShapeDefinition"/> stencils.</summary>
public class CustomElement : FloorPlanElement
{
    /// <summary>The <see cref="ShapeDefinition.Id"/> this instance draws.</summary>
    /// <remarks>
    /// A dangling id is not an error: the renderer falls back to a boxed question mark, so a document
    /// that lost a stencil still opens and still shows you where the missing thing was.
    /// </remarks>
    public string ShapeDefinitionId { get; set; } = string.Empty;

    public float Width { get; set; } = 50;
    public float Height { get; set; } = 50;

    public override PlanRect GetBounds() =>
        new(this.Transform.X, this.Transform.Y, this.Width, this.Height);
}
