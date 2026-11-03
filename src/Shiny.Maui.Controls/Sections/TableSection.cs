using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Maui.Controls.Cells;
using TvTableView = Shiny.Maui.Controls.TableView;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Sections;

[ContentProperty(nameof(Cells))]
public class TableSection : BindableObject
{
    readonly ObservableCollection<CellBase> cells = new();
    IDisposable? itemsSourceSubscription;
    readonly List<CellBase> generatedCells = new();
    bool regeneratePending;

    public TableSection()
    {
        cells.CollectionChanged += OnCellsCollectionChanged;
    

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(TableSection));
    }

    public TableSection(string title) : this()
    {
        Title = title;
    }


    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(TableSection), string.Empty,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableSection), () =>
            {
                ((TableSection)b).RaiseSectionChanged();
            }));

    public static readonly BindableProperty FooterTextProperty = BindableProperty.Create(
        nameof(FooterText), typeof(string), typeof(TableSection), string.Empty,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableSection), () =>
            {
                ((TableSection)b).RaiseSectionChanged();
            }));

    public static readonly BindableProperty HeaderViewProperty = BindableProperty.Create(
        nameof(HeaderView), typeof(View), typeof(TableSection), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableSection), () =>
            {
                ((TableSection)b).RaiseSectionChanged();
            }));

    public static readonly BindableProperty FooterViewProperty = BindableProperty.Create(
        nameof(FooterView), typeof(View), typeof(TableSection), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableSection), () =>
            {
                ((TableSection)b).RaiseSectionChanged();
            }));

    public static readonly BindableProperty IsVisibleProperty = BindableProperty.Create(
        nameof(IsVisible), typeof(bool), typeof(TableSection), true,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableSection), () =>
            {
                ((TableSection)b).RaiseSectionChanged();
            }));

    public static readonly BindableProperty FooterVisibleProperty = BindableProperty.Create(
        nameof(FooterVisible), typeof(bool), typeof(TableSection), true,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableSection), () =>
            {
                ((TableSection)b).RaiseSectionChanged();
            }));

    public static readonly BindableProperty HeaderBackgroundColorProperty = BindableProperty.Create(
        nameof(HeaderBackgroundColor), typeof(Color), typeof(TableSection), null);

    public static readonly BindableProperty HeaderTextColorProperty = BindableProperty.Create(
        nameof(HeaderTextColor), typeof(Color), typeof(TableSection), null);

    public static readonly BindableProperty HeaderFontSizeProperty = BindableProperty.Create(
        nameof(HeaderFontSize), typeof(double), typeof(TableSection), -1d);

    public static readonly BindableProperty HeaderFontFamilyProperty = BindableProperty.Create(
        nameof(HeaderFontFamily), typeof(string), typeof(TableSection), null);

    public static readonly BindableProperty HeaderFontAttributesProperty = BindableProperty.Create(
        nameof(HeaderFontAttributes), typeof(FontAttributes?), typeof(TableSection), null);

    public static readonly BindableProperty HeaderHeightProperty = BindableProperty.Create(
        nameof(HeaderHeight), typeof(double), typeof(TableSection), -1d);

    public static readonly BindableProperty FooterTextColorProperty = BindableProperty.Create(
        nameof(FooterTextColor), typeof(Color), typeof(TableSection), null);

    public static readonly BindableProperty FooterFontSizeProperty = BindableProperty.Create(
        nameof(FooterFontSize), typeof(double), typeof(TableSection), -1d);

    public static readonly BindableProperty FooterFontFamilyProperty = BindableProperty.Create(
        nameof(FooterFontFamily), typeof(string), typeof(TableSection), null);

    public static readonly BindableProperty FooterFontAttributesProperty = BindableProperty.Create(
        nameof(FooterFontAttributes), typeof(FontAttributes?), typeof(TableSection), null);

    public static readonly BindableProperty FooterBackgroundColorProperty = BindableProperty.Create(
        nameof(FooterBackgroundColor), typeof(Color), typeof(TableSection), null);

    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource), typeof(IEnumerable), typeof(TableSection), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableSection), () => OnItemsSourceChanged(b, o, n)));

    public static readonly BindableProperty ItemTemplateProperty = BindableProperty.Create(
        nameof(ItemTemplate), typeof(DataTemplate), typeof(TableSection), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableSection), () =>
            {
                ((TableSection)b).RegenerateTemplatedCells();
            }));

    public static readonly BindableProperty TemplateStartIndexProperty = BindableProperty.Create(
        nameof(TemplateStartIndex), typeof(int), typeof(TableSection), 0,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableSection), () =>
            {
                ((TableSection)b).RegenerateTemplatedCells();
            }));

    public static readonly BindableProperty UseDragSortProperty = BindableProperty.Create(
        nameof(UseDragSort), typeof(bool), typeof(TableSection), false,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TableSection), () =>
            {
                ((TableSection)b).RaiseSectionChanged();
            }));



    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string FooterText
    {
        get => (string)GetValue(FooterTextProperty);
        set => SetValue(FooterTextProperty, value);
    }

    public View? HeaderView
    {
        get => (View?)GetValue(HeaderViewProperty);
        set => SetValue(HeaderViewProperty, value);
    }

    public View? FooterView
    {
        get => (View?)GetValue(FooterViewProperty);
        set => SetValue(FooterViewProperty, value);
    }

    public bool IsVisible
    {
        get => (bool)GetValue(IsVisibleProperty);
        set => SetValue(IsVisibleProperty, value);
    }

    public bool FooterVisible
    {
        get => (bool)GetValue(FooterVisibleProperty);
        set => SetValue(FooterVisibleProperty, value);
    }

    public Color? HeaderBackgroundColor
    {
        get => (Color?)GetValue(HeaderBackgroundColorProperty);
        set => SetValue(HeaderBackgroundColorProperty, value);
    }

    public Color? HeaderTextColor
    {
        get => (Color?)GetValue(HeaderTextColorProperty);
        set => SetValue(HeaderTextColorProperty, value);
    }

    public double HeaderFontSize
    {
        get => (double)GetValue(HeaderFontSizeProperty);
        set => SetValue(HeaderFontSizeProperty, value);
    }

    public string? HeaderFontFamily
    {
        get => (string?)GetValue(HeaderFontFamilyProperty);
        set => SetValue(HeaderFontFamilyProperty, value);
    }

    public FontAttributes? HeaderFontAttributes
    {
        get => (FontAttributes?)GetValue(HeaderFontAttributesProperty);
        set => SetValue(HeaderFontAttributesProperty, value);
    }

    public double HeaderHeight
    {
        get => (double)GetValue(HeaderHeightProperty);
        set => SetValue(HeaderHeightProperty, value);
    }

    public Color? FooterTextColor
    {
        get => (Color?)GetValue(FooterTextColorProperty);
        set => SetValue(FooterTextColorProperty, value);
    }

    public double FooterFontSize
    {
        get => (double)GetValue(FooterFontSizeProperty);
        set => SetValue(FooterFontSizeProperty, value);
    }

    public string? FooterFontFamily
    {
        get => (string?)GetValue(FooterFontFamilyProperty);
        set => SetValue(FooterFontFamilyProperty, value);
    }

    public FontAttributes? FooterFontAttributes
    {
        get => (FontAttributes?)GetValue(FooterFontAttributesProperty);
        set => SetValue(FooterFontAttributesProperty, value);
    }

    public Color? FooterBackgroundColor
    {
        get => (Color?)GetValue(FooterBackgroundColorProperty);
        set => SetValue(FooterBackgroundColorProperty, value);
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

    public bool UseDragSort
    {
        get => (bool)GetValue(UseDragSortProperty);
        set => SetValue(UseDragSortProperty, value);
    }

    public ObservableCollection<CellBase> Cells => cells;

    internal TvTableView? ParentTableView { get; set; }



    public IReadOnlyList<CellBase> GetVisibleCells()
    {
        var all = GetAllCells();
        // Filter by cell IsVisible
        var visible = new List<CellBase>(all.Count);
        foreach (var cell in all)
        {
            if (cell.IsVisible)
                visible.Add(cell);
        }
        return visible;
    }

    internal IReadOnlyList<CellBase> GetAllCells()
    {
        if (generatedCells.Count == 0)
            return cells;

        var result = new List<CellBase>();
        var insertIndex = Math.Min(TemplateStartIndex, cells.Count);

        for (var i = 0; i < insertIndex; i++)
            result.Add(cells[i]);

        result.AddRange(generatedCells);

        for (var i = insertIndex; i < cells.Count; i++)
            result.Add(cells[i]);

        return result;
    }

    void OnCellsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (CellBase cell in e.OldItems)
                if (cell.OwnerSection == this)
                    cell.OwnerSection = null;

        // Reset (Clear) carries no old items; whatever is left is what the section owns.
        foreach (var cell in cells)
            cell.OwnerSection = this;

        RaiseSectionChanged();
    }

    /// <summary>
    /// A cell of this section was shown or hidden. Only visible cells are rendered (so separators sit
    /// between the rows that draw), which meant a cell hidden when the table rendered never appeared
    /// when its IsVisible later turned true - a "Sign out" row that came and went with the signed-in
    /// state stayed missing until something else re-rendered.
    /// </summary>
    /// <remarks>
    /// Re-renders (coalesced, after the write) only for a cell that turns visible and isn't in the table
    /// yet. A rendered cell hides and shows itself, as it always did: a full re-render re-attaches every
    /// cell, which would take focus - and on iOS/Android the keyboard - from an EntryCell being typed in
    /// whenever a row near it (a validation message, say) came and went.
    /// </remarks>
    internal void OnCellVisibilityChanged(CellBase cell)
    {
        // Rendering detaches and re-attaches cells; a bound IsVisible can flicker while it does.
        if (ParentTableView is not { IsRendering: false } table)
            return;
        if (!cell.IsVisible || !IsVisible || table.IsRendered(cell))
            return;
        RaiseSectionChanged();
    }



    static void OnItemsSourceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var section = (TableSection)bindable;

        // Weak: ItemsSource is usually a view model's collection, which outlives the page.
        section.itemsSourceSubscription?.Dispose();
        section.itemsSourceSubscription = WeakEventSubscription.CollectionChanged(newValue, section.OnItemsSourceCollectionChanged);

        section.RegenerateTemplatedCells();
    }

    // Coalesced: filling a collection one item at a time used to rebuild every cell per add - n(n+1)/2
    // cells for n items. With no dispatcher (a bare unit test), it regenerates inline.
    void OnItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (this.Dispatcher is not { } dispatcher)
        {
            RegenerateTemplatedCells();
            return;
        }

        if (regeneratePending)
            return;

        regeneratePending = true;
        dispatcher.Dispatch(() =>
        {
            if (regeneratePending)
                RegenerateTemplatedCells();
        });
    }

    internal void RegenerateTemplatedCells()
    {
        regeneratePending = false;
        generatedCells.Clear();

        if (ItemsSource == null || ItemTemplate == null)
        {
            RaiseSectionChanged();
            return;
        }

        foreach (var item in ItemsSource)
        {
            var template = ItemTemplate;
            if (template is DataTemplateSelector selector)
                template = selector.SelectTemplate(item, null);

            if (template.CreateContent() is CellBase cell)
            {
                cell.BindingContext = item;
                cell.OwnerSection = this;
                generatedCells.Add(cell);
            }
        }

        RaiseSectionChanged();
    }



    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        foreach (var cell in cells)
            SetInheritedBindingContext(cell, BindingContext);
        foreach (var cell in generatedCells)
            SetInheritedBindingContext(cell, BindingContext);
    }



    public event EventHandler? SectionChanged;

    internal void RaiseSectionChanged()
    {
        SectionChanged?.Invoke(this, EventArgs.Empty);
    }

}