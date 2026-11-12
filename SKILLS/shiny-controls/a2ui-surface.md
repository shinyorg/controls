# A2UISurfaceView (A2UI v1.0 renderer)

NuGet: `Shiny.Maui.Controls.Agents` / `Shiny.Blazor.Controls.Agents`. The engine `Shiny.Controls.AgentUI.Shared` comes with them.

- Namespace for the engine types: `Shiny.Controls.AgentUI.A2UI`.
- The control is **`A2UISurfaceView`**. `A2UISurface` is the engine's model of one surface; don't confuse them.

It renders agent-described UI (A2UI v1.0: `createSurface`, `updateComponents`, `updateDataModel`, `deleteSurface`) with native controls. The basic catalog plus the Shiny catalog cover 30 components.

## Rules for generated code
- **Build a host, apply messages, and send `Outbound` back to the agent:**
  ```csharp
  var host = new A2UISurfaceHost(new A2UICatalogRegistry().Add(A2UICatalog.CreateShiny()));
  host.Outbound += (_, m) => agent.Send(m.Message);   // action / error / callAgentFunction / rendererFunctionResponse
  host.Apply(messageJson);                             // string, JsonNode, or a JsonArray of messages
  ```
- Inside **AgentView** you write none of this. AG-UI `ACTIVITY_SNAPSHOT`s of type `a2ui-surface` are rendered automatically through `A2UIAgentBinding`.
- **Catalog ids:**
  - `A2UICatalog.BasicCatalogId` (18 components).
  - `A2UICatalog.ShinyCatalogId` = `https://shinylib.net/a2ui/shiny.controls/v1.json`: basic plus DataGrid, Gauge, ProgressBar, Pill, Avatar, ChipGroup, TagEntry, DateRangePicker, RangeSlider, Timeline, Expander and Markdown.
  - Give the agent `A2UICatalog.CreateShiny().Definition` (the full schema) or the published URL.
- **Standalone it fills its parent and scrolls** (`IsScrollEnabled=true`). Inside a page that already scrolls, set `IsScrollEnabled=false` so it sizes to its content. Never nest it in a ScrollView while it scrolls itself.
- **Custom components.**
  - MAUI: `renderers.Register("Name", ctx => view)`.
  - Blazor: `renderers.Register<MyComponent>("Name")` (the component has an `A2UIRenderContext Context` parameter) or `Register("Name", fragment)`.
  - Then add an `A2UIComponentDefinition` to a catalog. An unregistered type renders a placeholder and reports `VALIDATION_FAILED`.
- **Inside a renderer, use `A2UIRenderContext`:**
  - Read properties: `String(prop)`, `Number(prop)`, `Boolean(prop)`, `StringList(prop)`, `Value(prop)`.
  - Two-way bind: `Write(prop, value)`.
  - Buttons: `Dispatch("action")`.
  - Also `Checks()` and the children helpers.
  - Never evaluate `{"@path"}` / `{"@call"}` yourself.

## Agent-side JSON (Shiny catalog example)
```json
[{"version":"1.0","createSurface":{"surfaceId":"dash","catalogId":"https://shinylib.net/a2ui/shiny.controls/v1.json",
  "dataModel":{"orders":[{"id":"A-1","total":12.5}],"range":{"start":"2026-10-01","end":"2026-10-31"}},
  "components":[
    {"id":"root","component":"Column","children":["range","grid"]},
    {"id":"range","component":"DateRangePicker","start":{"@path":"/range/start"},"end":{"@path":"/range/end"}},
    {"id":"grid","component":"DataGrid","rows":{"@path":"/orders"},
     "columns":[{"key":"id","header":"Order"},{"key":"total","header":"Total","format":"currency","currency":"EUR"}],
     "rowAction":{"event":{"name":"openOrder","context":{"orderId":{"@path":"id"}}}}}]}}]
```
- Checks go in `"@checks"`, not `"checks"`.
- Relative paths (`{"@path":"id"}`) inside `rowAction` and List templates read the current row.

## MAUI
```xml
<shiny:A2UISurfaceView x:Name="Surface" />
```
```csharp
Surface.Host = host; Surface.SurfaceId = "dash";
```

## Blazor
```razor
@using Shiny.Blazor.Controls.Agents
<A2UISurfaceView Host="@host" SurfaceId="dash" />
```
