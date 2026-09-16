using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.DataGrid;

public partial class DataGrid
{
    public static readonly BindableProperty RowHighlightProperty = BindableProperty.Create(
        nameof(RowHighlight), typeof(Func<object, DataGridCellStyle?>), typeof(DataGrid), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(DataGrid), () =>
            {
                ((DataGrid)b).RebuildRows();
            }));

    public static readonly BindableProperty HighlightsProperty = BindableProperty.Create(
        nameof(Highlights), typeof(IList<DataGridHighlight>), typeof(DataGrid), null,
        defaultValueCreator: _ => new ObservableCollection<DataGridHighlight>(),
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(DataGrid), () =>
            {
                ((DataGrid)b).OnHighlightsChanged(o, n);
            }));

    /// <summary>
    /// Highlights whole rows - a fill, a stroke, or both, applied to every cell of the rows the
    /// delegate returns a style for. Return <c>null</c> for a row to leave it alone.
    /// </summary>
    /// <remarks>
    /// Evaluated when a row binds (including when the virtualized list recycles a row onto a different
    /// item), not when a property on the item changes.
    /// </remarks>
    public Func<object, DataGridCellStyle?>? RowHighlight
    {
        get => (Func<object, DataGridCellStyle?>?)this.GetValue(RowHighlightProperty);
        set => this.SetValue(RowHighlightProperty, value);
    }

    /// <summary>
    /// Declarative highlighting rules, each covering a row, a column, one cell, or the whole grid
    /// depending on which of its targeting members are set. Later rules win over earlier ones at the
    /// same scope; a narrower scope always wins over a wider one.
    /// </summary>
    /// <remarks>
    /// Defaults to an <see cref="ObservableCollection{T}"/> the grid watches, so rules can be added and
    /// removed after the fact; assigning a collection of your own is watched the same way when it
    /// raises <see cref="INotifyCollectionChanged"/>. Mutating a rule's own properties in place is
    /// <b>not</b> observed - swap the rule, or call <see cref="RefreshHighlights"/>.
    /// </remarks>
    public IList<DataGridHighlight> Highlights
    {
        get => (IList<DataGridHighlight>)this.GetValue(HighlightsProperty);
        set => this.SetValue(HighlightsProperty, value);
    }

    IDisposable? highlightsSubscription;

    /// <summary>
    /// Re-evaluates every highlight against the current rows. Needed only when a rule was mutated in
    /// place, or when the data a <see cref="RowHighlight"/> predicate reads changed without the item
    /// source changing - neither of which the grid can see.
    /// </summary>
    public void RefreshHighlights() => this.RebuildRows();

    void OnHighlightsChanged(object? oldValue, object? newValue)
    {
        // Weak: a bound collection can outlive the page (memory-leak fix).
        this.highlightsSubscription?.Dispose();
        this.highlightsSubscription = WeakEventSubscription.CollectionChanged(newValue, this.OnHighlightRulesChanged);

        this.RebuildRows();
    }

    void OnHighlightRulesChanged(object? sender, NotifyCollectionChangedEventArgs e) => this.RebuildRows();

    /// <summary>Starts watching the collection the default value creator handed us - no propertyChanged fires for that.</summary>
    void ObserveDefaultHighlights()
    {
        this.highlightsSubscription?.Dispose();
        this.highlightsSubscription = WeakEventSubscription.CollectionChanged(this.Highlights, this.OnHighlightRulesChanged);
    }

    /// <summary>
    /// True when anything at all could paint a cell. Every cell of every row asks, so the whole
    /// resolution path - and the extra view each cell needs to carry a stroke - is skipped for the
    /// grids, most of them, that style nothing.
    /// </summary>
    internal bool HasCellStyling
        => this.RowHighlight is not null
            || this.Highlights.Count > 0
            || this.Columns.Any(c => c.Highlight is not null || c.CellStyle is not null);

    /// <summary>
    /// The final style for one cell: every rule that covers it, laid over each other from the widest
    /// scope to the narrowest. The position flags are what let a stroke trace the perimeter of the
    /// region rather than boxing each cell in it.
    /// </summary>
    internal DataGridCellStyle? ResolveCellStyle(
        DataGridColumn column,
        object data,
        bool firstColumn,
        bool lastColumn,
        bool firstRow,
        bool lastRow
    )
    {
        DataGridCellStyle? result = null;

        void Apply(DataGridCellStyle? style, DataGridHighlightScope scope)
        {
            if (style is null)
                return;

            if (style.HasBorder && style.BorderEdges is null)
                style = style.WithEdges(EdgesFor(scope, firstColumn, lastColumn, firstRow, lastRow));

            result = DataGridCellStyle.Merge(result, style);
        }

        // Widest first. Within one scope the declared column property goes under the collection, so a
        // rule handed in at runtime (a user tapping "highlight this") can override the markup.
        this.ApplyRules(column, data, DataGridHighlightScope.Grid, Apply);
        Apply(column.Highlight, DataGridHighlightScope.Column);
        this.ApplyRules(column, data, DataGridHighlightScope.Column, Apply);
        Apply(this.RowHighlight?.Invoke(data), DataGridHighlightScope.Row);
        this.ApplyRules(column, data, DataGridHighlightScope.Row, Apply);
        this.ApplyRules(column, data, DataGridHighlightScope.Cell, Apply);
        Apply(column.CellStyle?.Invoke(data), DataGridHighlightScope.Cell);

        return result;
    }

    void ApplyRules(
        DataGridColumn column,
        object data,
        DataGridHighlightScope scope,
        Action<DataGridCellStyle?, DataGridHighlightScope> apply
    )
    {
        foreach (var rule in this.Highlights)
        {
            if (rule is null || !rule.IsEnabled || rule.Scope != scope)
                continue;

            if (rule.MatchesRow(data) && rule.MatchesColumn(column))
                apply(rule, scope);
        }
    }

    /// <summary>
    /// The sides of one cell that fall on the outside of its highlight's region. A region only one
    /// column wide is stroked on both vertical edges of every cell in it; one that spans the columns
    /// is stroked only where it actually ends. Same, transposed, for rows.
    /// </summary>
    internal static DataGridBorderEdges EdgesFor(
        DataGridHighlightScope scope,
        bool firstColumn,
        bool lastColumn,
        bool firstRow,
        bool lastRow
    )
    {
        var spansColumns = scope is DataGridHighlightScope.Row or DataGridHighlightScope.Grid;
        var spansRows = scope is DataGridHighlightScope.Column or DataGridHighlightScope.Grid;

        var edges = DataGridBorderEdges.None;
        if (!spansColumns || firstColumn) edges |= DataGridBorderEdges.Left;
        if (!spansColumns || lastColumn) edges |= DataGridBorderEdges.Right;
        if (!spansRows || firstRow) edges |= DataGridBorderEdges.Top;
        if (!spansRows || lastRow) edges |= DataGridBorderEdges.Bottom;
        return edges;
    }

    /// <summary>
    /// Wraps a cell so it can carry a highlight: the fill goes on the wrapper's own background, which
    /// puts it under the content rather than over it, and the stroke goes on a transparent
    /// <see cref="GraphicsView"/> laid on top - added only if a stroke ever actually shows up, since
    /// most styled grids only ever colour text.
    /// </summary>
    View WrapHighlight(View content, Label? label, DataGridColumn column)
    {
        var columns = this.VisibleColumns;
        var firstColumn = ReferenceEquals(columns.FirstOrDefault(), column);
        var lastColumn = ReferenceEquals(columns.LastOrDefault(), column);

        var host = new Grid();
        host.Add(content);

        GraphicsView? paint = null;
        DataGridHighlightDrawable? drawable = null;

        host.BindingContextChanged += (sender, _) =>
        {
            var cell = (Grid)sender!;
            var row = cell.BindingContext as DataGridRow;
            var style = row is null
                ? null
                : this.ResolveCellStyle(column, row.Data, firstColumn, lastColumn, row.IsFirstRow, row.IsLastRow);

            // Re-asserted rather than only overridden: the virtualized list recycles this exact view
            // onto other rows, and a highlight left behind would follow the wrong item down the list.
            if (label is not null)
            {
                if (style?.TextColor is not null)
                    label.TextColor = style.TextColor;
                else
                    label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

                label.FontAttributes = style?.FontAttributes ?? Microsoft.Maui.Controls.FontAttributes.None;
            }

            cell.BackgroundColor = style?.EffectiveBackground() ?? Colors.Transparent;

            if (style?.HasBorder == true)
            {
                if (paint is null)
                {
                    drawable = new DataGridHighlightDrawable();
                    paint = new GraphicsView { Drawable = drawable, InputTransparent = true };
                    // Added last, so it paints over the content - a stroke that landed under an opaque
                    // cell background would be invisible. It draws nothing but lines, so the text below
                    // it still reads.
                    cell.Add(paint);
                }

                drawable!.Apply(style, style.BorderEdges ?? DataGridBorderEdges.All);
                paint.IsVisible = true;
                paint.Invalidate();
            }
            else if (paint is not null)
            {
                paint.IsVisible = false;
            }
        };

        return host;
    }
}
