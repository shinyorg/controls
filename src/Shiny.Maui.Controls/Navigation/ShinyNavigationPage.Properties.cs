using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

public partial class ShinyNavigationPage
{
    static void Restyle(BindableObject b)
        => StyleGuard.WhenReady<ShinyNavigationPage>(b, page => page.RefreshAll());


    /// <summary>Re-applies this page's settings to every bar it has installed.</summary>
    void RefreshAll()
    {
        foreach (var page in this.Navigation.NavigationStack.OfType<ContentPage>())
            this.Refresh(page);

        // A page can be off the stack and still be showing - mid-pop, or hosted without one at all
        // in a test - so the current page is refreshed whether or not the walk above reached it.
        if (this.CurrentPage is ContentPage current && !this.Navigation.NavigationStack.Contains(current))
            this.Refresh(current);
    }


    /// <summary>Backing store for <see cref="IsNavBarVisible"/>.</summary>
    public static readonly BindableProperty IsNavBarVisibleProperty = BindableProperty.Create(
        nameof(IsNavBarVisible), typeof(bool), typeof(ShinyNavigationPage), true,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <summary>
    /// Hides the bar over every page at once. To hide it for one page use MAUI's own
    /// <see cref="NavigationPage.SetHasNavigationBar(BindableObject, bool)"/>, which this honours.
    /// </summary>
    public bool IsNavBarVisible
    {
        get => (bool)this.GetValue(IsNavBarVisibleProperty);
        set => this.SetValue(IsNavBarVisibleProperty, value);
    }

    /// <summary>Backing store for <see cref="TitleAlignment"/>.</summary>
    public static readonly BindableProperty TitleAlignmentProperty = BindableProperty.Create(
        nameof(TitleAlignment), typeof(NavBarTitleAlignment), typeof(ShinyNavigationPage), NavBarTitleAlignment.Auto,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <inheritdoc cref="NavBarTitleAlignment"/>
    public NavBarTitleAlignment TitleAlignment
    {
        get => (NavBarTitleAlignment)this.GetValue(TitleAlignmentProperty);
        set => this.SetValue(TitleAlignmentProperty, value);
    }

    /// <summary>Backing store for <see cref="LargeTitleDisplay"/>.</summary>
    public static readonly BindableProperty LargeTitleDisplayProperty = BindableProperty.Create(
        nameof(LargeTitleDisplay), typeof(LargeTitleDisplay), typeof(ShinyNavigationPage), LargeTitleDisplay.None,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <inheritdoc cref="Shiny.Maui.Controls.LargeTitleDisplay"/>
    public LargeTitleDisplay LargeTitleDisplay
    {
        get => (LargeTitleDisplay)this.GetValue(LargeTitleDisplayProperty);
        set => this.SetValue(LargeTitleDisplayProperty, value);
    }

    /// <summary>Backing store for <see cref="LargeTitleHeight"/>.</summary>
    public static readonly BindableProperty LargeTitleHeightProperty = BindableProperty.Create(
        nameof(LargeTitleHeight), typeof(double), typeof(ShinyNavigationPage), 52d,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <inheritdoc cref="ShinyNavBar.LargeTitleHeight"/>
    public double LargeTitleHeight
    {
        get => (double)this.GetValue(LargeTitleHeightProperty);
        set => this.SetValue(LargeTitleHeightProperty, value);
    }

    /// <summary>Backing store for <see cref="LargeTitleCollapseDistance"/>.</summary>
    public static readonly BindableProperty LargeTitleCollapseDistanceProperty = BindableProperty.Create(
        nameof(LargeTitleCollapseDistance), typeof(double), typeof(ShinyNavigationPage), 48d,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <inheritdoc cref="ShinyNavBar.LargeTitleCollapseDistance"/>
    public double LargeTitleCollapseDistance
    {
        get => (double)this.GetValue(LargeTitleCollapseDistanceProperty);
        set => this.SetValue(LargeTitleCollapseDistanceProperty, value);
    }

    /// <summary>Backing store for <see cref="LargeTitleFontSize"/>.</summary>
    public static readonly BindableProperty LargeTitleFontSizeProperty = BindableProperty.Create(
        nameof(LargeTitleFontSize), typeof(double), typeof(ShinyNavigationPage), ThemeTokens.Unset,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <inheritdoc cref="ShinyNavBar.LargeTitleFontSize"/>
    public double LargeTitleFontSize
    {
        get => (double)this.GetValue(LargeTitleFontSizeProperty);
        set => this.SetValue(LargeTitleFontSizeProperty, value);
    }

    /// <summary>Backing store for <see cref="TitleFontSize"/>.</summary>
    public static readonly BindableProperty TitleFontSizeProperty = BindableProperty.Create(
        nameof(TitleFontSize), typeof(double), typeof(ShinyNavigationPage), ThemeTokens.Unset,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <inheritdoc cref="ShinyNavBar.TitleFontSize"/>
    public double TitleFontSize
    {
        get => (double)this.GetValue(TitleFontSizeProperty);
        set => this.SetValue(TitleFontSizeProperty, value);
    }

    /// <summary>Backing store for <see cref="TitleFontFamily"/>.</summary>
    public static readonly BindableProperty TitleFontFamilyProperty = BindableProperty.Create(
        nameof(TitleFontFamily), typeof(string), typeof(ShinyNavigationPage), null,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <inheritdoc cref="ShinyNavBar.TitleFontFamily"/>
    public string? TitleFontFamily
    {
        get => (string?)this.GetValue(TitleFontFamilyProperty);
        set => this.SetValue(TitleFontFamilyProperty, value);
    }

    /// <summary>Backing store for <see cref="TitleFontAttributes"/>.</summary>
    public static readonly BindableProperty TitleFontAttributesProperty = BindableProperty.Create(
        nameof(TitleFontAttributes), typeof(FontAttributes), typeof(ShinyNavigationPage), FontAttributes.Bold,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <inheritdoc cref="ShinyNavBar.TitleFontAttributes"/>
    public FontAttributes TitleFontAttributes
    {
        get => (FontAttributes)this.GetValue(TitleFontAttributesProperty);
        set => this.SetValue(TitleFontAttributesProperty, value);
    }

    /// <summary>Backing store for <see cref="BarIconColor"/>.</summary>
    /// <remarks>
    /// Named <c>BarIconColor</c> rather than <c>IconColor</c> so it cannot be mistaken for — or
    /// shadow — MAUI's attached <see cref="NavigationPage.IconColorProperty"/>, which is per-page and
    /// which this is only the default for.
    /// </remarks>
    public static readonly BindableProperty BarIconColorProperty = BindableProperty.Create(
        nameof(BarIconColor), typeof(Color), typeof(ShinyNavigationPage), null,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <summary>
    /// Tints the back chevron and the item artwork on every page. A page overrides it with
    /// <see cref="NavigationPage.SetIconColor(BindableObject, Color)"/>. Unset follows
    /// <see cref="NavigationPage.BarTextColor"/>, and then the theme.
    /// </summary>
    public Color? BarIconColor
    {
        get => (Color?)this.GetValue(BarIconColorProperty);
        set => this.SetValue(BarIconColorProperty, value);
    }

    /// <summary>Backing store for <see cref="BarHeight"/>.</summary>
    public static readonly BindableProperty BarHeightProperty = BindableProperty.Create(
        nameof(BarHeight), typeof(double), typeof(ShinyNavigationPage), 56d,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <inheritdoc cref="ShinyNavBar.BarHeight"/>
    public double BarHeight
    {
        get => (double)this.GetValue(BarHeightProperty);
        set => this.SetValue(BarHeightProperty, value);
    }

    /// <summary>Backing store for <see cref="BarPadding"/>.</summary>
    public static readonly BindableProperty BarPaddingProperty = BindableProperty.Create(
        nameof(BarPadding), typeof(Thickness), typeof(ShinyNavigationPage), new Thickness(4, 0),
        propertyChanged: (b, _, _) => Restyle(b));

    /// <inheritdoc cref="ShinyNavBar.BarPadding"/>
    public Thickness BarPadding
    {
        get => (Thickness)this.GetValue(BarPaddingProperty);
        set => this.SetValue(BarPaddingProperty, value);
    }

    /// <summary>Backing store for <see cref="HasShadow"/>.</summary>
    public static readonly BindableProperty HasShadowProperty = BindableProperty.Create(
        nameof(HasShadow), typeof(bool), typeof(ShinyNavigationPage), true,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <inheritdoc cref="ShinyNavBar.HasShadow"/>
    public bool HasShadow
    {
        get => (bool)this.GetValue(HasShadowProperty);
        set => this.SetValue(HasShadowProperty, value);
    }

    /// <summary>Backing store for <see cref="HasSeparator"/>.</summary>
    public static readonly BindableProperty HasSeparatorProperty = BindableProperty.Create(
        nameof(HasSeparator), typeof(bool), typeof(ShinyNavigationPage), false,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <inheritdoc cref="ShinyNavBar.HasSeparator"/>
    public bool HasSeparator
    {
        get => (bool)this.GetValue(HasSeparatorProperty);
        set => this.SetValue(HasSeparatorProperty, value);
    }

    /// <summary>Backing store for <see cref="ItemSpacing"/>.</summary>
    public static readonly BindableProperty ItemSpacingProperty = BindableProperty.Create(
        nameof(ItemSpacing), typeof(double), typeof(ShinyNavigationPage), 2d,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <inheritdoc cref="ShinyNavBar.ItemSpacing"/>
    public double ItemSpacing
    {
        get => (double)this.GetValue(ItemSpacingProperty);
        set => this.SetValue(ItemSpacingProperty, value);
    }

    /// <summary>Backing store for <see cref="IconSize"/>.</summary>
    public static readonly BindableProperty IconSizeProperty = BindableProperty.Create(
        nameof(IconSize), typeof(double), typeof(ShinyNavigationPage), 22d,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <inheritdoc cref="ShinyNavBar.IconSize"/>
    public double IconSize
    {
        get => (double)this.GetValue(IconSizeProperty);
        set => this.SetValue(IconSizeProperty, value);
    }

    /// <summary>Backing store for <see cref="MaxVisibleItems"/>.</summary>
    public static readonly BindableProperty MaxVisibleItemsProperty = BindableProperty.Create(
        nameof(MaxVisibleItems), typeof(int), typeof(ShinyNavigationPage), 3,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <inheritdoc cref="ShinyNavBar.MaxVisibleItems"/>
    public int MaxVisibleItems
    {
        get => (int)this.GetValue(MaxVisibleItemsProperty);
        set => this.SetValue(MaxVisibleItemsProperty, value);
    }

    /// <summary>Backing store for <see cref="OverflowIcon"/>.</summary>
    public static readonly BindableProperty OverflowIconProperty = BindableProperty.Create(
        nameof(OverflowIcon), typeof(string), typeof(ShinyNavigationPage), null,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <inheritdoc cref="ShinyNavBar.OverflowIcon"/>
    public string? OverflowIcon
    {
        get => (string?)this.GetValue(OverflowIconProperty);
        set => this.SetValue(OverflowIconProperty, value);
    }

    /// <summary>Backing store for <see cref="MenuTemplate"/>.</summary>
    public static readonly BindableProperty MenuTemplateProperty = BindableProperty.Create(
        nameof(MenuTemplate), typeof(DataTemplate), typeof(ShinyNavigationPage), null,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <inheritdoc cref="ShinyNavBar.MenuTemplate"/>
    public DataTemplate? MenuTemplate
    {
        get => (DataTemplate?)this.GetValue(MenuTemplateProperty);
        set => this.SetValue(MenuTemplateProperty, value);
    }

    /// <summary>Backing store for <see cref="AnimationDuration"/>.</summary>
    public static readonly BindableProperty AnimationDurationProperty = BindableProperty.Create(
        nameof(AnimationDuration), typeof(uint), typeof(ShinyNavigationPage), (uint)180,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <inheritdoc cref="ShinyNavBar.AnimationDuration"/>
    public uint AnimationDuration
    {
        get => (uint)this.GetValue(AnimationDurationProperty);
        set => this.SetValue(AnimationDurationProperty, value);
    }

    /// <summary>Backing store for <see cref="RespectSafeArea"/>.</summary>
    public static readonly BindableProperty RespectSafeAreaProperty = BindableProperty.Create(
        nameof(RespectSafeArea), typeof(bool), typeof(ShinyNavigationPage), true,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <inheritdoc cref="ShinyNavBar.RespectSafeArea"/>
    public bool RespectSafeArea
    {
        get => (bool)this.GetValue(RespectSafeAreaProperty);
        set => this.SetValue(RespectSafeAreaProperty, value);
    }

    /// <summary>Backing store for <see cref="StatusBarStyle"/>.</summary>
    public static readonly BindableProperty StatusBarStyleProperty = BindableProperty.Create(
        nameof(StatusBarStyle), typeof(StatusBarStyle), typeof(ShinyNavigationPage), StatusBarStyle.Auto,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <summary>
    /// How the status bar's clock and icons are drawn over the bar. <see cref="Shiny.Maui.Controls.StatusBarStyle.Auto"/>
    /// by default, which reads the bar's own background and picks the readable one. A page overrides
    /// it with <see cref="ShinyNav.StatusBarStyleProperty"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>iOS and Mac Catalyst</b> need <c>UIViewControllerBasedStatusBarAppearance</c> set to
    /// <c>false</c> in <c>Info.plist</c> for any status bar style to apply — that is UIKit's rule and
    /// it applies to MAUI's own <c>BarTextColor</c> handling just the same. Without the key the
    /// status bar follows the system appearance and only the <em>background</em> behind it changes.
    /// <b>Android</b> needs no opt-in.</para>
    /// <para>Nothing to do on Windows, GTK4 or macOS AppKit — none of them has a status bar.</para>
    /// </remarks>
    public StatusBarStyle StatusBarStyle
    {
        get => (StatusBarStyle)this.GetValue(StatusBarStyleProperty);
        set => this.SetValue(StatusBarStyleProperty, value);
    }

    /// <summary>Backing store for <see cref="StatusBarColor"/>.</summary>
    public static readonly BindableProperty StatusBarColorProperty = BindableProperty.Create(
        nameof(StatusBarColor), typeof(Color), typeof(ShinyNavigationPage), null,
        propertyChanged: (b, _, _) => Restyle(b));

    /// <summary>
    /// Pins the colour the status bar is told about, instead of the bar's own background. Unset —
    /// which is the normal case — follows the bar.
    /// </summary>
    /// <remarks>
    /// It is worth setting for a bar painted with an image or a pattern brush, which has no single
    /// colour to derive <see cref="Shiny.Maui.Controls.StatusBarStyle.Auto"/> from, and on Android
    /// below 15, where this is the colour actually filled in behind the clock.
    /// </remarks>
    public Color? StatusBarColor
    {
        get => (Color?)this.GetValue(StatusBarColorProperty);
        set => this.SetValue(StatusBarColorProperty, value);
    }

    /// <summary>Backing store for <see cref="EnableSwipeBackGesture"/>.</summary>
    public static readonly BindableProperty EnableSwipeBackGestureProperty = BindableProperty.Create(
        nameof(EnableSwipeBackGesture), typeof(bool), typeof(ShinyNavigationPage), true);

    /// <summary>
    /// Keeps iOS's swipe-from-the-edge pop working. UIKit disables it whenever the navigation bar is
    /// hidden — which is exactly what this page does to make room for the drawn bar — so it has to be
    /// put back deliberately. No effect on any other platform.
    /// </summary>
    public bool EnableSwipeBackGesture
    {
        get => (bool)this.GetValue(EnableSwipeBackGestureProperty);
        set => this.SetValue(EnableSwipeBackGestureProperty, value);
    }
}
