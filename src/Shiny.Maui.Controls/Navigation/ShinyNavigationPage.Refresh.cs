namespace Shiny.Maui.Controls;

public partial class ShinyNavigationPage
{
    /// <summary>
    /// Pushes everything the page says into its bar. Called on install, on every navigation, and
    /// whenever a property either of them exposes changes — so it is written to be cheap and
    /// idempotent rather than incremental.
    /// </summary>
    void Refresh(ContentPage page)
    {
        if (!this.installs.TryGetValue(page, out var install))
            return;

        var bar = install.Bar;

        // ---- what the page says -----------------------------------------------------------------
        bar.Title = page.Title;
        bar.Subtitle = ShinyNav.GetSubtitle(page);
        bar.LargeTitle = ShinyNav.GetLargeTitle(page);
        bar.TitleView = NavigationPage.GetTitleView(page);
        bar.TitleIcon = NavigationPage.GetTitleIconImageSource(page);

        // Each of these carries its own "not answered" value - Auto and Inherit - so a page can
        // override the navigation page with any real value, the default included. Asking IsSet
        // instead would make "the page set it to the default" unreadable.
        var alignment = ShinyNav.GetTitleAlignment(page);
        bar.TitleAlignment = alignment == NavBarTitleAlignment.Auto ? this.TitleAlignment : alignment;

        var large = ShinyNav.GetLargeTitleDisplay(page);
        bar.LargeTitleDisplay = large == LargeTitleDisplay.Inherit ? this.LargeTitleDisplay : large;

        // ---- colours: the page overrides the navigation page, which overrides the theme ----------
        bar.BarBackgroundColor = ShinyNav.GetBarBackgroundColor(page) ?? this.BarBackgroundColor;
        bar.BarBackground = this.IsSet(BarBackgroundProperty) ? this.BarBackground : null;
        bar.BarTextColor = ShinyNav.GetBarTextColor(page) ?? this.BarTextColor;
        bar.IconColor = page.IsSet(NavigationPage.IconColorProperty)
            ? NavigationPage.GetIconColor(page)
            : this.BarIconColor;

        // ---- appearance the navigation page owns -------------------------------------------------
        bar.BarHeight = this.BarHeight;
        bar.BarPadding = this.BarPadding;
        bar.HasShadow = this.HasShadow;
        bar.HasSeparator = this.HasSeparator;
        bar.RespectSafeArea = this.RespectSafeArea;
        bar.ItemSpacing = this.ItemSpacing;
        bar.IconSize = this.IconSize;
        bar.MaxVisibleItems = this.MaxVisibleItems;
        bar.OverflowIcon = this.OverflowIcon;
        bar.MenuTemplate = this.MenuTemplate;
        bar.AnimationDuration = this.AnimationDuration;
        bar.LargeTitleHeight = this.LargeTitleHeight;
        bar.LargeTitleCollapseDistance = this.LargeTitleCollapseDistance;
        bar.LargeTitleFontSize = this.LargeTitleFontSize;
        bar.TitleFontSize = this.TitleFontSize;
        bar.TitleFontFamily = this.TitleFontFamily;
        bar.TitleFontAttributes = this.TitleFontAttributes;

        // ---- back button --------------------------------------------------------------------------
        // Not "is there more than one page": a page can be inserted below the root, and the page that
        // is showing is the only one whose back button is on screen.
        // NavigationStack is IReadOnlyList, so the position is walked rather than asked for.
        var stack = this.Navigation.NavigationStack;
        var index = -1;
        for (var i = 0; i < stack.Count; i++)
        {
            if (ReferenceEquals(stack[i], page))
            {
                index = i;
                break;
            }
        }

        // Off the stack entirely means mid-navigation or hosted without one; fall back to whether
        // there is anything under the page at all.
        var canGoBack = index > 0 || (index < 0 && stack.Count > 1);

        bar.IsBackButtonVisible = canGoBack && NavigationPage.GetHasBackButton(page);
        bar.BackButtonText = NavigationPage.GetBackButtonTitle(page);
        bar.BackButtonIcon = ShinyNav.GetBackButtonIcon(page);
        bar.BackButtonCommand = ShinyNav.GetBackButtonCommand(page);
        bar.BackButtonCommandParameter = ShinyNav.GetBackButtonCommandParameter(page);

        // ---- visibility ---------------------------------------------------------------------------
        bar.IsVisible = install.WantsBar && ShinyNav.GetIsNavBarVisible(page) && this.IsNavBarVisible;

        // ---- items --------------------------------------------------------------------------------
        this.SyncItems(page, bar);

        // ---- the collapsing large title's scroll source -------------------------------------------
        bar.AttachScrollSource(bar.EffectiveLargeTitleDisplay == LargeTitleDisplay.Collapsing
            ? ShinyNav.GetScrollSource(page) ?? ShinyNavBar.FindScrollSource(install.Host)
            : null);

        // ---- the status bar over it ---------------------------------------------------------------
        // Last, and unconditionally: the colours above are what Auto is derived from, and a push onto
        // a page with a different bar colour reaches here as a Refresh of the page being shown.
        this.SyncStatusBar(page);
    }


    void SyncItems(ContentPage page, ShinyNavBar bar)
    {
        var left = ShinyNav.GetLeftItems(page);
        var right = ShinyNav.GetRightItems(page);

        Replace(bar.LeftItems, left);

        // The page's own ToolbarItems come first: they are the items that were already there before
        // the bar was adopted, and pushing them behind newly-declared ones would silently reorder a
        // toolbar nobody touched.
        Replace(bar.RightItems, page.ToolbarItems.Concat(right));

        // An attached-property collection has no parent, so nothing has ever given its items a
        // binding context - their bindings would resolve against null and the item would look unwired.
        // The page's own ToolbarItems are already parented by the page and are left alone.
        foreach (var item in left.Concat(right))
            SetInheritedBindingContext(item, page.BindingContext);
    }


    static void Replace(IList<ToolbarItem> target, IEnumerable<ToolbarItem> source)
    {
        var items = source.ToList();

        // Rebuilt wholesale rather than diffed: the lists are a handful of items long, and an
        // incremental sync of two collections against a third is where ordering bugs live.
        if (target.Count == items.Count && target.SequenceEqual(items))
            return;

        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

}
