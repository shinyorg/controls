namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>
/// Recognizes a payment card from the camera via OCR and raises
/// <see cref="DocumentAnalyzer{CreditCard}.DocumentDetected"/> with a strongly-typed <see cref="CreditCard"/>
/// (brand + number validated deterministically; the rest best-effort OCR). Pass a custom
/// <see cref="IDocumentParser{CreditCard}"/> for a specific layout.
/// </summary>
public class CreditCardAnalyzer : DocumentAnalyzer<CreditCard>
{
    /// <summary>Use the built-in best-effort <see cref="CreditCardParser"/>.</summary>
    public CreditCardAnalyzer() : base(new CreditCardParser())
    {
    }

    /// <summary>Use a custom parser.</summary>
    public CreditCardAnalyzer(Shiny.Controls.Analysis.IDocumentParser<CreditCard> parser) : base(parser)
    {
    }

    /// <inheritdoc/>
    public override string Id => "shiny.camera.creditcard";
}
