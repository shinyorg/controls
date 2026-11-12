using System.Text.Json;
using System.Text.Json.Nodes;
using AGUI.Abstractions;

namespace Shiny.Controls.AgentUI;

/// <summary>What an <see cref="AgentItem"/> is.</summary>
public enum AgentItemKind
{
    /// <summary>A user, assistant, system or developer message (<see cref="AgentMessageItem"/>).</summary>
    Message,
    /// <summary>Model reasoning (<see cref="AgentReasoningItem"/>).</summary>
    Reasoning,
    /// <summary>A tool call and its result (<see cref="AgentToolCallItem"/>).</summary>
    ToolCall,
    /// <summary>An activity widget (<see cref="AgentActivityItem"/>).</summary>
    Activity,
    /// <summary>A run or sub-agent failure (<see cref="AgentErrorItem"/>).</summary>
    Error
}

/// <summary>
/// One row of an agent conversation, in the order it first appeared. Items are mutated in place
/// as their events stream in; <see cref="AgentThread.Changed"/> says which one moved.
/// </summary>
public abstract class AgentItem
{
    /// <summary>Creates an item with its protocol id.</summary>
    protected AgentItem(string id) => Id = id;

    /// <summary>The message id (or tool call id for <see cref="AgentToolCallItem"/>) from the protocol.</summary>
    public string Id { get; }

    /// <summary>What this item is, for renderers that switch on it.</summary>
    public abstract AgentItemKind Kind { get; }

    /// <summary>The sub-agent that produced this item, or null for the top-level agent.</summary>
    public string? SubagentRunId { get; internal set; }

    /// <summary>The run that produced this item. Null for items restored from a messages snapshot or added locally.</summary>
    public string? RunId { get; internal set; }

    /// <summary>Bumped on every change, so renderers can skip work when nothing moved.</summary>
    public int Version { get; internal set; }
}

/// <summary>A user, assistant, system or developer message.</summary>
public sealed class AgentMessageItem : AgentItem
{
    internal AgentMessageItem(string id, string role) : base(id) => Role = role;

    /// <summary>Always <see cref="AgentItemKind.Message"/>.</summary>
    public override AgentItemKind Kind => AgentItemKind.Message;

    /// <summary>One of <see cref="AGUIRoles"/>: user, assistant, system or developer.</summary>
    public string Role { get; }

    /// <summary>The author's display name, when the agent sent one.</summary>
    public string? Name { get; internal set; }

    /// <summary>The text so far. For a multimodal user message, its text parts joined.</summary>
    public string Text { get; internal set; } = "";

    /// <summary>The user message's full content when it carries more than text (images, documents).</summary>
    public AGUIContent? Content { get; internal set; }

    /// <summary>Opaque provider state for the message, round-tripped to the agent and never shown.</summary>
    public string? EncryptedValue { get; internal set; }

    /// <summary>True while deltas may still arrive.</summary>
    public bool IsStreaming { get; internal set; }

    /// <summary>True for the user's own messages.</summary>
    public bool IsUser => Role == AGUIRoles.User;
    /// <summary>True for the agent's messages.</summary>
    public bool IsAssistant => Role == AGUIRoles.Assistant;
}

/// <summary>Model reasoning ("thinking"). Encrypted reasoning is kept for round-trip and never shown.</summary>
public sealed class AgentReasoningItem : AgentItem
{
    internal AgentReasoningItem(string id) : base(id) { }

    /// <summary>Always <see cref="AgentItemKind.Reasoning"/>.</summary>
    public override AgentItemKind Kind => AgentItemKind.Reasoning;

    /// <summary>The reasoning text so far (empty when the model only sent it encrypted).</summary>
    public string Text { get; internal set; } = "";

    /// <summary>Encrypted reasoning, round-tripped so the model can continue it. Never shown.</summary>
    public string? EncryptedValue { get; internal set; }

    /// <summary>True while the model is still thinking.</summary>
    public bool IsStreaming { get; internal set; }

    /// <summary>When the reasoning started and stopped, for "thought for 4s" labels. Null when restored from history.</summary>
    public DateTimeOffset? StartedAt { get; internal set; }
    /// <summary>When the reasoning stopped. Null while streaming or when restored from history.</summary>
    public DateTimeOffset? EndedAt { get; internal set; }
}

/// <summary>Where a tool call is in its life.</summary>
public enum AgentToolCallStatus
{
    /// <summary>Arguments are still streaming in.</summary>
    Streaming,
    /// <summary>The call is complete and waiting for a result.</summary>
    Ready,
    /// <summary>A frontend tool is executing it.</summary>
    Running,
    /// <summary>The call has a result.</summary>
    Completed,
    /// <summary>The call failed; <see cref="AgentToolCallItem.Error"/> says why.</summary>
    Failed
}

/// <summary>A tool call, its streaming arguments and (once it has one) its result.</summary>
public sealed class AgentToolCallItem : AgentItem
{
    internal AgentToolCallItem(string toolCallId, string name) : base(toolCallId) => Name = name;

    /// <summary>Always <see cref="AgentItemKind.ToolCall"/>.</summary>
    public override AgentItemKind Kind => AgentItemKind.ToolCall;

    /// <summary>The call's id (the same as <see cref="AgentItem.Id"/>).</summary>
    public string ToolCallId => Id;

    /// <summary>The tool's name.</summary>
    public string Name { get; internal set; }

    /// <summary>The assistant message this call belongs to, when the agent said.</summary>
    public string? ParentMessageId { get; internal set; }

    /// <summary>The raw argument JSON as streamed (may be partial while <see cref="Status"/> is Streaming).</summary>
    public string ArgumentsText { get; internal set; } = "";

    /// <summary>The parsed arguments once the call is complete. Null if they weren't valid JSON.</summary>
    public JsonElement? Arguments { get; internal set; }

    /// <summary>Where the call is: streaming, waiting, running, done or failed.</summary>
    public AgentToolCallStatus Status { get; internal set; }

    /// <summary>The result content, from the agent (backend tool) or a frontend tool.</summary>
    public string? Result { get; internal set; }

    /// <summary>The failure message when <see cref="Status"/> is Failed.</summary>
    public string? Error { get; internal set; }

    /// <summary>The message id the result arrived with.</summary>
    public string? ResultMessageId { get; internal set; }

    /// <summary>Opaque provider state for the call, round-tripped to the agent.</summary>
    public string? EncryptedValue { get; internal set; }

    /// <summary>True once the call is completed or failed.</summary>
    public bool HasResult => Status is AgentToolCallStatus.Completed or AgentToolCallStatus.Failed;
}

/// <summary>
/// Structured progress the agent wants drawn as its own widget, keyed by <see cref="ActivityType"/>.
/// A2UI surfaces arrive this way (<c>a2ui-surface</c>).
/// </summary>
public sealed class AgentActivityItem : AgentItem
{
    internal AgentActivityItem(string messageId, string activityType) : base(messageId) => ActivityType = activityType;

    /// <summary>Always <see cref="AgentItemKind.Activity"/>.</summary>
    public override AgentItemKind Kind => AgentItemKind.Activity;

    /// <summary>What kind of activity this is; renderers pick a widget by it (A2UI uses <c>a2ui-surface</c>).</summary>
    public string ActivityType { get; internal set; }

    /// <summary>The current content, with every delta applied.</summary>
    public JsonNode? Content { get; internal set; }
}

/// <summary>A run (or sub-agent) failure, shown inline.</summary>
public sealed class AgentErrorItem : AgentItem
{
    internal AgentErrorItem(string id, string message, string? code) : base(id)
    {
        Message = message;
        Code = code;
    }

    /// <summary>Always <see cref="AgentItemKind.Error"/>.</summary>
    public override AgentItemKind Kind => AgentItemKind.Error;

    /// <summary>What went wrong, as the agent (or transport) said it.</summary>
    public string Message { get; }
    /// <summary>A machine-readable error code, when there is one.</summary>
    public string? Code { get; }
}

/// <summary>Where the conversation's current run is.</summary>
public enum AgentRunStatus
{
    /// <summary>No run has started yet.</summary>
    Idle,
    /// <summary>A run is streaming.</summary>
    Running,
    /// <summary>The last run finished normally.</summary>
    Completed,
    /// <summary>The run ended waiting on <see cref="AgentThread.PendingInterrupts"/>; resume with responses.</summary>
    Interrupted,
    /// <summary>The last run was stopped before it finished.</summary>
    Cancelled,
    /// <summary>The last run failed; see <see cref="AgentThread.LastError"/>.</summary>
    Failed
}

/// <summary>A step currently running (<c>STEP_STARTED</c> without its <c>STEP_FINISHED</c>).</summary>
public sealed record AgentStep(string Name, string? SubagentRunId);

/// <summary>Where a sub-agent is in its life.</summary>
public enum AgentSubagentStatus
{
    /// <summary>The sub-agent is working.</summary>
    Running,
    /// <summary>The sub-agent finished.</summary>
    Completed,
    /// <summary>The sub-agent is waiting on interrupts (<see cref="AgentSubagent.InterruptIds"/>).</summary>
    Suspended,
    /// <summary>The sub-agent failed; see <see cref="AgentSubagent.Error"/>.</summary>
    Failed
}

/// <summary>A sub-agent the top-level agent delegated to, with where it hangs in the conversation.</summary>
public sealed class AgentSubagent
{
    internal AgentSubagent(string id, string name) { Id = id; Name = name; }

    /// <summary>The sub-agent run's id.</summary>
    public string Id { get; }
    /// <summary>The sub-agent's name.</summary>
    public string Name { get; }
    /// <summary>What the sub-agent is for, when the agent said.</summary>
    public string? Description { get; internal set; }
    /// <summary>The sub-agent that started this one, for nested delegation.</summary>
    public string? ParentSubagentRunId { get; internal set; }
    /// <summary>The tool call that started this sub-agent.</summary>
    public string? ParentToolCallId { get; internal set; }
    /// <summary>The message this sub-agent hangs under.</summary>
    public string? ParentMessageId { get; internal set; }
    /// <summary>Where the sub-agent is.</summary>
    public AgentSubagentStatus Status { get; internal set; }
    /// <summary>What the sub-agent returned, when it finished.</summary>
    public JsonElement? Result { get; internal set; }
    /// <summary>Why the sub-agent failed.</summary>
    public string? Error { get; internal set; }

    /// <summary>The interrupts it is suspended on.</summary>
    public IReadOnlyList<string> InterruptIds { get; internal set; } = [];
}

/// <summary>What moved in an <see cref="AgentThread"/>.</summary>
public enum AgentThreadChangeKind
{
    /// <summary>An item was appended.</summary>
    ItemAdded,
    /// <summary>An item changed in place (more text, a result, ...).</summary>
    ItemUpdated,
    /// <summary>The whole item list was replaced (a messages snapshot, or <see cref="AgentThread.Clear"/>).</summary>
    ItemsReset,
    /// <summary>The shared state changed.</summary>
    StateChanged,
    /// <summary>The run started, finished, failed or was interrupted.</summary>
    RunStatusChanged,
    /// <summary>A step started or finished.</summary>
    StepsChanged,
    /// <summary>A sub-agent started, finished or failed.</summary>
    SubagentsChanged,
    /// <summary><see cref="AgentThread.PendingInterrupts"/> changed (a run asked, or a resume or new run cleared them).</summary>
    InterruptsChanged
}

/// <param name="Kind">What moved.</param>
/// <param name="Item">The item, for ItemAdded and ItemUpdated.</param>
/// <param name="Index">The item's position, for ItemAdded and ItemUpdated.</param>
public sealed record AgentThreadChange(AgentThreadChangeKind Kind, AgentItem? Item = null, int Index = -1);

/// <summary>How an agent view shows the model's reasoning.</summary>
public enum AgentReasoningDisplay
{
    /// <summary>Not shown.</summary>
    Hidden,
    /// <summary>A one-line "Thought for 4s" row that expands on tap (the default).</summary>
    Collapsed,
    /// <summary>Shown in full.</summary>
    Expanded
}
