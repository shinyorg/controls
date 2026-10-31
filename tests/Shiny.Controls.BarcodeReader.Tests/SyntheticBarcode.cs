using ZXing;
using ZXing.Common;
using ZxFormat = ZXing.BarcodeFormat;

namespace Shiny.Controls.BarcodeReader.Tests;

/// <summary>
/// Renders real codes (encoded by ZXing.Net) the way a camera would see them from a distance: any module
/// size including fractions of a pixel, rotated, blurred by defocus, with sensor noise and reduced contrast.
/// Shared with the benchmark by file link.
/// </summary>
public static class SyntheticBarcode
{
    public sealed record Scene(byte[] Pixels, int Width, int Height, float CenterX, float CenterY, float Angle, float HalfLength, float HalfHeight);

    /// <param name="text">Payload.</param>
    /// <param name="format">ZXing symbology to encode.</param>
    /// <param name="module">Module width in pixels.</param>
    /// <param name="angleDegrees">Rotation of the code.</param>
    /// <param name="blur">Gaussian sigma in pixels (defocus).</param>
    /// <param name="noise">Uniform noise amplitude in grey levels.</param>
    /// <param name="contrast">Dark-to-light difference, 0..255.</param>
    /// <param name="width">Canvas.</param>
    /// <param name="height">Canvas.</param>
    /// <param name="heightModules">Bar height in modules for a linear code (ignored for 2D).</param>
    /// <param name="seed">Noise seed.</param>
    public static Scene Render(
        string text,
        ZxFormat format,
        float module,
        float angleDegrees = 0,
        float blur = 0,
        float noise = 0,
        int contrast = 180,
        int width = 1280,
        int height = 720,
        float heightModules = 40,
        int seed = 1,
        float centerX = -1,
        float centerY = -1)
    {
        var matrix = new MultiFormatWriter().encode(text, format, 0, 0, new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 0 });
        var linear = matrix.Height <= 1 || IsLinear(format);
        var cols = matrix.Width;
        var rows = linear ? heightModules : matrix.Height;

        var cx = centerX < 0 ? width / 2f : centerX;
        var cy = centerY < 0 ? height / 2f : centerY;
        var a = angleDegrees * MathF.PI / 180f;
        var c = MathF.Cos(a);
        var s = MathF.Sin(a);
        var light = 128 + contrast / 2f;
        var dark = 128 - contrast / 2f;

        // 4x4 supersampling: each output pixel is the coverage of dark modules under it
        var img = new float[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var dark16 = 0;
            for (var sy = 0; sy < 4; sy++)
            for (var sx = 0; sx < 4; sx++)
            {
                var px = x + (sx + 0.5f) / 4f - cx;
                var py = y + (sy + 0.5f) / 4f - cy;
                // inverse rotate into code space, in modules from the top-left corner
                var u = (px * c + py * s) / module + cols / 2f;
                var v = (-px * s + py * c) / module + rows / 2f;
                if (u < 0 || v < 0 || u >= cols || v >= rows)
                    continue;
                var bit = linear ? matrix[(int)u, 0] : matrix[(int)u, (int)v];
                if (bit)
                    dark16++;
            }
            img[y * width + x] = light + (dark - light) * dark16 / 16f;
        }

        if (blur > 0)
            img = GaussianBlur(img, width, height, blur);

        var rng = new Random(seed);
        var pixels = new byte[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            var n = noise > 0 ? (float)(rng.NextDouble() * 2 - 1) * noise : 0;
            pixels[i] = (byte)Math.Clamp(img[i] + n + 0.5f, 0, 255);
        }

        return new Scene(pixels, width, height, cx, cy, a, cols * module / 2f, rows * module / 2f);
    }

    public static bool IsLinear(ZxFormat format) => format is ZxFormat.EAN_13 or ZxFormat.EAN_8 or ZxFormat.UPC_A or ZxFormat.UPC_E
        or ZxFormat.CODE_128 or ZxFormat.CODE_39 or ZxFormat.ITF or ZxFormat.CODE_93 or ZxFormat.CODABAR;

    static float[] GaussianBlur(float[] src, int w, int h, float sigma)
    {
        var r = (int)MathF.Ceiling(sigma * 3);
        var k = new float[2 * r + 1];
        var sum = 0f;
        for (var i = -r; i <= r; i++)
            sum += k[i + r] = MathF.Exp(-i * i / (2 * sigma * sigma));
        for (var i = 0; i < k.Length; i++)
            k[i] /= sum;

        var tmp = new float[src.Length];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var acc = 0f;
            for (var i = -r; i <= r; i++)
                acc += src[y * w + Math.Clamp(x + i, 0, w - 1)] * k[i + r];
            tmp[y * w + x] = acc;
        }
        var dst = new float[src.Length];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var acc = 0f;
            for (var i = -r; i <= r; i++)
                acc += tmp[Math.Clamp(y + i, 0, h - 1) * w + x] * k[i + r];
            dst[y * w + x] = acc;
        }
        return dst;
    }
}
