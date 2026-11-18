using System.Text.Json.Nodes;

namespace Shiny.Controls.AgentUI.A2UI;

/// <summary>
/// Declares a custom A2UI component as a C# class. The source generator turns the class's properties
/// into the component's catalog schema and a typed reader for renderers, so neither the catalog nor
/// the renderer names a property in a string.
/// </summary>
/// <example>
/// <code language="csharp">
/// [A2UIComponent("Rating", Description = "A row of stars the user can tap to score something.")]
/// public partial class Rating
/// {
///     [A2UIProperty(Description = "The score. Bind it to receive the user's choice.")]
///     public required double Value { get; init; }
///
///     [A2UIProperty(Literal = true, Minimum = 1)]
///     public int Max { get; init; } = 5;
///
///     public Tone Tone { get; init; }          // an enum: a fixed choice
///     public A2UIActionRef Action { get; init; }
/// }
///
/// var catalog = A2UICatalogBuilder.ExtendShiny(id).Component&lt;Rating&gt;().Build();
/// renderers.Register&lt;Rating&gt;(ctx =&gt; { var rating = ctx.Read&lt;Rating&gt;(); ... });
/// </code>
/// </example>
/// <remarks>
/// <para><b>How a property maps.</b> The JSON name is the property name camel-cased (<c>MaxStars</c> →
/// <c>maxStars</c>). <c>required</c> makes it required. <see cref="string"/>, <see cref="double"/>,
/// <see cref="float"/>, <see cref="bool"/> and string lists (<c>IReadOnlyList&lt;string&gt;</c>,
/// <c>string[]</c>) are dynamic — a literal, a binding or a function call. <see cref="int"/> and
/// <see cref="long"/> are literal integers; an enum is a literal choice of its camel-cased names;
/// <see cref="JsonNode"/> is a value of any type. <see cref="A2UIActionRef"/>,
/// <see cref="A2UIChildRef"/>, <see cref="A2UIChildrenRef"/> and <see cref="A2UIBindingRef"/> declare an
/// action, a child, children and a write-back binding. A list of an <see cref="A2UIItemAttribute"/> class is
/// an array of objects.</para>
/// <para><b>Descriptions</b> come from <see cref="A2UIPropertyAttribute.Description"/>, else
/// <c>[System.ComponentModel.Description]</c>, else the property's XML doc summary. The agent reads them,
/// so write them.</para>
/// <para><b>Defaults.</b> A property initializer (<c>= 5</c>) is the value <c>Read</c> falls back to when
/// the agent leaves the property out, and — on a literal property — the schema's <c>default</c>. Keep
/// initializers to constants.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class A2UIComponentAttribute(string name) : Attribute
{
    /// <summary>The component type, as the agent writes it in <c>"component"</c>.</summary>
    public string Name { get; } = name;

    /// <summary>What it is and when to use it. Falls back to the class's XML doc summary.</summary>
    public string? Description { get; set; }

    /// <summary>Lets the agent attach <c>checks</c>.</summary>
    public bool Checkable { get; set; }

    /// <summary>Whether the component gets the <c>weight</c> property every built-in component has. True by default.</summary>
    public bool Weight { get; set; } = true;

    /// <summary>Only these component types may contain it.</summary>
    public string[]? AllowedParents { get; set; }

    /// <summary>Only these component types may be its children.</summary>
    public string[]? AllowedChildren { get; set; }
}

/// <summary>
/// Declares a class as the shape of each object in an array property of an <see cref="A2UIComponentAttribute"/>
/// component — a Gauge's ranges, a Tabs' tabs. Its properties map the same way a component's do.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class A2UIItemAttribute : Attribute;

/// <summary>Fine-tunes how one property of a generated component or item maps.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class A2UIPropertyAttribute : Attribute
{
    /// <summary>The JSON name, when the camel-cased property name isn't it.</summary>
    public string? Name { get; set; }

    /// <summary>What the property means. The agent reads this.</summary>
    public string? Description { get; set; }

    /// <summary>Only a literal value is accepted, not a binding or function call. Needed for
    /// <see cref="Minimum"/>, <see cref="Maximum"/> and a schema default.</summary>
    public bool Literal { get; set; }

    /// <summary>The smallest literal number accepted.</summary>
    public double Minimum { get; set; } = double.NaN;

    /// <summary>The largest literal number accepted.</summary>
    public double Maximum { get; set; } = double.NaN;
}

/// <summary>Leaves a property of a generated component or item out of the schema.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class A2UIIgnoreAttribute : Attribute;

/// <summary>An action property: what the user's interaction sends. Dispatch it with its <see cref="Property"/>.</summary>
/// <param name="Property">The JSON property name, for <c>ctx.Dispatch</c>.</param>
/// <param name="IsSet">Whether the agent supplied one.</param>
public readonly record struct A2UIActionRef(string Property, bool IsSet);

/// <summary>A single child component.</summary>
/// <param name="Property">The JSON property name, for <c>ctx.RenderChild</c>.</param>
/// <param name="ComponentId">The child's id, when the agent supplied one.</param>
public readonly record struct A2UIChildRef(string Property, string? ComponentId);

/// <summary>A list of child components, fixed or templated.</summary>
/// <param name="Property">The JSON property name, for <c>ctx.WatchChildren</c>.</param>
public readonly record struct A2UIChildrenRef(string Property);

/// <summary>A data path the component writes the user's input to.</summary>
/// <param name="Property">The JSON property name, for <c>ctx.Write</c>.</param>
/// <param name="Path">The bound path, when the agent bound one.</param>
public readonly record struct A2UIBindingRef(string Property, string? Path);

/// <summary>A component class the source generator implemented. Don't implement it by hand.</summary>
public interface IA2UIComponentModel<TSelf> where TSelf : IA2UIComponentModel<TSelf>
{
    /// <summary>The component type.</summary>
    static abstract string ComponentName { get; }

    /// <summary>The component's description for the agent.</summary>
    static abstract string ComponentDescription { get; }

    /// <summary>Declares the component's schema.</summary>
    static abstract void Describe(A2UIComponentBuilder builder);

    /// <summary>Evaluates a component's properties into a model.</summary>
    static abstract TSelf Read(A2UIModelSource source);
}

/// <summary>An array-item class the source generator implemented. Don't implement it by hand.</summary>
public interface IA2UIItemModel<TSelf> where TSelf : IA2UIItemModel<TSelf>
{
    /// <summary>Declares the item's schema.</summary>
    static abstract void Describe(A2UIItemBuilder builder);

    /// <summary>Evaluates one item object into a model.</summary>
    static abstract TSelf Read(A2UIModelSource source);
}

/// <summary>
/// Where a generated model was read from: the component's properties, the surface and scope they were
/// evaluated in, and how to write a bound value back. The generated <c>Set…</c> methods write through it.
/// </summary>
public sealed class A2UIModelSource
{
    readonly Func<string, JsonNode?, bool>? write;

    /// <param name="properties">The component's (or item's) properties.</param>
    /// <param name="surface">The surface they belong to.</param>
    /// <param name="scope">The scope they were evaluated in.</param>
    /// <param name="write">The host's write-back — the render context's <c>Write</c>, which also marks the
    /// input as edited so its check messages show. Null writes straight to the surface.</param>
    public A2UIModelSource(JsonObject properties, A2UISurface surface, A2UIScope scope, Func<string, JsonNode?, bool>? write = null)
    {
        Properties = properties ?? throw new ArgumentNullException(nameof(properties));
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        this.write = write;
    }

    /// <summary>The component's (or item's) properties.</summary>
    public JsonObject Properties { get; }

    /// <summary>The surface they belong to.</summary>
    public A2UISurface Surface { get; }

    /// <summary>The scope they were evaluated in.</summary>
    public A2UIScope Scope { get; }

    /// <summary>Writes a value to the path a property is bound to. False when it isn't bound.</summary>
    public bool Write(string property, JsonNode? value)
        => write?.Invoke(property, value) ?? Surface.Write(Properties[property], Scope, value);
}

/// <summary>Converts typed values for the generated <c>Set…</c> methods, so no caller builds JSON.</summary>
public static class A2UIModelWriter
{
    /// <summary>Writes a string. False when the model wasn't read from a surface or the property isn't bound.</summary>
    public static bool Set(A2UIModelSource? source, string property, string? value)
        => source?.Write(property, value is null ? null : JsonValue.Create(value)) ?? false;

    /// <summary>Writes a number.</summary>
    public static bool Set(A2UIModelSource? source, string property, double value)
        => source?.Write(property, JsonValue.Create(value)) ?? false;

    /// <summary>Writes a boolean.</summary>
    public static bool Set(A2UIModelSource? source, string property, bool value)
        => source?.Write(property, JsonValue.Create(value)) ?? false;

    /// <summary>Writes a list of strings.</summary>
    public static bool Set(A2UIModelSource? source, string property, IEnumerable<string>? value)
        => source?.Write(property, value is null ? null : new JsonArray(value.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray())) ?? false;
}

/// <summary>
/// Declares a static method as an A2UI catalog function. The source generator writes its schema from the
/// parameters and return type, and the adapter that evaluates the agent's arguments and converts the result —
/// so the function is plain C#, with no JSON in or out.
/// </summary>
/// <example>
/// <code language="csharp">
/// public static partial class MyFunctions
/// {
///     /// &lt;summary&gt;The initials of a name.&lt;/summary&gt;
///     [A2UIFunction("initials")]
///     public static string Initials(string name) =&gt; string.Concat(name.Split(' ').Select(w =&gt; w[0]));
/// }
///
/// var catalog = A2UICatalogBuilder.ExtendShiny(id).Functions(MyFunctions.A2UIFunctions).Build();
/// </code>
/// </example>
/// <remarks>
/// <para>Parameters may be <see cref="string"/>, <see cref="double"/>, <see cref="float"/>, <see cref="int"/>,
/// <see cref="long"/>, <see cref="bool"/>, an enum, or a string list; a parameter with a default value or a
/// nullable type is optional. An <see cref="A2UIFunctionContext"/> parameter receives the call's context
/// (culture, surface) and isn't part of the schema. The return type may be <see cref="string"/>, a number,
/// <see cref="bool"/> or <see langword="void"/>.</para>
/// <para>The containing class must be <see langword="partial"/>; the generator adds a static
/// <c>A2UIFunctions(A2UICatalogBuilder)</c> method that registers every function it declares.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class A2UIFunctionAttribute(string name) : Attribute
{
    /// <summary>The function name, as the agent writes it in <c>"@call"</c>.</summary>
    public string Name { get; } = name;

    /// <summary>What it does. Falls back to the method's XML doc summary.</summary>
    public string? Description { get; set; }

    /// <summary>Who may call it. Renderer only by default.</summary>
    public A2UIAllowedCallers AllowedCallers { get; set; } = A2UIAllowedCallers.RendererOnly;

    /// <summary>Only run it from a user's tap.</summary>
    public bool RequiresUserActivation { get; set; }
}

/// <summary>Reads generated models; the hosts' <c>A2UIRenderContext.Read&lt;T&gt;()</c> go through here.</summary>
public static class A2UIModelReader
{
    /// <summary>Evaluates a component into its generated model in a scope. Its <c>Set…</c> methods write straight to the surface.</summary>
    public static T Read<T>(A2UISurface surface, A2UIComponent component, A2UIScope scope) where T : IA2UIComponentModel<T>
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(scope);
        return T.Read(new A2UIModelSource(component.Properties, surface, scope));
    }

    /// <summary>Reads an array property of item objects. Used by generated code.</summary>
    public static IReadOnlyList<T> ReadItems<T>(JsonNode? node, A2UISurface surface, A2UIScope scope) where T : IA2UIItemModel<T>
        => node is JsonArray array
            ? array.OfType<JsonObject>().Select(o => T.Read(new A2UIModelSource(o, surface, scope))).ToList()
            : [];

    /// <summary>Reads a string list. Used by generated code.</summary>
    public static string[] ReadStringList(JsonNode? node, A2UISurface surface, A2UIScope scope)
        => [.. surface.EvaluateStringList(node, scope)];

    /// <summary>The path a binding property points at, when it is one. Used by generated code.</summary>
    public static string? BindingPath(JsonNode? node)
        => A2UIValues.IsBinding(node, out var path) ? path : A2UIValues.AsString(node);

    /// <summary>A function argument as a string list. Used by generated code.</summary>
    public static string[] ArgStringList(A2UIFunctionContext context, string name)
        => [.. A2UIValues.ToStringList(context.Arg(name))];

    /// <summary>A function's string result. Used by generated code.</summary>
    public static JsonNode? Result(string? value) => value is null ? null : JsonValue.Create(value);

    /// <summary>A function's numeric result. Used by generated code.</summary>
    public static JsonNode? Result(double value) => JsonValue.Create(value);

    /// <summary>A function's boolean result. Used by generated code.</summary>
    public static JsonNode? Result(bool value) => JsonValue.Create(value);
}

public sealed partial class A2UICatalogBuilder
{
    /// <summary>Adds (or replaces) a component declared with <see cref="A2UIComponentAttribute"/>.</summary>
    public A2UICatalogBuilder Component<T>() where T : IA2UIComponentModel<T>
        => Component(T.ComponentName, T.ComponentDescription, T.Describe);

    /// <summary>
    /// Adds the functions a class declares with <see cref="A2UIFunctionAttribute"/>:
    /// <c>.Functions(MyFunctions.A2UIFunctions)</c>.
    /// </summary>
    public A2UICatalogBuilder Functions(Action<A2UICatalogBuilder> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        register(this);
        return this;
    }
}

public abstract partial class A2UIPropertiesBuilder<TSelf>
{
    /// <summary>An array of objects shaped by an <see cref="A2UIItemAttribute"/> class.</summary>
    public TSelf Array<TItem>(string name, string? description = null, bool required = false) where TItem : IA2UIItemModel<TItem>
        => Array(name, TItem.Describe, description, required);
}
