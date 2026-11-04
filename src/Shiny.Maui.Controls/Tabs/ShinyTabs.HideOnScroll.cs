using System.Runtime.CompilerServices;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

public static partial class ShinyTabs
{
    /// <summary>
    /// Set on a <see cref="ScrollView"/> or an <see cref="ItemsView"/> (<c>CollectionView</c>,
    /// <c>CarouselView</c>) inside a tab to slide the tab bar away while the user scrolls down and
    /// bring it back as soon as they scroll up, or reach the top.
    /// </summary>
    /// <remarks>
    /// <para>Works under a <see cref="ShinyTabbedPage"/> and under a Shell with
    /// <see cref="ShinyTabBarBehavior"/>: the bar is found by walking up from the scroller the first
    /// time it scrolls, so it can be set on a view that is not in a page yet.</para>
    /// <para>It never writes <see cref="ShinyTabBar.IsBarVisible"/> - it drives a separate flag
    /// alongside it. A page that hid the bar on purpose is not re-shown by a scroll, and a binding
    /// on the property is never overwritten. Changing tabs brings a scrolled-away bar back.</para>
    /// </remarks>
    public static readonly BindableProperty HideOnScrollProperty = BindableProperty.CreateAttached(
        "HideOnScroll", typeof(bool), typeof(ShinyTabs), false,
        propertyChanged: (b, _, n) => OnHideOnScrollChanged(b, (bool)n));

    /// <summary>
    /// How far the user has to scroll in one direction, in device-independent units, before the bar
    /// hides or comes back. Stops a finger resting on the screen flickering it. Defaults to 24.
    /// </summary>
    public static readonly BindableProperty HideOnScrollThresholdProperty = BindableProperty.CreateAttached(
        "HideOnScrollThreshold", typeof(double), typeof(ShinyTabs), 24d);

    /// <summary>Gets <see cref="HideOnScrollProperty"/>.</summary>
    public static bool GetHideOnScroll(BindableObject target) => (bool)target.GetValue(HideOnScrollProperty);

    /// <summary>Sets <see cref="HideOnScrollProperty"/>.</summary>
    public static void SetHideOnScroll(BindableObject target, bool value) => target.SetValue(HideOnScrollProperty, value);

    /// <summary>Gets <see cref="HideOnScrollThresholdProperty"/>.</summary>
    public static double GetHideOnScrollThreshold(BindableObject target) => (double)target.GetValue(HideOnScrollThresholdProperty);

    /// <summary>Sets <see cref="HideOnScrollThresholdProperty"/>.</summary>
    public static void SetHideOnScrollThreshold(BindableObject target, double value) => target.SetValue(HideOnScrollThresholdProperty, value);


    sealed class ScrollHideState
    {
        public double LastOffset;
        public double Travel;
        public WeakReference<ShinyTabBar>? Bar;
    }

    static readonly ConditionalWeakTable<BindableObject, ScrollHideState> scrollStates = new();


    static void OnHideOnScrollChanged(BindableObject target, bool enabled)
    {
        switch (target)
        {
            case ScrollView scrollView:
                scrollView.Scrolled -= OnScrollViewScrolled;
                if (enabled)
                    scrollView.Scrolled += OnScrollViewScrolled;
                break;

            case ItemsView itemsView:
                itemsView.Scrolled -= OnItemsViewScrolled;
                if (enabled)
                    itemsView.Scrolled += OnItemsViewScrolled;
                break;

            default:
                return;
        }

        if (!enabled && scrollStates.TryGetValue(target, out var state))
        {
            // Turning it off mid-scroll must not strand the bar off screen.
            if (state.Bar?.TryGetTarget(out var bar) == true)
                bar.SetScrollHidden(false);
            scrollStates.Remove(target);
        }
    }


    static void OnScrollViewScrolled(object? sender, ScrolledEventArgs e)
    {
        if (sender is ScrollView scrollView)
            TrackScroll(scrollView, e.ScrollY);
    }


    static void OnItemsViewScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        // A horizontal list scrolls sideways; only the vertical axis says anything about reading on.
        if (sender is ItemsView itemsView)
            TrackScroll(itemsView, e.VerticalOffset);
    }


    /// <summary>
    /// Folds one scroll position into the scroller's running travel and hides or shows the bar once
    /// that travel crosses the threshold. Internal so the logic is testable without a platform
    /// scroller, which a headless test host cannot make raise <c>Scrolled</c>.
    /// </summary>
    internal static void TrackScroll(BindableObject scroller, double offset)
    {
        var state = scrollStates.GetValue(scroller, _ => new ScrollHideState());
        var delta = offset - state.LastOffset;
        state.LastOffset = offset;

        if (FindBar(scroller, state) is not { } bar)
            return;

        // At (or bounced past) the top there is nothing above to read, so the chrome always comes back.
        if (offset <= 0)
        {
            state.Travel = 0;
            bar.SetScrollHidden(false);
            return;
        }

        // Travel resets when the direction flips, so the threshold is measured from the turn.
        if (delta == 0 || Math.Sign(delta) != Math.Sign(state.Travel))
            state.Travel = 0;
        state.Travel += delta;

        var threshold = GetHideOnScrollThreshold(scroller);
        if (state.Travel > threshold)
            bar.SetScrollHidden(true);
        else if (state.Travel < -threshold)
            bar.SetScrollHidden(false);
    }


    static ShinyTabBar? FindBar(BindableObject scroller, ScrollHideState state)
    {
        // Walked every time rather than cached: under Shell the one bar is moved from page to page,
        // and a cached reference would hide it on a page it is no longer on. The chain is short.
        var found = LocateBar(scroller);
        if (found is not null)
            state.Bar = new WeakReference<ShinyTabBar>(found);
        return found;
    }


    static ShinyTabBar? LocateBar(BindableObject scroller)
    {
        for (var element = (scroller as Element)?.Parent; element is not null; element = element.Parent)
        {
            switch (element)
            {
                case ShinyTabbedPage page:
                    return page.TabBar;

                case PageOverlay.ShinyOverlayRoot root:
                    foreach (var child in root.Children)
                    {
                        if (child is PageOverlay.TabBarLayer layer)
                        {
                            foreach (var item in layer.Children)
                            {
                                if (item is ShinyTabBar bar)
                                    return bar;
                            }
                        }
                    }
                    break;
            }
        }
        return null;
    }
}
