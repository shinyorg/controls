namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>
/// Recognizes a passport from the camera by locating and parsing its MRZ, raising
/// <see cref="DocumentAnalyzer{Passport}.DocumentDetected"/> with a strongly-typed <see cref="Passport"/>.
/// The MRZ parse is deterministic. Pass a custom <see cref="IDocumentParser{Passport}"/> to change how the
/// lines are located/parsed.
/// </summary>
public class PassportAnalyzer : DocumentAnalyzer<Passport>
{
    /// <summary>Use the built-in MRZ <see cref="PassportParser"/>.</summary>
    public PassportAnalyzer() : base(new PassportParser())
    {
    }

    /// <summary>Use a custom parser.</summary>
    public PassportAnalyzer(Shiny.Controls.Analysis.IDocumentParser<Passport> parser) : base(parser)
    {
    }

    /// <inheritdoc/>
    public override string Id => "shiny.camera.passport";
}
