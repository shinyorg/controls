using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui;

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
    internal static readonly Thickness TabPadding = new(10, 5);
    internal static readonly Thickness StripPadding = new(6, 4, 6, 0);

    readonly HorizontalStackLayout stack;
    readonly ScrollView scroller;
    readonly List<(DockTab Tab, Border View)> tabViews = new();

    public event EventHandler<DockTab>? TabTapped;
    public event EventHandler<DockTab>? TabCloseTapped;
    public event EventHandler<(DockTab Tab, Border View, PanUpdatedEventArgs Pan)>? TabPan;
    public event EventHandler? CollapseTapped;

    public DockTabStrip()
    {
        BackgroundColor = Color.FromArgb("#E5E7EB");
        stack = new HorizontalStackLayout { Spacing = 2, Padding = StripPadding };

        var collapseButton = new Label
        {
            Text = "−",
            FontSize = 15,
            TextColor = Color.FromArgb("#4B5563"),
            Padding = new Thickness(8, 2),
            VerticalOptions = LayoutOptions.Center
        };
        var collapseTap = new TapGestureRecognizer();
        collapseTap.Tapped += (_, _) => CollapseTapped?.Invoke(this, EventArgs.Empty);
        collapseButton.GestureRecognizers.Add(collapseTap);
        collapse = collapseButton;

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        scroller = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = stack
        };
        grid.Add(scroller, 0, 0);
        grid.Add(collapseButton, 1, 0);
        Content = grid;
        UpdateStripHeight(TitleFontSize);
    }

    readonly Label collapse;

    /// <summary>Glyph for the collapse button, or null to hide it. The host picks
    /// a direction-appropriate arrow based on where the group is docked.</summary>
    public string? CollapseGlyph
    {
        get => collapse.IsVisible ? collapse.Text : null;
        set
        {
            collapse.IsVisible = value is not null;
            collapse.Text = value ?? string.Empty;
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

    public void SetTabs(
        DockGroup group,
        Func<DockTab, string> titleSelector,
        Func<DockTab, bool> canClose,
        bool isLocked,
        Func<DockTab, string?>? iconSelector = null)
    {
        stack.Children.Clear();
        tabViews.Clear();
        var activeIndex = Math.Clamp(group.ActiveTabIndex, 0, Math.Max(0, group.Tabs.Count - 1));

        for (var i = 0; i < group.Tabs.Count; i++)
        {
            var tab = group.Tabs[i];
            var isActive = i == activeIndex;

            var title = new Label
            {
                Text = titleSelector(tab),
                FontSize = TitleFontSize,
                FontAttributes = isActive ? FontAttributes.Bold : FontAttributes.None,
                TextColor = isActive ? Color.FromArgb("#111827") : Color.FromArgb("#4B5563"),
                VerticalOptions = LayoutOptions.Center
            };

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

            if (!isLocked && canClose(tab))
            {
                var close = new Label
                {
                    Text = "×",
                    FontSize = CloseFontSize,
                    TextColor = Color.FromArgb("#9CA3AF"),
                    VerticalOptions = LayoutOptions.Center,
                    Padding = new Thickness(2, 0)
                };
                var closeTap = new TapGestureRecognizer();
                closeTap.Tapped += (_, _) => TabCloseTapped?.Invoke(this, tab);
                close.GestureRecognizers.Add(closeTap);
                row.Children.Add(close);
            }

            var border = new Border
            {
                Content = row,
                Padding = TabPadding,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(5, 5, 0, 0) },
                BackgroundColor = isActive ? Colors.White : Colors.Transparent
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => TabTapped?.Invoke(this, tab);
            border.GestureRecognizers.Add(tap);

            if (!isLocked)
            {
                var pan = new PanGestureRecognizer();
                pan.PanUpdated += (_, e) => TabPan?.Invoke(this, (tab, border, e));
                border.GestureRecognizers.Add(pan);
            }

            stack.Children.Add(border);
            tabViews.Add((tab, border));
        }

        var maxFontSize = TitleFontSize;
        foreach (var child in stack.Children)
            if (child is Border { Content: HorizontalStackLayout r })
                foreach (var l in r.Children.OfType<Label>())
                    maxFontSize = Math.Max(maxFontSize, l.FontSize);
        UpdateStripHeight(maxFontSize);
    }
}
