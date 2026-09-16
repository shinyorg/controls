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

Roughly 80 functions are implemented across math, statistics, logic, text, lookup, date and
information categories. Unknown functions evaluate to `#NAME?` rather than throwing.

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

`ShowToolbar` puts the built-in formatting bar above the formula bar. **It is off by default** — unlike
`ShowFormulaBar` and `ShowSheetTabs`, which are on.

```xml
<office:SpreadsheetView Workbook="{Binding Workbook}" ShowToolbar="True" />
```

```razor
<SpreadsheetView Workbook="workbook" ShowToolbar="true" />
```

The bar is a ribbon with two tabs.

- **Home** — clipboard; font/size, bold, italic, underline, strikethrough, text colour, cell fill;
  horizontal *and* vertical alignment, indent, wrap text; number formats and decimal places; AutoSum,
  clear contents, clear formatting.
- **Data** — insert and delete rows and columns; column width (fit on the button, four presets behind
  its chevron) and hide/unhide; a function button each for SUM, AVERAGE, COUNT, MIN and MAX.

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
tracked changes, custom XML, pivot caches, embedded objects, conditional formatting, charts — are
never touched and survive a save intact.

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

- **Insert/delete rows and columns.** Deliberately deferred: it requires rewriting references across
  formulas, merged cells, conditional formatting, defined names, data validation, charts and tables.
  (Formatting, resizing and hiding a column *are* supported — it is inserting and deleting that is not.)
- **Cell borders.** `ResolvedFormat` does not model them, so the toolbar cannot apply them and a
  file's existing borders are neither drawn nor lost.
- **Wrapped text rendering.** `ToggleWrapText` is stored and saved, and Excel honours it on open, but
  the grid still paints one line per cell — wrapping needs row auto-height, which the layout has not
  got.
- Adding or removing merged cells (existing merges render, but cannot be changed).
- Editing charts, pivot tables or conditional formatting.
- Multi-range ("Ctrl-click") selection.
- Copy/paste and the fill handle's drag-to-fill behaviour (the handle is drawn but inert).
- **Replace.** Find is implemented (see **Find**); replacing what it finds is not.
- Physical-key navigation on MAUI — MAUI has no portable key-down event, so arrow keys work on
  Blazor only. On MAUI, call `Move`/`BeginEdit`/`ClearSelection` from your own platform key hook.

### Dark mode

`Theme` is nullable; **leave it unset** and the grid, formula bar, toolbar and sheet tabs follow the
host's light/dark scheme live. Pass `SpreadsheetTheme.Light` / `.Dark` only to pin one.

- On MAUI a **pinned** theme carries the whole chrome with it: the formula bar inks its boxes from the
  theme, and the toolbar scopes the app theme pack's matching light/dark token palette over the ribbon,
  so a pinned `SpreadsheetTheme.Dark` in a light app gets a dark ribbon too.
- Text with no colour of its own on a **filled** cell is made to contrast with the fill (a light header
  fill keeps dark text in dark mode); an explicit font colour is left as authored.

### Toolbar

The bar is a [Ribbon](ribbon.md) on both hosts — titled groups, with undo/redo in the quick access
row. You do not build any of it; it is what the control renders.

Do **not** hand-roll a formatting strip beside this control. Use `ToolbarContent` (Blazor) /
`ToolbarItems` (MAUI) to add your own commands — they land in their own group that never collapses.

The tab strip is on by default (Blazor `ShowTabs`, MAUI `Ribbon.ShowTabStrip`). Setting Blazor's
`ShowTabs="false"` does not remove the Data commands — it folds those groups onto the single tab. Below
600px the bar switches itself to `Simplified` — no code needed.

### Clipboard and structure

On the controller: `Cut()`, `Copy()`, `Paste()`, `ClearClipboard()`, `CanPaste`, `Clipboard`,
`ClipboardRange`, `ClipboardChanged`, and `InsertRows(count = 1)` / `InsertColumns(count = 1)` /
`DeleteRows` / `DeleteColumns`.

`ClipboardRange` is the source range of the pending cut or copy, and the control draws the animated
dashed marching-ants border around it for you — do not draw your own, and do not repurpose
`SpreadsheetTheme.SelectionBorder` for it; the border has its own `ClipboardBorder` token precisely so
the two read as different things. `ClipboardChanged` is the event to hook if a host needs to react to
the clipboard filling or emptying; `Changed` also fires, but it fires on every keystroke as well.

The toolbar already carries cut, copy, paste, insert and delete for rows and columns, column width and
hide/unhide, clear contents, and the five aggregates — do not add your own buttons for any of them.
What is left for a host to wire is what has no affordance on the bar: row heights, `GoTo`, and
`SetSheetVisible`.

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
