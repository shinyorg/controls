using System.Windows.Input;

namespace Shiny.Maui.Controls;

/// <summary>
/// Attached properties a page uses to say what <see cref="ShinyNavigationPage"/>'s bar should show
/// for it — its left and right items, its subtitle, its large title, and the per-page overrides of
/// the bar's appearance.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of the bar the page owns, and it is deliberately the same shape as
/// <c>Page.ToolbarItems</c>. Everything MAUI already gives a <see cref="NavigationPage"/> keeps
/// working and is <b>not</b> repeated here — the bar reads it straight off the page:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="Page.Title"/> and <see cref="Page.ToolbarItems"/> (drawn on the right).</description></item>
///   <item><description><see cref="NavigationPage.SetHasNavigationBar(BindableObject, bool)"/> hides the bar for a page.</description></item>
///   <item><description><see cref="NavigationPage.SetHasBackButton(Page, bool)"/> hides the back button.</description></item>
///   <item><description><see cref="NavigationPage.SetBackButtonTitle(BindableObject, string)"/> labels it.</description></item>
///   <item><description><see cref="NavigationPage.SetTitleView(BindableObject, View)"/> replaces the title outright.</description></item>
///   <item><description><see cref="NavigationPage.SetTitleIconImageSource(BindableObject, ImageSource)"/> puts artwork before the title.</description></item>
///   <item><description><see cref="NavigationPage.SetIconColor(BindableObject, Color)"/> tints the back chevron and the item icons.</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code language="xaml">
/// &lt;ContentPage Title="Inbox"
///              xmlns:shiny="http://shiny.net/maui/controls"
///              shiny:ShinyNav.Subtitle="12 unread"&gt;
///     &lt;shiny:ShinyNav.LeftItems&gt;
///         &lt;shiny:NavBarItem Icon="menu" Command="{Binding MenuCommand}" /&gt;
///     &lt;/shiny:ShinyNav.LeftItems&gt;
///     &lt;shiny:ShinyNav.RightItems&gt;
///         &lt;shiny:NavBarItem Icon="search" Command="{Binding SearchCommand}" /&gt;
///     &lt;/shiny:ShinyNav.RightItems&gt;
/// &lt;/ContentPage&gt;
/// </code>
/// </example>
public static class ShinyNav
{
    /// <summary>
    /// The items drawn before the title — the side a stock <see cref="NavigationPage"/> reserves for
    /// the back button and gives you no way to use.
    /// </summary>
    /// <remarks>
    /// They sit after the back button when there is one. A live collection on first read, so code can
    /// add items without assigning one first.
    /// </remarks>
    public static readonly BindableProperty LeftItemsProperty = BindableProperty.CreateAttached(
        "LeftItems",
        typeof(NavBarItemCollection),
        typeof(ShinyNav),
        null,
        defaultValueCreator: _ => new NavBarItemCollection());

    /// <summary>
    /// The items drawn after the title. The page's own <see cref="Page.ToolbarItems"/> are drawn here
    /// too, ahead of these — so adopting the bar does not mean rewriting a page's toolbar.
    /// </summary>
    public static readonly BindableProperty RightItemsProperty = BindableProperty.CreateAttached(
        "RightItems",
        typeof(NavBarItemCollection),
        typeof(ShinyNav),
        null,
        defaultValueCreator: _ => new NavBarItemCollection());

    /// <summary>
    /// Badge text drawn over any toolbar item's top-right corner — the badge a
    /// <see cref="NavBarItem"/> has as its own <see cref="NavBarItem.Badge"/> property, made
    /// available to the plain <see cref="ToolbarItem"/>s a page already had.
    /// </summary>
    /// <remarks>
    /// <para>An empty string draws a dot; null draws nothing. Set it on the item, not the page.</para>
    /// <para>On a <see cref="NavBarItem"/> the item's own <see cref="NavBarItem.Badge"/> wins when it
    /// is non-null, so this can be attached to one without silently overriding what it already
    /// says.</para>
    /// <para>An item that ends up in the overflow menu keeps its badge there, and the overflow
    /// button itself draws a dot while any item behind it is badged — so a count never disappears
    /// just because the bar ran out of room.</para>
    /// </remarks>
    /// <example>
    /// <code language="xaml">
    /// &lt;ContentPage.ToolbarItems&gt;
    ///     &lt;ToolbarItem Text="Alerts" shiny:ShinyNav.Badge="{Binding UnreadCount}" /&gt;
    /// &lt;/ContentPage.ToolbarItems&gt;
    /// </code>
    /// </example>
    public static readonly BindableProperty BadgeProperty = BindableProperty.CreateAttached(
        "Badge", typeof(string), typeof(ShinyNav), null);

    /// <summary>The badge's fill for this item. Unset follows the theme's error colour.</summary>
    public static readonly BindableProperty BadgeColorProperty = BindableProperty.CreateAttached(
        "BadgeColor", typeof(Color), typeof(ShinyNav), null);

    /// <summary>A second line under the title. Nothing is drawn when it is null or empty.</summary>
    public static readonly BindableProperty SubtitleProperty = BindableProperty.CreateAttached(
        "Subtitle", typeof(string), typeof(ShinyNav), null);

    /// <summary>The large title's text, when it should differ from <see cref="Page.Title"/>.</summary>
    public static readonly BindableProperty LargeTitleProperty = BindableProperty.CreateAttached(
        "LargeTitle", typeof(string), typeof(ShinyNav), null);

    /// <summary>
    /// Whether this page gets a large title, overriding
    /// <see cref="ShinyNavigationPage.LargeTitleDisplay"/>.
    /// </summary>
    public static readonly BindableProperty LargeTitleDisplayProperty = BindableProperty.CreateAttached(
        "LargeTitleDisplay", typeof(LargeTitleDisplay), typeof(ShinyNav), LargeTitleDisplay.Inherit);

    /// <summary>
    /// The scrollable the collapsing large title follows. Set it when the page has more than one, or
    /// when the one that scrolls is built later than the bar and auto-discovery has already run.
    /// </summary>
    /// <remarks>
    /// Left unset, the bar takes the first <see cref="ScrollView"/> or <see cref="ItemsView"/> it
    /// finds in the page's content, which is the right answer for the overwhelming majority of pages.
    /// </remarks>
    public static readonly BindableProperty ScrollSourceProperty = BindableProperty.CreateAttached(
        "ScrollSource", typeof(View), typeof(ShinyNav), null);

    /// <summary>Overrides the bar's background for this page.</summary>
    public static readonly BindableProperty BarBackgroundColorProperty = BindableProperty.CreateAttached(
        "BarBackgroundColor", typeof(Color), typeof(ShinyNav), null);

    /// <summary>
    /// Overrides <see cref="ShinyNavigationPage.StatusBarStyle"/> for this page — a page with a dark
    /// bar pushed onto a navigation page with a light one.
    /// </summary>
    public static readonly BindableProperty StatusBarStyleProperty = BindableProperty.CreateAttached(
        "StatusBarStyle", typeof(StatusBarStyle), typeof(ShinyNav), StatusBarStyle.Inherit);

    /// <summary>Overrides the bar's title and item colour for this page.</summary>
    public static readonly BindableProperty BarTextColorProperty = BindableProperty.CreateAttached(
        "BarTextColor", typeof(Color), typeof(ShinyNav), null);

    /// <summary>
    /// Hides the bar for this page. This is the runtime switch —
    /// <see cref="NavigationPage.SetHasNavigationBar(BindableObject, bool)"/> is honoured too, but
    /// only as the starting value: the drawn bar exists because the native one was forced off, so
    /// the page's copy of that property is no longer somewhere a later answer can be read from.
    /// </summary>
    public static readonly BindableProperty IsNavBarVisibleProperty = BindableProperty.CreateAttached(
        "IsNavBarVisible", typeof(bool), typeof(ShinyNav), true);

    /// <summary>Overrides where the title sits for this page.</summary>
    public static readonly BindableProperty TitleAlignmentProperty = BindableProperty.CreateAttached(
        "TitleAlignment", typeof(NavBarTitleAlignment), typeof(ShinyNav), NavBarTitleAlignment.Auto);

    /// <summary>
    /// The motion icon the back button draws for this page. Unset draws the chevron, which is what
    /// almost every page wants; <c>close</c> is the usual override on a page pushed as a form.
    /// </summary>
    public static readonly BindableProperty BackButtonIconProperty = BindableProperty.CreateAttached(
        "BackButtonIcon", typeof(string), typeof(ShinyNav), null);

    /// <summary>
    /// Runs instead of popping when the back button is tapped — the "you have unsaved changes" hook.
    /// Nothing is popped for you; call <c>Navigation.PopAsync()</c> when you are done.
    /// </summary>
    public static readonly BindableProperty BackButtonCommandProperty = BindableProperty.CreateAttached(
        "BackButtonCommand", typeof(ICommand), typeof(ShinyNav), null);

    /// <summary>Passed to <see cref="BackButtonCommandProperty"/>.</summary>
    public static readonly BindableProperty BackButtonCommandParameterProperty = BindableProperty.CreateAttached(
        "BackButtonCommandParameter", typeof(object), typeof(ShinyNav), null);

    /// <summary>Gets <see cref="LeftItemsProperty"/>.</summary>
    public static NavBarItemCollection GetLeftItems(BindableObject target) => (NavBarItemCollection)target.GetValue(LeftItemsProperty);

    /// <summary>Sets <see cref="LeftItemsProperty"/>.</summary>
    public static void SetLeftItems(BindableObject target, NavBarItemCollection value) => target.SetValue(LeftItemsProperty, value);

    /// <summary>Gets <see cref="RightItemsProperty"/>.</summary>
    public static NavBarItemCollection GetRightItems(BindableObject target) => (NavBarItemCollection)target.GetValue(RightItemsProperty);

    /// <summary>Sets <see cref="RightItemsProperty"/>.</summary>
    public static void SetRightItems(BindableObject target, NavBarItemCollection value) => target.SetValue(RightItemsProperty, value);

    /// <summary>Gets <see cref="BadgeProperty"/>.</summary>
    public static string? GetBadge(BindableObject target) => (string?)target.GetValue(BadgeProperty);

    /// <summary>Sets <see cref="BadgeProperty"/>.</summary>
    public static void SetBadge(BindableObject target, string? value) => target.SetValue(BadgeProperty, value);

    /// <summary>Gets <see cref="BadgeColorProperty"/>.</summary>
    public static Color? GetBadgeColor(BindableObject target) => (Color?)target.GetValue(BadgeColorProperty);

    /// <summary>Sets <see cref="BadgeColorProperty"/>.</summary>
    public static void SetBadgeColor(BindableObject target, Color? value) => target.SetValue(BadgeColorProperty, value);

    /// <summary>Gets <see cref="SubtitleProperty"/>.</summary>
    public static string? GetSubtitle(BindableObject target) => (string?)target.GetValue(SubtitleProperty);

    /// <summary>Sets <see cref="SubtitleProperty"/>.</summary>
    public static void SetSubtitle(BindableObject target, string? value) => target.SetValue(SubtitleProperty, value);

    /// <summary>Gets <see cref="LargeTitleProperty"/>.</summary>
    public static string? GetLargeTitle(BindableObject target) => (string?)target.GetValue(LargeTitleProperty);

    /// <summary>Sets <see cref="LargeTitleProperty"/>.</summary>
    public static void SetLargeTitle(BindableObject target, string? value) => target.SetValue(LargeTitleProperty, value);

    /// <summary>Gets <see cref="IsNavBarVisibleProperty"/>.</summary>
    public static bool GetIsNavBarVisible(BindableObject target) => (bool)target.GetValue(IsNavBarVisibleProperty);

    /// <summary>Sets <see cref="IsNavBarVisibleProperty"/>.</summary>
    public static void SetIsNavBarVisible(BindableObject target, bool value) => target.SetValue(IsNavBarVisibleProperty, value);

    /// <summary>Gets <see cref="LargeTitleDisplayProperty"/>.</summary>
    public static LargeTitleDisplay GetLargeTitleDisplay(BindableObject target) => (LargeTitleDisplay)target.GetValue(LargeTitleDisplayProperty);

    /// <summary>Sets <see cref="LargeTitleDisplayProperty"/>.</summary>
    public static void SetLargeTitleDisplay(BindableObject target, LargeTitleDisplay value) => target.SetValue(LargeTitleDisplayProperty, value);

    /// <summary>Gets <see cref="ScrollSourceProperty"/>.</summary>
    public static View? GetScrollSource(BindableObject target) => (View?)target.GetValue(ScrollSourceProperty);

    /// <summary>Sets <see cref="ScrollSourceProperty"/>.</summary>
    public static void SetScrollSource(BindableObject target, View? value) => target.SetValue(ScrollSourceProperty, value);

    /// <summary>Gets <see cref="BarBackgroundColorProperty"/>.</summary>
    public static Color? GetBarBackgroundColor(BindableObject target) => (Color?)target.GetValue(BarBackgroundColorProperty);

    /// <summary>Sets <see cref="BarBackgroundColorProperty"/>.</summary>
    public static void SetBarBackgroundColor(BindableObject target, Color? value) => target.SetValue(BarBackgroundColorProperty, value);

    /// <summary>Gets <see cref="BarTextColorProperty"/>.</summary>
    public static Color? GetBarTextColor(BindableObject target) => (Color?)target.GetValue(BarTextColorProperty);

    /// <summary>Sets <see cref="BarTextColorProperty"/>.</summary>
    public static void SetBarTextColor(BindableObject target, Color? value) => target.SetValue(BarTextColorProperty, value);

    /// <summary>Gets <see cref="StatusBarStyleProperty"/>.</summary>
    public static StatusBarStyle GetStatusBarStyle(BindableObject target) => (StatusBarStyle)target.GetValue(StatusBarStyleProperty);

    /// <summary>Sets <see cref="StatusBarStyleProperty"/>.</summary>
    public static void SetStatusBarStyle(BindableObject target, StatusBarStyle value) => target.SetValue(StatusBarStyleProperty, value);

    /// <summary>Gets <see cref="TitleAlignmentProperty"/>.</summary>
    public static NavBarTitleAlignment GetTitleAlignment(BindableObject target) => (NavBarTitleAlignment)target.GetValue(TitleAlignmentProperty);

    /// <summary>Sets <see cref="TitleAlignmentProperty"/>.</summary>
    public static void SetTitleAlignment(BindableObject target, NavBarTitleAlignment value) => target.SetValue(TitleAlignmentProperty, value);

    /// <summary>Gets <see cref="BackButtonIconProperty"/>.</summary>
    public static string? GetBackButtonIcon(BindableObject target) => (string?)target.GetValue(BackButtonIconProperty);

    /// <summary>Sets <see cref="BackButtonIconProperty"/>.</summary>
    public static void SetBackButtonIcon(BindableObject target, string? value) => target.SetValue(BackButtonIconProperty, value);

    /// <summary>Gets <see cref="BackButtonCommandProperty"/>.</summary>
    public static ICommand? GetBackButtonCommand(BindableObject target) => (ICommand?)target.GetValue(BackButtonCommandProperty);

    /// <summary>Sets <see cref="BackButtonCommandProperty"/>.</summary>
    public static void SetBackButtonCommand(BindableObject target, ICommand? value) => target.SetValue(BackButtonCommandProperty, value);

    /// <summary>Gets <see cref="BackButtonCommandParameterProperty"/>.</summary>
    public static object? GetBackButtonCommandParameter(BindableObject target) => target.GetValue(BackButtonCommandParameterProperty);

    /// <summary>Sets <see cref="BackButtonCommandParameterProperty"/>.</summary>
    public static void SetBackButtonCommandParameter(BindableObject target, object? value) => target.SetValue(BackButtonCommandParameterProperty, value);

    /// <summary>The bar drawn over a page, or null when none was installed.</summary>
    public static ShinyNavBar? GetNavBar(Page page) => ShinyNavigationPage.GetNavBar(page);
}
