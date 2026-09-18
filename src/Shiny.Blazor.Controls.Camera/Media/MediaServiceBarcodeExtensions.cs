using Shiny.Controls.Camera;

namespace Shiny.Blazor.Controls.Camera.Media;

/// <summary>
/// Barcode scanning off <see cref="IMediaService"/> — the modal camera opened with a
/// <see cref="BarcodeAnalyzer"/>, so a scan is one line and no page of your own. The same verbs, arguments and
/// result type (<see cref="DetectedBarcode"/>) as the MAUI <c>Shiny.Maui.Controls.Camera.Barcode</c> extensions.
/// </summary>
/// <remarks>
/// Decoding uses the browser's native <c>BarcodeDetector</c> — Chromium (Chrome, Edge, Android) today. Elsewhere
/// the modal opens and says so; check <see cref="IsBarcodeScanningSupportedAsync"/> first to hide the button instead.
/// </remarks>
public static class MediaServiceBarcodeExtensions
{
    /// <summary>Whether this browser can decode barcodes (it has the native <c>BarcodeDetector</c>).</summary>
    public static Task<bool> IsBarcodeScanningSupportedAsync(this IMediaService media)
        => media is MediaService service ? service.IsBarcodeDetectorAvailableAsync() : Task.FromResult(false);


    /// <summary>
    /// Open the scanner and return the first code read, or <c>null</c> if the user backs out. The modal closes
    /// itself the moment a code is decoded.
    /// </summary>
    /// <param name="media">The media service.</param>
    /// <param name="formats">Restrict to specific symbologies. Null (default) reads everything the browser supports.</param>
    /// <param name="options">Modal appearance and scan behaviour. Null uses the defaults.</param>
    /// <param name="ct">Cancels the scan and closes the modal.</param>
    public static Task<DetectedBarcode?> ScanBarcodeAsync(
        this IMediaService media,
        IEnumerable<BarcodeFormat>? formats = null,
        MediaScanOptions? options = null,
        CancellationToken ct = default
    ) => media.ScanBarcodesAsync(false, formats, options, ct).FirstOrDefaultAsync(ct).AsTask();


    /// <summary>
    /// Open the scanner and stream every code read until the user finishes (✓), the caller stops enumerating, or
    /// <see cref="MediaScanOptions.MaxResults"/>/<see cref="MediaScanOptions.Timeout"/> is hit.
    /// </summary>
    /// <param name="media">The media service.</param>
    /// <param name="filterDuplicates">
    /// Skip a code already returned in this session, keyed on symbology + value. Default <c>true</c>. Overrides
    /// <see cref="MediaScanOptions.FilterDuplicates"/> when both are supplied.
    /// </param>
    /// <param name="formats">Restrict to specific symbologies. Null (default) reads everything the browser supports.</param>
    /// <param name="options">Modal appearance and scan behaviour. Null uses the defaults.</param>
    /// <param name="ct">Cancels the scan and closes the modal.</param>
    /// <example>
    /// <code>
    /// await foreach (var code in media.ScanBarcodesAsync())
    ///     items.Add(code.Value);
    /// </code>
    /// </example>
    public static IAsyncEnumerable<DetectedBarcode> ScanBarcodesAsync(
        this IMediaService media,
        bool filterDuplicates = true,
        IEnumerable<BarcodeFormat>? formats = null,
        MediaScanOptions? options = null,
        CancellationToken ct = default
    )
    {
        var allowed = formats?.ToHashSet();

        return media.ScanAsync(
            new MediaScanRequest<DetectedBarcode>
            {
                Analyzer = new BarcodeAnalyzer(),
                Next = async (ctx, token) =>
                {
                    // a single frame can hold several codes; each is its own result
                    var codes = await ctx.Camera.RequestBarcodesAsync(token);
                    return codes
                        .Select(c => c.ToDetectedBarcode())
                        .Where(b => allowed is null || allowed.Contains(b.Format))
                        .ToList();
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
