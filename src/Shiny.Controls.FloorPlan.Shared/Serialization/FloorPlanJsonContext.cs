using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shiny.Controls.FloorPlan;

/// <summary>
/// The source-generated metadata the serializer runs on.
/// </summary>
/// <remarks>
/// Generated rather than reflected because this package is trimmable and AOT-compatible, and
/// reflection-based <c>JsonSerializer</c> is neither. A trimmed app using the reflection path loses
/// the element subclasses first - the very types the polymorphic discriminators name - and the
/// failure shows up as an empty plan rather than as an exception.
/// </remarks>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(FloorPlanDocument))]
[JsonSerializable(typeof(Building))]
public partial class FloorPlanJsonContext : JsonSerializerContext
{
}
