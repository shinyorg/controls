using Shiny.Controls.Office.Editing;

namespace Shiny.Controls.Office.Spreadsheet.Commands;

/// <summary>
/// Pushes blank rows into a sheet, moving everything below them down.
/// </summary>
/// <remarks>
/// The cells are only half of it. Every formula in the workbook that named a cell the insert moved is
/// rewritten to follow it, on this sheet and on any other — a formula left reading <c>B5</c> after the
/// value moved to <c>B6</c> does not fail, it quietly starts totalling the blank row.
/// </remarks>
public sealed class InsertRowsCommand(string sheetName, int at, int count = 1) : IEditCommand<Workbook>
{
    public string SheetName { get; } = sheetName;

    /// <summary>Zero-based index of the first inserted row. Existing rows from here down move.</summary>
    public int At { get; } = at;

    public int Count { get; } = count;

    public string Name => this.Count == 1 ? "Insert Row" : "Insert Rows";

    public IEditCommand<Workbook> Apply(Workbook context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.InsertBand(this.SheetName, rows: true, this.At, this.Count);
        return new DeleteRowsCommand(this.SheetName, this.At, this.Count);
    }
}

/// <summary>Pushes blank columns into a sheet, moving everything to their right further right.</summary>
public sealed class InsertColumnsCommand(string sheetName, int at, int count = 1) : IEditCommand<Workbook>
{
    public string SheetName { get; } = sheetName;

    /// <summary>Zero-based index of the first inserted column. Existing columns from here right move.</summary>
    public int At { get; } = at;

    public int Count { get; } = count;

    public string Name => this.Count == 1 ? "Insert Column" : "Insert Columns";

    public IEditCommand<Workbook> Apply(Workbook context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.InsertBand(this.SheetName, rows: false, this.At, this.Count);
        return new DeleteColumnsCommand(this.SheetName, this.At, this.Count);
    }
}

/// <summary>
/// Removes rows from a sheet and closes the gap.
/// </summary>
/// <remarks>
/// The inverse is a composite of three things, because that is genuinely what undoing a delete is:
/// put the rows back, put their contents back, and put back the formulas elsewhere in the workbook
/// that the delete rewrote. Skipping the third leaves an undo that restores the data and leaves
/// <c>#REF!</c> in every formula that pointed at it.
/// </remarks>
public sealed class DeleteRowsCommand(string sheetName, int at, int count = 1) : IEditCommand<Workbook>
{
    public string SheetName { get; } = sheetName;
    public int At { get; } = at;
    public int Count { get; } = count;

    public string Name => this.Count == 1 ? "Delete Row" : "Delete Rows";

    public IEditCommand<Workbook> Apply(Workbook context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sheet = context[this.SheetName];
        var snapshot = SpreadsheetClipboardContent.Capture(
            sheet,
            new CellRange(new CellRef(0, this.At), new CellRef(CellRef.MaxColumn, this.At + this.Count - 1)),
            SpreadsheetClipboardOperation.Copy);

        var ranges = StructureUndo.Snapshot(sheet, rows: true, this.At, this.Count);
        var displaced = context.DeleteBand(this.SheetName, rows: true, this.At, this.Count);

        return new CompositeCommand<Workbook>(this.Name, StructureUndo.Build(
            new InsertRowsCommand(this.SheetName, this.At, this.Count),
            new PasteClipboardCommand(snapshot, this.SheetName, new CellRef(0, this.At)),
            displaced,
            ranges));
    }
}

/// <summary>Removes columns from a sheet and closes the gap.</summary>
public sealed class DeleteColumnsCommand(string sheetName, int at, int count = 1) : IEditCommand<Workbook>
{
    public string SheetName { get; } = sheetName;
    public int At { get; } = at;
    public int Count { get; } = count;

    public string Name => this.Count == 1 ? "Delete Column" : "Delete Columns";

    public IEditCommand<Workbook> Apply(Workbook context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sheet = context[this.SheetName];
        var snapshot = SpreadsheetClipboardContent.Capture(
            sheet,
            new CellRange(new CellRef(this.At, 0), new CellRef(this.At + this.Count - 1, CellRef.MaxRow)),
            SpreadsheetClipboardOperation.Copy);

        var ranges = StructureUndo.Snapshot(sheet, rows: false, this.At, this.Count);
        var displaced = context.DeleteBand(this.SheetName, rows: false, this.At, this.Count);

        return new CompositeCommand<Workbook>(this.Name, StructureUndo.Build(
            new InsertColumnsCommand(this.SheetName, this.At, this.Count),
            new PasteClipboardCommand(snapshot, this.SheetName, new CellRef(this.At, 0)),
            displaced,
            ranges));
    }
}

static class StructureUndo
{
    /// <summary>Re-open the gap, refill it, then repair the formulas the delete rewrote — in that order.</summary>
    public static IReadOnlyList<IEditCommand<Workbook>> Build(
        IEditCommand<Workbook> reopen,
        IEditCommand<Workbook> refill,
        IReadOnlyList<(string Sheet, CellRef Cell, string Formula)> displaced,
        IReadOnlyList<IEditCommand<Workbook>>? ranges = null)
    {
        var commands = new List<IEditCommand<Workbook>> { reopen, refill };

        foreach (var (sheet, cell, formula) in displaced)
            commands.Add(new SetCellFormulaCommand(sheet, cell, formula));

        // Last: re-inserting the band has just grown every range that straddled it, which is right for
        // most of them but not for one the delete swallowed whole. Putting the lists back verbatim is
        // what makes the undo exact.
        if (ranges is not null)
            commands.AddRange(ranges);

        return commands;
    }

    /// <summary>
    /// What a delete is about to lose from the sheet's range lists — as commands that put it back.
    /// </summary>
    public static IReadOnlyList<IEditCommand<Workbook>> Snapshot(Worksheet sheet, bool rows, int at, int count)
    {
        var commands = new List<IEditCommand<Workbook>>();

        foreach (var name in Worksheet.BandSensitiveElements)
            commands.Add(new ReplaceSheetElementsCommand(sheet.Name, name, sheet.ReadElements(name)));

        foreach (var note in sheet.Notes)
        {
            var index = rows ? note.Cell.Row : note.Cell.Column;
            if (index >= at && index < at + count)
                commands.Add(new SetNoteCommand(sheet.Name, note.Cell, note));
        }

        return commands;
    }
}
