using System.Text.Json;
using AGUI.Abstractions;

namespace Shiny.Controls.AgentUI.Tests;

public class AgentThreadTests
{
    // ── recorded streams ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Text_StreamsIntoOneSettledMessage()
    {
        var thread = await Fixtures.ReplayAsync("text");

        thread.RunStatus.ShouldBe(AgentRunStatus.Completed);
        var message = thread.Items.ShouldHaveSingleItem().ShouldBeOfType<AgentMessageItem>();
        message.IsAssistant.ShouldBeTrue();
        message.IsStreaming.ShouldBeFalse();
        message.Text.ShouldBe("Hi! **Markdown** works, and so does `code`.\n\n- one\n- two");
    }

    [Fact]
    public async Task Reasoning_PrecedesTheAnswerAndKeepsItsEncryptedValue()
    {
        var thread = await Fixtures.ReplayAsync("reasoning");

        thread.Items.Count.ShouldBe(2);
        var reasoning = thread.Items[0].ShouldBeOfType<AgentReasoningItem>();
        reasoning.Text.ShouldBe("The user wants a greeting. Keep it short.");
        reasoning.EncryptedValue.ShouldBe("ENCRYPTED-BLOB");
        reasoning.IsStreaming.ShouldBeFalse();
        reasoning.EndedAt.ShouldNotBeNull();
        thread.Items[1].ShouldBeOfType<AgentMessageItem>().Text.ShouldBe("Hello there!");
    }

    [Fact]
    public async Task FrontendTool_IsReadyWithParsedArguments()
    {
        var thread = await Fixtures.ReplayAsync("frontend-tool");

        var call = thread.Items.OfType<AgentToolCallItem>().ShouldHaveSingleItem();
        call.Name.ShouldBe("pick_date_range");
        call.ParentMessageId.ShouldBe("m1");
        call.Status.ShouldBe(AgentToolCallStatus.Ready);
        call.Arguments!.Value.GetProperty("min").GetString().ShouldBe("2026-11-01");
        call.HasResult.ShouldBeFalse();
    }

    [Fact]
    public async Task BackendTool_ResultAndStateSnapshot()
    {
        var thread = await Fixtures.ReplayAsync("backend-state");

        var call = thread.Items.OfType<AgentToolCallItem>().ShouldHaveSingleItem();
        call.Status.ShouldBe(AgentToolCallStatus.Completed);
        call.Result.ShouldNotBeNull().ShouldContain("\"total\"");

        // the result arrived with messageId == toolCallId; that must not collide with the call
        thread.FindToolCall("call-be-1").ShouldBeSameAs(call);
        thread.FindMessage("call-be-1").ShouldBeNull();

        thread.State!["cart"]!["total"]!.GetValue<double>().ShouldBe(19.98);
        thread.Items.OfType<AgentMessageItem>().Last().Text.ShouldBe("Done — that's updated.");
    }

    [Fact]
    public async Task A2UI_ArrivesAsAnActivity()
    {
        var thread = await Fixtures.ReplayAsync("a2ui");

        var activity = thread.Items.OfType<AgentActivityItem>().ShouldHaveSingleItem();
        activity.ActivityType.ShouldBe("a2ui-surface");
        activity.Content!["a2ui_operations"]![0]!["createSurface"]!["surfaceId"]!.GetValue<string>().ShouldBe("s1");
    }

    [Fact]
    public async Task Interrupt_EndsTheRunWithPendingInterrupts()
    {
        var thread = await Fixtures.ReplayAsync("interrupt");

        thread.RunStatus.ShouldBe(AgentRunStatus.Interrupted);
        var interrupt = thread.PendingInterrupts.ShouldHaveSingleItem();
        interrupt.Id.ShouldBe("int-1");
        interrupt.Message.ShouldBe("Place order for $19.98?");
        interrupt.ResponseSchema.ShouldNotBeNull();
    }

    [Fact]
    public async Task Error_SettlesTheOpenMessageAndAddsAnErrorRow()
    {
        var thread = await Fixtures.ReplayAsync("error");

        thread.RunStatus.ShouldBe(AgentRunStatus.Failed);
        thread.Items[0].ShouldBeOfType<AgentMessageItem>().IsStreaming.ShouldBeFalse();
        thread.Items[1].ShouldBeOfType<AgentErrorItem>().Code.ShouldBe("StreamingError");
        thread.LastError.ShouldNotBeNull();
    }

    [Fact]
    public async Task ToAGUIMessages_RoundTripsTheConversation()
    {
        var thread = await Fixtures.ReplayAsync("frontend-tool");
        thread.SetToolResult("call-fe-1", """{"start":"2026-11-10"}""");

        var messages = thread.ToAGUIMessages();
        var assistant = messages[0].ShouldBeOfType<AGUIAssistantMessage>();
        assistant.Content.ShouldBe("Let me ask you for dates.");
        assistant.ToolCalls!.ShouldHaveSingleItem().Function.Name.ShouldBe("pick_date_range");
        var tool = messages[1].ShouldBeOfType<AGUIToolMessage>();
        tool.ToolCallId.ShouldBe("call-fe-1");
        tool.Content.ToString().ShouldBe("""{"start":"2026-11-10"}""");

        // and back again through a snapshot
        var restored = new AgentThread();
        restored.Apply(new MessagesSnapshotEvent { Messages = messages });
        restored.Items.Count.ShouldBe(2);
        restored.FindToolCall("call-fe-1")!.Status.ShouldBe(AgentToolCallStatus.Completed);
    }

    // ── hand-built edge cases ────────────────────────────────────────────────────

    [Fact]
    public void Chunks_NormaliseToStartContentEnd()
    {
        var thread = new AgentThread();
        thread.Apply(new RunStartedEvent { ThreadId = "t", RunId = "r" });
        thread.Apply(new TextMessageChunkEvent { MessageId = "m1", Role = "assistant", Delta = "Hel" });
        thread.Apply(new TextMessageChunkEvent { Delta = "lo" });   // id omitted: continues the open chunk
        var message = thread.FindMessage("m1")!;
        message.IsStreaming.ShouldBeTrue();

        thread.Apply(new ToolCallChunkEvent { ToolCallId = "c1", ToolCallName = "lookup", Delta = "{\"q\":" });
        message.IsStreaming.ShouldBeFalse();    // any other event closes the open chunk
        message.Text.ShouldBe("Hello");

        thread.Apply(new ToolCallChunkEvent { ToolCallId = "c1", Delta = "1}" });
        thread.Apply(new RunFinishedEvent { ThreadId = "t", RunId = "r" });

        var call = thread.FindToolCall("c1")!;
        call.Status.ShouldBe(AgentToolCallStatus.Ready);
        call.Arguments!.Value.GetProperty("q").GetInt32().ShouldBe(1);
    }

    [Fact]
    public void ReasoningChunk_EmptyDeltaCloses()
    {
        var thread = new AgentThread();
        thread.Apply(new ReasoningMessageChunkEvent { MessageId = "r1", Delta = "Thinking" });
        thread.Apply(new ReasoningMessageChunkEvent { MessageId = "r1", Delta = "" });
        thread.Items.ShouldHaveSingleItem().ShouldBeOfType<AgentReasoningItem>().IsStreaming.ShouldBeFalse();
    }

    [Fact]
    public void StateDelta_AppliesJsonPatch()
    {
        var thread = new AgentThread();
        thread.Apply(new StateSnapshotEvent { Snapshot = JsonElement.Parse("""{"count":1,"tags":["a"]}""") });
        thread.Apply(new StateDeltaEvent { Delta = JsonElement.Parse("""[{"op":"replace","path":"/count","value":2},{"op":"add","path":"/tags/-","value":"b"}]""") });

        thread.State!.ToJsonString().ShouldBe("""{"count":2,"tags":["a","b"]}""");
    }

    [Fact]
    public void ActivityDelta_PatchesInPlace_AndReplaceFalseIsIgnoredOnceSeen()
    {
        var thread = new AgentThread();
        thread.Apply(new ActivitySnapshotEvent { MessageId = "a1", ActivityType = "progress", Content = JsonElement.Parse("""{"pct":10}""") });
        thread.Apply(new ActivityDeltaEvent { MessageId = "a1", ActivityType = "progress", Patch = JsonElement.Parse("""[{"op":"replace","path":"/pct","value":50}]""") });
        thread.Apply(new ActivitySnapshotEvent { MessageId = "a1", ActivityType = "progress", Content = JsonElement.Parse("""{"pct":0}"""), Replace = false });

        var activity = thread.FindActivity("a1")!;
        activity.Content!["pct"]!.GetValue<int>().ShouldBe(50);
        thread.Items.Count.ShouldBe(1);
    }

    [Fact]
    public void Steps_TrackWhatIsRunning()
    {
        var thread = new AgentThread();
        thread.Apply(new RunStartedEvent { ThreadId = "t", RunId = "r" });
        thread.Apply(new StepStartedEvent { StepName = "plan" });
        thread.Apply(new StepStartedEvent { StepName = "search" });
        thread.Steps.Select(s => s.Name).ShouldBe(["plan", "search"]);

        thread.Apply(new StepFinishedEvent { StepName = "search" });
        thread.Steps.ShouldHaveSingleItem().Name.ShouldBe("plan");

        thread.Apply(new RunFinishedEvent { ThreadId = "t", RunId = "r" });
        thread.Steps.ShouldBeEmpty();
    }

    [Fact]
    public void Subagents_NestAndAttribute()
    {
        var thread = new AgentThread();
        thread.Apply(new RunStartedEvent { ThreadId = "t", RunId = "r" });
        thread.Apply(new SubagentStartedEvent { SubagentRunId = "s1", Name = "researcher", ParentToolCallId = "c1" });
        thread.Apply(new TextMessageStartEvent { MessageId = "m1", Role = "assistant", SubagentRunId = "s1" });
        thread.Apply(new TextMessageContentEvent { MessageId = "m1", Delta = "found it", SubagentRunId = "s1" });
        thread.Apply(new SubagentFinishedEvent
        {
            SubagentRunId = "s1",
            Outcome = new SubagentFinishedSuspendedOutcome { InterruptIds = ["i1"] }
        });

        var sub = thread.FindSubagent("s1")!;
        sub.Name.ShouldBe("researcher");
        sub.ParentToolCallId.ShouldBe("c1");
        sub.Status.ShouldBe(AgentSubagentStatus.Suspended);
        sub.InterruptIds.ShouldBe(["i1"]);

        var message = thread.FindMessage("m1")!;
        message.SubagentRunId.ShouldBe("s1");
        message.IsStreaming.ShouldBeFalse();   // the sub-agent finishing settles what it left open
    }

    [Fact]
    public void Custom_IsForwardedNotRendered()
    {
        var thread = new AgentThread();
        BaseEvent? seen = null;
        thread.CustomEventReceived += (_, e) => seen = e;

        thread.Apply(new CustomEvent { Name = "confetti" });

        seen.ShouldBeOfType<CustomEvent>().Name.ShouldBe("confetti");
        thread.Items.ShouldBeEmpty();
    }

    [Fact]
    public void Changed_ReportsAddsAndUpdatesWithIndex()
    {
        var thread = new AgentThread();
        var changes = new List<AgentThreadChange>();
        thread.Changed += (_, c) => changes.Add(c);

        thread.Apply(new TextMessageStartEvent { MessageId = "m1", Role = "assistant" });
        thread.Apply(new TextMessageContentEvent { MessageId = "m1", Delta = "x" });

        changes.Select(c => c.Kind).ShouldBe([AgentThreadChangeKind.ItemAdded, AgentThreadChangeKind.ItemUpdated]);
        changes.ShouldAllBe(c => c.Index == 0);
        thread.Items[0].Version.ShouldBe(1);
    }

    [Fact]
    public void RunFinished_SettlesAnythingLeftOpen()
    {
        var thread = new AgentThread();
        thread.Apply(new RunStartedEvent { ThreadId = "t", RunId = "r" });
        thread.Apply(new TextMessageStartEvent { MessageId = "m1", Role = "assistant" });
        thread.Apply(new ToolCallStartEvent { ToolCallId = "c1", ToolCallName = "x" });
        thread.Apply(new RunFinishedEvent { ThreadId = "t", RunId = "r" });

        thread.FindMessage("m1")!.IsStreaming.ShouldBeFalse();
        thread.FindToolCall("c1")!.Status.ShouldBe(AgentToolCallStatus.Ready);
    }
}
