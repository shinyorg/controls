# Spreadsheet

[← All Shiny Controls](../../README.md)

> Separate packages: `Shiny.Maui.Controls.Office` and `Shiny.Blazor.Controls.Office`, over the shared
> `Shiny.Controls.Office.Shared` kernel and `Shiny.Controls.Office.Skia` renderer.

Opens, renders and edits `.xlsx` workbooks. Both hosts drive the same controller and paint with the
same SkiaSharp routine, so MAUI and Blazor are not two implementations kept in step — they are one.

```bash
dotnet add package Shiny.Maui.Controls.Office     # or Shiny.Blazor.Controls.Office
```

MAUI registers its Skia surface through one call:

```csharp
builder.UseMauiApp<App>().UseShinyControls().UseShinyOffice();
```

`UseShinyOffice()` calls `UseSkiaSharp()` for you and, on the macOS AppKit head (`net10.0-macos`), adds
the Skia canvas SkiaSharp itself does not ship — it has no `-macos` target, so that head falls back to a
handler whose `CreatePlatformView()` throws and every Office control came up blank. Elsewhere the two
calls are equivalent.

```xml
<office:SpreadsheetView Workbook="{Binding Workbook}" SheetName="Budget" />
```

```razor
<div style="height:420px">
    <SpreadsheetView Workbook="workbook"
                 @bind-SheetName="sheetName"
                 ShowFormulaBar="true" />
</div>
```

```csharp
using var workbook = await Workbook.OpenAsync("book.xlsx");   // or Workbook.Create("Sheet1")

workbook.Execute(new SetCellValueCommand("Budget", CellRef.Parse("B2"), CellValue.FromNumber(42)));
workbook.Execute(new SetCellFormulaCommand("Budget", CellRef.Parse("D2"), "B2*C2"));
workbook.Undo.Undo();

await workbook.SaveAsync();
```

**Formula bar.** A name box and formula field above the grid, on by default and turned off with
`ShowFormulaBar="false"`. It matters because the grid paints the *result*: a cell reading 156.75 gives
no way to discover that it holds `=SUM(D2:D4)`, and no way to edit it as a formula rather than retyping
it. Editing there is the same undoable command as typing into the cell; Enter commits and moves down,
Escape reverts, and clicking away commits into the cell that was being edited rather than the one
clicked. Typing an address into the name box goes there.

Touching the grid is what ends an edit in the bar. That has to be explicit on MAUI: a canvas is not
focusable, so tapping a cell leaves the field holding first responder and no `Unfocused` is raised. The
bar tracks the selection through the same flag that stops a stray controller change from overwriting a
half-typed formula, so a field left focused froze it — the grid moved, the address and contents did
not. In a browser the click blurs the input and this happens on its own.

**Formatting toolbar.** `ShowToolbar="true"` puts a `SpreadsheetToolbar` above the formula bar. It is a
ribbon with two tabs. **Home** is the usual half — clipboard, font, size, bold, italic, underline,
strikethrough, text colour, **cell fill**, alignment on both axes, indent, wrap text, number formats
(general, number, currency in the reader's own culture, percent, scientific, date, time, text),
increase and decrease decimal places, AutoSum, clear contents and clear formatting. **Data** is the
half only a spreadsheet needs: rows and columns in and out, column width — fit-to-contents on the
button, four presets behind its chevron — hide and unhide columns, and the function library, where
SUM, AVERAGE, COUNT, MIN and MAX each have a button of their own. Every button is one undoable command
through the same `SpreadsheetController` a keyboard shortcut would reach, so a toolbar action and a
typed edit share one undo stack. The toolbar is off by default — the
formula bar and tab strip are how a workbook is *read*, and a viewer should not grow a formatting bar
it never asked for.

Formatting is applied as a **delta**, not as a format assigned wholesale: bolding a range that mixes a
red heading with black body text leaves both colours where they are. Styles are interned, so bolding a
thousand cells adds exactly one entry to the styles part rather than a thousand identical ones.

**Formatting columns.** Select a column from its header and the format is written as a *column* style —
one attribute on one `<col>` element, the way Excel does it — so it applies to every row including the
ones that do not exist yet. That is what makes a column formatted as currency still show currency for a
value typed into it tomorrow. Formatting one cell inside a formatted column still overrides it, and
clearing that cell's formatting does not let the column's come back. Row-header selections work the
same way. Column widths and row heights are now recorded in the file, so a column dragged wider — or
fitted to its contents from the toolbar — survives a save and reopen.

```csharp
var controller = view.Controller;                    // MAUI: Sheet.Controller, Blazor: view.Controller
controller.ToggleBold();
controller.SetFillColor(new ArgbColor(255, 0xFF, 0xEB, 0x3B));
controller.SetNumberFormat(NumberFormatPreset.Currency);
controller.AdjustDecimals(+1);
controller.ApplyAutoFunction(AutoFunction.Sum);      // false when there is nothing to total
controller.AutoFitColumns();
controller.ActiveFormat;                             // ResolvedFormat — what a toolbar shows the state of
```

**Worksheets.** A workbook is a book, not a sheet, and the control shows it as one: a tab strip under
the grid switches between the visible sheets and adds, renames, duplicates, reorders, hides and deletes
them. Every one of those is an undoable command like any cell edit, and each sheet keeps its own
selection, scroll position and hand-dragged column widths, so moving between tabs comes back to where
you were. Hidden sheets stay off the strip — they are hidden in Excel too — and are reachable from the
overflow menu, which is also the only place to unhide one. Set `ShowSheetTabs="false"` to leave the
strip out, or `AllowSheetEditing="false"` to keep it as a switcher only.

Renaming rewrites every formula and defined name that pointed at the old name — including the quoted
spelling (`'Q1 Sales'!A1`) and both ends of a 3-D span — so a rename cannot leave `#REF!` behind.
Formulas already read across sheets, and always did.

```csharp
workbook.Execute(new AddSheetCommand("Forecast", index: 1));
workbook.Execute(new RenameSheetCommand("Sheet1", "Actuals"));   // rewrites Sheet1!B2 everywhere
workbook.Execute(new DuplicateSheetCommand("Actuals", "Actuals (2)", index: 2));
workbook.Execute(new MoveSheetCommand("Forecast", 0));
workbook.Execute(new SetSheetVisibilityCommand("Scratch", false));
workbook.Execute(new DeleteSheetCommand("Forecast"));            // undo restores it with its contents
```

| Capability | Notes |
|---|---|
| Rendering | Virtualized over all 1,048,576 rows; frozen panes, merged cells, number formats, fonts, fills, alignment, theme colours with tint |
| Editing | Cell values and formulas, range clear, column/row resize, range selection, in-cell editing through a native `Entry` / `<input>` |
| Formula bar | Name box and formula field on both hosts, `ShowFormulaBar` to hide; shows the formula, not the result |
| Formatting | `ShowToolbar` on both hosts: font, bold/italic/underline/strike, text colour, cell fill, alignment on both axes, indent, wrap; applied as a delta so a mixed selection keeps what each cell had |
| Number formats | Currency (culture-aware), percent, scientific, date, time, text, plus increase/decrease decimals |
| Auto functions | Σ writes SUM, AVERAGE, COUNT, MIN or MAX over the range the selection implies — the run above, the run to the left, or one total per column of a block |
| Columns | Header selections format the whole column via a `<col>` style; widths, row heights, auto-fit and hide/show are recorded in the file |
| Worksheets | Tab strip on both hosts: switch, add, rename, duplicate, reorder, hide/unhide, delete — all undoable, with per-sheet selection and scroll |
| Undo | Transactional, with typing-run coalescing; a range clear is one step |
| Formulas | ~80 functions, dependency-ordered incremental recalculation, circular-reference detection |
| Round-trip | Edits are surgical. An unmodified workbook saves byte-identical; macros, tracked changes, pivot caches and custom XML survive untouched |
| Reporting | `UnsupportedFeatureCollector` names anything in a document the editor cannot show or edit |

## Mouse and touch are not the same gesture

A mouse drag across the grid extends the selection and the wheel scrolls, which is what every desktop
spreadsheet does. A finger has no wheel, so if dragging also meant "extend the selection" there would
be no gesture left to scroll with — which is exactly how the grid ended up unpannable on a phone.

Under touch the grid takes the mobile convention instead:

| Gesture | Mouse | Touch |
|---|---|---|
| Drag on a cell | Extends the selection | **Pans** the grid, both axes |
| Tap / click a cell | Selects it | Selects it |
| Extend a selection | Drag, or shift-click | Drag one of the two round **handles** on the selection's corners |
| Header press | Selects the column or row | Selects the column or row |

The kind is read off each pointer event rather than decided per platform, because both turn up in one
session — an iPad with a trackpad, a laptop with a touchscreen. The handles only appear once a finger
has actually been used; for a mouse they would be two targets that do nothing a drag does not.

A press on a header still selects, and still resizes, under touch: row and column selection is what
cut, copy and insert operate on, and turning those into a pan would take them away from touch entirely.

A pan is clamped to the sheet's used range plus one screen. A wheel moves a notch at a time and can be
left unbounded; a finger flings, and a grid that scrolls into an unbounded field of blank cells is
indistinguishable from one that has lost its data.

**Constraints.** Blazor is **WebAssembly only** (a Server round-trip per keystroke is unusable, and
SkiaSharp on WASM needs the `wasm-tools` workload — without it `libSkiaSharp` is never linked into the
runtime and the app fails in the browser, so `Shiny.Blazor.Controls.Office` fails the build up front
with `SHINY0001` instead; bypass with `ShinySkipWasmToolsCheck=true`). MAUI requires `UseShinyOffice()` (which registers SkiaSharp, plus the AppKit canvas on `net10.0-macos`). Inserting and
deleting rows and columns is deliberately not implemented — it requires rewriting references across
formulas, merged cells, conditional formatting, defined names, data validation, charts and tables.
Chart, dialog and macro sheets are preserved on save but have no tab: the grid has nothing to draw for
them. Deleting a worksheet drops any defined name scoped to it, which is what Excel does.

## Dark mode

`Theme` is nullable and **unset means follow the host** — the app's light/dark appearance on MAUI,
the page's `color-scheme` on Blazor — and it keeps up live when that flips. Pass `SpreadsheetTheme.Light`
or `SpreadsheetTheme.Dark` only to pin one regardless of the app around it. See
[Styling & theming](styling.md#dark-mode).

Following the host means the **theme's neutrals**, not just its light/dark bit. The grid takes its
background from `Surface`, its text from `OnSurface`, its grid lines from `OutlineVariant` and its
headers from `SurfaceContainer` and `Outline` — the same tokens the ribbon above it is built from, so
the two sit on one ground. Before this the painter had a fixed pair of palettes, a neutral grey and a
white, and in any theme whose neutrals carry a tint (the packs here run blue) that put a blue-grey bar
directly on top of a flat grey grid.

Only the neutrals are taken. The selection green, the clipboard marquee's blue and the error red carry
meaning rather than surface, and an app's accent is no substitute for any of them — a spreadsheet with
a purple selection is not a themed spreadsheet, it is a different control. Set `Theme` to override any
of it.

A **pinned** theme on MAUI carries the chrome with it. The formula bar's boxes take their ink and ground
from the theme (they used to take the app's ink on the theme's ground, so a pinned dark theme in a light
app drew black text on a near-black bar), and the toolbar merges the active theme pack's light or dark
token palette — whichever matches the pinned theme's background — into its own resources, so the ribbon
and the pickers hosted in it match the grid. Unpinning removes it.

On Blazor a pinned theme does the same through CSS: the view's root takes the `shiny-theme-dark` (or
`shiny-theme-light`) scoping class — chosen by the pinned theme's background, so a custom theme built
from `SpreadsheetTheme.Dark` scopes dark too — which re-derives every `--shiny-color-*` token beneath
it. The toolbar/ribbon, its pickers and menus, the formula bar and the sheet tabs all match the grid,
and the class comes off again when `Theme` goes back to `null`. It used to repaint only the canvas,
leaving a light ribbon and formula bar around a dark grid.

Following the app also survives the process running for a while: the MAUI views used to lose their
light/dark subscription at the first garbage collection (`Application.RequestedThemeChanged` holds its
handlers weakly), after which a flip repainted the ribbon but left the formula bar, sheet tabs and grid
on the old appearance.

Cell text with no colour of its own takes the theme's ink, and on a cell the author **filled** that ink
is made to contrast with the fill — a light header band keeps dark text in dark mode. An explicit font
colour is the author's pairing with their own fill and is left alone.

A **document** or a **deck** takes only its surround from the theme: the page and the slides are
pictures of printed things, and tinting the paper would misrepresent what the document actually looks
like.

## The toolbar is a Ribbon

The formatting bar is a [Ribbon](ribbon.md) on both hosts, replacing the single scrolling strip of
icons it used to be.

**Two tabs, split by what a command changes rather than by how often it is reached.** *Home* changes
how a cell looks — Clipboard, Font, Alignment, Number, Editing — and clipboard leads, as it does in
Excel, because cut/copy/paste apply to whatever is selected and are reached far more often than any
formatting command. *Data* changes the shape of the sheet under it — Cells, Columns, Functions.

The second tab is what let the structural half grow. On one tab there was room for insert-row and
insert-column between clear-formatting and a colour picker, and that was the ceiling; deleting rows,
column widths, hiding columns and the individual aggregates all existed on the controller with no
affordance on the bar. AutoSum is on both tabs, as it is in Excel — the face of Home's *Editing* group
and the head of Data's *Functions* — because it is the one command here reached often enough that a
tab switch in front of it would be felt.

Two things the strip could not do:

- **The ad-hoc dropdowns became real ribbon items.** Number formats is a `RibbonMenuButton` and AutoSum a `RibbonSplitButton` — the face still writes SUM, the chevron still offers average, count, min and max. That deleted a hand-written backdrop
  div, an absolutely-positioned panel and a `bool …Open` field per menu on Blazor, and an action sheet
  per menu on MAUI — along with their dismissal, keyboard and edge-flipping behaviour, which the
  ribbon already has.
- **Commands are grouped and captioned** instead of separated by anonymous hairlines.

Undo and redo sit in the ribbon's quick access row, outside the tabs, so they never move or disappear.

**The tab strip is on** — a change on Blazor, where `ShowTabs` used to default to false because a strip
carrying a single "Home" is noise. There are two tabs now, and the strip is the only way to reach the
second. Setting `ShowTabs="false"` does not hide the Data tab's commands: it folds those groups back
onto the one tab, where the ribbon's own collapsing deals with the width. A setting that quietly
removed a third of the bar would be a worse bargain than a crowded one. MAUI shows the strip either
way; `Ribbon.ShowTabStrip` is the equivalent switch there.

**Below 600px wide the bar runs in `Simplified` mode** — one dense row, every item small, group titles
dropped. Group collapsing is the wrong answer at phone width: it folds groups into dropdowns
worst-first, which is right when a window is a little too narrow, but on a phone there is room for no
group at all and every command ends up behind a dropdown. See [Ribbon](ribbon.md).

## Find

**Home ▸ Find** — the same box, `3/12` readout and pair of arrows the document editor has, and the same
`IFindController` behind them. See [Document Editor ▸ Find](document-editor.md#find) for the walk, the
wrap and the keyboard.

What is searched is the cell's text **as the formula bar shows it**: the formula when the cell has one,
otherwise the literal. That is Excel's own default — *look in: formulas* — and the only choice under
which searching for `SUM` finds the cells that total something. A cell's *formatted* value is
deliberately not searched, or `1234` would miss a cell showing `1,234.00` and `1,234` would find one
that holds no comma.

The **active sheet only**, again matching Excel. A workbook-wide search moves the user between sheets
on every press of "next", which is rarely what they meant when they typed into a box on the sheet they
were looking at. `SearchAllSheets` opts in:

```csharp
var find = view.Controller!.Find;

find.SearchAllSheets = true;
find.Query = "Q1";
find.FindNext();          // switches sheets when the hit is on another one
```

Matches are collected in **book order**, never with the active sheet first. Ordering the list around
whichever sheet is showing re-orders it every time "next" crosses a sheet boundary, and stepping then
resumes from the moved match's new index — which walks two sheets forever and never reaches the third.
Hidden sheets stay out either way: they are not on screen, and stepping onto one would show the user a
sheet the workbook has deliberately put away.

The wash covers **whole cells** rather than the matched characters. A cell is the smallest thing a
selection can address, so highlighting three characters inside one would mark something the arrows
cannot land on — and the cell's own formatting can right-align, indent or reformat the text out from
under a character range measured against the raw value. Only the showing sheet's cells are drawn; the
readout is what says how many are on the others.

## Clipboard and structure

The **Clipboard** group carries cut, copy and paste. Paste is the only one with a precondition of its
own — there has to be something held, which is what `SpreadsheetController.CanPaste` reports; cut and
copy only need a selection, and there always is one. Whole rows and columns are supported, not just
cell ranges: select a row or column header and the cut or copy takes the band with its values,
formulas and formatting, and the paste is a single undoable step.

A pending cut or copy is drawn with a **marching-ants border** — the animated dashed outline Excel
uses, around the range the capture came from. It is a distinct colour from the selection border rather
than a dashed version of it, because the two are routinely on screen at once: marking a source and then
moving to a destination is the whole shape of a paste. `ClipboardRange` is what gets drawn, and it is
null when the capture came from another sheet — the content is still pasteable, but those coordinates
mean something else on this one. The border clears on Escape, on typing, on Delete, on a structural
insert, and on the paste that spends a cut; a copy survives its own paste, so the same block can be put
down twice. Its colour is `SpreadsheetTheme.ClipboardBorder`, defined in both schemes.

A copied formula is rebased onto its new position — `=B1*2` copied one row down becomes `=B2*2` — while
`$`-pinned references stay where they are. A **cut** formula is moved bodily and keeps pointing at
exactly the cells it always pointed at, which is Excel's behaviour and the reason cut and copy are not
the same operation with a flag. References held by *other* formulas to cut cells are not repointed.

The Data tab's **Cells** group carries insert and delete for both axes. They act on the selection —
`InsertRows(count)` opens blank rows *above* it and pushes everything below down, `InsertColumns(count)`
opens columns to its *left*. Both shift formulas and merged ranges to follow. `DeleteRows` and
`DeleteColumns` close the gap; a formula that pointed *into* the removed band becomes `#REF!`, as it
does in Excel, and undo puts both the band and those formulas back. Delete had no button while the bar
was one tab — deleting is destructive, and a destructive command wedged between a colour picker and
clear-formatting is one misclick from a lost row. On a tab of its own, next to the insert pair whose
icons it mirrors, it is where someone goes looking for it, and it is one Ctrl+Z away either way.

The **Columns** group is the width and visibility half. The *Width* button fits the selected columns to
their contents; its chevron offers four fixed widths, narrowest first, including the sheet's own
default — which is the only way back once a column has been dragged or fitted. Hide and unhide act on
the selected columns, and both are recorded in the file.

The **Functions** group gives SUM, AVERAGE, COUNT, MIN and MAX a button each, labelled with the formula
name rather than a friendly one: the button writes `=AVERAGE(…)` into a cell, and that is the thing
worth naming. Each picks its own range the way AutoSum does — the run above, the run to the left, or
one total per column of a block.

## Accent

The bar wears Excel green (`#107C41`) by default — see
[Document Editor ▸ Accent](document-editor.md#accent) for how the three controls are coloured and how
to set your own.

## Watermarks

`Watermark` draws a picture behind the content, on the viewer as well as the editor. The button picks
one through the same path as inserting a picture. See
[Document Editor ▸ Watermarks](document-editor.md#watermarks) — including why it is a display
watermark rather than one written into the file.
