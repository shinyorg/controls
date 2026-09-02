namespace Shiny.Maui.Controls.Camera.Media;

/// <summary>
/// One scanning session described to <see cref="IMediaService.ScanAsync{T}"/>: which analyzer to run, how to
/// get results out of it, and how to tell two results apart.
/// </summary>
/// <remarks>
/// <para>
/// This exists so the service knows nothing about barcodes, OCR or credit cards. Every <c>Scan…</c>
/// extension lives in the package that owns its analyzer, builds one of these, and the modal-camera
/// plumbing — permissions, presentation, arming, duplicate filtering, cancellation, teardown — is written
/// once here rather than once per result type.
/// </para>
/// <para>
/// You only build this directly to scan with an analyzer Shiny does not ship an extension for; otherwise
/// call <c>ScanBarcodesAsync</c>, <c>ScanCreditCardAsync</c> and friends.
/// </para>
/// </remarks>
/// <typeparam name="T">The result type yielded to the caller.</typeparam>
public sealed class MediaScanRequest<T>
{
    /// <summary>The analyzer run against every frame. Configure it fully before handing it over.</summary>
    public required FrameAnalyzer Analyzer { get; init; }

    /// <summary>
    /// Wire <see cref="Analyzer"/>'s own typed delivery to the supplied callback. Invoked once, before the
    /// camera starts. Each call to the callback yields one result to the caller's <c>await foreach</c>.
    /// </summary>
    /// <remarks>
    /// Analyzers deliver through their own strongly-typed <c>OnDetected</c> / event, and the service cannot
    /// see those without knowing the analyzer's type — so the extension that <i>does</i> know it does the
    /// wiring. Set <c>OnDetected</c> to return <c>true</c> (keep scanning); the service handles stopping.
    /// </remarks>
    public required Action<Action<T>> Subscribe { get; init; }

    /// <summary>
    /// The identity used by <see cref="MediaScanOptions.FilterDuplicates"/>. Null means every result is
    /// distinct, so duplicate filtering does nothing.
    /// </summary>
    public Func<T, string>? DuplicateKey { get; init; }

    /// <summary>
    /// A short caption for the result, shown in the modal's running list when
    /// <see cref="MediaScanOptions.ShowResultCount"/> is on. Null shows the count only.
    /// </summary>
    public Func<T, string>? Describe { get; init; }
}
