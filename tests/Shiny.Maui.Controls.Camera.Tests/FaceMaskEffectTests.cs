using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Analysis.Face;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Camera.Tests;

/// <summary>
/// Covers the maths that decides where a mask lands. Rendering itself needs a device; what is pinned here is
/// the anchoring, the scale reference, the roll, and the smoothing that stops the mask juddering when the
/// analysis pipeline drops frames.
/// </summary>
public class FaceMaskEffectTests
{
    const int FrameWidth = 1000;
    const int FrameHeight = 1000;

    static CameraEffectContext Context(IReadOnlyList<DetectedFace> faces, CameraSurface surface = CameraSurface.Preview)
        => new(TimeSpan.Zero, 0, FrameWidth, FrameHeight, CameraFacing.Front, surface, [], faces);

    // A face centred at (0.5, 0.5) with eyes 0.1 apart and level.
    static DetectedFace LevelFace(float centerX = 0.5f, int? trackingId = null) => new(
        new RectF(centerX - 0.15f, 0.35f, 0.3f, 0.3f),
        1f,
        new FaceLandmarks(
            LeftEye: new PointF(centerX - 0.05f, 0.45f),
            RightEye: new PointF(centerX + 0.05f, 0.45f)),
        trackingId);

    // GetPlacements is the whole decision Draw makes, so the placement maths is testable without a canvas.
    static IReadOnlyList<FaceMaskPlacement> Capture(FaceMaskEffect effect, CameraEffectContext context)
        => effect.GetPlacements(context);


    [Fact]
    public void Anchors_to_the_eye_centre_in_frame_pixels()
    {
        var effect = new FaceMaskEffect { Anchor = FaceAnchor.EyeCenter, Smoothing = 0f };
        var placements = Capture(effect, Context([LevelFace()]));

        placements.Count.ShouldBe(1);
        placements[0].Center.X.ShouldBe(500f, 0.5f);
        placements[0].Center.Y.ShouldBe(450f, 0.5f);
    }

    [Fact]
    public void Scales_from_the_eye_distance_not_the_face_box()
    {
        var effect = new FaceMaskEffect { Scale = 2f, Smoothing = 0f };
        var placements = Capture(effect, Context([LevelFace()]));

        // eye distance 0.1 * scale 2 * frame width 1000 = 200px, not the 0.3-wide box
        placements[0].Width.ShouldBe(200f, 0.5f);
    }

    [Fact]
    public void Falls_back_to_the_face_box_when_there_are_no_landmarks()
    {
        var noLandmarks = new DetectedFace(new RectF(0.35f, 0.35f, 0.3f, 0.3f));
        var effect = new FaceMaskEffect { Scale = 1f, Smoothing = 0f };

        var placements = Capture(effect, Context([noLandmarks]));

        placements.Count.ShouldBe(1);
        placements[0].Width.ShouldBe(300f, 0.5f);       // box width 0.3 * 1000
        placements[0].Center.X.ShouldBe(500f, 0.5f);    // box centre
    }

    [Fact]
    public void Follows_head_roll()
    {
        var tilted = new DetectedFace(
            new RectF(0.35f, 0.35f, 0.3f, 0.3f),
            1f,
            new FaceLandmarks(
                LeftEye: new PointF(0.45f, 0.45f),
                RightEye: new PointF(0.55f, 0.55f))); // 45 degrees down to the right

        var effect = new FaceMaskEffect { Smoothing = 0f };
        var placements = Capture(effect, Context([tilted]));

        placements[0].RotationDegrees.ShouldBe(45f, 0.5f);
    }

    [Fact]
    public void Roll_can_be_turned_off()
    {
        var tilted = new DetectedFace(
            new RectF(0.35f, 0.35f, 0.3f, 0.3f),
            1f,
            new FaceLandmarks(LeftEye: new PointF(0.45f, 0.45f), RightEye: new PointF(0.55f, 0.55f)));

        var effect = new FaceMaskEffect { Smoothing = 0f, FollowRoll = false };
        Capture(effect, Context([tilted]))[0].RotationDegrees.ShouldBe(0f);
    }

    [Fact]
    public void Offset_is_measured_in_multiples_of_the_mask_size()
    {
        var effect = new FaceMaskEffect { Scale = 2f, Smoothing = 0f, Offset = new PointF(0f, -0.5f) };
        var placements = Capture(effect, Context([LevelFace()]));

        // mask is 200px wide/tall, so -0.5 lifts it 100px
        placements[0].Center.Y.ShouldBe(350f, 0.5f);
    }


    [Fact]
    public void Smoothing_eases_towards_a_moved_face_rather_than_snapping()
    {
        var effect = new FaceMaskEffect { Smoothing = 0.5f };

        Capture(effect, Context([LevelFace(0.5f, trackingId: 1)]));
        var second = Capture(effect, Context([LevelFace(0.7f, trackingId: 1)]));

        // halfway between the old 500 and the new 700
        second[0].Center.X.ShouldBe(600f, 1f);
    }

    [Fact]
    public void Smoothing_off_tracks_the_raw_detection_exactly()
    {
        var effect = new FaceMaskEffect { Smoothing = 0f };

        Capture(effect, Context([LevelFace(0.5f, trackingId: 1)]));
        var second = Capture(effect, Context([LevelFace(0.7f, trackingId: 1)]));

        second[0].Center.X.ShouldBe(700f, 0.5f);
    }

    [Fact]
    public void Preview_and_recording_smooth_independently()
    {
        var effect = new FaceMaskEffect { Smoothing = 0.5f };

        // prime only the preview surface
        Capture(effect, Context([LevelFace(0.5f, trackingId: 1)], CameraSurface.Preview));

        // the video surface has no history, so its first frame must snap, not ease from the preview's state
        var video = Capture(effect, Context([LevelFace(0.7f, trackingId: 1)], CameraSurface.Video));
        video[0].Center.X.ShouldBe(700f, 0.5f);
    }

    [Fact]
    public void ResetTracking_makes_the_next_frame_snap()
    {
        var effect = new FaceMaskEffect { Smoothing = 0.5f };

        Capture(effect, Context([LevelFace(0.5f, trackingId: 1)]));
        effect.ResetTracking();
        var after = Capture(effect, Context([LevelFace(0.7f, trackingId: 1)]));

        after[0].Center.X.ShouldBe(700f, 0.5f);
    }

    [Fact]
    public void Tracking_ids_keep_masks_attached_to_the_right_face()
    {
        var effect = new FaceMaskEffect { Smoothing = 0.5f };

        Capture(effect, Context([LevelFace(0.2f, 1), LevelFace(0.8f, 2)]));

        // the detector reports them in the opposite order next frame; ids must win over ordering
        var second = Capture(effect, Context([LevelFace(0.8f, 2), LevelFace(0.2f, 1)]));

        second[0].Center.X.ShouldBe(800f, 1f);
        second[1].Center.X.ShouldBe(200f, 1f);
    }


    [Fact]
    public void Reports_once_when_there_is_no_face_analyzer_to_follow()
    {
        var effect = new FaceMaskEffect();
        var errors = new List<string>();
        effect.Error += (_, message) => errors.Add(message);

        var context = new CameraEffectContext(
            TimeSpan.Zero, 0, FrameWidth, FrameHeight, CameraFacing.Back, CameraSurface.Preview, [], null);

        effect.GetPlacements(context);
        effect.GetPlacements(context);

        errors.Count.ShouldBe(1, "the message is per-frame code, so it must not spam");
        errors[0].ShouldContain("FaceAnalyzer");
    }

    [Fact]
    public void Draws_nothing_when_no_faces_are_in_frame()
    {
        var effect = new FaceMaskEffect();
        Capture(effect, Context([])).ShouldBeEmpty();
    }


    [Fact]
    public void Landmarks_derive_eye_distance_and_roll()
    {
        var landmarks = new FaceLandmarks(
            LeftEye: new PointF(0.4f, 0.5f),
            RightEye: new PointF(0.6f, 0.5f));

        landmarks.EyeDistance!.Value.ShouldBe(0.2f, 0.0001f);
        landmarks.Roll!.Value.ShouldBe(0f, 0.0001f);
        landmarks.EyeCenter!.Value.X.ShouldBe(0.5f, 0.0001f);
        landmarks.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void Landmarks_report_empty_when_nothing_was_detected()
        => new FaceLandmarks().IsEmpty.ShouldBeTrue();
}
