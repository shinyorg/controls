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

## The Excel window

The control is Excel's whole window, not a grid with a toolbar. It wraps itself in the
[Office shell](office-shell.md) dressed as Excel, and that is **on by default**:

| Part | What it does |
|---|---|
| Title bar | Excel green. AutoSave, Save / Undo / Redo, the workbook name (click to rename), the save status ("Unsaved changes" → "Saving…" → "Saved" / "Saved locally"), and the command search |
| Command search | Every ribbon command, with its shortcut (tooltips carry `Shortcut` now, not "(Ctrl+B)" in the text), plus Save, New Workbook, Export to PDF / CSV, Print, Comments and the three views. A query that matches no command is searched for across every visible sheet |
| Ribbon | **File** opens the backstage. **Comments**, the **Editing / Reviewing / Viewing** menu (Viewing makes the workbook read-only) and **Share** sit at the right end of the tab strip. Undo/Redo move to the title bar |
| Comments pane | Every note in the workbook — `Sheet!Cell`, author, text — and a click selects the cell, switching sheets |
| Status bar | **Ready / Enter / Edit** (typing over a cell is Enter, F2 is Edit), **"Average: 8  Count: 3  Sum: 24"** when the selection holds at least two values (hidden otherwise, as in Excel), **Normal / Page Layout / Page Break Preview**, and a zoom slider bound to `Zoom` (10–400%) |
| Backstage | New (blank workbook plus Monthly budget, Invoice and Weekly schedule, built in code by `SpreadsheetTemplates`), Open, Info (sheets, cells with data, formulas, notes), Save, Save As, Print, Export |

**Page views.** Page Layout dashes the edges of the printed pages over the grid; Page Break Preview
draws them solid blue, greys out everything off the pages and writes "Page n" on each. Pages are US
Letter with Excel's Normal margins, cut at whole columns and rows from A1 to the end of the used range
(`SheetPagination`); nothing in the workbook changes. They are views of the grid, not a page-by-page
layout with headers and footers.

**Files.** The shell reads and writes nothing itself. Save (title bar, backstage, Ctrl+S), Save As,
Export and Print each raise `FileRequested` with a `SpreadsheetFileRequest` — the format, a file name
("Budget.csv"), the action, and `WriteToAsync(stream)` / `ToBytesAsync()`. xlsx is the whole workbook;
**CSV** is the active sheet's formatted values (UTF-8 with a BOM, as Excel writes it); **PDF** is the
active sheet's used range painted by the grid's own `SpreadsheetPainter` onto Letter pages through
SkiaSharp's `SKDocument`, without headings, gridlines or the selection. On Blazor an unhandled request
downloads the file (Print opens the PDF in a new tab for the browser to print); on MAUI handle it:

```csharp
sheet.FileRequested += async (_, request) =>
{
    var path = Path.Combine(FileSystem.AppDataDirectory, request.FileName);
    await using var file = File.Create(path);
    await request.WriteToAsync(file);
};
```

```razor
<SpreadsheetView @bind-Workbook="workbook"
                 @bind-Zoom="zoom"
                 DocumentName="Budget"
                 UserName="Allan Ritchie"
                 RecentFiles="recent"
                 FileRequested="SaveAsync" />
```

Picking a template builds the workbook, shows it and reports it (`WorkbookChanged` on Blazor — use
`@bind-Workbook` — `WorkbookReplaced` on MAUI); handle `TemplateSelected` to do it yourself.
`OpenRequested`, `RecentFileSelected` and `ShareRequested` are the host's. `UserName` is also the
author written into new notes. `Commands` is the `OfficeCommandIndex` behind the search, for the app's
own entries.

**Turning parts off.** `ShowShell="false"` drops the shell and gives the ribbon + formula bar + grid +
tabs layout of before. Short of that, `ShowTitleBar`, `ShowStatusBar`, `ShowBackstage` (File then only
raises `FileMenuRequested`), `ShowCommentsPane` and, on Blazor, `ShowRibbonActions`. MAUI builds every
part in the constructor and only toggles `IsVisible`, so the AppKit head renders it; the comments list
is rebuilt when notes change and may not repaint on AppKit until a resize, like the shell's other
lists.

> **Behaviour change:** the shell is on by default, so an existing `SpreadsheetView` grows a title bar,
> status bar and backstage. `FileMenuRequested` is still raised, after the backstage opens. A plain
> viewer sets `ShowShell="false"` (and `ShowToolbar="false"`).

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

**The ribbon.** A `SpreadsheetToolbar` sits above the formula bar, laid out the way Excel's is:
**File** (the backstage — see [The Excel window](#the-excel-window)), **Home**,
**Insert**, **Formulas**, **Data**, **Review** and **View**. Every button is one undoable command
through the same `SpreadsheetController` a keyboard shortcut would reach, so a ribbon action and a typed
edit share one undo stack.

> **Behaviour change:** `ShowToolbar` now defaults to **true**. It used to be off, on the grounds that a
> viewer should not grow a formatting bar it never asked for; with the ribbon now carrying most of what
> Excel does, a spreadsheet control without it undersells what it is. A read-only viewer sets
> `ShowToolbar="false"`.

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
| Rendering | Virtualized over all 1,048,576 rows; frozen panes, merged cells, borders, number formats, fonts, fills, alignment, wrapped text, text overflow into empty neighbours, `####` for numbers that don't fit, theme colours with tint |
| Editing | Cell values and formulas, range clear, column/row resize, range selection, in-cell editing through a native `Entry` / `<input>` with formula autocomplete |
| Formula bar | Name box (cells, ranges, defined names — typing a new name defines it) and formula field, `ShowFormulaBar` to hide |
| Formatting | Font, bold/italic/underline/strike, text colour, fill, **borders**, alignment on both axes, indent, wrap, **merge**, **cell styles**, **Format Cells** (Ctrl+1); applied as a delta so a mixed selection keeps what each cell had |
| Number formats | Currency (culture-aware), percent, scientific, date, time, text, fraction, custom codes, decimals |
| Data | **Sort** (A→Z, Z→A, multi-level), **AutoFilter** (value lists, text and number conditions), **data validation** with in-cell dropdown lists, **fill handle** and Fill Down/Right |
| Styles | **Conditional formatting** (highlight rules, top/bottom, above/below average, duplicates, data bars, colour scales), **Format as Table** (table parts, banded rows, filter arrows) |
| Insert | **Charts** (column, bar, line, pie, area — moved, resized and deleted on the grid), **hyperlinks**, **notes** |
| Formulas | ~140 functions incl. XLOOKUP, XMATCH, SUBTOTAL, AGGREGATE and the financial set; **defined names** and a Name Manager; Insert Function; autocomplete with signature help; dependency-ordered recalculation, circular-reference detection |
| View | Gridlines, headings, formula bar, Show Formulas (Ctrl+`), freeze panes, **zoom 10–400%** (Ctrl+wheel / pinch) |
| Status bar | `SelectionStatistics` — Average, Count, Numerical Count, Min, Max, Sum — for a host's status bar |
| Columns | Header selections format the whole column via a `<col>` style; widths, row heights, auto-fit and hide/show are recorded in the file |
| Worksheets | Tab strip on both hosts: switch, add, rename, duplicate, reorder, hide/unhide, delete — all undoable, with per-sheet selection and scroll |
| Undo | Transactional, with typing-run coalescing; every ribbon command is one step |
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
deleting rows and columns rewrites references across formulas, defined names, merged cells, conditional
formatting, data validation, hyperlinks, notes, the AutoFilter, tables and charts (anchors and series).
Chart, dialog and macro sheets are preserved on save but have no tab: the grid has nothing to draw for
them. Deleting a worksheet drops any defined name scoped to it, which is what Excel does. **Dynamic
arrays are not supported** — UNIQUE, SORT, FILTER and spilled ranges need a grid that holds values it
was not asked to store; the engine computes one value per formula cell.

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

The formatting bar is a [Ribbon](ribbon.md) on both hosts, organised the way Excel's is:

| Tab | Groups |
|---|---|
| **File** | The application button. Opens the built-in backstage (see [The Excel window](#the-excel-window)) and raises `FileMenuRequested`. |
| **Home** | Clipboard · Font (with the **Borders** dropdown — edges, line style, line colour) · Alignment (with **Merge & Center**) · Number (with More Number Formats…) · **Styles** (Conditional Formatting, Format as Table, Cell Styles) · **Cells** (Insert, Delete, Format — row height, column width, hide/unhide, Format Cells…) · Editing (AutoSum, Fill, Clear, Sort & Filter, Go To) · Find |
| **Insert** | Table · Charts (column, bar, line, pie, area) · Link · Note · Watermark |
| **Formulas** | Function Library (Insert Function, AutoSum, one menu per category) · Defined Names (Name Manager, Define Name, Use in Formula) · Calculation (Calculate Now, Show Formulas) |
| **Data** | Sort & Filter (A→Z, Z→A, Sort…, Filter, Clear, Reapply) · Data Tools (Data Validation) |
| **Review** | Notes (New/Edit, Delete, Previous, Next, Show All Notes) |
| **View** | Show (Gridlines, Headings, Formula Bar, Show Formulas) · Zoom (Zoom…, 100%, Zoom to Selection) · Window (Freeze Panes) |

Clipboard leads Home, as it does in Excel, because cut/copy/paste apply to whatever is selected and
are reached far more often than any formatting command. AutoSum is on both Home and Formulas, as it is
in Excel, because it is the one command reached often enough that a tab switch in front of it would be
felt.

Two things the strip could not do:

- **The ad-hoc dropdowns became real ribbon items.** Number formats is a `RibbonMenuButton` and AutoSum a `RibbonSplitButton` — the face still writes SUM, the chevron still offers average, count, min and max. That deleted a hand-written backdrop
  div, an absolutely-positioned panel and a `bool …Open` field per menu on Blazor, and an action sheet
  per menu on MAUI — along with their dismissal, keyboard and edge-flipping behaviour, which the
  ribbon already has.
- **Commands are grouped and captioned** instead of separated by anonymous hairlines.

Undo and redo sit outside the tabs, so they never move or disappear: in the window's title bar when
the shell is on (`ShowShell` with `ShowTitleBar`), in the ribbon's quick access row otherwise.

**The tab strip is on** — the strip is the only way to reach anything past Home. Setting
`ShowTabs="false"` on Blazor does not hide the other tabs' commands: it folds those groups back onto the
one tab, where the ribbon's own collapsing deals with the width. A setting that quietly removed most of
the bar would be a worse bargain than a crowded one. MAUI shows the strip either way;
`Ribbon.ShowTabStrip` is the equivalent switch there.

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

Home ▸ **Cells** carries insert and delete for both axes, as Excel's does. **Insert** offers Insert
Sheet Rows / Insert Sheet Columns / Insert Sheet, **Delete** the matching three. They act on the
selection — `InsertRows(count)` opens blank rows *above* it and pushes everything below down,
`InsertColumns(count)` opens columns to its *left*. Both carry the sheet along with the band: formulas
(on every sheet), defined names, merged cells, conditional formatting, data validation, hyperlinks,
notes, the AutoFilter, tables and charts (anchors and series). `DeleteRows` and `DeleteColumns` close
the gap; a formula that pointed *into* the removed band becomes `#REF!`, as it does in Excel, and undo
puts both the band and those formulas back.

**Format** in the same group is the size and visibility half: Row Height…, AutoFit Column Width,
Column Width…, a **Default Width** submenu of fixed widths (including the sheet's own default — the only
way back once a column has been dragged or fitted), hide and unhide for rows and columns, all recorded
in the file, and Format Cells….

The functions live in two places. Home ▸ Editing's **AutoSum** split button writes SUM on its face and
offers AVERAGE, COUNT, MIN and MAX on its chevron; the **Formulas** tab's Function Library has AutoSum
again plus one menu per function category and Insert Function. Each picks its own range the way AutoSum
does — the run above, the run to the left, or one total per column of a block.

## Excel features

Every ribbon command is also a controller method, one undo step each, written into the file where
Excel expects it — worksheet children are inserted in `CT_Worksheet` schema order (`SheetXml`), since a
`mergeCells` after `conditionalFormatting` saves fine and then opens in Excel as a repair.

```csharp
controller.MergeCells(MergeMode.MergeAndCenter);      // MergeAcross, MergeCells; UnmergeCells()
controller.FreezeTopRow();                            // FreezePanes(), FreezeFirstColumn(), UnfreezePanes()
controller.ApplyBorders(BorderPreset.ThickOutside);   // with controller.BorderLine for style and colour
controller.SortAscending();                           // current region, header detected
controller.Sort([new SortKey(2, Descending: true), new SortKey(0)], hasHeader: true);
controller.ToggleAutoFilter();                        // Ctrl+Shift+L
controller.ApplyColumnFilter(sheet.AutoFilter!, 1, ColumnFilter.ForValues(1, ["North"]));
controller.FillDown();                                // Ctrl+D; FillRight(), AutoFillTo(range)
controller.AddConditionalFormat(ConditionalFormatRule.CellIs(ConditionalOperator.GreaterThan, DxfFormat.LightRedFill, "100"));
controller.SetValidation(DataValidationRule.ForList(["Red", "Green", "Blue"]));
controller.ApplyCellStyle(CellStylePresets.Find("Good")!);
controller.FormatAsTable("TableStyleMedium2");
controller.InsertChart(ChartKind.Column, "Units");
controller.SetNote("Check this");
controller.SetHyperlink(new CellHyperlink(cell) { Address = "https://shinylib.net" });
controller.DefineName("Sales", "Data!$B$2:$B$13");    // =SUM(Sales) works and recalculates
```

**Filters write both halves.** An AutoFilter is the `<autoFilter>` element *and* `hidden="1"` on each
rejected row. Excel does not re-run a filter on open — it trusts the hidden rows — so the two are one
command, and undo restores both.

**Notes need their VML.** A note is text in the comments part plus a hidden shape in a VML drawing;
without the shape Excel keeps the note and never shows it. Both are rewritten together.

**Charts** are real DrawingML (`c:chartSpace` in a drawing, anchored to cells), drawn by a Skia
`ChartPainter` over the grid. Click selects, drag moves, the corner handles resize, Delete removes —
each one undo step. Charts from Excel files render with default styling; only type, series and title
are read. Series references follow inserted and deleted rows.

**Format as Table** writes a table part. A table's header cells *are* its column names — Excel refuses
a file where they differ or repeat — so blank or duplicate headers become `Column1`, `Column2`… in the
same step. The 60 built-in table styles are not in the file; the painter derives each from its family
(Light/Medium/Dark) and accent, close enough to read as the style chosen.

**Cell Styles** apply the style's formatting directly rather than as a named `cellStyles` entry, so the
cell looks right in Excel but does not remember it was "Good".

**Dialogs are data.** Format Cells (Ctrl+1), Data Validation, the Highlight Cells prompts, Insert
Function, Name Manager, Hyperlink, Note, Sort, Filter, Create Table, Go To, Zoom, Row Height and Column
Width are each a `SheetDialog` built once in the kernel (`SpreadsheetDialogs`), with its validation and
the command it runs. Each host has one generic renderer, so the two cannot disagree about what a field
means. The right-click menu and a validated cell's dropdown work the same way (`SheetMenuRequest`).

**Keyboard.** `controller.HandleKey(key, modifiers)` is Excel's shortcut table for both hosts — Ctrl+arrow
to the region edge, Ctrl+Shift+arrow to extend, Ctrl/Shift+Space, Ctrl+A, F2, Ctrl+; and Ctrl+Shift+:,
Alt+=, Ctrl+1, Ctrl+B/I/U, Ctrl+D/R, Ctrl+K, Ctrl+` and the rest. Blazor wires it, and in the shell
Ctrl/Cmd+S saves and Ctrl/Cmd+P prints. On MAUI call `SpreadsheetView.HandleKey` from a platform key
hook: MAUI has no portable key-down event and the Office package ships no physical-key hook (the
`DocumentEditor` has the same seam and the same gap), so without one a MAUI host gets no shortcuts.

**Formula autocomplete.** Typing `=SU` in a cell or the formula bar drops a list of matching functions
and defined names; inside a call, a tip shows its signature. It is `FormulaAssist`, shared by both hosts.

### Zoom and the status bar

`Zoom` (0.1–4) scales everything, headings included. Ctrl+wheel zooms on Blazor, pinch on MAUI.
`SelectionStatistics` is what Excel's status bar shows — Average, Count, Numerical Count, Min, Max,
Sum — computed over the visible cells of the selection, so a filtered column sums what is on screen.
The built-in status bar (see [The Excel window](#the-excel-window)) already shows it and drives
`Zoom`; the members below are for a host that turned the shell off and draws its own.

```csharp
// MAUI: Zoom is a two-way bindable property; the rest are events
view.Zoom = 1.5;
view.ZoomChanged += (_, zoom) => { };
view.SelectionStatisticsChanged += (_, _) => status.Text = $"Sum: {view.SelectionStatistics.Sum}";
view.FileMenuRequested += (_, _) => ShowBackstage();
```

```razor
<SpreadsheetView Workbook="workbook"
                 @bind-Zoom="zoom"
                 SelectionStatisticsChanged="stats => this.stats = stats"
                 FileMenuRequested="ShowBackstage" />
```

A host passes pointer positions in its own units; the controller divides by the zoom. Position an
overlay on a cell with `controller.EditorBounds` / `controller.ToScreen(...)`, which are already zoomed.

## Accent

The bar wears Excel green (`#107C41`) by default — see
[Document Editor ▸ Accent](document-editor.md#accent) for how the three controls are coloured and how
to set your own.

## Watermarks

`Watermark` draws a picture behind the content, on the viewer as well as the editor. The button picks
one through the same path as inserting a picture. See
[Document Editor ▸ Watermarks](document-editor.md#watermarks) — including why it is a display
watermark rather than one written into the file.
