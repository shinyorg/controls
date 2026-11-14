using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;
using Windows.Security.Cryptography;

namespace Shiny.Maui.Controls.Analysis.Face;

/// <summary>Face detection via Windows.Media.FaceAnalysis over the frame's Gray8 luminance plane.</summary>
public partial class FaceAnalyzer
{
    FaceDetector? detector;

    /// <inheritdoc/>
    public override async ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeAsync(VideoFrame frame, CancellationToken ct)
    {
        if (frame is not WindowsVideoFrame)
            return null;

        this.detector ??= await FaceDetector.CreateAsync();

        var lum = frame.GetLuminance().ToArray();
        var buffer = CryptographicBuffer.CreateFromByteArray(lum);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Gray8, frame.Width, frame.Height);

        var detected = await this.detector.DetectFacesAsync(bitmap);

        var faces = new List<DetectedFace>();
        foreach (var face in detected)
        {
            var b = face.FaceBox;
            var raw = new RectF((float)b.X / frame.Width, (float)b.Y / frame.Height,
                (float)b.Width / frame.Width, (float)b.Height / frame.Height);
            var box = CoordinateTransform.ApplyOrientation(raw, frame.Rotation, frame.IsMirrored);
            faces.Add(new DetectedFace(box));
        }
        return this.Report(faces);
    }
}
