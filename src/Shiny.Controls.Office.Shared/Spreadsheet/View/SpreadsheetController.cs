using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Spreadsheet.Commands;
using Shiny.Controls.Office.View;

namespace Shiny.Controls.Office.Spreadsheet.View;

public enum EditCommitDirection
{
    None,
    Down,
    Up,
    Right,
    Left
}

/// <summary>
/// Host-independent interaction logic for the grid: selection, dragging, resizing and cell editing.
/// </summary>
/// <remarks>
/// Both hosts forward raw pointer and key events here rather than implementing behaviour themselves.
/// That is what keeps MAUI and Blazor genuinely identical — the only thing either host owns is turning
/// a platform event into a call on this class, and putting a text box on screen when editing starts.
/// </remarks>
public sealed class SpreadsheetController
{
    /// <summary>
    /// Where each sheet was left: its selection, its scroll position and its column and row sizes.
    /// </summary>
    /// <remarks>
    /// Keyed by name because that is what survives an undo — deleting a sheet and undoing it produces a
    /// different <see cref="Worksheet"/> instance with the same name and the same contents, and coming
    /// back to a restored sheet at the top-left when you left it halfway down reads as data loss.
    /// </remarks>
    readonly Dictionary<string, SheetViewState> sheetState = new(StringComparer.OrdinalIgnoreCase);

    Worksheet sheet;
    DragMode drag = DragMode.None;
    int resizeIndex = -1;
    double resizeOrigin;
    double resizeStartSize;

    SelectionHandle handle;
    double panOriginX;
    double panOriginY;
    double panScrollX;
    double panScrollY;
    CellRef? tapCandidate;
    bool panMoved;

    /// <summary>What a sheet looked like when it was last showing.</summary>
    sealed record SheetViewState(GridMetrics Metrics, double ScrollX, double ScrollY, CellRef Anchor, CellRef Active);

    enum DragMode
    {
        None,
        SelectingCells,
        SelectingColumns,
        SelectingRows,
        ResizingColumn,
        ResizingRow,

        /// <summary>A finger dragging the sheet under itself.</summary>
        Panning,

        /// <summary>A finger dragging one of the selection's grab handles.</summary>
        ExtendingFromHandle
    }

    /// <summary>
    /// How far a finger may travel before a press stops being a tap.
    /// </summary>
    /// <remarks>
    /// Not zero: a finger never lands and lifts on the same pixel, so without some slack every tap
    /// would register as a one-pixel pan and select nothing.
    /// </remarks>
    const double TapSlop = 6;

    public SpreadsheetController(Workbook workbook, Worksheet sheet)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);

        this.Workbook = workbook;
        this.sheet = sheet;
        this.Metrics = GridMetrics.FromWorksheet(sheet);
        this.Viewport = new GridViewport(this.Metrics);
        this.Selection = new SpreadsheetSelection();
        this.Selection.Changed += (_, _) => this.RaiseChanged();
        this.Find = new SpreadsheetFinder(this);

        // Every edit, undo and redo goes through the stack, so this is the one signal that catches all
        // of them - a cell typed into, a range pasted, a row inserted. Hooking Changed instead would
        // drop the cached matches on every scroll, which is the same as not caching them at all.
        // Weakly: the workbook is the app's and outlives the view this controller paints. See WeakEvent.
        workbook.Undo.Changed += WeakEvent.Forward(this, workbook.Undo, static c => c.Find.Invalidate(), static (u, h) => u.Changed -= h);
        workbook.SheetsChanged += WeakEvent.Forward(this, workbook, static c => c.Find.Invalidate(), static (w, h) => w.SheetsChanged -= h);

        // The match list leads with the active sheet - and, unless the search spans the workbook, is
        // only that sheet - so switching tabs makes it the wrong list rather than a stale one.
        this.ActiveSheetChanged += (_, _) => this.Find.Invalidate();
    }

    /// <summary>
    /// Text search over the workbook, which is what the toolbar's find box drives.
    /// </summary>
    /// <remarks>
    /// Created with the controller so a host can bind a find bar to it before anything is searched
    /// for, and the bar's readout is live from the first keystroke.
    /// </remarks>
    public SpreadsheetFinder Find { get; }

    /// <summary>The sheets a tab strip should offer, in book order. Hidden sheets are left out.</summary>
    public IReadOnlyList<Worksheet> VisibleSheets => this.Workbook.Sheets.Where(x => x.IsVisible).ToList();

    public Workbook Workbook { get; }

    public Worksheet Sheet => this.sheet;

    public GridMetrics Metrics { get; private set; }

    public GridViewport Viewport { get; private set; }

    public SpreadsheetSelection Selection { get; }

    /// <summary>The cell currently being edited, or null when no editor is open.</summary>
    public CellRef? EditingCell { get; private set; }

    /// <summary>The text the editor should show. Meaningful only while <see cref="EditingCell"/> is set.</summary>
    public string EditingText { get; private set; } = string.Empty;

    /// <summary>Raised whenever the host needs to repaint.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when an editor should be opened or closed over the active cell.</summary>
    public event EventHandler<CellRef?>? EditingChanged;

    /// <summary>
    /// Raised when a different sheet becomes the one on screen — by a tab click, or because a
    /// structural edit took the old one away.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Changed"/>, which every scroll and keystroke raises. A host binding
    /// its sheet name two-way needs the rare event, not the constant one.
    /// </remarks>
    public event EventHandler<Worksheet>? ActiveSheetChanged;

    public void SwitchSheet(Worksheet target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (ReferenceEquals(target, this.sheet))
            return;

        this.CancelEdit();
        this.Remember();
        this.Adopt(target);
        this.RaiseChanged();
        this.ActiveSheetChanged?.Invoke(this, target);
    }

    /// <summary>Switches to a sheet by name. Unknown or hidden names are ignored.</summary>
    public void SwitchSheet(string name)
    {
        if (this.Workbook.Find(name) is { } target)
            this.SwitchSheet(target);
    }

    /// <summary>Files the current sheet's position away so coming back to it lands where it was left.</summary>
    void Remember()
        => this.sheetState[this.sheet.Name] = new SheetViewState(
            this.Metrics,
            this.Viewport.ScrollX,
            this.Viewport.ScrollY,
            this.Selection.Anchor,
            this.Selection.Active);

    /// <summary>Makes a sheet the current one, restoring where it was left if it has been seen before.</summary>
    void Adopt(Worksheet target)
    {
        var remembered = this.sheetState.GetValueOrDefault(target.Name);

        this.sheet = target;

        // A remembered metrics object carries the column widths the user dragged out by hand. Rebuilding
        // it from the sheet would throw those away every time a tab was clicked.
        this.Metrics = remembered?.Metrics ?? GridMetrics.FromWorksheet(target);
        this.Viewport = new GridViewport(this.Metrics) { Width = this.Viewport.Width, Height = this.Viewport.Height };

        if (remembered is null)
        {
            this.Selection.MoveTo(new CellRef(0, 0));
            return;
        }

        this.Viewport.ScrollTo(remembered.ScrollX, remembered.ScrollY);

        // Anchor first, then extend: that reproduces both the range and which end of it is active.
        this.Selection.MoveTo(remembered.Anchor);
        if (remembered.Active != remembered.Anchor)
            this.Selection.ExtendTo(remembered.Active);
    }

    public void Resize(double width, double height)
    {
        this.Viewport.Width = width;
        this.Viewport.Height = height;
        this.RaiseChanged();
    }

    /// <summary>The text a formula bar should show: the formula when there is one, otherwise the literal.</summary>
    public string ActiveCellText => this.CellText(this.Selection.Active);

    /// <summary>The active cell's address in A1 notation — what a formula bar's name box shows.</summary>
    public string ActiveCellAddress => this.Selection.Active.Relative().ToString();

    /// <summary>
    /// Writes text into the active cell, reading it exactly as typing it into the cell would.
    /// </summary>
    /// <remarks>
    /// For a formula bar, which edits the same cell from somewhere else on screen. It deliberately does
    /// not go through <see cref="BeginEdit"/>: that raises <see cref="EditingChanged"/> and would open
    /// the in-cell editor on top of the grid, leaving two editors live on one cell and each unaware of
    /// what the other holds.
    /// </remarks>
    public void SetActiveCellText(string text) => this.SetCellText(this.Selection.Active, text);

    /// <summary>
    /// Writes text into a named cell, reading it exactly as typing it into that cell would.
    /// </summary>
    /// <remarks>
    /// The cell is named rather than assumed to be the active one because a formula bar loses focus
    /// <em>after</em> the click that moved the selection: committing to whatever is active by then
    /// would put the text in the cell the user clicked on rather than the one they were editing.
    /// </remarks>
    public void SetCellText(CellRef cell, string text)
    {
        this.CancelEdit();
        this.Workbook.Undo.BreakCoalescing();
        this.Apply(cell.Relative(), text ?? string.Empty);
        this.RaiseChanged();
    }

    /// <summary>
    /// Moves the selection to a cell and scrolls it into view — what typing an address into the name
    /// box does.
    /// </summary>
    public void GoTo(CellRef cell)
    {
        this.CancelEdit();
        this.Selection.MoveTo(cell.Relative());
        this.Viewport.ScrollIntoView(this.Selection.Active);
        this.RaiseChanged();
    }

    /// <summary>
    /// The cells on the sheet being shown that hold a find match, for the painter to wash.
    /// </summary>
    /// <remarks>
    /// Whole cells, and only this sheet's. Matches elsewhere in the workbook have nothing on screen to
    /// draw — the toolbar's count is what says they are there.
    /// </remarks>
    public IReadOnlyList<CellRef> FindMatchCells()
    {
        if (!this.Find.IsSearching)
            return [];

        var name = this.sheet.Name;
        var cells = new List<CellRef>();

        foreach (var match in this.Find.Matches)
        {
            if (!string.Equals(match.Sheet, name, StringComparison.Ordinal))
                continue;

            // Two hits in one cell wash the same rectangle twice. The list is in cell order, so the
            // duplicate is always the one just added.
            if (cells.Count > 0 && cells[^1] == match.Cell)
                continue;

            cells.Add(match.Cell);
        }

        return cells;
    }

    /// <summary>
    /// Moves the selection onto a find match, switching sheets first when the hit is on another one.
    /// </summary>
    internal void SelectFindMatch(SpreadsheetFindMatch match)
    {
        if (!string.Equals(match.Sheet, this.sheet.Name, StringComparison.Ordinal)
            && this.Workbook.Find(match.Sheet) is { } target)
        {
            this.SwitchSheet(target);
        }

        this.GoTo(match.Cell);
    }

    /// <summary>What a cell would show in a formula bar: its formula when it has one, else its literal.</summary>
    public string CellText(CellRef cell)
    {
        var formula = this.sheet.GetFormula(cell);
        if (formula is not null)
            return "=" + formula;

        var value = this.sheet.GetValue(cell);
        return value.IsBlank ? string.Empty : Calc.Coercion.ToText(value);
    }

    // ---- pointer ----

    /// <summary>True once a finger has been seen, so the surface should draw touch affordances.</summary>
    /// <remarks>
    /// Tracked rather than asked of the platform because both kinds turn up in one session — an iPad
    /// with a keyboard, a Windows laptop with a touchscreen — and the handles are only useful to
    /// whichever one is actually in the user's hand right now.
    /// </remarks>
    public bool UsesTouch { get; private set; }

    public void PointerDown(double x, double y, bool extend = false, PointerKind kind = PointerKind.Mouse)
    {
        this.CommitEdit(EditCommitDirection.None);

        if (kind == PointerKind.Touch)
        {
            this.UsesTouch = true;

            if (this.BeginTouchDrag(x, y))
                return;
        }

        var hit = this.Viewport.HitTest(x, y);
        switch (hit.Target)
        {
            case HitTarget.SelectAllCorner:
                this.Selection.SelectAll();
                return;

            case HitTarget.ColumnResize:
                this.drag = DragMode.ResizingColumn;
                this.resizeIndex = hit.Cell.Column;
                this.resizeOrigin = x;
                this.resizeStartSize = this.Metrics.Columns.SizeOf(hit.Cell.Column);
                return;

            case HitTarget.RowResize:
                this.drag = DragMode.ResizingRow;
                this.resizeIndex = hit.Cell.Row;
                this.resizeOrigin = y;
                this.resizeStartSize = this.Metrics.Rows.SizeOf(hit.Cell.Row);
                return;

            case HitTarget.ColumnHeader:
                this.drag = DragMode.SelectingColumns;
                this.Selection.SelectColumn(hit.Cell.Column);
                return;

            case HitTarget.RowHeader:
                this.drag = DragMode.SelectingRows;
                this.Selection.SelectRow(hit.Cell.Row);
                return;

            case HitTarget.Cell:
                this.drag = DragMode.SelectingCells;
                if (extend)
                    this.Selection.ExtendTo(hit.Cell);
                else
                    this.Selection.MoveTo(hit.Cell);

                return;
        }
    }

    /// <summary>
    /// Decides what a finger landing on the grid begins. Returns false to fall through to the mouse
    /// behaviour.
    /// </summary>
    /// <remarks>
    /// Only the content area changes under touch. A tap on a header is a deliberate grab at a specific
    /// control — it selects the column or row, or resizes it — and turning those into a pan as well
    /// would take the row and column selection away from touch entirely, which is what cut, copy and
    /// insert operate on.
    /// </remarks>
    bool BeginTouchDrag(double x, double y)
    {
        if (this.Viewport.SelectionHandleAt(this.Selection.Range, x, y) is { } grabbed)
        {
            this.drag = DragMode.ExtendingFromHandle;
            this.handle = grabbed;
            return true;
        }

        var hit = this.Viewport.HitTest(x, y);
        if (hit.Target is not (HitTarget.Cell or HitTarget.None))
            return false;

        this.drag = DragMode.Panning;
        this.panOriginX = x;
        this.panOriginY = y;
        this.panScrollX = this.Viewport.ScrollX;
        this.panScrollY = this.Viewport.ScrollY;

        // Held rather than applied: the press only becomes a selection if the finger lifts without
        // travelling, and applying it now would move the selection on every fling.
        this.tapCandidate = hit.IsCell ? hit.Cell : null;
        this.panMoved = false;
        return true;
    }

    public void PointerMove(double x, double y)
    {
        switch (this.drag)
        {
            case DragMode.Panning:
            {
                var dx = this.panOriginX - x;
                var dy = this.panOriginY - y;

                if (!this.panMoved && Math.Abs(dx) <= TapSlop && Math.Abs(dy) <= TapSlop)
                    return;

                this.panMoved = true;
                this.ApplyScrollLimits();
                this.Viewport.ScrollTo(this.panScrollX + dx, this.panScrollY + dy);
                this.RaiseChanged();
                return;
            }

            case DragMode.ExtendingFromHandle:
            {
                var grabbed = this.Viewport.HitTest(x, y);
                if (grabbed.IsCell)
                {
                    // The handle being dragged moves; the corner opposite it stays put. Selecting the
                    // range from the fixed corner is what lets a drag past it flip the selection
                    // rather than collapsing it to nothing.
                    var range = this.Selection.Range;
                    var fixedCorner = this.handle == SelectionHandle.End
                        ? new CellRef(range.Left, range.Top)
                        : new CellRef(range.Right, range.Bottom);

                    this.Selection.SelectRange(new CellRange(fixedCorner, grabbed.Cell));
                    this.Viewport.ScrollIntoView(grabbed.Cell);
                    this.RaiseChanged();
                }

                return;
            }

            case DragMode.None:
                return;

            case DragMode.ResizingColumn:
                // Below a few pixels a column reads as hidden rather than narrow, so clamp above zero.
                this.Metrics.Columns.SetSize(this.resizeIndex, Math.Max(2, this.resizeStartSize + (x - this.resizeOrigin)));
                this.RaiseChanged();
                return;

            case DragMode.ResizingRow:
                this.Metrics.Rows.SetSize(this.resizeIndex, Math.Max(2, this.resizeStartSize + (y - this.resizeOrigin)));
                this.RaiseChanged();
                return;
        }

        var hit = this.Viewport.HitTest(x, y);
        if (hit.Cell is { } cell)
        {
            switch (this.drag)
            {
                case DragMode.SelectingColumns:
                    this.Selection.ExtendTo(new CellRef(cell.Column, CellRef.MaxRow));
                    break;

                case DragMode.SelectingRows:
                    this.Selection.ExtendTo(new CellRef(CellRef.MaxColumn, cell.Row));
                    break;

                default:
                    this.Selection.ExtendTo(cell);
                    break;
            }
        }
    }

    public void PointerUp()
    {
        if (this.drag is DragMode.Panning or DragMode.ExtendingFromHandle)
        {
            var wasPan = this.drag == DragMode.Panning;
            var candidate = this.tapCandidate;
            var moved = this.panMoved;

            this.drag = DragMode.None;
            this.tapCandidate = null;
            this.panMoved = false;

            // A press that went nowhere was a tap, and a tap selects.
            if (wasPan && !moved && candidate is { } cell)
            {
                this.Selection.MoveTo(cell);
                this.RaiseChanged();
            }

            return;
        }

        // A resize drag only moved the in-memory metrics. Recording it here is what makes a column the
        // user widened still be that width after a save and reopen.
        if (this.resizeIndex >= 0)
        {
            var index = this.resizeIndex;
            var mode = this.drag;

            this.drag = DragMode.None;
            this.resizeIndex = -1;

            if (mode == DragMode.ResizingColumn)
                this.CommitColumnWidth(index, index, this.Metrics.Columns.SizeOf(index));
            else if (mode == DragMode.ResizingRow)
                this.CommitRowHeight(index, this.Metrics.Rows.SizeOf(index));

            return;
        }

        this.drag = DragMode.None;
        this.resizeIndex = -1;
    }

    public void DoubleClick(double x, double y)
    {
        var hit = this.Viewport.HitTest(x, y);
        if (hit.Target == HitTarget.Cell)
        {
            this.Selection.MoveTo(hit.Cell);
            this.BeginEdit();
        }
    }

    public void Scroll(double dx, double dy)
    {
        this.ApplyScrollLimits();
        this.Viewport.ScrollBy(dx, dy);
        this.RaiseChanged();
    }

    /// <summary>
    /// Sets how far the sheet may be scrolled, from what is actually in it.
    /// </summary>
    /// <remarks>
    /// Recomputed per gesture rather than cached: the used range grows as cells are typed and the
    /// viewport is resized by the host without telling anyone, so a limit worked out once is wrong by
    /// the next rotation. It is two offset lookups, which is nothing beside the repaint that follows.
    /// The slack past the last used cell is one screen — enough to type below or to the right of the
    /// data, which is how a sheet grows, without the sheet being able to scroll into nowhere.
    /// </remarks>
    void ApplyScrollLimits()
    {
        var viewWidth = Math.Max(0, this.Viewport.Width - this.Viewport.ContentOriginX);
        var viewHeight = Math.Max(0, this.Viewport.Height - this.Viewport.ContentOriginY);

        if (this.sheet.UsedRange is not { } used)
        {
            this.Viewport.MaxScrollX = viewWidth;
            this.Viewport.MaxScrollY = viewHeight;
            return;
        }

        var contentWidth = this.Metrics.Columns.SizeOfRange(this.Metrics.FrozenPane.Column, used.Right + 1);
        var contentHeight = this.Metrics.Rows.SizeOfRange(this.Metrics.FrozenPane.Row, used.Bottom + 1);

        this.Viewport.MaxScrollX = Math.Max(0, contentWidth - viewWidth) + viewWidth;
        this.Viewport.MaxScrollY = Math.Max(0, contentHeight - viewHeight) + viewHeight;
    }

    // ---- keyboard ----

    public void Move(MoveDirection direction, bool extend = false, bool toEdge = false)
    {
        this.CommitEdit(EditCommitDirection.None);

        if (toEdge)
            this.Selection.MoveToEdge(direction, cell => !this.sheet.GetValue(cell).IsBlank, extend);
        else
            this.Selection.Move(direction, extend);

        this.Viewport.ScrollIntoView(this.Selection.Active);
        this.RaiseChanged();
    }

    public void Advance(bool byRow, bool backwards = false)
    {
        this.Selection.Advance(byRow, backwards);
        this.Viewport.ScrollIntoView(this.Selection.Active);
        this.RaiseChanged();
    }

    /// <summary>Clears the contents of the selection, leaving formatting intact.</summary>
    public void ClearSelection()
    {
        this.Workbook.Execute(new ClearRangeCommand(this.sheet.Name, this.Selection.Range));
        this.SetClipboard(null);
        this.RaiseChanged();
    }

    // ---- clipboard ----
    //
    // This is the control's own clipboard, not the operating system's: nothing here reaches another
    // application and nothing another application copied reaches here. It holds a snapshot rather than
    // a live reference to the source range, because a cut has to survive its own source being cleared,
    // and because pasting the same block twice must produce the same thing both times.

    SpreadsheetClipboardContent? clipboard;

    /// <summary>What was last cut or copied, or null when nothing is pending.</summary>
    public SpreadsheetClipboardContent? Clipboard => this.clipboard;

    /// <summary>True when <see cref="Paste"/> would do something — what a paste button enables on.</summary>
    public bool CanPaste => this.clipboard is not null;

    /// <summary>
    /// The range the marching-ants border is drawn around.
    /// </summary>
    /// <remarks>
    /// Null when nothing is pending, and also when the pending capture came from a different sheet:
    /// the content is still there to paste, but the cells it was read from are not on screen, and
    /// drawing the border over whatever happens to occupy those coordinates here would be a lie.
    /// </remarks>
    public CellRange? ClipboardRange
        => this.clipboard is { } content && string.Equals(content.SheetName, this.sheet.Name, StringComparison.OrdinalIgnoreCase)
            ? content.Source
            : null;

    /// <summary>Raised when the pending cut or copy changes, including when it is abandoned.</summary>
    /// <remarks>
    /// Separate from <see cref="Changed"/> so a host can start and stop the border's animation on it
    /// rather than restarting a timer on every keystroke.
    /// </remarks>
    public event EventHandler? ClipboardChanged;

    /// <summary>Takes a copy of the selection, leaving it where it is.</summary>
    public void Copy() => this.Capture(SpreadsheetClipboardOperation.Copy);

    /// <summary>
    /// Marks the selection to be moved by the next paste.
    /// </summary>
    /// <remarks>
    /// Nothing is removed here, which is Excel's behaviour and not an omission: the source is cleared
    /// by the paste, in the same undo step, so a cut that is never pasted leaves the sheet untouched.
    /// </remarks>
    public void Cut() => this.Capture(SpreadsheetClipboardOperation.Cut);

    void Capture(SpreadsheetClipboardOperation operation)
    {
        this.CommitEdit(EditCommitDirection.None);
        this.SetClipboard(SpreadsheetClipboardContent.Capture(this.sheet, this.Selection.Range, operation));
    }

    /// <summary>
    /// Writes the pending cut or copy at the selection, as one undo step.
    /// </summary>
    /// <remarks>
    /// A whole-row or whole-column capture lands on whole rows or columns — only the row of the
    /// selection is read for the former and only its column for the latter, because a row put down
    /// three columns to the right would no longer be that row.
    /// </remarks>
    /// <returns>False when there was nothing on the clipboard, in which case nothing was written.</returns>
    public bool Paste()
    {
        if (this.clipboard is not { } content)
            return false;

        var target = this.Selection.Range.TopLeft;

        this.Begin();
        this.Workbook.Execute(new PasteClipboardCommand(content, this.sheet.Name, target));

        this.SyncBandMetrics(content, target);
        this.Selection.SelectRange(PastedRange(content, target));

        // A copy stays on the clipboard so it can be pasted again, the way Excel keeps its marquee up
        // after Ctrl+V. A cut has been spent — its source is now empty, and pasting it a second time
        // would move cells that are no longer there.
        if (content.Operation == SpreadsheetClipboardOperation.Cut)
            this.SetClipboard(null);

        this.RaiseChanged();
        return true;
    }

    /// <summary>Abandons the pending cut or copy, taking the marching-ants border with it.</summary>
    public void ClearClipboard() => this.SetClipboard(null);

    /// <summary>Where a paste lands, so the selection can be moved onto it the way Excel does.</summary>
    static CellRange PastedRange(SpreadsheetClipboardContent content, CellRef target) => content.Kind switch
    {
        SpreadsheetClipboardKind.Rows => new CellRange(
            new CellRef(0, target.Row),
            new CellRef(CellRef.MaxColumn, Math.Min(CellRef.MaxRow, target.Row + content.RowCount - 1))),

        SpreadsheetClipboardKind.Columns => new CellRange(
            new CellRef(target.Column, 0),
            new CellRef(Math.Min(CellRef.MaxColumn, target.Column + content.ColumnCount - 1), CellRef.MaxRow)),

        _ => new CellRange(
            target,
            new CellRef(
                Math.Min(CellRef.MaxColumn, target.Column + content.ColumnCount - 1),
                Math.Min(CellRef.MaxRow, target.Row + content.RowCount - 1)))
    };

    /// <summary>
    /// Re-reads the heights or widths a band paste just wrote into the file.
    /// </summary>
    /// <remarks>
    /// The metrics are the grid's own copy of that geometry and nothing else updates them, so without
    /// this a pasted row keeps the height of the row it replaced and every row below it is drawn at
    /// an offset the file no longer agrees with.
    /// </remarks>
    void SyncBandMetrics(SpreadsheetClipboardContent content, CellRef target)
    {
        foreach (var band in content.Bands)
        {
            switch (content.Kind)
            {
                case SpreadsheetClipboardKind.Rows:
                    var row = target.Row + band.Offset;
                    if (row > CellRef.MaxRow)
                        continue;

                    if (this.sheet.GetRowHeight(row) is { } points)
                        this.Metrics.Rows.SetSize(row, GridMetrics.PointsToPixels(points));
                    else
                        this.Metrics.Rows.ResetSize(row);

                    break;

                case SpreadsheetClipboardKind.Columns:
                    var column = target.Column + band.Offset;
                    if (column > CellRef.MaxColumn)
                        continue;

                    if (this.sheet.GetColumnWidth(column) is { } characters)
                        this.Metrics.Columns.SetSize(column, GridMetrics.WidthToPixels(characters));
                    else
                        this.Metrics.Columns.ResetSize(column);

                    break;
            }
        }
    }

    void SetClipboard(SpreadsheetClipboardContent? content)
    {
        if (ReferenceEquals(this.clipboard, content))
            return;

        this.clipboard = content;
        this.ClipboardChanged?.Invoke(this, EventArgs.Empty);
        this.RaiseChanged();
    }

    // ---- inserting and removing rows and columns ----
    //
    // Each of these is two things that have to happen together: the command, which moves the cells and
    // repoints every formula in the workbook that named them, and the metrics shift, which moves the
    // grid's own record of the heights and widths so it still describes the sheet it is painting.

    /// <summary>Inserts blank rows above the selection, pushing everything below them down.</summary>
    public void InsertRows(int count = 1) => this.EditBand(rows: true, count, inserting: true);

    /// <summary>Inserts blank columns to the left of the selection, pushing everything right.</summary>
    public void InsertColumns(int count = 1) => this.EditBand(rows: false, count, inserting: true);

    /// <summary>
    /// Removes rows from the top of the selection down, closing the gap.
    /// </summary>
    /// <remarks>
    /// A formula that pointed into the removed rows becomes <c>#REF!</c>, as it does in Excel — the
    /// cells are gone rather than moved, and there is nothing left for it to follow. Undo puts both
    /// the rows and those formulas back.
    /// </remarks>
    public void DeleteRows(int count = 1) => this.EditBand(rows: true, count, inserting: false);

    /// <summary>Removes columns from the left of the selection across, closing the gap.</summary>
    public void DeleteColumns(int count = 1) => this.EditBand(rows: false, count, inserting: false);

    void EditBand(bool rows, int count, bool inserting)
    {
        if (count < 1)
            return;

        var at = rows ? this.Selection.Range.Top : this.Selection.Range.Left;
        var limit = (rows ? CellRef.MaxRow : CellRef.MaxColumn) + 1;
        count = Math.Min(count, limit - at);

        if (count < 1)
            return;

        this.Begin();

        this.Workbook.Execute((rows, inserting) switch
        {
            (true, true) => new InsertRowsCommand(this.sheet.Name, at, count),
            (true, false) => new DeleteRowsCommand(this.sheet.Name, at, count),
            (false, true) => new InsertColumnsCommand(this.sheet.Name, at, count),
            _ => (IEditCommand<Workbook>)new DeleteColumnsCommand(this.sheet.Name, at, count)
        });

        var axis = rows ? this.Metrics.Rows : this.Metrics.Columns;
        axis.Shift(at, inserting ? count : -count);

        // The capture's coordinates describe a sheet that no longer exists at those addresses, so
        // keeping it would let a later paste put the cells back in the wrong place.
        this.SetClipboard(null);
        this.RaiseChanged();
    }

    // ---- formatting ----
    //
    // Everything a toolbar needs, and nothing a host has to reimplement. Each of these is one undoable
    // command over the current selection, and each reads the active cell's own format first - so
    // "bold" is a toggle rather than a set, and pressing it on a bold selection unbolds it.

    /// <summary>
    /// The formatting the active cell renders with — what a toolbar shows as its current state.
    /// </summary>
    /// <remarks>
    /// The active cell's, not the selection's. A range can hold twenty different formats and has no
    /// single answer; Excel resolves the same way, by reporting the one cell that receives typing.
    /// </remarks>
    public ResolvedFormat ActiveFormat
        => this.Workbook.Styles.Resolve(this.sheet.GetEffectiveStyleIndex(this.Selection.Active));

    public bool CanUndo => this.Workbook.Undo.CanUndo;

    public bool CanRedo => this.Workbook.Undo.CanRedo;

    /// <summary>Applies a formatting change to the selection as one undo step.</summary>
    public void ApplyFormat(CellFormatChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.IsEmpty)
            return;

        this.Begin();
        this.Workbook.Execute(new FormatRangeCommand(this.sheet.Name, this.Selection.Range, change));
        this.RaiseChanged();
    }

    public void ToggleBold() => this.ApplyFormat(new CellFormatChange { Bold = !this.ActiveFormat.Bold });

    public void ToggleItalic() => this.ApplyFormat(new CellFormatChange { Italic = !this.ActiveFormat.Italic });

    public void ToggleUnderline() => this.ApplyFormat(new CellFormatChange { Underline = !this.ActiveFormat.Underline });

    public void ToggleStrikethrough() => this.ApplyFormat(new CellFormatChange { Strike = !this.ActiveFormat.Strike });

    /// <summary>
    /// Wraps or unwraps text in the selection.
    /// </summary>
    /// <remarks>
    /// Stored and saved, and Excel honours it on open. The grid itself still paints one line per cell —
    /// wrapped text needs row auto-height, which the layout does not do yet.
    /// </remarks>
    public void ToggleWrapText() => this.ApplyFormat(new CellFormatChange { WrapText = !this.ActiveFormat.WrapText });

    /// <summary>
    /// Sets the horizontal alignment, or clears it back to General by passing the current one again.
    /// </summary>
    /// <remarks>
    /// Pressing "align left" twice returning to General is deliberate: General is not a fourth option
    /// nobody would pick, it is the setting that lets numbers stay right-aligned and text left-aligned,
    /// and there is otherwise no way back to it from a toolbar.
    /// </remarks>
    public void SetAlignment(CellHorizontalAlignment alignment)
        => this.ApplyFormat(new CellFormatChange
        {
            HorizontalAlignment = this.ActiveFormat.HorizontalAlignment == alignment
                ? CellHorizontalAlignment.General
                : alignment
        });

    public void SetVerticalAlignment(CellVerticalAlignment alignment)
        => this.ApplyFormat(new CellFormatChange { VerticalAlignment = alignment });

    public void SetFontFamily(string family)
    {
        if (!string.IsNullOrWhiteSpace(family))
            this.ApplyFormat(new CellFormatChange { FontName = family });
    }

    public void SetFontSize(double size)
    {
        if (size > 0)
            this.ApplyFormat(new CellFormatChange { FontSize = size });
    }

    public void SetTextColor(ArgbColor color) => this.ApplyFormat(new CellFormatChange { Foreground = color });

    /// <summary>Highlights the selection, or removes the highlight when <paramref name="color"/> is null.</summary>
    public void SetFillColor(ArgbColor? color)
        => this.ApplyFormat(new CellFormatChange { Background = color ?? ArgbColor.Transparent });

    /// <summary>Moves the selection's indent by <paramref name="delta"/> levels, never below zero.</summary>
    public void AdjustIndent(int delta)
    {
        var indent = Math.Max(0, this.ActiveFormat.Indent + delta);
        if (indent != this.ActiveFormat.Indent)
            this.ApplyFormat(new CellFormatChange { Indent = indent });
    }

    public void SetNumberFormat(NumberFormatPreset preset)
        => this.ApplyFormat(new CellFormatChange { NumberFormatCode = NumberFormats.CodeOf(preset) });

    /// <summary>Applies a raw Excel number format code, e.g. <c>#,##0.00_);[Red](#,##0.00)</c>.</summary>
    public void SetNumberFormatCode(string code)
        => this.ApplyFormat(new CellFormatChange { NumberFormatCode = code ?? string.Empty });

    /// <summary>Adds or removes decimal places on the selection's number format.</summary>
    public void AdjustDecimals(int delta)
    {
        var code = NumberFormats.AdjustDecimals(this.ActiveFormat.NumberFormatCode, delta);
        if (!string.Equals(code, this.ActiveFormat.NumberFormatCode, StringComparison.Ordinal))
            this.ApplyFormat(new CellFormatChange { NumberFormatCode = code });
    }

    /// <summary>Strips the selection back to the default format, leaving its contents alone.</summary>
    public void ClearFormatting() => this.ApplyFormat(CellFormatChange.Clear);

    // ---- auto functions ----

    /// <summary>
    /// Writes SUM, AVERAGE, COUNT, MIN or MAX over whatever the selection implies, as one undo step.
    /// </summary>
    /// <remarks>
    /// See <see cref="AutoFunctions"/> for how the range and destination are chosen. The selection
    /// moves to the last formula written, which is where Excel leaves it and what makes a second press
    /// of the button total the totals rather than repeating the first one.
    /// </remarks>
    /// <returns>False when there was nothing to total, in which case nothing was written.</returns>
    public bool ApplyAutoFunction(AutoFunction function)
    {
        var plan = AutoFunctions.Plan(this.sheet, this.Selection.Range);
        if (plan.Count == 0)
            return false;

        this.Begin();

        var commands = new List<IEditCommand<Workbook>>(plan.Count);
        foreach (var entry in plan)
            commands.Add(new SetCellFormulaCommand(this.sheet.Name, entry.Target, AutoFunctions.Formula(function, entry.Source)));

        this.Workbook.Execute(new CompositeCommand<Workbook>(AutoFunctions.DisplayName(function), commands));

        this.Selection.MoveTo(plan[^1].Target);
        this.Viewport.ScrollIntoView(this.Selection.Active);
        this.RaiseChanged();
        return true;
    }

    // ---- columns and rows ----

    /// <summary>The columns the selection spans, as an inclusive zero-based pair.</summary>
    public (int First, int Last) SelectedColumns => (this.Selection.Range.Left, this.Selection.Range.Right);

    /// <summary>The rows the selection spans, as an inclusive zero-based pair.</summary>
    public (int First, int Last) SelectedRows => (this.Selection.Range.Top, this.Selection.Range.Bottom);

    /// <summary>Sets the width of every column in the selection, in pixels, and records it in the file.</summary>
    public void SetColumnWidth(double pixels)
    {
        var (first, last) = this.SelectedColumns;
        this.CommitColumnWidth(first, last, Math.Max(2, pixels));
    }

    /// <summary>
    /// Sizes the selection's columns to their contents.
    /// </summary>
    /// <remarks>
    /// Measured in characters rather than pixels, from the formatted text of every populated cell in
    /// the column. That is an approximation — it takes no account of the font each cell uses — but the
    /// alternative is a text measurer, which lives in the paint layer and would put a rendering
    /// dependency into a class that deliberately has none.
    /// </remarks>
    public void AutoFitColumns()
    {
        var (first, last) = this.SelectedColumns;

        // A header click selects every column; fitting all 16,384 of them is not what was meant.
        if (this.sheet.UsedRange is not { } used)
            return;

        last = Math.Min(last, used.Right);
        if (first > last)
            return;

        this.Begin();

        var commands = new List<IEditCommand<Workbook>>();
        for (var column = first; column <= last; column++)
        {
            var characters = this.FitWidth(column, used);
            commands.Add(new SetColumnWidthCommand(this.sheet.Name, column, column, characters));
            this.Metrics.Columns.SetSize(column, GridMetrics.WidthToPixels(characters));
        }

        this.Workbook.Execute(new CompositeCommand<Workbook>("Auto Fit", commands));
        this.RaiseChanged();
    }

    double FitWidth(int column, CellRange used)
    {
        var widest = 0;
        var styles = this.Workbook.Styles;

        for (var row = used.Top; row <= used.Bottom; row++)
        {
            var cell = new CellRef(column, row);
            var value = this.sheet.GetDisplayValue(cell);
            if (value.IsBlank)
                continue;

            var text = styles.Format(value, styles.Resolve(this.sheet.GetEffectiveStyleIndex(cell)));
            widest = Math.Max(widest, text.Length);
        }

        // One character of padding, and never narrower than a column header's own label.
        return Math.Clamp(widest + 1, 3, 255);
    }

    /// <summary>Hides or shows the selection's columns.</summary>
    public void SetColumnsHidden(bool hidden)
    {
        var (first, last) = this.SelectedColumns;

        // Hiding every column would leave a sheet with nothing on it and no header to unhide from.
        if (hidden && first == 0 && last >= CellRef.MaxColumn)
            return;

        this.Begin();
        this.Workbook.Execute(new SetColumnHiddenCommand(this.sheet.Name, first, last, hidden));

        for (var column = first; column <= last; column++)
            this.Metrics.Columns.SetHidden(column, hidden);

        this.RaiseChanged();
    }

    /// <summary>Records a column's dragged-out width in the file, so it survives a save.</summary>
    void CommitColumnWidth(int first, int last, double pixels)
    {
        var characters = GridMetrics.PixelsToWidth(pixels);

        this.Begin();
        this.Workbook.Execute(new SetColumnWidthCommand(this.sheet.Name, first, last, characters));

        for (var column = first; column <= last; column++)
            this.Metrics.Columns.SetSize(column, pixels);

        this.RaiseChanged();
    }

    void CommitRowHeight(int row, double pixels)
    {
        this.Begin();
        this.Workbook.Execute(new SetRowHeightCommand(this.sheet.Name, row, GridMetrics.PixelsToPoints(pixels)));
        this.Metrics.Rows.SetSize(row, pixels);
        this.RaiseChanged();
    }

    public void Undo()
    {
        this.CancelEdit();

        var before = this.CurrentSheetNames();
        this.Workbook.Undo.Undo();

        // What was undone may have been a sheet edit, in which case the sheet on screen has just been
        // renamed, hidden or deleted out from under this controller.
        this.AfterSheetsChanged(this.Appeared(before) ?? this.sheet.Name);
    }

    public void Redo()
    {
        this.CancelEdit();

        var before = this.CurrentSheetNames();
        this.Workbook.Undo.Redo();
        this.AfterSheetsChanged(this.Appeared(before) ?? this.sheet.Name);
    }

    HashSet<string> CurrentSheetNames()
        => new(this.Workbook.Sheets.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The sheet an undo or redo brought back, if it brought one back.
    /// </summary>
    /// <remarks>
    /// Undoing a delete has to land on the sheet that returned — staying put would leave the user
    /// looking at a different sheet and wondering whether the undo did anything. The stack cannot say
    /// what a command restored, but a sheet that was not there a moment ago is unambiguous enough.
    /// </remarks>
    string? Appeared(HashSet<string> before)
        => this.Workbook.Sheets.FirstOrDefault(x => x.IsVisible && !before.Contains(x.Name))?.Name;

    // ---- sheets ----
    //
    // Each of these is the pair of things a sheet edit actually is: the command, which changes the
    // workbook and can be undone, and the reconciliation, which decides what should be on screen
    // afterwards. Neither half is any use without the other - a delete that leaves the controller
    // pointing at the sheet it just removed paints a grid that no longer exists.

    /// <summary>
    /// Adds a sheet after the current one and switches to it.
    /// </summary>
    /// <param name="name">A name, or null for the next free <c>SheetN</c>.</param>
    /// <param name="index">Where to put it, or null for immediately after the current sheet.</param>
    public Worksheet AddSheet(string? name = null, int? index = null)
    {
        var chosen = name ?? SheetNames.NextDefault(this.Workbook.Sheets.Select(x => x.Name));
        var position = index ?? this.IndexOf(this.sheet) + 1;

        this.Begin();
        this.Workbook.Execute(new AddSheetCommand(chosen, position));
        this.AfterSheetsChanged(chosen);

        return this.Workbook[chosen];
    }

    /// <summary>
    /// Renames a sheet, repointing every formula that referred to it.
    /// </summary>
    /// <exception cref="ArgumentException">The name is illegal or already taken.</exception>
    public void RenameSheet(Worksheet target, string newName)
    {
        ArgumentNullException.ThrowIfNull(target);

        var previous = target.Name;
        if (string.Equals(previous, newName, StringComparison.Ordinal))
            return;

        this.Begin();
        this.Workbook.Execute(new RenameSheetCommand(previous, newName));

        // The remembered position is filed under the old name; without this the sheet you are looking
        // at jumps back to A1 the moment you rename it.
        if (this.sheetState.Remove(previous, out var state))
            this.sheetState[newName] = state;

        this.AfterSheetsChanged(newName);
    }

    /// <summary>Copies a sheet in beside the original and switches to the copy.</summary>
    public Worksheet DuplicateSheet(Worksheet target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var copyName = SheetNames.MakeUnique(target.Name, this.Workbook.Sheets.Select(x => x.Name));

        this.Begin();
        this.Workbook.Execute(new DuplicateSheetCommand(target.Name, copyName, this.IndexOf(target) + 1));
        this.AfterSheetsChanged(copyName);

        return this.Workbook[copyName];
    }

    /// <summary>Deletes a sheet. There is no confirmation here; that belongs to the host.</summary>
    /// <exception cref="InvalidOperationException">It is the only visible sheet left.</exception>
    public void DeleteSheet(Worksheet target)
    {
        ArgumentNullException.ThrowIfNull(target);

        // Whichever tab Excel would land on: the one to the right, or the one to the left at the end.
        var visible = this.VisibleSheets;
        var at = -1;
        for (var i = 0; i < visible.Count && at < 0; i++)
        {
            if (ReferenceEquals(visible[i], target))
                at = i;
        }

        var next = at < 0 ? null : visible.ElementAtOrDefault(at + 1) ?? visible.ElementAtOrDefault(at - 1);

        this.Begin();
        this.Workbook.Execute(new DeleteSheetCommand(target.Name));

        // The remembered position is deliberately kept: undoing the delete brings the sheet back, and
        // it should come back where it was rather than scrolled to the top.
        this.AfterSheetsChanged(next?.Name);
    }

    /// <summary>Moves a sheet to a position in the tab order.</summary>
    public void MoveSheet(Worksheet target, int index)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (this.IndexOf(target) == index)
            return;

        this.Begin();
        this.Workbook.Execute(new MoveSheetCommand(target.Name, index));
        this.AfterSheetsChanged(this.sheet.Name);
    }

    /// <summary>Hides or shows a sheet.</summary>
    /// <exception cref="InvalidOperationException">Hiding it would leave no visible sheet.</exception>
    public void SetSheetVisible(Worksheet target, bool visible)
    {
        ArgumentNullException.ThrowIfNull(target);

        this.Begin();
        this.Workbook.Execute(new SetSheetVisibilityCommand(target.Name, visible));
        this.AfterSheetsChanged(visible ? target.Name : null);
    }

    /// <summary>False when the sheet is the last one Excel would have a tab for.</summary>
    public bool CanRemoveFromView(Worksheet target)
        => target is not null && (!target.IsVisible || this.VisibleSheets.Count > 1);

    public int IndexOf(Worksheet target)
    {
        for (var i = 0; i < this.Workbook.Sheets.Count; i++)
        {
            if (ReferenceEquals(this.Workbook.Sheets[i], target))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Closes the editor and ends the coalescing run before a structural edit.
    /// </summary>
    /// <remarks>
    /// Without the break, adding a sheet in the middle of a typing run folds into that run's undo step,
    /// and one Ctrl+Z then takes the sheet away along with the characters.
    /// </remarks>
    void Begin()
    {
        this.CancelEdit();
        this.Workbook.Undo.BreakCoalescing();
    }

    /// <summary>Picks what should be on screen after the sheet list changed, and switches to it.</summary>
    void AfterSheetsChanged(string? preferred)
    {
        var target = this.Workbook.Find(preferred) is { IsVisible: true } named
            ? named
            : this.Resolve();

        if (target is null)
        {
            // Nothing visible is left to show. The workbook guards against this, so reaching here means
            // the grid keeps painting the last sheet rather than crashing on a null one.
            this.RaiseChanged();
            return;
        }

        if (ReferenceEquals(target, this.sheet))
        {
            this.RaiseChanged();
            return;
        }

        this.Remember();
        this.Adopt(target);
        this.RaiseChanged();
        this.ActiveSheetChanged?.Invoke(this, target);
    }

    /// <summary>
    /// The sheet the current one has become: itself if it survived, the sheet with its name if it was
    /// restored as a new instance, otherwise the first visible one.
    /// </summary>
    Worksheet? Resolve()
    {
        if (this.Workbook.Sheets.Any(x => ReferenceEquals(x, this.sheet)) && this.sheet.IsVisible)
            return this.sheet;

        if (this.Workbook.Find(this.sheet.Name) is { IsVisible: true } byName)
            return byName;

        return this.Workbook.Sheets.FirstOrDefault(x => x.IsVisible);
    }

    // ---- editing ----

    /// <summary>Opens the editor on the active cell, seeded with its current content.</summary>
    public void BeginEdit(string? initialText = null)
    {
        if (this.EditingCell is not null)
            return;

        var cell = this.Selection.Active;
        this.EditingCell = cell;

        // Typing over a cell replaces it; F2 or a double-click edits what is there.
        this.EditingText = initialText ?? this.CellText(cell);

        this.Workbook.Undo.BreakCoalescing();
        this.EditingChanged?.Invoke(this, cell);
        this.RaiseChanged();
    }

    public void UpdateEditingText(string text)
    {
        if (this.EditingCell is null)
            return;

        this.EditingText = text ?? string.Empty;
    }

    public void CancelEdit()
    {
        if (this.EditingCell is null)
            return;

        this.EditingCell = null;
        this.EditingText = string.Empty;
        this.EditingChanged?.Invoke(this, null);
        this.RaiseChanged();
    }

    /// <summary>Writes the editor's content into the cell and closes the editor.</summary>
    public void CommitEdit(EditCommitDirection direction)
    {
        if (this.EditingCell is not { } cell)
            return;

        var text = this.EditingText;
        this.EditingCell = null;
        this.EditingText = string.Empty;
        this.EditingChanged?.Invoke(this, null);

        this.Apply(cell, text);

        switch (direction)
        {
            case EditCommitDirection.Down:
                this.Advance(byRow: true);
                break;
            case EditCommitDirection.Up:
                this.Advance(byRow: true, backwards: true);
                break;
            case EditCommitDirection.Right:
                this.Advance(byRow: false);
                break;
            case EditCommitDirection.Left:
                this.Advance(byRow: false, backwards: true);
                break;
            default:
                this.RaiseChanged();
                break;
        }
    }

    /// <summary>
    /// Interprets typed text the way Excel does: a leading <c>=</c> is a formula, otherwise the text is
    /// parsed as a number, boolean, error or date before falling back to text.
    /// </summary>
    void Apply(CellRef cell, string text)
    {
        var sheetName = this.sheet.Name;

        // Typing supersedes a pending cut or copy, the way it does in Excel. Leaving the border up
        // over a sheet the user has moved on from is the part that reads as a bug.
        this.SetClipboard(null);

        if (text.Length == 0)
        {
            this.Workbook.Execute(new SetCellValueCommand(sheetName, cell, CellValue.Blank));
            return;
        }

        if (text.StartsWith('=') && text.Length > 1)
        {
            this.Workbook.Execute(new SetCellFormulaCommand(sheetName, cell, text[1..]));
            return;
        }

        this.Workbook.Execute(new SetCellValueCommand(sheetName, cell, ParseInput(text)));
    }

    /// <summary>Converts typed text into the value Excel would store.</summary>
    public static CellValue ParseInput(string text)
    {
        if (string.IsNullOrEmpty(text))
            return CellValue.Blank;

        var trimmed = text.Trim();

        if (trimmed.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            return CellValue.FromBoolean(true);

        if (trimmed.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            return CellValue.FromBoolean(false);

        if (CellValue.TryParseError(trimmed.ToUpperInvariant(), out var error))
            return CellValue.FromError(error);

        if (double.TryParse(trimmed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var number))
            return CellValue.FromNumber(number);

        // A percentage typed into a cell stores the fraction; the display format supplies the sign.
        if (trimmed.EndsWith('%') &&
            double.TryParse(trimmed[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var percent))
            return CellValue.FromNumber(percent / 100d);

        // Text that merely looks like a date is left as text; converting it would change what the user
        // typed, and only an explicit date format should do that.
        return CellValue.FromText(text);
    }

    void RaiseChanged() => this.Changed?.Invoke(this, EventArgs.Empty);
}
