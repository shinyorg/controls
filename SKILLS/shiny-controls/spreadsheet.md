# Spreadsheet (xlsx viewer & editor)

`SpreadsheetView` opens, renders and edits `.xlsx` workbooks on both MAUI and Blazor.

**Packages**

| Package | Host |
|---|---|
| `Shiny.Controls.Office.Shared` | the document kernel — no UI dependency |
| `Shiny.Controls.Office.Skia` | the shared SkiaSharp paint layer |
| `Shiny.Maui.Controls.Office` | `SpreadsheetView` for MAUI |
| `Shiny.Blazor.Controls.Office` | `<SpreadsheetView>` for Blazor **WebAssembly** |

## Two hard constraints

1. **Blazor WASM only.** The grid repaints on every keystroke; a Blazor Server round-trip per key makes
   editing unusable. Also, SkiaSharp on WASM forces native relinking, so consumers need the
   `wasm-tools` workload installed.
2. **MAUI needs `UseShinyOffice()`** in `MauiProgram`, or the canvas never renders. It calls
   `UseSkiaSharp()` for you and, on the macOS AppKit head (`net10.0-macos`), registers the Skia canvas
   SkiaSharp does not ship for that head — without it every Office control renders blank there.

```csharp
builder
    .UseMauiApp<App>()
    .UseShinyControls()
    .UseShinyOffice();        // required - registers SkiaSharp + the AppKit canvas
```

## Opening a workbook

```csharp
using var workbook = await Workbook.OpenAsync("/path/to/book.xlsx");
// or from a stream:
using var workbook = await Workbook.OpenAsync(stream);
// or start empty:
using var workbook = Workbook.Create("Sheet1");
```

`Workbook` is `IDisposable` and holds the package open. Dispose it when the page goes away.

### MAUI

```xml
<office:SpreadsheetView x:Name="Sheet"
                        Workbook="{Binding Workbook}"
                        SheetName="Budget"
                        CellChanged="OnCellChanged" />
```

### Blazor

```razor
<div style="height:420px">
    <SpreadsheetView Workbook="workbook"
                     CellChanged="OnCellChanged" />
</div>
```

The Blazor host paints to a canvas that fills its container, so **the container needs an explicit
height** — without one it collapses to zero and nothing appears.

### The Excel window (Office shell) — on by default

`SpreadsheetView` is Excel's whole window, not just a grid: an `OfficeShell` (see
[office-shell.md](office-shell.md)) dressed as `OfficeApp.Excel` with

- the green **title bar** — AutoSave, Save / Undo / Redo, the workbook name (rename dropdown), save
  status ("Unsaved changes" / "Saving…" / "Saved" / "Saved locally"), and the command search (every
  ribbon command, with its shortcut, plus Save / New Workbook / Export to PDF / Export to CSV / Print /
  Comments / the three views; a query matching no command searches every sheet);
- the **ribbon** with **File** opening the backstage and **Comments / Editing mode / Share** at its end
  (Viewing mode makes the workbook read-only);
- formula bar, grid and sheet tabs as before;
- the **Comments pane** on the right: every note in the workbook (`Sheet!Cell`, author, text); a click
  selects the cell, switching sheets;
- the **status bar**: Ready / Enter / Edit, "Average: x  Count: n  Sum: y" (hidden unless
  `SelectionStatistics.IsMeaningful`), Normal / Page Layout / Page Break Preview, and a zoom slider
  (10–400%) bound to `Zoom`;
- the **backstage**: templates (blank, Monthly budget, Invoice, Weekly schedule — built in code by
  `SpreadsheetTemplates`), recent files (host-supplied), Info with statistics (sheets, cells with data,
  formulas, notes), Save, Save As / Export (xlsx, CSV of the active sheet, PDF), Print.

Switches (all default `true`): `ShowShell` (false = the old ribbon + formula bar + grid + tabs),
`ShowTitleBar`, `ShowStatusBar`, `ShowBackstage`, `ShowCommentsPane`, and on Blazor `ShowRibbonActions`.
`ShowToolbar`, `ShowFormulaBar`, `ShowSheetTabs` still work.

The shell reads and writes **no files**. Save (title bar, backstage, Ctrl+S), Save As, Export and Print
raise **`FileRequested`** with a `SpreadsheetFileRequest` (`Format`, `FileName`, `Action`
Save/SaveAs/Export/Print, `Sheet`, `WriteToAsync(stream)`, `ToBytesAsync()`). On **Blazor**, leaving it
unhandled downloads the file in the browser (Print opens the PDF in a new tab); on **MAUI** it must be
handled for anything to be written:

```csharp
sheet.FileRequested += async (_, request) =>
{
    var path = Path.Combine(FileSystem.AppDataDirectory, request.FileName);
    await using var file = File.Create(path);
    await request.WriteToAsync(file);
};
```

```razor
<SpreadsheetView @bind-Workbook="workbook" @bind-Zoom="zoom"
                 DocumentName="Budget" UserName="Allan Ritchie" RecentFiles="recent"
                 FileRequested="SaveAsync" />
```

Other shell members: `DocumentName` (two-way), `UserName` (avatar + author of new notes), `Templates`,
`RecentFiles`, `SaveState` (Blazor override), `AutoSave` (Blazor; saves 2 s after the last edit when
`FileRequested` is handled), events `TemplateSelected` (handle it to build templates yourself —
unhandled, the view builds the workbook, shows it and raises Blazor `WorkbookChanged` /
MAUI `WorkbookReplaced`; use `@bind-Workbook` on Blazor), `OpenRequested`, `RecentFileSelected`,
`ShareRequested`. `Commands` is the `OfficeCommandIndex` behind the search — add your own commands to
it. MAUI also exposes `Shell`, `TitleBar`, `StatusBar`, `Backstage`, `GoToNote(...)` and `SaveAsync()`.
`FileMenuRequested` is still raised by File (after the backstage opens).

Shared helpers for custom chrome: `SpreadsheetShell.Aggregates(stats)`, `.ModeText(controller.EditMode)`,
`.Notes(workbook)`, `.GoTo(controller, note)`, `.Search(controller, text)`, `.DocumentInfo(workbook, name)`,
`.ToCsv(workbook, sheet)`; `SpreadsheetExport.WriteAsync(workbook, sheet, format, stream)` (xlsx / csv /
pdf — PDF paints the used range with the grid's own painter onto Letter pages, no headings, gridlines
or selection); `controller.ViewMode` (`SheetViewMode.Normal/PageLayout/PageBreakPreview`) and
`controller.PageLayout` (`SheetPagination`).

## Editing

All edits go through the undo stack. Never mutate cells directly.

```csharp
workbook.Execute(new SetCellValueCommand("Budget", CellRef.Parse("B2"), CellValue.FromNumber(42)));
workbook.Execute(new SetCellFormulaCommand("Budget", CellRef.Parse("D2"), "B2*C2"));
workbook.Execute(new ClearRangeCommand("Budget", CellRange.Parse("A1:C3")));

workbook.Undo.Undo();
workbook.Undo.Redo();
```

`ClearRangeCommand` is one undo step for the whole range, not one per cell.

## Formulas

The calc engine indexes formulas lazily on the first edit or the first read of a calculated value.

```csharp
workbook.GetEffectiveValue("Budget", CellRef.Parse("D5"));   // computed result
workbook.Evaluate("SUM(A1:A9)", "Budget", CellRef.Parse("Z1")); // ad-hoc, not stored
workbook.Calc.CircularCells;   // non-empty when the sheet has a circular reference
```

About 140 functions are implemented across financial, logical, text, date & time, lookup & reference,
math & trig, statistical and information categories — including XLOOKUP, XMATCH, SUBTOTAL, AGGREGATE,
SUMPRODUCT, IFERROR/IFNA, AVERAGEIFS/MAXIFS/MINIFS, TEXTJOIN, CONCAT and PMT, PV, FV, NPV, IRR, RATE,
NPER, IPMT, PPMT, SLN. Unknown functions evaluate to `#NAME?` rather than throwing.
`FunctionCatalog` describes every one (category, arguments, description) — it is what the Insert
Function dialog and autocomplete read; `FunctionRegistry.Default` is what evaluates.

**No dynamic arrays.** UNIQUE, SORT, FILTER and spilled ranges are not supported — the engine holds one
value per formula cell. An XLOOKUP whose return range has several columns returns the first.

Defined names work in formulas (`=SUM(Sales)`), including recalculation when a cell inside the named
range changes:

```csharp
controller.DefineName("Sales", "Budget!$B$2:$B$13");            // throws on a name Excel would refuse
controller.RenameName("Sales", scope: null, "Revenue", "Budget!$B$2:$B$13");
controller.DeleteName("Revenue");
workbook.DefinedNames;                                           // IReadOnlyList<DefinedNameInfo>
workbook.VisibleNames;                                           // without Excel's _xlnm.* and hidden ones
controller.GoTo("Sales");                                        // the name box: names, ranges, Sheet2!B5
DefinedNameRules.IsValid(name, out var error);
```

Typing a new name into the name box with a range selected defines it, as in Excel.

**Reading a value: use `GetEffectiveValue` / `GetDisplayValue`, not `Worksheet.GetValue`.**
`GetValue` returns what is stored in the file, which for a formula cell is the cached result and is
stale the moment anything upstream changes.

## Reading and formatting

```csharp
var sheet = workbook["Budget"];
sheet.UsedRange;                       // bounding box of populated cells, or null
sheet.GetFormula(cell);                // formula text without the leading '='
sheet.GetDisplayValue(cell);           // computed value

// GetEffectiveStyleIndex, not GetStyleIndex: a cell formatted through its column or row carries no
// style of its own, and the plain getter reports it as unformatted.
var format = workbook.Styles.Resolve(sheet.GetEffectiveStyleIndex(cell));
var text = workbook.Styles.Format(sheet.GetDisplayValue(cell), format);
```

## Formatting

`ShowToolbar` puts the built-in Excel ribbon above the formula bar. **It is ON by default** (it used to
be off — set `ShowToolbar="false"` for a read-only viewer that should not grow a ribbon).

```xml
<office:SpreadsheetView Workbook="{Binding Workbook}" ShowToolbar="False" />
```

```razor
<SpreadsheetView Workbook="workbook" ShowToolbar="false" />
```

The ribbon is organised like Excel's:

- **File** — the application button. Opens the built-in backstage (see *The Excel window* above) and
  raises `FileMenuRequested`; with `ShowBackstage="false"` it only raises the event.
- **Home** — Clipboard; Font (incl. text colour, fill, the **Borders** dropdown with line style and
  colour); Alignment (incl. **Merge & Center** split button); Number (formats, currency, percent,
  decimals, More Number Formats…); **Styles** (Conditional Formatting, Format as Table, Cell Styles);
  **Cells** (Insert / Delete / Format — row height, column width, hide/unhide, Format Cells…);
  Editing (AutoSum, Fill, Clear, Sort & Filter, Go To); Find.
- **Insert** — Table, Charts (column, bar, line, pie, area), Link, Note, Watermark.
- **Formulas** — Insert Function, AutoSum, one menu per Function Library category, Name Manager,
  Define Name, Use in Formula, Calculate Now, Show Formulas.
- **Data** — Sort A→Z / Z→A / custom Sort, Filter, Clear, Reapply, Data Validation.
- **Review** — New/Edit Note, Delete, Previous/Next, Show All Notes.
- **View** — Gridlines, Headings, Formula Bar, Show Formulas; Zoom…, 100%, Zoom to Selection;
  Freeze Panes.

Extra items go in `ToolbarContent` (Blazor) or `Toolbar.ToolbarItems` (MAUI); they land in their own
never-collapsing group on **Home**, so they are on the tab that opens.

Everything it does is also on the controller, so a host can drive the same commands from its own
chrome:

```csharp
var controller = view.Controller;

controller.ToggleBold();            // ToggleItalic, ToggleUnderline, ToggleStrikethrough, ToggleWrapText
controller.SetFontFamily("Cambria");
controller.SetFontSize(14);
controller.SetTextColor(new ArgbColor(255, 0xC0, 0x00, 0x00));
controller.SetFillColor(new ArgbColor(255, 0xFF, 0xEB, 0x3B));   // null removes the fill
controller.SetAlignment(CellHorizontalAlignment.Center);         // same value again returns to General
controller.SetVerticalAlignment(CellVerticalAlignment.Center);
controller.AdjustIndent(+1);
controller.ClearFormatting();       // formatting only; the contents stay

controller.ActiveFormat;            // ResolvedFormat for the active cell — what a toolbar shows
controller.CanUndo;                 // and CanRedo
```

**Formatting is a delta, not an assignment.** `CellFormatChange` names only what changes, so bolding a
range that mixes colours leaves each cell's colour alone. Applying one to a range is a single undoable
command:

```csharp
workbook.Execute(new FormatRangeCommand("Budget", CellRange.Parse("A1:D1"), new CellFormatChange
{
    Bold = true,
    Background = new ArgbColor(255, 0xFF, 0xEB, 0x3B)
}));
```

Do **not** write a style index onto a cell directly. Resolve, fold the change in, intern, write:
`Workbook.Styles` reads a style index into a `ResolvedFormat` and `Workbook.StyleWriter.Intern` turns
one back into an index. Interning is what keeps the styles part from growing one entry per formatted
cell.

### Number formats

```csharp
controller.SetNumberFormat(NumberFormatPreset.Currency);   // culture-aware symbol and placement
controller.SetNumberFormatCode("#,##0.00;[Red](#,##0.00)");
controller.AdjustDecimals(+1);                             // General becomes 0.0
```

Presets: `General`, `Number`, `Currency`, `Percent`, `Scientific`, `ShortDate`, `Time`, `Text`.
`NumberFormats.PresetOf(code)` returns null for a code no preset produces — show nothing selected
rather than guessing.

### Auto formulas

```csharp
controller.ApplyAutoFunction(AutoFunction.Sum);   // Average, Count, Min, Max
```

Returns **false** when there is nothing to total, and writes nothing — do not assume it always acts.
Where the formula goes and what it covers follows Excel:

- One cell selected: the run of numbers immediately above it, else the run to its left, with the result
  in that cell. A cell already holding SUM/AVERAGE/COUNT/MIN/MAX ends the run, so a second total does
  not double-count.
- A single row or column selected: the total goes just past the end — or into the last cell when that
  cell is empty, which is what selecting the numbers *and* the blank below them means.
- A block selected: one total per column, in the row underneath.

`AutoFunctions.Plan(sheet, range)` exposes the same plan without writing anything.

## Formatting columns and rows

A selection made from a **column header** is written as a column style, not as a million cell styles:

```csharp
controller.Selection.SelectColumn(2);
controller.SetNumberFormat(NumberFormatPreset.Currency);   // applies to C1:C1048576, including empty rows
```

That is the only way a format reaches rows that do not exist yet. Row-header selections behave the
same. A cell's own style still wins over its row's, which wins over its column's — read the effective
one with `Worksheet.GetEffectiveStyleIndex(cell)`, **not** `GetStyleIndex`, which returns only the
cell's own and shows an unformatted column.

Column widths and row heights are recorded in the file:

```csharp
controller.SetColumnWidth(180);        // pixels, for the selected columns
controller.AutoFitColumns();           // approximate: character counts, not measured text
controller.SetColumnsHidden(true);

workbook.Execute(new SetColumnWidthCommand("Budget", first: 1, last: 3, characters: 24.5));
workbook.Execute(new SetColumnStyleCommand("Budget", 1, 1, styleIndex));
workbook.Execute(new SetRowHeightCommand("Budget", row: 0, points: 24));
```

Dragging a column-header edge in the grid commits the same command, so a hand-dragged width survives a
save.

## Excel features on the controller

Everything the ribbon does is a controller method, each **one undo step**, each written into the file
in the place Excel expects (worksheet children are kept in `CT_Worksheet` order — see `SheetXml`).

```csharp
// Merge
controller.MergeCells(MergeMode.MergeAndCenter);   // MergeAcross, MergeCells; keeps only the top-left value
controller.UnmergeCells();
controller.ToggleMergeAndCenter();
controller.IsActiveCellMerged;

// Freeze panes (writes <sheetViews><pane state="frozen">)
controller.FreezePanes();          // at the active cell
controller.FreezeTopRow();
controller.FreezeFirstColumn();
controller.UnfreezePanes();

// Borders — interned in styles.xml <borders>
controller.BorderLine = new BorderEdge(CellBorderStyle.Medium, new ArgbColor(255, 0, 0x70, 0xC0));
controller.ApplyBorders(BorderPreset.Outside);   // Bottom, Top, Left, Right, None, All, ThickOutside, DoubleBottom…

// Sort and filter
controller.SortAscending();                      // current region, header detected (SheetSort.DetectHeader)
controller.Sort([new SortKey(Column: 2, Descending: true), new SortKey(0)], hasHeader: true);
controller.ToggleAutoFilter();                   // Ctrl+Shift+L
controller.ApplyColumnFilter(sheet.AutoFilter!, column: 1,
    ColumnFilter.ForCondition(1, new FilterCondition(FilterOperator.GreaterThan, "100")));
controller.ApplyColumnFilter(filter, 0, ColumnFilter.ForValues(0, ["North", "East"]));
controller.ClearFilters();
controller.ReapplyFilters();

// Fill
controller.FillDown();                           // Ctrl+D
controller.FillRight();                          // Ctrl+R
controller.AutoFillTo(CellRange.Parse("A1:A20"));   // what dragging the fill handle does: series, weekdays, "Q1"→"Q2", rebased formulas

// Conditional formatting (<conditionalFormatting> + <dxfs>)
controller.AddConditionalFormat(ConditionalFormatRule.CellIs(ConditionalOperator.GreaterThan, DxfFormat.LightRedFill, "100"));
controller.AddConditionalFormat(ConditionalFormatRule.TopBottom(10, percent: false, bottom: false, DxfFormat.GreenFill));
controller.AddConditionalFormat(ConditionalFormatRule.Bar(ConditionalPresets.DataBars[0].Color));
controller.AddConditionalFormat(ConditionalPresets.ColorScales[0].Rule);
controller.ClearConditionalFormats(entireSheet: false);

// Data validation (<dataValidations>)
controller.SetValidation(DataValidationRule.ForList(["Red", "Green", "Blue"]));
controller.SetValidation(DataValidationRule.ForNumber(ValidationType.Whole, ValidationOperator.Between, "1", "10"));
controller.SetValidation(null);                  // clears
controller.ValidationFailed += (_, failure) => { };   // refused input never reaches the cell

// Cell styles and tables
controller.ApplyCellStyle(CellStylePresets.Find("Good")!);
controller.FormatAsTable("TableStyleMedium2");   // writes a table part + <tableParts>; filter arrows included
controller.ConvertTableToRange();

// Charts (DrawingML chart part in a drawing, anchored to cells)
var id = controller.InsertChart(ChartKind.Column, title: "Sales");   // from the selection / current region
controller.SelectedChartId = id;
controller.DeleteSelectedChart();                // or Delete with the chart selected

// Notes (legacy comments part + the VML Excel needs to show them)
controller.SetNote("Check this");
controller.DeleteNote();
controller.ShowAllNotes = true;

// Hyperlinks
controller.SetHyperlink(new CellHyperlink(cell) { Address = "https://shinylib.net", Display = "Shiny" });
controller.SetHyperlink(new CellHyperlink(cell) { Location = "Sheet2!A1" });
controller.HyperlinkActivated += (_, url) => { /* open it — the host's job */ };

// View (saved in the sheet's <sheetView>)
controller.ShowGridlines = false;
controller.ShowHeadings = false;
controller.ToggleShowFormulas();                 // Ctrl+`

// Zoom and the status bar
controller.Zoom = 1.25;                          // 0.1 – 4; ZoomIn()/ZoomOut() step Excel's stops
controller.ZoomChanged += (_, zoom) => { };
var stats = controller.SelectionStatistics;      // Average, Count, NumericalCount, Min, Max, Sum
controller.SelectionStatisticsChanged += (_, _) => { };
```

**Zoom and pointer coordinates.** Pass pointer positions in the host's own units; the controller divides
by the zoom. Position an in-cell editor with `controller.EditorBounds` (already zoomed) and
`controller.EditorFontSize`, never with `Viewport.CellRect` directly.

**Views expose the status-bar members too:** `SpreadsheetView.Zoom` (MAUI bindable, two-way; Blazor
`Zoom` + `ZoomChanged`), `SelectionStatistics` and `SelectionStatisticsChanged`.

### Dialogs are data

Every dialog — Format Cells (Ctrl+1), Data Validation, the Highlight Cells prompts, Insert Function,
Name Manager, Hyperlink, Note, Sort, Filter, Create Table, Go To, Zoom, Row Height, Column Width — is a
`SheetDialog` built in the kernel (`SpreadsheetDialogs`) and rendered by one generic component per
host. A command that needs input raises `controller.DialogRequested`; the views render it for you. To
open one yourself:

```csharp
controller.ShowDialog(SpreadsheetDialogs.FormatCells(controller));
controller.ShowDialog(SpreadsheetDialogs.Conditional(controller, ConditionalDialogKind.GreaterThan));
```

Popup menus (right-click, a validated cell's dropdown) arrive as `controller.MenuRequested`
(`SheetMenuRequest` of `SheetMenuItem`); the views render those too. Right-click / long-press calls
`controller.OpenContextMenu(x, y)`.

### Keyboard

`controller.HandleKey(key, modifiers)` is Excel's shortcut table for both hosts — `key` named the way
a browser's `KeyboardEvent.key` names it. Blazor wires it for you (plus Ctrl/Cmd+S = Save and
Ctrl/Cmd+P = Print in the shell); on MAUI call `SpreadsheetView.HandleKey` from your platform key hook —
the Office package has no MAUI physical-key hook (neither does `DocumentEditor`), so a MAUI host without
one gets no keyboard shortcuts. Covered: arrows (Ctrl = to edge, Shift =
extend), Tab/Enter, Home/Ctrl+Home/Ctrl+End, PageUp/Down, Ctrl+PageUp/Down (sheets), F2, Shift+F2 (note),
Shift+F3 (insert function), Ctrl+F3 (names), F5/Ctrl+G, F9, Delete, Escape, Ctrl+Space / Shift+Space,
Ctrl+A (region, then all), Ctrl+C/X/V/Z/Y, Ctrl+B/I/U/5, Ctrl+D/R, Ctrl+K, Ctrl+1, Ctrl+9/0,
Ctrl+; (date), Ctrl+Shift+: (time), Ctrl+` (formulas), Ctrl+Shift+L (filter), Ctrl+Shift+$ % & _,
Alt+= (AutoSum), Alt+Down (list), Shift+F10 (context menu).

### Formula autocomplete

`FormulaAssist.Analyze(text, caret, definedNames)` returns the suggestions and the signature of the
call the caret is in; `FormulaAssist.Accept(...)` applies one. Both views' in-cell editor and formula
bar use it — do not build another.

## Driving the grid from a toolbar

Both hosts expose the same `SpreadsheetController`:

```csharp
var controller = view.Controller;   // MAUI: Sheet.Controller, Blazor: view.Controller
controller.Selection.Active;        // CellRef
controller.ActiveCellText;          // what a formula bar should show
controller.BeginEdit();
controller.Move(MoveDirection.Down, extend: false, toEdge: true);   // Ctrl+Down
controller.ClearSelection();
controller.Undo();
```

## Find

**Home ▸ Find** — the same `OfficeFindBar` the document editor carries, over the same
`IFindController`. See `document-editor.md` ▸ **Find** for the API and the rules it shares.

```csharp
var find = controller.Find;         // SpreadsheetFinder : IFindController

find.SearchAllSheets = true;        // off by default: the active sheet only, as in Excel
find.Query = "Q1";                  // searches and steps onto the first hit at or after the active cell
find.FindNext();                    // switches sheets when the hit is on another one
find.Matches;                       // IReadOnlyList<SpreadsheetFindMatch> (Sheet name, CellRef, Start, Length)

controller.FindMatchCells();        // cells to wash on the showing sheet — what the painter takes
```

Workbook-specific behaviour:

- What is searched is the cell text **as the formula bar shows it** — the formula when there is one,
  otherwise the literal. Not the formatted value: `1234` would miss a cell showing `1,234.00`.
- Matches are collected in **book order**, never active-sheet-first — ordering around the showing
  sheet re-orders the list every time "next" crosses a boundary, which walks two sheets forever.
- **Hidden sheets are never searched**, even with `SearchAllSheets`.
- Stepping calls `GoTo`, so the cell is selected and scrolled into view; the wash covers **whole
  cells**, because a cell is the smallest thing a selection can address.

## Saving

```csharp
await workbook.SaveAsync();                 // back over the path it was opened from
await workbook.SaveAsAsync("/new/path.xlsx");
await workbook.SaveToAsync(stream);
var bytes = workbook.ToArray();
```

Saving writes atomically (temp file plus move), refreshes the cached results of every formula, and
sets `fullCalcOnLoad` so Excel re-verifies the numbers when it opens the file.

**An unmodified workbook is never rewritten** — opening and saving without an edit produces a
byte-identical file.

## What is preserved, and what is not

Edits are applied surgically to the open OOXML package. Parts the editor does not model — macros,
tracked changes, custom XML, pivot caches, embedded objects, sparklines, x14 extensions — are never
touched and survive a save intact. Conditional-format blocks, validation rules and charts the model does
not understand are kept as they were when the sheet's other rules are edited.

Pass an `UnsupportedFeatureCollector` when opening to find out what a document contains that the
editor cannot show or edit:

```csharp
var collector = new UnsupportedFeatureCollector();
using var workbook = await Workbook.OpenAsync(path, collector);

foreach (var feature in collector.Features)
    Console.WriteLine($"{feature.Part}: {feature.Feature} ({feature.Severity})");
```

## Not implemented yet

Do not generate code that assumes these exist:

- **Dynamic arrays** (UNIQUE, SORT, FILTER, spill ranges).
- **Row auto-height.** Wrapped text paints on several lines inside the row's height; rows do not grow.
- Chart *editing* beyond insert, move, resize and delete — series, axes and chart styles cannot be
  changed; charts from Excel render with default styling. Pivot tables are preserved, not shown.
- Named cell styles: the Cell Styles gallery applies the style's formatting directly rather than
  writing a `cellStyles` entry.
- Threaded comments (Excel 365's "Comments"); the control reads and writes Notes.
- Multi-range ("Ctrl-click") selection; the OS clipboard (the control keeps its own).
- **Replace.** Find is implemented (see **Find**); replacing what it finds is not.

### Dark mode

`Theme` is nullable; **leave it unset** and the grid, formula bar, toolbar and sheet tabs follow the
host's light/dark scheme live. Pass `SpreadsheetTheme.Light` / `.Dark` only to pin one.

- On MAUI a **pinned** theme carries the whole chrome with it: the formula bar inks its boxes from the
  theme, and the toolbar scopes the app theme pack's matching light/dark token palette over the ribbon,
  so a pinned `SpreadsheetTheme.Dark` in a light app gets a dark ribbon too.
- On Blazor a **pinned** theme puts `shiny-theme-dark` / `shiny-theme-light` on the view's root, so the
  toolbar/ribbon, pickers, formula bar and sheet tabs re-derive their `--shiny-color-*` tokens to match
  the grid (same for `DocumentEditorView`, `SlideEditorView`, `NotebookEditorView`). Do not wrap the
  view in your own scoping container just to theme the chrome.
- Text with no colour of its own on a **filled** cell is made to contrast with the fill (a light header
  fill keeps dark text in dark mode); an explicit font colour is left as authored.

### Toolbar

The bar is a [Ribbon](ribbon.md) on both hosts — titled groups, with undo/redo in the shell's title
bar (or the ribbon's quick access row when the shell or its title bar is off). You do not build any of it; it is what the control renders.

Do **not** hand-roll a formatting strip beside this control. Use `ToolbarContent` (Blazor) /
`ToolbarItems` (MAUI) to add your own commands — they land in their own group that never collapses.

The tab strip is on by default (Blazor `ShowTabs`, MAUI `Ribbon.ShowTabStrip`). Setting Blazor's
`ShowTabs="false"` does not remove the other tabs' commands — it folds those groups onto the single
tab. Below 600px the bar switches itself to `Simplified` — no code needed.

### Clipboard and structure

On the controller: `Cut()`, `Copy()`, `Paste()`, `ClearClipboard()`, `CanPaste`, `Clipboard`,
`ClipboardRange`, `ClipboardChanged`, and `InsertRows(count = 1)` / `InsertColumns(count = 1)` /
`DeleteRows` / `DeleteColumns` (Home ▸ Cells on the ribbon). A structural edit repoints formulas on
every sheet, defined names, merged cells, conditional formatting, data validation, hyperlinks, notes,
the AutoFilter, tables and charts (anchors and series) — no host fix-up needed.

`ClipboardRange` is the source range of the pending cut or copy, and the control draws the animated
dashed marching-ants border around it for you — do not draw your own, and do not repurpose
`SpreadsheetTheme.SelectionBorder` for it; the border has its own `ClipboardBorder` token precisely so
the two read as different things. `ClipboardChanged` is the event to hook if a host needs to react to
the clipboard filling or emptying; `Changed` also fires, but it fires on every keystroke as well.

The ribbon already carries every command on this page — do not add your own buttons for any of them.
With the shell on, the backstage and status bar are built in too; what is left for a host to wire is
where files go (`FileRequested`) and, optionally, recent files and its own templates.

## Touch

Both editable surfaces read a pointer's kind and behave differently under a finger:

- **Spreadsheet** — tap selects a cell, drag **pans** both axes, and a selection is extended by
  dragging one of the two round handles on its corners. Header presses still select and resize.
- **Document editor** — tap places the caret, drag **pans**, double/triple-tap select a word or
  paragraph, and the selection is adjusted by the handles under each end. Long-press opens the
  spelling menu.

Mouse behaviour is unchanged (drag extends, wheel scrolls) and the handles are not drawn for it. Do
not add a separate pan gesture or a scroll control on top of this — it is already there.

## Theming

`Theme` unset follows the host, and that means the app's **neutral tokens** — the grid's background,
text, grid lines and headers come from `Surface` / `OnSurface` / `OutlineVariant` / `SurfaceContainer`
/ `Outline`, so the grid and the ribbon above it share one ground. Semantic colours (selection green,
clipboard blue) are not themed. Do not set `Theme` just to get dark mode; it is already automatic.

`Watermark` (an `OfficeWatermark`) is on both the editor and the viewer - a picture drawn behind the
content, defaulting to a 0.15 wash. It is a **display** watermark: drawn, never written into the file,
because the three formats store one in three unrelated ways. The editors' watermark button uses the
same picker as inserting a picture.
