
namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>
/// Parses the AAMVA (American Association of Motor Vehicle Administrators) data string encoded in the
/// PDF417 barcode on the back of US/Canadian driver's licenses into a <see cref="DriversLicense"/>.
/// Deterministic and dependency-free — pure string parsing, fully unit-testable.
/// </summary>
/// <remarks>
/// AAMVA data is a set of newline-separated elements, each a 3-letter element id followed by its value
/// (e.g. <c>DAQ</c> = license number, <c>DCS</c> = family name, <c>DBB</c> = date of birth). Dates are 8
/// digits: <c>MMDDCCYY</c> in the USA and <c>CCYYMMDD</c> in Canada.
/// </remarks>
public static class AamvaParser
{
    /// <summary>
    /// Attempt to parse an AAMVA data string. Returns <c>true</c> with <paramref name="license"/> populated
    /// when the string is a recognizable AAMVA record carrying at least a number or a name.
    /// </summary>
    // Province/territory codes used by Canadian AAMVA jurisdictions (incl. legacy NF/PQ). Used both to surface
    // the province and to drive Canadian date order (CCYYMMDD) when the country element (DCG) is absent.
    static readonly HashSet<string> CanadianJurisdictions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ON", "QC", "BC", "AB", "MB", "SK", "NS", "NB", "NL", "PE", "YT", "NT", "NU", "NF", "PQ"
    };

    public static bool TryParse(string? raw, out DriversLicense license)
    {
        license = null!;
        // AAMVA 2000+ uses the "ANSI " file header; pre-2000 (and some Canadian) cards use "AAMVA".
        if (string.IsNullOrWhiteSpace(raw) ||
            (!raw.Contains("ANSI", StringComparison.Ordinal) && !raw.Contains("AAMVA", StringComparison.Ordinal)))
            return false;

        var country = Read(raw, "DCG");
        var number = Read(raw, "DAQ");
        var last = Read(raw, "DCS") ?? Read(raw, "DAB");
        var first = Read(raw, "DAC") ?? Read(raw, "DCT");
        var middle = Read(raw, "DAD");
        var jurisdiction = Read(raw, "DAJ");

        // Canada encodes dates as CCYYMMDD (vs USA MMDDCCYY). DCG is the authoritative signal, but many cards
        // omit it — fall back to the province code so Canadian licences still parse dates correctly.
        var canada =
            (country is not null &&
                (country.Equals("CAN", StringComparison.OrdinalIgnoreCase) ||
                 country.Equals("CANADA", StringComparison.OrdinalIgnoreCase))) ||
            (jurisdiction is not null && CanadianJurisdictions.Contains(jurisdiction));

        var dob = ParseDate(Read(raw, "DBB"), canada);
        var expiry = ParseDate(Read(raw, "DBA"), canada);
        var issue = ParseDate(Read(raw, "DBD"), canada);
        var address = JoinAddress(Read(raw, "DAG"), Read(raw, "DAI"), jurisdiction, Read(raw, "DAK"));

        if (number == null && last == null && first == null)
            return false;

        var fields = new List<DocumentField>();
        Add(fields, "License #", number);
        Add(fields, "First Name", first);
        Add(fields, "Middle Name", middle);
        Add(fields, "Last Name", last);
        Add(fields, "Date of Birth", dob?.ToString("yyyy-MM-dd"));
        Add(fields, "Expiry", expiry?.ToString("yyyy-MM-dd"));
        Add(fields, "Issued", issue?.ToString("yyyy-MM-dd"));
        Add(fields, "Address", address);
        Add(fields, canada ? "Province" : "Jurisdiction", jurisdiction);
        Add(fields, "Country", country ?? (canada ? "CAN" : null));

        license = new DriversLicense(number, first, last, dob, expiry, address, jurisdiction, fields);
        return true;
    }

    static void Add(List<DocumentField> fields, string label, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            fields.Add(new DocumentField(label, value));
    }

    // AAMVA elements appear as "<CODE><value>" terminated by a newline. The first data element is often
    // prefixed by the subfile designator ("DL"/"ID"); searching for the code substring handles both.
    static string? Read(string raw, string code)
    {
        var i = raw.IndexOf(code, StringComparison.Ordinal);
        while (i >= 0)
        {
            var start = i + code.Length;
            var end = raw.IndexOfAny(['\n', '\r'], start);
            if (end < 0)
                end = raw.Length;

            var value = raw[start..end].Trim();
            if (value.Length > 0)
                return value;

            i = raw.IndexOf(code, start, StringComparison.Ordinal);
        }
        return null;
    }

    static string? JoinAddress(string? street, string? city, string? state, string? postal)
    {
        var parts = new[] { street, city, state, postal }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim());
        var joined = string.Join(", ", parts);
        return joined.Length == 0 ? null : joined;
    }

    static DateOnly? ParseDate(string? s, bool canada)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        s = s.Trim();
        if (s.Length != 8 || !s.All(char.IsDigit))
            return null;

        // USA = MMDDCCYY, Canada = CCYYMMDD; when unknown, try US order then ISO order.
        return canada
            ? FromYmd(s) ?? FromMdy(s)
            : FromMdy(s) ?? FromYmd(s);
    }

    static DateOnly? FromMdy(string s) => Valid(int.Parse(s[4..8]), int.Parse(s[..2]), int.Parse(s[2..4]));
    static DateOnly? FromYmd(string s) => Valid(int.Parse(s[..4]), int.Parse(s[4..6]), int.Parse(s[6..8]));

    static DateOnly? Valid(int year, int month, int day)
    {
        if (month is < 1 or > 12 || day is < 1 or > 31 || year is < 1900 or > 2200)
            return null;
        try
        {
            return new DateOnly(year, month, day);
        }
        catch
        {
            return null;
        }
    }
}
