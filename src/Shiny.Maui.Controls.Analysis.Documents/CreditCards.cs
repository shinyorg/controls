namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>
/// Deterministic, dependency-free helpers for payment card numbers: brand detection from the IIN/BIN prefix
/// and Luhn checksum validation. Pure and unit-testable.
/// </summary>
public static class CreditCards
{
    /// <summary>Validate a card number (digits only) with the Luhn (mod-10) checksum.</summary>
    public static bool IsValidNumber(string? digits)
    {
        if (string.IsNullOrEmpty(digits) || digits.Length is < 12 or > 19)
            return false;

        var sum = 0;
        var alt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            if (!char.IsDigit(digits[i]))
                return false;

            var d = digits[i] - '0';
            if (alt)
            {
                d *= 2;
                if (d > 9)
                    d -= 9;
            }
            sum += d;
            alt = !alt;
        }
        return sum % 10 == 0;
    }

    /// <summary>Detect the card brand from the leading digits of a card number.</summary>
    public static CreditCardType DetectType(string? digits)
    {
        if (string.IsNullOrEmpty(digits) || !char.IsDigit(digits[0]))
            return CreditCardType.Unknown;

        int Prefix(int len) => int.Parse(digits[..Math.Min(len, digits.Length)]);
        var p2 = digits.Length >= 2 ? Prefix(2) : -1;
        var p3 = digits.Length >= 3 ? Prefix(3) : -1;
        var p4 = digits.Length >= 4 ? Prefix(4) : -1;

        // order matters where ranges overlap (Discover/UnionPay sit inside the Maestro 56–69 band)
        if (p2 is 34 or 37) return CreditCardType.Amex;
        if (digits[0] == '4') return CreditCardType.Visa;
        if (p2 is >= 51 and <= 55 || p4 is >= 2221 and <= 2720) return CreditCardType.Mastercard;
        if (digits.StartsWith("6011") || p2 == 65 || p3 is >= 644 and <= 649) return CreditCardType.Discover;
        if (p4 is >= 3528 and <= 3589) return CreditCardType.JCB;
        if (p3 is >= 300 and <= 305 || p2 is 36 or 38 or 39) return CreditCardType.DinersClub;
        if (p2 == 62) return CreditCardType.UnionPay;
        if (p2 == 50 || p2 is >= 56 and <= 69) return CreditCardType.Maestro;
        return CreditCardType.Unknown;
    }
}
