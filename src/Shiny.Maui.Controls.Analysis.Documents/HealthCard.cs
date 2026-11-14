
namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>
/// A health-insurance card recognized from OCR text. Card fronts carry no standard barcode, so extraction
/// is OCR + rules and therefore best-effort; supply a custom parser for a specific issuer/format.
/// </summary>
/// <param name="Number">The health card / member number.</param>
/// <param name="Name">The cardholder name, when found.</param>
/// <param name="Expiry">The expiry date, when found.</param>
/// <param name="Issuer">The issuing plan/authority, when found.</param>
/// <param name="Province">The issuing Canadian province name (e.g. "Ontario", "Quebec"), when recognized.</param>
/// <param name="Fields">All recognized fields as label/value pairs.</param>
public record HealthCard(
    string? Number,
    string? Name,
    DateOnly? Expiry,
    string? Issuer,
    string? Province,
    IReadOnlyList<DocumentField> Fields
);
