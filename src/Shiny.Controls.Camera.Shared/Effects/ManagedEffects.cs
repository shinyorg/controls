namespace Shiny.Controls.Camera;

/// <summary>
/// Portable CPU implementations of the spatial effects, used for <b>still images</b> on any backend that has
/// no GPU path for them — Windows, the bare <c>net11.0</c> head, and Android below the shader API level.
/// </summary>
/// <remarks>
/// <para>
/// These are deliberately plain nested loops over a BGRA buffer. They exist so a photo is never silently
/// unfiltered, not to be fast: a still is one-shot, whereas the preview is a frame budget, which is why
/// nothing here is ever invoked on a live frame.
/// </para>
/// <para>
/// Where a platform <i>does</i> have a GPU path, its output will not be pixel-identical to these — a Core
/// Image comic pass and this one are different algorithms aiming at the same look. That is the same
/// fidelity-over-uniformity trade the twelve colour built-ins already make.
/// </para>
/// </remarks>
public static class ManagedEffects
{
    /// <summary>
    /// Flat, posterized colour with inked edges — the comic-panel look.
    /// </summary>
    /// <param name="levels">Quantization steps per channel. Fewer = flatter, more graphic.</param>
    /// <param name="edgeThreshold">Sobel magnitude (0..1) at which an edge becomes solid ink.</param>
    /// <param name="saturation">Saturation applied to the flats, so they read as ink rather than washed out.</param>
    public static PixelSurface Comic(PixelSurface source, int levels = 4, float edgeThreshold = 0.35f, float saturation = 1.35f)
    {
        ArgumentNullException.ThrowIfNull(source);

        var luminance = Luminance(source);
        var result = source.CloneShape();
        var src = source.Pixels;
        var dst = result.Pixels;
        var w = source.Width;
        var h = source.Height;
        var step = 1f / Math.Max(2, levels);

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = ((y * w) + x) * 4;

                // quantize to flat cels
                var b = Quantize(src[i] / 255f, step);
                var g = Quantize(src[i + 1] / 255f, step);
                var r = Quantize(src[i + 2] / 255f, step);

                // push saturation around the flat's own grey so the cels stay graphic
                var grey = (0.299f * r) + (0.587f * g) + (0.114f * b);
                r = grey + ((r - grey) * saturation);
                g = grey + ((g - grey) * saturation);
                b = grey + ((b - grey) * saturation);

                // ink the edges
                var ink = SmoothStep(edgeThreshold * 0.6f, edgeThreshold, Sobel(luminance, w, h, x, y));
                dst[i] = ToByte(b * (1f - ink));
                dst[i + 1] = ToByte(g * (1f - ink));
                dst[i + 2] = ToByte(r * (1f - ink));
                dst[i + 3] = src[i + 3];
            }
        }

        return result;
    }

    /// <summary>Pencil-sketch look: a light ground with dark lines where the image has edges.</summary>
    /// <param name="strength">Multiplier on the detected edge magnitude.</param>
    public static PixelSurface Sketch(PixelSurface source, float strength = 1.6f)
    {
        ArgumentNullException.ThrowIfNull(source);

        var luminance = Luminance(source);
        var result = source.CloneShape();
        var src = source.Pixels;
        var dst = result.Pixels;
        var w = source.Width;
        var h = source.Height;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = ((y * w) + x) * 4;
                var line = Math.Clamp(Sobel(luminance, w, h, x, y) * strength, 0f, 1f);
                var value = ToByte(1f - line);

                dst[i] = value;
                dst[i + 1] = value;
                dst[i + 2] = value;
                dst[i + 3] = src[i + 3];
            }
        }

        return result;
    }

    /// <summary>Quantize each channel to <paramref name="levels"/> steps — flat, poster-print colour.</summary>
    public static PixelSurface Posterize(PixelSurface source, int levels = 6)
    {
        ArgumentNullException.ThrowIfNull(source);

        var px = source.Pixels;
        var step = 1f / Math.Max(2, levels);

        for (var i = 0; i < px.Length; i += 4)
        {
            px[i] = ToByte(Quantize(px[i] / 255f, step));
            px[i + 1] = ToByte(Quantize(px[i + 1] / 255f, step));
            px[i + 2] = ToByte(Quantize(px[i + 2] / 255f, step));
        }

        return source;
    }

    /// <summary>Average each <paramref name="blockSize"/>-pixel block into one flat colour.</summary>
    public static PixelSurface Pixelate(PixelSurface source, int blockSize = 12)
    {
        ArgumentNullException.ThrowIfNull(source);

        var size = Math.Max(2, blockSize);
        var px = source.Pixels;
        var w = source.Width;
        var h = source.Height;

        for (var by = 0; by < h; by += size)
        {
            for (var bx = 0; bx < w; bx += size)
            {
                var maxX = Math.Min(bx + size, w);
                var maxY = Math.Min(by + size, h);
                long sumB = 0, sumG = 0, sumR = 0;
                var count = 0;

                for (var y = by; y < maxY; y++)
                {
                    for (var x = bx; x < maxX; x++)
                    {
                        var i = ((y * w) + x) * 4;
                        sumB += px[i];
                        sumG += px[i + 1];
                        sumR += px[i + 2];
                        count++;
                    }
                }

                if (count == 0)
                    continue;

                var b = (byte)(sumB / count);
                var g = (byte)(sumG / count);
                var r = (byte)(sumR / count);

                for (var y = by; y < maxY; y++)
                {
                    for (var x = bx; x < maxX; x++)
                    {
                        var i = ((y * w) + x) * 4;
                        px[i] = b;
                        px[i + 1] = g;
                        px[i + 2] = r;
                    }
                }
            }
        }

        return source;
    }

    /// <summary>
    /// Box blur of the given radius, run twice — two box passes approximate a Gaussian closely enough for a
    /// preview-grade look at a fraction of the cost.
    /// </summary>
    public static PixelSurface Blur(PixelSurface source, int radius = 8)
    {
        ArgumentNullException.ThrowIfNull(source);

        var r = Math.Max(1, radius);
        var surface = source;
        for (var pass = 0; pass < 2; pass++)
        {
            surface = BoxBlurPass(surface, r, horizontal: true);
            surface = BoxBlurPass(surface, r, horizontal: false);
        }

        return surface;
    }


    // --- internals -------------------------------------------------------------------------------

    // Separable box blur: one axis per call, so the cost is O(radius) per pixel rather than O(radius^2).
    static PixelSurface BoxBlurPass(PixelSurface source, int radius, bool horizontal)
    {
        var result = source.CloneShape();
        var src = source.Pixels;
        var dst = result.Pixels;
        var w = source.Width;
        var h = source.Height;
        var span = (radius * 2) + 1;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                int sumB = 0, sumG = 0, sumR = 0, sumA = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    var sx = horizontal ? Math.Clamp(x + k, 0, w - 1) : x;
                    var sy = horizontal ? y : Math.Clamp(y + k, 0, h - 1);
                    var i = ((sy * w) + sx) * 4;

                    sumB += src[i];
                    sumG += src[i + 1];
                    sumR += src[i + 2];
                    sumA += src[i + 3];
                }

                var o = ((y * w) + x) * 4;
                dst[o] = (byte)(sumB / span);
                dst[o + 1] = (byte)(sumG / span);
                dst[o + 2] = (byte)(sumR / span);
                dst[o + 3] = (byte)(sumA / span);
            }
        }

        return result;
    }

    static float[] Luminance(PixelSurface source)
    {
        var px = source.Pixels;
        var lum = new float[source.Width * source.Height];
        for (var i = 0; i < lum.Length; i++)
        {
            var o = i * 4;
            lum[i] = ((0.114f * px[o]) + (0.587f * px[o + 1]) + (0.299f * px[o + 2])) / 255f;
        }
        return lum;
    }

    // 3x3 Sobel magnitude, edges clamped to the border pixel
    static float Sobel(float[] lum, int w, int h, int x, int y)
    {
        var x0 = Math.Max(0, x - 1);
        var x2 = Math.Min(w - 1, x + 1);
        var y0 = Math.Max(0, y - 1);
        var y2 = Math.Min(h - 1, y + 1);

        var tl = lum[(y0 * w) + x0];
        var t = lum[(y0 * w) + x];
        var tr = lum[(y0 * w) + x2];
        var l = lum[(y * w) + x0];
        var r = lum[(y * w) + x2];
        var bl = lum[(y2 * w) + x0];
        var b = lum[(y2 * w) + x];
        var br = lum[(y2 * w) + x2];

        var gx = -tl - (2f * l) - bl + tr + (2f * r) + br;
        var gy = -tl - (2f * t) - tr + bl + (2f * b) + br;
        return MathF.Sqrt((gx * gx) + (gy * gy));
    }

    static float Quantize(float value, float step) => MathF.Floor((value / step) + 0.5f) * step;

    static float SmoothStep(float edge0, float edge1, float x)
    {
        if (edge1 <= edge0)
            return x < edge1 ? 0f : 1f;

        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
}
