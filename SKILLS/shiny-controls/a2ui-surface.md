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
- **Custom components: declare a class, never JSON.** Never write catalog JSON, `JsonNode.Parse`, `JsonValue.Create`, or a hand-built `A2UIComponentDefinition` for a custom component.
  ```csharp
  /// <summary>A row of stars the user can tap.</summary>        // the agent reads doc summaries
  [A2UIComponent("Rating")]
  public partial class Rating                                   // must be partial, top-level, no primary ctor
  {
      public required double Value { get; init; }              // dynamic; `required` → required
      [A2UIProperty(Literal = true, Minimum = 1)]
      public int Max { get; init; } = 5;                        // literal; initializer = default + fallback
      public Tone Tone { get; init; }                           // enum → choice
      public A2UIActionRef Action { get; init; }                // also A2UIChildRef, A2UIChildrenRef, A2UIBindingRef
  }

  public static partial class MyFunctions
  {
      [A2UIFunction("initials")]                                // typed params/return; defaults/nullable = optional
      public static string Initials(string name, bool lower = false) => ...;
  }

  var catalog = A2UICatalogBuilder.ExtendShiny("https://example.com/a2ui/my-app/v1.json")
      .Component<Rating>()
      .Functions(MyFunctions.A2UIFunctions)
      .Build();                                                 // catalog.Definition = schema for the agent
  ```
  - Types: `string`/`double`/`float`/`bool`/string lists are dynamic; `int`/`long`/enums are literal; a list of an `[A2UIItem]` class is an array. Unsupported types are compile errors (`SHINYA2UI005`).
  - Use `ExtendShiny`/`ExtendBasic`, not `Create`, unless the catalog should hold only your components.
- **Renderers for a generated component:**
  - MAUI: `renderers.Register<Rating>(ctx => { ... })`. Blazor: `renderers.Register<Rating, MyRatingComponent>()` or `Register<Rating>(fragment)`.
  - Read: `var rating = ctx.Read<Rating>(scope)` inside `ctx.Watch(s => ...)`.
  - Write back: `rating.SetValue(v)` (typed, one per bindable property; returns false for a literal). Never `ctx.Write(..., JsonValue.Create(...))` for a generated component.
  - Names: `Rating.A2UIProperties.Action` for `ctx.Dispatch(...)` / `ctx.RenderChild(...)`.
- **Without the generator:** `A2UICatalogBuilder.Component("Name", "desc", c => c.Number(...).Action())` (same vocabulary) and `renderers.Register("Name", ctx => view)`.
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
