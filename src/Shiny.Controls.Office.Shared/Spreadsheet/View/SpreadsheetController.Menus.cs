using Shiny.Controls.Office.Spreadsheet.Commands;

namespace Shiny.Controls.Office.Spreadsheet.View;

/// <summary>What a right-click landed on, which decides what its menu offers.</summary>
public enum ContextTarget
{
    Cell,
    RowHeader,
    ColumnHeader,
    Chart
}

public sealed partial class SpreadsheetController
{
    /// <summary>
    /// Opens the right-click menu for a point on the grid: a cell's, a row header's, a column header's or
    /// a chart's. Returns false when the point has no menu.
    /// </summary>
    /// <remarks>
    /// A right-click outside the selection moves it first, as in Excel — the menu acts on the selection,
    /// and acting on one the user can no longer see the edges of would be a surprise. One inside the
    /// selection leaves it be, which is how a block is right-clicked and copied.
    /// </remarks>
    public bool OpenContextMenu(double x, double y)
    {
        var gx = x / this.zoom;
        var gy = y / this.zoom;

        this.CommitEdit(EditCommitDirection.None);

        if (this.ChartAt(gx, gy) is { } chart)
        {
            this.selectedChart = chart.Id;
            this.RaiseChanged();
            this.ShowMenu(new SheetMenuRequest(this.ContextMenu(ContextTarget.Chart), x, y));
            return true;
        }

        var hit = this.Viewport.HitTest(gx, gy);
        var range = this.Selection.Range;
        ContextTarget target;

        switch (hit.Target)
        {
            case HitTarget.ColumnHeader or HitTarget.ColumnResize:
                if (!(range.Top == 0 && range.Bottom >= CellRef.MaxRow && hit.Cell.Column >= range.Left && hit.Cell.Column <= range.Right))
                    this.Selection.SelectColumn(hit.Cell.Column);

                target = ContextTarget.ColumnHeader;
                break;

            case HitTarget.RowHeader or HitTarget.RowResize:
                if (!(range.Left == 0 && range.Right >= CellRef.MaxColumn && hit.Cell.Row >= range.Top && hit.Cell.Row <= range.Bottom))
                    this.Selection.SelectRow(hit.Cell.Row);

                target = ContextTarget.RowHeader;
                break;

            case HitTarget.Cell:
                if (!range.Contains(hit.Cell))
                    this.Selection.MoveTo(hit.Cell);

                target = ContextTarget.Cell;
                break;

            default:
                return false;
        }

        this.selectedChart = null;
        this.RaiseChanged();
        this.ShowMenu(new SheetMenuRequest(this.ContextMenu(target), x, y));
        return true;
    }

    /// <summary>The context menu for the active cell — Shift+F10 and the menu key.</summary>
    public void OpenContextMenuAtSelection()
    {
        var rect = this.ToScreen(this.Viewport.CellRect(this.Selection.Active));
        this.ShowMenu(new SheetMenuRequest(this.ContextMenu(ContextTarget.Cell), rect.Right, rect.Bottom));
    }

    /// <summary>The right-click menu for a kind of target, acting on the current selection.</summary>
    public IReadOnlyList<SheetMenuItem> ContextMenu(ContextTarget target)
    {
        if (target == ContextTarget.Chart)
        {
            return
            [
                new SheetMenuItem("Delete Chart", () => this.DeleteSelectedChart()) { Shortcut = "Del" }
            ];
        }

        var items = new List<SheetMenuItem>
        {
            new("Cut", this.Cut) { Shortcut = "Ctrl+X" },
            new("Copy", this.Copy) { Shortcut = "Ctrl+C" },
            new("Paste", () => this.Paste()) { Shortcut = "Ctrl+V", IsEnabled = this.CanPaste },
            SheetMenuItem.Separator
        };

        switch (target)
        {
            case ContextTarget.RowHeader:
                var rows = this.Selection.Range.RowCount;
                items.Add(new SheetMenuItem("Insert", () => this.InsertRows(Math.Min(rows, 1000))));
                items.Add(new SheetMenuItem("Delete", () => this.DeleteRows(Math.Min(rows, 1000))));
                items.Add(new SheetMenuItem("Clear Contents", this.ClearSelection) { Shortcut = "Del" });
                items.Add(SheetMenuItem.Separator);
                items.Add(new SheetMenuItem("Format Cells...", () => this.ShowDialog(SpreadsheetDialogs.FormatCells(this))) { Shortcut = "Ctrl+1" });
                items.Add(new SheetMenuItem("Row Height...", () => this.ShowDialog(SpreadsheetDialogs.RowHeight(this))));
                items.Add(new SheetMenuItem("Hide", () => this.SetRowsHidden(true)) { Shortcut = "Ctrl+9" });
                items.Add(new SheetMenuItem("Unhide", () => this.UnhideRowsAround()));
                break;

            case ContextTarget.ColumnHeader:
                var columns = this.Selection.Range.ColumnCount;
                items.Add(new SheetMenuItem("Insert", () => this.InsertColumns(Math.Min(columns, 1000))));
                items.Add(new SheetMenuItem("Delete", () => this.DeleteColumns(Math.Min(columns, 1000))));
                items.Add(new SheetMenuItem("Clear Contents", this.ClearSelection) { Shortcut = "Del" });
                items.Add(SheetMenuItem.Separator);
                items.Add(new SheetMenuItem("Format Cells...", () => this.ShowDialog(SpreadsheetDialogs.FormatCells(this))) { Shortcut = "Ctrl+1" });
                items.Add(new SheetMenuItem("Column Width...", () => this.ShowDialog(SpreadsheetDialogs.ColumnWidth(this))));
                items.Add(new SheetMenuItem("Hide", () => this.SetColumnsHidden(true)) { Shortcut = "Ctrl+0" });
                items.Add(new SheetMenuItem("Unhide", this.UnhideColumnsAround));
                break;

            default:
                var note = this.ActiveNote;
                var link = this.ActiveHyperlink;

                items.Add(new SheetMenuItem("Insert...")
                {
                    Children =
                    [
                        new SheetMenuItem("Insert Sheet Rows", () => this.InsertRows(Math.Min(this.Selection.Range.RowCount, 1000))),
                        new SheetMenuItem("Insert Sheet Columns", () => this.InsertColumns(Math.Min(this.Selection.Range.ColumnCount, 1000)))
                    ]
                });

                items.Add(new SheetMenuItem("Delete...")
                {
                    Children =
                    [
                        new SheetMenuItem("Delete Sheet Rows", () => this.DeleteRows(Math.Min(this.Selection.Range.RowCount, 1000))),
                        new SheetMenuItem("Delete Sheet Columns", () => this.DeleteColumns(Math.Min(this.Selection.Range.ColumnCount, 1000)))
                    ]
                });

                items.Add(new SheetMenuItem("Clear Contents", this.ClearSelection) { Shortcut = "Del" });
                items.Add(SheetMenuItem.Separator);

                items.Add(new SheetMenuItem("Filter")
                {
                    Children =
                    [
                        new SheetMenuItem("Filter by Selected Cell's Value", this.FilterBySelectedValue),
                        new SheetMenuItem("Clear Filter", this.ClearFilters) { IsEnabled = this.sheet.AutoFilters.Any(x => x.IsFiltering) },
                        new SheetMenuItem("Reapply", this.ReapplyFilters) { IsEnabled = this.sheet.AutoFilters.Any(x => x.IsFiltering) }
                    ]
                });

                items.Add(new SheetMenuItem("Sort")
                {
                    Children =
                    [
                        new SheetMenuItem("Sort A to Z", this.SortAscending),
                        new SheetMenuItem("Sort Z to A", this.SortDescending),
                        new SheetMenuItem("Custom Sort...", () => this.ShowDialog(SpreadsheetDialogs.CustomSort(this)))
                    ]
                });

                items.Add(SheetMenuItem.Separator);
                items.Add(new SheetMenuItem(note is null ? "New Note" : "Edit Note", () => this.ShowDialog(SpreadsheetDialogs.Note(this))) { Shortcut = "Shift+F2" });

                if (note is not null)
                    items.Add(new SheetMenuItem("Delete Note", this.DeleteNote));

                items.Add(new SheetMenuItem(link is null ? "Link..." : "Edit Hyperlink...", () => this.ShowDialog(SpreadsheetDialogs.Hyperlink(this))) { Shortcut = "Ctrl+K" });

                if (link is not null)
                {
                    items.Add(new SheetMenuItem("Open Hyperlink", () => this.OpenHyperlink()));
                    items.Add(new SheetMenuItem("Remove Hyperlink", () => this.SetHyperlink(null)));
                }

                items.Add(SheetMenuItem.Separator);

                if (this.ActiveListItems.Count > 0)
                    items.Add(new SheetMenuItem("Pick From Drop-down List...", this.OpenListDropdown) { Shortcut = "Alt+Down" });

                items.Add(new SheetMenuItem("Format Cells...", () => this.ShowDialog(SpreadsheetDialogs.FormatCells(this))) { Shortcut = "Ctrl+1" });
                items.Add(new SheetMenuItem("Define Name...", () => this.ShowDialog(SpreadsheetDialogs.DefineName(this, null))));
                break;
        }

        return items;
    }

    /// <summary>Unhides the rows in the selection, and the hidden run right above it when it has none.</summary>
    void UnhideRowsAround()
    {
        var (first, last) = this.SelectedRows;
        if (first > 0 && !Enumerable.Range(first, Math.Min(last - first + 1, 100_000)).Any(this.Metrics.Rows.IsHidden))
        {
            // Excel's Unhide on the row under a hidden run: select across it first.
            while (first > 0 && this.Metrics.Rows.IsHidden(first - 1))
                first--;

            this.Selection.SelectRange(new CellRange(new CellRef(0, first), new CellRef(CellRef.MaxColumn, last)));
        }

        this.SetRowsHidden(false);
    }

    void UnhideColumnsAround()
    {
        var (first, last) = this.SelectedColumns;
        if (first > 0 && !Enumerable.Range(first, last - first + 1).Any(this.Metrics.Columns.IsHidden))
        {
            while (first > 0 && this.Metrics.Columns.IsHidden(first - 1))
                first--;

            this.Selection.SelectRange(new CellRange(new CellRef(first, 0), new CellRef(last, CellRef.MaxRow)));
        }

        this.SetColumnsHidden(false);
    }
}
