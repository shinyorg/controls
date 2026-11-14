using System.Diagnostics.CodeAnalysis;
using Microsoft.Maui.Graphics;

namespace Shiny.Controls.Analysis;

/// <summary>A block/line of text recognized by OCR, in normalized upright image space.</summary>
/// <param name="Text">The recognized text.</param>
/// <param name="BoundingBox">Normalized bounds (0..1) of the text in upright image space.</param>
/// <param name="Confidence">Recognizer confidence 0..1 (1 when unknown).</param>
public record RecognizedText(string Text, RectF BoundingBox, float Confidence = 1f);


/// <summary>
/// A single extracted field of a structured document — a reusable label/value building block. Carries its
/// own normalized bounds (so it can be highlighted) and confidence; documents expose a list of these (not
/// a dictionary) so duplicate labels and per-field geometry survive.
/// </summary>
/// <param name="Label">The field name (e.g. "Total", "License #", "Expiry").</param>
/// <param name="Value">The field value, or <c>null</c> when only the label was located.</param>
/// <param name="Bounds">Optional normalized bounds (0..1) of the value in upright image space.</param>
/// <param name="Confidence">Extraction confidence 0..1 (1 when unknown).</param>
public record DocumentField(string Label, string? Value, RectF? Bounds = null, float Confidence = 1f);


/// <summary>
/// One repeating row of a document — e.g. an invoice order line — modeled as its own group of
/// <see cref="DocumentField"/>s (Description / Qty / UnitPrice / Amount). This is how repeating groups are
/// represented; strongly-typed payloads may also project them into first-class line records.
/// </summary>
/// <param name="Fields">The fields making up this row.</param>
/// <param name="Bounds">Optional normalized bounds (0..1) of the whole row in upright image space.</param>
public record DocumentLineItem(IReadOnlyList<DocumentField> Fields, RectF? Bounds = null);


/// <summary>
/// Event args carrying a strongly-typed document payload. Each document analyzer exposes its own event of
/// this type closed over its payload (e.g. <c>EventHandler&lt;DocumentDetectedEventArgs&lt;Invoice&gt;&gt;</c>),
/// so consumers get the result strongly typed end to end.
/// </summary>
/// <typeparam name="TDocument">The document payload type (e.g. <c>Invoice</c>, <c>DriversLicense</c>).</typeparam>
public class DocumentDetectedEventArgs<TDocument>(TDocument document) : EventArgs
{
    /// <summary>The recognized document.</summary>
    public TDocument Document { get; } = document;
}


/// <summary>
/// Strategy that turns OCR text into a typed document payload plus the boxes to draw for it. Supply a
/// custom implementation to a <c>DocumentAnalyzer&lt;TDocument&gt;</c> to swap in rules, a cloud Document
/// AI, or an LLM without changing the analyzer.
/// </summary>
/// <typeparam name="TDocument">The document payload type produced on a successful parse.</typeparam>
public interface IDocumentParser<TDocument>
{
    /// <summary>
    /// Attempt to parse the recognized text into a document. Return <c>true</c> with
    /// <paramref name="document"/> and <paramref name="boxes"/> populated on success; <c>false</c> when the
    /// text is not a document of this type (the analyzer then clears its overlay).
    /// </summary>
    bool TryParse(
        IReadOnlyList<RecognizedText> text,
        [MaybeNullWhen(false)] out TDocument document,
        out IReadOnlyList<OverlayBox> boxes
    );

    /// <summary>
    /// Merge a freshly parsed read into the document accumulated over previous frames — typically filling in
    /// fields that were still <c>null</c> and preferring the richer of two repeating-row collections. The
    /// analyzer calls this each frame so a document that reveals its fields gradually (number first, then name,
    /// then expiry) ends up as one complete payload instead of a stream of partials. When <paramref name="incoming"/>
    /// is a <i>different</i> document (e.g. a different card number) implementations should return it as-is to
    /// start a fresh accumulation. The default replaces the accumulation with the latest read (no merging).
    /// </summary>
    /// <param name="accumulated">The document built from earlier frames in this run.</param>
    /// <param name="incoming">The document parsed from the current frame.</param>
    TDocument Merge(TDocument accumulated, TDocument incoming) => incoming;

    /// <summary>
    /// Whether the accumulated document already has enough to emit, letting the analyzer fire early instead of
    /// spending its full frame budget. The default returns <c>false</c> so the analyzer always emits once it has
    /// merged across <c>AccumulationFrames</c> frames; override to short-circuit as soon as the key fields are present.
    /// </summary>
    bool IsComplete(TDocument document) => false;
}
