namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>
/// Recognizes an invoice/receipt from the camera via OCR and raises <see cref="DocumentAnalyzer{Invoice}.DocumentDetected"/>
/// with a strongly-typed <see cref="Invoice"/> (header fields + order lines). Uses a best-effort
/// <see cref="InvoiceParser"/> by default; pass a custom <see cref="IDocumentParser{Invoice}"/> for better
/// accuracy.
/// </summary>
public class InvoiceAnalyzer : DocumentAnalyzer<Invoice>
{
    /// <summary>Use the built-in best-effort <see cref="InvoiceParser"/>.</summary>
    public InvoiceAnalyzer() : base(new InvoiceParser())
    {
    }

    /// <summary>Use a custom parser (rules, cloud Document AI, or an LLM).</summary>
    public InvoiceAnalyzer(Shiny.Controls.Analysis.IDocumentParser<Invoice> parser) : base(parser)
    {
    }

    /// <inheritdoc/>
    public override string Id => "shiny.camera.invoice";
}
