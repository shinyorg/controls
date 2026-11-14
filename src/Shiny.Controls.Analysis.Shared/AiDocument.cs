using System.Text.Json.Serialization;

namespace Shiny.Controls.Analysis;

/// <summary>
/// The built-in, schema-free payload produced by the default AI document scanner (MAUI <c>AiDocumentAnalyzer</c>
/// / Blazor <c>AiDocumentScanner</c>) — a best-effort classification plus a flat list of label/value fields the
/// model pulled out of the document. Use this when you don't want to define your own type; supply a
/// strongly-typed payload to the generic scanner when you do. The model fills these via MEAI structured output,
/// so keep the property names descriptive — they become the JSON schema the model sees.
/// </summary>
/// <param name="DocumentType">The model's guess at the kind of document (e.g. "Invoice", "Passport", "Receipt").</param>
/// <param name="Summary">A one-line, human-readable summary of the document.</param>
/// <param name="Fields">The extracted label/value pairs (e.g. "Total" → "$42.00", "Name" → "Jane Doe").</param>
public record AiDocument(
    string? DocumentType,
    string? Summary,
    IReadOnlyList<AiDocumentField> Fields
)
{
    /// <summary>An empty document (no type, no fields) — a safe default; the scanners never emit this.</summary>
    public static AiDocument Empty { get; } = new(null, null, []);
}


/// <summary>A single label/value pair extracted from a document by the model.</summary>
/// <param name="Label">The field name (e.g. "Invoice #", "Expiry", "Total").</param>
/// <param name="Value">The field value, or <c>null</c> when the model found the label but no value.</param>
public record AiDocumentField(string Label, string? Value);


/// <summary>
/// Source-generated JSON context for <see cref="AiDocument"/>, so the default scanner's structured-output call
/// is trim/AOT-safe without reflection-based serialization. Strongly-typed scanners using a custom payload type
/// should supply their own <see cref="System.Text.Json.JsonSerializerOptions"/> (built from a context for that
/// type).
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AiDocument))]
[JsonSerializable(typeof(AiDocumentField))]
public partial class AiDocumentJsonContext : JsonSerializerContext;
