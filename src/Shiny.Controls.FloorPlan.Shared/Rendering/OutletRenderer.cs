using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

public class OutletRenderer : IFloorPlanElementRenderer
{
    public Type ElementType => typeof(OutletElement);

    public void Render(SKCanvas canvas, FloorPlanElement element, FloorPlanRenderContext context)
    {
        var outlet = (OutletElement)element;
        var bounds = outlet.GetBounds();
        var cx = bounds.Center.X;
        var cy = bounds.Center.Y;
        var r = outlet.Size / 2;

        var fill = context.IsSelected
            ? context.Theme.Selection.WithAlpha(60)
            : context.Emphasize(PlanColor.ParseOr(outlet.Style.FillColor, context.Theme.OutletFill), outlet);

        using var fillPaint = new SKPaint
        {
            Color = fill.ToSKColor(),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        using var strokePaint = new SKPaint
        {
            Color = context.StrokeFor(outlet).ToSKColor(),
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        canvas.DrawCircle(cx, cy, r, fillPaint);
        canvas.DrawCircle(cx, cy, r, strokePaint);

        using var symbolPaint = new SKPaint
        {
            Color = context.Theme.OutletSymbol.ToSKColor(),
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };

        // Symbols are drawn as a fraction of the radius rather than at fixed offsets, so an outlet
        // resized in the document still looks like an outlet instead of a circle with a speck in it.
        switch (outlet.OutletType)
        {
            case OutletType.Standard:
                canvas.DrawLine(cx - r * 0.38f, cy - r * 0.38f, cx - r * 0.38f, cy + r * 0.12f, symbolPaint);
                canvas.DrawLine(cx + r * 0.38f, cy - r * 0.38f, cx + r * 0.38f, cy + r * 0.12f, symbolPaint);
                canvas.DrawCircle(cx, cy + r * 0.5f, r * 0.19f, symbolPaint);
                break;

            case OutletType.Floor:
            {
                using var textPaint = new SKPaint { Color = symbolPaint.Color, IsAntialias = true };
                using var font = new SKFont { Size = r * 1.25f };
                canvas.DrawText("F", cx, cy + r * 0.45f, SKTextAlign.Center, font, textPaint);
                break;
            }

            case OutletType.Data:
            {
                using var dataPath = new SKPath();
                dataPath.MoveTo(cx, cy - r * 0.5f);
                dataPath.LineTo(cx + r * 0.5f, cy + r * 0.38f);
                dataPath.LineTo(cx - r * 0.5f, cy + r * 0.38f);
                dataPath.Close();
                canvas.DrawPath(dataPath, symbolPaint);
                break;
            }
        }
    }

    public SKPath GetOutlinePath(FloorPlanElement element)
    {
        var outlet = (OutletElement)element;
        var bounds = outlet.GetBounds();
        var path = new SKPath();

        // Floored at a few plan units: a 16-unit outlet on a plan zoomed right out is a target barely
        // wider than the pointer, and an unclickable symbol reads as a broken control.
        path.AddCircle(bounds.Center.X, bounds.Center.Y, MathF.Max(outlet.Size / 2, 6f));
        return path;
    }
}
