using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

/// <summary>
/// The pan and zoom that maps plan coordinates onto the surface.
/// </summary>
public class FloorPlanCamera
{
    public float Zoom { get; set; } = 1.0f;
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float MinZoom { get; set; } = 0.1f;
    public float MaxZoom { get; set; } = 10f;

    public SKMatrix GetTransformMatrix() =>
        SKMatrix.CreateScaleTranslation(this.Zoom, this.Zoom, this.OffsetX, this.OffsetY);

    public SKPoint ScreenToWorld(float sx, float sy)
    {
        var matrix = this.GetTransformMatrix();
        matrix.TryInvert(out var inverse);
        return inverse.MapPoint(sx, sy);
    }

    public SKPoint WorldToScreen(float wx, float wy) =>
        this.GetTransformMatrix().MapPoint(wx, wy);

    public void Pan(float dx, float dy)
    {
        this.OffsetX += dx;
        this.OffsetY += dy;
    }

    /// <summary>
    /// Zooms by <paramref name="delta"/> keeping the plan point currently under
    /// (<paramref name="screenX"/>, <paramref name="screenY"/>) under it.
    /// </summary>
    /// <remarks>
    /// The anchor is the whole point. Zooming about the origin instead is what makes a plan appear to
    /// run away from the pointer, and it is why <see cref="FloorPlanEngine.ZoomIn"/> takes the
    /// viewport size rather than assuming (0, 0).
    /// </remarks>
    public void ZoomAt(float screenX, float screenY, float delta)
    {
        var worldBefore = this.ScreenToWorld(screenX, screenY);
        this.Zoom = Math.Clamp(this.Zoom * (1 + delta), this.MinZoom, this.MaxZoom);
        var worldAfter = this.ScreenToWorld(screenX, screenY);
        this.OffsetX += (worldAfter.X - worldBefore.X) * this.Zoom;
        this.OffsetY += (worldAfter.Y - worldBefore.Y) * this.Zoom;
    }
}
