using System.Windows.Input;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

public partial class ShinyTabBar
{
    /// <summary>Backing store for <see cref="SelectedIndex"/>.</summary>
    public static readonly BindableProperty SelectedIndexProperty = BindableProperty.Create(
        nameof(SelectedIndex), typeof(int), typeof(ShinyTabBar), 0, BindingMode.TwoWay,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.OnSelectedIndexChanged((int)o, (int)n)));

    /// <summary>Backing store for <see cref="SelectedItem"/>.</summary>
    public static readonly BindableProperty SelectedItemProperty = BindableProperty.Create(
        nameof(SelectedItem), typeof(ShinyTabItem), typeof(ShinyTabBar), null, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.OnSelectedItemChanged(n as ShinyTabItem)));

    /// <summary>Backing store for <see cref="BarHeight"/>.</summary>
    public static readonly BindableProperty BarHeightProperty = BindableProperty.Create(
        nameof(BarHeight), typeof(double), typeof(ShinyTabBar), 62d,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.ApplyMetrics()));

    /// <summary>Backing store for <see cref="BarStyle"/>.</summary>
    public static readonly BindableProperty BarStyleProperty = BindableProperty.Create(
        nameof(BarStyle), typeof(TabBarStyle), typeof(ShinyTabBar), TabBarStyle.Docked,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.ApplyBarStyle()));

    /// <summary>Backing store for <see cref="BarBackgroundColor"/>.</summary>
    public static readonly BindableProperty BarBackgroundColorProperty = BindableProperty.Create(
        nameof(BarBackgroundColor), typeof(Color), typeof(ShinyTabBar), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.ApplySurface()));

    /// <summary>Backing store for <see cref="BarCornerRadius"/>.</summary>
    public static readonly BindableProperty BarCornerRadiusProperty = BindableProperty.Create(
        nameof(BarCornerRadius), typeof(double), typeof(ShinyTabBar), ThemeTokens.Unset,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.ApplySurface()));

    /// <summary>Backing store for <see cref="BarMargin"/>.</summary>
    public static readonly BindableProperty BarMarginProperty = BindableProperty.Create(
        nameof(BarMargin), typeof(Thickness), typeof(ShinyTabBar), default(Thickness),
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.ApplySurface()));

    /// <summary>Backing store for <see cref="BarPadding"/>.</summary>
    public static readonly BindableProperty BarPaddingProperty = BindableProperty.Create(
        nameof(BarPadding), typeof(Thickness), typeof(ShinyTabBar), new Thickness(4, 6),
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.ApplySurface()));

    /// <summary>Backing store for <see cref="HasShadow"/>.</summary>
    public static readonly BindableProperty HasShadowProperty = BindableProperty.Create(
        nameof(HasShadow), typeof(bool), typeof(ShinyTabBar), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.ApplySurface()));

    /// <summary>Backing store for <see cref="BarMaterial"/>.</summary>
    public static readonly BindableProperty BarMaterialProperty = BindableProperty.Create(
        nameof(BarMaterial), typeof(TabBarMaterial), typeof(ShinyTabBar), TabBarMaterial.Solid,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.ApplySurface()));

    /// <summary>Backing store for <see cref="BarGlassTint"/>.</summary>
    public static readonly BindableProperty BarGlassTintProperty = BindableProperty.Create(
        nameof(BarGlassTint), typeof(Color), typeof(ShinyTabBar), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.ApplySurface()));

    /// <summary>Backing store for <see cref="BarBackgroundOpacity"/>.</summary>
    public static readonly BindableProperty BarBackgroundOpacityProperty = BindableProperty.Create(
        nameof(BarBackgroundOpacity), typeof(double), typeof(ShinyTabBar), 1d,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.ApplySurface()));

    /// <summary>
    /// Whether the bar paints a background or sits on Apple's Liquid Glass. <see cref="TabBarMaterial.Solid"/>
    /// by default.
    /// </summary>
    /// <remarks>
    /// <para>Glass is <b>iOS 26 and up</b>. Anywhere else the property is remembered and ignored, and
    /// the bar paints the fill it always did — a glass bar that silently went transparent on Android
    /// would just be a missing bar.</para>
    /// <para>Where it does take effect it takes over the background entirely:
    /// <see cref="BarBackgroundColor"/> and <see cref="BarBackgroundOpacity"/> stop being painted
    /// (use <see cref="BarGlassTint"/> to colour the glass), and <see cref="HasShadow"/> is ignored,
    /// because glass carries its own edge shading and a Material drop shadow under it reads as a
    /// sticker rather than as depth.</para>
    /// <para>The content is allowed to run underneath the bar for you, exactly as
    /// <see cref="TabBarStyle.Floating"/> already does — see <c>ShinyTabbedPage.ContentBehindTabBar</c>.</para>
    /// </remarks>
    public TabBarMaterial BarMaterial
    {
        get => (TabBarMaterial)this.GetValue(BarMaterialProperty);
        set => this.SetValue(BarMaterialProperty, value);
    }

    /// <summary>
    /// A colour wash over the glass. Unset leaves the system's own, which already answers light and
    /// dark on its own.
    /// </summary>
    /// <remarks>
    /// <para>A tint on glass is not a fill: the colour is blended into a surface that is still
    /// refracting what is behind it, so an opaque brand colour gives tinted glass rather than a
    /// tinted rectangle. Point it at a theme token — <c>SetDynamicResource</c> with
    /// <c>ShinyThemeKeys.Color.SurfaceContainer</c> or your primary — if the bar should follow the
    /// theme rather than the system.</para>
    /// <para>Ignored when <see cref="BarMaterial"/> is <see cref="TabBarMaterial.Solid"/>, and on
    /// every head that has no glass to tint.</para>
    /// </remarks>
    public Color? BarGlassTint
    {
        get => (Color?)this.GetValue(BarGlassTintProperty);
        set => this.SetValue(BarGlassTintProperty, value);
    }

    /// <summary>
    /// How opaque the bar's background is, from <c>0</c> (invisible) to <c>1</c> (solid, the default).
    /// </summary>
    /// <remarks>
    /// <para>The <b>background only</b> — the icons, labels, badges and indicator stay fully opaque.
    /// A bar whose tabs fade along with its background is not a translucent bar, it is a faded one,
    /// and it is unreadable well before the background is interesting.</para>
    /// <para>It multiplies into whatever alpha the colour already had, so a semi-transparent
    /// <see cref="BarBackgroundColor"/> keeps what it asked for and follows a theme swap.</para>
    /// <para>Worth pairing with <c>ShinyTabbedPage.ContentBehindTabBar</c>: without it the content
    /// stops above the bar and there is nothing behind the glass to see.</para>
    /// </remarks>
    public double BarBackgroundOpacity
    {
        get => (double)this.GetValue(BarBackgroundOpacityProperty);
        set => this.SetValue(BarBackgroundOpacityProperty, value);
    }

    /// <summary>Backing store for <see cref="MaxVisibleTabs"/>.</summary>
    public static readonly BindableProperty MaxVisibleTabsProperty = BindableProperty.Create(
        nameof(MaxVisibleTabs), typeof(int), typeof(ShinyTabBar), 0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.RebuildCells()));

    /// <summary>
    /// How many cells the bar will draw before the rest fold into an overflow tab. <c>0</c> — the
    /// default — works it out from the bar's own width and <see cref="MinTabWidth"/>.
    /// </summary>
    /// <remarks>
    /// The overflow tab counts as one of them, so <c>MaxVisibleTabs="4"</c> over six tabs draws three
    /// real tabs and a <b>More</b>. A value of <c>1</c> is treated as <c>2</c>: a bar that is nothing
    /// but an overflow button is not a tab bar.
    /// </remarks>
    public int MaxVisibleTabs
    {
        get => (int)this.GetValue(MaxVisibleTabsProperty);
        set => this.SetValue(MaxVisibleTabsProperty, value);
    }

    /// <summary>Backing store for <see cref="MinTabWidth"/>.</summary>
    public static readonly BindableProperty MinTabWidthProperty = BindableProperty.Create(
        nameof(MinTabWidth), typeof(double), typeof(ShinyTabBar), 72d,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.RebuildCells()));

    /// <summary>
    /// The narrowest a tab may become before the bar starts folding tabs away. This is what
    /// "overflow" actually means when <see cref="MaxVisibleTabs"/> is left at <c>0</c>.
    /// </summary>
    /// <remarks>
    /// A width rather than a count, because the count that fits is not a property of the bar — six
    /// tabs are fine on a tablet and unreadable on a phone in portrait, and the same app runs on
    /// both. Raise it for longer labels; lower it for an icon-only bar.
    /// </remarks>
    public double MinTabWidth
    {
        get => (double)this.GetValue(MinTabWidthProperty);
        set => this.SetValue(MinTabWidthProperty, value);
    }

    /// <summary>Backing store for <see cref="OverflowTitle"/>.</summary>
    public static readonly BindableProperty OverflowTitleProperty = BindableProperty.Create(
        nameof(OverflowTitle), typeof(string), typeof(ShinyTabBar), "More",
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.RebuildCells()));

    /// <summary>The overflow tab's label.</summary>
    public string OverflowTitle
    {
        get => (string)this.GetValue(OverflowTitleProperty);
        set => this.SetValue(OverflowTitleProperty, value);
    }

    /// <summary>Backing store for <see cref="OverflowIcon"/>.</summary>
    public static readonly BindableProperty OverflowIconProperty = BindableProperty.Create(
        nameof(OverflowIcon), typeof(string), typeof(ShinyTabBar), "more",
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.RebuildCells()));

    /// <summary>The motion icon the overflow tab draws. Defaults to the built-in <c>more</c> glyph.</summary>
    public string? OverflowIcon
    {
        get => (string?)this.GetValue(OverflowIconProperty);
        set => this.SetValue(OverflowIconProperty, value);
    }

    /// <summary>Backing store for <see cref="RespectSafeArea"/>.</summary>
    public static readonly BindableProperty RespectSafeAreaProperty = BindableProperty.Create(
        nameof(RespectSafeArea), typeof(bool), typeof(ShinyTabBar), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.ApplySurface()));

    /// <summary>Backing store for <see cref="SelectedColor"/>.</summary>
    public static readonly BindableProperty SelectedColorProperty = BindableProperty.Create(
        nameof(SelectedColor), typeof(Color), typeof(ShinyTabBar), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.ApplyAllCellStates()));

    /// <summary>Backing store for <see cref="UnselectedColor"/>.</summary>
    public static readonly BindableProperty UnselectedColorProperty = BindableProperty.Create(
        nameof(UnselectedColor), typeof(Color), typeof(ShinyTabBar), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.ApplyAllCellStates()));

    /// <summary>Backing store for <see cref="IndicatorColor"/>.</summary>
    public static readonly BindableProperty IndicatorColorProperty = BindableProperty.Create(
        nameof(IndicatorColor), typeof(Color), typeof(ShinyTabBar), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.ApplyAllCellStates()));

    /// <summary>Backing store for <see cref="IndicatorTransition"/>.</summary>
    public static readonly BindableProperty IndicatorTransitionProperty = BindableProperty.Create(
        nameof(IndicatorTransition), typeof(TabIndicatorTransition), typeof(ShinyTabBar), TabIndicatorTransition.Slide,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.ApplyAllCellStates()));

    /// <summary>Backing store for <see cref="IndicatorEasing"/>.</summary>
    public static readonly BindableProperty IndicatorEasingProperty = BindableProperty.Create(
        nameof(IndicatorEasing), typeof(Easing), typeof(ShinyTabBar), Easing.CubicInOut);

    /// <summary>Backing store for <see cref="IndicatorStyle"/>.</summary>
    public static readonly BindableProperty IndicatorStyleProperty = BindableProperty.Create(
        nameof(IndicatorStyle), typeof(TabIndicatorStyle), typeof(ShinyTabBar), TabIndicatorStyle.Pill,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.ApplyAllCellStates()));

    /// <summary>Backing store for <see cref="LabelMode"/>.</summary>
    public static readonly BindableProperty LabelModeProperty = BindableProperty.Create(
        nameof(LabelMode), typeof(TabLabelMode), typeof(ShinyTabBar), TabLabelMode.Always,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.ApplyAllCellStates()));

    /// <summary>Backing store for <see cref="IconSize"/>.</summary>
    public static readonly BindableProperty IconSizeProperty = BindableProperty.Create(
        nameof(IconSize), typeof(double), typeof(ShinyTabBar), 24d,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.RebuildCells()));

    /// <summary>Backing store for <see cref="FontSize"/>.</summary>
    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize), typeof(double), typeof(ShinyTabBar), 11d,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.ApplyAllCellStates()));

    /// <summary>Backing store for <see cref="AnimateIcons"/>.</summary>
    public static readonly BindableProperty AnimateIconsProperty = BindableProperty.Create(
        nameof(AnimateIcons), typeof(bool), typeof(ShinyTabBar), true);

    /// <summary>Backing store for <see cref="AnimationDuration"/>.</summary>
    public static readonly BindableProperty AnimationDurationProperty = BindableProperty.Create(
        nameof(AnimationDuration), typeof(uint), typeof(ShinyTabBar), 200u);

    /// <summary>Backing store for <see cref="SelectionAnimation"/>.</summary>
    public static readonly BindableProperty SelectionAnimationProperty = BindableProperty.Create(
        nameof(SelectionAnimation), typeof(TabSelectionAnimation), typeof(ShinyTabBar), TabSelectionAnimation.Scale);

    /// <summary>Backing store for <see cref="Animator"/>.</summary>
    public static readonly BindableProperty AnimatorProperty = BindableProperty.Create(
        nameof(Animator), typeof(ITabAnimator), typeof(ShinyTabBar), null);

    /// <summary>Backing store for <see cref="CenterButton"/>.</summary>
    public static readonly BindableProperty CenterButtonProperty = BindableProperty.Create(
        nameof(CenterButton), typeof(TabCenterButton), typeof(ShinyTabBar), null,
        propertyChanged: (b, o, n) =>
        {
            // Not gated: swapping the subscription touches no children, and losing it would leave the
            // old button driving the bar. Only the rebuild waits for the constructor.
            var bar = (ShinyTabBar)b;
            if (o is TabCenterButton previous)
            {
                previous.PropertyChanged -= bar.OnCenterButtonChanged;
                previous.Actions.CollectionChanged -= bar.OnCenterActionsChanged;
            }
            if (n is TabCenterButton next)
            {
                next.PropertyChanged += bar.OnCenterButtonChanged;
                next.Actions.CollectionChanged += bar.OnCenterActionsChanged;
            }
            StyleGuard.WhenReady<ShinyTabBar>(b, x => x.RebuildCells());
        });

    /// <summary>Backing store for <see cref="MenuTemplate"/>.</summary>
    public static readonly BindableProperty MenuTemplateProperty = BindableProperty.Create(
        nameof(MenuTemplate), typeof(DataTemplate), typeof(ShinyTabBar), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar =>
        {
            if (bar.IsMenuOpen)
                bar.RefreshMenuContent();
        }));

    /// <summary>Backing store for <see cref="PageContext"/>.</summary>
    public static readonly BindableProperty PageContextProperty = BindableProperty.Create(
        nameof(PageContext), typeof(BindableObject), typeof(ShinyTabBar), null,
        propertyChanged: (b, o, n) =>
        {
            var bar = (ShinyTabBar)b;
            if (o is BindableObject previous)
                previous.PropertyChanged -= bar.OnPageContextPropertyChanged;
            if (n is BindableObject next)
                next.PropertyChanged += bar.OnPageContextPropertyChanged;

            StyleGuard.WhenReady<ShinyTabBar>(b, x => x.ApplyAllCellStates());
        });

    /// <summary>Backing store for <see cref="IsMenuOpen"/>.</summary>
    public static readonly BindableProperty IsMenuOpenProperty = BindableProperty.Create(
        nameof(IsMenuOpen), typeof(bool), typeof(ShinyTabBar), false, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.SyncMenuState((bool)n)));

    /// <summary>Backing store for <see cref="SelectionChangedCommand"/>.</summary>
    public static readonly BindableProperty SelectionChangedCommandProperty = BindableProperty.Create(
        nameof(SelectionChangedCommand), typeof(ICommand), typeof(ShinyTabBar), null);


    /// <summary>The tabs, in bar order. The content property, so they can be listed inline in XAML.</summary>
    public IList<ShinyTabItem> Items => this.items;

    /// <summary>
    /// The selected tab's index into <see cref="Items"/>, counting hidden tabs. -1 when nothing is
    /// selected — which is what an empty bar reports, and what a bar whose selected tab was removed
    /// falls back to.
    /// </summary>
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
    /// Docked to the bottom edge, or a capsule floating over the content. Defaults to
    /// <see cref="TabBarStyle.Docked"/>.
    /// </summary>
    /// <remarks>
    /// Switching to <see cref="TabBarStyle.Floating"/> supplies a margin and a capsule corner radius
    /// of its own, and lets the page's content run underneath the bar. Setting
    /// <see cref="BarMargin"/> or <see cref="BarCornerRadius"/> explicitly still wins over both.
    /// </remarks>
    public TabBarStyle BarStyle
    {
        get => (TabBarStyle)this.GetValue(BarStyleProperty);
        set => this.SetValue(BarStyleProperty, value);
    }

    /// <summary>Height of the bar itself, excluding anything the centre button rises above it.</summary>
    public double BarHeight
    {
        get => (double)this.GetValue(BarHeightProperty);
        set => this.SetValue(BarHeightProperty, value);
    }

    /// <summary>Unset follows the theme's surface-container colour.</summary>
    public Color? BarBackgroundColor
    {
        get => (Color?)this.GetValue(BarBackgroundColorProperty);
        set => this.SetValue(BarBackgroundColorProperty, value);
    }

    /// <summary>
    /// Rounds the bar. Pair it with <see cref="BarMargin"/> for a floating, detached bar. Negative
    /// (the default) follows the theme's corner token, which is square for every pack that ships.
    /// </summary>
    public double BarCornerRadius
    {
        get => (double)this.GetValue(BarCornerRadiusProperty);
        set => this.SetValue(BarCornerRadiusProperty, value);
    }

    /// <summary>Insets the bar from the edges it is docked against.</summary>
    public Thickness BarMargin
    {
        get => (Thickness)this.GetValue(BarMarginProperty);
        set => this.SetValue(BarMarginProperty, value);
    }

    /// <summary>Padding between the bar's edges and the tabs.</summary>
    public Thickness BarPadding
    {
        get => (Thickness)this.GetValue(BarPaddingProperty);
        set => this.SetValue(BarPaddingProperty, value);
    }

    /// <summary>Lifts the bar off the content beneath it.</summary>
    public bool HasShadow
    {
        get => (bool)this.GetValue(HasShadowProperty);
        set => this.SetValue(HasShadowProperty, value);
    }

    /// <summary>
    /// Insets the tabs out of the bottom safe area — the home indicator, the gesture bar — while the
    /// bar's background still paints all the way to the screen edge. On by default. Turn it off for
    /// a bar that is not docked to the bottom of a page, or one already inside a safe-area-aware
    /// container.
    /// </summary>
    public bool RespectSafeArea
    {
        get => (bool)this.GetValue(RespectSafeAreaProperty);
        set => this.SetValue(RespectSafeAreaProperty, value);
    }

    /// <summary>Unset follows the theme's primary colour.</summary>
    public Color? SelectedColor
    {
        get => (Color?)this.GetValue(SelectedColorProperty);
        set => this.SetValue(SelectedColorProperty, value);
    }

    /// <summary>Unset follows the theme's on-surface-variant colour.</summary>
    public Color? UnselectedColor
    {
        get => (Color?)this.GetValue(UnselectedColorProperty);
        set => this.SetValue(UnselectedColorProperty, value);
    }

    /// <summary>Unset follows the theme's secondary-container colour.</summary>
    public Color? IndicatorColor
    {
        get => (Color?)this.GetValue(IndicatorColorProperty);
        set => this.SetValue(IndicatorColorProperty, value);
    }

    /// <summary>
    /// Whether the indicator travels from the old tab to the new one. Defaults to
    /// <see cref="TabIndicatorTransition.Slide"/>.
    /// </summary>
    /// <remarks>
    /// Sliding needs measured geometry, which does not exist until the bar has been laid out. Until
    /// it does — and on any tab the bar cannot measure — the indicator falls back to
    /// <see cref="TabIndicatorTransition.None"/> and is drawn inside the cell, so the first frame is
    /// correct rather than blank or parked in the corner.
    /// </remarks>
    public TabIndicatorTransition IndicatorTransition
    {
        get => (TabIndicatorTransition)this.GetValue(IndicatorTransitionProperty);
        set => this.SetValue(IndicatorTransitionProperty, value);
    }

    /// <summary>The travelling indicator's easing. Defaults to <see cref="Easing.CubicInOut"/>.</summary>
    public Easing IndicatorEasing
    {
        get => (Easing)this.GetValue(IndicatorEasingProperty);
        set => this.SetValue(IndicatorEasingProperty, value);
    }

    /// <summary>How the selected tab is marked. Defaults to <see cref="TabIndicatorStyle.Pill"/>.</summary>
    public TabIndicatorStyle IndicatorStyle
    {
        get => (TabIndicatorStyle)this.GetValue(IndicatorStyleProperty);
        set => this.SetValue(IndicatorStyleProperty, value);
    }

    /// <summary>When tabs show their labels. Defaults to <see cref="TabLabelMode.Always"/>.</summary>
    public TabLabelMode LabelMode
    {
        get => (TabLabelMode)this.GetValue(LabelModeProperty);
        set => this.SetValue(LabelModeProperty, value);
    }

    /// <summary>Icon size in the tabs. Defaults to 24, the size the motion icons are drawn for.</summary>
    public double IconSize
    {
        get => (double)this.GetValue(IconSizeProperty);
        set => this.SetValue(IconSizeProperty, value);
    }

    /// <summary>Label size. Defaults to 11.</summary>
    public double FontSize
    {
        get => (double)this.GetValue(FontSizeProperty);
        set => this.SetValue(FontSizeProperty, value);
    }

    /// <summary>Play a tab's motion icon when it becomes selected. Plain image icons are unaffected.</summary>
    public bool AnimateIcons
    {
        get => (bool)this.GetValue(AnimateIconsProperty);
        set => this.SetValue(AnimateIconsProperty, value);
    }

    /// <summary>How long the bar's own animations run — the indicator, the menu. Milliseconds.</summary>
    public uint AnimationDuration
    {
        get => (uint)this.GetValue(AnimationDurationProperty);
        set => this.SetValue(AnimationDurationProperty, value);
    }

    /// <summary>
    /// How a tab animates as it becomes selected and as it stops being. Defaults to
    /// <see cref="TabSelectionAnimation.Scale"/>. Ignored when <see cref="Animator"/> is set.
    /// </summary>
    public TabSelectionAnimation SelectionAnimation
    {
        get => (TabSelectionAnimation)this.GetValue(SelectionAnimationProperty);
        set => this.SetValue(SelectionAnimationProperty, value);
    }

    /// <summary>
    /// Replaces <see cref="SelectionAnimation"/> with your own. Called once per tab whose selected
    /// state actually changed, with the cell, the icon, the label and the indicator handed over
    /// separately.
    /// </summary>
    public ITabAnimator? Animator
    {
        get => (ITabAnimator?)this.GetValue(AnimatorProperty);
        set => this.SetValue(AnimatorProperty, value);
    }

    /// <summary>
    /// The raised button in the middle. Null (the default) gives an ordinary bar; setting one splits
    /// the tabs around it.
    /// </summary>
    public TabCenterButton? CenterButton
    {
        get => (TabCenterButton?)this.GetValue(CenterButtonProperty);
        set => this.SetValue(CenterButtonProperty, value);
    }

    /// <summary>
    /// Replaces everything inside the centre menu's card — the rows, their layout, all of it — with
    /// your own. The bar keeps the backdrop, the anchoring above the button, and the open/close
    /// animation.
    /// </summary>
    /// <remarks>
    /// Beats every other menu source, including a page's <see cref="ShinyTabs.MenuContentProperty"/>,
    /// because it is the bar-wide chrome decision. Build the template's contents from
    /// <see cref="ResolveMenuActions"/> if you want the pages to keep declaring rows; the binding
    /// context handed to it is the current page's, so bindings written in it resolve there.
    /// </remarks>
    public DataTemplate? MenuTemplate
    {
        get => (DataTemplate?)this.GetValue(MenuTemplateProperty);
        set => this.SetValue(MenuTemplateProperty, value);
    }

    /// <summary>
    /// The page the bar is currently showing chrome for. The bar reads <see cref="ShinyTabs"/>
    /// attached properties off it — the selected tab's badge, and what the centre button presents.
    /// </summary>
    /// <remarks>
    /// Set for you: <see cref="ShinyTabbedPage"/> points it at the selected tab's content and
    /// <see cref="ShinyTabBarBehavior"/> points it at the Shell's current page. Set it yourself only
    /// when hosting the bar somewhere neither of those covers.
    /// </remarks>
    public BindableObject? PageContext
    {
        get => (BindableObject?)this.GetValue(PageContextProperty);
        set => this.SetValue(PageContextProperty, value);
    }

    /// <summary>Whether the centre menu is showing. Two-way, so it can be opened from a view model.</summary>
    public bool IsMenuOpen
    {
        get => (bool)this.GetValue(IsMenuOpenProperty);
        set => this.SetValue(IsMenuOpenProperty, value);
    }

    /// <summary>Invoked with the newly selected <see cref="ShinyTabItem"/> after every change.</summary>
    public ICommand? SelectionChangedCommand
    {
        get => (ICommand?)this.GetValue(SelectionChangedCommandProperty);
        set => this.SetValue(SelectionChangedCommandProperty, value);
    }


    /// <summary>Raised after the selected tab changes.</summary>
    public event EventHandler<TabSelectionChangedEventArgs>? SelectionChanged;

    /// <summary>Raised when the already-selected tab is tapped again.</summary>
    public event EventHandler<TabReselectedEventArgs>? TabReselected;

    /// <summary>
    /// Raised when the centre button is pressed, before anything is presented. Cancel it to handle
    /// the press entirely yourself.
    /// </summary>
    public event EventHandler<TabCenterClickedEventArgs>? CenterClicked;

    /// <summary>Raised when a row of the centre menu is tapped, after its own command has run.</summary>
    public event EventHandler<TabActionEventArgs>? ActionInvoked;

    /// <summary>Raised once the centre menu is on screen.</summary>
    public event EventHandler? MenuOpened;

    /// <summary>Raised once the centre menu has closed.</summary>
    public event EventHandler? MenuClosed;
}
