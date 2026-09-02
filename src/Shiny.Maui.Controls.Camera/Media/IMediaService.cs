namespace Shiny.Maui.Controls.Camera.Media;

/// <summary>
/// The one service to inject for anything camera- or gallery-shaped: permissions, taking a photo or video
/// through Shiny's <b>own</b> modal <see cref="CameraView"/> page, picking from the gallery, and — through
/// the <c>Scan…</c> extensions in the analyzer packages — barcodes, OCR text, credit cards, driver's
/// licenses, passports and faces.
/// </summary>
/// <remarks>
/// <para>
/// The point of difference against MAUI's <c>IMediaPicker</c> is the modal: it is a page you own, drawn with
/// <see cref="CameraView"/>, so it can carry a scan reticle, a live effect strip, bounding boxes, your
/// branding and your instructions. The system camera UI can do none of that, which is why apps that need any
/// of it end up hand-rolling a camera page — that page is what this service is.
/// </para>
/// <para>
/// Register with <c>UseShinyCamera()</c> and inject <see cref="IMediaService"/>. Every method that presents
/// UI requests the permissions it needs first and returns <c>null</c> rather than throwing when the user
/// declines or cancels — a cancelled camera is an ordinary outcome, not an exceptional one.
/// </para>
/// <code>
/// var photo = await media.TakePhotoAsync(new PhotoCaptureOptions
/// {
///     Title = "Proof of delivery",
///     CompressionQuality = 80,
///     MaxDimension = 2048,
///     ShowEffectPicker = true
/// });
/// if (photo is not null)
///     await photo.SaveAsync(Path.Combine(FileSystem.AppDataDirectory, "pod.jpg"));
/// </code>
/// </remarks>
public interface IMediaService
{
    /// <summary>
    /// False where there is no camera to present — the bare <c>net10.0</c> head, or before the app has a
    /// window. Gallery picking may still work when this is false.
    /// </summary>
    bool IsCameraSupported { get; }

    /// <summary>The live defaults set at registration. Change them at runtime and later calls pick them up.</summary>
    MediaServiceOptions Options { get; }

    /// <summary>
    /// Ask for camera access (and the microphone when <paramref name="includeMicrophone"/>, which video
    /// recording with audio needs). Returns the <i>weakest</i> status of the ones requested, so
    /// <see cref="MediaPermissionStatus.Granted"/> means every requested permission is granted.
    /// </summary>
    Task<MediaPermissionStatus> RequestCameraPermissionAsync(bool includeMicrophone = false, CancellationToken ct = default);

    /// <summary>
    /// Ask for photo-gallery access. <paramref name="forWrite"/> requests the add-to-library permission
    /// instead of the read one — on iOS these are genuinely separate grants, and asking for read when you
    /// only intend to save is asking for more than you need.
    /// </summary>
    Task<MediaPermissionStatus> RequestGalleryPermissionAsync(bool forWrite = false, CancellationToken ct = default);

    /// <summary>Open this app's OS settings page — the reliable way back from <see cref="MediaPermissionStatus.Denied"/>.</summary>
    Task OpenSettingsAsync();

    /// <summary>The cameras on this device. Feed an <see cref="CameraInfo.Id"/> to <see cref="MediaCameraOptions.CameraId"/>.</summary>
    Task<IReadOnlyList<CameraInfo>> GetAvailableCamerasAsync(CancellationToken ct = default);

    /// <summary>
    /// Present the modal camera and take one photo, re-encoded to the requested format, compression rate and
    /// maximum dimension. Returns <c>null</c> when the user cancels or permission is refused.
    /// </summary>
    Task<MediaPhoto?> TakePhotoAsync(PhotoCaptureOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Present the modal camera and record one video. Returns <c>null</c> when the user cancels or
    /// permission is refused. Requests the microphone too unless
    /// <see cref="VideoCaptureOptions.IncludeAudio"/> is off.
    /// </summary>
    Task<MediaVideo?> RecordVideoAsync(VideoCaptureOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Pick one photo from the gallery with the system picker, re-encoded to the requested format,
    /// compression rate and maximum dimension. Returns <c>null</c> when cancelled or refused.
    /// </summary>
    Task<MediaPhoto?> PickPhotoAsync(MediaPickOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Pick up to <paramref name="maxCount"/> photos. Where the platform picker is single-select this loops,
    /// re-presenting it until the user cancels or the cap is reached; the list is returned either way.
    /// </summary>
    Task<IReadOnlyList<MediaPhoto>> PickPhotosAsync(int maxCount = 10, MediaPickOptions? options = null, CancellationToken ct = default);

    /// <summary>Pick one video from the gallery. Returns <c>null</c> when cancelled or refused.</summary>
    Task<MediaVideo?> PickVideoAsync(MediaPickOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Present the modal camera running <paramref name="request"/>'s analyzer and stream its results.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The modal opens when enumeration <i>starts</i> and closes when it ends — including when the caller
    /// <c>break</c>s out of the <c>await foreach</c>, which is exactly how the single-result overloads
    /// (<c>ScanBarcodeAsync</c> and friends) are built. It also ends when the user dismisses the modal, when
    /// <see cref="MediaScanOptions.MaxResults"/> or <see cref="MediaScanOptions.Timeout"/> is reached, or on
    /// cancellation.
    /// </para>
    /// <para>
    /// Prefer the typed extensions in the analyzer packages; reach for this directly only for an analyzer
    /// Shiny does not ship one for.
    /// </para>
    /// </remarks>
    IAsyncEnumerable<T> ScanAsync<T>(MediaScanRequest<T> request, MediaScanOptions? options = null, CancellationToken ct = default);
}
