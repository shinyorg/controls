# Document Editor

[← All Shiny Controls](../../README.md)

> Same packages as the viewers. Two controls: `DocumentEditor` is the lone editing surface;
> `DocumentEditorView` is the same thing plus a formatting toolbar.

```csharp
using var document = await WordDocument.OpenAsync("report.docx", editable: true);
```

```razor
<div style="height:520px">
    <DocumentEditorView Document="document" DocumentChanged="OnChanged" />
</div>
```

```xml
<office:DocumentEditorView Document="{Binding Document}" />
```

Edits are surgical on the OOXML runs: a run is split only where an edit actually needs a boundary and
is never rebuilt, so the language, proofing state and revision marks a run carries survive a
formatting change. An unedited document still saves byte-identical.

**Both toolbars are built from the same pickers.** `FontPickerButton`, `FontSizePickerButton` and
`ColorPickerButton` now exist in the core package on both hosts, and both editors use all three — so
the family list previews each face in its own typeface, and the colour swatch opens the full spectrum
rather than the operating system's own dialog. What still differs is only the bar around them: Blazor
composes `ShinyToolbar`, MAUI has no such control and lays out a scrolling row itself.

**One icon set across both toolbars and both hosts.** Every plain button on the Word and PowerPoint
bars draws from a single monochrome stroked set defined once in `Shiny.Controls.Office.Shared`, on a
24x24 grid at one weight: MAUI paints it onto a `GraphicsView`, Blazor writes it out as inline SVG
stroked in `currentColor`. That replaced a mixture of styled letters, geometric unicode and emoji —
and the emoji were the reason it had to go rather than a matter of taste, since a font paints those in
its own colour, size and weight, so the picture and delete buttons could not be tinted, did not dim
with a disabled button and looked different on every platform. The geometry is stored as drawing
commands rather than an SVG path string, because MAUI's `PathBuilder` drops implicit line-tos and
truncates run-together decimals silently — artwork that looks perfect in a browser can draw a stump on
a device. The **pickers are the deliberate exception**: font, size, text colour and the highlight
swatch have to show what they are currently set to, which is the one thing a monochrome icon cannot
do, so the highlight split button keeps the shared `A`-over-a-bar mark and tints only the bar.

**Icon-only buttons carry a tooltip on desktop and web.** Each one is wrapped in Shiny's own `Tooltip`
naming what it does, rather than the browser's `title` — which is slow to appear, cannot be themed and
is unreachable from a keyboard. On Blazor that is on by default; on MAUI it is on for Windows, Mac
Catalyst, macOS and the GTK head and **off on iOS and Android**, because the tooltip opens on hover
and there is no hover on a touch screen — and a long-press tooltip would compete with the tap the
button exists for. `ShowToolbarTooltips` on `DocumentEditorView` and `SlideEditorView` overrides either
way; turning it off on Blazor falls back to the native `title`. The accessible name is set on the
button regardless, since a tooltip is not what a screen reader reads.

| | Blazor | MAUI |
|---|---|---|
| Typing, IME, dictation, paste | ✅ via `beforeinput` | ✅ via a hidden `Entry` |
| Click, drag-select, toolbar commands | ✅ | ✅ |
| Double-click/tap a word, triple a paragraph | ✅ | ✅ |
| Physical keys (arrows, shortcuts) | ✅ | ⚠️ route through `HandleKey` — MAUI has no portable key-down event |

**Selecting what to format.** Drag across text, **double-click a word**, or **triple-click a
paragraph** — the same gestures on MAUI, where the click count is timed from the taps because
SkiaSharp's touch events do not carry one. Slides get the word gesture too: the first double-click
puts a caret in a shape's text, and a second one selects the word under it. A word stops at
punctuation but keeps its apostrophes, so `don't` selects whole and `end.` does not take the full stop.

**Formatting with nothing selected applies to what you type next**, the way Word does: put the caret
somewhere, pick a font, size, colour or weight, and the next text carries it. The choice shows on the
toolbar while it is pending, is spent by the first insertion, undoes together with the characters it
formatted, and is abandoned if the caret moves off the spot where it was made — so a colour picked and
thought better of cannot resurface in something typed later. Slides do the same thing through
PowerPoint's own mechanism, the paragraph end mark.

**Bulleted and numbered lists, from the toolbar or by typing.** Two toggle buttons turn every
paragraph the selection touches into a bulleted or numbered item, and pressing the lit one again takes
them back out. A Word paragraph does not carry its own bullet — it points at a definition in
`numbering.xml` — so the first list in a document that has never had one creates the part, a
nine-level definition and the instance behind it; press the button again elsewhere and the same
definition is reused rather than a near-identical one being added each time.

**Tab nests, Shift+Tab un-nests.** With the caret in a list item, <kbd>Tab</kbd> moves it in one level
and <kbd>Shift</kbd>+<kbd>Tab</kbd> moves it out; a selection spanning several items moves each one
relative to its own level rather than flattening them. The numbered levels **compound**, so the second
level reads `1a`, `1b` under item 1 and restarts at `1a` under item 2 — the label says which item it
belongs to, which a bare `a` does not. Bullets change glyph per level (`•`, `◦`, `▪`, repeating), and
each level carries its own hanging indent so the label sits beside the text rather than on top of it.
Outside a list <kbd>Tab</kbd> is still a tab character; the toolbar's indent and outdent buttons are
enabled only inside one, because that is the only thing they move.

**Typing `- ` or `1. ` starts a list.** Autoformat fires on the space after the marker, removes both
the marker and the space, and does it in a single undo step so one <kbd>Ctrl</kbd>+<kbd>Z</kbd> puts
the typed characters back. `-`, `*`, `+` and `•` give a bulleted list; a run of digits closed by `.`
or `)` gives a numbered one. It is deliberately narrow — the marker has to be everything before the
caret, so a hyphen mid-sentence is a hyphen, and a lone letter never numbers a list. Set
`IsAutoFormatListEnabled = false` on the controller to turn it off.

**Enter on an empty list item ends the list**, rather than making another empty one: a nested item
comes out one level first, so repeated <kbd>Enter</kbd> walks back up the nesting and then leaves.

```csharp
var controller = editor.Controller!;

controller.ToggleBulletList();      // or ToggleNumberedList()
controller.SetListStyle(ListStyle.Numbered);
controller.ChangeListLevel(1);      // nest; -1 to un-nest
controller.HandleTab(shift: false); // what the Tab key does, wherever the caret is

controller.CaretFormat.List;        // ListStyle.None / Bullet / Numbered
controller.CaretFormat.ListLevel;   // 0-8
```

Lists in a document that already had them keep whatever `numbering.xml` says — glyphs, formats,
`lvlText` templates and start values included, with each placeholder in a compound template rendered
in the format of the level it refers to. The numbers themselves are a function of position, so
inserting or deleting an item renumbers the rest of its list, and undo puts the numbers back rather
than advancing them.

**Page margins are settable from both toolbars.** A page-margins button opens Word's own four presets
— Normal, Narrow, Moderate and Wide — as an action sheet on MAUI and a popover on Blazor, with the
preset the document already matches marked. `DocumentEditorController.SetPageMargins` takes a preset,
`PageMargins.FromInches(...)`, or four numbers, and the change is one undo step: the whole `w:pgMar`
element is captured before the write, so a document that never had one goes back to not having one and
anything else Word wrote there — a binding gutter, most of all — survives. Only the paginated
(`Print`) layout can show the result; a reflowed column has no paper to inset from, so the margins are
written and saved but have nowhere to appear until the view is showing pages, exactly like a page
break.

**Spell check uses the platform's own dictionary.** On MAUI nothing has to be registered — referencing
the package installs `UITextChecker` (iOS, Mac Catalyst), `NSSpellChecker` (macOS), Android's
text-services session, or the Windows `ISpellChecker` COM API. It is the *user's* dictionary, so words
they taught the keyboard are already known and **Add to dictionary** writes back to it. Misspellings
get a red wavy underline; right-click or long-press for corrections, Ignore and Add to dictionary, and
applying a correction is one undo step.

The browser has no equivalent API — it spell-checks its own editable elements and exposes neither
results nor suggestions to script — so **Blazor defaults to no checking** and takes one you supply.
Either way it is replaceable, per control or globally:

```razor
<DocumentEditorView Document="document" SpellChecker="myChecker" />
```

```csharp
SpellCheckers.Default = new MyChecker();   // derive from SpellCheckerBase; two methods
```

Checking is per paragraph, cached on the paragraph's text, limited to what is on screen and debounced,
so scrolling re-checks nothing and typing re-checks one paragraph.

**Shapes, pictures and tables insert inline.** Twenty preset geometries — the same
`ShapeGeometry` set the slide editor draws, through the same path builder — plus pictures and tables,
all from the toolbar:

```csharp
c.InsertShape(ShapeGeometry.Ellipse, width: 160, height: 120);
c.InsertImage(bytes, "image/png", width: 240);
c.InsertTable(rows: 3, columns: 4);
```

Inline means a `wp:inline`, never a `wp:anchor`: an object behaves like a very large character, wraps
with its line, and moves as text is typed before it. The document view is a reflow engine with no
float layer, so a floating shape could be written but never drawn where it claimed to be — anchored
drawings in an opened file are read and shown in the flow, with the unsupported note saying so.

Selecting one draws a frame with eight resize handles: a corner keeps the aspect ratio, an edge
changes one dimension, and the whole drag is **one** undo step. An inline object counts as exactly one
character, so an arrow key steps over it and a backspace takes all of it.

**Dragging an image file onto the editor inserts it at the drop point** — Blazor everywhere, and on
MAUI Windows, iOS/iPadOS and Mac Catalyst. Android has no file drag from a file manager and the
AppKit/GTK heads have no drop implementation behind `DropGestureRecognizer`; there the toolbar's
picture button is the gesture. `DropRejected` fires for a file over 32MB or in a format OOXML cannot
store.

**Highlighting** is a split button over a sixteen-swatch palette, shared with the slide editor.
Word's `w:highlight` takes a name from a closed list rather than a colour, so a highlight resolves to
the nearest one it can express; `HighlightPalette` is that list, and every swatch on offer round-trips
exactly.

## Dark mode

`Theme` is nullable and **unset means follow the host** — the app's light/dark appearance on MAUI,
the page's `color-scheme` on Blazor — and it keeps up live when that flips. Pass `DocumentTheme.Light`
or `DocumentTheme.Dark` only to pin one regardless of the app around it. See
[Styling & theming](styling.md#dark-mode).

A **pinned** theme on Blazor carries the chrome with it: the view's root takes the matching
`shiny-theme-dark` / `shiny-theme-light` scoping class (read off the pinned theme's colours), so the
ribbon, its pickers and the rest of the bar re-derive their `--shiny-color-*` tokens to match the
canvas. Unpinning removes it.

## The toolbar is a Ribbon

The formatting bar is a [Ribbon](ribbon.md) on both hosts, replacing the single scrolling strip of
icons it used to be. Font, Paragraph, Insert and Page, each titled.

Two things the strip could not do:

- **The ad-hoc dropdowns became real ribbon items.** Insert and page margins are hosted menu components in their own groups. That deleted a hand-written backdrop
  div, an absolutely-positioned panel and a `bool …Open` field per menu on Blazor, and an action sheet
  per menu on MAUI — along with their dismissal, keyboard and edge-flipping behaviour, which the
  ribbon already has.
- **Commands are grouped and captioned** instead of separated by anonymous hairlines.

Undo and redo sit in the ribbon's quick access row, outside the tabs, so they never move or disappear.

**The tab strip is off by default** (`ShowRibbonTabs`). This is a bar a host drops above a surface, not
an application's whole chrome, and a strip carrying a single "Home" is noise — the groups do the
organising. Turn it on when the editor *is* the application, and you get the tab strip and the
collapse chevron with it.

**Below 600px wide the bar runs in `Simplified` mode** — one dense row, every item small, group titles
dropped. Group collapsing is the wrong answer at phone width: it folds groups into dropdowns
worst-first, which is right when a window is a little too narrow, but on a phone there is room for no
group at all and every command ends up behind a dropdown. See [Ribbon](ribbon.md).

## Mouse and touch are not the same gesture

A mouse drag selects text; a finger has no wheel, so a drag has to pan or the page cannot be scrolled
at all. Under touch the editor takes the mobile convention: **tap** places the caret, **drag** pans the
page, **double-tap** selects a word and **triple-tap** a paragraph, and a selection is adjusted by
dragging the round **handles** drawn under each of its ends. Long-press still opens the spelling menu.

The caret is placed on the way *up* rather than the way down, because until the finger lifts there is
no telling a tap from the start of a pan.

Nothing changes for a mouse: drag still selects, shift-click still extends, and the handles are not
drawn at all — they would be two targets that do nothing a drag does not.

## The toolbar

Two tabs, not four. **Home** is what you do to the text under the caret — Font, Paragraph and Proofing.
**Layout** is what you do to the page it sits on — Page Setup, Insert and Zoom. Splitting further gave
Insert, Layout and Review one group each, which is a click to reach a bar with a single button on it.

Proofing rides on Home rather than a Review tab of its own: spelling is something you do while writing,
not a separate pass.

## Reading a document on a phone

A page is a fixed width — that is what makes it a page — so on a phone it is always wider than the
screen and the right-hand end of every line is off it. Three things address that, and they are meant to
be used together:

| | |
|---|---|
| **Pan** | A one-finger drag moves the page on **both** axes. Under touch a drag pans rather than selecting; see below |
| **Zoom** | Pinch, or the Layout tab's zoom controls, which step through 50 / 75 / 100 / 125 / 150 / 200 / 300%. `Zoom` is also a plain property |
| **Fit width** | Sets the zoom so the page exactly spans the window — the one-tap answer to "I cannot see the whole line". Print layout only; reflow already fits by construction |

On the desktop the wheel scrolls, a wheel with a sideways component pans, and **ctrl-wheel zooms** —
which is not a shortcut anyone had to learn, but what a trackpad pinch is delivered as in every browser.

## Spelling

The red underline is only half of it; the other half is reaching the suggestions. There are three ways
in, because the one that works depends on the device:

| | |
|---|---|
| **Long press** (touch) or **right-click** (desktop) | Opens the menu on the word under the pointer: suggestions, Ignore, Add to dictionary |
| **Home ▸ Proofing** | Turn the pass on or off, and step to the previous or next misspelling. Stepping selects the word and opens its menu, so the arrows are a complete review loop on their own |
| **Keyboard accessory** (MAUI, iOS and Android) | While the caret is inside a misspelling, the corrections appear on the bar above the keyboard, one tap from the finger already typing. `ShowSpellingSuggestions="false"` turns it off |

The accessory bar is the mobile answer, and it exists because a long press is not a gesture anyone
performs on a word they were not already suspicious of — without it the underlines on a phone were
decoration. It appears only while the caret is actually in a misspelling, so it costs nothing the rest
of the time, and it is the same `KeyboardAccessoryView` the rest of the library uses, so iOS gets a
real `InputAccessoryView` and Android gets a bar anchored above the IME.

Stepping through errors checks each paragraph as it reaches it. The pass itself only ever runs over
what is on screen — nothing off screen can show a squiggle, so checking a long document up front would
stall it for no benefit — which means a walk that trusted the cache would step through a document full
of misspellings and report that it had none.

## Find

**Home ▸ Find** carries a box, a `3/12` readout and a pair of arrows. Type into the box and the editor
steps onto the first hit at or after the caret and **selects** it; the arrows walk the rest.

| | |
|---|---|
| **Typing** | Searches as you type. The first hit is the one at or after the caret, not the top of the document — a find that always restarted at the beginning takes the user away from what they were reading |
| **Next / Previous** | Step through the hits, wrapping at either end. A "next" that stopped at the last one looks identical to one that has finished the document |
| **`3/12`** | Which hit you are on, one-based, out of how many there are. `0/0` means the query found nothing; an empty readout means nothing is being searched for |
| **Enter / Shift+Enter** | Next and previous, from the keyboard, without reaching for the arrow. **Escape** clears the search (Blazor) |

Every hit is washed amber; the one you are on is drawn as the **selection** instead, so the current
match is the one that looks different rather than the one that looks the same. The hit is selected
rather than merely scrolled to, because everything a person does after finding a word — restyle it,
delete it, type over it — operates on the word.

Only **paragraphs** are searched, which is the same content the caret can reach and the same content
the spelling pass walks. Text inside a table's cells is not counted: a document position is a block and
an offset, and a table has neither — a count that included those hits would promise something the
arrows could never step to.

Editing the document re-counts, but never moves the view. Invalidating the match list is not the same
as re-running the search: jumping somebody somewhere because the paragraph they are typing in gained a
hit is the last thing a find should do.

The state lives on the controller, so a host can drive it without the toolbar:

```csharp
var find = editor.Controller!.Find;

find.Options = new FindOptions { MatchCase = true, WholeWord = true };
find.Query = "revenue";

Console.WriteLine(find.Status);   // "1/4"
find.FindNext();
find.Clear();
```

`Find` implements `IFindController`, which the slide and spreadsheet finders implement too — which is
why one find bar per host serves all three Office editors. `MatchCase` and `WholeWord` are on the
controller rather than on the bar; whole-word uses the same rule double-click selection does, so
searching `don` does not match `don't`.

Finding changes nothing, so it stays live in a read-only editor — where it matters more, not less.

## Inserting a picture

A file browser is the right answer on a desktop, where a picture is a file in a folder. On a phone it
is the wrong one twice over: photos live in the gallery rather than the filesystem, and the picture
someone wants in a document is often one that does not exist yet — they mean to take it. So on iOS and
Android the button asks first: **Take Photo**, **Photo Library**, **Browse Files**. Camera is offered
only where the platform reports one, which correctly leaves it out on a simulator.

Mac Catalyst is deliberately not treated as mobile: it runs the iOS code but presents as a desktop,
where a Files browser is what a user reaches for.

An iOS host needs `NSCameraUsageDescription` and `NSPhotoLibraryUsageDescription` in its `Info.plist`.

## Shapes are a tab, not a dropdown

Twenty shapes behind one button is a panel large enough to cover the document it is about to draw on,
and it has to be dismissed before the result can be seen. They are a **Shapes** tab instead — grouped
Rectangles / Basic / Arrows — and every button is drawn as the shape it inserts.

Those icons are built with the same polygon, star and arrow maths the painter uses to lay the shape
into the document, at a smaller size. Hand-drawn ones drift from what gets inserted the first time
either side is adjusted, and a pentagon icon that yields a differently proportioned pentagon is a small
lie the user only catches after clicking.

The gallery and its names are shared by both editors and both hosts, so the four copies cannot drift.

## Margins are on the ribbon

Four presets — Normal, Narrow, Moderate, Wide — as four buttons in Layout ▸ Margins, rather than one
button that opens a sheet of four. Four is few enough to show, and the whole reason to have a ribbon is
that the choices are on it.

## Page chrome on the ribbon

Four tabs now: **Home** (Font · Paragraph · Proofing), **Layout** (Margins · Page · Zoom),
**Insert** (Objects · Header & Footer · Breaks) and **Shapes**.

| | Where | |
|---|---|---|
| **Header** / **Footer** | Insert ▸ Header & Footer | Prompts for the line, seeded with whatever is there. An empty line removes it — the only way back out of having one |
| **Page number** | Insert ▸ Header & Footer | A menu, not a button: header or footer × left, centre or right. It appends to a header already there rather than replacing it |
| **Page break** | Insert ▸ Breaks | Splits the page at the caret |
| **Print layout** | Layout ▸ Page | A toggle between sheets of paper and one continuous column. Pressed means print |

Header and footer are asked for rather than edited in place on the page. They are separate stories in
the document — their own parts, laid out per page and repeated — so editing them in the canvas means a
second caret, a second selection and a way in and out of them. Asking for the line is the whole of
what most documents need, and it is undoable like any other command.

Note that headers and footers only *show* in print layout: a reflowing view has no pages to attach
them to. Setting one in reflow still writes it, and the Layout toggle is next to the buttons that do.

## Orientation

Layout ▸ Page carries **Portrait** and **Landscape** — two toggles rather than one, because a page is
one of two things rather than on or off, and a lone "Landscape" button leaves the reader working out
its pressed state backwards.

Turning the paper does two things at once, and doing only one is the failure worth knowing about:
the dimensions swap **and** the section records `w:orient`. Swapping without the attribute gives a page
the right shape that Word still calls portrait, so Word's own control shows the wrong state and the
next change flips it the wrong way; writing the attribute without swapping gives a section claiming
landscape on portrait paper, which Word obeys by re-swapping on open. Margins are deliberately left
alone — Word keeps them when the page turns.

`SetPageOrientation` is undoable and survives a save and reopen.

## Accent

Each Office control wears a colour — its ribbon's header band, the tab ink and the underline:

| | |
|---|---|
| `SpreadsheetView` / `SpreadsheetToolbar` | `OfficeAccent.Spreadsheet` — Excel green `#107C41` |
| `DocumentEditorView` | `OfficeAccent.Document` — Word blue `#185ABD` |
| `SlideEditorView` | `OfficeAccent.Presentation` — PowerPoint red `#C43E1C` |

These are the defaults, not a sample setting. They are the colours Microsoft uses, and that is the
point: a user reads them as "spreadsheet" and "slides" before any label has been looked at, and a
workbook and a deck open side by side want telling apart rather than matching.

Set `Accent` to take on your own brand, or to `null` to leave the bar on the theme's neutrals like the
rest of the chrome. `OfficeAccent.From(colour)` picks the ink for you — deliberately, because a caller
choosing a brand colour is not thinking about whether their tab labels have gone invisible on it.

This is the one part of an Office control's appearance that is **not** taken from the app's theme.
Everything else — the grid, the page, the surround — follows the host's neutrals so the control sits on
the same ground as the chrome around it. See [Spreadsheet](spreadsheet.md#dark-mode).

## Watermarks

A picture drawn behind the content — a logo, a DRAFT stamp, a company mark. `Watermark` is on all six
controls: the three editors and the three **viewers**, so a document opened read-only shows its mark
too.

```csharp
view.Watermark = new OfficeWatermark
{
    Image = bytes,
    Opacity = 0.15,          // a wash: there is text to read through it
    Scale = 0.6,             // of the surface's shorter side
    RotationDegrees = 315,   // the diagonal a stamp goes on
    Fit = OfficeWatermarkFit.Contain
};
```

The editors carry a **Watermark** button — document in Layout ▸ Page, slide in Insert, spreadsheet in
Data ▸ Sheet. It picks a picture through exactly the same path as inserting one: camera or gallery on
iOS and Android, the platform's own image-filtered dialog on a desktop, a file input in the browser.
Once a mark is set the button clears it, because a picker that reopens on a document already stamped is
a dead end.

It is drawn per page in print layout and once behind the viewport in reflow — reflow has no pages, and
a mark that scrolled with the content would slide away and leave most of the document unmarked. It is
clipped to the surface it marks, since a rotated mark scaled to the page is wider than the page across
its diagonal.

**This is a display watermark: it is drawn, not written into the file.** That is a deliberate limit.
The three formats have no common notion of one — Word keeps a VML shape in the header part, Excel has
no watermark at all and fakes it with a header-and-footer image, PowerPoint expects a picture on the
slide master. Persisting to all three means three unrelated mechanisms; drawing on all three means one.
So it is right for stamping a preview, marking a draft or badging an export, and wrong as the way to
put a permanent watermark into a file someone else will open in Word.
