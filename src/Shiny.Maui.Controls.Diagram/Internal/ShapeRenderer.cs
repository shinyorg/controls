using Shiny.Controls.Diagramming;

namespace Shiny.Maui.Controls.Diagram.Internal;

/// <summary>
/// Draws one node's outline onto an <see cref="ICanvas"/>.
/// </summary>
/// <remarks>
/// <para>
/// The proportions come from <see cref="ShapeGeometry"/>'s shared constants rather than from literals
/// here, because the same numbers decide where a connector anchors and where a click lands. A hexagon
/// drawn with a 0.3 inset and anchored against a 0.25 one has its arrows ending inside it, and
/// nothing about that failure points at the two numbers.
/// </para>
/// <para>
/// The rounded shapes use the canvas's own primitives rather than the tessellated polygon
/// <see cref="ShapeGeometry.Outline"/> returns. That polygon is precise enough to anchor and hit-test
/// against and visibly faceted when a circle is zoomed to fill a phone.
/// </para>
/// </remarks>
static class ShapeRenderer
{
    /// <summary>Fills and strokes a shape with the canvas's current colours.</summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="shape">Which outline to draw.</param>
    /// <param name="bounds">The box the shape fills.</param>
    /// <param name="cornerRadius">Corner radius for the rounded shapes. Negative takes the default.</param>
    public static void Draw(ICanvas canvas, DiagramNodeShape shape, DiagramRect bounds, double cornerRadius)
    {
        if (bounds.IsEmpty)
            return;

        var x = (float)bounds.X;
        var y = (float)bounds.Y;
        var w = (float)bounds.Width;
        var h = (float)bounds.Height;
        var radius = (float)(cornerRadius < 0 ? ShapeGeometry.DefaultCornerRadius : cornerRadius);

        switch (shape)
        {
            case DiagramNodeShape.RoundedRectangle:
                radius = Math.Min(radius, Math.Min(w, h) / 2);
                canvas.FillRoundedRectangle(x, y, w, h, radius);
                canvas.DrawRoundedRectangle(x, y, w, h, radius);
                break;

            case DiagramNodeShape.Stadium:
                canvas.FillRoundedRectangle(x, y, w, h, h / 2);
                canvas.DrawRoundedRectangle(x, y, w, h, h / 2);
                break;

            case DiagramNodeShape.Circle:
            {
                var box = ShapeGeometry.CircleBounds(bounds);
                var r = (float)box.Width / 2;
                canvas.FillCircle((float)box.CenterX, (float)box.CenterY, r);
                canvas.DrawCircle((float)box.CenterX, (float)box.CenterY, r);
                break;
            }

            case DiagramNodeShape.Ellipse:
                canvas.FillEllipse(x, y, w, h);
                canvas.DrawEllipse(x, y, w, h);
                break;

            case DiagramNodeShape.Cylinder:
                Cylinder(canvas, bounds);
                break;

            case DiagramNodeShape.Document:
                Document(canvas, bounds);
                break;

            case DiagramNodeShape.Rectangle:
                canvas.FillRectangle(x, y, w, h);
                canvas.DrawRectangle(x, y, w, h);
                break;

            default:
                // Diamond, parallelogram, hexagon and triangle are all straight-edged, so the shared
                // outline is the drawing as well as the hit-test geometry - one definition, and no
                // chance of the two disagreeing.
                Polygon(canvas, ShapeGeometry.Outline(shape, bounds, cornerRadius));
                break;
        }
    }

    static void Polygon(ICanvas canvas, IReadOnlyList<DiagramPoint> outline)
    {
        if (outline.Count < 3)
            return;

        var path = new PathF();
        path.MoveTo((float)outline[0].X, (float)outline[0].Y);

        for (var i = 1; i < outline.Count; i++)
            path.LineTo((float)outline[i].X, (float)outline[i].Y);

        path.Close();

        canvas.FillPath(path);
        canvas.DrawPath(path);
    }

    static void Cylinder(ICanvas canvas, DiagramRect bounds)
    {
        var cap = (float)(bounds.Height * ShapeGeometry.CylinderCap);
        var x = (float)bounds.X;
        var y = (float)bounds.Y;
        var w = (float)bounds.Width;
        var h = (float)bounds.Height;

        // Body first: the barrel plus the bulged bottom, as one filled path.
        var body = new PathF();
        body.MoveTo(x, y + cap);
        body.CurveTo(x, y, x + w, y, x + w, y + cap);
        body.LineTo(x + w, y + h - cap);
        body.CurveTo(x + w, y + h, x, y + h, x, y + h - cap);
        body.Close();

        canvas.FillPath(body);
        canvas.DrawPath(body);

        // Then the lid, stroked only - filling it would paint over the barrel.
        var lid = new PathF();
        lid.MoveTo(x, y + cap);
        lid.CurveTo(x, y + (cap * 2), x + w, y + (cap * 2), x + w, y + cap);

        canvas.DrawPath(lid);
    }

    static void Document(ICanvas canvas, DiagramRect bounds)
    {
        var wave = (float)(bounds.Height * ShapeGeometry.DocumentWave);
        var x = (float)bounds.X;
        var y = (float)bounds.Y;
        var w = (float)bounds.Width;
        var h = (float)bounds.Height;

        var path = new PathF();
        path.MoveTo(x, y);
        path.LineTo(x + w, y);
        path.LineTo(x + w, y + h - wave);
        path.CurveTo(
            x + w - (w / 4), y + h - (wave * 2),
            x + (w / 4), y + h,
            x, y + h - wave
        );
        path.Close();

        canvas.FillPath(path);
        canvas.DrawPath(path);
    }
}
