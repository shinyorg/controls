using System.Text.Json.Nodes;

namespace Shiny.Controls.AgentUI.A2UI;

/// <summary>
/// Carries A2UI over AG-UI: surfaces arrive as <c>ACTIVITY_SNAPSHOT</c>s and actions go back as a
/// run's forwarded properties, following the AG-UI a2ui-middleware convention.
/// </summary>
/// <remarks>
/// <para>
/// Inbound: an activity of <see cref="ActivityType"/> (default <c>a2ui-surface</c>) carries its
/// operations in <c>content.a2ui_operations</c> (a bare array also works). Snapshots replace per
/// message id; when a new snapshot extends the last one, only the new operations are applied, so a
/// re-sent snapshot doesn't wipe what the user has typed. A snapshot that rewrites history rebuilds
/// that message's surfaces.
/// </para>
/// <para>
/// Outbound: an action starts a run with <c>forwardedProps.a2uiAction.userAction</c>. Every run
/// carries <c>a2uiRendererCapabilities</c> (and <c>a2uiRendererDataModel</c> when a surface asked for
/// it); errors and function responses ride along in <c>a2uiClientMessages</c>.
/// </para>
/// </remarks>
public sealed class A2UIAgentBinding : IDisposable
{
    /// <summary>The activity type A2UI surfaces arrive as.</summary>
    public const string DefaultActivityType = "a2ui-surface";
    /// <summary>Where an activity's content holds its A2UI messages.</summary>
    public const string OperationsKey = "a2ui_operations";

    readonly Dictionary<string, List<JsonNode>> applied = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<string>> owned = new(StringComparer.Ordinal);
    readonly List<JsonObject> pendingMessages = [];
    readonly Queue<JsonObject> pendingActions = new();

    /// <summary>Connects a runner's conversation to a surface host (a new one with the basic catalog when omitted).</summary>
    public A2UIAgentBinding(AgentRunner runner, A2UISurfaceHost? host = null, string activityType = DefaultActivityType)
    {
        Runner = runner ?? throw new ArgumentNullException(nameof(runner));
        Host = host ?? new A2UISurfaceHost();
        ActivityType = activityType;

        Runner.Thread.Changed += OnThreadChanged;
        Runner.RunPreparing += OnRunPreparing;
        Runner.BusyChanged += OnBusyChanged;
        Host.Outbound += OnOutbound;

        foreach (var activity in Runner.Thread.Items.OfType<AgentActivityItem>())
            Sync(activity);
    }

    /// <summary>The conversation's runner.</summary>
    public AgentRunner Runner { get; }

    /// <summary>The surfaces the conversation has created.</summary>
    public A2UISurfaceHost Host { get; }

    /// <summary>The activity type this binding renders.</summary>
    public string ActivityType { get; }

    /// <summary>The surfaces an activity (by its message id) created, in creation order - what to draw for that activity row.</summary>
    public IReadOnlyList<A2UISurface> GetSurfaces(string activityMessageId) =>
        owned.TryGetValue(activityMessageId, out var ids)
            ? ids.Select(Host.GetSurface).OfType<A2UISurface>().ToList()
            : [];

    /// <summary>True when an activity is an A2UI surface this binding renders.</summary>
    public bool Handles(AgentActivityItem activity) => activity.ActivityType == ActivityType;

    /// <summary>Disconnects from the runner and the host.</summary>
    public void Dispose()
    {
        Runner.Thread.Changed -= OnThreadChanged;
        Runner.RunPreparing -= OnRunPreparing;
        Runner.BusyChanged -= OnBusyChanged;
        Host.Outbound -= OnOutbound;
    }

    void OnThreadChanged(object? sender, AgentThreadChange change)
    {
        switch (change)
        {
            case { Kind: AgentThreadChangeKind.ItemAdded or AgentThreadChangeKind.ItemUpdated, Item: AgentActivityItem activity }:
                Sync(activity);
                break;

            case { Kind: AgentThreadChangeKind.ItemsReset }:
                foreach (var activity in Runner.Thread.Items.OfType<AgentActivityItem>())
                    Sync(activity);
                break;
        }
    }

    void Sync(AgentActivityItem activity)
    {
        if (!Handles(activity))
            return;

        var operations = (activity.Content as JsonObject)?[OperationsKey] as JsonArray ?? activity.Content as JsonArray;
        var incoming = operations?.ToList() ?? [];

        if (!applied.TryGetValue(activity.Id, out var previous))
        {
            previous = [];
            applied[activity.Id] = previous;
        }

        var extends = incoming.Count >= previous.Count && previous.Select((op, i) => JsonNode.DeepEquals(op, incoming[i])).All(x => x);
        if (!extends)
        {
            foreach (var id in owned.GetValueOrDefault(activity.Id) ?? [])
                Host.DeleteSurface(id);
            owned.Remove(activity.Id);
            previous.Clear();
        }

        for (var i = previous.Count; i < incoming.Count; i++)
        {
            var op = incoming[i]?.DeepClone();
            previous.Add(op!);
            // only a createSurface that actually creates a surface belongs to this activity; one that
            // reuses a live id is rejected (INTEGRITY_ERROR) and must not draw that surface here too
            var created = CreatedSurfaceId(op);
            var existed = created is not null && Host.GetSurface(created) is not null;
            Host.Apply(op);
            if (created is not null && !existed && Host.GetSurface(created) is not null)
                Track(activity.Id, created);
        }
    }

    static string? CreatedSurfaceId(JsonNode? op) =>
        op?["createSurface"]?["surfaceId"] is JsonValue v && v.TryGetValue<string>(out var surfaceId) ? surfaceId : null;

    void Track(string activityId, string surfaceId)
    {
        if (!owned.TryGetValue(activityId, out var list))
        {
            list = [];
            owned[activityId] = list;
        }
        if (!list.Contains(surfaceId))
            list.Add(surfaceId);
    }

    void OnOutbound(object? sender, A2UIOutboundMessage message)
    {
        if (message.Kind == A2UIOutboundKind.Action)
        {
            pendingActions.Enqueue((JsonObject)message.Body.DeepClone());
            FlushActions();
            return;
        }

        pendingMessages.Add((JsonObject)message.Message.DeepClone());

        // the agent is waiting on these, so deliver them now rather than with the next user turn
        if (message.Kind is A2UIOutboundKind.CallAgentFunction or A2UIOutboundKind.RendererFunctionResponse && !Runner.IsBusy)
            _ = Runner.RunAsync();
    }

    void OnBusyChanged(object? sender, EventArgs e)
    {
        if (!Runner.IsBusy)
            FlushActions();
    }

    void FlushActions()
    {
        if (Runner.IsBusy || !pendingActions.TryDequeue(out var action))
            return;

        _ = Runner.RunAsync(new JsonObject
        {
            ["a2uiAction"] = new JsonObject { ["userAction"] = action }
        });
    }

    void OnRunPreparing(object? sender, AgentRunPreparingEventArgs e)
    {
        e.ForwardedProperties["a2uiRendererCapabilities"] = Host.GetRendererCapabilities();
        if (Host.GetRendererDataModel() is { } model)
            e.ForwardedProperties["a2uiRendererDataModel"] = model;
        if (pendingMessages.Count > 0)
        {
            e.ForwardedProperties["a2uiClientMessages"] = new JsonArray(pendingMessages.Select(m => (JsonNode)m).ToArray());
            pendingMessages.Clear();
        }
    }
}
