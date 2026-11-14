
namespace Shiny.Maui.Controls.Analysis.Ocr;

/// <summary>
/// Text recognition off <c>IMediaService</c> — the modal camera opened with an
/// <see cref="OcrAnalyzer"/> already wired up. Install <c>Shiny.Maui.Controls.Analysis.Ocr</c> and these
/// appear.
/// </summary>
public static class MediaServiceOcrExtensions
{
    /// <summary>
    /// Open the scanner and return everything read in the first frame that produced text — the whole block
    /// set, in reading order, because a page of text is one result and not many. <c>null</c> if the user
    /// backs out.
    /// </summary>
    /// <param name="media">The media service.</param>
    /// <param name="recognition">Language and correction settings. Null uses <see cref="TextRecognitionOptions.Default"/>.</param>
    /// <param name="options">Modal appearance and scan behaviour. Null uses the defaults.</param>
    /// <param name="ct">Cancels the scan and closes the modal.</param>
    public static Task<IReadOnlyList<RecognizedText>?> ScanTextAsync<TOptions>(
        this IFrameScanService<TOptions> media,
        TextRecognitionOptions? recognition = null,
        TOptions? options = null,
        CancellationToken ct = default
    ) where TOptions : class, IScanOptions, new() => media.ScanTextBlocksAsync(false, recognition, options, ct).FirstOrDefaultAsync(ct).AsTask();

    /// <summary>
    /// Open the scanner and return the recognized text of the first frame joined into a single string, one
    /// block per line — the shape most callers actually want ("read that label for me"). <c>null</c> if the
    /// user backs out.
    /// </summary>
    public static async Task<string?> ScanTextStringAsync<TOptions>(
        this IFrameScanService<TOptions> media,
        TextRecognitionOptions? recognition = null,
        TOptions? options = null,
        CancellationToken ct = default
    )
        where TOptions : class, IScanOptions, new()
    {
        var blocks = await media.ScanTextAsync(recognition, options, ct).ConfigureAwait(false);
        return blocks is null ? null : String.Join(Environment.NewLine, blocks.Select(b => b.Text));
    }

    /// <summary>
    /// Open the scanner and stream each frame's recognized text until the user finishes. Every result is the
    /// full block set for one frame.
    /// </summary>
    /// <param name="media">The media service.</param>
    /// <param name="filterDuplicates">
    /// Skip a frame whose text is identical to one already returned. Default <c>true</c> — a camera held on
    /// one sign otherwise yields the same paragraph at frame rate. Overrides
    /// <c>MediaScanOptions.FilterDuplicates</c> when both are supplied.
    /// </param>
    /// <param name="recognition">Language and correction settings. Null uses <see cref="TextRecognitionOptions.Default"/>.</param>
    /// <param name="options">Modal appearance and scan behaviour. Null uses the defaults.</param>
    /// <param name="ct">Cancels the scan and closes the modal.</param>
    public static IAsyncEnumerable<IReadOnlyList<RecognizedText>> ScanTextBlocksAsync<TOptions>(
        this IFrameScanService<TOptions> media,
        bool filterDuplicates = true,
        TextRecognitionOptions? recognition = null,
        TOptions? options = null,
        CancellationToken ct = default
    )
        where TOptions : class, IScanOptions, new()
    {
        // the analyzer builds its own TextRecognitionOptions from its properties (its ScanWindow is the
        // region of interest, and the service sets that from MediaScanOptions.ScanWindow), so the caller's
        // record is unpacked onto it rather than assigned
        var analyzer = new OcrAnalyzer();
        if (recognition is not null)
        {
            analyzer.MinimumTextHeight = recognition.MinimumTextHeight;
            analyzer.MinimumInputHeight = recognition.MinimumInputHeight;
            if (recognition.RegionOfInterest is { } region)
            {
                options ??= new TOptions();
                options.ScanWindow ??= region;
            }
        }

        return media.ScanAsync(
            new FrameScanRequest<IReadOnlyList<RecognizedText>>
            {
                Analyzer = analyzer,
                Subscribe = emit => analyzer.OnDetected = args =>
                {
                    emit(args.Blocks);
                    return Task.FromResult(true);
                },
                DuplicateKey = blocks => String.Join("\n", blocks.Select(b => b.Text)),
                Describe = blocks => blocks.Count > 0 ? blocks[0].Text : String.Empty
            },
            options.WithDuplicateFilter(filterDuplicates),
            ct
        );
    }
}
