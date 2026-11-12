using Sample.Features.Agents;
using Shiny.Controls.AgentUI.A2UI;

namespace Shiny.Controls.AgentUI.Tests;

/// <summary>Fixes the host builds asked for.</summary>
public class EngineFixTests
{
    static AgentRunner Demo() => new(new DemoAgentSession { TokenDelay = TimeSpan.Zero });

    [Fact]
    public async Task ReusedSurfaceId_IsNotDrawnInTheSecondActivity()
    {
        var runner = Demo();
        var binding = new A2UIAgentBinding(runner, new A2UISurfaceHost(new A2UICatalogRegistry().Add(A2UICatalog.CreateShiny())));
        var errors = new List<string>();
        binding.Host.Outbound += (_, m) => { if (m.Kind == A2UIOutboundKind.Error) errors.Add(m.Body["code"]!.GetValue<string>()); };

        await runner.SendAsync("show me the dashboard");
        await runner.SendAsync("show me the dashboard again");

        var activities = runner.Thread.Items.OfType<AgentActivityItem>().ToList();
        activities.Count.ShouldBe(2);
        binding.GetSurfaces(activities[0].Id).ShouldHaveSingleItem();
        binding.GetSurfaces(activities[1].Id).ShouldBeEmpty();
        errors.ShouldContain(A2UIErrorCodes.IntegrityError);
    }

    [Fact]
    public async Task InterruptsChanged_FiresWhenAskedAndWhenCleared()
    {
        var runner = Demo();
        var kinds = new List<AgentThreadChangeKind>();
        runner.Thread.Changed += (_, c) => kinds.Add(c.Kind);

        await runner.SendAsync("delete my drafts");
        kinds.Count(k => k == AgentThreadChangeKind.InterruptsChanged).ShouldBe(1);

        await runner.ResumeAllAsync(approved: false);
        kinds.Count(k => k == AgentThreadChangeKind.InterruptsChanged).ShouldBe(2);
        runner.Thread.PendingInterrupts.ShouldBeEmpty();
    }

    [Fact]
    public async Task RetryAsync_RunsAgainWithNoNewMessage()
    {
        var runner = Demo();
        await runner.SendAsync("fail on purpose");
        var users = runner.Thread.Items.OfType<AgentMessageItem>().Count(m => m.IsUser);

        await runner.RetryAsync();

        runner.Thread.Items.OfType<AgentMessageItem>().Count(m => m.IsUser).ShouldBe(users);
        runner.Thread.Items.OfType<AgentErrorItem>().Count().ShouldBe(2);   // the demo fails again, on purpose
    }
}

public class CancelReachesToolsTests
{
    [Fact]
    public async Task Cancel_WithdrawsAWaitingPrompt_AndEndsTheTurn()
    {
        var prompts = new AgentPromptQueue();
        var runner = new AgentRunner(new Sample.Features.Agents.DemoAgentSession { TokenDelay = TimeSpan.Zero }, tools: new AgentToolRegistry().AddAll(prompts));

        var turn = runner.SendAsync("book a trip");
        for (var i = 0; i < 200 && prompts.Pending.Count == 0; i++)
            await Task.Delay(5);
        prompts.Pending.ShouldHaveSingleItem();

        runner.Cancel();
        await turn;   // doesn't throw

        prompts.Pending.ShouldBeEmpty();
        runner.IsBusy.ShouldBeFalse();
        runner.Thread.Items.OfType<AgentToolCallItem>().Single().Error.ShouldBe("The user cancelled.");
        runner.Thread.RunStatus.ShouldBe(AgentRunStatus.Cancelled);
    }
}
