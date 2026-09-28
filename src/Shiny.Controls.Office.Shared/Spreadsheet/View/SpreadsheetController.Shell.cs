namespace Shiny.Controls.Office.Spreadsheet.View;

/// <summary>What Excel's status bar says on its far left: Ready, Enter or Edit.</summary>
public enum SheetEditMode
{
    /// <summary>No editor is open.</summary>
    Ready,

    /// <summary>Typing over a cell — the arrow keys commit and move.</summary>
    Enter,

    /// <summary>Editing a cell's existing content (F2, a double-click, the formula bar).</summary>
    Edit
}


/// <summary>The three views Excel's status bar switches between.</summary>
public enum SheetViewMode
{
    /// <summary>The plain grid.</summary>
    Normal,

    /// <summary>The grid with the printed pages' edges dashed over it.</summary>
    PageLayout,

    /// <summary>Solid page edges, "Page n" over each page and everything off the pages washed grey.</summary>
    PageBreakPreview
}


/// <summary>
/// How a sheet falls onto printed pages — the column and row each page starts at.
/// </summary>
/// <param name="Range">The cells that print: A1 to the bottom-right of the used range.</param>
/// <param name="ColumnStarts">The first column of each page across, in order. Never empty.</param>
/// <param name="RowStarts">The first row of each page down, in order. Never empty.</param>
public sealed record SheetPageLayout(CellRange Range, IReadOnlyList<int> ColumnStarts, IReadOnlyList<int> RowStarts)
{
    /// <summary>Pages across times pages down.</summary>
    public int PageCount => this.ColumnStarts.Count * this.RowStarts.Count;

    /// <summary>The cells of one page, counting down the columns first — Excel's default order.</summary>
    public CellRange PageAt(int across, int down)
    {
        var left = this.ColumnStarts[across];
        var right = across + 1 < this.ColumnStarts.Count ? this.ColumnStarts[across + 1] - 1 : this.Range.Right;
        var top = this.RowStarts[down];
        var bottom = down + 1 < this.RowStarts.Count ? this.RowStarts[down + 1] - 1 : this.Range.Bottom;
        return new CellRange(new CellRef(left, top), new CellRef(right, bottom));
    }

    /// <summary>Every page, down then across, as Excel numbers them.</summary>
    public IEnumerable<CellRange> Pages()
    {
        for (var across = 0; across < this.ColumnStarts.Count; across++)
            for (var down = 0; down < this.RowStarts.Count; down++)
                yield return this.PageAt(across, down);
    }
}


/// <summary>Cuts a sheet into printed pages.</summary>
public static class SheetPagination
{
    /// <summary>US Letter less Excel's Normal margins (0.7" left/right, 0.75" top/bottom), in grid pixels at 96 dpi.</summary>
    public const double LetterContentWidth = (8.5 - 1.4) * 96;

    /// <inheritdoc cref="LetterContentWidth"/>
    public const double LetterContentHeight = (11 - 1.5) * 96;

    /// <summary>
    /// Rows past this are not paginated. A single formatted cell a million rows down would otherwise
    /// cost a million iterations per frame in Page Break Preview.
    /// </summary>
    const int MaxRows = 100_000;

    /// <summary>
    /// The pages the sheet's used range falls onto, or null when the sheet is empty.
    /// </summary>
    /// <remarks>
    /// A page takes whole columns and rows, the way Excel breaks them: a column wider than the page gets
    /// a page of its own rather than being split.
    /// </remarks>
    public static SheetPageLayout? For(Worksheet sheet, GridMetrics metrics, double pageWidth = LetterContentWidth, double pageHeight = LetterContentHeight)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(metrics);

        if (sheet.UsedRange is not { } used)
            return null;

        var range = new CellRange(new CellRef(0, 0), new CellRef(used.Right, Math.Min(used.Bottom, MaxRows)));
        return new SheetPageLayout(
            range,
            Breaks(metrics.Columns, range.Right, pageWidth),
            Breaks(metrics.Rows, range.Bottom, pageHeight));
    }

    static List<int> Breaks(AxisMetrics axis, int last, double page)
    {
        var starts = new List<int> { 0 };
        var run = 0d;

        for (var i = 0; i <= last; i++)
        {
            var size = axis.SizeOf(i);
            if (run > 0 && run + size > page)
            {
                starts.Add(i);
                run = 0;
            }

            run += size;
        }

        return starts;
    }
}


public sealed partial class SpreadsheetController
{
    bool editStartedByTyping;
    SheetViewMode viewMode;
    (Worksheet Sheet, long Revision, SheetPageLayout? Layout)? pageLayoutCache;

    /// <summary>Ready, Enter or Edit — the status bar's mode indicator.</summary>
    public SheetEditMode EditMode => this.EditingCell is null
        ? SheetEditMode.Ready
        : this.editStartedByTyping ? SheetEditMode.Enter : SheetEditMode.Edit;

    /// <summary>
    /// Normal, Page Layout or Page Break Preview. The two page views draw where the printed pages fall;
    /// nothing in the workbook changes.
    /// </summary>
    public SheetViewMode ViewMode
    {
        get => this.viewMode;
        set
        {
            if (this.viewMode == value)
                return;

            this.viewMode = value;
            this.RaiseChanged();
        }
    }

    /// <summary>
    /// The printed pages of the sheet on screen, or null in Normal view or on an empty sheet. Cached
    /// against the workbook's revision.
    /// </summary>
    public SheetPageLayout? PageLayout
    {
        get
        {
            if (this.viewMode == SheetViewMode.Normal)
                return null;

            if (this.pageLayoutCache is { } cached && ReferenceEquals(cached.Sheet, this.sheet) && cached.Revision == this.Workbook.Revision)
                return cached.Layout;

            var layout = SheetPagination.For(this.sheet, this.Metrics);
            this.pageLayoutCache = (this.sheet, this.Workbook.Revision, layout);
            return layout;
        }
    }
}
