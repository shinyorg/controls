namespace Shiny.Maui.Controls.Markdown;

public class MarkdownView : ContentView
{
    readonly VerticalStackLayout layout = new();
    readonly List<string> blocks = [];
    ScrollView? scrollView;
    object? renderedThemeKey;

    public MarkdownView()
    {
        scrollView = new ScrollView { Content = layout };
        base.Content = scrollView;
    }

    public static readonly BindableProperty MarkdownProperty = BindableProperty.Create(
        nameof(Markdown),
        typeof(string),
        typeof(MarkdownView),
        string.Empty,
        propertyChanged: (b, _, _) => ((MarkdownView)b).Rebuild());

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public static readonly BindableProperty ThemeProperty = BindableProperty.Create(
        nameof(Theme),
        typeof(MarkdownTheme),
        typeof(MarkdownView),
        null,
        propertyChanged: (b, _, _) => ((MarkdownView)b).Rebuild());

    public MarkdownTheme? Theme
    {
        get => (MarkdownTheme?)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    /// <summary>
    /// When false the view has no scroller of its own and sizes to its content, so it can sit inside a
    /// page or list that scrolls (a chat bubble) without a second scroller on the same axis.
    /// </summary>
    public static readonly BindableProperty IsScrollEnabledProperty = BindableProperty.Create(
        nameof(IsScrollEnabled),
        typeof(bool),
        typeof(MarkdownView),
        true,
        propertyChanged: (b, _, n) => ((MarkdownView)b).UpdateScroller((bool)n));

    public bool IsScrollEnabled
    {
        get => (bool)GetValue(IsScrollEnabledProperty);
        set => SetValue(IsScrollEnabledProperty, value);
    }

    public event EventHandler<LinkTappedEventArgs>? LinkTapped;

    MarkdownTheme ResolveTheme() => Theme ?? (Application.Current?.RequestedTheme == AppTheme.Dark
        ? MarkdownTheme.Dark
        : MarkdownTheme.Light);

    void UpdateScroller(bool enabled)
    {
        if (enabled && scrollView is null)
        {
            base.Content = null;
            scrollView = new ScrollView { Content = layout };
            base.Content = scrollView;
        }
        else if (!enabled && scrollView is not null)
        {
            scrollView.Content = null;
            scrollView = null;
            base.Content = layout;
        }
    }

    void Rebuild()
    {
        var theme = ResolveTheme();
        // the built-in themes are new instances on every read, so key them by light/dark
        var themeKey = Theme ?? (object)(Application.Current?.RequestedTheme ?? AppTheme.Unspecified);
        if (!Equals(themeKey, renderedThemeKey))
        {
            // a theme change restyles every block, so nothing can be kept
            layout.Children.Clear();
            blocks.Clear();
            renderedThemeKey = themeKey;
        }

        // Only the blocks whose source changed are rebuilt, so a streamed reply that grows a token at a
        // time re-renders its last block rather than the whole document.
        MarkdownRenderer.RenderInto(layout, blocks, Markdown ?? string.Empty, theme, OnLinkTapped);
    }

    void OnLinkTapped(string url)
    {
        var args = new LinkTappedEventArgs(url);
        LinkTapped?.Invoke(this, args);

        if (!args.Handled)
            _ = Launcher.Default.OpenAsync(url);
    }
}

public class LinkTappedEventArgs : EventArgs
{
    public LinkTappedEventArgs(string url) => Url = url;
    public string Url { get; }
    public bool Handled { get; set; }
}
