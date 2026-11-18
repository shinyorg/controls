# A2UISurfaceView

[← All Shiny Controls](../../README.md)

> Separate NuGet packages: `Shiny.Maui.Controls.Agents` / `Shiny.Blazor.Controls.Agents` (engine: `Shiny.Controls.AgentUI.Shared`)

Renders agent-described UI, [A2UI v1.0](https://a2ui.org), with real native controls. An agent sends
declarative JSON: a flat list of components, a data model, bindings and actions. A2UISurfaceView draws it,
writes what the user types back into the data model, and sends button presses back to the agent. It is
**declarative JSON only, never code**: the agent can only place components that the renderer registered.

[AgentView](agent-view.md) hosts it for surfaces that arrive in a conversation. It also works on its own, with any
transport.

```csharp
var host = new A2UISurfaceHost(new A2UICatalogRegistry().Add(A2UICatalog.CreateShiny()));
host.Outbound += (_, m) => SendToAgent(m.Message);   // actions, errors, function calls
host.Apply(json);                                     // createSurface / updateComponents / updateDataModel / ...
```

```xml
<shiny:A2UISurfaceView x:Name="Surface" />   <!-- Surface.Host = host; Surface.SurfaceId = "dashboard"; -->
```

```razor
<A2UISurfaceView Host="@host" SurfaceId="dashboard" />
```

## Properties

| Property | Type | Description |
|---|---|---|
| `Surface` | `A2UISurface` | The surface to draw |
| `Host` + `SurfaceId` | `A2UISurfaceHost`, `string?` | An alternative to `Surface`. A null `SurfaceId` draws every surface on the host in creation order |
| `Renderers` | `A2UIRendererRegistry` | Component type → renderer. `A2UIRendererRegistry.CreateDefault()` covers the basic and Shiny catalogs |
| `IsScrollEnabled` | `bool` | `true` (default): the view owns vertical scroll. `false`: it sizes to its content (AgentView sets this) |
| `ShowBorder` | `bool` | Whether a root Card draws its edge (AgentView turns it off inside a message) |

## Catalogs

| Catalog | Id | Components |
|---|---|---|
| Basic | `https://a2ui.org/specification/v1_0/catalogs/basic/catalog.json` | Text, Image, Icon, Video, AudioPlayer, Row, Column, List, Card, Tabs, Divider, Modal, Button, TextField, CheckBox, ChoicePicker, Slider, DateTimeInput |
| **Shiny** | `https://shinylib.net/a2ui/shiny.controls/v1.json` | Everything in basic, plus **DataGrid**, **Gauge**, **ProgressBar**, **Pill**, **Avatar**, **ChipGroup**, **TagEntry**, **DateRangePicker**, **RangeSlider**, **Timeline**, **Expander**, **Markdown** |

The Shiny catalog's schema (give it to the agent) is `A2UICatalog.CreateShiny().Definition`, also published at
[shinylib.net/a2ui/shiny.controls/v1.json](https://shinylib.net/a2ui/shiny.controls/v1.json). The functions
are those of the basic catalog: `required`, `regex`, `length`, `numeric`, `email`, `formatString`, `formatNumber`,
`formatCurrency`, `formatDate`, `pluralize`, `openUrl`, `and`, `or`, `not`.

The Shiny components render with the real controls. DataGrid formats `number` / `currency` / `percent` / `date`
columns in the user's culture and sends `rowAction` with the tapped row as its scope (relative paths in the
action's context read that row). Its columns keep the width their text needs and the grid scrolls sideways, so
on a phone it never squeezes every column into "…"; on MAUI a grid with more than 12 rows shows those and a
"Show all" link that opens the rest in a modal, so it never scrolls vertically inside the conversation. DateRangePicker, ChipGroup, TagEntry, RangeSlider and Expander write back to
their bindings.

## Your own components

Any MAUI view or Blazor component can be an A2UI component. Declare it as a C# class, register a renderer
on each host, and hand both to the view. Nothing in your code is JSON or a property name in a string.

### 1. Declare the component as a class

```csharp
public enum Tone { Neutral, Good, Danger }

/// <summary>A row of stars the user can tap to score something.</summary>
[A2UIComponent("Rating")]
public partial class Rating
{
    /// <summary>The score. Bind it to receive the user's choice.</summary>
    public required double Value { get; init; }

    [A2UIProperty(Literal = true, Minimum = 1, Description = "How many stars.")]
    public int Max { get; init; } = 5;

    public Tone Tone { get; init; } = Tone.Neutral;
    public string? Label { get; init; }
    public A2UIActionRef Action { get; init; }
}
```

The source generator (it ships inside the Agents packages, so there's nothing to install) turns the class into
the component's catalog schema and a typed reader:

| C# | A2UI |
|---|---|
| `string`, `double`, `float`, `bool`, `IReadOnlyList<string>` / `string[]` | Dynamic: a literal, a `@path` binding or a `@call` function call |
| `int`, `long`, an enum | Literal; an enum becomes a choice of its camel-cased names |
| A list of an `[A2UIItem]` class | An array of objects |
| `A2UIActionRef`, `A2UIChildRef`, `A2UIChildrenRef`, `A2UIBindingRef` | An action, one child, children, a write-back path |
| `required` | Required |
| `= 5` | The fallback when the agent leaves it out, and (on a literal) the schema default |
| XML doc summary, `[Description]`, or `[A2UIProperty(Description = …)]` | The description the agent reads |

JSON names are the property names camel-cased; `[A2UIProperty(Name = "…")]` overrides one. `[A2UIIgnore]`
leaves a property out. `[A2UIComponent]` also takes `Checkable`, `AllowedParents`, `AllowedChildren` and
`Weight`. Mistakes are compile errors with a fix in the message, `SHINYA2UI001` to `SHINYA2UI009`: a class
that isn't partial, a type A2UI can't describe, a `Minimum` on a property that could be bound, and so on.

### 2. Functions are plain static methods

```csharp
public static partial class MyFunctions
{
    /// <summary>The initials of a name.</summary>
    /// <param name="name">The full name.</param>
    [A2UIFunction("initials")]
    public static string Initials(string name, bool lower = false) => ...;
}
```

Parameters are `string`, numbers, `bool`, enums or string lists. One with a default value or a nullable type is
optional. An `A2UIFunctionContext` parameter gets the call's culture and surface and isn't part of the schema.
The return type is `string`, a number, `bool` or `void`. `AllowedCallers` and `RequiresUserActivation` go on
the attribute.

### 3. Build the catalog

```csharp
var catalog = A2UICatalogBuilder
    .ExtendShiny("https://example.com/a2ui/my-app/v1.json")    // every basic + Shiny component, under your id
    .Instructions("Use Rating whenever you ask the user to score something.")
    .Component<Rating>()
    .Functions(MyFunctions.A2UIFunctions)
    .Build();

var catalogs = new A2UICatalogRegistry().Add(catalog);
// Give the agent catalog.Definition, and have it create surfaces with your catalog id.
```

`ExtendShiny` and `ExtendBasic` keep Row, Column, Text and every built-in function under your id. A surface
names one catalog, so a catalog holding only your components would leave the agent without them. `Create(id)`
starts empty.

### 4. Render it

```csharp
// MAUI
var renderers = A2UIRendererRegistry.CreateDefault()          // keep the basic and Shiny renderers
    .Register<Rating>(ctx =>
    {
        var view = new RatingView();
        ctx.Watch(s =>
        {
            var rating = ctx.Read<Rating>(s);                  // typed, bindings evaluated
            view.Max = rating.Max;
            view.Value = rating.Value;
        });
        view.ValueChanged += (_, v) =>
        {
            ctx.Read<Rating>().SetValue(v);                   // writes the bound path
            ctx.Dispatch(Rating.A2UIProperties.Action);
        };
        return view;
    });

// Blazor
var renderers = A2UIRendererRegistry.CreateDefault().Register<Rating, MyRating>();
// MyRating.razor: [Parameter] A2UIRenderContext Context; var rating = Context.Read<Rating>(); rating.SetValue(v);
```

- `Read<T>()` evaluates every property: bindings, function calls, items and child references. Call it inside
  `Watch` with the scope so it re-reads when the data model changes.
- **`SetX(value)`** exists for each property the agent can bind (string, number, boolean, string list, and
  `A2UIBindingRef` with an overload per type). It writes through the render context, so the input counts as
  edited and its check messages show. It returns `false` when the agent gave a literal. Literal-only properties
  have no setter, so writing one is a compile error.
- `Rating.A2UIProperties.X` holds each JSON name, for `Dispatch` and `RenderChild`.

Hand both registries to the view: `AgentView` takes `Catalogs` and `Renderers`; a standalone surface takes
`new A2UISurfaceHost(catalogs)` and `A2UISurfaceView.Renderers`. A component in no catalog is rejected with
`VALIDATION_FAILED`; one with no renderer draws an "unsupported component" placeholder.

### Without the generator

`A2UICatalogBuilder` can also declare components and functions directly, which is what the generated code
calls: `.Component("Rating", "…", c => c.Number("value", required: true).Action())`. It has the same property
vocabulary (`String`, `Number`, `Integer`, `Boolean`, `StringList`, `Value`, `Choice<TEnum>`, `Binding`,
`Array`, `Child`, `Children`, `Action`, `Checkable()`, `AllowedParents`, `AllowedChildren`, `NoWeight()`) and
throws on the same mistakes the generator reports at compile time.

## Rules it enforces (A2UI conformance)

- Nothing renders until `root` exists. Missing children show a placeholder, and unknown components show an
  "unsupported component" placeholder and report `VALIDATION_FAILED`. There is never a guess.
- `allowedParents` / `allowedChildren`, required properties, `allowedCallers`, and `requiresUserActivation`
  (`openUrl` only runs from a tap) are all enforced.
- `regex` uses the spec's dialect, with all four of its safety rules checked before anything runs.
- Media URLs must pass the spec's URL policy.
- Inputs write to the local data model immediately. The model goes to the agent only with an action, and only
  when the surface set `sendDataModel`.
- `@accessibility` maps to `SemanticProperties` on MAUI and to ARIA on Blazor.
- A Button whose checks fail is disabled. Check messages show under an input once the user has edited it.

## Layout

- Standalone (`IsScrollEnabled=true`) the view fills its parent and owns vertical scroll. Don't wrap it in a
  `ScrollView` or an `overflow:auto` parent.
- Inside AgentView it sizes to its content. A2UI `List` renders without virtualization; Row/Column `weight`
  maps to star sizing on MAUI and to `flex-grow` on Blazor.

Screenshots (MAUI on Android, Blazor) are on the [docs site](https://shinylib.net/controls/a2ui/).
