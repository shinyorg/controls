using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

public class WallRenderer : IFloorPlanElementRenderer
{
    public Type ElementType => typeof(WallElement);

    public void Render(SKCanvas canvas, FloorPlanElement element, FloorPlanRenderContext context)
    {
        var wall = (WallElement)element;
        var startX = wall.Start.X + wall.Transform.X;
        var startY = wall.Start.Y + wall.Transform.Y;
        var endX = wall.End.X + wall.Transform.X;
        var endY = wall.End.Y + wall.Transform.Y;

        using var paint = new SKPaint
        {
            Color = context.StrokeFor(wall).ToSKColor(),
            StrokeWidth = wall.Thickness,
            StrokeCap = SKStrokeCap.Butt,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        canvas.DrawLine(startX, startY, endX, endY, paint);
    }

    /// <summary>
    /// The wall's own footprint rather than its bounding box.
    /// </summary>
    /// <remarks>
    /// A diagonal wall's bounding box is mostly empty room, and testing against it means clicks
    /// several plan-metres away from the wall select it. The quad is the wall thickened along its
    /// normal, which is what was actually drawn.
    /// </remarks>
    public SKPath GetOutlinePath(FloorPlanElement element)
    {
        var wall = (WallElement)element;
        var x1 = wall.Start.X + wall.Transform.X;
        var y1 = wall.Start.Y + wall.Transform.Y;
        var x2 = wall.End.X + wall.Transform.X;
        var y2 = wall.End.Y + wall.Transform.Y;

        var dx = x2 - x1;
        var dy = y2 - y1;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        var half = MathF.Max(wall.Thickness, 4f) / 2f;

        var path = new SKPath();

        if (length < 0.0001f)
        {
            // A zero-length wall is a click target, not a line. Give it something square to hit.
            path.AddRect(new SKRect(x1 - half, y1 - half, x1 + half, y1 + half));
            return path;
        }

        var nx = -dy / length * half;
        var ny = dx / length * half;

        path.MoveTo(x1 + nx, y1 + ny);
        path.LineTo(x2 + nx, y2 + ny);
        path.LineTo(x2 - nx, y2 - ny);
        path.LineTo(x1 - nx, y1 - ny);
        path.Close();
        return path;
    }
}
