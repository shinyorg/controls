using System.Text.Json.Nodes;

namespace Shiny.Controls.AgentUI.A2UI;

/// <summary>Who may invoke a catalog function.</summary>
public enum A2UIAllowedCallers
{
    /// <summary>Only the renderer's own bindings, checks and actions (the default).</summary>
    RendererOnly,
    /// <summary>Only the agent, through <c>callRendererFunction</c>. The renderer never evaluates it in a binding.</summary>
    AgentOnly,
    /// <summary>Both the renderer and the agent.</summary>
    RendererOrAgent
}

/// <summary>How a component property references child components.</summary>
public enum A2UIChildPropertyKind
{
    /// <summary>A single child id (<c>Child</c>).</summary>
    Single,
    /// <summary>A static id list or a template (<c>ChildList</c>).</summary>
    List,
    /// <summary>An array of objects that each hold a child id (Tabs' <c>tabs[].child</c>).</summary>
    ItemChild
}

/// <summary>A component property that references children, read off the catalog schema.</summary>
/// <param name="Name">The property name.</param>
/// <param name="Kind">How it references them.</param>
/// <param name="ItemProperty">For <see cref="A2UIChildPropertyKind.ItemChild"/>, the member of each item that holds the id.</param>
public sealed record A2UIChildProperty(string Name, A2UIChildPropertyKind Kind, string? ItemProperty = null);

/// <summary>A component type from a catalog.</summary>
public sealed record A2UIComponentDefinition(
    string Name,
    IReadOnlyList<string> Properties,
    IReadOnlyList<string> RequiredProperties,
    IReadOnlyList<A2UIChildProperty> ChildProperties,
    IReadOnlyList<string>? AllowedParents,
    IReadOnlyList<string>? AllowedChildren,
    bool IsCheckable,
    JsonObject? Schema = null);

/// <summary>A function from a catalog.</summary>
public sealed record A2UIFunctionDefinition(
    string Name,
    string? ReturnType,
    A2UIAllowedCallers AllowedCallers,
    bool RequiresUserActivation,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> RequiredArguments,
    bool AllowsAdditionalArguments,
    JsonObject? Schema = null);

/// <summary>A renderer-side function implementation.</summary>
public delegate JsonNode? A2UIFunction(A2UIFunctionContext context);

/// <summary>
/// An A2UI catalog: the component types and functions a surface may use. Parsed from the catalog
/// JSON schema itself, so the definitions the agent reads and the ones the renderer enforces are
/// the same document.
/// </summary>
public sealed class A2UICatalog
{
    /// <summary>The A2UI v1.0 basic catalog's id.</summary>
    public const string BasicCatalogId = "https://a2ui.org/specification/v1_0/catalogs/basic/catalog.json";

    /// <summary>The Shiny Controls catalog's id (the basic catalog plus Shiny components).</summary>
    public const string ShinyCatalogId = "https://shinylib.net/a2ui/shiny.controls/v1.json";

    readonly Dictionary<string, A2UIComponentDefinition> components = new(StringComparer.Ordinal);
    readonly Dictionary<string, A2UIFunctionDefinition> functions = new(StringComparer.Ordinal);
    readonly Dictionary<string, A2UIFunction> implementations = new(StringComparer.Ordinal);

    /// <summary>Creates an empty catalog to build up in code; use <see cref="Parse"/> for a catalog JSON.</summary>
    public A2UICatalog(string catalogId, JsonObject? definition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogId);
        CatalogId = catalogId;
        Definition = definition;
    }

    /// <summary>The catalog's id, as surfaces and components name it.</summary>
    public string CatalogId { get; }

    /// <summary>The catalog JSON this was parsed from, when it was.</summary>
    public JsonObject? Definition { get; }

    /// <summary>The component types, by name.</summary>
    public IReadOnlyDictionary<string, A2UIComponentDefinition> Components => components;

    /// <summary>The functions, by name.</summary>
    public IReadOnlyDictionary<string, A2UIFunctionDefinition> Functions => functions;

    /// <summary>Adds (or replaces) a component type.</summary>
    public A2UICatalog AddComponent(A2UIComponentDefinition definition)
    {
        components[definition.Name] = definition;
        return this;
    }

    /// <summary>Adds (or replaces) a function, optionally with its renderer-side implementation.</summary>
    public A2UICatalog AddFunction(A2UIFunctionDefinition definition, A2UIFunction? implementation = null)
    {
        functions[definition.Name] = definition;
        if (implementation is not null)
            implementations[definition.Name] = implementation;
        return this;
    }

    /// <summary>
    /// Supplies (or replaces) the renderer-side implementation of a declared function. A declared
    /// function with no implementation is routed to the agent (<c>callAgentFunction</c>).
    /// </summary>
    public A2UICatalog Implement(string name, A2UIFunction implementation)
    {
        if (!functions.ContainsKey(name))
            throw new ArgumentException($"Catalog '{CatalogId}' declares no function '{name}'.", nameof(name));
        implementations[name] = implementation;
        return this;
    }

    /// <summary>The renderer-side implementation of a function, when there is one.</summary>
    public bool TryGetImplementation(string name, out A2UIFunction implementation)
        => implementations.TryGetValue(name, out implementation!);

    /// <summary>A fresh copy of the basic catalog with every function implemented. Fresh, so an app can extend it.</summary>
    public static A2UICatalog CreateBasic() => A2UIBasicCatalog.Create();

    /// <summary>
    /// A fresh copy of the Shiny Controls catalog: every basic component and function plus DataGrid,
    /// Gauge, ProgressBar, Pill, Avatar, ChipGroup, TagEntry, DateRangePicker, RangeSlider, Timeline,
    /// Expander and Markdown. <see cref="Definition"/> is the self-contained schema to give the agent.
    /// </summary>
    public static A2UICatalog CreateShiny() => A2UIShinyCatalog.Create();

    /// <summary>The component names the Shiny catalog adds to the basic catalog.</summary>
    public static IReadOnlyList<string> ShinyComponentNames => A2UIShinyCatalog.ComponentNames;

    /// <summary>Parses a catalog definition (catalog_definition.json).</summary>
    public static A2UICatalog Parse(JsonObject json)
    {
        var id = A2UIValues.AsString(json["catalogId"])
                 ?? throw new FormatException("A catalog needs a 'catalogId'.");
        var catalog = new A2UICatalog(id, json);
        var defs = json["$defs"] as JsonObject;

        if (json["components"] is JsonObject comps)
        {
            foreach (var (name, schema) in comps)
            {
                if (schema is JsonObject s)
                    catalog.AddComponent(ParseComponent(name, s, defs));
            }
        }

        if (json["functions"] is JsonObject fns)
        {
            foreach (var (name, schema) in fns)
            {
                if (schema is JsonObject s)
                    catalog.AddFunction(ParseFunction(name, s));
            }
        }
        return catalog;
    }

    static A2UIComponentDefinition ParseComponent(string name, JsonObject schema, JsonObject? defs)
    {
        var props = new List<string> { "id", "component", "@catalogId", "@accessibility", "@metadata" };
        var required = new List<string>();
        var children = new List<A2UIChildProperty>();
        var checkable = false;
        Walk(schema, 0);

        return new A2UIComponentDefinition(
            name,
            props.Distinct().ToList(),
            required.Where(r => r is not "component" and not "id").Distinct().ToList(),
            children,
            StringList(schema["allowedParents"]),
            StringList(schema["allowedChildren"]),
            checkable,
            schema);

        void Walk(JsonObject s, int depth)
        {
            if (depth > 8)
                return;

            if (A2UIValues.AsString(s["$ref"]) is { } r)
            {
                if (r.EndsWith("/Checkable", StringComparison.Ordinal))
                {
                    checkable = true;
                    props.Add("@checks");
                }
                else if (r.StartsWith("#/$defs/", StringComparison.Ordinal) && defs?[r["#/$defs/".Length..]] is JsonObject local)
                {
                    Walk(local, depth + 1);
                }
            }

            foreach (var key in (string[])["allOf", "anyOf", "oneOf"])
            {
                if (s[key] is JsonArray parts)
                {
                    foreach (var p in parts.OfType<JsonObject>())
                        Walk(p, depth + 1);
                }
            }

            if (s["required"] is JsonArray req)
                required.AddRange(req.Select(A2UIValues.AsString).OfType<string>());

            if (s["properties"] is JsonObject properties)
            {
                foreach (var (prop, propSchema) in properties)
                {
                    props.Add(prop);
                    if (propSchema is JsonObject ps && ChildKind(ps, out var kind, out var item))
                        children.Add(new A2UIChildProperty(prop, kind, item));
                }
            }
        }
    }

    static bool ChildKind(JsonObject propSchema, out A2UIChildPropertyKind kind, out string? itemProperty)
    {
        itemProperty = null;
        var r = A2UIValues.AsString(propSchema["$ref"]);
        if (r is not null && r.EndsWith("/Child", StringComparison.Ordinal))
        {
            kind = A2UIChildPropertyKind.Single;
            return true;
        }
        if (r is not null && r.EndsWith("/ChildList", StringComparison.Ordinal))
        {
            kind = A2UIChildPropertyKind.List;
            return true;
        }
        if (propSchema["items"] is JsonObject items && items["properties"] is JsonObject itemProps)
        {
            foreach (var (name, s) in itemProps)
            {
                if (s is JsonObject o && A2UIValues.AsString(o["$ref"]) is { } ir && ir.EndsWith("/Child", StringComparison.Ordinal))
                {
                    kind = A2UIChildPropertyKind.ItemChild;
                    itemProperty = name;
                    return true;
                }
            }
        }
        kind = default;
        return false;
    }

    static A2UIFunctionDefinition ParseFunction(string name, JsonObject schema)
    {
        var args = schema["properties"]?["args"] as JsonObject;
        var argProps = (args?["properties"] as JsonObject)?.Select(p => p.Key).ToList() ?? [];
        var required = StringList(args?["required"]) ?? [];
        var closed = args?["unevaluatedProperties"] is JsonValue u && u.GetValueKind() == System.Text.Json.JsonValueKind.False
                     || args?["additionalProperties"] is JsonValue a && a.GetValueKind() == System.Text.Json.JsonValueKind.False;

        var callers = A2UIValues.AsString(schema["allowedCallers"]) switch
        {
            "agentOnly" => A2UIAllowedCallers.AgentOnly,
            "rendererOrAgent" => A2UIAllowedCallers.RendererOrAgent,
            _ => A2UIAllowedCallers.RendererOnly
        };

        return new A2UIFunctionDefinition(
            name,
            A2UIValues.AsString(schema["returnType"]),
            callers,
            A2UIValues.ToBoolean(schema["requiresUserActivation"]),
            argProps,
            required,
            !closed,
            schema);
    }

    static List<string>? StringList(JsonNode? node) =>
        node is JsonArray a ? a.Select(A2UIValues.AsString).OfType<string>().ToList() : null;
}

/// <summary>The catalogs a renderer supports, advertised to the agent as <c>supportedCatalogIds</c>.</summary>
public sealed class A2UICatalogRegistry
{
    readonly Dictionary<string, A2UICatalog> catalogs = new(StringComparer.Ordinal);

    /// <param name="includeBasic">Register the basic catalog (default).</param>
    public A2UICatalogRegistry(bool includeBasic = true)
    {
        if (includeBasic)
            Add(A2UICatalog.CreateBasic());
    }

    /// <summary>Adds (or replaces) a catalog.</summary>
    public A2UICatalogRegistry Add(A2UICatalog catalog)
    {
        catalogs[catalog.CatalogId] = catalog;
        return this;
    }

    /// <summary>Looks a catalog up by id.</summary>
    public bool TryGet(string catalogId, out A2UICatalog catalog) => catalogs.TryGetValue(catalogId, out catalog!);

    /// <summary>Every registered catalog's id.</summary>
    public IReadOnlyList<string> SupportedCatalogIds => catalogs.Keys.ToList();

    /// <summary>The registered catalogs.</summary>
    public IEnumerable<A2UICatalog> Catalogs => catalogs.Values;
}
