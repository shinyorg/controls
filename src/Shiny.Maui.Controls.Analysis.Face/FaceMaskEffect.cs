using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;

// Microsoft.Maui also defines an IImage (the ImageSource-backed one); the drawable image is the Graphics one.
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Shiny.Maui.Controls.Analysis.Face;

/// <summary>Which facial feature a mask is pinned to.</summary>
public enum FaceAnchor
{
    /// <summary>Midpoint between the eyes — the right choice for glasses, masks and most overlays.</summary>
    EyeCenter,

    /// <summary>Centre of the detected face box — the fallback when landmarks aren't available.</summary>
    FaceCenter,

    /// <summary>Top of the face box — hats and crowns.</summary>
    Forehead,

    /// <summary>Base of the nose — moustaches, noses.</summary>
    Nose,

    /// <summary>Bottom of the mouth — beards.</summary>
    Mouth
}


/// <summary>Where and how big to draw a mask for one face, in frame pixel space.</summary>
/// <param name="Face">The face this placement was computed from.</param>
/// <param name="Center">Anchor point in frame pixels.</param>
/// <param name="Width">Mask width in frame pixels.</param>
/// <param name="Height">Mask height in frame pixels.</param>
/// <param name="RotationDegrees">Head roll, in degrees clockwise.</param>
public readonly record struct FaceMaskPlacement(
    DetectedFace Face,
    PointF Center,
    float Width,
    float Height,
    float RotationDegrees
);


/// <summary>
/// Draws an image (or anything you like) pinned to every tracked face — the Messenger-style funny-face
/// overlay. Add it to <c>CameraView.Effects</c> and it paints on the preview, into captured stills and into
/// recorded video from the one implementation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Requires a <see cref="FaceAnalyzer"/></b> assigned to <c>CameraView.Analyzer</c>, with
/// <see cref="FaceAnalyzer.DetectLandmarks"/> turned on for anything anchored to a feature rather than the
/// face box. Without it there is nothing to follow, and <see cref="Error"/> fires once to say so rather than
/// the effect silently drawing nothing.
/// </para>
/// <para>
/// <b>Smoothing matters.</b> The analysis pipeline runs one frame at a time and drops frames while it is busy,
/// so raw detections arrive in bursts at well under the display rate. Drawn directly they visibly judder,
/// which is why every placement is run through an exponential smoother keyed on the face. Set
/// <see cref="Smoothing"/> to 0 to see the raw signal.
/// </para>
/// <code>
/// camera.Analyzer = new FaceAnalyzer { DetectLandmarks = true };
/// camera.Effects.Add(new FaceMaskEffect
/// {
///     Mask = await LoadImageAsync("sunglasses.png"),
///     Anchor = FaceAnchor.EyeCenter,
///     Scale = 2.4f
/// });
/// </code>
/// </remarks>
public class FaceMaskEffect : IDrawEffect
{
    readonly object gate = new();
    readonly Dictionary<(CameraSurface Surface, int Key), Smoothed> tracked = [];
    int reportedMissingAnalyzer;

    /// <inheritdoc/>
    public string Id { get; set; } = "shiny.camera.face.mask";

    /// <inheritdoc/>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// The image drawn over each face. Ignored when <see cref="OnDraw"/> is set.
    /// </summary>
    public IImage? Mask { get; set; }

    /// <summary>
    /// Custom per-face drawing, for anything an image can't express. Receives the canvas already translated
    /// and rotated so that <c>(0,0)</c> is the anchor point and the x-axis follows the head's roll — draw
    /// around the origin and it tracks the face for free. Takes precedence over <see cref="Mask"/>.
    /// </summary>
    public Action<ICanvas, FaceMaskPlacement>? OnDraw { get; set; }

    /// <summary>Which feature the mask is pinned to. Default <see cref="FaceAnchor.EyeCenter"/>.</summary>
    public FaceAnchor Anchor { get; set; } = FaceAnchor.EyeCenter;

    /// <summary>
    /// Mask width as a multiple of the reference measure — the eye distance when landmarks are available, else
    /// the face box width. Default 2.4 (roughly face-width for a glasses-style overlay).
    /// </summary>
    public float Scale { get; set; } = 2.4f;

    /// <summary>
    /// Mask aspect ratio (width / height). Default 1. Ignored when <see cref="Mask"/> is set, whose own aspect
    /// is used instead.
    /// </summary>
    public float AspectRatio { get; set; } = 1f;

    /// <summary>Offset from the anchor, in multiples of the mask size. Default none.</summary>
    public PointF Offset { get; set; } = new(0f, 0f);

    /// <summary>Whether the mask rotates with the head's roll. Default <c>true</c>.</summary>
    public bool FollowRoll { get; set; } = true;

    /// <summary>
    /// Exponential smoothing factor, 0..1 — 0 disables smoothing (raw, juddery), higher is steadier but lags
    /// faster movement. Default 0.35.
    /// </summary>
    public float Smoothing { get; set; } = 0.35f;

    /// <summary>
    /// Raised <b>once</b> when the effect finds no face data to follow — almost always because the camera's
    /// analyzer isn't a <see cref="FaceAnalyzer"/>. One-shot because this is invoked per frame.
    /// </summary>
    public event EventHandler<string>? Error;

    /// <summary>
    /// Where the mask would be drawn for each face in <paramref name="context"/>, in frame pixel space and
    /// with smoothing applied.
    /// </summary>
    /// <remarks>
    /// Exposed because it is the whole decision this effect makes — useful for hit-testing, for driving your
    /// own renderer, and for asserting on placement without a canvas. Calling it advances the smoother, so
    /// call it once per frame, not speculatively.
    /// </remarks>
    public IReadOnlyList<FaceMaskPlacement> GetPlacements(CameraEffectContext context)
    {
        if (context.AnalyzerResult is not IReadOnlyList<DetectedFace> faces)
        {
            // AnalyzerResult being null every frame means nothing is publishing faces at all
            if (context.AnalyzerResult is null && Interlocked.Exchange(ref this.reportedMissingAnalyzer, 1) == 0)
            {
                this.Error?.Invoke(this,
                    "FaceMaskEffect has no faces to follow. Set CameraView.Analyzer to a FaceAnalyzer " +
                    "(with DetectLandmarks = true for feature-anchored masks).");
            }
            return [];
        }

        var placements = new List<FaceMaskPlacement>(faces.Count);
        for (var i = 0; i < faces.Count; i++)
        {
            if (this.Place(faces[i], i, context) is { } placement)
                placements.Add(placement);
        }

        return placements;
    }

    /// <inheritdoc/>
    public void Draw(ICanvas canvas, RectF frame, CameraEffectContext context)
    {
        foreach (var p in this.GetPlacements(context))
        {
            canvas.SaveState();
            try
            {
                canvas.Translate(p.Center.X, p.Center.Y);
                if (this.FollowRoll && p.RotationDegrees != 0)
                    canvas.Rotate(p.RotationDegrees);

                if (this.OnDraw is { } custom)
                    custom(canvas, p);
                else if (this.Mask is { } image)
                    canvas.DrawImage(image, -p.Width / 2f, -p.Height / 2f, p.Width, p.Height);
            }
            finally
            {
                canvas.RestoreState();
            }
        }
    }

    FaceMaskPlacement? Place(DetectedFace face, int index, CameraEffectContext context)
    {
        var anchor = this.ResolveAnchor(face);
        if (anchor is not { } normalized)
            return null;

        // Eye distance tracks apparent head size far more stably than the box, which breathes as the detector
        // re-fits it; fall back to the box only when landmarks aren't available.
        var reference = face.Landmarks?.EyeDistance ?? face.Bounds.Width;
        if (reference <= 0)
            return null;

        var width = reference * this.Scale * context.Width;
        var aspect = this.MaskAspect();
        var height = aspect > 0 ? width / aspect : width;

        var center = new PointF(
            (normalized.X * context.Width) + (this.Offset.X * width),
            (normalized.Y * context.Height) + (this.Offset.Y * height));

        var rotation = this.FollowRoll && face.Landmarks?.Roll is { } roll
            ? roll * 180f / MathF.PI
            : 0f;

        // Key on the tracking id where the backend supplies one, so masks stay attached to the right person
        // when faces cross; otherwise fall back to detection order, which is stable enough frame to frame.
        var key = (context.Surface, face.TrackingId ?? index);
        var smoothed = this.Smooth(key, center, width, height, rotation);

        return new FaceMaskPlacement(face, smoothed.Center, smoothed.Width, smoothed.Height, smoothed.Rotation);
    }

    PointF? ResolveAnchor(DetectedFace face)
    {
        var bounds = face.Bounds;
        var landmarks = face.Landmarks;

        return this.Anchor switch
        {
            FaceAnchor.EyeCenter => landmarks?.EyeCenter ?? Center(bounds),
            FaceAnchor.Nose => landmarks?.NoseBase ?? Center(bounds),
            FaceAnchor.Mouth => landmarks?.MouthBottom ?? new PointF(bounds.Center.X, bounds.Bottom),
            FaceAnchor.Forehead => new PointF(bounds.Center.X, bounds.Top),
            _ => Center(bounds)
        };

        static PointF Center(RectF r) => new(r.Center.X, r.Center.Y);
    }

    float MaskAspect()
    {
        if (this.Mask is { } image && image.Height > 0)
            return image.Width / image.Height;

        return this.AspectRatio > 0 ? this.AspectRatio : 1f;
    }

    Smoothed Smooth(( CameraSurface, int) key, PointF center, float width, float height, float rotation)
    {
        var factor = Math.Clamp(this.Smoothing, 0f, 0.95f);
        var next = new Smoothed(center, width, height, rotation);

        lock (this.gate)
        {
            if (factor <= 0f || !this.tracked.TryGetValue(key, out var previous))
            {
                this.tracked[key] = next;
                return next;
            }

            // Interpolate rotation the short way round so a wrap past ±180° doesn't spin the mask the long way.
            var delta = next.Rotation - previous.Rotation;
            while (delta > 180f) delta -= 360f;
            while (delta < -180f) delta += 360f;

            var blended = new Smoothed(
                new PointF(
                    Lerp(previous.Center.X, next.Center.X, factor),
                    Lerp(previous.Center.Y, next.Center.Y, factor)),
                Lerp(previous.Width, next.Width, factor),
                Lerp(previous.Height, next.Height, factor),
                previous.Rotation + (delta * (1f - factor)));

            this.tracked[key] = blended;
            return blended;
        }

        static float Lerp(float previous, float next, float factor) => (previous * factor) + (next * (1f - factor));
    }

    /// <summary>
    /// Forget all tracked positions, so the next frame snaps rather than easing in from a stale one. Call
    /// after flipping the camera or restarting the session.
    /// </summary>
    public void ResetTracking()
    {
        lock (this.gate)
            this.tracked.Clear();
    }

    readonly record struct Smoothed(PointF Center, float Width, float Height, float Rotation);
}
