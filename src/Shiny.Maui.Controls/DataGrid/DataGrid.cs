using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.DataGrid;

/// <summary>
/// A pure cross-platform data grid: a header built from <see cref="Columns"/> over a virtualized
/// <see cref="CollectionView"/> of rows. Supports typed/template columns, selection (single/multi with
/// checkboxes), and density/striped/bordered styling. Sorting, filtering, grouping, editing, paging,
/// resize/reorder build on this foundation.
/// </summary>
[ContentProperty(nameof(Columns))]
public partial class DataGrid : ContentView
{
    const double CheckboxColumnWidth = 48;

    readonly Grid headerGrid;
    readonly Border headerWrapper;
    CollectionView collection;
    readonly Grid loadingOverlay;
    readonly List<DataGridRow> dataRows = new();
    readonly ObservableCollection<object> displayItems = new();
    readonly SelectionBackgroundConverter selectionConverter = new();
    readonly SelectionBackgroundConverter frozenBackgroundConverter = new() { Opaque = true };
    readonly Border footerWrapper;
    DataGridRow? editingRow;
    readonly Dictionary<string, object?> editValues = new();
    Border? editActionsBar;

    readonly List<DataGridSortDefinition> sortDefs = new();
    readonly List<DataGridFilterDefinition> filterDefs = new();
    readonly Grid pagerBar;
    readonly Grid toolbarBar;
    readonly Entry quickSearchEntry;
    readonly Grid filterRowGrid;
    readonly Border filterPopup;
    Label? pagerRangeLabel;
    Button? pagerFirst, pagerPrev, pagerNext, pagerLast;

    IDisposable? itemsSubscription;
    bool syncingSelection;
    int currentPage;
    string quickSearch = string.Empty;
    IList? serverItems;
    int serverTotal;
    string? rowStructure;

    public DataGrid()
    {
        this.Columns.CollectionChanged += this.OnColumnsCollectionChanged;
        this.SummaryRows.CollectionChanged += this.OnSummaryRowsChanged;
        this.InitGrouping();

        this.headerGrid = new Grid { ColumnSpacing = 0 };
        this.filterRowGrid = new Grid { ColumnSpacing = 0, IsVisible = false };

        var headerLine = new BoxView { HeightRequest = 1, VerticalOptions = LayoutOptions.End };
        headerLine.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.OutlineVariant);

        var headerStack = new VerticalStackLayout { Children = { this.headerGrid, this.filterRowGrid } };
        var headerContent = new Grid();
        headerContent.Add(headerStack);
        headerContent.Add(headerLine);

        this.headerWrapper = new Border
        {
            Content = headerContent,
            StrokeThickness = 0,
            Padding = 0
        };
        this.headerWrapper.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);

        this.collection = this.CreateCollectionView();

        this.pagerBar = this.BuildPager();
        (this.toolbarBar, this.quickSearchEntry) = this.BuildToolbar();
        this.filterPopup = this.BuildFilterPopup();

        this.footerStack = new VerticalStackLayout { Spacing = 0 };
        this.footerWrapper = new Border { Content = this.footerStack, StrokeThickness = 0, Padding = 0, IsVisible = false };
        this.footerWrapper.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);

        this.bodyGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };
        this.ApplyLayoutMode();

        this.loadingOverlay = this.BuildLoadingOverlay();

        this.editActionsBar = this.BuildEditActionsBar();

        var host = new Grid();
        host.Add(this.bodyGrid);
        host.Add(this.loadingOverlay);
        host.Add(this.filterPopup);
        host.Add(this.editActionsBar);
        this.Content = host;

        this.ApplyTheme();
        this.RebuildAll();

        this.Loaded += (_, _) =>
        {
            if (this.ServerData is not null)
                this.LoadServerDataAsync();
        };

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        this.ObserveDefaultHighlights();
        StyleGuard.MarkReady(this, typeof(DataGrid));
    }

    /// <summary>The column definitions. Add <see cref="DataGridColumn"/> / <see cref="DataGridTemplateColumn"/>.</summary>
    public ObservableCollection<DataGridColumn> Columns { get; } = new();

    IReadOnlyList<DataGridColumn> VisibleColumns
        => this.Columns.Where(c => c.IsVisible).ToList();

    /// <summary>The flattened list the CollectionView renders - rows, group headers and detail rows.</summary>
    internal IReadOnlyList<object> DisplayItems => this.displayItems;

    bool HasMultiSelect => this.SelectionMode == DataGridSelectionMode.Multiple;

    void OnColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        this.PushBindingContextToColumns();
        this.RebuildAll();
    }

    /// <summary>
    /// Columns are <see cref="BindableObject"/>s, not elements, so they are not in the visual tree and
    /// inherit nothing on their own - a <c>{Binding}</c> on a column would silently resolve against no
    /// context at all. Handing them the grid's context is what makes <c>TextFormatter</c>,
    /// <c>CellStyle</c> and the rest reachable from a view model instead of code-behind only.
    /// </summary>
    void PushBindingContextToColumns()
    {
        foreach (var column in this.Columns)
            SetInheritedBindingContext(column, this.BindingContext);

        this.PushBindingContextToSummaryRows();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        this.PushBindingContextToColumns();
    }

    // ---------- Items ----------
    void OnItemsSourceChanged()
    {
        // Weak: ItemsSource is usually a view model's collection, which outlives the page (memory-leak fix).
        this.itemsSubscription?.Dispose();
        this.itemsSubscription = WeakEventSubscription.CollectionChanged(this.ItemsSource, this.OnItemsCollectionChanged);

        this.autoWidthsDirty = true;
        this.RebuildRows();
    }

    void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        this.autoWidthsDirty = true;
        if (MainThread.IsMainThread)
            this.RebuildRows();
        else
            MainThread.BeginInvokeOnMainThread(this.RebuildRows);
    }

    void RebuildRows()
    {
        this.InvalidateImplicitSummaryRow();
        foreach (var row in this.dataRows)
            row.PropertyChanged -= this.OnRowPropertyChanged;
        this.dataRows.Clear();
        this.displayItems.Clear();

        this.selectionConverter.StripedEnabled = this.Striped;
        this.frozenBackgroundConverter.StripedEnabled = this.Striped;

        var index = 0;
        if (this.Grouped)
        {
            // Grouping owns the whole list: it pages nothing (a page of rows would slice groups in
            // half) and walks every level itself, rows included.
            this.AppendGroupRows(this.ProcessedData(), 0, string.Empty, ref index);
        }
        else if (this.TreeEnabled)
        {
            // Hierarchy replaces the flat pass entirely: the page is a page of *roots*, and each
            // expanded root brings its own (separately filtered and sorted) subtree with it.
            this.AppendTreeRows(this.GetPageData(), 0, index);
        }
        else
        {
            foreach (var item in this.GetPageData())
            {
                var row = this.CreateRow(item, 0, index++);
                this.dataRows.Add(row);
                this.displayItems.Add(row);
                this.AppendDetailRow(row);
            }
        }

        if (!this.Grouped)
            this.StampBlock(0);

        // Before the template and structure below read the widths. A change means the header and
        // filter row were built against the old ones - rebuild them so every row agrees.
        if (this.RefreshAutoWidths())
        {
            this.UpdateScrollContentWidth();
            this.RebuildHeader();
        }

        // Re-create the item template so density/striping changes take effect, and swap the whole
        // CollectionView out when the row *shape* changed - see EnsureRowStructure.
        this.collection.ItemTemplate = this.BuildItemTemplateSelector();
        this.EnsureRowStructure();
        this.RebuildFooter();
        this.UpdatePager();
    }

    /// <summary>
    /// Marks the ends of one run of rows, so a highlight that spans them knows where to stop stroking.
    /// A single-row block is both ends of itself, which is what boxes it in on all four sides.
    /// </summary>
    void StampBlock(int from)
    {
        if (from >= this.dataRows.Count)
            return;

        this.dataRows[from].IsFirstRow = true;
        this.dataRows[^1].IsLastRow = true;
    }

    CollectionView CreateCollectionView()
        => new()
        {
            ItemsSource = this.displayItems,
            SelectionMode = Microsoft.Maui.Controls.SelectionMode.None,
            ItemTemplate = this.BuildItemTemplateSelector()
        };

    /// <summary>
    /// Everything that changes the *shape* of a row view rather than the data in it - column set and
    /// widths, the checkbox column, the frozen runs, density.
    /// </summary>
    string RowStructure()
    {
        var sb = new System.Text.StringBuilder()
            .Append(this.HorizontalScroll).Append('|')
            .Append(this.frozenStart).Append('|')
            .Append(this.frozenEnd).Append('|')
            .Append(this.HasMultiSelect).Append('|')
            .Append(this.HasExpanderColumn).Append('|')
            .Append(this.TreeEnabled).Append('|')
            .Append(this.TreeIndentSize).Append('|')
            .Append(this.Dense).Append('|')
            .Append(this.Bordered).Append('|')
            .Append(this.RowHeight).Append('|')
            // A styled cell is wrapped in a highlight host; an unstyled one is the bare label. Turning
            // highlighting on has to drop the cells the CollectionView already built.
            .Append(this.HasCellStyling).Append('|');

        foreach (var column in this.VisibleColumns)
            sb.Append(column.Id).Append(':').Append(this.ResolveWidth(column)).Append(',');

        return sb.ToString();
    }

    /// <summary>
    /// Replaces the CollectionView when the row shape changed. Handing it a fresh ItemTemplate is not
    /// enough - it dequeues the cells it already built and only re-binds them, so a row keeps whatever
    /// column layout (and frozen panes) it was first created with. Only a new CollectionView reliably
    /// drops them, so this is gated on the structure actually changing rather than run every reload.
    /// </summary>
    void EnsureRowStructure()
    {
        var structure = this.RowStructure();
        if (structure == this.rowStructure)
            return;

        var first = this.rowStructure is null;
        this.rowStructure = structure;
        if (first)
            return;

        if (this.collection.Parent is not Grid host)
            return;

        var old = this.collection;
        var row = Grid.GetRow(old);
        this.collection = this.CreateCollectionView();
        host.Children.Remove(old);
        host.Add(this.collection, 0, row);
    }

    DataTemplateSelector BuildItemTemplateSelector()
        => new DataGridItemTemplateSelector
        {
            RowTemplate = new DataTemplate(this.BuildRowView),
            GroupTemplate = new DataTemplate(this.BuildGroupHeaderView),
            SummaryTemplates = this.BuildSummaryTemplates(),
            BlankTemplate = new DataTemplate(() => new Grid()),
            EditRowTemplate = new DataTemplate(this.BuildEditRowView),
            DetailTemplate = new DataTemplate(this.BuildDetailRowView),
            DetailLoadingTemplate = new DataTemplate(this.BuildDetailLoadingRowView)
        };

    // ---------- Inline editing ----------
    bool EditingEnabled => this.EditMode != DataGridEditMode.None && !this.ReadOnly;

    bool EffectiveEditable(DataGridColumn col) => this.EditingEnabled && col.Editable && col.HasValue;

    void StartEdit(DataGridRow row)
    {
        this.editingRow = row;
        this.editValues.Clear();
        foreach (var col in this.Columns.Where(c => c.HasValue))
            this.editValues[col.Id] = col.GetCellValue(row.Data);

        row.IsEditing = true;
        this.RefreshRow(row);
        if (this.editActionsBar is not null)
            this.editActionsBar.IsVisible = true;
        this.StartedEditingItem?.Invoke(this, row.Data);
    }

    void CommitEdit()
    {
        if (this.editingRow is null)
            return;
        var row = this.editingRow;

        foreach (var col in this.Columns.Where(this.EffectiveEditable))
        {
            if (this.editValues.TryGetValue(col.Id, out var v))
                col.SetCellValue(row.Data, v);
        }

        row.IsEditing = false;
        this.editingRow = null;
        if (this.editActionsBar is not null)
            this.editActionsBar.IsVisible = false;
        this.CommittedItemChanges?.Invoke(this, row.Data);
        this.Reload();
    }

    void CancelEdit()
    {
        if (this.editingRow is null)
            return;
        var row = this.editingRow;
        row.IsEditing = false;
        this.editingRow = null;
        this.editValues.Clear();
        if (this.editActionsBar is not null)
            this.editActionsBar.IsVisible = false;
        this.RefreshRow(row);
        this.CanceledEditingItem?.Invoke(this, row.Data);
    }

    void RefreshRow(DataGridRow row)
    {
        var idx = this.displayItems.IndexOf(row);
        if (idx < 0)
            return;
        this.displayItems.RemoveAt(idx);
        this.displayItems.Insert(idx, row);
    }

    View BuildEditRowView()
    {
        var grid = new Grid
        {
            ColumnDefinitions = this.BuildColumnDefinitions(),
            ColumnSpacing = 0
        };
        grid.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);
        if (this.RowHeight > 0)
            grid.HeightRequest = this.RowHeight;

        this.LayoutCells(
            grid,
            this.LeadingPlaceholders(),
            this.BuildEditCellView,
            this.StyleContainerPane
        );
        return grid;
    }

    View BuildEditCellView(DataGridColumn column)
    {
        if (!this.EffectiveEditable(column))
            return this.BuildCellView(column);

        if (column.EditTemplate is not null)
        {
            var content = (View)column.EditTemplate.CreateContent();
            content.SetBinding(BindableObject.BindingContextProperty, new Binding(nameof(DataGridRow.Data)));
            return content;
        }

        var capture = column;
        var entry = new Entry
        {
            Text = this.editValues.TryGetValue(column.Id, out var v) ? v?.ToString() : null,
            Margin = new Thickness(8, 2),
            VerticalOptions = LayoutOptions.Center
        };
        entry.TextChanged += (_, e) => this.editValues[capture.Id] = e.NewTextValue;
        return entry;
    }

    void Reload()
    {
        if (this.ServerData is not null)
            this.LoadServerDataAsync();
        else
            this.RebuildRows();
    }

    void OnPagingChanged()
    {
        this.currentPage = 0;
        this.Reload();
    }

    // ---------- Pipeline ----------
    bool Paging => this.PageSize > 0;

    int TotalItems => this.serverItems is not null ? this.serverTotal : this.ProcessedData().Count;

    int TotalPages => this.Paging
        ? Math.Max(1, (int)Math.Ceiling(this.TotalItems / (double)Math.Max(1, this.PageSize)))
        : 1;

    /// <summary>
    /// The rows the grid is working from: in a flat grid every item, in tree mode the *roots*. Both
    /// go through <see cref="ProcessLevel"/>, which is also what each expanded node's children run
    /// through - so a tree is filtered and sorted one level at a time.
    /// </summary>
    IReadOnlyList<object> ProcessedData()
    {
        if (this.serverItems is not null)
            return this.serverItems.Cast<object>().ToList();

        return this.ProcessLevel(this.ItemsSource?.Cast<object>() ?? Enumerable.Empty<object>());
    }

    /// <summary>Filters and sorts one level of items - the roots, or one node's children.</summary>
    IReadOnlyList<object> ProcessLevel(IEnumerable<object> items)
        => this.ApplySort(this.HasActiveFilters ? items.Where(this.KeepInResults) : items);

    bool HasActiveFilters => this.filterDefs.Count > 0 || !string.IsNullOrEmpty(this.quickSearch);

    /// <summary>
    /// A node survives filtering if it matches, or if anything beneath it does - dropping a parent
    /// whose child matched would hide the match along with it.
    /// </summary>
    bool KeepInResults(object item)
        => this.MatchesFilters(item) || (this.TreeEnabled && this.AnyDescendantMatches(item));

    bool AnyDescendantMatches(object item)
    {
        // Children that have not been fetched yet cannot be searched; keep the branch so the user
        // can still open it rather than silently pruning a subtree that may well match.
        if (this.NeedsChildrenLoad(item))
            return true;

        foreach (var child in this.RawChildren(item))
        {
            if (this.KeepInResults(child))
                return true;
        }
        return false;
    }

    bool MatchesFilters(object item)
    {
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
            var col = this.Columns.FirstOrDefault(c => c.Id == def.ColumnId);
            if (col is null)
                continue;
            if (!DataGridFilterEvaluator.Matches(col.GetCellValue(item), def.Operator, def.Value))
                return false;
        }
        return true;
    }

    IReadOnlyList<object> ApplySort(IEnumerable<object> source)
    {
        if (this.sortDefs.Count == 0)
            return source as IReadOnlyList<object> ?? source.ToList();

        IOrderedEnumerable<object>? ordered = null;
        foreach (var def in this.sortDefs.OrderBy(d => d.Order))
        {
            var col = this.Columns.FirstOrDefault(c => c.Id == def.ColumnId);
            if (col is null)
                continue;

            var comparer = col.Comparer ?? DataGridValueComparer.Instance;
            Func<object, object?> key = col.GetCellValue;
            var asc = def.Direction == DataGridSortDirection.Ascending;

            ordered = ordered is null
                ? (asc ? source.OrderBy(key, comparer) : source.OrderByDescending(key, comparer))
                : (asc ? ordered.ThenBy(key, comparer) : ordered.ThenByDescending(key, comparer));
        }
        return ((IEnumerable<object>?)ordered ?? source).ToList();
    }

    IReadOnlyList<object> GetPageData()
    {
        var processed = this.ProcessedData();
        if (!this.Paging || this.serverItems is not null)
            return processed;

        if (this.currentPage >= this.TotalPages)
            this.currentPage = this.TotalPages - 1;
        if (this.currentPage < 0)
            this.currentPage = 0;

        return processed.Skip(this.currentPage * this.PageSize).Take(this.PageSize).ToList();
    }

    async void LoadServerDataAsync()
    {
        if (this.ServerData is null)
            return;

        var state = new DataGridGridState
        {
            Page = this.currentPage,
            PageSize = this.PageSize,
            SortDefinitions = this.sortDefs.ToList(),
            FilterDefinitions = this.filterDefs.ToList()
        };
        var data = await this.ServerData(state);

        // The first page is the first chance an Auto column has to sample server rows.
        if (this.serverItems is null)
            this.autoWidthsDirty = true;
        this.serverItems = data.Items;
        this.serverTotal = data.TotalItems;
        this.RebuildRows();
    }

    // ---------- Sorting ----------
    bool EffectiveSortable(DataGridColumn col)
        => col.Sortable && this.SortMode != DataGridSortMode.None && col.HasValue;

    void OnHeaderTapped(DataGridColumn col)
    {
        if (!this.EffectiveSortable(col))
            return;

        var existing = this.sortDefs.FirstOrDefault(d => d.ColumnId == col.Id);
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
            this.sortDefs.Add(new DataGridSortDefinition(col.Id, next, this.sortDefs.Count));

        this.RebuildHeader();
        this.Reload();
    }

    string SortGlyph(DataGridColumn col)
    {
        var def = this.sortDefs.FirstOrDefault(d => d.ColumnId == col.Id);
        if (def is null)
            return string.Empty;
        var arrow = def.Direction == DataGridSortDirection.Ascending ? "▲" : "▼";
        if (this.SortMode == DataGridSortMode.Multiple && this.sortDefs.Count > 1)
            arrow += (def.Order + 1).ToString();
        return arrow;
    }

    // ---------- Build header + rows ----------
    void RebuildAll()
    {
        this.RefreshFrozenCounts();

        // Anything that rebuilds everything (columns, density, header affordances) can change what an
        // Auto column has to fit.
        this.autoWidthsDirty = true;
        this.RefreshAutoWidths();
        this.UpdateScrollContentWidth();
        this.RebuildHeader();
        this.RebuildRows();
    }

    void RebuildHeader()
    {
        this.ClearColumnDragCells();
        this.headerGrid.Children.Clear();
        this.headerGrid.ColumnDefinitions = this.BuildColumnDefinitions();
        this.headerWrapper.IsVisible = this.ShowColumnHeaders;
        if (!this.ShowColumnHeaders)
            return;

        var leading = new List<View>();
        if (this.HasExpanderColumn)
            leading.Add(new Grid());

        if (this.HasMultiSelect)
        {
            var check = new CheckBox { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
            check.CheckedChanged += (_, e) => this.ToggleSelectAll(e.Value);
            leading.Add(check);
        }

        this.LayoutCells(this.headerGrid, leading, this.BuildHeaderCell, this.StyleSurfacePane);

        this.BuildFilterRow();
        this.toolbarBar.IsVisible = this.FilterMode == DataGridFilterMode.Toolbar;
    }

    View BuildHeaderCell(DataGridColumn column)
    {
        {
            var capture = column;
            View headerView;
            if (column.HeaderTemplate is not null)
            {
                headerView = (View)column.HeaderTemplate.CreateContent();
                if (this.EffectiveSortable(column))
                {
                    var t = new TapGestureRecognizer();
                    t.Tapped += (_, _) => this.OnHeaderTapped(capture);
                    headerView.GestureRecognizers.Add(t);
                }
            }
            else
            {
                var title = new Label
                {
                    Text = column.Title,
                    FontAttributes = FontAttributes.Bold,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Fill,
                    HorizontalTextAlignment = this.ResolveHeaderAlignment(column),
                    // Narrow columns are the norm on phones; a header that refuses to shrink spills
                    // over the next one instead of ellipsizing like the cells below it do.
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 1
                };
                title.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);

                var sortGlyph = new Label
                {
                    Text = this.SortGlyph(column),
                    VerticalOptions = LayoutOptions.Center
                }.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);
                sortGlyph.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.Primary);

                // Grid rather than a stack so the title actually gets squeezed (and ellipsized)
                // instead of the whole part overflowing its column.
                var sortablePart = new Grid
                {
                    ColumnSpacing = 4,
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto)
                    }
                };
                sortablePart.Add(title, 0, 0);
                sortablePart.Add(sortGlyph, 1, 0);
                if (this.EffectiveSortable(column))
                {
                    var t = new TapGestureRecognizer();
                    t.Tapped += (_, _) => this.OnHeaderTapped(capture);
                    sortablePart.GestureRecognizers.Add(t);
                }

                // The sort/filter/group/reorder affordances go in their own strip so the header can
                // be laid out with the title taking priority. A HorizontalStackLayout here is fine:
                // the strip lives in a star column that clips, so it shrinks by losing glyphs off
                // the end rather than by pushing the title out of the way.
                var glyphs = new HorizontalStackLayout
                {
                    Spacing = 6,
                    HorizontalOptions = LayoutOptions.End,
                    VerticalOptions = LayoutOptions.Center
                };

                void AddGlyph(View glyph) => glyphs.Children.Add(glyph);

                if (this.FilterMode == DataGridFilterMode.Menu && this.EffectiveFilterable(column))
                {
                    var filterGlyph = new Label
                    {
                        Text = "▾",
                        VerticalOptions = LayoutOptions.Center,
                        Opacity = this.HasActiveFilter(column) ? 1 : 0.5
                    }.WithFontSize(ShinyThemeKeys.Type.BodySmallSize);
                    filterGlyph.SetDynamicResource(Label.TextColorProperty,
                        this.HasActiveFilter(column) ? ShinyThemeKeys.Color.Primary : ShinyThemeKeys.Color.OnSurfaceVariant);
                    var ft = new TapGestureRecognizer();
                    ft.Tapped += (_, _) => this.OpenFilterMenu(capture);
                    filterGlyph.GestureRecognizers.Add(ft);
                    AddGlyph(filterGlyph);
                }

                if (this.EffectiveGroupable(column))
                {
                    var level = this.GroupLevelOf(column);
                    var grouped = level >= 0;
                    var groupGlyph = new Label
                    {
                        // Multi-level grouping numbers the glyph so the header says which level a
                        // column is, not merely that it is one of several.
                        Text = grouped && this.GroupColumns.Count > 1 ? $"⊞{level + 1}" : "⊞",
                        VerticalOptions = LayoutOptions.Center,
                        Opacity = grouped ? 1 : 0.5
                    }.WithFontSize(ShinyThemeKeys.Type.BodySmallSize);
                    groupGlyph.SetDynamicResource(Label.TextColorProperty,
                        grouped ? ShinyThemeKeys.Color.Primary : ShinyThemeKeys.Color.OnSurfaceVariant);
                    var gt = new TapGestureRecognizer();
                    gt.Tapped += (_, _) => this.ToggleGroupBy(capture);
                    groupGlyph.GestureRecognizers.Add(gt);
                    AddGlyph(groupGlyph);
                }

                if (this.AllowColumnReorder)
                {
                    AddGlyph(this.ReorderArrow("‹", capture, -1));
                    AddGlyph(this.ReorderArrow("›", capture, +1));
                }

                // The glyphs take only the width they measure and the title gets the rest, with the
                // title guaranteed most of a column too narrow for both. A fixed 3*/2* split used to
                // reserve 40% for a lone ▾ and ellipsize narrow headers down to a bare "…".
                headerView = new DataGridHeaderCellLayout(sortablePart, glyphs.Children.Count > 0 ? glyphs : null)
                {
                    Spacing = 6,
                    Padding = this.CellPadding
                };
            }

            var resizable = this.AllowColumnResize && column.Resizable;
            if (resizable || this.DragDropColumnReordering)
            {
                var container = new Grid();
                container.Add(headerView);

                // The drag gesture goes on the container, the resize handle is a child of it - so the
                // handle wins the touch on the few pixels the two overlap, which is what a user
                // reaching for the column edge means.
                if (this.DragDropColumnReordering)
                    this.AttachColumnDrag(capture, container);

                if (resizable)
                    container.Add(this.ResizeHandle(capture, container));

                headerView = container;
            }

            return headerView;
        }
    }

    void BuildFilterRow()
    {
        this.filterRowGrid.Children.Clear();
        this.filterRowGrid.IsVisible = this.FilterMode == DataGridFilterMode.Row;
        if (this.FilterMode != DataGridFilterMode.Row)
            return;

        this.filterRowGrid.ColumnDefinitions = this.BuildColumnDefinitions();
        this.LayoutCells(
            this.filterRowGrid,
            this.LeadingPlaceholders(),
            this.BuildFilterCell,
            this.StyleSurfacePane
        );
    }

    View BuildFilterCell(DataGridColumn column)
    {
        if (!this.EffectiveFilterable(column))
            return new Grid();

        var capture = column;
        var entry = new Entry
        {
            Placeholder = "Filter",
            FontSize = 13,
            Margin = new Thickness(8, 2),
            Text = this.filterDefs.FirstOrDefault(d => d.ColumnId == column.Id)?.Value?.ToString()
        };
        entry.TextChanged += (_, e) => this.ApplyColumnFilter(
            capture,
            this.DefaultOperator(capture.GetDataType(this.ItemTypeOrString())),
            string.IsNullOrEmpty(e.NewTextValue) ? null : e.NewTextValue);
        return entry;
    }

    // ---------- Reorder / resize ----------
    Label ReorderArrow(string glyph, DataGridColumn col, int direction)
    {
        var arrow = new Label { Text = glyph, Opacity = 0.6, VerticalOptions = LayoutOptions.Center }.WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);
        arrow.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => this.MoveColumn(col, direction);
        arrow.GestureRecognizers.Add(tap);
        return arrow;
    }

    void MoveColumn(DataGridColumn col, int direction)
    {
        var idx = this.Columns.IndexOf(col);
        var target = idx + direction;
        if (idx < 0 || target < 0 || target >= this.Columns.Count)
            return;
        this.Columns.Move(idx, target);
    }

    View ResizeHandle(DataGridColumn col, VisualElement headerCell)
    {
        // Invisible hit target only - a Grid, not a BoxView, so a host app's implicit
        // Style TargetType="BoxView" cannot paint it (see the parallax/skeleton fixes).
        var handle = new Grid
        {
            WidthRequest = 8,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Fill
        };

        var startWidth = 0d;
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    // The measured header is the truth once laid out; before that fall back to what
                    // the column asks for, so the first drag nudges the column instead of teleporting
                    // it to some unrelated default.
                    startWidth = headerCell.Width > 0 ? headerCell.Width : this.ResolveWidth(col).Value;
                    if (startWidth <= 0)
                        startWidth = this.DefaultColumnWidth;
                    break;
                case GestureStatus.Running:
                    var width = this.ClampColumnWidth(col, startWidth + e.TotalX);
                    if (Math.Abs(width - col.Width.Value) < 0.5 && col.Width.IsAbsolute)
                        break;
                    col.Width = new GridLength(width);
                    this.RebuildHeader();
                    break;
                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    this.RebuildRows();
                    break;
            }
        };
        handle.GestureRecognizers.Add(pan);
        return handle;
    }

    // ---------- Filtering ----------
    bool EffectiveFilterable(DataGridColumn col) => col.Filterable && col.HasValue;

    bool HasActiveFilter(DataGridColumn col)
        => this.filterDefs.Any(d => d.ColumnId == col.Id &&
            (d.Value is not null || d.Operator is DataGridFilterOperator.Empty or DataGridFilterOperator.NotEmpty));

    Type ItemTypeOrString()
        => this.ItemsSource?.Cast<object>().FirstOrDefault()?.GetType() ?? typeof(string);

    internal void SetQuickSearch(string? text)
    {
        this.quickSearch = text ?? string.Empty;
        this.currentPage = 0;
        this.Reload();
    }

    void ApplyColumnFilter(DataGridColumn col, DataGridFilterOperator op, object? value)
    {
        var def = this.filterDefs.FirstOrDefault(d => d.ColumnId == col.Id);
        if (def is null)
        {
            def = new DataGridFilterDefinition { ColumnId = col.Id };
            this.filterDefs.Add(def);
        }
        def.Operator = op;
        def.Value = value;
        this.currentPage = 0;
        this.Reload();
    }

    void ClearColumnFilter(DataGridColumn col)
    {
        this.filterDefs.RemoveAll(d => d.ColumnId == col.Id);
        this.currentPage = 0;
        this.Reload();
    }

    void OpenFilterMenu(DataGridColumn col)
    {
        var type = col.GetDataType(this.ItemTypeOrString());
        var ops = this.OperatorsFor(type).ToList();
        var existing = this.filterDefs.FirstOrDefault(d => d.ColumnId == col.Id);

        var picker = new Picker { Title = "Operator" };
        foreach (var op in ops)
            picker.Items.Add(this.OperatorLabel(op));
        picker.SelectedIndex = existing is null ? 0 : Math.Max(0, ops.IndexOf(existing.Operator));

        var valueEntry = new Entry { Placeholder = "Value", Text = existing?.Value?.ToString() };

        var clear = new Button { Text = "Clear", FontSize = 13 }.Neutralize();
        clear.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        clear.BackgroundColor = Colors.Transparent;
        clear.Clicked += (_, _) =>
        {
            this.ClearColumnFilter(col);
            this.filterPopup.IsVisible = false;
            this.RebuildHeader();
        };

        var apply = new Button { Text = "Apply", FontSize = 13 }.Neutralize();
        apply.SetDynamicResource(Button.BackgroundColorProperty, ShinyThemeKeys.Color.Primary);
        apply.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);
        apply.Clicked += (_, _) =>
        {
            var op = ops[Math.Max(0, picker.SelectedIndex)];
            this.ApplyColumnFilter(col, op, string.IsNullOrEmpty(valueEntry.Text) ? null : valueEntry.Text);
            this.filterPopup.IsVisible = false;
            this.RebuildHeader();
        };

        var title = new Label { Text = col.Title, FontAttributes = FontAttributes.Bold };
        title.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);

        this.filterPopup.Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                title,
                picker,
                valueEntry,
                new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End, Children = { clear, apply } }
            }
        };
        this.filterPopup.IsVisible = true;
    }

    IReadOnlyList<DataGridFilterOperator> OperatorsFor(Type type)
    {
        if (type == typeof(string))
            return new[] { DataGridFilterOperator.Contains, DataGridFilterOperator.NotContains, DataGridFilterOperator.Equals, DataGridFilterOperator.NotEquals, DataGridFilterOperator.StartsWith, DataGridFilterOperator.EndsWith, DataGridFilterOperator.Empty, DataGridFilterOperator.NotEmpty };
        if (type == typeof(bool))
            return new[] { DataGridFilterOperator.Is };
        if (type.IsEnum)
            return new[] { DataGridFilterOperator.Is, DataGridFilterOperator.IsNot };
        return new[] { DataGridFilterOperator.Equals, DataGridFilterOperator.NotEquals, DataGridFilterOperator.GreaterThan, DataGridFilterOperator.GreaterThanOrEqual, DataGridFilterOperator.LessThan, DataGridFilterOperator.LessThanOrEqual };
    }

    DataGridFilterOperator DefaultOperator(Type type)
        => type == typeof(string) ? DataGridFilterOperator.Contains
            : type == typeof(bool) || type.IsEnum ? DataGridFilterOperator.Is
            : DataGridFilterOperator.Equals;

    string OperatorLabel(DataGridFilterOperator op) => op switch
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

    (Grid Bar, Entry Search) BuildToolbar()
    {
        var entry = new Entry { Placeholder = "Search…" };
        entry.TextChanged += (_, e) => this.SetQuickSearch(e.NewTextValue);
        var bar = new Grid { IsVisible = false, Padding = new Thickness(8, 6) };
        bar.Add(entry);
        return (bar, entry);
    }

    Border BuildFilterPopup()
    {
        var popup = new Border
        {
            IsVisible = false,
            Padding = 12,
            StrokeThickness = 0,
            WidthRequest = 250,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 8, 8, 0),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerMediumRadius)
        };
        popup.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);
        popup.SetDynamicResource(VisualElement.ShadowProperty, ShinyThemeKeys.Elevation.Level4);
        return popup;
    }

    ColumnDefinitionCollection BuildColumnDefinitions()
    {
        var defs = new ColumnDefinitionCollection();
        if (this.HasExpanderColumn)
            defs.Add(new ColumnDefinition(new GridLength(ExpanderColumnWidth)));
        if (this.HasMultiSelect)
            defs.Add(new ColumnDefinition(new GridLength(CheckboxColumnWidth)));

        foreach (var column in this.VisibleColumns)
            defs.Add(new ColumnDefinition(this.ResolveWidth(column)));

        return defs;
    }

    Thickness CellPadding => this.Dense ? new Thickness(10, 6) : new Thickness(16, 12);

    View BuildRowView()
    {
        var grid = new Grid
        {
            ColumnDefinitions = this.BuildColumnDefinitions(),
            ColumnSpacing = 0
        };
        if (this.RowHeight > 0)
            grid.HeightRequest = this.RowHeight;

        // Selection / stripe background reacts to the row's IsSelected + index parity.
        grid.SetBinding(VisualElement.BackgroundColorProperty, new MultiBinding
        {
            Converter = this.selectionConverter,
            Bindings =
            {
                new Binding(nameof(DataGridRow.IsSelected)),
                new Binding(nameof(DataGridRow.Index)),
                // Unused by the converter: raised on a theme change so the background re-resolves.
                new Binding(nameof(DataGridRow.BackgroundRevision))
            }
        });

        var bottomLine = new BoxView { HeightRequest = 1, VerticalOptions = LayoutOptions.End };
        bottomLine.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.OutlineVariant);

        var leading = new List<View>();
        if (this.HasExpanderColumn)
            leading.Add(this.BuildExpanderCell());

        if (this.HasMultiSelect)
        {
            var box = new CheckBox { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
            box.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(DataGridRow.IsSelected), BindingMode.TwoWay));
            leading.Add(box);
        }

        this.LayoutCells(grid, leading, this.BuildCellView, pane =>
        {
            this.StyleRowPane(pane);
            if (this.Bordered)
                this.AddVerticalBorders(pane);
        });

        // Wrap so we can overlay the separator line and capture taps without disturbing the cells.
        var container = new Grid();
        container.Add(grid);
        if (this.Bordered)
            this.AddVerticalBorders(grid);
        container.Add(bottomLine);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, _) =>
        {
            if (((View)s!).BindingContext is DataGridRow row)
                this.OnRowTapped(row);
        };
        container.GestureRecognizers.Add(tap);

        return container;
    }

    void AddVerticalBorders(Grid rowGrid)
    {
        // Right border per cell for the "bordered" look.
        for (var i = 0; i < rowGrid.ColumnDefinitions.Count; i++)
        {
            var line = new BoxView { WidthRequest = 1, HorizontalOptions = LayoutOptions.End };
            line.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.OutlineVariant);
            rowGrid.Add(line, i, 0);
        }
    }

    /// <summary>The column the tree caret and indent live in - the first visible one.</summary>
    bool IsTreeColumn(DataGridColumn column)
        => this.TreeEnabled && ReferenceEquals(this.VisibleColumns.FirstOrDefault(), column);

    View BuildCellView(DataGridColumn column)
    {
        var content = this.BuildPlainCellView(column, out var label);
        if (this.IsTreeColumn(column))
            content = this.WrapTreeCell(content);

        return this.HasCellStyling ? this.WrapHighlight(content, label, column) : content;
    }

    /// <summary>
    /// The cell's content. <paramref name="label"/> comes back set for the default text cell and null
    /// for a templated one - the styling hook needs the label to colour its text, and a template owns
    /// its own.
    /// </summary>
    View BuildPlainCellView(DataGridColumn column, out Label? label)
    {
        label = null;
        if (column.CellTemplate is not null)
        {
            var content = (View)column.CellTemplate.CreateContent();
            // The row's BindingContext is the DataGridRow wrapper; point the cell at the data item.
            content.SetBinding(BindableObject.BindingContextProperty, new Binding(nameof(DataGridRow.Data)));
            return content;
        }

        // Fill rather than Center so the cell's own alignment and background cover the whole cell;
        // VerticalTextAlignment keeps the text centred exactly as LayoutOptions.Center used to.
        var cell = new Label
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = this.ResolveAlignment(column, column.Alignment),
            LineBreakMode = column.Wrap ? LineBreakMode.WordWrap : LineBreakMode.TailTruncation,
            Padding = this.CellPadding
        };
        if (column.MaxLines > 0)
            cell.MaxLines = column.MaxLines;

        cell.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        if (!string.IsNullOrEmpty(column.PropertyName))
        {
            // Converter rather than the binding's own StringFormat: presets, prefix/suffix, the null
            // placeholder and a TextFormatter delegate all live in Column.FormatValue, and routing the
            // cell through it is what keeps the cell text identical to what search and export see.
            cell.SetBinding(Label.TextProperty, new Binding(
                $"{nameof(DataGridRow.Data)}.{column.PropertyName}",
                converter: new DataGridCellTextConverter(column)));
        }

        label = cell;
        return cell;
    }

    /// <summary>
    /// Resolves <see cref="DataGridCellAlignment.Auto"/> against the column's preset and CLR type -
    /// quantities right, everything else left.
    /// </summary>
    internal TextAlignment ResolveAlignment(DataGridColumn column, DataGridCellAlignment alignment)
    {
        if (alignment == DataGridCellAlignment.Auto)
        {
            if (!column.HasValue)
                return TextAlignment.Start;

            return DataGridValueFormatter.IsNumericAlignment(column.DisplayAs, column.GetDataType(this.ItemTypeOrString()))
                ? TextAlignment.End
                : TextAlignment.Start;
        }
        return alignment switch
        {
            DataGridCellAlignment.Center => TextAlignment.Center,
            DataGridCellAlignment.End => TextAlignment.End,
            _ => TextAlignment.Start
        };
    }

    /// <summary>Header alignment - <c>Auto</c> follows the cells so a header sits over its own values.</summary>
    TextAlignment ResolveHeaderAlignment(DataGridColumn column)
        => column.HeaderAlignment == DataGridCellAlignment.Auto
            ? this.ResolveAlignment(column, column.Alignment)
            : this.ResolveAlignment(column, column.HeaderAlignment);

    // ---------- Selection ----------
    void OnRowTapped(DataGridRow row)
    {
        if (this.ExpandOnRowTap && this.ExpansionEnabled)
        {
            if (row.HasDetail || row.HasChildren)
                this.ToggleRow(row.Data);
        }

        if (this.EditingEnabled && this.EditTrigger == DataGridEditTrigger.OnRowClick)
        {
            if (!ReferenceEquals(this.editingRow, row))
                this.StartEdit(row);
            return;
        }

        switch (this.SelectionMode)
        {
            case DataGridSelectionMode.Single:
                foreach (var r in this.dataRows)
                    r.IsSelected = ReferenceEquals(r, row);
                break;

            case DataGridSelectionMode.Multiple:
                row.IsSelected = !row.IsSelected;
                break;
        }
    }

    void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DataGridRow.IsSelected) && !this.syncingSelection)
            this.SyncSelectionFromRows();
    }

    void SyncSelectionFromRows()
    {
        this.syncingSelection = true;
        try
        {
            var selectedData = this.dataRows.Where(r => r.IsSelected).Select(r => r.Data).ToList();

            if (this.SelectedItems is { } list)
            {
                list.Clear();
                foreach (var item in selectedData)
                    list.Add(item);
            }

            this.SelectedItem = selectedData.Count > 0 ? selectedData[0] : null;

            this.SelectionChanged?.Invoke(this, new DataGridSelectionChangedEventArgs(selectedData));
            if (this.SelectionChangedCommand?.CanExecute(selectedData) == true)
                this.SelectionChangedCommand.Execute(selectedData);
        }
        finally
        {
            this.syncingSelection = false;
        }
    }

    void OnSelectedItemChanged(object? newValue)
    {
        if (this.syncingSelection || this.SelectionMode != DataGridSelectionMode.Single)
            return;

        this.syncingSelection = true;
        try
        {
            foreach (var row in this.dataRows)
                row.IsSelected = ReferenceEquals(row.Data, newValue);
        }
        finally
        {
            this.syncingSelection = false;
        }
    }

    void ToggleSelectAll(bool select)
    {
        foreach (var row in this.dataRows)
            row.IsSelected = select;
    }

    // ---------- Pager ----------
    Grid BuildPager()
    {
        this.pagerRangeLabel = new Label { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.End };
        this.pagerRangeLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        this.pagerFirst = this.PagerButton("«", () => this.GoToPage(0));
        this.pagerPrev = this.PagerButton("‹", () => this.GoToPage(this.currentPage - 1));
        this.pagerNext = this.PagerButton("›", () => this.GoToPage(this.currentPage + 1));
        this.pagerLast = this.PagerButton("»", () => this.GoToPage(this.TotalPages - 1));

        var bar = new Grid
        {
            IsVisible = false,
            Padding = new Thickness(8, 4),
            ColumnSpacing = 2,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        bar.Add(this.pagerRangeLabel, 0, 0);
        bar.Add(this.pagerFirst, 1, 0);
        bar.Add(this.pagerPrev, 2, 0);
        bar.Add(this.pagerNext, 3, 0);
        bar.Add(this.pagerLast, 4, 0);
        return bar;
    }

    Button PagerButton(string text, Action action)
    {
        // Neutralize, or the app's implicit Button style repaints the disabled ones: the MAUI
        // template's Disabled setter fills them with Gray600 in dark mode, so the buttons you
        // cannot press are the only ones with a background.
        var button = new Button
        {
            Text = text,
            WidthRequest = 40,
            HeightRequest = 40,
            Padding = 0,
            BackgroundColor = Colors.Transparent
        }.Neutralize().WithFontSize(ShinyThemeKeys.Type.BodyLargeSize);
        button.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
        button.Clicked += (_, _) => action();
        return button;
    }

    void GoToPage(int page)
    {
        this.currentPage = Math.Clamp(page, 0, this.TotalPages - 1);
        this.Reload();
    }

    void UpdatePager()
    {
        this.pagerBar.IsVisible = this.Paging && !this.Grouped;
        if (!this.pagerBar.IsVisible || this.pagerRangeLabel is null)
            return;

        var total = this.TotalItems;
        if (total == 0)
        {
            this.pagerRangeLabel.Text = "0";
        }
        else
        {
            var start = this.currentPage * this.PageSize + 1;
            var end = Math.Min(start + this.PageSize - 1, total);
            this.pagerRangeLabel.Text = $"{start}-{end} of {total}";
        }

        var atStart = this.currentPage <= 0;
        var atEnd = this.currentPage >= this.TotalPages - 1;
        if (this.pagerFirst is not null) this.pagerFirst.IsEnabled = !atStart;
        if (this.pagerPrev is not null) this.pagerPrev.IsEnabled = !atStart;
        if (this.pagerNext is not null) this.pagerNext.IsEnabled = !atEnd;
        if (this.pagerLast is not null) this.pagerLast.IsEnabled = !atEnd;
    }

    Border BuildEditActionsBar()
    {
        var cancel = new Button { Text = "Cancel", FontSize = 13, BackgroundColor = Colors.Transparent }.Neutralize();
        cancel.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        cancel.Clicked += (_, _) => this.CancelEdit();

        var save = new Button { Text = "Save", FontSize = 13 }.Neutralize();
        save.SetDynamicResource(Button.BackgroundColorProperty, ShinyThemeKeys.Color.Primary);
        save.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);
        save.Clicked += (_, _) => this.CommitEdit();

        var bar = new Border
        {
            IsVisible = false,
            Padding = 8,
            StrokeThickness = 0,
            VerticalOptions = LayoutOptions.End,
            Content = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End, Children = { cancel, save } }
        };
        bar.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);
        bar.SetDynamicResource(VisualElement.ShadowProperty, ShinyThemeKeys.Elevation.Level3);
        return bar;
    }

    // ---------- Theme + loading ----------

    // The row backgrounds are computed by a converter (selected tint, stripe, and for frozen panes an
    // opaque composite onto the surface), so the colours have to be concrete values rather than a
    // dynamic resource on the view. They used to be copied out of Application.Current.Resources once,
    // in the constructor, which left every selection, stripe and pinned pane in the old palette after a
    // light/dark flip and never saw a palette scoped over the grid. These three properties resolve the
    // tokens live through the grid's own element tree; a change re-feeds the converters and re-runs
    // just the row-background bindings - no rows are rebuilt.

    static readonly BindableProperty ThemePrimaryProperty = BindableProperty.Create(
        "ThemePrimary", typeof(Color), typeof(DataGrid), null,
        propertyChanged: static (b, _, _) => ((DataGrid)b).OnThemeColorsChanged());

    static readonly BindableProperty ThemeOnSurfaceProperty = BindableProperty.Create(
        "ThemeOnSurface", typeof(Color), typeof(DataGrid), null,
        propertyChanged: static (b, _, _) => ((DataGrid)b).OnThemeColorsChanged());

    static readonly BindableProperty ThemeSurfaceProperty = BindableProperty.Create(
        "ThemeSurface", typeof(Color), typeof(DataGrid), null,
        propertyChanged: static (b, _, _) => ((DataGrid)b).OnThemeColorsChanged());

    bool themeTracking;

    void ApplyTheme()
    {
        if (!this.themeTracking)
        {
            // Resolving during these calls fires OnThemeColorsChanged, which just lands on the same
            // converter values the ApplyThemeColors below writes.
            this.themeTracking = true;
            this.SetDynamicResource(ThemePrimaryProperty, ShinyThemeKeys.Color.Primary);
            this.SetDynamicResource(ThemeOnSurfaceProperty, ShinyThemeKeys.Color.OnSurface);
            this.SetDynamicResource(ThemeSurfaceProperty, ShinyThemeKeys.Color.Surface);
        }
        this.ApplyThemeColors();
    }

    void ApplyThemeColors()
    {
        var primary = (Color?)this.GetValue(ThemePrimaryProperty) ?? Color.FromArgb("#7C3AED");
        var onSurface = (Color?)this.GetValue(ThemeOnSurfaceProperty) ?? Colors.Black;
        var surface = (Color?)this.GetValue(ThemeSurfaceProperty) ?? Colors.White;

        this.selectionConverter.Selected = primary.WithAlpha(0.14f);
        this.selectionConverter.Stripe = onSurface.WithAlpha(0.04f);

        this.frozenBackgroundConverter.Selected = this.selectionConverter.Selected;
        this.frozenBackgroundConverter.Stripe = this.selectionConverter.Stripe;
        this.frozenBackgroundConverter.Surface = surface;
    }

    void OnThemeColorsChanged()
    {
        this.ApplyThemeColors();
        foreach (var row in this.dataRows)
            row.RaiseBackgroundChanged();
    }

    void UpdateLoading() => this.loadingOverlay.IsVisible = this.IsLoading;

    Grid BuildLoadingOverlay()
    {
        var spinner = new ActivityIndicator
        {
            IsRunning = true,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        spinner.SetDynamicResource(ActivityIndicator.ColorProperty, ShinyThemeKeys.Color.Primary);

        var overlay = new Grid { IsVisible = false, BackgroundColor = Colors.Black.WithAlpha(0.06f) };
        overlay.Add(spinner);
        return overlay;
    }
}
