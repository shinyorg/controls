# ImageEditor

[← All Shiny Controls](../../README.md)

An inline image editor with cropping, rotation, freehand drawing, line and arrow drawing, **shapes — rectangle, ellipse and circle, each with its own fill and border** — text annotations with font family and font size selection, and **zoom/pan that stays live in every tool** — pinch (or wheel / zoom buttons) to magnify up to 8x and draw, crop or place text with pixel accuracy, then two-finger drag to pan without leaving the tool. Includes a built-in undo/redo stack, reset-to-original, and export to PNG/JPEG/WEBP at configurable resolutions. Every feature can be toggled on/off, and the default toolbar can be replaced with a custom template.

The default toolbar is a floating rounded bar with vector (not glyph-font) icons, a horizontally scrollable tool row that never clips on narrow screens, a contextual options row for the active tool (colour swatch, pen weights, font pickers), and an action row with undo/redo/reset, a zoom cluster, and save.

| Editor | Crop Mode |
|:---:|:---:|
| ![Image Editor](../../assets/imageeditor1.png) | ![Crop Mode](../../assets/imageeditor2.png) |

```xml
<shiny:ImageEditor Source="{Binding ImageSource}"
                   CurrentToolMode="{Binding ToolMode}"
                   AllowCrop="True"
                   AllowRotate="True"
                   AllowDraw="True"
                   AllowTextAnnotation="True"
                   DrawStrokeColor="Red"
                   DrawStrokeWidth="3" />
```

| Property | Type | Default | Description |
|---|---|---|---|
| Source | ImageSource? | null | Image to edit (supports file, stream, URI) |
| CurrentToolMode | ImageEditorToolMode | Move | Active tool (Move, Crop, Draw, Text, Line, Arrow, Rectangle, Ellipse, Circle) — TwoWay |
| AllowCrop | bool | true | Enable/disable crop tool |
| AllowRotate | bool | true | Enable/disable rotate action |
| AllowDraw | bool | true | Enable/disable freehand drawing |
| AllowTextAnnotation | bool | true | Enable/disable text annotation |
| AllowLine | bool | true | Enable/disable line drawing tool |
| AllowRectangle | bool | true | Enable/disable the rectangle shape tool |
| AllowEllipse | bool | true | Enable/disable the ellipse shape tool |
| AllowCircle | bool | true | Enable/disable the circle shape tool |
| ShapeFillColor | Color? | null | Shape interior; null draws the outline only. Alpha honoured — TwoWay |
| ShowShapeFillPicker | bool | true | Show the fill swatch and fill on/off toggle while a shape tool is active |
| AllowFontSelection | bool | false | Show font picker button in text mode |
| AllowFontSizeSelection | bool | false | Show font size picker button in text mode |
| AllowZoom | bool | true | Enable/disable zoom & pan |
| ZoomLevel | double | 1 | Current zoom factor where 1.0 is fit-to-view — TwoWay |
| MinZoom | double | 1 | Lower zoom bound |
| MaxZoom | double | 8 | Upper zoom bound |
| ShowZoomControls | bool | true | Show the zoom out / % / zoom in / fit cluster in the toolbar |
| ShowToolLabels | bool | true | Show captions under the tool icons |
| ShowStrokeWidthPicker | bool | true | Show pen-weight presets next to the colour swatch |
| StrokeWidthPresets | IList\<double\> | 2, 4, 8 | Pen weights offered by the stroke-width picker |
| ToolbarBackgroundColor | Color | dark scrim | Background of the default toolbar |
| CanUndo | bool | false | Whether undo is available (OneWayToSource) |
| CanRedo | bool | false | Whether redo is available (OneWayToSource) |
| DrawStrokeColor | Color | White | Drawing stroke color — TwoWay |
| DrawStrokeWidth | double | 3 | Drawing stroke width — TwoWay |
| TextFontSize | double | 16 | Text annotation font size |
| TextFontFamily | string? | null | Font family for text annotations (TwoWay) |
| AnnotationTextColor | Color | White | Text annotation color |
| AvailableFonts | IList\<string\>? | null | Font families shown in font picker |
| AvailableFontSizes | IList\<double\>? | null | Font sizes shown in font size picker |
| SaveCommand | ICommand? | null | Invoked with `EditedImage` parameter on save |
| SaveText | string | "Save" | Save button label |
| CropApplyText | string | "Apply" | Crop apply button label |
| CropCancelText | string | "Cancel" | Crop cancel button label |
| ToolbarTemplate | DataTemplate? | null | Custom toolbar (replaces default) |
| ToolbarPosition | ToolbarPosition | Bottom | Toolbar placement (Top or Bottom) |
| UseFeedback | bool | true | Feedback on actions |

**Features:**
- Zoom and pan in **every** tool: pinch anywhere (two fingers), two-finger drag to pan, double-tap to toggle, plus toolbar zoom buttons and a live zoom % readout. Blazor adds mouse-wheel zoom about the cursor and middle-button pan. Crop chrome and hit targets keep a constant on-screen size at any zoom.
- Crop with drag handles, rule-of-thirds grid, dimmed overlay, and dedicated Apply/Cancel toolbar
- 90° rotation (or arbitrary angles)
- Freehand drawing with configurable color and stroke width (constrained to image bounds)
- Line and arrow drawing between two points with configurable color and width
- Shapes — rectangle, ellipse and circle — dragged corner to corner. The ink colour and pen weight are the border; the fill is its own swatch with its own opacity and an on/off toggle, so the same tool draws a translucent highlight box or an opaque redaction block. The circle tool constrains the drag to a square, and on Blazor holding **Shift** does the same for the rectangle and ellipse.
- Inline text annotations placed by tapping the image with optional font family and size selection
- Integrated color picker for draw color
- Font picker and font size picker integration (when `AllowFontSelection`/`AllowFontSizeSelection` enabled)
- Undo/redo for every edit action
- Reset to original image
- Save via `SaveCommand` with `EditedImage` — call `ToStreamAsync(format)` to get PNG, JPEG, or WEBP
- Image border showing the drawable surface area
- Strokes, lines, shapes and text record the on-screen image size they were drawn at, so annotations made on a small preview (or while zoomed in) keep their proportions when exported at full resolution

**Commands:** `UndoCommand`, `RedoCommand`, `RotateCommand`, `ResetCommand`, `CropCommand`, `DrawCommand`, `TextCommand`, `LineCommand`, `RectangleCommand`, `EllipseCommand`, `CircleCommand`, `SaveCommand`, `ZoomInCommand`, `ZoomOutCommand`, `ZoomToFitCommand`

**Methods:** `Undo()`, `Redo()`, `Rotate(float)`, `Reset()`, `ApplyCrop()`, `GetEditedImage()`, `ZoomIn()`, `ZoomOut()`, `ZoomToFit()`

**Events:** `ZoomChanged`

On Blazor the equivalents are `ZoomInAsync()`, `ZoomOutAsync()`, `ZoomToFitAsync()`, `SetZoomAsync(double)`, the `ZoomLevel` property and the `ZoomLevelChanged` callback, plus a `ToolbarActions` render fragment for host-supplied buttons at the trailing edge of the bar. `ShapeFillColor` there is a `#rrggbb` string and carries its alpha in a companion `ShapeFillOpacity` (0-1), because `<input type="color">` cannot express alpha — MAUI keeps it in the `Color` itself.

## The toolbar is a Ribbon

The editor's toolbar is a [Ribbon](ribbon.md) on both hosts, replacing the three-row bar it used to
build by hand. The tools were already grouped the way a ribbon wants them, so most of it is a change
of container — but one part is genuinely better for it: the per-tool options are a **contextual tab**
now, captioned *Drawing Tools* / *Shape Tools* / *Text Tools*, instead of an unlabelled strip that
changed shape under the buttons that caused it.

| Where | What |
| --- | --- |
| **Home** | Tools (Move, Crop, Draw, Text), Shapes (Line, Arrow, Rectangle, Ellipse, Circle), Image (Rotate), and the host's own actions — which never fold into an overflow button. |
| **View** | Zoom out / in / fit, and the zoom readout. Only present when `AllowZoom` and `ShowZoomControls` are both on. |
| **Contextual tab** | Colour, stroke weights, shape fill and opacity, font and font size — whichever apply to the tool in hand. Absent for Move and Crop, which have no options. |
| **Quick access** | Undo, redo, reset. Outside the tabs, so they never move or disappear when the tab does. |

The bar is **two rows** rather than the ribbon's three: a shorter bar matters more here than in a
document app — the picture is the point of the control — and the editor's groups divide more evenly
over two, so the columns come out square instead of leaving one item stranded in a column of its own.
Rotate shares the *Image* group with the host's actions rather than taking a group of its own, because
a group costs a divider and a title whatever is in it.

Every item is `Small` rather than the ribbon's `Large` default. Two reasons: expanded, a dozen large
buttons is not a palette — small stacks them three to a column and the whole tool set reads at a
glance; and simplified mode keeps a label only on items declared small, so a mix of sizes produced a
row where some tools were labelled and some were bare icons.

**On a narrow editor the ribbon runs in `Simplified` mode** — one dense row, every item small, group
titles dropped — below 600px wide. An expanded ribbon is about a quarter of a phone screen, and this
control's whole job is to show the picture underneath it. On MAUI that is measured against the editor,
so it holds for an editor in a side panel too.

**Crop is still a hand-rolled bar.** It is modal and two commands wide; a ribbon would be the wrong
shape for it entirely.

`ToolbarTemplate` still replaces the whole thing, as before.

## Dark mode

`ToolbarBackgroundColor` defaulted to `Color.FromRgba(20, 20, 22, 0.86f)`, which binds to the
**all-float** overload — the ints widen, and channels there run 0-1, so 20 clamped to 1 and the
"dark scrim" was painted white. With white icons and labels on it, the bar read as an empty strip.
Fixed; the ribbon toolbar draws on a themed surface and does not use it, but the crop bar still does.

**Collapsing sticks.** The editor rebuilds its toolbar on every tool and property change, which means
a fresh `Ribbon` each time — so it reads the display mode back before discarding the old one. Without
that the chevron did nothing you could keep: collapse the bar, pick a tool, and it was open again.

## Teardown (Blazor)

**Closing the editor no longer takes the page with it.** The component holds a JS module, and every
call through it — a queued render syncing parameters, a pointer gesture JS already had in flight, an
operation still awaiting when the host switched views — could land after disposal had begun. Checking
the field for `null` never covered that: the reference can be disposed *while* a call is on the wire,
and `IJSObjectReference` throws `ObjectDisposedException` rather than returning. Unhandled async work
from a component tears down the whole renderer, so an editor closed at the wrong moment took every
other component on the page with it.

Interop now runs through one guarded path that stops once teardown starts, and `DisposeAsync` sets
that flag before releasing anything. Three things follow, and generated code should not fight any of
them:

- **A call made during teardown is a no-op, not a throw.** The methods that also record state
  (`SetModeAsync`, `ApplyCropAsync`, `ResetAsync`) only do so when the call actually ran, so the
  component never reports a mode it never entered.
- **`ExportAsync` answers with an empty array** when there is nothing left to export from. A host
  exporting on the way out gets "no image" rather than an exception out of a teardown path.
- **`JSException` still surfaces.** Only disconnection, disposal and cancellation are swallowed — a
  genuine fault in the editor's script is a bug worth reporting, and hiding it would turn a crash
  into an editor that silently stops responding.

The `[JSInvokable]` callbacks (`OnCanUndoChanged`, `OnCanRedoChanged`, `OnZoomChanged`,
`OnRequestTextInput`) return early once disposed, so a late gesture cannot raise
`CanUndoChanged` at a host that has already dropped the component.

Disposal is idempotent, and an editor removed while its module import is still in flight releases the
module and abandons initialisation rather than wiring handlers to a component that is already gone.
