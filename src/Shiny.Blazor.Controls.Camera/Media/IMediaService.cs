namespace Shiny.Blazor.Controls.Camera.Media;

/// <summary>
/// The one service to inject for anything camera- or gallery-shaped in Blazor: permissions, taking a photo or
/// recording a video through Shiny's <b>own</b> modal <see cref="CameraView"/>, picking from the device's
/// files/gallery, and — through the <c>Scan…</c> extensions — barcodes and documents. The Blazor mirror of the
/// MAUI <c>IMediaService</c>.
/// </summary>
/// <remarks>
/// <para>
/// Register with <c>AddShinyMediaService()</c>, render one <c>&lt;MediaHost /&gt;</c> in your layout (it draws
/// the modal), and inject <see cref="IMediaService"/>. Every method returns <c>null</c> (or an empty list)
/// rather than throwing when the user declines or cancels — a cancelled camera is an ordinary outcome.
/// </para>
/// <para>
/// One modal at a time: starting a second capture or scan while one is on screen throws
/// <see cref="InvalidOperationException"/>.
/// </para>
/// <code>
/// var photo = await media.TakePhotoAsync(new PhotoCaptureOptions
/// {
///     Title = "Proof of delivery",
///     CompressionQuality = 80,
///     MaxDimension = 2048
/// });
/// if (photo is not null)
///     imageUrl = photo.ToDataUrl();
/// </code>
/// </remarks>
public interface IMediaService
{
    /// <summary>The live defaults set at registration. Change them at runtime and later calls pick them up.</summary>
    MediaServiceOptions Options { get; }

    /// <summary>
    /// False where there is no <c>getUserMedia</c> — an insecure (non-HTTPS) origin, or a browser without camera
    /// support. Gallery picking works either way.
    /// </summary>
    Task<bool> IsCameraSupportedAsync(CancellationToken ct = default);

    /// <summary>
    /// Ask for camera access (and the microphone when <paramref name="includeMicrophone"/>). The browser shows its
    /// own prompt; <see cref="MediaPermissionStatus.Granted"/> means every requested permission was granted.
    /// </summary>
    Task<MediaPermissionStatus> RequestCameraPermissionAsync(bool includeMicrophone = false, CancellationToken ct = default);

    /// <summary>
    /// The camera devices the browser exposes. Their names are only populated once camera permission has been
    /// granted. Feed an id to <see cref="MediaCameraOptions.CameraId"/>.
    /// </summary>
    Task<IReadOnlyList<CameraDevice>> GetAvailableCamerasAsync(CancellationToken ct = default);

    /// <summary>
    /// Present the modal camera and take one photo, re-encoded to the requested format, compression rate and
    /// maximum dimension. Returns <c>null</c> when the user closes the modal.
    /// </summary>
    Task<MediaPhoto?> TakePhotoAsync(PhotoCaptureOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Present the modal camera and record one video (<c>MediaRecorder</c>). Returns <c>null</c> when the user
    /// closes the modal. Dispose the result when done with it.
    /// </summary>
    Task<MediaVideo?> RecordVideoAsync(VideoCaptureOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Pick one photo with the browser's file chooser (the photo library / camera sheet on phones), re-encoded to
    /// the requested format, compression rate and maximum dimension. Returns <c>null</c> when cancelled.
    /// </summary>
    Task<MediaPhoto?> PickPhotoAsync(MediaPickOptions? options = null, CancellationToken ct = default);

    /// <summary>Pick up to <paramref name="maxCount"/> photos in one multi-select chooser. Empty when cancelled.</summary>
    Task<IReadOnlyList<MediaPhoto>> PickPhotosAsync(int maxCount = 10, MediaPickOptions? options = null, CancellationToken ct = default);

    /// <summary>Pick one video. Returns <c>null</c> when cancelled. Dispose the result when done with it.</summary>
    Task<MediaVideo?> PickVideoAsync(CancellationToken ct = default);

    /// <summary>
    /// Present the modal camera running <paramref name="request"/>'s analyzer and stream its results.
    /// </summary>
    /// <remarks>
    /// The modal opens when enumeration <i>starts</i> and closes when it ends — including when the caller
    /// <c>break</c>s out of the <c>await foreach</c>, which is how the single-result overloads
    /// (<c>ScanBarcodeAsync</c> and friends) are built. It also ends when the user closes the modal or taps ✓,
    /// when <see cref="MediaScanOptions.MaxResults"/> or <see cref="MediaScanOptions.Timeout"/> is reached, or
    /// on cancellation. Prefer the typed <c>Scan…</c> extensions; build a request yourself only for an analyzer
    /// Shiny does not ship one for.
    /// </remarks>
    IAsyncEnumerable<T> ScanAsync<T>(MediaScanRequest<T> request, MediaScanOptions? options = null, CancellationToken ct = default);
}
