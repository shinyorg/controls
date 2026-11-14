using System.Text.Json;
using System.Windows.Input;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;

namespace Shiny.Maui.Controls.Analysis.Ai;

/// <summary>
/// A <see cref="CameraView"/> analyzer that detects when a document is <i>present</i> in the frame (cheaply,
/// natively — no OCR), and only then ships that one frame to a Microsoft.Extensions.AI
/// <see cref="IChatClient"/> to extract the data. The cheap presence gate runs every frame and draws a live
/// outline; the (slow, paid) model call fires at most once per document — when one is steadily in view and the
/// analyzer is armed (<see cref="CameraView.Scan"/>) — so you're not running an LLM on every preview frame.
/// </summary>
/// <remarks>
/// The model call runs off the analysis thread, so the preview and overlay never stall while it's in flight.
/// The frame buffer is only valid during <c>AnalyzeAsync</c>, so the JPEG is encoded synchronously the moment
/// a document is confirmed and handed to the background call as bytes. Parsing uses MEAI structured output, so
/// <typeparamref name="TDocument"/> comes back strongly typed; for AOT/trim builds supply
/// <see cref="SerializerOptions"/> built from a <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>
/// for your type (the built-in <see cref="AiDocument"/> already does this — see <see cref="AiDocumentAnalyzer"/>).
/// </remarks>
/// <typeparam name="TDocument">The strongly-typed payload the model fills in (e.g. <c>Invoice</c>, or the built-in <see cref="AiDocument"/>).</typeparam>
public class AiDocumentAnalyzer<TDocument> : FrameAnalyzer
{
    readonly IChatClient chatClient;
    readonly DocumentImageExtractor extractor = new();

    /// <param name="chatClient">The MEAI chat client the frame is sent to. Must support image input (a vision model).</param>
    public AiDocumentAnalyzer(IChatClient chatClient)
        => this.chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));

    /// <inheritdoc/>
    public override string Id => "shiny.camera.ai.document";

    /// <summary>
    /// The instruction sent with the image. Keep it about <i>what</i> to extract; the JSON shape is supplied
    /// automatically by structured output. Default asks for a faithful, structured transcription.
    /// </summary>
    public string Prompt { get; set; } =
        "You are a document scanner. Extract the data from this document image accurately and completely. " +
        "Transcribe values exactly as printed; do not invent fields that aren't present.";

    /// <summary>Optional MEAI <see cref="ChatOptions"/> (model id, temperature, …) passed to the chat client.</summary>
    public ChatOptions? Options { get; set; }

    /// <summary>
    /// <see cref="JsonSerializerOptions"/> used to build the structured-output schema and deserialize the
    /// result. Supply context-backed options for AOT/trim safety; when null the MEAI reflection defaults are
    /// used (fine for JIT, not trim-safe). The built-in <see cref="AiDocumentAnalyzer"/> sets this for you.
    /// </summary>
    public JsonSerializerOptions? SerializerOptions { get; set; }

    /// <summary>
    /// How many consecutive frames a document must stay in view before it's shipped to the model — a debounce
    /// that avoids spending a call on a blurry, half-in-frame, or moving document. Default 3.
    /// </summary>
    public int StabilityFrames { get; set; } = 3;

    /// <summary>
    /// How many consecutive frames with no detected document clear the in-progress state, so presenting a
    /// different document re-arms a fresh scan. Default 5.
    /// </summary>
    public int ResetAfterEmptyFrames { get; set; } = 5;

    /// <summary>
    /// Fraction of the detected document's size added as margin on every side before cropping the frame for the
    /// model, so edges/corners aren't clipped. Default 0.04 (4%). Set 0 for a tight crop, or use
    /// <see cref="SendWholeFrame"/> to skip cropping entirely.
    /// </summary>
    public float CropPadding { get; set; } = 0.04f;

    /// <summary>
    /// When <c>true</c>, send the whole frame to the model instead of cropping to the detected document. The
    /// presence detection is still used as the trigger (and for the overlay); only the crop is skipped. Default
    /// <c>false</c>.
    /// </summary>
    public bool SendWholeFrame { get; set; }

    /// <summary>Outline color drawn around a detected document. Default a teal accent.</summary>
    public Color BoxColor { get; set; } = Color.FromArgb("#14B8A6");

    /// <summary>
    /// Optional selector deciding the boxes to draw for the live document outline; return <c>null</c> for no
    /// overlay. When unset the analyzer draws the detected document's bounding box.
    /// </summary>
    public Func<DocumentQuad, IReadOnlyList<OverlayBox>?>? OverlayProvider { get; set; }

    /// <summary>Command invoked (with <see cref="DocumentDetectedEventArgs{TDocument}"/>) when the model returns a parsed document.</summary>
    public static readonly BindableProperty DocumentDetectedCommandProperty = BindableProperty.Create(
        nameof(DocumentDetectedCommand), typeof(ICommand), typeof(AiDocumentAnalyzer<TDocument>));

    /// <inheritdoc cref="DocumentDetectedCommandProperty"/>
    public ICommand? DocumentDetectedCommand
    {
        get => (ICommand?)this.GetValue(DocumentDetectedCommandProperty);
        set => this.SetValue(DocumentDetectedCommandProperty, value);
    }

    /// <summary>
    /// Continuation invoked (on the UI thread) with the parsed document while the analyzer is armed; return
    /// <c>true</c> to keep scanning (the next <i>different</i> document is shipped), <c>false</c> to stop until
    /// the next <see cref="CameraView.Scan"/>. When unset, delivery is single-shot. Bindable so it can target a VM method.
    /// </summary>
    public static readonly BindableProperty OnDetectedProperty = BindableProperty.Create(
        nameof(OnDetected), typeof(Func<DocumentDetectedEventArgs<TDocument>, Task<bool>>), typeof(AiDocumentAnalyzer<TDocument>));

    /// <inheritdoc cref="OnDetectedProperty"/>
    public Func<DocumentDetectedEventArgs<TDocument>, Task<bool>>? OnDetected
    {
        get => (Func<DocumentDetectedEventArgs<TDocument>, Task<bool>>?)this.GetValue(OnDetectedProperty);
        set => this.SetValue(OnDetectedProperty, value);
    }

    /// <summary>Raised on the UI thread when the model returns a parsed document (while the analyzer is armed).</summary>
    public event EventHandler<DocumentDetectedEventArgs<TDocument>>? DocumentDetected;

    /// <summary>Raised on the UI thread when the model call fails (network, auth, deserialization). The pipeline keeps running.</summary>
    public event EventHandler<Exception>? Error;

    int stableFrames;
    int emptyFrames;
    bool emitted;
    int inFlight;
    IReadOnlyList<OverlayBox>? lastOverlay;

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeAsync(VideoFrame frame, CancellationToken ct)
    {
        var quad = this.extractor.Detect(frame);
        if (quad is null)
        {
            // tolerate brief drop-outs while a document is being positioned; reset once it's clearly gone
            if (++this.emptyFrames >= Math.Max(1, this.ResetAfterEmptyFrames))
                this.Reset();
            return new ValueTask<IReadOnlyList<OverlayBox>?>(this.lastOverlay);
        }

        this.emptyFrames = 0;
        this.stableFrames++;

        // Ship to the model only when: armed, not already shipped for this document, no call in flight, and the
        // document has been steadily in view. Encoding happens synchronously here (the frame is about to be
        // disposed); the model call itself runs in the background so the pipeline isn't blocked.
        if (this.IsArmed &&
            !this.emitted &&
            Interlocked.CompareExchange(ref this.inFlight, 1, 0) == 0)
        {
            if (this.stableFrames >= Math.Max(1, this.StabilityFrames))
            {
                var crop = this.SendWholeFrame ? new RectF(0, 0, 1, 1) : Pad(quad.Bounds, this.CropPadding);
                var jpeg = this.extractor.Encode(frame, crop);
                if (jpeg is { Length: > 0 })
                    _ = this.ShipAsync(jpeg, ct);
                else
                    Interlocked.Exchange(ref this.inFlight, 0); // encode unsupported (bare net10.0) — stay armed, no-op
            }
            else
            {
                Interlocked.Exchange(ref this.inFlight, 0); // not stable yet; try again next frame
            }
        }

        this.lastOverlay = this.ResolveOverlay(quad, this.OverlayProvider,
            () => [new OverlayBox(quad.Bounds, this.BoxColor, null, this.BoxColor)]);
        return new ValueTask<IReadOnlyList<OverlayBox>?>(this.lastOverlay);
    }

    async Task ShipAsync(byte[] jpeg, CancellationToken ct)
    {
        try
        {
            var message = new ChatMessage(ChatRole.User,
            [
                new TextContent(this.Prompt),
                new DataContent(jpeg, "image/jpeg")
            ]);

            var response = this.SerializerOptions is { } opts
                ? await this.chatClient.GetResponseAsync<TDocument>([message], opts, this.Options, cancellationToken: ct).ConfigureAwait(false)
                : await this.chatClient.GetResponseAsync<TDocument>([message], this.Options, cancellationToken: ct).ConfigureAwait(false);

            if (response.TryGetResult(out var doc) && doc is not null)
            {
                this.emitted = true;
                var args = new DocumentDetectedEventArgs<TDocument>(doc);
                this.Deliver(args, () => this.DocumentDetected?.Invoke(this, args), this.DocumentDetectedCommand, this.OnDetected);
            }
        }
        catch (Exception ex)
        {
            this.Raise(() => this.Error?.Invoke(this, ex));
        }
        finally
        {
            Interlocked.Exchange(ref this.inFlight, 0);
        }
    }

    void Reset()
    {
        this.stableFrames = 0;
        this.emptyFrames = 0;
        this.emitted = false;
        this.lastOverlay = null;
    }

    // pad a normalized rect by a fraction of its own size on every side, clamped to the unit square
    static RectF Pad(RectF r, float pad)
    {
        if (pad <= 0)
            return r;
        var dx = r.Width * pad;
        var dy = r.Height * pad;
        var x = Math.Max(0f, r.X - dx);
        var y = Math.Max(0f, r.Y - dy);
        var right = Math.Min(1f, r.X + r.Width + dx);
        var bottom = Math.Min(1f, r.Y + r.Height + dy);
        return new RectF(x, y, right - x, bottom - y);
    }
}
