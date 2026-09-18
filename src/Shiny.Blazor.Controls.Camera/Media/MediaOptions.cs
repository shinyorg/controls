using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;

namespace Shiny.Blazor.Controls.Camera.Media;

/// <summary>
/// Everything the modal camera shares regardless of what it is opened for — which lens it starts on, what
/// chrome it offers, and what the frame looks like. Mirrors the MAUI <c>MediaCameraOptions</c> minus what a
/// browser cannot do (torch, zoom, flash: <c>getUserMedia</c> has no portable control for them).
/// </summary>
public abstract class MediaCameraOptions
{
    /// <summary>Title shown in the modal's top bar. Null hides the title.</summary>
    public string? Title { get; set; }

    /// <summary>A line of guidance under the title (e.g. "Line the barcode up inside the box"). Null hides it.</summary>
    public string? Instructions { get; set; }

    /// <summary>Which lens to open on. Default <see cref="CameraFacing.Back"/>.</summary>
    public CameraFacing Facing { get; set; } = CameraFacing.Back;

    /// <summary>
    /// An exact device from <see cref="IMediaService.GetAvailableCamerasAsync"/> — wins over
    /// <see cref="Facing"/> when set.
    /// </summary>
    public string? CameraId { get; set; }

    /// <summary>Show the flip-camera button. Default <c>true</c>.</summary>
    public bool AllowCameraSwitch { get; set; } = true;

    /// <summary>The colour grade the modal opens with (the initially-selected chip when <see cref="ShowEffectPicker"/> is on).</summary>
    public CameraFilter Filter { get; set; } = CameraFilter.None;

    /// <summary>Effects applied on top of <see cref="Filter"/>, in order.</summary>
    public IList<ICameraEffect> Effects { get; } = new List<ICameraEffect>();

    /// <summary>Offer an on-screen strip of looks the user can tap through. Default <c>false</c>.</summary>
    public bool ShowEffectPicker { get; set; }

    /// <summary>The looks offered when <see cref="ShowEffectPicker"/> is on. Null uses <see cref="MediaEffectChoices.Default"/>.</summary>
    public IReadOnlyList<MediaEffectChoice>? EffectChoices { get; set; }

    /// <summary>Shown over the preview when the browser refuses the camera.</summary>
    public string PermissionDeniedText { get; set; } = "Camera access was blocked. Allow it from the site settings in your browser's address bar, then try again.";

    /// <summary>Accessible label and caption of the close button. Default "Close".</summary>
    public string CloseText { get; set; } = "Close";
}


/// <summary>Options for <see cref="IMediaService.TakePhotoAsync"/>.</summary>
public class PhotoCaptureOptions : MediaCameraOptions
{
    /// <summary>Re-encode compression rate, 1–100. Null uses <see cref="MediaServiceOptions.CompressionQuality"/>. Ignored for PNG.</summary>
    public int? CompressionQuality { get; set; }

    /// <summary>Cap the longest edge at this many pixels. Null uses <see cref="MediaServiceOptions.MaxDimension"/>; 0 keeps the captured size.</summary>
    public int? MaxDimension { get; set; }

    /// <summary>The encoding handed back. Null uses <see cref="MediaServiceOptions.OutputFormat"/>.</summary>
    public MediaImageFormat? OutputFormat { get; set; }

    /// <summary>
    /// Show the captured shot with retake (✕) / accept (✓) buttons before returning. Default <c>true</c> —
    /// without it a blurred shot is only discovered after the modal has gone.
    /// </summary>
    public bool ShowConfirmation { get; set; } = true;
}


/// <summary>Options for <see cref="IMediaService.RecordVideoAsync"/>.</summary>
public class VideoCaptureOptions : MediaCameraOptions
{
    /// <summary>Record audio too. Default <c>true</c> — the browser asks for the microphone when recording starts, and records video-only if refused.</summary>
    public bool IncludeAudio { get; set; } = true;

    /// <summary>Stop and return automatically after this long. Null records until the user stops.</summary>
    public TimeSpan? MaxDuration { get; set; }

    /// <summary>Show the elapsed-time readout while recording. Default <c>true</c>.</summary>
    public bool ShowElapsed { get; set; } = true;
}


/// <summary>
/// Options for a scanning session — the shape shared by every <c>Scan…</c> extension.
/// </summary>
public class MediaScanOptions : MediaCameraOptions
{
    /// <summary>
    /// Restrict detection (and the viewfinder reticle) to a normalized rectangle in upright video space. Null
    /// scans the whole frame.
    /// </summary>
    public RectF? ScanWindow { get; set; }

    /// <summary>Draw the analyzer's bounding boxes over the preview. Default <c>true</c>.</summary>
    public bool ShowBoundingBox { get; set; } = true;

    /// <summary>
    /// Suppress a result whose key matches one already yielded in this session. Default <c>true</c> — a code
    /// held in front of the lens is otherwise read again on every frame.
    /// </summary>
    public bool FilterDuplicates { get; set; } = true;

    /// <summary>Stop the session after this many results. Null runs until the caller or user stops it.</summary>
    public int? MaxResults { get; set; }

    /// <summary>
    /// Close the modal (ending the sequence) if nothing is found in this long. An <i>idle</i> timeout — each
    /// result restarts the clock. Null waits indefinitely.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Show a running count of results collected so far. Default <c>true</c>.</summary>
    public bool ShowResultCount { get; set; } = true;

    /// <summary>Show the accept (✓) button that finishes a multi-result session. Default <c>true</c>.</summary>
    public bool ShowDoneButton { get; set; } = true;

    /// <summary>Vibrate briefly on each accepted result, where the browser supports it (Android Chrome). Default <c>true</c>.</summary>
    public bool VibrateOnResult { get; set; } = true;
}


/// <summary>Options for the gallery pickers on <see cref="IMediaService"/>.</summary>
public class MediaPickOptions
{
    /// <summary>Re-encode compression rate, 1–100. Null uses <see cref="MediaServiceOptions.CompressionQuality"/>. Ignored for PNG.</summary>
    public int? CompressionQuality { get; set; }

    /// <summary>Cap the longest edge at this many pixels. Null uses <see cref="MediaServiceOptions.MaxDimension"/>; 0 keeps the original size.</summary>
    public int? MaxDimension { get; set; }

    /// <summary>The encoding handed back. Null uses <see cref="MediaServiceOptions.OutputFormat"/>.</summary>
    public MediaImageFormat? OutputFormat { get; set; }
}


/// <summary>
/// Service-wide defaults, set once at <c>AddShinyMediaService(cfg =&gt; …)</c> and applied to every call that
/// does not override them.
/// </summary>
public class MediaServiceOptions
{
    /// <summary>Default compression rate for photos. Default 92.</summary>
    public int CompressionQuality { get; set; } = 92;

    /// <summary>Default maximum long edge for photos. Default 0 (no downscale).</summary>
    public int MaxDimension { get; set; }

    /// <summary>Default output encoding for photos. Default <see cref="MediaImageFormat.Jpeg"/>.</summary>
    public MediaImageFormat OutputFormat { get; set; } = MediaImageFormat.Jpeg;

    /// <summary>Applied to every modal before its per-call options — set the house style once.</summary>
    public Action<MediaCameraOptions>? ConfigureDefaults { get; set; }
}


/// <summary>The stock look strip offered when <see cref="MediaCameraOptions.ShowEffectPicker"/> is on.</summary>
public static class MediaEffectChoices
{
    /// <summary>"None", the built-in colour grades, then the spatial effects the browser renders through SVG filters.</summary>
    public static IReadOnlyList<MediaEffectChoice> Default { get; } =
    [
        new("None"),
        .. Enum.GetValues<CameraFilter>()
            .Where(f => f != CameraFilter.None)
            .Select(f => new MediaEffectChoice(f.ToString(), f)),
        new("Comic", CameraFilter.None, CameraEffects.Comic),
        new("Sketch", CameraFilter.None, CameraEffects.Sketch),
        new("Poster", CameraFilter.None, CameraEffects.Posterize),
        new("Pixelate", CameraFilter.None, CameraEffects.Pixelate),
        new("Blur", CameraFilter.None, CameraEffects.Blur)
    ];
}
