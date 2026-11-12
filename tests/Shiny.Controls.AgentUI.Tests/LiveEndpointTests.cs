using System.Text.Json;
using Sample.Features.Agents;
using Shiny.Controls.AgentUI.A2UI;

namespace Shiny.Controls.AgentUI.Tests;

/// <summary>
/// End to end against a real AG-UI server over HTTP + SSE. Opt-in: start the spike
/// (<c>dotnet run --project spikes/AgUiSpike -- serve</c>) and set <c>AGUI_ENDPOINT=http://localhost:5299/agent</c>.
/// </summary>
public class LiveEndpointTests
{
    static Uri? Endpoint => Environment.GetEnvironmentVariable("AGUI_ENDPOINT") is { Length: > 0 } url ? new Uri(url) : null;

    [Fact]
    public async Task Text_A2UI_AndFrontendTool_OverRealHttp()
    {
        if (Endpoint is not { } endpoint)
            return;

        using var http = new HttpClient();
        var tools = new AgentToolRegistry().Add(new AgentTool("pick_date_range", "Pick dates", JsonElement.Parse("""{"type":"object"}"""),
            (_, _) => ValueTask.FromResult(AgentToolResult.Success("""{"start":"2026-11-10","end":"2026-11-14"}"""))));
        var runner = new AgentRunner(new HttpAgentSession(http, endpoint), tools: tools);
        var binding = new A2UIAgentBinding(runner);

        await runner.SendAsync("text");
        runner.LastException.ShouldBeNull();
        runner.Thread.Items.OfType<AgentMessageItem>().Last().Text.ShouldContain("Markdown");

        await runner.SendAsync("a2ui");
        var activity = runner.Thread.Items.OfType<AgentActivityItem>().ShouldHaveSingleItem();
        var surface = binding.GetSurfaces(activity.Id).ShouldHaveSingleItem();

        // a button on the surface starts a run with no new message (forwardedProps.a2uiAction)
        surface.DispatchAction(surface.GetComponent("b")!).ShouldBeTrue();
        for (var i = 0; i < 400 && (runner.IsBusy || runner.Thread.RunStatus != AgentRunStatus.Completed); i++)
            await Task.Delay(5);
        runner.LastException.ShouldBeNull();

        await runner.SendAsync("interrupt");
        runner.LastException.ShouldBeNull();
        runner.Thread.RunStatus.ShouldBe(AgentRunStatus.Interrupted);
        await runner.ResumeAllAsync(approved: true);
        runner.LastException.ShouldBeNull();

        await runner.SendAsync("frontend-tool");
        runner.Thread.Items.OfType<AgentToolCallItem>().Last().Status.ShouldBe(AgentToolCallStatus.Completed);
        runner.Thread.Items.OfType<AgentMessageItem>().Last(m => m.IsAssistant).Text.ShouldContain("updated");
        runner.Thread.RunStatus.ShouldBe(AgentRunStatus.Completed);

        // AGUI.Server 1.0.1 rejects the activity and reasoning roles, which the runner leaves out by default
        runner.LastException.ShouldBeNull();
        await runner.SendAsync("reasoning");
        await runner.SendAsync("text");
        runner.LastException.ShouldBeNull();
        runner.Thread.RunStatus.ShouldBe(AgentRunStatus.Completed);
    }
}
