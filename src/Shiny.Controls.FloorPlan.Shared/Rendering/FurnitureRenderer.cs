using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

public class FurnitureRenderer : IFloorPlanElementRenderer
{
    public Type ElementType => typeof(FurnitureElement);

    public void Render(SKCanvas canvas, FloorPlanElement element, FloorPlanRenderContext context)
    {
        var furniture = (FurnitureElement)element;
        var bounds = furniture.GetBounds();
        var rect = new SKRect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);

        // The document's own fill wins over the kind's material colour, so a plan can colour-code its
        // furniture by department without losing the silhouettes.
        var baseColor = furniture.Style.FillColor is null
            ? context.Emphasize(context.Theme.ColorFor(furniture.Kind), furniture)
            : context.FillFor(furniture);

        canvas.Save();
        if (furniture.Transform.Rotation != 0)
            canvas.RotateDegrees(furniture.Transform.Rotation, bounds.Center.X, bounds.Center.Y);

        using var fillPaint = new SKPaint
        {
            Color = baseColor.ToSKColor(),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        using var strokePaint = new SKPaint
        {
            Color = context.StrokeFor(furniture).ToSKColor(),
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        if (furniture.Kind == FurnitureKind.Chair)
        {
            var radius = MathF.Min(bounds.Width, bounds.Height) / 2;
            canvas.DrawCircle(bounds.Center.X, bounds.Center.Y, radius, fillPaint);
            canvas.DrawCircle(bounds.Center.X, bounds.Center.Y, radius, strokePaint);
        }
        else
        {
            canvas.DrawRoundRect(rect, 3, 3, fillPaint);
            canvas.DrawRoundRect(rect, 3, 3, strokePaint);
        }

        // A caption only helps while it fits. Below that it is a grey smear across the piece.
        var font = new SKFont { Size = context.Theme.FurnitureFontSize };
        using (font)
        {
            var caption = furniture.Kind.ToString();
            if (font.MeasureText(caption) <= bounds.Width - 4 && font.Size <= bounds.Height - 2)
            {
                using var textPaint = new SKPaint
                {
                    Color = context.Theme.LabelText.ToSKColor(),
                    IsAntialias = true
                };
                canvas.DrawText(caption, bounds.Center.X, bounds.Center.Y + font.Size * 0.35f, SKTextAlign.Center, font, textPaint);
            }
        }

        canvas.Restore();
    }

    public SKPath GetOutlinePath(FloorPlanElement element)
    {
        var furniture = (FurnitureElement)element;
        var bounds = furniture.GetBounds();
        var path = new SKPath();

        if (furniture.Kind == FurnitureKind.Chair)
            path.AddCircle(bounds.Center.X, bounds.Center.Y, MathF.Min(bounds.Width, bounds.Height) / 2);
        else
            path.AddRect(new SKRect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom));

        if (furniture.Transform.Rotation != 0)
        {
            path.Transform(SKMatrix.CreateRotationDegrees(
                furniture.Transform.Rotation,
                bounds.Center.X,
                bounds.Center.Y
            ));
        }

        return path;
    }
}
