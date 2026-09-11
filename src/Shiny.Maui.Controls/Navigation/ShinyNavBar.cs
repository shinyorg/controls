using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Controls.MotionIcons;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>
/// A navigation bar with items on <b>both</b> sides of the title, drawn rather than delegated to the
/// platform's own bar.
/// </summary>
/// <remarks>
/// <para>Nothing in it touches a platform SDK, which is the whole point: the left slot of a native
/// bar is the back button's on every platform MAUI reaches, and on AppKit and GTK4 there is no bar
/// to put anything on at all. Drawing it means the same left items, overflow menu, badges and
/// collapsing large title render identically on iOS, Android, Windows, Mac Catalyst, AppKit and
/// GTK4.</para>
/// <para>The bar is only chrome. It owns no navigation — <see cref="ShinyNavigationPage"/> feeds it
/// the current page's title, items and back state and does the popping. Used on its own it is a
/// self-contained bar you can put at the top of any layout.</para>
/// </remarks>
public partial class ShinyNavBar : Grid
{
    const string BackChevronPath = "M 15 4.5 L 7.5 12 L 15 19.5";

    readonly ObservableCollection<ToolbarItem> leftItems = new();
    readonly ObservableCollection<ToolbarItem> rightItems = new();
    readonly List<ToolbarItem> subscribed = new();

    readonly Border surface;
    readonly Grid stack;
    readonly RowDefinition largeTitleRow;
    readonly Grid barGrid;
    readonly HorizontalStackLayout leadingHost;
    readonly HorizontalStackLayout trailingHost;
    readonly Grid titleHost;
    readonly HorizontalStackLayout titleRow;
    readonly Image titleIconImage;
    readonly VerticalStackLayout titleStack;
    readonly Label titleLabel;
    readonly Label subtitleLabel;
    readonly Grid largeTitleHost;
    readonly Label largeTitleLabel;
    readonly BoxView separator;
    readonly BoxView surfaceProbe;
    readonly SolidColorBrush surfaceBrush;

    View? backButton;
    View? scrollSource;
    EventHandler<ScrolledEventArgs>? scrollHandler;
    EventHandler<ItemsViewScrolledEventArgs>? itemsScrollHandler;

    Layout? menuLayer;
    BoxView? menuBackdrop;
    Border? menuCard;
    NavBarSide menuSide;

    readonly List<ToolbarItem> leftOverflow = new();
    readonly List<ToolbarItem> rightOverflow = new();

    /// <summary>Creates the bar.</summary>
    public ShinyNavBar()
    {
        this.titleLabel = new Label
        {
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            VerticalTextAlignment = TextAlignment.Center
        };

        this.subtitleLabel = new Label
        {
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            IsVisible = false,
            VerticalTextAlignment = TextAlignment.Center
        };

        this.titleStack = new VerticalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Center,
            Children = { this.titleLabel, this.subtitleLabel }
        };

        this.titleIconImage = new Image
        {
            Aspect = Aspect.AspectFit,
            WidthRequest = 24,
            HeightRequest = 24,
            IsVisible = false,
            VerticalOptions = LayoutOptions.Center
        };

        this.titleRow = new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children = { this.titleIconImage, this.titleStack }
        };

        // A Grid rather than the row itself, so a TitleView can replace the row without the
        // alignment attached properties having to be re-applied to whatever the consumer handed over.
        this.titleHost = new Grid
        {
            VerticalOptions = LayoutOptions.Fill,

            // In Center mode the host spans the whole bar and sits underneath the item groups, so it
            // must not swallow their taps. Turned back on for a TitleView, which is there to be used.
            InputTransparent = true,
            CascadeInputTransparent = false,
            Children = { this.titleRow }
        };

        this.leadingHost = new HorizontalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start
        };

        this.trailingHost = new HorizontalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End
        };

        this.barGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        // Title first so the item groups paint over it - in Center mode it spans all three columns
        // and would otherwise cover the buttons it is inset to clear.
        this.barGrid.Children.Add(this.titleHost);
        Grid.SetColumn(this.leadingHost, 0);
        Grid.SetColumn(this.trailingHost, 2);
        this.barGrid.Children.Add(this.leadingHost);
        this.barGrid.Children.Add(this.trailingHost);

        this.largeTitleLabel = new Label
        {
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            VerticalTextAlignment = TextAlignment.End,
            HorizontalOptions = LayoutOptions.Start,
            FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold
        };

        this.largeTitleHost = new Grid
        {
            Padding = new Thickness(16, 0, 16, 6),
            IsVisible = false,
            Children = { this.largeTitleLabel }
        };

        this.largeTitleRow = new RowDefinition(GridLength.Auto);
        this.stack = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), this.largeTitleRow }
        };
        Grid.SetRow(this.barGrid, 0);
        Grid.SetRow(this.largeTitleHost, 1);
        this.stack.Children.Add(this.barGrid);
        this.stack.Children.Add(this.largeTitleHost);

        (this.surfaceBrush, this.surfaceProbe) = ThemeProbe.Create();
        this.stack.Children.Add(this.surfaceProbe);

        this.surface = new Border
        {
            StrokeThickness = 0,
            Stroke = null,
            Background = this.surfaceBrush,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerNoneRadius),
            Content = this.stack
        };

        this.separator = new BoxView
        {
            HeightRequest = 1,
            VerticalOptions = LayoutOptions.End,
            IsVisible = false,
            InputTransparent = true
        };
        this.separator.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);

        this.Children.Add(this.surface);
        this.Children.Add(this.separator);

        // The bar is a Grid, and a Grid defaults to SafeAreaRegions.Container - so left alone it
        // takes the top inset itself, reserving a 68pt strip at the top of its own frame that the
        // background can never be laid out into. The inset has to land on the one view *inside* the
        // background instead, which ApplyBarSurface does; this makes room for it.
        this.SafeAreaEdges = SafeAreaEdges.None;

        this.leftItems.CollectionChanged += this.OnItemsChanged;
        this.rightItems.CollectionChanged += this.OnItemsChanged;

        // A theme token resolves on the probe long after the property that asked for it was set - on
        // the first merge of the dictionary, and again on every theme swap - so the probe, not the
        // property, is what says the bar's colour has actually changed.
        this.surfaceProbe.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BoxView.Color))
                this.UpdateEffectiveBarColor();
        };

        // The centred title is inset by whichever group is wider, and neither has a width until it
        // has been measured - so the inset is recomputed rather than guessed at build time.
        this.leadingHost.SizeChanged += (_, _) => this.UpdateCenterInset();
        this.trailingHost.SizeChanged += (_, _) => this.UpdateCenterInset();

        this.Unloaded += (_, _) => this.CloseMenu();

        // The item views below are built off-tree, from the constructor. A MotionIconView resolves
        // its artwork on Loaded, and a child parented before the bar itself is attached never gets
        // that event - so every icon in the first bar drew as an empty 22pt box while its badge and
        // the overflow glyph, which are ordinary views, rendered fine. Rebuilding on load rebuilds
        // them against an attached tree, where Loaded does fire. RebuildItems is idempotent - it is
        // already what every item property change runs - so a page that loads more than once is fine.
        this.Loaded += (_, _) => this.RebuildItems();

        // Seeded from the properties' defaults: a property left at its default never raises
        // propertyChanged, so without this the bar would be built but unpainted.
        this.ApplyBarSurface();
        this.ApplyTypography();
        this.ApplyTitle();
        this.ApplyTitleAlignment();
        this.ApplyLargeTitle();
        this.RebuildItems();

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(ShinyNavBar));
    }


    /// <summary>The items drawn before the title, after the back button.</summary>
    public IList<ToolbarItem> LeftItems => this.leftItems;

    /// <summary>The items drawn after the title.</summary>
    public IList<ToolbarItem> RightItems => this.rightItems;

    /// <summary>True while an overflow menu is on screen.</summary>
    public bool IsMenuOpen => this.menuCard is not null;

    /// <summary>Raised when a bar item is tapped, after its own command and <c>Clicked</c> have run.</summary>
    public event EventHandler<NavBarItemEventArgs>? ItemInvoked;

    /// <summary>
    /// Raised when the back affordance is tapped, before anything happens. Set
    /// <see cref="NavBarBackEventArgs.Cancel"/> to stop the pop.
    /// </summary>
    public event EventHandler<NavBarBackEventArgs>? BackButtonPressed;

    /// <summary>
    /// What the bar does for "back" when nothing intercepted it. Assigned by the host, because
    /// popping is the host's job and the bar has no navigation stack of its own.
    /// </summary>
    internal Action? BackAction { get; set; }


    // ---------------------------------------------------------------------------------------------
    // Test seams. The bar's children are never handed to a handler in a headless test, so the visual
    // tree cannot be walked from the outside - these name the pieces the tests assert on.
    // ---------------------------------------------------------------------------------------------

    internal Layout LeadingHost => this.leadingHost;

    internal Layout TrailingHost => this.trailingHost;

    internal Grid BarLayout => this.barGrid;

    internal Border Surface => this.surface;

    internal Grid SurfaceContent => this.stack;

    internal BoxView SurfaceProbe => this.surfaceProbe;

    internal Grid TitleHost => this.titleHost;

    internal View TitleRow => this.titleRow;

    internal Label TitleLabel => this.titleLabel;

    internal Label SubtitleLabel => this.subtitleLabel;

    internal Label LargeTitleLabel => this.largeTitleLabel;

    internal Grid LargeTitleHost => this.largeTitleHost;

    internal View? BackButton => this.backButton;

    internal View? ScrollSource => this.scrollSource;

    internal IReadOnlyList<ToolbarItem> OverflowFor(NavBarSide side)
        => side == NavBarSide.Left ? this.leftOverflow : this.rightOverflow;

    /// <summary>Drives the collapse without a platform scroll view to raise the event.</summary>
    internal void SimulateScroll(double offset) => this.CollapseProgress = this.ProgressFor(offset);


    /// <summary>
    /// Opens the overflow menu for a side, or closes it if that side's menu is the one already open —
    /// which is what a second tap of the same button means. Does nothing when the side has no
    /// overflow.
    /// </summary>
    public void OpenMenu(NavBarSide side)
    {
        if (this.menuCard is not null && this.menuSide == side)
        {
            this.CloseMenu();
            return;
        }

        var items = side == NavBarSide.Left ? this.leftOverflow : this.rightOverflow;
        if (items.Count == 0)
            return;

        this.menuSide = side;
        _ = this.OpenMenuAsync();
    }


    /// <summary>Closes whichever overflow menu is open.</summary>
    public void CloseMenu() => _ = this.CloseMenuAsync();


    // ---------------------------------------------------------------------------------------------
    // Scroll source - what the collapsing large title follows
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Follows <paramref name="source"/>'s vertical offset with the large title. Pass null to stop.
    /// </summary>
    /// <remarks>
    /// A <see cref="ScrollView"/> and an <see cref="ItemsView"/> report their offset through
    /// different events, so both are handled; anything else is ignored and the title simply never
    /// collapses, which is the honest outcome for a page with nothing to scroll.
    /// </remarks>
    public void AttachScrollSource(View? source)
    {
        this.DetachScrollSource();
        this.scrollSource = source;

        switch (source)
        {
            case ScrollView scroll:
                this.scrollHandler = (_, e) => this.CollapseProgress = this.ProgressFor(e.ScrollY);
                scroll.Scrolled += this.scrollHandler;
                break;

            case ItemsView items:
                this.itemsScrollHandler = (_, e) => this.CollapseProgress = this.ProgressFor(e.VerticalOffset);
                items.Scrolled += this.itemsScrollHandler;
                break;
        }
    }


    void DetachScrollSource()
    {
        if (this.scrollSource is ScrollView scroll && this.scrollHandler is not null)
            scroll.Scrolled -= this.scrollHandler;

        if (this.scrollSource is ItemsView items && this.itemsScrollHandler is not null)
            items.Scrolled -= this.itemsScrollHandler;

        this.scrollHandler = null;
        this.itemsScrollHandler = null;
        this.scrollSource = null;
    }


    double ProgressFor(double offset)
    {
        var distance = this.LargeTitleCollapseDistance;
        if (distance <= 0)
            return offset > 0 ? 1 : 0;

        return Math.Clamp(offset / distance, 0, 1);
    }


    /// <summary>
    /// The first thing in <paramref name="root"/> that scrolls, or null. Used when a page has not
    /// nominated one with <see cref="ShinyNav.ScrollSourceProperty"/>.
    /// </summary>
    internal static View? FindScrollSource(Element? root)
    {
        if (root is null)
            return null;

        if (root is ScrollView or ItemsView)
            return (View)root;

        foreach (var child in ChildrenOf(root))
        {
            if (FindScrollSource(child) is { } found)
                return found;
        }
        return null;
    }


    static IEnumerable<Element> ChildrenOf(Element element) => element switch
    {
        Layout layout => layout.Children.OfType<Element>(),
        ContentView content when content.Content is not null => new Element[] { content.Content },
        Border border when border.Content is not null => new Element[] { border.Content },
        ScrollView scroll when scroll.Content is not null => new Element[] { scroll.Content },
        ContentPage page when page.Content is not null => new Element[] { page.Content },
        _ => Array.Empty<Element>()
    };


    // ---------------------------------------------------------------------------------------------
    // Appearance
    // ---------------------------------------------------------------------------------------------

    void ApplyBarSurface()
    {
        ThemeProbe.Tint(this.surfaceProbe, BoxView.ColorProperty, this.BarBackgroundColor, ShinyThemeKeys.Color.SurfaceContainer);

        // A brush wins outright - it is the only way to say "gradient", and a colour set alongside it
        // could only ever be the one it replaces.
        this.surface.Background = this.BarBackground ?? this.surfaceBrush;

        // On the Border's *content*, not on the Border. The inset is applied by offsetting the view
        // that carries it, not by padding it - so putting it on the surface pushed the whole
        // background down and left the strip above it painted in the page's colour, which is the
        // exact failure this is here to fix. On the content the surface stays at the top of the bar
        // and grows by the inset, so the background fills the status bar and the notch while the
        // title and the items sit below them.
        this.stack.SafeAreaEdges = this.RespectSafeArea ? ContainerSafeArea : SafeAreaEdges.None;

        this.barGrid.Padding = this.BarPadding;
        this.barGrid.HeightRequest = this.BarHeight;
        this.separator.IsVisible = this.HasSeparator;

        if (this.HasShadow)
            this.surface.WithElevation(ShinyThemeKeys.Elevation.Level2);
        else
            // Not ClearValue: it leaves WithElevation's dynamic resource in place, which puts the
            // shadow straight back - see ThemeTokens.WithoutElevation.
            this.surface.WithoutElevation();

        this.UpdateEffectiveBarColor();
    }


    /// <summary>
    /// Every edge in <see cref="SafeAreaRegions.Container"/> mode. Built rather than taken from
    /// <c>SafeAreaEdges.Container</c>, which MAUI keeps internal.
    /// </summary>
    static readonly SafeAreaEdges ContainerSafeArea = new(SafeAreaRegions.Container);


    /// <summary>
    /// The one colour the bar paints behind the status bar, or null when it cannot be reduced to one.
    /// </summary>
    /// <remarks>
    /// Read rather than computed by the consumer because the usual case is a theme token: the colour
    /// only exists once the dictionary has merged and the probe has resolved it, which is later than
    /// any property this bar exposes. <see cref="EffectiveBarColorChanged"/> is the signal that it
    /// finally has - or that a theme swap has changed it.
    /// </remarks>
    public Color? EffectiveBarColor { get; private set; }

    /// <summary>Raised when <see cref="EffectiveBarColor"/> becomes a different colour.</summary>
    public event EventHandler? EffectiveBarColorChanged;


    void UpdateEffectiveBarColor()
    {
        var resolved = this.BarBackground switch
        {
            SolidColorBrush solid => solid.Color,

            // The top stop, not an average: it is the end of the gradient the status bar sits over.
            // Ordered rather than trusted in declaration order - GradientStops is a plain collection.
            GradientBrush gradient => gradient.GradientStops.OrderBy(s => s.Offset).FirstOrDefault()?.Color,

            // No brush, so the surface is painted by the probe - which is where an explicit
            // BarBackgroundColor and a resolved theme token both end up.
            null => this.surfaceProbe.Color,

            // An image or a pattern brush has no single colour, and guessing one would drive the
            // status bar to a contrast that is not on screen.
            _ => null
        };

        if (Equals(this.EffectiveBarColor, resolved))
            return;

        this.EffectiveBarColor = resolved;
        this.EffectiveBarColorChanged?.Invoke(this, EventArgs.Empty);
    }


    void ApplyItemMetrics()
    {
        this.leadingHost.Spacing = this.ItemSpacing;
        this.trailingHost.Spacing = this.ItemSpacing;
    }


    void ApplyTypography()
    {
        this.titleLabel.SetTokenOrValue(Label.FontSizeProperty, this.TitleFontSize, ShinyThemeKeys.Type.TitleLargeSize);
        this.titleLabel.FontAttributes = this.TitleFontAttributes;
        this.subtitleLabel.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);
        this.largeTitleLabel.SetTokenOrValue(Label.FontSizeProperty, this.LargeTitleFontSize, ShinyThemeKeys.Type.HeadlineMediumSize);

        if (this.TitleFontFamily is { Length: > 0 } family)
        {
            this.titleLabel.FontFamily = family;
            this.subtitleLabel.FontFamily = family;
            this.largeTitleLabel.FontFamily = family;
        }
        else
        {
            foreach (var label in new[] { this.titleLabel, this.subtitleLabel, this.largeTitleLabel })
                label.SetDynamicResource(Label.FontFamilyProperty, ShinyThemeKeys.Type.FontFamily);
        }

        ThemeProbe.Tint(this.titleLabel, Label.TextColorProperty, this.BarTextColor, ShinyThemeKeys.Color.OnSurface);
        ThemeProbe.Tint(this.largeTitleLabel, Label.TextColorProperty, this.BarTextColor, ShinyThemeKeys.Color.OnSurface);
        ThemeProbe.Tint(this.subtitleLabel, Label.TextColorProperty, this.BarTextColor, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.subtitleLabel.Opacity = this.BarTextColor is null ? 1 : 0.75;
    }


    void ApplyTitle()
    {
        this.titleLabel.Text = this.Title ?? String.Empty;
        this.titleLabel.IsVisible = !String.IsNullOrEmpty(this.Title);

        this.subtitleLabel.Text = this.Subtitle ?? String.Empty;
        this.subtitleLabel.IsVisible = !String.IsNullOrEmpty(this.Subtitle);

        this.titleIconImage.Source = this.TitleIcon;
        this.titleIconImage.IsVisible = this.TitleIcon is not null;

        this.largeTitleLabel.Text = this.LargeTitle ?? this.Title ?? String.Empty;
        this.ApplyLargeTitle();
    }


    void ApplyTitleView()
    {
        this.titleHost.Children.Clear();

        if (this.TitleView is { } view)
        {
            this.titleHost.InputTransparent = false;
            view.VerticalOptions = LayoutOptions.Center;
            this.titleHost.Children.Add(view);
        }
        else
        {
            this.titleHost.InputTransparent = true;
            this.titleHost.Children.Add(this.titleRow);
        }

        this.ApplyTitleAlignment();
        this.ApplyCollapse();
    }


    void ApplyTitleAlignment()
    {
        var centered = this.ResolvedAlignment() == NavBarTitleAlignment.Center;

        Grid.SetColumn(this.titleHost, centered ? 0 : 1);
        Grid.SetColumnSpan(this.titleHost, centered ? 3 : 1);
        this.titleHost.HorizontalOptions = centered ? LayoutOptions.Center : LayoutOptions.Start;
        this.titleRow.HorizontalOptions = centered ? LayoutOptions.Center : LayoutOptions.Start;
        this.titleStack.HorizontalOptions = centered ? LayoutOptions.Center : LayoutOptions.Start;
        this.titleLabel.HorizontalTextAlignment = centered ? TextAlignment.Center : TextAlignment.Start;
        this.subtitleLabel.HorizontalTextAlignment = centered ? TextAlignment.Center : TextAlignment.Start;

        if (centered)
            this.UpdateCenterInset();
        else
            this.titleHost.Margin = new Thickness(8, 0, 8, 0);
    }


    /// <summary>Nothing sits above the bar to inherit from, so <c>Inherit</c> resolves to <c>None</c>.</summary>
    internal LargeTitleDisplay EffectiveLargeTitleDisplay
        => this.LargeTitleDisplay == LargeTitleDisplay.Inherit ? LargeTitleDisplay.None : this.LargeTitleDisplay;


    NavBarTitleAlignment ResolvedAlignment()
    {
        if (this.TitleAlignment != NavBarTitleAlignment.Auto)
            return this.TitleAlignment;

        // Apple centres, everyone else leads. Mac Catalyst reports as MacCatalyst, not iOS.
        return DeviceInfo.Platform == DevicePlatform.iOS || DeviceInfo.Platform == DevicePlatform.MacCatalyst
            ? NavBarTitleAlignment.Center
            : NavBarTitleAlignment.Start;
    }


    /// <summary>
    /// Insets the centred title past whichever item group is wider, so it stays centred on the bar
    /// (as every platform's own bar does) instead of centring on the gap left between the groups.
    /// </summary>
    void UpdateCenterInset()
    {
        if (this.ResolvedAlignment() != NavBarTitleAlignment.Center)
            return;

        var leading = Double.IsNaN(this.leadingHost.Width) ? 0 : this.leadingHost.Width;
        var trailing = Double.IsNaN(this.trailingHost.Width) ? 0 : this.trailingHost.Width;
        var inset = Math.Max(Math.Max(leading, trailing), 0) + 8;

        this.titleHost.Margin = new Thickness(inset, 0, inset, 0);
    }


    void ApplyLargeTitle()
    {
        var mode = this.EffectiveLargeTitleDisplay;
        var text = this.LargeTitle ?? this.Title;
        var show = mode != LargeTitleDisplay.None && !String.IsNullOrEmpty(text);

        this.largeTitleRow.Height = show ? GridLength.Auto : new GridLength(0);

        if (!show)
        {
            this.largeTitleHost.IsVisible = false;
            this.largeTitleHost.HeightRequest = 0;
            this.titleRow.Opacity = 1;
            return;
        }

        // Visibility from here on is ApplyCollapse's: it is the one that knows whether the title has
        // finished folding away.
        this.ApplyCollapse();
    }


    void ApplyCollapse()
    {
        var mode = this.EffectiveLargeTitleDisplay;
        var text = this.LargeTitle ?? this.Title;

        // Asked of the properties rather than of largeTitleHost.IsVisible, which this method owns.
        if (mode == LargeTitleDisplay.None || String.IsNullOrEmpty(text))
        {
            this.largeTitleHost.IsVisible = false;
            this.titleRow.Opacity = 1;
            return;
        }

        var progress = mode == LargeTitleDisplay.Always ? 0 : Math.Clamp(this.CollapseProgress, 0, 1);
        var height = this.LargeTitleHeight * (1 - progress);

        this.largeTitleHost.HeightRequest = height;

        // A HeightRequest of zero is not the same as gone: the host keeps its padding, and an Auto
        // row still measures the label inside it, which left a dead band under the bar at the end of
        // the collapse. Taking the host out of the layout is what actually closes the gap.
        this.largeTitleHost.IsVisible = height > 0.5;
        this.largeTitleHost.Padding = new Thickness(16, 0, 16, height > 0.5 ? 6 : 0);

        // The text fades out over the first half of the travel and the inline title fades in over the
        // second, so the two are never both at full strength - which reads as a duplicated title.
        this.largeTitleLabel.Opacity = Math.Clamp(1 - (progress * 2), 0, 1);
        this.largeTitleLabel.TranslationY = -6 * progress;

        this.titleRow.Opacity = mode == LargeTitleDisplay.Always
            ? 0
            : Math.Clamp((progress - 0.5) * 2, 0, 1);
    }


    // ---------------------------------------------------------------------------------------------
    // Back button
    // ---------------------------------------------------------------------------------------------

    void ApplyBackButton()
    {
        if (this.backButton is not null)
        {
            this.leadingHost.Children.Remove(this.backButton);
            this.backButton = null;
        }

        if (!this.IsBackButtonVisible)
            return;

        var spec = new BackIconSpec(this.BackButtonIcon);
        var icon = TabIcons.Realize(spec, null, this.IconSize);
        TabIcons.Tint(icon, this.ResolvedIconColor(), ShinyThemeKeys.Color.OnSurface);

        var content = new HorizontalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };

        if (icon is not null)
            content.Children.Add(icon);

        if (this.BackButtonText is { Length: > 0 } text)
        {
            var label = new Label
            {
                Text = text,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1
            }.WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);
            ThemeProbe.Tint(label, Label.TextColorProperty, this.ResolvedIconColor(), ShinyThemeKeys.Color.OnSurface);
            content.Children.Add(label);
        }

        var button = this.WrapAsButton(content, "nav-back", () => this.RaiseBack());
        this.backButton = button;
        this.leadingHost.Children.Insert(0, button);
    }


    void RaiseBack()
    {
        var args = new NavBarBackEventArgs();
        this.BackButtonPressed?.Invoke(this, args);
        if (args.Cancel)
            return;

        if (this.BackButtonCommand is { } command)
        {
            if (command.CanExecute(this.BackButtonCommandParameter))
                command.Execute(this.BackButtonCommandParameter);

            return;
        }

        this.BackAction?.Invoke();
    }


    /// <summary>
    /// The chevron, unless the consumer named a built-in icon. A raw path rather than a library icon
    /// because the library has no left chevron and this is the one glyph the bar cannot do without.
    /// </summary>
    sealed class BackIconSpec(string? icon) : ITabIcon
    {
        public string? Icon => icon;
        public MotionIconDefinition? IconSource => null;
        public string? IconPathData => icon is { Length: > 0 } ? null : BackChevronPath;
        public ImageSource? IconImage => null;
        public MotionPreset Motion => MotionPreset.Default;
    }


    /// <summary>A plain <see cref="ToolbarItem"/> seen through the icon contract: its image, and nothing else.</summary>
    sealed class PlainIconSpec(ImageSource? image) : ITabIcon
    {
        public string? Icon => null;
        public MotionIconDefinition? IconSource => null;
        public string? IconPathData => null;
        public ImageSource? IconImage => image;
        public MotionPreset Motion => MotionPreset.Default;
    }
}
