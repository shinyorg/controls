namespace Shiny.Maui.Controls.Camera.Media;

/// <summary>
/// Everything the modal camera page shares regardless of what it is being opened for — which lens it starts
/// on, what chrome it offers, and what the frame should look like. The per-operation option classes
/// (<see cref="PhotoCaptureOptions"/>, <see cref="VideoCaptureOptions"/>, <see cref="MediaScanOptions"/>)
/// add only what is genuinely specific to that operation.
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

    /// <summary>Show the torch button. Default <c>true</c>.</summary>
    public bool AllowTorch { get; set; } = true;

    /// <summary>Start with the torch lit. Default <c>false</c>.</summary>
    public bool IsTorchOn { get; set; }

    /// <summary>
    /// Initial zoom factor. Default 1 (no zoom). Clamped to the lens's own range and to
    /// <see cref="MaxZoom"/>, and applied once the handler reports what the lens can actually do — so a
    /// request beyond the device's reach lands at its maximum rather than being dropped.
    /// </summary>
    public double Zoom { get; set; } = 1d;

    /// <summary>
    /// Whether the preview can be zoomed at all. Default <c>true</c>. Setting it <c>false</c> both disables
    /// pinch-to-zoom and pins the camera's usable range to its minimum, so nothing — a gesture, a
    /// <see cref="ConfigureCamera"/> hook, a binding — can zoom past 1×. Turn it off for a scanner or a
    /// document capture, where a zoomed frame is a cropped one and the detector loses the edges it needs.
    /// </summary>
    public bool AllowZoom { get; set; } = true;

    /// <summary>
    /// Cap the zoom the user can reach, as a factor (e.g. 4 for 4×). Null (default) allows whatever the lens
    /// reports.
    /// </summary>
    /// <remarks>
    /// This is a ceiling, never a floor: a device whose maximum is below the cap keeps its own, and a cap
    /// below the lens's minimum is raised to it rather than producing an empty range. Worth setting because
    /// the far end of a phone's range is usually digital crop — the picture gets bigger and no better — so
    /// letting a user reach 50× on a document scan mostly produces unreadable captures.
    /// </remarks>
    public double? MaxZoom { get; set; }

    /// <summary>How the preview fills its bounds. Default <see cref="PreviewScaleMode.AspectFill"/>.</summary>
    public PreviewScaleMode ScaleMode { get; set; } = PreviewScaleMode.AspectFill;

    /// <summary>
    /// The colour grade the modal opens with. When <see cref="ShowEffectPicker"/> is on this is just the
    /// initially-selected chip and the user can change it.
    /// </summary>
    public CameraFilter Filter { get; set; } = CameraFilter.None;

    /// <summary>
    /// Effects applied on top of <see cref="Filter"/>, in order. Live: the modal copies them onto the
    /// camera when it opens.
    /// </summary>
    public IList<ICameraEffect> Effects { get; } = new List<ICameraEffect>();

    /// <summary>
    /// Offer an on-screen strip of looks the user can tap through. Default <c>false</c> — set it (and
    /// optionally <see cref="EffectChoices"/>) when the picture is the point; leave it off for scanning,
    /// where a stylized frame is actively unhelpful.
    /// </summary>
    public bool ShowEffectPicker { get; set; }

    /// <summary>
    /// The looks offered when <see cref="ShowEffectPicker"/> is on. Null uses
    /// <see cref="MediaEffectChoices.Default"/> — "None" plus the eleven built-in colour grades and the five
    /// spatial effects.
    /// </summary>
    public IReadOnlyList<MediaEffectChoice>? EffectChoices { get; set; }

    /// <summary>
    /// Shown over the preview when camera permission is refused, in place of the message the service would
    /// otherwise word itself.
    /// </summary>
    public string PermissionDeniedText { get; set; } = "Camera access was denied. Enable it in Settings to continue.";

    /// <summary>
    /// Escape hatch: called with the modal's <see cref="CameraView"/> after it is configured and before it
    /// starts. Reach for it when you need a property this options class does not surface.
    /// </summary>
    public Action<CameraView>? ConfigureCamera { get; set; }

    /// <summary>
    /// Escape hatch: called with the modal page itself before it is presented — restyle the chrome, add your
    /// own overlay, set a background.
    /// </summary>
    public Action<ContentPage>? ConfigurePage { get; set; }
}


/// <summary>Options for <see cref="IMediaService.TakePhotoAsync"/>.</summary>
public class PhotoCaptureOptions : MediaCameraOptions
{
    /// <summary>
    /// How much the device should spend on the capture. Default <see cref="PhotoQuality.Highest"/> — this is
    /// a deliberate shutter press, not a scan.
    /// </summary>
    public PhotoQuality Quality { get; set; } = PhotoQuality.Highest;

    /// <summary>
    /// Re-encode compression rate, 1–100. Null (default) uses
    /// <see cref="MediaServiceOptions.CompressionQuality"/>. Ignored when <see cref="OutputFormat"/> resolves
    /// to <see cref="MediaImageFormat.Png"/>, which is lossless.
    /// </summary>
    public int? CompressionQuality { get; set; }

    /// <summary>
    /// Cap the longest edge at this many pixels, downscaling if needed. Null (default) uses
    /// <see cref="MediaServiceOptions.MaxDimension"/>; 0 keeps the captured size. This is the setting that
    /// actually shrinks a file — a 12MP capture stays multi-megabyte at any compression rate.
    /// </summary>
    public int? MaxDimension { get; set; }

    /// <summary>The encoding handed back on <see cref="MediaPhoto.Data"/>. Null (default) uses <see cref="MediaServiceOptions.OutputFormat"/>.</summary>
    public MediaImageFormat? OutputFormat { get; set; }

    /// <summary>Flash behaviour for the capture. Default <see cref="CameraFlashMode.Auto"/>.</summary>
    public CameraFlashMode FlashMode { get; set; } = CameraFlashMode.Auto;

    /// <summary>Show the flash-mode button. Default <c>true</c>.</summary>
    public bool AllowFlashToggle { get; set; } = true;

    /// <summary>
    /// Show the captured shot with retake (✕) / accept (✓) buttons before returning. Default <c>true</c> —
    /// without it a blurred shot is only discovered after the modal has gone.
    /// </summary>
    public bool ShowConfirmation { get; set; } = true;
}


/// <summary>Options for <see cref="IMediaService.RecordVideoAsync"/>.</summary>
public class VideoCaptureOptions : MediaCameraOptions
{
    /// <summary>Capture resolution. Default <see cref="VideoQuality.High"/> (1080p).</summary>
    public VideoQuality Quality { get; set; } = VideoQuality.High;

    /// <summary>Record audio too. Default <c>true</c> — which is why the service also asks for the microphone.</summary>
    public bool IncludeAudio { get; set; } = true;

    /// <summary>Stop and return automatically after this long. Null (default) records until the user stops.</summary>
    public TimeSpan? MaxDuration { get; set; }

    /// <summary>Encoder bitrate in bits/sec. Null uses the platform default for <see cref="Quality"/>.</summary>
    public int? Bitrate { get; set; }

    /// <summary>Capture frame rate. Null uses the platform default.</summary>
    public int? FrameRate { get; set; }

    /// <summary>Destination file. Null writes to a unique temp path with the platform's native extension.</summary>
    public string? FilePath { get; set; }

    /// <summary>Overlay burned into every recorded frame — watermark, timestamp, telemetry.</summary>
    public IVideoOverlayRenderer? Overlay { get; set; }

    /// <summary>Show the elapsed-time readout while recording. Default <c>true</c>.</summary>
    public bool ShowElapsed { get; set; } = true;
}


/// <summary>
/// Options for a scanning session — the shape shared by every <c>Scan…</c> extension (barcodes, OCR, credit
/// cards, licenses, passports, faces).
/// </summary>
public class MediaScanOptions : MediaCameraOptions
{
    /// <summary>
    /// Restrict detection (and the viewfinder reticle) to a normalized rectangle in upright image space.
    /// Null (default) scans the whole frame. A tight band is both faster and a better aim guide for
    /// single-code scanning.
    /// </summary>
    public RectF? ScanWindow { get; set; }

    /// <summary>Draw the analyzer's bounding boxes over the preview. Default <c>true</c>.</summary>
    public bool ShowBoundingBox { get; set; } = true;

    /// <summary>
    /// Suppress a result whose key matches one already yielded in this session. Default <c>true</c> — a code
    /// sitting in front of the lens is otherwise re-read every time it leaves and re-enters view. Only
    /// meaningful for the multi-result overloads; a single-result scan returns on the first hit either way.
    /// </summary>
    public bool FilterDuplicates { get; set; } = true;

    /// <summary>Stop the session after this many results. Null (default) runs until the caller or user stops it.</summary>
    public int? MaxResults { get; set; }

    /// <summary>
    /// Close the modal (ending the sequence) if nothing is found in this long. Null (default) waits
    /// indefinitely. This is an <i>idle</i> timeout — each result restarts the clock, so a session that is
    /// finding things is never cut off mid-scan.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Show a running count of results collected so far. Default <c>true</c> for multi-result scans.</summary>
    public bool ShowResultCount { get; set; } = true;

    /// <summary>
    /// Show the accept (✓) button that finishes a multi-result session. Default <c>true</c>. Turn it off for
    /// a scan the caller ends itself (a single-result scan, or one bounded by
    /// <see cref="MaxResults"/>/<see cref="Timeout"/>) — the dismiss button is always there regardless.
    /// </summary>
    public bool ShowDoneButton { get; set; } = true;

    /// <summary>Fire a short haptic tick on each accepted result. Default <c>true</c>.</summary>
    public bool VibrateOnResult { get; set; } = true;
}


/// <summary>Options for the gallery pickers on <see cref="IMediaService"/>.</summary>
public class MediaPickOptions
{
    /// <summary>Title for the system picker where the platform shows one.</summary>
    public string? Title { get; set; }

    /// <summary>Re-encode compression rate, 1–100. Null (default) uses <see cref="MediaServiceOptions.CompressionQuality"/>. Ignored for PNG output.</summary>
    public int? CompressionQuality { get; set; }

    /// <summary>Cap the longest edge at this many pixels. Null (default) uses <see cref="MediaServiceOptions.MaxDimension"/>; 0 keeps the original size.</summary>
    public int? MaxDimension { get; set; }

    /// <summary>The encoding handed back. Null (default) uses <see cref="MediaServiceOptions.OutputFormat"/>.</summary>
    public MediaImageFormat? OutputFormat { get; set; }
}


/// <summary>
/// Service-wide defaults, set once at <c>UseShinyCamera(cfg =&gt; …)</c> and applied to every call that does
/// not override them. This is where an app states "our photos are 85% JPEG capped at 2048px" rather than
/// repeating it at twenty call sites.
/// </summary>
public class MediaServiceOptions
{
    /// <summary>Default <see cref="PhotoCaptureOptions.CompressionQuality"/> / <see cref="MediaPickOptions.CompressionQuality"/>. Default 92.</summary>
    public int CompressionQuality { get; set; } = 92;

    /// <summary>Default <see cref="PhotoCaptureOptions.MaxDimension"/>. Default 0 (no downscale).</summary>
    public int MaxDimension { get; set; }

    /// <summary>Default output encoding for photos. Default <see cref="MediaImageFormat.Jpeg"/>.</summary>
    public MediaImageFormat OutputFormat { get; set; } = MediaImageFormat.Jpeg;

    /// <summary>Applied to every modal before its per-call options — set the house style once.</summary>
    public Action<MediaCameraOptions>? ConfigureDefaults { get; set; }
}


/// <summary>The stock look strip offered when <see cref="MediaCameraOptions.ShowEffectPicker"/> is on.</summary>
public static class MediaEffectChoices
{
    /// <summary>"None", the eleven built-in colour grades, then the five spatial effects.</summary>
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
