using System.Text.Json.Nodes;
using Shiny.Controls.AgentUI.A2UI;

namespace Shiny.Controls.AgentUI.Tests.A2UI;

public class A2UIHostTests
{
    readonly A2UITestHost test = new();

    // ── envelope & integrity ─────────────────────────────────────────────────────

    [Fact]
    public void CreatingAnExistingSurfaceIsAnIntegrityError_UntilItIsDeleted()
    {
        test.Surface();
        test.Surface();
        test.ErrorCodes.ShouldBe([A2UIErrorCodes.IntegrityError]);

        test.Apply("""{"version":"1.0","deleteSurface":{"surfaceId":"s1"}}""");
        test.Host.Surfaces.ShouldBeEmpty();
        test.Surface();
        test.Host.Surfaces.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("""{"version":"0.9","createSurface":{"surfaceId":"x"}}""")]
    [InlineData("""{"version":"1.0"}""")]
    [InlineData("""{"version":"1.0","createSurface":{"surfaceId":"x"},"deleteSurface":{"surfaceId":"x"}}""")]
    [InlineData("""{"version":"1.0","createSurface":{"surfaceId":"x","theme":{"primaryColor":"#f00"}}}""")]
    [InlineData("""{"version":"1.0","updateComponents":{"surfaceId":"nope","components":[]}}""")]
    [InlineData("""not json""")]
    public void BadMessagesAreReported(string json)
    {
        test.Apply(json);
        test.Errors.ShouldNotBeEmpty();
        test.Host.Surfaces.ShouldBeEmpty();
    }

    [Fact]
    public void Components_BufferUntilRoot_AndUnknownTypesAreReported()
    {
        var surface = test.Surface(components: """[{"id":"t","component":"Text","text":"waiting"}]""");
        surface.Root.ShouldBeNull();

        test.Apply("""{"version":"1.0","updateComponents":{"surfaceId":"s1","components":[{"id":"root","component":"Column","children":["t","ghost"]},{"id":"x","component":"Hologram"}]}}""");

        surface.Root.ShouldNotBeNull();
        var children = surface.GetChildren(surface.Root!, "children");
        children[0].Component.ShouldNotBeNull();
        children[1].Component.ShouldBeNull();      // missing reference: renderer draws a placeholder
        test.Errors.ShouldHaveSingleItem()["message"]!.GetValue<string>().ShouldContain("Hologram");
    }

    [Fact]
    public void MissingRequiredPropertyIsReported()
    {
        test.Surface(components: """[{"id":"root","component":"Text"}]""");
        test.Errors.ShouldHaveSingleItem()["code"]!.GetValue<string>().ShouldBe(A2UIErrorCodes.ValidationFailed);
    }

    [Fact]
    public void SurfaceContainerCantBeModified()
    {
        test.Surface(components: """[{"id":"root","component":"Surface"}]""");
        test.ErrorCodes.ShouldContain(A2UIErrorCodes.ValidationFailed);
    }

    [Fact]
    public void Composition_AllowedParentsAndChildren()
    {
        var catalog = new A2UICatalog("test:catalog")
            .AddComponent(new A2UIComponentDefinition("Menu", ["items"], [], [new A2UIChildProperty("items", A2UIChildPropertyKind.List)], null, ["MenuItem"], false))
            .AddComponent(new A2UIComponentDefinition("MenuItem", ["label"], [], [], ["Menu"], null, false))
            .AddComponent(new A2UIComponentDefinition("Note", [], [], [], null, null, false));
        var t = new A2UITestHost(new A2UICatalogRegistry().Add(catalog));

        t.Apply("""
            {"version":"1.0","createSurface":{"surfaceId":"m","catalogId":"test:catalog","components":[
              {"id":"root","component":"MenuItem"},
              {"id":"menu","component":"Menu","items":["n"]},
              {"id":"n","component":"Note"}]}}
            """);
        t.ErrorCodes.ShouldBe([A2UIErrorCodes.UnallowedParent]);   // MenuItem directly under Surface

        t.Apply("""{"version":"1.0","updateComponents":{"surfaceId":"m","components":[{"id":"root","component":"Menu","items":["n"]}]}}""");
        t.ErrorCodes.ShouldContain(A2UIErrorCodes.UnallowedChild);   // Note inside Menu
    }

    [Fact]
    public void Capabilities_AndDataModelMetadata()
    {
        test.Apply("""{"version":"1.0","createSurface":{"surfaceId":"a","catalogId":"BASIC","sendDataModel":true,"dataModel":{"x":1}}}""".Replace("BASIC", A2UICatalog.BasicCatalogId));
        test.Apply("""{"version":"1.0","createSurface":{"surfaceId":"b","catalogId":"BASIC","dataModel":{"y":2}}}""".Replace("BASIC", A2UICatalog.BasicCatalogId));

        test.Host.GetRendererCapabilities()["1.0"]!["supportedCatalogIds"]![0]!.GetValue<string>().ShouldBe(A2UICatalog.BasicCatalogId);
        test.Host.GetRendererDataModel()!.ToJsonString().ShouldBe("""{"version":"1.0","surfaces":{"a":{"x":1}}}""");
    }

    // ── actions ──────────────────────────────────────────────────────────────────

    [Fact]
    public void EventAction_ResolvesContextAtTapTime()
    {
        var surface = test.Surface("""{"form":{"email":"jane@example.com"}}""", """
            [{"id":"root","component":"Button","child":"l","action":{"event":{"name":"submit_form",
               "userMessage":{"@call":"formatString","args":{"value":"Sent to ${/form/email}"}},
               "context":{"email":{"@path":"/form/email"},"fixed":7}}}},
             {"id":"l","component":"Text","text":"Send"}]
            """);

        surface.DispatchAction(surface.Root!).ShouldBeTrue();

        var action = test.Outbound.Single(m => m.Kind == A2UIOutboundKind.Action);
        action.Message["version"]!.GetValue<string>().ShouldBe("1.0");
        var body = action.Body;
        body["name"]!.GetValue<string>().ShouldBe("submit_form");
        body["surfaceId"]!.GetValue<string>().ShouldBe("s1");
        body["sourceComponentId"]!.GetValue<string>().ShouldBe("root");
        body["timestamp"]!.GetValue<string>().ShouldStartWith("2026-11-12T09:30:00");
        body["userMessage"]!.GetValue<string>().ShouldBe("Sent to jane@example.com");
        body["context"]!.ToJsonString().ShouldBe("""{"email":"jane@example.com","fixed":7}""");
    }

    [Fact]
    public void OpenUrl_OnlyFromAGesture_AndOnlyHttp()
    {
        var opened = new List<Uri>();
        test.Host.OpenUrl = opened.Add;
        test.Host.BaseUri = new Uri("https://shinylib.net/docs/");
        var surface = test.Surface(components: """
            [{"id":"root","component":"Button","child":"l","action":{"functionCall":{"@call":"openUrl","args":{"url":"controls/"}}}},
             {"id":"bad","component":"Button","child":"l","action":{"functionCall":{"@call":"openUrl","args":{"url":"javascript:alert(1)"}}}},
             {"id":"l","component":"Text","text":"Open"}]
            """);

        // passive evaluation (a binding) is blocked
        test.Eval(surface, """{"@call":"openUrl","args":{"url":"https://example.com"}}""").ShouldBeNull();
        opened.ShouldBeEmpty();

        surface.DispatchAction(surface.Root!).ShouldBeTrue();
        opened.ShouldHaveSingleItem().ToString().ShouldBe("https://shinylib.net/docs/controls/");

        surface.DispatchAction(surface.GetComponent("bad")!).ShouldBeFalse();
        test.ErrorCodes.ShouldContain(A2UIErrorCodes.ExecutionError);
        opened.Count.ShouldBe(1);
    }

    // ── functions across the wire ────────────────────────────────────────────────

    [Fact]
    public void CallRendererFunction_RespectsAllowedCallers()
    {
        var catalog = A2UICatalog.CreateBasic();
        catalog.AddFunction(new A2UIFunctionDefinition("screenSize", "object", A2UIAllowedCallers.AgentOnly, false, [], [], true), _ => new JsonObject { ["w"] = 390 });
        var t = new A2UITestHost(new A2UICatalogRegistry(includeBasic: false).Add(catalog));

        t.Apply("""{"version":"1.0","callRendererFunction":{"functionCallId":"f1","callFunction":{"@call":"screenSize","catalogId":"BASIC"}}}""".Replace("BASIC", A2UICatalog.BasicCatalogId));
        t.Apply("""{"version":"1.0","callRendererFunction":{"functionCallId":"f2","callFunction":{"@call":"email","catalogId":"BASIC","args":{"value":"x"}}}}""".Replace("BASIC", A2UICatalog.BasicCatalogId));
        t.Apply("""{"version":"1.0","callRendererFunction":{"functionCallId":"f3","callFunction":{"@call":"screenSize"}}}""");

        var responses = t.Outbound.Where(m => m.Kind == A2UIOutboundKind.RendererFunctionResponse).Select(m => m.Body).ToList();
        responses[0]["value"]!["w"]!.GetValue<int>().ShouldBe(390);
        responses[1]["error"]!["code"]!.GetValue<string>().ShouldBe(A2UIErrorCodes.InvalidFunctionCall);   // rendererOnly
        responses[2]["error"]!["code"]!.GetValue<string>().ShouldBe(A2UIErrorCodes.InvalidFunctionCall);   // no catalogId

        // and an agentOnly function never runs from a binding
        var surface = t.Surface();
        t.Eval(surface, """{"@call":"screenSize"}""").ShouldBeNull();
    }

    [Fact]
    public void AgentFunction_IsCalledOnceAndResolvesWhenAnswered()
    {
        var catalog = A2UICatalog.CreateBasic();
        catalog.AddFunction(new A2UIFunctionDefinition("lookupPrice", "string", A2UIAllowedCallers.RendererOnly, false, ["sku"], ["sku"], false));
        var t = new A2UITestHost(new A2UICatalogRegistry(includeBasic: false).Add(catalog));
        var surface = t.Surface();
        var changes = 0;
        surface.Changed += (_, c) => changes += c.Kind == A2UISurfaceChangeKind.FunctionResults ? 1 : 0;
        var call = """{"@call":"lookupPrice","args":{"sku":"A1"}}""";

        t.Eval(surface, call).ShouldBeNull();       // pending
        t.Eval(surface, call).ShouldBeNull();       // still pending: no second request
        var request = t.Outbound.Where(m => m.Kind == A2UIOutboundKind.CallAgentFunction).ShouldHaveSingleItem().Body;
        request["callFunction"]!["args"]!["sku"]!.GetValue<string>().ShouldBe("A1");

        var id = request["functionCallId"]!.GetValue<string>();
        t.Apply("""{"version":"1.0","agentFunctionResponse":{"functionCallId":"CALL_ID","value":"$9.99"}}""".Replace("CALL_ID", id));

        changes.ShouldBe(1);
        t.EvalString(surface, call).ShouldBe("$9.99");
    }

    // ── URL policy ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://cdn.example.com/a.png", true)]
    [InlineData("  http://example.com/a.png  ", true)]
    [InlineData("data:image/png;base64,iVBOR", true)]
    [InlineData("/images/a.png", true)]
    [InlineData("images/a.png", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("content://media/1", false)]
    [InlineData("blob:https://x/1", false)]
    [InlineData("https:host/a.png", false)]
    [InlineData("//evil.com/a.png", false)]
    [InlineData("/\\evil.com/a.png", false)]
    [InlineData("ht\ttps://host/a.png", true)]
    [InlineData("/\t/evil.com", false)]
    [InlineData("", false)]
    public void MediaUrls(string url, bool allowed) =>
        A2UIUrlPolicy.IsAllowedMediaUrl(url, out _).ShouldBe(allowed);
}
