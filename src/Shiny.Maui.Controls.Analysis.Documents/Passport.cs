
namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>Sex as encoded in the passport MRZ.</summary>
public enum PassportSex
{
    /// <summary>Unspecified or not found (MRZ <c>&lt;</c>).</summary>
    Unspecified,
    Male,
    Female
}


/// <summary>
/// A passport read from its Machine Readable Zone (MRZ). All fields are nullable — only what the MRZ yields
/// is populated. Parsed deterministically from the ICAO TD3 two-line MRZ, so it is far more reliable than
/// free-form OCR of the visual zone.
/// </summary>
/// <param name="Number">The passport number.</param>
/// <param name="Surname">The holder's family name.</param>
/// <param name="GivenNames">The holder's given names (space-separated).</param>
/// <param name="Nationality">3-letter nationality code (ICAO).</param>
/// <param name="IssuingCountry">3-letter issuing-state code (ICAO).</param>
/// <param name="DateOfBirth">Date of birth.</param>
/// <param name="Expiry">Expiry date.</param>
/// <param name="Sex">Sex (<see cref="PassportSex.Unspecified"/> when not encoded).</param>
/// <param name="Fields">All recognized fields as label/value pairs.</param>
public record Passport(
    string? Number,
    string? Surname,
    string? GivenNames,
    string? Nationality,
    string? IssuingCountry,
    DateOnly? DateOfBirth,
    DateOnly? Expiry,
    PassportSex Sex,
    IReadOnlyList<DocumentField> Fields
);
