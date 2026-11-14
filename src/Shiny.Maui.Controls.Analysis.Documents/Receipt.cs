using Microsoft.Maui.Graphics;

namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>A single purchased line item on a receipt — the repeating-group case.</summary>
/// <param name="Description">The item description / name.</param>
/// <param name="Quantity">Quantity, when present.</param>
/// <param name="UnitPrice">Unit price, when present.</param>
/// <param name="Amount">Line total / extended amount.</param>
/// <param name="Bounds">Normalized bounds (0..1) of the line in upright image space.</param>
public record ReceiptLine(
    string? Description,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal? Amount,
    RectF? Bounds = null
);


/// <summary>
/// A single tax line on a receipt — receipts frequently carry more than one (e.g. GST + PST, state + city),
/// so each is its own row with an optional rate.
/// </summary>
/// <param name="Label">The tax name (e.g. "GST", "VAT", "Sales Tax"), when found.</param>
/// <param name="Rate">The tax rate as a percentage (e.g. 13 for 13%), when found.</param>
/// <param name="Amount">The tax amount.</param>
/// <param name="Bounds">Normalized bounds (0..1) of the line in upright image space.</param>
public record ReceiptTax(
    string? Label,
    decimal? Rate,
    decimal? Amount,
    RectF? Bounds = null
);


/// <summary>
/// A point-of-sale receipt extracted from OCR text. Strongly-typed header/summary fields, the purchased
/// <see cref="Lines"/> (repeating groups), a per-tax <see cref="Taxes"/> breakdown, and a <see cref="Fields"/>
/// bag of anything else recognized. Every value is nullable — only what was found is set. OCR + best-effort
/// rules by nature; supply a custom <see cref="IDocumentParser{Receipt}"/> for production accuracy.
/// </summary>
/// <param name="Merchant">The store / company name, usually the top line.</param>
/// <param name="MerchantPhone">The merchant phone number, when found.</param>
/// <param name="ReceiptNumber">The receipt / invoice / transaction number, when found.</param>
/// <param name="Date">The purchase date, when found.</param>
/// <param name="Time">The purchase time, when found.</param>
/// <param name="Lines">The purchased line items (may be empty).</param>
/// <param name="Subtotal">The pre-tax subtotal, when found.</param>
/// <param name="Taxes">The per-tax breakdown (may be empty).</param>
/// <param name="Tax">The total tax (sum of <see cref="Taxes"/> or a single "tax" line), when found.</param>
/// <param name="Tip">The tip / gratuity, when found.</param>
/// <param name="Discount">The discount applied, when found.</param>
/// <param name="Total">The grand total paid, when found.</param>
/// <param name="Currency">The currency symbol/code detected (e.g. "$", "€", "USD"), when found.</param>
/// <param name="PaymentMethod">The payment method (e.g. "Visa", "Cash", "Debit"), when found.</param>
/// <param name="CardLast4">The last four digits of the card used, when found.</param>
/// <param name="Fields">All recognized summary fields as label/value pairs.</param>
public record Receipt(
    string? Merchant,
    string? MerchantPhone,
    string? ReceiptNumber,
    DateOnly? Date,
    TimeOnly? Time,
    IReadOnlyList<ReceiptLine> Lines,
    decimal? Subtotal,
    IReadOnlyList<ReceiptTax> Taxes,
    decimal? Tax,
    decimal? Tip,
    decimal? Discount,
    decimal? Total,
    string? Currency,
    string? PaymentMethod,
    string? CardLast4,
    IReadOnlyList<DocumentField> Fields
);
