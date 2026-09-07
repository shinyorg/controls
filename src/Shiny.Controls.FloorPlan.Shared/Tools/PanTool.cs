using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

/// <summary>Drags the camera. Screen-space, so the plan tracks the pointer exactly.</summary>
public class PanTool : IFloorPlanTool
{
    bool isPanning;
    SKPoint lastScreenPos;

    public string Name => "Pan";

    public void OnActivated(FloorPlanToolContext context)
    {
    }

    public void OnDeactivated() => this.isPanning = false;

    public void OnPointerPressed(FloorPlanToolContext context, FloorPlanPointerEventArgs e)
    {
        this.isPanning = true;
        this.lastScreenPos = e.ScreenPosition;
    }

    public void OnPointerMoved(FloorPlanToolContext context, FloorPlanPointerEventArgs e)
    {
        if (!this.isPanning)
            return;

        context.Camera.Pan(e.ScreenPosition.X - this.lastScreenPos.X, e.ScreenPosition.Y - this.lastScreenPos.Y);
        this.lastScreenPos = e.ScreenPosition;
        context.Invalidate();
    }

    public void OnPointerReleased(FloorPlanToolContext context, FloorPlanPointerEventArgs e) => this.isPanning = false;

    public void Render(SKCanvas canvas, FloorPlanToolRenderContext context)
    {
    }
}
