using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AGUI.Abstractions;
using Shiny.Controls.AgentUI.Json;

namespace Shiny.Controls.AgentUI;

/// <summary>
/// Folds an AG-UI event stream into a renderable conversation. This is the only thing the agent
/// renderers bind to.
/// </summary>
/// <remarks>
/// <para>
/// Pure and synchronous: <see cref="Apply"/> mutates the model and raises <see cref="Changed"/>
/// before it returns. It is not thread-safe; apply events on the thread that renders (the
/// <see cref="AgentRunner"/> stays on its caller's synchronization context for exactly this reason).
/// </para>
/// <para>
/// Chunk events (<c>TEXT_MESSAGE_CHUNK</c>, <c>TOOL_CALL_CHUNK</c>, <c>REASONING_MESSAGE_CHUNK</c>)
/// are normalised to their start/content/end form: a chunk opens its entity, later chunks for the
/// same id append, and the entity closes when any other event arrives. Renderers see one shape.
/// Event types this version doesn't know are ignored, so a newer agent never breaks an older app.
/// </para>
/// </remarks>
public sealed class AgentThread
{
    readonly List<AgentItem> items = [];
    readonly Dictionary<string, AgentItem> index = new(StringComparer.Ordinal);
    readonly List<AgentStep> steps = [];
    readonly List<AgentSubagent> subagents = [];
    readonly TimeProvider time;
    (string Key, AgentItem Item)? openChunk;

    /// <summary>Creates an empty conversation. <paramref name="timeProvider"/> stamps reasoning times (tests pass a fixed one).</summary>
    public AgentThread(string? threadId = null, TimeProvider? timeProvider = null)
    {
        ThreadId = threadId;
        time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Raised after every change, on the thread that called <see cref="Apply"/>.</summary>
    public event EventHandler<AgentThreadChange>? Changed;

    /// <summary>Raised for <c>CUSTOM</c> and <c>RAW</c> events, which the thread doesn't render.</summary>
    public event EventHandler<BaseEvent>? CustomEventReceived;

    /// <summary>The AG-UI thread, from the constructor or the first <c>RUN_STARTED</c>.</summary>
    public string? ThreadId { get; private set; }

    /// <summary>The current (or last) run.</summary>
    public string? RunId { get; private set; }

    /// <summary>Where the current (or last) run is.</summary>
    public AgentRunStatus RunStatus { get; private set; }

    /// <summary>The conversation, in the order items first appeared.</summary>
    public IReadOnlyList<AgentItem> Items => items;

    /// <summary>Steps running right now, outermost first.</summary>
    public IReadOnlyList<AgentStep> Steps => steps;

    /// <summary>Every sub-agent the conversation has started.</summary>
    public IReadOnlyList<AgentSubagent> Subagents => subagents;

    /// <summary>What the last run stopped to ask. Answer with <see cref="AgentRunner.ResumeAsync"/>.</summary>
    public IReadOnlyList<AGUIInterrupt> PendingInterrupts
    {
        get => pendingInterrupts;
        private set
        {
            if (pendingInterrupts.Count == 0 && value.Count == 0)
                return;
            pendingInterrupts = value;
            Raise(AgentThreadChangeKind.InterruptsChanged);
        }
    }
    IReadOnlyList<AGUIInterrupt> pendingInterrupts = [];

    /// <summary>Tool calls the last successful run left for the client to complete.</summary>
    public IReadOnlyList<string> PendingToolCallIds { get; private set; } = [];

    /// <summary>The shared agent state, with every snapshot and delta applied. Null until the agent sends one.</summary>
    public JsonNode? State { get; private set; }

    /// <summary>The last run's <c>result</c>, when it sent one.</summary>
    public JsonElement? Result { get; private set; }

    /// <summary>Token usage the last run reported.</summary>
    public IReadOnlyList<TokenUsage> Usage { get; private set; } = [];

    /// <summary>The last run error's message, when <see cref="RunStatus"/> is Failed.</summary>
    public string? LastError { get; private set; }

    /// <summary>True while a run is streaming.</summary>
    public bool IsRunning => RunStatus == AgentRunStatus.Running;

    /// <summary>The tool call with this id, or null.</summary>
    public AgentToolCallItem? FindToolCall(string toolCallId) => index.GetValueOrDefault(ToolKey(toolCallId)) as AgentToolCallItem;

    /// <summary>The message with this id, or null.</summary>
    public AgentMessageItem? FindMessage(string messageId) => index.GetValueOrDefault(MessageKey(messageId)) as AgentMessageItem;

    /// <summary>The activity with this message id, or null.</summary>
    public AgentActivityItem? FindActivity(string messageId) => index.GetValueOrDefault(ActivityKey(messageId)) as AgentActivityItem;

    /// <summary>The sub-agent with this run id, or null.</summary>
    public AgentSubagent? FindSubagent(string subagentRunId) => subagents.FirstOrDefault(x => x.Id == subagentRunId);

    /// <summary>Applies one event.</summary>
    public void Apply(BaseEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (openChunk is { } chunk && !ContinuesChunk(e, chunk.Key))
            CloseChunk();

        switch (e)
        {
            case RunStartedEvent x: OnRunStarted(x); break;
            case RunFinishedEvent x: OnRunFinished(x); break;
            case RunErrorEvent x: OnRunError(x); break;

            case StepStartedEvent x:
                steps.Add(new AgentStep(x.StepName, x.SubagentRunId));
                Raise(AgentThreadChangeKind.StepsChanged);
                break;

            case StepFinishedEvent x:
                var step = steps.FindLastIndex(s => s.Name == x.StepName && s.SubagentRunId == x.SubagentRunId);
                if (step >= 0)
                {
                    steps.RemoveAt(step);
                    Raise(AgentThreadChangeKind.StepsChanged);
                }
                break;

            case TextMessageStartEvent x:
                StartMessage(x.MessageId, x.Role, x.Name, x.SubagentRunId);
                break;

            case TextMessageContentEvent x:
                AppendMessage(x.MessageId, x.Delta, x.SubagentRunId);
                break;

            case TextMessageEndEvent x:
                if (FindMessage(x.MessageId) is { IsStreaming: true } ended)
                {
                    ended.IsStreaming = false;
                    Updated(ended);
                }
                break;

            case TextMessageChunkEvent x: OnTextChunk(x); break;

            case ReasoningStartEvent x:
                GetOrAddReasoning(x.MessageId, x.SubagentRunId, streaming: true);
                break;

            case ReasoningMessageStartEvent x:
                GetOrAddReasoning(x.MessageId, x.SubagentRunId, streaming: true);
                break;

            case ReasoningMessageContentEvent x:
                AppendReasoning(x.MessageId, x.Delta, x.SubagentRunId);
                break;

            case ReasoningMessageChunkEvent x: OnReasoningChunk(x); break;

            case ReasoningMessageEndEvent:
                // The reasoning block stays open until REASONING_END; a block can hold several messages.
                break;

            case ReasoningEndEvent x:
                if (index.GetValueOrDefault(ReasoningKey(x.MessageId)) is AgentReasoningItem { IsStreaming: true } r)
                    EndReasoning(r);
                break;

            case ReasoningEncryptedValueEvent x: OnEncryptedValue(x); break;

            case ToolCallStartEvent x:
                StartToolCall(x.ToolCallId, x.ToolCallName, x.ParentMessageId, x.SubagentRunId);
                break;

            case ToolCallArgsEvent x:
                AppendToolArgs(x.ToolCallId, x.Delta, x.SubagentRunId);
                break;

            case ToolCallEndEvent x:
                if (FindToolCall(x.ToolCallId) is { } endedCall)
                    EndToolCall(endedCall);
                break;

            case ToolCallChunkEvent x: OnToolCallChunk(x); break;

            case ToolCallResultEvent x: OnToolCallResult(x); break;

            case StateSnapshotEvent x:
                State = ToNode(x.Snapshot);
                Raise(AgentThreadChangeKind.StateChanged);
                break;

            case StateDeltaEvent x:
                State = JsonPatch.Apply(State, x.Delta);
                Raise(AgentThreadChangeKind.StateChanged);
                break;

            case MessagesSnapshotEvent x:
                LoadMessages(x.Messages);
                break;

            case ActivitySnapshotEvent x: OnActivitySnapshot(x); break;
            case ActivityDeltaEvent x: OnActivityDelta(x); break;

            case SubagentStartedEvent x: OnSubagentStarted(x); break;
            case SubagentFinishedEvent x: OnSubagentFinished(x); break;
            case SubagentErrorEvent x: OnSubagentError(x); break;

            case CustomEvent or RawEvent:
                CustomEventReceived?.Invoke(this, e);
                break;
        }
    }

    /// <summary>Adds a message the client is sending, before the run that carries it starts.</summary>
    public AgentMessageItem AddLocalMessage(AGUIUserMessage message)
    {
        var item = new AgentMessageItem(message.Id, AGUIRoles.User)
        {
            Name = message.Name,
            Text = TextOf(message.Content),
            Content = message.Content.IsText ? (AGUIContent?)null : message.Content
        };
        Add(MessageKey(item.Id), item);
        return item;
    }

    /// <summary>Records a frontend tool's result (or failure) against its call.</summary>
    public void SetToolResult(string toolCallId, string? content, string? error = null)
    {
        if (FindToolCall(toolCallId) is not { } call)
            return;

        call.Result = content ?? "";
        call.Error = error;
        call.Status = error is null ? AgentToolCallStatus.Completed : AgentToolCallStatus.Failed;
        call.ResultMessageId ??= "result-" + toolCallId;
        Updated(call);
    }

    internal void SetToolRunning(AgentToolCallItem call)
    {
        call.Status = AgentToolCallStatus.Running;
        Updated(call);
    }

    /// <summary>Replaces the shared state, for the client's own edits; it goes to the agent with the next run.</summary>
    public void SetState(JsonNode? state)
    {
        State = state;
        Raise(AgentThreadChangeKind.StateChanged);
    }

    /// <summary>Marks the current run cancelled (the client stopped reading it).</summary>
    public void MarkCancelled()
    {
        if (RunStatus != AgentRunStatus.Running)
            return;
        CloseStreaming();
        SetRunStatus(AgentRunStatus.Cancelled);
    }

    // Stop between runs (a frontend tool was waiting on the user): the last run completed, but the turn didn't.
    internal void MarkTurnCancelled()
    {
        if (RunStatus is not (AgentRunStatus.Running or AgentRunStatus.Completed))
            return;
        CloseStreaming();
        SetRunStatus(AgentRunStatus.Cancelled);
    }

    /// <summary>Marks the current run failed for a reason outside the stream (a transport error).</summary>
    public void MarkFailed(string message, string? code = null)
    {
        CloseStreaming();
        LastError = message;
        AddError(message, code, null);
        SetRunStatus(AgentRunStatus.Failed);
    }

    /// <summary>Clears the conversation.</summary>
    public void Clear()
    {
        items.Clear();
        index.Clear();
        steps.Clear();
        subagents.Clear();
        openChunk = null;
        State = null;
        Result = null;
        PendingInterrupts = [];
        PendingToolCallIds = [];
        LastError = null;
        RunStatus = AgentRunStatus.Idle;
        Raise(AgentThreadChangeKind.ItemsReset);
    }

    /// <summary>
    /// The conversation as AG-UI messages, ready to send as <see cref="RunAgentInput.Messages"/>:
    /// tool calls are folded into their assistant message, results become tool messages, and
    /// encrypted reasoning rides along so the agent can continue it.
    /// </summary>
    /// <param name="includeActivity">Include activity messages (A2UI surfaces and other widgets). The
    /// official AG-UI .NET server rejects the <c>activity</c> role in run input, so
    /// <see cref="AgentRunner"/> leaves them out unless <see cref="AgentRunner.SendActivityMessages"/> is set.</param>
    /// <param name="includeReasoning">Include reasoning messages (and their encrypted values). Left out by
    /// <see cref="AgentRunner"/> unless <see cref="AgentRunner.SendReasoningMessages"/> is set, for the same reason.</param>
    public List<AGUIMessage> ToAGUIMessages(bool includeActivity = true, bool includeReasoning = true)
    {
        var list = new List<AGUIMessage>(items.Count);
        var assistants = new Dictionary<string, AGUIAssistantMessage>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            switch (item)
            {
                case AgentMessageItem { Role: AGUIRoles.User } m:
                    list.Add(new AGUIUserMessage { Id = m.Id, Name = m.Name, EncryptedValue = m.EncryptedValue, Content = m.Content ?? new AGUIContent(m.Text), SubagentRunId = m.SubagentRunId });
                    break;

                case AgentMessageItem { Role: AGUIRoles.Assistant } m:
                    var assistant = new AGUIAssistantMessage { Id = m.Id, Name = m.Name, Content = m.Text, EncryptedValue = m.EncryptedValue, SubagentRunId = m.SubagentRunId };
                    assistants[m.Id] = assistant;
                    list.Add(assistant);
                    break;

                case AgentMessageItem { Role: AGUIRoles.System } m:
                    list.Add(new AGUISystemMessage { Id = m.Id, Name = m.Name, Content = m.Text, EncryptedValue = m.EncryptedValue });
                    break;

                case AgentMessageItem { Role: AGUIRoles.Developer } m:
                    list.Add(new AGUIDeveloperMessage { Id = m.Id, Name = m.Name, Content = m.Text, EncryptedValue = m.EncryptedValue });
                    break;

                case AgentReasoningItem r when includeReasoning:
                    list.Add(new AGUIReasoningMessage { Id = r.Id, Content = r.Text, EncryptedValue = r.EncryptedValue, SubagentRunId = r.SubagentRunId });
                    break;

                case AgentToolCallItem t:
                    var parentId = t.ParentMessageId ?? "call-" + t.ToolCallId;
                    if (!assistants.TryGetValue(parentId, out var parent))
                    {
                        parent = new AGUIAssistantMessage { Id = parentId, SubagentRunId = t.SubagentRunId };
                        assistants[parentId] = parent;
                        list.Add(parent);
                    }
                    parent.ToolCalls ??= [];
                    parent.ToolCalls.Add(new AGUIToolCall
                    {
                        Id = t.ToolCallId,
                        Type = "function",
                        Function = new AGUIToolCallFunction { Name = t.Name, Arguments = t.ArgumentsText.Length == 0 ? "{}" : t.ArgumentsText },
                        EncryptedValue = t.EncryptedValue
                    });
                    if (t.HasResult)
                    {
                        list.Add(new AGUIToolMessage
                        {
                            Id = t.ResultMessageId ?? "result-" + t.ToolCallId,
                            ToolCallId = t.ToolCallId,
                            Content = new AGUIContent(t.Result ?? ""),
                            Error = t.Error,
                            SubagentRunId = t.SubagentRunId
                        });
                    }
                    break;

                case AgentActivityItem a when includeActivity:
                    list.Add(new AGUIActivityMessage { Id = a.Id, ActivityType = a.ActivityType, Content = ToElement(a.Content) });
                    break;
            }
        }
        return list;
    }

    // ── run lifecycle ────────────────────────────────────────────────────────────

    void OnRunStarted(RunStartedEvent e)
    {
        ThreadId ??= e.ThreadId;
        RunId = e.RunId;
        steps.Clear();
        PendingInterrupts = [];
        PendingToolCallIds = [];
        LastError = null;
        Result = null;
        SetRunStatus(AgentRunStatus.Running);
    }

    void OnRunFinished(RunFinishedEvent e)
    {
        CloseStreaming();
        Result = e.Result;
        Usage = e.Usage is { Count: > 0 } u ? [.. u] : [];
        steps.Clear();

        switch (e.Outcome)
        {
            case RunFinishedInterruptOutcome interrupt:
                PendingInterrupts = interrupt.Interrupts is { } list ? [.. list] : [];
                SetRunStatus(AgentRunStatus.Interrupted);
                break;

            case RunFinishedCancelledOutcome:
                SetRunStatus(AgentRunStatus.Cancelled);
                break;

            case RunFinishedSuccessOutcome success:
                PendingToolCallIds = success.PendingToolCallIds is { } pending ? [.. pending] : [];
                SetRunStatus(AgentRunStatus.Completed);
                break;

            default:
                SetRunStatus(AgentRunStatus.Completed);
                break;
        }
    }

    void OnRunError(RunErrorEvent e)
    {
        CloseStreaming();
        steps.Clear();
        LastError = e.Message;
        Usage = e.Usage is { Count: > 0 } u ? [.. u] : [];
        AddError(e.Message, e.Code, null);
        SetRunStatus(AgentRunStatus.Failed);
    }

    void SetRunStatus(AgentRunStatus status)
    {
        RunStatus = status;
        Raise(AgentThreadChangeKind.RunStatusChanged);
    }

    // ── text ─────────────────────────────────────────────────────────────────────

    AgentMessageItem StartMessage(string id, string? role, string? name, string? subagent)
    {
        if (FindMessage(id) is { } existing)
        {
            existing.IsStreaming = true;
            Updated(existing);
            return existing;
        }

        var item = new AgentMessageItem(id, string.IsNullOrEmpty(role) ? AGUIRoles.Assistant : role)
        {
            Name = name,
            IsStreaming = true,
            SubagentRunId = subagent,
            RunId = RunId
        };
        Add(MessageKey(id), item);
        return item;
    }

    void AppendMessage(string id, string? delta, string? subagent)
    {
        var item = FindMessage(id) ?? StartMessage(id, AGUIRoles.Assistant, null, subagent);
        if (string.IsNullOrEmpty(delta))
            return;
        item.Text += delta;
        Updated(item);
    }

    void OnTextChunk(TextMessageChunkEvent e)
    {
        var id = e.MessageId ?? (openChunk is { Item: AgentMessageItem open } ? open.Id : null);
        if (id is null)
            return;

        var item = FindMessage(id);
        if (item is null || !item.IsStreaming)
            item = StartMessage(id, e.Role, e.Name, e.SubagentRunId);
        openChunk = (MessageKey(id), item);
        AppendMessage(id, e.Delta, e.SubagentRunId);
    }

    // ── reasoning ────────────────────────────────────────────────────────────────

    AgentReasoningItem GetOrAddReasoning(string id, string? subagent, bool streaming)
    {
        if (index.GetValueOrDefault(ReasoningKey(id)) is AgentReasoningItem existing)
            return existing;

        var item = new AgentReasoningItem(id)
        {
            IsStreaming = streaming,
            StartedAt = time.GetUtcNow(),
            SubagentRunId = subagent,
            RunId = RunId
        };
        Add(ReasoningKey(id), item);
        return item;
    }

    void AppendReasoning(string id, string? delta, string? subagent)
    {
        var item = GetOrAddReasoning(id, subagent, streaming: true);
        if (string.IsNullOrEmpty(delta))
            return;
        item.Text += delta;
        Updated(item);
    }

    void OnReasoningChunk(ReasoningMessageChunkEvent e)
    {
        var id = e.MessageId ?? (openChunk is { Item: AgentReasoningItem open } ? open.Id : null);
        if (id is null)
            return;

        var item = GetOrAddReasoning(id, e.SubagentRunId, streaming: true);
        if (string.IsNullOrEmpty(e.Delta))
        {
            // an empty delta closes a chunked reasoning message
            EndReasoning(item);
            openChunk = null;
            return;
        }
        openChunk = (ReasoningKey(id), item);
        AppendReasoning(id, e.Delta, e.SubagentRunId);
    }

    void EndReasoning(AgentReasoningItem item)
    {
        if (!item.IsStreaming)
            return;
        item.IsStreaming = false;
        item.EndedAt = time.GetUtcNow();
        Updated(item);
    }

    void OnEncryptedValue(ReasoningEncryptedValueEvent e)
    {
        AgentItem? target = e.Subtype == "tool-call"
            ? FindToolCall(e.EntityId)
            : index.GetValueOrDefault(ReasoningKey(e.EntityId)) ?? FindMessage(e.EntityId);

        switch (target)
        {
            case AgentReasoningItem r: r.EncryptedValue = e.EncryptedValue; break;
            case AgentMessageItem m: m.EncryptedValue = e.EncryptedValue; break;
            case AgentToolCallItem t: t.EncryptedValue = e.EncryptedValue; break;
            default: return;
        }
        Updated(target);
    }

    // ── tool calls ───────────────────────────────────────────────────────────────

    AgentToolCallItem StartToolCall(string id, string? name, string? parentMessageId, string? subagent)
    {
        if (FindToolCall(id) is { } existing)
            return existing;

        var item = new AgentToolCallItem(id, name ?? "")
        {
            ParentMessageId = parentMessageId,
            Status = AgentToolCallStatus.Streaming,
            SubagentRunId = subagent,
            RunId = RunId
        };
        Add(ToolKey(id), item);
        return item;
    }

    void AppendToolArgs(string id, string? delta, string? subagent)
    {
        var item = FindToolCall(id) ?? StartToolCall(id, null, null, subagent);
        if (string.IsNullOrEmpty(delta))
            return;
        item.ArgumentsText += delta;
        Updated(item);
    }

    void EndToolCall(AgentToolCallItem item)
    {
        if (item.Status != AgentToolCallStatus.Streaming)
            return;

        item.Arguments = TryParse(item.ArgumentsText.Length == 0 ? "{}" : item.ArgumentsText);
        item.Status = AgentToolCallStatus.Ready;
        Updated(item);
    }

    void OnToolCallChunk(ToolCallChunkEvent e)
    {
        var id = e.ToolCallId ?? (openChunk is { Item: AgentToolCallItem open } ? open.Id : null);
        if (id is null)
            return;

        var item = FindToolCall(id) ?? StartToolCall(id, e.ToolCallName, e.ParentMessageId, e.SubagentRunId);
        openChunk = (ToolKey(id), item);
        AppendToolArgs(id, e.Delta, e.SubagentRunId);
    }

    void OnToolCallResult(ToolCallResultEvent e)
    {
        var call = FindToolCall(e.ToolCallId) ?? StartToolCall(e.ToolCallId, null, null, e.SubagentRunId);
        if (call.Status == AgentToolCallStatus.Streaming)
            EndToolCall(call);

        call.Result = TextOf(e.Content);
        call.ResultMessageId = e.MessageId;
        call.Status = AgentToolCallStatus.Completed;
        Updated(call);
    }

    // ── activity ─────────────────────────────────────────────────────────────────

    void OnActivitySnapshot(ActivitySnapshotEvent e)
    {
        var existing = FindActivity(e.MessageId);
        if (existing is not null && e.Replace == false)
            return;

        if (existing is null)
        {
            var item = new AgentActivityItem(e.MessageId, e.ActivityType)
            {
                Content = ToNode(e.Content),
                SubagentRunId = e.SubagentRunId,
                RunId = RunId
            };
            Add(ActivityKey(e.MessageId), item);
            return;
        }

        existing.ActivityType = e.ActivityType;
        existing.Content = ToNode(e.Content);
        Updated(existing);
    }

    void OnActivityDelta(ActivityDeltaEvent e)
    {
        if (FindActivity(e.MessageId) is not { } item)
            return;
        item.Content = JsonPatch.Apply(item.Content, e.Patch);
        Updated(item);
    }

    // ── sub-agents ───────────────────────────────────────────────────────────────

    void OnSubagentStarted(SubagentStartedEvent e)
    {
        if (e.SubagentRunId is null)
            return;

        var sub = FindSubagent(e.SubagentRunId);
        if (sub is null)
        {
            sub = new AgentSubagent(e.SubagentRunId, e.Name ?? e.SubagentRunId);
            subagents.Add(sub);
        }
        sub.Description = e.Description;
        sub.ParentSubagentRunId = e.ParentSubagentRunId;
        sub.ParentToolCallId = e.ParentToolCallId;
        sub.ParentMessageId = e.ParentMessageId;
        sub.Status = AgentSubagentStatus.Running;
        Raise(AgentThreadChangeKind.SubagentsChanged);
    }

    void OnSubagentFinished(SubagentFinishedEvent e)
    {
        if (e.SubagentRunId is null || FindSubagent(e.SubagentRunId) is not { } sub)
            return;

        sub.Result = e.Result;
        if (e.Outcome is SubagentFinishedSuspendedOutcome suspended)
        {
            sub.Status = AgentSubagentStatus.Suspended;
            sub.InterruptIds = suspended.InterruptIds is { } ids ? [.. ids] : [];
        }
        else
        {
            sub.Status = AgentSubagentStatus.Completed;
        }
        CloseStreaming(e.SubagentRunId);
        Raise(AgentThreadChangeKind.SubagentsChanged);
    }

    void OnSubagentError(SubagentErrorEvent e)
    {
        if (e.SubagentRunId is null || FindSubagent(e.SubagentRunId) is not { } sub)
            return;

        sub.Status = AgentSubagentStatus.Failed;
        sub.Error = e.Message;
        CloseStreaming(e.SubagentRunId);
        AddError(e.Message ?? "Sub-agent failed.", e.Code, e.SubagentRunId);
        Raise(AgentThreadChangeKind.SubagentsChanged);
    }

    // ── history ──────────────────────────────────────────────────────────────────

    void LoadMessages(IList<AGUIMessage>? messages)
    {
        items.Clear();
        index.Clear();
        openChunk = null;

        foreach (var message in messages ?? [])
        {
            switch (message)
            {
                case AGUIUserMessage m:
                    AddQuiet(MessageKey(m.Id), new AgentMessageItem(m.Id, AGUIRoles.User)
                    {
                        Name = m.Name,
                        Text = TextOf(m.Content),
                        Content = m.Content.IsText ? (AGUIContent?)null : m.Content,
                        EncryptedValue = m.EncryptedValue,
                        SubagentRunId = m.SubagentRunId
                    });
                    break;

                case AGUIAssistantMessage m:
                    if (!string.IsNullOrEmpty(m.Content) || m.ToolCalls is not { Count: > 0 })
                    {
                        AddQuiet(MessageKey(m.Id), new AgentMessageItem(m.Id, AGUIRoles.Assistant)
                        {
                            Name = m.Name,
                            Text = m.Content ?? "",
                            EncryptedValue = m.EncryptedValue,
                            SubagentRunId = m.SubagentRunId
                        });
                    }
                    foreach (var call in m.ToolCalls ?? [])
                    {
                        AddQuiet(ToolKey(call.Id), new AgentToolCallItem(call.Id, call.Function?.Name ?? "")
                        {
                            ParentMessageId = m.Id,
                            ArgumentsText = call.Function?.Arguments ?? "",
                            Arguments = TryParse(call.Function?.Arguments ?? "{}"),
                            Status = AgentToolCallStatus.Ready,
                            EncryptedValue = call.EncryptedValue,
                            SubagentRunId = m.SubagentRunId
                        });
                    }
                    break;

                case AGUIToolMessage m:
                    if (FindToolCall(m.ToolCallId) is { } target)
                    {
                        target.Result = TextOf(m.Content);
                        target.Error = m.Error;
                        target.ResultMessageId = m.Id;
                        target.Status = m.Error is null ? AgentToolCallStatus.Completed : AgentToolCallStatus.Failed;
                    }
                    break;

                case AGUIReasoningMessage m:
                    AddQuiet(ReasoningKey(m.Id), new AgentReasoningItem(m.Id)
                    {
                        Text = m.Content ?? "",
                        EncryptedValue = m.EncryptedValue,
                        SubagentRunId = m.SubagentRunId
                    });
                    break;

                case AGUIActivityMessage m:
                    AddQuiet(ActivityKey(m.Id), new AgentActivityItem(m.Id, m.ActivityType) { Content = ToNode(m.Content) });
                    break;

                case AGUISystemMessage m:
                    AddQuiet(MessageKey(m.Id), new AgentMessageItem(m.Id, AGUIRoles.System) { Name = m.Name, Text = m.Content ?? "" });
                    break;

                case AGUIDeveloperMessage m:
                    AddQuiet(MessageKey(m.Id), new AgentMessageItem(m.Id, AGUIRoles.Developer) { Name = m.Name, Text = m.Content ?? "" });
                    break;
            }
        }
        Raise(AgentThreadChangeKind.ItemsReset);
    }

    // ── plumbing ─────────────────────────────────────────────────────────────────

    bool ContinuesChunk(BaseEvent e, string key) => e switch
    {
        TextMessageChunkEvent x => x.MessageId is null || MessageKey(x.MessageId) == key,
        ReasoningMessageChunkEvent x => x.MessageId is null || ReasoningKey(x.MessageId) == key,
        ToolCallChunkEvent x => x.ToolCallId is null || ToolKey(x.ToolCallId) == key,
        _ => false
    };

    void CloseChunk()
    {
        if (openChunk is not { } chunk)
            return;
        openChunk = null;

        switch (chunk.Item)
        {
            case AgentMessageItem { IsStreaming: true } m:
                m.IsStreaming = false;
                Updated(m);
                break;
            case AgentReasoningItem r:
                EndReasoning(r);
                break;
            case AgentToolCallItem t:
                EndToolCall(t);
                break;
        }
    }

    // A run that ends (or fails) without closing what it opened still leaves a settled conversation.
    void CloseStreaming(string? subagentRunId = null)
    {
        CloseChunk();
        foreach (var item in items)
        {
            if (subagentRunId is not null && item.SubagentRunId != subagentRunId)
                continue;

            switch (item)
            {
                case AgentMessageItem { IsStreaming: true } m:
                    m.IsStreaming = false;
                    Updated(m);
                    break;
                case AgentReasoningItem { IsStreaming: true } r:
                    EndReasoning(r);
                    break;
                case AgentToolCallItem { Status: AgentToolCallStatus.Streaming } t:
                    EndToolCall(t);
                    break;
            }
        }
    }

    void AddError(string message, string? code, string? subagentRunId)
    {
        var id = Guid.NewGuid().ToString("N");
        Add("error:" + id, new AgentErrorItem(id, message, code) { SubagentRunId = subagentRunId, RunId = RunId });
    }

    void Add(string key, AgentItem item)
    {
        AddQuiet(key, item);
        Changed?.Invoke(this, new AgentThreadChange(AgentThreadChangeKind.ItemAdded, item, items.Count - 1));
    }

    void AddQuiet(string key, AgentItem item)
    {
        index[key] = item;
        items.Add(item);
    }

    void Updated(AgentItem item)
    {
        item.Version++;
        Changed?.Invoke(this, new AgentThreadChange(AgentThreadChangeKind.ItemUpdated, item, items.IndexOf(item)));
    }

    void Raise(AgentThreadChangeKind kind) => Changed?.Invoke(this, new AgentThreadChange(kind));

    // Message ids and tool call ids are separate namespaces that legitimately collide
    // (AGUI.Server emits TOOL_CALL_RESULT with messageId == toolCallId), so each kind keys apart.
    static string MessageKey(string id) => "msg:" + id;
    static string ReasoningKey(string id) => "rsn:" + id;
    static string ToolKey(string id) => "tool:" + id;
    static string ActivityKey(string id) => "act:" + id;

    internal static string TextOf(AGUIContent content)
    {
        if (content.IsText)
            return content.Value as string ?? "";
        if (content.Value is null)
            return "";

        var sb = new StringBuilder();
        foreach (var part in content)
        {
            if (part is AGUITextInputContent text && !string.IsNullOrEmpty(text.Text))
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(text.Text);
            }
        }
        return sb.ToString();
    }

    static JsonElement? TryParse(string json)
    {
        try
        {
            return JsonElement.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static JsonNode? ToNode(JsonElement element) =>
        element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : JsonNode.Parse(element.GetRawText());

    internal static JsonElement ToElement(JsonNode? node) =>
        JsonElement.Parse(node?.ToJsonString() ?? "null");
}
