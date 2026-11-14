using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Shiny.Maui.Controls.Analysis.Ai;

// Windows: presence detection uses the managed edge detector over the luminance plane (no native rectangle
// detector for live frames). Encoding crops the BGRA frame (un-mirroring the front camera; Windows frames are
// already upright, Rotation == 0) and JPEGs it with BitmapEncoder.
partial class DocumentImageExtractor
{
    public partial DocumentQuad? Detect(VideoFrame frame) => ManagedDocumentEdgeDetector.Detect(frame);

    public partial byte[]? Encode(VideoFrame frame, RectF cropUpright)
    {
        if (frame is not WindowsVideoFrame win)
            return null;

        var src = win.Bgra;
        int w = frame.Width, h = frame.Height;

        var x = Clamp((int)(cropUpright.X * w), 0, w - 1);
        var y = Clamp((int)(cropUpright.Y * h), 0, h - 1);
        var cw = Clamp((int)(cropUpright.Width * w), 1, w - x);
        var ch = Clamp((int)(cropUpright.Height * h), 1, h - y);

        // copy the crop into a tight BGRA buffer, un-mirroring horizontally if the source is a front camera
        var crop = new byte[cw * ch * 4];
        for (var row = 0; row < ch; row++)
        {
            var sy = y + row;
            for (var col = 0; col < cw; col++)
            {
                var sx = frame.IsMirrored ? (w - 1 - (x + col)) : (x + col);
                var s = (sy * w + sx) * 4;
                var d = (row * cw + col) * 4;
                crop[d] = src[s];
                crop[d + 1] = src[s + 1];
                crop[d + 2] = src[s + 2];
                crop[d + 3] = src[s + 3];
            }
        }

        var buffer = crop.AsBuffer();
        using var bmp = SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8, cw, ch, BitmapAlphaMode.Ignore);
        return EncodeJpeg(bmp);
    }

    static byte[]? EncodeJpeg(SoftwareBitmap bmp)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream).AsTask().GetAwaiter().GetResult();
        encoder.SetSoftwareBitmap(bmp);
        encoder.FlushAsync().AsTask().GetAwaiter().GetResult();

        var size = (int)stream.Size;
        var bytes = new byte[size];
        stream.Seek(0);
        using var reader = new DataReader(stream);
        reader.LoadAsync((uint)size).AsTask().GetAwaiter().GetResult();
        reader.ReadBytes(bytes);
        return bytes;
    }

    static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
}
