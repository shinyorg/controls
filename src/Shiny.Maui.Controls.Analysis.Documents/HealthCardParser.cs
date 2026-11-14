using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Maui.Graphics;

namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>
/// Best-effort rules parser for Canadian health-insurance cards. Detects the issuing province from on-card
/// keywords, then extracts the member number using that province's format — Quebec/RAMQ (4 letters + 8
/// digits), Ontario/OHIP (10 digits + 2-letter version code), BC PHN, Alberta/AHCIP, etc. — plus a
/// cardholder name and expiry. When no province is recognized it falls back to the longest plausible digit
/// run. Heuristic — swap in a custom <see cref="IDocumentParser{HealthCard}"/> for a specific issuer's layout.
/// </summary>
public partial class HealthCardParser : IDocumentParser<HealthCard>
{
    /// <summary>Color for the highlighted member-number box.</summary>
    public Color NumberColor { get; set; } = Color.FromArgb("#EF4444");

    /// <summary>Color for the cardholder-name box.</summary>
    public Color NameColor { get; set; } = Color.FromArgb("#3B82F6");

    [GeneratedRegex(@"\b\d(?:[\d ]{6,})\d\b")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\b(\d{4}-\d{2}-\d{2}|\d{1,2}[/-]\d{1,2}[/-]\d{2,4})\b")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"^[A-Z][A-Za-z'.-]+,\s*[A-Z][A-Za-z'.\- ]+$")]
    private static partial Regex NameRegex();

    // Quebec/RAMQ: 4 letters (derived from the name) + 8 digits, e.g. "DOEJ 1234 5678".
    [GeneratedRegex(@"\b([A-Za-z]{4})[ ]?(\d{4})[ ]?(\d{4})\b")]
    private static partial Regex QuebecNumberRegex();

    // Ontario/OHIP: 10 digits grouped 4-3-3 with an optional 2-letter version code, e.g. "1234-567-890 AB".
    [GeneratedRegex(@"\b(\d{4})[- ]?(\d{3})[- ]?(\d{3})(?:[- ]?([A-Za-z]{2}))?\b")]
    private static partial Regex OntarioNumberRegex();

    /// <summary>A Canadian province's health plan, the keywords that identify it, and its member-number length(s).</summary>
    sealed record Province(string Code, string Name, string Plan, string[] Keywords, int[] DigitLengths);

    // Ordered most-distinctive-first; the first province whose keyword appears wins.
    static readonly Province[] Provinces =
    [
        new("QC", "Quebec", "RAMQ", ["ramq", "assurance maladie", "régie de l", "regie de l", "québec", "quebec"], [12]),
        new("ON", "Ontario", "OHIP", ["ohip", "serviceontario", "service ontario", "ontario"], [10]),
        new("BC", "British Columbia", "MSP", ["british columbia", "bc services", "carecard", "care card", "medical services plan"], [10]),
        new("AB", "Alberta", "AHCIP", ["ahcip", "alberta health", "alberta"], [9]),
        new("MB", "Manitoba", "Manitoba Health", ["manitoba"], [9]),
        new("SK", "Saskatchewan", "eHealth Saskatchewan", ["saskatchewan"], [9]),
        new("NS", "Nova Scotia", "MSI", ["nova scotia", "msi health"], [10]),
        new("NB", "New Brunswick", "Medicare", ["new brunswick", "nouveau-brunswick"], [9]),
        new("NL", "Newfoundland and Labrador", "MCP", ["newfoundland", "labrador", "mcp"], [12]),
        new("PE", "Prince Edward Island", "Health PEI", ["prince edward island", "health pei"], [8]),
    ];

    public bool TryParse(
        IReadOnlyList<RecognizedText> text,
        out HealthCard document,
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

        // Pass 1: detect province, name, expiry, issuer line, and gather number candidates.
        Province? province = null;
        RecognizedText? provinceLine = null;
        string? name = null;
        RecognizedText? nameLine = null;
        DateOnly? expiry = null;
        string? issuer = null;
        var candidates = new List<(string Digits, RecognizedText Line)>();

        foreach (var line in lines)
        {
            var t = line.Text.Trim();
            var lower = t.ToLowerInvariant();

            if (province == null && MatchProvince(lower) is { } p)
            {
                province = p;
                provinceLine = line;
            }

            if (name == null && NameRegex().IsMatch(t))
            {
                name = t;
                nameLine = line;
            }

            expiry ??= MatchDate(t);

            if (issuer == null && (lower.Contains("health") || lower.Contains("ministry") || lower.Contains("insurance")))
                issuer = t;

            foreach (Match m in NumberRegex().Matches(t))
            {
                var digits = m.Value.Replace(" ", string.Empty);
                if (digits.Length is >= 6 and <= 14 && digits.All(char.IsDigit))
                    candidates.Add((digits, line));
            }
        }

        // Resolve the member number using the province's format, else the generic heuristic.
        string? number = null;
        string? versionCode = null;
        RecognizedText? numberLine = null;

        if (province is { Code: "QC" })
        {
            foreach (var line in lines)
            {
                var m = QuebecNumberRegex().Match(line.Text.Trim());
                if (m.Success)
                {
                    number = $"{m.Groups[1].Value.ToUpperInvariant()} {m.Groups[2].Value} {m.Groups[3].Value}";
                    numberLine = line;
                    break;
                }
            }
        }
        else if (province is { Code: "ON" })
        {
            foreach (var line in lines)
            {
                var m = OntarioNumberRegex().Match(line.Text.Trim());
                if (m.Success)
                {
                    number = m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value;
                    if (m.Groups[4].Success)
                        versionCode = m.Groups[4].Value.ToUpperInvariant();
                    numberLine = line;
                    break;
                }
            }
        }

        if (number == null)
        {
            // province known → prefer a candidate of the expected length; else the longest 8..14 run.
            if (province != null)
            {
                foreach (var c in candidates)
                {
                    if (province.DigitLengths.Contains(c.Digits.Length))
                    {
                        number = c.Digits;
                        numberLine = c.Line;
                        break;
                    }
                }
            }

            if (number == null)
            {
                foreach (var c in candidates
                    .Where(c => c.Digits.Length is >= 8 and <= 14)
                    .OrderByDescending(c => c.Digits.Length))
                {
                    number = c.Digits;
                    numberLine = c.Line;
                    break;
                }
            }
        }

        if (number == null)
            return false;

        // Issuer fallback: the line that matched the province keyword, then the plan name.
        issuer ??= provinceLine?.Text.Trim() ?? province?.Plan;

        var fields = new List<DocumentField>
        {
            new("Member #", number)
        };
        if (versionCode != null) fields.Add(new DocumentField("Version code", versionCode));
        if (name != null) fields.Add(new DocumentField("Name", name));
        if (expiry != null) fields.Add(new DocumentField("Expiry", expiry.Value.ToString("yyyy-MM-dd")));
        if (province != null) fields.Add(new DocumentField("Province", province.Name));
        if (province != null) fields.Add(new DocumentField("Plan", province.Plan));
        if (issuer != null) fields.Add(new DocumentField("Issuer", issuer));

        document = new HealthCard(number, name, expiry, issuer, province?.Name, fields);

        var overlay = new List<OverlayBox>(2);
        if (numberLine is { } nl)
            overlay.Add(new OverlayBox(nl.BoundingBox, this.NumberColor, number, this.NumberColor));
        if (nameLine is { } nm)
            overlay.Add(new OverlayBox(nm.BoundingBox, this.NameColor));
        boxes = overlay;
        return true;
    }

    /// <inheritdoc/>
    public HealthCard Merge(HealthCard accumulated, HealthCard incoming)
    {
        // a different card in view replaces the accumulation
        if (accumulated.Number is not null && incoming.Number is not null && accumulated.Number != incoming.Number)
            return incoming;

        return accumulated with
        {
            Number = accumulated.Number ?? incoming.Number,
            Name = accumulated.Name ?? incoming.Name,
            Expiry = accumulated.Expiry ?? incoming.Expiry,
            Issuer = accumulated.Issuer ?? incoming.Issuer,
            Province = accumulated.Province ?? incoming.Province,
            Fields = DocumentMerge.Richer(accumulated.Fields, incoming.Fields)
        };
    }

    /// <inheritdoc/>
    public bool IsComplete(HealthCard document)
        => document.Number is not null;

    static Province? MatchProvince(string lower)
    {
        foreach (var p in Provinces)
            foreach (var kw in p.Keywords)
                if (lower.Contains(kw))
                    return p;
        return null;
    }

    static DateOnly? MatchDate(string line)
    {
        var m = DateRegex().Match(line);
        if (!m.Success)
            return null;

        foreach (var fmt in new[] { "yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy", "d/M/yyyy", "M-d-yyyy", "MM/dd/yy" })
        {
            if (DateOnly.TryParseExact(m.Value, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d;
        }
        return DateOnly.TryParse(m.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var any) ? any : null;
    }
}
