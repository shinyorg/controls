# Document Editor

[← All Shiny Controls](../../README.md)

> Same packages as the viewers. Two controls: `DocumentEditor` is the lone editing surface;
> `DocumentEditorView` is the same thing dressed as Word — the [Office shell](office-shell.md)'s title
> bar, ribbon, ruler, panes, status bar and File backstage, all wired to the editor.

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

## The Word window

> **Behaviour change.** `DocumentEditorView` now wears the [Office shell](office-shell.md) by default,
> modelled on Word for the web: an accent title bar, the ribbon, a ruler above the page, a navigation
> pane on the left, a comments pane on the right, a status bar along the bottom and the File backstage.
> Give the Blazor host more height (≈700px) than the bare ribbon needed. `ShowShell="false"` puts back
> exactly the previous layout — the ribbon over the page with its own Undo/Redo and navigation pane.

Every part is wired to the editor already, and each has its own switch:

| Part | Switch | What it does |
|---|---|---|
| **Title bar** | `ShowTitleBar` | AutoSave (`AutoSave`), Save / Undo / Redo, the document name (`DocumentName`, default "Document1", renamed in place), the save status (`SaveState`; left null it reads "Unsaved changes" once the document is edited), the command search, the account (`UserName`, which also signs comments and tracked changes) |
| **Ribbon** | `ShowToolbar` | Word's tabs as before. **File** opens the backstage. The right end carries **Comments**, **Editing / Reviewing / Viewing** (`EditMode`: Reviewing switches Track Changes on, Viewing makes the document read-only) and **Share** (`ShareRequested`). Home › Styles is the "AaBbCcDd" gallery (the plain Styles dropdown in the simplified phone ribbon, whose one-line rows are too short for it). Keyboard shortcuts moved out of the tooltips into each item's `Shortcut`, where the command search shows them too |
| **Ruler** | `ShowRuler` | Print Layout only. Shows the caret paragraph's indents and tab stops and the section's margins; dragging a marker indents the selected paragraphs, a click adds a tab stop, dragging a margin edge moves it — each one undo step. Tab stops are saved as `w:tabs` |
| **Navigation pane** | `ShowNavigationPane` | The headings (click to jump; the one the caret is under is marked) and a search box whose Results tab lists every hit with its context. View › Navigation Pane, Ctrl+F and the status bar's page count open it |
| **Comments pane** | `ShowCommentsPane` | Every comment with its author, date and the text it is anchored to. Click a card to select that text; delete one with its button; **New** comments on the selection |
| **Status bar** | `ShowStatusBar` | "Page 2 of 5", "197 words" ("12 of 197 words" with a selection — click for Word Count), the proofing language; Focus; Read / Print / Web view buttons; the zoom slider, − / + and the percentage (which opens the Zoom dialog with page-width / text-width / whole-page presets) |
| **Backstage** | File | Home and New offer `Templates` (null = the built-in **Blank**, **Report** and **Letter**, generated in code), `RecentFiles` (your list), Info with the document's statistics, Save, Save As, Print with a preview of the first page, Export |

The command search (`CommandIndex`) holds the ribbon's commands plus the ones people look for on tabs
not yet opened (Table, Page Break, Link, Comment, Header, Table of Contents, Track Changes, Landscape…).
A query that matches no command searches the document instead.

### Files are the host's

The shell reads and writes nothing on its own; these are events. What happens when you leave one unhandled:

| Event | Unhandled on Blazor | Unhandled on MAUI |
|---|---|---|
| `SaveRequested` | downloads the `.docx` | nothing |
| `SaveAsRequested(OfficeFileFormat)` / `ExportRequested(…)` | downloads `.docx`, `.pdf`, `.txt` or `.html` | nothing |
| `PrintRequested` | opens the browser's print dialog over a PDF of the document | nothing |
| `NewDocumentRequested(OfficeTemplate)` | opens the template in place, raises `DocumentOpened(WordDocument)` | same |
| `OpenRequested`, `RecentFileSelected(OfficeRecentFile)`, `ShareRequested`, `DocumentRenamed(string)` | — | — |

```razor
<div style="height:760px">
    <DocumentEditorView Document="document"
                        @bind-DocumentName="name"
                        SaveState="saveState"
                        UserName="Allan Ritchie"
                        RecentFiles="recent"
                        SaveRequested="SaveAsync"
                        RecentFileSelected="OpenAsync" />
</div>
```

```csharp
editor.SaveRequested += async (_, _) => await File.WriteAllBytesAsync(path, editor.Document!.ToArray());
editor.ExportRequested += (_, format) =>
{
    if (format.Id == OfficeFileFormats.Pdf.Id)
    {
        using var file = File.Create(Path.ChangeExtension(path, ".pdf"));
        editor.ExportPdf(file);
    }
};
```

### PDF

`ExportPdf(Stream)` on both views — or `DocumentPdfExporter.Export(document, stream, options)` in
`Shiny.Controls.Office.Skia` for a document with no view — writes one PDF page per printed page. The
document is laid out afresh in print layout at 100%, whatever the view is showing, and drawn by the same
`DocumentPainter` as the screen, so headers, footers, footnotes, page colour and the text watermark come
along and the editing overlays (caret, selection, squiggles, comment balloons) do not. Text stays text,
with the fonts embedded. It works on WebAssembly. `DocumentPdfExporter.RenderPagePng(document, page,
scale)` renders one page as a PNG — the backstage's print preview.

### Building your own chrome

Everything the view wires up is public: `Shiny.Controls.Office.Shell.WordShell` turns the controller into
what the shell parts take (headings, search results, styles, ruler indents and tab stops in points,
view-mode ids, document info, an HTML rendering), and the controller gained `CurrentTabStops` /
`SetTabStops`, `GoToComment(id)` and `CommentedText(comment)`. `WordTemplates` builds the three
templates.

On MAUI the shell is built once and parts are shown and hidden rather than added later, so the AppKit head
renders it; lists that change after layout (headings, comments, status text) may not repaint there until
the window is resized.

TODO: capture screenshots for the Word window (document-editor).

Edits are surgical on the OOXML runs: a run is split only where an edit actually needs a boundary and
is never rebuilt, so the language, proofing state and revision marks a run carries survive a
formatting change. An unedited document still saves byte-identical.

## Word's feature set

The editor now covers the everyday Word feature set, laid out the way Word lays it out. Every command
below is a method on `DocumentEditorController` first — the ribbon on each host is a thin layer over
it — so a host with its own chrome gets all of it without the ribbon.

> **Behaviour change.** The ribbon is organised into Word's tabs — **Home · Insert · Design · Layout ·
> References · Review · View**, plus a contextual **Table** tab — and the tab strip is **on by default**
> on both hosts (`ShowRibbonTabs`). Proofing moved from Home to **Review**, and zoom moved from Layout
> to **View**. A host that turned the strip off to show a single row of Home commands should set
> `ShowRibbonTabs="False"` explicitly.

| Tab | Groups |
|---|---|
| **Home** | Clipboard (Paste ▸ Keep Text Only, Cut, Copy, Format Painter) · Font (font, size, Grow/Shrink, Change Case, Clear Formatting, B I U S, subscript, superscript, colour, highlight) · Paragraph (bullets, numbering, multilevel, indent −/+, Show/Hide ¶, alignment, line spacing, shading, borders) · Styles · Editing (Find, Replace, Select All) |
| **Insert** | Pages (Blank Page, Page Break, Section Break) · Tables · Illustrations · Links (Link, Bookmark, Remove Link) · Comments · Header & Footer (Header, Footer, Page Number) · Text (Date & Time) · Symbols (Symbol, Horizontal Line) |
| **Design** | Page Background (Watermark, Page Color) |
| **Layout** | Page Setup (Margins, Orientation, Size, Columns, Breaks) · Paragraph (indent, spacing before/after) |
| **References** | Table of Contents (Insert, Update Table) · Footnotes (Insert Footnote, Next Footnote) |
| **Review** | Proofing (Spelling, previous/next misspelling, Word Count) · Comments (New, Delete, Previous, Next, Show) · Tracking (Track Changes) · Changes (Accept, Reject, Previous, Next) |
| **View** | Views (Read Mode, Print Layout, Web Layout) · Show (Navigation Pane, Formatting Marks) · Zoom |
| **Table** *(contextual)* | Rows & Columns (Insert Above/Below/Left/Right, Delete Rows/Columns/Table) · Merge (Merge Cells, Split Cells) |

The **File** button opens the backstage (see [The Word window](#the-word-window)). It still raises
`FileRequested` on MAUI and `FileClicked` on Blazor; with the shell off it is shown only when one of
those is wired (`ShowFileButton` on MAUI), as before.

### Tables you can type in

Positions index the **story** — every paragraph in reading order, table cells included
(`WordDocument.Paragraphs`) — so the caret goes into a cell, typing, Enter, formatting, find, spelling
and every other command work there, and the arrow keys walk out of one cell into the next.
`WordDocument.Blocks` is still the top-level list the layout walks; `TopBlockOf(paragraph)` and
`CellOf(paragraph)` map between the two.

| | |
|---|---|
| **Tab / Shift+Tab** | Next / previous cell, selecting its text. Tab in the last cell adds a row. In a list inside a cell Tab still nests |
| **Rows and columns** | `InsertTableRowAbove/Below`, `InsertTableColumnLeft/Right`, `DeleteTableRow/Column`, `DeleteTable` — columns are addressed by grid position, so inserting beside a spanned cell widens it |
| **Merge / split** | Select from one cell into another and `MergeTableCells()` merges the rectangle between them (`w:gridSpan` across, `w:vMerge` down, contents moved in). `SplitTableCell()` undoes a merge, or splits a plain cell in two |
| **Backspace at a cell's start** | Does nothing, as in Word — nothing joins across a cell boundary |

Each structural edit is one undo step that puts the whole table back.

### Find and Replace

`Ctrl+H` (Home ▸ Editing ▸ Replace) opens Replace: Find Next, Replace, Replace All, with Match case and
Whole words. Replace All is **one** undo step; the replacement keeps the formatting of the text it
replaces. Find and Replace now cover table cells.

```csharp
c.Find.Query = "colour";
c.ReplaceCurrent("color");                       // replaces the hit the selection is on, steps on
var n = c.ReplaceAll("colour", "color", new FindOptions { MatchCase = true });
```

### Clipboard and Format Painter

`Copy()` / `Cut()` return the plain text for the system clipboard and keep a formatted copy;
`Paste(systemText)` pastes that copy with its formatting when the system clipboard still holds what was
copied here, and pastes plain text otherwise. `PasteText` is Keep Text Only (newlines become
paragraphs). Comment anchors, bookmarks and footnote references are left out of a copy, since they are
unique per document. On MAUI the surface's `CopyAsync`/`CutAsync`/`PasteAsync` go through
`Clipboard.Default`; on Blazor through `navigator.clipboard` and the browser's paste event.

**Format Painter** (`CopyFormatting(sticky)`, `Ctrl+Shift+C`/`Ctrl+Shift+V`): pick up the caret's
formatting, then select text — the paint happens when the selecting gesture ends
(`CompletePointerGesture`). A selection covering whole paragraphs takes their paragraph formatting too.

### Font and Paragraph

| | |
|---|---|
| `GrowFont` / `ShrinkFont` | Word's size steps (`Ctrl+Shift+>` / `<`) |
| `ChangeCase(TextCase)` | Sentence / lower / UPPER / Capitalize / tOGGLE; `Shift+F3` cycles |
| `ToggleSubscript` / `ToggleSuperscript` | `Ctrl+=` / `Ctrl+Shift+=` |
| `ClearFormatting` | Direct character formatting off, paragraphs back to Normal (lists kept) |
| `SetLineSpacing(1.15)`, `SetParagraphSpacing(before, after)` | Multiples of single; spacing in points (`Ctrl+1/5/2`) |
| `ChangeIndent(±1)`, `SetIndents(left, right, firstLine)` | Half an inch per press for ordinary paragraphs (a list item moves a level); a negative first line is a hanging indent |
| `SetParagraphShading(color)`, `SetParagraphBorders(preset)` | Drawn and saved (`w:shd`, `w:pBdr`) |
| `ShowFormattingMarks` | Pilcrows and space dots, a view setting |

Every run and paragraph property is written at its schema position — `w:rPr` and `w:pPr` are
sequences, and a `w:color` after a `w:u` is a file Word calls corrupt — which the test suite checks by
schema-validating every feature's saved output.

### Styles

The style API a gallery binds to:

```csharp
IReadOnlyList<DocumentStyleInfo> styles = c.AvailableStyles;  // Id, Name, FontFamily, FontSize (pt), Color, Bold, Italic, OutlineLevel
string current = c.CurrentStyleId;                           // also CaretFormat.StyleId
c.ApplyStyle("Heading2");                                    // creates Word's definition when the document lacks it
c.CurrentStyleChanged += ...;                                // the caret moved onto a different style
```

`AvailableStyles` is Word's gallery set — Normal, No Spacing, Heading 1-3, Title, Subtitle, Quote,
Intense Quote, List Paragraph — followed by the document's own visible paragraph styles, each with the
formatting to preview it in. Applying a built-in the document does not define writes Word's own
definition (ids, names, `basedOn` chain) into `styles.xml` first, so it shows in Word's gallery by its
usual name. Both ribbons carry a Styles dropdown; `Ctrl+Alt+1..3` apply the headings and `Ctrl+Shift+N`
Normal.

### Insert

| | |
|---|---|
| `InsertHyperlink(target, text)` (`Ctrl+K`) | A `w:hyperlink` with an external relationship, or `#bookmark` for a place in the document. Drawn blue and underlined; `RemoveHyperlink`; `CurrentHyperlink` for editing |
| Following a link | Ctrl/Cmd+click on Blazor; **Open Link** (Insert ▸ Links) on MAUI, where a touch screen has no Ctrl. External links raise `LinkActivated`/`LinkClicked` (default: open in the browser); bookmark links jump |
| `InsertBookmark(name)`, `GoToBookmark(name)`, `Bookmarks` | Word's naming rule enforced by `IsValidBookmarkName` |
| `InsertDateTime(format)`, `DateTimeFormats(now)` | Typed as text |
| `InsertSymbol`, `CommonSymbols` | Symbol grid |
| `InsertHorizontalLine` | An empty paragraph with a bottom border, which is what Word's line is |
| `InsertPageNumberField` | A `PAGE` field in the body |
| `InsertBlankPage` | Two page breaks |
| `InsertSectionBreak(NextPage \| Continuous)` | A paragraph-level `w:sectPr`; a next-page break starts a new page in print layout |

### References

**Table of contents** — `InsertTableOfContents()` builds one from Heading 1-3 before the caret's
paragraph, the way Word writes it: a `TOC \o "1-3" \h \z \u` field whose result is a line per heading,
each a hyperlink to a hidden `_Toc` bookmark on the heading, with a right tab stop and dotted leader
before the page number. Page numbers come from a print pagination whatever the view is showing.
`UpdateTableOfContents()` rebuilds it in place; Word regenerates it from the field too.

**Footnotes** — `InsertFootnote(text)` writes the reference and the note (`footnotes.xml`, created with
Word's separator notes). References are numbered by order of citation, not id, and renumber as notes
are added. In print layout each page's notes are drawn at its foot under a short rule, and the
paginator reserves the room for them; in reflow they follow the text under a rule. Endnotes are read
and numbered but not drawn.

### Review

**Comments** — `AddComment(text)` anchors a comment to the selection (or the word at the caret) with
`w:commentRangeStart/End` and a reference run, and writes it to `comments.xml` with the `Author`, date
and initials. `DeleteComment(id)`, `DeleteAllComments()`, `NextComment()`, `PreviousComment()`,
`Comments`, `CurrentComment`. Commented text is washed; in print layout each comment gets a **balloon**
in the margin beside its line, stacked so they never overlap (`CommentMarks()` is what the painter
draws; `ShowComments` hides the balloons).

**Track changes** — `IsTrackingChanges` is stored in the document (`w:trackRevisions`), so it is still on
when the file is reopened here or in Word. While it is on, typing lands in `w:ins` and deleting wraps
the text in `w:del` (with `w:delText`) carrying `Author` and the date; consecutive keystrokes grow one
insertion and coalesce into one undo step, and deleting your own unaccepted insertion just removes it.
Insertions draw underlined and deletions struck through in the reviewer's ink. `AcceptChange` /
`RejectChange` act on the change at the caret and move on; `AcceptAllChanges` / `RejectAllChanges`;
`NextChange` / `PreviousChange`; `Revisions`. Joining paragraphs is not tracked — the caret steps over
the paragraph mark instead.

**Word count** — `Statistics` (below), shown by Review ▸ Word Count and `Ctrl+Shift+G`.

### Design and Layout

| | |
|---|---|
| `SetPageColor(color)` | `w:background`, plus `w:displayBackgroundShape` so Word shows it |
| `SetWatermarkText("DRAFT")` | **Persisted** — VML WordArt in the default header, the way Word writes one, so Word shows it too. Presets CONFIDENTIAL / DRAFT / DO NOT COPY / ASAP, or custom text. `WatermarkText` reads it back |
| `SetPaperSize(PaperSize.A4)` | Letter, A4, Legal, A5; orientation kept |
| `SetColumns(1..3)` | Written as `w:cols` and saved (see limitations) |

### View, status bar and navigation

What a status bar or a navigation pane binds to:

```csharp
DocumentStatistics s = c.Statistics;   // CurrentPage, Pages, Words, CharactersNoSpaces, CharactersWithSpaces, Paragraphs, Lines, SelectedWords
c.StatisticsChanged += ...;            // an edit, or the caret moving to another page
c.Zoom = 1.25;  c.ZoomChanged += ...;  // any zoom change: slider, pinch, ctrl-wheel
IReadOnlyList<DocumentHeading> h = c.Headings();   // Level, Text, Paragraph
c.GoToParagraph(h[0].Paragraph);
```

The views forward them: `Statistics`, `StatisticsChanged`, `ZoomChanged`, `CurrentStyleChanged`.
**Read Mode** (`ReadMode`) hides the ribbon and reflows the document into one read-only column, with an
Exit button; **Navigation Pane** (`ShowNavigationPane`) lists the headings down the left.

### Keyboard shortcuts

Both hosts resolve keys through the shared `WordShortcuts.Resolve(key, ctrl, shift, alt)` table, so they
cannot disagree. On MAUI a desktop host routes keys in with `DocumentEditorView.HandleShortcut(key,
ctrl, shift, alt)` (MAUI has no portable key-down event); Blazor handles them itself.

| Keys | | Keys | |
|---|---|---|---|
| Ctrl+B / I / U | Bold / italic / underline | Ctrl+E / L / R / J | Centre / left / right / justify |
| Ctrl+C / X / V | Copy / cut / paste | Ctrl+Shift+> / < | Grow / shrink font |
| Ctrl+Z / Y | Undo / redo | Ctrl+= / Ctrl+Shift+= | Subscript / superscript |
| Ctrl+F / H | Find / replace | Ctrl+Enter | Page break |
| Ctrl+K | Hyperlink | Ctrl+1 / 5 / 2 | Line spacing 1 / 1.5 / 2 |
| Ctrl+Alt+1..3 | Heading 1-3 | Ctrl+Shift+N | Normal style |
| Ctrl+Space | Clear formatting | Ctrl+M / Ctrl+Shift+M | Indent / outdent |
| Ctrl+Shift+C / V | Copy / paste formatting | Shift+F3 | Change case |
| Ctrl+Alt+M | New comment | Ctrl+Shift+E | Track changes |
| Ctrl+Shift+G | Word count | Ctrl+Shift+8 | Show/hide ¶ |

`DocumentEditorController.Execute(WordCommand)` runs the engine's commands; the ones that need the
host's UI or clipboard — Find, Replace, Hyperlink, New Comment, Word Count, Copy, Cut, Paste — return
false (see `WordShortcuts.IsHostCommand`) and surface as `ShortcutRequested` on the surfaces.

### Limitations

- **Columns** are written and saved but the page view still lays text out in a single column.
- **Sections** — breaks paginate, and each section's own page setup is written, but every page is drawn
  on the last section's paper size and margins.
- **Headers and footers** are still set as a line of text rather than edited in place.
- **Tracked formatting changes** (`w:rPrChange`) and tracked paragraph joins are not recorded; existing
  ones are preserved.
- Endnotes are numbered but not drawn.

## Pickers and icons

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
| Physical keys (arrows, shortcuts) | ✅ | ⚠️ route through `HandleKey` / `HandleShortcut` — MAUI has no portable key-down event |

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
PowerPoint's own mechanism, the paragraph end mark. The one exception is also Word's: with the caret
*inside* a word, the change formats that whole word straight away. Turning bold, italic, underline or
strikethrough **off** also overrides a style that turns it on, so a heading can be un-bolded.

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

**The tab strip is on by default** (`ShowRibbonTabs`) — with Word's eight tabs it is the only way to
reach most of the editor. Turn it off only when a host shows its own tab of commands.

**Below 600px wide the bar runs in `Simplified` mode** — one dense row, every item small, group titles
dropped. Group collapsing is the wrong answer at phone width: it folds groups into dropdowns
worst-first, which is right when a window is a little too narrow, but on a phone there is room for no
group at all and every command ends up behind a dropdown. See [Ribbon](ribbon.md).

## Mouse and touch are not the same gesture

A mouse drag selects text; a finger has no wheel, so a drag has to pan or the page cannot be scrolled
at all. Under touch the editor takes the mobile convention: **tap** places the caret, **drag** pans the
page, **double-tap** selects a word and **triple-tap** a paragraph, and a selection is adjusted by
dragging the round **handles** drawn under each of its ends. Long-press opens the spelling menu on a
misspelt word and otherwise selects the word, handles and all (MAUI; a finger held still is a long
press even though Android keeps reporting moves at the same point). On a touch screen a ribbon command
or a jump from a pane hands focus back to the page only if it had it, so the soft keyboard does not pop
up over the page for a formatting tap; when it does open, the page shrinks above it and scrolls the
caret into view.

The caret is placed on the way *up* rather than the way down, because until the finger lifts there is
no telling a tap from the start of a pan.

Nothing changes for a mouse: drag still selects, shift-click still extends, and the handles are not
drawn at all — they would be two targets that do nothing a drag does not.

## The toolbar

Word's tabs, in Word's order — see [Word's feature set](#words-feature-set). The contextual **Table**
tab appears only while the caret is in a table, as Word's Table Layout tab does. Undo and redo sit in
the quick access row above the tabs.

## Reading a document on a phone

A page is a fixed width — that is what makes it a page — so on a phone it is always wider than the
screen and the right-hand end of every line is off it. Three things address that, and they are meant to
be used together:

| | |
|---|---|
| **Pan** | A one-finger drag moves the page on **both** axes. Under touch a drag pans rather than selecting; see below |
| **Zoom** | Pinch, or the View tab's zoom controls, which step through 50 / 75 / 100 / 125 / 150 / 200 / 300%. `Zoom` is also a plain property, and `ZoomChanged` follows it |
| **Fit width** | Sets the zoom so the page exactly spans the window — the one-tap answer to "I cannot see the whole line". Print layout only; reflow already fits by construction |

On the desktop the wheel scrolls, a wheel with a sideways component pans, and **ctrl-wheel zooms** —
which is not a shortcut anyone had to learn, but what a trackpad pinch is delivered as in every browser.

## Spelling

The red underline is only half of it; the other half is reaching the suggestions. There are three ways
in, because the one that works depends on the device:

| | |
|---|---|
| **Long press** (touch) or **right-click** (desktop) | Opens the menu on the word under the pointer: suggestions, Ignore, Add to dictionary |
| **Review ▸ Proofing** | Turn the pass on or off, and step to the previous or next misspelling. Stepping selects the word and opens its menu, so the arrows are a complete review loop on their own |
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

Every paragraph the caret can reach is searched — **table cells included**, since positions now index
the story rather than the top-level blocks. **Ctrl+H** opens Replace; see
[Find and Replace](#find-and-replace).

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

Word's four presets — Normal, Narrow, Moderate, Wide — under Layout ▸ Page Setup ▸ **Margins**, with the
preset the document matches ticked.

## Page chrome on the ribbon

Header, footer, page number and breaks live on **Insert** (and breaks on **Layout ▸ Breaks** too); print
layout is a **View** setting.

| | Where | |
|---|---|---|
| **Header** / **Footer** | Insert ▸ Header & Footer | Prompts for the line, seeded with whatever is there. An empty line removes it — the only way back out of having one |
| **Page number** | Insert ▸ Header & Footer | A menu, not a button: header or footer × left, centre or right. It appends to a header already there rather than replacing it |
| **Page break** | Insert ▸ Pages | Splits the page at the caret (Ctrl+Enter) |
| **Print layout** | View ▸ Views | Print Layout (sheets of paper) or Web Layout (one continuous column) |

Header and footer are asked for rather than edited in place on the page. They are separate stories in
the document — their own parts, laid out per page and repeated — so editing them in the canvas means a
second caret, a second selection and a way in and out of them. Asking for the line is the whole of
what most documents need, and it is undoable like any other command.

Note that headers and footers only *show* in print layout: a reflowing view has no pages to attach
them to. Setting one in reflow still writes it.

## Orientation

Layout ▸ Page Setup ▸ **Orientation** offers Portrait and Landscape, the current one ticked.

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

The editors carry a **Watermark** button — document in Design ▸ Page Background (as *Picture
Watermark…*), slide in Insert, spreadsheet in Data ▸ Sheet. It picks a picture through exactly the same path as inserting one: camera or gallery on
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

**For a watermark that is saved, the document editor has a second, text one**: Design ▸ Watermark's
CONFIDENTIAL / DRAFT / DO NOT COPY / ASAP / custom text, or `SetWatermarkText("DRAFT")`. That one is
written into the document's default header as VML WordArt — Word's own form — so Word shows it, and
`WordDocument.WatermarkText` reads it back from any document that has one.
