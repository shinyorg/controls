using Microsoft.Extensions.AI;
using Shiny.Blazor.Controls.Camera.Media;
using Shiny.Controls.Camera;

namespace Shiny.Blazor.Controls.Camera.Ai;

/// <summary>
/// AI document scanning off <see cref="IMediaService"/>: the modal camera waits (cheaply, in-browser) for a
/// document to be held steady, then sends that one cropped frame to a Microsoft.Extensions.AI
/// <see cref="IChatClient"/> and returns what the model extracted. The model runs once per document — never per
/// frame.
/// </summary>
/// <remarks>
/// The browser has no on-device OCR, so this is how the Blazor service covers what the MAUI analyzer packages do
/// natively (credit cards, licenses, receipts, …): define a record for the document, hand it to an
/// <see cref="AiDocumentScanner{TDocument}"/>, and scan. The chat client must accept image input (a vision model).
/// </remarks>
public static class MediaServiceAiExtensions
{
    /// <summary>Shown over the preview while the model reads the captured page.</summary>
    public const string DefaultWorkingText = "Reading document…";


    /// <summary>
    /// Scan one document and return the schema-free <see cref="AiDocument"/> (type, summary, label/value fields),
    /// or <c>null</c> if the user backs out or the model could not produce one.
    /// </summary>
    /// <param name="media">The media service.</param>
    /// <param name="chatClient">A vision-capable chat client.</param>
    /// <param name="options">Modal appearance and scan behaviour. Null uses the defaults.</param>
    /// <param name="ct">Cancels the scan and any model call in flight.</param>
    public static Task<AiDocument?> ScanDocumentAsync(
        this IMediaService media,
        IChatClient chatClient,
        MediaScanOptions? options = null,
        CancellationToken ct = default
    ) => media.ScanDocumentAsync(new AiDocumentScanner(chatClient), options, ct);


    /// <summary>
    /// Scan one document into your own strongly-typed <typeparamref name="TDocument"/> — the scanner carries the
    /// prompt, chat options and (for trimmed WASM) the source-generated serializer options.
    /// </summary>
    /// <example>
    /// <code>
    /// var card = await media.ScanDocumentAsync(new AiDocumentScanner&lt;BusinessCard&gt;(chat)
    /// {
    ///     SerializerOptions = new(AppJsonContext.Default.Options)
    /// });
    /// </code>
    /// </example>
    public static Task<TDocument?> ScanDocumentAsync<TDocument>(
        this IMediaService media,
        AiDocumentScanner<TDocument> scanner,
        MediaScanOptions? options = null,
        CancellationToken ct = default
    ) => media.ScanDocumentsAsync(scanner, options, DefaultWorkingText, ct).FirstOrDefaultAsync(ct).AsTask();


    /// <summary>
    /// Stream a <typeparamref name="TDocument"/> for each document held up in turn — a stack of receipts, say —
    /// until the user finishes (✓) or the caller stops. Each page has to leave the frame before the next counts.
    /// </summary>
    /// <param name="media">The media service.</param>
    /// <param name="scanner">The configured AI scanner.</param>
    /// <param name="options">Modal appearance and scan behaviour. Null uses the defaults.</param>
    /// <param name="workingText">Shown over the preview while the model runs. Null shows nothing.</param>
    /// <param name="ct">Cancels the scan and any model call in flight.</param>
    public static IAsyncEnumerable<TDocument> ScanDocumentsAsync<TDocument>(
        this IMediaService media,
        AiDocumentScanner<TDocument> scanner,
        MediaScanOptions? options = null,
        string? workingText = DefaultWorkingText,
        CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(scanner);

        return media.ScanAsync(
            new MediaScanRequest<TDocument>
            {
                Analyzer = new DocumentAnalyzer(),
                Next = async (ctx, token) =>
                {
                    var image = await ctx.Camera.RequestDocumentImageAsync(true, token);

                    ctx.SetStatus(workingText);
                    var document = await scanner.ExtractAsync(image.Jpeg, token);
                    ctx.SetStatus(null);

                    // a page the model could not read is not a result — ask for the next one
                    return document is null ? [] : [document];
                },
                Describe = d => d is AiDocument ai ? ai.DocumentType ?? "Document" : "Document"
            },
            options,
            ct
        );
    }
}
