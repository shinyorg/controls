using System.Windows.Input;
using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

public partial class ShinyNavBar
{
    static void Apply(BindableObject b, Action<ShinyNavBar> apply)
        => StyleGuard.WhenReady(b, typeof(ShinyNavBar), () => apply((ShinyNavBar)b));


    // ---------------------------------------------------------------------------------------------
    // Title
    // ---------------------------------------------------------------------------------------------

    /// <summary>Backing store for <see cref="Title"/>.</summary>
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(ShinyNavBar), null,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyTitle()));

    /// <summary>The bar's title. A hosted page's <see cref="Page.Title"/> is pushed into this.</summary>
    public string? Title
    {
        get => (string?)this.GetValue(TitleProperty);
        set => this.SetValue(TitleProperty, value);
    }

    /// <summary>Backing store for <see cref="Subtitle"/>.</summary>
    public static readonly BindableProperty SubtitleProperty = BindableProperty.Create(
        nameof(Subtitle), typeof(string), typeof(ShinyNavBar), null,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyTitle()));

    /// <summary>A second, smaller line under the title. Nothing is drawn when it is empty.</summary>
    public string? Subtitle
    {
        get => (string?)this.GetValue(SubtitleProperty);
        set => this.SetValue(SubtitleProperty, value);
    }

    /// <summary>Backing store for <see cref="LargeTitle"/>.</summary>
    public static readonly BindableProperty LargeTitleProperty = BindableProperty.Create(
        nameof(LargeTitle), typeof(string), typeof(ShinyNavBar), null,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyTitle()));

    /// <summary>The large title's text. Falls back to <see cref="Title"/> when unset.</summary>
    public string? LargeTitle
    {
        get => (string?)this.GetValue(LargeTitleProperty);
        set => this.SetValue(LargeTitleProperty, value);
    }

    /// <summary>Backing store for <see cref="TitleView"/>.</summary>
    public static readonly BindableProperty TitleViewProperty = BindableProperty.Create(
        nameof(TitleView), typeof(View), typeof(ShinyNavBar), null,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyTitleView()));

    /// <summary>
    /// Replaces the title text outright — a search field, a segmented control. It is laid out in the
    /// same slot, so the item groups still bound it.
    /// </summary>
    public View? TitleView
    {
        get => (View?)this.GetValue(TitleViewProperty);
        set => this.SetValue(TitleViewProperty, value);
    }

    /// <summary>Backing store for <see cref="TitleIcon"/>.</summary>
    public static readonly BindableProperty TitleIconProperty = BindableProperty.Create(
        nameof(TitleIcon), typeof(ImageSource), typeof(ShinyNavBar), null,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyTitle()));

    /// <summary>Artwork drawn immediately before the title — a logo, a workspace avatar.</summary>
    public ImageSource? TitleIcon
    {
        get => (ImageSource?)this.GetValue(TitleIconProperty);
        set => this.SetValue(TitleIconProperty, value);
    }

    /// <summary>Backing store for <see cref="TitleAlignment"/>.</summary>
    public static readonly BindableProperty TitleAlignmentProperty = BindableProperty.Create(
        nameof(TitleAlignment), typeof(NavBarTitleAlignment), typeof(ShinyNavBar), NavBarTitleAlignment.Auto,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyTitleAlignment()));

    /// <inheritdoc cref="NavBarTitleAlignment"/>
    public NavBarTitleAlignment TitleAlignment
    {
        get => (NavBarTitleAlignment)this.GetValue(TitleAlignmentProperty);
        set => this.SetValue(TitleAlignmentProperty, value);
    }

    /// <summary>Backing store for <see cref="TitleFontSize"/>.</summary>
    public static readonly BindableProperty TitleFontSizeProperty = BindableProperty.Create(
        nameof(TitleFontSize), typeof(double), typeof(ShinyNavBar), ThemeTokens.Unset,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyTypography()));

    /// <summary>Unset follows the theme's title-large size.</summary>
    public double TitleFontSize
    {
        get => (double)this.GetValue(TitleFontSizeProperty);
        set => this.SetValue(TitleFontSizeProperty, value);
    }

    /// <summary>Backing store for <see cref="TitleFontFamily"/>.</summary>
    public static readonly BindableProperty TitleFontFamilyProperty = BindableProperty.Create(
        nameof(TitleFontFamily), typeof(string), typeof(ShinyNavBar), null,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyTypography()));

    /// <summary>Unset follows the theme's font family.</summary>
    public string? TitleFontFamily
    {
        get => (string?)this.GetValue(TitleFontFamilyProperty);
        set => this.SetValue(TitleFontFamilyProperty, value);
    }

    /// <summary>Backing store for <see cref="TitleFontAttributes"/>.</summary>
    public static readonly BindableProperty TitleFontAttributesProperty = BindableProperty.Create(
        nameof(TitleFontAttributes), typeof(FontAttributes), typeof(ShinyNavBar), FontAttributes.Bold,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyTypography()));

    /// <summary>Bold by default, which is what every platform's own bar does.</summary>
    public FontAttributes TitleFontAttributes
    {
        get => (FontAttributes)this.GetValue(TitleFontAttributesProperty);
        set => this.SetValue(TitleFontAttributesProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // Large title
    // ---------------------------------------------------------------------------------------------

    /// <summary>Backing store for <see cref="LargeTitleDisplay"/>.</summary>
    public static readonly BindableProperty LargeTitleDisplayProperty = BindableProperty.Create(
        nameof(LargeTitleDisplay), typeof(LargeTitleDisplay), typeof(ShinyNavBar), LargeTitleDisplay.None,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyLargeTitle()));

    /// <inheritdoc cref="Shiny.Maui.Controls.LargeTitleDisplay"/>
    public LargeTitleDisplay LargeTitleDisplay
    {
        get => (LargeTitleDisplay)this.GetValue(LargeTitleDisplayProperty);
        set => this.SetValue(LargeTitleDisplayProperty, value);
    }

    /// <summary>Backing store for <see cref="LargeTitleHeight"/>.</summary>
    public static readonly BindableProperty LargeTitleHeightProperty = BindableProperty.Create(
        nameof(LargeTitleHeight), typeof(double), typeof(ShinyNavBar), 52d,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyLargeTitle()));

    /// <summary>How tall the large title's row is at rest.</summary>
    public double LargeTitleHeight
    {
        get => (double)this.GetValue(LargeTitleHeightProperty);
        set => this.SetValue(LargeTitleHeightProperty, value);
    }

    /// <summary>Backing store for <see cref="LargeTitleFontSize"/>.</summary>
    public static readonly BindableProperty LargeTitleFontSizeProperty = BindableProperty.Create(
        nameof(LargeTitleFontSize), typeof(double), typeof(ShinyNavBar), ThemeTokens.Unset,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyTypography()));

    /// <summary>Unset follows the theme's headline-medium size.</summary>
    public double LargeTitleFontSize
    {
        get => (double)this.GetValue(LargeTitleFontSizeProperty);
        set => this.SetValue(LargeTitleFontSizeProperty, value);
    }

    /// <summary>Backing store for <see cref="LargeTitleCollapseDistance"/>.</summary>
    public static readonly BindableProperty LargeTitleCollapseDistanceProperty = BindableProperty.Create(
        nameof(LargeTitleCollapseDistance), typeof(double), typeof(ShinyNavBar), 48d);

    /// <summary>
    /// How far the page has to scroll for the large title to finish collapsing. Values at or below
    /// zero collapse it the moment the page moves at all.
    /// </summary>
    public double LargeTitleCollapseDistance
    {
        get => (double)this.GetValue(LargeTitleCollapseDistanceProperty);
        set => this.SetValue(LargeTitleCollapseDistanceProperty, value);
    }

    /// <summary>
    /// How far the large title has collapsed: 0 fully open, 1 fully folded into the inline title.
    /// Driven by the scroll source; readable so a page can follow it (a header image parallax, say).
    /// </summary>
    public static readonly BindableProperty CollapseProgressProperty = BindableProperty.Create(
        nameof(CollapseProgress), typeof(double), typeof(ShinyNavBar), 0d, BindingMode.OneWayToSource,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyCollapse()));

    /// <inheritdoc cref="CollapseProgressProperty"/>
    public double CollapseProgress
    {
        get => (double)this.GetValue(CollapseProgressProperty);
        set => this.SetValue(CollapseProgressProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // Appearance
    // ---------------------------------------------------------------------------------------------

    /// <summary>Backing store for <see cref="BarBackgroundColor"/>.</summary>
    public static readonly BindableProperty BarBackgroundColorProperty = BindableProperty.Create(
        nameof(BarBackgroundColor), typeof(Color), typeof(ShinyNavBar), null,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyBarSurface()));

    /// <summary>Unset follows the theme's surface-container colour.</summary>
    public Color? BarBackgroundColor
    {
        get => (Color?)this.GetValue(BarBackgroundColorProperty);
        set => this.SetValue(BarBackgroundColorProperty, value);
    }

    /// <summary>Backing store for <see cref="BarBackground"/>.</summary>
    public static readonly BindableProperty BarBackgroundProperty = BindableProperty.Create(
        nameof(BarBackground), typeof(Brush), typeof(ShinyNavBar), null,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyBarSurface()));

    /// <summary>A gradient or image fill. Beats <see cref="BarBackgroundColor"/>.</summary>
    public Brush? BarBackground
    {
        get => (Brush?)this.GetValue(BarBackgroundProperty);
        set => this.SetValue(BarBackgroundProperty, value);
    }

    /// <summary>Backing store for <see cref="BarTextColor"/>.</summary>
    public static readonly BindableProperty BarTextColorProperty = BindableProperty.Create(
        nameof(BarTextColor), typeof(Color), typeof(ShinyNavBar), null,
        propertyChanged: (b, _, _) => Apply(b, bar =>
        {
            bar.ApplyTypography();
            bar.RebuildItems();
        }));

    /// <summary>The title's and the items' colour. Unset follows the theme's on-surface colour.</summary>
    public Color? BarTextColor
    {
        get => (Color?)this.GetValue(BarTextColorProperty);
        set => this.SetValue(BarTextColorProperty, value);
    }

    /// <summary>Backing store for <see cref="IconColor"/>.</summary>
    public static readonly BindableProperty IconColorProperty = BindableProperty.Create(
        nameof(IconColor), typeof(Color), typeof(ShinyNavBar), null,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.RebuildItems()));

    /// <summary>
    /// Tints the back chevron and the items' motion artwork. Unset follows
    /// <see cref="BarTextColor"/>, and then the theme.
    /// </summary>
    public Color? IconColor
    {
        get => (Color?)this.GetValue(IconColorProperty);
        set => this.SetValue(IconColorProperty, value);
    }

    /// <summary>Backing store for <see cref="BarHeight"/>.</summary>
    public static readonly BindableProperty BarHeightProperty = BindableProperty.Create(
        nameof(BarHeight), typeof(double), typeof(ShinyNavBar), 56d,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyBarSurface()));

    /// <summary>The bar row's height, not counting the large title beneath it.</summary>
    public double BarHeight
    {
        get => (double)this.GetValue(BarHeightProperty);
        set => this.SetValue(BarHeightProperty, value);
    }

    /// <summary>Backing store for <see cref="BarPadding"/>.</summary>
    public static readonly BindableProperty BarPaddingProperty = BindableProperty.Create(
        nameof(BarPadding), typeof(Thickness), typeof(ShinyNavBar), new Thickness(4, 0),
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyBarSurface()));

    /// <summary>Inset around the bar's contents.</summary>
    public Thickness BarPadding
    {
        get => (Thickness)this.GetValue(BarPaddingProperty);
        set => this.SetValue(BarPaddingProperty, value);
    }

    /// <summary>Backing store for <see cref="RespectSafeArea"/>.</summary>
    public static readonly BindableProperty RespectSafeAreaProperty = BindableProperty.Create(
        nameof(RespectSafeArea), typeof(bool), typeof(ShinyNavBar), true,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyBarSurface()));

    /// <summary>
    /// Extends the bar's background up through the status bar, notch and Dynamic Island while
    /// keeping the title and the items below them. On by default.
    /// </summary>
    /// <remarks>
    /// <para>The inset goes on the bar's background surface rather than on the bar, which is the
    /// whole distinction: insetting the bar itself would leave a strip of page showing above it in
    /// the colour of the page, not of the bar. Because the background is what fills that strip, this
    /// is also what makes the status bar appear to take the bar's colour — neither iOS nor Android
    /// 15 has a status bar background to set.</para>
    /// <para>Turn it off for a bar that is meant to sit under the status bar — a media or camera
    /// overlay on a full-bleed page.</para>
    /// </remarks>
    public bool RespectSafeArea
    {
        get => (bool)this.GetValue(RespectSafeAreaProperty);
        set => this.SetValue(RespectSafeAreaProperty, value);
    }

    /// <summary>Backing store for <see cref="HasShadow"/>.</summary>
    public static readonly BindableProperty HasShadowProperty = BindableProperty.Create(
        nameof(HasShadow), typeof(bool), typeof(ShinyNavBar), true,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyBarSurface()));

    /// <summary>Lifts the bar off the page content. On by default.</summary>
    public bool HasShadow
    {
        get => (bool)this.GetValue(HasShadowProperty);
        set => this.SetValue(HasShadowProperty, value);
    }

    /// <summary>Backing store for <see cref="HasSeparator"/>.</summary>
    public static readonly BindableProperty HasSeparatorProperty = BindableProperty.Create(
        nameof(HasSeparator), typeof(bool), typeof(ShinyNavBar), false,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyBarSurface()));

    /// <summary>A hairline along the bar's bottom edge — the flat alternative to <see cref="HasShadow"/>.</summary>
    public bool HasSeparator
    {
        get => (bool)this.GetValue(HasSeparatorProperty);
        set => this.SetValue(HasSeparatorProperty, value);
    }

    /// <summary>Backing store for <see cref="ItemSpacing"/>.</summary>
    public static readonly BindableProperty ItemSpacingProperty = BindableProperty.Create(
        nameof(ItemSpacing), typeof(double), typeof(ShinyNavBar), 2d,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyItemMetrics()));

    /// <summary>Gap between neighbouring items.</summary>
    public double ItemSpacing
    {
        get => (double)this.GetValue(ItemSpacingProperty);
        set => this.SetValue(ItemSpacingProperty, value);
    }

    /// <summary>Backing store for <see cref="IconSize"/>.</summary>
    public static readonly BindableProperty IconSizeProperty = BindableProperty.Create(
        nameof(IconSize), typeof(double), typeof(ShinyNavBar), 22d,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.RebuildItems()));

    /// <summary>How big an item's artwork is drawn. The tap target around it is larger.</summary>
    public double IconSize
    {
        get => (double)this.GetValue(IconSizeProperty);
        set => this.SetValue(IconSizeProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // Items and overflow
    // ---------------------------------------------------------------------------------------------

    /// <summary>Backing store for <see cref="MaxVisibleItems"/>.</summary>
    public static readonly BindableProperty MaxVisibleItemsProperty = BindableProperty.Create(
        nameof(MaxVisibleItems), typeof(int), typeof(ShinyNavBar), 3,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.RebuildItems()));

    /// <summary>
    /// How many items each side draws before the rest fold into that side's overflow menu. Zero or
    /// less means no limit, and then only <see cref="ToolbarItemOrder.Secondary"/> items overflow.
    /// </summary>
    public int MaxVisibleItems
    {
        get => (int)this.GetValue(MaxVisibleItemsProperty);
        set => this.SetValue(MaxVisibleItemsProperty, value);
    }

    /// <summary>Backing store for <see cref="OverflowIcon"/>.</summary>
    public static readonly BindableProperty OverflowIconProperty = BindableProperty.Create(
        nameof(OverflowIcon), typeof(string), typeof(ShinyNavBar), null,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.RebuildItems()));

    /// <summary>
    /// The motion icon the overflow button draws. Unset draws the three-dot glyph.
    /// </summary>
    public string? OverflowIcon
    {
        get => (string?)this.GetValue(OverflowIconProperty);
        set => this.SetValue(OverflowIconProperty, value);
    }

    /// <summary>Backing store for <see cref="MenuTemplate"/>.</summary>
    public static readonly BindableProperty MenuTemplateProperty = BindableProperty.Create(
        nameof(MenuTemplate), typeof(DataTemplate), typeof(ShinyNavBar), null);

    /// <summary>
    /// Replaces the overflow menu's card contents wholesale. The bar keeps the backdrop, the
    /// anchoring and the open/close animation; the rows are yours. The template's binding context is
    /// the <see cref="NavBarItemCollection"/> that overflowed.
    /// </summary>
    public DataTemplate? MenuTemplate
    {
        get => (DataTemplate?)this.GetValue(MenuTemplateProperty);
        set => this.SetValue(MenuTemplateProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // Back button
    // ---------------------------------------------------------------------------------------------

    /// <summary>Backing store for <see cref="IsBackButtonVisible"/>.</summary>
    public static readonly BindableProperty IsBackButtonVisibleProperty = BindableProperty.Create(
        nameof(IsBackButtonVisible), typeof(bool), typeof(ShinyNavBar), false,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyBackButton()));

    /// <summary>
    /// Whether the back affordance is drawn. A hosted page gets this from its place in the
    /// navigation stack and from <see cref="NavigationPage.SetHasBackButton(Page, bool)"/>.
    /// </summary>
    public bool IsBackButtonVisible
    {
        get => (bool)this.GetValue(IsBackButtonVisibleProperty);
        set => this.SetValue(IsBackButtonVisibleProperty, value);
    }

    /// <summary>Backing store for <see cref="BackButtonText"/>.</summary>
    public static readonly BindableProperty BackButtonTextProperty = BindableProperty.Create(
        nameof(BackButtonText), typeof(string), typeof(ShinyNavBar), null,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyBackButton()));

    /// <summary>
    /// A label beside the chevron. Unset draws the chevron alone —
    /// <see cref="NavigationPage.SetBackButtonTitle(BindableObject, string)"/> fills this in.
    /// </summary>
    public string? BackButtonText
    {
        get => (string?)this.GetValue(BackButtonTextProperty);
        set => this.SetValue(BackButtonTextProperty, value);
    }

    /// <summary>Backing store for <see cref="BackButtonIcon"/>.</summary>
    public static readonly BindableProperty BackButtonIconProperty = BindableProperty.Create(
        nameof(BackButtonIcon), typeof(string), typeof(ShinyNavBar), null,
        propertyChanged: (b, _, _) => Apply(b, bar => bar.ApplyBackButton()));

    /// <summary>A built-in motion icon name to draw instead of the chevron — <c>close</c>, say.</summary>
    public string? BackButtonIcon
    {
        get => (string?)this.GetValue(BackButtonIconProperty);
        set => this.SetValue(BackButtonIconProperty, value);
    }

    /// <summary>Backing store for <see cref="BackButtonCommand"/>.</summary>
    public static readonly BindableProperty BackButtonCommandProperty = BindableProperty.Create(
        nameof(BackButtonCommand), typeof(ICommand), typeof(ShinyNavBar), null);

    /// <summary>
    /// Runs instead of the bar's own back behaviour. Nothing is popped for you.
    /// </summary>
    public ICommand? BackButtonCommand
    {
        get => (ICommand?)this.GetValue(BackButtonCommandProperty);
        set => this.SetValue(BackButtonCommandProperty, value);
    }

    /// <summary>Backing store for <see cref="BackButtonCommandParameter"/>.</summary>
    public static readonly BindableProperty BackButtonCommandParameterProperty = BindableProperty.Create(
        nameof(BackButtonCommandParameter), typeof(object), typeof(ShinyNavBar), null);

    /// <summary>Passed to <see cref="BackButtonCommand"/>.</summary>
    public object? BackButtonCommandParameter
    {
        get => this.GetValue(BackButtonCommandParameterProperty);
        set => this.SetValue(BackButtonCommandParameterProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // Behaviour
    // ---------------------------------------------------------------------------------------------

    /// <summary>Backing store for <see cref="AnimationDuration"/>.</summary>
    public static readonly BindableProperty AnimationDurationProperty = BindableProperty.Create(
        nameof(AnimationDuration), typeof(uint), typeof(ShinyNavBar), (uint)180);

    /// <summary>How long the overflow menu and the title cross-fade take. Zero disables both.</summary>
    public uint AnimationDuration
    {
        get => (uint)this.GetValue(AnimationDurationProperty);
        set => this.SetValue(AnimationDurationProperty, value);
    }
}
