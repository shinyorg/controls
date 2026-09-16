using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.DataGrid;

/// <summary>
/// Grouping - any number of nested levels, each with its own summary rows.
/// </summary>
/// <remarks>
/// <see cref="GroupBy"/> is the whole state: a list of column ids, outermost first. The header's ⊞
/// button appends to (or removes from) that same list, so interactive grouping and a declared or
/// bound <c>GroupBy</c> are never two competing sources of truth.
/// <para>
/// Groups are flattened into the same <c>displayItems</c> list the rows live in - a header, then
/// either the next level's headers or the rows themselves, then the group's summary rows. That keeps
/// one virtualized <see cref="CollectionView"/> for the whole thing rather than nesting scrollers.
/// </para>
/// </remarks>
public partial class DataGrid
{
    /// <summary>Separates one level's key from the next in a group path - never appears in a key.</summary>
    const char GroupPathSeparator = '\u001f';

    /// <summary>How far each nesting level indents its header.</summary>
    internal const double GroupIndentSize = 18;

    /// <summary>Group paths whose expand/collapse state differs from <see cref="GroupDefaultExpanded"/>.</summary>
    readonly HashSet<string> toggledGroups = new();

    /// <summary>Set by Expand/CollapseAllGroups, which move the default rather than listing paths.</summary>
    bool? groupExpandedOverride;

    IDisposable? groupBySubscription;

    public static readonly BindableProperty GroupByProperty = BindableProperty.Create(
        nameof(GroupBy), typeof(IList<string>), typeof(DataGrid), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(DataGrid), () =>
            {
                ((DataGrid)b).OnGroupByChanged(o as INotifyCollectionChanged, n as INotifyCollectionChanged);
            }));

    public static readonly BindableProperty GroupSummaryPlacementProperty = BindableProperty.Create(
        nameof(GroupSummaryPlacement), typeof(DataGridGroupSummaryPlacement), typeof(DataGrid), DataGridGroupSummaryPlacement.Footer,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(DataGrid), () =>
            {
                ((DataGrid)b).RebuildRows();
            }));

    public static readonly BindableProperty GroupsInitiallyExpandedProperty = BindableProperty.Create(
        nameof(GroupsInitiallyExpanded), typeof(bool), typeof(DataGrid), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(DataGrid), () =>
            {
                var grid = (DataGrid)b;
                grid.groupExpandedOverride = null;
                grid.toggledGroups.Clear();
                grid.RebuildRows();
            }));

    public static readonly BindableProperty GroupSortDirectionProperty = BindableProperty.Create(
        nameof(GroupSortDirection), typeof(DataGridSortDirection), typeof(DataGrid), DataGridSortDirection.Ascending,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(DataGrid), () =>
            {
                ((DataGrid)b).RebuildRows();
            }));

    public static readonly BindableProperty GroupHeaderTemplateProperty = BindableProperty.Create(
        nameof(GroupHeaderTemplate), typeof(DataTemplate), typeof(DataGrid), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(DataGrid), () =>
            {
                ((DataGrid)b).RebuildRows();
            }));

    /// <summary>
    /// The columns to group by, outermost first - each entry a column's <c>PropertyName</c> (or
    /// <c>Title</c>). Grouping is on whenever this has an entry; <see cref="Groupable"/> only controls
    /// whether the user may change it from the column headers.
    /// </summary>
    public IList<string> GroupBy
    {
        get => (IList<string>)this.GetValue(GroupByProperty);
        set => this.SetValue(GroupByProperty, value);
    }

    /// <summary>Where each group's summary rows sit. Defaults to a footer under the group's rows.</summary>
    public DataGridGroupSummaryPlacement GroupSummaryPlacement
    {
        get => (DataGridGroupSummaryPlacement)this.GetValue(GroupSummaryPlacementProperty);
        set => this.SetValue(GroupSummaryPlacementProperty, value);
    }

    /// <summary>Whether a group starts expanded. Changing it resets every group to the new default.</summary>
    public bool GroupsInitiallyExpanded
    {
        get => (bool)this.GetValue(GroupsInitiallyExpandedProperty);
        set => this.SetValue(GroupsInitiallyExpandedProperty, value);
    }

    /// <summary>
    /// How groups are ordered among themselves. <c>None</c> leaves them in row order, which is what a
    /// sort on the grouped column already gives you.
    /// </summary>
    public DataGridSortDirection GroupSortDirection
    {
        get => (DataGridSortDirection)this.GetValue(GroupSortDirectionProperty);
        set => this.SetValue(GroupSortDirectionProperty, value);
    }

    /// <summary>Replaces the default group header. Its BindingContext is a <see cref="DataGridGroupHeader"/>.</summary>
    public DataTemplate? GroupHeaderTemplate
    {
        get => (DataTemplate?)this.GetValue(GroupHeaderTemplateProperty);
        set => this.SetValue(GroupHeaderTemplateProperty, value);
    }

    /// <summary>Every grouping level's column, outermost first. Empty when the grid is not grouped.</summary>
    internal IReadOnlyList<DataGridColumn> GroupColumns
    {
        get
        {
            var by = this.GroupBy;
            if (by is null || by.Count == 0)
                return Array.Empty<DataGridColumn>();

            var cols = new List<DataGridColumn>(by.Count);
            foreach (var id in by)
            {
                var col = this.FindColumn(id);
                if (col is not null && col.HasValue && !cols.Contains(col))
                    cols.Add(col);
            }
            return cols;
        }
    }

    DataGridColumn? FindColumn(string? id)
        => string.IsNullOrEmpty(id)
            ? null
            : this.Columns.FirstOrDefault(c =>
                string.Equals(c.Id, id, StringComparison.Ordinal) ||
                string.Equals(c.PropertyName, id, StringComparison.Ordinal) ||
                string.Equals(c.Title, id, StringComparison.Ordinal));

    bool Grouped => this.GroupColumns.Count > 0;

    void InitGrouping() => this.GroupBy = new ObservableCollection<string>();

    void OnGroupByChanged(INotifyCollectionChanged? oldValue, INotifyCollectionChanged? newValue)
    {
        // Weak: a bound collection can outlive the page (memory-leak fix).
        this.groupBySubscription?.Dispose();
        this.groupBySubscription = WeakEventSubscription.CollectionChanged(newValue, this.OnGroupByCollectionChanged);

        this.ResetGroupState();
        this.RebuildAll();
    }

    // No main-thread hop, unlike the items collection: grouping levels come from a tap or a view
    // model bound to the UI, never from the background thread a data load runs on.
    void OnGroupByCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        this.ResetGroupState();
        this.RebuildAll();
    }

    void ResetGroupState()
    {
        this.groupExpandedOverride = null;
        this.toggledGroups.Clear();
    }

    /// <summary>True when the user may group by this column from its header.</summary>
    bool EffectiveGroupable(DataGridColumn col) => this.Groupable && col.Groupable && col.HasValue;

    internal bool IsGroupedBy(DataGridColumn col) => this.GroupLevelOf(col) >= 0;

    /// <summary>The 0-based grouping level this column sits at, or -1 when it is not grouped by.</summary>
    internal int GroupLevelOf(DataGridColumn col)
    {
        var cols = this.GroupColumns;
        for (var i = 0; i < cols.Count; i++)
        {
            if (ReferenceEquals(cols[i], col))
                return i;
        }
        return -1;
    }

    /// <summary>Adds this column as the innermost grouping level, or removes it if it is already one.</summary>
    internal void ToggleGroupBy(DataGridColumn col)
    {
        var by = this.MutableGroupBy();
        var existing = by.FirstOrDefault(id => ReferenceEquals(this.FindColumn(id), col));
        if (existing is not null)
            by.Remove(existing);
        else
            by.Add(col.Id);

        this.ResetGroupState();

        // A bound list can be anything - if it doesn't raise CollectionChanged nothing else will
        // rebuild for us.
        if (by is not INotifyCollectionChanged)
        {
            this.RebuildHeader();
            this.RebuildRows();
        }
    }

    /// <summary>
    /// The list to mutate. A caller is free to bind <see cref="GroupBy"/> to a read-only list, in which
    /// case interactive grouping swaps in a copy rather than throwing halfway through a tap handler.
    /// </summary>
    IList<string> MutableGroupBy()
    {
        var by = this.GroupBy;
        if (by is null)
        {
            by = new ObservableCollection<string>();
            this.GroupBy = by;
        }
        else if (by.IsReadOnly)
        {
            by = new ObservableCollection<string>(by);
            this.GroupBy = by;
        }
        return by;
    }

    /// <summary>Drops every grouping level.</summary>
    public void ClearGrouping()
    {
        var by = this.MutableGroupBy();
        if (by.Count == 0)
            return;

        by.Clear();
        this.ResetGroupState();
        if (by is not INotifyCollectionChanged)
        {
            this.RebuildHeader();
            this.RebuildRows();
        }
    }

    public void ExpandAllGroups() => this.SetAllGroups(expanded: true);

    public void CollapseAllGroups() => this.SetAllGroups(expanded: false);

    /// <summary>
    /// Moves the default rather than listing paths. Listing them could only reach the groups currently
    /// rendered - a nested group inside a collapsed one is not in the item list, so "collapse all"
    /// would leave it expanded, ready to spring open the moment its parent was reopened.
    /// </summary>
    void SetAllGroups(bool expanded)
    {
        this.groupExpandedOverride = expanded;
        this.toggledGroups.Clear();
        this.RebuildRows();
    }

    bool GroupDefaultExpanded => this.groupExpandedOverride ?? this.GroupsInitiallyExpanded;

    internal bool IsGroupCollapsed(string path)
        => this.GroupDefaultExpanded ? this.toggledGroups.Contains(path) : !this.toggledGroups.Contains(path);

    /// <summary>Expands a collapsed group, or collapses an expanded one.</summary>
    public void ToggleGroup(DataGridGroupHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);

        if (!this.toggledGroups.Add(header.Path))
            this.toggledGroups.Remove(header.Path);

        this.RebuildRows();
    }

    /// <summary>The group headers currently in the item list, outermost first.</summary>
    public IReadOnlyList<DataGridGroupHeader> Groups
        => this.displayItems.OfType<DataGridGroupHeader>().ToList();

    /// <summary>
    /// Emits one grouping level into <c>displayItems</c>, recursing until the innermost level, which
    /// emits the rows themselves.
    /// </summary>
    void AppendGroupRows(IReadOnlyList<object> items, int level, string parentPath, ref int index)
    {
        var cols = this.GroupColumns;
        var col = cols[level];

        foreach (var group in this.OrderGroups(items.GroupBy(col.GetCellValue)))
        {
            var groupItems = group.ToList();
            var keyText = col.FormatValue(group.Key);
            var path = parentPath + GroupPathSeparator + (keyText ?? group.Key?.ToString() ?? "\u0000");
            var collapsed = this.IsGroupCollapsed(path);

            var header = new DataGridGroupHeader(
                group.Key,
                keyText,
                col.Title ?? col.PropertyName ?? string.Empty,
                groupItems.Count,
                collapsed,
                groupItems,
                level,
                path
            );
            this.displayItems.Add(header);

            if (this.GroupSummaryPlacement is DataGridGroupSummaryPlacement.Header or DataGridGroupSummaryPlacement.Both)
                this.AppendSummaryRows(header);

            if (collapsed)
                continue;

            if (level + 1 < cols.Count)
            {
                this.AppendGroupRows(groupItems, level + 1, path, ref index);
            }
            else
            {
                // Each group is its own block, so a column highlight is closed off at the group
                // boundary rather than drawn straight through the header sitting between them.
                var groupStart = this.dataRows.Count;
                foreach (var item in groupItems)
                {
                    var row = this.CreateRow(item, 0, index++);
                    this.dataRows.Add(row);
                    this.displayItems.Add(row);
                    this.AppendDetailRow(row);
                }
                this.StampBlock(groupStart);
            }

            if (this.GroupSummaryPlacement is DataGridGroupSummaryPlacement.Footer or DataGridGroupSummaryPlacement.Both)
                this.AppendSummaryRows(header);
        }
    }

    IEnumerable<IGrouping<object?, object>> OrderGroups(IEnumerable<IGrouping<object?, object>> groups)
        => this.GroupSortDirection switch
        {
            DataGridSortDirection.Ascending => groups.OrderBy(g => g.Key, DataGridValueComparer.Instance),
            DataGridSortDirection.Descending => groups.OrderByDescending(g => g.Key, DataGridValueComparer.Instance),
            _ => groups
        };

    View BuildGroupHeaderView()
    {
        if (this.GroupHeaderTemplate is not null)
        {
            var custom = (View)this.GroupHeaderTemplate.CreateContent();
            var host = new Grid();
            host.Add(custom);
            this.AddGroupHeaderTap(host);
            return host;
        }

        var caret = new Label { VerticalOptions = LayoutOptions.Center, WidthRequest = 18 }.WithFontSize(ShinyThemeKeys.Type.BodySmallSize);
        caret.SetBinding(Label.TextProperty, nameof(DataGridGroupHeader.CaretGlyph));
        caret.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        var text = new Label { FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center };
        text.SetBinding(Label.TextProperty, nameof(DataGridGroupHeader.Display));
        text.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);

        var layout = new HorizontalStackLayout { Spacing = 4, Padding = this.CellPadding, Children = { caret, text } };

        // Nested levels step in from their parent. The padding is the cell padding plus the indent, so
        // a level-0 header lines up with the cells exactly as it did before nesting existed.
        layout.SetBinding(Microsoft.Maui.Controls.Layout.PaddingProperty, new Binding(
            nameof(DataGridGroupHeader.Level),
            converter: new DataGridGroupIndentConverter(this.CellPadding)));

        // The group label spans every column, so it would slide out of view with the rest of the
        // content; pin it to the leading edge alongside the frozen cells.
        if (this.FrozenEnabled)
            this.TrackPane(layout, start: true);

        var container = new Grid();
        container.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);
        container.Add(layout);
        this.AddGroupHeaderTap(container);
        return container;
    }

    void AddGroupHeaderTap(View view)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, _) =>
        {
            if (((View)s!).BindingContext is DataGridGroupHeader h)
                this.ToggleGroup(h);
        };
        view.GestureRecognizers.Add(tap);
    }
}


/// <summary>Turns a group's nesting level into the header's padding.</summary>
sealed class DataGridGroupIndentConverter : IValueConverter
{
    readonly Thickness basePadding;

    public DataGridGroupIndentConverter(Thickness basePadding) => this.basePadding = basePadding;

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var level = value is int i ? i : 0;
        return new Thickness(
            this.basePadding.Left + (level * DataGrid.GroupIndentSize),
            this.basePadding.Top,
            this.basePadding.Right,
            this.basePadding.Bottom
        );
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
