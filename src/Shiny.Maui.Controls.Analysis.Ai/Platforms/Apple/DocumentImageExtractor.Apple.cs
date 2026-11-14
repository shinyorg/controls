using CoreGraphics;
using CoreImage;
using Foundation;
using ImageIO;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using Vision;

namespace Shiny.Maui.Controls.Analysis.Ai;

// Apple: detect the document with Vision's document segmentation (the same native detector the OCR document
// path uses, but presence-only — no text recognition, which is the whole cost saving). Encoding orients the
// frame upright, crops to the requested region and JPEGs it via ImageIO so it works on iOS, Mac Catalyst and
// macOS (no UIKit dependency).
partial class DocumentImageExtractor
{
    public partial DocumentQuad? Detect(VideoFrame frame)
    {
        if (frame is not AppleVideoFrame apple)
            return null;

        using var cg = apple.ToCGImage();
        if (cg == null)
            return null;

        try
        {
            VNRectangleObservation? doc = null;
            var seg = new VNDetectDocumentSegmentationRequest((req, err) =>
            {
                if (err == null)
                    doc = req.GetResults<VNRectangleObservation>()?.FirstOrDefault();
            });
            using var handler = new VNImageRequestHandler(cg, new NSDictionary());
            handler.Perform([seg], out _);

            return doc == null ? null : ToFrameQuad(doc, frame);
        }
        catch
        {
            return null;
        }
    }

    public partial byte[]? Encode(VideoFrame frame, RectF cropUpright)
    {
        if (frame is not AppleVideoFrame apple)
            return null;

        using var cg = apple.ToCGImage();
        if (cg == null)
            return null;

        // 1) orient the sensor image upright (and un-mirror the front camera)
        using var sensor = new CIImage(cg);
        using var upright = sensor.CreateByApplyingOrientation(Orientation(frame.Rotation, frame.IsMirrored));
        var ext = upright.Extent;

        // 2) crop to the requested upright-normalized region. CIImage's origin is bottom-left, so flip Y.
        var cropX = ext.X + cropUpright.X * ext.Width;
        var cropY = ext.Y + (1f - cropUpright.Y - cropUpright.Height) * ext.Height;
        var crop = new CGRect(cropX, cropY, cropUpright.Width * ext.Width, cropUpright.Height * ext.Height);
        crop = CGRect.Intersect(crop, ext);
        if (crop.Width < 1 || crop.Height < 1)
            crop = ext;

        using var ctx = new CIContext();
        using var outImage = ctx.CreateCGImage(upright, crop);
        if (outImage == null)
            return null;

        return EncodeJpeg(outImage, 0.85f);
    }

    // Vision corners are normalized with a bottom-left origin; flip Y to top-left and apply the frame's
    // orientation so the quad lands in the same upright space the overlay draws in.
    static DocumentQuad ToFrameQuad(VNRectangleObservation doc, VideoFrame frame)
    {
        PointF P(CGPoint p) => CoordinateTransform.ApplyOrientation(
            new PointF((float)p.X, (float)(1 - p.Y)), frame.Rotation, frame.IsMirrored);

        return new DocumentQuad(P(doc.TopLeft), P(doc.TopRight), P(doc.BottomRight), P(doc.BottomLeft));
    }

    // Map "clockwise degrees to make upright" + mirror to the EXIF orientation CIImage applies.
    static CGImagePropertyOrientation Orientation(int rotation, bool mirrored)
    {
        var r = ((rotation % 360) + 360) % 360;
        return (r, mirrored) switch
        {
            (90, false) => CGImagePropertyOrientation.Right,        // 6
            (180, false) => CGImagePropertyOrientation.Down,        // 3
            (270, false) => CGImagePropertyOrientation.Left,        // 8
            (0, true) => CGImagePropertyOrientation.UpMirrored,     // 2
            (90, true) => CGImagePropertyOrientation.RightMirrored, // 7
            (180, true) => CGImagePropertyOrientation.DownMirrored, // 4
            (270, true) => CGImagePropertyOrientation.LeftMirrored, // 5
            _ => CGImagePropertyOrientation.Up                      // 1 (upright)
        };
    }

    static byte[]? EncodeJpeg(CGImage image, float quality)
    {
        using var data = new NSMutableData();
        using var dest = CGImageDestination.Create(data, "public.jpeg", 1);
        if (dest == null)
            return null;

        dest.AddImage(image, new CGImageDestinationOptions { LossyCompressionQuality = quality });
        return dest.Close() ? data.ToArray() : null;
    }
}
