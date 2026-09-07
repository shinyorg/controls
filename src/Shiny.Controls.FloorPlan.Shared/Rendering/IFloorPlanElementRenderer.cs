using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

/// <summary>
/// Draws one kind of element, and describes its shape for hit-testing.
/// </summary>
/// <remarks>
/// Register your own with <see cref="FloorPlanEngine.RegisterRenderer"/> to replace a built-in look,
/// or to draw an element type of your own. The outline path is what a pointer is tested against, so a
/// renderer that draws a circle should return a circle - otherwise the corners of its bounding box
/// are clickable and the control feels wrong for a reason nobody can point at.
/// </remarks>
public interface IFloorPlanElementRenderer
{
    /// <summary>The element type this renderer is registered for. Matched exactly, not by assignment.</summary>
    Type ElementType { get; }

    void Render(SKCanvas canvas, FloorPlanElement element, FloorPlanRenderContext context);

    /// <summary>The element's silhouette in plan coordinates. The caller disposes it.</summary>
    SKPath GetOutlinePath(FloorPlanElement element);
}
