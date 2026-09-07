using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

public class CubicleRenderer : IFloorPlanElementRenderer
{
    public Type ElementType => typeof(CubicleElement);

    public void Render(SKCanvas canvas, FloorPlanElement element, FloorPlanRenderContext context)
    {
        var cubicle = (CubicleElement)element;
        var bounds = cubicle.GetBounds();
        var rect = new SKRect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);

        using var fillPaint = new SKPaint
        {
            Color = context.FillFor(cubicle).ToSKColor(),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawRect(rect, fillPaint);

        // Dashed, because a cubicle wall is a partition rather than structure.
        using var strokePaint = new SKPaint
        {
            Color = context.StrokeFor(cubicle).ToSKColor(),
            StrokeWidth = cubicle.Style.StrokeWidth,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash([6f, 4f], 0)
        };
        canvas.DrawRect(rect, strokePaint);

        var deskRect = new SKRect(
            bounds.X + bounds.Width * 0.15f,
            bounds.Y + bounds.Height * 0.5f,
            bounds.Right - bounds.Width * 0.15f,
            bounds.Bottom - bounds.Height * 0.15f
        );
        using var deskPaint = new SKPaint
        {
            Color = context.Emphasize(context.Theme.CubicleDesk, cubicle).ToSKColor(),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawRect(deskRect, deskPaint);

        if (String.IsNullOrEmpty(cubicle.Occupant))
            return;

        using var textPaint = new SKPaint
        {
            Color = context.Theme.LabelText.ToSKColor(),
            IsAntialias = true
        };
        using var font = new SKFont { Size = context.Theme.OccupantFontSize };
        canvas.DrawText(
            cubicle.Occupant,
            bounds.Center.X,
            bounds.Y + bounds.Height * 0.35f,
            SKTextAlign.Center,
            font,
            textPaint
        );
    }

    public SKPath GetOutlinePath(FloorPlanElement element)
    {
        var bounds = element.GetBounds();
        var path = new SKPath();
        path.AddRect(new SKRect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom));
        return path;
    }
}
