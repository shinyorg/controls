using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Maui.Graphics;

namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>
/// Best-effort rules parser that turns OCR text into a <see cref="Receipt"/>: merchant header, purchased
/// <see cref="ReceiptLine"/>s, a per-tax <see cref="ReceiptTax"/> breakdown, and the summary totals
/// (subtotal / tax / tip / discount / total). Heuristic by nature — swap in a custom
/// <see cref="IDocumentParser{Receipt}"/> (rules, cloud Document AI, or an LLM) for production accuracy.
/// </summary>
public partial class ReceiptParser : IDocumentParser<Receipt>
{
    /// <summary>Color for the highlighted grand-total line.</summary>
    public Color TotalColor { get; set; } = Color.FromArgb("#22C55E");

    /// <summary>Color for the purchased-line boxes.</summary>
    public Color LineColor { get; set; } = Color.FromArgb("#A78BFA");

    /// <summary>Color for tax / subtotal / fee summary boxes.</summary>
    public Color TaxColor { get; set; } = Color.FromArgb("#F59E0B");

    [GeneratedRegex(@"(?<sym>[$€£¥])?\s?-?\d[\d,]*\.\d{2}")]
    private static partial Regex MoneyRegex();

    [GeneratedRegex(@"\b(\d{1,4}[/-]\d{1,2}[/-]\d{1,4}|\d{4}-\d{2}-\d{2})\b")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"\b(\d{1,2}:\d{2}(?::\d{2})?\s*(?:[AaPp]\.?[Mm]\.?)?)\b")]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"(?:receipt|invoice|trans(?:action)?|order|ref|check)\s*(?:#|no\.?|number|num|id)?\s*[:#]?\s*([A-Za-z0-9][A-Za-z0-9-]{2,})", RegexOptions.IgnoreCase)]
    private static partial Regex ReceiptNumberRegex();

    [GeneratedRegex(@"(?:tel|phone|ph)\.?\s*[:#]?\s*(\+?\d[\d\s().-]{7,}\d)", RegexOptions.IgnoreCase)]
    private static partial Regex LabeledPhoneRegex();

    [GeneratedRegex(@"(?:ending(?:\s*in)?|x{2,}|\*{2,}|•{2,})\s*(\d{4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex CardLast4Regex();

    [GeneratedRegex(@"(\d{1,2}(?:\.\d+)?)\s*%")]
    private static partial Regex PercentRegex();

    [GeneratedRegex(@"^\s*(\d{1,3})\s+\D")]
    private static partial Regex LeadingQtyRegex();

    public bool TryParse(
        IReadOnlyList<RecognizedText> text,
        out Receipt document,
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

        string? merchant = null;
        string? phone = null;
        string? receiptNumber = null;
        string? currency = null;
        string? paymentMethod = null;
        string? cardLast4 = null;
        DateOnly? date = null;
        TimeOnly? time = null;
        decimal? subtotal = null;
        decimal? tax = null;
        decimal? tip = null;
        decimal? discount = null;
        decimal? total = null;

        RecognizedText? totalLine = null;
        var itemLines = new List<ReceiptLine>();
        var itemBoxes = new List<RecognizedText>();
        var taxes = new List<ReceiptTax>();
        var summaryBoxes = new List<RecognizedText>();

        foreach (var line in lines)
        {
            var t = line.Text.Trim();
            var lower = t.ToLowerInvariant();
            var money = MoneyRegex().Matches(t);

            // first money symbol seen sets the currency
            currency ??= money.Select(m => m.Groups["sym"].Value).FirstOrDefault(s => s.Length > 0);

            receiptNumber ??= MatchReceiptNumber(t);
            date ??= MatchDate(t);
            time ??= MatchTime(t);
            phone ??= MatchPhone(t, money.Count);
            cardLast4 ??= MatchCardLast4(t);
            paymentMethod ??= MatchPaymentMethod(lower);

            // pick the merchant from the first text-only line at the top
            if (merchant == null && money.Count == 0 && DateRegex().Match(t).Success == false && HasLetters(t))
                merchant = t;

            var lineAmount = money.Count > 0 ? ParseMoney(money[^1].Value) : null;

            var isTotal = (lower.Contains("total") || lower.Contains("amount due") || lower.Contains("balance due"))
                && !lower.Contains("subtotal") && !lower.Contains("sub total");

            if (isTotal && lineAmount != null)
            {
                // prefer the last "total" line (grand total usually appears below subtotal)
                total = lineAmount;
                totalLine = line;
                continue;
            }

            if ((lower.Contains("subtotal") || lower.Contains("sub total")) && lineAmount != null)
            {
                subtotal = lineAmount;
                summaryBoxes.Add(line);
                continue;
            }

            if (IsTaxLine(lower) && lineAmount != null)
            {
                taxes.Add(new ReceiptTax(TaxLabel(t, money[0].Index), MatchPercent(t), lineAmount, line.BoundingBox));
                summaryBoxes.Add(line);
                continue;
            }

            if ((lower.Contains("tip") || lower.Contains("gratuity")) && lineAmount != null)
            {
                tip = lineAmount;
                summaryBoxes.Add(line);
                continue;
            }

            if (lower.Contains("discount") && lineAmount != null)
            {
                discount = lineAmount;
                summaryBoxes.Add(line);
                continue;
            }

            // a non-summary purchased line: has a trailing amount and isn't a tender / payment row
            if (lineAmount != null && !IsNonItemSummary(lower) && MatchPaymentMethod(lower) == null)
            {
                var unit = money.Count >= 2 ? ParseMoney(money[^2].Value) : null;
                var qty = MatchLeadingQty(t);
                var description = DescriptionOf(t, money[0].Index);
                itemLines.Add(new ReceiptLine(description, qty, unit, lineAmount, line.BoundingBox));
                itemBoxes.Add(line);
            }
        }

        // total tax: sum the breakdown, else fall back to a single "tax" amount captured above
        if (taxes.Count > 0)
            tax = taxes.Sum(x => x.Amount ?? 0m);

        // a top line alone (merchant) isn't enough — require a real receipt signal
        if (receiptNumber == null && total == null && subtotal == null && taxes.Count == 0 && itemLines.Count == 0)
            return false;

        var fields = new List<DocumentField>();
        if (merchant != null) fields.Add(new DocumentField("Merchant", merchant));
        if (receiptNumber != null) fields.Add(new DocumentField("Receipt #", receiptNumber));
        if (date != null) fields.Add(new DocumentField("Date", date.Value.ToString("yyyy-MM-dd")));
        if (time != null) fields.Add(new DocumentField("Time", time.Value.ToString("HH:mm")));
        if (subtotal != null) fields.Add(new DocumentField("Subtotal", Format(subtotal.Value)));
        if (tax != null) fields.Add(new DocumentField("Tax", Format(tax.Value)));
        if (tip != null) fields.Add(new DocumentField("Tip", Format(tip.Value)));
        if (discount != null) fields.Add(new DocumentField("Discount", Format(discount.Value)));
        if (total != null) fields.Add(new DocumentField("Total", Format(total.Value)));
        if (paymentMethod != null) fields.Add(new DocumentField("Payment", paymentMethod));
        if (cardLast4 != null) fields.Add(new DocumentField("Card", "•••• " + cardLast4));
        if (phone != null) fields.Add(new DocumentField("Phone", phone));

        document = new Receipt(
            merchant, phone, receiptNumber, date, time,
            itemLines, subtotal, taxes, tax, tip, discount, total,
            currency, paymentMethod, cardLast4, fields);

        var overlay = new List<OverlayBox>(itemBoxes.Count + summaryBoxes.Count + 1);
        foreach (var item in itemBoxes)
            overlay.Add(new OverlayBox(item.BoundingBox, this.LineColor));
        foreach (var summary in summaryBoxes)
            overlay.Add(new OverlayBox(summary.BoundingBox, this.TaxColor));
        if (totalLine is { } tl)
            overlay.Add(new OverlayBox(tl.BoundingBox, this.TotalColor, total?.ToString("0.00", CultureInfo.InvariantCulture), this.TotalColor));
        boxes = overlay;
        return true;
    }

    /// <inheritdoc/>
    public Receipt Merge(Receipt accumulated, Receipt incoming)
    {
        // a clearly different receipt (different merchant + total) replaces the accumulation
        if (accumulated.Merchant is not null && incoming.Merchant is not null &&
            !string.Equals(accumulated.Merchant, incoming.Merchant, StringComparison.OrdinalIgnoreCase) &&
            accumulated.Total is not null && incoming.Total is not null && accumulated.Total != incoming.Total)
            return incoming;

        return accumulated with
        {
            Merchant = accumulated.Merchant ?? incoming.Merchant,
            MerchantPhone = accumulated.MerchantPhone ?? incoming.MerchantPhone,
            ReceiptNumber = accumulated.ReceiptNumber ?? incoming.ReceiptNumber,
            Date = accumulated.Date ?? incoming.Date,
            Time = accumulated.Time ?? incoming.Time,
            Lines = DocumentMerge.Longer(accumulated.Lines, incoming.Lines),
            Subtotal = accumulated.Subtotal ?? incoming.Subtotal,
            Taxes = DocumentMerge.Longer(accumulated.Taxes, incoming.Taxes),
            Tax = accumulated.Tax ?? incoming.Tax,
            Tip = accumulated.Tip ?? incoming.Tip,
            Discount = accumulated.Discount ?? incoming.Discount,
            Total = accumulated.Total ?? incoming.Total,
            Currency = accumulated.Currency ?? incoming.Currency,
            PaymentMethod = accumulated.PaymentMethod ?? incoming.PaymentMethod,
            CardLast4 = accumulated.CardLast4 ?? incoming.CardLast4,
            Fields = DocumentMerge.Richer(accumulated.Fields, incoming.Fields)
        };
    }

    /// <inheritdoc/>
    public bool IsComplete(Receipt document)
        => document.Merchant is not null && document.Total is not null;

    static bool IsTaxLine(string lower) =>
        lower.Contains("tax") || lower.Contains("vat") ||
        lower.Contains("gst") || lower.Contains("hst") || lower.Contains("pst") || lower.Contains("qst");

    static bool IsNonItemSummary(string lower) =>
        lower.Contains("total") || lower.Contains("subtotal") || lower.Contains("sub total") ||
        IsTaxLine(lower) || lower.Contains("tip") || lower.Contains("gratuity") ||
        lower.Contains("discount") || lower.Contains("change") || lower.Contains("tendered") ||
        lower.Contains("cash") || lower.Contains("balance") || lower.Contains("amount due");

    static string? MatchReceiptNumber(string line)
    {
        var m = ReceiptNumberRegex().Match(line);
        return m.Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : null;
    }

    static string? MatchPaymentMethod(string lower)
    {
        foreach (var (needle, label) in PaymentMethods)
            if (lower.Contains(needle))
                return label;
        return null;
    }

    static readonly (string Needle, string Label)[] PaymentMethods =
    [
        ("visa", "Visa"), ("mastercard", "Mastercard"), ("master card", "Mastercard"),
        ("amex", "Amex"), ("american express", "Amex"), ("discover", "Discover"),
        ("debit", "Debit"), ("credit", "Credit"), ("cash", "Cash"),
        ("apple pay", "Apple Pay"), ("google pay", "Google Pay"), ("paypal", "PayPal")
    ];

    static string? MatchPhone(string line, int moneyCount)
    {
        var labeled = LabeledPhoneRegex().Match(line);
        if (labeled.Success)
            return labeled.Groups[1].Value.Trim();

        // a bare line that is only a phone number (avoid money/price rows)
        if (moneyCount > 0)
            return null;

        var trimmed = line.Trim();
        var digits = 0;
        foreach (var c in trimmed)
        {
            if (char.IsDigit(c))
                digits++;
            else if (c is not (' ' or '(' or ')' or '-' or '.' or '+' or '/'))
                return null;   // a non-phone character → not a bare phone line
        }
        return digits is >= 10 and <= 15 ? trimmed : null;
    }

    static string? MatchCardLast4(string line)
    {
        var m = CardLast4Regex().Match(line);
        return m.Success ? m.Groups[1].Value : null;
    }

    static decimal? MatchPercent(string line)
    {
        var m = PercentRegex().Match(line);
        return m.Success && decimal.TryParse(m.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var p)
            ? p
            : null;
    }

    static string? TaxLabel(string line, int firstMoneyIndex)
    {
        var label = (firstMoneyIndex > 0 ? line[..firstMoneyIndex] : line).Trim(' ', '\t', '.', '-', ':', '$');
        return label.Length == 0 ? null : label;
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

    static TimeOnly? MatchTime(string line)
    {
        var m = TimeRegex().Match(line);
        if (!m.Success)
            return null;

        var value = m.Value.Replace(".", string.Empty).Trim();
        foreach (var fmt in new[] { "h:mm tt", "hh:mm tt", "H:mm", "HH:mm", "h:mm:ss tt", "HH:mm:ss" })
        {
            if (TimeOnly.TryParseExact(value, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                return t;
        }
        return TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var any) ? any : null;
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
        var cleaned = value.Replace("$", string.Empty).Replace("€", string.Empty)
            .Replace("£", string.Empty).Replace("¥", string.Empty).Replace(",", string.Empty).Trim();
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    static bool HasLetters(string line)
    {
        foreach (var c in line)
            if (char.IsLetter(c))
                return true;
        return false;
    }

    static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
