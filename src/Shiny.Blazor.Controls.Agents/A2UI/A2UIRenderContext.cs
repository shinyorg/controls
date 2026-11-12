using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;
using Shiny.Controls.AgentUI.A2UI;

namespace Shiny.Blazor.Controls.Agents;

/// <summary>
/// What a renderer gets for one component: the surface, the component, the scope to evaluate it in
/// (a template item's scope inside a List or DataGrid row), and helpers that resolve its properties,
/// write two-way bindings, run its checks and draw its children.
/// </summary>
/// <remarks>
/// A context is created per render, so it is cheap and holds no state. Renderers that need state
/// (the selected tab, an open modal) are components that take the context as a parameter.
/// </remarks>
public sealed class A2UIRenderContext
{
    internal A2UIRenderContext(A2UISurfaceView view, A2UIComponent component, A2UIScope scope, A2UIRenderContext? parent, int depth)
    {
        View = view;
        Component = component;
        Scope = scope;
        Parent = parent;
        Depth = depth;
    }

    /// <summary>The enclosing component's context; null for the root.</summary>
    public A2UIRenderContext? Parent { get; }

    /// <summary>How deep the component is nested (0 for the root).</summary>
    public int Depth { get; }

    /// <summary>The view drawing the surface.</summary>
    public A2UISurfaceView View { get; }

    /// <summary>The surface the component belongs to.</summary>
    public A2UISurface Surface => Scope.Surface;

    /// <summary>The component to draw.</summary>
    public A2UIComponent Component { get; }

    /// <summary>The evaluation scope: the root scope, or a template item's scope.</summary>
    public A2UIScope Scope { get; }

    /// <summary>True for the surface's root component (an outermost Card honours <see cref="A2UISurfaceView.ShowBorder"/>).</summary>
    public bool IsRoot => Parent is null;

    /// <summary>The culture numbers and dates are shown in (the surface host's culture).</summary>
    public CultureInfo Culture => Scope.Surface.Host.Culture;

    /// <summary>True when the component sets this property at all.</summary>
    public bool Has(string property) => Component.Properties.ContainsKey(property);

    /// <summary>A property resolved to a value (a literal, a binding or a function call).</summary>
    public JsonNode? Value(string property) => Scope.Surface.Evaluate(Component[property], Scope);

    /// <summary>A property resolved to a string ("" when unset).</summary>
    public string String(string property) => Scope.Surface.EvaluateString(Component[property], Scope);

    /// <summary>A property resolved to a string, or null when unset or empty.</summary>
    public string? StringOrNull(string property)
    {
        if (!Has(property))
            return null;
        var s = String(property);
        return s.Length == 0 ? null : s;
    }

    /// <summary>A property resolved to a number, or null.</summary>
    public double? Number(string property) => Scope.Surface.EvaluateNumber(Component[property], Scope);

    /// <summary>A property resolved to a boolean with the spec's coercion; <paramref name="fallback"/> when unset.</summary>
    public bool Bool(string property, bool fallback = false) => Has(property) ? Scope.Surface.EvaluateBoolean(Component[property], Scope) : fallback;

    /// <summary>A property resolved to a string list.</summary>
    public IReadOnlyList<string> StringList(string property) => Scope.Surface.EvaluateStringList(Component[property], Scope);

    /// <summary>A literal (unbound) string property such as <c>variant</c>, or <paramref name="fallback"/>.</summary>
    public string Literal(string property, string fallback = "") => A2UIValues.AsString(Component[property]) ?? fallback;

    /// <summary>Writes an input's value back through the property's binding. False when the property is a literal.</summary>
    public bool Write(string property, JsonNode? value) => Scope.Surface.Write(Component[property], Scope, value);

    /// <summary>Runs the component's action property (a tap) in this scope, or in <paramref name="scope"/> (a DataGrid row).</summary>
    public bool Dispatch(string property = "action", A2UIScope? scope = null) => Scope.Surface.DispatchAction(Component, scope ?? Scope, property);

    /// <summary>The component's <c>@checks</c>, evaluated now.</summary>
    public A2UICheckResult Checks() => Scope.Surface.EvaluateChecks(Component, Scope);

    /// <summary>The component's <c>@accessibility</c>, resolved.</summary>
    public A2UIAccessibility Accessibility() => Scope.Surface.GetAccessibility(Component, Scope);

    /// <summary>
    /// ARIA attributes for the component's main element: <c>aria-label</c> and <c>aria-description</c>
    /// from <c>@accessibility</c>. An explicit label the renderer passes wins.
    /// </summary>
    public Dictionary<string, object> Aria(string? explicitLabel = null)
    {
        var a = Accessibility();
        var d = new Dictionary<string, object>(2);
        var label = explicitLabel ?? a.Label;
        if (!string.IsNullOrEmpty(label))
            d["aria-label"] = label;
        if (!string.IsNullOrEmpty(a.Description))
            d["aria-description"] = a.Description;
        return d;
    }

    /// <summary>Draws the child a <c>Child</c> property references (a placeholder while it hasn't arrived).</summary>
    public RenderFragment Child(string property = "child")
        => Scope.Surface.GetChild(Component, property, Scope) is { } child ? View.RenderChild(child, this) : static _ => { };

    /// <summary>Draws a child by id, in this scope.</summary>
    public RenderFragment ChildById(string? id)
        => id is null ? static _ => { } : View.RenderChild(new A2UIChild(id, Scope.Surface.GetComponent(id), Scope), this);

    /// <summary>Draws one resolved child (from <see cref="Children"/>).</summary>
    public RenderFragment Child(A2UIChild child) => View.RenderChild(child, this);

    /// <summary>The children a <c>ChildList</c> property describes: static ids or one template instance per data item.</summary>
    public IReadOnlyList<A2UIChild> Children(string property = "children") => Scope.Surface.GetChildren(Component, property, Scope);
}
