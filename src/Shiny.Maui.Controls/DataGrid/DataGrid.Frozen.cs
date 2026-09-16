using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.DataGrid;

/// <summary>
/// Horizontal scrolling and frozen (pinned) columns.
/// </summary>
/// <remarks>
/// The header is frozen vertically for free - it lives in its own row above the CollectionView, so
/// scrolling the rows never moves it. Freezing a column is the harder half: MAUI has no sticky
/// positioning, and splitting the rows across two CollectionViews would mean keeping two scroll
/// offsets in lockstep. Instead the frozen cells stay in the same row Grid as everything else, moved
/// into a small pane that spans their columns, and each pane is translated back by the scroll offset
/// so it lands where it started. One ScrollView drives header, rows and footer together, so the
/// three can never drift apart.
/// </remarks>
public partial class DataGrid
{
    Grid bodyGrid = null!;
    Grid? scrollContent;
    ScrollView? hostScroll;
    double scrollX;
    int frozenStart;
    int frozenEnd;

    // Row panes are created by the CollectionView's item template and die with view recycling, so
    // they are held weakly and pruned as we go. Header/filter/footer panes are in here too - the
    // visual tree keeps those alive.
    readonly List<WeakReference<View>> frozenStartPanes = new();
    readonly List<WeakReference<View>> frozenEndPanes = new();

    /// <summary>True when something is actually pinned - freezing without sideways scroll is a no-op.</summary>
    bool FrozenEnabled => this.HorizontalScroll && (this.frozenStart > 0 || this.frozenEnd > 0);

    /// <summary>
    /// Resolves the leading/trailing pinned runs. Only a contiguous run at each edge can be frozen:
    /// a pinned column with scrolling columns on both sides has nowhere coherent to sit.
    /// </summary>
    void RefreshFrozenCounts()
    {
        var cols = this.VisibleColumns;

        var start = 0;
        while (start < cols.Count && cols[start].Frozen == DataGridFrozen.Start)
            start++;
        start = Math.Clamp(Math.Max(start, this.FrozenColumns), 0, cols.Count);

        var end = 0;
        while (end < cols.Count && cols[cols.Count - 1 - end].Frozen == DataGridFrozen.End)
            end++;
        end = Math.Clamp(Math.Max(end, this.FrozenEndColumns), 0, cols.Count - start);

        this.frozenStart = start;
        this.frozenEnd = end;
    }

    /// <summary>
    /// Star widths collapse to nothing under the unbounded measure a horizontal ScrollView hands its
    /// content, so in scroll mode every proportional width resolves to a concrete one.
    /// </summary>
    /// <remarks>
    /// Min/max only reach a width that is (or resolves to) a concrete number. A star column outside
    /// <see cref="HorizontalScroll"/> stays a star - MAUI's Grid has no notion of a bounded star, so
    /// clamping one here would mean silently turning it absolute and killing the proportional layout
    /// the caller asked for.
    /// <para>
    /// Only the column's own <see cref="DataGridColumn.MinWidth"/> / <see cref="DataGridColumn.MaxWidth"/>
    /// apply here. The grid-level <see cref="MinColumnWidth"/> / <see cref="MaxColumnWidth"/> bound a
    /// resize drag, not the layout - otherwise the 48 default would quietly widen a deliberately narrow
    /// icon column that nobody asked to resize.
    /// </para>
    /// </remarks>
    internal GridLength ResolveWidth(DataGridColumn col)
    {
        if (col.WidthPercent > 0)
            return this.ResolvePercentWidth(col);

        var w = col.Width;

        if (w.IsAbsolute)
            return new GridLength(ClampToColumnBounds(col, w.Value));

        // One measured width shared by every row - see DataGrid.AutoWidth.cs. Until the grid can
        // measure (no handler yet) it falls through to the provisional behaviour below.
        if (w.IsAuto && this.ResolvedAutoWidth(col) is { } auto)
            return new GridLength(auto);

        if (!this.HorizontalScroll)
            return w;

        var factor = w.IsStar && w.Value > 0 ? w.Value : 1;
        return new GridLength(Math.Max(1, ClampToColumnBounds(col, this.DefaultColumnWidth * factor)));
    }

    /// <summary>
    /// A percentage becomes a star of the same factor - the Grid already divides the available width
    /// in the ratio of the factors, which is what a percentage is asking for. Under
    /// <see cref="HorizontalScroll"/> there is no "available width" to share (the columns are meant to
    /// overflow it), so it resolves against the scroller's measured width instead.
    /// </summary>
    GridLength ResolvePercentWidth(DataGridColumn col)
    {
        if (!this.HorizontalScroll)
            return new GridLength(col.WidthPercent, GridUnitType.Star);

        var viewport = this.ViewportWidth;

        // Before the first layout there is no viewport to take a percentage of. DefaultColumnWidth is
        // the same stand-in a star gets in this mode, and the SizeChanged hook rebuilds once the real
        // width arrives - so this is what the grid shows for one frame, not what it settles on.
        return new GridLength(Math.Max(1, ClampToColumnBounds(
            col,
            viewport > 0 ? viewport * col.WidthPercent / 100 : this.DefaultColumnWidth)));
    }

    /// <summary>The width a percentage column is a percentage <i>of</i> under HorizontalScroll.</summary>
    double ViewportWidth
        => this.hostScroll is { Width: > 0 } scroll ? scroll.Width : this.Width;

    /// <summary>
    /// Re-resolves percentage columns when the scroller's width changes. Star columns need none of
    /// this - the Grid re-divides them itself - so this is gated on a percentage actually being in
    /// play, and on the width having genuinely moved, because the rebuild re-lays out the very
    /// scroller whose SizeChanged called it.
    /// </summary>
    void RefreshPercentWidths()
    {
        if (!this.HorizontalScroll)
            return;

        var viewport = this.ViewportWidth;
        if (viewport <= 0 || Math.Abs(viewport - this.lastViewportWidth) < 0.5)
            return;

        this.lastViewportWidth = viewport;
        if (this.VisibleColumns.Any(c => c.WidthPercent > 0))
            this.RebuildAll();
    }

    double lastViewportWidth;

    /// <summary>The floor for <paramref name="col"/>: its own MinWidth, else the grid's.</summary>
    internal double EffectiveMinWidth(DataGridColumn col)
        => col.MinWidth > 0 ? col.MinWidth : Math.Max(1, this.MinColumnWidth);

    /// <summary>The ceiling for <paramref name="col"/>: its own MaxWidth, else the grid's, else none.</summary>
    internal double? EffectiveMaxWidth(DataGridColumn col)
    {
        var max = col.MaxWidth > 0 ? col.MaxWidth : this.MaxColumnWidth;
        return max > 0 ? max : null;
    }

    /// <summary>
    /// Holds <paramref name="width"/> inside the column's min/max, falling back to the grid-level
    /// defaults. The floor wins a contradictory pair (max below min) so a bad configuration still
    /// leaves a usable column rather than a sliver.
    /// </summary>
    internal double ClampColumnWidth(DataGridColumn col, double width)
        => Clamp(width, this.EffectiveMinWidth(col), this.EffectiveMaxWidth(col));

    /// <summary>The same clamp, but with no grid-level fallback - see <see cref="ResolveWidth"/>.</summary>
    static double ClampToColumnBounds(DataGridColumn col, double width)
        => Clamp(width, col.MinWidth > 0 ? col.MinWidth : 0, col.MaxWidth > 0 ? col.MaxWidth : null);

    static double Clamp(double width, double min, double? max)
    {
        if (max is not null)
            width = Math.Min(width, max.Value);

        return Math.Max(width, min);
    }

    double TotalColumnsWidth
    {
        get
        {
            var total = (this.HasMultiSelect ? CheckboxColumnWidth : 0)
                + (this.HasExpanderColumn ? ExpanderColumnWidth : 0);
            foreach (var column in this.VisibleColumns)
                total += this.ResolveWidth(column).Value;
            return total;
        }
    }

    double MaxScrollX
        => this.hostScroll is null ? 0 : Math.Max(0, this.TotalColumnsWidth - this.hostScroll.Width);

    /// <summary>
    /// Re-parents header/rows/footer into (or out of) the shared horizontal ScrollView. Everything
    /// that scrolls sideways has to share one ScrollView, otherwise the columns drift apart.
    /// </summary>
    void ApplyLayoutMode()
    {
        this.bodyGrid.Children.Clear();
        if (this.hostScroll is not null)
            this.hostScroll.Content = null;
        this.scrollContent?.Children.Clear();

        this.bodyGrid.Add(this.toolbarBar, 0, 0);
        this.bodyGrid.Add(this.pagerBar, 0, 4);

        if (this.HorizontalScroll)
        {
            if (this.scrollContent is null)
            {
                this.scrollContent = new Grid
                {
                    RowSpacing = 0,
                    RowDefinitions =
                    {
                        new RowDefinition(GridLength.Auto),
                        new RowDefinition(GridLength.Star),
                        new RowDefinition(GridLength.Auto)
                    }
                };

                // Rows are transparent, so without this they sit on whatever is behind the grid while
                // the frozen panes - which must be opaque - paint Surface. Same colour or the pinned
                // columns read as a slightly different shade from the ones beside them.
                this.scrollContent.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);
            }

            if (this.hostScroll is null)
            {
                this.hostScroll = new ScrollView { Orientation = ScrollOrientation.Horizontal };
                this.hostScroll.Scrolled += (_, e) =>
                {
                    this.scrollX = e.ScrollX;
                    this.ApplyFrozenTranslation();
                };
                this.hostScroll.SizeChanged += (_, _) =>
                {
                    this.RefreshPercentWidths();
                    this.UpdateScrollContentWidth();
                    this.ApplyFrozenTranslation();
                };
            }

            this.scrollContent.Add(this.headerWrapper, 0, 0);
            this.scrollContent.Add(this.collection, 0, 1);
            if (this.footerWrapper is not null)
                this.scrollContent.Add(this.footerWrapper, 0, 2);

            this.hostScroll.Content = this.scrollContent;
            this.bodyGrid.Add(this.hostScroll, 0, 1);
            Grid.SetRowSpan(this.hostScroll, 3);
            this.UpdateScrollContentWidth();
        }
        else
        {
            this.scrollX = 0;
            this.bodyGrid.Add(this.headerWrapper, 0, 1);
            this.bodyGrid.Add(this.collection, 0, 2);
            if (this.footerWrapper is not null)
                this.bodyGrid.Add(this.footerWrapper, 0, 3);
        }
    }

    void UpdateScrollContentWidth()
    {
        if (this.scrollContent is null)
            return;

        // Never narrower than the viewport, or a short grid would sit in a puddle on the left.
        var viewport = this.hostScroll?.Width ?? 0;
        this.scrollContent.WidthRequest = Math.Max(this.TotalColumnsWidth, viewport);
    }

    void ApplyFrozenTranslation()
    {
        var offset = this.scrollX;
        var trailing = this.scrollX - this.MaxScrollX;

        Apply(this.frozenStartPanes, offset);
        Apply(this.frozenEndPanes, trailing);

        static void Apply(List<WeakReference<View>> panes, double translation)
        {
            for (var i = panes.Count - 1; i >= 0; i--)
            {
                if (panes[i].TryGetTarget(out var pane))
                    pane.TranslationX = translation;
                else
                    panes.RemoveAt(i);
            }
        }
    }

    void TrackPane(View pane, bool start)
    {
        var panes = start ? this.frozenStartPanes : this.frozenEndPanes;
        panes.RemoveAll(w => !w.TryGetTarget(out _));
        panes.Add(new WeakReference<View>(pane));
        pane.TranslationX = start ? this.scrollX : this.scrollX - this.MaxScrollX;
    }

    /// <summary>Empty stand-ins for the leading columns, for the rows that have nothing to put there.</summary>
    IReadOnlyList<View> LeadingPlaceholders()
    {
        var cells = new List<View>();
        for (var i = 0; i < this.LeadingColumnCount; i++)
            cells.Add(new Grid());
        return cells;
    }

    /// <summary>
    /// Fills a single-row grid (header, filter row, a data row, the footer) with one cell per column,
    /// moving the pinned runs into their own panes. <paramref name="leadingCells"/> are the detail
    /// expander and the multi-select checkbox, which are always leftmost and so travel with the start
    /// pane.
    /// </summary>
    void LayoutCells(
        Grid grid,
        IReadOnlyList<View> leadingCells,
        Func<DataGridColumn, View> cellFactory,
        Action<Grid>? stylePane = null
    )
    {
        var cols = this.VisibleColumns;
        var lead = leadingCells.Count;

        if (!this.FrozenEnabled)
        {
            for (var i = 0; i < lead; i++)
                grid.Add(leadingCells[i], i, 0);

            for (var i = 0; i < cols.Count; i++)
                grid.Add(cellFactory(cols[i]), lead + i, 0);
            return;
        }

        var startCount = this.frozenStart;
        var endCount = this.frozenEnd;

        for (var i = startCount; i < cols.Count - endCount; i++)
            grid.Add(cellFactory(cols[i]), lead + i, 0);

        if (startCount > 0)
        {
            var pane = this.CreatePane(grid, 0, lead + startCount);
            var slot = 0;
            for (var i = 0; i < lead; i++)
                pane.Add(leadingCells[i], slot++, 0);
            for (var i = 0; i < startCount; i++)
                pane.Add(cellFactory(cols[i]), slot++, 0);

            stylePane?.Invoke(pane);
            this.AddPane(grid, pane, 0, lead + startCount, start: true);
        }
        else
        {
            for (var i = 0; i < lead; i++)
                grid.Add(leadingCells[i], i, 0);
        }

        if (endCount > 0)
        {
            var first = lead + cols.Count - endCount;
            var pane = this.CreatePane(grid, first, endCount);
            var slot = 0;
            for (var i = cols.Count - endCount; i < cols.Count; i++)
                pane.Add(cellFactory(cols[i]), slot++, 0);

            stylePane?.Invoke(pane);
            this.AddPane(grid, pane, first, endCount, start: false);
        }
    }

    Grid CreatePane(Grid parent, int firstColumn, int span)
    {
        var pane = new Grid { ColumnSpacing = 0 };

        // Same widths as the slot the pane occupies, so the cells inside land exactly where they
        // would have if they had stayed in the parent grid.
        for (var i = firstColumn; i < firstColumn + span && i < parent.ColumnDefinitions.Count; i++)
            pane.ColumnDefinitions.Add(new ColumnDefinition(parent.ColumnDefinitions[i].Width));

        return pane;
    }

    void AddPane(Grid grid, Grid pane, int column, int span, bool start)
    {
        grid.Add(pane, column, 0);
        Grid.SetColumnSpan(pane, span);

        // Added last and raised so the scrolling cells slide underneath rather than over. Static -
        // no drag is in flight, so the native re-parent ZIndex does is harmless here.
        pane.ZIndex = 1;
        this.TrackPane(pane, start);
    }

    /// <summary>An opaque pane background - the scrolling cells pass underneath and must not show through.</summary>
    void StyleSurfacePane(Grid pane)
        => pane.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);

    void StyleContainerPane(Grid pane)
        => pane.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);

    /// <summary>
    /// Row panes repeat the row's own selection/stripe state, but composited onto the surface colour:
    /// the row grid paints a translucent tint over whatever is behind it, and a translucent pin would
    /// let the scrolled cells show through.
    /// </summary>
    void StyleRowPane(Grid pane)
        => pane.SetBinding(VisualElement.BackgroundColorProperty, new MultiBinding
        {
            Converter = this.frozenBackgroundConverter,
            Bindings =
            {
                new Binding(nameof(DataGridRow.IsSelected)),
                new Binding(nameof(DataGridRow.Index)),
                // Unused by the converter: raised on a theme change so the background re-resolves.
                new Binding(nameof(DataGridRow.BackgroundRevision))
            }
        });
}
