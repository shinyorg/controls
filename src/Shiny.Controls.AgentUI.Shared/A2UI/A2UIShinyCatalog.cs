using System.Text.Json.Nodes;

namespace Shiny.Controls.AgentUI.A2UI;

/// <summary>
/// The Shiny Controls catalog: the basic catalog's components and functions, plus Shiny components
/// (DataGrid, Gauge, ChipGroup, DateRangePicker, ...). One catalog id, so an agent can lay out a
/// surface with basic Rows and Columns and drop Shiny controls into it without per-component
/// <c>@catalogId</c>s.
/// </summary>
static class A2UIShinyCatalog
{
    static readonly Lazy<string> Json = new(() =>
    {
        using var stream = typeof(A2UIShinyCatalog).Assembly.GetManifestResourceStream("Shiny.Controls.AgentUI.A2UI.shiny_catalog.json")
                           ?? throw new InvalidOperationException("The Shiny catalog resource is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    /// <summary>The Shiny components only, as the embedded file declares them.</summary>
    public static IReadOnlyList<string> ComponentNames =>
        ((JsonObject)JsonNode.Parse(Json.Value)!)["components"]!.AsObject().Select(p => p.Key).ToList();

    public static A2UICatalog Create()
    {
        var basic = A2UIBasicCatalog.Create();
        var shiny = (JsonObject)JsonNode.Parse(Json.Value)!;

        // the published document is the basic catalog with the Shiny header and components merged in,
        // so an agent reads one self-contained schema
        var merged = (JsonObject)basic.Definition!.DeepClone();
        foreach (var key in (string[])["$id", "title", "description", "catalogId", "instructions"])
            merged[key] = shiny[key]?.DeepClone();

        var components = merged["components"]!.AsObject();
        var anyComponent = merged["$defs"]?["anyComponent"]?["oneOf"] as JsonArray;
        foreach (var (name, schema) in shiny["components"]!.AsObject())
        {
            components[name] = schema!.DeepClone();
            anyComponent?.Add((JsonNode)new JsonObject { ["$ref"] = "#/components/" + name });
        }

        var catalog = A2UICatalog.Parse(merged);
        foreach (var name in basic.Functions.Keys)
        {
            if (basic.TryGetImplementation(name, out var implementation))
                catalog.Implement(name, implementation);
        }
        return catalog;
    }
}
