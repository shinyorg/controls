using System.Text.Json.Nodes;
using Shiny.Controls.AgentUI.A2UI;

namespace Shiny.Controls.AgentUI.Tests.A2UI;

public class A2UIShinyCatalogTests
{
    static A2UICatalogRegistry Registry() => new A2UICatalogRegistry().Add(A2UICatalog.CreateShiny());

    [Fact]
    public void Catalog_IsBasicPlusShinyComponents()
    {
        var basic = A2UICatalog.CreateBasic();
        var shiny = A2UICatalog.CreateShiny();

        shiny.CatalogId.ShouldBe(A2UICatalog.ShinyCatalogId);
        foreach (var name in basic.Components.Keys)
            shiny.Components.ShouldContainKey(name);
        foreach (var name in basic.Functions.Keys)
        {
            shiny.Functions.ShouldContainKey(name);
            shiny.TryGetImplementation(name, out _).ShouldBeTrue(name);
        }

        A2UICatalog.ShinyComponentNames.ShouldBe(
            ["DataGrid", "Gauge", "ProgressBar", "Pill", "Avatar", "ChipGroup", "TagEntry", "DateRangePicker", "RangeSlider", "Timeline", "Expander", "Markdown"],
            ignoreOrder: true);
        shiny.Components.Count.ShouldBe(basic.Components.Count + A2UICatalog.ShinyComponentNames.Count);
    }

    [Fact]
    public void Catalog_ReadsChildrenRequiredAndChecksFromTheSchema()
    {
        var shiny = A2UICatalog.CreateShiny();

        shiny.Components["Expander"].ChildProperties.ShouldHaveSingleItem().Name.ShouldBe("child");
        shiny.Components["DataGrid"].RequiredProperties.ShouldBe(["columns", "rows"], ignoreOrder: true);
        shiny.Components["ChipGroup"].IsCheckable.ShouldBeTrue();
        shiny.Components["Gauge"].IsCheckable.ShouldBeFalse();
    }

    [Fact]
    public void Definition_IsSelfContained()
    {
        var def = A2UICatalog.CreateShiny().Definition!;

        def["catalogId"]!.GetValue<string>().ShouldBe(A2UICatalog.ShinyCatalogId);
        def["functions"]!.AsObject().Count.ShouldBe(A2UICatalog.CreateBasic().Functions.Count);
        var refs = def["$defs"]!["anyComponent"]!["oneOf"]!.AsArray().Select(r => r!["$ref"]!.GetValue<string>()).ToList();
        refs.ShouldContain("#/components/DataGrid");
        refs.ShouldContain("#/components/Text");
    }

    [Fact]
    public void CreateShiny_ReturnsAFreshCopy()
    {
        var a = A2UICatalog.CreateShiny();
        a.AddFunction(new A2UIFunctionDefinition("mine", "string", A2UIAllowedCallers.RendererOnly, false, [], [], true));

        A2UICatalog.CreateShiny().Functions.ShouldNotContainKey("mine");
    }

    [Fact]
    public void Dashboard_AppliesAndResolvesCleanly()
    {
        var test = new A2UITestHost(Registry());
        var json = JsonNode.Parse(File.ReadAllText(Fixtures.PathOf("a2ui", "shiny", "dashboard.json")))!;

        test.Host.Apply(json["messages"]);

        var surface = test.Host.GetSurface("dashboard").ShouldNotBeNull();
        A2UITestHost.Walk(surface).ShouldBe(15); // every component, the Expander and Button children included
        test.Errors.Select(e => e.ToJsonString()).ShouldBeEmpty();
    }

    [Fact]
    public void Dashboard_RowActionResolvesAgainstTheRow()
    {
        var test = new A2UITestHost(Registry());
        test.Host.Apply(JsonNode.Parse(File.ReadAllText(Fixtures.PathOf("a2ui", "shiny", "dashboard.json")))!["messages"]);
        var surface = test.Host.GetSurface("dashboard")!;
        var grid = surface.GetComponent("grid")!;

        var row = surface.RootScope.ForItem("/orders/1", 1);
        surface.DispatchAction(grid, row, "rowAction").ShouldBeTrue();

        var action = test.Outbound.Single(m => m.Kind == A2UIOutboundKind.Action).Body;
        action["name"]!.GetValue<string>().ShouldBe("openOrder");
        action["context"]!["orderId"]!.GetValue<string>().ShouldBe("A-1002");
    }

    [Fact]
    public void Dashboard_MissingRequiredShinyPropertyIsReported()
    {
        var test = new A2UITestHost(Registry());
        test.Host.Apply(new JsonObject
        {
            ["version"] = "1.0",
            ["createSurface"] = new JsonObject
            {
                ["surfaceId"] = "s",
                ["catalogId"] = A2UICatalog.ShinyCatalogId,
                ["components"] = JsonNode.Parse("""[{"id":"root","component":"Gauge","value":3}]""")
            }
        });

        test.ErrorCodes.ShouldContain(A2UIErrorCodes.ValidationFailed);
    }

    // Writes the merged catalog where the docs site publishes it from (only when asked, so CI doesn't write files)
    [Fact]
    public void Export_WhenRequested()
    {
        var target = Environment.GetEnvironmentVariable("SHINY_A2UI_EXPORT");
        if (string.IsNullOrEmpty(target))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, A2UICatalog.CreateShiny().Definition!.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
