using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

/// <summary>The snap grid, and the rectangle bounding the document itself.</summary>
/// <remarks>
/// Two methods rather than one, because the two are not the same thing.
/// <see cref="FloorPlanEditorState.IsGridVisible"/> turns off the snap grid; the plan's own boundary
/// is where the document ends, and hiding that leaves a plan floating on an unbounded sheet with no
/// way to tell how much room is left.
/// </remarks>
public static class FloorPlanGridRenderer
{
    public static void RenderGrid(SKCanvas canvas, FloorPlanDocument document, FloorPlanCamera camera, FloorPlanTheme theme)
    {
        // Below about three pixels a cell the grid stops being a grid and becomes a grey wash, which
        // reads as a rendering fault rather than as a zoomed-out plan.
        var spacing = document.GridSize;
        if (spacing <= 0 || spacing * camera.Zoom < 3f)
            return;

        using var paint = new SKPaint
        {
            Color = theme.GridLine.ToSKColor(),
            StrokeWidth = 1f / camera.Zoom,
            Style = SKPaintStyle.Stroke,
            IsAntialias = false
        };

        for (var x = 0f; x <= document.Width; x += spacing)
            canvas.DrawLine(x, 0, x, document.Height, paint);

        for (var y = 0f; y <= document.Height; y += spacing)
            canvas.DrawLine(0, y, document.Width, y, paint);
    }

    public static void RenderBoundary(SKCanvas canvas, FloorPlanDocument document, FloorPlanCamera camera, FloorPlanTheme theme)
    {
        using var paint = new SKPaint
        {
            Color = theme.PlanBorder.ToSKColor(),
            StrokeWidth = 2f / camera.Zoom,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        canvas.DrawRect(0, 0, document.Width, document.Height, paint);
    }
}
