using System.Globalization;
using System.Text.Json.Nodes;
using Shiny.Controls.AgentUI.A2UI;

namespace Shiny.Controls.AgentUI.Tests.A2UI;

public class A2UIEvaluationTests
{
    readonly A2UITestHost test = new();

    // ── bindings & scopes ────────────────────────────────────────────────────────

    [Fact]
    public void Bindings_AbsoluteRelativeAndMissing()
    {
        var surface = test.Surface("""{"company":"Acme","employees":[{"name":"Alice"},{"name":"Bob"}]}""", """
            [{"id":"root","component":"List","children":{"path":"/employees","componentId":"row"}},
             {"id":"row","component":"Text","text":{"@path":"name"}}]
            """);

        var rows = surface.GetChildren(surface.Root!, "children");
        rows.Count.ShouldBe(2);
        rows[1].Scope.BasePath.ShouldBe("/employees/1");
        surface.EvaluateString(rows[1].Component!["text"], rows[1].Scope).ShouldBe("Bob");
        test.EvalString(surface, """{"@path":"/company"}""", rows[0].Scope).ShouldBe("Acme");
        test.Eval(surface, """{"@path":"/nope"}""").ShouldBeNull();
        test.EvalString(surface, """{"@path":"/nope"}""").ShouldBe("");
    }

    [Fact]
    public void Index_IsTheInnermostTemplate_WithOffset()
    {
        var surface = test.Surface("""{"groups":[{"items":["a","b"]},{"items":["c"]}]}""", """
            [{"id":"root","component":"Column","children":{"path":"/groups","componentId":"group"}},
             {"id":"group","component":"Column","children":{"path":"items","componentId":"item"}},
             {"id":"item","component":"Text","text":{"@call":"formatString","args":{"value":"#${@index(offset: 1)}"}}}]
            """);

        var group = surface.GetChildren(surface.Root!, "children")[1];
        var items = surface.GetChildren(group.Component!, "children", group.Scope);
        items.ShouldHaveSingleItem().Scope.BasePath.ShouldBe("/groups/1/items/0");
        surface.EvaluateString(items[0].Component!["text"], items[0].Scope).ShouldBe("#1");
    }

    [Fact]
    public void Index_OutsideATemplateIsAnError()
    {
        var surface = test.Surface();
        test.Eval(surface, """{"@call":"@index"}""").ShouldBeNull();
        test.ErrorCodes.ShouldContain(A2UIErrorCodes.ExpressionError);
    }

    [Fact]
    public void TwoWayBinding_CreatesThePathAndRaisesTheChange()
    {
        var surface = test.Surface();
        string? changed = null;
        surface.Changed += (_, c) => changed = c.Path;

        surface.Write(JsonNode.Parse("""{"@path":"/form/email"}"""), null, JsonValue.Create("jane@example.com")).ShouldBeTrue();

        surface.GetData("/form/email")!.GetValue<string>().ShouldBe("jane@example.com");
        changed.ShouldBe("/form/email");
        surface.Write(JsonValue.Create("literal"), null, JsonValue.Create("x")).ShouldBeFalse();
    }

    [Fact]
    public void Dependencies_RecordWhatWasRead()
    {
        var surface = test.Surface("""{"a":1,"b":2}""");
        var deps = new HashSet<string>();

        surface.Evaluate(JsonNode.Parse("""{"@call":"formatString","args":{"value":"${/a} and ${/b}"}}"""), surface.RootScope.Tracking(deps));

        deps.ShouldBe(["/a", "/b"], ignoreOrder: true);
    }

    [Fact]
    public void UpdateDataModel_UpsertsRemovesAndReplaces()
    {
        var surface = test.Surface("""{"user":{"first":"A","temp":1}}""");

        test.Apply("""{"version":"1.0","updateDataModel":{"surfaceId":"s1","path":"/user/last","value":"B"}}""");
        test.Apply("""{"version":"1.0","updateDataModel":{"surfaceId":"s1","path":"/user/temp","value":null}}""");
        surface.DataModel!.ToJsonString().ShouldBe("""{"user":{"first":"A","last":"B"}}""");

        test.Apply("""{"version":"1.0","updateDataModel":{"surfaceId":"s1","value":{"fresh":true}}}""");
        surface.DataModel!.ToJsonString().ShouldBe("""{"fresh":true}""");
    }

    // ── formatString ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Hello, ${/user/first}!", "Hello, Ada!")]
    [InlineData(@"Cost: \${literal}", "Cost: ${literal}")]
    [InlineData("${/count} items", "3 items")]
    [InlineData("${'quoted'} ${42} ${true} ${null}|", "quoted 42 true |")]
    [InlineData("${formatNumber(value:${/price}, decimals:2)}", "1,234.50")]
    [InlineData("${formatDate(value:${/when}, format:'yyyy-MM-dd')}", "2026-03-04")]
    [InlineData("${/user}", """{"first":"Ada"}""")]
    [InlineData("${pluralize(value:${/count}, one:'item', other:'items')}", "items")]
    public void FormatString(string template, string expected)
    {
        var surface = test.Surface("""{"user":{"first":"Ada"},"count":3,"price":1234.5,"when":"2026-03-04T10:00:00Z"}""");
        var call = new JsonObject { ["@call"] = "formatString", ["args"] = new JsonObject { ["value"] = template } };

        surface.EvaluateString(call).ShouldBe(expected);
        test.Errors.ShouldBeEmpty();
    }

    // ── functions ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("""{"@call":"required","args":{"value":"x"}}""", true)]
    [InlineData("""{"@call":"required","args":{"value":""}}""", false)]
    [InlineData("""{"@call":"required","args":{"value":[]}}""", false)]
    [InlineData("""{"@call":"required","args":{"value":{"@path":"/missing"}}}""", false)]
    [InlineData("""{"@call":"length","args":{"value":"abcd","min":2,"max":4}}""", true)]
    [InlineData("""{"@call":"length","args":{"value":"abcde","max":4}}""", false)]
    [InlineData("""{"@call":"numeric","args":{"value":"42","min":0,"max":100}}""", true)]
    [InlineData("""{"@call":"numeric","args":{"value":"abc"}}""", false)]
    [InlineData("""{"@call":"email","args":{"value":"a@b.co"}}""", true)]
    [InlineData("""{"@call":"email","args":{"value":"a@b"}}""", false)]
    [InlineData("""{"@call":"and","args":{"values":[true,{"@call":"required","args":{"value":"x"}},"TRUE"]}}""", true)]
    [InlineData("""{"@call":"and","args":{"values":[true,"yes"]}}""", false)]
    [InlineData("""{"@call":"or","args":{"values":[0,null,1]}}""", true)]
    [InlineData("""{"@call":"not","args":{"value":{"@call":"required","args":{"value":""}}}}""", true)]
    public void BooleanAndValidationFunctions(string call, bool expected)
    {
        var surface = test.Surface();
        surface.EvaluateBoolean(JsonNode.Parse(call)).ShouldBe(expected);
        test.Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("en-US", """{"@call":"formatNumber","args":{"value":1234567.891}}""", "1,234,567.891")]
    [InlineData("en-US", """{"@call":"formatNumber","args":{"value":1234.5,"grouping":false,"decimals":0}}""", "1235")]
    [InlineData("de-DE", """{"@call":"formatNumber","args":{"value":1234.5,"decimals":2}}""", "1.234,50")]
    [InlineData("en-US", """{"@call":"formatCurrency","args":{"value":19.98,"currency":"USD"}}""", "$19.98")]
    [InlineData("en-US", """{"@call":"formatCurrency","args":{"value":1500,"currency":"JPY"}}""", "¥1,500")]
    [InlineData("en-US", """{"@call":"pluralize","args":{"value":1,"one":"one item","other":"many items"}}""", "one item")]
    [InlineData("en-US", """{"@call":"pluralize","args":{"value":0,"zero":"none","one":"one","other":"lots"}}""", "lots")]
    [InlineData("ru-RU", """{"@call":"pluralize","args":{"value":22,"one":"one","few":"few","many":"many","other":"other"}}""", "few")]
    [InlineData("ru-RU", """{"@call":"pluralize","args":{"value":11,"one":"one","few":"few","many":"many","other":"other"}}""", "many")]
    [InlineData("ar-SA", """{"@call":"pluralize","args":{"value":0,"zero":"zero","other":"other"}}""", "zero")]
    public void LocaleFunctions(string culture, string call, string expected)
    {
        test.Host.Culture = CultureInfo.GetCultureInfo(culture);
        var surface = test.Surface();
        surface.EvaluateString(JsonNode.Parse(call)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("yyyy-MM-dd HH:mm:ss", "2026-03-04 15:07:09")]
    [InlineData("E, MMM d, y h:mm a", "Wed, Mar 4, 2026 3:07 PM")]
    [InlineData("EEEE MMMM dd", "Wednesday March 04")]
    [InlineData("'Week' w, 'Q'Q, D", "Week 10, Q1, 63")]
    [InlineData("hh 'o''clock' a", "03 o'clock PM")]
    [InlineData("HH:mm XXX", "15:07 Z")]
    [InlineData("SSS", "250")]
    public void Tr35Dates(string pattern, string expected)
    {
        var date = new DateTimeOffset(2026, 3, 4, 15, 7, 9, 250, TimeSpan.Zero);
        A2UIFormatting.FormatDate(date, pattern, CultureInfo.GetCultureInfo("en-US"), TimeZoneInfo.Utc).ShouldBe(expected);
    }

    [Fact]
    public void FormatDate_AcceptsEpochMilliseconds()
    {
        var surface = test.Surface("""{"t":1772636829000}""");
        test.EvalString(surface, """{"@call":"formatDate","args":{"value":{"@path":"/t"},"format":"yyyy-MM-dd"}}""").ShouldBe("2026-03-04");
    }

    [Fact]
    public void UnknownFunction_ReportsAndResolvesToNull()
    {
        var surface = test.Surface();
        test.Eval(surface, """{"@call":"teleport","args":{}}""").ShouldBeNull();
        test.Errors.ShouldHaveSingleItem()["code"]!.GetValue<string>().ShouldBe(A2UIErrorCodes.ExpressionError);
    }

    [Fact]
    public void ArgumentsAreValidatedAgainstTheCatalog()
    {
        var surface = test.Surface();
        test.Eval(surface, """{"@call":"length","args":{"min":1}}""").ShouldBeNull();           // missing 'value'
        test.Eval(surface, """{"@call":"not","args":{"value":true,"extra":1}}""").ShouldBeNull(); // closed args
        test.Errors.Count().ShouldBe(2);
    }

    // ── checks ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Checks_CoerceBooleansAndFallBackToTheRuleMessage()
    {
        var surface = test.Surface("""{"email":"","terms":false}""", """
            [{"id":"root","component":"Button","child":"l","action":{"event":{"name":"submit"}},
              "@checks":[
                {"condition":{"@call":"required","args":{"value":{"@path":"/email"}}},"message":"Email is required"},
                {"condition":{"@path":"/terms"},"message":"Accept the terms"},
                {"condition":{"@call":"length","args":{"value":"ok","min":1}}}
              ]},
             {"id":"l","component":"Text","text":"Go"}]
            """);

        var result = surface.EvaluateChecks(surface.Root!);
        result.IsValid.ShouldBeFalse();
        result.Failures.Select(f => f.Message).ShouldBe(["Email is required", "Accept the terms"]);

        surface.SetData("/email", JsonValue.Create("a@b.co"));
        surface.SetData("/terms", JsonValue.Create(true));
        surface.EvaluateChecks(surface.Root!).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Accessibility_Resolves()
    {
        var surface = test.Surface("""{"n":"Save"}""", """
            [{"id":"root","component":"Text","text":"x","@accessibility":{"label":{"@path":"/n"},"live":"polite","hidden":"TRUE"}}]
            """);

        var a = surface.GetAccessibility(surface.Root!);
        a.Label.ShouldBe("Save");
        a.Live.ShouldBe("polite");
        a.Hidden.ShouldBeTrue();
    }
}
