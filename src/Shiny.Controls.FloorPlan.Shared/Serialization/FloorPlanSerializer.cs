using System.Text.Json;

namespace Shiny.Controls.FloorPlan;

/// <summary>
/// Reads and writes plans as JSON.
/// </summary>
/// <remarks>
/// Nulls are dropped and nothing else is. The obvious economy - dropping every default as well -
/// is a trap here: <c>IsVisible</c> and <c>Opacity</c> both default to the *non*-default CLR value,
/// so a hidden element would serialize to nothing and come back visible.
/// </remarks>
public static class FloorPlanSerializer
{
    public static string SerializeDocument(FloorPlanDocument doc) =>
        JsonSerializer.Serialize(doc, FloorPlanJsonContext.Default.FloorPlanDocument);

    public static FloorPlanDocument? DeserializeDocument(string json) =>
        JsonSerializer.Deserialize(json, FloorPlanJsonContext.Default.FloorPlanDocument);

    public static string SerializeBuilding(Building building) =>
        JsonSerializer.Serialize(building, FloorPlanJsonContext.Default.Building);

    public static Building? DeserializeBuilding(string json) =>
        JsonSerializer.Deserialize(json, FloorPlanJsonContext.Default.Building);
}
