using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Sample.Features.Agents;
using Shiny.Controls.AgentUI.A2UI;

namespace Shiny.Controls.AgentUI.Tests;

/// <summary>
/// The samples' bring-your-own-key agent: an <see cref="IChatClient"/> made into AG-UI in-process by
/// AGUI.Server, driven through the real runner. A scripted chat client stands in for the model.
/// </summary>
public class ChatClientAgentSessionTests
{
    /// <summary>Answers each request with the next scripted reply, and records what it was sent.</summary>
    sealed class ScriptedModel(params Func<IList<ChatMessage>, ChatOptions?, IEnumerable<AIContent>>[] replies) : IChatClient
    {
        int turn;
        public List<(List<ChatMessage> Messages, ChatOptions? Options)> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var list = messages.ToList();
            Requests.Add((list, options));
            var reply = replies[Math.Min(turn++, replies.Length - 1)](list, options);
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, [.. reply]) { MessageId = "m" + turn };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    const string Operations = """
        [{"version":"1.0","createSurface":{"surfaceId":"pets","catalogId":"https://shinylib.net/a2ui/shiny.controls/v1.json",
          "dataModel":{"rating":72},
          "components":[
            {"id":"root","component":"Column","children":["title","gauge"]},
            {"id":"title","component":"Text","text":"Pet match"},
            {"id":"gauge","component":"Gauge","value":{"@path":"/rating"},"min":0,"max":100}
          ]}}]
        """;

    static IEnumerable<AIContent> Call(string id, string name, string argsJson) =>
        [new FunctionCallContent(id, name, JsonSerializer.Deserialize<Dictionary<string, object?>>(argsJson))];

    static IEnumerable<AIContent> Say(string text) => [new TextContent(text)];

    static (AgentRunner Runner, A2UIAgentBinding Binding) Create(IChatClient model, AgentToolRegistry? tools = null)
    {
        var runner = new AgentRunner(new ChatClientAgentSession(model), tools: tools);
        var binding = new A2UIAgentBinding(runner, new A2UISurfaceHost(new A2UICatalogRegistry().Add(A2UICatalog.CreateShiny())));
        return (runner, binding);
    }

    static string LastAssistantText(AgentRunner runner) =>
        runner.Thread.Items.OfType<AgentMessageItem>().Last(m => m.IsAssistant).Text;

    [Fact]
    public async Task Text_Streams()
    {
        var model = new ScriptedModel((_, _) => Say("Hello from the model."));
        var (runner, _) = Create(model);

        await runner.SendAsync("hi");

        LastAssistantText(runner).ShouldBe("Hello from the model.");
        runner.Thread.RunStatus.ShouldBe(AgentRunStatus.Completed);
    }

    [Fact]
    public async Task SystemPrompt_And_RenderUi_GoToTheModel()
    {
        var model = new ScriptedModel((_, _) => Say("ok"));
        var (runner, _) = Create(model);

        await runner.SendAsync("hi");

        var (messages, options) = model.Requests.ShouldHaveSingleItem();
        messages[0].Role.ShouldBe(ChatRole.System);
        messages[0].Text.ShouldContain("render_ui");
        // the catalog summary names the Shiny components, read off the catalog itself
        messages[0].Text.ShouldContain("- DataGrid(");
        messages[0].Text.ShouldContain("- Gauge(");
        options!.Tools!.Select(t => t.Name).ShouldContain(ChatClientAgentSession.RenderUiTool);
    }

    [Fact]
    public async Task RenderUi_BecomesAnA2UISurface()
    {
        var model = new ScriptedModel(
            (_, _) => Call("call-1", "render_ui", $$"""{"operations":{{Operations}}}"""),
            (_, _) => Say("Here's your match."));
        var (runner, binding) = Create(model);

        await runner.SendAsync("ask me a few questions to pick a pet");

        var activity = runner.Thread.Items.OfType<AgentActivityItem>().ShouldHaveSingleItem();
        activity.ActivityType.ShouldBe("a2ui-surface");
        var surface = binding.GetSurfaces(activity.Id).ShouldHaveSingleItem();
        surface.SurfaceId.ShouldBe("pets");
        surface.GetComponent("gauge")!.Type.ShouldBe("Gauge");
        LastAssistantText(runner).ShouldBe("Here's your match.");

        // the model saw its own tool result on the second request
        model.Requests.Count.ShouldBe(2);
        model.Requests[1].Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().ShouldHaveSingleItem().CallId.ShouldBe("call-1");
    }

    [Fact]
    public async Task FrontendTool_IsAdvertised_AndTheAppAnswersIt()
    {
        var prompts = new AgentPromptQueue();
        var tools = new AgentToolRegistry().AddAll(prompts);
        prompts.Changed += (_, _) =>
        {
            // the user answers the confirm prompt the moment it appears
            if (prompts.Pending.FirstOrDefault(p => !p.IsCompleted) is { } prompt)
                prompt.Submit(new JsonObject { ["confirmed"] = true });
        };

        var model = new ScriptedModel(
            (_, options) => Call("call-c", "confirm", """{"title":"Book the venue?"}"""),
            (messages, _) => Say(messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Any(r => r.CallId == "call-c")
                ? "Venue booked."
                : "No answer."));
        var (runner, _) = Create(model, tools);

        await runner.SendAsync("help me plan a team offsite");

        model.Requests[0].Options!.Tools!.Select(t => t.Name).ShouldContain("confirm");
        runner.Thread.Items.OfType<AgentToolCallItem>().ShouldHaveSingleItem().Name.ShouldBe("confirm");
        LastAssistantText(runner).ShouldBe("Venue booked.");
    }

    [Fact]
    public void Instructions_FitInAReasonablePrompt()
    {
        // a summary, not the whole schema: keep it under ~24KB (roughly 6k tokens)
        ChatClientAgentSession.DefaultInstructions.Length.ShouldBeLessThan(24_000);
    }
}
