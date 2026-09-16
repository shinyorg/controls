using Android.Runtime;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Face;
using MlFace = Xamarin.Google.MLKit.Vision.Face.Face;

namespace Shiny.Maui.Controls.Camera.Face;

/// <summary>Face detection via Android MLKit.</summary>
public partial class FaceAnalyzer
{
    // Two detectors rather than one: landmark mode costs meaningfully more per frame, and MLKit bakes the
    // mode into the client at construction. They are created lazily so an app that never asks for landmarks
    // never pays to initialise that model.
    IFaceDetector? rectangleDetector;
    IFaceDetector? landmarkDetector;

    IFaceDetector Detector(bool withLandmarks)
    {
        if (withLandmarks)
        {
            return this.landmarkDetector ??= FaceDetection.GetClient(
                new FaceDetectorOptions.Builder()
                    .SetPerformanceMode(FaceDetectorOptions.PerformanceModeFast)!
                    .SetLandmarkMode(FaceDetectorOptions.LandmarkModeAll)!
                    .Build());
        }

        return this.rectangleDetector ??= FaceDetection.GetClient(
            new FaceDetectorOptions.Builder()
                .SetPerformanceMode(FaceDetectorOptions.PerformanceModeFast)!
                .Build());
    }

    // ML Kit detector clients hold native detector memory until Close(); dropping the reference is not enough.
    partial void ReleasePlatform()
    {
        Close(Interlocked.Exchange(ref this.rectangleDetector, null));
        Close(Interlocked.Exchange(ref this.landmarkDetector, null));

        static void Close(IFaceDetector? detector)
        {
            if (detector is null)
                return;
            try
            {
                detector.Close();
            }
            catch
            {
                // already closed / torn down with the process — nothing left to release
            }
            (detector as IDisposable)?.Dispose();
        }
    }

    /// <inheritdoc/>
    public override async ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeAsync(CameraFrame frame, CancellationToken ct)
    {
        if (frame is not AndroidCameraFrame android)
            return null;

        var mediaImage = android.Proxy.Image;
        if (mediaImage == null)
            return null;

        var rotation = android.Proxy.ImageInfo.RotationDegrees;
        var input = InputImage.FromMediaImage(mediaImage, rotation);

        // MLKit returns boxes in the upright (rotation-applied) image space
        var uprightW = rotation is 90 or 270 ? frame.Height : frame.Width;
        var uprightH = rotation is 90 or 270 ? frame.Width : frame.Height;

        var wantsLandmarks = this.DetectLandmarks;
        var result = await GmsTaskAwaiter
            .AwaitAsync(this.Detector(wantsLandmarks).Process(input))
            .ConfigureAwait(false);

        var faces = new List<DetectedFace>();
        if (result is JavaList items)
        {
            foreach (var item in items)
            {
                if (item is not MlFace face)
                    continue;

                var r = face.BoundingBox;
                var raw = new RectF((float)r.Left / uprightW, (float)r.Top / uprightH,
                    (float)r.Width() / uprightW, (float)r.Height() / uprightH);
                var box = CoordinateTransform.ApplyOrientation(raw, 0, frame.IsMirrored);

                var landmarks = wantsLandmarks
                    ? ReadLandmarks(face, uprightW, uprightH, frame.IsMirrored)
                    : null;

                faces.Add(new DetectedFace(box, 1f, landmarks, face.TrackingId?.IntValue()));
            }
        }
        return this.Report(faces);
    }

    // MLKit reports landmark positions in upright image pixels, so the mapping is a divide plus the same
    // mirror flip the bounding box gets.
    static FaceLandmarks? ReadLandmarks(MlFace face, int width, int height, bool mirrored)
    {
        PointF? Map(int landmarkType)
        {
            var landmark = face.GetLandmark(landmarkType);
            if (landmark?.Position is not { } p)
                return null;

            var x = p.X / width;
            var y = p.Y / height;
            if (mirrored)
                x = 1f - x;

            return new PointF(x, y);
        }

        // MLKit names eyes from the SUBJECT's point of view; FaceLandmarks names them as seen on screen, so
        // the mirrored front camera swaps them.
        var left = Map(FaceLandmark.LeftEye);
        var right = Map(FaceLandmark.RightEye);
        if (mirrored)
            (left, right) = (right, left);

        var mouthLeft = Map(FaceLandmark.MouthLeft);
        var mouthRight = Map(FaceLandmark.MouthRight);
        if (mirrored)
            (mouthLeft, mouthRight) = (mouthRight, mouthLeft);

        var result = new FaceLandmarks(
            LeftEye: left,
            RightEye: right,
            NoseBase: Map(FaceLandmark.NoseBase),
            MouthLeft: mouthLeft,
            MouthRight: mouthRight,
            MouthBottom: Map(FaceLandmark.MouthBottom)
        );

        return result.IsEmpty ? null : result;
    }
}
