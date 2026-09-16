using SkiaSharp;
using Shiny.Controls.Office.Theming;

namespace Shiny.Controls.Office.Skia;

/// <summary>
/// Draws an <see cref="OfficeWatermark"/> behind a page, slide or sheet.
/// </summary>
/// <remarks>
/// One implementation for all three surfaces. The three formats have nothing in common about storing
/// a watermark, but they have everything in common about drawing one — a picture, washed out, centred
/// on the thing it marks — so the part worth sharing is this end.
/// </remarks>
public static class WatermarkPainter
{
    /// <summary>Decoded pictures, keyed by content. A mark repeats on every page of a document.</summary>
    /// <remarks>
    /// Bounded: this is process-wide, so every distinct picture ever painted - by every document, and on
    /// Blazor Server by every user - would otherwise stay decoded for the life of the process. Evicted
    /// images are dropped rather than disposed, because another surface may be drawing one right now
    /// outside the lock; the SKImage finalizer releases the native pixels once nothing holds it.
    /// </remarks>
    internal const int CacheCapacity = 8;

    static readonly Dictionary<int, SKImage?> Cache = new();

    /// <summary>Insertion order of <see cref="Cache"/>, oldest first, for eviction.</summary>
    static readonly Queue<int> CacheOrder = new();

    /// <summary>How many decoded pictures are currently held. For tests.</summary>
    internal static int CachedCount
    {
        get
        {
            lock (Gate)
                return Cache.Count;
        }
    }

    static readonly Lock Gate = new();

    /// <summary>
    /// Draws the mark inside <paramref name="bounds"/>, clipped to it.
    /// </summary>
    /// <remarks>
    /// Clipped, because a rotated mark scaled to the page is wider than the page across its diagonal —
    /// without this a document watermark spills onto the surround between pages, and a sheet's spills
    /// over the headers.
    /// </remarks>
    public static void Draw(SKCanvas canvas, SKRect bounds, OfficeWatermark? watermark)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        if (watermark is null || watermark.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (Resolve(watermark.Image) is not { } image)
            return;

        using var paint = new SKPaint
        {
            IsAntialias = true,

            // Alpha on the paint rather than a translucent layer: it composites once per draw, where a
            // saveLayer would allocate a page-sized surface on every frame of a scroll.
            Color = SKColors.White.WithAlpha((byte)Math.Clamp(watermark.Opacity * 255, 0, 255))
        };

        canvas.Save();
        canvas.ClipRect(bounds);

        if (watermark.Fit == OfficeWatermarkFit.Tile)
        {
            DrawTiled(canvas, bounds, image, paint, watermark);
            canvas.Restore();
            return;
        }

        var size = Measure(bounds, image, watermark);

        var centreX = bounds.MidX;
        var centreY = bounds.MidY;

        if (watermark.RotationDegrees != 0)
            canvas.RotateDegrees((float)watermark.RotationDegrees, centreX, centreY);

        canvas.DrawImage(
            image,
            new SKRect(
                centreX - (size.Width / 2),
                centreY - (size.Height / 2),
                centreX + (size.Width / 2),
                centreY + (size.Height / 2)),
            paint);

        canvas.Restore();
    }

    static SKSize Measure(SKRect bounds, SKImage image, OfficeWatermark watermark)
    {
        if (watermark.Fit == OfficeWatermarkFit.Native)
            return new SKSize(image.Width, image.Height);

        // Contain: the mark spans `Scale` of the surface without being distorted, so the shorter side
        // of the picture is what the fraction applies to.
        var target = (float)(Math.Min(bounds.Width, bounds.Height) * watermark.Scale);
        var ratio = image.Width / (float)image.Height;

        return ratio >= 1
            ? new SKSize(target * ratio, target)
            : new SKSize(target, target / ratio);
    }

    static void DrawTiled(SKCanvas canvas, SKRect bounds, SKImage image, SKPaint paint, OfficeWatermark watermark)
    {
        var size = Measure(bounds, image, watermark);
        if (size.Width <= 0 || size.Height <= 0)
            return;

        // Started one tile before the top-left so a rotated field still covers the corners.
        for (var y = bounds.Top - size.Height; y < bounds.Bottom + size.Height; y += size.Height)
        {
            for (var x = bounds.Left - size.Width; x < bounds.Right + size.Width; x += size.Width)
                canvas.DrawImage(image, new SKRect(x, y, x + size.Width, y + size.Height), paint);
        }
    }

    static SKImage? Resolve(byte[] data)
    {
        var key = HashCode.Combine(data.Length, data[0], data[^1], data.Length > 64 ? data[64] : 0);

        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached))
                return cached;

            SKImage? image;

            try
            {
                image = SKImage.FromEncodedData(data);
            }
            catch (Exception)
            {
                // A picture that will not decode is not a reason to fail a paint - the surface simply
                // draws without its mark.
                image = null;
            }

            while (Cache.Count >= CacheCapacity && CacheOrder.TryDequeue(out var oldest))
                Cache.Remove(oldest);

            Cache[key] = image;
            CacheOrder.Enqueue(key);
            return image;
        }
    }
}
