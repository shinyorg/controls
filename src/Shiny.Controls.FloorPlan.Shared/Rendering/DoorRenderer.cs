using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

public class DoorRenderer : IFloorPlanElementRenderer
{
    public Type ElementType => typeof(DoorElement);

    public void Render(SKCanvas canvas, FloorPlanElement element, FloorPlanRenderContext context)
    {
        var door = (DoorElement)element;
        var w = door.Width;
        var stroke = context.StrokeFor(door).ToSKColor();

        canvas.Save();
        canvas.Translate(door.Transform.X, door.Transform.Y);
        canvas.RotateDegrees(door.Transform.Rotation);

        // Punch the opening: paint the plan's own ground over the wall the door sits in.
        using var gapPaint = new SKPaint
        {
            Color = context.Theme.DoorOpening.ToSKColor(),
            StrokeWidth = 12,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        canvas.DrawLine(0, 0, w, 0, gapPaint);

        using var arcPaint = new SKPaint
        {
            Color = context.StrokeFor(door).WithAlpha(100).ToSKColor(),
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash([4f, 4f], 0)
        };
        canvas.DrawArc(new SKRect(0, -w, w * 2, w), -90, door.SwingAngle, false, arcPaint);

        using var doorPaint = new SKPaint
        {
            Color = stroke,
            StrokeWidth = 2,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        switch (door.DoorType)
        {
            case DoorType.Double:
                // Two leaves meeting in the middle, each swinging the opposite way.
                canvas.DrawLine(0, 0, w / 2, -w * 0.3f, doorPaint);
                canvas.DrawLine(w, 0, w / 2, -w * 0.3f, doorPaint);
                break;

            case DoorType.Sliding:
                // A slider has no swing - it is two overlapping panels in the opening.
                canvas.DrawLine(0, -2, w * 0.6f, -2, doorPaint);
                canvas.DrawLine(w * 0.4f, 2, w, 2, doorPaint);
                break;

            default:
                canvas.DrawLine(0, 0, w, -w * 0.3f, doorPaint);
                break;
        }

        canvas.Restore();
    }

    public SKPath GetOutlinePath(FloorPlanElement element)
    {
        var door = (DoorElement)element;
        var path = new SKPath();

        // The hit area is the opening itself plus a little depth, not the whole swing arc: the arc is
        // an annotation, and a click well away from the door should reach whatever is under it.
        var depth = MathF.Max(door.Width * 0.3f, 8f);
        path.AddRect(new SKRect(0, -depth, door.Width, depth));

        if (door.Transform.Rotation != 0)
            path.Transform(SKMatrix.CreateRotationDegrees(door.Transform.Rotation));

        path.Transform(SKMatrix.CreateTranslation(door.Transform.X, door.Transform.Y));
        return path;
    }
}
