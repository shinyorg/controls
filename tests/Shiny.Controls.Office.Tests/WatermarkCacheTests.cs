using Shiny.Controls.Office.Skia;
using Shiny.Controls.Office.Theming;
using Shouldly;
using SkiaSharp;
using Xunit;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// The decoded-watermark cache is process-wide, so it must not grow with every picture ever painted.
/// </summary>
public class WatermarkCacheTests
{
    static byte[] Png(int size, byte red)
    {
        using var bitmap = new SKBitmap(size, size);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(new SKColor(red, 0, 0));

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void DistinctPicturesDoNotGrowTheCacheWithoutBound()
    {
        using var bitmap = new SKBitmap(64, 64);
        using var canvas = new SKCanvas(bitmap);

        // Every size gives a different length, so every picture gets its own cache key.
        for (var i = 0; i < WatermarkPainter.CacheCapacity * 4; i++)
        {
            WatermarkPainter.Draw(
                canvas,
                new SKRect(0, 0, 64, 64),
                new OfficeWatermark { Image = Png(8 + i, (byte)(i * 7)) });
        }

        WatermarkPainter.CachedCount.ShouldBeLessThanOrEqualTo(WatermarkPainter.CacheCapacity);
    }
}
