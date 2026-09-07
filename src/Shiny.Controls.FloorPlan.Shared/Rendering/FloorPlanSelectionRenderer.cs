using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

/// <summary>The dashed outline and eight resize handles around a selected element.</summary>
/// <remarks>
/// Handles are sized in screen pixels and divided back out by the zoom, so they stay grabbable at any
/// magnification. <see cref="HitTestHandle"/> has to be given the same zoom for the same reason -
/// passing a constant makes the handles impossible to grab when zoomed out and trivially easy to grab
/// by accident when zoomed in.
/// </remarks>
public static class FloorPlanSelectionRenderer
{
    public static void RenderHandles(SKCanvas canvas, FloorPlanElement element, FloorPlanCamera camera, FloorPlanTheme theme)
    {
        var bounds = element.GetBounds();
        var rect = new SKRect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);
        var dash = 4f / camera.Zoom;

        using var outlinePaint = new SKPaint
        {
            Color = theme.Selection.ToSKColor(),
            StrokeWidth = 1.5f / camera.Zoom,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash([dash, dash], 0)
        };
        canvas.DrawRect(rect, outlinePaint);

        // A locked element still shows where it is, but there is nothing to grab.
        if (element.IsLocked)
            return;

        var handleHalf = theme.HandleSize / (2 * camera.Zoom);

        using var handleFill = new SKPaint
        {
            Color = theme.HandleFill.ToSKColor(),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        using var handleStroke = new SKPaint
        {
            Color = theme.Selection.ToSKColor(),
            StrokeWidth = 1.5f / camera.Zoom,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        foreach (var (hx, hy) in GetHandlePositions(bounds))
        {
            var handleRect = new SKRect(hx - handleHalf, hy - handleHalf, hx + handleHalf, hy + handleHalf);
            canvas.DrawRect(handleRect, handleFill);
            canvas.DrawRect(handleRect, handleStroke);
        }
    }

    /// <summary>
    /// The eight handles, clockwise from the north-west corner. The order is the handle index
    /// <see cref="SelectTool"/> resizes by, so it is part of the contract rather than an
    /// implementation detail.
    /// </summary>
    public static (float X, float Y)[] GetHandlePositions(PlanRect bounds)
    {
        var (x, y, w, h) = (bounds.X, bounds.Y, bounds.Width, bounds.Height);

        return
        [
            (x, y),              // 0 NW
            (x + w / 2, y),      // 1 N
            (x + w, y),          // 2 NE
            (x + w, y + h / 2),  // 3 E
            (x + w, y + h),      // 4 SE
            (x + w / 2, y + h),  // 5 S
            (x, y + h),          // 6 SW
            (x, y + h / 2)       // 7 W
        ];
    }

    /// <summary>
    /// The index of the handle at a plan point, or -1. <paramref name="zoom"/> must be the camera's,
    /// so that the grab area is a constant number of screen pixels.
    /// </summary>
    public static int HitTestHandle(FloorPlanElement element, float worldX, float worldY, float zoom, float handleSize = 8f)
    {
        if (element.IsLocked)
            return -1;

        var handles = GetHandlePositions(element.GetBounds());
        var threshold = handleSize / MathF.Max(zoom, 0.0001f);

        for (var i = 0; i < handles.Length; i++)
        {
            var (hx, hy) = handles[i];
            if (MathF.Abs(worldX - hx) <= threshold && MathF.Abs(worldY - hy) <= threshold)
                return i;
        }

        return -1;
    }
}
