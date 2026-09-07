namespace Shiny.Controls.FloorPlan;

/// <summary>
/// Finds what is under a point, testing renderers' own outlines rather than bounding boxes.
/// </summary>
/// <remarks>
/// Handles come first and elements come topmost-first, which is the order the user perceives: a
/// handle sitting on a neighbouring element belongs to the selection, and a click where two elements
/// overlap belongs to the one drawn last.
/// </remarks>
public class FloorPlanHitTester
{
    readonly Dictionary<Type, IFloorPlanElementRenderer> renderers;

    public FloorPlanHitTester(Dictionary<Type, IFloorPlanElementRenderer> renderers)
        => this.renderers = renderers;

    public FloorPlanHitResult? HitTest(
        FloorPlanDocument document,
        float worldX,
        float worldY,
        FloorPlanEditorState state,
        float zoom,
        float handleSize = 8f
    )
    {
        // Handles are only live in Edit mode - they are not drawn in View mode, and something you
        // cannot see should not be swallowing your taps.
        if (state.Mode == FloorPlanEditorMode.Edit)
        {
            foreach (var selected in state.SelectedElements)
            {
                var handleIndex = FloorPlanSelectionRenderer.HitTestHandle(selected, worldX, worldY, zoom, handleSize);
                if (handleIndex >= 0)
                    return new FloorPlanHitResult(selected, handleIndex);
            }
        }

        // Reverse paint order: the element on top is the one you meant.
        var ordered = document.Elements
            .Where(x => x.IsVisible)
            .OrderBy(x => x.ZIndex)
            .ToList();

        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            var element = ordered[i];
            if (!this.renderers.TryGetValue(element.GetType(), out var renderer))
                continue;

            using var path = renderer.GetOutlinePath(element);
            if (path.Contains(worldX, worldY))
                return new FloorPlanHitResult(element);
        }

        return null;
    }

    /// <summary>Every visible element whose bounds meet <paramref name="area"/>, in paint order.</summary>
    public IEnumerable<FloorPlanElement> HitTestArea(FloorPlanDocument document, PlanRect area) =>
        document.Elements
            .Where(x => x.IsVisible && area.IntersectsWith(x.GetBounds()))
            .OrderBy(x => x.ZIndex);
}
