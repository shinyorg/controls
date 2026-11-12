using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using AGUI.Abstractions;

namespace Shiny.Maui.Controls.Agents;

/// <summary>What an interrupt asks the user for, which decides the card <see cref="AgentView"/> draws.</summary>
public enum AgentInterruptKind
{
    /// <summary>Approve or reject (any interrupt that isn't one of the others). Approve answers <c>{"approved": true}</c>.</summary>
    Approval,
    /// <summary>Pick from <c>metadata.choices</c> (<c>[{"id","label"}]</c>, optionally <c>allowMultiple</c>). Answers <c>{"choiceIds": [...]}</c>.</summary>
    Choices,
    /// <summary>Free text: the response schema has a string property <c>text</c>. Answers <c>{"text": "..."}</c>.</summary>
    Text
}

/// <summary>One choice an interrupt offers.</summary>
/// <param name="Id">What goes back in <c>choiceIds</c>.</param>
/// <param name="Label">What the user sees.</param>
public sealed record AgentInterruptChoice(string Id, string Label);

/// <summary>
/// One pending interrupt as a question with typed answers - the <see cref="AgentView.InterruptTemplate"/>
/// binding context. When the last pending interrupt is answered, every answer goes back to the agent in
/// one <see cref="AgentRunner.ResumeAsync"/> call.
/// </summary>
public sealed class AgentInterruptRequest
{
    readonly AgentInterruptBatch batch;

    internal AgentInterruptRequest(AGUIInterrupt interrupt, AgentInterruptBatch batch)
    {
        this.batch = batch;
        this.Interrupt = interrupt;
        (this.Kind, this.Choices, this.AllowMultiple) = Classify(interrupt);
        this.ApproveCommand = new Command(this.Approve, () => !this.IsAnswered);
        this.RejectCommand = new Command(this.Reject, () => !this.IsAnswered);
        this.ChooseCommand = new Command<string>(id => this.Choose([id]), _ => !this.IsAnswered);
        this.ReplyCommand = new Command<string>(text => this.Reply(text ?? ""), _ => !this.IsAnswered);
    }

    /// <summary>The interrupt as the agent sent it.</summary>
    public AGUIInterrupt Interrupt { get; }

    /// <summary>What it asks for.</summary>
    public AgentInterruptKind Kind { get; }

    /// <summary>What to show: the interrupt's message, else its reason.</summary>
    public string Message => this.Interrupt.Message ?? this.Interrupt.Reason ?? "The agent needs your input to continue.";

    /// <summary>The choices, for <see cref="AgentInterruptKind.Choices"/>.</summary>
    public IReadOnlyList<AgentInterruptChoice> Choices { get; }

    /// <summary>Whether several choices may be picked.</summary>
    public bool AllowMultiple { get; }

    /// <summary>True once this interrupt has an answer (the resume may still be waiting on others).</summary>
    public bool IsAnswered { get; private set; }

    /// <summary>Approves: resolved with <c>{"approved": true}</c>.</summary>
    public ICommand ApproveCommand { get; }
    /// <summary>Rejects: cancelled.</summary>
    public ICommand RejectCommand { get; }
    /// <summary>Picks one choice by id (the command parameter).</summary>
    public ICommand ChooseCommand { get; }
    /// <summary>Replies with text (the command parameter).</summary>
    public ICommand ReplyCommand { get; }

    /// <summary>Approves the interrupt.</summary>
    public void Approve() => this.Answer(ResumeStatus.Resolved, Payload(new JsonObject { ["approved"] = true }));

    /// <summary>Rejects the interrupt.</summary>
    public void Reject() => this.Answer(ResumeStatus.Cancelled, null);

    /// <summary>Answers with the chosen choice ids.</summary>
    public void Choose(IEnumerable<string> choiceIds)
        => this.Answer(ResumeStatus.Resolved, Payload(new JsonObject
        {
            ["choiceIds"] = new JsonArray(choiceIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray())
        }));

    /// <summary>Answers with text.</summary>
    public void Reply(string text) => this.Answer(ResumeStatus.Resolved, Payload(new JsonObject { ["text"] = text }));

    /// <summary>Answers with any status (<see cref="ResumeStatus"/>) and payload.</summary>
    public void Answer(string status, JsonElement? payload)
    {
        if (this.IsAnswered)
            return;
        this.IsAnswered = true;
        foreach (var command in new[] { this.ApproveCommand, this.RejectCommand, this.ChooseCommand, this.ReplyCommand })
            ((Command)command).ChangeCanExecute();
        this.batch.Answered(new AGUIResume { InterruptId = this.Interrupt.Id, Status = status, Payload = payload });
    }

    static JsonElement Payload(JsonObject o) => JsonElement.Parse(o.ToJsonString());

    static (AgentInterruptKind, IReadOnlyList<AgentInterruptChoice>, bool) Classify(AGUIInterrupt interrupt)
    {
        if (interrupt.Metadata is { ValueKind: JsonValueKind.Object } meta
            && meta.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array)
        {
            var list = choices.EnumerateArray()
                .Where(c => c.ValueKind == JsonValueKind.Object)
                .Select(c => new AgentInterruptChoice(
                    Str(c, "id") ?? Str(c, "label") ?? "",
                    Str(c, "label") ?? Str(c, "id") ?? ""))
                .Where(c => c.Id.Length > 0)
                .ToList();
            var multiple = meta.TryGetProperty("allowMultiple", out var m) && m.ValueKind == JsonValueKind.True;
            if (list.Count > 0)
                return (AgentInterruptKind.Choices, list, multiple);
        }

        if (interrupt.ResponseSchema is { ValueKind: JsonValueKind.Object } schema
            && schema.TryGetProperty("properties", out var props)
            && props.ValueKind == JsonValueKind.Object
            && props.TryGetProperty("text", out var text)
            && text.ValueKind == JsonValueKind.Object
            && Str(text, "type") == "string")
            return (AgentInterruptKind.Text, [], false);

        return (AgentInterruptKind.Approval, [], false);
    }

    static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>The interrupts one run stopped on; resumes once, when every one has an answer.</summary>
sealed class AgentInterruptBatch
{
    readonly AgentRunner runner;
    readonly Func<CancellationToken> token;
    readonly List<AGUIResume> answers = [];

    public AgentInterruptBatch(AgentRunner runner, IReadOnlyList<AGUIInterrupt> interrupts, Func<CancellationToken> token)
    {
        this.runner = runner;
        this.token = token;
        this.Requests = interrupts.Select(i => new AgentInterruptRequest(i, this)).ToList();
    }

    public IReadOnlyList<AgentInterruptRequest> Requests { get; }

    public void Answered(AGUIResume resume)
    {
        this.answers.Add(resume);
        if (this.answers.Count < this.Requests.Count || this.runner.IsBusy)
            return;
        _ = AgentView.Swallow(this.runner.ResumeAsync(this.answers.ToList(), this.token()));
    }
}
