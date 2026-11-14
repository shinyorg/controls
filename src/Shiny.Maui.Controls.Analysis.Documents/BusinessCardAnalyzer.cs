namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>
/// Recognizes a business card from the camera via OCR and raises
/// <see cref="DocumentAnalyzer{BusinessCard}.DocumentDetected"/> with a strongly-typed <see cref="BusinessCard"/>
/// (name + job title + company + emails + phones + website + address). Uses a best-effort
/// <see cref="BusinessCardParser"/> by default; pass a custom <see cref="IDocumentParser{BusinessCard}"/>
/// (rules, cloud Document AI, or an LLM) for better accuracy.
/// </summary>
public class BusinessCardAnalyzer : DocumentAnalyzer<BusinessCard>
{
    /// <summary>Use the built-in best-effort <see cref="BusinessCardParser"/>.</summary>
    public BusinessCardAnalyzer() : base(new BusinessCardParser())
    {
    }

    /// <summary>Use a custom parser (rules, cloud Document AI, or an LLM).</summary>
    public BusinessCardAnalyzer(Shiny.Controls.Analysis.IDocumentParser<BusinessCard> parser) : base(parser)
    {
    }

    /// <inheritdoc/>
    public override string Id => "shiny.camera.businesscard";
}
