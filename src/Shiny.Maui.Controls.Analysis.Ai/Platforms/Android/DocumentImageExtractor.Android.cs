using Android.Graphics;
using Java.Nio;
using Shiny.Controls.Camera;
using AGraphicsRect = Android.Graphics.Rect;
using RectF = Microsoft.Maui.Graphics.RectF;

namespace Shiny.Maui.Controls.Analysis.Ai;

// Android: there's no lightweight native rectangle detector for live frames (MLKit's document scanner is a
// full-screen flow), so presence detection uses the managed edge detector over the cached luminance plane —
// the same approach the OCR document path uses. Encoding pulls the YUV_420_888 frame, JPEGs it, then rotates,
// un-mirrors and crops to the requested region.
partial class DocumentImageExtractor
{
    public partial DocumentQuad? Detect(VideoFrame frame) => ManagedDocumentEdgeDetector.Detect(frame);

    public partial byte[]? Encode(VideoFrame frame, RectF cropUpright)
    {
        if (frame is not AndroidVideoFrame android)
            return null;

        var nv21 = android.ToNv21();
        int w = frame.Width, h = frame.Height;

        // YUV -> full-frame JPEG (still in sensor orientation)
        byte[] sensorJpeg;
        using (var yuv = new YuvImage(nv21, ImageFormatType.Nv21, w, h, null))
        using (var ms = new MemoryStream())
        {
            yuv.CompressToJpeg(new AGraphicsRect(0, 0, w, h), 90, ms);
            sensorJpeg = ms.ToArray();
        }

        using var sensor = BitmapFactory.DecodeByteArray(sensorJpeg, 0, sensorJpeg.Length);
        if (sensor == null)
            return null;

        // rotate upright + un-mirror the front camera in one transform
        Bitmap upright;
        if (frame.Rotation != 0 || frame.IsMirrored)
        {
            using var m = new Matrix();
            m.PostRotate(frame.Rotation);
            if (frame.IsMirrored)
                m.PostScale(-1f, 1f);
            upright = Bitmap.CreateBitmap(sensor, 0, 0, sensor.Width, sensor.Height, m, true)!;
        }
        else
        {
            upright = sensor;
        }

        try
        {
            int uw = upright.Width, uh = upright.Height;
            var x = Clamp((int)(cropUpright.X * uw), 0, uw - 1);
            var y = Clamp((int)(cropUpright.Y * uh), 0, uh - 1);
            var cw = Clamp((int)(cropUpright.Width * uw), 1, uw - x);
            var ch = Clamp((int)(cropUpright.Height * uh), 1, uh - y);

            using var cropped = Bitmap.CreateBitmap(upright, x, y, cw, ch)!;
            using var outMs = new MemoryStream();
            cropped.Compress(Bitmap.CompressFormat.Jpeg!, 88, outMs);
            return outMs.ToArray();
        }
        finally
        {
            if (!ReferenceEquals(upright, sensor))
                upright.Dispose();
        }
    }

    static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
}
