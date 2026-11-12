using System.Globalization;
using System.Text.Json.Nodes;
using Shiny.Controls.AgentUI.A2UI;

namespace Shiny.Controls.AgentUI.Tests.A2UI;

sealed class A2UITestHost
{
    public A2UITestHost(A2UICatalogRegistry? catalogs = null)
    {
        Host = new A2UISurfaceHost(catalogs)
        {
            Culture = CultureInfo.GetCultureInfo("en-US"),
            TimeZone = TimeZoneInfo.Utc,
            Time = new FixedTime(new DateTimeOffset(2026, 11, 12, 9, 30, 0, TimeSpan.Zero))
        };
        Host.Outbound += (_, m) => Outbound.Add(m);
    }

    public A2UISurfaceHost Host { get; }

    public List<A2UIOutboundMessage> Outbound { get; } = [];

    public IEnumerable<JsonObject> Errors => Outbound.Where(m => m.Kind == A2UIOutboundKind.Error).Select(m => m.Body);

    public IEnumerable<string> ErrorCodes => Errors.Select(e => e["code"]!.GetValue<string>());

    public void Apply(string json) => Host.Apply(json);

    /// <summary>A surface on the basic catalog with the given data model and components.</summary>
    public A2UISurface Surface(string dataModel = "{}", string components = """[{"id":"root","component":"Text","text":"hi"}]""", string id = "s1")
    {
        Host.Apply(new JsonObject
        {
            ["version"] = "1.0",
            ["createSurface"] = new JsonObject
            {
                ["surfaceId"] = id,
                ["catalogId"] = A2UICatalog.BasicCatalogId,
                ["dataModel"] = JsonNode.Parse(dataModel),
                ["components"] = JsonNode.Parse(components)
            }
        });
        return Host.GetSurface(id)!;
    }

    public JsonNode? Eval(A2UISurface surface, string expression, A2UIScope? scope = null) =>
        surface.Evaluate(JsonNode.Parse(expression), scope);

    public string EvalString(A2UISurface surface, string expression, A2UIScope? scope = null) =>
        surface.EvaluateString(JsonNode.Parse(expression), scope);

    /// <summary>Resolves every property, child, template and check reachable from the root, as a renderer would.</summary>
    public static int Walk(A2UISurface surface)
    {
        var count = 0;
        if (surface.Root is { } root)
            Visit(root, surface.RootScope, 0);
        return count;

        void Visit(A2UIComponent component, A2UIScope scope, int depth)
        {
            if (depth > 50)
                throw new InvalidOperationException("Cycle");
            count++;

            var def = surface.GetDefinition(component);
            foreach (var (name, value) in component.Properties)
            {
                if (name is "id" or "component" or "@checks" || def?.ChildProperties.Any(c => c.Name == name) == true)
                    continue;
                surface.Evaluate(value, scope);
            }
            surface.EvaluateChecks(component, scope);
            surface.GetAccessibility(component, scope);

            foreach (var prop in def?.ChildProperties ?? [])
            {
                switch (prop.Kind)
                {
                    case A2UIChildPropertyKind.Single:
                        if (surface.GetChild(component, prop.Name, scope) is { Component: { } child } c)
                            Visit(child, c.Scope, depth + 1);
                        break;
                    case A2UIChildPropertyKind.List:
                        foreach (var item in surface.GetChildren(component, prop.Name, scope))
                            if (item.Component is { } listChild)
                                Visit(listChild, item.Scope, depth + 1);
                        break;
                    case A2UIChildPropertyKind.ItemChild:
                        foreach (var tab in (component[prop.Name] as JsonArray ?? []).OfType<JsonObject>())
                        {
                            surface.Evaluate(tab["title"], scope);
                            if (surface.GetComponent(tab[prop.ItemProperty!]!.GetValue<string>()) is { } tabChild)
                                Visit(tabChild, scope, depth + 1);
                        }
                        break;
                }
            }
        }
    }

    sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
