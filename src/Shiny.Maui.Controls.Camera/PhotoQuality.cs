namespace Shiny.Maui.Controls.Camera;

/// <summary>
/// How much the device should spend on a still capture, expressed as an intent rather than exact pixels.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately separate from <see cref="VideoQuality"/>, which sizes the capture <i>session</i>. A
/// still and a video frame are not the same picture: phones expose a still pipeline that reaches the full
/// sensor and, at the top rung, fuses several exposures, while the video pipeline is sized for a sustained
/// frame rate. Tying the two together — which is what this control did before this property existed — meant a
/// 1080p session handed back a 2MP photo from a 12MP sensor, which is the wrong answer for anything a user
/// would call "taking a picture".
/// </para>
/// <para>
/// Only <see cref="Session"/> costs nothing. The other two ask the device for its full still resolution, so
/// the shutter takes longer, the file is larger, and on Apple the capture may briefly interrupt the video
/// data output. That is the trade being made, and it is why the rungs are named for what they spend rather
/// than for a pixel count that varies by an order of magnitude between devices.
/// </para>
/// <para>
/// Every rung falls back rather than failing: a device that cannot honour the request captures at the best it
/// can. Read <see cref="CameraPhoto.Width"/> and <see cref="CameraPhoto.Height"/> off the result to see what
/// was actually produced — this property records what was asked for, not what was granted.
/// </para>
/// </remarks>
public enum PhotoQuality
{
    /// <summary>
    /// Capture at whatever the session is already configured for — the cheapest and fastest option, and the
    /// behaviour of this control before <see cref="CameraView.PhotoQuality"/> existed.
    /// </summary>
    /// <remarks>
    /// The still comes out at roughly <see cref="CameraView.VideoQuality"/>'s resolution, because that is what
    /// the session is sized for. Worth choosing when the picture is an input to something else — a scan, a
    /// thumbnail, a frame posted to a service — rather than a photograph somebody will look at.
    /// </remarks>
    Session,

    /// <summary>
    /// The device's full still resolution, favouring a fast shutter over the last of the image quality.
    /// </summary>
    /// <remarks>
    /// The right default for burst-ish or hand-held use where shutter lag is more annoying than noise:
    /// Apple's balanced prioritization and CameraX's minimise-latency capture mode both mean "full size, one
    /// exposure, now".
    /// </remarks>
    Balanced,

    /// <summary>
    /// The device's full still resolution at the best quality it offers. The default.
    /// </summary>
    /// <remarks>
    /// Lets the platform spend time: Apple's quality prioritization enables multi-frame fusion where the
    /// hardware has it, and CameraX's maximise-quality mode runs additional processing. Noticeably slower per
    /// shot — tens to hundreds of milliseconds — which is the correct trade for a camera on a mount or a
    /// deliberate shutter press, and the wrong one for a scanner.
    /// </remarks>
    Highest
}
