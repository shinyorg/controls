using System.Collections;
using System.Collections.Specialized;
using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Ribbons;

/// <summary>
/// A gallery of previews inside a ribbon group — Word's Styles, Excel's Cell Styles, PowerPoint's
/// Themes — that steps a row at a time in place and drops the whole set as a grid.
/// </summary>
/// <example>
/// <code language="xaml">
/// &lt;shiny:RibbonGallery ItemsSource="{Binding Styles}" SelectedItem="{Binding CurrentStyle}" Columns="5"&gt;
///     &lt;shiny:RibbonGallery.ItemTemplate&gt;
///         &lt;DataTemplate&gt;&lt;Label Text="{Binding Name}" /&gt;&lt;/DataTemplate&gt;
///     &lt;/shiny:RibbonGallery.ItemTemplate&gt;
/// &lt;/shiny:RibbonGallery&gt;
/// </code>
/// </example>
/// <remarks>
/// <para>
/// A <see cref="RibbonContentItem"/> that builds its own content, so the ribbon places it like any
/// hosted view and needs no new drawing path.
/// </para>
/// <para>
/// Every entry's cell is built when the items arrive and the strip only toggles which are visible and
/// where. Scrolling never adds a child, which is what keeps it working on the AppKit head — a child
/// added to a laid-out page there never gets a native view.
/// </para>
/// </remarks>
public class RibbonGallery : RibbonContentItem
{
    readonly Grid strip;
    readonly Border upArrow;
    readonly Border downArrow;
    readonly Border moreArrow;
    readonly List<(object Item, Border Cell)> cells = [];
    INotifyCollectionChanged? observed;
    int first;

    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource), typeof(IEnumerable), typeof(RibbonGallery), null,
        propertyChanged: (b, _, _) => ((RibbonGallery)b).OnItemsSourceChanged());

    public static readonly BindableProperty ItemTemplateProperty = BindableProperty.Create(
        nameof(ItemTemplate), typeof(DataTemplate), typeof(RibbonGallery), null,
        propertyChanged: (b, _, _) => ((RibbonGallery)b).BuildCells());

    public static readonly BindableProperty SelectedItemProperty = BindableProperty.Create(
        nameof(SelectedItem), typeof(object), typeof(RibbonGallery), null, BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((RibbonGallery)b).OnSelectedChanged());

    public static readonly BindableProperty ColumnsProperty = BindableProperty.Create(
        nameof(Columns), typeof(int), typeof(RibbonGallery), 5,
        propertyChanged: (b, _, _) => ((RibbonGallery)b).Layout());

    public static readonly BindableProperty RowsProperty = BindableProperty.Create(
        nameof(Rows), typeof(int), typeof(RibbonGallery), 1,
        propertyChanged: (b, _, _) => ((RibbonGallery)b).Layout());

    public static readonly BindableProperty ExpandedColumnsProperty = BindableProperty.Create(
        nameof(ExpandedColumns), typeof(int), typeof(RibbonGallery), 0);

    public static readonly BindableProperty ItemWidthProperty = BindableProperty.Create(
        nameof(ItemWidth), typeof(double), typeof(RibbonGallery), 72d,
        propertyChanged: (b, _, _) => ((RibbonGallery)b).BuildCells());

    public static readonly BindableProperty ItemHeightProperty = BindableProperty.Create(
        nameof(ItemHeight), typeof(double), typeof(RibbonGallery), 58d,
        propertyChanged: (b, _, _) => ((RibbonGallery)b).BuildCells());

    public static readonly BindableProperty SelectionCommandProperty = BindableProperty.Create(
        nameof(SelectionCommand), typeof(ICommand), typeof(RibbonGallery));

    public static readonly BindableProperty PanelFooterTemplateProperty = BindableProperty.Create(
        nameof(PanelFooterTemplate), typeof(DataTemplate), typeof(RibbonGallery));


    public RibbonGallery()
    {
        this.strip = new Grid { RowSpacing = 2, ColumnSpacing = 2, Padding = 2 };

        this.upArrow = Arrow(new PointCollection { new(0, 5), new(5, 0), new(10, 5) }, "Previous row", () => this.Scroll(-1));
        this.downArrow = Arrow(new PointCollection { new(0, 0), new(5, 5), new(10, 0) }, "Next row", () => this.Scroll(1));
        this.moreArrow = Arrow(new PointCollection { new(0, 2), new(5, 7), new(10, 2) }, "More", this.OpenPanel);

        var arrows = new Grid
        {
            RowDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) },
            WidthRequest = 16
        };
        arrows.Add(this.upArrow, 0, 0);
        arrows.Add(this.downArrow, 0, 1);
        arrows.Add(this.moreArrow, 0, 2);

        var rule = new BoxView { WidthRequest = 1 };
        rule.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);

        var layout = new Grid
        {
            ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) }
        };
        layout.Add(this.strip, 0);
        layout.Add(rule, 1);
        layout.Add(arrows, 2);

        var frame = new Border
        {
            Content = layout,
            StrokeThickness = 1,
            Padding = 0,
            VerticalOptions = LayoutOptions.Fill,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerExtraSmallRadius)
        };
        frame.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Brush.OutlineVariant);
        frame.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);

        this.Content = frame;
    }


    /// <summary>The entries.</summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)this.GetValue(ItemsSourceProperty);
        set => this.SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Draws one entry. Its binding context is the entry. Without it the entry's text is shown.</summary>
    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)this.GetValue(ItemTemplateProperty);
        set => this.SetValue(ItemTemplateProperty, value);
    }

    /// <summary>The highlighted entry — the style under the caret. Two-way.</summary>
    public object? SelectedItem
    {
        get => this.GetValue(SelectedItemProperty);
        set => this.SetValue(SelectedItemProperty, value);
    }

    /// <summary>How many entries the in-ribbon strip shows across. Default 5.</summary>
    public int Columns
    {
        get => (int)this.GetValue(ColumnsProperty);
        set => this.SetValue(ColumnsProperty, value);
    }

    /// <summary>How many rows the strip shows. Default 1.</summary>
    public int Rows
    {
        get => (int)this.GetValue(RowsProperty);
        set => this.SetValue(RowsProperty, value);
    }

    /// <summary>Columns in the expanded grid. Zero (the default) uses <see cref="Columns"/>.</summary>
    public int ExpandedColumns
    {
        get => (int)this.GetValue(ExpandedColumnsProperty);
        set => this.SetValue(ExpandedColumnsProperty, value);
    }

    public double ItemWidth
    {
        get => (double)this.GetValue(ItemWidthProperty);
        set => this.SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)this.GetValue(ItemHeightProperty);
        set => this.SetValue(ItemHeightProperty, value);
    }

    /// <summary>Runs with the picked entry as its parameter.</summary>
    public ICommand? SelectionCommand
    {
        get => (ICommand?)this.GetValue(SelectionCommandProperty);
        set => this.SetValue(SelectionCommandProperty, value);
    }

    /// <summary>Content under the expanded grid — "Clear Formatting", "Apply Styles…". Built each time it opens.</summary>
    public DataTemplate? PanelFooterTemplate
    {
        get => (DataTemplate?)this.GetValue(PanelFooterTemplateProperty);
        set => this.SetValue(PanelFooterTemplateProperty, value);
    }

    /// <summary>
    /// An entry's name — the text drawn without a template, its tooltip, and what the command search
    /// finds it by. Defaults to <c>ToString()</c>.
    /// </summary>
    public Func<object, string?>? ItemText { get; set; }

    /// <summary>Raised when an entry is picked, whether or not it was already selected.</summary>
    public event EventHandler<RibbonGalleryEventArgs>? ItemSelected;


    /// <summary>The first entry the strip shows.</summary>
    public int FirstVisible => this.first;

    int PageSize => Math.Max(1, this.Columns) * Math.Max(1, this.Rows);

    internal bool CanScrollBack => this.first > 0;

    internal bool CanScrollForward => this.first + this.PageSize < this.cells.Count;


    /// <summary>Picks an entry as a tap would. The seam tests and the command search press through.</summary>
    public void Pick(object item)
    {
        if (!this.IsEnabled)
            return;

        this.FindRibbon()?.CloseMenu();
        this.SelectedItem = item;

        if (this.SelectionCommand?.CanExecute(item) == true)
            this.SelectionCommand.Execute(item);

        this.ItemSelected?.Invoke(this, new RibbonGalleryEventArgs(item));
    }


    /// <summary>Moves the strip a row back (-1) or forward (1).</summary>
    public void Scroll(int direction)
    {
        var columns = Math.Max(1, this.Columns);
        this.first = Math.Clamp(this.first + (direction * columns), 0, MaxFirst(this.PageSize, columns, this.cells.Count));
        this.Layout();
    }


    /// <summary>The entries with their names, for the command index.</summary>
    internal IEnumerable<(string Text, object Item)> Choices()
    {
        foreach (var (item, _) in this.cells)
        {
            if (this.TextOf(item) is { Length: > 0 } text)
                yield return (text, item);
        }
    }


    /// <summary>The largest first index — the start of the last row that still fills the strip.</summary>
    internal static int MaxFirst(int pageSize, int columns, int count)
    {
        if (count <= pageSize)
            return 0;

        var lastRowStart = ((count - 1) / columns) * columns;
        var rows = pageSize / columns;
        return Math.Max(0, lastRowStart - ((rows - 1) * columns));
    }


    string? TextOf(object item) => this.ItemText?.Invoke(item) ?? item.ToString();


    void OnItemsSourceChanged()
    {
        if (this.observed is not null)
            this.observed.CollectionChanged -= this.OnCollectionChanged;

        this.observed = this.ItemsSource as INotifyCollectionChanged;
        if (this.observed is not null)
            this.observed.CollectionChanged += this.OnCollectionChanged;

        this.first = 0;
        this.BuildCells();
    }


    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => this.BuildCells();


    void BuildCells()
    {
        // Bindable callbacks can run from an implicit style before the constructor body has.
        if (this.strip is null)
            return;

        foreach (var (_, cell) in this.cells)
            this.strip.Children.Remove(cell);

        this.cells.Clear();

        if (this.ItemsSource is { } source)
        {
            foreach (var item in source)
            {
                if (item is not null)
                    this.cells.Add((item, this.BuildCell(item)));
            }
        }

        foreach (var (_, cell) in this.cells)
            this.strip.Children.Add(cell);

        this.OnSelectedChanged();
    }


    Border BuildCell(object item)
    {
        View content;
        if (this.ItemTemplate?.CreateContent() is View templated)
        {
            templated.BindingContext = item;
            content = templated;
        }
        else
        {
            content = new Label
            {
                Text = this.TextOf(item),
                LineBreakMode = LineBreakMode.TailTruncation,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);
        }

        var cell = new Border
        {
            Content = content,
            WidthRequest = this.ItemWidth,
            HeightRequest = this.ItemHeight,
            Padding = new Thickness(2),
            StrokeThickness = 1,
            Stroke = Colors.Transparent,
            BackgroundColor = Colors.Transparent,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerExtraSmallRadius)
        };

        SemanticProperties.SetDescription(cell, this.TextOf(item));
        cell.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => this.Pick(item)) });
        return cell;
    }


    void OnSelectedChanged()
    {
        var selected = this.SelectedItem;
        var index = -1;

        for (var i = 0; i < this.cells.Count; i++)
        {
            var on = selected is not null && Equals(this.cells[i].Item, selected);
            if (on)
                index = i;

            Paint(this.cells[i].Cell, on);
        }

        if (index >= 0)
        {
            var columns = Math.Max(1, this.Columns);
            if (index < this.first)
                this.first = (index / columns) * columns;
            else if (index >= this.first + this.PageSize)
                this.first = Math.Min(((index / columns) * columns) - this.PageSize + columns, MaxFirst(this.PageSize, columns, this.cells.Count));
        }

        this.Layout();
    }


    static void Paint(Border cell, bool selected)
    {
        if (selected)
        {
            cell.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Brush.Primary);
            cell.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SecondaryContainer);
        }
        else
        {
            cell.RemoveDynamicResource(Border.StrokeProperty);
            cell.RemoveDynamicResource(VisualElement.BackgroundColorProperty);
            cell.Stroke = Colors.Transparent;
            cell.BackgroundColor = Colors.Transparent;
        }
    }


    /// <summary>Places the visible window of cells and hides the rest. Never adds or removes a child.</summary>
    void Layout()
    {
        if (this.strip is null)
            return;

        var columns = Math.Max(1, this.Columns);
        var rows = Math.Max(1, this.Rows);

        while (this.strip.ColumnDefinitions.Count < columns)
            this.strip.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        while (this.strip.ColumnDefinitions.Count > columns)
            this.strip.ColumnDefinitions.RemoveAt(this.strip.ColumnDefinitions.Count - 1);
        while (this.strip.RowDefinitions.Count < rows)
            this.strip.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        while (this.strip.RowDefinitions.Count > rows)
            this.strip.RowDefinitions.RemoveAt(this.strip.RowDefinitions.Count - 1);

        this.first = Math.Clamp(this.first, 0, MaxFirst(this.PageSize, columns, this.cells.Count));

        for (var i = 0; i < this.cells.Count; i++)
        {
            var cell = this.cells[i].Cell;
            var offset = i - this.first;
            var visible = offset >= 0 && offset < this.PageSize;

            cell.IsVisible = visible;
            if (visible)
            {
                Grid.SetColumn(cell, offset % columns);
                Grid.SetRow(cell, offset / columns);
            }
        }

        SetArrowEnabled(this.upArrow, this.CanScrollBack);
        SetArrowEnabled(this.downArrow, this.CanScrollForward);
    }


    static void SetArrowEnabled(Border arrow, bool enabled) => arrow.Opacity = enabled ? 1 : 0.38;


    void OpenPanel()
    {
        if (!this.IsEnabled || this.FindRibbon() is not { } ribbon)
            return;

        var columns = this.ExpandedColumns > 0 ? this.ExpandedColumns : Math.Max(1, this.Columns);
        var grid = new Grid { RowSpacing = 2, ColumnSpacing = 2 };
        for (var c = 0; c < columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var index = 0;
        foreach (var (item, _) in this.cells)
        {
            if (index % columns == 0)
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var cell = this.BuildCell(item);
            Paint(cell, Equals(item, this.SelectedItem));
            grid.Add(cell, index % columns, index / columns);
            index++;
        }

        var body = new VerticalStackLayout { Spacing = 4 };
        body.Children.Add(new ScrollView { Content = grid, MaximumHeightRequest = 420 });

        if (this.PanelFooterTemplate?.CreateContent() is View footer)
        {
            var rule = new BoxView { HeightRequest = 1 };
            rule.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);
            body.Children.Add(rule);
            body.Children.Add(footer);
        }

        ribbon.Present(this.Content, null, body);
    }


    Ribbon? FindRibbon()
    {
        Element? node = this.Content;
        while (node is not null)
        {
            if (node is Ribbon ribbon)
                return ribbon;

            node = node.Parent;
        }

        return null;
    }


    static Border Arrow(PointCollection points, string hint, Action action)
    {
        var glyph = new Polyline
        {
            Points = points,
            StrokeThickness = 1.4,
            StrokeLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            WidthRequest = 10,
            HeightRequest = 8,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        glyph.SetDynamicResource(Shape.StrokeProperty, ShinyThemeKeys.Brush.OnSurfaceVariant);

        var border = new Border
        {
            Content = glyph,
            StrokeThickness = 0,
            Padding = new Thickness(2, 0),
            BackgroundColor = Colors.Transparent
        };

        SemanticProperties.SetDescription(border, hint);
        border.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(action) });
        return border;
    }
}


/// <summary>The entry a <see cref="RibbonGallery"/> had picked.</summary>
public class RibbonGalleryEventArgs(object item) : EventArgs
{
    public object Item { get; } = item;
}
