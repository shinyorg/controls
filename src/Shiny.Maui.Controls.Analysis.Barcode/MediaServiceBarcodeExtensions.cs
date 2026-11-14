
namespace Shiny.Maui.Controls.Analysis.Barcode;

/// <summary>
/// Barcode scanning off <c>IMediaService</c> — the modal camera opened with a
/// <see cref="BarcodeAnalyzer"/> already wired up, so a scan is one line and no page of your own.
/// </summary>
/// <remarks>
/// These live in the barcode package rather than on <c>IMediaService</c> itself because the service
/// deliberately knows nothing about symbologies; it exposes <c>IMediaService.ScanAsync</c> and
/// each analyzer package hangs its own typed verb off it. Install
/// <c>Shiny.Maui.Controls.Analysis.Barcode</c> and these appear.
/// </remarks>
public static class MediaServiceBarcodeExtensions
{
    /// <summary>
    /// Open the scanner and return the first code read, or <c>null</c> if the user backs out. The modal
    /// closes itself the moment a code is decoded.
    /// </summary>
    /// <param name="media">The media service.</param>
    /// <param name="formats">Restrict to specific symbologies. Null (default) reads everything the native scanner supports.</param>
    /// <param name="options">Modal appearance and scan behaviour. Null uses the defaults.</param>
    /// <param name="mode">The analyzer's <see cref="BarcodeAnalyzer.Mode"/> — <see cref="BarcodeScanMode.LongRange"/> for codes across a room.</param>
    /// <param name="autoZoom">The analyzer's <see cref="BarcodeAnalyzer.AutoZoom"/> (long-range mode only).</param>
    /// <param name="ct">Cancels the scan and closes the modal.</param>
    public static Task<DetectedBarcode?> ScanBarcodeAsync<TOptions>(
        this IFrameScanService<TOptions> media,
        IEnumerable<BarcodeFormat>? formats = null,
        TOptions? options = null,
        BarcodeScanMode mode = BarcodeScanMode.Standard,
        bool autoZoom = false,
        CancellationToken ct = default
    ) where TOptions : class, IScanOptions, new() => media.ScanBarcodesAsync(false, formats, options, mode, autoZoom, ct).FirstOrDefaultAsync(ct).AsTask();

    /// <summary>
    /// Open the scanner and stream every code read until the user finishes (the ✓ button), the caller stops
    /// enumerating, or <c>MediaScanOptions.MaxResults</c>/<c>MediaScanOptions.Timeout</c> is
    /// hit. The modal stays up throughout, showing a running count.
    /// </summary>
    /// <param name="media">The media service.</param>
    /// <param name="filterDuplicates">
    /// Skip a code already returned in this session, keyed on symbology + value. Default <c>true</c>: a code
    /// sitting in front of the lens is otherwise re-read every time it drifts out of view and back.
    /// Overrides <c>MediaScanOptions.FilterDuplicates</c> when both are supplied.
    /// </param>
    /// <param name="formats">Restrict to specific symbologies. Null (default) reads everything the native scanner supports.</param>
    /// <param name="options">Modal appearance and scan behaviour. Null uses the defaults.</param>
    /// <param name="mode">The analyzer's <see cref="BarcodeAnalyzer.Mode"/> — <see cref="BarcodeScanMode.LongRange"/> for codes across a room.</param>
    /// <param name="autoZoom">The analyzer's <see cref="BarcodeAnalyzer.AutoZoom"/> (long-range mode only).</param>
    /// <param name="ct">Cancels the scan and closes the modal.</param>
    /// <example>
    /// <code>
    /// await foreach (var code in media.ScanBarcodesAsync())
    ///     this.Items.Add(code.Value);
    /// </code>
    /// </example>
    public static IAsyncEnumerable<DetectedBarcode> ScanBarcodesAsync<TOptions>(
        this IFrameScanService<TOptions> media,
        bool filterDuplicates = true,
        IEnumerable<BarcodeFormat>? formats = null,
        TOptions? options = null,
        BarcodeScanMode mode = BarcodeScanMode.Standard,
        bool autoZoom = false,
        CancellationToken ct = default
    )
        where TOptions : class, IScanOptions, new()
    {
        var analyzer = new BarcodeAnalyzer
        {
            Mode = mode,
            AutoZoom = autoZoom
        };
        if (formats is not null)
            analyzer.Formats = formats.ToList();

        return media.ScanAsync(
            new FrameScanRequest<DetectedBarcode>
            {
                Analyzer = analyzer,
                Subscribe = emit => analyzer.OnDetected = args =>
                {
                    // a single frame can hold several codes; each is its own result
                    foreach (var barcode in args.Barcodes)
                        emit(barcode);

                    return Task.FromResult(true); // stay armed — the service decides when to stop
                },
                // symbology is part of the identity: the same digits as an EAN-13 and as a QR code are two
                // different things to scan
                DuplicateKey = b => $"{b.Format}|{b.Value}",
                Describe = b => b.Value
            },
            options.WithDuplicateFilter(filterDuplicates),
            ct
        );
    }
}
