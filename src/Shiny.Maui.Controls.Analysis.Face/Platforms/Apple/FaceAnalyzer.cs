using Foundation;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using Vision;

namespace Shiny.Maui.Controls.Analysis.Face;

/// <summary>Face detection via Apple Vision (iOS, MacCatalyst, macOS).</summary>
public partial class FaceAnalyzer
{
    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeAsync(VideoFrame frame, CancellationToken ct)
    {
        if (frame is not AppleVideoFrame apple)
            return default;

        using var cg = apple.ToCGImage();
        if (cg == null)
            return default;

        var tcs = new TaskCompletionSource<IReadOnlyList<OverlayBox>?>();

        // Landmarks cost more than rectangles alone, so only ask for them when something wants them.
        VNRequest request = this.DetectLandmarks
            ? new VNDetectFaceLandmarksRequest((req, err) => tcs.TrySetResult(this.Handle(req, err, frame)))
            : new VNDetectFaceRectanglesRequest((req, err) => tcs.TrySetResult(this.Handle(req, err, frame)));

        try
        {
            using var handler = new VNImageRequestHandler(cg, new NSDictionary());
            handler.Perform([request], out _);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
        finally
        {
            request.Dispose();
        }

        return new ValueTask<IReadOnlyList<OverlayBox>?>(tcs.Task);
    }

    IReadOnlyList<OverlayBox>? Handle(VNRequest req, NSError? err, VideoFrame frame)
    {
        var faces = new List<DetectedFace>();
        if (err == null && req.GetResults<VNFaceObservation>() is { } observations)
        {
            foreach (var face in observations)
            {
                var bb = face.BoundingBox; // normalized, origin bottom-left
                var raw = new RectF((float)bb.X, (float)(1 - bb.Y - bb.Height), (float)bb.Width, (float)bb.Height);
                var box = CoordinateTransform.ApplyOrientation(raw, frame.Rotation, frame.IsMirrored);

                faces.Add(new DetectedFace(box, face.Confidence, ReadLandmarks(face, frame)));
            }
        }
        return this.Report(faces);
    }

    // Vision reports landmark points normalized to the FACE BOX, origin bottom-left. Getting from there to
    // normalized upright image space means: scale into the box, flip Y, then apply the frame's orientation —
    // the same transform the bounding box goes through, so the two stay registered with each other.
    static FaceLandmarks? ReadLandmarks(VNFaceObservation face, VideoFrame frame)
    {
        if (face.Landmarks is not { } landmarks)
            return null;

        var bb = face.BoundingBox;

        PointF? Map(VNFaceLandmarkRegion2D? region)
        {
            if (region is null || region.PointCount == 0)
                return null;

            // average the region's points to a single anchor (Vision gives contours, not single points)
            var points = region.NormalizedPoints;
            if (points is null || points.Length == 0)
                return null;

            double sx = 0, sy = 0;
            foreach (var p in points)
            {
                sx += p.X;
                sy += p.Y;
            }

            var cx = (float)(bb.X + ((sx / points.Length) * bb.Width));
            var cyBottomUp = (float)(bb.Y + ((sy / points.Length) * bb.Height));

            // flip to top-left origin, then orient exactly as the bounding box was
            var asRect = new RectF(cx, 1f - cyBottomUp, 0f, 0f);
            var oriented = CoordinateTransform.ApplyOrientation(asRect, frame.Rotation, frame.IsMirrored);
            return new PointF(oriented.X, oriented.Y);
        }

        // Vision names eyes from the SUBJECT's point of view; FaceLandmarks names them as seen on screen. The
        // mirror flip above moved the coordinates correctly but not the labels, so swap them here — otherwise
        // anything anchored to one eye jumps to the other the moment the user flips to the front camera.
        var left = Map(landmarks.LeftEye);
        var right = Map(landmarks.RightEye);
        if (frame.IsMirrored)
            (left, right) = (right, left);

        var result = new FaceLandmarks(
            LeftEye: left,
            RightEye: right,
            NoseBase: Map(landmarks.Nose),
            MouthLeft: null,
            MouthRight: null,
            MouthBottom: Map(landmarks.OuterLips)
        );

        return result.IsEmpty ? null : result;
    }
}
