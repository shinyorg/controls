
namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>
/// A driver's license parsed from the AAMVA PDF417 barcode on the back of North American licenses.
/// Strongly-typed common fields plus a <see cref="Fields"/> bag of everything the parser recognized.
/// </summary>
/// <param name="Number">The driver's license / ID number (AAMVA element DAQ).</param>
/// <param name="FirstName">Given name (DAC/DCT).</param>
/// <param name="LastName">Family name (DCS/DAB).</param>
/// <param name="DateOfBirth">Date of birth (DBB).</param>
/// <param name="Expiry">Expiry date (DBA).</param>
/// <param name="Address">Combined mailing address (DAG/DAI/DAJ/DAK), when present.</param>
/// <param name="Jurisdiction">The issuing jurisdiction code (DAJ) — a Canadian province (e.g. "ON", "QC", "BC") or US state, when present.</param>
/// <param name="Fields">All recognized fields as label/value pairs.</param>
public record DriversLicense(
    string? Number,
    string? FirstName,
    string? LastName,
    DateOnly? DateOfBirth,
    DateOnly? Expiry,
    string? Address,
    string? Jurisdiction,
    IReadOnlyList<DocumentField> Fields
);
