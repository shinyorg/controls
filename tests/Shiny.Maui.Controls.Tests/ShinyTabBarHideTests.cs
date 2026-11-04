using Microsoft.Maui.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// Hiding the bar on a flag and on scroll. Headless, so every hide applies its end state without
/// animating - what is under test is which state it ends in and who owns the row.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ShinyTabBarHideTests
{
    public ShinyTabBarHideTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }

    static (ShinyTabbedPage Page, ScrollView Scroller) Build()
    {
        var scroller = new ScrollView { Content = new Label { Text = "long" } };
        ShinyTabs.SetHideOnScroll(scroller, true);

        var page = new ShinyTabbedPage { Transition = StateTransition.None };
        page.Tabs.Add(new ShinyTabItem { Title = "A", Route = "a", ContentTemplate = new DataTemplate(() => scroller) });
        page.Tabs.Add(new ShinyTabItem { Title = "B", Route = "b", ContentTemplate = new DataTemplate(() => new Label()) });
        return (page, scroller);
    }


    [Fact]
    public void TheFlagHidesTheBarAndHandsItsRowToTheContent()
    {
        var (page, _) = Build();
        Grid.GetRowSpan(page.ContentHost).ShouldBe(1);

        page.TabBarIsVisible = false;

        page.TabBar.IsBarVisible.ShouldBeFalse();
        page.TabBar.IsVisible.ShouldBeFalse();
        page.TabBar.InputTransparent.ShouldBeTrue();
        Grid.GetRowSpan(page.ContentHost).ShouldBe(2);

        page.TabBarIsVisible = true;

        page.TabBar.IsVisible.ShouldBeTrue();
        page.TabBar.TranslationY.ShouldBe(0);
        page.TabBar.InputTransparent.ShouldBeFalse();
        Grid.GetRowSpan(page.ContentHost).ShouldBe(1);
    }


    [Fact]
    public void FadeLeavesTheBarInPlace()
    {
        var (page, _) = Build();
        page.TabBarHideAnimation = TabBarHideAnimation.Fade;

        page.TabBarIsVisible = false;

        page.TabBar.TranslationY.ShouldBe(0);
        page.TabBar.Opacity.ShouldBe(0);

        page.TabBarIsVisible = true;
        page.TabBar.Opacity.ShouldBe(1);
    }


    [Fact]
    public void ScrollingDownHidesAndScrollingUpShows()
    {
        var (page, scroller) = Build();

        ShinyTabs.TrackScroll(scroller, 10);
        page.TabBar.EffectiveBarVisible.ShouldBeTrue("under the threshold");

        ShinyTabs.TrackScroll(scroller, 60);
        page.TabBar.EffectiveBarVisible.ShouldBeFalse();
        page.TabBar.IsBarVisible.ShouldBeTrue("the consumer's property is never written");

        ShinyTabs.TrackScroll(scroller, 50);
        page.TabBar.EffectiveBarVisible.ShouldBeFalse("a small turn is not enough");

        ShinyTabs.TrackScroll(scroller, 20);
        page.TabBar.EffectiveBarVisible.ShouldBeTrue();
    }


    [Fact]
    public void ReachingTheTopAlwaysShows()
    {
        var (page, scroller) = Build();
        ShinyTabs.TrackScroll(scroller, 200);
        page.TabBar.EffectiveBarVisible.ShouldBeFalse();

        ShinyTabs.TrackScroll(scroller, 190);
        page.TabBar.EffectiveBarVisible.ShouldBeFalse();

        ShinyTabs.TrackScroll(scroller, 0);
        page.TabBar.EffectiveBarVisible.ShouldBeTrue();
    }


    [Fact]
    public void ScrollNeverOverridesAPageThatHidTheBar()
    {
        var (page, scroller) = Build();
        page.TabBarIsVisible = false;

        ShinyTabs.TrackScroll(scroller, 200);
        ShinyTabs.TrackScroll(scroller, 0);

        page.TabBar.EffectiveBarVisible.ShouldBeFalse();
        page.TabBar.IsVisible.ShouldBeFalse();
    }


    [Fact]
    public void ChangingTabsBringsAScrolledAwayBarBack()
    {
        var (page, scroller) = Build();
        ShinyTabs.TrackScroll(scroller, 200);
        page.TabBar.EffectiveBarVisible.ShouldBeFalse();

        page.GoTo(1).ShouldBeTrue();

        page.TabBar.EffectiveBarVisible.ShouldBeTrue();
        page.TabBar.IsVisible.ShouldBeTrue();
    }


    [Fact]
    public void TurningHideOnScrollOffRestoresTheBar()
    {
        var (page, scroller) = Build();
        ShinyTabs.TrackScroll(scroller, 200);

        ShinyTabs.SetHideOnScroll(scroller, false);

        page.TabBar.EffectiveBarVisible.ShouldBeTrue();
    }
}
