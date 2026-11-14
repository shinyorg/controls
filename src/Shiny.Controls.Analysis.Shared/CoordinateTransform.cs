using Microsoft.Maui.Graphics;

namespace Shiny.Controls.Analysis;

/// <summary>
/// Pure geometry helpers that map detections between coordinate spaces. Split into two concerns:
/// <list type="bullet">
/// <item><see cref="ApplyOrientation(RectF,int,bool)"/> — used by <b>analyzers</b> to turn raw
/// sensor-space normalized coordinates into upright, mirror-corrected normalized coordinates (the
/// <see cref="OverlayBox"/> contract).</item>
/// <item><see cref="MapToView(RectF,float,float,float,PreviewScaleMode)"/> — used by the <b>overlay</b>
/// to turn upright normalized coordinates into view-space pixels, accounting for the preview's
/// aspect-fill crop or aspect-fit letterbox.</item>
/// </list>
/// Kept dependency-free and side-effect-free so it can be exhaustively unit-tested.
/// </summary>
public static class CoordinateTransform
{
    /// <summary>
    /// Convert a normalized rectangle from raw sensor space into upright, mirror-corrected space.
    /// Rotation is applied first (clockwise), then the horizontal mirror flip.
    /// </summary>
    /// <param name="raw">Normalized rect (0..1) in raw sensor space.</param>
    /// <param name="rotationDegrees">Clockwise rotation to make the scene upright: 0, 90, 180 or 270.</param>
    /// <param name="mirrored"><c>true</c> to mirror horizontally (front camera).</param>
    public static RectF ApplyOrientation(RectF raw, int rotationDegrees, bool mirrored)
    {
        var r = RotateRect(raw, NormalizeRotation(rotationDegrees));
        if (mirrored)
            r = new RectF(1f - r.X - r.Width, r.Y, r.Width, r.Height);
        return r;
    }


    /// <summary>
    /// The inverse of <see cref="ApplyOrientation(RectF,int,bool)"/>: map an upright, mirror-corrected
    /// normalized rectangle back into raw sensor space. Used to push an analyzer's
    /// <c>ScanWindow</c> (which lives in upright space) down to a native engine's region-of-interest / crop,
    /// which work in the sensor's own coordinates. Origin stays top-left; callers flip Y where the native API
    /// is bottom-left (e.g. Vision).
    /// </summary>
    /// <param name="upright">Normalized rect (0..1) in upright, mirror-corrected space.</param>
    /// <param name="rotationDegrees">The same clockwise rotation that made the scene upright (0, 90, 180, 270).</param>
    /// <param name="mirrored"><c>true</c> if the upright space is mirrored (front camera).</param>
    public static RectF InvertOrientation(RectF upright, int rotationDegrees, bool mirrored)
    {
        var u = upright;
        if (mirrored) // mirror is its own inverse
            u = new RectF(1f - u.X - u.Width, u.Y, u.Width, u.Height);
        var inv = (360 - NormalizeRotation(rotationDegrees)) % 360;
        return RotateRect(u, inv);
    }


    // Rotate a normalized rect clockwise within the unit square, rebuilding the axis-aligned bounds.
    static RectF RotateRect(RectF r, int rot)
    {
        var a = RotatePoint(new PointF(r.X, r.Y), rot);
        var b = RotatePoint(new PointF(r.X + r.Width, r.Y + r.Height), rot);
        return new RectF(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Abs(b.X - a.X), MathF.Abs(b.Y - a.Y));
    }


    /// <summary>Convert a normalized point from raw sensor space into upright, mirror-corrected space.</summary>
    public static PointF ApplyOrientation(PointF raw, int rotationDegrees, bool mirrored)
    {
        var p = RotatePoint(raw, NormalizeRotation(rotationDegrees));
        if (mirrored)
            p = new PointF(1f - p.X, p.Y);
        return p;
    }


    /// <summary>
    /// Map an upright normalized rectangle into view-space pixels, accounting for how the preview
    /// scales into the view.
    /// </summary>
    /// <param name="upright">Normalized rect (0..1) in upright image space.</param>
    /// <param name="viewWidth">View width in pixels (device-independent units).</param>
    /// <param name="viewHeight">View height in pixels.</param>
    /// <param name="imageAspect">Upright image aspect ratio (width / height).</param>
    /// <param name="mode">How the preview fills the view.</param>
    public static RectF MapToView(RectF upright, float viewWidth, float viewHeight, float imageAspect, PreviewScaleMode mode = PreviewScaleMode.AspectFill)
    {
        // model the image as a unit cell of (imageAspect x 1) and scale it into the view
        var unitW = imageAspect <= 0 ? 1f : imageAspect;
        const float unitH = 1f;

        var sx = viewWidth / unitW;
        var sy = viewHeight / unitH;
        var scale = mode == PreviewScaleMode.AspectFill ? MathF.Max(sx, sy) : MathF.Min(sx, sy);

        var dispW = unitW * scale;
        var dispH = unitH * scale;
        var offsetX = (viewWidth - dispW) / 2f;
        var offsetY = (viewHeight - dispH) / 2f;

        return new RectF(
            offsetX + upright.X * dispW,
            offsetY + upright.Y * dispH,
            upright.Width * dispW,
            upright.Height * dispH
        );
    }


    /// <summary>Map an upright normalized point into view-space pixels (see <see cref="MapToView(RectF,float,float,float,PreviewScaleMode)"/>).</summary>
    public static PointF MapToView(PointF upright, float viewWidth, float viewHeight, float imageAspect, PreviewScaleMode mode = PreviewScaleMode.AspectFill)
    {
        var r = MapToView(new RectF(upright.X, upright.Y, 0, 0), viewWidth, viewHeight, imageAspect, mode);
        return new PointF(r.X, r.Y);
    }


    static int NormalizeRotation(int degrees)
    {
        var r = degrees % 360;
        if (r < 0)
            r += 360;
        // snap to the nearest right angle; anything else is treated as upright
        return r switch { 90 => 90, 180 => 180, 270 => 270, _ => 0 };
    }


    static PointF RotatePoint(PointF p, int rot) => rot switch
    {
        90 => new PointF(1f - p.Y, p.X),
        180 => new PointF(1f - p.X, 1f - p.Y),
        270 => new PointF(p.Y, 1f - p.X),
        _ => p
    };
}
