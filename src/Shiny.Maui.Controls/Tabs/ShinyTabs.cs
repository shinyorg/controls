namespace Shiny.Maui.Controls;

/// <summary>
/// Attached properties a page (or a <see cref="ShellContent"/>) uses to say what the tab bar should
/// show for it — its badge, and what its centre button presents.
/// </summary>
/// <remarks>
/// <para>This is the half of the bar that is owned by the page rather than by the bar, and it is
/// deliberately the same shape as <c>Page.ToolbarItems</c>: the page declares actions, the chrome
/// renders them. It means a page can be moved between a <see cref="ShinyTabbedPage"/> and a
/// <see cref="Shell"/> without any of this changing.</para>
/// <para>Set on a <see cref="ShellContent"/> (or a <see cref="ShinyTabItem"/>) the values are
/// readable before the page exists, which is what a lazily-loaded tab needs. Set on the page they
/// are readable only once it has been realized — so a badge that must show on a tab the user has
/// never opened belongs on the shell content, and one the page computes belongs on the page.</para>
/// </remarks>
public static partial class ShinyTabs
{
    /// <summary>
    /// Badge text for the tab showing this element. An empty string draws a dot; null draws nothing.
    /// Beats <see cref="ShinyTabItem.Badge"/> when both are set.
    /// </summary>
    public static readonly BindableProperty BadgeProperty = BindableProperty.CreateAttached(
        "Badge", typeof(string), typeof(ShinyTabs), null);

    /// <summary>Badge colour for the tab showing this element. Unset follows the theme's error colour.</summary>
    public static readonly BindableProperty BadgeColorProperty = BindableProperty.CreateAttached(
        "BadgeColor", typeof(Color), typeof(ShinyTabs), null);

    /// <summary>
    /// The motion icon the tab draws for this element. Only consulted by
    /// <see cref="ShinyTabBarBehavior"/>, where a <see cref="ShellContent"/> has nowhere else to say
    /// it; a <see cref="ShinyTabItem"/> carries its own <see cref="ShinyTabItem.Icon"/>.
    /// </summary>
    public static readonly BindableProperty IconProperty = BindableProperty.CreateAttached(
        "Icon", typeof(string), typeof(ShinyTabs), null);

    /// <summary>Overrides the tab's label for this element.</summary>
    public static readonly BindableProperty TitleProperty = BindableProperty.CreateAttached(
        "Title", typeof(string), typeof(ShinyTabs), null);

    /// <summary>
    /// The rows the centre button presents while this page is showing — the per-page menu, in the
    /// shape of <c>ToolbarItems</c>.
    /// </summary>
    public static readonly BindableProperty ActionsProperty = BindableProperty.CreateAttached(
        "Actions", typeof(TabActionCollection), typeof(ShinyTabs), null,
        // A live collection on first read, so code can add rows without assigning one first. Nothing
        // subscribes to it - the menu pulls the rows when it opens - so the usual defaultValueCreator
        // trap (lazily-created defaults never raise propertyChanged) costs nothing here.
        defaultValueCreator: _ => new TabActionCollection());

    /// <summary>
    /// Content the centre button presents while this page is showing, instead of
    /// <see cref="ActionsProperty"/>. Anything at all — a grid of shortcuts, a compose form.
    /// </summary>
    public static readonly BindableProperty MenuContentProperty = BindableProperty.CreateAttached(
        "MenuContent", typeof(View), typeof(ShinyTabs), null);

    /// <summary>
    /// The same, built the first time the menu opens rather than with the page. Beats
    /// <see cref="MenuContentProperty"/>.
    /// </summary>
    public static readonly BindableProperty MenuContentTemplateProperty = BindableProperty.CreateAttached(
        "MenuContentTemplate", typeof(DataTemplate), typeof(ShinyTabs), null);

    /// <summary>
    /// Hides the whole bar while this page is showing — a full-bleed media page, a checkout flow.
    /// </summary>
    public static readonly BindableProperty IsTabBarVisibleProperty = BindableProperty.CreateAttached(
        "IsTabBarVisible", typeof(bool), typeof(ShinyTabs), true);

    /// <summary>Gets <see cref="BadgeProperty"/>.</summary>
    public static string? GetBadge(BindableObject target) => (string?)target.GetValue(BadgeProperty);

    /// <summary>Sets <see cref="BadgeProperty"/>.</summary>
    public static void SetBadge(BindableObject target, string? value) => target.SetValue(BadgeProperty, value);

    /// <summary>Gets <see cref="BadgeColorProperty"/>.</summary>
    public static Color? GetBadgeColor(BindableObject target) => (Color?)target.GetValue(BadgeColorProperty);

    /// <summary>Sets <see cref="BadgeColorProperty"/>.</summary>
    public static void SetBadgeColor(BindableObject target, Color? value) => target.SetValue(BadgeColorProperty, value);

    /// <summary>Gets <see cref="IconProperty"/>.</summary>
    public static string? GetIcon(BindableObject target) => (string?)target.GetValue(IconProperty);

    /// <summary>Sets <see cref="IconProperty"/>.</summary>
    public static void SetIcon(BindableObject target, string? value) => target.SetValue(IconProperty, value);

    /// <summary>Gets <see cref="TitleProperty"/>.</summary>
    public static string? GetTitle(BindableObject target) => (string?)target.GetValue(TitleProperty);

    /// <summary>Sets <see cref="TitleProperty"/>.</summary>
    public static void SetTitle(BindableObject target, string? value) => target.SetValue(TitleProperty, value);

    /// <summary>Gets <see cref="ActionsProperty"/>.</summary>
    public static TabActionCollection GetActions(BindableObject target) => (TabActionCollection)target.GetValue(ActionsProperty);

    /// <summary>Sets <see cref="ActionsProperty"/>.</summary>
    public static void SetActions(BindableObject target, TabActionCollection value) => target.SetValue(ActionsProperty, value);

    /// <summary>Gets <see cref="MenuContentProperty"/>.</summary>
    public static View? GetMenuContent(BindableObject target) => (View?)target.GetValue(MenuContentProperty);

    /// <summary>Sets <see cref="MenuContentProperty"/>.</summary>
    public static void SetMenuContent(BindableObject target, View? value) => target.SetValue(MenuContentProperty, value);

    /// <summary>Gets <see cref="MenuContentTemplateProperty"/>.</summary>
    public static DataTemplate? GetMenuContentTemplate(BindableObject target) => (DataTemplate?)target.GetValue(MenuContentTemplateProperty);

    /// <summary>Sets <see cref="MenuContentTemplateProperty"/>.</summary>
    public static void SetMenuContentTemplate(BindableObject target, DataTemplate? value) => target.SetValue(MenuContentTemplateProperty, value);

    /// <summary>Gets <see cref="IsTabBarVisibleProperty"/>.</summary>
    public static bool GetIsTabBarVisible(BindableObject target) => (bool)target.GetValue(IsTabBarVisibleProperty);

    /// <summary>Sets <see cref="IsTabBarVisibleProperty"/>.</summary>
    public static void SetIsTabBarVisible(BindableObject target, bool value) => target.SetValue(IsTabBarVisibleProperty, value);

    /// <summary>
    /// Reads an attached value that may have been set anywhere along a chain of candidates, taking
    /// the first that was set explicitly. Used to resolve a page's value ahead of the tab's, and a
    /// tab's ahead of the bar's default, without a set-but-equal-to-the-default value being mistaken
    /// for "not set".
    /// </summary>
    internal static T? Resolve<T>(BindableProperty property, params BindableObject?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (candidate is null || candidate.IsSet(property) == false)
                continue;

            return (T?)candidate.GetValue(property);
        }
        return default;
    }
}
