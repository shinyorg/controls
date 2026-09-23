# Slide Editor (pptx editing)

Two controls on both hosts, over the same packages as the viewers:

| Control | What it is |
|---|---|
| `SlideEditor` | the lone editing surface — canvas, selection, caret, typing. No chrome. |
| `SlideEditorView` | `SlideEditor` plus an editing toolbar and a status line |

Same two constraints as everything else in these packages: **MAUI needs `UseShinyOffice()`** (it registers SkiaSharp, plus the AppKit canvas on `net10.0-macos`), **Blazor is
WASM-only**, and on Blazor the container needs an **explicit height**.

## Open a deck for editing

```csharp
using var deck = await SlideDeck.OpenAsync("deck.pptx", editable: true);
```

Without `editable: true` the deck is read-only and `Execute` throws.

### Blazor

```razor
<div style="height:560px">
    <SlideEditorView Deck="deck" @bind-SlideIndex="index" DeckChanged="OnChanged" />
</div>
```

### MAUI

```xml
<office:SlideEditorView Deck="{Binding Deck}" SlideIndex="{Binding Index}" />
```

## Two gestures — this is the part to get right when generating code

**Shape mode**: a single click/tap selects a shape and draws a **dashed** frame with eight resize
handles. Drag the body to move, a handle to resize.

**Text mode**: a double-click/double-tap puts a caret inside that shape's text; the frame turns
**solid**. A single click inside then moves the caret; a click outside leaves text mode.

Do not expect typing to reach the document while a shape is merely selected — it is deliberately
dropped. `Controller.IsEditingText` is the flag; a toolbar's text-formatting buttons should be
disabled unless it is true.

## Only slide-owned shapes are editable

`SlideShape.IsEditable` is false for anything painted from the layout or master, and for shapes
flattened out of a group. Hit testing skips them. Don't write code that indexes into
`Slide.Shapes` assuming every entry can be selected.

## Driving it

```csharp
var c = editor.Controller!;

c.Select(index);                  // or let PointerDown do it
c.BeginTextEditing(x, y);         // caret into the selected shape's text
c.InsertText("Hello");
c.InsertParagraph();              // Enter — keeps level and bullet
c.Backspace();
c.ToggleBold();                   // selection; a caret INSIDE a word = that word; else the end mark (next typed text)
c.SetFontSize(24);                // POINTS, not pixels
c.SetAlignment(TextAlignment.Center);
c.ShiftLevel(+1);                 // indent the bullet; each paragraph moves relative to its own level
c.HandleTab(shift: false);        // what the Tab key does — always a level change inside a shape
c.ToggleBulletList(); c.ToggleNumberedList();
c.SetListStyle(ListStyle.None);   // explicit: writes a:buNone, see below
c.SetHighlight(color);            // null clears it; ToggleHighlight(color) for a toolbar button
c.AddTextBox(slideX, slideY);
c.DeleteSelectedShape();
c.Undo();
```

## Playing the deck

**Home ▸ Slide ▸ Slide show**. The editor does not grow a presenting mode of its own — it hands a
`SlideView` the deck it is already holding and lets that present it, so the show is the viewer's, whole
(black surround, auto-hiding presenter bar, tap to advance with a back band down the left quarter,
speaker notes, `KeepScreenOnWhilePresenting`). See **document-viewer.md ▸ Presenting mode**. The deck
is one shared object, so what plays is what was typed a moment ago; nothing is saved or reloaded.

```csharp
await view.StartPresentingAsync();      // Blazor; StartPresentingAsync(0) for the run-through
view.StartPresenting();                 // MAUI

view.IsPresenting;                      // read-only - a show is started by calling, not by assigning
await view.StopPresentingAsync();       // MAUI: view.StopPresenting()
```

| Member | |
| --- | --- |
| `PresentingChanged` | Fires however the show ended — Exit, Escape, F11, the Android back gesture. |
| `ShowPresenterControls` | The auto-hiding bar. Default `true`. Forwarded to the show surface. |
| `KeepScreenOnWhilePresenting` | MAUI only, default `true`. |

Rules worth knowing:

- **From the current slide**, not from the top. Pass `0` for a run-through.
- Starting a show **clears the selection**; ending one leaves the editor on the slide the show ended on
  and puts focus back on the surface.
- **No `F5` on the editor**, unlike the viewer. Blazor fixes `preventDefault` at render time, one
  keystroke behind the handler that decides it, so a bound `F5` would sometimes reload the page — and
  on the web that loses an unsaved deck. Do not add one.
- `IsPresenting` is **not** a two-way parameter here (it is on `SlideView`). There is no
  `@bind-IsPresenting` on `SlideEditorView`; call the method.

## Lists

The two toggle buttons write into the paragraph's own `a:pPr`: `a:buChar` for a bullet, `a:buAutoNum`
for a number, `a:buNone` for neither. Read the state back off `CaretFormat.List` and `CaretFormat.Level`.

Three things to get right:

- **`ListStyle.None` writes `a:buNone`, it does not remove the element.** A body placeholder inherits
  its bullet from the master's list style, so leaving the properties alone puts that bullet straight
  back and the button looks like it did nothing.
- **`a:pPr`'s children are a sequence**, and the bullet slot sits between `a:buFont` and `a:tabLst`.
  Appending a `a:buChar` after a `a:defRPr` that was already there saves without complaint and produces
  a file PowerPoint reports as corrupt. Go through `SetBullet`, never build the element by hand.
- **Nine levels, 0-8.** A tenth is a file PowerPoint will not open.

`ShapeParagraph.Bullet` is the mark to draw and is already resolved: an auto-numbered paragraph arrives
with a **real number** in it, counted per text body at its own outline level and rendered in the file's
own scheme (arabic, alphabetic or roman; period, trailing paren or both). `ShapeParagraph.List` is the
kind, which `Bullet` cannot tell you — `"1."` is a perfectly good literal bullet glyph.

Typing `- ` or `1. ` at the start of a paragraph starts a list, using the **same detector as the Word
side** so the two hosts and the two file types cannot drift. `c.IsAutoFormatListEnabled = false` off.

Unlike the document editor, Tab has no "not in a list" case: every paragraph in a shape carries an
outline level whether or not it draws a mark, so `HandleTab` always changes level.

## Adding shapes, pictures and tables

```csharp
c.AddShape(ShapeGeometry.Hexagon, slideX, slideY, width: 240, height: 180, fill: accent);
c.AddPicture(bytes, "image/png", slideX, slideY, width: 400);
c.AddTable(rows: 3, columns: 4, slideX, slideY, width: 480, height: 200);
```

All three place in **slide** coordinates and select what they added, so the next gesture is a drag of
the new object. `AddShape` writes a real drawn shape rather than a text box — no `TextBox` flag on the
non-visual properties — which is what makes PowerPoint give it the theme fill and treat it as a shape.
A table is a `p:graphicFrame`, not a shape, and comes out with a built-in table style applied.

`ShapeGeometry` lives in `Shiny.Controls.Office.Shapes` and is **shared with the document editor** —
the same twenty presets, the same path builder.

Both toolbars already offer these, over the same galleries as the Word side.

## Toolbar icons and tooltips

The slide toolbar draws from the **same `OfficeIcons` set** as the document toolbar — monochrome
stroked artwork on a 24x24 grid, one weight, no colour of its own, shared between MAUI and Blazor. The
slide-only marks are `Previous`, `Next`, `SlideShow`, `TextBox` and `Delete`; `BulletList`, `NumberedList`,
`Indent` and `Outdent` are shared with the document toolbar. No glyph, letter
or emoji goes on a toolbar button; add to the enum instead. `ShowToolbarTooltips` controls the hover
tooltips on those icon-only buttons — on for Blazor and for MAUI desktop, off on iOS and Android.

The full rules, and why they are rules, are in **document-editor.md** — one icon strategy, one place.

## Highlighting

`a:highlight` holds a real colour, so nothing is approximated. The pickers still offer
`HighlightPalette.Swatches` — the same sixteen swatches as the document editor, so one highlight
button behaves the same over both file types.

## Dropping files in

Dragging an image file onto a slide inserts it **centred on the drop point**, sized to at most half the
slide. On by default:

```razor
<SlideEditorView Deck="deck" AllowFileDrop="true" DropRejected="OnRejected" />
```

```csharp
Editor.DropRejected += (_, e) => Toast(e.Reason);   // MAUI
```

Where it works, and what is rejected, is identical to the document editor — see `document-editor.md`.

`CaretFormat.FontSize` is reported in **points** to match `SetFontSize`. The model itself carries
pixels — reporting those makes a size box read 24 for text the user set to 18.

## Coordinates

Two spaces, and only `ToSlide` / `ToViewport` cross between them:

- **slide** coordinates — what the model and the OOXML store
- **viewport** coordinates — what a pointer arrives in

`BoundsOf`, `SelectionBounds`, `SelectionHandles`, `CaretRect` and `TextSelectionRects` all return
viewport coordinates, ready to hand to the painter's `SlideEditorChrome`.

## Painting the chrome

Both hosts build a `SlideEditorChrome` and hand it to `SlidePainter` on the paint request. It is drawn
*outside* the slide's fit transform on purpose — inside it, a handle would scale with the slide and a
zoomed-out deck would have grab targets too small to hit.

## Keyboard

**Blazor**: complete, through `beforeinput` (so IME, dictation and paste work). Arrows, Home/End, Tab,
Escape, Delete and Ctrl/Cmd+B/I/U/Z are wired.

**MAUI**: typing works via a hidden `Entry`. **Physical keys do not** — MAUI has no portable key-down
event. Route them with `editor.HandleKey(EditorKey.Left, shift: true)` from a platform hook;
`EditorKey.Tab` carries the nesting.

## Find

**Home ▸ Find** — the same `OfficeFindBar` the document editor carries, over the same
`IFindController`. See `document-editor.md` ▸ **Find** for the API and the rules it shares.

```csharp
var find = c.Find;                  // SlideFinder : IFindController

find.Query = "roadmap";             // searches the whole deck and steps onto the first hit
find.FindNext();
find.Status;                        // "2/5"
find.Matches;                       // IReadOnlyList<SlideFindMatch>
```

Deck-specific behaviour:

- A search spans **every slide**, not the one being shown.
- Stepping onto a match opens its slide, selects the shape, puts the caret in its text and selects the
  matched word. If the deck is in `SlideViewMode.Grid` it switches back to `Single` first — a
  thumbnail has no caret to move.
- **Only `IsEditable` shapes are searched.** Layout and master shapes are template decoration shared
  by every slide using them; a hit inside one would count the company name once per slide and step the
  user into something they cannot select. Table cells and notes are out too — a `SlidePosition` is a
  shape, a paragraph and an offset, and neither has one.
- `FindMatchRects()` returns highlights for the **showing slide only**, in viewport coordinates.

## Slides: new, duplicate, delete, reorder

`SlideEditorView` has a **Home ▸ Slides** group (New slide, Duplicate, Delete, Earlier, Later). The
controller API, all undoable:

```csharp
var c = view.Controller!;          // SlideEditorView.Controller on both hosts

c.NewSlide();                      // inserts after the current slide and navigates to it
c.DuplicateSlide();                // copy goes to Index + 1
c.DeleteSlide();                   // or DeleteSlide(index) - NEVER confirms
c.MoveSlide(from: 3, to: 0);       // `to` is counted with the slide already removed
c.MoveSlideEarlier();  c.MoveSlideLater();

bool ok = c.CanDeleteSlide && c.CanMoveSlideEarlier && c.CanMoveSlideLater;
```

Or through the commands: `new NewSlideCommand(at, layoutOf)`, `new DuplicateSlideCommand(i)`,
`new DeleteSlideCommand(i)`, `new MoveSlideCommand(from, to)` via `deck.Execute(...)`.

Rules to generate against:

- A new slide uses the current slide's layout; after a **title** slide it uses the master's
  "Title and Content" (`obj`) layout. Its placeholders are empty and `SlideShape.Prompt` carries the
  "Click to add title/text/subtitle" body the **editor** paints (the viewer never does). Don't put
  prompt text into the slide yourself — it would become real content.
- **Delete confirmation lives in the view, not the controller.** `SlideEditorView` confirms by default
  (`ConfirmSlideDelete = true`); supply `ConfirmDeleteSlide = Func<int, Task<bool>>` to use the app's
  `IDialogService.Confirm`. If you build your own toolbar over `SlideEditor`, confirm before calling
  `DeleteSlide`. `RequestDeleteSlide()` (Blazor) / `RequestDeleteSlideAsync()` (MAUI) runs the view's flow.
- Every structural change (including undo/redo) raises `SlideDeck.SlidesChanged` with a `Focus` index,
  **clears the shape selection** and moves `Index` there — don't hold a shape index across one.
- Undo of a delete works across saves: deleted parts stay in the live package and are stripped only
  from the saved copy. Sections (`p14:sectionLst`) and custom shows are kept consistent.
- Keyboard: Ctrl+M = New slide on Blazor. MAUI has no key for it (`EditorKey` has none).

## Shapes: nudge, arrange, clipboard, groups, table cells

```csharp
c.Nudge(1, 0);                 // arrows while a SHAPE (not text) is selected; fine: true for 1px
c.Arrange(ShapeZOrder.BringToFront);   // BringToFront/BringForward/SendBackward/SendToBack
c.CopyShape(); c.CutShape(); c.Paste(); c.DuplicateShape();
c.Clipboard;                   // per controller - assign it to another editor to share
```

- Clipboard holds shapes, not text, and carries referenced parts (images) — pastes across decks work.
- Groups: `SlideShape.IsGroup` is the group's own entry (listed after its children); `IsInGroup` marks
  children. A click selects the group; `PointerDoubleClick` enters it (`IsInsideGroup`). Moves are
  written in the group's child units automatically.
- Tables: `PointerDoubleClick` on a cell (or `BeginTextEditing(x, y)`) sets `ActiveCell`; all text
  commands then target that cell; `HandleTab` walks cells. Tables can be moved/resized.
- MAUI keys: `EditorKey.Copy/Cut/Paste/Duplicate/NewSlide/BringForward/SendBackward` through `HandleKey`.

## Layouts, notes, rail

```csharp
var layouts = c.Layouts;       // SlideLayoutOption(Name, Index, IsCurrent)
c.SetLayout(layouts[1]);       // re-lays placeholders; undoable
c.NewSlide(layouts[1]);

c.Notes; c.SetNotes("...");    // creates notes page + notes master if missing; merges into one undo step
```

`SlideEditorView`: `ShowSlideRail` (default true, hidden < 600px), `ShowNotes` (default false, two-way).
`SlideRail` is a standalone control on both hosts (`Controller` = the editor's controller); its logic is
`SlideRailController` (tap = open slide, drag = `MoveSlide`).

## Not implemented

- **Soft line breaks** (`a:br`) — read and rendered, but they contribute no characters to the offset
  space, so the caret cannot sit on one and Shift+Enter does not insert one.
- **Rotation** handles — a rotated shape renders rotated and can be moved/resized, but the drag works
  in unrotated slide coordinates.
- Everything the viewer does not render — see `document-viewer.md`.

## Saving

An unedited deck saves **byte-identical**. After an edit, the parts the reader materialised (every
slide, layout, master, theme and notes part) are re-serialised by the SDK's only public flush — nothing
is lost and nothing is added or removed, but their bytes move.

### Dark mode

`Theme` is nullable; **leave it unset** to follow the host's light/dark scheme. `SlideTheme.Dark`
darkens only the surround — a slide is an authored artboard and is never inverted.

### Toolbar

The bar is a [Ribbon](ribbon.md) on both hosts — titled groups, with undo/redo in the quick access
row. You do not build any of it; it is what the control renders.

Do **not** hand-roll a formatting strip beside this control. Use `ToolbarContent` (Blazor) /
`ToolbarItems` (MAUI) to add your own commands — they land in their own group that never collapses.

The tab strip is off by default; turn it on only when the editor is the whole application. Below
600px the bar switches itself to `Simplified` — no code needed.

## Toolbar

Two ribbon tabs: **Home** (Slide nav, Font, Paragraph) and **Insert** (text box, shape, table, picture,
delete). Do not add Layout or Zoom tabs - a slide is a fixed artboard always scaled to fit, so it is
never clipped and there is nothing to pan or zoom.

Inserting a picture goes through the same shared path as the document editor: camera/gallery on
iOS and Android, a filtered native file dialog on every desktop head.

Shapes are the shared **Shapes ribbon tab**, same as the document editor. `OfficeInsertMenu` takes
`ShowShapes="false"` wherever that tab is present.

`Watermark` (an `OfficeWatermark`) is on both the editor and the viewer - a picture drawn behind the
content, defaulting to a 0.15 wash. It is a **display** watermark: drawn, never written into the file,
because the three formats store one in three unrelated ways. The editors' watermark button uses the
same picker as inserting a picture.
