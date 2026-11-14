
namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>
/// Parses the ICAO 9303 <b>TD3</b> Machine Readable Zone (the two 44-character lines at the bottom of a
/// passport) into a <see cref="Passport"/>. Deterministic and dependency-free — pure string parsing, fully
/// unit-testable.
/// </summary>
/// <remarks>
/// Line 1: <c>P&lt;ISSSURNAME&lt;&lt;GIVEN&lt;NAMES&lt;…</c> (type, issuing country, names).
/// Line 2: <c>NUMBER&lt;C NAT YYMMDD C S YYMMDD C PERSONAL C C</c> (number, nationality, DOB, sex, expiry).
/// </remarks>
public static class Mrz
{
    /// <summary>
    /// Attempt to parse a TD3 passport MRZ from its two lines. Returns <c>true</c> with
    /// <paramref name="passport"/> populated when line 1 is a passport ('P') record carrying at least a
    /// number or a name. <paramref name="time"/> supplies "today" for the birth-year century window;
    /// null means <see cref="TimeProvider.System"/>.
    /// </summary>
    public static bool TryParseTd3(string? line1, string? line2, out Passport passport, TimeProvider? time = null)
    {
        passport = null!;
        if (string.IsNullOrWhiteSpace(line1) || string.IsNullOrWhiteSpace(line2))
            return false;

        var l1 = Pad(Clean(line1));
        var l2 = Pad(Clean(line2));
        if (l1[0] != 'P')
            return false;

        var issuing = Letters(l1.Substring(2, 3));
        var (surname, given) = SplitNames(l1.Substring(5));

        var number = l2.Substring(0, 9).Replace("<", "").Trim();
        var nationality = Letters(l2.Substring(10, 3));
        var currentYear = (time ?? TimeProvider.System).GetLocalNow().Year;
        var dob = ParseDate(l2.Substring(13, 6), expiry: false, currentYear);
        var sex = ParseSex(l2[20]);
        var expiry = ParseDate(l2.Substring(21, 6), expiry: true, currentYear);

        if (number.Length == 0 && surname == null)
            return false;

        var fields = new List<DocumentField>();
        Add(fields, "Passport #", number);
        Add(fields, "Surname", surname);
        Add(fields, "Given Names", given);
        Add(fields, "Nationality", nationality);
        Add(fields, "Issuing Country", issuing);
        Add(fields, "Date of Birth", dob?.ToString("yyyy-MM-dd"));
        Add(fields, "Expiry", expiry?.ToString("yyyy-MM-dd"));
        if (sex != PassportSex.Unspecified)
            Add(fields, "Sex", sex.ToString());

        passport = new Passport(
            number.Length > 0 ? number : null,
            surname, given, nationality, issuing, dob, expiry, sex, fields);
        return true;
    }

    /// <summary>Whether a (cleaned) line looks like one MRZ line — TD3 alphabet, filler present, ~44 chars.</summary>
    internal static bool LooksLikeMrzLine(string cleaned)
        => cleaned.Length is >= 28 and <= 46 && cleaned.Contains('<') && cleaned.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '<');

    internal static string Clean(string s)
    {
        Span<char> buffer = stackalloc char[s.Length];
        var n = 0;
        foreach (var ch in s)
        {
            var c = char.ToUpperInvariant(ch);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '<')
                buffer[n++] = c;
        }
        return new string(buffer[..n]);
    }

    static string Pad(string s) => s.Length >= 44 ? s[..44] : s.PadRight(44, '<');

    static string? Letters(string s)
    {
        var letters = new string(s.Where(char.IsLetter).ToArray());
        return letters.Length == 0 ? null : letters;
    }

    static (string? Surname, string? Given) SplitNames(string names)
    {
        var split = names.IndexOf("<<", StringComparison.Ordinal);
        var surname = split >= 0 ? names[..split] : names;
        var given = split >= 0 ? names[(split + 2)..] : null;
        return (Normalize(surname), Normalize(given));
    }

    static string? Normalize(string? s)
    {
        if (s == null)
            return null;
        var words = s.Replace('<', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? null : string.Join(' ', words);
    }

    static PassportSex ParseSex(char c) => char.ToUpperInvariant(c) switch
    {
        'M' => PassportSex.Male,
        'F' => PassportSex.Female,
        _ => PassportSex.Unspecified
    };

    static DateOnly? ParseDate(string s, bool expiry, int currentYear)
    {
        if (s.Length != 6 || !s.All(char.IsDigit))
            return null;

        var yy = int.Parse(s[..2]);
        var mm = int.Parse(s.Substring(2, 2));
        var dd = int.Parse(s.Substring(4, 2));
        if (mm is < 1 or > 12 || dd is < 1 or > 31)
            return null;

        // MRZ carries no century. Expiry dates are in the 2000s; birth dates use a sliding window so they
        // land in the past.
        int year;
        if (expiry)
            year = 2000 + yy;
        else
        {
            var pivot = currentYear % 100;
            year = yy <= pivot ? 2000 + yy : 1900 + yy;
        }

        try
        {
            return new DateOnly(year, mm, dd);
        }
        catch
        {
            return null;
        }
    }

    static void Add(List<DocumentField> fields, string label, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            fields.Add(new DocumentField(label, value));
    }
}
