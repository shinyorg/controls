using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.View;

namespace Shiny.Controls.Office.Spreadsheet.View;

/// <summary>Which corner of a chart a resize is dragging.</summary>
public enum ChartHandle
{
    None,
    Move,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

/// <summary>
/// Everything that floats over the grid or sits on top of a cell rather than being one: charts, the fill
/// handle, the filter arrows and a validated cell's dropdown, and the note shown under the pointer.
/// </summary>
public sealed partial class SpreadsheetController
{
    /// <summary>The side of the square a chart's resize handles are drawn and grabbed at.</summary>
    public const double ChartHandleSize = 8;

    /// <summary>The filter arrow's square, drawn at the right of a header cell.</summary>
    public const double FilterButtonSize = 16;

    string? selectedChart;
    ChartHandle chartDrag;
    double chartOriginX;
    double chartOriginY;
    ChartAnchor chartStartAnchor;
    ChartAnchor? chartPreview;

    bool filling;
    CellRange? fillPreview;

    CellRef? hovered;

    /// <summary>
    /// Rebuilds the grid's geometry from the sheet — widths, heights, hidden bands, the frozen pane —
    /// keeping the scroll position.
    /// </summary>
    /// <remarks>
    /// Called after anything that changes that geometry through the file rather than through the grid: an
    /// undo, a filter hiding rows, a freeze. Patching the metrics by hand after each would be a second
    /// copy of what each command does, and the one place they drifted would be a row the grid draws that
    /// the file says is hidden.
    /// </remarks>
    public void RefreshMetrics()
    {
        var scrollX = this.Viewport.ScrollX;
        var scrollY = this.Viewport.ScrollY;

        this.Metrics = GridMetrics.FromWorksheet(this.sheet);
        this.ApplyHeadings();
        this.Viewport = new GridViewport(this.Metrics);
        this.ApplyViewportSize();
        this.Viewport.ScrollTo(scrollX, scrollY);
    }

    // ---- charts ----

    /// <summary>The chart that is selected, by id, or null. Selecting one shows its handles.</summary>
    public string? SelectedChartId
    {
        get => this.selectedChart;
        set
        {
            if (this.selectedChart == value)
                return;

            this.selectedChart = value;
            this.RaiseChanged();
        }
    }

    /// <summary>The selected chart, when it still exists.</summary>
    public SheetChart? SelectedChart => this.selectedChart is { } id ? this.sheet.ChartById(id) : null;

    /// <summary>A chart being dragged, and where it currently is — for the painter to draw it there.</summary>
    public (string Id, ChartAnchor Anchor)? ChartPreview
        => this.chartPreview is { } anchor && this.selectedChart is { } id ? (id, anchor) : null;

    /// <summary>The rectangle a chart occupies in grid units (not yet zoomed).</summary>
    public GridRect ChartRect(ChartAnchor anchor)
    {
        var from = this.Viewport.CellRect(anchor.From);
        var to = this.Viewport.CellRect(anchor.To);

        var x = from.X + anchor.FromDx;
        var y = from.Y + anchor.FromDy;
        return new GridRect(x, y, Math.Max(4, to.X + anchor.ToDx - x), Math.Max(4, to.Y + anchor.ToDy - y));
    }

    /// <summary>The chart under a point, topmost first.</summary>
    SheetChart? ChartAt(double x, double y)
    {
        if (x < this.Metrics.RowHeaderWidth || y < this.Metrics.ColumnHeaderHeight)
            return null;

        var charts = this.sheet.Charts;
        for (var i = charts.Count - 1; i >= 0; i--)
        {
            var rect = this.ChartRect(charts[i].Anchor);
            if (rect.Contains(x, y))
                return charts[i];
        }

        return null;
    }

    ChartHandle HandleAt(GridRect rect, double x, double y)
    {
        var grip = ChartHandleSize + 2;

        bool Near(double px, double py) => Math.Abs(x - px) <= grip && Math.Abs(y - py) <= grip;

        if (Near(rect.X, rect.Y)) return ChartHandle.TopLeft;
        if (Near(rect.Right, rect.Y)) return ChartHandle.TopRight;
        if (Near(rect.X, rect.Bottom)) return ChartHandle.BottomLeft;
        if (Near(rect.Right, rect.Bottom)) return ChartHandle.BottomRight;
        return rect.Contains(x, y) ? ChartHandle.Move : ChartHandle.None;
    }

    /// <summary>
    /// Inserts a chart of the data region beside it, and selects it.
    /// </summary>
    /// <returns>The new chart's id, or null when the selection had nothing to plot.</returns>
    public string? InsertChart(ChartKind kind, string? title = null)
    {
        var region = this.DataRegion;
        var series = SheetCharts.SeriesFromRange(this.sheet, region);
        if (series.Count == 0)
        {
            this.ShowDialog(SheetDialog.Message("Insert Chart", "Select the data to chart first — a block of numbers, with labels in its first row or column if it has them."));
            return null;
        }

        if (kind == ChartKind.Pie && series.Count > 1)
            series = [series[0]];

        // Beside the data, eight columns wide and fifteen rows tall — about Excel's default size.
        var from = new CellRef(Math.Min(CellRef.MaxColumn - 8, region.Right + 2), region.Top);
        var anchor = new ChartAnchor(from, 0, 0, new CellRef(from.Column + 7, Math.Min(CellRef.MaxRow, from.Row + 15)), 0, 0);

        var chartXml = SheetCharts.ChartSpaceXml(kind, title, series, this.sheet);
        var shapeId = this.sheet.NextDrawingShapeId();
        var anchorXml = SheetCharts.AnchorXml(anchor, shapeId, $"Chart {shapeId - 1}");

        var command = new InsertChartCommand(this.sheet.Name, chartXml, anchorXml);
        this.Run(command);

        this.selectedChart = command.InsertedId;
        this.RaiseChanged();
        return command.InsertedId;
    }

    /// <summary>Deletes the selected chart. False when none is selected.</summary>
    public bool DeleteSelectedChart()
    {
        if (this.selectedChart is not { } id)
            return false;

        this.selectedChart = null;
        this.Run(new DeleteChartCommand(this.sheet.Name, id));
        return true;
    }

    /// <summary>Converts a sheet-space pixel position back into a cell plus an offset into it.</summary>
    (CellRef Cell, double Dx, double Dy) SheetPoint(double sheetX, double sheetY)
    {
        sheetX = Math.Max(0, sheetX);
        sheetY = Math.Max(0, sheetY);

        var column = Math.Clamp(this.Metrics.Columns.IndexAt(sheetX), 0, CellRef.MaxColumn);
        var row = Math.Clamp(this.Metrics.Rows.IndexAt(sheetY), 0, CellRef.MaxRow);
        return (new CellRef(column, row), sheetX - this.Metrics.Columns.OffsetOf(column), sheetY - this.Metrics.Rows.OffsetOf(row));
    }

    (double X, double Y) SheetOffset(CellRef cell, double dx, double dy)
        => (this.Metrics.Columns.OffsetOf(cell.Column) + dx, this.Metrics.Rows.OffsetOf(cell.Row) + dy);

    ChartAnchor DraggedAnchor(double x, double y)
    {
        var dx = x - this.chartOriginX;
        var dy = y - this.chartOriginY;
        var start = this.chartStartAnchor;

        var (fx, fy) = this.SheetOffset(start.From, start.FromDx, start.FromDy);
        var (tx, ty) = this.SheetOffset(start.To, start.ToDx, start.ToDy);

        switch (this.chartDrag)
        {
            case ChartHandle.Move:
                fx += dx; tx += dx; fy += dy; ty += dy;

                // Moving stops at the sheet's top-left edge rather than squashing the chart against it.
                if (fx < 0) { tx -= fx; fx = 0; }
                if (fy < 0) { ty -= fy; fy = 0; }
                break;

            case ChartHandle.TopLeft: fx += dx; fy += dy; break;
            case ChartHandle.TopRight: tx += dx; fy += dy; break;
            case ChartHandle.BottomLeft: fx += dx; ty += dy; break;
            case ChartHandle.BottomRight: tx += dx; ty += dy; break;
        }

        const double minimum = 40;
        if (tx - fx < minimum)
        {
            if (this.chartDrag is ChartHandle.TopLeft or ChartHandle.BottomLeft)
                fx = tx - minimum;
            else
                tx = fx + minimum;
        }

        if (ty - fy < minimum)
        {
            if (this.chartDrag is ChartHandle.TopLeft or ChartHandle.TopRight)
                fy = ty - minimum;
            else
                ty = fy + minimum;
        }

        var from = this.SheetPoint(fx, fy);
        var to = this.SheetPoint(tx, ty);
        return new ChartAnchor(from.Cell, from.Dx, from.Dy, to.Cell, to.Dx, to.Dy);
    }

    // ---- fill handle ----

    /// <summary>The range a fill-handle drag would fill, while one is under way — the painter outlines it.</summary>
    public CellRange? FillPreview => this.fillPreview;

    bool OnFillHandle(double x, double y)
    {
        var rect = this.Viewport.RangeRect(this.Selection.Range);
        var grip = 5;
        return Math.Abs(x - rect.Right) <= grip && Math.Abs(y - rect.Bottom) <= grip;
    }

    /// <summary>
    /// The target a fill drag to <paramref name="cell"/> means: the selection extended along whichever
    /// axis the pointer has moved further out on — a fill runs one way at a time.
    /// </summary>
    CellRange FillTarget(CellRef cell)
    {
        var range = this.Selection.Range;

        var down = cell.Row > range.Bottom ? cell.Row - range.Bottom : 0;
        var up = cell.Row < range.Top ? range.Top - cell.Row : 0;
        var right = cell.Column > range.Right ? cell.Column - range.Right : 0;
        var left = cell.Column < range.Left ? range.Left - cell.Column : 0;

        var vertical = Math.Max(down, up);
        var horizontal = Math.Max(right, left);

        if (vertical == 0 && horizontal == 0)
            return range;

        if (vertical >= horizontal)
        {
            return down > 0
                ? new CellRange(range.TopLeft, new CellRef(range.Right, cell.Row))
                : new CellRange(new CellRef(range.Left, cell.Row), range.BottomRight);
        }

        return right > 0
            ? new CellRange(range.TopLeft, new CellRef(cell.Column, range.Bottom))
            : new CellRange(new CellRef(cell.Column, range.Top), range.BottomRight);
    }

    // ---- filter arrows and list dropdowns ----

    /// <summary>Where a filter arrow is drawn in a header cell, in grid units.</summary>
    public GridRect FilterButtonRect(CellRef cell)
    {
        var rect = this.Viewport.CellRect(cell);
        var size = Math.Min(FilterButtonSize, rect.Height - 2);
        return new GridRect(rect.Right - size - 1, rect.Bottom - size - 1, size, size);
    }

    /// <summary>Where the active cell's list arrow is drawn — just outside its right edge — or null.</summary>
    public GridRect? ListButtonRect
    {
        get
        {
            if (this.EditingCell is not null || this.ActiveValidation is not { Type: ValidationType.List, InCellDropDown: true })
                return null;

            var active = this.Selection.Active;
            var rect = this.sheet.MergeAt(active) is { } merge ? this.Viewport.RangeRect(merge) : this.Viewport.CellRect(active);
            var size = Math.Min(FilterButtonSize, rect.Height);
            return new GridRect(rect.Right + 1, rect.Bottom - size, size, size);
        }
    }

    // ---- hover ----

    /// <summary>The cell under a hovering mouse, for the note popup.</summary>
    public CellRef? HoveredCell => this.hovered;

    /// <summary>
    /// The notes to draw open: every note with Show All Notes on, otherwise the one under the pointer — or,
    /// for a touch user who has no pointer to hover, the one on the active cell.
    /// </summary>
    public IReadOnlyList<CellNote> VisibleNotes
    {
        get
        {
            if (this.ShowAllNotes)
                return this.sheet.Notes;

            var cell = this.hovered ?? (this.UsesTouch ? this.Selection.Active : null);
            return cell is { } at && this.sheet.NoteAt(at) is { } note ? [note] : [];
        }
    }

    /// <summary>A mouse moving with no button held. Tracks the hovered cell for notes.</summary>
    public void PointerHover(double x, double y)
    {
        x /= this.zoom;
        y /= this.zoom;

        var hit = this.Viewport.HitTest(x, y);
        CellRef? cell = hit.IsCell ? this.sheet.MergeAt(hit.Cell)?.TopLeft ?? hit.Cell : null;

        if (cell == this.hovered)
            return;

        var hadNote = this.hovered is { } before && this.sheet.NoteAt(before) is not null;
        this.hovered = cell;

        if (hadNote || (cell is { } now && this.sheet.NoteAt(now) is not null))
            this.RaiseChanged();
    }

    /// <summary>The pointer left the grid.</summary>
    public void PointerExit()
    {
        if (this.hovered is null)
            return;

        this.hovered = null;
        this.RaiseChanged();
    }

    // ---- pointer overlay routing ----

    /// <summary>
    /// Gives the floating things first refusal of a press. True when one of them took it.
    /// </summary>
    bool PointerDownOverlay(double x, double y, PointerKind kind, bool modifier)
    {
        // The selected chart's handles sit partly outside it, so they are tested before anything else.
        if (this.SelectedChart is { } selected)
        {
            var handle = this.HandleAt(this.ChartRect(selected.Anchor), x, y);
            if (handle != ChartHandle.None)
            {
                this.BeginChartDrag(selected, handle, x, y);
                return true;
            }
        }

        if (this.ChartAt(x, y) is { } chart)
        {
            this.selectedChart = chart.Id;
            this.BeginChartDrag(chart, ChartHandle.Move, x, y);
            this.RaiseChanged();
            return true;
        }

        if (this.selectedChart is not null)
        {
            this.selectedChart = null;
            this.RaiseChanged();
        }

        if (this.ListButtonRect is { } list && list.Contains(x, y))
        {
            this.OpenListDropdown();
            return true;
        }

        var hit = this.Viewport.HitTest(x, y);
        if (hit.IsCell && this.sheet.FilterWithHeaderAt(hit.Cell) is not null && this.FilterButtonRect(hit.Cell).Contains(x, y))
        {
            this.Selection.MoveTo(hit.Cell);
            this.OpenFilterMenu(hit.Cell.Column);
            return true;
        }

        // Ctrl/Cmd-click follows a link; a plain click selects the cell, which is how a linked cell is
        // ever edited at all.
        if (hit.IsCell && modifier && this.sheet.HyperlinkAt(hit.Cell) is not null)
        {
            this.Selection.MoveTo(hit.Cell);
            this.OpenHyperlink(hit.Cell);
            return true;
        }

        if (kind != PointerKind.Touch && this.OnFillHandle(x, y))
        {
            this.filling = true;
            this.fillPreview = this.Selection.Range;
            return true;
        }

        return false;
    }

    void BeginChartDrag(SheetChart chart, ChartHandle handle, double x, double y)
    {
        this.chartDrag = handle;
        this.chartOriginX = x;
        this.chartOriginY = y;
        this.chartStartAnchor = chart.Anchor;
        this.chartPreview = null;
    }

    bool PointerMoveOverlay(double x, double y)
    {
        if (this.chartDrag != ChartHandle.None)
        {
            if (Math.Abs(x - this.chartOriginX) > 2 || Math.Abs(y - this.chartOriginY) > 2 || this.chartPreview is not null)
            {
                this.chartPreview = this.DraggedAnchor(x, y);
                this.RaiseChanged();
            }

            return true;
        }

        if (this.filling)
        {
            var hit = this.Viewport.HitTest(Math.Max(x, this.Metrics.RowHeaderWidth + 1), Math.Max(y, this.Metrics.ColumnHeaderHeight + 1));
            if (hit.IsCell)
            {
                this.fillPreview = this.FillTarget(hit.Cell);
                this.Viewport.ScrollIntoView(hit.Cell);
                this.RaiseChanged();
            }

            return true;
        }

        return false;
    }

    bool PointerUpOverlay()
    {
        if (this.chartDrag != ChartHandle.None)
        {
            var preview = this.chartPreview;
            this.chartDrag = ChartHandle.None;
            this.chartPreview = null;

            if (preview is { } anchor && this.selectedChart is { } id && anchor != this.chartStartAnchor)
                this.Run(new MoveChartCommand(this.sheet.Name, id, anchor));
            else
                this.RaiseChanged();

            return true;
        }

        if (this.filling)
        {
            var target = this.fillPreview;
            this.filling = false;
            this.fillPreview = null;

            if (target is { } range && range != this.Selection.Range)
                this.AutoFillTo(range);
            else
                this.RaiseChanged();

            return true;
        }

        return false;
    }
}
