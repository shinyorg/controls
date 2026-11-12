using System.Text.Json;
using System.Text.Json.Nodes;
using AGUI.Abstractions;
using Shiny.Controls.AgentUI;

namespace Shiny.Blazor.Controls.Agents;

/// <summary>What kind of answer an interrupt wants, read from its metadata and response schema.</summary>
public enum AgentInterruptKind
{
    /// <summary>Approve or reject (anything that isn't one of the other two, such as <c>{"approved": boolean}</c>).</summary>
    Approval,
    /// <summary>One or several of <see cref="AgentInterruptRequest.Choices"/> (<c>metadata.choices</c>). Answered with <c>{"choiceIds": [...]}</c>.</summary>
    Choices,
    /// <summary>Free text (the response schema has a string <c>text</c> property). Answered with <c>{"text": "..."}</c>.</summary>
    Text
}

/// <summary>One option of a choices interrupt.</summary>
public sealed record AgentInterruptChoice(string Id, string Label);

/// <summary>
/// One pending interrupt and the answer the user is building for it. The view collects an answer for
/// every pending interrupt and resumes the run once, with all of them.
/// </summary>
public sealed class AgentInterruptRequest
{
    internal AgentInterruptRequest(AGUIInterrupt interrupt)
    {
        Interrupt = interrupt;
        var metadata = ToNode(interrupt.Metadata) as JsonObject;
        var schema = ToNode(interrupt.ResponseSchema) as JsonObject;

        if (metadata?["choices"] is JsonArray choices)
        {
            Choices = choices.OfType<JsonObject>()
                .Select(c => new AgentInterruptChoice(Str(c["id"]) ?? Str(c["label"]) ?? "", Str(c["label"]) ?? Str(c["id"]) ?? ""))
                .Where(c => c.Id.Length > 0)
                .ToList();
            AllowMultiple = metadata["allowMultiple"] is JsonValue v && v.GetValueKind() == JsonValueKind.True;
            Kind = AgentInterruptKind.Choices;
        }
        else if (schema?["properties"]?["text"] is JsonObject text && Str(text["type"]) == "string")
        {
            Kind = AgentInterruptKind.Text;
        }
    }

    /// <summary>The interrupt as the agent sent it.</summary>
    public AGUIInterrupt Interrupt { get; }

    /// <summary>What to ask.</summary>
    public AgentInterruptKind Kind { get; } = AgentInterruptKind.Approval;

    /// <summary>The prompt to show.</summary>
    public string Message => Interrupt.Message ?? Interrupt.Reason ?? "The agent needs your input to continue.";

    /// <summary>The options of a <see cref="AgentInterruptKind.Choices"/> interrupt.</summary>
    public IReadOnlyList<AgentInterruptChoice> Choices { get; } = [];

    /// <summary>True when several choices may be picked.</summary>
    public bool AllowMultiple { get; }

    /// <summary>The answer so far; null until the user answers this interrupt.</summary>
    public AGUIResume? Answer { get; private set; }

    /// <summary>Approves (payload <c>{"approved": true}</c>).</summary>
    public void Approve() => Resolve("""{"approved":true}""");

    /// <summary>Rejects: the interrupt is cancelled.</summary>
    public void Reject() => Answer = new AGUIResume { InterruptId = Interrupt.Id, Status = ResumeStatus.Cancelled };

    /// <summary>Answers a choices interrupt.</summary>
    public void Choose(IEnumerable<string> choiceIds)
    {
        var ids = new JsonArray(choiceIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        Resolve(new JsonObject { ["choiceIds"] = ids }.ToJsonString());
    }

    /// <summary>Answers a free-text interrupt.</summary>
    public void Reply(string text) => Resolve(new JsonObject { ["text"] = text }.ToJsonString());

    void Resolve(string payload) => Answer = new AGUIResume
    {
        InterruptId = Interrupt.Id,
        Status = ResumeStatus.Resolved,
        Payload = JsonElement.Parse(payload)
    };

    static string? Str(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        JsonNode n => n,
        JsonElement { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null } => null,
        JsonElement e => JsonNode.Parse(e.GetRawText()),
        string s => TryParse(s),
        _ => null
    };

    static JsonNode? TryParse(string s)
    {
        try
        {
            return JsonNode.Parse(s);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The pending interrupts of a run and their answers, handed to <c>InterruptTemplate</c>. Answer each
/// request, then <see cref="SubmitAsync"/> resumes the run with every answer in one call.
/// </summary>
public sealed class AgentInterruptContext
{
    readonly AgentRunner runner;

    internal AgentInterruptContext(AgentRunner runner, IReadOnlyList<AgentInterruptRequest> requests)
    {
        this.runner = runner;
        Requests = requests;
    }

    /// <summary>One request per pending interrupt.</summary>
    public IReadOnlyList<AgentInterruptRequest> Requests { get; }

    /// <summary>True once every request has an answer.</summary>
    public bool IsComplete => Requests.All(r => r.Answer is not null);

    /// <summary>The runner, for templates that build their own resume.</summary>
    public AgentRunner Runner => runner;

    /// <summary>Resumes with every answer. Unanswered requests are rejected.</summary>
    public Task SubmitAsync()
    {
        foreach (var r in Requests.Where(r => r.Answer is null))
            r.Reject();
        return runner.IsBusy ? Task.CompletedTask : runner.ResumeAsync(Requests.Select(r => r.Answer!));
    }

    /// <summary>Approves every interrupt.</summary>
    public Task ApproveAllAsync()
    {
        foreach (var r in Requests)
            r.Approve();
        return SubmitAsync();
    }

    /// <summary>Rejects every interrupt.</summary>
    public Task RejectAllAsync()
    {
        foreach (var r in Requests)
            r.Reject();
        return SubmitAsync();
    }
}
