# Floor Plan

Rooms, walls, doors, cubicles, outlets, furniture and custom SVG stencils on a pan/zoom SkiaSharp
surface, for **both MAUI and Blazor**.

- **MAUI** — `Shiny.Maui.Controls.FloorPlan.FloorPlanView` (package `Shiny.Maui.Controls.FloorPlan`)
- **Blazor** — `Shiny.Blazor.Controls.FloorPlan.FloorPlanView` (package `Shiny.Blazor.Controls.FloorPlan`)
- **Engine** — `Shiny.Controls.FloorPlan.Shared`, namespace `Shiny.Controls.FloorPlan`

## Things that go wrong if you do not know them

1. **These are add-on packages, never core.** The engine is SkiaSharp; the core packages carry none.
2. **MAUI needs `builder.UseShinyFloorPlan()`.** Without it MAUI hands the `SKCanvasView` no platform
   view and the page is a blank rectangle with nothing in the log.
3. **The XAML namespace is `http://shiny.net/maui/floorplan`**, not the core `shiny:` one.
4. **Blazor needs a sized container.** A drawn surface has no intrinsic height; without one the canvas
   collapses and the control looks like it failed to load.
5. **Sizes are plan units, not pixels.** The camera is the only thing that converts.
6. **A tool instance holds the state of a gesture in progress.** Build a new one each time.
7. **`ZoomToFit`/`ZoomIn`/`ZoomOut`/`ScrollTo` are methods, not bindable properties** — they need the
   surface size, which a view model does not have.

## MAUI

```csharp
// MauiProgram.cs
builder.UseShinyFloorPlan();
```

```xml
<ContentPage xmlns:fp="http://shiny.net/maui/floorplan">
    <fp:FloorPlanView x:Name="Plan"
                      Document="{Binding Plan}"
                      ActiveTool="{Binding ActiveTool}"
                      ShowGrid="{Binding ShowGrid}"
                      SnapToGrid="True"
                      SelectedElement="{Binding Selected, Mode=TwoWay}"
                      ElementTapped="OnElementTapped" />
</ContentPage>
```

```csharp
// Safe in the constructor: the surface has no size yet, so the request is held for the first frame.
this.Plan.ZoomToFit();
```

Events: `ElementTapped` (View mode only), `SelectionChanged`, `ElementAdded`, `ElementsRemoved` —
all `EventHandler<FloorPlan*EventArgs>`.

A tool can also be declared inline, because the shared engine's types are mapped into the same XAML
namespace:

```xml
<fp:FloorPlanView.ActiveTool>
    <fp:DrawRoomTool />
</fp:FloorPlanView.ActiveTool>
```

## Blazor

No registration call.

```razor
@* The height is not optional. *@
<div style="height: 60vh">
    <FloorPlanView @ref="plan"
                   Document="document"
                   ActiveTool="tool"
                   ShowGrid="showGrid"
                   SnapToGrid="true"
                   @bind-SelectedElement="selected"
                   OnElementTapped="OnTapped" />
</div>

@code {
    FloorPlanView? plan;
    readonly FloorPlanDocument document = BuildPlan();
    IFloorPlanTool tool = new SelectTool();
    FloorPlanElement? selected;
    bool showGrid = true;
}
```

Parameters mirror MAUI: `Document`, `Mode`, `ShowGrid`, `SnapToGrid`, `ActiveTool`, `Theme`,
`@bind-SelectedElement`, `FitOnLoad` (default true), `Class`, `Style`. Callbacks are
`OnElementTapped`, `OnSelectionChanged`, `OnElementAdded`, `OnElementsRemoved`.

On Blazor WebAssembly the package fails the build (SHINY0001) when the WebAssembly native build
toolchain is missing, rather than shipping an app that throws `DllNotFoundException: libSkiaSharp`.

## Modes

```csharp
FloorPlanEditorMode.Edit   // default: the active tool has the pointer, selection carries handles
FloorPlanEditorMode.View   // the surface pans, nothing is editable, a tap raises ElementTapped
```

View mode decides between a tap and a pan by distance: a press that travels more than
`FloorPlanEngine.DragThreshold` (5 screen pixels) pans, one that does not is a tap. That is what
makes a seating chart usable on touch, where no finger holds still.

Switching modes clears the selection — handles are Edit-mode chrome.

## The document

```csharp
using Shiny.Controls.FloorPlan;

var plan = new FloorPlanDocument { Width = 1200, Height = 900, GridSize = 20 };

plan.Elements.Add(new RoomElement
{
    Name = "Main Office",          // what a tap reports
    Label = "Main Office",         // what is drawn in the middle of the room
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

| Element | Key members |
| --- | --- |
| `RoomElement` | `Width`, `Height`, `Label` |
| `WallElement` | `Start`, `End` (relative to `Transform`), `Thickness` |
| `DoorElement` | `Width` (= swing radius), `SwingAngle`, `DoorType` (Single/Double/Sliding) |
| `CubicleElement` | `Width`, `Height`, `Occupant` |
| `OutletElement` | `OutletType` (Standard/Floor/Data), `Size` — **anchored on its centre**, unlike everything else |
| `FurnitureElement` | `Kind` (Desk/Chair/Table/Bookshelf/Sofa/FileCabinet), `Width`, `Height` |
| `CustomElement` | `ShapeDefinitionId` into `Document.CustomShapes`, `Width`, `Height` |

Common to all: `Id`, `Name`, `Transform` (X/Y/Rotation/ScaleX/ScaleY), `Style`, `IsLocked`,
`IsVisible`, `ZIndex`, `Metadata` (a `Dictionary<string, string>` the engine never reads).

`Building` → `List<Floor>` → `Floor.Plan` is the multi-storey shape. The control only ever draws one
`FloorPlanDocument`.

### Colours are theme-aware by default

`ElementStyle.FillColor` and `StrokeColor` are **`string?` defaulting to null**, meaning "whatever the
theme says". Do not populate them out of habit — a document that hard-codes `#CCCCCC` for every room
can only be viewed on white. Set them when the plan is genuinely colour-coded:

```csharp
new RoomElement { Label = "Open plan" }                                        // follows the theme
new RoomElement { Label = "Boardroom", Style = { FillColor = "#E8F5E9" } }     // green in both schemes
```

### Locking

`IsLocked` is enforced in the editor state, so a locked element cannot be selected, dragged, resized
or deleted — including by a rubber band dragged over it.

## Saving

```csharp
var json = FloorPlanSerializer.SerializeDocument(plan);
var back = FloorPlanSerializer.DeserializeDocument(json);

FloorPlanSerializer.SerializeBuilding(building);
FloorPlanSerializer.DeserializeBuilding(json);
```

Source-generated (`FloorPlanJsonContext`), so it survives trimming and Native AOT. The
`JsonDerivedType` discriminators (`"room"`, `"wall"`, `"door"`, `"cubicle"`, `"outlet"`,
`"furniture"`, `"custom"`) are part of the saved format — never rename one.

## Tools

| Tool | |
| --- | --- |
| `SelectTool` | Default. Pick, drag, resize (8 handles), rubber-band. Shift adds to the selection |
| `PanTool` | Drags the camera |
| `DrawRoomTool` | Drag out a room. `MinimumSize` (20) rejects a stray click; `DefaultLabel` |
| `DrawWallTool` | One click per end. `Chained` (default true) starts the next where the last ended; right-click ends the run. `Thickness` |
| `PlaceElementTool` | Drops an element per click, with a ghost of the real thing under the pointer |

The middle button pans from any tool.

```csharp
new PlaceElementTool(FurnitureKind.Chair)
new PlaceElementTool(OutletType.Data)
new PlaceElementTool { Kind = FloorPlanElementKind.Cubicle }
new PlaceElementTool { Kind = FloorPlanElementKind.Custom, ShapeDefinitionId = "plant" }
new PlaceElementTool(() => new CubicleElement { Metadata = { ["bookable"] = "true" } })  // Factory wins
```

`Repeat = false` falls back to `SelectTool` after one drop.

Snapping applies **on release** for a drag, and immediately for a draw or place tool. `GridSize = 0`
turns both the grid and snapping off.

### A custom tool

```csharp
public class MeasureTool : IFloorPlanTool
{
    public string Name => "Measure";

    public void OnActivated(FloorPlanToolContext context) { }
    public void OnDeactivated() { }
    public void OnPointerPressed(FloorPlanToolContext context, FloorPlanPointerEventArgs e) { }
    public void OnPointerMoved(FloorPlanToolContext context, FloorPlanPointerEventArgs e) { }
    public void OnPointerReleased(FloorPlanToolContext context, FloorPlanPointerEventArgs e) { }

    // Plan coordinates - the camera transform is already applied. Divide stroke widths and dash
    // lengths by context.Camera.Zoom so they stay constant on screen.
    public void Render(SKCanvas canvas, FloorPlanToolRenderContext context) { }
}
```

`context.SnapToGrid(point)`, `context.HitTest(world)` and `context.AddAndSelect(element)` are the
helpers. Only `OnPointerReleased` should change the document.

## Theming

Leave `Theme` null and the plan follows the app: MAUI reads `Application.Current.Resources`, Blazor
reads the theme's CSS custom properties. Only neutrals follow — the selection blue and the furniture
colours are fixed, the same rule the Office surfaces use.

```csharp
plan.Theme = FloorPlanTheme.Dark with { Selection = PlanColor.Rgb(0xFF, 0x6B, 0x00) };
```

`FloorPlanTheme` carries `Background`, `GridLine`, `PlanBorder`, `ElementFill`, `ElementStroke`,
`LabelText`, `DoorOpening`, `OutletFill`, `OutletSymbol`, `Selection`, `SelectionFill`, `HandleFill`,
`Preview`, the six furniture colours, `CubicleDesk`, and the font/handle metrics.

## Custom renderers

```csharp
plan.Engine.RegisterRenderer(new MyRoomRenderer());   // IFloorPlanElementRenderer
```

Keyed on `ElementType` **exactly** — a base-type registration does not cover subclasses.
`GetOutlinePath` is what the pointer is tested against, so return the silhouette actually drawn.

## Platforms

MAUI: iOS, Android, Mac Catalyst, Windows. Blazor: WASM, Server, Hybrid.

`net10.0-macos` (AppKit) is **not supported** on its own — `SkiaSharp.Views.Maui` ships no macOS
asset and the stock handler throws. An app that also references `Shiny.Maui.Controls.Office` gets a
working canvas from that package's handler. Mac Catalyst is the supported macOS story.
