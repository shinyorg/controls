using System.Collections;
using System.Collections.Specialized;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Maui.Controls.Cells;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;
using TvTableSection = Shiny.Maui.Controls.Sections.TableSection;
using TvTableRoot = Shiny.Maui.Controls.Sections.TableRoot;

namespace Shiny.Maui.Controls;

[ContentProperty(nameof(Root))]
public partial class TableView : ContentView
{
    ScrollView scrollView = default!;
    VerticalStackLayout rootLayout = default!;
    TvTableRoot root = default!;
    bool isRendering;
    IDisposable? viewItemsSourceSubscription;
    readonly List<TvTableSection> generatedSections = new();
    DragSortController? dragSort;
    internal bool SuppressRender { get; set; }

    public TableView()
    {
        root = new TvTableRoot();
        root.SetParentTableView(this);
        root.RootChanged += OnRootChanged;

        scrollView = new ScrollView();
        rootLayout = new VerticalStackLayout();
        scrollView.Content = rootLayout;
        Content = scrollView;

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(TableView));
    }


    public TvTableRoot Root
    {
        get => root;
        set
        {
            if (root != null)
            {
                root.RootChanged -= OnRootChanged;
                root.SetParentTableView(null);
            }

            root = value ?? new TvTableRoot();
            root.SetParentTableView(this);
            root.RootChanged += OnRootChanged;
            RenderSections();
        }
    }



    public static readonly BindableProperty ShowSectionSeparatorProperty = BindableProperty.Create(
        nameof(ShowSectionSeparator), typeof(bool), typeof(TableView), true,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableView), () =>
            {
                ((TableView)b).RenderSections();
            }));

    public static readonly BindableProperty SectionSeparatorHeightProperty = BindableProperty.Create(
        nameof(SectionSeparatorHeight), typeof(double), typeof(TableView), 8d,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableView), () =>
            {
                ((TableView)b).RenderSections();
            }));

    public static readonly BindableProperty SectionSeparatorColorProperty = BindableProperty.Create(
        nameof(SectionSeparatorColor), typeof(Color), typeof(TableView), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableView), () =>
            {
                ((TableView)b).RenderSections();
            }));

    public static readonly BindableProperty SeparatorColorProperty = BindableProperty.Create(
        nameof(SeparatorColor), typeof(Color), typeof(TableView), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableView), () =>
            {
                ((TableView)b).RenderSections();
            }));

    public static readonly BindableProperty SeparatorHeightProperty = BindableProperty.Create(
        nameof(SeparatorHeight), typeof(double), typeof(TableView), -1d,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableView), () =>
            {
                ((TableView)b).RenderSections();
            }));

    public static readonly BindableProperty SeparatorPaddingProperty = BindableProperty.Create(
        nameof(SeparatorPadding), typeof(double), typeof(TableView), -1d,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableView), () =>
            {
                ((TableView)b).RenderSections();
            }));

    public static readonly BindableProperty ItemDroppedCommandProperty = BindableProperty.Create(
        nameof(ItemDroppedCommand), typeof(ICommand), typeof(TableView), null);

    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource), typeof(IEnumerable), typeof(TableView), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableView), () => OnViewItemsSourceChanged(b, o, n)));

    public static readonly BindableProperty ItemTemplateProperty = BindableProperty.Create(
        nameof(ItemTemplate), typeof(DataTemplate), typeof(TableView), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableView), () =>
            {
                ((TableView)b).RegenerateTemplatedSections();
            }));

    public static readonly BindableProperty TemplateStartIndexProperty = BindableProperty.Create(
        nameof(TemplateStartIndex), typeof(int), typeof(TableView), 0,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableView), () =>
            {
                ((TableView)b).RegenerateTemplatedSections();
            }));

    public static readonly BindableProperty ScrollToTopProperty = BindableProperty.Create(
        nameof(ScrollToTop), typeof(bool), typeof(TableView), false,
        // Deliberately not an async lambda: that compiles to async void, so a fault here
        // becomes an unobserved task exception rather than something a caller can handle.
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableView), () =>
        {
            if ((bool)n)
                _ = ((TableView)b).ScrollToTopAndResetAsync();
        }));

    public static readonly BindableProperty ScrollToBottomProperty = BindableProperty.Create(
        nameof(ScrollToBottom), typeof(bool), typeof(TableView), false,
        // Deliberately not an async lambda: that compiles to async void, so a fault here
        // becomes an unobserved task exception rather than something a caller can handle.
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableView), () =>
        {
            if ((bool)n)
                _ = ((TableView)b).ScrollToBottomAndResetAsync();
        }));



    public bool ShowSectionSeparator
    {
        get => (bool)GetValue(ShowSectionSeparatorProperty);
        set => SetValue(ShowSectionSeparatorProperty, value);
    }

    public double SectionSeparatorHeight
    {
        get => (double)GetValue(SectionSeparatorHeightProperty);
        set => SetValue(SectionSeparatorHeightProperty, value);
    }

    public Color? SectionSeparatorColor
    {
        get => (Color?)GetValue(SectionSeparatorColorProperty);
        set => SetValue(SectionSeparatorColorProperty, value);
    }

    public Color? SeparatorColor
    {
        get => (Color?)GetValue(SeparatorColorProperty);
        set => SetValue(SeparatorColorProperty, value);
    }

    public double SeparatorHeight
    {
        get => (double)GetValue(SeparatorHeightProperty);
        set => SetValue(SeparatorHeightProperty, value);
    }

    public double SeparatorPadding
    {
        get => (double)GetValue(SeparatorPaddingProperty);
        set => SetValue(SeparatorPaddingProperty, value);
    }

    public ICommand? ItemDroppedCommand
    {
        get => (ICommand?)GetValue(ItemDroppedCommandProperty);
        set => SetValue(ItemDroppedCommandProperty, value);
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public int TemplateStartIndex
    {
        get => (int)GetValue(TemplateStartIndexProperty);
        set => SetValue(TemplateStartIndexProperty, value);
    }

    public bool ScrollToTop
    {
        get => (bool)GetValue(ScrollToTopProperty);
        set => SetValue(ScrollToTopProperty, value);
    }

    public bool ScrollToBottom
    {
        get => (bool)GetValue(ScrollToBottomProperty);
        set => SetValue(ScrollToBottomProperty, value);
    }



    public event EventHandler<ItemDroppedEventArgs>? ItemDropped;
    public event EventHandler? ModelChanged;
    public event EventHandler<CellPropertyChangedEventArgs>? CellPropertyChanged;



    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (root != null)
            SetInheritedBindingContext(root, BindingContext);
    }



    void OnRootChanged(object? sender, EventArgs e)
    {
        RenderSections();
        ModelChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------- Drag sort plumbing (see DragSortController) ----------

    internal DragSortController DragSort => dragSort ??= new DragSortController(this);

    internal double ScrollOffsetY => scrollView.ScrollY;

    internal double ViewportHeight => scrollView.Height;

    internal double ContentHeight => rootLayout.Height;

    internal VisualElement ScrollContent => rootLayout;

    internal void ScrollToY(double y) => _ = scrollView.ScrollToAsync(0, y, false);

    // Note: suppressing the scroller during a drag via ScrollView.Orientation = Neither looks
    // like the obvious lever, but on iOS toggling Orientation snaps the offset back to the top,
    // which drops the drag geometry on the floor the instant it starts. Locking the scroller is
    // left to the DragSortRow platform hooks, which do it natively without moving the content.


    internal void RenderSections()
    {
        if (isRendering || SuppressRender)
            return;

        // Rebuilding the tree invalidates every row the drag is tracking.
        dragSort?.Abort();

        isRendering = true;

        try
        {
            // Detach all cells from their current parent views before clearing.
            // Android requires native views to be removed from their parent ViewGroup
            // before they can be re-added to a new one.
            foreach (var section in GetAllSections())
            {
                foreach (var cell in section.GetVisibleCells())
                {
                    (cell.Parent as Layout)?.Remove(cell);
                }
            }

            rootLayout.Children.Clear();

            // Only the visible sections, and the filter has to happen here rather than in the loop.
            // A hidden section renders as a zero-height placeholder, so counting it would put a
            // separator on both sides of nothing — two rules together where one section is hidden
            // between two visible ones, and a rule under the last visible section when the hidden
            // ones are trailing. Both read as a rendering fault rather than as a hidden section.
            var sections = GetVisibleSections();

            for (var i = 0; i < sections.Count; i++)
            {
                var sectionView = SectionRenderer.Render(sections[i], this);
                rootLayout.Children.Add(sectionView);

                // Section separator
                if (ShowSectionSeparator && i < sections.Count - 1)
                {
                    var gap = new BoxView { HeightRequest = SectionSeparatorHeight };

                    // Bound, not assigned: a literal captured here is read once, at render time, so
                    // it survives an appearance flip and leaves a near-black band sitting between the
                    // sections of a light table.
                    if (SectionSeparatorColor is { } explicitColor)
                        gap.Color = explicitColor;
                    else if (BackgroundColor is { } ownBackground && ownBackground != Colors.Transparent)
                        gap.Color = ownBackground;
                    else
                        gap.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.Background);

                    rootLayout.Children.Add(gap);
                }
            }
        }
        finally
        {
            isRendering = false;
        }
    }

    /// <summary>
    /// The sections that will actually draw something, in order. What the separator logic counts.
    /// </summary>
    internal IReadOnlyList<TvTableSection> GetVisibleSections()
    {
        var all = GetAllSections();

        var visible = new List<TvTableSection>(all.Count);
        foreach (var section in all)
        {
            if (section.IsVisible)
                visible.Add(section);
        }
        return visible;
    }

    internal IReadOnlyList<TvTableSection> GetAllSections()
    {
        var staticSections = root.Sections;

        if (generatedSections.Count == 0)
            return staticSections;

        var result = new List<TvTableSection>();
        var insertIndex = Math.Min(TemplateStartIndex, staticSections.Count);

        for (var i = 0; i < insertIndex; i++)
            result.Add(staticSections[i]);

        result.AddRange(generatedSections);

        for (var i = insertIndex; i < staticSections.Count; i++)
            result.Add(staticSections[i]);

        return result;
    }



    static void OnViewItemsSourceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var tv = (TableView)bindable;

        // Weak: the source is usually a view model's collection, which outlives the page.
        tv.viewItemsSourceSubscription?.Dispose();
        tv.viewItemsSourceSubscription = WeakEventSubscription.CollectionChanged(newValue, tv.OnViewItemsSourceCollectionChanged);

        tv.RegenerateTemplatedSections();
    }

    void OnViewItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RegenerateTemplatedSections();
    }

    void RegenerateTemplatedSections()
    {
        foreach (var section in generatedSections)
        {
            section.SectionChanged -= OnGeneratedSectionChanged;
            section.ParentTableView = null;
        }

        generatedSections.Clear();

        if (ItemsSource == null || ItemTemplate == null)
        {
            RenderSections();
            return;
        }

        foreach (var item in ItemsSource)
        {
            var template = ItemTemplate;
            if (template is DataTemplateSelector selector)
                template = selector.SelectTemplate(item, null);

            if (template.CreateContent() is TvTableSection section)
            {
                section.BindingContext = item;
                section.ParentTableView = this;
                section.SectionChanged += OnGeneratedSectionChanged;
                generatedSections.Add(section);
            }
        }

        RenderSections();
    }

    void OnGeneratedSectionChanged(object? sender, EventArgs e)
    {
        RenderSections();
    }



    internal void RaiseItemDropped(TvTableSection section, CellBase cell, int fromIndex, int toIndex)
    {
        var args = new ItemDroppedEventArgs(section, cell, fromIndex, toIndex);
        ItemDropped?.Invoke(this, args);
        if (ItemDroppedCommand?.CanExecute(args) == true)
            ItemDroppedCommand.Execute(args);
    }



    internal void RaiseCellPropertyChanged(TvTableSection section, CellBase cell, string propertyName)
    {
        CellPropertyChanged?.Invoke(this, new CellPropertyChangedEventArgs(section, cell, propertyName));
    }



    async Task ScrollToTopAndResetAsync()
    {
        await this.ScrollToTopAsync();
        this.ScrollToTop = false;
    }


    async Task ScrollToBottomAndResetAsync()
    {
        await this.ScrollToBottomAsync();
        this.ScrollToBottom = false;
    }


    public Task ScrollToTopAsync(bool animated = true)
        => scrollView.ScrollToAsync(0, 0, animated);

    public Task ScrollToBottomAsync(bool animated = true)
        => scrollView.ScrollToAsync(0, rootLayout.Height, animated);

    public double VisibleContentHeight => scrollView.ContentSize.Height;


}

public class ItemDroppedEventArgs : EventArgs
{
    public TvTableSection Section { get; }
    public CellBase Cell { get; }

    /// <summary>Position the cell was dragged from, among the section's rendered rows.</summary>
    public int FromIndex { get; }

    /// <summary>Position the cell was dropped at, among the section's rendered rows.</summary>
    public int ToIndex { get; }

    /// <summary>
    /// The moved cell's binding context. This is the item to reorder when the section's rows
    /// come from ItemsSource/ItemTemplate - the control cannot reorder a templated section for
    /// you, because the order lives in your collection.
    /// </summary>
    public object? Item => Cell.BindingContext;

    public ItemDroppedEventArgs(TvTableSection section, CellBase cell, int fromIndex, int toIndex)
    {
        Section = section;
        Cell = cell;
        FromIndex = fromIndex;
        ToIndex = toIndex;
    }
}

public class CellPropertyChangedEventArgs : EventArgs
{
    public TvTableSection Section { get; }
    public CellBase Cell { get; }
    public string PropertyName { get; }

    public CellPropertyChangedEventArgs(TvTableSection section, CellBase cell, string propertyName)
    {
        Section = section;
        Cell = cell;
        PropertyName = propertyName;
    }
}