using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Shiny.Maui.Controls.Analysis;

/// <summary>
/// A <see cref="VideoFrame"/> backed by a managed copy of a BGRA8 <see cref="SoftwareBitmap"/>. The
/// pixels are copied synchronously when the frame arrives, so the pooled native frame can be released
/// immediately and async analyzers work on a stable buffer.
/// </summary>
public sealed class WindowsVideoFrame : VideoFrame
{
    readonly byte[] bgra;

    public WindowsVideoFrame(SoftwareBitmap bitmap, bool mirrored)
    {
        var bmp = bitmap;
        var converted = false;
        if (bmp.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
        {
            bmp = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            converted = true;
        }

        this.Width = bmp.PixelWidth;
        this.Height = bmp.PixelHeight;
        this.IsMirrored = mirrored;

        var len = this.Width * this.Height * 4;
        var buffer = new Windows.Storage.Streams.Buffer((uint)len);
        bmp.CopyToBuffer(buffer);
        this.bgra = new byte[len];
        using (var reader = DataReader.FromBuffer(buffer))
            reader.ReadBytes(this.bgra);

        if (converted)
            bmp.Dispose();
    }

    public override int Width { get; }
    public override int Height { get; }
    public override int Rotation => 0;
    public override bool IsMirrored { get; }
    public override VideoFrameFormat Format => VideoFrameFormat.Bgra32;

    /// <summary>The raw BGRA pixels (4 bytes/px, row-packed at <see cref="VideoFrame.Width"/>).</summary>
    public byte[] Bgra => this.bgra;

    protected override byte[] MaterializeLuminance()
    {
        int w = this.Width, h = this.Height;
        var lum = new byte[w * h];
        var src = this.bgra;
        for (var i = 0; i < w * h; i++)
        {
            var b = src[i * 4];
            var g = src[i * 4 + 1];
            var r = src[i * 4 + 2];
            lum[i] = (byte)((r * 77 + g * 150 + b * 29) >> 8);
        }
        return lum;
    }
}
