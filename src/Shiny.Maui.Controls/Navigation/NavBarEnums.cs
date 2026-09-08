namespace Shiny.Maui.Controls;

/// <summary>Where <see cref="ShinyNavBar"/> puts the title.</summary>
public enum NavBarTitleAlignment
{
    /// <summary>Centred on iOS and Mac Catalyst, leading-aligned everywhere else — the platform convention.</summary>
    Auto,

    /// <summary>Leading edge, immediately after the back button and the left items.</summary>
    Start,

    /// <summary>
    /// Centred in the bar itself, not in the gap between the two item groups — so it stays put when
    /// an item is added to one side. It is inset far enough to clear the wider group and truncates
    /// rather than running underneath one.
    /// </summary>
    Center
}

/// <summary>How <see cref="ShinyNavBar"/> draws the oversized title beneath the bar row.</summary>
public enum LargeTitleDisplay
{
    /// <summary>
    /// Follow the host. On a page it means "whatever the <see cref="ShinyNavigationPage"/> said";
    /// on the navigation page or the bar itself there is nothing above it to ask, so it means
    /// <see cref="None"/>.
    /// </summary>
    /// <remarks>
    /// It exists so a page can turn the large title <em>off</em> against a navigation page that
    /// turned it on. Without a distinct "not answered" value, a page setting <see cref="None"/>
    /// would be indistinguishable from a page that never said anything.
    /// </remarks>
    Inherit = 0,

    /// <summary>No large title. The title is drawn inline, in the bar row.</summary>
    None,

    /// <summary>Always shown, and never collapses. The inline title stays hidden.</summary>
    Always,

    /// <summary>
    /// Shown at rest and collapsed into the inline title as the page scrolls — the iOS behaviour.
    /// Needs something to scroll: see <see cref="ShinyNav.ScrollSourceProperty"/>.
    /// </summary>
    Collapsing
}

/// <summary>What a bar item renders as.</summary>
public enum NavBarItemDisplay
{
    /// <summary>Icon when the item has one, text otherwise. The default.</summary>
    Auto,

    /// <summary>Icon only. An item with no icon draws nothing, so it is only ever set deliberately.</summary>
    Icon,

    /// <summary>Text only, even when the item has an icon.</summary>
    Text,

    /// <summary>Icon with the text beside it.</summary>
    IconAndText
}

/// <summary>Which end of the bar a group of items sits at.</summary>
public enum NavBarSide
{
    /// <summary>The leading end — where the back button is.</summary>
    Left,

    /// <summary>The trailing end.</summary>
    Right
}

/// <summary>How the system status bar's clock and icons are drawn over a <see cref="ShinyNavBar"/>.</summary>
/// <remarks>
/// This is the <em>foreground</em> only. The status bar has no background of its own on a modern
/// device — iOS never had one and Android 15 took the settable colour away — so what shows behind
/// the clock is whatever the app paints there, which is the bar's own background extended through
/// the top safe-area inset. That part is <see cref="ShinyNavigationPage.RespectSafeArea"/>'s job and
/// happens whichever value this holds.
/// </remarks>
public enum StatusBarStyle
{
    /// <summary>
    /// Follow the host: on a page it means "whatever the <see cref="ShinyNavigationPage"/> said".
    /// It exists so a page can choose <see cref="Auto"/> against a navigation page that pinned a
    /// style, which a page leaving the property alone would otherwise be indistinguishable from.
    /// </summary>
    Inherit = 0,

    /// <summary>
    /// Derived from the bar's background: light content over a dark bar, dark content over a light
    /// one. The default, and the only value that keeps up with a theme swap on its own.
    /// </summary>
    Auto,

    /// <summary>White clock and icons.</summary>
    LightContent,

    /// <summary>Black clock and icons.</summary>
    DarkContent,

    /// <summary>Leave the status bar entirely alone.</summary>
    None
}
