using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shiny.Controls.AgentUI;

/// <summary>The kinds of question the built-in UI tools ask. Hosts draw one inline prompt per kind.</summary>
public static class AgentPromptKinds
{
    /// <summary>Yes or no. Arguments: <c>title</c>, <c>message</c>, <c>confirmText</c>, <c>cancelText</c>. Result: <c>{"confirmed": bool}</c>.</summary>
    public const string Confirm = "confirm";
    /// <summary>One of a list. Arguments: <c>title</c>, <c>options</c> (strings). Result: <c>{"choice": string}</c>.</summary>
    public const string ChooseOne = "choose_one";
    /// <summary>Several of a list. Arguments: <c>title</c>, <c>options</c>, <c>min</c>, <c>max</c>. Result: <c>{"choices": [string]}</c>.</summary>
    public const string ChooseMany = "choose_many";
    /// <summary>A date. Arguments: <c>title</c>, <c>min</c>, <c>max</c> (YYYY-MM-DD). Result: <c>{"date": "YYYY-MM-DD"}</c>.</summary>
    public const string Date = "date";
    /// <summary>A date range. Arguments: <c>title</c>, <c>min</c>, <c>max</c>. Result: <c>{"start": "YYYY-MM-DD", "end": "YYYY-MM-DD"}</c>.</summary>
    public const string DateRange = "date_range";
    /// <summary>Free text. Arguments: <c>title</c>, <c>placeholder</c>, <c>multiline</c>. Result: <c>{"text": string}</c>.</summary>
    public const string Text = "text";
}

/// <summary>
/// One question a frontend tool is waiting on. The host draws it inline under its tool call (by
/// <see cref="Kind"/>) and calls <see cref="Submit"/> or <see cref="Decline"/>; the tool's result goes
/// back to the agent and the run continues.
/// </summary>
public sealed class AgentPrompt
{
    readonly TaskCompletionSource<AgentToolResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal AgentPrompt(string toolCallId, string kind, JsonObject arguments)
    {
        ToolCallId = toolCallId;
        Kind = kind;
        Arguments = arguments;
    }

    /// <summary>The tool call this prompt answers; hosts place the prompt under that call's card.</summary>
    public string ToolCallId { get; }

    /// <summary>What to draw (<see cref="AgentPromptKinds"/>, or an app's own kind).</summary>
    public string Kind { get; }

    /// <summary>The tool call's arguments.</summary>
    public JsonObject Arguments { get; }

    /// <summary>The prompt's title, or its message when there is no title.</summary>
    public string Title => Str("title") ?? Str("message") ?? "";

    /// <summary>A string argument, or null.</summary>
    public string? Str(string name) => Arguments[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>A string list argument (<c>options</c>).</summary>
    public IReadOnlyList<string> List(string name) => Arguments[name] is JsonArray a
        ? a.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n?.ToJsonString() ?? "").ToList()
        : [];

    /// <summary>A date argument (<c>min</c>/<c>max</c>), or null when absent or unparseable.</summary>
    public DateOnly? Date(string name) => DateOnly.TryParse(Str(name), System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;

    /// <summary>A number argument, or null.</summary>
    public double? Number(string name) => Arguments[name] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    /// <summary>True once answered or declined.</summary>
    public bool IsCompleted => completion.Task.IsCompleted;

    internal Task<AgentToolResult> Completion => completion.Task;

    /// <summary>Answers the prompt; <paramref name="result"/> is the JSON the agent reads.</summary>
    public void Submit(JsonObject result) => completion.TrySetResult(AgentToolResult.Success(result.ToJsonString()));

    /// <summary>The user said no (or closed the prompt). The agent gets a failed tool result.</summary>
    public void Decline(string reason = "The user declined.") => completion.TrySetResult(AgentToolResult.Failure(reason));

    internal void Cancel() => completion.TrySetCanceled();
}

/// <summary>
/// The prompts frontend tools are waiting on. A tool made with <see cref="CreateTool"/> (or one of
/// <see cref="AgentUiTools"/>) adds a prompt here and waits for the host to answer it; an AgentView
/// watches <see cref="Changed"/> and draws each pending prompt inline. Host-neutral, so MAUI and
/// Blazor ask the same questions with the same arguments and results.
/// </summary>
public sealed class AgentPromptQueue
{
    readonly List<AgentPrompt> pending = [];

    /// <summary>The prompts waiting for an answer, oldest first.</summary>
    public IReadOnlyList<AgentPrompt> Pending => pending;

    /// <summary>Raised when a prompt is added or answered.</summary>
    public event EventHandler? Changed;

    /// <summary>The pending prompt for a tool call, if there is one.</summary>
    public AgentPrompt? Find(string toolCallId) => pending.FirstOrDefault(p => p.ToolCallId == toolCallId);

    /// <summary>Adds a prompt and waits for the host to answer it. Cancelling the token (Stop) withdraws it.</summary>
    public async ValueTask<AgentToolResult> AskAsync(string toolCallId, string kind, JsonObject arguments, CancellationToken cancellationToken = default)
    {
        var prompt = new AgentPrompt(toolCallId, kind, arguments);
        pending.Add(prompt);
        Changed?.Invoke(this, EventArgs.Empty);

        try
        {
            using (cancellationToken.Register(prompt.Cancel))
                return await prompt.Completion;
        }
        finally
        {
            pending.Remove(prompt);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>A frontend tool that asks the user through this queue.</summary>
    /// <param name="name">The tool's name, as the agent calls it.</param>
    /// <param name="description">What the tool does, for the agent.</param>
    /// <param name="kind">What the host draws (<see cref="AgentPromptKinds"/> or an app's own kind).</param>
    /// <param name="parameters">The arguments' JSON Schema.</param>
    public AgentTool CreateTool(string name, string description, string kind, JsonElement parameters)
        => new(name, description, parameters, (call, ct) =>
        {
            var args = call.Arguments.ValueKind == JsonValueKind.Object
                ? JsonNode.Parse(call.Arguments.GetRawText()) as JsonObject ?? new JsonObject()
                : new JsonObject();
            return AskAsync(call.ToolCallId, kind, args, ct);
        });
}

/// <summary>
/// The built-in human-in-the-loop tools: the agent asks, a real control answers inline in the
/// conversation. Each is one line to add: <c>tools.Add(AgentUiTools.DateRange(prompts))</c>.
/// </summary>
public static class AgentUiTools
{
    /// <summary>Adds every built-in UI tool to a registry.</summary>
    public static AgentToolRegistry AddAll(this AgentToolRegistry tools, AgentPromptQueue prompts)
        => tools
            .Add(Confirm(prompts))
            .Add(ChooseOne(prompts))
            .Add(ChooseMany(prompts))
            .Add(Date(prompts))
            .Add(DateRange(prompts))
            .Add(Text(prompts));

    /// <summary><c>confirm</c>: asks yes or no.</summary>
    public static AgentTool Confirm(AgentPromptQueue prompts, string name = "confirm") => prompts.CreateTool(
        name, "Ask the user to confirm or cancel something before you do it. Returns {\"confirmed\": true|false}.", AgentPromptKinds.Confirm,
        Schema("""{"type":"object","properties":{"title":{"type":"string"},"message":{"type":"string","description":"What the user is agreeing to."},"confirmText":{"type":"string"},"cancelText":{"type":"string"}},"required":["message"]}"""));

    /// <summary><c>choose_one</c>: asks for one option.</summary>
    public static AgentTool ChooseOne(AgentPromptQueue prompts, string name = "choose_one") => prompts.CreateTool(
        name, "Ask the user to pick exactly one of a few options. Returns {\"choice\": string}.", AgentPromptKinds.ChooseOne,
        Schema("""{"type":"object","properties":{"title":{"type":"string"},"options":{"type":"array","items":{"type":"string"},"minItems":2}},"required":["title","options"]}"""));

    /// <summary><c>choose_many</c>: asks for several options.</summary>
    public static AgentTool ChooseMany(AgentPromptQueue prompts, string name = "choose_many") => prompts.CreateTool(
        name, "Ask the user to pick any number of options (tags, categories). Returns {\"choices\": [string]}.", AgentPromptKinds.ChooseMany,
        Schema("""{"type":"object","properties":{"title":{"type":"string"},"options":{"type":"array","items":{"type":"string"},"minItems":1},"min":{"type":"integer","minimum":0},"max":{"type":"integer","minimum":1}},"required":["title","options"]}"""));

    /// <summary><c>pick_date</c>: asks for a date.</summary>
    public static AgentTool Date(AgentPromptQueue prompts, string name = "pick_date") => prompts.CreateTool(
        name, "Ask the user to pick a date on a calendar. Returns {\"date\": \"YYYY-MM-DD\"}.", AgentPromptKinds.Date,
        Schema("""{"type":"object","properties":{"title":{"type":"string"},"min":{"type":"string","format":"date"},"max":{"type":"string","format":"date"}},"required":["title"]}"""));

    /// <summary><c>pick_date_range</c>: asks for a start and end date.</summary>
    public static AgentTool DateRange(AgentPromptQueue prompts, string name = "pick_date_range") => prompts.CreateTool(
        name, "Ask the user to choose a date range (a trip, a report period). Returns {\"start\": \"YYYY-MM-DD\", \"end\": \"YYYY-MM-DD\"}.", AgentPromptKinds.DateRange,
        Schema("""{"type":"object","properties":{"title":{"type":"string"},"min":{"type":"string","format":"date"},"max":{"type":"string","format":"date"}},"required":["title"]}"""));

    /// <summary><c>ask_text</c>: asks for free text.</summary>
    public static AgentTool Text(AgentPromptQueue prompts, string name = "ask_text") => prompts.CreateTool(
        name, "Ask the user to type something you can't infer. Returns {\"text\": string}.", AgentPromptKinds.Text,
        Schema("""{"type":"object","properties":{"title":{"type":"string"},"placeholder":{"type":"string"},"multiline":{"type":"boolean"}},"required":["title"]}"""));

    static JsonElement Schema(string json) => JsonElement.Parse(json);
}
