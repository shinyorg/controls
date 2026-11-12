using AGUI.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sample.Features.Agents;
using Shiny.Controls.AgentUI.A2UI;

namespace Shiny.Controls.AgentUI.Tests;

/// <summary>The samples' scripted agent, through the real runner and A2UI binding.</summary>
public class DemoAgentSessionTests
{
    static (AgentRunner Runner, A2UIAgentBinding Binding, List<A2UIOutboundMessage> Errors) Create()
    {
        var tools = new AgentToolRegistry();
        tools.Add(new AgentTool(DemoAgentSession.PickDateRangeTool, "Pick dates", JsonElement.Parse("""{"type":"object"}"""),
            (_, _) => ValueTask.FromResult(AgentToolResult.Success("""{"start":"2026-12-01","end":"2026-12-08"}"""))));
        var runner = new AgentRunner(new DemoAgentSession { TokenDelay = TimeSpan.Zero }, tools: tools);
        var binding = new A2UIAgentBinding(runner, new A2UISurfaceHost(new A2UICatalogRegistry().Add(A2UICatalog.CreateShiny())));
        var errors = new List<A2UIOutboundMessage>();
        binding.Host.Outbound += (_, m) => { if (m.Kind == A2UIOutboundKind.Error) errors.Add(m); };
        return (runner, binding, errors);
    }

    static string LastAssistantText(AgentRunner runner) =>
        runner.Thread.Items.OfType<AgentMessageItem>().Last(m => m.IsAssistant).Text;

    [Fact]
    public async Task Help_StreamsReasoningAndMarkdown()
    {
        var (runner, _, _) = Create();
        await runner.SendAsync("what can you do?");

        runner.Thread.Items.OfType<AgentReasoningItem>().ShouldHaveSingleItem().IsStreaming.ShouldBeFalse();
        LastAssistantText(runner).ShouldContain("scripted demo agent");
        runner.Thread.RunStatus.ShouldBe(AgentRunStatus.Completed);
    }

    [Fact]
    public async Task Dashboard_RendersTheShinySurface_AndActionsGoBack()
    {
        var (runner, binding, errors) = Create();
        await runner.SendAsync("show me the sales dashboard");

        runner.Thread.Items.OfType<AgentToolCallItem>().ShouldHaveSingleItem().Status.ShouldBe(AgentToolCallStatus.Completed);
        var activity = runner.Thread.Items.OfType<AgentActivityItem>().ShouldHaveSingleItem();
        var surface = binding.GetSurfaces(activity.Id).ShouldHaveSingleItem();
        surface.SurfaceId.ShouldBe("dashboard");
        surface.Root.ShouldNotBeNull();
        errors.ShouldBeEmpty();

        surface.DispatchAction(surface.GetComponent("grid")!, surface.RootScope.ForItem("/orders/0", 0), "rowAction").ShouldBeTrue();
        await WaitIdle(runner);

        LastAssistantText(runner).ShouldContain("A-1001");
    }

    [Fact]
    public async Task Trip_RunsTheFrontendToolAndFollowsUp()
    {
        var (runner, _, _) = Create();
        await runner.SendAsync("book a trip");

        var call = runner.Thread.Items.OfType<AgentToolCallItem>().ShouldHaveSingleItem();
        call.Name.ShouldBe(DemoAgentSession.PickDateRangeTool);
        call.Status.ShouldBe(AgentToolCallStatus.Completed);
        LastAssistantText(runner).ShouldContain("2026-12-08");
    }

    [Fact]
    public async Task Newsletter_FormActionCarriesTheTypedValues()
    {
        var (runner, binding, errors) = Create();
        await runner.SendAsync("sign me up for the newsletter");

        var surface = binding.Host.GetSurface("newsletter").ShouldNotBeNull();
        surface.SetData("/name", "Ada");
        surface.SetData("/email", "ada@example.com");
        surface.EvaluateChecks(surface.GetComponent("email")!).IsValid.ShouldBeTrue();
        surface.DispatchAction(surface.GetComponent("submit")!).ShouldBeTrue();
        await WaitIdle(runner);

        LastAssistantText(runner).ShouldContain("Ada");
        errors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Delete_InterruptsThenResumes()
    {
        var (runner, _, _) = Create();
        await runner.SendAsync("delete my old drafts");

        runner.Thread.RunStatus.ShouldBe(AgentRunStatus.Interrupted);
        runner.Thread.PendingInterrupts.ShouldHaveSingleItem().Reason.ShouldBe("approval");

        await runner.ResumeAllAsync(approved: true);
        LastAssistantText(runner).ShouldContain("3 drafts");
        runner.Thread.PendingInterrupts.ShouldBeEmpty();
    }

    [Fact]
    public async Task Research_NestsTheSubagent()
    {
        var (runner, _, _) = Create();
        await runner.SendAsync("research AG-UI");

        var sub = runner.Thread.Subagents.ShouldHaveSingleItem();
        sub.Name.ShouldBe("researcher");
        runner.Thread.Items.OfType<AgentMessageItem>().ShouldContain(m => m.SubagentRunId == sub.Id);
    }

    [Fact]
    public async Task ReadingList_SnapshotThenDelta()
    {
        var (runner, _, _) = Create();
        await runner.SendAsync("track my reading list");
        (runner.Thread.State!["books"] as JsonArray)!.Count.ShouldBe(2);

        await runner.SendAsync("add to my reading list");
        (runner.Thread.State!["books"] as JsonArray)!.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Fail_RecordsTheError()
    {
        var (runner, _, _) = Create();
        await runner.SendAsync("fail on purpose");

        runner.Thread.RunStatus.ShouldBe(AgentRunStatus.Failed);
        runner.Thread.Items.OfType<AgentErrorItem>().ShouldHaveSingleItem();
    }

    static AGUIResume Answer(AGUIInterrupt interrupt, string payloadJson) => new()
    {
        InterruptId = interrupt.Id,
        Status = ResumeStatus.Resolved,
        Payload = JsonElement.Parse(payloadJson)
    };

    [Fact]
    public async Task Lunch_AsksForOneChoice_AndOrdersIt()
    {
        var (runner, _, _) = Create();
        await runner.SendAsync("order lunch");

        var interrupt = runner.Thread.PendingInterrupts.ShouldHaveSingleItem();
        var choices = interrupt.Metadata!.Value.GetProperty("choices");
        choices.GetArrayLength().ShouldBe(3);
        choices[1].GetProperty("id").GetString().ShouldBe("salad");
        interrupt.Metadata.Value.TryGetProperty("allowMultiple", out _).ShouldBeFalse();

        await runner.ResumeAsync([Answer(interrupt, """{"choiceIds":["salad"]}""")]);

        runner.Thread.PendingInterrupts.ShouldBeEmpty();
        LastAssistantText(runner).ShouldContain("Greek salad");
    }

    [Fact]
    public async Task Toppings_AllowsSeveralChoices()
    {
        var (runner, _, _) = Create();
        await runner.SendAsync("pick pizza toppings");

        var interrupt = runner.Thread.PendingInterrupts.ShouldHaveSingleItem();
        interrupt.Metadata!.Value.GetProperty("allowMultiple").GetBoolean().ShouldBeTrue();
        interrupt.Metadata.Value.GetProperty("choices").GetArrayLength().ShouldBe(5);

        await runner.ResumeAsync([Answer(interrupt, """{"choiceIds":["mushroom","basil"]}""")]);

        LastAssistantText(runner).ShouldContain("Mushroom, Basil");
    }

    [Fact]
    public async Task Rename_AsksForText()
    {
        var (runner, _, _) = Create();
        await runner.SendAsync("rename my project");

        var interrupt = runner.Thread.PendingInterrupts.ShouldHaveSingleItem();
        interrupt.ResponseSchema!.Value.GetProperty("properties").GetProperty("text").GetProperty("type").GetString().ShouldBe("string");
        interrupt.Metadata.ShouldBeNull();

        await runner.ResumeAsync([Answer(interrupt, """{"text":"Glitter"}""")]);

        LastAssistantText(runner).ShouldContain("**Glitter**");
    }

    [Fact]
    public async Task Choice_Cancelled_SaysNeverMind()
    {
        var (runner, _, _) = Create();
        await runner.SendAsync("order lunch");
        await runner.ResumeAllAsync(approved: false);

        LastAssistantText(runner).ShouldContain("never mind");
    }

    [Fact]
    public async Task Media_DrawsVideoAudio_AndTheLinkOpensTheUrl()
    {
        var (runner, binding, errors) = Create();
        Uri? opened = null;
        binding.Host.OpenUrl = uri => opened = uri;

        await runner.SendAsync("show me some media");

        var surface = binding.Host.GetSurface("media").ShouldNotBeNull();
        surface.GetComponent("video")!.Type.ShouldBe("Video");
        surface.GetComponent("audio")!.Type.ShouldBe("AudioPlayer");
        errors.ShouldBeEmpty();

        // openUrl needs a user gesture, which DispatchAction is
        surface.DispatchAction(surface.GetComponent("open")!).ShouldBeTrue();
        opened.ShouldBe(new Uri("https://a2ui.org"));
        runner.IsBusy.ShouldBeFalse(); // a local function, so nothing goes to the agent
    }

    static async Task WaitIdle(AgentRunner runner)
    {
        for (var i = 0; i < 200 && runner.IsBusy; i++)
            await Task.Delay(5);
        runner.IsBusy.ShouldBeFalse();
    }
}
