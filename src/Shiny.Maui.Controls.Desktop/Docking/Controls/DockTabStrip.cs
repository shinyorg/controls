using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui;
using Keys = Shiny.Maui.Controls.Themes.ShinyThemeKeys;

namespace Shiny.Maui.Controls.Desktop.Docking;

/// <summary>
/// Tab strip rendered at the top of a <see cref="DockGroupView"/>.
/// Handles tab activation, close buttons, and originates tab drags.
/// </summary>
public class DockTabStrip : ContentView
{
    // Sizes shared by every tab. The strip's height is derived from these plus the fonts, never
    // from the ScrollView's own measurement - see UpdateStripHeight.
    internal const double TitleFontSize = 12;
    internal const double IconFontSize = 12;
    internal const double CloseFontSize = 13;
    internal static readonly Thickness TabPadding = new(12, 6, 6, 6);
    internal static readonly Thickness StripPadding = new(0, 0, 4, 0);

    readonly HorizontalStackLayout stack;
    readonly ScrollView scroller;
    readonly HorizontalStackLayout tools;
    readonly List<(DockTab Tab, Border View)> tabViews = new();
    readonly List<BoxView> accentBars = new();
    int activeIndex = -1;
    bool isFocused;
    DockArea? collapseDirection;
    bool showFloatButton;

    public event EventHandler<DockTab>? TabTapped;
    public event EventHandler<DockTab>? TabDoubleTapped;
    public event EventHandler<DockTab>? TabCloseTapped;
    public event EventHandler<(DockTab Tab, Border View, PanUpdatedEventArgs Pan)>? TabPan;

    /// <summary>Where on a tab the pointer went down (tab-relative), so a drag can start under it.</summary>
    public event EventHandler<(DockTab Tab, Border View, Point Position)>? TabPressed;
    public event EventHandler? CollapseTapped;
    public event EventHandler? FloatTapped;

    public DockTabStrip()
    {
        this.Tint(BackgroundColorProperty, Keys.Color.SurfaceContainerLow);
        stack = new HorizontalStackLayout { Spacing = 1, Padding = StripPadding };
        tools = new HorizontalStackLayout { Spacing = 1, Padding = new Thickness(4, 0), VerticalOptions = LayoutOptions.Center };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(new GridLength(1))
            }
        };
        scroller = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = stack
        };
        // the strip's bottom rule; the active tab paints over it so it reads as part of its page
        var rule = new BoxView { HeightRequest = 1 }.Tint(BoxView.ColorProperty, Keys.Color.OutlineVariant);
        grid.Add(rule, 0, 1);
        Grid.SetColumnSpan(rule, 2);
        grid.Add(scroller, 0, 0);
        Grid.SetRowSpan(scroller, 2);
        grid.Add(tools, 1, 0);
        Content = grid;
        UpdateStripHeight(TitleFontSize);
    }

    /// <summary>
    /// Direction of the collapse chevron, or null to hide it. The host picks it from where the
    /// group is docked.
    /// </summary>
    public DockArea? CollapseDirection
    {
        get => collapseDirection;
        set
        {
            collapseDirection = value;
            RebuildTools();
        }
    }

    /// <summary>Shows the "float" tool. Off inside floating windows, which are already floating.</summary>
    public bool ShowFloatButton
    {
        get => showFloatButton;
        set
        {
            if (showFloatButton == value) return;
            showFloatButton = value;
            RebuildTools();
        }
    }

    /// <summary>The focused group's active tab carries the accent bar; idle groups a quiet one.</summary>
    public bool IsGroupFocused
    {
        get => isFocused;
        set
        {
            isFocused = value;
            ApplyAccent();
        }
    }

    public IReadOnlyList<(DockTab Tab, Border View)> TabViews => tabViews;

    /// <summary>The horizontally scrolling host of the tabs (exposed for layout tests).</summary>
    internal ScrollView Scroller => scroller;

    /// <summary>
    /// A conservative single-line height for text at <paramref name="fontSize"/>. System UI fonts
    /// have a natural line height of roughly 1.2x the point size, and AppKit's
    /// <c>NSTextFieldCell</c> adds a couple of points of cell inset on top of that, so 1.2x + 3
    /// covers every head without visibly padding the others.
    /// </summary>
    internal static double LineHeightFor(double fontSize) => Math.Ceiling(fontSize * 1.2) + 3;

    /// <summary>The minimum height of one tab: its tallest line of text plus the tab padding.</summary>
    internal static double TabHeightFor(double maxFontSize) =>
        LineHeightFor(maxFontSize) + TabPadding.VerticalThickness;

    // The strip must not rely on the ScrollView measuring its content. On the AppKit head
    // (net10.0-macos) ScrollViewHandler has no GetDesiredSize override, so the NSScrollView reports
    // its FittingSize instead of the tabs' height; the Auto row then collapses to whatever the
    // collapse button happens to need, and the handler arranges the document view at that smaller
    // height - clipping the top and bottom of every tab title. A minimum height derived from the
    // fonts sizes the row correctly there and is a no-op everywhere the ScrollView already measures.
    void UpdateStripHeight(double maxFontSize)
    {
        var tab = TabHeightFor(maxFontSize);
        scroller.MinimumHeightRequest = tab + StripPadding.VerticalThickness;
        foreach (var (_, view) in tabViews)
            view.MinimumHeightRequest = tab;
    }

    void RebuildTools()
    {
        tools.Children.Clear();
        if (showFloatButton)
            tools.Children.Add(DockChrome.ChromeButton(DockChrome.FloatIcon, Keys.Brush.OnSurfaceVariant, "Float",
                () => FloatTapped?.Invoke(this, EventArgs.Empty)));
        if (collapseDirection is { } dir)
            tools.Children.Add(DockChrome.ChromeButton(k => DockChrome.ChevronIcon(dir, k), Keys.Brush.OnSurfaceVariant,
                "Collapse", () => CollapseTapped?.Invoke(this, EventArgs.Empty)));
        tools.IsVisible = tools.Children.Count > 0;
    }

    void ApplyAccent()
    {
        for (var i = 0; i < accentBars.Count; i++)
        {
            var bar = accentBars[i];
            bar.IsVisible = i == activeIndex;
            bar.Tint(BoxView.ColorProperty, isFocused ? Keys.Color.Primary : Keys.Color.Outline);
        }
    }

    public void SetTabs(
        DockGroup group,
        Func<DockTab, string> titleSelector,
        Func<DockTab, bool> canClose,
        bool isLocked,
        Func<DockTab, string?>? iconSelector = null)
    {
        stack.Children.Clear();
        tabViews.Clear();
        accentBars.Clear();
        activeIndex = Math.Clamp(group.ActiveTabIndex, 0, Math.Max(0, group.Tabs.Count - 1));

        for (var i = 0; i < group.Tabs.Count; i++)
        {
            var tab = group.Tabs[i];
            var isActive = i == activeIndex;

            var title = new Label
            {
                Text = titleSelector(tab),
                FontSize = TitleFontSize,
                FontAttributes = isActive ? FontAttributes.Bold : FontAttributes.None,
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaximumWidthRequest = 180
            }.Tint(Label.TextColorProperty, isActive ? Keys.Color.OnSurface : Keys.Color.OnSurfaceVariant);

            var row = new HorizontalStackLayout { Spacing = 6 };
            if (iconSelector?.Invoke(tab) is { } icon)
            {
                row.Children.Add(new Label
                {
                    Text = icon,
                    FontSize = IconFontSize,
                    VerticalOptions = LayoutOptions.Center
                });
            }
            row.Children.Add(title);

            View? close = null;
            if (!isLocked && canClose(tab))
            {
                // revealed on the active tab and on hover, the way VS keeps idle strips calm
                close = DockChrome.ChromeButton(DockChrome.CloseIcon, Keys.Brush.OnSurfaceVariant, "Close",
                    () => TabCloseTapped?.Invoke(this, tab));
                close.WidthRequest = close.HeightRequest = 18;
                close.Opacity = isActive ? 1 : 0;
                row.Children.Add(close);
            }

            var border = new Border
            {
                Content = row,
                Padding = TabPadding,
                StrokeThickness = 0,
                StrokeShape = new Rectangle()
            };
            border.Tint(BackgroundColorProperty, isActive ? Keys.Color.Surface : Keys.Color.SurfaceContainerLow);

            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => TabTapped?.Invoke(this, tab);
            border.GestureRecognizers.Add(tap);

            var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
            doubleTap.Tapped += (_, _) => TabDoubleTapped?.Invoke(this, tab);
            border.GestureRecognizers.Add(doubleTap);

            var pointer = new PointerGestureRecognizer();
            pointer.PointerPressed += (_, e) =>
            {
                if (e.GetPosition(border) is { } p)
                    TabPressed?.Invoke(this, (tab, border, p));
            };
            if (close is not null && !isActive)
            {
                pointer.PointerEntered += (_, _) => close.Opacity = 1;
                pointer.PointerExited += (_, _) => close.Opacity = 0;
            }
            border.GestureRecognizers.Add(pointer);

            if (!isLocked)
            {
                var pan = new PanGestureRecognizer();
                pan.PanUpdated += (_, e) => TabPan?.Invoke(this, (tab, border, e));
                border.GestureRecognizers.Add(pan);
            }

            // the accent bar sits across the top of the active tab
            var accent = new BoxView
            {
                HeightRequest = 2,
                VerticalOptions = LayoutOptions.Start,
                InputTransparent = true,
                IsVisible = isActive
            };
            accentBars.Add(accent);

            stack.Children.Add(new Grid { Children = { border, accent } });
            tabViews.Add((tab, border));
        }
        ApplyAccent();

        var maxFontSize = TitleFontSize;
        foreach (var (_, view) in tabViews)
            if (view.Content is HorizontalStackLayout r)
                foreach (var l in r.Children.OfType<Label>())
                    maxFontSize = Math.Max(maxFontSize, l.FontSize);
        UpdateStripHeight(maxFontSize);
    }
}
