using Microsoft.Maui.Controls.Shapes;
using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Shell;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// Word's Navigation pane: a search box over three tabs — the heading tree, page thumbnails, and search
/// results.
/// </summary>
/// <remarks>
/// <para>
/// Fed, not smart: the editor supplies <see cref="Headings"/> (a flat document-order list the pane nests
/// with <see cref="OfficeHeadingTree"/>), marks the caret's heading with <see cref="CurrentHeadingId"/>,
/// answers <see cref="SearchRequested"/> by setting <see cref="SearchResults"/>, and navigates on
/// <see cref="HeadingSelected"/> / <see cref="ResultSelected"/>.
/// </para>
/// <para>
/// The row lists are rebuilt when their data changes — the one place the pane adds children after
/// layout, which the AppKit head may not paint until the next window resize.
/// </para>
/// </remarks>
public class OfficeNavigationPane : ContentView
{
    readonly Entry search;
    readonly Border headingsTab;
    readonly Border pagesTab;
    readonly Border resultsTab;
    readonly ScrollView headingsView;
    readonly VerticalStackLayout headingRows;
    readonly Label noHeadings;
    readonly ContentView pagesHost;
    readonly ScrollView resultsView;
    readonly Label resultCount;
    readonly VerticalStackLayout resultRows;
    readonly HashSet<string> collapsed = new(StringComparer.Ordinal);

    static BindableProperty Prop(string name, Type type, object? value, Action<OfficeNavigationPane> changed, BindingMode mode = BindingMode.OneWay)
        => BindableProperty.Create(name, type, typeof(OfficeNavigationPane), value, mode,
            propertyChanged: (b, _, _) => { if (((OfficeNavigationPane)b).resultRows is not null) changed((OfficeNavigationPane)b); });

    public static readonly BindableProperty HeadingsProperty = Prop(nameof(Headings), typeof(IEnumerable<OfficeHeading>), null, p => p.BuildHeadings());
    public static readonly BindableProperty CurrentHeadingIdProperty = Prop(nameof(CurrentHeadingId), typeof(string), null, p => p.BuildHeadings());
    public static readonly BindableProperty SearchResultsProperty = Prop(nameof(SearchResults), typeof(IEnumerable<OfficeSearchResult>), null, p => p.BuildResults());
    public static readonly BindableProperty SelectedTabProperty = Prop(nameof(SelectedTab), typeof(OfficeNavigationTab), OfficeNavigationTab.Headings, p => p.ApplyTab(), BindingMode.TwoWay);
    public static readonly BindableProperty ShowPagesTabProperty = Prop(nameof(ShowPagesTab), typeof(bool), false, p => p.ApplyTab());
    public static readonly BindableProperty PagesContentProperty = Prop(nameof(PagesContent), typeof(View), null, p => p.pagesHost.Content = p.PagesContent);
    public static readonly BindableProperty SearchTextProperty = Prop(nameof(SearchText), typeof(string), null, p => { if (p.search.Text != p.SearchText) p.search.Text = p.SearchText; }, BindingMode.TwoWay);


    public OfficeNavigationPane()
    {
        this.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerLow);

        var title = ShellChrome.Text("Navigation", 15, attributes: FontAttributes.Bold);
        var close = ShellChrome.IconButton(OfficeShellIcon.Close, "Close", this.Close, size: 14);
        var header = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, Padding = new Thickness(12, 8, 6, 4) };
        header.Add(title, 0);
        header.Add(close, 1);

        this.search = new Entry { Placeholder = "Search document", FontSize = 13, BackgroundColor = Colors.Transparent };
        this.search.SetDynamicResource(Entry.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
        this.search.TextChanged += (_, e) => this.SearchText = e.NewTextValue;
        this.search.Completed += (_, _) => this.Search(this.search.Text);
        var glyph = new OfficeShellIconView { Icon = OfficeShellIcon.Search, WidthRequest = 14, HeightRequest = 14, VerticalOptions = LayoutOptions.Center };
        var searchRow = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, Padding = new Thickness(8, 0) };
        searchRow.Add(this.search, 0);
        searchRow.Add(glyph, 1);
        var searchBox = new Border { Content = searchRow, StrokeThickness = 1, Margin = new Thickness(12, 4), StrokeShape = new RoundRectangle { CornerRadius = 4 } };
        searchBox.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Brush.Outline);
        searchBox.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.Surface);

        this.headingsTab = Tab("Headings", () => this.SelectedTab = OfficeNavigationTab.Headings);
        this.pagesTab = Tab("Pages", () => this.SelectedTab = OfficeNavigationTab.Pages);
        this.resultsTab = Tab("Results", () => this.SelectedTab = OfficeNavigationTab.Results);
        var tabs = new HorizontalStackLayout { Spacing = 4, Padding = new Thickness(10, 4) };
        tabs.Children.Add(this.headingsTab);
        tabs.Children.Add(this.pagesTab);
        tabs.Children.Add(this.resultsTab);

        this.headingRows = new VerticalStackLayout { Spacing = 0 };
        this.noHeadings = ShellChrome.Text("Create an interactive outline of your document. Add headings to it and they appear here.", 12, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.noHeadings.LineBreakMode = LineBreakMode.WordWrap;
        this.noHeadings.Margin = new Thickness(12, 8);
        var headingStack = new VerticalStackLayout();
        headingStack.Children.Add(this.noHeadings);
        headingStack.Children.Add(this.headingRows);
        this.headingsView = new ScrollView { Content = headingStack };

        this.pagesHost = new ContentView();

        this.resultCount = ShellChrome.Text(OfficeHeadingTree.ResultCount(0), 12, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.resultCount.Margin = new Thickness(12, 6);
        this.resultRows = new VerticalStackLayout { Spacing = 0 };
        var resultStack = new VerticalStackLayout();
        resultStack.Children.Add(this.resultCount);
        resultStack.Children.Add(this.resultRows);
        this.resultsView = new ScrollView { Content = resultStack };

        var body = new Grid();
        body.Children.Add(this.headingsView);
        body.Children.Add(this.pagesHost);
        body.Children.Add(this.resultsView);

        var stack = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) } };
        stack.Add(header, 0, 0);
        stack.Add(searchBox, 0, 1);
        stack.Add(tabs, 0, 2);
        stack.Add(body, 0, 3);

        var root = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        root.Add(stack, 0);
        root.Add(ShellChrome.Rule(vertical: true), 1);
        this.Content = root;

        this.ApplyTab();
        this.BuildHeadings();
        this.BuildResults();
    }


    /// <summary>The document's headings in document order. Nested by level.</summary>
    public IEnumerable<OfficeHeading>? Headings { get => (IEnumerable<OfficeHeading>?)this.GetValue(HeadingsProperty); set => this.SetValue(HeadingsProperty, value); }

    /// <summary>The heading the caret is under, highlighted.</summary>
    public string? CurrentHeadingId { get => (string?)this.GetValue(CurrentHeadingIdProperty); set => this.SetValue(CurrentHeadingIdProperty, value); }

    /// <summary>What the Results tab lists — set it in answer to <see cref="SearchRequested"/>.</summary>
    public IEnumerable<OfficeSearchResult>? SearchResults { get => (IEnumerable<OfficeSearchResult>?)this.GetValue(SearchResultsProperty); set => this.SetValue(SearchResultsProperty, value); }

    public OfficeNavigationTab SelectedTab { get => (OfficeNavigationTab)this.GetValue(SelectedTabProperty); set => this.SetValue(SelectedTabProperty, value); }

    public bool ShowPagesTab { get => (bool)this.GetValue(ShowPagesTabProperty); set => this.SetValue(ShowPagesTabProperty, value); }

    /// <summary>The Pages tab's body — thumbnails the editor renders.</summary>
    public View? PagesContent { get => (View?)this.GetValue(PagesContentProperty); set => this.SetValue(PagesContentProperty, value); }

    /// <summary>The search box's text. Two-way.</summary>
    public string? SearchText { get => (string?)this.GetValue(SearchTextProperty); set => this.SetValue(SearchTextProperty, value); }

    public event EventHandler<OfficeHeading>? HeadingSelected;
    public event EventHandler<OfficeSearchResult>? ResultSelected;

    /// <summary>Enter in the search box. Answer by setting <see cref="SearchResults"/>.</summary>
    public event EventHandler<string>? SearchRequested;

    public event EventHandler? CloseRequested;


    /// <summary>Runs a search as Enter would, and shows the Results tab.</summary>
    public void Search(string? text)
    {
        this.SearchText = text;
        this.SelectedTab = OfficeNavigationTab.Results;
        this.SearchRequested?.Invoke(this, text ?? string.Empty);
    }

    public void SelectHeading(OfficeHeading heading) => this.HeadingSelected?.Invoke(this, heading);

    public void SelectResult(OfficeSearchResult result) => this.ResultSelected?.Invoke(this, result);

    /// <summary>Collapses or expands a heading's children.</summary>
    public void ToggleHeading(string id)
    {
        if (!this.collapsed.Remove(id))
            this.collapsed.Add(id);

        this.BuildHeadings();
    }

    public void Close()
    {
        this.CloseRequested?.Invoke(this, EventArgs.Empty);
        ShellChrome.Ancestor<OfficeShell>(this)?.ClosePane(this);
    }


    static Border Tab(string text, Action action)
    {
        var label = ShellChrome.Text(text, 13);
        var border = new Border { Content = label, Padding = new Thickness(10, 4), StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 4 } };
        ShellChrome.OnTap(border, action);
        return border;
    }


    void ApplyTab()
    {
        if (!this.ShowPagesTab && this.SelectedTab == OfficeNavigationTab.Pages)
        {
            this.SelectedTab = OfficeNavigationTab.Headings;
            return;
        }

        this.pagesTab.IsVisible = this.ShowPagesTab;
        this.headingsView.IsVisible = this.SelectedTab == OfficeNavigationTab.Headings;
        this.pagesHost.IsVisible = this.SelectedTab == OfficeNavigationTab.Pages;
        this.resultsView.IsVisible = this.SelectedTab == OfficeNavigationTab.Results;

        foreach (var (tab, which) in new[] { (this.headingsTab, OfficeNavigationTab.Headings), (this.pagesTab, OfficeNavigationTab.Pages), (this.resultsTab, OfficeNavigationTab.Results) })
        {
            if (which == this.SelectedTab)
                tab.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.SecondaryContainer);
            else
            {
                tab.RemoveDynamicResource(BackgroundColorProperty);
                tab.BackgroundColor = Colors.Transparent;
            }
        }
    }


    void BuildHeadings()
    {
        this.headingRows.Children.Clear();

        var roots = OfficeHeadingTree.Build(this.Headings);
        this.noHeadings.IsVisible = roots.Count == 0;

        foreach (var node in OfficeHeadingTree.Flatten(roots, this.collapsed))
        {
            var heading = node.Heading;
            var chevron = node.HasChildren
                ? ShellChrome.IconButton(this.collapsed.Contains(heading.Id) ? OfficeShellIcon.Collapse : OfficeShellIcon.Expand, "Expand or collapse", () => this.ToggleHeading(heading.Id), size: 10)
                : (View)new BoxView { WidthRequest = 22, Color = Colors.Transparent };
            chevron.Margin = new Thickness(node.Depth * 14, 0, 0, 0);
            if (chevron is Border b)
                b.Padding = new Thickness(6, 4);

            var text = ShellChrome.Text(heading.Text, 13, attributes: node.Depth == 0 ? FontAttributes.Bold : FontAttributes.None);
            text.LineBreakMode = LineBreakMode.TailTruncation;

            var grid = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, Padding = new Thickness(6, 3) };
            grid.Add(chevron, 0);
            grid.Add(text, 1);

            var row = new Border { Content = grid, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 4 }, Margin = new Thickness(6, 0) };
            if (heading.Id == this.CurrentHeadingId)
                row.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.SecondaryContainer);
            else
                row.BackgroundColor = Colors.Transparent;

            ShellChrome.OnTap(row, () => this.SelectHeading(heading));
            this.headingRows.Children.Add(row);
        }
    }


    void BuildResults()
    {
        this.resultRows.Children.Clear();

        var results = this.SearchResults?.ToList() ?? [];
        this.resultCount.Text = OfficeHeadingTree.ResultCount(results.Count);

        foreach (var result in results)
        {
            var r = result;
            var formatted = new FormattedString();
            formatted.Spans.Add(new Span { Text = result.Before });
            formatted.Spans.Add(new Span { Text = result.Match, FontAttributes = FontAttributes.Bold });
            formatted.Spans.Add(new Span { Text = result.After });

            var label = new Label { FormattedText = formatted, FontSize = 12, LineBreakMode = LineBreakMode.WordWrap, MaxLines = 3 };
            label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);

            var row = new Border { Content = label, Padding = new Thickness(12, 6), StrokeThickness = 0, BackgroundColor = Colors.Transparent };
            ShellChrome.OnTap(row, () => this.SelectResult(r));
            this.resultRows.Children.Add(row);
            this.resultRows.Children.Add(ShellChrome.Rule());
        }
    }
}
