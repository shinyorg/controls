using System.Collections;
using Microsoft.Maui.Platform;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.DataGrid;

/// <summary>
/// <c>Width="Auto"</c> columns, resolved once for the whole grid.
/// </summary>
/// <remarks>
/// <para>The header, the filter row, every data row, every summary row and the footer are separate
/// <see cref="Grid"/>s. Handing each of them <see cref="GridLength.Auto"/> let each one size the
/// column to its <i>own</i> content, so a header, a row holding "7" and a row holding "Seventeen"
/// all drew the same column at three different widths and nothing lined up.</para>
/// <para>Instead the grid measures the header cell, the footer summary cells and the cells of the
/// first <see cref="AutoWidthSampleSize"/> items of the source (in source order, so sorting, filtering
/// and paging never move the columns), takes the widest, and gives every row that one absolute width.
/// A value wider than anything in the sample is ellipsized like any other overlong cell. The result is
/// cached: it is re-measured when the columns, the source (or its contents), density, the sample size
/// or the platform handler change - not on a sort, a page turn or an expand.</para>
/// </remarks>
public partial class DataGrid
{
    /// <summary>Backing store for <see cref="AutoWidthSampleSize"/>.</summary>
    public static readonly BindableProperty AutoWidthSampleSizeProperty = BindableProperty.Create(
        nameof(AutoWidthSampleSize), typeof(int), typeof(DataGrid), 100,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(DataGrid), () =>
        {
            var grid = (DataGrid)b;
            grid.autoWidthsDirty = true;
            grid.RebuildAll();
        }));

    /// <summary>
    /// How many items (from the start of the source, in source order) an <c>Auto</c> column measures
    /// to find its width, alongside its header and footer. Default 100; 0 measures the header and
    /// footer only. Kept small on purpose: every sampled cell is a real platform measure.
    /// </summary>
    public int AutoWidthSampleSize
    {
        get => (int)this.GetValue(AutoWidthSampleSizeProperty);
        set => this.SetValue(AutoWidthSampleSizeProperty, value);
    }

    // Measured content widths (header / cell / footer, padding included) - the expensive part, cached.
    readonly Dictionary<DataGridColumn, double> autoMeasured = new();
    // The width each Auto column is actually laid out at: measured + tree indent, clamped.
    readonly Dictionary<DataGridColumn, double> autoWidths = new();
    bool autoWidthsDirty = true;

    /// <summary>
    /// Test seam: returns a view's desired width. Unset, the view is given a platform handler from the
    /// grid's own <c>MauiContext</c> and measured unconstrained - which is impossible before the grid
    /// has a handler, so until then an Auto column keeps a provisional width.
    /// </summary>
    internal Func<View, double>? AutoWidthMeasurer { get; set; }

    /// <summary>The header row's column widths, for tests.</summary>
    internal IReadOnlyList<GridLength> HeaderColumnWidths => this.headerGrid.ColumnDefinitions.Select(d => d.Width).ToList();

    /// <summary>Inflates the view the CollectionView would build for one display item, for tests.</summary>
    internal View BuildDisplayItemView(object item)
    {
        var template = this.collection.ItemTemplate is DataTemplateSelector selector
            ? selector.SelectTemplate(item, this.collection)
            : this.collection.ItemTemplate;

        var view = (View)template.CreateContent();
        view.BindingContext = item;
        return view;
    }

    /// <summary>The resolved width of an Auto column, or null while it cannot be measured yet.</summary>
    internal double? ResolvedAutoWidth(DataGridColumn column)
        => this.autoWidths.TryGetValue(column, out var width) ? width : null;

    /// <inheritdoc/>
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        // The first moment real measurement is possible.
        if (this.Handler is not null && this.VisibleColumns.Any(IsAutoColumn))
        {
            this.autoWidthsDirty = true;
            this.RebuildAll();
        }
    }

    static bool IsAutoColumn(DataGridColumn column) => column.WidthPercent <= 0 && column.Width.IsAuto;

    /// <summary>
    /// Brings <see cref="autoWidths"/> up to date. Returns true when any width changed, which means
    /// views already built with the old widths (the header, the filter row) are stale.
    /// </summary>
    bool RefreshAutoWidths()
    {
        var columns = this.VisibleColumns.Where(IsAutoColumn).ToList();
        if (columns.Count == 0)
        {
            var had = this.autoWidths.Count > 0;
            this.autoWidths.Clear();
            this.autoMeasured.Clear();
            return had;
        }

        if (this.autoWidthsDirty || columns.Any(c => !this.autoMeasured.ContainsKey(c)))
        {
            this.autoMeasured.Clear();
            if (this.MeasureAutoColumns(columns))
                this.autoWidthsDirty = false;
        }

        // The tree column also carries the caret and the deepest visible indent. Cheap, so it is
        // recomputed every rebuild rather than cached - expanding a level deeper widens it.
        var maxLevel = this.TreeEnabled && this.dataRows.Count > 0 ? this.dataRows.Max(r => r.Level) : 0;

        var next = new Dictionary<DataGridColumn, double>();
        foreach (var column in columns)
        {
            if (!this.autoMeasured.TryGetValue(column, out var measured))
                continue;

            if (this.IsTreeColumn(column))
                measured += TreeCaretSize + maxLevel * this.TreeIndentSize;

            next[column] = Math.Max(1, Math.Ceiling(ClampToColumnBounds(column, measured)));
        }

        var changed = next.Count != this.autoWidths.Count
            || next.Any(kv => !this.autoWidths.TryGetValue(kv.Key, out var old) || Math.Abs(old - kv.Value) >= 0.5);

        this.autoWidths.Clear();
        foreach (var kv in next)
            this.autoWidths[kv.Key] = kv.Value;

        return changed;
    }

    /// <summary>Measures every Auto column. False when nothing can be measured yet (no handler).</summary>
    bool MeasureAutoColumns(IReadOnlyList<DataGridColumn> columns)
    {
        var measurer = this.AutoWidthMeasurer;
        var context = this.Handler?.MauiContext;
        if (measurer is null && context is null)
            return false;

        var sample = this.AutoWidthSample();
        var footer = this.EffectiveSummaryRows(group: false);
        var footerItems = footer.Count > 0 ? this.ProcessedData() : null;

        foreach (var column in columns)
        {
            var widest = 0d;
            var temporary = new List<View>();

            double Measure(View view)
            {
                if (measurer is not null)
                    return measurer(view);

                if (view.Handler is null)
                {
                    view.ToHandler(context!);
                    temporary.Add(view);
                }
                return ((IView)view).Measure(double.PositiveInfinity, double.PositiveInfinity).Width;
            }

            try
            {
                if (this.ShowColumnHeaders)
                {
                    // Building a header cell registers it as the column's drag target; a throwaway
                    // measuring copy must not replace the live header's registration.
                    var hadDragCell = this.dragCells.TryGetValue(column.Id, out var liveDragCell);
                    try
                    {
                        widest = Math.Max(widest, Measure(this.BuildHeaderCell(column)));
                    }
                    finally
                    {
                        if (hadDragCell)
                            this.dragCells[column.Id] = liveDragCell!;
                        else
                            this.dragCells.Remove(column.Id);
                    }
                }

                if (sample.Count > 0)
                {
                    if (column.CellTemplate is not null)
                    {
                        // One instance, re-pointed at each item: bindings update synchronously, and
                        // building a template per sampled item is the cost the sample cap exists to avoid.
                        var content = (View)column.CellTemplate.CreateContent();
                        foreach (var item in sample)
                        {
                            content.BindingContext = item;
                            widest = Math.Max(widest, Measure(content));
                        }
                    }
                    else if (column.HasValue)
                    {
                        var label = this.BuildMeasureLabel(column, bold: false);
                        foreach (var item in sample)
                        {
                            label.Text = column.GetText(item);
                            widest = Math.Max(widest, Measure(label));
                        }
                    }
                }

                if (footerItems is not null)
                {
                    foreach (var definition in footer)
                    {
                        var cell = definition.CellFor(column);
                        if (cell is null || cell.CellTemplate is not null || cell.LegacyTemplate is not null)
                            continue;

                        var label = this.BuildMeasureLabel(column, cell.Bold);
                        label.Text = new DataGridSummaryRowItem(definition, footerItems, null).TextFor(column);
                        widest = Math.Max(widest, Measure(label));
                    }
                }
            }
            finally
            {
                foreach (var view in temporary)
                    view.Handler?.DisconnectHandler();
            }

            this.autoMeasured[column] = widest;
        }
        return true;
    }

    /// <summary>A label configured the way a text cell is, so it measures the same.</summary>
    Label BuildMeasureLabel(DataGridColumn column, bool bold) => new()
    {
        Padding = this.CellPadding,
        FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
        LineBreakMode = LineBreakMode.NoWrap,
        MaxLines = 1
    };

    /// <summary>The first <see cref="AutoWidthSampleSize"/> items, in source order.</summary>
    IReadOnlyList<object> AutoWidthSample()
    {
        var size = this.AutoWidthSampleSize;
        if (size <= 0)
            return [];

        IEnumerable? source = this.serverItems ?? this.ItemsSource;
        return source?.Cast<object>().Take(size).ToList() ?? [];
    }
}
