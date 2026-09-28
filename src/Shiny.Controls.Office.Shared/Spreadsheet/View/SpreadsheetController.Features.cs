using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Spreadsheet.Calc;
using Shiny.Controls.Office.Spreadsheet.Commands;

namespace Shiny.Controls.Office.Spreadsheet.View;

/// <summary>How Merge &amp; Center's dropdown merges.</summary>
public enum MergeMode
{
    /// <summary>One merge over the whole selection, centred.</summary>
    MergeAndCenter,

    /// <summary>One merge per row of the selection.</summary>
    MergeAcross,

    /// <summary>One merge over the whole selection, alignment untouched.</summary>
    MergeCells
}

/// <summary>
/// What the status bar shows about the selection: Excel's Average, Count, Numerical Count, Min, Max and
/// Sum.
/// </summary>
/// <param name="Count">Non-blank cells — Excel's "Count", which is COUNTA.</param>
/// <param name="NumericalCount">Cells holding a number.</param>
/// <param name="Sum">The numbers added up; zero when there are none.</param>
/// <param name="Average">Their mean, or null with no numbers.</param>
/// <param name="Min">The smallest, or null with no numbers.</param>
/// <param name="Max">The largest, or null with no numbers.</param>
public sealed record SelectionStatistics(int Count, int NumericalCount, double Sum, double? Average, double? Min, double? Max)
{
    public static readonly SelectionStatistics Empty = new(0, 0, 0, null, null, null);

    /// <summary>
    /// Whether a status bar should show anything. Excel shows nothing for a single cell, and nothing
    /// until at least two cells hold something.
    /// </summary>
    public bool IsMeaningful => this.Count > 1;
}

/// <summary>A cell whose input a validation rule refused.</summary>
public sealed record ValidationFailure(CellRef Cell, string Input, DataValidationRule Rule);

public sealed partial class SpreadsheetController
{
    // ---- dialogs and menus ----

    /// <summary>
    /// Raised when a command needs input — Format Cells, a conditional-format value, the name manager —
    /// or has something to say, like a refused entry. The host renders it; see <see cref="SheetDialog"/>.
    /// </summary>
    public event EventHandler<SheetDialog>? DialogRequested;

    /// <summary>Raised to show a popup menu: the right-click menu, or a validated cell's dropdown list.</summary>
    public event EventHandler<SheetMenuRequest>? MenuRequested;

    /// <summary>Raised when input breaks a validation rule, alongside the dialog that says so.</summary>
    public event EventHandler<ValidationFailure>? ValidationFailed;

    /// <summary>Raised when an external hyperlink is followed. The host opens it; the grid cannot.</summary>
    public event EventHandler<string>? HyperlinkActivated;

    /// <summary>Asks the host to show a dialog.</summary>
    public void ShowDialog(SheetDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        this.CommitEdit(EditCommitDirection.None);
        this.DialogRequested?.Invoke(this, dialog);
    }

    void ShowMenu(SheetMenuRequest request) => this.MenuRequested?.Invoke(this, request);

    /// <summary>Runs a command as one undo step through the usual break-coalescing path, and repaints.</summary>
    internal void Run(IEditCommand<Workbook> command)
    {
        this.Begin();
        this.Workbook.Execute(command);
        this.RaiseChanged();
    }

    /// <summary>The selection clipped to the cells the sheet uses — what a whole-column pick means for data commands.</summary>
    internal CellRange DataSelection
    {
        get
        {
            var range = this.Selection.Range;
            if (range.CellCount <= 250_000 || this.sheet.UsedRange is not { } used || !used.Intersects(range))
                return range;

            return new CellRange(
                new CellRef(Math.Max(range.Left, used.Left), Math.Max(range.Top, used.Top)),
                new CellRef(Math.Min(range.Right, used.Right), Math.Min(range.Bottom, used.Bottom)));
        }
    }

    // ---- merge ----

    /// <summary>True when the active cell is part of a merge — what the Merge &amp; Center toggle shows.</summary>
    public bool IsActiveCellMerged => this.sheet.MergeAt(this.Selection.Active) is not null;

    /// <summary>
    /// Merges the selection. Only the top-left cell's content is kept, as in Excel; the others are
    /// cleared in the same undo step. A merge that overlaps an existing one absorbs it.
    /// </summary>
    public void MergeCells(MergeMode mode = MergeMode.MergeAndCenter)
    {
        var range = this.DataSelection;
        if (range.IsSingleCell)
            return;

        var targets = mode == MergeMode.MergeAcross
            ? Enumerable.Range(range.Top, range.RowCount).Select(r => new CellRange(new CellRef(range.Left, r), new CellRef(range.Right, r))).ToList()
            : [range];

        if (targets.All(x => x.IsSingleCell))
            return;

        var merges = this.sheet.MergedRanges.Where(x => !targets.Any(t => t.Intersects(x))).Concat(targets).ToList();
        var commands = new List<IEditCommand<Workbook>>();

        foreach (var target in targets)
        {
            foreach (var cell in target.Cells())
            {
                if (cell != target.TopLeft && (!this.sheet.GetValue(cell).IsBlank || this.sheet.GetFormula(cell) is not null))
                    commands.Add(new SetCellValueCommand(this.sheet.Name, cell, CellValue.Blank));
            }
        }

        commands.Add(new ReplaceSheetElementsCommand(this.sheet.Name, "mergeCells", Worksheet.MergeFragments(merges), "Merge Cells"));

        if (mode == MergeMode.MergeAndCenter)
            commands.Add(new FormatRangeCommand(this.sheet.Name, range, new CellFormatChange { HorizontalAlignment = CellHorizontalAlignment.Center }));

        this.Run(new CompositeCommand<Workbook>(mode == MergeMode.MergeAndCenter ? "Merge & Center" : "Merge Cells", commands));
        this.Selection.SelectRange(range);
    }

    /// <summary>Splits every merge the selection touches back into cells.</summary>
    public void UnmergeCells()
    {
        var range = this.Selection.Range;
        var merges = this.sheet.MergedRanges;
        var kept = merges.Where(x => !x.Intersects(range)).ToList();

        if (kept.Count == merges.Count)
            return;

        this.Run(new ReplaceSheetElementsCommand(this.sheet.Name, "mergeCells", Worksheet.MergeFragments(kept), "Unmerge Cells"));
    }

    /// <summary>Merge &amp; Center as a toggle: unmerges when the active cell is merged, merges otherwise.</summary>
    public void ToggleMergeAndCenter()
    {
        if (this.IsActiveCellMerged)
            this.UnmergeCells();
        else
            this.MergeCells(MergeMode.MergeAndCenter);
    }

    // ---- freeze panes ----

    public bool HasFrozenPanes => this.Metrics.HasFrozenColumns || this.Metrics.HasFrozenRows;

    /// <summary>Freezes the rows above and the columns left of the active cell — Excel's Freeze Panes.</summary>
    public void FreezePanes()
    {
        var active = this.Selection.Active;

        // Freezing from A1 has nothing above or left of it; Excel freezes at the middle of the view then.
        var split = active is { Column: 0, Row: 0 }
            ? new CellRef(0, Math.Max(1, this.Viewport.VisibleRows().First + (this.Viewport.VisibleRows().Last - this.Viewport.VisibleRows().First) / 2))
            : active;

        this.SetFrozen(split);
    }

    public void FreezeTopRow() => this.SetFrozen(new CellRef(0, 1));

    public void FreezeFirstColumn() => this.SetFrozen(new CellRef(1, 0));

    public void UnfreezePanes() => this.SetFrozen(null);

    void SetFrozen(CellRef? split)
    {
        this.Run(new ReplaceSheetElementsCommand(
            this.sheet.Name,
            "sheetViews",
            this.sheet.FrozenPaneFragment(split, this.Selection.Active),
            split is null ? "Unfreeze Panes" : "Freeze Panes"));

        this.SyncFrozenPane();
    }

    /// <summary>Re-reads the frozen pane into the grid's metrics — after a freeze, and after an undo of one.</summary>
    internal void SyncFrozenPane()
    {
        var before = this.Metrics.FrozenPane;
        this.RefreshMetrics();

        if (this.Metrics.FrozenPane != before)
            this.Viewport.ScrollTo(0, 0);

        this.RaiseChanged();
    }

    // ---- borders ----

    /// <summary>The line style and colour the Borders dropdown draws with. Thin and automatic by default.</summary>
    public BorderEdge BorderLine { get; set; } = BorderEdge.Thin();

    /// <summary>Applies one of the Borders dropdown's presets to the selection.</summary>
    public void ApplyBorders(BorderPreset preset)
        => this.Run(BorderPresets.Build(this.sheet.Name, this.Selection.Range, preset, this.BorderLine));

    // ---- clearing ----

    /// <summary>Clears contents, formatting, notes and links — Clear All.</summary>
    public void ClearAll()
    {
        var range = this.DataSelection;
        var commands = new List<IEditCommand<Workbook>>
        {
            new ClearRangeCommand(this.sheet.Name, range),
            new FormatRangeCommand(this.sheet.Name, range, CellFormatChange.Clear)
        };

        foreach (var note in this.sheet.Notes.Where(x => range.Contains(x.Cell)).ToList())
            commands.Add(new SetNoteCommand(this.sheet.Name, note.Cell, null));

        foreach (var link in this.sheet.Hyperlinks.Where(x => range.Contains(x.Cell)).Select(x => x.Cell).Distinct().ToList())
            commands.Add(new SetHyperlinkCommand(this.sheet.Name, link, null));

        this.Run(new CompositeCommand<Workbook>("Clear All", commands));
    }

    /// <summary>Deletes the notes in the selection.</summary>
    public void ClearNotes()
    {
        var range = this.DataSelection;
        var commands = this.sheet.Notes.Where(x => range.Contains(x.Cell))
            .Select(x => (IEditCommand<Workbook>)new SetNoteCommand(this.sheet.Name, x.Cell, null)).ToList();

        if (commands.Count > 0)
            this.Run(new CompositeCommand<Workbook>("Clear Notes", commands));
    }

    /// <summary>Removes the hyperlinks in the selection, leaving their text.</summary>
    public void ClearHyperlinks()
    {
        var range = this.DataSelection;
        var commands = this.sheet.Hyperlinks.Where(x => range.Contains(x.Cell)).Select(x => x.Cell).Distinct()
            .Select(x => (IEditCommand<Workbook>)new SetHyperlinkCommand(this.sheet.Name, x, null)).ToList();

        if (commands.Count > 0)
            this.Run(new CompositeCommand<Workbook>("Clear Hyperlinks", commands));
    }

    // ---- sort ----

    /// <summary>
    /// The range a sort, filter or table made from the current selection covers: the selection itself
    /// when it is a block, the data around the active cell when it is one cell — Excel's rule.
    /// </summary>
    public CellRange DataRegion
        => this.Selection.IsSingleCell ? SheetSort.CurrentRegion(this.sheet, this.Selection.Active) : this.DataSelection;

    /// <summary>Sorts the data region A to Z by the active cell's column.</summary>
    public void SortAscending() => this.SortByActiveColumn(descending: false);

    /// <summary>Sorts the data region Z to A by the active cell's column.</summary>
    public void SortDescending() => this.SortByActiveColumn(descending: true);

    void SortByActiveColumn(bool descending)
    {
        // Inside a filter the filter's range is what gets sorted, header excepted.
        if (this.sheet.FilterAt(this.Selection.Active) is { } filter)
        {
            this.Sort([new SortKey(this.Selection.Active.Column, descending)], hasHeader: true, filter.Range);
            return;
        }

        this.Sort([new SortKey(this.Selection.Active.Column, descending)]);
    }

    /// <summary>
    /// Sorts rows by one or more keys.
    /// </summary>
    /// <param name="hasHeader">Whether the first row is a header; null lets <see cref="SheetSort.DetectHeader"/> decide.</param>
    /// <param name="range">The rows to sort; null for <see cref="DataRegion"/>.</param>
    public void Sort(IReadOnlyList<SortKey> keys, bool? hasHeader = null, CellRange? range = null)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var target = range ?? this.DataRegion;
        var header = hasHeader ?? SheetSort.DetectHeader(this.sheet, target);
        this.Run(SheetSort.Build(this.sheet, target, keys, header));
    }

    // ---- filter ----

    /// <summary>Whether the sheet has an AutoFilter — what the Filter toggle shows.</summary>
    public bool HasAutoFilter => this.sheet.AutoFilter is not null;

    /// <summary>
    /// Turns the AutoFilter on over the data region, or off — showing every row it had hidden. Ctrl+Shift+L.
    /// </summary>
    public void ToggleAutoFilter()
    {
        if (this.sheet.AutoFilter is { } existing)
        {
            this.Run(AutoFilterStateCommand.For(this.sheet, null, existing.Range));
            this.SyncRowVisibility(existing.Range);
            return;
        }

        var region = this.DataRegion;
        if (region.RowCount < 1)
            return;

        this.Run(AutoFilterStateCommand.For(this.sheet, new SheetAutoFilter(region, [])));
    }

    /// <summary>Filters one column of the filter covering <paramref name="column"/>, or clears it with null.</summary>
    public void ApplyColumnFilter(SheetAutoFilter filter, int column, ColumnFilter? columnFilter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var updated = filter.With(column, columnFilter);
        this.Run(AutoFilterStateCommand.For(this.sheet, updated, tableName: filter.TableName));
        this.SyncRowVisibility(filter.Range);
    }

    /// <summary>Clears every column's criteria, keeping the arrows — Data ▸ Clear.</summary>
    public void ClearFilters()
    {
        var filter = this.sheet.FilterAt(this.Selection.Active) ?? this.sheet.AutoFilter;
        if (filter is null || !filter.IsFiltering)
            return;

        this.Run(AutoFilterStateCommand.For(this.sheet, filter with { Columns = [] }, tableName: filter.TableName));
        this.SyncRowVisibility(filter.Range);
    }

    /// <summary>Runs the filters again over data that has changed since — Data ▸ Reapply.</summary>
    public void ReapplyFilters()
    {
        foreach (var filter in this.sheet.AutoFilters.Where(x => x.IsFiltering).ToList())
        {
            this.Run(AutoFilterStateCommand.For(this.sheet, filter, tableName: filter.TableName));
            this.SyncRowVisibility(filter.Range);
        }
    }

    /// <summary>Filters to the active cell's value — the context menu's "Filter by Selected Cell's Value".</summary>
    public void FilterBySelectedValue()
    {
        var cell = this.Selection.Active;
        var filter = this.sheet.FilterAt(cell);

        if (filter is null)
        {
            this.Run(AutoFilterStateCommand.For(this.sheet, new SheetAutoFilter(SheetSort.CurrentRegion(this.sheet, cell), [])));
            filter = this.sheet.FilterAt(cell);
            if (filter is null)
                return;
        }

        var text = SheetAutoFilter.DisplayText(this.sheet, cell);
        this.ApplyColumnFilter(filter, cell.Column, text.Length == 0
            ? ColumnFilter.ForValues(cell.Column, [], includeBlanks: true)
            : ColumnFilter.ForValues(cell.Column, [text]));
    }

    /// <summary>Opens the filter dropdown for a column — what clicking a header arrow does.</summary>
    public void OpenFilterMenu(int column)
    {
        var filter = this.sheet.AutoFilters.FirstOrDefault(x => column >= x.Range.Left && column <= x.Range.Right);
        if (filter is not null)
            this.ShowDialog(SpreadsheetDialogs.Filter(this, filter, column));
    }

    /// <summary>Copies the sheet's hidden-row flags into the grid's metrics over a range.</summary>
    internal void SyncRowVisibility(CellRange range)
    {
        _ = range;
        this.RefreshMetrics();
        this.RaiseChanged();
    }

    // ---- fill ----

    /// <summary>Copies the top row of the selection down through it — Ctrl+D. A single row copies from the row above.</summary>
    public void FillDown()
    {
        var range = this.DataSelection;
        var source = range.RowCount == 1
            ? (range.Top == 0 ? (CellRange?)null : new CellRange(new CellRef(range.Left, range.Top - 1), new CellRef(range.Right, range.Top - 1)))
            : new CellRange(range.TopLeft, new CellRef(range.Right, range.Top));

        if (source is not { } from)
            return;

        var target = new CellRange(from.TopLeft, range.BottomRight);
        if (AutoFill.Build(this.sheet, from, target, series: false) is { } command)
            this.Run(new CompositeCommand<Workbook>("Fill Down", [command]));
    }

    /// <summary>Copies the left column of the selection across it — Ctrl+R. A single column copies from the left.</summary>
    public void FillRight()
    {
        var range = this.DataSelection;
        var source = range.ColumnCount == 1
            ? (range.Left == 0 ? (CellRange?)null : new CellRange(new CellRef(range.Left - 1, range.Top), new CellRef(range.Left - 1, range.Bottom)))
            : new CellRange(range.TopLeft, new CellRef(range.Left, range.Bottom));

        if (source is not { } from)
            return;

        var target = new CellRange(from.TopLeft, range.BottomRight);
        if (AutoFill.Build(this.sheet, from, target, series: false) is { } command)
            this.Run(new CompositeCommand<Workbook>("Fill Right", [command]));
    }

    /// <summary>Extends the selection into <paramref name="target"/> the way dragging the fill handle does.</summary>
    public void AutoFillTo(CellRange target, bool series = true)
    {
        var source = this.Selection.Range;
        if (AutoFill.Build(this.sheet, source, target, series) is not { } command)
            return;

        this.Run(command);
        this.Selection.SelectRange(target);
    }

    // ---- conditional formatting ----

    /// <summary>Adds a rule over the selection, at the top of the priority order.</summary>
    public void AddConditionalFormat(ConditionalFormatRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        uint? dxfId = rule.Format is { } dxf ? this.Workbook.StyleWriter.InternDifferential(dxf) : null;
        var fragments = ConditionalFormatting.WithRule(this.sheet.ReadElements("conditionalFormatting"), [this.DataSelection], rule, dxfId);
        this.Run(new ReplaceSheetElementsCommand(this.sheet.Name, "conditionalFormatting", fragments, "Conditional Formatting"));
    }

    /// <summary>Clear Rules from Selected Cells, or from the Entire Sheet.</summary>
    public void ClearConditionalFormats(bool entireSheet = false)
    {
        var existing = this.sheet.ReadElements("conditionalFormatting");
        if (existing.Count == 0)
            return;

        var remaining = entireSheet ? [] : ConditionalFormatting.WithoutArea(existing, this.Selection.Range);
        this.Run(new ReplaceSheetElementsCommand(this.sheet.Name, "conditionalFormatting", remaining, "Clear Rules"));
    }

    // ---- data validation ----

    /// <summary>The rule covering the active cell, or null.</summary>
    public DataValidationRule? ActiveValidation => this.sheet.ValidationAt(this.Selection.Active);

    /// <summary>Sets the validation over the selection, replacing whatever covered it; null clears it.</summary>
    public void SetValidation(DataValidationRule? rule)
    {
        var area = this.DataSelection;
        var rules = DataValidation.WithoutArea(this.sheet.Validations, area).ToList();

        if (rule is not null)
            rules.Add(rule with { Ranges = [area] });

        this.Run(new ReplaceSheetElementsCommand(this.sheet.Name, "dataValidations", DataValidation.ToXml(rules), rule is null ? "Clear Validation" : "Data Validation"));
    }

    /// <summary>
    /// Checks typed input against the cell's rule. A refusal shows Excel's message and returns false; a
    /// warning or information rule lets it through.
    /// </summary>
    bool PassesValidation(CellRef cell, string text)
    {
        if (text.StartsWith('=') || this.sheet.ValidationAt(cell) is null)
            return true;

        var outcome = DataValidation.Check(this.sheet, cell, ParseInput(text));
        if (outcome.IsValid || outcome.Rule is not { } rule)
            return true;

        if (!rule.ShowErrorMessage || rule.ErrorStyle != ValidationErrorStyle.Stop)
            return true;

        this.ValidationFailed?.Invoke(this, new ValidationFailure(cell, text, rule));
        this.DialogRequested?.Invoke(this, SheetDialog.Message(rule.ErrorTitleText, rule.ErrorMessageText));
        return false;
    }

    /// <summary>The items the active cell's list rule offers, or empty.</summary>
    public IReadOnlyList<string> ActiveListItems
        => this.ActiveValidation is { Type: ValidationType.List } rule ? DataValidation.ListItems(this.sheet, rule) : [];

    /// <summary>Drops the active cell's list open — Alt+Down, or the arrow beside the cell.</summary>
    public void OpenListDropdown()
    {
        var items = this.ActiveListItems;
        if (items.Count == 0)
            return;

        var cell = this.Selection.Active;
        var rect = this.ToScreen(this.Viewport.CellRect(cell));
        var current = Coercion.ToText(this.sheet.GetValue(cell));

        this.ShowMenu(new SheetMenuRequest(
            items.Select(item => new SheetMenuItem(item, () => this.SetCellText(cell, item)) { IsChecked = item == current }).ToList(),
            rect.X,
            rect.Bottom));
    }

    // ---- cell styles and tables ----

    /// <summary>Applies one of the Cell Styles gallery's styles to the selection.</summary>
    public void ApplyCellStyle(CellStylePreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        this.ApplyFormat(preset.Change);
    }

    /// <summary>The table the active cell is in, or null.</summary>
    public SheetTable? ActiveTable => this.sheet.TableAt(this.Selection.Active);

    /// <summary>
    /// Makes the data region a table with the named style — Format as Table.
    /// </summary>
    /// <param name="hasHeader">Whether the first row holds headers; null guesses.</param>
    public void FormatAsTable(string style, bool? hasHeader = null)
    {
        if (this.ActiveTable is { } table)
        {
            // Already a table: the gallery restyles it, as Excel's does.
            this.RestyleTable(table, style);
            return;
        }

        var region = this.DataRegion;
        if (this.sheet.Tables.Any(x => x.Range.Intersects(region)))
        {
            this.ShowDialog(SheetDialog.Message("Format as Table", "A table cannot overlap another table."));
            return;
        }

        var header = hasHeader ?? (SheetSort.DetectHeader(this.sheet, region) || region.RowCount > 1);

        // A sheet AutoFilter over the same data would be a second set of arrows; the table's replaces it.
        var commands = new List<IEditCommand<Workbook>>();
        if (this.sheet.AutoFilter is { } filter && filter.Range.Intersects(region))
            commands.Add(AutoFilterStateCommand.For(this.sheet, null, filter.Range));

        commands.Add(SheetTables.Build(this.sheet, region, style, header));
        this.Run(new CompositeCommand<Workbook>("Format as Table", commands));
    }

    void RestyleTable(SheetTable table, string style)
    {
        var xml = this.sheet.TableXml(table.Name);
        if (xml is null)
            return;

        var element = System.Xml.Linq.XElement.Parse(xml);
        var info = element.Elements().FirstOrDefault(x => x.Name.LocalName == "tableStyleInfo");
        info?.SetAttributeValue("name", style);

        this.Run(new CompositeCommand<Workbook>("Table Style",
        [
            new RemoveTableCommand(this.sheet.Name, table.Name),
            new AddTableCommand(this.sheet.Name, element.ToString(System.Xml.Linq.SaveOptions.DisableFormatting))
        ]));
    }

    /// <summary>Turns the active table back into a plain range, keeping its cells and formatting.</summary>
    public void ConvertTableToRange()
    {
        if (this.ActiveTable is { } table)
            this.Run(new RemoveTableCommand(this.sheet.Name, table.Name));
    }

    // ---- notes ----

    /// <summary>The name written as a new note's author.</summary>
    public string NoteAuthor { get; set; } = Environment.UserName is { Length: > 0 } user ? user : "Author";

    /// <summary>Whether every note is drawn open, rather than only the one under the pointer.</summary>
    public bool ShowAllNotes { get; set; }

    public CellNote? ActiveNote => this.sheet.NoteAt(this.Selection.Active);

    /// <summary>Sets the active cell's note, or deletes it with null or empty text.</summary>
    public void SetNote(string? text)
    {
        var cell = this.Selection.Active;
        var existing = this.sheet.NoteAt(cell);
        var note = string.IsNullOrWhiteSpace(text) ? null : new CellNote(cell, text, existing?.Author ?? this.NoteAuthor);
        this.Run(new SetNoteCommand(this.sheet.Name, cell, note));
    }

    public void DeleteNote() => this.SetNote(null);

    /// <summary>Moves to the next note in reading order, wrapping at the end.</summary>
    public void NextNote(bool backwards = false)
    {
        var notes = this.sheet.Notes.OrderBy(x => x.Cell.Row).ThenBy(x => x.Cell.Column).ToList();
        if (notes.Count == 0)
            return;

        var active = this.Selection.Active;
        var next = backwards
            ? notes.LastOrDefault(x => x.Cell.Row < active.Row || (x.Cell.Row == active.Row && x.Cell.Column < active.Column)) ?? notes[^1]
            : notes.FirstOrDefault(x => x.Cell.Row > active.Row || (x.Cell.Row == active.Row && x.Cell.Column > active.Column)) ?? notes[0];

        this.GoTo(next.Cell);
    }

    // ---- hyperlinks ----

    public CellHyperlink? ActiveHyperlink => this.sheet.HyperlinkAt(this.Selection.Active);

    /// <summary>
    /// Links the active cell. A blank cell gets the link's display text, and the cell takes Excel's
    /// Hyperlink look — blue, underlined — in the same undo step.
    /// </summary>
    public void SetHyperlink(CellHyperlink? link)
    {
        var cell = this.Selection.Active;
        var commands = new List<IEditCommand<Workbook>>();

        if (link is null)
        {
            commands.Add(new SetHyperlinkCommand(this.sheet.Name, cell, null));
        }
        else
        {
            var display = string.IsNullOrWhiteSpace(link.Display) ? link.Target : link.Display!;

            if (this.sheet.GetValue(cell).IsBlank || !string.IsNullOrWhiteSpace(link.Display))
                commands.Add(new SetCellValueCommand(this.sheet.Name, cell, CellValue.FromText(display)));

            commands.Add(new SetHyperlinkCommand(this.sheet.Name, cell, link with { Cell = cell, Display = display }));
            commands.Add(new FormatRangeCommand(this.sheet.Name, new CellRange(cell), new CellFormatChange
            {
                Foreground = HyperlinkColor,
                Underline = true
            }));
        }

        this.Run(new CompositeCommand<Workbook>(link is null ? "Remove Hyperlink" : "Hyperlink", commands));
    }

    /// <summary>Excel's Hyperlink style colour.</summary>
    public static readonly ArgbColor HyperlinkColor = new(255, 0x05, 0x63, 0xC1);

    /// <summary>
    /// Follows the active cell's link: an internal one moves the selection, an external one is handed to
    /// the host through <see cref="HyperlinkActivated"/>.
    /// </summary>
    public bool OpenHyperlink(CellRef? cell = null)
    {
        if (this.sheet.HyperlinkAt(cell ?? this.Selection.Active) is not { } link)
            return false;

        if (!link.IsInternal)
        {
            this.HyperlinkActivated?.Invoke(this, link.Address!);
            return true;
        }

        return link.Location is { } location && this.GoTo(location);
    }

    // ---- names ----

    /// <summary>Defines or redefines a name. Throws when the name is not one Excel allows.</summary>
    public void DefineName(string name, string formula, string? scope = null, string? comment = null)
    {
        if (!DefinedNameRules.IsValid(name, out var error))
            throw new ArgumentException(error, nameof(name));

        this.Run(new SetDefinedNameCommand(name, scope, new DefinedNameInfo(name, formula.TrimStart('='), scope, Comment: comment)));
    }

    /// <summary>Renames a name, keeping what it refers to.</summary>
    public void RenameName(string name, string? scope, string newName, string formula, string? comment = null)
    {
        if (!DefinedNameRules.IsValid(newName, out var error))
            throw new ArgumentException(error, nameof(newName));

        if (!string.Equals(name, newName, StringComparison.OrdinalIgnoreCase) &&
            this.Workbook.DefinedNames.Any(x => string.Equals(x.Name, newName, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Scope, scope, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"The name '{newName}' already exists.", nameof(newName));

        this.Run(new SetDefinedNameCommand(name, scope, new DefinedNameInfo(newName, formula.TrimStart('='), scope, Comment: comment)));
    }

    public void DeleteName(string name, string? scope = null)
        => this.Run(new SetDefinedNameCommand(name, scope, null));

    /// <summary>The selection as an absolute, sheet-qualified reference — what Define Name suggests.</summary>
    public string SelectionReference
    {
        get
        {
            var range = this.Selection.Range;
            var sheetName = FormulaSheetRenamer.RequiresQuoting(this.sheet.Name) ? FormulaSheetRenamer.Quote(this.sheet.Name) : this.sheet.Name;
            static string Abs(CellRef c) => $"${CellRef.ColumnName(c.Column)}${c.Row + 1}";
            return range.IsSingleCell ? $"{sheetName}!{Abs(range.TopLeft)}" : $"{sheetName}!{Abs(range.TopLeft)}:{Abs(range.BottomRight)}";
        }
    }

    /// <summary>
    /// Goes to a reference or a name, typed into the name box. A new name typed there with a range selected
    /// defines it, as in Excel. Returns false when the text is neither.
    /// </summary>
    public bool GoTo(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        var text = reference.Trim();

        if (this.Workbook.ResolveNameRange(text, this.sheet.Name) is { } named)
        {
            this.SwitchSheet(named.Sheet);
            this.CancelEdit();
            this.Selection.SelectRange(named.Range);
            this.Viewport.ScrollIntoView(named.Range.TopLeft);
            this.RaiseChanged();
            return true;
        }

        var bang = text.LastIndexOf('!');
        if (bang > 0)
        {
            var sheetName = text[..bang].Trim('\'');
            if (this.Workbook.Find(sheetName) is not { } target)
                return false;

            this.SwitchSheet(target);
            text = text[(bang + 1)..];
        }

        text = text.Replace("$", string.Empty, StringComparison.Ordinal);

        if (CellRange.TryParse(text, out var range))
        {
            this.CancelEdit();
            this.Selection.SelectRange(range);
            this.Viewport.ScrollIntoView(range.TopLeft);
            this.RaiseChanged();
            return true;
        }

        if (CellRef.TryParse(text, out var cell))
        {
            this.GoTo(cell);
            return true;
        }

        if (!this.Selection.IsSingleCell || bang < 0)
        {
            // A new name with a selection: define it over the selection, the name box's other job.
            if (DefinedNameRules.IsValid(text, out _))
            {
                this.DefineName(text, this.SelectionReference);
                return true;
            }
        }

        return false;
    }

    // ---- view ----

    /// <summary>Whether the active sheet draws gridlines. Saved with the sheet.</summary>
    public bool ShowGridlines
    {
        get => this.sheet.ShowGridLines;
        set
        {
            if (value == this.sheet.ShowGridLines)
                return;

            this.sheet.WriteViewFlag("showGridLines", value);
            this.RaiseChanged();
        }
    }

    /// <summary>Whether the active sheet shows its row and column headings. Saved with the sheet.</summary>
    public bool ShowHeadings
    {
        get => this.sheet.ShowHeadings;
        set
        {
            if (value == this.sheet.ShowHeadings)
                return;

            this.sheet.WriteViewFlag("showRowColHeaders", value);
            this.ApplyHeadings();
            this.RaiseChanged();
        }
    }

    /// <summary>Whether cells show their formulas rather than results — Ctrl+`. Saved with the sheet.</summary>
    public bool ShowFormulas
    {
        get => this.sheet.ShowFormulas;
        set
        {
            if (value == this.sheet.ShowFormulas)
                return;

            this.sheet.WriteViewFlag("showFormulas", value);
            this.RaiseChanged();
        }
    }

    public void ToggleShowFormulas() => this.ShowFormulas = !this.ShowFormulas;

    void ApplyHeadings()
    {
        var show = this.sheet.ShowHeadings;
        this.Metrics.RowHeaderWidth = show ? 46 : 0;
        this.Metrics.ColumnHeaderHeight = show ? 22 : 0;
    }

    /// <summary>Recomputes every formula now — Formulas ▸ Calculate Now, F9.</summary>
    public void CalculateNow()
    {
        this.Workbook.RecalculateAll();
        this.RaiseChanged();
    }

    // ---- zoom ----

    public const double MinZoom = 0.1;
    public const double MaxZoom = 4;

    double zoom = 1;
    double hostWidth = 800;
    double hostHeight = 600;

    /// <summary>
    /// The magnification, 0.1 to 4 — 10% to 400%. Everything scales, headings included, as in Excel.
    /// Pointer coordinates handed to the controller stay in the host's own units; the controller divides.
    /// </summary>
    public double Zoom
    {
        get => this.zoom;
        set
        {
            var clamped = Math.Clamp(Math.Round(value, 3), MinZoom, MaxZoom);
            if (Math.Abs(clamped - this.zoom) < 0.0005)
                return;

            this.zoom = clamped;
            this.ApplyViewportSize();
            this.ZoomChanged?.Invoke(this, clamped);
            this.RaiseChanged();
        }
    }

    public event EventHandler<double>? ZoomChanged;

    /// <summary>Steps up through Excel's zoom stops.</summary>
    public void ZoomIn() => this.Zoom = ZoomStops.FirstOrDefault(x => x > this.zoom + 0.001, MaxZoom);

    /// <summary>Steps down through Excel's zoom stops.</summary>
    public void ZoomOut() => this.Zoom = ZoomStops.LastOrDefault(x => x < this.zoom - 0.001, MinZoom);

    /// <summary>The stops the zoom buttons move between.</summary>
    public static IReadOnlyList<double> ZoomStops { get; } = [0.1, 0.25, 0.5, 0.75, 0.9, 1, 1.1, 1.25, 1.5, 2, 3, 4];

    void ApplyViewportSize()
    {
        this.Viewport.Width = this.hostWidth / this.zoom;
        this.Viewport.Height = this.hostHeight / this.zoom;
    }

    /// <summary>A rectangle in grid units, in the host's units — multiplied by the zoom.</summary>
    public GridRect ToScreen(GridRect rect)
        => new(rect.X * this.zoom, rect.Y * this.zoom, rect.Width * this.zoom, rect.Height * this.zoom);

    /// <summary>Where the in-cell editor goes, in the host's units — covering the whole merge for a merged cell.</summary>
    public GridRect? EditorBounds
    {
        get
        {
            if (this.EditingCell is not { } cell)
                return null;

            var rect = this.sheet.MergeAt(cell) is { } merge ? this.Viewport.RangeRect(merge) : this.Viewport.CellRect(cell);
            return this.ToScreen(rect);
        }
    }

    /// <summary>The font size the in-cell editor should use, zoom included.</summary>
    public double EditorFontSize => this.ActiveFormat.FontSize * 96d / 72d * this.zoom;

    // ---- selection statistics ----

    SelectionStatistics? statistics;
    (long Revision, string Sheet, CellRange Range) statisticsKey = (-1, string.Empty, default);
    (long Revision, string Sheet, CellRange Range) announcedKey = (-1, string.Empty, default);

    /// <summary>Raised when the selection or the data under it changes — what a status bar listens to.</summary>
    public event EventHandler? SelectionStatisticsChanged;

    /// <summary>Average, Count, Numerical Count, Min, Max and Sum of the selection, for the status bar.</summary>
    /// <remarks>
    /// Computed when read and cached until the selection or the workbook changes. Hidden rows are left
    /// out, which is what makes a filtered column's sum the sum of what is on screen.
    /// </remarks>
    public SelectionStatistics SelectionStatistics
    {
        get
        {
            var key = (this.Workbook.Revision, this.sheet.Name, this.Selection.Range);
            if (this.statistics is { } cached && key == this.statisticsKey)
                return cached;

            this.statistics = this.ComputeStatistics(this.Selection.Range);
            this.statisticsKey = key;
            return this.statistics;
        }
    }

    SelectionStatistics ComputeStatistics(CellRange range)
    {
        if (this.sheet.UsedRange is not { } used || !used.Intersects(range))
            return SelectionStatistics.Empty;

        var clipped = new CellRange(
            new CellRef(Math.Max(range.Left, used.Left), Math.Max(range.Top, used.Top)),
            new CellRef(Math.Min(range.Right, used.Right), Math.Min(range.Bottom, used.Bottom)));

        int count = 0, numbers = 0;
        double sum = 0, min = double.MaxValue, max = double.MinValue;

        foreach (var cell in this.sheet.PopulatedCells())
        {
            if (!clipped.Contains(cell) || this.Metrics.Rows.IsHidden(cell.Row) || this.Metrics.Columns.IsHidden(cell.Column))
                continue;

            var value = this.sheet.GetDisplayValue(cell);
            if (value.IsBlank)
                continue;

            count++;
            if (value.Kind != CellValueKind.Number)
                continue;

            var number = value.AsNumber();
            numbers++;
            sum += number;
            min = Math.Min(min, number);
            max = Math.Max(max, number);
        }

        return numbers == 0
            ? new SelectionStatistics(count, 0, 0, null, null, null)
            : new SelectionStatistics(count, numbers, sum, sum / numbers, min, max);
    }

    /// <summary>Tells a status bar the statistics may have moved — once per selection or revision change.</summary>
    void AnnounceStatistics()
    {
        var key = (this.Workbook.Revision, this.sheet.Name, this.Selection.Range);
        if (key == this.announcedKey)
            return;

        this.announcedKey = key;
        this.SelectionStatisticsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- rows ----

    /// <summary>Hides or shows the selection's rows.</summary>
    public void SetRowsHidden(bool hidden)
    {
        var (first, last) = this.SelectedRows;
        if (hidden && first == 0 && last >= CellRef.MaxRow)
            return;

        if (last - first > 100_000)
            last = Math.Max(first, Math.Min(last, this.sheet.UsedRange?.Bottom ?? first));

        this.Run(SetRowsHiddenCommand.Range(this.sheet.Name, first, last, hidden));

        for (var row = first; row <= last; row++)
            this.Metrics.Rows.SetHidden(row, hidden);

        this.RaiseChanged();
    }

    /// <summary>Sets the selection's rows to a height in points.</summary>
    public void SetRowHeight(double points)
    {
        var (first, last) = this.SelectedRows;
        last = Math.Min(last, first + 10_000);

        var commands = new List<IEditCommand<Workbook>>();
        for (var row = first; row <= last; row++)
            commands.Add(new SetRowHeightCommand(this.sheet.Name, row, points));

        this.Run(new CompositeCommand<Workbook>("Row Height", commands));

        for (var row = first; row <= last; row++)
            this.Metrics.Rows.SetSize(row, GridMetrics.PointsToPixels(points));

        this.RaiseChanged();
    }

    /// <summary>Sets the selection's columns to a width in characters — the unit Excel's dialog asks for.</summary>
    public void SetColumnWidthCharacters(double characters)
        => this.SetColumnWidth(GridMetrics.WidthToPixels(Math.Clamp(characters, 0, 255)));

    // ---- insertion ----

    /// <summary>Writes today's date into the active cell, formatted as a date — Ctrl+;.</summary>
    public void InsertCurrentDate() => this.InsertNow(DateTime.Now.Date, NumberFormats.CodeOf(NumberFormatPreset.ShortDate));

    /// <summary>Writes the time now into the active cell — Ctrl+Shift+:.</summary>
    public void InsertCurrentTime() => this.InsertNow(new DateTime(1899, 12, 31) + DateTime.Now.TimeOfDay, NumberFormats.CodeOf(NumberFormatPreset.Time));

    void InsertNow(DateTime value, string format)
    {
        var cell = this.Selection.Active;
        var serial = value.Year == 1899 ? value.TimeOfDay.TotalDays : ExcelDate.FromDateTime(value);

        this.Run(new CompositeCommand<Workbook>("Insert Date",
        [
            new SetCellValueCommand(this.sheet.Name, cell, CellValue.FromNumber(serial)),
            new FormatRangeCommand(this.sheet.Name, new CellRange(cell), new CellFormatChange { NumberFormatCode = format })
        ]));
    }

    /// <summary>Opens the in-cell editor with a function started — what picking one from the library does.</summary>
    public void InsertFunction(string name)
    {
        var active = this.Selection.Active;
        var current = this.CellText(active);

        this.CancelEdit();
        this.BeginEdit(current.StartsWith('=') && current.Length > 1
            ? current + (current.EndsWith('(') || current.EndsWith(',') ? string.Empty : "+") + name + "("
            : "=" + name + "(");
    }

    // ---- select ----

    /// <summary>Ctrl+A: the data around the active cell first, then the whole sheet on a second press.</summary>
    public void SelectAllSmart()
    {
        var region = SheetSort.CurrentRegion(this.sheet, this.Selection.Active);
        if (region.IsSingleCell || this.Selection.Range == region)
            this.Selection.SelectAll();
        else
            this.Selection.SelectRange(region);

        this.RaiseChanged();
    }

    /// <summary>Ctrl+Space: the active cell's whole column.</summary>
    public void SelectEntireColumn()
    {
        this.Selection.SelectRange(new CellRange(new CellRef(this.Selection.Range.Left, 0), new CellRef(this.Selection.Range.Right, CellRef.MaxRow)));
        this.RaiseChanged();
    }

    /// <summary>Shift+Space: the active cell's whole row.</summary>
    public void SelectEntireRow()
    {
        this.Selection.SelectRange(new CellRange(new CellRef(0, this.Selection.Range.Top), new CellRef(CellRef.MaxColumn, this.Selection.Range.Bottom)));
        this.RaiseChanged();
    }

    /// <summary>Moves to the next or previous visible sheet, stopping at either end — Ctrl+PageDown/PageUp.</summary>
    public void StepSheet(int offset)
    {
        var sheets = this.VisibleSheets;
        var at = -1;
        for (var i = 0; i < sheets.Count && at < 0; i++)
        {
            if (ReferenceEquals(sheets[i], this.sheet))
                at = i;
        }

        if (at >= 0 && sheets.ElementAtOrDefault(at + offset) is { } target)
            this.SwitchSheet(target);
    }
}
