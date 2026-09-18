namespace Shiny.Blazor.Controls.Camera.Media;

/// <summary>
/// One scanning session described to <see cref="IMediaService.ScanAsync{T}"/>: which analyzer to run, how to
/// pull results out of the camera, and how to tell two results apart.
/// </summary>
/// <remarks>
/// <para>
/// This exists so the service knows nothing about barcodes or documents. Each <c>Scan…</c> extension builds
/// one of these, and the modal plumbing — presentation, duplicate filtering, limits, idle timeout,
/// cancellation, teardown — is written once in the service.
/// </para>
/// <para>
/// Differs from the MAUI request in one respect: browser analyzers are <i>armed</i> per request
/// (<see cref="CameraView.RequestBarcodesAsync"/>, <see cref="CameraView.RequestDocumentImageAsync"/>) rather
/// than pushing through a callback, so results are pulled with <see cref="Next"/>.
/// </para>
/// </remarks>
/// <typeparam name="T">The result type yielded to the caller.</typeparam>
public sealed class MediaScanRequest<T>
{
    /// <summary>The analyzer assigned to the modal's camera. Its scan window and box toggle come from the <see cref="MediaScanOptions"/>.</summary>
    public required CameraAnalyzer Analyzer { get; init; }

    /// <summary>
    /// Wait for the next batch of results from the started camera. Called in a loop; honour the token (it fires
    /// when the session ends). Returning an empty list simply asks again.
    /// </summary>
    public required Func<MediaScanContext, CancellationToken, Task<IReadOnlyList<T>>> Next { get; init; }

    /// <summary>The identity used by <see cref="MediaScanOptions.FilterDuplicates"/>. Null means every result is distinct.</summary>
    public Func<T, string>? DuplicateKey { get; init; }

    /// <summary>A short caption for the result, shown in the modal when <see cref="MediaScanOptions.ShowResultCount"/> is on.</summary>
    public Func<T, string>? Describe { get; init; }
}


/// <summary>What <see cref="MediaScanRequest{T}.Next"/> works against: the modal's camera, and its status line.</summary>
public sealed class MediaScanContext
{
    readonly MediaSession session;
    readonly Action changed;

    internal MediaScanContext(MediaSession session, CameraView camera, Action changed)
    {
        this.session = session;
        this.Camera = camera;
        this.changed = changed;
    }

    /// <summary>The started camera, with the request's analyzer assigned.</summary>
    public CameraView Camera { get; }

    /// <summary>
    /// Show a line over the preview — "Reading document…" while a slow extraction runs, so the user knows to hold
    /// still. Null clears it. The service clears it before every pull.
    /// </summary>
    public void SetStatus(string? text)
    {
        if (this.session.Status == text)
            return;

        this.session.Status = text;
        this.changed();
    }
}


/// <summary>Helpers for shaping a scan session.</summary>
/// <remarks>
/// To collapse a session to one result use .NET 10's <c>System.Linq.AsyncEnumerable.FirstOrDefaultAsync</c> —
/// it disposes the enumerator, which is what closes the modal. Collecting a whole session is <c>ToListAsync</c>.
/// </remarks>
public static class MediaScanExtensions
{
    /// <summary>
    /// Return <paramref name="options"/> (or a fresh default) with <see cref="MediaScanOptions.FilterDuplicates"/>
    /// set — so the typed overloads' <c>filterDuplicates</c> argument <b>wins</b> over the options object.
    /// </summary>
    public static MediaScanOptions WithDuplicateFilter(this MediaScanOptions? options, bool filterDuplicates)
    {
        var resolved = options ?? new MediaScanOptions();
        resolved.FilterDuplicates = filterDuplicates;
        return resolved;
    }
}
