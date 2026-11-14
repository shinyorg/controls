using CoreGraphics;
using CoreImage;
using Foundation;
using Microsoft.Maui.Graphics;
using Vision;

namespace Shiny.Maui.Controls.Analysis.Ocr;

public partial class TextRecognizer
{
    private partial Task<List<RecognizedText>> RecognizeCoreAsync(VideoFrame frame, TextRecognitionOptions options, CancellationToken ct)
    {
        if (frame is not AppleVideoFrame apple)
            return Task.FromResult(new List<RecognizedText>());

        using var cg = apple.ToCGImage();
        if (cg == null)
            return Task.FromResult(new List<RecognizedText>());

        try
        {
            if (options.RegionOfInterest is not { } roi)
                // whole-frame OCR: boxes are in sensor space, so apply the frame's orientation
                return Task.FromResult(RecognizeText(cg, frame, options.MinimumTextHeight));

            return Task.FromResult(RecognizeRegion(cg, frame, roi, options));
        }
        catch (Exception ex)
        {
            return Task.FromException<List<RecognizedText>>(ex);
        }
    }


    // Region OCR. The ROI arrives in upright space but the CGImage is the raw sensor buffer, so the crop
    // rectangle has to be inverted back into sensor coordinates first. Recognizing the crop (rather than
    // setting Vision's regionOfInterest) is what actually helps small text: minimumTextHeight and Vision's
    // internal downscale are both relative to the image it is handed, so a crop makes the text a far larger
    // fraction of it — and upscaling that crop pushes it past the engine's resolution floor.
    static List<RecognizedText> RecognizeRegion(CGImage full, VideoFrame frame, RectF roi, TextRecognitionOptions options)
    {
        var sensor = CoordinateTransform.InvertOrientation(Clamp(roi), frame.Rotation, frame.IsMirrored);

        // integral pixel rect, clamped inside the image and never degenerate
        var x = (int)MathF.Floor(sensor.X * full.Width);
        var y = (int)MathF.Floor(sensor.Y * full.Height);
        var w = (int)MathF.Ceiling(sensor.Width * full.Width);
        var h = (int)MathF.Ceiling(sensor.Height * full.Height);
        x = Math.Clamp(x, 0, (int)full.Width - 1);
        y = Math.Clamp(y, 0, (int)full.Height - 1);
        w = Math.Clamp(w, 1, (int)full.Width - x);
        h = Math.Clamp(h, 1, (int)full.Height - y);

        using var crop = full.WithImageInRect(new CGRect(x, y, w, h));
        if (crop == null)
            return RecognizeText(full, frame, options.MinimumTextHeight); // crop failed — whole frame is better than nothing

        using var scaled = Upscale(crop, options.MinimumInputHeight);
        var blocks = RecognizeText(scaled ?? crop, null, options.MinimumTextHeight);

        // crop space -> full sensor space -> upright space
        var sx = (float)x / full.Width;
        var sy = (float)y / full.Height;
        var sw = (float)w / full.Width;
        var sh = (float)h / full.Height;

        for (var i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i].BoundingBox;
            var inSensor = new RectF(sx + b.X * sw, sy + b.Y * sh, b.Width * sw, b.Height * sh);
            blocks[i] = blocks[i] with
            {
                BoundingBox = CoordinateTransform.ApplyOrientation(inSensor, frame.Rotation, frame.IsMirrored)
            };
        }
        return blocks;
    }


    static RectF Clamp(RectF r)
    {
        var x = Math.Clamp(r.X, 0f, 1f);
        var y = Math.Clamp(r.Y, 0f, 1f);
        return new RectF(x, y, Math.Clamp(r.Width, 0.001f, 1f - x), Math.Clamp(r.Height, 0.001f, 1f - y));
    }


    // Resample the crop up so it is at least minHeight tall, preserving aspect. Returns null when no upscale
    // is wanted or needed, so the caller keeps using the original and nothing is allocated.
    static CGImage? Upscale(CGImage crop, int minHeight)
    {
        if (minHeight <= 0 || crop.Height >= minHeight)
            return null;

        var scale = (float)minHeight / crop.Height;
        var w = Math.Max(1, (int)(crop.Width * scale));
        var h = Math.Max(1, (int)(crop.Height * scale));

        using var colorSpace = CGColorSpace.CreateDeviceRGB();
        using var context = new CGBitmapContext(null, w, h, 8, 0, colorSpace, CGImageAlphaInfo.NoneSkipFirst);
        context.InterpolationQuality = CGInterpolationQuality.High;
        context.DrawImage(new CGRect(0, 0, w, h), crop);
        return context.ToImage();
    }

    private partial Task<RecognizedDocument> RecognizeDocumentCoreAsync(VideoFrame frame, CancellationToken ct)
    {
        if (frame is not AppleVideoFrame apple)
            return Task.FromResult(new RecognizedDocument([], null));

        using var cg = apple.ToCGImage();
        if (cg == null)
            return Task.FromResult(new RecognizedDocument([], null));

        try
        {
            // 1) Detect the document quad. Vision document segmentation is iOS 15+/macOS 12+; if it's
            //    unavailable or finds nothing we just OCR the whole frame.
            VNRectangleObservation? doc = null;
            try
            {
                var seg = new VNDetectDocumentSegmentationRequest((req, err) =>
                {
                    if (err == null)
                        doc = req.GetResults<VNRectangleObservation>()?.FirstOrDefault();
                });
                using var segHandler = new VNImageRequestHandler(cg, new NSDictionary());
                segHandler.Perform([seg], out _);
            }
            catch
            {
                doc = null;
            }

            if (doc == null)
                // no document found: whole-frame OCR (boxes in sensor space → orient)
                return Task.FromResult(new RecognizedDocument(RecognizeText(cg, frame), null));

            // 2) Deskew the document into a flat image, then OCR that (boxes already in upright doc space).
            using var rectified = PerspectiveCorrect(cg, doc);
            var quad = ToFrameQuad(doc, frame);
            if (rectified == null)
                return Task.FromResult(new RecognizedDocument(RecognizeText(cg, frame), quad));

            return Task.FromResult(new RecognizedDocument(RecognizeText(rectified, null), quad));
        }
        catch (Exception ex)
        {
            return Task.FromException<RecognizedDocument>(ex);
        }
    }

    // Run Vision text recognition over an image. When frameForOrientation is set the image is the raw sensor
    // buffer, so results are mapped into upright space; when null the image is already upright (a deskewed crop).
    static List<RecognizedText> RecognizeText(CGImage image, VideoFrame? frameForOrientation, float minimumTextHeight = 0f)
    {
        var blocks = new List<RecognizedText>();
        var request = new VNRecognizeTextRequest((req, err) =>
        {
            if (err == null && req.GetResults<VNRecognizedTextObservation>() is { } results)
            {
                foreach (var obs in results)
                {
                    var candidate = obs.TopCandidates(1)?.FirstOrDefault();
                    var bb = obs.BoundingBox; // normalized, origin bottom-left
                    var raw = new RectF((float)bb.X, (float)(1 - bb.Y - bb.Height), (float)bb.Width, (float)bb.Height);
                    var box = frameForOrientation is { } f
                        ? CoordinateTransform.ApplyOrientation(raw, f.Rotation, f.IsMirrored)
                        : raw;
                    blocks.Add(new RecognizedText(candidate?.String ?? string.Empty, box, candidate?.Confidence ?? 1f));
                }
            }
        })
        {
            RecognitionLevel = VNRequestTextRecognitionLevel.Accurate,
            // Off: Vision's dictionary "correction" mangles structured fields (license/MRZ/card numbers,
            // totals, dates) by snapping codes to words (0->O, 1->I). Our parsers fuzzy-match raw text.
            UsesLanguageCorrection = false
        };

        // Vision ignores text shorter than this fraction of the image height; the default 1/32 is ~34px in a
        // 1080p frame, which silently discards anything small (a plate, a distant sign) before we ever see it.
        if (minimumTextHeight > 0f)
            request.MinimumTextHeight = minimumTextHeight;

        using var handler = new VNImageRequestHandler(image, new NSDictionary());
        handler.Perform([request], out _);
        return blocks;
    }

    // Perspective-correct (deskew) the document region into a flat CGImage using Core Image. Vision corner
    // points are normalized with a bottom-left origin, matching CIImage's coordinate space.
    static CGImage? PerspectiveCorrect(CGImage source, VNRectangleObservation doc)
    {
        using var ci = new CIImage(source);
        var ext = ci.Extent;
        CIVector V(CGPoint p) => new((nfloat)(ext.X + p.X * ext.Width), (nfloat)(ext.Y + p.Y * ext.Height));

        using var filter = CIFilter.FromName("CIPerspectiveCorrection");
        if (filter == null)
            return null;
        filter.SetValueForKey(ci, (NSString)"inputImage");
        filter.SetValueForKey(V(doc.TopLeft), (NSString)"inputTopLeft");
        filter.SetValueForKey(V(doc.TopRight), (NSString)"inputTopRight");
        filter.SetValueForKey(V(doc.BottomRight), (NSString)"inputBottomRight");
        filter.SetValueForKey(V(doc.BottomLeft), (NSString)"inputBottomLeft");

        var output = filter.OutputImage;
        if (output == null)
            return null;

        using var context = new CIContext();
        return context.CreateCGImage(output, output.Extent);
    }

    // Vision corners are normalized with a bottom-left origin; flip Y to top-left and apply the frame's
    // orientation so the quad lands in the same upright space the overlay draws in.
    static DocumentQuad ToFrameQuad(VNRectangleObservation doc, VideoFrame frame)
    {
        PointF P(CGPoint p) => CoordinateTransform.ApplyOrientation(
            new PointF((float)p.X, (float)(1 - p.Y)), frame.Rotation, frame.IsMirrored);

        return new DocumentQuad(P(doc.TopLeft), P(doc.TopRight), P(doc.BottomRight), P(doc.BottomLeft));
    }
}
