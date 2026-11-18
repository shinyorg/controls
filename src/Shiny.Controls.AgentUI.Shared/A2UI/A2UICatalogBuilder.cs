using System.Text.Json.Nodes;

namespace Shiny.Controls.AgentUI.A2UI;

/// <summary>What a catalog function returns, as the catalog schema declares it.</summary>
public enum A2UIReturnType
{
    /// <summary>A string.</summary>
    String,
    /// <summary>A number.</summary>
    Number,
    /// <summary>A boolean.</summary>
    Boolean,
    /// <summary>Nothing — a function called for its effect, such as <c>openUrl</c>.</summary>
    Void,
    /// <summary>A check result, for functions used in <c>checks</c>.</summary>
    ValidationResult
}

/// <summary>
/// Builds an A2UI catalog in C#: components and functions described with typed calls, never JSON
/// text. <see cref="Build"/> produces the catalog schema — the document you give the agent, in
/// <see cref="A2UICatalog.Definition"/> — and parses it with <see cref="A2UICatalog.Parse"/>, so what the
/// agent reads and what the engine enforces are the same document.
/// </summary>
/// <example>
/// <code language="csharp">
/// var catalog = A2UICatalogBuilder
///     .ExtendShiny("https://example.com/a2ui/my-app/v1.json")
///     .Instructions("Use Rating whenever you ask the user to score something.")
///     .Component("Rating", "A row of stars the user can tap to score something.", c => c
///         .Number("value", "The score, 0 to max. Bind it to receive the user's choice.", required: true)
///         .Number("max", "How many stars.", dynamic: false, defaultValue: 5, minimum: 1)
///         .Action("action", "Sent when the user picks a score."))
///     .Build();
/// </code>
/// </example>
/// <remarks>
/// <para><b>Start from Shiny or basic.</b> A surface names one catalog, so a catalog that only holds
/// your components leaves the agent without Rows, Columns and Text. <see cref="ExtendShiny"/> and
/// <see cref="ExtendBasic"/> start from a copy of those catalogs — every component, every function and
/// its implementation — under your id, the way the Shiny catalog itself extends the basic one.</para>
/// <para>Every component gets the <c>weight</c> property, like the built-in ones, unless
/// <see cref="A2UIComponentBuilder.NoWeight"/> says otherwise.</para>
/// </remarks>
public sealed partial class A2UICatalogBuilder
{
    const string Common = "common_types.json#/$defs/";

    readonly JsonObject document;
    readonly Dictionary<string, A2UIFunction> implementations = new(StringComparer.Ordinal);

    A2UICatalogBuilder(JsonObject document, A2UICatalog? source)
    {
        this.document = document;

        if (source is not null)
        {
            foreach (var name in source.Functions.Keys)
            {
                if (source.TryGetImplementation(name, out var implementation))
                    implementations[name] = implementation;
            }
        }
    }

    /// <summary>A catalog holding only what you add.</summary>
    public static A2UICatalogBuilder Create(string catalogId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogId);

        var document = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = catalogId,
            ["catalogId"] = catalogId,
            ["components"] = new JsonObject(),
            ["functions"] = new JsonObject(),
            ["$defs"] = new JsonObject
            {
                ["anyComponent"] = new JsonObject { ["oneOf"] = new JsonArray() },
                ["anyFunction"] = new JsonObject { ["oneOf"] = new JsonArray() },
                ["Weight"] = new JsonObject
                {
                    ["type"] = "number",
                    ["description"] = "The relative weight of this component within a Row or Column, like CSS 'flex-grow'. Only for a direct child of a Row or Column."
                }
            }
        };
        return new A2UICatalogBuilder(document, null);
    }

    /// <summary>Starts from the basic catalog — every component and function — under your id.</summary>
    public static A2UICatalogBuilder ExtendBasic(string catalogId) => Extend(A2UICatalog.CreateBasic(), catalogId);

    /// <summary>Starts from the Shiny catalog — basic plus DataGrid, Gauge, ChipGroup and the rest — under your id.</summary>
    public static A2UICatalogBuilder ExtendShiny(string catalogId) => Extend(A2UICatalog.CreateShiny(), catalogId);

    /// <summary>Starts from any catalog that has a <see cref="A2UICatalog.Definition"/>, under your id.</summary>
    public static A2UICatalogBuilder Extend(A2UICatalog catalog, string catalogId)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogId);

        if (catalog.Definition is null)
            throw new ArgumentException($"Catalog '{catalog.CatalogId}' has no schema to extend. Build it with A2UICatalogBuilder or parse it from its definition.", nameof(catalog));

        var document = (JsonObject)catalog.Definition.DeepClone();
        document["$id"] = catalogId;
        document["catalogId"] = catalogId;
        return new A2UICatalogBuilder(document, catalog);
    }

    /// <summary>Sets the catalog's title.</summary>
    public A2UICatalogBuilder Title(string title)
    {
        document["title"] = title;
        return this;
    }

    /// <summary>Sets the catalog's description.</summary>
    public A2UICatalogBuilder Description(string description)
    {
        document["description"] = description;
        return this;
    }

    /// <summary>
    /// Adds a paragraph to the catalog's instructions — guidance for the agent on when to use what.
    /// Extending a catalog keeps its instructions; this appends after them.
    /// </summary>
    public A2UICatalogBuilder Instructions(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var existing = A2UIValues.AsString(document["instructions"]);
        document["instructions"] = string.IsNullOrWhiteSpace(existing) ? text : existing + "\n\n" + text;
        return this;
    }

    /// <summary>Adds (or replaces) a component.</summary>
    /// <param name="name">The component type, as the agent writes it in <c>"component"</c>.</param>
    /// <param name="description">What it is and when to use it. The agent reads this.</param>
    /// <param name="configure">Its properties and rules.</param>
    public A2UICatalogBuilder Component(string name, string description, Action<A2UIComponentBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new A2UIComponentBuilder(name, description);
        configure(builder);

        var components = Section("components");
        var isNew = !components.ContainsKey(name);
        components[name] = builder.ToSchema();

        if (isNew && document["$defs"]?["anyComponent"]?["oneOf"] is JsonArray any)
            any.Add((JsonNode)new JsonObject { ["$ref"] = "#/components/" + name });

        return this;
    }

    /// <summary>Adds (or replaces) a function, optionally with its renderer-side implementation.</summary>
    /// <param name="name">The function name, as the agent writes it in <c>"@call"</c>.</param>
    /// <param name="description">What it does. The agent reads this.</param>
    /// <param name="returns">What it returns.</param>
    /// <param name="configure">Its arguments and rules.</param>
    /// <param name="implementation">The renderer-side implementation. Leave it out and calls go to the agent.</param>
    public A2UICatalogBuilder Function(
        string name,
        string description,
        A2UIReturnType returns,
        Action<A2UIFunctionBuilder> configure,
        A2UIFunction? implementation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new A2UIFunctionBuilder(name, description, returns);
        configure(builder);

        var functions = Section("functions");
        var isNew = !functions.ContainsKey(name);
        functions[name] = builder.ToSchema();

        if (isNew && document["$defs"]?["anyFunction"]?["oneOf"] is JsonArray any)
            any.Add((JsonNode)new JsonObject { ["$ref"] = "#/functions/" + name });

        if (implementation is null)
            implementations.Remove(name);
        else
            implementations[name] = implementation;

        return this;
    }

    /// <summary>Builds the catalog. The builder can keep going afterwards; each build is independent.</summary>
    public A2UICatalog Build()
    {
        var catalog = A2UICatalog.Parse((JsonObject)document.DeepClone());

        foreach (var (name, implementation) in implementations.Where(x => catalog.Functions.ContainsKey(x.Key)))
            catalog.Implement(name, implementation);

        return catalog;
    }

    JsonObject Section(string key)
    {
        if (document[key] is JsonObject section)
            return section;

        var created = new JsonObject();
        document[key] = created;
        return created;
    }

    internal static JsonObject Ref(string commonType, string? description)
    {
        var schema = new JsonObject { ["$ref"] = Common + commonType };
        if (!string.IsNullOrWhiteSpace(description))
            schema["description"] = description;
        return schema;
    }
}

/// <summary>
/// The typed property vocabulary shared by components, functions and the items of an array property.
/// Each call adds one property to the schema the agent reads.
/// </summary>
/// <typeparam name="TSelf">The concrete builder, so calls chain.</typeparam>
/// <remarks>
/// <b>Dynamic or literal.</b> Most calls take <c>dynamic</c>, default true: the agent may give a literal,
/// a <c>{"@path": ...}</c> data binding, or a <c>{"@call": ...}</c> function call — and a bound input writes the user's value back
/// to its path. Pass <c>dynamic: false</c> for a configuration value the agent must give as a literal; only
/// then do <c>minimum</c>, <c>maximum</c> and <c>defaultValue</c> apply, because JSON Schema can't put them on
/// a binding.
/// </remarks>
public abstract partial class A2UIPropertiesBuilder<TSelf> where TSelf : A2UIPropertiesBuilder<TSelf>
{
    readonly JsonObject properties = new();
    readonly List<string> required = [];

    TSelf Self => (TSelf)this;

    /// <summary>A text property.</summary>
    public TSelf String(string name, string? description = null, bool required = false, bool dynamic = true, string? defaultValue = null)
    {
        if (dynamic)
            return Add(name, A2UICatalogBuilder.Ref("DynamicString", description), required, defaultValue is null ? null : "defaultValue");

        var schema = Literal("string", description);
        if (defaultValue is not null)
            schema["default"] = defaultValue;
        return Add(name, schema, required);
    }

    /// <summary>A number property.</summary>
    public TSelf Number(string name, string? description = null, bool required = false, bool dynamic = true,
        double? minimum = null, double? maximum = null, double? defaultValue = null)
        => Numeric("number", "DynamicNumber", name, description, required, dynamic, minimum, maximum, defaultValue);

    /// <summary>A whole-number property. Always a literal: A2UI has no dynamic integer type.</summary>
    public TSelf Integer(string name, string? description = null, bool required = false,
        int? minimum = null, int? maximum = null, int? defaultValue = null)
        => Numeric("integer", null, name, description, required, dynamic: false, minimum, maximum, defaultValue);

    /// <summary>A true/false property.</summary>
    public TSelf Boolean(string name, string? description = null, bool required = false, bool dynamic = true, bool? defaultValue = null)
    {
        if (dynamic)
            return Add(name, A2UICatalogBuilder.Ref("DynamicBoolean", description), required, defaultValue is null ? null : "defaultValue");

        var schema = Literal("boolean", description);
        if (defaultValue is not null)
            schema["default"] = defaultValue.Value;
        return Add(name, schema, required);
    }

    /// <summary>A list of strings.</summary>
    public TSelf StringList(string name, string? description = null, bool required = false, bool dynamic = true)
    {
        if (dynamic)
            return Add(name, A2UICatalogBuilder.Ref("DynamicStringList", description), required);

        var schema = Literal("array", description);
        schema["items"] = new JsonObject { ["type"] = "string" };
        return Add(name, schema, required);
    }

    /// <summary>A value of any type — a number, string, boolean, list or object, or a binding to one.</summary>
    public TSelf Value(string name, string? description = null, bool required = false)
        => Add(name, A2UICatalogBuilder.Ref("DynamicValue", description), required);

    /// <summary>A literal string from a fixed set — a variant, a size, a tone.</summary>
    public TSelf Choice(string name, IEnumerable<string> values, string? description = null, bool required = false, string? defaultValue = null)
    {
        ArgumentNullException.ThrowIfNull(values);

        var list = values.ToList();
        if (list.Count == 0)
            throw new ArgumentException("A choice needs at least one value.", nameof(values));
        if (defaultValue is not null && !list.Contains(defaultValue, StringComparer.Ordinal))
            throw new ArgumentException($"The default '{defaultValue}' is not one of the values.", nameof(defaultValue));

        var schema = Literal("string", description);
        schema["enum"] = new JsonArray(list.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());
        if (defaultValue is not null)
            schema["default"] = defaultValue;
        return Add(name, schema, required);
    }

    /// <summary>
    /// A literal string from an enum's names, camel-cased the way A2UI writes them
    /// (<c>Primary</c> → <c>primary</c>). Read it back with <see cref="A2UICatalogValues.ToEnum{TEnum}"/>.
    /// </summary>
    public TSelf Choice<TEnum>(string name, string? description = null, bool required = false, TEnum? defaultValue = null)
        where TEnum : struct, Enum
        => Choice(
            name,
            Enum.GetNames<TEnum>().Select(A2UICatalogValues.CamelCase),
            description,
            required,
            defaultValue is null ? null : A2UICatalogValues.CamelCase(defaultValue.Value.ToString()));

    /// <summary>A data path the component writes to, without a value of its own — a selection, an upload.</summary>
    public TSelf Binding(string name, string? description = null, bool required = false)
        => Add(name, A2UICatalogBuilder.Ref("DataBinding", description), required);

    /// <summary>An array of objects, each with the properties <paramref name="item"/> declares.</summary>
    public TSelf Array(string name, Action<A2UIItemBuilder> item, string? description = null, bool required = false)
    {
        ArgumentNullException.ThrowIfNull(item);

        var builder = new A2UIItemBuilder();
        item(builder);

        var schema = Literal("array", description);
        schema["items"] = builder.ToObjectSchema(closed: true);
        return Add(name, schema, required);
    }

    /// <summary>The property schema and the names of the required ones.</summary>
    internal (JsonObject Properties, List<string> Required) Snapshot() => ((JsonObject)properties.DeepClone(), required.ToList());

    internal JsonObject ToObjectSchema(bool closed)
    {
        var (props, req) = Snapshot();
        var schema = new JsonObject { ["type"] = "object", ["properties"] = props };
        if (req.Count > 0)
            schema["required"] = Strings(req);
        if (closed)
            schema["additionalProperties"] = false;
        return schema;
    }

    /// <summary>Adds a property; for subclasses with vocabulary of their own (children, actions).</summary>
    protected TSelf Add(string name, JsonObject schema, bool isRequired, string? invalidParameter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (invalidParameter is not null)
            throw new ArgumentException($"'{name}' is dynamic, so it can't have a default: JSON Schema can't put one on a binding. Pass dynamic: false, or describe the default in the description.", invalidParameter);

        if (name is "component" or "id" || name.StartsWith('@'))
            throw new ArgumentException($"'{name}' is reserved by A2UI.", nameof(name));

        properties[name] = schema;
        required.Remove(name);
        if (isRequired)
            required.Add(name);

        return Self;
    }

    internal static JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());

    static JsonObject Literal(string type, string? description)
    {
        var schema = new JsonObject { ["type"] = type };
        if (!string.IsNullOrWhiteSpace(description))
            schema["description"] = description;
        return schema;
    }

    TSelf Numeric(string type, string? dynamicType, string name, string? description, bool required, bool dynamic,
        double? minimum, double? maximum, double? defaultValue)
    {
        if (dynamic && dynamicType is not null)
        {
            if (minimum is not null || maximum is not null)
                throw new ArgumentException($"'{name}' is dynamic, so it can't have a minimum or maximum: JSON Schema can't put one on a binding. Pass dynamic: false, or validate it with a check.", nameof(minimum));

            return Add(name, A2UICatalogBuilder.Ref(dynamicType, description), required, defaultValue is null ? null : "defaultValue");
        }

        // An integer's bounds and default are written as integers, so the schema reads as authored.
        var integer = type == "integer";
        var schema = Literal(type, description);
        if (minimum is not null)
            schema["minimum"] = integer ? JsonValue.Create((long)minimum.Value) : JsonValue.Create(minimum.Value);
        if (maximum is not null)
            schema["maximum"] = integer ? JsonValue.Create((long)maximum.Value) : JsonValue.Create(maximum.Value);
        if (defaultValue is not null)
            schema["default"] = integer ? JsonValue.Create((long)defaultValue.Value) : JsonValue.Create(defaultValue.Value);
        return Add(name, schema, required);
    }
}

/// <summary>The properties of each object in an <see cref="A2UIPropertiesBuilder{TSelf}.Array"/> property.</summary>
public sealed class A2UIItemBuilder : A2UIPropertiesBuilder<A2UIItemBuilder>
{
    internal A2UIItemBuilder() { }

    /// <summary>A child component id inside each item, the way Tabs' <c>tabs[].child</c> holds one.</summary>
    public A2UIItemBuilder Child(string name, string? description = null, bool required = false)
        => Add(name, A2UICatalogBuilder.Ref("Child", description), required);
}

/// <summary>A component's properties and the rules the engine enforces for it.</summary>
public sealed class A2UIComponentBuilder : A2UIPropertiesBuilder<A2UIComponentBuilder>
{
    readonly string name;
    readonly string description;
    bool checkable;
    bool weight = true;
    List<string>? allowedParents;
    List<string>? allowedChildren;

    internal A2UIComponentBuilder(string name, string description)
    {
        this.name = name;
        this.description = description;
    }

    /// <summary>One child component, by id — a Card's content, an Expander's body.</summary>
    public A2UIComponentBuilder Child(string name, string? description = null, bool required = false)
        => Add(name, A2UICatalogBuilder.Ref("Child", description), required);

    /// <summary>
    /// Child components: a fixed list of ids, or a template repeated over a data list. The renderer
    /// draws them through <c>A2UIRenderContext.WatchChildren</c>.
    /// </summary>
    public A2UIComponentBuilder Children(string name = "children", string? description = null, bool required = false)
        => Add(name, A2UICatalogBuilder.Ref("ChildList", description), required);

    /// <summary>An action sent to the agent (or a function run locally) when the user does something.</summary>
    public A2UIComponentBuilder Action(string name = "action", string? description = null, bool required = false)
        => Add(name, A2UICatalogBuilder.Ref("Action", description), required);

    /// <summary>
    /// Lets the agent attach <c>checks</c> — validation that shows a message under the component and
    /// disables Buttons while it fails. Read the result with <c>A2UIRenderContext.Checks</c>.
    /// </summary>
    public A2UIComponentBuilder Checkable()
    {
        checkable = true;
        return this;
    }

    /// <summary>Leaves out the <c>weight</c> property every built-in component has.</summary>
    public A2UIComponentBuilder NoWeight()
    {
        weight = false;
        return this;
    }

    /// <summary>Only these component types may contain it. The engine rejects anything else.</summary>
    public A2UIComponentBuilder AllowedParents(params string[] componentTypes)
    {
        allowedParents = [.. componentTypes];
        return this;
    }

    /// <summary>Only these component types may be its children. The engine rejects anything else.</summary>
    public A2UIComponentBuilder AllowedChildren(params string[] componentTypes)
    {
        allowedChildren = [.. componentTypes];
        return this;
    }

    internal JsonObject ToSchema()
    {
        var (props, req) = Snapshot();

        // "component" leads, as in the published catalogs, so the agent reads the type first.
        var ordered = new JsonObject { ["component"] = new JsonObject { ["const"] = name } };
        foreach (var (key, value) in props)
            ordered[key] = value?.DeepClone();
        if (weight && !ordered.ContainsKey("weight"))
            ordered["weight"] = new JsonObject { ["$ref"] = "#/$defs/Weight" };

        var body = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = ordered,
            ["required"] = Strings(["component", .. req])
        };

        // Checkable composes the shared check rules in, the same allOf shape the basic catalog uses.
        var schema = checkable
            ? new JsonObject
            {
                ["type"] = "object",
                ["allOf"] = new JsonArray(new JsonObject { ["$ref"] = "common_types.json#/$defs/Checkable" }, body)
            }
            : body;

        if (!string.IsNullOrWhiteSpace(description))
            schema["description"] = description;
        if (allowedParents is not null)
            schema["allowedParents"] = Strings(allowedParents);
        if (allowedChildren is not null)
            schema["allowedChildren"] = Strings(allowedChildren);

        return schema;
    }
}

/// <summary>A function's arguments and calling rules.</summary>
public sealed class A2UIFunctionBuilder : A2UIPropertiesBuilder<A2UIFunctionBuilder>
{
    readonly string name;
    readonly string description;
    readonly A2UIReturnType returns;
    A2UIAllowedCallers callers = A2UIAllowedCallers.RendererOnly;
    bool requiresUserActivation;
    bool additionalArguments;

    internal A2UIFunctionBuilder(string name, string description, A2UIReturnType returns)
    {
        this.name = name;
        this.description = description;
        this.returns = returns;
    }

    /// <summary>Who may call it. Renderer only (bindings, checks, actions) by default.</summary>
    public A2UIFunctionBuilder AllowedCallers(A2UIAllowedCallers value)
    {
        callers = value;
        return this;
    }

    /// <summary>Only run it from a user's tap — the rule <c>openUrl</c> follows.</summary>
    public A2UIFunctionBuilder RequiresUserActivation()
    {
        requiresUserActivation = true;
        return this;
    }

    /// <summary>Accept arguments beyond the declared ones, instead of rejecting them.</summary>
    public A2UIFunctionBuilder AllowAdditionalArguments()
    {
        additionalArguments = true;
        return this;
    }

    internal JsonObject ToSchema()
    {
        var args = ToObjectSchema(closed: false);
        if (!additionalArguments)
            args["unevaluatedProperties"] = false;

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["description"] = description,
            ["returnType"] = A2UICatalogValues.CamelCase(returns.ToString()),
            ["properties"] = new JsonObject
            {
                ["@call"] = new JsonObject { ["const"] = name },
                ["args"] = args
            },
            ["required"] = Strings(["@call", "args"])
        };

        if (callers != A2UIAllowedCallers.RendererOnly)
            schema["allowedCallers"] = A2UICatalogValues.CamelCase(callers.ToString());
        if (requiresUserActivation)
            schema["requiresUserActivation"] = true;

        return schema;
    }
}

/// <summary>Helpers for reading values a builder-declared catalog describes.</summary>
public static class A2UICatalogValues
{
    /// <summary>
    /// Reads a <see cref="A2UIPropertiesBuilder{TSelf}.Choice{TEnum}"/> value back as the enum, ignoring
    /// case. Returns <paramref name="fallback"/> for null or unknown text.
    /// </summary>
    public static TEnum ToEnum<TEnum>(string? value, TEnum fallback = default) where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : fallback;

    internal static string CamelCase(string value)
        => string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
