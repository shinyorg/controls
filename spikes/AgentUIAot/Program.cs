using System.Text.Json.Nodes;
using AGUI.Formatting;
using Shiny.Controls.AgentUI;
using Shiny.Controls.AgentUI.A2UI;

var root = args.Length > 0 ? args[0] : "../../tests/Shiny.Controls.AgentUI.Tests/Fixtures";

// AG-UI: decode a recorded stream with the SDK and fold it
var thread = new AgentThread();
await using (var s = File.OpenRead(Path.Combine(root, "agui", "a2ui.sse")))
    await foreach (var e in new SseEventStreamFormatter().ReadAsync(s, default))
        thread.Apply(e);
Console.WriteLine($"thread: {thread.Items.Count} items, status {thread.RunStatus}, messages out {thread.ToAGUIMessages().Count}");

// A2UI: every example, with errors counted
var host = new A2UISurfaceHost();
var errors = 0;
host.Outbound += (_, m) => { if (m.Kind == A2UIOutboundKind.Error) { errors++; Console.WriteLine(m.Message.ToJsonString()); } };
foreach (var file in Directory.GetFiles(Path.Combine(root, "a2ui", "examples"), "*.json"))
    host.Apply(JsonNode.Parse(File.ReadAllText(file))!["messages"]);
Console.WriteLine($"a2ui: {host.Surfaces.Count} surfaces, {errors} errors");

var surface = host.Surfaces.Values.First();
Console.WriteLine("regex: " + surface.EvaluateBoolean(JsonNode.Parse("""{"@call":"regex","args":{"value":"a@b.com","pattern":"^[a-z]+@[a-z]+\\.com$"}}""")));
Console.WriteLine("currency: " + surface.EvaluateString(JsonNode.Parse("""{"@call":"formatCurrency","args":{"value":19.98,"currency":"EUR"}}""")));
Console.WriteLine("date: " + surface.EvaluateString(JsonNode.Parse("""{"@call":"formatDate","args":{"value":"2026-11-12T09:30:00Z","format":"EEEE d MMMM y"}}""")));

// Shiny catalog: the embedded schema merged with the basic catalog, applied with the dashboard fixture
var shinyHost = new A2UISurfaceHost(new A2UICatalogRegistry().Add(A2UICatalog.CreateShiny()));
var shinyErrors = 0;
shinyHost.Outbound += (_, m) => { if (m.Kind == A2UIOutboundKind.Error) shinyErrors++; };
shinyHost.Apply(JsonNode.Parse(File.ReadAllText(Path.Combine(root, "a2ui", "shiny", "dashboard.json")))!["messages"]);
Console.WriteLine($"shiny: {A2UICatalog.CreateShiny().Components.Count} components, dashboard {shinyHost.Surfaces.Count} surface, {shinyErrors} errors");

// UI tools: a prompt answered through the queue
var prompts = new AgentPromptQueue();
var ask = prompts.AskAsync("c1", AgentPromptKinds.DateRange, new JsonObject { ["title"] = "When?" });
prompts.Pending[0].Submit(new JsonObject { ["start"] = "2026-12-01", ["end"] = "2026-12-08" });
Console.WriteLine("prompt: " + (await ask).Content);
