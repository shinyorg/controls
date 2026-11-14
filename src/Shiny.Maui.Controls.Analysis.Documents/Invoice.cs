using Microsoft.Maui.Graphics;

namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>A single order line on an invoice/receipt — the repeating-group case.</summary>
/// <param name="Description">The line description / item name.</param>
/// <param name="Quantity">Quantity, when present.</param>
/// <param name="UnitPrice">Unit price, when present.</param>
/// <param name="Amount">Line total / extended amount.</param>
/// <param name="Bounds">Normalized bounds (0..1) of the line in upright image space.</param>
public record InvoiceLine(
    string? Description,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal? Amount,
    RectF? Bounds = null
);


/// <summary>
/// An invoice/receipt extracted from OCR text. Strongly-typed header fields plus the order
/// <see cref="Lines"/> (repeating groups) and a <see cref="Fields"/> bag of anything else recognized.
/// </summary>
/// <param name="Number">The invoice/receipt number, when found.</param>
/// <param name="Date">The invoice date, when found.</param>
/// <param name="Total">The grand total, when found.</param>
/// <param name="Lines">The order lines (may be empty).</param>
/// <param name="Fields">All recognized header fields as label/value pairs.</param>
public record Invoice(
    string? Number,
    DateOnly? Date,
    decimal? Total,
    IReadOnlyList<InvoiceLine> Lines,
    IReadOnlyList<DocumentField> Fields
);
