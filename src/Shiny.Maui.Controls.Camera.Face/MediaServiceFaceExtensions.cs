using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Camera.Media;

namespace Shiny.Maui.Controls.Camera.Face;

/// <summary>
/// Face detection off <see cref="IMediaService"/> — the modal camera opened with a
/// <see cref="FaceAnalyzer"/> already wired up. Install <c>Shiny.Maui.Controls.Camera.Face</c> and these
/// appear.
/// </summary>
/// <remarks>
/// Detection, not recognition: these tell you a face is there and where it is, never who it belongs to.
/// The usual use is a liveness/framing gate before a capture — "wait until a face is in frame, then take
/// the photo".
/// </remarks>
public static class MediaServiceFaceExtensions
{
    /// <summary>
    /// Open the camera and return the faces in the first frame that has any, or <c>null</c> if the user
    /// backs out. Defaults to the front lens, which is what a "point it at yourself" flow needs.
    /// </summary>
    public static Task<IReadOnlyList<DetectedFace>?> DetectFaceAsync(
        this IMediaService media,
        MediaScanOptions? options = null,
        CancellationToken ct = default
    ) => media.DetectFacesAsync(false, options, ct).FirstOrDefaultAsync(ct).AsTask();

    /// <summary>
    /// Open the camera and stream each frame's faces until the user finishes. Every result is the full set
    /// of faces for one frame.
    /// </summary>
    /// <param name="media">The media service.</param>
    /// <param name="filterDuplicates">
    /// Skip a frame whose face count is unchanged from the last one returned. Default <c>false</c> here,
    /// unlike the other scanners: faces move, and a caller streaming them almost always wants the movement.
    /// </param>
    /// <param name="options">Modal appearance and scan behaviour. Defaults <see cref="MediaCameraOptions.Facing"/> to the front lens.</param>
    /// <param name="ct">Cancels the scan and closes the modal.</param>
    public static IAsyncEnumerable<IReadOnlyList<DetectedFace>> DetectFacesAsync(
        this IMediaService media,
        bool filterDuplicates = false,
        MediaScanOptions? options = null,
        CancellationToken ct = default
    )
    {
        var analyzer = new FaceAnalyzer();
        var resolved = options.WithDuplicateFilter(filterDuplicates);
        if (options is null)
            resolved.Facing = CameraFacing.Front;

        return media.ScanAsync(
            new MediaScanRequest<IReadOnlyList<DetectedFace>>
            {
                Analyzer = analyzer,
                Subscribe = emit => analyzer.OnDetected = args =>
                {
                    emit(args.Faces);
                    return Task.FromResult(true);
                },
                DuplicateKey = faces => faces.Count.ToString(),
                Describe = faces => faces.Count == 1 ? "1 face" : $"{faces.Count} faces"
            },
            resolved,
            ct
        );
    }
}
