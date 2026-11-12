using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Shiny.Controls.AgentUI.A2UI;

/// <summary>Locale formatting for the basic catalog: Unicode TR35 date patterns, numbers, currency and CLDR plural categories.</summary>
public static class A2UIFormatting
{
    // ── dates (Unicode TR35) ─────────────────────────────────────────────────────

    /// <summary>
    /// Formats a date with a Unicode TR35 pattern (<c>yyyy-MM-dd</c>, <c>E MMM d, y h:mm a</c>) -
    /// the pattern language the agent writes, which differs from .NET's (E vs ddd, a vs tt).
    /// </summary>
    public static string FormatDate(DateTimeOffset value, string pattern, CultureInfo culture, TimeZoneInfo? zone = null)
    {
        var dtf = culture.DateTimeFormat;
        var local = value.DateTime;
        var sb = new StringBuilder();

        for (var i = 0; i < pattern.Length;)
        {
            var c = pattern[i];
            if (c == '\'')
            {
                // '' is a literal apostrophe; 'text' is literal text
                if (i + 1 < pattern.Length && pattern[i + 1] == '\'')
                {
                    sb.Append('\'');
                    i += 2;
                    continue;
                }
                var end = i + 1;
                while (end < pattern.Length)
                {
                    if (pattern[end] == '\'' && end + 1 < pattern.Length && pattern[end + 1] == '\'')
                    {
                        sb.Append('\'');
                        end += 2;
                        continue;
                    }
                    if (pattern[end] == '\'')
                        break;
                    sb.Append(pattern[end++]);
                }
                i = end + 1;
                continue;
            }

            if (!char.IsAsciiLetter(c))
            {
                sb.Append(c);
                i++;
                continue;
            }

            var n = 1;
            while (i + n < pattern.Length && pattern[i + n] == c)
                n++;
            i += n;

            switch (c)
            {
                case 'G':
                    var era = culture.Calendar.GetEra(local);
                    sb.Append(n >= 4 ? dtf.GetEraName(era) : dtf.GetAbbreviatedEraName(era));
                    break;

                case 'y' or 'u':
                    var year = culture.Calendar.GetYear(local);
                    sb.Append(n == 2 ? (year % 100).ToString("00", culture) : year.ToString(new string('0', n), culture));
                    break;

                case 'Y':
                    var weekYear = ISOWeek.GetYear(local);
                    sb.Append(n == 2 ? (weekYear % 100).ToString("00", culture) : weekYear.ToString(new string('0', n), culture));
                    break;

                case 'Q' or 'q':
                    var quarter = (local.Month - 1) / 3 + 1;
                    sb.Append(n switch { 1 => quarter.ToString(culture), 2 => quarter.ToString("00", culture), _ => "Q" + quarter.ToString(culture) });
                    break;

                case 'M' or 'L':
                    var month = culture.Calendar.GetMonth(local);
                    sb.Append(n switch
                    {
                        1 => month.ToString(culture),
                        2 => month.ToString("00", culture),
                        3 => c == 'M' ? dtf.AbbreviatedMonthGenitiveNames[month - 1] : dtf.AbbreviatedMonthNames[month - 1],
                        4 => c == 'M' ? dtf.MonthGenitiveNames[month - 1] : dtf.MonthNames[month - 1],
                        _ => First(dtf.MonthNames[month - 1])
                    });
                    break;

                case 'w':
                    sb.Append(ISOWeek.GetWeekOfYear(local).ToString(new string('0', n), culture));
                    break;

                case 'd':
                    sb.Append(culture.Calendar.GetDayOfMonth(local).ToString(new string('0', n), culture));
                    break;

                case 'D':
                    sb.Append(culture.Calendar.GetDayOfYear(local).ToString(new string('0', n), culture));
                    break;

                case 'E':
                    sb.Append(DayName(local.DayOfWeek, n, dtf));
                    break;

                case 'e' or 'c':
                    if (n <= 2)
                    {
                        var dayIndex = ((int)local.DayOfWeek - (int)dtf.FirstDayOfWeek + 7) % 7 + 1;
                        sb.Append(dayIndex.ToString(new string('0', n), culture));
                    }
                    else
                    {
                        sb.Append(DayName(local.DayOfWeek, n, dtf));
                    }
                    break;

                case 'a' or 'b' or 'B':
                    sb.Append(local.Hour < 12 ? dtf.AMDesignator : dtf.PMDesignator);
                    break;

                case 'h':
                    sb.Append((local.Hour % 12 == 0 ? 12 : local.Hour % 12).ToString(new string('0', n), culture));
                    break;

                case 'H':
                    sb.Append(local.Hour.ToString(new string('0', n), culture));
                    break;

                case 'K':
                    sb.Append((local.Hour % 12).ToString(new string('0', n), culture));
                    break;

                case 'k':
                    sb.Append((local.Hour == 0 ? 24 : local.Hour).ToString(new string('0', n), culture));
                    break;

                case 'm':
                    sb.Append(local.Minute.ToString(new string('0', n), culture));
                    break;

                case 's':
                    sb.Append(local.Second.ToString(new string('0', n), culture));
                    break;

                case 'S':
                    var fraction = (local.Ticks % TimeSpan.TicksPerSecond).ToString("0000000", CultureInfo.InvariantCulture);
                    sb.Append(n <= 7 ? fraction[..n] : fraction.PadRight(n, '0'));
                    break;

                case 'A':
                    sb.Append(((long)local.TimeOfDay.TotalMilliseconds).ToString(new string('0', n), culture));
                    break;

                case 'z':
                    if (n >= 4 && zone is not null)
                        sb.Append(zone.IsDaylightSavingTime(value) ? zone.DaylightName : zone.StandardName);
                    else
                        sb.Append(Gmt(value.Offset, longForm: false));
                    break;

                case 'O':
                    sb.Append(Gmt(value.Offset, longForm: n >= 4));
                    break;

                case 'Z':
                    sb.Append(n switch
                    {
                        <= 3 => Offset(value.Offset, colon: false, zulu: false),
                        4 => Gmt(value.Offset, longForm: true),
                        _ => Offset(value.Offset, colon: true, zulu: true)
                    });
                    break;

                case 'X' or 'x':
                    var zulu = c == 'X';
                    sb.Append(n switch
                    {
                        1 => value.Offset == TimeSpan.Zero && zulu ? "Z" : Offset(value.Offset, colon: false, zulu: false)[..3] + (value.Offset.Minutes != 0 ? Math.Abs(value.Offset.Minutes).ToString("00", CultureInfo.InvariantCulture) : ""),
                        2 or 4 => Offset(value.Offset, colon: false, zulu),
                        _ => Offset(value.Offset, colon: true, zulu)
                    });
                    break;

                case 'V' or 'v':
                    sb.Append(zone is null ? Gmt(value.Offset, longForm: false) : n >= 4 || c == 'V' ? zone.Id : zone.StandardName);
                    break;

                default:
                    // reserved letters print as themselves
                    sb.Append(c, n);
                    break;
            }
        }
        return sb.ToString();

        static string First(string s) => s.Length == 0 ? s : char.ConvertFromUtf32(char.ConvertToUtf32(s, 0));
    }

    static string DayName(DayOfWeek day, int n, DateTimeFormatInfo dtf) => n switch
    {
        <= 3 => dtf.AbbreviatedDayNames[(int)day],
        4 => dtf.DayNames[(int)day],
        5 => dtf.DayNames[(int)day][..1],
        _ => dtf.ShortestDayNames[(int)day]
    };

    static string Offset(TimeSpan offset, bool colon, bool zulu)
    {
        if (zulu && offset == TimeSpan.Zero)
            return "Z";
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        return sign + abs.Hours.ToString("00", CultureInfo.InvariantCulture) + (colon ? ":" : "") + abs.Minutes.ToString("00", CultureInfo.InvariantCulture);
    }

    static string Gmt(TimeSpan offset, bool longForm)
    {
        if (offset == TimeSpan.Zero)
            return "GMT";
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        return longForm || abs.Minutes != 0
            ? $"GMT{sign}{abs.Hours.ToString("00", CultureInfo.InvariantCulture)}:{abs.Minutes.ToString("00", CultureInfo.InvariantCulture)}"
            : $"GMT{sign}{abs.Hours.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>Parses a date value: an ISO 8601 string, or a number of milliseconds since the Unix epoch.</summary>
    public static bool TryParseDate(System.Text.Json.Nodes.JsonNode? node, TimeZoneInfo zone, out DateTimeOffset value)
    {
        if (A2UIValues.AsString(node) is { } s)
        {
            // a string with an offset is converted to the display zone; one without is wall time, shown as is
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces, out var parsed))
            {
                var hasOffset = s.EndsWith('Z') || s.EndsWith('z') || System.Text.RegularExpressions.Regex.IsMatch(s, @"[+-]\d\d:?\d\d$");
                value = hasOffset ? TimeZoneInfo.ConvertTime(parsed, zone) : new DateTimeOffset(parsed.DateTime, zone.GetUtcOffset(parsed.DateTime));
                return true;
            }
        }
        else if (A2UIValues.ToNumber(node) is { } ms && node is System.Text.Json.Nodes.JsonValue)
        {
            value = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds((long)ms), zone);
            return true;
        }
        value = default;
        return false;
    }

    // ── numbers & currency ───────────────────────────────────────────────────────

    /// <summary><c>formatNumber</c>: locale grouping (unless turned off) and, when given, exactly <paramref name="decimals"/> fraction digits; otherwise up to three.</summary>
    public static string FormatNumber(double value, int? decimals, bool grouping, CultureInfo culture)
    {
        var nfi = (NumberFormatInfo)culture.NumberFormat.Clone();
        if (!grouping)
            nfi.NumberGroupSeparator = "";
        // Intl.NumberFormat rounds half away from zero; .NET's formatter doesn't, so round first
        var digits = Math.Clamp(decimals ?? 3, 0, 15);
        var rounded = Math.Round(value, digits, MidpointRounding.AwayFromZero);
        return decimals is not null
            ? rounded.ToString("N" + digits.ToString(CultureInfo.InvariantCulture), nfi)
            : rounded.ToString("#,##0.###", nfi);
    }

    static readonly ConcurrentDictionary<string, string> CurrencySymbols = new(StringComparer.OrdinalIgnoreCase);

    // ISO 4217 minor units that aren't 2
    static readonly Dictionary<string, int> MinorUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BIF"] = 0, ["CLP"] = 0, ["DJF"] = 0, ["GNF"] = 0, ["ISK"] = 0, ["JPY"] = 0, ["KMF"] = 0, ["KRW"] = 0,
        ["PYG"] = 0, ["RWF"] = 0, ["UGX"] = 0, ["UYI"] = 0, ["VND"] = 0, ["VUV"] = 0, ["XAF"] = 0, ["XOF"] = 0, ["XPF"] = 0,
        ["BHD"] = 3, ["IQD"] = 3, ["JOD"] = 3, ["KWD"] = 3, ["LYD"] = 3, ["OMR"] = 3, ["TND"] = 3
    };

    /// <summary><c>formatCurrency</c>: the culture's currency pattern with the ISO 4217 code's symbol and minor units.</summary>
    public static string FormatCurrency(double value, string currency, int? decimals, bool grouping, CultureInfo culture)
    {
        var nfi = (NumberFormatInfo)culture.NumberFormat.Clone();
        nfi.CurrencySymbol = CurrencySymbol(currency, culture);
        nfi.CurrencyDecimalDigits = Math.Clamp(decimals ?? MinorUnits.GetValueOrDefault(currency, 2), 0, 15);
        if (!grouping)
            nfi.CurrencyGroupSeparator = "";
        return Math.Round(value, nfi.CurrencyDecimalDigits, MidpointRounding.AwayFromZero).ToString("C", nfi);
    }

    static string CurrencySymbol(string code, CultureInfo culture)
    {
        if (culture.Name.Length > 0)
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                if (region.ISOCurrencySymbol.Equals(code, StringComparison.OrdinalIgnoreCase))
                    return culture.NumberFormat.CurrencySymbol;
            }
            catch (ArgumentException) { }
        }

        return CurrencySymbols.GetOrAdd(code + "|" + culture.TwoLetterISOLanguageName, _ =>
        {
            // prefer a culture in the reader's language that uses the currency, then any culture
            CultureInfo[] all;
            try
            {
                all = CultureInfo.GetCultures(CultureTypes.SpecificCultures);
            }
            catch (Exception)
            {
                return code.ToUpperInvariant();
            }

            string? fallback = null;
            foreach (var c in all)
            {
                try
                {
                    if (!new RegionInfo(c.Name).ISOCurrencySymbol.Equals(code, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (c.TwoLetterISOLanguageName == culture.TwoLetterISOLanguageName)
                        return c.NumberFormat.CurrencySymbol;
                    fallback ??= c.NumberFormat.CurrencySymbol;
                }
                catch (ArgumentException) { }
            }
            return fallback ?? code.ToUpperInvariant();
        });
    }

    // ── plurals (CLDR cardinal rules) ────────────────────────────────────────────

    /// <summary>The CLDR plural category (zero, one, two, few, many, other) of a number in a language.</summary>
    public static string PluralCategory(double number, CultureInfo culture)
    {
        var n = Math.Abs(number);
        var text = n.ToString("0.###############", CultureInfo.InvariantCulture);
        var dot = text.IndexOf('.');
        var fraction = dot < 0 ? "" : text[(dot + 1)..];
        var i = (long)Math.Floor(n);
        var v = fraction.Length;                                     // visible fraction digits
        var f = v == 0 ? 0 : long.Parse(fraction, CultureInfo.InvariantCulture);
        var i10 = i % 10;
        var i100 = i % 100;
        var isInt = v == 0;

        return culture.TwoLetterISOLanguageName switch
        {
            "ja" or "zh" or "ko" or "th" or "vi" or "id" or "ms" or "lo" or "my" or "km" or "bo" or "dz" or "jv" or "yo" or "ig" or "wo" or "to" or "sg" => "other",

            "fr" or "pt" when culture.Name != "pt-PT" => i is 0 or 1 ? "one" : "other",
            "hi" or "bn" or "gu" or "kn" or "fa" or "am" or "zu" or "as" => i == 0 || n == 1 ? "one" : "other",

            "ru" or "uk" or "be" => !isInt ? "other"
                : i10 == 1 && i100 != 11 ? "one"
                : i10 is >= 2 and <= 4 && i100 is not (>= 12 and <= 14) ? "few"
                : "many",

            "pl" => !isInt ? "other"
                : i == 1 ? "one"
                : i10 is >= 2 and <= 4 && i100 is not (>= 12 and <= 14) ? "few"
                : "many",

            "cs" or "sk" => !isInt ? "many" : i == 1 ? "one" : i is >= 2 and <= 4 ? "few" : "other",

            "hr" or "sr" or "bs" => (isInt && i10 == 1 && i100 != 11) || (f % 10 == 1 && f % 100 != 11) ? "one"
                : (isInt && i10 is >= 2 and <= 4 && i100 is not (>= 12 and <= 14)) || (f % 10 is >= 2 and <= 4 && f % 100 is not (>= 12 and <= 14)) ? "few"
                : "other",

            "sl" => !isInt ? "few" : i100 == 1 ? "one" : i100 == 2 ? "two" : i100 is 3 or 4 ? "few" : "other",

            "lt" => !isInt ? "many"
                : i10 == 1 && i100 is not (>= 11 and <= 19) ? "one"
                : i10 is >= 2 and <= 9 && i100 is not (>= 11 and <= 19) ? "few"
                : "other",

            "lv" => (isInt && (i10 == 0 || i100 is >= 11 and <= 19)) ? "zero"
                : isInt && i10 == 1 && i100 != 11 ? "one"
                : "other",

            "ro" => isInt && i == 1 ? "one" : !isInt || n == 0 || (n != 1 && i100 is >= 1 and <= 19) ? "few" : "other",

            "he" or "iw" => isInt && i == 1 ? "one" : isInt && i == 2 ? "two" : "other",

            "ar" => !isInt ? "other"
                : n == 0 ? "zero" : n == 1 ? "one" : n == 2 ? "two"
                : i100 is >= 3 and <= 10 ? "few"
                : i100 is >= 11 and <= 99 ? "many"
                : "other",

            "ga" => n == 1 ? "one" : n == 2 ? "two" : isInt && n is >= 3 and <= 6 ? "few" : isInt && n is >= 7 and <= 10 ? "many" : "other",

            "cy" => n == 0 ? "zero" : n == 1 ? "one" : n == 2 ? "two" : n == 3 ? "few" : n == 6 ? "many" : "other",

            "da" => n == 1 || (!isInt && i is 0 or 1) ? "one" : "other",

            "is" => (isInt && i10 == 1 && i100 != 11) || !isInt ? "one" : "other",

            // English and the many languages that share "one" for exactly 1
            _ => isInt && i == 1 ? "one" : "other"
        };
    }
}
