using System.Text.Json;
using System.Text.Json.Nodes;
using Sample.Features.Agents;

namespace Shiny.Controls.AgentUI.Tests;

public class AgentPromptTests
{
    [Fact]
    public async Task DateRangeTool_WaitsForTheHost_ThenTheRunContinues()
    {
        var prompts = new AgentPromptQueue();
        var tools = new AgentToolRegistry().AddAll(prompts);
        tools.Tools.Select(t => t.Name).ShouldBe(["confirm", "choose_one", "choose_many", "pick_date", "pick_date_range", "ask_text"], ignoreOrder: true);

        var runner = new AgentRunner(new DemoAgentSession { TokenDelay = TimeSpan.Zero }, tools: tools);
        var changes = 0;
        prompts.Changed += (_, _) => changes++;

        var turn = runner.SendAsync("book a trip");
        for (var i = 0; i < 200 && prompts.Pending.Count == 0; i++)
            await Task.Delay(5);

        var prompt = prompts.Pending.ShouldHaveSingleItem();
        prompt.Kind.ShouldBe(AgentPromptKinds.DateRange);
        prompt.Title.ShouldBe("When are you travelling?");
        prompt.Date("min").ShouldBe(DateOnly.FromDateTime(DateTime.Today));
        prompts.Find(prompt.ToolCallId).ShouldBeSameAs(prompt);
        runner.Thread.FindToolCall(prompt.ToolCallId)!.Status.ShouldBe(AgentToolCallStatus.Running);

        prompt.Submit(new JsonObject { ["start"] = "2026-12-01", ["end"] = "2026-12-08" });
        await turn;

        prompts.Pending.ShouldBeEmpty();
        changes.ShouldBe(2);
        runner.Thread.Items.OfType<AgentMessageItem>().Last().Text.ShouldContain("2026-12-08");
    }

    [Fact]
    public async Task Decline_IsAFailedToolResult()
    {
        var prompts = new AgentPromptQueue();
        var tool = AgentUiTools.Confirm(prompts);
        var ask = prompts.AskAsync("c1", AgentPromptKinds.Confirm, new JsonObject { ["message"] = "Go?" });

        prompts.Pending.ShouldHaveSingleItem().Title.ShouldBe("Go?");
        prompts.Pending[0].Decline();

        var result = await ask;
        result.Error.ShouldBe("The user declined.");
        tool.Parameters.GetProperty("required")[0].GetString().ShouldBe("message");
    }

    [Fact]
    public async Task Cancel_WithdrawsThePrompt()
    {
        var prompts = new AgentPromptQueue();
        using var cts = new CancellationTokenSource();
        var ask = prompts.AskAsync("c1", AgentPromptKinds.Text, new JsonObject { ["title"] = "Name?" }, cts.Token).AsTask();

        cts.Cancel();

        await Should.ThrowAsync<TaskCanceledException>(ask);
        prompts.Pending.ShouldBeEmpty();
    }

    [Fact]
    public void Prompt_ReadsListsAndNumbers()
    {
        var prompts = new AgentPromptQueue();
        _ = prompts.AskAsync("c1", AgentPromptKinds.ChooseMany, (JsonObject)JsonNode.Parse("""{"title":"Tags","options":["a","b"],"max":2}""")!);

        var p = prompts.Pending[0];
        p.List("options").ShouldBe(["a", "b"]);
        p.Number("max").ShouldBe(2);
        p.Str("missing").ShouldBeNull();
    }
}
