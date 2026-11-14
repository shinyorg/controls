using System.Text.RegularExpressions;
using Microsoft.Maui.Graphics;

namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>
/// Best-effort rules parser for the front of a payment card: locates a Luhn-valid number (and derives the
/// brand via <see cref="CreditCards"/>), the MM/YY expiry, the cardholder name, and an optional company /
/// CVV. Heuristic — swap in a custom <see cref="IDocumentParser{CreditCard}"/> for a specific layout.
/// </summary>
public partial class CreditCardParser : IDocumentParser<CreditCard>
{
    /// <summary>Color for the highlighted number box.</summary>
    public Color NumberColor { get; set; } = Color.FromArgb("#22C55E");

    /// <summary>Color for the cardholder-name box.</summary>
    public Color NameColor { get; set; } = Color.FromArgb("#3B82F6");

    static readonly string[] NonNameKeywords =
        ["VALID", "THRU", "FROM", "MONTH", "GOOD", "MEMBER", "SINCE", "DEBIT", "CREDIT", "BANK", "CARD", "EXPIRES", "EXP"];

    [GeneratedRegex(@"\b(0[1-9]|1[0-2])\s*/\s*(\d{4}|\d{2})\b")]
    private static partial Regex ExpiryRegex();

    [GeneratedRegex(@"\b(?:CVV2?|CVC2?|CID)\b\s*[:#]?\s*(\d{3,4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex CvvRegex();

    [GeneratedRegex(@"^[A-Z][A-Z .'\-]{4,}$")]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"\b(?:INC|LLC|LTD|CORP|CO|GMBH|BANK)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CompanyRegex();

    public bool TryParse(
        IReadOnlyList<RecognizedText> text,
        out CreditCard document,
        out IReadOnlyList<OverlayBox> boxes)
    {
        document = null!;
        boxes = [];

        var lines = text
            .Where(t => !string.IsNullOrWhiteSpace(t.Text))
            .OrderBy(t => t.BoundingBox.Y)
            .ThenBy(t => t.BoundingBox.X)
            .ToList();
        if (lines.Count == 0)
            return false;

        string? number = null;
        RecognizedText? numberLine = null;
        DateOnly? expiry = null;
        string? cvv = null;
        string? company = null;
        string? name = null;
        RecognizedText? nameLine = null;

        foreach (var line in lines)
        {
            var t = line.Text.Trim();

            if (number == null)
            {
                var digits = new string(t.Where(char.IsDigit).ToArray());
                if (digits.Length is >= 13 and <= 19 && CreditCards.IsValidNumber(digits))
                {
                    number = digits;
                    numberLine = line;
                    continue;
                }
            }

            expiry ??= MatchExpiry(t);

            if (cvv == null && CvvRegex().Match(t) is { Success: true } cm)
                cvv = cm.Groups[1].Value;

            if (company == null && CompanyRegex().IsMatch(t))
                company = t;
            else if (name == null && IsNameLine(t))
            {
                name = t;
                nameLine = line;
            }
        }

        if (number == null)
            return false;

        var (first, last) = SplitName(name);
        var type = CreditCards.DetectType(number);

        var fields = new List<DocumentField>
        {
            new("Type", type.ToString()),
            new("Number", number)
        };
        if (expiry != null) fields.Add(new DocumentField("Expiry", expiry.Value.ToString("MM/yyyy")));
        if (first != null) fields.Add(new DocumentField("First Name", first));
        if (last != null) fields.Add(new DocumentField("Last Name", last));
        if (company != null) fields.Add(new DocumentField("Company", company));
        if (cvv != null) fields.Add(new DocumentField("CVV", cvv));

        document = new CreditCard(type, number, expiry, first, last, company, cvv, fields);

        var overlay = new List<OverlayBox>(2);
        if (numberLine is { } nl)
            overlay.Add(new OverlayBox(nl.BoundingBox, this.NumberColor, type.ToString(), this.NumberColor));
        if (nameLine is { } nm)
            overlay.Add(new OverlayBox(nm.BoundingBox, this.NameColor));
        boxes = overlay;
        return true;
    }

    /// <inheritdoc/>
    public CreditCard Merge(CreditCard accumulated, CreditCard incoming)
    {
        // a different card in view replaces the accumulation
        if (accumulated.Number is not null && incoming.Number is not null && accumulated.Number != incoming.Number)
            return incoming;

        return accumulated with
        {
            Type = accumulated.Type != CreditCardType.Unknown ? accumulated.Type : incoming.Type,
            Number = accumulated.Number ?? incoming.Number,
            Expiry = accumulated.Expiry ?? incoming.Expiry,
            FirstName = accumulated.FirstName ?? incoming.FirstName,
            LastName = accumulated.LastName ?? incoming.LastName,
            CompanyName = accumulated.CompanyName ?? incoming.CompanyName,
            Cvv = accumulated.Cvv ?? incoming.Cvv,
            Fields = DocumentMerge.Richer(accumulated.Fields, incoming.Fields)
        };
    }

    /// <inheritdoc/>
    public bool IsComplete(CreditCard document)
        => document.Number is not null && document.Expiry is not null;

    static DateOnly? MatchExpiry(string line)
    {
        var m = ExpiryRegex().Match(line);
        if (!m.Success)
            return null;

        var month = int.Parse(m.Groups[1].Value);
        var yearPart = m.Groups[2].Value;
        var year = yearPart.Length == 2 ? 2000 + int.Parse(yearPart) : int.Parse(yearPart);
        try
        {
            return new DateOnly(year, month, 1);
        }
        catch
        {
            return null;
        }
    }

    static bool IsNameLine(string line)
    {
        if (!line.Contains(' ') || !NameRegex().IsMatch(line))
            return false;
        foreach (var kw in NonNameKeywords)
            if (line.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    static (string? First, string? Last) SplitName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (null, null);

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => (null, null),
            1 => (parts[0], null),
            _ => (string.Join(' ', parts[..^1]), parts[^1])
        };
    }
}
