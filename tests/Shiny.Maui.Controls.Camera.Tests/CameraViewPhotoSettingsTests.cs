using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Camera.Tests;

/// <summary>
/// The still capture settings. As with the video ones, the native mapping lives in the platform handlers and
/// is unreachable from the base TFM, so what is pinned here is the contract every handler reads: the
/// defaults, the independence from <see cref="VideoQuality"/>, and the clamping the encoders rely on.
/// </summary>
public class CameraViewPhotoSettingsTests
{
    [Fact]
    public void Photo_quality_defaults_to_highest()
    {
        // The whole point of the property. Before it existed the still rode the session preset, so a default
        // 1080p session produced a ~2MP photo from a 12MP sensor and there was no way to ask for more. A
        // change to this default silently makes every consumer's photos smaller again, which is exactly the
        // regression worth failing loudly on.
        new CameraView().PhotoQuality.ShouldBe(PhotoQuality.Highest);
    }


    [Fact]
    public void Photo_quality_is_independent_of_video_quality()
    {
        // The two were one knob before, and the coupling was the bug. A recording sized down for storage or
        // heat must not drag the stills down with it.
        var view = new CameraView { VideoQuality = VideoQuality.Low };

        view.PhotoQuality.ShouldBe(PhotoQuality.Highest);
        view.VideoQuality.ShouldBe(VideoQuality.Low);

        view.PhotoQuality = PhotoQuality.Session;
        view.VideoQuality.ShouldBe(VideoQuality.Low);
    }


    [Fact]
    public void Jpeg_quality_defaults_to_high_but_not_lossless()
    {
        // 0.9 rather than 1.0: the top of the JPEG scale costs a large multiple of the file size for a
        // difference that does not survive being looked at. This is the value handed to CameraX and to the
        // Windows encoder on every capture, and to ImageIO on Apple whenever effects force a re-encode.
        new CameraView().PhotoJpegQuality.ShouldBe(0.9d);
    }


    [Theory]
    [InlineData(-1.0, 0f)]
    [InlineData(0.0, 0f)]
    [InlineData(0.5, 0.5f)]
    [InlineData(1.0, 1f)]
    [InlineData(1.5, 1f)]
    [InlineData(double.NaN, 0.9f)]
    public void Encoder_jpeg_quality_is_clamped(double set, float expected)
    {
        // Every platform encoder rejects or misbehaves outside 0-1, and a binding that produces 1.2 should
        // cost a slightly-too-large photo rather than a failed capture. NaN is the case Math.Clamp does NOT
        // handle — it compares false both ways and passes straight through — so it is sent back to the
        // default instead, a zero-quality photo being the worse of the two recoveries.
        var view = new CameraView { PhotoJpegQuality = set };

        view.EncoderJpegQuality.ShouldBe(expected);
    }


    [Fact]
    public void Every_rung_round_trips()
    {
        // The property is driven remotely in at least one consumer (set over HTTP, applied on the UI thread),
        // so it has to survive being written to as an ordinary bindable property from off the view.
        foreach (var rung in Enum.GetValues<PhotoQuality>())
        {
            var view = new CameraView { PhotoQuality = rung };
            view.PhotoQuality.ShouldBe(rung);
        }
    }
}
