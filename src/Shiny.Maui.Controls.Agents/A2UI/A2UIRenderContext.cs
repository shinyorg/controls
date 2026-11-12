using System.Text.Json.Nodes;

namespace Shiny.Maui.Controls.Agents;

/// <summary>
/// What a renderer gets when it draws one component: the surface, the component, the scope to
/// evaluate it in, and helpers to draw its children, bind its properties to the data model, write
/// inputs back and dispatch its action.
/// </summary>
/// <remarks>
/// Anything that reads the data model belongs in <see cref="Watch"/>: the update runs once now and
/// again only when a data change touches a path it read, so a renderer never redraws itself for data
/// it doesn't show. When the agent re-sends the component itself, the whole renderer runs again.
/// </remarks>
public sealed class A2UIRenderContext
{
    readonly A2UINode node;

    internal A2UIRenderContext(A2UINode node, A2UIComponent component)
    {
        this.node = node;
        this.Component = component;
    }

    /// <summary>The surface being drawn.</summary>
    public A2UISurface Surface => this.node.Owner.Surface;

    /// <summary>The component to draw.</summary>
    public A2UIComponent Component { get; }

    /// <summary>
    /// The scope to evaluate the component in - the root scope, or a template item's scope (relative
    /// paths and <c>@index</c> resolve against it). Use the scope <see cref="Watch"/> hands you for reads
    /// that should redraw on change.
    /// </summary>
    public A2UIScope Scope => this.node.Scope;

    /// <summary>The view hosting the surface.</summary>
    public A2UISurfaceView View => this.node.Owner.View;

    /// <summary>The renderers in use, for composite components that draw another type inline.</summary>
    public A2UIRendererRegistry Renderers => this.node.Owner.Renderers;

    /// <summary>
    /// Whether this component should draw its own edge. False for an outermost Card in a view whose
    /// <see cref="A2UISurfaceView.ShowBorder"/> is off (inside an agent message), so the message and the
    /// card don't draw two edges.
    /// </summary>
    public bool ShowBorder => this.View.ShowBorder || this.node.InsideCard;

    /// <summary>True when the surface view owns vertical scrolling; false when it sizes to its content (inside an AgentView).</summary>
    public bool IsScrollEnabled => this.View.IsScrollEnabled;

    // ── children ─────────────────────────────────────────────────────────────────

    /// <summary>Draws a child (a placeholder while it hasn't arrived) and returns its view.</summary>
    public View Render(A2UIChild child) => this.node.Owner.CreateChild(this.node, child).Slot;

    /// <summary>Draws the child a <c>Child</c> property references, or returns null when it is absent.</summary>
    public View? RenderChild(string property)
        => this.Surface.GetChild(this.Component, property, this.Scope) is { } child ? this.Render(child) : null;

    /// <summary>
    /// Draws a <c>ChildList</c> property - a static list or one template instance per data item - and
    /// calls <paramref name="apply"/> with the children's views. It runs again (with fresh views) only
    /// when the list itself changes shape: a different count, ids or item paths. An edit inside one
    /// item redraws just what read it.
    /// </summary>
    /// <param name="property">The child list property, usually <c>children</c>.</param>
    /// <param name="apply">Puts the views (and the children, for per-child properties like weight) in the layout.</param>
    public void WatchChildren(string property, Action<IReadOnlyList<(A2UIChild Child, View View)>> apply)
    {
        string? signature = null;
        var built = new List<A2UINode>();
        this.Watch(scope =>
        {
            var children = this.Surface.GetChildren(this.Component, property, scope);
            var next = string.Join("|", children.Select(c => c.Id + "@" + c.Scope.BasePath));
            if (next == signature)
                return;

            signature = next;
            foreach (var old in built)
                this.node.Owner.RemoveChild(this.node, old);
            built.Clear();

            var list = new List<(A2UIChild, View)>(children.Count);
            foreach (var child in children)
            {
                var created = this.node.Owner.CreateChild(this.node, child);
                built.Add(created);
                list.Add((child, created.Slot));
            }
            apply(list);
        });
    }

    // ── data ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs <paramref name="update"/> now with a tracking scope, and again whenever a data change
    /// touches a path it read (or an agent function it called answers).
    /// </summary>
    public void Watch(Action<A2UIScope> update) => this.node.AddWatcher(update);

    /// <summary>Resolves a property as a string.</summary>
    public string String(string property, A2UIScope? scope = null) => this.Surface.EvaluateString(this.Component[property], scope ?? this.Scope);

    /// <summary>Resolves a property as a string, or null when the component doesn't set it.</summary>
    public string? OptionalString(string property, A2UIScope? scope = null)
        => this.Component.Properties.ContainsKey(property) ? this.String(property, scope) : null;

    /// <summary>Resolves a property as a number, or null.</summary>
    public double? Number(string property, A2UIScope? scope = null) => this.Surface.EvaluateNumber(this.Component[property], scope ?? this.Scope);

    /// <summary>Resolves a property as a boolean (the spec's coercion).</summary>
    public bool Boolean(string property, A2UIScope? scope = null) => this.Surface.EvaluateBoolean(this.Component[property], scope ?? this.Scope);

    /// <summary>Resolves a property as a string list.</summary>
    public IReadOnlyList<string> StringList(string property, A2UIScope? scope = null) => this.Surface.EvaluateStringList(this.Component[property], scope ?? this.Scope);

    /// <summary>Resolves a property to its JSON value (a view of the model - clone before keeping it).</summary>
    public JsonNode? Value(string property, A2UIScope? scope = null) => this.Surface.Evaluate(this.Component[property], scope ?? this.Scope);

    /// <summary>A literal (unbound) string property, e.g. a <c>variant</c>.</summary>
    public string? Literal(string property) => A2UIValues.AsString(this.Component[property]);

    /// <summary>
    /// Two-way binding: writes an input's value to the path <paramref name="property"/> is bound to.
    /// Returns false when the property is a literal (nothing to write back to).
    /// </summary>
    public bool Write(string property, JsonNode? value)
    {
        this.node.Touched = true;
        return this.Surface.Write(this.Component[property], this.Scope, value);
    }

    /// <summary>Runs the component's <c>@checks</c>.</summary>
    public A2UICheckResult Checks(A2UIScope? scope = null) => this.Surface.EvaluateChecks(this.Component, scope ?? this.Scope);

    /// <summary>True once the user has changed this input (through <see cref="Write"/>), for showing check failures only after an edit.</summary>
    public bool IsTouched => this.node.Touched;

    /// <summary>Dispatches an action property (a tap). Call it from the gesture handler itself.</summary>
    public bool Dispatch(string property = "action", A2UIScope? scope = null)
        => this.Surface.DispatchAction(this.Component, scope ?? this.Scope, property);

    // ── lifetime ─────────────────────────────────────────────────────────────────

    /// <summary>Redraws this component when another one it draws inline (e.g. a Button's Text label) is re-sent.</summary>
    public void Consume(string componentId) => this.node.Consumed.Add(componentId);

    /// <summary>
    /// Holds off redrawing this component (or an ancestor) while <paramref name="input"/> has focus,
    /// so a re-sent component never replaces a field the user is typing in. The redraw happens on blur.
    /// </summary>
    public void KeepWhileFocused(InputView input) => this.node.Holds.Add(input);

    /// <summary>Runs when the component's view is thrown away (redrawn, or the surface removed).</summary>
    public void OnDispose(Action dispose) => this.node.Disposers.Add(dispose);

    // Card draws its own edge, so anything under it may draw edges freely.
    internal void MarkChildrenInsideCard() => this.node.ChildrenInsideCard = true;

    // Modal: a Button trigger opens the modal from its own tap handler.
    internal Action? Trigger => this.node.Trigger;

    internal View RenderTrigger(A2UIChild child, Action open)
    {
        var created = this.node.Owner.CreateChild(this.node, child, open);
        return created.Slot;
    }

    internal A2UINode Node => this.node;
}
