using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>
/// The page's own settings, plus pass-throughs for the bar's common styling so a whole tabbed page
/// can be written without ever naming <see cref="ShinyTabbedPage.TabBar"/>. Anything not passed
/// through is set on that property directly.
/// </summary>
public partial class ShinyTabbedPage
{
    /// <summary>Backing store for <see cref="SelectedIndex"/>.</summary>
    public static readonly BindableProperty SelectedIndexProperty = BindableProperty.Create(
        nameof(SelectedIndex), typeof(int), typeof(ShinyTabbedPage), 0, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page =>
        {
            if (!page.suppressSelectionSync)
                page.tabBar.SelectedIndex = (int)n;
        }));

    /// <summary>Backing store for <see cref="SelectedItem"/>.</summary>
    public static readonly BindableProperty SelectedItemProperty = BindableProperty.Create(
        nameof(SelectedItem), typeof(ShinyTabItem), typeof(ShinyTabbedPage), null, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page =>
        {
            if (!page.suppressSelectionSync)
                page.tabBar.SelectedItem = n as ShinyTabItem;
        }));

    /// <summary>Backing store for <see cref="Transition"/>.</summary>
    public static readonly BindableProperty TransitionProperty = BindableProperty.Create(
        nameof(Transition), typeof(StateTransition), typeof(ShinyTabbedPage), StateTransition.Slide,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.contentHost.Transition = (StateTransition)n));

    /// <summary>Backing store for <see cref="TransitionDuration"/>.</summary>
    public static readonly BindableProperty TransitionDurationProperty = BindableProperty.Create(
        nameof(TransitionDuration), typeof(uint), typeof(ShinyTabbedPage), 220u,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.contentHost.TransitionDuration = (uint)n));

    /// <summary>Backing store for <see cref="TransitionEasing"/>.</summary>
    public static readonly BindableProperty TransitionEasingProperty = BindableProperty.Create(
        nameof(TransitionEasing), typeof(Easing), typeof(ShinyTabbedPage), Easing.CubicOut,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.contentHost.TransitionEasing = (Easing)n));

    /// <summary>Backing store for <see cref="CacheTabContent"/>.</summary>
    public static readonly BindableProperty CacheTabContentProperty = BindableProperty.Create(
        nameof(CacheTabContent), typeof(bool), typeof(ShinyTabbedPage), true,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.contentHost.CacheContent = (bool)n));

    /// <summary>Backing store for <see cref="TabBarIsVisible"/>.</summary>
    public static readonly BindableProperty TabBarIsVisibleProperty = BindableProperty.Create(
        nameof(TabBarIsVisible), typeof(bool), typeof(ShinyTabbedPage), true,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.IsVisible = (bool)n));

    /// <summary>Backing store for <see cref="ContentBehindTabBar"/>.</summary>
    public static readonly BindableProperty ContentBehindTabBarProperty = BindableProperty.Create(
        nameof(ContentBehindTabBar), typeof(bool), typeof(ShinyTabbedPage), false,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.ApplyBarPlacement()));

    /// <summary>Backing store for <see cref="SyncTitleWithTab"/>.</summary>
    public static readonly BindableProperty SyncTitleWithTabProperty = BindableProperty.Create(
        nameof(SyncTitleWithTab), typeof(bool), typeof(ShinyTabbedPage), true);

    /// <summary>Backing store for <see cref="CenterButton"/>.</summary>
    public static readonly BindableProperty CenterButtonProperty = BindableProperty.Create(
        nameof(CenterButton), typeof(TabCenterButton), typeof(ShinyTabbedPage), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.CenterButton = n as TabCenterButton));

    /// <summary>Backing store for <see cref="IndicatorStyle"/>.</summary>
    public static readonly BindableProperty IndicatorStyleProperty = BindableProperty.Create(
        nameof(IndicatorStyle), typeof(TabIndicatorStyle), typeof(ShinyTabbedPage), TabIndicatorStyle.Pill,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.IndicatorStyle = (TabIndicatorStyle)n));

    /// <summary>Backing store for <see cref="LabelMode"/>.</summary>
    public static readonly BindableProperty LabelModeProperty = BindableProperty.Create(
        nameof(LabelMode), typeof(TabLabelMode), typeof(ShinyTabbedPage), TabLabelMode.Always,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.LabelMode = (TabLabelMode)n));

    /// <summary>Backing store for <see cref="SelectedColor"/>.</summary>
    public static readonly BindableProperty SelectedColorProperty = BindableProperty.Create(
        nameof(SelectedColor), typeof(Color), typeof(ShinyTabbedPage), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.SelectedColor = n as Color));

    /// <summary>Backing store for <see cref="UnselectedColor"/>.</summary>
    public static readonly BindableProperty UnselectedColorProperty = BindableProperty.Create(
        nameof(UnselectedColor), typeof(Color), typeof(ShinyTabbedPage), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.UnselectedColor = n as Color));

    /// <summary>Backing store for <see cref="IndicatorColor"/>.</summary>
    public static readonly BindableProperty IndicatorColorProperty = BindableProperty.Create(
        nameof(IndicatorColor), typeof(Color), typeof(ShinyTabbedPage), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.IndicatorColor = n as Color));

    /// <summary>Backing store for <see cref="BarHeight"/>.</summary>
    public static readonly BindableProperty BarHeightProperty = BindableProperty.Create(
        nameof(BarHeight), typeof(double), typeof(ShinyTabbedPage), 62d,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.BarHeight = (double)n));

    /// <summary>Backing store for <see cref="BarBackgroundColor"/>.</summary>
    public static readonly BindableProperty BarBackgroundColorProperty = BindableProperty.Create(
        nameof(BarBackgroundColor), typeof(Color), typeof(ShinyTabbedPage), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.BarBackgroundColor = n as Color));

    /// <summary>Backing store for <see cref="BarCornerRadius"/>.</summary>
    public static readonly BindableProperty BarCornerRadiusProperty = BindableProperty.Create(
        nameof(BarCornerRadius), typeof(double), typeof(ShinyTabbedPage), ThemeTokens.Unset,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.BarCornerRadius = (double)n));

    /// <summary>Backing store for <see cref="BarMargin"/>.</summary>
    public static readonly BindableProperty BarMarginProperty = BindableProperty.Create(
        nameof(BarMargin), typeof(Thickness), typeof(ShinyTabbedPage), default(Thickness),
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.BarMargin = (Thickness)n));

    /// <summary>Backing store for <see cref="HasShadow"/>.</summary>
    public static readonly BindableProperty HasShadowProperty = BindableProperty.Create(
        nameof(HasShadow), typeof(bool), typeof(ShinyTabbedPage), true,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.HasShadow = (bool)n));

    /// <summary>Backing store for <see cref="BarStyle"/>.</summary>
    public static readonly BindableProperty BarStyleProperty = BindableProperty.Create(
        nameof(BarStyle), typeof(TabBarStyle), typeof(ShinyTabbedPage), TabBarStyle.Docked,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.BarStyle = (TabBarStyle)n));

    /// <inheritdoc cref="ShinyTabBar.BarStyle"/>
    public TabBarStyle BarStyle
    {
        get => (TabBarStyle)this.GetValue(BarStyleProperty);
        set => this.SetValue(BarStyleProperty, value);
    }

    /// <summary>Backing store for <see cref="BarMaterial"/>.</summary>
    public static readonly BindableProperty BarMaterialProperty = BindableProperty.Create(
        nameof(BarMaterial), typeof(TabBarMaterial), typeof(ShinyTabbedPage), TabBarMaterial.Solid,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.BarMaterial = (TabBarMaterial)n));

    /// <inheritdoc cref="ShinyTabBar.BarMaterial"/>
    public TabBarMaterial BarMaterial
    {
        get => (TabBarMaterial)this.GetValue(BarMaterialProperty);
        set => this.SetValue(BarMaterialProperty, value);
    }

    /// <summary>Backing store for <see cref="BarGlassTint"/>.</summary>
    public static readonly BindableProperty BarGlassTintProperty = BindableProperty.Create(
        nameof(BarGlassTint), typeof(Color), typeof(ShinyTabbedPage), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.BarGlassTint = n as Color));

    /// <inheritdoc cref="ShinyTabBar.BarGlassTint"/>
    public Color? BarGlassTint
    {
        get => (Color?)this.GetValue(BarGlassTintProperty);
        set => this.SetValue(BarGlassTintProperty, value);
    }

    /// <summary>Backing store for <see cref="BarBackgroundOpacity"/>.</summary>
    public static readonly BindableProperty BarBackgroundOpacityProperty = BindableProperty.Create(
        nameof(BarBackgroundOpacity), typeof(double), typeof(ShinyTabbedPage), 1d,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.BarBackgroundOpacity = (double)n));

    /// <inheritdoc cref="ShinyTabBar.BarBackgroundOpacity"/>
    public double BarBackgroundOpacity
    {
        get => (double)this.GetValue(BarBackgroundOpacityProperty);
        set => this.SetValue(BarBackgroundOpacityProperty, value);
    }

    /// <summary>Backing store for <see cref="MaxVisibleTabs"/>.</summary>
    public static readonly BindableProperty MaxVisibleTabsProperty = BindableProperty.Create(
        nameof(MaxVisibleTabs), typeof(int), typeof(ShinyTabbedPage), 0,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.MaxVisibleTabs = (int)n));

    /// <inheritdoc cref="ShinyTabBar.MaxVisibleTabs"/>
    public int MaxVisibleTabs
    {
        get => (int)this.GetValue(MaxVisibleTabsProperty);
        set => this.SetValue(MaxVisibleTabsProperty, value);
    }

    /// <summary>Backing store for <see cref="MinTabWidth"/>.</summary>
    public static readonly BindableProperty MinTabWidthProperty = BindableProperty.Create(
        nameof(MinTabWidth), typeof(double), typeof(ShinyTabbedPage), 72d,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.MinTabWidth = (double)n));

    /// <inheritdoc cref="ShinyTabBar.MinTabWidth"/>
    public double MinTabWidth
    {
        get => (double)this.GetValue(MinTabWidthProperty);
        set => this.SetValue(MinTabWidthProperty, value);
    }

    /// <summary>Backing store for <see cref="OverflowTitle"/>.</summary>
    public static readonly BindableProperty OverflowTitleProperty = BindableProperty.Create(
        nameof(OverflowTitle), typeof(string), typeof(ShinyTabbedPage), "More",
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.OverflowTitle = (string)n));

    /// <inheritdoc cref="ShinyTabBar.OverflowTitle"/>
    public string OverflowTitle
    {
        get => (string)this.GetValue(OverflowTitleProperty);
        set => this.SetValue(OverflowTitleProperty, value);
    }

    /// <summary>Backing store for <see cref="OverflowIcon"/>.</summary>
    public static readonly BindableProperty OverflowIconProperty = BindableProperty.Create(
        nameof(OverflowIcon), typeof(string), typeof(ShinyTabbedPage), "more",
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.OverflowIcon = n as string));

    /// <inheritdoc cref="ShinyTabBar.OverflowIcon"/>
    public string? OverflowIcon
    {
        get => (string?)this.GetValue(OverflowIconProperty);
        set => this.SetValue(OverflowIconProperty, value);
    }

    /// <summary>Backing store for <see cref="IconSize"/>.</summary>
    public static readonly BindableProperty IconSizeProperty = BindableProperty.Create(
        nameof(IconSize), typeof(double), typeof(ShinyTabbedPage), 24d,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.IconSize = (double)n));

    /// <summary>Backing store for <see cref="AnimateIcons"/>.</summary>
    public static readonly BindableProperty AnimateIconsProperty = BindableProperty.Create(
        nameof(AnimateIcons), typeof(bool), typeof(ShinyTabbedPage), true,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabbedPage>(b, page => page.tabBar.AnimateIcons = (bool)n));


    /// <summary>The selected tab's index into <see cref="Tabs"/>. -1 when nothing is selected.</summary>
    public int SelectedIndex
    {
        get => (int)this.GetValue(SelectedIndexProperty);
        set => this.SetValue(SelectedIndexProperty, value);
    }

    /// <summary>The selected tab. Kept in step with <see cref="SelectedIndex"/> in both directions.</summary>
    public ShinyTabItem? SelectedItem
    {
        get => (ShinyTabItem?)this.GetValue(SelectedItemProperty);
        set => this.SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// How content animates between tabs. Defaults to <see cref="StateTransition.Slide"/>, which
    /// takes its direction from the tab order.
    /// </summary>
    public StateTransition Transition
    {
        get => (StateTransition)this.GetValue(TransitionProperty);
        set => this.SetValue(TransitionProperty, value);
    }

    /// <summary>Transition length in milliseconds. Zero swaps instantly.</summary>
    public uint TransitionDuration
    {
        get => (uint)this.GetValue(TransitionDurationProperty);
        set => this.SetValue(TransitionDurationProperty, value);
    }

    /// <summary>Transition easing. Defaults to <see cref="Easing.CubicOut"/>.</summary>
    public Easing TransitionEasing
    {
        get => (Easing)this.GetValue(TransitionEasingProperty);
        set => this.SetValue(TransitionEasingProperty, value);
    }

    /// <summary>
    /// Keep a tab's content alive after it is left, so returning to it is instant and its scroll
    /// position and entry text survive. Turn it off to rebuild — and reset — the tab every time.
    /// </summary>
    public bool CacheTabContent
    {
        get => (bool)this.GetValue(CacheTabContentProperty);
        set => this.SetValue(CacheTabContentProperty, value);
    }

    /// <summary>Hides the whole bar. The content keeps its row unless <see cref="ContentBehindTabBar"/> is on.</summary>
    public bool TabBarIsVisible
    {
        get => (bool)this.GetValue(TabBarIsVisibleProperty);
        set => this.SetValue(TabBarIsVisibleProperty, value);
    }

    /// <summary>
    /// Runs the content full-bleed under the bar instead of stopping above it — for a translucent
    /// bar. Remember to leave room at the bottom of your own content. A
    /// <see cref="TabBarStyle.Floating"/> bar does this whatever this is set to, since a capsule laid
    /// over the page has nothing to stop above.
    /// </summary>
    public bool ContentBehindTabBar
    {
        get => (bool)this.GetValue(ContentBehindTabBarProperty);
        set => this.SetValue(ContentBehindTabBarProperty, value);
    }

    /// <summary>Follow the selected tab's <see cref="ShinyTabItem.Title"/> with the page's own.</summary>
    public bool SyncTitleWithTab
    {
        get => (bool)this.GetValue(SyncTitleWithTabProperty);
        set => this.SetValue(SyncTitleWithTabProperty, value);
    }

    /// <summary>The raised button in the middle of the bar. Null gives an ordinary bar.</summary>
    public TabCenterButton? CenterButton
    {
        get => (TabCenterButton?)this.GetValue(CenterButtonProperty);
        set => this.SetValue(CenterButtonProperty, value);
    }

    /// <inheritdoc cref="ShinyTabBar.IndicatorStyle"/>
    public TabIndicatorStyle IndicatorStyle
    {
        get => (TabIndicatorStyle)this.GetValue(IndicatorStyleProperty);
        set => this.SetValue(IndicatorStyleProperty, value);
    }

    /// <inheritdoc cref="ShinyTabBar.LabelMode"/>
    public TabLabelMode LabelMode
    {
        get => (TabLabelMode)this.GetValue(LabelModeProperty);
        set => this.SetValue(LabelModeProperty, value);
    }

    /// <inheritdoc cref="ShinyTabBar.SelectedColor"/>
    public Color? SelectedColor
    {
        get => (Color?)this.GetValue(SelectedColorProperty);
        set => this.SetValue(SelectedColorProperty, value);
    }

    /// <inheritdoc cref="ShinyTabBar.UnselectedColor"/>
    public Color? UnselectedColor
    {
        get => (Color?)this.GetValue(UnselectedColorProperty);
        set => this.SetValue(UnselectedColorProperty, value);
    }

    /// <inheritdoc cref="ShinyTabBar.IndicatorColor"/>
    public Color? IndicatorColor
    {
        get => (Color?)this.GetValue(IndicatorColorProperty);
        set => this.SetValue(IndicatorColorProperty, value);
    }

    /// <inheritdoc cref="ShinyTabBar.BarHeight"/>
    public double BarHeight
    {
        get => (double)this.GetValue(BarHeightProperty);
        set => this.SetValue(BarHeightProperty, value);
    }

    /// <inheritdoc cref="ShinyTabBar.BarBackgroundColor"/>
    public Color? BarBackgroundColor
    {
        get => (Color?)this.GetValue(BarBackgroundColorProperty);
        set => this.SetValue(BarBackgroundColorProperty, value);
    }

    /// <inheritdoc cref="ShinyTabBar.BarCornerRadius"/>
    public double BarCornerRadius
    {
        get => (double)this.GetValue(BarCornerRadiusProperty);
        set => this.SetValue(BarCornerRadiusProperty, value);
    }

    /// <inheritdoc cref="ShinyTabBar.BarMargin"/>
    public Thickness BarMargin
    {
        get => (Thickness)this.GetValue(BarMarginProperty);
        set => this.SetValue(BarMarginProperty, value);
    }

    /// <inheritdoc cref="ShinyTabBar.HasShadow"/>
    public bool HasShadow
    {
        get => (bool)this.GetValue(HasShadowProperty);
        set => this.SetValue(HasShadowProperty, value);
    }

    /// <inheritdoc cref="ShinyTabBar.IconSize"/>
    public double IconSize
    {
        get => (double)this.GetValue(IconSizeProperty);
        set => this.SetValue(IconSizeProperty, value);
    }

    /// <inheritdoc cref="ShinyTabBar.AnimateIcons"/>
    public bool AnimateIcons
    {
        get => (bool)this.GetValue(AnimateIconsProperty);
        set => this.SetValue(AnimateIconsProperty, value);
    }
}
