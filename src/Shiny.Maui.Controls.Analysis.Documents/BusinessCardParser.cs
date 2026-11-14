using System.Text.RegularExpressions;
using Microsoft.Maui.Graphics;

namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>
/// Best-effort rules parser that turns OCR text into a <see cref="BusinessCard"/>: the cardholder name and
/// job title, the company, the email(s) / phone(s) / website, and a best-effort mailing address. Email,
/// phone, and URL are matched deterministically; name / title / company are heuristic (a card has no fixed
/// layout). Swap in a custom <see cref="IDocumentParser{BusinessCard}"/> (rules, cloud Document AI, or an
/// LLM) for production accuracy.
/// </summary>
public partial class BusinessCardParser : IDocumentParser<BusinessCard>
{
    /// <summary>Color for the highlighted name box.</summary>
    public Color NameColor { get; set; } = Color.FromArgb("#3B82F6");

    /// <summary>Color for the company box.</summary>
    public Color CompanyColor { get; set; } = Color.FromArgb("#A78BFA");

    /// <summary>Color for the contact boxes (email / phone / website).</summary>
    public Color ContactColor { get; set; } = Color.FromArgb("#22C55E");

    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b")]
    private static partial Regex EmailRegex();

    // A phone run: optional +, then 7+ digits with the usual separators. Validated by digit count after.
    [GeneratedRegex(@"\+?\d[\d\s().\-/]{6,}\d")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"\b(?:https?://|www\.)[^\s,;]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    // A bare domain (no scheme/www) — e.g. "acme.com". Excludes email locals via the leading word boundary check.
    [GeneratedRegex(@"\b[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\.(?:com|net|org|io|co|dev|ai|app|biz|info|me|us|ca|uk|de|fr|au|eu)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BareDomainRegex();

    // A US/Canada ZIP/postal or a street address signal.
    [GeneratedRegex(@"\b\d{5}(?:-\d{4})?\b|\b[A-Za-z]\d[A-Za-z]\s?\d[A-Za-z]\d\b", RegexOptions.IgnoreCase)]
    private static partial Regex PostalRegex();

    [GeneratedRegex(@"^\s*\d+\s+\S", RegexOptions.IgnoreCase)]
    private static partial Regex StreetNumberRegex();

    public bool TryParse(
        IReadOnlyList<RecognizedText> text,
        out BusinessCard document,
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

        var emails = new List<string>();
        var phones = new List<BusinessCardPhone>();
        string? website = null;
        string? company = null;
        string? jobTitle = null;
        string? address = null;

        RecognizedText? nameLine = null;
        RecognizedText? companyLine = null;
        var contactBoxes = new List<RecognizedText>();
        var seenPhones = new HashSet<string>();

        foreach (var line in lines)
        {
            var t = line.Text.Trim();
            var lower = t.ToLowerInvariant();

            var isContact = false;

            // emails
            foreach (Match m in EmailRegex().Matches(t))
            {
                var email = m.Value.Trim('.', ',', ';');
                if (!emails.Contains(email, StringComparer.OrdinalIgnoreCase))
                    emails.Add(email);
                isContact = true;
            }

            // website (URL or bare domain) — but not the host part of an email line
            if (website == null && EmailRegex().Matches(t).Count == 0)
            {
                var url = UrlRegex().Match(t);
                if (url.Success)
                {
                    website = url.Value.Trim('.', ',', ';');
                    isContact = true;
                }
                else if (BareDomainRegex().Match(t) is { Success: true } bare)
                {
                    website = bare.Value;
                    isContact = true;
                }
            }

            // phones — skip lines that are really a website/email so a domain isn't read as digits
            if (EmailRegex().Matches(t).Count == 0 && UrlRegex().Match(t).Success == false)
            {
                foreach (Match m in PhoneRegex().Matches(t))
                {
                    var raw = m.Value.Trim();
                    var digits = CountDigits(raw);
                    if (digits is < 7 or > 15)
                        continue;

                    var key = new string([.. raw.Where(char.IsDigit)]);
                    if (!seenPhones.Add(key))
                        continue;

                    phones.Add(new BusinessCardPhone(raw, PhoneType(lower), line.BoundingBox));
                    isContact = true;
                }
            }

            // address — a street-number line or a line carrying a ZIP/postal code
            if (address == null && (StreetNumberRegex().IsMatch(t) || PostalRegex().IsMatch(t)) && HasLetters(t))
                address = t;

            // company — first line with a corporate suffix / keyword (and not already a contact line)
            if (company == null && !isContact && IsCompany(lower))
            {
                company = t;
                companyLine = line;
                continue;
            }

            // job title — first line that reads like a role
            if (jobTitle == null && !isContact && IsJobTitle(lower))
            {
                jobTitle = t;
                continue;
            }

            // name — first prominent text-only line at the top (letters, 2+ words, no digits / @)
            if (nameLine == null && !isContact && IsName(t) && !IsCompany(lower) && !IsJobTitle(lower))
            {
                nameLine = line;
                continue;
            }

            if (isContact)
                contactBoxes.Add(line);
        }

        // a card needs at least one real contact signal — a top line alone isn't enough
        if (emails.Count == 0 && phones.Count == 0 && website == null)
            return false;

        var name = nameLine?.Text.Trim();

        var fields = new List<DocumentField>();
        if (name != null) fields.Add(new DocumentField("Name", name, nameLine!.BoundingBox));
        if (jobTitle != null) fields.Add(new DocumentField("Title", jobTitle));
        if (company != null) fields.Add(new DocumentField("Company", company, companyLine?.BoundingBox));
        foreach (var email in emails)
            fields.Add(new DocumentField("Email", email));
        foreach (var phone in phones)
            fields.Add(new DocumentField(phone.Type ?? "Phone", phone.Number, phone.Bounds));
        if (website != null) fields.Add(new DocumentField("Website", website));
        if (address != null) fields.Add(new DocumentField("Address", address));

        document = new BusinessCard(name, jobTitle, company, emails, phones, website, address, fields);

        var overlay = new List<OverlayBox>(contactBoxes.Count + 2);
        if (nameLine is { } nl)
            overlay.Add(new OverlayBox(nl.BoundingBox, this.NameColor, name, this.NameColor));
        if (companyLine is { } cl)
            overlay.Add(new OverlayBox(cl.BoundingBox, this.CompanyColor));
        foreach (var contact in contactBoxes)
            overlay.Add(new OverlayBox(contact.BoundingBox, this.ContactColor));
        boxes = overlay;
        return true;
    }

    /// <inheritdoc/>
    public BusinessCard Merge(BusinessCard accumulated, BusinessCard incoming)
    {
        // a clearly different card (different name + no shared email) replaces the accumulation
        if (accumulated.Name is not null && incoming.Name is not null &&
            !string.Equals(accumulated.Name, incoming.Name, StringComparison.OrdinalIgnoreCase) &&
            !accumulated.Emails.Intersect(incoming.Emails, StringComparer.OrdinalIgnoreCase).Any())
            return incoming;

        return accumulated with
        {
            Name = accumulated.Name ?? incoming.Name,
            JobTitle = accumulated.JobTitle ?? incoming.JobTitle,
            Company = accumulated.Company ?? incoming.Company,
            Emails = Union(accumulated.Emails, incoming.Emails),
            Phones = UnionPhones(accumulated.Phones, incoming.Phones),
            Website = accumulated.Website ?? incoming.Website,
            Address = accumulated.Address ?? incoming.Address,
            Fields = DocumentMerge.Richer(accumulated.Fields, incoming.Fields)
        };
    }

    /// <inheritdoc/>
    public bool IsComplete(BusinessCard document)
        => document.Name is not null && (document.Emails.Count > 0 || document.Phones.Count > 0);

    static IReadOnlyList<string> Union(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (b.Count == 0) return a;
        var set = new List<string>(a);
        foreach (var x in b)
            if (!set.Contains(x, StringComparer.OrdinalIgnoreCase))
                set.Add(x);
        return set;
    }

    static IReadOnlyList<BusinessCardPhone> UnionPhones(IReadOnlyList<BusinessCardPhone> a, IReadOnlyList<BusinessCardPhone> b)
    {
        if (b.Count == 0) return a;
        var set = new List<BusinessCardPhone>(a);
        foreach (var x in b)
        {
            var xd = new string([.. x.Number.Where(char.IsDigit)]);
            if (!set.Any(y => new string([.. y.Number.Where(char.IsDigit)]) == xd))
                set.Add(x);
        }
        return set;
    }

    static string? PhoneType(string lower)
    {
        foreach (var (needle, label) in PhoneTypes)
            if (lower.Contains(needle))
                return label;
        return null;
    }

    static readonly (string Needle, string Label)[] PhoneTypes =
    [
        ("mobile", "Mobile"), ("cell", "Mobile"), ("cellular", "Mobile"),
        ("office", "Office"), ("work", "Office"), ("tel", "Office"), ("phone", "Office"),
        ("direct", "Direct"), ("fax", "Fax"), ("home", "Home"), ("toll", "Toll-free")
    ];

    static bool IsCompany(string lower)
    {
        foreach (var kw in CompanyKeywords)
            if (ContainsWord(lower, kw))
                return true;
        return false;
    }

    static readonly string[] CompanyKeywords =
    [
        "inc", "llc", "ltd", "corp", "corporation", "company", "gmbh", "plc", "llp", "pty",
        "group", "technologies", "technology", "solutions", "systems", "consulting", "studio",
        "studios", "labs", "agency", "associates", "partners", "holdings", "enterprises", "industries"
    ];

    static bool IsJobTitle(string lower)
    {
        foreach (var kw in JobTitleKeywords)
            if (ContainsWord(lower, kw))
                return true;
        return false;
    }

    static readonly string[] JobTitleKeywords =
    [
        "ceo", "cto", "cfo", "coo", "cmo", "cio", "president", "vp", "vice president", "director",
        "manager", "engineer", "developer", "designer", "founder", "co-founder", "owner", "consultant",
        "analyst", "specialist", "coordinator", "officer", "lead", "head", "architect", "administrator",
        "sales", "marketing", "account", "partner", "associate", "representative", "attorney",
        "accountant", "advisor", "strategist", "scientist", "principal", "supervisor", "executive"
    ];

    // A name line: 2+ words, mostly letters, no digit / @, not ALL-CAPS noise like a slogan.
    static bool IsName(string line)
    {
        if (line.Length is < 3 or > 48)
            return false;
        if (line.Contains('@') || line.Any(char.IsDigit))
            return false;

        var words = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is < 2 or > 5)
            return false;

        var letters = 0;
        var other = 0;
        foreach (var c in line)
        {
            if (char.IsLetter(c)) letters++;
            else if (c is not (' ' or '.' or '\'' or '-' or ',')) other++;
        }
        return letters >= 3 && other == 0;
    }

    static bool ContainsWord(string lower, string word)
    {
        var idx = lower.IndexOf(word, StringComparison.Ordinal);
        while (idx >= 0)
        {
            var before = idx == 0 || !char.IsLetterOrDigit(lower[idx - 1]);
            var afterIdx = idx + word.Length;
            var after = afterIdx >= lower.Length || !char.IsLetterOrDigit(lower[afterIdx]);
            if (before && after)
                return true;
            idx = lower.IndexOf(word, idx + 1, StringComparison.Ordinal);
        }
        return false;
    }

    static int CountDigits(string value)
    {
        var n = 0;
        foreach (var c in value)
            if (char.IsDigit(c))
                n++;
        return n;
    }

    static bool HasLetters(string line)
    {
        foreach (var c in line)
            if (char.IsLetter(c))
                return true;
        return false;
    }
}
