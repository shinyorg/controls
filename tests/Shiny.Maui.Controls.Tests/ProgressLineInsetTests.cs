using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.Infrastructure;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The decision that stops the line hiding behind the chrome it is reporting on. The rule under test
/// is that a bar earns an inset exactly when it is painted inside the same coordinate space the line
/// is positioned in — every other arrangement already starts past it.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ProgressLineInsetTests
{
    public ProgressLineInsetTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }

    /// <summary>A page whose content is an overlay root, as every Shiny overlay leaves it.</summary>
    static ContentPage PageWithRoot(out PageOverlay.ShinyOverlayRoot root)
    {
        var page = new ContentPage { Content = new VerticalStackLayout() };
        root = PageOverlay.GetOrCreateRoot(page);
        return page;
    }


    [Fact]
    public void ATabBarInsideTheOverlayRootPushesTheLineUp()
    {
        var page = PageWithRoot(out var root);
        var layer = PageOverlay.GetOrCreateLayer<PageOverlay.TabBarLayer>(root, PageOverlay.Layers.TabBar);
        var bar = new ShinyTabBar { BarHeight = 62 };
        layer.Children.Add(bar);

        ProgressLineInsets
            .Resolve(page, root, ProgressLinePosition.Bottom)
            .ShouldBe(62);
    }


    /// <summary>
    /// The <c>ShinyTabbedPage</c> / native <c>TabbedPage</c> shape: the bar is a sibling of the whole
    /// overlay root, so the root's own space already ends above it and an inset would be a gap.
    /// </summary>
    [Fact]
    public void ATabBarOutsideTheOverlayRootEarnsNoInset()
    {
        var page = new ContentPage();
        var root = new PageOverlay.ShinyOverlayRoot();
        var host = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) }
        };
        var bar = new ShinyTabBar { BarHeight = 62 };

        host.Children.Add(root);
        host.Children.Add(bar);
        page.Content = host;

        ProgressLineInsets
            .Resolve(page, root, ProgressLinePosition.Bottom)
            .ShouldBe(0);
    }


    [Fact]
    public void ANavBarInsideTheOverlayRootPushesTheLineDown()
    {
        var page = PageWithRoot(out var root);
        var bar = new ShinyNavBar { BarHeight = 56 };
        root.Children.Add(bar);

        ProgressLineInsets
            .Resolve(page, root, ProgressLinePosition.Top)
            .ShouldBe(56);
    }


    /// <summary>
    /// The <c>ShinyNavigationPage</c> shape: the bar sits in row 0 of a host grid wrapping the root,
    /// so the root already begins below it.
    /// </summary>
    [Fact]
    public void ANavBarWrappingTheOverlayRootEarnsNoInset()
    {
        var page = new ContentPage();
        var root = new PageOverlay.ShinyOverlayRoot();
        var host = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }
        };
        var bar = new ShinyNavBar { BarHeight = 56 };

        Grid.SetRow(bar, 0);
        Grid.SetRow(root, 1);
        host.Children.Add(root);
        host.Children.Add(bar);
        page.Content = host;

        ProgressLineInsets
            .Resolve(page, root, ProgressLinePosition.Top)
            .ShouldBe(0);
    }


    /// <summary>
    /// A hidden bar occupies no space, so insetting for it would leave the line floating away from
    /// the edge for no visible reason.
    /// </summary>
    [Fact]
    public void AHiddenBarIsIgnored()
    {
        var page = PageWithRoot(out var root);
        root.Children.Add(new ShinyNavBar { BarHeight = 56, IsVisible = false });

        ProgressLineInsets
            .Resolve(page, root, ProgressLinePosition.Top)
            .ShouldBe(0);
    }


    /// <summary>
    /// MAUI hands a page inside a <c>NavigationPage</c> a content area that already excludes the
    /// native bar and the status bar behind it.
    /// </summary>
    [Fact]
    public void ANativeNavigationBarEarnsNoInset()
    {
        var page = new ContentPage { Content = new VerticalStackLayout() };
        var root = PageOverlay.GetOrCreateRoot(page);
        _ = new NavigationPage(page);

        NavigationPage.SetHasNavigationBar(page, true);

        ProgressLineInsets
            .Resolve(page, root, ProgressLinePosition.Top)
            .ShouldBe(0);
    }


    /// <summary>
    /// A <c>NavigationPage</c> whose bar the page turned off leaves the top edge to the line — and on
    /// a head with a notch, the safe area is then the line's problem rather than the bar's.
    /// </summary>
    [Fact]
    public void ANavigationPageWithItsBarOffFallsBackToTheSafeArea()
    {
        var page = new ContentPage { Content = new VerticalStackLayout() };
        var root = PageOverlay.GetOrCreateRoot(page);
        _ = new NavigationPage(page);

        NavigationPage.SetHasNavigationBar(page, false);

        // Zero on this TFM: only Apple's heads report an inset, and the tests do not run there.
        ProgressLineInsets
            .Resolve(page, root, ProgressLinePosition.Top)
            .ShouldBe(0);
    }


    /// <summary>
    /// Shell draws its navigation bar natively, so the page's content area already starts below it
    /// and the status bar. Missing this added the safe area on top, and the line painted a status
    /// bar's height down the page, through the page's own content.
    /// </summary>
    [Fact]
    public void AShellNavigationBarOwnsTheTopEdge()
    {
        var page = ShellPage(sections: 1);

        ProgressLineInsets.NativeChromeOwnsEdge(page, ProgressLinePosition.Top).ShouldBeTrue();
    }


    [Fact]
    public void AShellPageWithItsNavBarHiddenLeavesTheTopEdgeToTheLine()
    {
        var page = ShellPage(sections: 1);
        Shell.SetNavBarIsVisible(page, false);

        ProgressLineInsets.NativeChromeOwnsEdge(page, ProgressLinePosition.Top).ShouldBeFalse();
    }


    [Fact]
    public void ShellBottomTabsOwnTheBottomEdge()
        => ProgressLineInsets.NativeChromeOwnsEdge(ShellPage(sections: 2), ProgressLinePosition.Bottom).ShouldBeTrue();


    /// <summary>A single-section item draws no tab bar, so the bottom edge is the home indicator's.</summary>
    [Fact]
    public void ASingleSectionShellItemDrawsNoBottomTabs()
        => ProgressLineInsets.NativeChromeOwnsEdge(ShellPage(sections: 1), ProgressLinePosition.Bottom).ShouldBeFalse();


    static ContentPage ShellPage(int sections)
    {
        var page = new ContentPage { Content = new VerticalStackLayout() };
        var item = new ShellItem();
        item.Items.Add(new ShellSection { Items = { new ShellContent { Content = page } } });
        for (var i = 1; i < sections; i++)
            item.Items.Add(new ShellSection { Items = { new ShellContent { Content = new ContentPage() } } });

        var shell = new Shell();
        shell.Items.Add(item);
        return page;
    }

    /// <summary>
    /// The line applies the safe area itself. A layer that also inset by it (a Grid's default) pushed
    /// a bottom line up by the home indicator twice.
    /// </summary>
    [Fact]
    public void TheLineLayerDoesNotApplyTheSafeAreaAgain()
        => new PageOverlay.ProgressLineLayer().SafeAreaEdges.ShouldBe(SafeAreaEdges.None);
}
