using System.Text.Json;
using System.Text.Json.Nodes;
using AGUI.Abstractions;

namespace Shiny.Controls.AgentUI.Tests;

public class AgentRunnerTests
{
    static AgentTool DateRangeTool(Action<JsonElement>? seen = null) => new(
        "pick_date_range",
        "Ask the user for a date range",
        JsonElement.Parse("""{"type":"object","properties":{"min":{"type":"string"},"max":{"type":"string"}}}"""),
        (call, _) =>
        {
            seen?.Invoke(call.Arguments);
            return ValueTask.FromResult(AgentToolResult.Success("""{"start":"2026-11-10","end":"2026-11-14"}"""));
        });

    [Fact]
    public async Task Send_AddsTheUserTurnAndStreamsTheReply()
    {
        var session = ScriptedSession.FromFixtures(await Fixtures.LoadAsync("text"));
        using var runner = new AgentRunner(session);

        await runner.SendAsync("hi");

        runner.Thread.Items[0].ShouldBeOfType<AgentMessageItem>().IsUser.ShouldBeTrue();
        runner.Thread.Items[1].ShouldBeOfType<AgentMessageItem>().IsAssistant.ShouldBeTrue();
        runner.IsBusy.ShouldBeFalse();

        var input = session.Inputs.ShouldHaveSingleItem();
        input.ThreadId.ShouldBe("thread-1");
        input.Messages.ShouldHaveSingleItem().ShouldBeOfType<AGUIUserMessage>().Content.ToString().ShouldBe("hi");
    }

    [Fact]
    public async Task FrontendTool_RunsLocallyThenFollowsUp()
    {
        var session = ScriptedSession.FromFixtures(
            await Fixtures.LoadAsync("frontend-tool"),
            await Fixtures.LoadAsync("frontend-tool-followup"));
        JsonElement? args = null;
        using var runner = new AgentRunner(session);
        runner.Tools.Add(DateRangeTool(a => args = a));

        await runner.SendAsync("frontend-tool");

        args!.Value.GetProperty("max").GetString().ShouldBe("2026-12-31");
        session.Inputs.Count.ShouldBe(2);

        // the tool is declared on every run
        session.Inputs[0].Tools.ShouldHaveSingleItem().Name.ShouldBe("pick_date_range");

        // the follow-up carries the assistant's call and our result
        var followUp = session.Inputs[1].Messages;
        followUp.OfType<AGUIAssistantMessage>().SelectMany(m => m.ToolCalls ?? []).ShouldHaveSingleItem().Id.ShouldBe("call-fe-1");
        followUp.OfType<AGUIToolMessage>().ShouldHaveSingleItem().Content.ToString().ShouldContain("2026-11-10");

        runner.Thread.FindToolCall("call-fe-1")!.Status.ShouldBe(AgentToolCallStatus.Completed);
        runner.Thread.Items.OfType<AgentMessageItem>().Last().Text.ShouldBe("Done — that's updated.");
    }

    [Fact]
    public async Task FrontendTool_FailureIsReportedAsAnError()
    {
        var session = ScriptedSession.FromFixtures(
            await Fixtures.LoadAsync("frontend-tool"),
            await Fixtures.LoadAsync("frontend-tool-followup"));
        using var runner = new AgentRunner(session);
        runner.Tools.Add(new AgentTool("pick_date_range", "", AgentTool.OpenObjectSchema, (_, _) => throw new InvalidOperationException("no calendar")));

        await runner.SendAsync("frontend-tool");

        var call = runner.Thread.FindToolCall("call-fe-1")!;
        call.Status.ShouldBe(AgentToolCallStatus.Failed);
        session.Inputs[1].Messages.OfType<AGUIToolMessage>().ShouldHaveSingleItem().Error.ShouldBe("no calendar");
    }

    [Fact]
    public async Task UnknownTool_IsLeftForTheApp()
    {
        var session = ScriptedSession.FromFixtures(await Fixtures.LoadAsync("frontend-tool"));
        using var runner = new AgentRunner(session);

        await runner.SendAsync("frontend-tool");

        session.Inputs.Count.ShouldBe(1);
        runner.Thread.FindToolCall("call-fe-1")!.Status.ShouldBe(AgentToolCallStatus.Ready);
    }

    [Fact]
    public async Task Interrupt_ResumeSendsTheResponses()
    {
        var session = ScriptedSession.FromFixtures(
            await Fixtures.LoadAsync("interrupt"),
            await Fixtures.LoadAsync("text"));
        using var runner = new AgentRunner(session);

        await runner.SendAsync("interrupt");
        runner.Thread.RunStatus.ShouldBe(AgentRunStatus.Interrupted);

        await runner.ResumeAllAsync(approved: true, JsonElement.Parse("""{"approved":true}"""));

        var resume = session.Inputs[1].Resume.ShouldNotBeNull().ShouldHaveSingleItem();
        resume.InterruptId.ShouldBe("int-1");
        resume.Status.ShouldBe(ResumeStatus.Resolved);
        runner.Thread.RunStatus.ShouldBe(AgentRunStatus.Completed);
        runner.Thread.PendingInterrupts.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancel_MarksTheRunCancelled()
    {
        var gate = new TaskCompletionSource();
        var session = AgentSession.Create("t", (input, ct) => Slow(ct));
        using var runner = new AgentRunner(session);

        var turn = runner.SendAsync("hi");
        await gate.Task.WaitAsync(TimeSpan.FromSeconds(5));
        runner.Cancel();
        await turn;

        runner.Thread.RunStatus.ShouldBe(AgentRunStatus.Cancelled);
        runner.Thread.FindMessage("m1")!.IsStreaming.ShouldBeFalse();
        runner.IsBusy.ShouldBeFalse();

        async IAsyncEnumerable<BaseEvent> Slow([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            yield return new RunStartedEvent { ThreadId = "t", RunId = "r" };
            yield return new TextMessageStartEvent { MessageId = "m1", Role = "assistant" };
            gate.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            yield break;
        }
    }

    [Fact]
    public async Task TransportFailure_IsRecordedNotThrown()
    {
        var session = AgentSession.Create("t", (_, _) => Throws());
        using var runner = new AgentRunner(session);

        await runner.SendAsync("hi");

        runner.Thread.RunStatus.ShouldBe(AgentRunStatus.Failed);
        runner.LastException.ShouldBeOfType<HttpRequestException>();
        runner.Thread.Items.Last().ShouldBeOfType<AgentErrorItem>().Message.ShouldBe("connection refused");

        static async IAsyncEnumerable<BaseEvent> Throws()
        {
            await Task.Yield();
            throw new HttpRequestException("connection refused");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    [Fact]
    public async Task StreamEndingWithoutRunFinished_IsAFailure()
    {
        var session = ScriptedSession.FromFixtures([new RunStartedEvent { ThreadId = "t", RunId = "r" }]);
        using var runner = new AgentRunner(session);

        await runner.SendAsync("hi");

        runner.Thread.RunStatus.ShouldBe(AgentRunStatus.Failed);
        runner.Thread.Items.Last().ShouldBeOfType<AgentErrorItem>().Code.ShouldBe("STREAM_ENDED");
    }

    [Fact]
    public async Task RunPreparing_ContributesForwardedProps_AndStateGoesOut()
    {
        var session = ScriptedSession.FromFixtures(await Fixtures.LoadAsync("text"));
        using var runner = new AgentRunner(session);
        runner.Thread.SetState(JsonNode.Parse("""{"mode":"draft"}"""));
        runner.Context.Add(new AGUIContext { Description = "page", Value = "orders" });
        runner.RunPreparing += (_, e) => e.ForwardedProperties["caps"] = "x";

        await runner.RunAsync(new JsonObject { ["a2uiAction"] = new JsonObject { ["userAction"] = new JsonObject { ["name"] = "go" } } });

        var input = session.Inputs.ShouldHaveSingleItem();
        input.State!.Value.GetProperty("mode").GetString().ShouldBe("draft");
        input.Context.ShouldHaveSingleItem().Value.ShouldBe("orders");
        var props = input.ForwardedProperties!.Value;
        props.GetProperty("caps").GetString().ShouldBe("x");
        props.GetProperty("a2uiAction").GetProperty("userAction").GetProperty("name").GetString().ShouldBe("go");
        input.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Busy_RejectsAnOverlappingTurn()
    {
        var gate = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var session = AgentSession.Create("t", (_, ct) => Held(ct));
        using var runner = new AgentRunner(session);

        var turn = runner.SendAsync("one");
        await gate.Task.WaitAsync(TimeSpan.FromSeconds(5));
        runner.IsBusy.ShouldBeTrue();
        await Should.ThrowAsync<InvalidOperationException>(() => runner.SendAsync("two"));
        release.SetResult();
        await turn;

        async IAsyncEnumerable<BaseEvent> Held([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            yield return new RunStartedEvent { ThreadId = "t", RunId = "r" };
            gate.TrySetResult();
            await release.Task.WaitAsync(ct);
            yield return new RunFinishedEvent { ThreadId = "t", RunId = "r" };
        }
    }
}

public class AgentRunnerHistoryTests
{
    [Fact]
    public async Task ActivityMessages_StayOutOfTheHistoryUnlessAskedFor()
    {
        static IEnumerable<AGUI.Abstractions.BaseEvent> Surface(AGUI.Abstractions.RunAgentInput _) =>
        [
            new AGUI.Abstractions.RunStartedEvent { ThreadId = "t", RunId = "r1" },
            new AGUI.Abstractions.ActivitySnapshotEvent { MessageId = "a1", ActivityType = "a2ui-surface", Content = System.Text.Json.JsonElement.Parse("{}") },
            new AGUI.Abstractions.RunFinishedEvent { ThreadId = "t", RunId = "r1" }
        ];
        var session = new ScriptedSession(Surface, Surface, Surface);
        var runner = new AgentRunner(session);

        await runner.SendAsync("one");
        await runner.SendAsync("two");
        session.Inputs[1].Messages!.OfType<AGUI.Abstractions.AGUIActivityMessage>().ShouldBeEmpty();

        runner.SendActivityMessages = true;
        await runner.SendAsync("three");
        session.Inputs[2].Messages!.OfType<AGUI.Abstractions.AGUIActivityMessage>().ShouldHaveSingleItem();   // one id, snapshotted each run
    }
}
