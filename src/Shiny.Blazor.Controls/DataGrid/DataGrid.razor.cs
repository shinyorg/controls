using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

public partial class DataGrid<TItem> : IAsyncDisposable
{
    readonly List<ColumnBase<TItem>> columns = new();
    readonly HashSet<TItem> selected = new();
    IReadOnlyList<TItem>? serverItems;
    int? serverTotal;
    bool serverLoaded;

    [Parameter] public IEnumerable<TItem>? Items { get; set; }

    /// <summary>The column declarations — <see cref="PropertyColumn{TItem, TProperty}"/> / <see cref="TemplateColumn{TItem}"/>.</summary>
    [Parameter] public RenderFragment? Columns { get; set; }

    // --- Selection ---
    [Parameter] public DataGridSelectionMode SelectionMode { get; set; } = DataGridSelectionMode.None;
    [Parameter] public TItem? SelectedItem { get; set; }
    [Parameter] public EventCallback<TItem?> SelectedItemChanged { get; set; }
    [Parameter] public IReadOnlyCollection<TItem> SelectedItems { get; set; } = Array.Empty<TItem>();
    [Parameter] public EventCallback<IReadOnlyCollection<TItem>> SelectedItemsChanged { get; set; }

    // --- Styling ---
    [Parameter] public bool Dense { get; set; }
    [Parameter] public bool Striped { get; set; }
    [Parameter] public bool Bordered { get; set; }
    [Parameter] public bool Hover { get; set; } = true;
    [Parameter] public bool Outlined { get; set; } = true;
    [Parameter] public bool FixedHeader { get; set; }
    [Parameter] public string? Height { get; set; }
    [Parameter] public bool ShowColumnHeaders { get; set; } = true;

    // --- State ---
    [Parameter] public bool Loading { get; set; }
    [Parameter] public RenderFragment? LoadingContent { get; set; }
    [Parameter] public string NoRecordsText { get; set; } = "No records";
    [Parameter] public RenderFragment? NoRecordsContent { get; set; }

    // --- Events ---
    [Parameter] public EventCallback<TItem> RowClick { get; set; }

    [Parameter] public string? Class { get; set; }
    [Parameter] public string? Style { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }

    internal IReadOnlyList<ColumnBase<TItem>> VisibleColumns
    {
        get
        {
            var visible = this.columns.Where(c => !c.Hidden);
            if (this.columnOrder.Count == 0)
                return visible.ToList();
            return visible
                .OrderBy(c => { var i = this.columnOrder.IndexOf(c.Id); return i < 0 ? int.MaxValue : i; })
                .ToList();
        }
    }

    // ---- Virtualization / resize / reorder ----
    [Parameter] public bool Virtualize { get; set; }

    /// <summary>
    /// Enables the column resize handles. <see cref="DataGridColumnResizeMode.Column"/> widens the
    /// grid as a column grows; <see cref="DataGridColumnResizeMode.Container"/> takes the difference
    /// out of the next resizable column so the total stays put.
    /// </summary>
    [Parameter] public DataGridColumnResizeMode ColumnResizeMode { get; set; } = DataGridColumnResizeMode.None;

    /// <summary>
    /// Lets a column header be dragged and dropped into a new position. Off by default. The order
    /// lives on the grid, not on the columns - <see cref="ResetColumnOrder"/> drops back to declaration
    /// order, and <see cref="ColumnReordered"/> reports each drop so it can be persisted.
    /// </summary>
    [Parameter] public bool DragDropColumnReordering { get; set; }

    /// <summary>Raised after a column is dropped, with the resulting left-to-right column ids.</summary>
    [Parameter] public EventCallback<ColumnReorderedEventArgs> ColumnReordered { get; set; }

    /// <summary>
    /// Floor in pixels for every column that does not set its own <see cref="ColumnBase{TItem}.MinWidth"/>
    /// (default 48). Keeps a resize drag from collapsing a column to nothing.
    /// </summary>
    [Parameter] public double MinColumnWidth { get; set; } = 48;

    /// <summary>
    /// Ceiling in pixels for every column that does not set its own <see cref="ColumnBase{TItem}.MaxWidth"/>.
    /// Null (the default) leaves columns unbounded.
    /// </summary>
    [Parameter] public double? MaxColumnWidth { get; set; }

    /// <summary>Raised after a resize drag ends, with the column's id and its final pixel width.</summary>
    [Parameter] public EventCallback<ColumnResizedEventArgs> ColumnResized { get; set; }

    readonly Dictionary<string, string> columnWidths = new();
    readonly List<string> columnOrder = new();

    ColumnBase<TItem>? resizingColumn;
    ColumnBase<TItem>? resizeNeighbour;
    double resizeStartX;
    double resizeStartWidth;
    double resizeNeighbourStartWidth;
    bool resizeMeasured;
    string? dragColumnId;
    string? dragOverColumnId;

    internal bool CanVirtualize => this.Virtualize && !this.IsGrouped && !this.Paging && this.serverItems is null;

    /// <summary>True when this column offers a resize handle.</summary>
    internal bool CanResize(ColumnBase<TItem> col)
        => this.ColumnResizeMode != DataGridColumnResizeMode.None && (col.Resizable ?? true);

    /// <summary>The floor for <paramref name="col"/>: its own pixel MinWidth, else the grid's.</summary>
    internal double EffectiveMinWidth(ColumnBase<TItem> col)
        => ParseCssPx(col.MinWidth) ?? Math.Max(1, this.MinColumnWidth);

    /// <summary>The ceiling for <paramref name="col"/>: its own pixel MaxWidth, else the grid's, else none.</summary>
    internal double? EffectiveMaxWidth(ColumnBase<TItem> col)
    {
        var max = ParseCssPx(col.MaxWidth) ?? this.MaxColumnWidth;
        return max > 0 ? max : null;
    }

    /// <summary>
    /// Holds <paramref name="width"/> inside the column's min/max. The floor wins a contradictory
    /// pair (max below min) so a bad configuration still leaves a usable column rather than a sliver.
    /// </summary>
    internal double ClampColumnWidth(ColumnBase<TItem> col, double width)
    {
        var max = this.EffectiveMaxWidth(col);
        if (max is not null)
            width = Math.Min(width, max.Value);

        return Math.Max(width, this.EffectiveMinWidth(col));
    }

    static double? ParseCssPx(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var v = value.Trim();
        if (!v.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            return null;

        return double.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var px) && px > 0
            ? px
            : null;
    }

    internal async Task OnResizeStartAsync(ColumnBase<TItem> col, PointerEventArgs e)
    {
        this.resizingColumn = col;
        this.resizeStartX = e.ClientX;
        this.resizeMeasured = false;
        this.resizeNeighbour = this.ColumnResizeMode == DataGridColumnResizeMode.Container
            ? this.NextResizable(col)
            : null;

        // A column with no declared width has no width in C# at all - only the browser knows what the
        // table gave it. Seeding from a constant is what made the first drag jump the column to some
        // unrelated size instead of nudging it from where the user sees it.
        this.resizeStartWidth = await this.MeasureColumnAsync(col)
            ?? this.PxWidth(col)
            ?? Math.Max(this.EffectiveMinWidth(col), 150);

        this.resizeNeighbourStartWidth = this.resizeNeighbour is null
            ? 0
            : await this.MeasureColumnAsync(this.resizeNeighbour)
                ?? this.PxWidth(this.resizeNeighbour)
                ?? Math.Max(this.EffectiveMinWidth(this.resizeNeighbour), 150);

        this.resizeMeasured = true;
    }

    ColumnBase<TItem>? NextResizable(ColumnBase<TItem> col)
        => this.VisibleColumns
            .SkipWhile(c => !ReferenceEquals(c, col))
            .Skip(1)
            .FirstOrDefault(c => c.Resizable ?? true);

    internal void OnResizeMove(PointerEventArgs e)
    {
        // resizeMeasured gates the drag on the measurement round-trip: on Blazor Server a pointermove
        // can beat the JS call home, and applying a delta to a start width of 0 snaps the column shut.
        if (this.resizingColumn is null || !this.resizeMeasured)
            return;

        var col = this.resizingColumn;
        var width = this.ClampColumnWidth(col, this.resizeStartWidth + (e.ClientX - this.resizeStartX));

        if (this.resizeNeighbour is not null)
        {
            var (resized, neighbourWidth) = this.ResolveContainerResize(
                col, this.resizeStartWidth, this.resizeNeighbour, this.resizeNeighbourStartWidth, width);

            width = resized;
            this.columnWidths[this.resizeNeighbour.Id] = Px(neighbourWidth);
        }

        this.columnWidths[col.Id] = Px(width);
        this.StateHasChanged();
    }

    /// <summary>
    /// Container mode: the drag moves the boundary between two columns, so whatever one gains the
    /// other gives up and the pair's total never changes.
    /// </summary>
    /// <remarks>
    /// The neighbour's own clamp can refuse part of the delta - it will not shrink past its minimum -
    /// and the refusal is handed back to the dragged column so the total still holds. What the
    /// neighbour gives up is capped at what the drag actually asked for: a neighbour that already sits
    /// outside its own bounds would otherwise be yanked into them on the first pixel of an unrelated
    /// drag, and hand the dragged column that entire correction as a jump.
    /// </remarks>
    internal (double Width, double NeighbourWidth) ResolveContainerResize(
        ColumnBase<TItem> col,
        double startWidth,
        ColumnBase<TItem> neighbour,
        double neighbourStartWidth,
        double targetWidth)
    {
        var wanted = targetWidth - startWidth;
        var refused = neighbourStartWidth - this.ClampColumnWidth(neighbour, neighbourStartWidth - wanted);
        var accepted = Math.Clamp(refused, Math.Min(0, wanted), Math.Max(0, wanted));

        return (this.ClampColumnWidth(col, startWidth + accepted), neighbourStartWidth - accepted);
    }

    internal async Task OnResizeEndAsync()
    {
        // Also wired to pointerleave on the root, which fires on every mouse-out of the grid - not
        // only after a drag - so this returns before touching any state when nothing is resizing.
        if (this.resizingColumn is null)
            return;

        var col = this.resizingColumn;
        this.resizingColumn = null;
        this.resizeNeighbour = null;
        this.resizeMeasured = false;

        if (col is not null && this.ColumnResized.HasDelegate && this.columnWidths.TryGetValue(col.Id, out var w))
            await this.ColumnResized.InvokeAsync(new ColumnResizedEventArgs(col.Id, ParseCssPx(w) ?? 0));
    }

    static string Px(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "px";

    /// <summary>Clears any interactive resize, dropping every column back to its declared width.</summary>
    public void ResetColumnWidths()
    {
        this.columnWidths.Clear();
        this.StateHasChanged();
    }

    internal bool CanReorder => this.DragDropColumnReordering;

    internal void OnColumnDragStart(ColumnBase<TItem> col)
    {
        this.dragColumnId = col.Id;
        this.dragOverColumnId = null;
    }

    /// <summary>The drop marker: which header is hovered, and which of its edges the column lands on.</summary>
    internal string? DropCssClass(ColumnBase<TItem> col)
    {
        if (this.dragColumnId is null || this.dragOverColumnId != col.Id || this.dragColumnId == col.Id)
            return null;

        return DropsAfter(this.EffectiveOrder, this.dragColumnId, col.Id)
            ? " shiny-dg-drop-after"
            : " shiny-dg-drop-before";
    }

    internal void OnColumnDragOver(ColumnBase<TItem> col)
    {
        if (this.dragColumnId is null || this.dragOverColumnId == col.Id)
            return;

        this.dragOverColumnId = col.Id;
        this.StateHasChanged();
    }

    /// <summary>Ends a drag that was released outside a header, so no marker is left behind.</summary>
    internal void OnColumnDragEnd()
    {
        if (this.dragColumnId is null && this.dragOverColumnId is null)
            return;

        this.dragColumnId = null;
        this.dragOverColumnId = null;
        this.StateHasChanged();
    }

    /// <summary>
    /// A column dropped on one to its right lands <i>after</i> it, and on one to its left,
    /// <i>before</i> it.
    /// </summary>
    /// <remarks>
    /// Inserting before the target unconditionally - which is what this did - made dragging a column
    /// one place to the right a no-op: removing it and re-inserting it in front of its own right-hand
    /// neighbour puts it back exactly where it started, so the header simply refused to move.
    /// </remarks>
    internal static bool DropsAfter(IReadOnlyList<string> order, string draggedId, string targetId)
    {
        var from = IndexOf(order, draggedId);
        var to = IndexOf(order, targetId);
        return from >= 0 && to > from;
    }

    /// <summary>The order after <paramref name="draggedId"/> is dropped on <paramref name="targetId"/>.</summary>
    internal static List<string> Reorder(IReadOnlyList<string> order, string draggedId, string targetId)
    {
        var result = order.ToList();
        if (draggedId == targetId || !result.Remove(draggedId))
            return result;

        var after = DropsAfter(order, draggedId, targetId);
        var targetIdx = result.IndexOf(targetId);
        result.Insert(targetIdx < 0 ? result.Count : after ? targetIdx + 1 : targetIdx, draggedId);
        return result;
    }

    static int IndexOf(IReadOnlyList<string> order, string id)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (order[i] == id)
                return i;
        }
        return -1;
    }

    /// <summary>Declaration order until a drop has established one of its own.</summary>
    internal IReadOnlyList<string> EffectiveOrder
        => this.columnOrder.Count > 0
            ? this.columnOrder
            : this.columns.Where(c => !c.Hidden).Select(c => c.Id).ToList();

    internal async Task OnColumnDropAsync(ColumnBase<TItem> target)
    {
        var dragged = this.dragColumnId;
        this.dragOverColumnId = null;
        this.dragColumnId = null;

        if (dragged is null || dragged == target.Id)
        {
            this.StateHasChanged();
            return;
        }

        var order = Reorder(this.EffectiveOrder, dragged, target.Id);
        this.columnOrder.Clear();
        this.columnOrder.AddRange(order);

        this.StateHasChanged();

        if (this.ColumnReordered.HasDelegate)
            await this.ColumnReordered.InvokeAsync(new ColumnReorderedEventArgs(dragged, order));
    }

    /// <summary>Drops any interactive reordering, putting the columns back in declaration order.</summary>
    public void ResetColumnOrder()
    {
        this.columnOrder.Clear();
        this.StateHasChanged();
    }

    internal bool HasMultiSelect => this.SelectionMode == DataGridSelectionMode.Multiple;

    // ---- Frozen (pinned) columns ----

    /// <summary>Freezes the first N visible columns to the leading edge. Overridden upward by any
    /// leading columns that set <see cref="ColumnBase{TItem}.Frozen"/> themselves.</summary>
    [Parameter] public int FrozenColumns { get; set; }

    /// <summary>Freezes the last N visible columns to the trailing edge.</summary>
    [Parameter] public int FrozenEndColumns { get; set; }

    readonly Dictionary<string, FrozenCell> frozenCells = new();

    internal int FrozenStartCount { get; private set; }

    internal int FrozenEndCount { get; private set; }

    internal bool HasFrozenColumns => this.FrozenStartCount > 0 || this.FrozenEndCount > 0;

    /// <summary>
    /// The expander and multi-select columns are always leftmost, so they pin with the start block.
    /// </summary>
    internal bool FrozenLeadColumns => this.LeadColumnCount > 0 && this.FrozenStartCount > 0;

    /// <summary>Lead-column index: the expander comes first, then the checkbox.</summary>
    internal int CheckColumnIndex => this.HasExpanderColumn ? 1 : 0;

    internal string? LeadFrozenAttr() => this.FrozenLeadColumns ? "start" : null;

    internal string LeadFrozenCssClass()
        => this.FrozenLeadColumns ? " shiny-dg-frozen shiny-dg-frozen-start" : string.Empty;

    /// <summary>
    /// First-paint offset for a pinned lead cell. The expander column has a fixed CSS width so its
    /// neighbour can be placed up front; datagrid.js re-measures and corrects both after render.
    /// </summary>
    internal string? LeadFrozenOffsetStyle(int index)
        => this.FrozenLeadColumns
            ? string.Create(CultureInfo.InvariantCulture, $"left:{(index == 0 ? 0 : ExpanderWidthPx):0.##}px;")
            : null;

    readonly record struct FrozenCell(DataGridFrozen Position, bool Edge, double? Offset);

    /// <summary>
    /// Recomputes which columns are pinned and (where the widths are known up front) how far in.
    /// Called once at the top of the render so the per-cell helpers below are plain lookups.
    /// </summary>
    void RefreshFrozenLayout()
    {
        var cols = this.VisibleColumns;

        var start = 0;
        while (start < cols.Count && cols[start].EffectiveFrozen == DataGridFrozen.Start)
            start++;
        start = Math.Clamp(Math.Max(start, this.FrozenColumns), 0, cols.Count);

        var end = 0;
        while (end < cols.Count && cols[cols.Count - 1 - end].EffectiveFrozen == DataGridFrozen.End)
            end++;
        end = Math.Clamp(Math.Max(end, this.FrozenEndColumns), 0, cols.Count - start);

        this.FrozenStartCount = start;
        this.FrozenEndCount = end;

        this.frozenCells.Clear();
        if (start == 0 && end == 0)
            return;

        // Best-effort offsets so the pinning is right on the very first paint (and without JS at
        // all when every pinned column declares a px width). datagrid.js re-measures and corrects
        // whatever we could not work out here.
        // The checkbox column is sized by content, so anything after it has to wait for the JS
        // measurement; the fixed-width expander column can be resolved here.
        double? offset = this.HasMultiSelect ? null : this.HasExpanderColumn ? ExpanderWidthPx : 0;
        for (var i = 0; i < start; i++)
        {
            this.frozenCells[cols[i].Id] = new FrozenCell(DataGridFrozen.Start, i == start - 1, offset);
            var w = this.PxWidth(cols[i]);
            offset = offset is null || w is null ? null : offset + w;
        }

        offset = 0;
        for (var i = 0; i < end; i++)
        {
            var col = cols[cols.Count - 1 - i];
            this.frozenCells[col.Id] = new FrozenCell(DataGridFrozen.End, i == end - 1, offset);
            var w = this.PxWidth(col);
            offset = offset is null || w is null ? null : offset + w;
        }
    }

    internal double? PxWidth(ColumnBase<TItem> col)
    {
        var w = this.columnWidths.TryGetValue(col.Id, out var resized) ? resized : col.Width;
        if (string.IsNullOrWhiteSpace(w))
            return null;

        w = w.Trim();
        return w.EndsWith("px", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(w[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var px)
            ? px
            : null;
    }

    /// <summary>The <c>data-dg-frozen</c> marker datagrid.js keys its measurements off.</summary>
    internal string? FrozenAttr(ColumnBase<TItem> col)
    {
        return this.frozenCells.TryGetValue(col.Id, out var f)
            ? f.Position == DataGridFrozen.Start ? "start" : "end"
            : null;
    }

    internal string FrozenCssClass(ColumnBase<TItem> col)
    {
        if (!this.frozenCells.TryGetValue(col.Id, out var f))
            return string.Empty;

        var side = f.Position == DataGridFrozen.Start ? "start" : "end";
        return f.Edge
            ? $" shiny-dg-frozen shiny-dg-frozen-{side} shiny-dg-frozen-{side}-edge"
            : $" shiny-dg-frozen shiny-dg-frozen-{side}";
    }

    internal string? FrozenOffsetStyle(ColumnBase<TItem> col)
    {
        if (!this.frozenCells.TryGetValue(col.Id, out var f) || f.Offset is not { } offset)
            return null;

        var edge = f.Position == DataGridFrozen.Start ? "left" : "right";
        return string.Create(CultureInfo.InvariantCulture, $"{edge}:{offset:0.##}px;");
    }

    // ---- Column registration ----
    internal void AddColumn(ColumnBase<TItem> column)
    {
        if (!this.columns.Contains(column))
        {
            this.columns.Add(column);
            this.InvalidateImplicitSummaryRow();
            this.StateHasChanged();
        }
    }

    internal void RemoveColumn(ColumnBase<TItem> column)
    {
        if (this.columns.Remove(column))
        {
            this.InvalidateImplicitSummaryRow();
            this.StateHasChanged();
        }
    }

    internal void NotifyColumnsChanged()
    {
        // A column's own Aggregate/FooterTemplate is what the synthesized summary row is built from.
        this.InvalidateImplicitSummaryRow();
        this.StateHasChanged();
    }

    protected override void OnParametersSet()
    {
        this.SyncGroupByParameter();
        this.InvalidateImplicitSummaryRow();

        // Same for expansion - a caller can drive it from the outside.
        if (this.ExpandedItems.Count > 0 || this.expandedItems.Count > 0)
        {
            if (!this.expandedItems.SetEquals(this.ExpandedItems))
            {
                this.expandedItems.Clear();
                foreach (var item in this.ExpandedItems)
                    this.expandedItems.Add(item);
            }
        }

        // Keep the internal selection set in sync with bound parameters.
        if (this.SelectionMode == DataGridSelectionMode.Multiple)
        {
            this.selected.Clear();
            foreach (var item in this.SelectedItems)
                this.selected.Add(item);
        }
        else if (this.SelectionMode == DataGridSelectionMode.Single)
        {
            this.selected.Clear();
            if (this.SelectedItem is not null)
                this.selected.Add(this.SelectedItem);
        }
    }

    // ---- Sorting / filtering / paging pipeline ----
    [Parameter] public DataGridSortMode SortMode { get; set; } = DataGridSortMode.Single;

    /// <summary>A quick filter predicate applied across all rows (toolbar search).</summary>
    [Parameter] public Func<TItem, bool>? QuickFilter { get; set; }

    [Parameter] public int RowsPerPage { get; set; } = 10;
    [Parameter] public EventCallback<int> RowsPerPageChanged { get; set; }
    [Parameter] public int CurrentPage { get; set; }
    [Parameter] public EventCallback<int> CurrentPageChanged { get; set; }

    /// <summary>Pager slot — place a <see cref="DataGridPager{TItem}"/> here to enable paging.</summary>
    [Parameter] public RenderFragment? PagerContent { get; set; }

    readonly List<SortDefinition> sortDefs = new();
    readonly List<FilterDefinition> filterDefs = new();

    internal bool Paging => this.PagerContent is not null;

    internal bool EffectiveSortable(ColumnBase<TItem> col)
        => col.Sortable ?? (this.SortMode != DataGridSortMode.None && col.HasValue);

    internal IReadOnlyList<SortDefinition> SortDefinitions => this.sortDefs;
    internal IReadOnlyList<FilterDefinition> FilterDefinitions => this.filterDefs;

    internal SortDefinition? GetSortDefinition(ColumnBase<TItem> col)
        => this.sortDefs.FirstOrDefault(d => d.ColumnId == col.Id);

    internal int SortOrderBadge(ColumnBase<TItem> col)
    {
        if (this.SortMode != DataGridSortMode.Multiple || this.sortDefs.Count < 2)
            return 0;
        var def = this.GetSortDefinition(col);
        return def is null ? 0 : def.Order + 1;
    }

    internal async Task ToggleSortAsync(ColumnBase<TItem> col)
    {
        if (!this.EffectiveSortable(col))
            return;

        var existing = this.GetSortDefinition(col);
        var next = existing is null
            ? DataGridSortDirection.Ascending
            : existing.Direction == DataGridSortDirection.Ascending
                ? DataGridSortDirection.Descending
                : DataGridSortDirection.None;

        if (this.SortMode != DataGridSortMode.Multiple)
            this.sortDefs.Clear();
        else
            this.sortDefs.RemoveAll(d => d.ColumnId == col.Id);

        if (next != DataGridSortDirection.None)
            this.sortDefs.Add(new SortDefinition(col.Id, next, this.sortDefs.Count));

        await this.ReloadServerDataAsync();
        this.StateHasChanged();
    }

    // ---- Filtering UI ----
    [Parameter] public DataGridFilterMode FilterMode { get; set; } = DataGridFilterMode.Menu;

    string quickSearch = string.Empty;
    string? openFilterColumnId;

    internal DataGridFilterMode EffectiveFilterMode => this.FilterMode;

    internal bool EffectiveFilterable(ColumnBase<TItem> col)
        => (col.Filterable ?? col.HasValue) && col.HasValue;

    internal bool AnyFilterable => this.VisibleColumns.Any(this.EffectiveFilterable);

    internal string QuickSearch => this.quickSearch;

    internal async Task SetQuickSearchAsync(string? value)
    {
        this.quickSearch = value ?? string.Empty;
        this.CurrentPage = 0;
        await this.ReloadServerDataAsync();
        this.StateHasChanged();
    }

    internal bool IsFilterMenuOpen(ColumnBase<TItem> col) => this.openFilterColumnId == col.Id;

    internal void ToggleFilterMenu(ColumnBase<TItem> col)
    {
        this.openFilterColumnId = this.openFilterColumnId == col.Id ? null : col.Id;
        this.StateHasChanged();
    }

    internal FilterDefinition GetOrCreateFilter(ColumnBase<TItem> col)
    {
        var def = this.filterDefs.FirstOrDefault(d => d.ColumnId == col.Id);
        if (def is null)
        {
            def = new FilterDefinition { ColumnId = col.Id, Operator = DefaultOperator(col.GetDataType()) };
            this.filterDefs.Add(def);
        }
        return def;
    }

    internal bool HasActiveFilter(ColumnBase<TItem> col)
        => this.filterDefs.Any(d => d.ColumnId == col.Id &&
            (d.Value is not null || d.Operator is DataGridFilterOperator.Empty or DataGridFilterOperator.NotEmpty));

    internal async Task ApplyColumnFilterAsync(ColumnBase<TItem> col, DataGridFilterOperator op, object? value)
    {
        var def = this.GetOrCreateFilter(col);
        def.Operator = op;
        def.Value = value;
        this.openFilterColumnId = null;
        this.CurrentPage = 0;
        await this.ReloadServerDataAsync();
        this.StateHasChanged();
    }

    internal async Task ClearColumnFilterAsync(ColumnBase<TItem> col)
    {
        this.filterDefs.RemoveAll(d => d.ColumnId == col.Id);
        this.openFilterColumnId = null;
        await this.ReloadServerDataAsync();
        this.StateHasChanged();
    }

    internal static IReadOnlyList<DataGridFilterOperator> OperatorsFor(Type type)
    {
        if (type == typeof(string))
            return new[] { DataGridFilterOperator.Contains, DataGridFilterOperator.NotContains, DataGridFilterOperator.Equals, DataGridFilterOperator.NotEquals, DataGridFilterOperator.StartsWith, DataGridFilterOperator.EndsWith, DataGridFilterOperator.Empty, DataGridFilterOperator.NotEmpty };
        if (type == typeof(bool))
            return new[] { DataGridFilterOperator.Is };
        if (type.IsEnum)
            return new[] { DataGridFilterOperator.Is, DataGridFilterOperator.IsNot };
        // numeric / date
        return new[] { DataGridFilterOperator.Equals, DataGridFilterOperator.NotEquals, DataGridFilterOperator.GreaterThan, DataGridFilterOperator.GreaterThanOrEqual, DataGridFilterOperator.LessThan, DataGridFilterOperator.LessThanOrEqual };
    }

    internal static string OperatorLabel(DataGridFilterOperator op) => op switch
    {
        DataGridFilterOperator.Contains => "contains",
        DataGridFilterOperator.NotContains => "not contains",
        DataGridFilterOperator.Equals => "equals",
        DataGridFilterOperator.NotEquals => "not equals",
        DataGridFilterOperator.StartsWith => "starts with",
        DataGridFilterOperator.EndsWith => "ends with",
        DataGridFilterOperator.Empty => "is empty",
        DataGridFilterOperator.NotEmpty => "is not empty",
        DataGridFilterOperator.GreaterThan => ">",
        DataGridFilterOperator.GreaterThanOrEqual => "≥",
        DataGridFilterOperator.LessThan => "<",
        DataGridFilterOperator.LessThanOrEqual => "≤",
        DataGridFilterOperator.Is => "is",
        DataGridFilterOperator.IsNot => "is not",
        _ => op.ToString()
    };

    static DataGridFilterOperator DefaultOperator(Type type)
        => type == typeof(string) ? DataGridFilterOperator.Contains
            : type == typeof(bool) || type.IsEnum ? DataGridFilterOperator.Is
            : DataGridFilterOperator.Equals;

    /// <summary>
    /// The rows the grid is working from: in a flat grid every item, in tree mode the *roots*. Both go
    /// through <see cref="ProcessLevel"/>, which is also what each expanded node's children run through
    /// - so a tree is filtered and sorted one level at a time.
    /// </summary>
    internal IReadOnlyList<TItem> ProcessedItems()
    {
        if (this.serverItems is not null)
            return this.serverItems;

        return this.ProcessLevel(this.Items ?? Enumerable.Empty<TItem>());
    }

    /// <summary>Filters and sorts one level of items - the roots, or one node's children.</summary>
    internal IReadOnlyList<TItem> ProcessLevel(IEnumerable<TItem> items)
        => this.ApplySort(this.HasActiveFilters ? items.Where(this.KeepInResults) : items);

    bool HasActiveFilters
        => this.filterDefs.Count > 0 || !string.IsNullOrEmpty(this.quickSearch) || this.QuickFilter is not null;

    /// <summary>
    /// A node survives filtering if it matches, or if anything beneath it does - dropping a parent
    /// whose child matched would hide the match along with it.
    /// </summary>
    bool KeepInResults(TItem item)
        => this.MatchesFilters(item) || (this.TreeEnabled && this.AnyDescendantMatches(item));

    bool AnyDescendantMatches(TItem item)
    {
        // Children that have not been fetched yet cannot be searched; keep the branch so the user can
        // still open it rather than silently pruning a subtree that may well match.
        if (this.NeedsChildrenLoad(item))
            return true;

        foreach (var child in this.RawChildren(item))
        {
            if (this.KeepInResults(child))
                return true;
        }
        return false;
    }

    bool MatchesFilters(TItem item)
    {
        if (this.QuickFilter is not null && !this.QuickFilter(item))
            return false;

        if (!string.IsNullOrEmpty(this.quickSearch))
        {
            var term = this.quickSearch;
            var hit = this.VisibleColumns.Any(c =>
                c.HasValue && (c.GetText(item)?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false));
            if (!hit)
                return false;
        }

        foreach (var def in this.filterDefs)
        {
            var col = this.columns.FirstOrDefault(c => c.Id == def.ColumnId);
            if (col is null)
                continue;
            if (!DataGridFilterEvaluator.Matches(col.GetValue(item), def.Operator, def.Value))
                return false;
        }
        return true;
    }

    IReadOnlyList<TItem> ApplySort(IEnumerable<TItem> source)
    {
        if (this.sortDefs.Count == 0)
            return source as IReadOnlyList<TItem> ?? source.ToList();

        IOrderedEnumerable<TItem>? ordered = null;
        foreach (var def in this.sortDefs.OrderBy(d => d.Order))
        {
            var col = this.columns.FirstOrDefault(c => c.Id == def.ColumnId);
            if (col is null)
                continue;

            var comparer = col.Comparer ?? DataGridValueComparer.Instance;
            Func<TItem, object?> key = col.GetValue;
            var asc = def.Direction == DataGridSortDirection.Ascending;

            ordered = ordered is null
                ? (asc ? source.OrderBy(key, comparer) : source.OrderByDescending(key, comparer))
                : (asc ? ordered.ThenBy(key, comparer) : ordered.ThenByDescending(key, comparer));
        }
        return ((IEnumerable<TItem>?)ordered ?? source).ToList();
    }

    internal int TotalItems => this.serverTotal ?? this.ProcessedItems().Count;

    internal int TotalPages => this.Paging
        ? Math.Max(1, (int)Math.Ceiling(this.TotalItems / (double)Math.Max(1, this.RowsPerPage)))
        : 1;

    internal async Task SetPageAsync(int page)
    {
        var clamped = Math.Clamp(page, 0, this.TotalPages - 1);
        if (clamped == this.CurrentPage && this.serverItems is not null)
            return;
        this.CurrentPage = clamped;
        await this.CurrentPageChanged.InvokeAsync(this.CurrentPage);
        await this.ReloadServerDataAsync();
        this.StateHasChanged();
    }

    internal async Task SetRowsPerPageAsync(int rows)
    {
        this.RowsPerPage = rows;
        this.CurrentPage = 0;
        await this.RowsPerPageChanged.InvokeAsync(rows);
        await this.CurrentPageChanged.InvokeAsync(0);
        await this.ReloadServerDataAsync();
        this.StateHasChanged();
    }

    /// <summary>The rows to render after filter → sort → page.</summary>
    internal IReadOnlyList<TItem> GetRenderedItems()
    {
        var processed = this.ProcessedItems();
        if (!this.Paging || this.serverItems is not null)
            return processed;

        var start = this.CurrentPage * this.RowsPerPage;
        if (start >= processed.Count && processed.Count > 0)
        {
            this.CurrentPage = this.TotalPages - 1;
            start = this.CurrentPage * this.RowsPerPage;
        }
        return processed.Skip(start).Take(this.RowsPerPage).ToList();
    }

    // ---- Server-side data ----
    [Parameter] public Func<GridState, Task<GridData<TItem>>>? ServerData { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await this.SyncStickyLayoutAsync();

        if (firstRender && this.ServerData is not null && !this.serverLoaded)
        {
            this.serverLoaded = true;
            await this.ReloadServerDataAsync();
        }
    }

    // ---- Sticky header / frozen column measurement ----
    [Inject] IJSRuntime JS { get; set; } = default!;

    ElementReference rootRef;
    IJSObjectReference? stickyModule;
    bool stickyDisposed;

    bool NeedsStickyLayout => this.FixedHeader || this.HasFrozenColumns;

    // Resizing needs the module too - not for the sticky observer, but to ask the browser how wide a
    // column actually ended up before a drag starts moving it.
    bool NeedsJsModule => this.NeedsStickyLayout || this.ColumnResizeMode != DataGridColumnResizeMode.None;

    async Task SyncStickyLayoutAsync()
    {
        if (this.stickyDisposed)
            return;

        try
        {
            if (!this.NeedsJsModule)
            {
                if (this.stickyModule is not null)
                    await this.stickyModule.InvokeVoidAsync("dispose", this.rootRef);
                return;
            }

            if (this.stickyModule is null)
            {
                var loaded = await this.JS.InvokeAsync<IJSObjectReference>(
                    "import", "./_content/Shiny.Blazor.Controls/datagrid.js");
                if (this.stickyDisposed) { await loaded.ReleaseLateAsync(); return; }
                this.stickyModule = loaded;
            }

            if (!this.NeedsStickyLayout)
                return;

            // init is idempotent: it wires the observer once and re-measures on every call, which is
            // exactly what we want after a re-render changed the columns or their widths.
            await this.stickyModule.InvokeVoidAsync("init", this.rootRef);
        }
        catch (JSDisconnectedException)
        {
            // circuit went away mid-render; nothing to clean up
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>The column's rendered header width in pixels, or null when JS is unavailable.</summary>
    async Task<double?> MeasureColumnAsync(ColumnBase<TItem> col)
    {
        if (this.stickyModule is null || this.stickyDisposed)
            return null;

        try
        {
            var width = await this.stickyModule.InvokeAsync<double>("measureColumn", this.rootRef, col.Id);
            return width > 0 ? width : null;
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        this.stickyDisposed = true;
        if (this.stickyModule is null)
            return;

        try
        {
            await this.stickyModule.InvokeVoidAsync("dispose", this.rootRef);
            await this.stickyModule.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            this.stickyModule = null;
        }
    }

    async Task ReloadServerDataAsync()
    {
        if (this.ServerData is null)
            return;

        var state = new GridState
        {
            Page = this.CurrentPage,
            PageSize = this.RowsPerPage,
            SortDefinitions = this.sortDefs.ToList(),
            FilterDefinitions = this.filterDefs.ToList()
        };
        var data = await this.ServerData(state);
        this.serverItems = data.Items;
        this.serverTotal = data.TotalItems;
        this.StateHasChanged();
    }

    internal bool IsSelected(TItem item) => this.selected.Contains(item);

    internal bool AllSelected
    {
        get
        {
            var items = this.GetRenderedItems();
            return items.Count > 0 && items.All(this.selected.Contains);
        }
    }

    internal async Task ToggleRowAsync(TItem item)
    {
        switch (this.SelectionMode)
        {
            case DataGridSelectionMode.Single:
                this.selected.Clear();
                this.selected.Add(item);
                this.SelectedItem = item;
                await this.SelectedItemChanged.InvokeAsync(item);
                break;

            case DataGridSelectionMode.Multiple:
                if (!this.selected.Add(item))
                    this.selected.Remove(item);
                await this.RaiseSelectedItemsAsync();
                break;
        }
        this.StateHasChanged();
    }

    internal async Task ToggleSelectAllAsync(bool select)
    {
        this.selected.Clear();
        if (select)
        {
            foreach (var item in this.GetRenderedItems())
                this.selected.Add(item);
        }
        await this.RaiseSelectedItemsAsync();
        this.StateHasChanged();
    }

    async Task RaiseSelectedItemsAsync()
    {
        this.SelectedItems = this.selected.ToList();
        await this.SelectedItemsChanged.InvokeAsync(this.SelectedItems);
    }

    internal async Task RowClickedAsync(TItem item)
    {
        if (this.RowClick.HasDelegate)
            await this.RowClick.InvokeAsync(item);

        if (this.ExpandOnRowClick && this.CanExpand(item))
            await this.ToggleExpandAsync(item);

        if (this.EditMode == DataGridEditMode.Form && this.EditTrigger == DataGridEditTrigger.OnRowClick && this.EditingEnabled)
        {
            await this.StartEditRowAsync(item);
            return;
        }

        if (this.SelectionMode != DataGridSelectionMode.None)
            await this.ToggleRowAsync(item);
    }

    internal Task OnCellClickAsync(TItem item, ColumnBase<TItem> col)
        => this.EditMode == DataGridEditMode.Cell && this.EffectiveEditable(col)
            ? this.StartEditCellAsync(item, col)
            : Task.CompletedTask;

    internal async Task EditorKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
            await this.CommitEditAsync();
        else if (e.Key == "Escape")
            await this.CancelEditAsync();
    }

    internal Task OnEditorBlurAsync()
        => this.editingCell is not null ? this.CommitEditAsync() : Task.CompletedTask;

    internal CellContext<TItem> CreateCellContext(TItem item)
        => new(
            item,
            this.IsSelected(item),
            new CellContext<TItem>.CellActions
            {
                SetSelectedItem = select => { _ = this.ToggleRowAsync(item); }
            });

    [Parameter] public RenderFragment? ToolbarContent { get; set; }

    // ---- Inline editing ----
    [Parameter] public DataGridEditMode EditMode { get; set; } = DataGridEditMode.None;
    [Parameter] public DataGridEditTrigger EditTrigger { get; set; } = DataGridEditTrigger.OnRowClick;
    [Parameter] public bool ReadOnly { get; set; }
    [Parameter] public EventCallback<TItem> StartedEditingItem { get; set; }
    [Parameter] public EventCallback<TItem> CommittedItemChanges { get; set; }
    [Parameter] public EventCallback<TItem> CanceledEditingItem { get; set; }

    TItem? editingItem;
    ColumnBase<TItem>? editingCell;
    readonly Dictionary<string, object?> editValues = new();

    internal bool EditingEnabled => this.EditMode != DataGridEditMode.None && !this.ReadOnly;

    internal bool EffectiveEditable(ColumnBase<TItem> col)
        => this.EditingEnabled && (col.Editable ?? col.HasValue) && col.HasValue;

    internal bool IsEditingRow(TItem item) => this.editingItem is not null && EqualityComparer<TItem>.Default.Equals(this.editingItem, item);

    internal bool IsEditingCell(TItem item, ColumnBase<TItem> col)
        => this.IsEditingRow(item) && ReferenceEquals(this.editingCell, col);

    internal object? GetEditValue(ColumnBase<TItem> col) => this.editValues.TryGetValue(col.Id, out var v) ? v : null;

    internal void SetEditValue(ColumnBase<TItem> col, object? value) => this.editValues[col.Id] = value;

    void BeginEdit(TItem item)
    {
        this.editingItem = item;
        this.editValues.Clear();
        foreach (var col in this.columns.Where(c => c.HasValue))
            this.editValues[col.Id] = col.GetValue(item);
    }

    internal async Task StartEditRowAsync(TItem item)
    {
        if (!this.EditingEnabled)
            return;
        this.BeginEdit(item);
        this.editingCell = null;
        await this.StartedEditingItem.InvokeAsync(item);
        this.StateHasChanged();
    }

    internal async Task StartEditCellAsync(TItem item, ColumnBase<TItem> col)
    {
        if (!this.EffectiveEditable(col))
            return;
        this.BeginEdit(item);
        this.editingCell = col;
        await this.StartedEditingItem.InvokeAsync(item);
        this.StateHasChanged();
    }

    internal async Task CommitEditAsync()
    {
        if (this.editingItem is null)
            return;
        var item = this.editingItem;

        foreach (var col in this.columns.Where(this.EffectiveEditable))
        {
            if (this.editValues.TryGetValue(col.Id, out var v))
                col.SetValue(item, v);
        }

        this.editingItem = default;
        this.editingCell = null;
        await this.CommittedItemChanges.InvokeAsync(item);
        await this.ReloadServerDataAsync();
        this.StateHasChanged();
    }

    internal async Task CancelEditAsync()
    {
        var item = this.editingItem;
        this.editingItem = default;
        this.editingCell = null;
        this.editValues.Clear();
        if (item is not null)
            await this.CanceledEditingItem.InvokeAsync(item);
        this.StateHasChanged();
    }

    internal Task OnHeaderClickAsync(ColumnBase<TItem> col, bool sortable)
        => sortable ? this.ToggleSortAsync(col) : Task.CompletedTask;

    string RootCssClass
    {
        get
        {
            var sb = new System.Text.StringBuilder("shiny-dg");
            if (this.Dense) sb.Append(" shiny-dg-dense");
            if (this.Striped) sb.Append(" shiny-dg-striped");
            if (this.Bordered) sb.Append(" shiny-dg-bordered");
            if (this.Hover) sb.Append(" shiny-dg-hover");
            if (this.Outlined) sb.Append(" shiny-dg-outlined");
            if (this.FixedHeader) sb.Append(" shiny-dg-fixedheader");
            // Holds the col-resize cursor across the whole grid for the length of a drag and kills
            // the text selection a header drag would otherwise paint over every row it passes.
            if (this.resizingColumn is not null) sb.Append(" shiny-dg-resizing");
            // Sticky cells lose their borders under border-collapse:collapse, so the sticky
            // variants of the table only switch on when something is actually pinned.
            if (this.FixedHeader || this.HasFrozenColumns) sb.Append(" shiny-dg-sticky");
            if (!string.IsNullOrEmpty(this.Class)) sb.Append(' ').Append(this.Class);
            return sb.ToString();
        }
    }

    // The height caps the scroller (.shiny-dg-scroll), not the root: position:sticky only engages
    // against the ancestor that actually scrolls, and capping the root leaves the scroller unbounded.
    string? RootStyle
        => string.IsNullOrEmpty(this.Height) ? this.Style : $"--shiny-dg-height:{this.Height};{this.Style}";

    internal int ColSpan => this.VisibleColumns.Count + this.LeadColumnCount;

    /// <summary>
    /// A declared width has to carry a <c>min-width</c> with it. Under <c>table-layout: auto</c> the
    /// browser treats <c>width</c> on a cell as a *suggestion* and happily compresses every column to
    /// fit the container - so a grid asking for 1320px of columns inside an 810px scroller rendered at
    /// 810px, never overflowed, and its frozen columns had nothing to stay put against. Percentages are
    /// left alone: they are asking to be relative to the container, which is exactly what shrinking is.
    /// </summary>
    /// <remarks>
    /// An explicit <see cref="ColumnBase{TItem}.MinWidth"/> replaces that implied floor rather than
    /// stacking on top of it - a column saying "160 wide, may shrink to 80" is asking for exactly the
    /// compression the implied floor exists to prevent, and it gets to have it.
    /// <para>
    /// The grid-level <see cref="MinColumnWidth"/> / <see cref="MaxColumnWidth"/> are deliberately not
    /// emitted here. They bound a resize drag, not the layout: pinning a 48px floor onto every cell of
    /// every grid would quietly override the percentage widths that asked to be free to shrink.
    /// </para>
    /// </remarks>
    internal string? ColumnWidthStyle(ColumnBase<TItem> col)
    {
        string? width;
        if (this.columnWidths.TryGetValue(col.Id, out var resized))
        {
            // Already clamped when the drag produced it, and pinned on all three so neither the table
            // nor the column's own bounds can move it afterwards.
            width = $"width:{resized};min-width:{resized};max-width:{resized};";
        }
        else
        {
            var min = string.IsNullOrWhiteSpace(col.MinWidth)
                ? string.IsNullOrEmpty(col.Width) || col.Width.Contains('%', StringComparison.Ordinal)
                    ? null
                    : col.Width
                : col.MinWidth.Trim();

            var max = string.IsNullOrWhiteSpace(col.MaxWidth) ? null : col.MaxWidth.Trim();

            width = (string.IsNullOrEmpty(col.Width) ? null : $"width:{col.Width};")
                + (min is null ? null : $"min-width:{min};")
                + (max is null ? null : $"max-width:{max};");
        }

        // Concatenating two nulls gives "", which Blazor renders as a pointless style="" on every cell
        // of an unsized column; null is skipped entirely.
        var style = width + this.FrozenOffsetStyle(col);
        return string.IsNullOrEmpty(style) ? null : style;
    }

    string HeaderCssClass(ColumnBase<TItem> col, bool sortable)
        => "shiny-dg-header"
            + this.FrozenCssClass(col)
            + (sortable ? " shiny-dg-sortable" : null)
            + AlignCssClass(col.EffectiveHeaderAlignment)
            + this.DropCssClass(col);

    /// <summary>
    /// The full class list for one data cell. Built in a single call because the Razor generator
    /// mangles a <c>class</c> attribute stitched together from several consecutive <c>@(...)</c>
    /// expressions inside the row's templated delegate.
    /// </summary>
    internal string CellCssClass(ColumnBase<TItem> col, DataGridCellStyle? style)
        => "shiny-dg-cell"
            + this.FrozenCssClass(col)
            + AlignCssClass(col.EffectiveAlignment)
            + (col.Wrap ? " shiny-dg-wrap" : null)
            + (string.IsNullOrWhiteSpace(style?.CssClass) ? null : " " + style!.CssClass!.Trim());

    /// <summary>The full inline style for one data cell - width, line clamp, and the CellStyle delegate.</summary>
    internal string? CellInlineStyle(ColumnBase<TItem> col, DataGridCellStyle? style)
        => this.ColumnWidthStyle(col) + LineClampStyle(col) + CellStyleAttr(style);

    /// <summary>The full class list for one footer cell (grid footer and group footer alike).</summary>
    internal string FooterCellCssClass(ColumnBase<TItem> col)
        => "shiny-dg-footer-cell" + this.FrozenCssClass(col) + AlignCssClass(col.EffectiveAlignment);


    static string? AlignCssClass(DataGridCellAlignment alignment)
        => alignment switch
        {
            DataGridCellAlignment.Center => " shiny-dg-align-center",
            DataGridCellAlignment.End => " shiny-dg-align-end",
            _ => null
        };

    /// <summary>
    /// Line clamping is inline because the line count is per column. Only meaningful with
    /// <c>Wrap</c> - clamping a <c>nowrap</c> cell caps it at a line it already had.
    /// </summary>
    static string? LineClampStyle(ColumnBase<TItem> col)
        => col.Wrap && col.MaxLines > 0
            ? $"display:-webkit-box;-webkit-box-orient:vertical;-webkit-line-clamp:{col.MaxLines};overflow:hidden;"
            : null;

    static string? CellStyleAttr(DataGridCellStyle? style)
    {
        if (style is null)
            return null;

        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(style.TextColor))
            sb.Append("color:").Append(style.TextColor).Append(';');
        // background-color, not the `background` shorthand: the shorthand resets background-image,
        // which is the layer a highlight's fill is painted on.
        if (!string.IsNullOrWhiteSpace(style.BackgroundColor))
            sb.Append("background-color:").Append(style.BackgroundColor).Append(';');
        sb.Append(FillCss(style));
        sb.Append(BorderCss(style));
        if (style.Bold == true)
            sb.Append("font-weight:600;");
        if (!string.IsNullOrWhiteSpace(style.Style))
            sb.Append(style.Style!.TrimEnd().TrimEnd(';')).Append(';');

        return sb.Length == 0 ? null : sb.ToString();
    }

    string RowCssClass(TItem item)
    {
        var sb = new System.Text.StringBuilder("shiny-dg-row");
        if (this.IsSelected(item)) sb.Append(" shiny-dg-selected");
        if (this.SelectionMode != DataGridSelectionMode.None || this.RowClick.HasDelegate)
            sb.Append(" shiny-dg-clickable");
        return sb.ToString();
    }
}
