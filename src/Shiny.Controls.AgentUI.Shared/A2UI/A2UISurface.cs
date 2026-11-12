using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shiny.Controls.AgentUI.Json;

namespace Shiny.Controls.AgentUI.A2UI;

/// <summary>What changed on a surface.</summary>
public enum A2UISurfaceChangeKind
{
    /// <summary>Components were added or replaced (<see cref="A2UISurfaceChange.ComponentIds"/>).</summary>
    Components,
    /// <summary>The data model changed at <see cref="A2UISurfaceChange.Path"/> (re-evaluate what read it).</summary>
    DataModel,
    /// <summary>An agent function answered; re-evaluate anything that was pending.</summary>
    FunctionResults,
    /// <summary>The surface was deleted; stop drawing it.</summary>
    Deleted
}

/// <summary>A change to a surface, for renderers to react to.</summary>
/// <param name="Kind">What changed.</param>
/// <param name="ComponentIds">The components added or replaced, for Components changes.</param>
/// <param name="Path">The data model path that changed, for DataModel changes.</param>
public sealed record A2UISurfaceChange(A2UISurfaceChangeKind Kind, IReadOnlyList<string>? ComponentIds = null, string? Path = null);

/// <summary>
/// One A2UI surface: its components, its data model, and the evaluation of everything a renderer
/// draws from them - bindings, functions, checks, children and actions.
/// </summary>
/// <remarks>Not thread-safe; use it from the thread that renders.</remarks>
public sealed class A2UISurface
{
    readonly Dictionary<string, A2UIComponent> components = new(StringComparer.Ordinal);
    readonly Dictionary<string, RemoteCall> remoteCalls = new(StringComparer.Ordinal);
    readonly HashSet<string> reported = new(StringComparer.Ordinal);

    internal A2UISurface(A2UISurfaceHost host, string surfaceId, string? catalogId, bool sendDataModel, JsonObject? metadata)
    {
        Host = host;
        SurfaceId = surfaceId;
        CatalogId = catalogId;
        SendDataModel = sendDataModel;
        Metadata = metadata;
        RootScope = A2UIScope.Root(this);
    }

    /// <summary>The host that owns the surface.</summary>
    public A2UISurfaceHost Host { get; }

    /// <summary>The surface's id, unique for the host's lifetime.</summary>
    public string SurfaceId { get; }

    /// <summary>The surface's default catalog.</summary>
    public string? CatalogId { get; }

    /// <summary>When true, the whole data model goes back to the agent with every renderer message.</summary>
    public bool SendDataModel { get; }

    /// <summary>The surface metadata the agent sent (extensions).</summary>
    public JsonObject? Metadata { get; }

    /// <summary>The data model. Read it through bindings; write it with <see cref="Write"/> or <see cref="SetData"/>.</summary>
    public JsonNode? DataModel { get; private set; } = new JsonObject();

    /// <summary>Every component received so far, by id.</summary>
    public IReadOnlyDictionary<string, A2UIComponent> Components => components;

    /// <summary>The component mounted under the surface (<c>id: "root"</c>). Nothing renders until it exists.</summary>
    public A2UIComponent? Root => components.GetValueOrDefault("root");

    /// <summary>The root evaluation scope.</summary>
    public A2UIScope RootScope { get; }

    /// <summary>True once the surface has been deleted.</summary>
    public bool IsDeleted { get; private set; }

    /// <summary>Raised when components, data or agent function results change, and when the surface is deleted.</summary>
    public event EventHandler<A2UISurfaceChange>? Changed;

    // ── reading ──────────────────────────────────────────────────────────────────

    /// <summary>The component with this id, or null while it hasn't arrived.</summary>
    public A2UIComponent? GetComponent(string id) => components.GetValueOrDefault(id);

    /// <summary>The catalog definition for a component, or null when its type isn't in its catalog.</summary>
    public A2UIComponentDefinition? GetDefinition(A2UIComponent component)
    {
        var catalogId = component.CatalogId ?? CatalogId;
        return catalogId is not null && Host.Catalogs.TryGet(catalogId, out var catalog)
            ? catalog.Components.GetValueOrDefault(component.Type)
            : null;
    }

    /// <summary>The children a <c>ChildList</c> property describes: a static id list, or one template instance per data item.</summary>
    public IReadOnlyList<A2UIChild> GetChildren(A2UIComponent component, string property, A2UIScope? scope = null)
    {
        scope ??= RootScope;
        switch (component[property])
        {
            case JsonArray ids:
                return ids.Select(A2UIValues.AsString).OfType<string>()
                    .Select(id => new A2UIChild(id, GetComponent(id), scope))
                    .ToList();

            case JsonObject template when A2UIValues.AsString(template["componentId"]) is { } templateId
                                          && A2UIValues.AsString(template["path"]) is { } path:
                var listPath = scope.Resolve(path);
                if (scope.Read(path) is not JsonArray items)
                    return [];

                var templateComponent = GetComponent(templateId);
                var list = new List<A2UIChild>(items.Count);
                for (var i = 0; i < items.Count; i++)
                    list.Add(new A2UIChild(templateId, templateComponent, scope.ForItem(JsonPointer.Append(listPath, i), i)));
                return list;

            default:
                return [];
        }
    }

    /// <summary>The child a <c>Child</c> property references.</summary>
    public A2UIChild? GetChild(A2UIComponent component, string property, A2UIScope? scope = null)
    {
        scope ??= RootScope;
        return A2UIValues.AsString(component[property]) is { } id ? new A2UIChild(id, GetComponent(id), scope) : null;
    }

    /// <summary>Reads the data model at a JSON Pointer ("/" is the whole model).</summary>
    public JsonNode? GetData(string pointer) =>
        JsonPointer.TryGet(DataModel, JsonPointer.Parse(pointer, slashIsRoot: true), out var value) ? value : null;

    // ── evaluation ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a dynamic value: a literal as is, a <c>{"@path"}</c> binding from the data model, a
    /// <c>{"@call"}</c> through its catalog. Errors are reported to the agent and resolve to null.
    /// The result is a view of the model; clone it before attaching it anywhere.
    /// </summary>
    public JsonNode? Evaluate(JsonNode? value, A2UIScope? scope = null)
    {
        scope ??= RootScope;
        if (value is JsonObject o)
        {
            if (A2UIValues.IsBinding(o, out var path))
                return scope.Read(path);
            if (A2UIValues.IsCall(o))
                return EvaluateCall(o, scope, inAction: false);
        }
        return value;
    }

    /// <summary>Resolves a <c>DynamicString</c> to its string form.</summary>
    public string EvaluateString(JsonNode? value, A2UIScope? scope = null) => A2UIValues.ToDisplayString(Evaluate(value, scope));

    /// <summary>Resolves a <c>DynamicNumber</c>; null when it isn't a number.</summary>
    public double? EvaluateNumber(JsonNode? value, A2UIScope? scope = null) => A2UIValues.ToNumber(Evaluate(value, scope));

    /// <summary>Resolves a <c>DynamicBoolean</c> with the spec's coercion.</summary>
    public bool EvaluateBoolean(JsonNode? value, A2UIScope? scope = null) => A2UIValues.ToBoolean(Evaluate(value, scope));

    /// <summary>Resolves a <c>DynamicStringList</c>.</summary>
    public IReadOnlyList<string> EvaluateStringList(JsonNode? value, A2UIScope? scope = null) => A2UIValues.ToStringList(Evaluate(value, scope));

    /// <summary>
    /// Two-way binding: writes an input's value back to the path the property is bound to, creating
    /// it if the agent never initialised it. Returns false when the property is a literal.
    /// </summary>
    public bool Write(JsonNode? binding, A2UIScope? scope, JsonNode? value)
    {
        scope ??= RootScope;
        if (!A2UIValues.IsBinding(binding, out var path))
            return false;
        SetData(scope.Resolve(path), value?.Parent is null ? value : value.DeepClone());
        return true;
    }

    /// <summary>Upserts a value in the data model (null removes the key) and raises a DataModel change.</summary>
    public void SetData(string pointer, JsonNode? value)
    {
        var tokens = JsonPointer.Parse(pointer, slashIsRoot: true);
        DataModel = tokens.Count == 0 ? value ?? new JsonObject() : JsonPointer.Upsert(DataModel, tokens, value);
        Changed?.Invoke(this, new A2UISurfaceChange(A2UISurfaceChangeKind.DataModel, Path: tokens.Count == 0 ? "/" : pointer));
    }

    /// <summary>Runs a component's <c>@checks</c> with the spec's coercion (a false condition takes the rule's message).</summary>
    public A2UICheckResult EvaluateChecks(A2UIComponent component, A2UIScope? scope = null)
    {
        if (component.Checks is not { Count: > 0 } checks)
            return A2UICheckResult.Valid;

        var failures = new List<A2UIValidationResult>();
        foreach (var rule in checks.OfType<JsonObject>())
        {
            var fallback = A2UIValues.AsString(rule["message"]);
            JsonNode? result;
            try
            {
                result = EvaluateStrict(rule["condition"], scope ?? RootScope);
            }
            catch (A2UIExpressionException ex)
            {
                failures.Add(new A2UIValidationResult(false, fallback ?? ex.Message));
                continue;
            }

            if (A2UIValidationResult.From(result) is { } vr)
            {
                if (!vr.Valid)
                    failures.Add(vr with { Message = vr.Message ?? fallback });
            }
            else if (!A2UIValues.ToBoolean(result))
            {
                failures.Add(new A2UIValidationResult(false, fallback));
            }
        }
        return failures.Count == 0 ? A2UICheckResult.Valid : new A2UICheckResult(failures);
    }

    /// <summary>Resolves a component's <c>@accessibility</c> attributes.</summary>
    public A2UIAccessibility GetAccessibility(A2UIComponent component, A2UIScope? scope = null)
    {
        if (component["@accessibility"] is not JsonObject a)
            return A2UIAccessibility.None;

        scope ??= RootScope;
        return new A2UIAccessibility(
            a.ContainsKey("label") ? EvaluateString(a["label"], scope) : null,
            a.ContainsKey("description") ? EvaluateString(a["description"], scope) : null,
            A2UIValues.AsString(a["live"]) ?? "off",
            EvaluateBoolean(a["hidden"], scope));
    }

    // ── actions ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a component's action (a Button tap). An <c>event</c> action resolves its context and
    /// user message and sends an <c>action</c> to the agent; a <c>functionCall</c> runs locally (or
    /// is routed to the agent) inside a user gesture. Returns false if it failed.
    /// </summary>
    public bool DispatchAction(A2UIComponent source, A2UIScope? scope = null, string property = "action")
    {
        if (source[property] is not JsonObject action)
            return false;

        scope = (scope ?? RootScope).WithUserActivation();

        if (action["event"] is JsonObject ev && A2UIValues.AsString(ev["name"]) is { } name)
        {
            var context = new JsonObject();
            if (ev["context"] is JsonObject ctx)
            {
                foreach (var (key, value) in ctx)
                    context[key] = Evaluate(value, scope)?.DeepClone();
            }

            var message = new JsonObject
            {
                ["name"] = name,
                ["surfaceId"] = SurfaceId,
                ["sourceComponentId"] = source.Id,
                ["timestamp"] = Host.Time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture),
                ["context"] = context
            };
            if (ev.ContainsKey("userMessage"))
                message["userMessage"] = EvaluateString(ev["userMessage"], scope);

            Host.Send(A2UIOutboundKind.Action, "action", message, SurfaceId);
            return true;
        }

        if (action["functionCall"] is JsonObject call)
        {
            try
            {
                EvaluateCall(call, scope, inAction: true, strict: true);
                return true;
            }
            catch (A2UIExpressionException ex)
            {
                Report(ex.Code == A2UIErrorCodes.ExpressionError ? A2UIErrorCodes.ExecutionError : ex.Code, ex.Message);
                return false;
            }
        }
        return false;
    }

    // ── functions ────────────────────────────────────────────────────────────────

    JsonNode? EvaluateStrict(JsonNode? value, A2UIScope scope)
    {
        if (value is JsonObject o)
        {
            if (A2UIValues.IsBinding(o, out var path))
                return scope.Read(path);
            if (A2UIValues.IsCall(o))
                return EvaluateCall(o, scope, inAction: false, strict: true);
        }
        return value;
    }

    internal JsonNode? EvaluateCall(JsonObject call, A2UIScope scope, bool inAction, bool strict = false)
    {
        try
        {
            return Invoke(call, scope, inAction);
        }
        catch (A2UIExpressionException ex) when (!strict)
        {
            Report(ex.Code, ex.Message);
            return null;
        }
        catch (Exception ex) when (ex is not A2UIExpressionException)
        {
            // a function implementation threw: same recovery as an expression error
            if (strict)
                throw new A2UIExpressionException(ex.Message, A2UIErrorCodes.ExecutionError);
            Report(A2UIErrorCodes.ExpressionError, ex.Message);
            return null;
        }
    }

    JsonNode? Invoke(JsonObject call, A2UIScope scope, bool inAction)
    {
        var name = A2UIValues.AsString(call["@call"])!;
        var args = call["args"] as JsonObject ?? new JsonObject();

        if (name.StartsWith('@'))
            return InvokeSystem(name, args, scope);

        var catalogId = A2UIValues.AsString(call["catalogId"]) ?? scope.CatalogOverride ?? CatalogId
                        ?? throw new A2UIExpressionException($"Function '{name}' has no catalog: the surface has no default catalogId and the call names none.");
        if (!Host.Catalogs.TryGet(catalogId, out var catalog))
            throw new A2UIExpressionException($"Unknown catalog '{catalogId}' for function '{name}'.");
        if (!catalog.Functions.TryGetValue(name, out var definition))
            throw new A2UIExpressionException($"Catalog '{catalogId}' has no function '{name}'.");
        if (definition.AllowedCallers == A2UIAllowedCallers.AgentOnly)
            throw new A2UIExpressionException($"Function '{name}' may only be called by the agent.");
        if (definition.RequiresUserActivation && !scope.UserActivation)
            throw new A2UIExpressionException($"Function '{name}' requires a user gesture and can't run from a binding or check.");

        ValidateArguments(definition, args);

        if (catalog.TryGetImplementation(name, out var implementation))
            return implementation(new A2UIFunctionContext(name, catalogId, args, scope));

        return CallAgent(name, catalogId, args, scope, inAction);
    }

    static void ValidateArguments(A2UIFunctionDefinition definition, JsonObject args)
    {
        foreach (var required in definition.RequiredArguments)
        {
            if (!args.ContainsKey(required))
                throw new A2UIExpressionException($"Function '{definition.Name}' is missing argument '{required}'.");
        }
        if (!definition.AllowsAdditionalArguments)
        {
            foreach (var (key, _) in args)
            {
                if (!definition.Arguments.Contains(key))
                    throw new A2UIExpressionException($"Function '{definition.Name}' has no argument '{key}'.");
            }
        }
    }

    JsonNode? InvokeSystem(string name, JsonObject args, A2UIScope scope)
    {
        if (name != "@index")
            throw new A2UIExpressionException($"Unknown system function '{name}'.");

        var innermost = scope;
        while (innermost is { Index: null })
            innermost = innermost.Parent;
        if (innermost?.Index is not { } i)
            throw new A2UIExpressionException("@index can only be used inside a list template.");

        var offset = args.ContainsKey("offset") ? A2UIValues.ToNumber(Evaluate(args["offset"], scope)) ?? 0 : 0;
        return JsonValue.Create(i + offset);
    }

    // A declared function the renderer doesn't implement runs on the agent. Bindings and checks share
    // one in-flight call per identical (resolved) call; every action sends a new one.
    JsonNode? CallAgent(string name, string catalogId, JsonObject args, A2UIScope scope, bool inAction)
    {
        var resolved = new JsonObject();
        foreach (var (key, value) in args)
            resolved[key] = Evaluate(value, scope)?.DeepClone();

        var key2 = catalogId + "|" + name + "|" + resolved.ToJsonString();
        if (!inAction && remoteCalls.TryGetValue(key2, out var existing))
        {
            if (existing.Error is not null)
                throw new A2UIExpressionException(existing.Error);
            return existing.Value;
        }

        var id = Guid.NewGuid().ToString("N");
        if (!inAction)
            remoteCalls[key2] = new RemoteCall(id);

        Host.Send(A2UIOutboundKind.CallAgentFunction, "callAgentFunction", new JsonObject
        {
            ["surfaceId"] = SurfaceId,
            ["functionCallId"] = id,
            ["callFunction"] = new JsonObject { ["@call"] = name, ["catalogId"] = catalogId, ["args"] = resolved }
        }, SurfaceId);
        return null;
    }

    internal bool ResolveAgentFunction(string functionCallId, JsonNode? value, string? error)
    {
        var call = remoteCalls.Values.FirstOrDefault(c => c.Id == functionCallId);
        if (call is null)
            return false;

        call.Value = value?.DeepClone();
        call.Error = error;
        Changed?.Invoke(this, new A2UISurfaceChange(A2UISurfaceChangeKind.FunctionResults));
        return true;
    }

    // ── updates from the host ────────────────────────────────────────────────────

    internal void ApplyComponents(JsonArray list)
    {
        var changed = new List<string>();
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is not JsonObject o
                || A2UIValues.AsString(o["id"]) is not { Length: > 0 } id
                || A2UIValues.AsString(o["component"]) is not { Length: > 0 } type)
            {
                Report(A2UIErrorCodes.ValidationFailed, "A component needs a string 'id' and 'component'.", $"/components/{i}");
                continue;
            }
            if (type == "Surface")
            {
                Report(A2UIErrorCodes.ValidationFailed, "The Surface container can't be modified.", $"/components/{i}/component");
                continue;
            }

            var copy = (JsonObject)o.DeepClone();
            var component = new A2UIComponent(copy, id, type) { Version = (components.GetValueOrDefault(id)?.Version ?? -1) + 1 };
            components[id] = component;
            changed.Add(id);
            ValidateComponent(component, i);
        }

        ValidateComposition();
        if (changed.Count > 0)
            Changed?.Invoke(this, new A2UISurfaceChange(A2UISurfaceChangeKind.Components, changed));
    }

    void ValidateComponent(A2UIComponent component, int index)
    {
        var catalogId = component.CatalogId ?? CatalogId;
        if (catalogId is null)
        {
            Report(A2UIErrorCodes.ValidationFailed, $"Component '{component.Id}' has no catalog.", $"/components/{index}");
            return;
        }
        if (!Host.Catalogs.TryGet(catalogId, out var catalog))
        {
            Report(A2UIErrorCodes.ValidationFailed, $"Catalog '{catalogId}' is not supported.", $"/components/{index}");
            return;
        }
        if (!catalog.Components.TryGetValue(component.Type, out var definition))
        {
            Report(A2UIErrorCodes.ValidationFailed, $"Catalog '{catalogId}' has no component '{component.Type}'.", $"/components/{index}/component");
            return;
        }
        foreach (var required in definition.RequiredProperties)
        {
            if (!component.Properties.ContainsKey(required))
                Report(A2UIErrorCodes.ValidationFailed, $"Component '{component.Id}' ({component.Type}) is missing '{required}'.", $"/components/{index}");
        }
    }

    // allowedParents / allowedChildren, walked from the Surface container down.
    void ValidateComposition()
    {
        if (Root is null)
            return;

        var visited = new HashSet<string>(StringComparer.Ordinal);
        Visit("Surface", null, Root);

        void Visit(string parentType, A2UIComponentDefinition? parentDef, A2UIComponent component)
        {
            var def = GetDefinition(component);
            if (def?.AllowedParents is { } parents && !parents.Contains(parentType))
                Report(A2UIErrorCodes.UnallowedParent, $"'{component.Type}' can't be placed in '{parentType}'.", "/components/" + component.Id);
            if (parentDef?.AllowedChildren is { } allowed && !allowed.Contains(component.Type))
                Report(A2UIErrorCodes.UnallowedChild, $"'{parentType}' can't contain '{component.Type}'.", "/components/" + component.Id);

            if (def is null || !visited.Add(component.Id))
                return;

            foreach (var childId in ChildIds(component, def))
            {
                if (GetComponent(childId) is { } child)
                    Visit(component.Type, def, child);
            }
        }
    }

    internal static IEnumerable<string> ChildIds(A2UIComponent component, A2UIComponentDefinition definition)
    {
        foreach (var prop in definition.ChildProperties)
        {
            switch (prop.Kind)
            {
                case A2UIChildPropertyKind.Single when A2UIValues.AsString(component[prop.Name]) is { } id:
                    yield return id;
                    break;

                case A2UIChildPropertyKind.List when component[prop.Name] is JsonArray ids:
                    foreach (var id in ids.Select(A2UIValues.AsString).OfType<string>())
                        yield return id;
                    break;

                case A2UIChildPropertyKind.List when component[prop.Name] is JsonObject t && A2UIValues.AsString(t["componentId"]) is { } templateId:
                    yield return templateId;
                    break;

                case A2UIChildPropertyKind.ItemChild when component[prop.Name] is JsonArray items:
                    foreach (var item in items.OfType<JsonObject>())
                    {
                        if (A2UIValues.AsString(item[prop.ItemProperty!]) is { } id)
                            yield return id;
                    }
                    break;
            }
        }
    }

    internal void ApplyDataModel(string? path, JsonNode? value, bool hasValue)
    {
        if (!hasValue)
        {
            Report(A2UIErrorCodes.ValidationFailed, "updateDataModel needs a 'value' (use null to remove).", "/value");
            return;
        }
        SetData(string.IsNullOrEmpty(path) ? "/" : path, value?.DeepClone());
    }

    internal void MarkDeleted()
    {
        IsDeleted = true;
        Changed?.Invoke(this, new A2UISurfaceChange(A2UISurfaceChangeKind.Deleted));
    }

    // Validation errors are reported once per surface, not on every re-render.
    internal void Report(string code, string message, string? path = null)
    {
        if (reported.Add(code + "|" + path + "|" + message))
            Host.ReportError(new A2UIError(code, message, SurfaceId, path));
    }

    sealed class RemoteCall(string id)
    {
        public string Id { get; } = id;
        public JsonNode? Value { get; set; }
        public string? Error { get; set; }
    }
}
