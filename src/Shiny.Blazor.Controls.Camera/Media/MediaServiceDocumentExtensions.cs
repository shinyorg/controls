namespace Shiny.Blazor.Controls.Camera.Media;

/// <summary>
/// Document capture off <see cref="IMediaService"/>: the modal camera runs the in-browser
/// <see cref="DocumentAnalyzer"/> (a cheap presence check — no OCR) and hands back the cropped image of the next
/// document held steady in view. Send that to a vision model, an OCR service, or just store it; the
/// <c>Shiny.Blazor.Controls.Camera.Ai</c> package adds <c>ScanDocumentAsync</c> overloads that do the model call.
/// </summary>
public static class MediaServiceDocumentExtensions
{
    /// <summary>
    /// Open the camera and return the cropped JPEG of the first document held steady in view, or <c>null</c> if
    /// the user backs out.
    /// </summary>
    public static Task<CameraDocumentImage?> ScanDocumentImageAsync(
        this IMediaService media,
        MediaScanOptions? options = null,
        CancellationToken ct = default
    ) => media.ScanDocumentImagesAsync(options, ct).FirstOrDefaultAsync(ct).AsTask();


    /// <summary>
    /// Open the camera and stream a cropped JPEG for each document held steady in view — a stack of pages, one
    /// after another — until the user finishes (✓) or the caller stops.
    /// </summary>
    /// <remarks>
    /// Documents have no stable identity to de-duplicate on, so instead each capture waits for the previous page
    /// to leave the frame and a new one to settle in view.
    /// </remarks>
    public static IAsyncEnumerable<CameraDocumentImage> ScanDocumentImagesAsync(
        this IMediaService media,
        MediaScanOptions? options = null,
        CancellationToken ct = default
    ) => media.ScanAsync(
        new MediaScanRequest<CameraDocumentImage>
        {
            Analyzer = new DocumentAnalyzer(),
            // requireNewDocument: the page just captured has to leave the frame before the next one counts
            Next = async (ctx, token) => [await ctx.Camera.RequestDocumentImageAsync(true, token)],
            Describe = _ => "Page captured"
        },
        options,
        ct
    );
}
