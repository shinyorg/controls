# ImageEditor

An inline image editor with cropping, rotation, freehand drawing with color, line/arrow tools, shape tools (rectangle, ellipse, circle) with fill and border, text annotations, undo/redo, reset, and export to PNG/JPEG/WEBP at configurable resolutions. Every feature can be toggled on or off via properties, and the built-in toolbar can be replaced entirely with a `ToolbarTemplate`.

**Zoom/pan works in every tool.** The zoom is a transform on the drawing surface (not on the native view), and touch points are mapped back through it before any tool math runs — so the user can magnify to 8x and draw, crop or place text with pixel accuracy. Pinch anywhere, two-finger drag to pan, double-tap to toggle, or use the toolbar zoom cluster. On Blazor the mouse wheel zooms about the cursor and middle-drag pans.

The default toolbar is a floating rounded bar: a horizontally scrollable tool row (never clips on a phone), a contextual options row for the active tool only, and an action row with undo/redo/reset, the zoom cluster and save. Icons are vector paths, not unicode glyphs.

## Basic Usage

```xml
<shiny:ImageEditor Source="{Binding ImageData}"
                   CurrentToolMode="{Binding ToolMode}"
                   AllowCrop="True"
                   AllowRotate="True"
                   AllowDraw="True"
                   AllowTextAnnotation="True"
                   DrawStrokeColor="Red"
                   DrawStrokeWidth="3" />
```

## ImageEditor Properties

| Property | Type | Default | Binding Mode | Description |
|---|---|---|---|---|
| `Source` | `byte[]?` | `null` | OneWay | Image data to edit |
| `CurrentToolMode` | `ImageEditorToolMode` | `None` | TwoWay | Active tool: None, Move, Crop, Draw, Text, Line, Arrow, Rectangle, Ellipse, Circle |
| `AllowCrop` | `bool` | `true` | OneWay | Enable/disable crop tool and toolbar button |
| `AllowRotate` | `bool` | `true` | OneWay | Enable/disable rotate action and toolbar button |
| `AllowDraw` | `bool` | `true` | OneWay | Enable/disable freehand drawing tool and toolbar button |
| `AllowTextAnnotation` | `bool` | `true` | OneWay | Enable/disable text annotation tool and toolbar button |
| `AllowRectangle` | `bool` | `true` | OneWay | Enable/disable the rectangle shape tool and toolbar button |
| `AllowEllipse` | `bool` | `true` | OneWay | Enable/disable the ellipse shape tool and toolbar button |
| `AllowCircle` | `bool` | `true` | OneWay | Enable/disable the circle shape tool and toolbar button |
| `ShapeFillColor` | `Color?` | `null` | TwoWay | Shape interior; null draws the outline only. Alpha is honoured (Blazor: `string?` hex + `ShapeFillOpacity`) |
| `ShowShapeFillPicker` | `bool` | `true` | OneWay | Fill swatch and fill on/off toggle while a shape tool is active |
| `AllowZoom` | `bool` | `true` | OneWay | Enable/disable zoom & pan |
| `ZoomLevel` | `double` | `1` | TwoWay | Current zoom factor; 1.0 is fit-to-view |
| `MinZoom` | `double` | `1` | OneWay | Lower zoom bound |
| `MaxZoom` | `double` | `8` | OneWay | Upper zoom bound |
| `ShowZoomControls` | `bool` | `true` | OneWay | Show the zoom cluster in the default toolbar |
| `ShowToolLabels` | `bool` | `true` | OneWay | Captions under the tool icons; off gives a compact icon-only bar |
| `ShowStrokeWidthPicker` | `bool` | `true` | OneWay | Pen-weight presets beside the colour swatch |
| `StrokeWidthPresets` | `IList<double>` | `2, 4, 8` | OneWay | Weights offered by the stroke-width picker |
| `ToolbarBackgroundColor` | `Color` | dark scrim | OneWay | Background of the default toolbar |
| `CanUndo` | `bool` | `false` | OneWayToSource | Whether undo is available |
| `CanRedo` | `bool` | `false` | OneWayToSource | Whether redo is available |
| `DrawStrokeColor` | `Color` | `Red` | OneWay | Drawing stroke color |
| `DrawStrokeWidth` | `double` | `3` | TwoWay | Drawing stroke width (the toolbar's weight picker writes back to it) |
| `TextFontSize` | `double` | `16` | OneWay | Text annotation font size |
| `AnnotationTextColor` | `Color` | `White` | OneWay | Text annotation color |
| `ToolbarTemplate` | `DataTemplate?` | `null` | OneWay | Custom toolbar (replaces the default toolbar entirely) |
| `ToolbarPosition` | `ToolbarPosition` | `Bottom` | OneWay | Toolbar placement: Top or Bottom |
| `UseFeedback` | `bool` | `true` | OneWay | Feedback on tool actions |

## ImageEditor Commands

| Command | Parameter | Description |
|---|---|---|
| `UndoCommand` | — | Undo the last edit action |
| `RedoCommand` | — | Redo the last undone action |
| `RotateCommand` | `float` (degrees) | Rotate the image |
| `ResetCommand` | — | Clear all edits and restore the original image |
| `CropCommand` | — | Toggle crop mode on/off |
| `DrawCommand` | — | Toggle draw mode on/off |
| `TextCommand` | — | Toggle text mode on/off |
| `RectangleCommand` | — | Toggle the rectangle tool on/off |
| `EllipseCommand` | — | Toggle the ellipse tool on/off |
| `CircleCommand` | — | Toggle the circle tool on/off |
| `ZoomInCommand` | — | Zoom in one step about the centre |
| `ZoomOutCommand` | — | Zoom out one step about the centre |
| `ZoomToFitCommand` | — | Return to fit-to-view |

## ImageEditor Methods

| Method | Returns | Description |
|---|---|---|
| `Undo()` | `void` | Undo the last action |
| `Redo()` | `void` | Redo the last undone action |
| `Rotate(float degrees)` | `void` | Rotate by the given angle |
| `Reset()` | `void` | Clear all edits |
| `ApplyCrop()` | `void` | Commit the active crop selection |
| `ZoomIn()` / `ZoomOut()` / `ZoomToFit()` | `void` | Zoom about the centre of the view |
| `ExportAsync(ImageExportOptions?)` | `Task<Stream>` | Export the edited image |

## ImageEditor Features

- **Crop**: Drag-handle area selection starting at full image; areas outside the crop are dimmed, the selected area stays fully lit. 8 drag handles (4 corners + 4 midpoints) with rule-of-thirds grid overlay.
- **Rotate**: 90° increments or arbitrary angle rotation. Each rotation is an undoable action.
- **Freehand drawing**: Draw strokes with configurable color and width. Each completed stroke is one undoable action.
- **Shapes**: Rectangle, ellipse and circle, dragged corner to corner. The ink colour and pen weight are the border; the fill is its own swatch with its own opacity and an on/off toggle — an unfilled shape is a highlight box, an opaque one is a redaction block. The circle tool constrains the drag to a square; on Blazor holding **Shift** does the same for the rectangle and ellipse. Each finished shape is one undoable action.
- **Text annotations**: Tap to place text on the image. Prompts for input, configurable font size and color.
- **Zoom & pan**: View-only (never baked into the export) but live in *every* tool — pinch, two-finger pan, double-tap, toolbar buttons, and on Blazor wheel-zoom about the cursor. Crop handles and hairlines keep a constant on-screen size at any zoom, and the pan is clamped so the image can't be lost off-screen.
- **Resolution-independent annotations**: strokes, lines, shapes and text record the on-screen image width they were captured at, so what you draw on a small preview (or while zoomed in) keeps its proportions when exported at full resolution.
- **Undo/redo**: Every edit action (crop, rotate, draw stroke, shape, text) is pushed to a stack. Undo pops to a redo stack; redo re-applies.
- **Reset**: Clears all actions and restores the original image.
- **Export**: Render the edited image to a stream (MAUI) or byte array (Blazor) in PNG, JPEG, or WEBP format at a target resolution.
- **Toolbar**: Floating rounded bar with vector icons. Row 1 is the tool picker in a horizontal scroller so it never clips; row 2 shows options for the active tool only (colour, pen weight, font, size); row 3 carries undo/redo/reset, the zoom cluster and save. Set `AllowX=false` to hide a tool, `ShowToolLabels=false` for a compact icon-only bar, or replace the whole thing with `ToolbarTemplate` (Blazor: `ToolbarTemplate`, or `ToolbarActions` to just add trailing buttons to the default bar).

## ImageEditor ViewModel Pattern

```csharp
public partial class ImageEditorViewModel : ObservableObject
{
    [ObservableProperty] byte[]? imageData;
    [ObservableProperty] bool canUndo;
    [ObservableProperty] bool canRedo;
    [ObservableProperty] ImageEditorToolMode currentToolMode;
    [ObservableProperty] Color drawColor = Colors.Red;
    [ObservableProperty] double zoomLevel = 1;

    [RelayCommand]
    async Task LoadImage()
    {
        var result = await MediaPicker.PickPhotoAsync();
        if (result == null) return;
        using var stream = await result.OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        ImageData = ms.ToArray();
    }
}
```

## ImageEditor Export

```csharp
// MAUI — get a Stream
var stream = await editor.ExportAsync(new ImageExportOptions
{
    Format = ImageExportFormat.Jpeg,
    Quality = 0.85f,
    Width = 1920,
    Height = 1080
});

// Blazor — get byte[]
var bytes = await editor.ExportAsync("jpeg", 0.85, 1920, 1080);
```

## ImageEditor Shapes

The border comes from the ink settings the other tools share (`DrawStrokeColor` / `DrawStrokeWidth`);
only the fill is shape-specific. `ShapeFillColor` of `null` means "outline only".

```xml
<shiny:ImageEditor Source="{Binding ImageSource}"
                   AllowRectangle="True"
                   AllowEllipse="True"
                   AllowCircle="True"
                   DrawStrokeColor="Red"
                   DrawStrokeWidth="4"
                   ShapeFillColor="{Binding ShapeFillColor}"
                   ShowShapeFillPicker="True" />
```

```csharp
// Redaction block: opaque fill, same colour border
editor.CurrentToolMode = ImageEditorToolMode.Rectangle;
editor.ShapeFillColor = Colors.Black;
editor.DrawStrokeColor = Colors.Black;

// Highlight box: translucent fill, contrasting border
editor.ShapeFillColor = Colors.Yellow.WithAlpha(0.3f);
editor.DrawStrokeColor = Colors.Yellow;
```

```razor
@* Blazor — the fill is a hex string and carries alpha in ShapeFillOpacity,
   because <input type="color"> cannot express it *@
<ImageEditor Source="@url"
             AllowRectangle="true"
             AllowEllipse="true"
             AllowCircle="true"
             ShapeFillColor="#ffcc00"
             ShapeFillOpacity="0.3" />
```

## ImageEditor Zoom

```xml
<shiny:ImageEditor Source="{Binding ImageSource}"
                   AllowZoom="True"
                   ZoomLevel="{Binding ZoomLevel}"
                   MaxZoom="8"
                   ShowZoomControls="True" />
```

```razor
@* Blazor — the zoom cluster is built in; ZoomLevelChanged reports gestures too *@
<ImageEditor @ref="editor"
             Source="@url"
             AllowZoom="true"
             MaxZoom="8"
             ZoomLevelChanged="v => zoom = v" />

@code {
    ImageEditor? editor;
    double zoom = 1;

    Task ZoomTo400() => editor!.SetZoomAsync(4).AsTask();
}
```

### Toolbar

The default toolbar is a [Ribbon](ribbon.md) on both hosts — Home (Tools / Shapes / Image), View
(zoom), a contextual tab for the active tool's options, and undo/redo/reset in quick access. You do
not build any of that; it is what the control renders when you do not pass `ToolbarTemplate`.

Do **not** hand-roll a tool row beside the editor. `ToolbarTemplate` replaces the whole bar if a host
genuinely needs its own, and `ToolbarActions` adds the host's own commands into the Home tab without
giving up the rest.

On a narrow editor the ribbon switches itself to `Simplified` below 600px — no code needed.

## Teardown (Blazor)

The component survives being removed mid-operation: interop runs through one guarded path that stops
once disposal begins, so closing the editor while a call is in flight is a no-op rather than an
`ObjectDisposedException` reaching the renderer (which would tear down the whole page, not just the
editor). Do **not** generate defensive wrappers, try/catch or `ErrorBoundary` around `<ImageEditor>`
for this - it is handled in the control.

- `ExportAsync` returns an **empty array** when there is nothing to export from - check `Length`
  rather than assuming bytes.
- Methods that record state (`SetModeAsync`, `ApplyCropAsync`, `ResetAsync`) only do so when the call
  actually ran.
- A genuine `JSException` still propagates; only disconnection, disposal and cancellation are
  swallowed.
- `CanUndoChanged` / `CanRedoChanged` / `ZoomLevelChanged` never fire after the component is disposed.
