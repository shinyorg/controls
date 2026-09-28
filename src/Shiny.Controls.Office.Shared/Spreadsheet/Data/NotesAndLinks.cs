using Shiny.Controls.Office.Editing;

namespace Shiny.Controls.Office.Spreadsheet;

/// <summary>
/// A note on a cell — what Excel calls a Note since 365 renamed its old comments, and what the file
/// still stores as a legacy comment.
/// </summary>
public sealed record CellNote(CellRef Cell, string Text, string Author);

/// <summary>A hyperlink on a cell: to an address outside the workbook, or to a place inside it.</summary>
public sealed record CellHyperlink(CellRef Cell)
{
    /// <summary>A URL, a <c>mailto:</c>, a file path — anything outside the workbook.</summary>
    public string? Address { get; init; }

    /// <summary>A place in the workbook, <c>Sheet2!A1</c> or a defined name.</summary>
    public string? Location { get; init; }

    /// <summary>The text Excel shows in the cell, recorded alongside the link.</summary>
    public string? Display { get; init; }

    public string? Tooltip { get; init; }

    public bool IsInternal => string.IsNullOrEmpty(this.Address);

    /// <summary>Where the link goes, however it is stored — for a dialog or a tooltip.</summary>
    public string Target => this.Address ?? this.Location ?? string.Empty;
}

/// <summary>Adds, edits or deletes a cell's note.</summary>
public sealed class SetNoteCommand(string sheetName, CellRef cell, CellNote? note) : IEditCommand<Workbook>
{
    public string SheetName { get; } = sheetName;
    public CellRef Cell { get; } = cell.Relative();
    public CellNote? Note { get; } = note;

    public string Name => this.Note is null ? "Delete Note" : "Edit Note";

    public IEditCommand<Workbook> Apply(Workbook context)
    {
        var previous = context[this.SheetName].WriteNote(this.Cell, this.Note);
        return new SetNoteCommand(this.SheetName, this.Cell, previous);
    }
}

/// <summary>Adds, edits or removes a cell's hyperlink.</summary>
public sealed class SetHyperlinkCommand(string sheetName, CellRef cell, CellHyperlink? link) : IEditCommand<Workbook>
{
    public string SheetName { get; } = sheetName;
    public CellRef Cell { get; } = cell.Relative();
    public CellHyperlink? Link { get; } = link;

    public string Name => this.Link is null ? "Remove Hyperlink" : "Hyperlink";

    public IEditCommand<Workbook> Apply(Workbook context)
    {
        var previous = context[this.SheetName].WriteHyperlink(this.Cell, this.Link);
        return new SetHyperlinkCommand(this.SheetName, this.Cell, previous);
    }
}
