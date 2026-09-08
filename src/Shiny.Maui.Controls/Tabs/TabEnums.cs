namespace Shiny.Maui.Controls;

/// <summary>
/// How <see cref="ShinyTabBar"/> marks the selected tab.
/// </summary>
/// <remarks>
/// Every style draws inside the selected tab's own cell rather than sliding a shared marker between
/// cells. A shared marker has to be positioned from measured cell geometry, and there is a window
/// before the first layout pass where that geometry is zero — which is how an indicator ends up
/// parked in the corner on the first frame. Per-cell costs one extra (collapsed) view per tab and
/// is right on every head from the first frame.
/// </remarks>
public enum TabIndicatorStyle
{
    /// <summary>Nothing but the colour change.</summary>
    None,

    /// <summary>A filled rounded capsule behind the icon. The Material 3 look, and the default.</summary>
    Pill,

    /// <summary>A short bar along the top edge of the tab.</summary>
    Line,

    /// <summary>A short bar along the bottom edge of the tab.</summary>
    Underline,

    /// <summary>A small dot beneath the label.</summary>
    Dot
}


/// <summary>
/// How the selection indicator gets from the tab it was on to the tab it is going to.
/// </summary>
public enum TabIndicatorTransition
{
    /// <summary>
    /// It does not. The indicator is drawn inside each cell and simply appears on the new tab as it
    /// disappears from the old — which is the only thing that works before the bar has been laid
    /// out, and so is also the automatic fallback for <see cref="Slide"/>.
    /// </summary>
    None,

    /// <summary>
    /// One indicator travels horizontally from the old tab to the new one. The default, and the
    /// thing that makes a tab bar feel like a single control rather than five separate buttons.
    /// </summary>
    Slide
}


/// <summary>When a tab shows its text label.</summary>
public enum TabLabelMode
{
    /// <summary>Every tab is labelled.</summary>
    Always,

    /// <summary>Only the selected tab is labelled; the rest are icon-only.</summary>
    SelectedOnly,

    /// <summary>Icons only.</summary>
    Never
}


/// <summary>What pressing <see cref="ShinyTabBar.CenterButton"/> does.</summary>
public enum TabCenterMode
{
    /// <summary>
    /// Raise <see cref="ShinyTabBar.CenterClicked"/> and run <see cref="TabCenterButton.Command"/>.
    /// Nothing is presented.
    /// </summary>
    Action,

    /// <summary>
    /// Present the current page's actions or content above the button. Falls back to
    /// <see cref="Action"/> when the page (and the button) declare neither, so a centre button that
    /// is only ever a button behaves like one without being reconfigured.
    /// </summary>
    Menu
}


/// <summary>Which level of a <see cref="Shell"/>'s hierarchy <see cref="ShinyTabBarBehavior"/> turns into tabs.</summary>
public enum ShellTabSource
{
    /// <summary>
    /// The sections (<c>Tab</c> elements) of the current <see cref="ShellItem"/> when there is more
    /// than one, otherwise the Shell's top-level items. This mirrors where MAUI's own bottom bar
    /// takes its tabs from, so an existing Shell needs no restructuring.
    /// </summary>
    Auto,

    /// <summary>Always the sections of the current <see cref="ShellItem"/>.</summary>
    Sections,

    /// <summary>Always the Shell's top-level items.</summary>
    Items
}


/// <summary>
/// Whether <see cref="ShinyTabBar"/> is welded to the bottom edge or floats above the content.
/// </summary>
/// <remarks>
/// The two differ in more than corner radius. A docked bar owns the bottom of the page: the content
/// stops where the bar starts, and the bar's background keeps painting down through the home
/// indicator so no strip of page shows underneath it. A floating bar owns nothing — the content runs
/// the full height of the page and the bar is a capsule laid over it, inset out of the safe area on
/// every side so it clears the home indicator rather than painting into it.
/// </remarks>
public enum TabBarStyle
{
    /// <summary>Welded to the bottom edge, square corners, background through the safe area. The default.</summary>
    Docked,

    /// <summary>A capsule inset from the page edges, floating over content that scrolls beneath it.</summary>
    Floating
}
