using System.Text.Json.Nodes;
using Shiny.Controls.AgentUI.A2UI;

namespace Shiny.Controls.AgentUI.Tests.A2UI;

public class A2UICatalogBuilderTests
{
    const string Id = "https://example.com/a2ui/my-app/v1.json";

    enum Tone { Neutral, Good, Danger }

    static A2UICatalog Rating(Action<A2UIComponentBuilder>? extra = null) => A2UICatalogBuilder
        .ExtendShiny(Id)
        .Component("Rating", "A row of stars the user can tap.", c =>
        {
            c.Number("value", "The score.", required: true)
             .Number("max", "How many stars.", dynamic: false, minimum: 1, defaultValue: 5)
             .Choice<Tone>("tone", defaultValue: Tone.Neutral)
             .Action();
            extra?.Invoke(c);
        })
        .Build();

    [Fact]
    public void BuiltComponentHasTheDeclaredShape()
    {
        var rating = Rating().Components["Rating"];

        rating.RequiredProperties.ShouldBe(["value"]);
        rating.Properties.ShouldContain("max");
        rating.Properties.ShouldContain("tone");
        rating.Properties.ShouldContain("action");
        rating.Properties.ShouldContain("weight");
        rating.IsCheckable.ShouldBeFalse();
        rating.ChildProperties.ShouldBeEmpty();
    }

    [Fact]
    public void ExtendingShinyKeepsEveryComponentAndFunctionImplementation()
    {
        var catalog = Rating();

        catalog.CatalogId.ShouldBe(Id);
        foreach (var name in A2UICatalog.CreateShiny().Components.Keys)
            catalog.Components.ShouldContainKey(name);
        catalog.TryGetImplementation("formatCurrency", out _).ShouldBeTrue();
    }

    [Fact]
    public void DefinitionIsTheSchemaTheAgentReads()
    {
        var definition = Rating().Definition.ShouldNotBeNull();

        definition["catalogId"]!.GetValue<string>().ShouldBe(Id);
        var schema = definition["components"]!["Rating"]!;
        schema["properties"]!["component"]!["const"]!.GetValue<string>().ShouldBe("Rating");
        schema["properties"]!["value"]!["$ref"]!.GetValue<string>().ShouldEndWith("/DynamicNumber");
        schema["properties"]!["max"]!["minimum"]!.GetValue<double>().ShouldBe(1);
        schema["properties"]!["tone"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()).ShouldBe(["neutral", "good", "danger"]);
        definition["$defs"]!["anyComponent"]!["oneOf"]!.AsArray()
            .Select(r => r!["$ref"]!.GetValue<string>())
            .ShouldContain("#/components/Rating");
    }

    [Fact]
    public void ChildrenCheckableAndParentRulesReachTheEngine()
    {
        var catalog = A2UICatalogBuilder.ExtendBasic(Id)
            .Component("Board", "Columns of cards.", c => c.Children().Checkable().AllowedChildren("Card"))
            .Component("Lane", "One titled lane.", c => c.String("title", required: true).Child("content").AllowedParents("Board"))
            .Component("Tabbed", "Titled tabs.", c => c.Array("tabs", t => t.String("title").Child("child", required: true)))
            .Build();

        var board = catalog.Components["Board"];
        board.IsCheckable.ShouldBeTrue();
        board.ChildProperties.ShouldHaveSingleItem().Kind.ShouldBe(A2UIChildPropertyKind.List);
        board.AllowedChildren.ShouldBe(["Card"]);

        var lane = catalog.Components["Lane"];
        lane.ChildProperties.ShouldHaveSingleItem().Kind.ShouldBe(A2UIChildPropertyKind.Single);
        lane.AllowedParents.ShouldBe(["Board"]);

        var tab = catalog.Components["Tabbed"].ChildProperties.ShouldHaveSingleItem();
        tab.Kind.ShouldBe(A2UIChildPropertyKind.ItemChild);
        tab.ItemProperty.ShouldBe("child");
    }

    [Fact]
    public void ASurfaceOnTheBuiltCatalogRendersAndValidatesTheComponent()
    {
        var catalogs = new A2UICatalogRegistry().Add(Rating());
        var test = new A2UITestHost(catalogs);

        test.Host.Apply(new JsonObject
        {
            ["version"] = "1.0",
            ["createSurface"] = new JsonObject
            {
                ["surfaceId"] = "s",
                ["catalogId"] = Id,
                ["dataModel"] = new JsonObject { ["score"] = 3 },
                ["components"] = new JsonArray(
                    new JsonObject { ["id"] = "root", ["component"] = "Column", ["children"] = new JsonArray("stars", "missing") },
                    new JsonObject { ["id"] = "stars", ["component"] = "Rating", ["value"] = new JsonObject { ["@path"] = "/score" } },
                    new JsonObject { ["id"] = "missing", ["component"] = "Rating" })
            }
        });

        var surface = test.Host.GetSurface("s").ShouldNotBeNull();
        surface.EvaluateNumber(surface.GetComponent("stars")!["value"], surface.RootScope).ShouldBe(3);
        test.ErrorCodes.ShouldContain("VALIDATION_FAILED"); // "missing" has no required value
    }

    [Fact]
    public void FunctionsAreDeclaredAndImplemented()
    {
        var catalog = A2UICatalogBuilder.ExtendBasic(Id)
            .Function("initials", "The initials of a name.", A2UIReturnType.String,
                f => f.String("name", required: true),
                ctx => JsonValue.Create(string.Concat(ctx.RawArguments["name"]!.GetValue<string>().Split(' ').Select(w => w[0]))))
            .Function("lookup", "Asks the agent to look something up.", A2UIReturnType.String,
                f => f.String("query", required: true).AllowedCallers(A2UIAllowedCallers.AgentOnly))
            .Build();

        var initials = catalog.Functions["initials"];
        initials.ReturnType.ShouldBe("string");
        initials.RequiredArguments.ShouldBe(["name"]);
        initials.AllowsAdditionalArguments.ShouldBeFalse();
        catalog.TryGetImplementation("initials", out _).ShouldBeTrue();

        catalog.Functions["lookup"].AllowedCallers.ShouldBe(A2UIAllowedCallers.AgentOnly);
        catalog.TryGetImplementation("lookup", out _).ShouldBeFalse();
    }

    [Fact]
    public void CreateStartsEmptyButValid()
    {
        var catalog = A2UICatalogBuilder.Create(Id)
            .Title("Mine")
            .Component("Rating", "Stars.", c => c.Number("value", required: true))
            .Build();

        catalog.Components.Keys.ShouldBe(["Rating"]);
        catalog.Definition!["$defs"]!["Weight"].ShouldNotBeNull();
    }

    [Fact]
    public void InstructionsAppendToTheExtendedCatalogs()
    {
        var shiny = A2UICatalog.CreateShiny().Definition!["instructions"]!.GetValue<string>();

        var catalog = A2UICatalogBuilder.ExtendShiny(Id).Instructions("Use Rating to score things.").Build();

        var text = catalog.Definition!["instructions"]!.GetValue<string>();
        text.ShouldStartWith(shiny);
        text.ShouldEndWith("Use Rating to score things.");
    }

    [Fact]
    public void LiteralOnlyRulesAreRejectedOnDynamicProperties()
    {
        Should.Throw<ArgumentException>(() => A2UICatalogBuilder.Create(Id).Component("X", "x", c => c.Number("v", minimum: 0)));
        Should.Throw<ArgumentException>(() => A2UICatalogBuilder.Create(Id).Component("X", "x", c => c.String("v", defaultValue: "a")));
        Should.Throw<ArgumentException>(() => A2UICatalogBuilder.Create(Id).Component("X", "x", c => c.String("id")));
        Should.Throw<ArgumentException>(() => A2UICatalogBuilder.Create(Id).Component("X", "x", c => c.Choice("v", ["a"], defaultValue: "b")));
    }

    [Fact]
    public void EachBuildIsIndependent()
    {
        var builder = A2UICatalogBuilder.Create(Id).Component("A", "a", c => c.String("text"));
        var first = builder.Build();

        builder.Component("B", "b", c => c.String("text"));
        var second = builder.Build();

        first.Components.ShouldNotContainKey("B");
        second.Components.ShouldContainKey("B");
    }

    [Fact]
    public void ToEnumReadsChoicesBack()
    {
        A2UICatalogValues.ToEnum<Tone>("danger").ShouldBe(Tone.Danger);
        A2UICatalogValues.ToEnum("nope", Tone.Good).ShouldBe(Tone.Good);
        A2UICatalogValues.ToEnum("7", Tone.Good).ShouldBe(Tone.Good);
    }
}
