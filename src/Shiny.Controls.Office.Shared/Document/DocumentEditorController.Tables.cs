namespace Shiny.Controls.Office.Document;

/// <summary>Table editing: moving between cells, and changing the table's structure.</summary>
public sealed partial class DocumentEditorController
{
    /// <summary>The table cell the caret is in, or null when it is in body text.</summary>
    public DocumentStoryCell? CurrentCell => this.Document.CellOf(this.Selection.Focus.Block);

    /// <summary>True when the caret is inside a table — what brings up the contextual Table tab.</summary>
    public bool IsInTable => this.CurrentCell is not null;

    public void InsertTableRowAbove() => this.EditTable(TableEdit.InsertRowAbove);

    public void InsertTableRowBelow() => this.EditTable(TableEdit.InsertRowBelow);

    public void InsertTableColumnLeft() => this.EditTable(TableEdit.InsertColumnLeft);

    public void InsertTableColumnRight() => this.EditTable(TableEdit.InsertColumnRight);

    public void DeleteTableRow() => this.EditTable(TableEdit.DeleteRow);

    public void DeleteTableColumn() => this.EditTable(TableEdit.DeleteColumn);

    public void DeleteTable() => this.EditTable(TableEdit.DeleteTable);

    /// <summary>
    /// Merges every cell between the two ends of the selection into one.
    /// </summary>
    /// <remarks>
    /// The selection has to reach from one cell into another of the same table — select across the
    /// cells to merge, as in Word. The region is the rectangle the two ends span, so a selection from
    /// the top-left cell to one two rows down and a column across merges all six.
    /// </remarks>
    public void MergeTableCells() => this.EditTable(TableEdit.MergeCells);

    /// <summary>Splits a merged cell apart again, or an ordinary cell into two side by side.</summary>
    public void SplitTableCell() => this.EditTable(TableEdit.SplitCell);

    /// <summary>True when the selection runs between two cells of one table, which is what Merge needs.</summary>
    public bool CanMergeTableCells
    {
        get
        {
            var anchor = this.Document.CellOf(this.Selection.Anchor.Block);
            var focus = this.Document.CellOf(this.Selection.Focus.Block);

            return anchor is not null && focus is not null
                && anchor.TopBlock == focus.TopBlock
                && (anchor.Row != focus.Row || anchor.Cell != focus.Cell);
        }
    }

    void EditTable(TableEdit edit)
    {
        if (this.IsReadOnlyDocument || !this.IsInTable)
            return;

        var focus = this.Selection.Focus;
        var anchor = this.Selection.Anchor;
        var element = this.document.ParagraphElementAt(focus.Block);

        // Merge acts on the region between the selection's two ends; everything else on the caret.
        var to = edit == TableEdit.MergeCells ? anchor : (DocumentPosition?)null;

        if (edit == TableEdit.MergeCells && !this.CanMergeTableCells)
            return;

        this.document.Execute(new EditTableCommand(focus, edit, to));

        // The caret's own paragraph usually survives, just at a new story index - found again by the
        // element rather than guessed from the index, which shifts with every row inserted above.
        var index = this.document.IndexOf(element);
        var count = this.Document.Paragraphs.Count;

        if (index < 0)
            index = Math.Clamp(focus.Block, 0, Math.Max(0, count - 1));

        this.Selection.MoveTo(new DocumentPosition(index, Math.Min(focus.Offset, this.LengthOf(index))));
        this.AfterEdit();
    }

    /// <summary>
    /// Tab and Shift+Tab in a table: move to the next or previous cell and select what is in it.
    /// </summary>
    /// <remarks>
    /// Tab in the last cell adds a row and moves into it, which is how every word processor grows a
    /// table as you fill it in. Shift+Tab in the first cell stays put.
    /// </remarks>
    bool TabInTable(bool backwards)
    {
        if (this.CurrentCell is not { } cell)
            return false;

        var target = backwards ? this.PreviousCellStart(cell) : this.NextCellStart(cell);

        if (target < 0 && !backwards)
        {
            // The last cell: a new row below, then into its first cell.
            var element = this.document.ParagraphElementAt(this.Selection.Focus.Block);
            this.document.Execute(new EditTableCommand(this.Selection.Focus, TableEdit.InsertRowBelow));

            var index = this.document.IndexOf(element);
            if (index >= 0 && this.Document.CellOf(index) is { } refreshed)
            {
                this.Selection.MoveTo(new DocumentPosition(index, 0));
                target = this.NextCellStart(refreshed);
            }

            this.AfterEdit();
        }

        if (target < 0)
            return true;

        this.SelectCellContents(target);
        this.ScrollCaretIntoView();
        return true;
    }

    /// <summary>The story index of the first paragraph of the cell after this one, or -1.</summary>
    int NextCellStart(DocumentStoryCell cell)
    {
        for (var i = this.Selection.Focus.Block + 1; i < this.Document.Paragraphs.Count; i++)
        {
            var next = this.Document.CellOf(i);
            if (next is null || next.TopBlock != cell.TopBlock)
                return -1;

            if (next.Row != cell.Row || next.Cell != cell.Cell)
                return i;
        }

        return -1;
    }

    /// <summary>The story index of the first paragraph of the cell before this one, or -1.</summary>
    int PreviousCellStart(DocumentStoryCell cell)
    {
        var i = this.Selection.Focus.Block - 1;

        // Back out of the current cell first.
        while (i >= 0 && this.Document.CellOf(i) is { } same && same.Row == cell.Row && same.Cell == cell.Cell && same.TopBlock == cell.TopBlock)
            i--;

        if (i < 0 || this.Document.CellOf(i) is not { } previous || previous.TopBlock != cell.TopBlock)
            return -1;

        // Then to the start of the previous one.
        while (i > 0 && this.Document.CellOf(i - 1) is { } before && before.Row == previous.Row && before.Cell == previous.Cell && before.TopBlock == previous.TopBlock)
            i--;

        return i;
    }

    /// <summary>Selects everything in the cell whose first paragraph is at <paramref name="start"/>.</summary>
    void SelectCellContents(int start)
    {
        var cell = this.Document.CellOf(start);
        var end = start;

        while (end + 1 < this.Document.Paragraphs.Count && this.Document.CellOf(end + 1) is { } more
               && cell is not null && more.TopBlock == cell.TopBlock && more.Row == cell.Row && more.Cell == cell.Cell)
        {
            end++;
        }

        this.Selection.Select(new DocumentPosition(start, 0), new DocumentPosition(end, this.LengthOf(end)));
    }

    /// <summary>True when two story paragraphs can be joined — the same body, or the same cell.</summary>
    bool CanJoin(int first, int second) => this.document.ShareContainer(first, second);
}
