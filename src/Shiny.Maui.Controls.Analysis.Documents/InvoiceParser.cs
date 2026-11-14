using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Maui.Graphics;

namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>
/// Best-effort rules parser that turns OCR text into an <see cref="Invoice"/>: header total / number /
/// date plus repeating order <see cref="InvoiceLine"/>s. Heuristic by nature — swap in a custom
/// <see cref="IDocumentParser{Invoice}"/> (rules, cloud Document AI, or an LLM) for production accuracy.
/// </summary>
public partial class InvoiceParser : IDocumentParser<Invoice>
{
    /// <summary>Color for the highlighted total line.</summary>
    public Color TotalColor { get; set; } = Color.FromArgb("#22C55E");

    /// <summary>Color for the order-line boxes.</summary>
    public Color LineColor { get; set; } = Color.FromArgb("#A78BFA");

    [GeneratedRegex(@"-?\$?\d[\d,]*\.\d{2}")]
    private static partial Regex MoneyRegex();

    [GeneratedRegex(@"\b(\d{1,4}[/-]\d{1,2}[/-]\d{1,4}|\d{4}-\d{2}-\d{2})\b")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"invoice\s*(?:#|no\.?|number|num)?\s*[:#]?\s*([A-Za-z0-9-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex InvoiceNumberRegex();

    [GeneratedRegex(@"^\s*(\d{1,3})\s+\D")]
    private static partial Regex LeadingQtyRegex();

    public bool TryParse(
        IReadOnlyList<RecognizedText> text,
        out Invoice document,
        out IReadOnlyList<OverlayBox> boxes)
    {
        document = null!;
        boxes = [];

        // top-to-bottom reading order
        var lines = text
            .Where(t => !string.IsNullOrWhiteSpace(t.Text))
            .OrderBy(t => t.BoundingBox.Y)
            .ThenBy(t => t.BoundingBox.X)
            .ToList();
        if (lines.Count == 0)
            return false;

        string? number = null;
        DateOnly? date = null;
        decimal? total = null;
        RecognizedText? totalLine = null;
        var items = new List<InvoiceLine>();
        var itemLines = new List<RecognizedText>();

        foreach (var line in lines)
        {
            var t = line.Text.Trim();
            var lower = t.ToLowerInvariant();
            var money = MoneyRegex().Matches(t);

            number ??= MatchInvoiceNumber(t);
            date ??= MatchDate(t);

            var isTotal = lower.Contains("total") || lower.Contains("amount due") || lower.Contains("balance due");
            var isSummary = isTotal || lower.Contains("subtotal") || lower.Contains("tax") ||
                lower.Contains("discount") || lower.Contains("change") || lower.Contains("tendered");

            if (isTotal && money.Count > 0 && !lower.Contains("subtotal"))
            {
                // prefer the last "total" line (grand total usually appears below subtotal)
                total = ParseMoney(money[^1].Value);
                totalLine = line;
                continue;
            }

            // an order line: has a trailing amount and isn't a summary/header row
            if (money.Count > 0 && !isSummary)
            {
                var amount = ParseMoney(money[^1].Value);
                decimal? unit = money.Count >= 2 ? ParseMoney(money[^2].Value) : null;
                var qty = MatchLeadingQty(t);
                var description = DescriptionOf(t, money[0].Index);

                items.Add(new InvoiceLine(description, qty, unit, amount, line.BoundingBox));
                itemLines.Add(line);
            }
        }

        if (number == null && total == null && items.Count == 0)
            return false;

        var fields = new List<DocumentField>();
        if (number != null) fields.Add(new DocumentField("Invoice #", number));
        if (date != null) fields.Add(new DocumentField("Date", date.Value.ToString("yyyy-MM-dd")));
        if (total != null) fields.Add(new DocumentField("Total", total.Value.ToString("0.00", CultureInfo.InvariantCulture)));

        document = new Invoice(number, date, total, items, fields);

        var overlay = new List<OverlayBox>(itemLines.Count + 1);
        foreach (var item in itemLines)
            overlay.Add(new OverlayBox(item.BoundingBox, this.LineColor));
        if (totalLine is { } tl)
            overlay.Add(new OverlayBox(tl.BoundingBox, this.TotalColor,
                total?.ToString("0.00", CultureInfo.InvariantCulture), this.TotalColor));
        boxes = overlay;
        return true;
    }

    /// <inheritdoc/>
    public Invoice Merge(Invoice accumulated, Invoice incoming)
    {
        // a different invoice in view replaces the accumulation
        if (accumulated.Number is not null && incoming.Number is not null && accumulated.Number != incoming.Number)
            return incoming;

        return accumulated with
        {
            Number = accumulated.Number ?? incoming.Number,
            Date = accumulated.Date ?? incoming.Date,
            Total = accumulated.Total ?? incoming.Total,
            Lines = DocumentMerge.Longer(accumulated.Lines, incoming.Lines),
            Fields = DocumentMerge.Richer(accumulated.Fields, incoming.Fields)
        };
    }

    /// <inheritdoc/>
    public bool IsComplete(Invoice document)
        => document.Number is not null && document.Total is not null;

    static string? MatchInvoiceNumber(string line)
    {
        var m = InvoiceNumberRegex().Match(line);
        return m.Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : null;
    }

    static DateOnly? MatchDate(string line)
    {
        var m = DateRegex().Match(line);
        if (!m.Success)
            return null;

        foreach (var fmt in new[] { "yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy", "d/M/yyyy", "M-d-yyyy", "yyyy/MM/dd" })
        {
            if (DateOnly.TryParseExact(m.Value, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d;
        }
        return DateOnly.TryParse(m.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var any) ? any : null;
    }

    static decimal? MatchLeadingQty(string line)
    {
        var m = LeadingQtyRegex().Match(line);
        return m.Success && decimal.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var q)
            ? q
            : null;
    }

    static string? DescriptionOf(string line, int firstMoneyIndex)
    {
        var desc = (firstMoneyIndex > 0 ? line[..firstMoneyIndex] : line).Trim(' ', '\t', '.', '-', ':', '$');
        return desc.Length == 0 ? null : desc;
    }

    static decimal? ParseMoney(string value)
    {
        var cleaned = value.Replace("$", string.Empty).Replace(",", string.Empty).Trim();
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}
