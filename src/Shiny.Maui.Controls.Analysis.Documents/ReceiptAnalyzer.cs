namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>
/// Recognizes a point-of-sale receipt from the camera via OCR and raises
/// <see cref="DocumentAnalyzer{Receipt}.DocumentDetected"/> with a strongly-typed <see cref="Receipt"/>
/// (merchant + line items + tax breakdown + totals). Uses a best-effort <see cref="ReceiptParser"/> by
/// default; pass a custom <see cref="IDocumentParser{Receipt}"/> (rules, cloud Document AI, or an LLM) for
/// better accuracy.
/// </summary>
public class ReceiptAnalyzer : DocumentAnalyzer<Receipt>
{
    /// <summary>Use the built-in best-effort <see cref="ReceiptParser"/>.</summary>
    public ReceiptAnalyzer() : base(new ReceiptParser())
    {
    }

    /// <summary>Use a custom parser (rules, cloud Document AI, or an LLM).</summary>
    public ReceiptAnalyzer(Shiny.Controls.Analysis.IDocumentParser<Receipt> parser) : base(parser)
    {
    }

    /// <inheritdoc/>
    public override string Id => "shiny.camera.receipt";
}
