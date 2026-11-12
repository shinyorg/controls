using System.Text.Json.Nodes;

namespace Shiny.Controls.AgentUI.A2UI;

/// <summary>One component from a surface's flat adjacency list.</summary>
public sealed class A2UIComponent
{
    internal A2UIComponent(JsonObject properties, string id, string type)
    {
        Properties = properties;
        Id = id;
        Type = type;
    }

    /// <summary>The component's id within its surface.</summary>
    public string Id { get; }

    /// <summary>The component type (its <c>component</c> property), e.g. <c>Text</c>.</summary>
    public string Type { get; }

    /// <summary>The component's own catalog override (<c>@catalogId</c>).</summary>
    public string? CatalogId => A2UIValues.AsString(Properties["@catalogId"]);

    /// <summary>Every property as sent, unresolved. Treat as read-only.</summary>
    public JsonObject Properties { get; }

    /// <summary>An unresolved property; resolve it with <see cref="A2UISurface.Evaluate"/>.</summary>
    public JsonNode? this[string property] => Properties[property];

    /// <summary>The component's <c>@checks</c>, unresolved; run them with <see cref="A2UISurface.EvaluateChecks"/>.</summary>
    public JsonArray? Checks => Properties["@checks"] as JsonArray;

    /// <summary>Bumped when the agent re-sends the component.</summary>
    public int Version { get; internal set; }
}

/// <summary>A resolved child: the component (null while it hasn't arrived - draw a placeholder) and the scope to evaluate it in.</summary>
public readonly record struct A2UIChild(string Id, A2UIComponent? Component, A2UIScope Scope);

/// <summary>A component's accessibility attributes, resolved.</summary>
public sealed record A2UIAccessibility(string? Label, string? Description, string Live, bool Hidden)
{
    /// <summary>No accessibility attributes.</summary>
    public static readonly A2UIAccessibility None = new(null, null, "off", false);
}

/// <summary>The outcome of a component's <c>@checks</c>.</summary>
public sealed record A2UICheckResult(IReadOnlyList<A2UIValidationResult> Failures)
{
    /// <summary>Every check passed.</summary>
    public static readonly A2UICheckResult Valid = new([]);

    /// <summary>True when no check failed with severity "error" (warnings and info don't block).</summary>
    public bool IsValid => Failures.All(f => f.Severity != "error");

    /// <summary>The first failure's message, for a single inline error line.</summary>
    public string? FirstMessage => Failures.FirstOrDefault()?.Message;
}
