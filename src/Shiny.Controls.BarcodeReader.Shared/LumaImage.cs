namespace Shiny.Controls.BarcodeReader;

/// <summary>
/// An 8-bit luminance plane borrowed for the length of one synchronous engine call. A <c>ref struct</c> so a
/// camera frame's cached plane can be read in place — at 1080p a copy is 2 MB a frame.
/// </summary>
public readonly ref struct LumaImage
{
    readonly ReadOnlySpan<byte> pixels;

    /// <summary>Wrap a plane of <paramref name="width"/> x <paramref name="height"/> bytes, rows <paramref name="stride"/> apart (0 = width).</summary>
    public LumaImage(ReadOnlySpan<byte> pixels, int width, int height, int stride = 0)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "The image must have a size.");

        stride = stride <= 0 ? width : stride;
        if (pixels.Length < stride * (height - 1) + width)
            throw new ArgumentException("The plane is smaller than width x height.", nameof(pixels));

        this.pixels = pixels;
        this.Width = width;
        this.Height = height;
        this.Stride = stride;
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }
    /// <summary>Height in pixels.</summary>
    public int Height { get; }
    /// <summary>Bytes from one row to the next.</summary>
    public int Stride { get; }
    /// <summary>The plane.</summary>
    public ReadOnlySpan<byte> Pixels => this.pixels;

    /// <summary>The pixel at <paramref name="x"/>, <paramref name="y"/>.</summary>
    public byte this[int x, int y] => this.pixels[y * this.Stride + x];

    /// <summary>Bilinear sample at a sub-pixel position; positions off the edge read the nearest edge pixel.</summary>
    public float Sample(float x, float y)
    {
        x = Math.Clamp(x, 0f, this.Width - 1.001f);
        y = Math.Clamp(y, 0f, this.Height - 1.001f);
        var x0 = (int)x;
        var y0 = (int)y;
        var fx = x - x0;
        var fy = y - y0;
        var i = y0 * this.Stride + x0;
        var p = this.pixels;
        var top = p[i] + (p[i + 1] - p[i]) * fx;
        var bottom = p[i + this.Stride] + (p[i + this.Stride + 1] - p[i + this.Stride]) * fx;
        return top + (bottom - top) * fy;
    }

    /// <summary>
    /// Copy <paramref name="source"/> out at <paramref name="scale"/> (bilinear), for handing a single
    /// candidate to a native decoder at a size it can read. Enlarging matters more than anything else at
    /// distance: the native engines downscale a whole frame before they look at it, so a code 60 px wide
    /// arrives with its bars thinner than a pixel; on its own and enlarged it arrives readable.
    /// </summary>
    public byte[] CropScaled(BoxF source, float scale, out int width, out int height)
    {
        var src = source.Clamp(this.Width, this.Height);
        width = Math.Max(1, (int)MathF.Round(src.Width * scale));
        height = Math.Max(1, (int)MathF.Round(src.Height * scale));
        var result = new byte[width * height];
        var inv = 1f / scale;
        for (var y = 0; y < height; y++)
        {
            var sy = src.Y + (y + 0.5f) * inv - 0.5f;
            var row = y * width;
            for (var x = 0; x < width; x++)
                result[row + x] = (byte)(this.Sample(src.X + (x + 0.5f) * inv - 0.5f, sy) + 0.5f);
        }
        return result;
    }
}
