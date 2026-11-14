
namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>The card network/brand, derived from the number's IIN/BIN prefix.</summary>
public enum CreditCardType
{
    /// <summary>Unrecognized or no number found.</summary>
    Unknown,
    Visa,
    Mastercard,
    Amex,
    Discover,
    DinersClub,
    JCB,
    UnionPay,
    Maestro
}


/// <summary>
/// A payment card read from the camera. All fields are nullable — only what was found is populated.
/// </summary>
/// <remarks>
/// Front-of-card OCR is best-effort; <see cref="Type"/> (and number validity) are derived deterministically
/// from the number via IIN prefix + Luhn. <see cref="Cvv"/> is on the back signature panel and is
/// PCI-sensitive, so it is almost always <c>null</c> from a front scan — handle it with care if you do
/// capture it.
/// </remarks>
/// <param name="Type">The card brand derived from the number (<see cref="CreditCardType.Unknown"/> if none).</param>
/// <param name="Number">The card number (digits only), when a Luhn-valid 13–19 digit number was found.</param>
/// <param name="Expiry">The expiry (first day of the expiry month), when an MM/YY was found.</param>
/// <param name="FirstName">Cardholder given name, when a name line was found.</param>
/// <param name="LastName">Cardholder family name, when a name line was found.</param>
/// <param name="CompanyName">Company/issuer name on corporate cards, when found.</param>
/// <param name="Cvv">Card verification value, when found (rare on a front scan; sensitive).</param>
/// <param name="Fields">All recognized fields as label/value pairs.</param>
public record CreditCard(
    CreditCardType Type,
    string? Number,
    DateOnly? Expiry,
    string? FirstName,
    string? LastName,
    string? CompanyName,
    string? Cvv,
    IReadOnlyList<DocumentField> Fields
);
