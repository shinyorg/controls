using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Camera.Media;

namespace Shiny.Maui.Controls.Camera.Barcode;

/// <summary>
/// Barcode scanning off <see cref="IMediaService"/> — the modal camera opened with a
/// <see cref="BarcodeAnalyzer"/> already wired up, so a scan is one line and no page of your own.
/// </summary>
/// <remarks>
/// These live in the barcode package rather than on <see cref="IMediaService"/> itself because the service
/// deliberately knows nothing about symbologies; it exposes <see cref="IMediaService.ScanAsync{T}"/> and
/// each analyzer package hangs its own typed verb off it. Install
/// <c>Shiny.Maui.Controls.Camera.Barcode</c> and these appear.
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
    /// <param name="ct">Cancels the scan and closes the modal.</param>
    public static Task<DetectedBarcode?> ScanBarcodeAsync(
        this IMediaService media,
        IEnumerable<BarcodeFormat>? formats = null,
        MediaScanOptions? options = null,
        CancellationToken ct = default
    ) => media.ScanBarcodesAsync(false, formats, options, ct).FirstOrDefaultAsync(ct).AsTask();

    /// <summary>
    /// Open the scanner and stream every code read until the user finishes (the ✓ button), the caller stops
    /// enumerating, or <see cref="MediaScanOptions.MaxResults"/>/<see cref="MediaScanOptions.Timeout"/> is
    /// hit. The modal stays up throughout, showing a running count.
    /// </summary>
    /// <param name="media">The media service.</param>
    /// <param name="filterDuplicates">
    /// Skip a code already returned in this session, keyed on symbology + value. Default <c>true</c>: a code
    /// sitting in front of the lens is otherwise re-read every time it drifts out of view and back.
    /// Overrides <see cref="MediaScanOptions.FilterDuplicates"/> when both are supplied.
    /// </param>
    /// <param name="formats">Restrict to specific symbologies. Null (default) reads everything the native scanner supports.</param>
    /// <param name="options">Modal appearance and scan behaviour. Null uses the defaults.</param>
    /// <param name="ct">Cancels the scan and closes the modal.</param>
    /// <example>
    /// <code>
    /// await foreach (var code in media.ScanBarcodesAsync())
    ///     this.Items.Add(code.Value);
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
        var analyzer = new BarcodeAnalyzer();
        if (formats is not null)
            analyzer.Formats = formats.ToList();

        return media.ScanAsync(
            new MediaScanRequest<DetectedBarcode>
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
