# Floor Plan

<!-- TODO: capture screenshots for floorplan (MAUI + Blazor, editor and seating chart) -->

`FloorPlanView` draws a floor plan — rooms, walls, doors, cubicles, outlets, furniture and custom
SVG stencils — on a pan-and-zoom canvas, and lets you edit it. It has two modes, and they are quite
different controls in practice:

- **Edit** — the active tool has the pointer. Draw rooms and walls, drop furniture, drag things
  around, grab a handle to resize, rubber-band a selection.
- **View** — the whole surface is a pan surface, nothing can be moved, and a tap raises
  `ElementTapped`. This is the seating-chart case, and it needs no toolbar at all.

Packages:

- **MAUI** — `Shiny.Maui.Controls.FloorPlan` → `Shiny.Maui.Controls.FloorPlan.FloorPlanView`
- **Blazor** — `Shiny.Blazor.Controls.FloorPlan` → `Shiny.Blazor.Controls.FloorPlan.FloorPlanView`
- **Engine** — `Shiny.Controls.FloorPlan.Shared`, referenced by both and published on its own

## Why the engine is a separate package

The plan is *painted*, not laid out: renderers, hit-testing, a camera, snapping and the tools are all
arithmetic over a document, and none of it wants to know whether the surface under it is an
`SKCanvasView` in a MAUI page or one in a Blazor component. So it does not.
`Shiny.Controls.FloorPlan.Shared` owns all of it, and both controls are a surface and an event pump
around one `FloorPlanEngine`.

That also explains why this is an add-on package rather than part of the core ones: the engine is
SkiaSharp all the way down, and a core dependency on SkiaSharp would hand the native binaries to
every consumer — including every Blazor WebAssembly consumer, who would then need the WebAssembly
native build toolchain to publish anything at all.

## Getting started

### MAUI

```bash
dotnet add package Shiny.Maui.Controls.FloorPlan
```

```csharp
builder.UseShinyFloorPlan();
```

That call registers SkiaSharp, which is all the control needs — but it is not optional. MAUI will not
hand an `SKCanvasView` a platform view without it, and a plan on a page in an app that forgot it is a
blank rectangle with nothing in the log.

```xml
<ContentPage xmlns:fp="http://shiny.net/maui/floorplan">
    <fp:FloorPlanView Document="{Binding Plan}"
                      ActiveTool="{Binding ActiveTool}"
                      ShowGrid="{Binding ShowGrid}"
                      SnapToGrid="True"
                      SelectedElement="{Binding Selected, Mode=TwoWay}" />
</ContentPage>
```

### Blazor

```bash
dotnet add package Shiny.Blazor.Controls.FloorPlan
```

No registration call. The component needs a sized container, though — a drawn surface has no
intrinsic height, and without one the canvas collapses and the control looks like it failed to load.

```razor
<div style="height: 60vh">
    <FloorPlanView Document="plan"
                   ActiveTool="tool"
                   ShowGrid="true"
                   @bind-SelectedElement="selected" />
</div>
```

On Blazor WebAssembly the package fails your build (SHINY0001) if the WebAssembly native build
toolchain is missing, rather than letting you publish an app that throws
`DllNotFoundException: libSkiaSharp` on its first frame.

## The document

```csharp
using Shiny.Controls.FloorPlan;

var plan = new FloorPlanDocument { Width = 1200, Height = 900, GridSize = 20 };

plan.Elements.Add(new RoomElement
{
    Name = "Main Office",
    Label = "Main Office",
    Transform = { X = 100, Y = 100 },
    Width = 500,
    Height = 400
});

plan.Elements.Add(new CubicleElement
{
    Name = "Desk 1",
    Occupant = "Ada L.",
    Transform = { X = 120, Y = 150 }
});
```

Sizes are in **plan units**. The engine never assumes what one is — pixels, centimetres, inches — and
the camera is the only thing that turns them into screen coordinates.

| Element | |
| --- | --- |
| `RoomElement` | A rectangular room with an optional `Label` drawn in the middle |
| `WallElement` | A `Start`/`End` segment of a given `Thickness` |
| `DoorElement` | An opening with a swing arc; `Single`, `Double` or `Sliding` |
| `CubicleElement` | A partitioned bay with a desk in it and an optional `Occupant` |
| `OutletElement` | A power or data outlet — `Standard`, `Floor` or `Data` |
| `FurnitureElement` | Desk, chair, table, bookshelf, sofa or file cabinet |
| `CustomElement` | An instance of one of the document's own `ShapeDefinition` stencils |

Every element carries `Name` (what a tap reports), `Transform`, `Style`, `ZIndex`, `IsVisible`,
`IsLocked` and a `Metadata` dictionary the engine never looks at — hang a desk booking id or a room's
capacity off it and it round-trips through the document with everything else.

`Building` and `Floor` exist for the multi-storey case: a stack of plans saved as one file, so a
floor switcher has something to switch between.

### Locking

`IsLocked` is checked in the editor state rather than in each tool, so a locked element cannot be
selected, dragged, resized or deleted — including by a rubber band dragged across it, which is the
accident locking mostly exists to prevent. A locked element that *is* somehow selected still shows
its outline, but draws no handles.

## Saving and loading

```csharp
var json = FloorPlanSerializer.SerializeDocument(plan);
var back = FloorPlanSerializer.DeserializeDocument(json);
```

Source-generated, so it survives trimming and Native AOT. The `JsonDerivedType` discriminators on
`FloorPlanElement` (`"room"`, `"wall"`, …) are part of the saved format: renaming one breaks every
document already written, so add new element types with new discriminators rather than reusing old
ones.

## Tools

One tool is active at a time and the engine hands it every pointer event.

| Tool | |
| --- | --- |
| `SelectTool` | The default. Pick, drag, resize from eight handles, rubber-band. Shift adds to the selection |
| `PanTool` | Drags the camera |
| `DrawRoomTool` | Drag out a rectangular room |
| `DrawWallTool` | Click for each end. `Chained` (the default) starts the next wall where the last one ended; right-click ends the run |
| `PlaceElementTool` | Drops an element wherever you click, showing a ghost of it under the pointer first |

The middle button pans from any tool, the way every drawing application behaves.

```xml
<!-- Set a tool in XAML... -->
<fp:FloorPlanView.ActiveTool>
    <fp:DrawRoomTool />
</fp:FloorPlanView.ActiveTool>
```

```csharp
// ...or bind it, which is what makes a toolbar a set of commands rather than a pile of code-behind.
this.ActiveTool = new PlaceElementTool(FurnitureKind.Chair);
```

`PlaceElementTool` configures two ways. `Kind` plus its companions (`FurnitureKind`, `OutletType`,
`DoorType`, `ShapeDefinitionId`) covers the built-in elements and is settable from XAML or a Blazor
parameter; `Factory` takes over completely when set, which is how you place an element type of your
own or one carrying pre-filled metadata:

```csharp
new PlaceElementTool(() => new CubicleElement { Metadata = { ["bookable"] = "true" } })
```

Set `Repeat = false` to fall back to `SelectTool` after one drop.

**A tool instance holds the state of a gesture in progress.** Hand the same instance back after the
user has moved on and you resume a drag from several clicks ago — build a new one each time.

### Writing your own

Implement `IFloorPlanTool`. `Render` draws a preview in plan coordinates (the camera transform is
already applied) and `context.DrawElement` will paint a real element for you, which is how the
placement ghost is the actual thing you are about to create rather than a generic marker. Only
`OnPointerReleased` should change the document.

## Snapping

`SnapToGrid` snaps to the document's own `GridSize`. Set `GridSize` to `0` to turn both the grid and
snapping off entirely.

Dragging snaps **on release**, not on every move. Snapping mid-drag makes the element jump ahead of
the pointer, and a nudge smaller than one cell does nothing at all.

## Theming

The plan is painted, so the theme arrives as a value rather than as inherited colour. Leave `Theme`
unset and it follows the app: MAUI reads `Application.Current.Resources`, Blazor reads the theme's
CSS custom properties off the element, and both end at `FloorPlanSurface` so the two hosts cannot
drift into theming this differently. A scheme flip repaints on its own.

Only the neutrals follow the app. The selection blue says *this is what you have hold of* and the
furniture colours say *this is wood, that is upholstery*; restating either in an app's accent is not
theming, it is a different control. Same rule the Office surfaces follow — see
[styling.md](styling.md).

Element colours are part of the **document**, and default to null meaning "whatever the theme says".
That is what lets one saved plan read correctly in a light app and a dark one. Set a colour
explicitly and it is honoured exactly, in both schemes — which is what you want for a plan that
colour-codes its meeting rooms.

```csharp
// Follows the theme.
new RoomElement { Label = "Open plan" }

// Always green, in both schemes.
new RoomElement { Label = "Boardroom", Style = { FillColor = "#E8F5E9", StrokeColor = "#2E7D32" } }
```

For a fixed palette, hand it a `FloorPlanTheme`:

```csharp
plan.Theme = FloorPlanTheme.Dark with { Selection = PlanColor.Rgb(0xFF, 0x6B, 0x00) };
```

## Viewport

`ZoomToFit()`, `ZoomIn()`, `ZoomOut()` and `ScrollTo(element)` are methods on the control rather than
bindable properties: they all need the surface size, and a view model has no business knowing it.

On MAUI, `ZoomToFit()` is safe to call before the view has ever painted — which is the usual case,
since the natural place to call it is the page's constructor. The surface has no size until the first
frame, so the request is held and applied there. On Blazor the same thing happens automatically:
`FitOnLoad` (on by default) fits the plan on the first painted frame.

## Selection

`SelectedElement` is two-way. Reading it tells a view model what the user picked; writing it selects
that element in the plan, so a master list beside the plan drives it both ways. It is null when
nothing — or more than one thing — is selected; `SelectionChanged` / `OnSelectionChanged` hands you
the whole set.

## Custom renderers

`RegisterRenderer` replaces a built-in look or draws an element type of your own:

```csharp
plan.Engine.RegisterRenderer(new MyRoomRenderer());
```

Renderers are keyed on `ElementType` **exactly** — a renderer registered for a base type will not
pick up its subclasses. `GetOutlinePath` is what a pointer is tested against, so return the silhouette
you actually drew: a renderer that draws a circle but returns its bounding box makes the corners
clickable, and the control feels wrong for a reason nobody can point at.

## Platform support

| | |
| --- | --- |
| **MAUI** | iOS, Android, Mac Catalyst, Windows |
| **Blazor** | WebAssembly, Server, Hybrid |

The MAUI `net10.0-macos` (AppKit) head is **not supported yet**. `SkiaSharp.Views.Maui` ships no
macOS asset, so that head resolves a handler whose `CreatePlatformView` throws and the page comes up
blank. `Shiny.Maui.Controls.Office` carries a replacement handler that fixes it app-wide; until that
moves somewhere both packages can share, a plan on AppKit needs Office referenced too. Mac Catalyst
is the supported macOS story.

## Demo

- **MAUI** — `samples/Sample/Features/FloorPlan/` (**Floor Plan → Editor** and **Seating**)
- **Blazor** — `samples/Sample.Blazor/Pages/FloorPlanPage.razor` and `SeatingChartPage.razor`
