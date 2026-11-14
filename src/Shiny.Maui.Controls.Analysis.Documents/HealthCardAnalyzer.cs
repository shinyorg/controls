namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>
/// Recognizes a health-insurance card from the camera via OCR and raises
/// <see cref="DocumentAnalyzer{HealthCard}.DocumentDetected"/> with a strongly-typed <see cref="HealthCard"/>.
/// Uses a best-effort <see cref="HealthCardParser"/> by default; pass a custom
/// <see cref="IDocumentParser{HealthCard}"/> for a specific issuer's layout.
/// </summary>
public class HealthCardAnalyzer : DocumentAnalyzer<HealthCard>
{
    /// <summary>Use the built-in best-effort <see cref="HealthCardParser"/>.</summary>
    public HealthCardAnalyzer() : base(new HealthCardParser())
    {
    }

    /// <summary>Use a custom parser for a specific issuer's layout.</summary>
    public HealthCardAnalyzer(Shiny.Controls.Analysis.IDocumentParser<HealthCard> parser) : base(parser)
    {
    }

    /// <inheritdoc/>
    public override string Id => "shiny.camera.healthcard";
}
