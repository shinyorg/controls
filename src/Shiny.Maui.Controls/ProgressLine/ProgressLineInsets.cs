using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

/// <summary>
/// Works out how far in from a page edge a <see cref="ProgressLine"/> has to sit so that it lands
/// against the chrome on that edge rather than underneath it.
/// </summary>
/// <remarks>
/// <para>
/// The hard part is not measuring the chrome, it is that "the top of the page" means different
/// things depending on how the page was assembled. <see cref="ShinyNavigationPage"/> wraps the page's
/// content in a two-row grid; <see cref="ShinyTabBarBehavior"/> instead drops its bar into a sibling
/// overlay layer; a native <c>NavigationPage</c> or <c>TabbedPage</c> keeps its bar outside the
/// content area entirely. In the first and third cases the space the line is positioned in already
/// starts past the bar and any inset would be a visible gap; in the second the bar is painted over
/// that same space and no inset means the line is hidden behind it.
/// </para>
/// <para>
/// One rule covers all of them: <b>a bar needs an inset exactly when it is painted inside the same
/// coordinate space the line is positioned in</b> — that is, when it is a descendant of the line's
/// own overlay root. Everything else resolves to zero, either because the bar is an ancestor-side
/// wrapper that already pushed the space down, or because it is not in the managed tree at all.
/// </para>
/// </remarks>
static class ProgressLineInsets
{
    /// <summary>
    /// The inset for <paramref name="position"/>, in device-independent units.
    /// </summary>
    /// <param name="page">The page the line is docked on.</param>
    /// <param name="root">
    /// The overlay root the line is positioned in — the coordinate space the answer is relative to.
    /// </param>
    public static double Resolve(ContentPage page, Element? root, ProgressLinePosition position)
    {
        var bar = FindBar(page, position);

        if (bar is null)
            return NativeChromeOwnsEdge(page, position) ? 0 : SafeAreaFor(position);

        // The bar is an ancestor-side wrapper (a NavHost row, a ShinyTabbedPage's root grid): the
        // line's space begins past it already, and it has taken the safe area with it.
        if (!IsDescendantOf(bar, root))
            return 0;

        return HeightOf(bar);
    }


    /// <summary>
    /// The drawn bar on this edge, anywhere on the page. Searched over the whole subtree rather than
    /// a known slot because the same bar legitimately lives in three different places.
    /// </summary>
    static VisualElement? FindBar(ContentPage page, ProgressLinePosition position) => position switch
    {
        ProgressLinePosition.Top => Descendants(page).OfType<ShinyNavBar>().FirstOrDefault(b => b.IsVisible),
        _ => Descendants(page).OfType<ShinyTabBar>().FirstOrDefault(b => b.IsVisible)
    };


    /// <summary>
    /// The measured height, falling back to the declared one before the first layout pass.
    /// </summary>
    /// <remarks>
    /// Measured wins because it is the only number that includes what the bar added for itself —
    /// <see cref="ShinyTabBar.RespectSafeArea"/> pads the home indicator into the bar's own height,
    /// so adding a safe-area inset on top of the declared <c>BarHeight</c> would double-count it.
    /// </remarks>
    static double HeightOf(VisualElement bar) => bar.Height > 0
        ? bar.Height
        : bar switch
        {
            ShinyNavBar nav => nav.BarHeight,
            ShinyTabBar tab => tab.BarHeight,
            _ => 0
        };


    /// <summary>
    /// Whether a platform bar owns this edge. When one does, MAUI hands the page a content area that
    /// already excludes both the bar and the safe area behind it, so the correct inset is zero.
    /// </summary>
    /// <remarks>
    /// Shell counts. It draws its navigation bar and bottom tabs natively exactly as
    /// <c>NavigationPage</c> and <c>TabbedPage</c> do, and leaving it out added the status-bar safe
    /// area on top of a content area that already started below the bar - the line painted a
    /// safe area's height down the page, through the page's own content.
    /// </remarks>
    internal static bool NativeChromeOwnsEdge(ContentPage page, ProgressLinePosition position)
    {
        for (var element = (Element?)page; element is not null; element = element.Parent)
        {
            switch (position)
            {
                case ProgressLinePosition.Top when element is NavigationPage:
                    return NavigationPage.GetHasNavigationBar(page);

                case ProgressLinePosition.Top when element is Shell:
                    return Shell.GetNavBarIsVisible(page);

                case ProgressLinePosition.Bottom when element is TabbedPage:
                    return true;

                case ProgressLinePosition.Bottom when element is Shell shell:
                    // Shell only draws bottom tabs when the current item has more than one section.
                    return Shell.GetTabBarIsVisible(page) && shell.CurrentItem?.Items.Count > 1;
            }
        }
        return false;
    }


    static double SafeAreaFor(ProgressLinePosition position)
        => position == ProgressLinePosition.Top ? SafeArea.Top() : SafeArea.Bottom();


    static bool IsDescendantOf(Element? element, Element? ancestor)
    {
        if (ancestor is null)
            return false;

        for (; element is not null; element = element.Parent)
        {
            if (ReferenceEquals(element, ancestor))
                return true;
        }
        return false;
    }


    static IEnumerable<Element> Descendants(Element root)
    {
        foreach (var child in ((IElementController)root).LogicalChildren)
        {
            yield return child;

            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }
}
