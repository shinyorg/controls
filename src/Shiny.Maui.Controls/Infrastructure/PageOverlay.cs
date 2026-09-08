namespace Shiny.Maui.Controls.Infrastructure;

/// <summary>
/// The single wrapper a page grows so controls can paint above its content — tooltips, walkthrough
/// scrims, dialogs.
/// </summary>
/// <remarks>
/// A page's <c>Content</c> is one view. To put anything on top of it, that view has to become a child
/// of a layout the extra layer can also live in. Every control that needs this used to wrap the page
/// itself, so two of them wrapped it twice and the second one's layer sat inside the first one's
/// grid — which is fine until they disagree about z-order. Wrapping is done once here, into a marked
/// <see cref="ShinyOverlayRoot"/>, and every layer after that is a sibling inside it.
/// </remarks>
static class PageOverlay
{
    /// <summary>Marks the grid this class installed, so it is reused rather than nested.</summary>
    /// <remarks>
    /// Edge-to-edge. It is a pass-through wrapper, not a layout anyone asked for, so it must not
    /// introduce an inset of its own - and a <see cref="Grid"/> defaults to
    /// <see cref="SafeAreaRegions.Container"/>, which is exactly that. Left at the default it pushes
    /// the whole page in by the status bar and the home indicator, so anything that docks to a page
    /// edge - a nav bar's background, a tab bar, the progress line - stops short of the edge it is
    /// docked to and leaves a strip of page showing beyond it. The page's own content is a child of
    /// this and keeps whatever MAUI's default for its type is, so a layout still insets itself; the
    /// insets are computed against each view's own frame, so nothing double-pads.
    /// </remarks>
    internal sealed class ShinyOverlayRoot : Grid
    {
        public ShinyOverlayRoot() => this.SafeAreaEdges = SafeAreaEdges.None;
    }

    /// <summary>
    /// Marks a child of the root as one of ours rather than the page's own content. Without it,
    /// "the page's content" has to be inferred - and every inference (first child, lowest ZIndex)
    /// breaks the moment a layer is added in a different order.
    /// </summary>
    internal interface IOverlayLayer;

    /// <summary>
    /// Marker layers. Each control gets its own type because the layer is looked up by type — asking
    /// for a bare <see cref="AbsoluteLayout"/> would match any other control's layer that happened to
    /// be added first, and two controls would end up sharing (and clearing) each other's children.
    /// </summary>
    internal sealed class TooltipLayer : AbsoluteLayout, IOverlayLayer;

    internal sealed class WalkthroughLayer : AbsoluteLayout, IOverlayLayer;

    /// <summary>
    /// Grid-based, unlike the layers above. An <see cref="AbsoluteLayout"/> child sized with
    /// <c>AutoSize</c> and positioned proportionally is not reliably arranged across MAUI's heads —
    /// it comes back unmeasured, so the popup is present in the tree, reports itself visible, and
    /// paints nothing. Alignment plus a margin is boring and works everywhere.
    /// </summary>
    internal sealed class QuickEntryLayer : Grid, IOverlayLayer;

    internal sealed class ScreenGlowLayer : Grid, IOverlayLayer;

    /// <summary>
    /// Where <see cref="ShinyTabBarBehavior"/> docks the tab bar over a Shell page. Below the menu
    /// layer, because the menu is drawn above the bar that opened it.
    /// </summary>
    internal sealed class TabBarLayer : Grid, IOverlayLayer;

    /// <summary>
    /// Where <see cref="ProgressLine"/> docks. Above the tab bar, because a load indicator that runs
    /// underneath the chrome it is reporting on is not an indicator.
    /// </summary>
    internal sealed class ProgressLineLayer : Grid, IOverlayLayer;

    /// <summary>
    /// Where <see cref="ShinyTabBar"/> puts its centre menu when it is hosted on a page it does not
    /// own the layout of - a Shell page, chiefly. A <see cref="ShinyTabbedPage"/> hands the bar its
    /// own layer instead and never comes through here, because installing an overlay root re-parents
    /// the page's content and that costs a full native rebuild.
    /// </summary>
    internal sealed class TabMenuLayer : Grid, IOverlayLayer;

    /// <summary>
    /// Where <see cref="ShinyNavBar"/> puts its overflow menu. Above the tab bar's menu for the same
    /// reason the nav bar sits above the page: it is the chrome the user just touched.
    /// </summary>
    internal sealed class NavMenuLayer : Grid, IOverlayLayer;

    /// <summary>
    /// Where an <see cref="ImageViewer"/> puts its lightbox when it is not inside an explicit
    /// <see cref="OverlayHost"/>. Grid-based, so the overlay fills the page: the layer it replaced
    /// was "whatever Grid happens to be the page's root content", which drops a full-screen overlay
    /// into cell (0,0) of a grid that has more than one cell.
    /// </summary>
    internal sealed class ImageViewerLayer : Grid, IOverlayLayer;

    /// <summary>
    /// Z-order for the layers, so the intent is stated once rather than guessed at each call site.
    /// A tooltip sits above page content, a walkthrough dims everything including tooltips, and a
    /// modal dialog wins outright.
    /// </summary>
    public static class Layers
    {
        /// <summary>Page chrome: above the page's content, below everything that annotates it.</summary>
        public const int TabBar = 8_400;

        /// <summary>
        /// The page-edge progress line, just above the tab bar it sits against so the two never
        /// overlap ambiguously, and below any menu either bar opens.
        /// </summary>
        public const int ProgressLine = 8_450;

        /// <summary>Below a tooltip: the tab bar's menu is page chrome, not an annotation on top of it.</summary>
        public const int TabMenu = 8_500;

        /// <summary>The nav bar's overflow menu, above the tab bar's and below anything annotating the page.</summary>
        public const int NavMenu = 8_600;

        /// <summary>
        /// A ribbon's dropdowns and the popup a collapsed group opens. Above the other bars' menus
        /// because a ribbon is the topmost chrome on a desktop window, and still below a tooltip —
        /// which can be raised over a menu line the pointer is resting on.
        /// </summary>
        public const int RibbonMenu = 8_700;

        public const int Tooltip = 9_000;
        public const int Walkthrough = 9_500;

        /// <summary>
        /// A full-screen image lightbox. Above a walkthrough, which is annotating the page rather
        /// than replacing it, and below a dialog - a confirmation raised over an open photo is still
        /// the thing being answered.
        /// </summary>
        public const int ImageViewer = 9_800;

        public const int Dialog = 10_000;

        /// <summary>Above a dialog: the quick entry popup is summoned over whatever is on screen, including one.</summary>
        public const int QuickEntry = 10_500;

        /// <summary>Above everything. The glow rims the screen and is never the thing being interacted with.</summary>
        public const int ScreenGlow = 11_000;
    }


    /// <summary>
    /// Returns the page's overlay root, installing it on first use. Null when the element is not on a
    /// <see cref="ContentPage"/> — a control that is offscreen, mid-navigation, or hosted somewhere
    /// that has no page content to wrap at all.
    /// </summary>
    public static ShinyOverlayRoot? GetOrCreateRoot(Element anchor)
    {
        var page = FindPage(anchor);
        return page is null ? null : GetOrCreateRoot(page);
    }


    public static ShinyOverlayRoot GetOrCreateRoot(ContentPage page)
    {
        if (page.Content is ShinyOverlayRoot existing)
            return existing;

        var root = new ShinyOverlayRoot();
        if (page.Content is View content)
        {
            // Detach before re-parenting: MAUI throws if a view already has a parent.
            page.Content = null;
            root.Children.Add(content);
        }
        page.Content = root;
        return root;
    }


    /// <summary>
    /// Returns the page's layer of type <typeparamref name="T"/>, adding one at <paramref name="zIndex"/>
    /// on first use. The layer is input-transparent so it never eats a tap meant for the page; the
    /// children a control puts in it are not, so they still receive their own.
    /// </summary>
    public static T? GetOrCreateLayer<T>(Element anchor, int zIndex) where T : Layout, new()
    {
        var root = GetOrCreateRoot(anchor);
        return root is null ? null : GetOrCreateLayer<T>(root, zIndex);
    }


    public static T GetOrCreateLayer<T>(ShinyOverlayRoot root, int zIndex) where T : Layout, new()
    {
        if (root.Children.OfType<T>().FirstOrDefault() is { } existing)
            return existing;

        var layer = new T
        {
            InputTransparent = true,
            CascadeInputTransparent = false,
            ZIndex = zIndex
        };
        root.Children.Add(layer);
        return layer;
    }


    /// <summary>
    /// The page's own content inside an overlay root — everything that is not a layer this class
    /// installed. Null when the page had no content to begin with.
    /// </summary>
    public static View? ContentOf(ShinyOverlayRoot root)
        => root.Children.OfType<View>().FirstOrDefault(v => v is not IOverlayLayer);


    /// <summary>The <see cref="ContentPage"/> an element is sitting on, or null if it is not on one yet.</summary>
    public static ContentPage? FindPage(Element? element)
    {
        while (element is not null)
        {
            if (element is ContentPage page)
                return page;

            element = element.Parent;
        }
        return null;
    }


    /// <summary>
    /// The page currently on screen, for callers with no element to start from (a service, say).
    /// Walks the navigation containers down to the leaf that is actually showing.
    /// </summary>
    public static ContentPage? CurrentPage()
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        return page is null ? null : LeafPage(page);
    }


    public static ContentPage? LeafPage(Page page) => page switch
    {
        ContentPage cp => cp,
        NavigationPage np when np.CurrentPage is not null => LeafPage(np.CurrentPage),
        Shell shell when shell.CurrentPage is not null => LeafPage(shell.CurrentPage),
        TabbedPage tp when tp.CurrentPage is not null => LeafPage(tp.CurrentPage),
        FlyoutPage fp when fp.Detail is not null => LeafPage(fp.Detail),
        _ => null
    };
}
