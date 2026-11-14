using Android.Graphics;
using AndroidX.Camera.Core;
using Java.Nio;
using AImage = Android.Media.Image;

namespace Shiny.Maui.Controls.Camera;

/// <summary>
/// An <see cref="AndroidVideoFrame"/> backed by a CameraX <see cref="IImageProxy"/> (YUV_420_888). The proxy is
/// held open until the last reference is released, so native analyzers (MLKit) can consume
/// <see cref="MediaImage"/> directly with zero copies; the managed luminance path reads plane 0 (the Y plane).
/// Analyzers should downcast to <see cref="AndroidVideoFrame"/> rather than this type, so they also work on the
/// frames a <c>MediaElement</c> produces.
/// </summary>
public sealed class AndroidCameraFrame(IImageProxy proxy, bool mirrored) : AndroidVideoFrame
{
    /// <summary>The underlying CameraX frame. Valid until the frame is disposed. Do not close it yourself.</summary>
    public IImageProxy Proxy { get; } = proxy;

    public override int Width => this.Proxy.Width;
    public override int Height => this.Proxy.Height;
    public override int Rotation => this.Proxy.ImageInfo.RotationDegrees;
    public override bool IsMirrored => mirrored;
    public override VideoFrameFormat Format => VideoFrameFormat.Yuv420;

    protected override byte[] MaterializeLuminance()
    {
        var plane = this.Proxy.GetPlanes()![0];
        var buffer = plane.Buffer!;
        var rowStride = plane.RowStride;
        int w = this.Width, h = this.Height;
        var lum = new byte[w * h];

        if (rowStride == w)
        {
            buffer.Get(lum, 0, w * h);
        }
        else
        {
            for (var row = 0; row < h; row++)
            {
                buffer.Position(row * rowStride);
                buffer.Get(lum, row * w, w);
            }
        }
        return lum;
    }

    /// <summary>
    /// Reads the sampled pixels straight out of the Y plane — no 2 MB plane materialized to answer a
    /// question a thousand bytes can.
    /// </summary>
    public override void SampleLuminance(Span<byte> destination, int columns, int rows)
    {
        var plane = this.Proxy.GetPlanes()![0];
        var buffer = plane.Buffer!;
        var rowStride = plane.RowStride;
        var pixelStride = plane.PixelStride;
        var one = new byte[1];

        for (var r = 0; r < rows; r++)
        {
            var y = SampleCoordinate(r, rows, this.Height);
            for (var c = 0; c < columns; c++)
            {
                var x = SampleCoordinate(c, columns, this.Width);
                buffer.Position(y * rowStride + x * pixelStride);
                buffer.Get(one, 0, 1);
                destination[r * columns + c] = one[0];
            }
        }
    }

    /// <inheritdoc/>
    public override AImage? MediaImage => this.Proxy.Image;

    /// <inheritdoc/>
    protected override Bitmap CreateBitmap() => this.Proxy.ToBitmap();

    /// <inheritdoc/>
    public override byte[] ToNv21() => Yuv420ToNv21(this.Proxy);

    /// <inheritdoc/>
    protected override void ReleaseFrame() => this.Proxy.Close();

    static byte[] Yuv420ToNv21(IImageProxy image)
    {
        int w = image.Width, h = image.Height;
        var planes = image.GetPlanes()!;
        var nv21 = new byte[w * h * 3 / 2];

        // Y plane
        CopyPlane(planes[0].Buffer!, planes[0].RowStride, planes[0].PixelStride, w, h, nv21, 0, 1);

        // VU interleaved (NV21 = ...VUVUVU). Chroma planes are half resolution.
        var uBuffer = planes[1].Buffer!;
        var vBuffer = planes[2].Buffer!;
        int uRowStride = planes[1].RowStride, uPixStride = planes[1].PixelStride;
        int vRowStride = planes[2].RowStride, vPixStride = planes[2].PixelStride;
        int cw = w / 2, ch = h / 2;
        var pos = w * h;

        var uRow = new byte[uRowStride];
        var vRow = new byte[vRowStride];
        for (var row = 0; row < ch; row++)
        {
            vBuffer.Position(row * vRowStride);
            vBuffer.Get(vRow, 0, Math.Min(vRowStride, vBuffer.Remaining()));
            uBuffer.Position(row * uRowStride);
            uBuffer.Get(uRow, 0, Math.Min(uRowStride, uBuffer.Remaining()));
            for (var col = 0; col < cw; col++)
            {
                nv21[pos++] = vRow[col * vPixStride];
                nv21[pos++] = uRow[col * uPixStride];
            }
        }
        return nv21;
    }

    static void CopyPlane(ByteBuffer buffer, int rowStride, int pixelStride, int w, int h, byte[] dest, int destOffset, int destPixelStride)
    {
        var pos = destOffset;
        if (pixelStride == 1 && destPixelStride == 1 && rowStride == w)
        {
            buffer.Position(0);
            buffer.Get(dest, destOffset, w * h);
            return;
        }

        var rowBytes = new byte[rowStride];
        for (var row = 0; row < h; row++)
        {
            buffer.Position(row * rowStride);
            buffer.Get(rowBytes, 0, Math.Min(rowStride, buffer.Remaining()));
            for (var col = 0; col < w; col++)
            {
                dest[pos] = rowBytes[col * pixelStride];
                pos += destPixelStride;
            }
        }
    }

}
