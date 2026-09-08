using Microsoft.Maui.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// What <see cref="ShinyTabbedPage"/> promises to keep from MAUI's own <c>TabbedPage</c>: content
/// that is not built until its tab is reached, built once, and a <see cref="ContentPage"/> in a
/// template that still behaves like a page.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ShinyTabbedPageTests
{
    public ShinyTabbedPageTests()
    {
        TestDispatcherProvider.Install();

        // A fresh Application per test, not `Application.Current ?? new` - Application.Current is
        // process-wide, so anything one test merges would leak into the rest of the collection.
        _ = new Application();
    }

    static ShinyTabbedPage Build(out List<int> builds, int tabCount = 3)
    {
        var counts = new List<int>();
        var page = new ShinyTabbedPage { Transition = StateTransition.None };

        for (var i = 0; i < tabCount; i++)
        {
            counts.Add(0);
            var index = i;
            page.Tabs.Add(new ShinyTabItem
            {
                Title = $"Tab{index}",
                Route = $"tab{index}",
                ContentTemplate = new DataTemplate(() =>
                {
                    counts[index]++;
                    return new Label { Text = $"content{index}" };
                })
            });
        }

        builds = counts;
        return page;
    }

    static string? HostedText(ShinyTabbedPage page)
    {
        foreach (var host in Descendants(page.ContentHost).OfType<Label>())
            return host.Text;

        return null;
    }

    static IEnumerable<Element> Descendants(Element root)
    {
        var children = root switch
        {
            Layout layout => layout.Children.OfType<Element>(),
            ContentView content when content.Content is not null => [content.Content],
            Border border when border.Content is not null => [border.Content],
            _ => Enumerable.Empty<Element>()
        };

        foreach (var child in children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }


    [Fact]
    public void OnlyTheFirstTabIsBuiltOnLoad()
    {
        _ = Build(out var builds);

        builds.ShouldBe([1, 0, 0]);
    }


    [Fact]
    public void ATabIsBuiltWhenItIsFirstSelected()
    {
        var page = Build(out var builds);

        page.SelectedIndex = 2;

        builds.ShouldBe([1, 0, 1]);
    }


    [Fact]
    public void ReturningToATabDoesNotRebuildIt()
    {
        var page = Build(out var builds);

        page.SelectedIndex = 1;
        page.SelectedIndex = 0;
        page.SelectedIndex = 1;

        builds.ShouldBe([1, 1, 0]);
    }


    [Fact]
    public void TurningCachingOffRebuildsTheTab()
    {
        var page = Build(out var builds);
        page.CacheTabContent = false;

        page.SelectedIndex = 1;
        page.SelectedIndex = 0;
        page.SelectedIndex = 1;

        builds[1].ShouldBe(2);
    }


    [Fact]
    public void TheSelectedTabsContentIsTheOneOnScreen()
    {
        var page = Build(out _);

        page.SelectedIndex = 1;

        HostedText(page).ShouldBe("content1");
    }


    [Fact]
    public void SelectionIsMirroredBetweenThePageAndTheBar()
    {
        var page = Build(out _);

        page.SelectedIndex = 2;
        page.TabBar.SelectedIndex.ShouldBe(2);
        page.SelectedItem.ShouldBe(page.Tabs[2]);

        page.TabBar.SelectedIndex = 1;
        page.SelectedIndex.ShouldBe(1);
    }


    [Fact]
    public void GoToByRouteSelectsThatTab()
    {
        var page = Build(out _);

        page.GoTo("tab2").ShouldBeTrue();

        page.SelectedIndex.ShouldBe(2);
    }


    [Fact]
    public void TheTitleFollowsTheSelectedTab()
    {
        var page = Build(out _);

        page.SelectedIndex = 1;

        page.Title.ShouldBe("Tab1");
    }


    [Fact]
    public void TheTitleCanBeLeftAlone()
    {
        var page = Build(out _);
        page.SyncTitleWithTab = false;
        page.Title = "Fixed";

        page.SelectedIndex = 1;

        page.Title.ShouldBe("Fixed");
    }


    // --- ContentPage adoption --------------------------------------------------------------------

    [Fact]
    public void ATemplateThatInflatesAPageIsAdopted()
    {
        var page = new ShinyTabbedPage { Transition = StateTransition.None };
        page.Tabs.Add(new ShinyTabItem
        {
            ContentTemplate = new DataTemplate(() => new ContentPage
            {
                Title = "From the page",
                Content = new Label { Text = "adopted" }
            })
        });

        var tab = page.Tabs[0];

        tab.AdoptedPage.ShouldNotBeNull();
        tab.Title.ShouldBe("From the page");
        HostedText(page).ShouldBe("adopted");
    }


    [Fact]
    public void AnAdoptedPageIsParentedToTheTabbedPage()
    {
        var page = new ShinyTabbedPage { Transition = StateTransition.None };
        page.Tabs.Add(new ShinyTabItem
        {
            ContentTemplate = new DataTemplate(() => new ContentPage { Content = new Label() })
        });

        // Parented, not hosted: it is what makes Navigation and the inherited BindingContext resolve
        // in the adopted page's code-behind.
        page.Tabs[0].AdoptedPage!.Parent.ShouldBe(page);
    }


    [Fact]
    public void AnAdoptedPagesBindingContextReachesItsContent()
    {
        var model = new object();
        var page = new ShinyTabbedPage { Transition = StateTransition.None };
        page.Tabs.Add(new ShinyTabItem
        {
            ContentTemplate = new DataTemplate(() => new ContentPage
            {
                BindingContext = model,
                Content = new Label()
            })
        });

        Descendants(page.ContentHost).OfType<Label>().First().BindingContext.ShouldBe(model);
    }


    [Fact]
    public void ATabTellsItsContentWhenItIsEnteredAndLeft()
    {
        var page = new ShinyTabbedPage { Transition = StateTransition.None };
        page.Tabs.Add(new ShinyTabItem { ContentTemplate = new DataTemplate(() => new TrackingPage()) });
        page.Tabs.Add(new ShinyTabItem { Content = new Label() });

        var tracked = (TrackingPage)page.Tabs[0].AdoptedPage!;
        tracked.Appeared.ShouldBe(1);

        page.SelectedIndex = 1;
        tracked.Disappeared.ShouldBe(1);

        page.SelectedIndex = 0;
        tracked.Appeared.ShouldBe(2);
    }


    [Fact]
    public void AViewModelHearsAboutTheTabWithoutTheViewRelaying()
    {
        var model = new TrackingModel();
        var page = new ShinyTabbedPage { Transition = StateTransition.None };
        page.Tabs.Add(new ShinyTabItem
        {
            ContentTemplate = new DataTemplate(() => new ContentPage { BindingContext = model, Content = new Label() })
        });
        page.Tabs.Add(new ShinyTabItem { Content = new Label() });

        // Once, not twice: the model is reachable through the adopted page and through the content
        // whose context it was mirrored onto, and it is still one object.
        model.Appeared.ShouldBe(1);

        page.SelectedIndex = 1;
        model.Disappeared.ShouldBe(1);
    }


    [Fact]
    public void ATabIsToldWhenTheWholePageLeavesTheScreenAndComesBack()
    {
        var page = new ShinyTabbedPage { Transition = StateTransition.None };
        page.Tabs.Add(new ShinyTabItem { ContentTemplate = new DataTemplate(() => new TrackingPage()) });

        var tracked = (TrackingPage)page.Tabs[0].AdoptedPage!;
        tracked.Appeared.ShouldBe(1);

        page.SendLifecycle(appearing: false);
        tracked.Disappeared.ShouldBe(1);

        page.SendLifecycle(appearing: true);
        tracked.Appeared.ShouldBe(2);
    }


    [Fact]
    public void LeavingTheScreenTwiceOnlyAnnouncesOnce()
    {
        var page = new ShinyTabbedPage { Transition = StateTransition.None };
        page.Tabs.Add(new ShinyTabItem { ContentTemplate = new DataTemplate(() => new TrackingPage()) });
        var tracked = (TrackingPage)page.Tabs[0].AdoptedPage!;

        page.SendLifecycle(appearing: false);
        page.SendLifecycle(appearing: false);

        tracked.Disappeared.ShouldBe(1);
    }


    [Fact]
    public void ATabWithNoPageReportsNoAdoptedPage()
    {
        var page = new ShinyTabbedPage { Transition = StateTransition.None };
        page.Tabs.Add(new ShinyTabItem { ContentTemplate = new DataTemplate(() => new Label()) });

        page.Tabs[0].AdoptedPage.ShouldBeNull();
        page.Tabs[0].PageContext.ShouldBeOfType<Label>();
    }


    // --- the centre menu's surface ---------------------------------------------------------------

    static ShinyTabbedPage WithMenu(out Label content)
    {
        var page = new ShinyTabbedPage { Transition = StateTransition.None, TabBar = { AnimationDuration = 0 } };
        page.Tabs.Add(new ShinyTabItem { Title = "One", Content = new Label() });

        var custom = new Label { Text = "menu" };
        ShinyTabs.SetMenuContent(page.Tabs[0].PageContext!, custom);
        page.CenterButton = new TabCenterButton { Icon = "plus" };

        content = custom;
        return page;
    }


    [Fact]
    public void TheMenuPaintsIntoThePagesOwnLayer()
    {
        var page = WithMenu(out _);

        page.TabBar.OpenMenu();

        // Not through PageOverlay: installing an overlay root would re-parent the page's content,
        // which rebuilds every native view under it - and renders nothing at all on macOS AppKit.
        page.Content.ShouldBeOfType<Grid>();
        page.Content.ShouldNotBeOfType<Infrastructure.PageOverlay.ShinyOverlayRoot>();

        MenuLayerOf(page).Children.Count.ShouldBe(2); // backdrop + card
    }


    [Fact]
    public void ClosingTheMenuGivesThePageItsContentBack()
    {
        var page = WithMenu(out var custom);

        page.TabBar.OpenMenu();
        page.TabBar.CloseMenu();

        MenuLayerOf(page).Children.ShouldBeEmpty();

        // Unparented, or the second open hands MAUI a view that already has a parent and throws.
        custom.Parent.ShouldBeNull();

        page.TabBar.OpenMenu();
        MenuLayerOf(page).Children.Count.ShouldBe(2);
    }


    static Layout MenuLayerOf(ShinyTabbedPage page)
        => ((ShinyTabBar.ITabMenuHost)page).GetTabMenuLayer();


    /// <summary>
    /// Implements <see cref="ITabAware"/> rather than overriding <c>OnAppearing</c>: MAUI raises the
    /// page lifecycle for the page the platform presented, and an adopted page is never that page.
    /// </summary>
    sealed class TrackingPage : ContentPage, ITabAware
    {
        public TrackingPage() => this.Content = new Label { Text = "tracked" };

        public int Appeared { get; private set; }

        public int Disappeared { get; private set; }

        public void OnTabAppearing() => this.Appeared++;

        public void OnTabDisappearing() => this.Disappeared++;
    }


    sealed class TrackingModel : ITabAware
    {
        public int Appeared { get; private set; }

        public int Disappeared { get; private set; }

        public void OnTabAppearing() => this.Appeared++;

        public void OnTabDisappearing() => this.Disappeared++;
    }


    [Fact]
    public void ThePagesRootIsEdgeToEdgeSoTheBarCanReachTheBottom()
    {
        var page = new ShinyTabbedPage();
        page.Tabs.Add(new ShinyTabItem { Title = "One", Content = new Label() });

        // A Grid defaults to SafeAreaRegions.Container and insets the area its children are arranged
        // into, so left at the default this one hands the bar a row that stops short of the screen -
        // and no amount of work inside the bar can get its background past that.
        var root = (Grid)page.Content!;
        root.SafeAreaEdges.Bottom.ShouldBe(SafeAreaRegions.None);
    }
}
