using System.Text.Json;
using System.Text.Json.Nodes;
using AGUI.Abstractions;
using Shiny.Controls.AgentUI.A2UI;

namespace Shiny.Controls.AgentUI.Tests.A2UI;

public class A2UIAgentBindingTests
{
    static ActivitySnapshotEvent Snapshot(string messageId, params string[] operations) => new()
    {
        MessageId = messageId,
        ActivityType = A2UIAgentBinding.DefaultActivityType,
        Replace = true,
        Content = JsonElement.Parse($$"""{"a2ui_operations":[{{string.Join(",", operations)}}]}""")
    };

    const string Create = """{"version":"1.0","createSurface":{"surfaceId":"s1","catalogId":"https://a2ui.org/specification/v1_0/catalogs/basic/catalog.json","dataModel":{"name":""},"components":[{"id":"root","component":"TextField","label":"Name","value":{"@path":"/name"}}]}}""";
    const string Update = """{"version":"1.0","updateComponents":{"surfaceId":"s1","components":[{"id":"root","component":"TextField","label":"Your name","value":{"@path":"/name"}}]}}""";

    [Fact]
    public async Task RecordedStream_CreatesTheSurface_AndTheButtonStartsARun()
    {
        var session = ScriptedSession.FromFixtures(await Fixtures.LoadAsync("a2ui"), await Fixtures.LoadAsync("text"));
        using var runner = new AgentRunner(session);
        using var binding = new A2UIAgentBinding(runner);

        await runner.SendAsync("a2ui");

        var activity = runner.Thread.Items.OfType<AgentActivityItem>().Single();
        var surface = binding.GetSurfaces(activity.Id).ShouldHaveSingleItem();
        surface.SurfaceId.ShouldBe("s1");
        surface.EvaluateString(surface.GetComponent("t")!["text"]).ShouldBe("Allan");

        // capabilities ride on every run
        session.Inputs[0].ForwardedProperties!.Value
            .GetProperty("a2uiRendererCapabilities").GetProperty("1.0").GetProperty("supportedCatalogIds")[0].GetString()
            .ShouldBe(A2UICatalog.BasicCatalogId);

        surface.DispatchAction(surface.GetComponent("b")!).ShouldBeTrue();
        await WaitFor(() => session.Inputs.Count == 2 && !runner.IsBusy);

        var action = session.Inputs[1].ForwardedProperties!.Value.GetProperty("a2uiAction").GetProperty("userAction");
        action.GetProperty("name").GetString().ShouldBe("confirm");
        action.GetProperty("context").GetProperty("who").GetString().ShouldBe("Allan");
        session.Inputs[1].Messages.OfType<AGUIUserMessage>().Count().ShouldBe(1);   // no new user message
    }

    [Fact]
    public void ResentSnapshot_AppliesOnlyNewOperations_KeepingWhatTheUserTyped()
    {
        using var runner = new AgentRunner(AgentSession.Create("t", (_, _) => throw new NotSupportedException()));
        using var binding = new A2UIAgentBinding(runner);

        runner.Thread.Apply(Snapshot("ui-1", Create));
        var surface = binding.Host.GetSurface("s1")!;
        surface.Write(surface.Root!["value"], null, JsonValue.Create("Allan"));

        runner.Thread.Apply(Snapshot("ui-1", Create, Update));

        binding.Host.GetSurface("s1").ShouldBeSameAs(surface);
        surface.GetData("/name")!.GetValue<string>().ShouldBe("Allan");
        surface.EvaluateString(surface.Root!["label"]).ShouldBe("Your name");
    }

    [Fact]
    public void RewrittenSnapshot_RebuildsThatMessagesSurfaces()
    {
        using var runner = new AgentRunner(AgentSession.Create("t", (_, _) => throw new NotSupportedException()));
        using var binding = new A2UIAgentBinding(runner);
        var errors = new List<A2UIError>();
        binding.Host.Error += (_, e) => errors.Add(e);

        runner.Thread.Apply(Snapshot("ui-1", Create, Update));
        var first = binding.Host.GetSurface("s1");
        runner.Thread.Apply(Snapshot("ui-1", Create));

        binding.Host.GetSurface("s1").ShouldNotBeSameAs(first);
        binding.Host.GetSurface("s1")!.EvaluateString(binding.Host.GetSurface("s1")!.Root!["label"]).ShouldBe("Name");
        errors.ShouldBeEmpty();   // no INTEGRITY_ERROR: the old surface was deleted first
    }

    [Fact]
    public void OtherActivityTypesAreIgnored()
    {
        using var runner = new AgentRunner(AgentSession.Create("t", (_, _) => throw new NotSupportedException()));
        using var binding = new A2UIAgentBinding(runner);

        runner.Thread.Apply(new ActivitySnapshotEvent { MessageId = "p", ActivityType = "progress", Content = JsonElement.Parse("""{"pct":5}""") });

        binding.Host.Surfaces.ShouldBeEmpty();
    }

    static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        condition().ShouldBeTrue();
    }
}
