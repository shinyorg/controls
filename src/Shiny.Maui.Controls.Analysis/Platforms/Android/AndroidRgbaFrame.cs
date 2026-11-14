using Android.Graphics;
using Java.Nio;
using AImage = Android.Media.Image;

namespace Shiny.Maui.Controls.Analysis;

/// <summary>
/// An <see cref="AndroidVideoFrame"/> over a managed RGBA buffer — what a <c>MediaElement</c> reads back from the
/// GPU, scaled down to analysis size. There is no <see cref="AndroidVideoFrame.MediaImage"/>; analyzers take
/// <see cref="AndroidVideoFrame.ToBitmap"/>.
/// </summary>
public sealed class AndroidRgbaFrame : AndroidVideoFrame
{
    readonly byte[] rgba;

    /// <summary>Wraps <paramref name="rgba"/> (tightly packed, <paramref name="width"/> x <paramref name="height"/> x 4 bytes, top row first).</summary>
    public AndroidRgbaFrame(byte[] rgba, int width, int height, int rotation = 0)
    {
        if (rgba.Length < width * height * 4)
            throw new ArgumentException("The buffer is smaller than width x height x 4.", nameof(rgba));

        this.rgba = rgba;
        this.Width = width;
        this.Height = height;
        this.Rotation = rotation;
    }

    /// <summary>The RGBA bytes, top row first.</summary>
    public ReadOnlySpan<byte> Rgba => this.rgba;

    /// <inheritdoc/>
    public override int Width { get; }

    /// <inheritdoc/>
    public override int Height { get; }

    /// <inheritdoc/>
    public override int Rotation { get; }

    /// <inheritdoc/>
    public override bool IsMirrored => false;

    /// <inheritdoc/>
    public override VideoFrameFormat Format => VideoFrameFormat.Rgba32;

    /// <inheritdoc/>
    public override AImage? MediaImage => null;

    /// <inheritdoc/>
    protected override Bitmap CreateBitmap()
    {
        // ARGB_8888 is laid out in memory as R, G, B, A — the same bytes as the readback
        var bitmap = Bitmap.CreateBitmap(this.Width, this.Height, Bitmap.Config.Argb8888!)!;
        using var buffer = ByteBuffer.Wrap(this.rgba, 0, this.Width * this.Height * 4)!;
        bitmap.CopyPixelsFromBuffer(buffer);
        return bitmap;
    }

    /// <inheritdoc/>
    public override byte[] ToNv21()
    {
        int w = this.Width & ~1, h = this.Height & ~1;
        var nv21 = new byte[w * h * 3 / 2];
        var uv = w * h;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = (y * this.Width + x) * 4;
                int r = this.rgba[i], g = this.rgba[i + 1], b = this.rgba[i + 2];
                nv21[y * w + x] = (byte)Math.Clamp(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16, 0, 255);

                if ((y & 1) == 0 && (x & 1) == 0)
                {
                    nv21[uv++] = (byte)Math.Clamp(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128, 0, 255); // V
                    nv21[uv++] = (byte)Math.Clamp(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128, 0, 255); // U
                }
            }
        }
        return nv21;
    }

    /// <inheritdoc/>
    protected override byte[] MaterializeLuminance()
    {
        var lum = new byte[this.Width * this.Height];
        for (var p = 0; p < lum.Length; p++)
        {
            var i = p * 4;
            lum[p] = (byte)((this.rgba[i] * 77 + this.rgba[i + 1] * 150 + this.rgba[i + 2] * 29) >> 8);
        }
        return lum;
    }

    /// <inheritdoc/>
    public override void SampleLuminance(Span<byte> destination, int columns, int rows)
    {
        for (var r = 0; r < rows; r++)
        {
            var y = SampleCoordinate(r, rows, this.Height);
            for (var c = 0; c < columns; c++)
            {
                var i = (y * this.Width + SampleCoordinate(c, columns, this.Width)) * 4;
                destination[r * columns + c] = (byte)((this.rgba[i] * 77 + this.rgba[i + 1] * 150 + this.rgba[i + 2] * 29) >> 8);
            }
        }
    }
}
