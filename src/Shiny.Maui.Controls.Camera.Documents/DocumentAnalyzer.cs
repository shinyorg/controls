using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Camera.Ocr;

namespace Shiny.Maui.Controls.Camera.Documents;

/// <summary>
/// Base class for OCR-backed document analyzers. Runs the shared <see cref="TextRecognizer"/> over each
/// frame, hands the recognized text to an <see cref="IDocumentParser{TDocument}"/>, raises
/// <see cref="DocumentDetected"/> (this analyzer's own strongly-typed event) on a successful parse, and
/// draws the parser's boxes. Derive and supply a parser for a specific document type (see
/// <see cref="InvoiceAnalyzer"/>, <see cref="HealthCardAnalyzer"/>).
/// </summary>
/// <typeparam name="TDocument">The strongly-typed document payload (e.g. <c>Invoice</c>).</typeparam>
public abstract class DocumentAnalyzer<TDocument> : FrameAnalyzer
{
    readonly TextRecognizer recognizer = new();
    readonly IDocumentParser<TDocument> parser;

    /// <param name="parser">Strategy turning recognized text into a typed payload + overlay boxes.</param>
    protected DocumentAnalyzer(IDocumentParser<TDocument> parser) => this.parser = parser;

    /// <summary>Command invoked (with the <see cref="DocumentDetectedEventArgs{TDocument}"/>) on a recognition.</summary>
    public static readonly BindableProperty DocumentDetectedCommandProperty = BindableProperty.Create(
        nameof(DocumentDetectedCommand), typeof(ICommand), typeof(DocumentAnalyzer<TDocument>));

    /// <inheritdoc cref="DocumentDetectedCommandProperty"/>
    public ICommand? DocumentDetectedCommand
    {
        get => (ICommand?)this.GetValue(DocumentDetectedCommandProperty);
        set => this.SetValue(DocumentDetectedCommandProperty, value);
    }

    /// <summary>
    /// Optional selector deciding the boxes to draw for the recognized document; return <c>null</c> for no
    /// overlay. When unset the analyzer draws the detected document outline (when the frame was deskewed) or
    /// the parser's field boxes (whole-frame fallback).
    /// </summary>
    public Func<TDocument, IReadOnlyList<OverlayBox>?>? OverlayProvider { get; set; }

    /// <summary>
    /// Continuation invoked (on the UI thread) with the recognized document while the analyzer is armed; return
    /// <c>true</c> to keep scanning (stay armed for the next document), <c>false</c> to stop until the next
    /// <see cref="CameraView.Scan"/>. When unset, delivery is single-shot. Bindable so it can target a VM method.
    /// </summary>
    public static readonly BindableProperty OnDetectedProperty = BindableProperty.Create(
        nameof(OnDetected), typeof(Func<DocumentDetectedEventArgs<TDocument>, Task<bool>>), typeof(DocumentAnalyzer<TDocument>));

    /// <inheritdoc cref="OnDetectedProperty"/>
    public Func<DocumentDetectedEventArgs<TDocument>, Task<bool>>? OnDetected
    {
        get => (Func<DocumentDetectedEventArgs<TDocument>, Task<bool>>?)this.GetValue(OnDetectedProperty);
        set => this.SetValue(OnDetectedProperty, value);
    }

    /// <summary>Outline color drawn around a detected document. Default a blue accent.</summary>
    public Color BoxColor { get; set; } = Color.FromArgb("#3B82F6");

    /// <summary>
    /// How many frames to merge a document's reads across before firing <see cref="DocumentDetected"/> — so a
    /// card/passport that reveals its fields gradually (number, then name, then expiry) yields one complete
    /// payload instead of a flood of partials. The event fires earlier if the parser reports the accumulation
    /// <see cref="IDocumentParser{TDocument}.IsComplete"/>, and only once per document (it won't re-fire while
    /// the same document stays in view). Default 5; set 1 for the old fire-every-frame behavior.
    /// </summary>
    public int AccumulationFrames { get; set; } = 5;

    /// <summary>
    /// How many consecutive frames with no recognized document clear the in-progress accumulation, so a
    /// different document presented next re-arms the event. Default 5.
    /// </summary>
    public int ResetAfterEmptyFrames { get; set; } = 5;

    /// <summary>Raised on the UI thread when a document of this type is recognized in a frame.</summary>
    public event EventHandler<DocumentDetectedEventArgs<TDocument>>? DocumentDetected;

    TDocument? accumulated;
    int framesAccrued;
    int emptyFrames;
    bool emitted;
    IReadOnlyList<OverlayBox>? lastOverlay;

    /// <inheritdoc/>
    /// <remarks>Closes the native text recognizer client (Android ML Kit); it is re-created on the next frame.</remarks>
    protected override void OnDetached()
    {
        base.OnDetached();
        this.recognizer.ReleaseNativeResources();
    }

    /// <inheritdoc/>
    public override async ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeAsync(CameraFrame frame, CancellationToken ct)
    {
        var doc = await this.recognizer.RecognizeDocumentAsync(frame, ct).ConfigureAwait(false);
        var text = doc.Blocks;

        if (text.Count == 0 || !this.parser.TryParse(text, out var incoming, out var boxes))
        {
            // tolerate brief drop-outs while a document is in hand; reset once it's clearly gone
            if (++this.emptyFrames >= Math.Max(1, this.ResetAfterEmptyFrames))
            {
                this.Reset();
                return null;
            }
            return this.lastOverlay; // persist the last box so it doesn't flicker between reads
        }

        this.emptyFrames = 0;
        this.accumulated = this.accumulated is null ? incoming : this.parser.Merge(this.accumulated, incoming);
        this.framesAccrued++;

        // gate the one-shot transition on IsArmed so a completion seen while disarmed isn't consumed (it would
        // otherwise never deliver once the user arms). The same document won't re-deliver while it stays in view;
        // once it leaves, Reset() re-opens delivery for the next document.
        if (!this.emitted && this.IsArmed &&
            (this.parser.IsComplete(this.accumulated) || this.framesAccrued >= Math.Max(1, this.AccumulationFrames)))
        {
            this.emitted = true;
            var args = new DocumentDetectedEventArgs<TDocument>(this.accumulated);
            // Pass the typed event args (not the raw document) as the command parameter — bound commands are
            // Command<DocumentDetectedEventArgs<TDocument>>, so a raw TDocument fails their CanExecute type check
            // and the command (e.g. "add to the scanned-documents store") never runs.
            this.Deliver(args, () => this.DocumentDetected?.Invoke(this, args), this.DocumentDetectedCommand, this.OnDetected);
        }

        // Overlay: when the frame was deskewed the parser's boxes are in flat document space (wrong on the
        // live preview), so draw the detected document outline instead; otherwise draw the parser's field boxes.
        this.lastOverlay = this.ResolveOverlay(this.accumulated, this.OverlayProvider, () =>
            doc.Quad is { } q
                ? [new OverlayBox(q.Bounds, this.BoxColor, null, this.BoxColor)]
                : (boxes.Count == 0 ? null : boxes));
        return this.lastOverlay;
    }

    void Reset()
    {
        this.accumulated = default;
        this.framesAccrued = 0;
        this.emptyFrames = 0;
        this.emitted = false;
        this.lastOverlay = null;
    }
}
