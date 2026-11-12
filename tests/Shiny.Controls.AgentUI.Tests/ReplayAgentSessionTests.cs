using System.Text.Json;
using Sample.Features.Agents;
using Shiny.Controls.AgentUI.A2UI;

namespace Shiny.Controls.AgentUI.Tests;

/// <summary>The samples' replay agent: recorded AGUI.Server streams played back through the real runner.</summary>
public class ReplayAgentSessionTests
{
    static (AgentRunner Runner, A2UIAgentBinding Binding) Create(AgentToolRegistry? tools = null)
    {
        var session = new ReplayAgentSession(LoadAll()) { EventDelay = TimeSpan.Zero };
        var runner = new AgentRunner(session, tools: tools);
        return (runner, new A2UIAgentBinding(runner));
    }

    static Dictionary<string, string> LoadAll() =>
        Directory.GetFiles(Fixtures.PathOf("agui"), "*.sse")
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f), File.ReadAllText);

    static string LastAssistantText(AgentRunner runner) =>
        runner.Thread.Items.OfType<AgentMessageItem>().Last(m => m.IsAssistant).Text;

    // the test project embeds the fixtures under the same resource names the samples do
    [Fact]
    public void FromResources_FindsEveryRecording_ButNotTheFollowUps()
    {
        var session = ReplayAgentSession.FromResources(typeof(ReplayAgentSessionTests).Assembly);
        session.Suggestions.ShouldBe(["a2ui", "backend-state", "error", "frontend-tool", "interrupt", "reasoning", "text"]);
    }

    [Fact]
    public async Task Text_PlaysTheRecording()
    {
        var (runner, _) = Create();
        await runner.SendAsync("text");

        LastAssistantText(runner).ShouldStartWith("Hi!");
        runner.Thread.RunStatus.ShouldBe(AgentRunStatus.Completed);
    }

    [Fact]
    public async Task SameRecordingTwice_GetsDistinctIds_SoMessagesDontMerge()
    {
        var (runner, _) = Create();
        await runner.SendAsync("text");
        await runner.SendAsync("text");

        var replies = runner.Thread.Items.OfType<AgentMessageItem>().Where(m => m.IsAssistant).ToList();
        replies.Count.ShouldBe(2);
        replies[0].Id.ShouldNotBe(replies[1].Id);
        replies[1].Text.ShouldBe(replies[0].Text);
    }

    [Fact]
    public async Task FrontendTool_RunsTheAppsTool_ThenPlaysTheFollowUp()
    {
        var tools = new AgentToolRegistry();
        string? args = null;
        tools.Add(new AgentTool("pick_date_range", "Pick dates", JsonElement.Parse("""{"type":"object"}"""),
            (call, _) =>
            {
                args = call.Arguments.GetRawText();
                return ValueTask.FromResult(AgentToolResult.Success("""{"start":"2026-11-02","end":"2026-11-09"}"""));
            }));
        var (runner, _) = Create(tools);

        await runner.SendAsync("frontend-tool");

        args.ShouldNotBeNull().ShouldContain("2026-11-01");
        runner.Thread.Items.OfType<AgentToolCallItem>().ShouldHaveSingleItem().Status.ShouldBe(AgentToolCallStatus.Completed);
        LastAssistantText(runner).ShouldStartWith("Done");
    }

    [Fact]
    public async Task A2UI_DrawsTheRecordedSurface()
    {
        var (runner, binding) = Create();
        await runner.SendAsync("a2ui");

        var activity = runner.Thread.Items.OfType<AgentActivityItem>().ShouldHaveSingleItem();
        binding.GetSurfaces(activity.Id).ShouldHaveSingleItem().Root.ShouldNotBeNull();
    }

    [Fact]
    public async Task Interrupt_Stops_AndResumingEndsTheRecording()
    {
        var (runner, _) = Create();
        await runner.SendAsync("interrupt");

        runner.Thread.RunStatus.ShouldBe(AgentRunStatus.Interrupted);
        runner.Thread.PendingInterrupts.ShouldHaveSingleItem();

        await runner.ResumeAllAsync(approved: true);
        LastAssistantText(runner).ShouldContain("end of the recording");
    }

    [Fact]
    public async Task UnknownPrompt_ListsTheRecordings()
    {
        var (runner, _) = Create();
        await runner.SendAsync("hello?");

        LastAssistantText(runner).ShouldContain("frontend-tool");
        runner.Thread.RunStatus.ShouldBe(AgentRunStatus.Completed);
    }

    [Fact]
    public async Task Error_FailsTheRun()
    {
        var (runner, _) = Create();
        await runner.SendAsync("error");

        runner.Thread.RunStatus.ShouldBe(AgentRunStatus.Failed);
        runner.Thread.Items.OfType<AgentErrorItem>().ShouldHaveSingleItem();
    }
}
