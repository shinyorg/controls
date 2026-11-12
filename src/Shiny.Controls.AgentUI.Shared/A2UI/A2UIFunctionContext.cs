using System.Globalization;
using System.Text.Json.Nodes;

namespace Shiny.Controls.AgentUI.A2UI;

/// <summary>Everything a function implementation needs: its arguments (resolved on demand), the scope and the host's services.</summary>
public sealed class A2UIFunctionContext
{
    internal A2UIFunctionContext(string name, string? catalogId, JsonObject args, A2UIScope scope)
    {
        Name = name;
        CatalogId = catalogId;
        RawArguments = args;
        Scope = scope;
    }

    /// <summary>The function being called.</summary>
    public string Name { get; }

    /// <summary>The catalog the call resolved to (null for <c>@</c> system functions).</summary>
    public string? CatalogId { get; }

    /// <summary>The arguments as written, unresolved.</summary>
    public JsonObject RawArguments { get; }

    /// <summary>Where the call is evaluated.</summary>
    public A2UIScope Scope { get; }

    /// <summary>The surface the call belongs to.</summary>
    public A2UISurface Surface => Scope.Surface;

    /// <summary>The host, for its culture, time zone and services.</summary>
    public A2UISurfaceHost Host => Scope.Surface.Host;

    /// <summary>The culture to format with.</summary>
    public CultureInfo Culture => Host.Culture;

    /// <summary>True when the call passed this argument.</summary>
    public bool Has(string name) => RawArguments.ContainsKey(name);

    /// <summary>An argument, resolved (literal, binding or nested call).</summary>
    public JsonNode? Arg(string name) => Resolve(RawArguments[name]);

    /// <summary>An argument resolved and converted to its string form.</summary>
    public string ArgString(string name) => A2UIValues.ToDisplayString(Arg(name));

    /// <summary>An argument resolved as a number, or null.</summary>
    public double? ArgNumber(string name) => A2UIValues.ToNumber(Arg(name));

    /// <summary>An argument resolved with the <c>DynamicBoolean</c> coercion.</summary>
    public bool ArgBoolean(string name) => A2UIValues.ToBoolean(Arg(name));

    /// <summary>Resolves any dynamic value in this call's scope.</summary>
    public JsonNode? Resolve(JsonNode? value) => Surface.Evaluate(value, Scope);

    /// <summary>Resolves a <c>DynamicBoolean</c> with the spec's coercion.</summary>
    public bool ResolveBoolean(JsonNode? value) => A2UIValues.ToBoolean(Resolve(value));
}
