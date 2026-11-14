namespace Shiny.Maui.Controls.Analysis;

/// <summary>
/// One scanning session described to <see cref="IFrameScanService{TOptions}.ScanAsync{T}"/> (the camera's
/// <c>IMediaService</c> implements it): which analyzer to run, how to get results out of it, and how to tell two
/// results apart.
/// </summary>
/// <remarks>
/// <para>
/// This exists so the service knows nothing about barcodes, OCR or credit cards. Every <c>Scan…</c>
/// extension lives in the package that owns its analyzer, builds one of these, and the modal-camera
/// plumbing — permissions, presentation, arming, duplicate filtering, cancellation, teardown — is written
/// once in the service rather than once per result type. The analyzer packages see only
/// <see cref="IFrameScanService{TOptions}"/>, which is what lets them stay free of the camera package.
/// </para>
/// <para>
/// You only build this directly to scan with an analyzer Shiny does not ship an extension for; otherwise
/// call <c>ScanBarcodesAsync</c>, <c>ScanCreditCardAsync</c> and friends.
/// </para>
/// </remarks>
/// <typeparam name="T">The result type yielded to the caller.</typeparam>
public sealed class FrameScanRequest<T>
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
    /// The identity used by <see cref="IScanOptions.FilterDuplicates"/>. Null means every result is
    /// distinct, so duplicate filtering does nothing.
    /// </summary>
    public Func<T, string>? DuplicateKey { get; init; }

    /// <summary>
    /// A short caption for the result, shown in the modal's running list when
    /// the service shows a result count. Null shows the count only.
    /// </summary>
    public Func<T, string>? Describe { get; init; }
}

/// <summary>
/// Something that can run a <see cref="FrameScanRequest{T}"/> and yield its results — the camera's modal scan sheet
/// (<c>IMediaService</c>, whose options are <c>MediaScanOptions</c>). The <c>Scan…</c> extension methods in the analyzer
/// packages are written against this, so <c>media.ScanBarcodesAsync()</c> works without the barcode package
/// referencing the camera.
/// </summary>
/// <typeparam name="TOptions">The service's own options type.</typeparam>
public interface IFrameScanService<TOptions> where TOptions : class, IScanOptions, new()
{
    /// <summary>Run <paramref name="request"/> until cancelled or the service ends the scan, yielding each result.</summary>
    IAsyncEnumerable<T> ScanAsync<T>(FrameScanRequest<T> request, TOptions? options = null, CancellationToken ct = default);
}

/// <summary>Helpers for <see cref="IScanOptions"/>.</summary>
public static class ScanOptionsExtensions
{
    /// <summary>Returns <paramref name="options"/> (or a new instance) with <see cref="IScanOptions.FilterDuplicates"/> set.</summary>
    public static TOptions WithDuplicateFilter<TOptions>(this TOptions? options, bool filterDuplicates)
        where TOptions : class, IScanOptions, new()
    {
        var resolved = options ?? new TOptions();
        resolved.FilterDuplicates = filterDuplicates;
        return resolved;
    }
}
