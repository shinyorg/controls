using Microsoft.Maui.Graphics;

namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>A single phone number on a business card — the repeating-group case (a card often lists several).</summary>
/// <param name="Number">The phone number as printed.</param>
/// <param name="Type">The line label when found (e.g. "Mobile", "Office", "Fax").</param>
/// <param name="Bounds">Normalized bounds (0..1) of the line in upright image space.</param>
public record BusinessCardPhone(
    string Number,
    string? Type,
    RectF? Bounds = null
);


/// <summary>
/// A business card extracted from OCR text. Strongly-typed contact fields plus the repeating
/// <see cref="Phones"/> / <see cref="Emails"/> groups and a <see cref="Fields"/> bag of anything else
/// recognized. Every value is nullable — only what was found is set. OCR + best-effort rules by nature;
/// supply a custom <see cref="IDocumentParser{BusinessCard}"/> (rules, cloud Document AI, or an LLM) for
/// production accuracy.
/// </summary>
/// <param name="Name">The person's full name, usually the most prominent line.</param>
/// <param name="JobTitle">The job title / role (e.g. "Software Engineer", "VP of Sales"), when found.</param>
/// <param name="Company">The company / organization name, when found.</param>
/// <param name="Emails">The email addresses (may be empty).</param>
/// <param name="Phones">The phone numbers, each with an optional type (may be empty).</param>
/// <param name="Website">The website / URL, when found.</param>
/// <param name="Address">The mailing address, when found.</param>
/// <param name="Fields">All recognized fields as label/value pairs.</param>
public record BusinessCard(
    string? Name,
    string? JobTitle,
    string? Company,
    IReadOnlyList<string> Emails,
    IReadOnlyList<BusinessCardPhone> Phones,
    string? Website,
    string? Address,
    IReadOnlyList<DocumentField> Fields
)
{
    /// <summary>The first email address, when any was found.</summary>
    public string? Email => this.Emails.Count > 0 ? this.Emails[0] : null;

    /// <summary>The first phone number, when any was found.</summary>
    public string? Phone => this.Phones.Count > 0 ? this.Phones[0].Number : null;
}
