# Document Editor (docx editing)

Two controls, on both hosts:

| Control | What it is |
|---|---|
| `DocumentEditor` | the lone editing surface — canvas, caret, selection, typing. No chrome. |
| `DocumentEditorView` | `DocumentEditor` plus a formatting toolbar |

Same packages as the viewers (`Shiny.Maui.Controls.Office` / `Shiny.Blazor.Controls.Office`), same two
constraints: **MAUI needs `UseShinyOffice()`** (it registers SkiaSharp, plus the AppKit canvas on `net10.0-macos`), **Blazor is WASM-only**, and on Blazor the container needs
an **explicit height**.

## Open a document for editing

`editable: true` is required — a read-only document throws on any edit.

```csharp
using var document = await WordDocument.OpenAsync("report.docx", editable: true);
```

### Blazor

```razor
<div style="height:520px">
    <DocumentEditorView Document="document" DocumentChanged="OnChanged" />
</div>

@* or the bare surface, with your own chrome: *@
<div style="height:520px">
    <DocumentEditor @ref="editor" Document="document" />
</div>
```

### MAUI

```xml
<office:DocumentEditorView x:Name="Editor" Document="{Binding Document}" />
<office:DocumentEditor x:Name="BareEditor" Document="{Binding Document}" />
```

## The toolbar is composed from what each host has

Both hosts now fill the same three slots with the same core controls — `FontPickerButton`,
`FontSizePickerButton` and `ColorPickerButton` exist on MAUI *and* Blazor. What differs is only the bar
around them:

- **MAUI** has **no toolbar control** — so `DocumentEditorView` builds a scrolling row of MAUI
  primitives and drops the pickers into it. Do not emit `shiny:ShinyToolbar` in XAML.
- **Blazor** composes `ShinyToolbar`, with the row inside it as its own flex container.

That flex container is load-bearing, not styling. `ShinyToolbar` renders `ChildContent` into a plain
block, so controls left inline align to the **text baseline** — which sits in a different place for a
button, a select and a colour swatch, and shows up as toolbar items a pixel or three out of line. Any
new Blazor toolbar built on `ShinyToolbar` should wrap its items the same way.

Scoped CSS bites here too: a rule written in one component's `.razor.css` cannot style a button
rendered by a **different** component, so shared toolbar buttons are styled from the row that owns them
with `::deep`.

## One icon set, no colour

Every plain button on the Word and PowerPoint toolbars — both hosts — draws from **one shared icon
set**: `OfficeIcons` in `Shiny.Controls.Office.Shared`, monochrome stroked artwork on a 24x24 grid at
one weight. MAUI paints it onto a `GraphicsView`; Blazor writes it out as inline SVG stroked in
`currentColor`. There is one definition of each mark, so the two hosts cannot drift.

**Do not put a glyph, a letter or an emoji on a toolbar button.** Emoji are painted in colour by the
font, at its own size and weight — they cannot be tinted, do not dim with a disabled button and look
different on every platform. Geometric unicode has the milder form of the same problem, plus tofu on
Android fonts that lack the character. If a new toolbar button needs a mark, add it to `OfficeIcon`
and `OfficeIcons.Shapes`.

The geometry is **commands, not an SVG path string**, on purpose. MAUI's `PathBuilder` has real gaps
parsing `d` attributes — implicit line-tos become move-tos, run-together decimals truncate — so
artwork authored as a path string can look perfect in a browser and draw a stump on a device with
nothing thrown. Neither host parses anything here.

The pickers are the **deliberate exception**: font, font size, text colour and the highlight swatch
have to show what they are currently set to, which is the one thing a monochrome icon cannot do. The
highlight split button keeps the shared `A`-over-a-bar artwork and tints only the bar.

## Icon-only buttons get a tooltip on desktop and web

Every button on these bars is icon only, so each is wrapped in Shiny's own `Tooltip` naming what it
does — the browser's `title` is slow to appear, cannot be themed and is unreachable from a keyboard.

- **Blazor**: on by default. `ShowToolbarTooltips="false"` falls back to the native `title`.
- **MAUI**: on for **desktop only** — Windows, Mac Catalyst, macOS and the GTK/plain-.NET head. Off on
  iOS and Android, because the tooltip opens on hover and there is no hover on a touch screen; a
  long-press tooltip would compete with the tap the button exists for. Override either way with
  `ShowToolbarTooltips`.

Both hosts always set an accessible name on the button (`aria-label` / `SemanticProperties.Description`)
whatever the tooltip setting is — a tooltip is not what a screen reader reads.

## Selecting what to format

Drag to select, **double-click for a word**, **triple-click for a paragraph**. On the controller these
are `SelectWordAt(position)` and `SelectParagraphAt(position)`, with `WordRangeAt(position)` if you
want the span without moving the selection; the word rule itself is `Text.WordBoundaries`, shared with
the slide editor so a double-click means the same thing in both.

Both hosts read the click count differently and it is worth knowing which: Blazor takes it from the
**click** event's `detail`, because `pointerdown` reports 0 and `mousedown` never fires at all — the
editor prevents pointerdown's default to keep focus on its hidden input. MAUI has no click count in
SkiaSharp's touch events, so `DocumentEditor` times consecutive presses itself.

## Formatting with nothing selected

`SetFontFamily`, `SetFontSize`, `SetTextColor`, `SetHighlight` and the Bold/Italic/Underline/Strike
toggles all work with a bare caret: the change is held and applied to the next `InsertText`, which is
what Word does. `CaretFormat` reflects it immediately so a toolbar can show it, the insert and the
format land as **one** undo step, and moving the caret off the spot abandons the choice. There is no
API for this — it is what the existing methods already do.

Exception (Word's rule): a bare caret strictly **inside** a word (letters/digits on both sides) formats
that whole word immediately — one undo step, the caret stays a caret. At a word edge or in whitespace
the change is held for typing as above.

Turning a toggle **off** writes an explicit `w:val="0"` (`w:u w:val="none"` for underline), so it
overrides formatting inherited from a style — un-bolding a Heading works. Do not "fix" this by
removing the element instead; that silently leaves style-bold text bold.

## Driving it

Everything lives on the shared controller, identical on both hosts:

```csharp
var c = editor.Controller;          // DocumentEditorController

c.InsertText("hello");
c.InsertParagraph();                 // Enter
c.DeleteBackward();                  // Backspace
c.Move(CaretMove.WordRight, extend: true);
c.SelectAll();

c.ToggleBold(); c.ToggleItalic(); c.ToggleUnderline(); c.ToggleStrikethrough();
c.SetFontFamily("Cambria");
c.SetFontSize(14);                   // points
c.SetTextColor(new ArgbColor(255, 0xC0, 0, 0));
c.SetHighlight(new ArgbColor(255, 255, 255, 0));   // null clears it
c.ToggleHighlight(new ArgbColor(255, 255, 255, 0));
c.SetAlignment(TextAlignment.Center);

c.ToggleBulletList(); c.ToggleNumberedList();
c.SetListStyle(ListStyle.Numbered);  // None / Bullet / Numbered, explicit rather than a toggle
c.ChangeListLevel(1);                // nest a list item; -1 un-nests
c.HandleTab(shift: false);           // what the Tab key does; returns false if it consumed nothing

c.Undo(); c.Redo();
c.CaretFormat;                       // what a toolbar should show as active
c.CaretFormat.List;                  // ListStyle at the caret; ListLevel is 0-8
c.Selection.Range;
```

### Page margins

The document's own margins, set for the whole document and undoable in one step:

```csharp
c.PageMargins;                                    // what it is set to now, in pixels at 96dpi
c.SetPageMargins(PageMargins.Narrow);             // Normal / Narrow / Moderate / Wide
c.SetPageMargins(PageMargins.FromInches(1, 1.25, 1, 1.25));   // left, top, right, bottom
c.SetPageMargins(left: 96, top: 96, right: 96, bottom: 96);   // pixels, keeps header/footer distances
```

`PageMargins` also carries `Header` and `Footer` — the distance from the page edge to the header and
footer, which sit **inside** the top and bottom margins rather than adding to them. `PageSetup.Margins`
reads them off an open document, `PageSetup.WithMargins` writes them back onto a copy.

`PageMarginPresets.All` is the gallery both toolbars offer (name, description, margins). Use it rather
than a list of your own — it is the one place the two hosts agree on what "Moderate" means.

**Both toolbars carry a page-margins button** — an action sheet on MAUI, a popover on Blazor, and the
preset the document already matches is marked.

Two things to know:

- Only `DocumentPageLayout.Print` can show it. A reflowed column has no paper, so it insets content by
  a cosmetic gutter instead. The change is still written and still saved — same as a page break.
- Margins are the **last section's**, matching how the geometry is read. Multi-section documents are
  not modelled.

### Highlighting

`w:highlight` takes a **name from a closed list**, not a colour, so a highlight is resolved to the
nearest one Word can express. `HighlightPalette` is that list, and it is what a picker should offer:

```csharp
foreach (var swatch in HighlightPalette.Swatches)   // Name, DisplayName, Color
    ...

HighlightPalette.NameOf(color);      // the w:highlight value, or "none" for null
HighlightPalette.ColorOf("yellow");  // the other way
```

Both toolbars already show a split highlight button over this palette — the same one on both hosts,
and the same palette the slide editor uses (there `a:highlight` holds a real colour, so nothing is
approximated).

### Lists

**Both toolbars carry two toggle buttons** — bulleted and numbered — plus indent and outdent. Use
`CaretFormat.List` to decide which is lit and `CaretFormat.ListLevel` to decide whether outdent is
enabled; the indent pair is enabled **only inside a list**, because that is the only thing it moves.

A Word paragraph does not carry its own bullet: it points at a definition in `numbering.xml`. So
`SetListStyle` on a document that has never had a list creates the part, a nine-level definition and
the instance behind it. The definition is stamped with a fixed `w:nsid`, so a second press finds the
first one's work instead of adding a near-identical abstract list every time — do not write your own
definitions to get a list, call the controller.

**Nesting.** `HandleTab` is the whole Tab story and hosts should route the key straight into it:

- In a list, Tab nests and Shift+Tab un-nests. A selection spanning levels moves each item relative to
  its own level rather than flattening them.
- Outside a list, Tab inserts a real `w:tab`, which is **four characters wide in the offset space** —
  matching what the reader projects. Shift+Tab outside a list does nothing and returns false.

**The numbered levels compound.** Level 1's `lvlText` is `%1%2.`, so the second level reads `1a`, `1b`
under item 1 and restarts at `1a` under item 2. Bullets cycle `•`, `◦`, `▪` by level. Each level
carries its own hanging indent — that is what the label is drawn *in*, so a level definition written
without one paints the bullet on top of the first letter.

**Autoformat.** Typing `-`, `*`, `+` or `•` then a space starts a bulleted list; digits closed by `.`
or `)` start a numbered one. The marker and the space both go, in one undo step. The marker has to be
everything before the caret, so a hyphen mid-sentence is untouched. `c.IsAutoFormatListEnabled = false`
turns it off — worth doing for a document of shell transcripts.

**Enter on an empty item ends the list**: a nested item comes out one level first, so repeated Enter
walks back up the nesting and then leaves. Enter on an item *with* text still makes a new item.

A list number is **not** stored on the paragraph that carries it — it is a function of every numbered
paragraph before it, so it is worked out in a pass over the whole block list after every edit. Two
consequences worth knowing:

- Formatting, typing in or undoing an edit inside a list item leaves its number alone. (This was not
  true before: the number was resolved once at read time from running counters that were then never
  rewound, so re-reading an edited paragraph handed it the number after the document's last one, and
  every further edit pushed it one higher.)
- Splitting an item, deleting one, or dropping a block in renumbers the rest of that list on its own.

Each `%n` in a compound `lvlText` renders in the format of the level it **refers to**, not the format
of the paragraph being labelled — that is what makes `%1%2.` come out as `1a.` rather than `11.`.

`ListLabel.Text` is what to draw, `ListLabel.IsBullet` says which of the two buttons the caret's
paragraph belongs to, and `ListLabel.Numbering` is the `numId`/level it came from.

## Shapes, pictures and tables

Everything the editor inserts is **inline** — a `wp:inline`, never a `wp:anchor`. The document view is
a reflow engine with no float layer, so an object behaves like a very large character: it wraps with
its line and moves as text is typed before it. There is no "behind text" or "square wrap".

```csharp
c.InsertShape(ShapeGeometry.Ellipse, width: 160, height: 120);
c.InsertShape(ShapeGeometry.RightArrow, fill: accent, outline: null, text: "Next");

c.InsertImage(bytes, "image/png", width: 240);        // height follows the ratio
c.InsertTable(rows: 3, columns: 4);                    // a block, after the caret's paragraph
```

`ShapeGeometry` lives in `Shiny.Controls.Office.Shapes` and is **shared with the slide editor** — the
same twenty presets, drawn by the same path builder.

An inline object counts as exactly **one character** for every caret purpose: one arrow key steps over
it, one backspace removes it, a selection that touches it takes all of it.

### Selecting and resizing

```csharp
c.ObjectAt(x, y);                    // DocumentPosition?, viewport coordinates
c.SelectObject(position);
c.SelectedInline;                    // InlineImage or InlineShape
c.SelectedObjectBounds();            // document coordinates
c.SelectedObjectHandles();           // the eight ShapeHandle rects
c.DeleteSelectedObject();

// the drag, if you are driving the pointer yourself
c.BeginObjectDrag(x, y);             // true when it took the gesture
c.DragObject(x, y);
c.EndObjectDrag();
```

Both `DocumentEditor` implementations already call these from their own pointer handling — a corner
handle keeps the aspect ratio, an edge handle changes one dimension, and the whole drag collapses to a
single undo step. An object cannot be dragged to a new *position*: it is in the text flow, and the
caret is what moves it.

## Dropping files in

Dragging an image file onto the editor inserts it at the drop point. On by default:

```razor
<DocumentEditorView Document="document" AllowFileDrop="true" DropRejected="OnRejected" />
```

```xml
<office:DocumentEditorView Document="{Binding Document}" />
```

```csharp
Editor.DropRejected += (_, e) => Toast(e.Reason);    // MAUI
```

`DropRejected` fires for a file that is too large (32MB) or not an image OOXML can store —
`ImageContentTypes.ByExtension` is the list, and SVG is deliberately not on it.

Where it works: **Blazor** everywhere, and on MAUI **Windows**, **iOS/iPadOS** and **Mac Catalyst**.
Android has no file drag from a file manager and the AppKit/GTK heads have no drop implementation
behind `DropGestureRecognizer`; on those the toolbar's picture button is the gesture. The drop
listener is attached to the canvas rather than the toolbar, so dropping onto Bold does nothing.

Saving is the same as everywhere else — and an unedited document still saves byte-identical:

```csharp
await document.SaveAsAsync("edited.docx");
```

## Keyboard input

**Blazor**: complete. Typing goes through `beforeinput`, so IME composition, autocorrect, dictation and
paste all work. Arrows, Home/End, Tab/Shift+Tab, Ctrl/Cmd+B/I/U, Ctrl/Cmd+Z and Shift+Ctrl/Cmd+Z are
wired. Tab's default must be prevented or the browser moves focus off the editor.

**MAUI**: typing works — a hidden `Entry` gives the platform keyboard and IME somewhere to send text.
**Physical keys do not**, because MAUI exposes no portable key-down event. Route them yourself:

```csharp
Editor.HandleKey(EditorKey.Left, shift: true);
Editor.HandleKey(EditorKey.Tab, shift: true);    // un-nest a list item
Editor.HandleKey(EditorKey.Undo, control: true);
```

A desktop host adds its own platform hook (`NSEvent` on macOS, `KeyDown` on Windows) and calls that.
Tapping, selection, typing and every toolbar command work without it.

## Find

**Home ▸ Find** on all three Office toolbars: a box, a `3/12` readout and previous/next arrows. One
component per host — `OfficeFindBar` — bound to an `IFindController`, which the document, slide and
spreadsheet finders all implement.

```csharp
var find = c.Find;                                 // DocumentFinder : IFindController

find.Options = new FindOptions { MatchCase = true, WholeWord = true };
find.Query = "revenue";                            // searches and steps onto the first hit

find.Count;                                        // how many
find.ActiveIndex;                                  // zero-based, -1 when none
find.Status;                                       // "1/4"; "0/0" for no hits; "" when not searching
find.Matches;                                      // IReadOnlyList<DocumentFindMatch>

find.FindNext();                                   // wraps at the end
find.FindPrevious();                               // wraps at the start
find.Clear();                                      // drops the query, leaves the selection alone
find.Changed += (_, _) => { };                     // query, options or match list changed
```

Rules that hold on all three hosts and all three editors:

- Setting `Query` **searches and steps onto the first hit at or after the caret** — not the top of the
  content. Do not follow it with a `FindNext()`; that skips to the second hit.
- Stepping **selects** the match rather than landing beside it, and scrolls it into view.
- Next and previous **wrap**. `FindNext()` returns false only when there are no matches at all.
- Editing invalidates the match list but **never moves the view**.
- Finding changes nothing, so leave it enabled in a read-only editor.

Every hit is washed amber by the painter; the one the selection covers is drawn as the selection
instead, so the current match is the one that looks different.

`MatchCase` and `WholeWord` live on the controller, not on the bar. Whole-word uses
`WordBoundaries.IsWordChar`, the same rule double-click selection uses, so `don` does not match
`don't`. `TextSearch.Matches(text, query, options)` is public if you need the same matcher elsewhere.

**Only paragraphs are searched.** Table cells are not: a `DocumentPosition` is a block and an offset,
and a table has neither — counting hits the arrows could never reach would be worse than not counting
them.

Wiring the bar by hand (it is what the three built-in toolbars host):

```xml
<office:OfficeFindBar Find="{Binding Controller.Find}" />
```

```razor
<OfficeFindBar Find="@editor?.Controller?.Find" Moved="StateHasChanged" />
```

## Spell check

Turned on by default, and on MAUI the checker is the **platform's own** — `UITextChecker` (iOS,
Mac Catalyst), `NSSpellChecker` (macOS AppKit), Android's text-services `SpellCheckerSession`, and the
Windows `ISpellChecker` COM API. Nothing has to be registered: referencing
`Shiny.Maui.Controls.Office` installs it. That matters because it is the *user's* dictionary — words
they taught the keyboard are already known, and **Add to dictionary** writes back to it.

**Blazor has no platform checker and defaults to none.** The browser spell-checks its own editable
elements and exposes neither the results nor the suggestions to script, and a canvas is not an
editable element anyway. Supply one:

```razor
<DocumentEditorView Document="document" SpellChecker="myChecker" SpellCheckEnabled="true" />
```

Misspellings get a red wavy underline; right-click (or long-press on touch) opens corrections plus
**Ignore** and **Add to dictionary**. Applying a correction is a single undo step.

### Supplying your own

Implement `ISpellChecker`, or derive from `SpellCheckerBase` which already handles the ignore list and
language defaulting — you write two methods:

```csharp
public sealed class MyChecker : SpellCheckerBase
{
    public override bool IsAvailable => true;

    protected override ValueTask<IReadOnlyList<SpellingError>> CheckCoreAsync(
        string text, string language, CancellationToken cancellationToken) => ...;

    protected override ValueTask<IReadOnlyList<string>> SuggestCoreAsync(
        string word, string language, CancellationToken cancellationToken) => ...;
}
```

Then either per control (`SpellChecker="..."` / `SpellChecker` bindable property) or globally:

```csharp
SpellCheckers.Default = new MyChecker();   // wins over the platform one
```

Set `SpellCheckers.Default` before the first editor is constructed. Registration uses
`SetDefaultIfUnset`, so an explicit choice is never overwritten.

`SpellingTokenizer` is public and worth reusing in a custom checker: it skips acronyms, camelCase,
numbers, URLs, email addresses and paths — the things every dictionary flags and no reader wants
underlined.

### Notes

- Checking is **per paragraph and cached on the paragraph's text**, and only the paragraphs on screen
  are checked. Scrolling re-checks nothing already seen; editing re-checks one paragraph.
- Calls are debounced (500 ms) — a platform checker is interop, and half-typed words are not errors.
- `IsAvailable` is false when there is no checker *or* no dictionary for the language. Check it before
  telling the user spelling is on.
- Set `SpellCheckEnabled`/`IsSpellCheckEnabled` to `false` to turn it off entirely.

## Not implemented

- Editing a table's *structure* once inserted — adding or removing rows and columns, merging cells.
  Typing in its cells works.
- **Floating (anchored) drawings.** They are read, and drawn in the text flow at the point they are
  anchored from rather than at their real position; the unsupported note says so. Nothing inserts one.
- A shape's own text is drawn but has no caret — pass it at insert time.
- Cut/copy/paste through the clipboard, and **replace** — find itself is implemented, see **Find**.
- **Grammar** checking. Android reports grammar errors and they are deliberately ignored — only
  `LooksLikeTypo` is treated as an error, so the behaviour matches the other three platforms.
- Inserting new paragraph styles.
- Setting the **paper size or orientation** — margins can be set, the sheet they sit on cannot.
- **Per-section** page setup. One geometry is read for the document and one is written back.
- Everything the viewer does not render is still not rendered — see `document-viewer.md`.

### Dark mode

`Theme` is nullable; **leave it unset** and the page and its chrome follow the host's light/dark
scheme live. Pass `DocumentTheme.Light` / `.Dark` only to pin one — a preview that must stay
paper-white, say. The `ToolbarBackground` / `ToolbarForeground` / `ToolbarBorder` parameters on
`DocumentEditorView` default to theme tokens; leave them unset too.

### Toolbar

The bar is a [Ribbon](ribbon.md) on both hosts — titled groups, with undo/redo in the quick access
row. You do not build any of it; it is what the control renders.

Do **not** hand-roll a formatting strip beside this control. Use `ToolbarContent` (Blazor) /
`ToolbarItems` (MAUI) to add your own commands — they land in their own group that never collapses.

The tab strip is off by default; turn it on only when the editor is the whole application. Below
600px the bar switches itself to `Simplified` — no code needed.

## Touch

Both editable surfaces read a pointer's kind and behave differently under a finger:

- **Spreadsheet** — tap selects a cell, drag **pans** both axes, and a selection is extended by
  dragging one of the two round handles on its corners. Header presses still select and resize.
- **Document editor** — tap places the caret, drag **pans**, double/triple-tap select a word or
  paragraph, and the selection is adjusted by the handles under each end. Long-press opens the
  spelling menu.

Mouse behaviour is unchanged (drag extends, wheel scrolls) and the handles are not drawn for it. Do
not add a separate pan gesture or a scroll control on top of this — it is already there.

## Toolbar and mobile behaviour

Two ribbon tabs: **Home** (Font, Paragraph, Proofing) and **Layout** (Page Setup, Insert, Zoom). Do not
add a tab per group - one-group tabs were removed on purpose.

Reading on a phone is covered by three things that already exist; do not reinvent them:
- one-finger drag pans **both** axes (touch); ctrl-wheel and sideways wheel on desktop
- pinch, or Layout ▸ Zoom (50-300% stops)
- Layout ▸ Fit width, which spans the page across the window (print layout only)

Spelling has three entry points: long-press/right-click menu, Home ▸ Proofing (toggle + prev/next,
which select the word and open its menu), and on MAUI a keyboard accessory bar offering the corrections
while the caret is in a misspelling (`ShowSpellingSuggestions`). `GoToNextSpellingErrorAsync` is async
because it has to spell-check each paragraph as it walks - the pass only covers what is on screen.

Inserting a picture on iOS/Android asks Take Photo / Photo Library / Browse Files; desktop goes
straight to the file browser. Hosts need the two iOS usage-description keys.

Shapes are a **Shapes ribbon tab** (`OfficeRibbonItems.ShapesTab` on MAUI, `<OfficeShapesTab>` on
Blazor), not a dropdown - do not reintroduce a picker panel. Margins are four preset buttons in
Layout ▸ Margins. Shape icons come from `ShapeIcons.For(geometry)`, built from the same maths the
painter uses; names from `ShapeNames`.

Four ribbon tabs: **Home** (Font, Paragraph, Proofing), **Layout** (Margins, Page, Zoom), **Insert**
(Objects, Header & Footer, Breaks) and **Shapes**. Header/footer/page-number/page-break/print-layout
are all on the bar - do not add separate host chrome for them. `ChromeText(header)` reads the current
line back for seeding an editor; `SetHeaderText(null)` removes it. Headers only render in print layout.

Layout ▸ Page has Portrait/Landscape toggles (`SetPageOrientation`, undoable, writes both the swapped
dimensions and `w:orient`). Each Office control has an `Accent` defaulting to its Microsoft colour -
Word blue, Excel green, PowerPoint red - which paints the ribbon header, tab ink and underline. Set
`Accent = null` to fall back to the theme. Use `OfficeAccent.From(colour)` for a custom one so the ink
is chosen for you. Multi-column layout is **not** supported.

`Watermark` (an `OfficeWatermark`) is on both the editor and the viewer - a picture drawn behind the
content, defaulting to a 0.15 wash. It is a **display** watermark: drawn, never written into the file,
because the three formats store one in three unrelated ways. The editors' watermark button uses the
same picker as inserting a picture.
