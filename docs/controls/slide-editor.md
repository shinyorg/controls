# Slide Editor

[← All Shiny Controls](../../README.md)

> Same packages as the viewers. Two controls: `SlideEditor` is the lone editing surface;
> `SlideEditorView` is the same thing in PowerPoint's window — title bar, ribbon, status bar and File
> backstage ([the Office shell](office-shell.md)), on by default.

```csharp
using var deck = await SlideDeck.OpenAsync("deck.pptx", editable: true);
```

```razor
<div style="height:560px">
    <SlideEditorView Deck="deck" @bind-SlideIndex="index" DeckChanged="OnChanged" />
</div>
```

```xml
<office:SlideEditorView Deck="{Binding Deck}" />
```

**Two gestures carry the whole design.** A single click selects a shape and draws a **dashed** frame
with eight resize handles — drag the body to move it, a handle to resize it. A double-click puts a
caret inside that shape's text and the frame turns **solid**. That is the split PowerPoint uses, and
the frame is the only thing telling a user where their next keystroke will go.

Only shapes the slide itself owns can be selected. Ones painted from the layout or master are skipped,
because they belong to every slide using that layout — letting a click grab one would drag the company
logo off the whole deck at once.

Edits are surgical on the DrawingML runs, the same way the document editor treats Word's: a run is
split only where an edit needs a boundary and never re-created, so the language, hyperlinks and
theme-derived fills it carries survive a formatting change. A whole drag is **one** undo step, not one
per pointer sample.

The toolbar draws from the **same icon set** as the document editor — see above — adding `Previous`,
`Next`, `SlideShow`, `NewSlide`, `Duplicate`, `MoveSlideEarlier`, `MoveSlideLater`, `BulletList`, `NumberedList`, `Indent`, `Outdent`, `TextBox` and `Delete`, and
takes the same `ShowToolbarTooltips`.

| | Blazor | MAUI |
|---|---|---|
| Select, move, resize, double-click into text | ✅ | ✅ |
| Typing, IME, dictation, paste | ✅ via `beforeinput` | ✅ via a hidden `Entry` |
| Physical keys (arrows, shortcuts) | ✅ | ⚠️ route through `HandleKey` — MAUI has no portable key-down event |

**Bullets and numbers work like the document editor's**, through PowerPoint's own mechanism. The two
toggle buttons write `a:buChar`, `a:buAutoNum` or `a:buNone` into the paragraph's properties, and
typing `- ` or `1. ` at the start of a paragraph does the same — the same detector the Word side uses,
so the two cannot drift. `ListStyle.None` is written **explicitly**: a body placeholder inherits its
bullet from the master, so leaving the element out puts that bullet back instead of taking it away.

Auto-numbered paragraphs show a **real number**. It is a function of the paragraph's position at its
outline level within the shape, counted per text body — two bulleted placeholders on one slide each
start at 1 — and rendered in whatever scheme the file asks for, arabic, alphabetic or roman, with a
period, a trailing paren or both.

<kbd>Tab</kbd> and <kbd>Shift</kbd>+<kbd>Tab</kbd> move outline level, which is what makes a bullet
nest; unlike the document editor there is no "not in a list" case, because every paragraph in a shape
carries a level whether or not it draws a mark. A selection spanning two levels moves each paragraph
relative to its own. Nine levels, and no more — a tenth is a file PowerPoint will not open.

```csharp
c.ToggleBulletList();          // or ToggleNumberedList()
c.SetListStyle(ListStyle.Numbered);
c.ShiftLevel(1);               // nest; -1 to un-nest
c.HandleTab(shift: false);

c.CaretFormat.List;            // ListStyle.None / Bullet / Numbered
c.CaretFormat.Level;           // 0-8
```

**Formatting with nothing selected** follows PowerPoint. With the caret inside a word, Bold, colour and
the other font commands format that word. Anywhere else the change goes on the paragraph's end mark and
applies to what you type next.

**Shapes, pictures and tables** come from the same galleries the document editor offers, placed in
slide coordinates and selected on arrival so the next gesture is a drag of the new object:

```csharp
c.AddShape(ShapeGeometry.Hexagon, x, y, width: 240, height: 180);
c.AddPicture(bytes, "image/png", x, y, width: 400);
c.AddTable(rows: 3, columns: 4, x, y, width: 480, height: 200);
```

`AddShape` writes a real drawn shape rather than a text box, which is what makes PowerPoint give it
the theme fill. A table is a `p:graphicFrame` with a built-in table style, not a shape.

**Dragging an image file onto a slide** drops it centred on the pointer, sized to at most half the
slide — same platforms, same rejections, same `DropRejected` event as the document editor.

**Highlighting** uses the same palette as the document side. `a:highlight` holds a real colour, so
nothing is approximated here.

<kbd>Shift</kbd>+<kbd>Enter</kbd> writes a soft line break (`a:br`) — a new line inside the same
paragraph, so it keeps the bullet. A selected shape shows a **rotation handle** above its top edge; drag
it to rotate, holding <kbd>Shift</kbd> to snap to 15°. See [PowerPoint feature set](#powerpoint-feature-set).

## Nudging, arranging and the shape clipboard

With a shape selected (not its text), the **arrow keys nudge it** 8 slide pixels, or 1 with
<kbd>Alt</kbd>/<kbd>Ctrl</kbd>; a run of nudges is one undo step, like a drag. With nothing selected
they still page through the deck. On MAUI route them through `HandleKey`.

**Insert ▸ Clipboard** — Paste, Cut, Copy, Duplicate — and **Insert ▸ Arrange** — To front, Forward,
Backward, To back — act on the selected shape. Keys on Blazor: <kbd>Ctrl</kbd>+<kbd>C</kbd>/<kbd>X</kbd>/
<kbd>V</kbd>/<kbd>D</kbd>, <kbd>Ctrl</kbd>+<kbd>]</kbd> / <kbd>[</kbd> (with <kbd>Shift</kbd> for all the
way); on MAUI the new `EditorKey.Copy`, `Cut`, `Paste`, `Duplicate`, `BringForward`, `SendBackward`.

```csharp
c.Nudge(dx: 1, dy: 0, fine: false);
c.CopyShape(); c.CutShape(); c.Paste(); c.DuplicateShape();
c.BringToFront(); c.BringForward(); c.SendBackward(); c.SendToBack();   // or Arrange(ShapeZOrder)
```

A copied picture brings its image with it, to another slide or **another deck**: the clip keeps the
parts the shape refers to and pasting relates the target slide to them (copying across packages).
Pasting onto the slide it came from cascades each copy 16px down and right; every pasted drawing gets a
fresh id. The clipboard is **per controller** (`Controller.Clipboard`) — on Blazor Server a static one
would paste one user's shapes into another's deck; share one between editors by assigning it. It is not
the system clipboard.

## Groups and table cells

A **group** now reads through its own coordinate space (`chOff`/`chExt` → `off`/`ext`), so a scaled
group draws where PowerPoint draws it. A click selects the whole group — move or resize it and its
children come along; a **double-click goes into it** and selects the child under the pointer, and a
further double-click edits that child's text. Moves are written back in the group's units. Delete and
undo put a child back inside its group.

A **table** can be moved and resized (its `p:xfrm` was never written before, so this silently did
nothing). **Double-click a cell to type in it**: every text command — typing, Enter, Backspace,
formatting, bullets — works inside the cell, and <kbd>Tab</kbd>/<kbd>Shift</kbd>+<kbd>Tab</kbd> walks
the cells. `Controller.ActiveCell` says which; `SlidePosition.Cell` carries it. Cells are read from their
own `a:txBody` now, not a copy, which is what lets an edit reach the file.

## Layouts

**Home ▸ Slides ▸ Layout** lists the master's layouts, the current one checked. Picking one re-lays
the slide: placeholders are matched by index then type and take the new layout's position; one with
content and nowhere to go is pinned where it was drawn; an empty one without a match goes; the new
layout's unmatched placeholders arrive empty with their prompts. **New slide with layout** adds one.

```csharp
foreach (var layout in c.Layouts) { /* Name, Index, IsCurrent */ }
c.SetLayout(c.Layouts[1]);
c.NewSlide(c.Layouts[1]);
```

## Speaker notes

The **Notes** button in the status bar (or `ShowNotes`) opens a notes box under the slide. It writes through as you
type and a run of edits undoes as one step. A slide without a notes page gets one — and a deck without a
notes master gets one, with a copy of the slide master's theme — so the result opens in PowerPoint.
`c.Notes` / `c.SetNotes(text)`. Notes are now read from the notes page's **body placeholder**, one line
per paragraph, blank lines kept; they used to include the slide-number placeholder's text.

## The slide rail

A column of thumbnails left of the slide (`ShowSlideRail`, on by default, hidden below 600px wide).
Tap one to open it; **drag one to move it** — an insertion line shows where it will land, the rail
scrolls itself near either edge, and dropping is one undoable `MoveSlide`. A small wobble is still a
tap. It follows the editor: a slide opened by the arrows, a search or New slide scrolls into view. The
behaviour is the shared `SlideRailController`; `SlideRail` is the control on both hosts.

## Adding, removing and reordering slides

**Home ▸ Slides** — New slide, Duplicate, Delete, Earlier and Later. All five are undoable, and all of
them are on the controller for a host building its own chrome:

```csharp
c.NewSlide();            // after the current slide, and opens it
c.DuplicateSlide();      // the copy goes straight after the original
c.DeleteSlide();         // never asks - see below
c.MoveSlideEarlier();    // or MoveSlideLater(), or MoveSlide(from, to)

c.CanDeleteSlide; c.CanMoveSlideEarlier; c.CanMoveSlideLater;
```

**New slide takes the current slide's layout**, except after a title slide, where it takes the
master's "Title and Content" layout instead — PowerPoint's rule. It arrives with the layout's
placeholders, empty, each showing its prompt (**Click to add title**, **Click to add text**) in a
dashed outline, laid out in the placeholder's own size, font and bullet so the prompt is exactly where
the first keystroke will land. The prompts are editor chrome: the viewer and a slide show never draw
them, and the one the caret is inside disappears. Date, footer and slide-number placeholders are left
off, as PowerPoint leaves them off. <kbd>Ctrl</kbd>+<kbd>M</kbd> adds a slide on Blazor.

**Duplicate** copies the slide's XML and shares its pictures, charts and media with the original — two
relationships to one part, the way PowerPoint stores a picture pasted twice — but gives the copy its
own notes page, since a notes page points back at its slide.

**Delete asks first.** Both toolbars confirm (*Delete slide 3? "Roadmap" and everything on it will be
removed from the deck. You can undo this.*) with Cancel focused. Turn it off with
`ConfirmSlideDelete="false"`, or put the app's own dialog in its place:

```razor
<SlideEditorView Deck="deck" ConfirmDeleteSlide="@(i => Dialogs.Confirm("Delete slide?", $"Slide {i + 1} will be removed.", "Delete", "Cancel"))" />
```

```csharp
view.ConfirmDeleteSlide = i => dialogs.Confirm("Delete slide?", $"Slide {i + 1} will be removed.", "Delete", "Cancel");
```

On MAUI the default is the page's alert; with no page to show it on, nothing is deleted.
`SlideEditorController.DeleteSlide` itself never asks.

**Undo brings back the same slide, even after a save.** A deleted slide's part stays in the live
package, out of the running order, and is stripped from the *saved copy* only — so undo restores the
very slide, notes and pictures included, not a reconstruction of it.

**Sections are kept consistent.** A deck with sections lists every slide in exactly one of them, in
running order, and PowerPoint repairs a file that does not. A new or moved slide joins the section of
the slide it now follows, a deleted one leaves its section, and a custom show loses its reference to a
deleted slide. Undo puts all of it back exactly.

Every structural change — including an undo — drops the shape selection and goes to the slide it
happened at; a selection is an index into the slide that was showing, and after a reorder it would name
a shape on a different one. `SlideDeck.SlidesChanged` reports these for a host keeping its own index.

Reorder by dragging in the slide rail (below), or with Earlier/Later.

## Playing the deck

**Home ▸ Slide ▸ Slide show** plays the deck full screen from the slide being edited. It is the same
show the viewer gives — the slide edge to edge on black, an auto-hiding presenter bar, click or tap to
advance with the left quarter going back, speaker notes on demand — because it *is* the viewer: the
editor hands a `SlideView` the deck it is already holding and lets that present it. The deck is one
shared object, so what plays is what you typed a moment ago. Nothing is saved or reloaded to get there.

```csharp
await view.StartPresentingAsync();   // Blazor
view.StartPresenting();              // MAUI
```

| Member | |
|---|---|
| `IsPresenting` | Read-only. A show is started by calling, never by assigning a parameter. |
| `StartPresentingAsync(int? from)` / `StartPresenting(int? from)` | From the slide being edited unless told otherwise. |
| `StopPresentingAsync()` / `StopPresenting()` | Ends it. A no-op when nothing is playing. |
| `PresentingChanged` | However the show ended — the Exit button, Escape, F11, the Android back gesture. |
| `ShowPresenterControls` | The auto-hiding bar. Default `true`; off for a kiosk or a second screen. |
| `KeepScreenOnWhilePresenting` | MAUI only, default `true`. |

From the **current** slide rather than from the top: while a deck is being built, a show is started to
see how the slide in front of you actually lands. Pass `0` for the run-through.

Starting one clears the selection first. A caret and eight drag handles are editing state, and left
standing they are what the editor paints the instant the show ends — over whichever slide the presenter
walked to, where the shape they belonged to is not. Ending one leaves the editor on the slide the show
ended on, with the focus back on the surface.

**`F5` plays from the beginning, `Shift`+`F5` from the current slide.** Blazor fixes `preventDefault`
at render time — one keystroke behind the handler that decides it — so a bound `F5` could reach the
browser and reload the page, unsaved deck and all. The editor therefore suppresses `F5` and its own
`Ctrl` combinations synchronously in `wwwroot/slideEditor.js` before .NET sees them. On MAUI the keys
arrive as `SlideShortcut.ShowFromBeginning` / `ShowFromCurrent` through `SlideEditor.HandleShortcut`.

`StartPresentingAsync(from, presenterView: true)` / `StartPresenting(from, presenterView: true)` opens
**presenter view**: the current slide beside the next one, the notes, an elapsed timer with pause, and
black/white screen buttons. The show honours transitions, animations, hidden slides and hyperlinks.

## Dark mode

`Theme` is nullable and **unset means follow the host** — the app's light/dark appearance on MAUI,
the page's `color-scheme` on Blazor — and it keeps up live when that flips. Pass `SlideTheme.Light`
or `SlideTheme.Dark` only to pin one regardless of the app around it. See
[Styling & theming](styling.md#dark-mode).

A **pinned** theme on Blazor carries the chrome with it: the view's root takes the matching
`shiny-theme-dark` / `shiny-theme-light` scoping class (read off the pinned theme's colours), so the
ribbon, its pickers and the rest of the bar re-derive their `--shiny-color-*` tokens to match the
canvas. Unpinning removes it.

## The PowerPoint window

`SlideEditorView` is PowerPoint's whole window, not a canvas with a toolbar. It wraps itself in the
[Office shell](office-shell.md) dressed as PowerPoint (red `#C43E1C`), and that is **on by default**:

| Part | What it does |
|---|---|
| Title bar | AutoSave, Save / Undo / Redo and **Start From Beginning** (F5) in quick access, the presentation name (click to rename), the save status ("Unsaved changes" → "Saving…" → "Saved" / "Saved locally"), the command search and the account (`UserName`) |
| Command search | Every ribbon command with its shortcut (tooltips now carry `Shortcut`, not "(Ctrl+B)" in the text), plus Save, New Presentation, Export to PDF / slide as PNG / all slides as PNG, Print, Reading View, Fit, Zoom 100%. On Blazor, where the ribbon indexes a tab only once it has rendered, the commands of the unopened tabs are listed up front — New Slide, Text Box, Chart, Link, Header & Footer, Themes, Format Background, Slide Size, Transitions, Animations, Animation Pane, From Beginning / Current Slide, Presenter View, Hide Slide, the views, Notes, Ruler, Gridlines, Guides — and each steps aside once its tab has been visited. A query that matches no command is found across the slides (Find) and the hit is selected |
| Ribbon | **File** opens the backstage (and still raises `FileMenuRequested`). The **Editing / Viewing** menu (Viewing makes the deck read-only; Reviewing edits as Editing — slides have no tracked changes) and **Share** sit at the right end of the tab strip. There is no Comments button: the slide editor has no comments engine, and the notes toggle is on the status bar. Slide Show gains an **Export** group — **PDF** and **Pictures** (this slide as PNG / JPEG, all slides as PNG in a .zip) — on both hosts |
| Status bar | **"Slide 3 of 12"** ("Slide Master" in that view), the proofing language, **Notes** (toggles `ShowNotes`), the three views — **Normal**, **Slide Sorter**, **Reading View** (plays the show from the current slide; the button stays pressed while it runs) — the zoom slider, two-way with `Zoom` (10–400%, following the fitted zoom when `Zoom` is null), and **Fit slide to current window**, pressed while the zoom is fitted |
| Backstage | New (Blank presentation plus **Project update** on Slate, **Pitch deck** on Midnight and **Lesson** on Botanical — built in code by `SlideTemplates`, each a real .pptx with PowerPoint's five layouts; Blazor draws each one's title slide as its thumbnail), Open, Info (slides, hidden slides, words, slides with notes, author), Save, Save As (pptx, PDF, PNG), Print (with a preview of the current slide), Export (PDF, PNG of this slide, PNG of every slide as a .zip, JPEG) |

**Files.** The shell reads and writes nothing itself. Save (title bar, backstage, Ctrl+S), Save As,
Export (backstage or ribbon) and Print each raise `FileRequested` with a `SlideFileRequest` — the deck,
the current slide, the format, a file name ("Pitch deck.pdf"), the action, and `WriteToAsync(stream)` /
`ToBytesAsync()` (`SlideExport` does the writing: pptx through `SaveToAsync`, the rest through
`SlideExporter`). On Blazor an unhandled request downloads the file and Print opens the PDF in the
browser's print dialog, as Word and Excel do; on MAUI handle it:

```csharp
slides.FileRequested += async (_, request) =>
{
    var path = Path.Combine(FileSystem.AppDataDirectory, request.FileName);
    await using var file = File.Create(path);
    await request.WriteToAsync(file);
};
```

```razor
<SlideEditorView Deck="deck"
                 DeckReplaced="d => deck = d"
                 @bind-Zoom="zoom"
                 DocumentName="Quarterly Review"
                 UserName="Allan Ritchie"
                 RecentFiles="recent"
                 FileRequested="SaveAsync" />
```

Picking a template opens it, shows it and reports it through `DeckReplaced` (both hosts; the host owns
that deck from then on); handle `TemplateSelected` to do it yourself. `OpenRequested`,
`RecentFileSelected` and `ShareRequested` are the host's. With `AutoSave` on and `FileRequested`
handled, edits are saved two seconds after the last one. `Commands` (Blazor) / `CommandIndex` (MAUI) is
the `OfficeCommandIndex` behind the search, for the app's own entries. MAUI also exposes the parts —
`Shell`, `TitleBar`, `StatusBar`, `Backstage`, `RibbonActions`, `Ribbon` — and the seams `Save()`,
`Export(format)`, `SelectViewMode(id)` and `SearchSlides(text)`.

**Turning parts off.** `ShowShell="false"` drops the shell and gives the ribbon + slide + plain status
line of before (File then appears only while `FileMenuRequested` is handled). Short of that,
`ShowTitleBar`, `ShowStatusBar` (`ShowStatus` off hides either status bar), `ShowBackstage` (File then
only raises `FileMenuRequested`) and `ShowRibbonActions`. MAUI builds every part in the constructor and
only toggles `IsVisible`, so the AppKit head renders it.

> **Behaviour change:** the shell is on by default, so an existing `SlideEditorView` grows a title bar,
> the Office status bar and a backstage, and its ribbon loses the accent-filled header (the accent moves
> to the title bar and the tab underline). `FileMenuRequested` is still raised, after the backstage
> opens. A host that wants the old layout sets `ShowShell="false"`.

## The toolbar is a Ribbon

The formatting bar is a [Ribbon](ribbon.md) on both hosts, replacing the single scrolling strip of
icons it used to be. It carries PowerPoint's tabs — Home, Insert, Design, Transitions, Animations, Slide
Show and View, plus the contextual tabs — each group titled (see [The toolbar](#the-toolbar)).

Two things the strip could not do:

- **The ad-hoc dropdowns became real ribbon items.** Insert is a hosted menu component in its own group. That deleted a hand-written backdrop
  div, an absolutely-positioned panel and a `bool …Open` field per menu on Blazor, and an action sheet
  per menu on MAUI — along with their dismissal, keyboard and edge-flipping behaviour, which the
  ribbon already has.
- **Commands are grouped and captioned** instead of separated by anonymous hairlines.

Undo and redo sit outside the tabs, so they never move or disappear: in the window's title bar when
the shell is on (`ShowShell` with `ShowTitleBar`), in the ribbon's quick access row otherwise.

**The tab strip is on by default** now that the editor carries PowerPoint's full set of tabs
(`ShowRibbonTabs` on Blazor turns it off for a single-row bar). **File opens the backstage** while the
shell is on (see [The PowerPoint window](#the-powerpoint-window)); `FileMenuRequested` is raised
either way. With `ShowShell="false"` (or `ShowBackstage="false"`) File only raises `FileMenuRequested`,
and the ribbon shows it only when that event is handled.

**Below 600px wide the bar runs in `Simplified` mode** — one dense row, every item small, group titles
dropped. Group collapsing is the wrong answer at phone width: it folds groups into dropdowns
worst-first, which is right when a window is a little too narrow, but on a phone there is room for no
group at all and every command ends up behind a dropdown. See [Ribbon](ribbon.md).

## The toolbar

Organised the way PowerPoint's is: **Home** (Clipboard, Slides, Font, Paragraph, Drawing, Editing with
Find and Replace), **Insert** (Slides, Tables, Images, Illustrations — shapes, icons, charts — Links,
Text — text box, header & footer, date & time, slide number — Media), **Design** (themes, variants,
slide size, format background), **Transitions**, **Animations**, **Slide Show** and **View**. Contextual
tabs appear with the selection: **Shape Format**, **Table Design** / **Table Layout**, **Chart Design**,
and **Slide Master** while that view is open. Speaker notes and the view/zoom controls sit in the status
bar, where PowerPoint keeps them. Icons without a glyph in the shared set come from
`Icons/OfficeIcons.PowerPoint.cs` (`SlideIcon`).

## PowerPoint feature set

Everything below is on the shared `SlideEditorController`, so both hosts' ribbons are thin, and every
command is **one undo step** (a drag, a rotation, a nudge run included). A deck opened and saved without
an edit is byte-identical; everything written follows the schema's element order.

**Shape Format.** Fill (solid, gradient, picture, none), outline colour / weight / dash / none, shadow,
quick styles, exact size fields (with aspect lock), align and distribute relative to the slide or the
selection, rotate ±90°, flip, and the rotation handle.

```csharp
c.SetShapeFill(SlideFillSpec.Color(color)); c.SetShapeOutlineDash(LineDash.Dash); c.SetShapeShadow(true);
c.ApplyQuickStyle(style); c.SetShapeSize(320, null, lockAspect: true);
c.AlignToSlide = true; c.Align(ShapeAlignment.Center); c.Distribute(horizontally: true);
c.RotateBy(90); c.SetRotation(30); c.Flip(horizontal: true);
```

**Multi-select, groups, guides.** <kbd>Shift</kbd>/<kbd>Ctrl</kbd>+click adds to the selection, dragging
on empty slide draws a marquee, and moving several moves them together. **Group** writes a real
`p:grpSp` (<kbd>Ctrl</kbd>+<kbd>G</kbd>), **Ungroup** (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>G</kbd>)
takes it apart keeping each child where it was drawn. While dragging, **smart guides** snap to the
slide's centre and edges and to other shapes' edges and centres (`SmartGuides`, `SnapDistance`).

**Text.** Soft break, grow/shrink font (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>&gt;</kbd>/<kbd>&lt;</kbd>),
change case, clear formatting, superscript/subscript, character spacing, line spacing, text direction
(horizontal, rotated, stacked), vertical alignment and autofit (none / shrink text / resize shape).
Shortcuts: <kbd>Ctrl</kbd>+<kbd>B</kbd>/<kbd>I</kbd>/<kbd>U</kbd>, <kbd>Ctrl</kbd>+<kbd>E</kbd>/<kbd>L</kbd>/
<kbd>R</kbd>/<kbd>J</kbd>, <kbd>Ctrl</kbd>+<kbd>D</kbd>, <kbd>Ctrl</kbd>+<kbd>M</kbd>, <kbd>F5</kbd>,
<kbd>Shift</kbd>+<kbd>F5</kbd>. On MAUI they are `SlideShortcut` values through `SlideEditor.HandleShortcut`.

**Hyperlinks** on text runs and on whole shapes — a web address or another slide (`#slide=N`) —
`c.SetHyperlink(new SlideHyperlink("https://…"))` or `new SlideHyperlink(null, Slide: 3)`. A show follows them: web links open in the browser, slide
links jump.

**Design.** Seven built-in themes (`SlideThemeDefinition.BuiltIn`) each with colour **variants**;
applying one rewrites the theme part's colour scheme and major/minor fonts. Text follows the theme
because run formatting is resolved down the full OOXML chain — run → paragraph → the shape's list style
→ layout placeholder → master placeholder → master text styles (title/body/other) → the presentation's
default text style — and scheme colours (`tx1`, `bg1`, `tx2`…) go through the master's colour map and
any layout/slide `clrMapOvr`. Text nobody coloured is `tx1`, so a dark theme such as Midnight or a
**Dark** variant draws it light on the dark background, in the editor and the slideshow alike. **Slide size** 16:9, 4:3 or
custom (content scales with it). **Format background** — solid, gradient or picture, this slide or all.

**Transitions.** None, Fade, Push, Wipe, Split, Reveal, Cover, Zoom and Morph, with duration, direction,
**Apply to all**, advance on click and/or after N seconds, and **Preview** on the editing surface.
Written as PowerPoint writes them (`mc:AlternateContent` with the `p14` duration) and played in the show.

**Animations.** Entrance (Appear, Fade, Fly In, Wipe, Zoom), emphasis (Grow/Shrink, Spin, Pulse) and exit
(Disappear, Fade Out, Fly Out); trigger on click / with previous / after previous, duration and delay. The
**animation pane** lists the slide's sequence to reorder or remove, and numbered markers show on the
slide while the Animations tab is open. Stored in `p:timing` (with `p:bldLst`), round-tripped, and played
in the show. Deleting a shape prunes its animations.

**Slide Show tab.** From beginning, from current, presenter view, and **Hide slide** (`show="0"`,
skipped in a show, dimmed in the rail).

**Insert.** Charts — column, bar, line, pie — with a data-grid dialog, drawn by Skia and written as a
real `c:chartSpace` with its caches (no embedded workbook); icons; audio and video (a poster frame, played
by the show on tap); date/time, slide number, and a **Header & Footer** dialog for this slide or all.

**View tab.** Normal, **Outline** (edit titles and bullets as text), **Slide Sorter** (drag to reorder),
**Notes Page**, and **Slide Master** (edit the master's and layouts' text styles and backgrounds; saved
back to those parts). Zoom from 25% to 400% or fit, with <kbd>Ctrl</kbd>+wheel or pinch; ruler,
gridlines and guides.

**Sections** in the rail: add, rename, remove (with or without their slides), move, collapse — written
to `p14:sectionLst`.

**Export.** `ExportSlidePng(slide, width)`, `ExportSlidesPng(width)`, `ExportPdf()` (`SKDocument.CreatePdf`,
one page per visible slide). The painter is `SlideExporter` in `Shiny.Controls.Office.Skia`.

### Status for a host's status bar

| Member | Blazor | MAUI |
|---|---|---|
| Current slide (0-based) | `CurrentSlideIndex` | `CurrentSlideIndex` (read-only bindable) |
| Slide count | `SlideCount` | `SlideCount` (read-only bindable) |
| Zoom requested, `null` = fit | `@bind-Zoom` (`Zoom`/`ZoomChanged`) | `Zoom` (two-way) |
| Zoom in effect | `EffectiveZoom` | `EffectiveZoom` (read-only bindable) |
| Notes pane | `@bind-ShowNotes` | `ShowNotes` |
| View | `@bind-ViewMode` (`SlideEditorViewMode`) | `ViewMode` (two-way) |
| Any of the above changed | `StatusChanged` callback, `StatusUpdated` event | `StatusChanged` event |
| Presenting | `IsPresenting` | `IsPresenting` |

⚠️ Known limits: character spacing is saved but not yet drawn; MAUI touch carries no modifier state, so
multi-select there goes through `SlideEditor.ShiftHeld` / `ControlHeld` (set by a host's keyboard hook) or
the marquee; the Blazor show has no separate audience window (presenter view is a side panel); audio and
video play through the host (a browser element / the platform launcher) rather than inline on the slide.

## Find

**Home ▸ Find** — the same box, `3/12` readout and pair of arrows the document editor has, and the same
`IFindController` behind them. See [Document Editor ▸ Find](document-editor.md#find) for the walk, the
wrap and the keyboard.

What differs is what a hit *is*. A deck search spans **every slide**, and stepping onto a match opens
the slide it is on, selects the shape, puts the caret inside its text and selects the matched word —
without all four the arrows look like they did nothing. A hit found while the deck is showing as a
thumbnail grid switches back to the single-slide view first, because a thumbnail has no caret to move.

Only shapes you could edit are searched. The rest come from the slide's layout and master and are
template decoration shared by every slide using them, so a hit inside one would count the company name
once per slide and step the user into something they cannot select. Table cells and speaker notes are
out for the same reason the document editor skips table cells: a caret position on a slide is a shape,
a paragraph and an offset, and neither of those has one.

The amber wash is drawn on the slide being shown only. A match three slides away has no rectangle on
screen to draw — the readout is what says how many there are elsewhere.

```csharp
var find = editor.Controller!.Find;
find.Query = "roadmap";
find.FindNext();
```

## Inserting a picture

Same as the document editor. On iOS and Android the button asks — **Take Photo**, **Photo Library**,
**Browse Files** — with the camera offered only where the platform reports one. Every desktop head
opens its own file dialog filtered to exactly the formats a deck can embed. See
[Document Editor](document-editor.md#inserting-a-picture).

## Shapes are a tab, not a dropdown

The same **Shapes** tab the document editor has — Rectangles / Basic / Arrows, each button drawn as
the shape it inserts. One gallery, shared by both editors and both hosts. See
[Document Editor](document-editor.md#shapes-are-a-tab-not-a-dropdown).

## Accent

The bar wears PowerPoint red (`#C43E1C`) by default — see
[Document Editor ▸ Accent](document-editor.md#accent).

## Watermarks

`Watermark` draws a picture behind the content, on the viewer as well as the editor. The button picks
one through the same path as inserting a picture. See
[Document Editor ▸ Watermarks](document-editor.md#watermarks) — including why it is a display
watermark rather than one written into the file.
