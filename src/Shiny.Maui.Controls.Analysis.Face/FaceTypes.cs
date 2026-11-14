using Microsoft.Maui.Graphics;

namespace Shiny.Maui.Controls.Analysis.Face;

/// <summary>
/// Feature points for one face, in normalized upright image space. Every point is optional — backends differ
/// in what they report, and a face at a steep angle may hide features from any of them.
/// </summary>
/// <remarks>
/// Typed rather than a bare point list because anchoring anything to a face means knowing <i>which</i> point is
/// which: a hat needs the eye line, glasses need both eyes, a moustache needs the nose base. An unordered
/// <c>PointF[]</c> can't answer that.
/// </remarks>
/// <param name="LeftEye">Centre of the subject's left eye (image-left, i.e. as seen).</param>
/// <param name="RightEye">Centre of the subject's right eye (image-right, as seen).</param>
/// <param name="NoseBase">Base of the nose.</param>
/// <param name="MouthLeft">Left corner of the mouth.</param>
/// <param name="MouthRight">Right corner of the mouth.</param>
/// <param name="MouthBottom">Bottom/centre of the mouth.</param>
public record FaceLandmarks(
    PointF? LeftEye = null,
    PointF? RightEye = null,
    PointF? NoseBase = null,
    PointF? MouthLeft = null,
    PointF? MouthRight = null,
    PointF? MouthBottom = null
)
{
    /// <summary>Midpoint between the eyes, or <c>null</c> when both aren't known.</summary>
    public PointF? EyeCenter => this.LeftEye is { } l && this.RightEye is { } r
        ? new PointF((l.X + r.X) / 2f, (l.Y + r.Y) / 2f)
        : null;

    /// <summary>
    /// Normalized distance between the eyes — the natural scale reference for anything pinned to a face,
    /// since it tracks the head's apparent size far more stably than the bounding box does.
    /// </summary>
    public float? EyeDistance => this.LeftEye is { } l && this.RightEye is { } r
        ? MathF.Sqrt(((r.X - l.X) * (r.X - l.X)) + ((r.Y - l.Y) * (r.Y - l.Y)))
        : null;

    /// <summary>
    /// Head roll in radians, measured from the eye line (0 = level, positive = tilted clockwise on screen).
    /// </summary>
    public float? Roll => this.LeftEye is { } l && this.RightEye is { } r
        ? MathF.Atan2(r.Y - l.Y, r.X - l.X)
        : null;

    /// <summary><c>true</c> when nothing at all was reported.</summary>
    public bool IsEmpty =>
        this.LeftEye is null && this.RightEye is null && this.NoseBase is null &&
        this.MouthLeft is null && this.MouthRight is null && this.MouthBottom is null;
}


/// <summary>A single detected face in normalized, upright image space.</summary>
/// <param name="Bounds">Normalized bounds (0..1) of the face in upright image space.</param>
/// <param name="Confidence">Detector confidence 0..1 (1 when the backend doesn't report one).</param>
/// <param name="Landmarks">
/// Feature points, when the backend reports them (Apple Vision and Android MLKit do; Windows and the managed
/// fallback do not). <c>null</c> means "not available", not "no features".
/// </param>
/// <param name="TrackingId">
/// Stable id for this face across frames where the backend supports tracking, else <c>null</c>. Use it to keep
/// a per-face effect (a mask, a smoother) attached to the same person when several are in frame.
/// </param>
public record DetectedFace(
    RectF Bounds,
    float Confidence = 1f,
    FaceLandmarks? Landmarks = null,
    int? TrackingId = null
);


/// <summary>Carries the faces found in a frame to <see cref="FaceAnalyzer.FacesDetected"/> subscribers.</summary>
public class FacesDetectedEventArgs(IReadOnlyList<DetectedFace> faces) : EventArgs
{
    /// <summary>The faces detected in the frame (never empty when the event is raised).</summary>
    public IReadOnlyList<DetectedFace> Faces { get; } = faces;
}
