using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AGUI.Abstractions;

namespace Shiny.Controls.AgentUI;

/// <summary>A frontend tool call, handed to the tool's handler.</summary>
/// <param name="ToolCallId">The call's id.</param>
/// <param name="Name">The tool's name.</param>
/// <param name="Arguments">The arguments the agent sent (an empty object when it sent none).</param>
/// <param name="Thread">The conversation, for tools that need to look at it.</param>
public sealed record AgentToolInvocation(string ToolCallId, string Name, JsonElement Arguments, AgentThread Thread);

/// <summary>What a frontend tool returns to the agent.</summary>
/// <param name="Content">The result the agent reads, usually JSON.</param>
/// <param name="Error">
/// Set when the tool failed. The agent must be told: a failure reported as content reads as success.
/// </param>
public sealed record AgentToolResult(string Content, string? Error = null)
{
    /// <summary>A successful result.</summary>
    public static AgentToolResult Success(string content) => new(content);

    /// <summary>A failed result; the agent is told it failed.</summary>
    public static AgentToolResult Failure(string error) => new("", error);

    /// <summary>Serializes a value as the result. Trim/AOT-safe: pass the source-generated type info.</summary>
    public static AgentToolResult FromValue<T>(T value, JsonTypeInfo<T> typeInfo) => new(JsonSerializer.Serialize(value, typeInfo));
}

/// <summary>
/// A tool the client runs on the agent's behalf (AG-UI frontend tools) - typically one that asks the
/// user something through a real control, such as a date range picker in a floating panel.
/// </summary>
public sealed class AgentTool
{
    readonly Func<AgentToolInvocation, CancellationToken, ValueTask<AgentToolResult>> handler;

    /// <param name="name">The tool's name, as the agent will call it.</param>
    /// <param name="description">What the tool does, for the agent's benefit.</param>
    /// <param name="parameters">A JSON Schema object (<c>"type": "object"</c>) describing the arguments.</param>
    /// <param name="handler">Runs the call. Invoked on the <see cref="AgentRunner"/>'s synchronization context (the UI thread).</param>
    public AgentTool(string name, string description, JsonElement parameters, Func<AgentToolInvocation, CancellationToken, ValueTask<AgentToolResult>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Description = description;
        Parameters = parameters;
        this.handler = handler;
    }

    /// <summary>The tool's name, as the agent calls it.</summary>
    public string Name { get; }
    /// <summary>What the tool does, for the agent's benefit.</summary>
    public string Description { get; }
    /// <summary>The JSON Schema of the arguments.</summary>
    public JsonElement Parameters { get; }

    /// <summary>Optional metadata sent with the tool definition.</summary>
    public JsonElement? Metadata { get; init; }

    /// <summary>
    /// Creates a tool with typed arguments and result. Trim/AOT-safe: pass source-generated type info.
    /// When <paramref name="parameters"/> is omitted the schema is an open object.
    /// </summary>
    public static AgentTool Create<TArgs, TResult>(
        string name,
        string description,
        JsonTypeInfo<TArgs> argsTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        Func<TArgs, AgentToolInvocation, CancellationToken, ValueTask<TResult>> handler,
        JsonElement? parameters = null)
        => new(name, description, parameters ?? OpenObjectSchema, async (call, ct) =>
        {
            var args = call.Arguments.Deserialize(argsTypeInfo)
                       ?? throw new JsonException($"Tool '{name}' received null arguments.");
            var result = await handler(args, call, ct);
            return AgentToolResult.FromValue(result, resultTypeInfo);
        });

    internal ValueTask<AgentToolResult> InvokeAsync(AgentToolInvocation call, CancellationToken ct) => handler(call, ct);

    internal AGUITool ToAGUITool() => new()
    {
        Name = Name,
        Description = Description,
        Parameters = Parameters,
        Metadata = Metadata
    };

    internal static readonly JsonElement OpenObjectSchema = JsonElement.Parse("""{"type":"object","properties":{}}""");
}

/// <summary>The frontend tools a conversation offers the agent.</summary>
public sealed class AgentToolRegistry
{
    readonly Dictionary<string, AgentTool> tools = new(StringComparer.Ordinal);

    /// <summary>The registered tools.</summary>
    public IReadOnlyCollection<AgentTool> Tools => tools.Values;

    /// <summary>Adds (or replaces) a tool.</summary>
    public AgentToolRegistry Add(AgentTool tool)
    {
        tools[tool.Name] = tool;
        return this;
    }

    /// <summary>Removes a tool; returns false when there was none by that name.</summary>
    public bool Remove(string name) => tools.Remove(name);

    /// <summary>True when a tool by that name is registered.</summary>
    public bool Contains(string name) => tools.ContainsKey(name);

    /// <summary>Looks a tool up by name.</summary>
    public bool TryGet(string name, out AgentTool tool) => tools.TryGetValue(name, out tool!);

    /// <summary>The definitions for <see cref="RunAgentInput.Tools"/>.</summary>
    public List<AGUITool> ToAGUITools() => tools.Values.Select(t => t.ToAGUITool()).ToList();
}
