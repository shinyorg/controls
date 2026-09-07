using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

/// <summary>
/// Draws a <see cref="CustomElement"/> by looking its stencil up on the document and scaling that
/// stencil's SVG path into the element's box.
/// </summary>
public class CustomElementRenderer : IFloorPlanElementRenderer
{
    readonly Func<string, ShapeDefinition?> shapeResolver;

    public CustomElementRenderer(Func<string, ShapeDefinition?> shapeResolver)
        => this.shapeResolver = shapeResolver;

    public Type ElementType => typeof(CustomElement);

    public void Render(SKCanvas canvas, FloorPlanElement element, FloorPlanRenderContext context)
    {
        var custom = (CustomElement)element;
        var bounds = custom.GetBounds();
        var rect = new SKRect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);
        var fill = context.FillFor(custom);
        var stroke = context.StrokeFor(custom);

        var shape = this.shapeResolver(custom.ShapeDefinitionId);
        if (shape is not null && !String.IsNullOrEmpty(shape.SvgPath))
        {
            using var svgPath = SKPath.ParseSvgPathData(shape.SvgPath);
            var svgBounds = svgPath?.Bounds ?? SKRect.Empty;

            // A path that parsed but has no area - a single moveto, or artwork whose commands were
            // truncated - would divide by zero here and vanish. Fall through to the placeholder,
            // which at least says something is wrong.
            if (svgPath is not null && svgBounds.Width > 0 && svgBounds.Height > 0)
            {
                var scaleX = custom.Width / svgBounds.Width;
                var scaleY = custom.Height / svgBounds.Height;

                canvas.Save();
                canvas.Translate(bounds.X, bounds.Y);
                canvas.Scale(scaleX, scaleY);
                canvas.Translate(-svgBounds.Left, -svgBounds.Top);

                using var svgFill = new SKPaint
                {
                    Color = fill.ToSKColor(),
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };
                using var svgStroke = new SKPaint
                {
                    Color = stroke.ToSKColor(),
                    // Undo the non-uniform scale so the outline is even, rather than fat on one axis.
                    StrokeWidth = custom.Style.StrokeWidth / MathF.Max((scaleX + scaleY) / 2, 0.0001f),
                    Style = SKPaintStyle.Stroke,
                    IsAntialias = true
                };

                canvas.DrawPath(svgPath, svgFill);
                canvas.DrawPath(svgPath, svgStroke);
                canvas.Restore();
                return;
            }
        }

        using var fallbackFill = new SKPaint
        {
            Color = fill.ToSKColor(),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        using var fallbackStroke = new SKPaint
        {
            Color = stroke.ToSKColor(),
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        canvas.DrawRect(rect, fallbackFill);
        canvas.DrawRect(rect, fallbackStroke);

        using var textPaint = new SKPaint
        {
            Color = context.Theme.LabelText.ToSKColor(),
            IsAntialias = true
        };
        using var font = new SKFont { Size = MathF.Min(bounds.Width, bounds.Height) * 0.5f };
        canvas.DrawText("?", bounds.Center.X, bounds.Center.Y + font.Size * 0.35f, SKTextAlign.Center, font, textPaint);
    }

    public SKPath GetOutlinePath(FloorPlanElement element)
    {
        var bounds = element.GetBounds();
        var path = new SKPath();
        path.AddRect(new SKRect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom));
        return path;
    }
}
