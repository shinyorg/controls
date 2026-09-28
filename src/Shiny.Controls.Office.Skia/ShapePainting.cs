using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using SkiaSharp;

namespace Shiny.Controls.Office.Skia;

/// <summary>
/// Turns a <see cref="ShapeGeometry"/> into an <see cref="SKPath"/>, and a <see cref="ShapeFill"/>
/// into paint.
/// </summary>
/// <remarks>
/// Lifted out of the slide painter when the document painter grew inline shapes. A preset geometry is
/// the same path whether it arrived as a <c>p:sp</c> on a slide or a <c>wps:wsp</c> inside a Word
/// drawing, and two copies of the star-point trigonometry is one copy too many.
/// </remarks>
public static class ShapePainting
{
    public static SKPath BuildPath(ShapeGeometry geometry, SKRect bounds, double cornerRadius = 0.16)
    {
        var path = new SKPath();
        var w = bounds.Width;
        var h = bounds.Height;

        switch (geometry)
        {
            case ShapeGeometry.Ellipse:
                path.AddOval(bounds);
                break;

            case ShapeGeometry.RoundedRectangle:
                var radius = (float)(Math.Min(w, h) * cornerRadius);
                path.AddRoundRect(bounds, radius, radius);
                break;

            case ShapeGeometry.Triangle:
                path.MoveTo(bounds.MidX, bounds.Top);
                path.LineTo(bounds.Right, bounds.Bottom);
                path.LineTo(bounds.Left, bounds.Bottom);
                path.Close();
                break;

            case ShapeGeometry.RightTriangle:
                path.MoveTo(bounds.Left, bounds.Top);
                path.LineTo(bounds.Left, bounds.Bottom);
                path.LineTo(bounds.Right, bounds.Bottom);
                path.Close();
                break;

            case ShapeGeometry.Diamond:
                path.MoveTo(bounds.MidX, bounds.Top);
                path.LineTo(bounds.Right, bounds.MidY);
                path.LineTo(bounds.MidX, bounds.Bottom);
                path.LineTo(bounds.Left, bounds.MidY);
                path.Close();
                break;

            case ShapeGeometry.Line:
                // A connector's box encodes its direction; flips are applied by the caller.
                path.MoveTo(bounds.Left, bounds.Top);
                path.LineTo(bounds.Right, bounds.Bottom);
                break;

            case ShapeGeometry.RightArrow:
                AddArrow(path, bounds, 0);
                break;

            case ShapeGeometry.LeftArrow:
                AddArrow(path, bounds, 180);
                break;

            case ShapeGeometry.UpArrow:
                AddArrow(path, bounds, 270);
                break;

            case ShapeGeometry.DownArrow:
                AddArrow(path, bounds, 90);
                break;

            case ShapeGeometry.Pentagon:
                AddPolygon(path, bounds, 5, -90);
                break;

            case ShapeGeometry.Hexagon:
                AddPolygon(path, bounds, 6, 0);
                break;

            case ShapeGeometry.Star5:
                AddStar(path, bounds, 5);
                break;

            case ShapeGeometry.Chevron:
                var notch = w * 0.25f;
                path.MoveTo(bounds.Left, bounds.Top);
                path.LineTo(bounds.Right - notch, bounds.Top);
                path.LineTo(bounds.Right, bounds.MidY);
                path.LineTo(bounds.Right - notch, bounds.Bottom);
                path.LineTo(bounds.Left, bounds.Bottom);
                path.LineTo(bounds.Left + notch, bounds.MidY);
                path.Close();
                break;

            case ShapeGeometry.Parallelogram:
                var slant = w * 0.2f;
                path.MoveTo(bounds.Left + slant, bounds.Top);
                path.LineTo(bounds.Right, bounds.Top);
                path.LineTo(bounds.Right - slant, bounds.Bottom);
                path.LineTo(bounds.Left, bounds.Bottom);
                path.Close();
                break;

            case ShapeGeometry.Trapezoid:
                var inset = w * 0.2f;
                path.MoveTo(bounds.Left + inset, bounds.Top);
                path.LineTo(bounds.Right - inset, bounds.Top);
                path.LineTo(bounds.Right, bounds.Bottom);
                path.LineTo(bounds.Left, bounds.Bottom);
                path.Close();
                break;

            case ShapeGeometry.Plus:
                var armX = w * 0.33f;
                var armY = h * 0.33f;
                path.MoveTo(bounds.Left + armX, bounds.Top);
                path.LineTo(bounds.Right - armX, bounds.Top);
                path.LineTo(bounds.Right - armX, bounds.Top + armY);
                path.LineTo(bounds.Right, bounds.Top + armY);
                path.LineTo(bounds.Right, bounds.Bottom - armY);
                path.LineTo(bounds.Right - armX, bounds.Bottom - armY);
                path.LineTo(bounds.Right - armX, bounds.Bottom);
                path.LineTo(bounds.Left + armX, bounds.Bottom);
                path.LineTo(bounds.Left + armX, bounds.Bottom - armY);
                path.LineTo(bounds.Left, bounds.Bottom - armY);
                path.LineTo(bounds.Left, bounds.Top + armY);
                path.LineTo(bounds.Left + armX, bounds.Top + armY);
                path.Close();
                break;

            case ShapeGeometry.Can:
                var lip = h * 0.12f;
                path.AddOval(new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Top + lip * 2));
                path.AddRect(new SKRect(bounds.Left, bounds.Top + lip, bounds.Right, bounds.Bottom - lip));
                path.AddOval(new SKRect(bounds.Left, bounds.Bottom - lip * 2, bounds.Right, bounds.Bottom));
                break;

            case ShapeGeometry.Cloud:
                // Overlapping circles, which is close enough for a shape used decoratively.
                path.AddOval(new SKRect(bounds.Left, bounds.Top + h * 0.3f, bounds.Left + w * 0.5f, bounds.Bottom));
                path.AddOval(new SKRect(bounds.Left + w * 0.2f, bounds.Top, bounds.Left + w * 0.75f, bounds.Bottom - h * 0.15f));
                path.AddOval(new SKRect(bounds.Left + w * 0.5f, bounds.Top + h * 0.25f, bounds.Right, bounds.Bottom));
                break;

            default:
                path.AddRect(bounds);
                break;
        }

        return path;
    }

    public static void AddArrow(SKPath path, SKRect bounds, double rotationDegrees)
    {
        // Built pointing right, then rotated about the centre.
        var w = bounds.Width;
        var h = bounds.Height;
        var headStart = bounds.Left + w * 0.6f;
        var shaftTop = bounds.Top + h * 0.3f;
        var shaftBottom = bounds.Bottom - h * 0.3f;

        path.MoveTo(bounds.Left, shaftTop);
        path.LineTo(headStart, shaftTop);
        path.LineTo(headStart, bounds.Top);
        path.LineTo(bounds.Right, bounds.MidY);
        path.LineTo(headStart, bounds.Bottom);
        path.LineTo(headStart, shaftBottom);
        path.LineTo(bounds.Left, shaftBottom);
        path.Close();

        if (rotationDegrees != 0)
            path.Transform(SKMatrix.CreateRotationDegrees((float)rotationDegrees, bounds.MidX, bounds.MidY));
    }

    public static void AddPolygon(SKPath path, SKRect bounds, int sides, double startAngleDegrees)
    {
        var cx = bounds.MidX;
        var cy = bounds.MidY;
        var rx = bounds.Width / 2;
        var ry = bounds.Height / 2;

        for (var i = 0; i < sides; i++)
        {
            var angle = (startAngleDegrees + i * 360d / sides) * Math.PI / 180;
            var x = (float)(cx + rx * Math.Cos(angle));
            var y = (float)(cy + ry * Math.Sin(angle));

            if (i == 0)
                path.MoveTo(x, y);
            else
                path.LineTo(x, y);
        }

        path.Close();
    }

    public static void AddStar(SKPath path, SKRect bounds, int points)
    {
        var cx = bounds.MidX;
        var cy = bounds.MidY;
        var outerX = bounds.Width / 2;
        var outerY = bounds.Height / 2;
        var innerRatio = 0.4;

        for (var i = 0; i < points * 2; i++)
        {
            var angle = (-90 + i * 180d / points) * Math.PI / 180;
            var ratio = i % 2 == 0 ? 1 : innerRatio;
            var x = (float)(cx + outerX * ratio * Math.Cos(angle));
            var y = (float)(cy + outerY * ratio * Math.Sin(angle));

            if (i == 0)
                path.MoveTo(x, y);
            else
                path.LineTo(x, y);
        }

        path.Close();
    }

    public static void ApplyFill(SKPaint fill, ShapeFill shapeFill, SKRect bounds)
    {
        fill.Shader?.Dispose();
        fill.Shader = null;

        if (shapeFill.Solid is { } solid)
        {
            fill.Color = ToSk(solid);
            return;
        }

        if (shapeFill.GradientStops.Count == 0)
            return;

        var colors = shapeFill.GradientStops.Select(x => ToSk(x.Color)).ToArray();
        var positions = shapeFill.GradientStops.Select(x => (float)x.Position).ToArray();

        var radians = shapeFill.GradientAngle * Math.PI / 180;
        var dx = (float)Math.Cos(radians) * bounds.Width / 2;
        var dy = (float)Math.Sin(radians) * bounds.Height / 2;

        fill.Color = SKColors.White;
        fill.Shader = SKShader.CreateLinearGradient(
            new SKPoint(bounds.MidX - dx, bounds.MidY - dy),
            new SKPoint(bounds.MidX + dx, bounds.MidY + dy),
            colors,
            positions,
            SKShaderTileMode.Clamp);
    }
    /// <summary>Draws a filled and outlined preset geometry into <paramref name="bounds"/>.</summary>
    /// <remarks>
    /// The two paints are borrowed, not owned: both are reset before use and the fill's shader is
    /// cleared afterwards, because a gradient left on a shared paint bleeds onto whatever is drawn
    /// next — which, in a document, is the following run of text.
    /// </remarks>
    public static void DrawShape(
        SKCanvas canvas,
        SKPaint fill,
        SKPaint stroke,
        ShapeGeometry geometry,
        SKRect bounds,
        ShapeFill? shapeFill,
        ShapeOutline? outline,
        double cornerRadius = 0.16)
    {
        if (geometry == ShapeGeometry.None)
            return;

        using var path = BuildPath(geometry, bounds, cornerRadius);

        // A line has no interior to fill; filling one paints a triangle between its endpoints.
        if (shapeFill is { IsEmpty: false } && geometry != ShapeGeometry.Line)
        {
            ApplyFill(fill, shapeFill, bounds);
            canvas.DrawPath(path, fill);
            fill.Shader?.Dispose();
            fill.Shader = null;
        }

        if (outline is null)
            return;

        stroke.Color = ToSk(outline.Color);
        stroke.StrokeWidth = (float)outline.Width;
        stroke.PathEffect = DashEffect(outline);

        canvas.DrawPath(path, stroke);
        stroke.PathEffect?.Dispose();
        stroke.PathEffect = null;
    }

    /// <summary>
    /// The path effect for an outline's dash pattern, in multiples of its width as DrawingML defines
    /// them — or null for a solid line.
    /// </summary>
    public static SKPathEffect? DashEffect(ShapeOutline outline)
    {
        float[]? pattern = outline.Dash switch
        {
            LineDash.Dot or LineDash.SystemDot => [1, 1],
            LineDash.Dash => [4, 3],
            LineDash.LargeDash => [8, 3],
            LineDash.DashDot => [4, 3, 1, 3],
            LineDash.LongDashDot => [8, 3, 1, 3],
            LineDash.LongDashDotDot => [8, 3, 1, 3, 1, 3],
            LineDash.SystemDash => [3, 1],
            _ => outline.Dashed ? [3, 2] : null
        };

        if (pattern is null)
            return null;

        var width = (float)Math.Max(0.5, outline.Width);
        return SKPathEffect.CreateDash(pattern.Select(x => x * width).ToArray(), 0);
    }

    static SKColor ToSk(ArgbColor color) => new(color.R, color.G, color.B, color.A);
}
