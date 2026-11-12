using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shiny.Controls.AgentUI.A2UI;

/// <summary>What a renderer-to-agent message is.</summary>
public enum A2UIOutboundKind
{
    /// <summary>A user action (a Button tap).</summary>
    Action,
    /// <summary>A renderer-side error.</summary>
    Error,
    /// <summary>A call to an agent-side function.</summary>
    CallAgentFunction,
    /// <summary>The answer to the agent's <c>callRendererFunction</c>.</summary>
    RendererFunctionResponse
}

/// <summary>A renderer-to-agent message: <see cref="Message"/> is the full envelope (<c>{"version":"1.0", "&lt;kind&gt;": {...}}</c>).</summary>
public sealed record A2UIOutboundMessage(A2UIOutboundKind Kind, JsonObject Message, string? SurfaceId)
{
    /// <summary>The message body without the envelope (the <c>action</c> object, the <c>error</c> object, ...).</summary>
    public JsonObject Body => (JsonObject)Message.First(p => p.Key != "version").Value!;
}

/// <summary>
/// The renderer side of A2UI for one conversation (or page): applies agent messages, owns the
/// surfaces they create, and raises <see cref="Outbound"/> for everything the renderer sends back.
/// </summary>
/// <remarks>
/// Surface ids are unique for the host's lifetime: creating one that exists without deleting it
/// first is an <c>INTEGRITY_ERROR</c>. Not thread-safe; use it from the thread that renders.
/// </remarks>
public sealed class A2UISurfaceHost
{
    /// <summary>The A2UI version this renderer speaks.</summary>
    public const string ProtocolVersion = "1.0";

    static readonly string[] AgentMessageKeys = ["createSurface", "updateComponents", "updateDataModel", "deleteSurface", "callRendererFunction", "agentFunctionResponse"];

    readonly Dictionary<string, A2UISurface> surfaces = new(StringComparer.Ordinal);

    /// <summary>Creates a host. Without a registry it supports the basic catalog.</summary>
    public A2UISurfaceHost(A2UICatalogRegistry? catalogs = null) => Catalogs = catalogs ?? new A2UICatalogRegistry();

    /// <summary>The catalogs surfaces may use.</summary>
    public A2UICatalogRegistry Catalogs { get; }

    /// <summary>Formats numbers, currency, dates and plurals. Defaults to the current UI culture.</summary>
    public CultureInfo Culture { get; set; } = CultureInfo.CurrentCulture;

    /// <summary>The zone dates are shown in. Defaults to local.</summary>
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Local;

    /// <summary>Stamps action timestamps (tests pass a fixed one).</summary>
    public TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>What relative URLs (<c>openUrl</c>, media) resolve against.</summary>
    public Uri? BaseUri { get; set; }

    /// <summary>Opens a URL for <c>openUrl</c> (only ever http/https, only inside a user gesture). Unset, <c>openUrl</c> fails.</summary>
    public Action<Uri>? OpenUrl { get; set; }

    /// <summary>The live surfaces, by id.</summary>
    public IReadOnlyDictionary<string, A2UISurface> Surfaces => surfaces;

    /// <summary>Raised after a surface is created and its initial data and components applied.</summary>
    public event EventHandler<A2UISurface>? SurfaceCreated;
    /// <summary>Raised after a surface is deleted.</summary>
    public event EventHandler<A2UISurface>? SurfaceDeleted;

    /// <summary>Every renderer-to-agent message (actions, errors, function calls and responses).</summary>
    public event EventHandler<A2UIOutboundMessage>? Outbound;

    /// <summary>Raised alongside the outbound error message, for logging.</summary>
    public event EventHandler<A2UIError>? Error;

    /// <summary>The surface with this id, or null.</summary>
    public A2UISurface? GetSurface(string surfaceId) => surfaces.GetValueOrDefault(surfaceId);

    /// <summary>Applies a JSON string: one message, or an array of them (a JSONL stream's lines also work one at a time).</summary>
    public void Apply(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            ReportError(new A2UIError(A2UIErrorCodes.ValidationFailed, "Not valid JSON: " + ex.Message));
            return;
        }
        Apply(node);
    }

    /// <summary>Applies one agent message, or an array of them in order.</summary>
    public void Apply(JsonNode? message)
    {
        if (message is JsonArray list)
        {
            foreach (var m in list)
                Apply(m);
            return;
        }

        if (message is not JsonObject envelope)
        {
            ReportError(new A2UIError(A2UIErrorCodes.ValidationFailed, "An A2UI message must be a JSON object."));
            return;
        }

        var version = A2UIValues.AsString(envelope["version"]);
        if (version != ProtocolVersion)
        {
            ReportError(new A2UIError(A2UIErrorCodes.ValidationFailed, $"Unsupported A2UI version '{version}'; this renderer speaks {ProtocolVersion}.", Path: "/version"));
            return;
        }

        var keys = envelope.Select(p => p.Key).Where(k => k != "version").ToList();
        if (keys.Count != 1 || !AgentMessageKeys.Contains(keys[0]) || envelope[keys[0]] is not JsonObject body)
        {
            ReportError(new A2UIError(A2UIErrorCodes.ValidationFailed, "An A2UI message must hold exactly one of: " + string.Join(", ", AgentMessageKeys) + "."));
            return;
        }

        switch (keys[0])
        {
            case "createSurface": CreateSurface(body); break;
            case "updateComponents": UpdateComponents(body); break;
            case "updateDataModel": UpdateDataModel(body); break;
            case "deleteSurface": DeleteSurface(body); break;
            case "callRendererFunction": CallRendererFunction(body); break;
            case "agentFunctionResponse": AgentFunctionResponse(body); break;
        }
    }

    /// <summary>Removes a surface on the renderer's own initiative (the user dismissed it).</summary>
    public bool DeleteSurface(string surfaceId)
    {
        if (!surfaces.Remove(surfaceId, out var surface))
            return false;
        surface.MarkDeleted();
        SurfaceDeleted?.Invoke(this, surface);
        return true;
    }

    /// <summary>The <c>a2uiRendererCapabilities</c> object to put in transport metadata.</summary>
    public JsonObject GetRendererCapabilities() => new()
    {
        [ProtocolVersion] = new JsonObject
        {
            ["supportedCatalogIds"] = new JsonArray(Catalogs.SupportedCatalogIds.Select(id => (JsonNode)JsonValue.Create(id)).ToArray())
        }
    };

    /// <summary>The <c>a2uiRendererDataModel</c> object for the surfaces that asked for it (<c>sendDataModel</c>), or null when none did.</summary>
    public JsonObject? GetRendererDataModel()
    {
        var models = new JsonObject();
        foreach (var s in surfaces.Values.Where(s => s.SendDataModel))
            models[s.SurfaceId] = s.DataModel?.DeepClone() ?? new JsonObject();

        return models.Count == 0 ? null : new JsonObject { ["version"] = ProtocolVersion, ["surfaces"] = models };
    }

    // ── agent messages ───────────────────────────────────────────────────────────

    void CreateSurface(JsonObject body)
    {
        if (A2UIValues.AsString(body["surfaceId"]) is not { Length: > 0 } id)
        {
            ReportError(new A2UIError(A2UIErrorCodes.ValidationFailed, "createSurface needs a 'surfaceId'.", Path: "/createSurface/surfaceId"));
            return;
        }
        if (body.ContainsKey("theme"))
        {
            ReportError(new A2UIError(A2UIErrorCodes.ValidationFailed, "createSurface may not carry 'theme' in A2UI 1.0; surfaces take the renderer's theme.", id, "/createSurface/theme"));
            return;
        }
        if (surfaces.ContainsKey(id))
        {
            ReportError(new A2UIError(A2UIErrorCodes.IntegrityError, $"Surface '{id}' already exists; delete it before creating it again.", id));
            return;
        }

        var surface = new A2UISurface(this, id, A2UIValues.AsString(body["catalogId"]), A2UIValues.ToBoolean(body["sendDataModel"]), body["metadata"]?.DeepClone() as JsonObject);
        surfaces[id] = surface;

        // the data model first, so components bind against it on their first resolution
        if (body["dataModel"] is JsonObject model)
            surface.ApplyDataModel("/", model, hasValue: true);
        if (body["components"] is JsonArray components)
            surface.ApplyComponents(components);

        SurfaceCreated?.Invoke(this, surface);
    }

    void UpdateComponents(JsonObject body)
    {
        if (Require(body, "updateComponents") is not { } surface)
            return;
        if (body["components"] is not JsonArray components)
        {
            surface.Report(A2UIErrorCodes.ValidationFailed, "updateComponents needs a 'components' array.", "/updateComponents/components");
            return;
        }
        surface.ApplyComponents(components);
    }

    void UpdateDataModel(JsonObject body)
    {
        if (Require(body, "updateDataModel") is not { } surface)
            return;
        surface.ApplyDataModel(A2UIValues.AsString(body["path"]), body["value"], body.ContainsKey("value"));
    }

    void DeleteSurface(JsonObject body)
    {
        if (Require(body, "deleteSurface") is { } surface)
            DeleteSurface(surface.SurfaceId);
    }

    void CallRendererFunction(JsonObject body)
    {
        var callId = A2UIValues.AsString(body["functionCallId"]);
        if (callId is null || body["callFunction"] is not JsonObject call || A2UIValues.AsString(call["@call"]) is not { } name)
        {
            ReportError(new A2UIError(A2UIErrorCodes.ValidationFailed, "callRendererFunction needs a 'functionCallId' and a 'callFunction'.", FunctionCallId: callId));
            return;
        }

        var surfaceId = A2UIValues.AsString(body["surfaceId"]);
        var surface = surfaceId is null ? null : GetSurface(surfaceId);
        var catalogId = A2UIValues.AsString(call["catalogId"]);

        if (catalogId is null || !Catalogs.TryGet(catalogId, out var catalog))
        {
            Respond(callId, null, A2UIErrorCodes.InvalidFunctionCall, catalogId is null ? "callRendererFunction requires a catalogId." : $"Unknown catalog '{catalogId}'.");
            return;
        }
        if (!catalog.Functions.TryGetValue(name, out var definition) || !catalog.TryGetImplementation(name, out var implementation))
        {
            Respond(callId, null, A2UIErrorCodes.InvalidFunctionCall, $"Catalog '{catalogId}' has no renderer function '{name}'.");
            return;
        }
        if (definition.AllowedCallers == A2UIAllowedCallers.RendererOnly)
        {
            Respond(callId, null, A2UIErrorCodes.InvalidFunctionCall, $"Function '{name}' can't be called by the agent.");
            return;
        }

        try
        {
            // agent-invoked functions run outside any binding; a detached surface gives them an empty model
            var scope = (surface ?? new A2UISurface(this, surfaceId ?? "", catalogId, false, null)).RootScope;
            var value = implementation(new A2UIFunctionContext(name, catalogId, call["args"] as JsonObject ?? new JsonObject(), scope));
            Respond(callId, value, null, null);
        }
        catch (Exception ex)
        {
            Respond(callId, null, A2UIErrorCodes.ExecutionError, ex.Message);
        }

        void Respond(string id, JsonNode? value, string? code, string? message)
        {
            var response = new JsonObject { ["functionCallId"] = id };
            if (code is null)
                response["value"] = value?.DeepClone();
            else
                response["error"] = new JsonObject { ["code"] = code, ["message"] = message };
            Send(A2UIOutboundKind.RendererFunctionResponse, "rendererFunctionResponse", response, surfaceId);
        }
    }

    void AgentFunctionResponse(JsonObject body)
    {
        if (A2UIValues.AsString(body["functionCallId"]) is not { } id)
            return;

        var error = body["error"] is JsonObject e ? A2UIValues.AsString(e["message"]) ?? A2UIValues.AsString(e["code"]) ?? "Agent function failed." : null;
        foreach (var surface in surfaces.Values)
        {
            if (surface.ResolveAgentFunction(id, body["value"], error))
            {
                if (error is not null)
                    surface.Report(A2UIErrorCodes.ExpressionError, error);
                return;
            }
        }
    }

    A2UISurface? Require(JsonObject body, string message)
    {
        var id = A2UIValues.AsString(body["surfaceId"]);
        if (id is not null && surfaces.TryGetValue(id, out var surface))
            return surface;

        ReportError(new A2UIError(A2UIErrorCodes.IntegrityError, $"{message}: surface '{id}' does not exist.", id));
        return null;
    }

    // ── outbound ─────────────────────────────────────────────────────────────────

    internal void Send(A2UIOutboundKind kind, string key, JsonObject body, string? surfaceId)
        => Outbound?.Invoke(this, new A2UIOutboundMessage(kind, new JsonObject { ["version"] = ProtocolVersion, [key] = body }, surfaceId));

    internal void ReportError(A2UIError error)
    {
        Error?.Invoke(this, error);

        var body = new JsonObject { ["code"] = error.Code, ["message"] = error.Message };
        // surfaceId and functionCallId are mutually exclusive
        if (error.FunctionCallId is not null)
            body["functionCallId"] = error.FunctionCallId;
        else if (error.SurfaceId is not null)
            body["surfaceId"] = error.SurfaceId;
        if (error.Path is not null)
            body["path"] = error.Path;

        Send(A2UIOutboundKind.Error, "error", body, error.SurfaceId);
    }
}
