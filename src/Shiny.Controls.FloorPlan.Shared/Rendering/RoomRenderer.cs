using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

public class RoomRenderer : IFloorPlanElementRenderer
{
    public Type ElementType => typeof(RoomElement);

    public void Render(SKCanvas canvas, FloorPlanElement element, FloorPlanRenderContext context)
    {
        var room = (RoomElement)element;
        var bounds = room.GetBounds();
        var rect = new SKRect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);

        using var fillPaint = new SKPaint
        {
            Color = context.FillFor(room).ToSKColor(),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawRect(rect, fillPaint);

        using var strokePaint = new SKPaint
        {
            Color = context.StrokeFor(room).ToSKColor(),
            StrokeWidth = context.IsSelected ? room.Style.StrokeWidth + 1 : room.Style.StrokeWidth,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        canvas.DrawRect(rect, strokePaint);

        if (String.IsNullOrEmpty(room.Label))
            return;

        using var textPaint = new SKPaint
        {
            Color = context.Theme.LabelText.ToSKColor(),
            IsAntialias = true
        };
        using var font = new SKFont { Size = context.Theme.LabelFontSize };
        var center = bounds.Center;

        // Baseline sits a little below the midpoint so the glyphs straddle it rather than hang off it.
        canvas.DrawText(room.Label, center.X, center.Y + font.Size * 0.35f, SKTextAlign.Center, font, textPaint);
    }

    public SKPath GetOutlinePath(FloorPlanElement element)
    {
        var bounds = element.GetBounds();
        var path = new SKPath();
        path.AddRect(new SKRect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom));
        return path;
    }
}
