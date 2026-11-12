using System.Text.Json.Nodes;

namespace Shiny.Controls.AgentUI.Tests.A2UI;

/// <summary>Every official basic-catalog example applies and fully resolves without a single error.</summary>
public class A2UIExampleTests
{
    public static TheoryData<string> Examples()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(Fixtures.PathOf("a2ui", "examples"), "*.json").Order())
            data.Add(Path.GetFileNameWithoutExtension(file));
        return data;
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void Example_AppliesAndResolvesCleanly(string name)
    {
        var json = JsonNode.Parse(File.ReadAllText(Fixtures.PathOf("a2ui", "examples", name + ".json")))!;
        var test = new A2UITestHost();

        test.Host.Apply(json["messages"]);

        test.Host.Surfaces.ShouldNotBeEmpty();
        var visited = 0;
        foreach (var surface in test.Host.Surfaces.Values)
        {
            surface.Root.ShouldNotBeNull($"{name}: surface {surface.SurfaceId} has no root");
            visited += A2UITestHost.Walk(surface);
        }

        visited.ShouldBeGreaterThan(0);
        test.Errors.Select(e => e.ToJsonString()).ShouldBeEmpty();
    }
}
