using System.ComponentModel;
using System.Text.Json.Nodes;
using Shiny.Controls.AgentUI.A2UI;

namespace Shiny.Controls.AgentUI.Tests.A2UI;

public enum StarTone { Neutral, Good, Danger }

/// <summary>A row of stars the user can tap to score something.</summary>
[A2UIComponent("Rating")]
public partial class RatingModel
{
    /// <summary>The score. Bind it to receive the user's choice.</summary>
    public required double Value { get; init; }

    [A2UIProperty(Literal = true, Minimum = 1, Maximum = 10, Description = "How many stars.")]
    public int Max { get; init; } = 5;

    [Description("The colour of the stars.")]
    public StarTone Tone { get; init; } = StarTone.Good;

    public string? Label { get; init; }

    public bool ShowValue { get; init; } = true;

    public IReadOnlyList<string> Tags { get; init; } = [];

    public A2UIActionRef Action { get; init; }

    public A2UIBindingRef Selection { get; init; }

    [A2UIIgnore]
    public string Ignored { get; set; } = "kept";
}

[A2UIComponent("Board", Description = "Lanes of cards.", Checkable = true, AllowedChildren = ["Lane"], Weight = false)]
public partial class BoardModel
{
    public A2UIChildrenRef Children { get; init; }

    [A2UIProperty(Name = "heading")]
    public required string Title { get; init; }

    public IReadOnlyList<BandModel> Bands { get; init; } = [];

    public A2UIChildRef Footer { get; init; }
}

[A2UIItem]
public partial class BandModel
{
    public required double From { get; init; }
    public required double To { get; init; }
    public StarTone Tone { get; init; }
}

public static partial class TestFunctions
{
    /// <summary>The initials of a name.</summary>
    /// <param name="name">The full name.</param>
    [A2UIFunction("initials")]
    public static string Initials(string name, bool lower = false)
    {
        var initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w[0]));
        return lower ? initials.ToLowerInvariant() : initials;
    }

    [A2UIFunction("stars", Description = "How many stars a score earns.", AllowedCallers = A2UIAllowedCallers.RendererOrAgent)]
    public static int Stars(double score, StarTone tone = StarTone.Neutral, int? cap = null)
        => (int)Math.Min(cap ?? 5, Math.Round(score)) + (tone == StarTone.Good ? 1 : 0);

    [A2UIFunction("culture")]
    public static string CultureName(A2UIFunctionContext context) => context.Culture.Name;
}

public class A2UIGeneratedModelTests
{
    const string Id = "https://example.com/a2ui/generated/v1.json";

    static A2UICatalog Catalog() => A2UICatalogBuilder.ExtendBasic(Id)
        .Component<RatingModel>()
        .Component<BoardModel>()
        .Functions(TestFunctions.A2UIFunctions)
        .Build();

    static (A2UITestHost Test, A2UISurface Surface) Rated()
    {
        var test = new A2UITestHost(new A2UICatalogRegistry().Add(Catalog()));
        test.Host.Apply(new JsonObject
        {
            ["version"] = "1.0",
            ["createSurface"] = new JsonObject
            {
                ["surfaceId"] = "s",
                ["catalogId"] = Id,
                ["dataModel"] = new JsonObject { ["score"] = 4 },
                ["components"] = new JsonArray(new JsonObject
                {
                    ["id"] = "root",
                    ["component"] = RatingModel.A2UIProperties.Component,
                    [RatingModel.A2UIProperties.Value] = new JsonObject { ["@path"] = "/score" },
                    [RatingModel.A2UIProperties.Label] = "fixed",
                    [RatingModel.A2UIProperties.Selection] = new JsonObject { ["@path"] = "/choice" }
                })
            }
        });
        return (test, test.Host.GetSurface("s")!);
    }

    [Fact]
    public void SetWritesTypedValuesToTheBoundPath()
    {
        var (_, surface) = Rated();
        var rating = A2UIModelReader.Read<RatingModel>(surface, surface.Root!, surface.RootScope);

        rating.SetValue(2.5).ShouldBeTrue();
        rating.SetSelection("gold").ShouldBeTrue();

        var reread = A2UIModelReader.Read<RatingModel>(surface, surface.Root!, surface.RootScope);
        reread.Value.ShouldBe(2.5);
        reread.Selection.Path.ShouldBe("/choice");
        surface.EvaluateString(new JsonObject { ["@path"] = "/choice" }).ShouldBe("gold");
    }

    [Fact]
    public void SetReturnsFalseForALiteralOrAModelBuiltInCode()
    {
        var (_, surface) = Rated();
        var rating = A2UIModelReader.Read<RatingModel>(surface, surface.Root!, surface.RootScope);

        rating.SetLabel("changed").ShouldBeFalse();          // the agent gave a literal
        new RatingModel { Value = 1 }.SetValue(3).ShouldBeFalse();
    }

    [Fact]
    public void SetGoesThroughTheHostWriterWhenOneIsGiven()
    {
        var (_, surface) = Rated();
        var writes = new List<(string Property, JsonNode? Value)>();
        var source = new A2UIModelSource(surface.Root!.Properties, surface, surface.RootScope, (p, v) => { writes.Add((p, v)); return true; });

        var rating = Read<RatingModel>(source);
        rating.SetValue(1);

        writes.ShouldHaveSingleItem().Property.ShouldBe("value");
        writes[0].Value!.GetValue<double>().ShouldBe(1);

        static TModel Read<TModel>(A2UIModelSource source) where TModel : IA2UIComponentModel<TModel> => TModel.Read(source);
    }

    [Fact]
    public void FunctionsAreDescribedFromTheirSignatures()
    {
        var catalog = Catalog();

        var initials = catalog.Functions["initials"];
        initials.ReturnType.ShouldBe("string");
        initials.RequiredArguments.ShouldBe(["name"]);
        initials.Arguments.ShouldBe(["name", "lower"], ignoreOrder: true);
        catalog.Definition!["functions"]!["initials"]!["description"]!.GetValue<string>().ShouldBe("The initials of a name.");
        catalog.Definition!["functions"]!["initials"]!["properties"]!["args"]!["properties"]!["name"]!["description"]!.GetValue<string>().ShouldBe("The full name.");

        var stars = catalog.Functions["stars"];
        stars.ReturnType.ShouldBe("number");
        stars.RequiredArguments.ShouldBe(["score"]);
        stars.AllowedCallers.ShouldBe(A2UIAllowedCallers.RendererOrAgent);

        catalog.Functions["culture"].Arguments.ShouldBeEmpty();
    }

    [Fact]
    public void FunctionsRunWithTypedArgumentsAndDefaults()
    {
        var (_, surface) = Rated();

        surface.EvaluateString(new JsonObject { ["@call"] = "initials", ["args"] = new JsonObject { ["name"] = "Ada Lovelace" } }).ShouldBe("AL");
        surface.EvaluateString(new JsonObject { ["@call"] = "initials", ["args"] = new JsonObject { ["name"] = "Ada Lovelace", ["lower"] = true } }).ShouldBe("al");
        surface.EvaluateNumber(new JsonObject { ["@call"] = "stars", ["args"] = new JsonObject { ["score"] = new JsonObject { ["@path"] = "/score" } } }).ShouldBe(4);
        surface.EvaluateNumber(new JsonObject { ["@call"] = "stars", ["args"] = new JsonObject { ["score"] = 9, ["tone"] = "good", ["cap"] = 3 } }).ShouldBe(4);
        surface.EvaluateString(new JsonObject { ["@call"] = "culture", ["args"] = new JsonObject() }).ShouldBe("en-US");
    }

    [Fact]
    public void TheClassBecomesTheComponentSchema()
    {
        var rating = Catalog().Components["Rating"];

        rating.RequiredProperties.ShouldBe(["value"]);
        rating.Properties.ShouldContain("max");
        rating.Properties.ShouldContain("tone");
        rating.Properties.ShouldContain("showValue");
        rating.Properties.ShouldContain("selection");
        rating.Properties.ShouldNotContain("ignored");

        var schema = Catalog().Definition!["components"]!["Rating"]!;
        schema["description"]!.GetValue<string>().ShouldBe("A row of stars the user can tap to score something.");
        var props = schema["properties"]!;
        props["value"]!["description"]!.GetValue<string>().ShouldBe("The score. Bind it to receive the user's choice.");
        props["value"]!["$ref"]!.GetValue<string>().ShouldEndWith("/DynamicNumber");
        props["max"]!["type"]!.GetValue<string>().ShouldBe("integer");
        props["max"]!["minimum"]!.GetValue<long>().ShouldBe(1);
        props["max"]!["maximum"]!.GetValue<long>().ShouldBe(10);
        props["max"]!["default"]!.GetValue<long>().ShouldBe(5);
        props["tone"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()).ShouldBe(["neutral", "good", "danger"]);
        props["tone"]!["default"]!.GetValue<string>().ShouldBe("good");
        props["tone"]!["description"]!.GetValue<string>().ShouldBe("The colour of the stars.");
        props["selection"]!["$ref"]!.GetValue<string>().ShouldEndWith("/DataBinding");
    }

    [Fact]
    public void ComponentAttributeOptionsReachTheEngine()
    {
        var board = Catalog().Components["Board"];

        board.IsCheckable.ShouldBeTrue();
        board.AllowedChildren.ShouldBe(["Lane"]);
        board.Properties.ShouldNotContain("weight");
        board.RequiredProperties.ShouldBe(["heading"]);
        board.ChildProperties.Select(c => (c.Name, c.Kind)).ShouldBe([("children", A2UIChildPropertyKind.List), ("footer", A2UIChildPropertyKind.Single)], ignoreOrder: true);

        var band = Catalog().Definition!["components"]!["Board"]!["allOf"]![1]!["properties"]!["bands"]!["items"]!;
        band["required"]!.AsArray().Select(v => v!.GetValue<string>()).ShouldBe(["from", "to"]);
    }

    [Fact]
    public void ReadEvaluatesBindingsAndKeepsInitializersForOmittedProperties()
    {
        var test = new A2UITestHost(new A2UICatalogRegistry().Add(Catalog()));
        test.Host.Apply(new JsonObject
        {
            ["version"] = "1.0",
            ["createSurface"] = new JsonObject
            {
                ["surfaceId"] = "s",
                ["catalogId"] = Id,
                ["dataModel"] = new JsonObject { ["score"] = 4, ["name"] = "Coffee", ["picked"] = new JsonArray("a", "b") },
                ["components"] = new JsonArray(new JsonObject
                {
                    ["id"] = "root",
                    ["component"] = RatingModel.A2UIProperties.Component,
                    [RatingModel.A2UIProperties.Value] = new JsonObject { ["@path"] = "/score" },
                    [RatingModel.A2UIProperties.Label] = new JsonObject { ["@path"] = "/name" },
                    [RatingModel.A2UIProperties.Tone] = "danger",
                    [RatingModel.A2UIProperties.Tags] = new JsonObject { ["@path"] = "/picked" },
                    [RatingModel.A2UIProperties.Selection] = new JsonObject { ["@path"] = "/choice" },
                    [RatingModel.A2UIProperties.Action] = new JsonObject { ["event"] = new JsonObject { ["name"] = "rated" } }
                })
            }
        });

        var surface = test.Host.GetSurface("s")!;
        var rating = A2UIModelReader.Read<RatingModel>(surface, surface.Root!, surface.RootScope);

        rating.Value.ShouldBe(4);
        rating.Label.ShouldBe("Coffee");
        rating.Tone.ShouldBe(StarTone.Danger);
        rating.Tags.ShouldBe(["a", "b"]);
        rating.Max.ShouldBe(5);               // left out: the initializer stays
        rating.ShowValue.ShouldBeTrue();      // left out: the initializer stays
        rating.Ignored.ShouldBe("kept");
        rating.Action.ShouldBe(new A2UIActionRef("action", true));
        rating.Selection.ShouldBe(new A2UIBindingRef("selection", "/choice"));
        test.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void ReadBuildsItemListsAndChildRefs()
    {
        var test = new A2UITestHost(new A2UICatalogRegistry().Add(Catalog()));
        test.Host.Apply(new JsonObject
        {
            ["version"] = "1.0",
            ["createSurface"] = new JsonObject
            {
                ["surfaceId"] = "s",
                ["catalogId"] = Id,
                ["components"] = new JsonArray(
                    new JsonObject
                    {
                        ["id"] = "root",
                        ["component"] = "Board",
                        ["heading"] = "Sprint",
                        ["children"] = new JsonArray(),
                        ["footer"] = "foot",
                        ["bands"] = new JsonArray(
                            new JsonObject { ["from"] = 0, ["to"] = 5, ["tone"] = "good" },
                            new JsonObject { ["from"] = 5, ["to"] = 9 })
                    },
                    new JsonObject { ["id"] = "foot", ["component"] = "Text", ["text"] = "done" })
            }
        });

        var surface = test.Host.GetSurface("s")!;
        var board = A2UIModelReader.Read<BoardModel>(surface, surface.Root!, surface.RootScope);

        board.Title.ShouldBe("Sprint");
        board.Footer.ShouldBe(new A2UIChildRef("footer", "foot"));
        board.Children.Property.ShouldBe("children");
        board.Bands.Count.ShouldBe(2);
        board.Bands[0].Tone.ShouldBe(StarTone.Good);
        board.Bands[1].To.ShouldBe(9);
        board.Bands[1].Tone.ShouldBe(StarTone.Neutral);
    }

    [Fact]
    public void TheClassStillConstructsInCode()
    {
        var rating = new RatingModel { Value = 2 };

        rating.Max.ShouldBe(5);
        rating.Tone.ShouldBe(StarTone.Good);
    }
}
