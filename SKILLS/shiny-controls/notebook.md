# Notebook (free-form OneNote-style canvas)

Two controls on both hosts, in the same packages as the other Office editors:

| Control | What it is |
|---|---|
| `NotebookEditor` | the lone canvas — tools, selection, caret, typing, ink. No chrome. |
| `NotebookEditorView` | `NotebookEditor` plus the ribbon, the section tabs and the page list |

Same two constraints as everything else in these packages: **MAUI needs `UseShinyOffice()`** (it
registers SkiaSharp, plus the AppKit canvas on `net10.0-macos`), **Blazor is WASM-only**, and on
Blazor the container needs an **explicit height**.

## Create or open

```csharp
var notebook = NotebookDocument.Create("Field notebook");        // one section, one empty page
using var notebook = await NotebookDocument.OpenAsync("field.shinynote");
using var notebook = await NotebookDocument.OpenAsync(stream);
```

There is **no `editable:` flag** — unlike the deck, the workbook and the document, a notebook is not a
projection of an OOXML package, so it is always editable. Use `IsReadOnly` on the control to present
one read-only.

### Blazor

```razor
<div style="height:640px">
    <NotebookEditorView Notebook="notebook" @bind-Tool="tool" NotebookChanged="OnChanged" />
</div>
```

### MAUI

```xml
<office:NotebookEditorView x:Name="Editor" />
```

```csharp
this.Editor.Notebook = notebook;
```

## The model

```
NotebookDocument → NotebookSection → NotebookPage → NoteItem
```

`NoteItem` is **one immutable record** with a `Kind` discriminator — `Text`, `Shape`, `Image`, `Ink` —
not a type hierarchy, for the same reason `SlideShape` carries its picture, its table and its text
side by side: every item has the same bounds, handles and z-order.

**Z-order is the index in `NotebookPage.Items`, not a field.** Do not look for or write a `Z`
property.

`NotebookDocument.Sections` and `NotebookSection.Pages` are **mutable lists**, for building a notebook
up before anyone edits it (a template, an import, seed content). Every edit a *user* makes goes
through a command so it can be undone.

```csharp
var page = new NotebookPage(NotebookDocument.NewId(), "Reading list") { Rule = PageRule.Dots };
page.Items.Add(NotebookDocument.NewTextItem(60, 60, 480, "Notes"));
page.Items.Add(NotebookDocument.NewShapeItem(ShapeGeometry.Hexagon, 200, 300, 120, 90));
document.Sections[0].Pages.Add(page);
```

## The page is unbounded — this is the part to get right

A slide is a fixed artboard that gets **fitted**. A notebook page has no edges: `Extent()` is
`MinWidth`/`MinHeight` unioned with every item's bounds plus `Padding`, and the canvas **scrolls and
zooms**. Do not write code that centres content on a page-sized rectangle or that assumes a fixed page
width — there isn't one.

```csharp
var (x, y) = controller.ToPage(viewportX, viewportY);   // pointer → model
var (vx, vy) = controller.ToViewport(pageX, pageY);     // model → pointer
controller.SetZoom(2, anchorX, anchorY);                // zoom about a viewport point
controller.ScrollBy(dx, dy);
controller.ScrollIntoView(x, y, w, h);
```

Scroll is held in **content pixels** (already multiplied by the zoom).

## Three layers of state

**Tool** — what a press *starts*. **Selection** — a `IReadOnlyList<string>` of item ids, because a
lasso catches many things at once. **Text editing** — a caret inside exactly one item.

```csharp
var c = editor.Controller!;

c.Tool = NoteTool.Pen;             // Select, Text, Shape, Pen, Highlighter, Eraser, Lasso, Pan
c.SelectedIds                      // ids, not indexes — a set, not a single int
c.SingleSelection                  // the one item, or null when nothing or several are selected
c.IsEditingText
```

There is **no `SelectedShape` int** and no `Selection` single-item property that a slide-editor habit
would reach for. Toolbar code should gate on `HasSelection` and `IsEditingText`.

`Escape` steps back one layer at a time: leaves text → puts the tool down → clears the selection.

## Driving it

```csharp
// insert — all take PAGE coordinates
c.AddTextBox(x, y, width: 320, text: null);   // creates and puts the caret in it
c.AddShape(ShapeGeometry.Ellipse, x, y, w, h);
c.AddImage(bytes, "image/png", x, y, pixelWidth, pixelHeight);   // scaled down to fit the page

// text — same engine as the document and slide editors
c.InsertText("Hello");
c.InsertParagraph();
c.Backspace(); c.Delete();
c.ToggleBold(); c.ToggleItalic(); c.ToggleUnderline(); c.ToggleStrikethrough();
c.SetFontSize(14);                 // POINTS, not pixels
c.SetFontFamily("Calibri");
c.SetTextColor(color); c.SetHighlight(color);   // null clears
c.SetAlignment(TextAlignment.Center);
c.ToggleBulletList(); c.ToggleNumberedList(); c.SetListStyle(ListStyle.None);
c.ShiftLevel(+1); c.HandleTab(shift: false);

// items
c.DeleteSelection(); c.DuplicateSelection(); c.NudgeSelection(dx, dy);
c.BringToFront(); c.SendToBack(); c.BringForward(); c.SendBackward();
c.SetSelectionFill(color); c.SetSelectionOutline(color); c.SetSelectionGeometry(geometry);
c.SetSelectionInkColor(color);     // the only thing Fill/Outline do not reach
c.SetSelectionLocked(true);        // painted, never selectable

// pages and sections
c.AddPage("Untitled page"); c.DeletePage(); c.RenamePage("Kick-off");
c.MovePage(pageId, targetSectionId, index);
c.AddSection("Research"); c.DeleteSection(); c.RenameSection("Research", color);
c.SetPageRule(PageRule.Grid, spacing: 28); c.SetPageBackground(color);

c.Undo(); c.Redo();
```

**Formatting reaches a selected container as well as a caret inside one** — unlike the slide editor.
A shape with a label goes bold from one click. So `HasText` for a toolbar is
`IsEditingText || HasSelection`, not just `IsEditingText`.

## Ink

```csharp
c.PenColor = new ArgbColor(255, 0x1A, 0x1A, 0x1A);
c.PenWidth = 2.2;                                       // page pixels
c.HighlighterColor = new ArgbColor(110, 0xFF, 0xE0, 0x3B);   // translucent — the alpha matters
c.HighlighterWidth = 16;
c.EraseMode = EraseMode.Point;                          // or Stroke
c.EraserRadius = 10;
```

- **Pressure** is normalised 0..1 where **0.5 means "no idea"** — a mouse, a finger and a pen mid-flick
  all report it. Only pass a real value for `pointerType == "pen"` / `SKTouchDeviceType.Pen`; taking a
  mouse's constant at face value changes the pen's weight per platform.
- The **highlighter paints behind every other item** (`NoteItem.PaintsBehind`). Do not try to fix that
  with z-order.
- `EraseMode.Point` **splits one stroke into several items**. Code that assumes an erase only removes
  items will be wrong.
- **Ink is hit-tested on its path**, not its bounding box, and a lasso judges ink by how many of its
  points are inside.
- Ink has no `Width`/`Height` of its own — its geometry is its points. `NoteItem.Bounds()` derives them;
  resizing scales the points via `InkStroke.Scale`.

## Pointer plumbing (only if writing a custom host)

```csharp
c.PointerDown(vx, vy, PointerKind.Pen, extend: shiftHeld);
c.PointerMoveWithPressure(vx, vy, pressure);
c.PointerUp();
c.PointerDoubleClick(vx, vy);
```

A **finger on empty canvas pans** rather than marquee-selecting — pass the real `PointerKind`.

Keyboard is one entry point taking **the browser's key names** (`"ArrowLeft"`, `"Backspace"`,
`"Escape"`):

```csharp
c.HandleKey("b", shift: false, control: true);
```

Blazor passes `e.Key` straight through. **MAUI has no portable key-down event**, so a desktop host
wires its own hook and calls `NotebookEditor.HandleKey(EditorKey.Bold, control: true)`.

## Painting

`NotebookPainter` (in `Shiny.Controls.Office.Skia`) draws the page and the overlay for **both** hosts.
Build the overlay with `NotebookChrome.From(controller, accent)` rather than assembling it by hand.

Chrome is drawn in **viewport** coordinates so frames and handles stay a constant pixel size at every
zoom; content is drawn in page coordinates under one canvas transform.

## The file format

`.shinynote` is a zip: `notebook.json`, `pages/{pageId}.json`, `media/{itemId}.png`. The **model is the
truth** and the file is written from it, so there is no reproject step and no byte-identical
round-trip promise — the promise is that everything survives a save and reopen.

```csharp
await notebook.SaveAsAsync("field.shinynote");   // atomic, through a temp file
await notebook.SaveToAsync(stream);              // leaves IsDirty alone
var bytes = notebook.ToArray();
```

## Which ink text is drawn in

Text left at `TextStyle.Default.Color` (black) is substituted at paint time for an ink chosen against
**whatever is directly behind it** — a shape's opaque fill where it has one, the page otherwise. So do
**not** set an explicit near-black colour on generated content: that pins it, and it disappears on a
dark page. Leave the colour alone and it follows the theme.

An authored colour is honoured as-is. Ink is content and is never recoloured; only `PenColor` follows
the theme, via `controller.ApplyDefaultInk(theme.DefaultInk)`, and only until `PenColor` is assigned.

## Theming

The **one** Office surface whose page follows the app's theme — a notebook page was never printed and
has no canonical appearance. Leave `Theme` unset to follow the app; pin it with `NotebookTheme.Light` /
`NotebookTheme.Dark`. Existing ink is never recoloured.

## Invariants worth not breaking

- Never a section with no pages, never a notebook with no sections — deleting the last one refills it.
- A text container the user typed nothing into is deleted when the caret leaves it.
- A whole drag is **one** undo step; so is a whole typing run. Pointer-up breaks the run.
- Undo prunes the selection, so no frame is drawn round an item that is no longer there.
- List numbers are resolved from position (`NotebookEditorController.Renumber`), never stored as typed.

## Not implemented

Rotation handles, tables on a page, ink-to-shape recognition, page templates, search across pages,
tags, and reordering sections by drag.
