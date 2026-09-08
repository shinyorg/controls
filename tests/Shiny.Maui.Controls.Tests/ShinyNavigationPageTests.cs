using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// What <see cref="ShinyNavigationPage"/> promises: everything a stock <c>NavigationPage</c> is told
/// still lands, the drawn bar replaces the native one rather than doubling it, and the page's own
/// declarations reach the bar.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ShinyNavigationPageTests
{
    public ShinyNavigationPageTests()
    {
        TestDispatcherProvider.Install();

        // A fresh Application per test, not `Application.Current ?? new` - Application.Current is
        // process-wide, so anything one test merges would leak into the rest of the collection.
        _ = new Application();
    }

    static ContentPage Page(string title = "Root") => new()
    {
        Title = title,
        Content = new Label { Text = title }
    };

    static ShinyNavBar BarOf(Page page) => ShinyNavigationPage.GetNavBar(page).ShouldNotBeNull();


    [Fact]
    public void TheRootPageGetsABarAsSoonAsItIsPushed()
    {
        var root = Page();
        var nav = new ShinyNavigationPage(root);

        ShinyNavigationPage.GetNavBar(root).ShouldNotBeNull();
        nav.CurrentNavBar.ShouldBe(ShinyNavigationPage.GetNavBar(root));
    }


    [Fact]
    public void TheNativeBarIsTurnedOffSoItCannotDrawTheTitleTwice()
    {
        var root = Page();
        _ = new ShinyNavigationPage(root);

        NavigationPage.GetHasNavigationBar(root).ShouldBeFalse();
    }


    [Fact]
    public void ThePagesContentIsWrappedNotReplaced()
    {
        var label = new Label { Text = "body" };
        var root = new ContentPage { Title = "Root", Content = label };
        var nav = new ShinyNavigationPage(root);

        var host = nav.HostFor(root).ShouldNotBeNull();

        // The page's content is the overlay root the bar's menu needs, and the wrapper sits inside
        // it - installed up front so that opening a menu later does not re-parent the whole page.
        root.Content.ShouldBeAssignableTo<Layout>()!.Children.ShouldContain(host);

        host.Children.ShouldContain(label);
        host.Children.ShouldContain(BarOf(root));
        Grid.GetRow(label).ShouldBe(1);
        Grid.GetRow(BarOf(root)).ShouldBe(0);
    }


    [Fact]
    public void AShinyContentPageIsWrappedAtItsPageContentSoOverlaysStayOnTop()
    {
        // Wrapping Content instead would put the nav bar above every toast, dialog and floating panel
        // the page owns.
        var body = new Label { Text = "body" };
        var page = new ShinyContentPage { Title = "Root", PageContent = body };
        var nav = new ShinyNavigationPage(page);

        var host = nav.HostFor(page).ShouldNotBeNull();
        page.PageContent.ShouldBe(host);
        host.Children.ShouldContain(body);
    }


    [Fact]
    public void APageTitleReachesTheBar()
    {
        var root = Page("Inbox");
        _ = new ShinyNavigationPage(root);

        BarOf(root).Title.ShouldBe("Inbox");

        root.Title = "Archive";
        BarOf(root).Title.ShouldBe("Archive");
    }


    [Fact]
    public void ThePagesOwnToolbarItemsAreDrawnOnTheRight()
    {
        var root = Page();
        root.ToolbarItems.Add(new ToolbarItem { Text = "Refresh" });
        _ = new ShinyNavigationPage(root);

        BarOf(root).RightItems.Select(i => i.Text).ShouldBe(new[] { "Refresh" });
    }


    [Fact]
    public void ToolbarItemsComeBeforeDeclaredRightItems()
    {
        var root = Page();
        root.ToolbarItems.Add(new ToolbarItem { Text = "Existing" });
        ShinyNav.GetRightItems(root).Add(new NavBarItem { Text = "New" });
        _ = new ShinyNavigationPage(root);

        BarOf(root).RightItems.Select(i => i.Text).ShouldBe(new[] { "Existing", "New" });
    }


    [Fact]
    public void LeftItemsAreTheOnesAStockNavigationPageHasNoRoomFor()
    {
        var root = Page();
        ShinyNav.GetLeftItems(root).Add(new NavBarItem { Text = "Menu" });
        _ = new ShinyNavigationPage(root);

        BarOf(root).LeftItems.Select(i => i.Text).ShouldBe(new[] { "Menu" });
    }


    [Fact]
    public void AddingAToolbarItemLaterReachesTheBar()
    {
        var root = Page();
        _ = new ShinyNavigationPage(root);

        root.ToolbarItems.Add(new ToolbarItem { Text = "Later" });

        BarOf(root).RightItems.Select(i => i.Text).ShouldBe(new[] { "Later" });
    }


    [Fact]
    public void AddingADeclaredItemLaterReachesTheBar()
    {
        var root = Page();
        _ = new ShinyNavigationPage(root);

        ShinyNav.GetLeftItems(root).Add(new NavBarItem { Text = "Later" });

        BarOf(root).LeftItems.Select(i => i.Text).ShouldBe(new[] { "Later" });
    }


    [Fact]
    public void DeclaredItemsInheritThePagesBindingContext()
    {
        // An attached-property collection has no parent, so nothing would ever hand its items a
        // binding context - every binding on one would silently resolve against null.
        var context = new object();
        var root = Page();
        root.BindingContext = context;

        var item = new NavBarItem { Text = "Menu" };
        ShinyNav.GetLeftItems(root).Add(item);
        _ = new ShinyNavigationPage(root);

        item.BindingContext.ShouldBe(context);
    }


    [Fact]
    public void TheRootPageHasNoBackButton()
    {
        var root = Page();
        _ = new ShinyNavigationPage(root);

        BarOf(root).IsBackButtonVisible.ShouldBeFalse();
    }


    [Fact]
    public void IsNavBarVisibleIsThePerPageSwitchAtRuntime()
    {
        var root = Page();
        _ = new ShinyNavigationPage(root);

        BarOf(root).IsVisible.ShouldBeTrue();

        ShinyNav.SetIsNavBarVisible(root, false);
        BarOf(root).IsVisible.ShouldBeFalse();

        ShinyNav.SetIsNavBarVisible(root, true);
        BarOf(root).IsVisible.ShouldBeTrue();
    }


    [Fact]
    public void TurningTheNativeBarBackOnAsksForTheDrawnOne()
    {
        // The reverse cannot work and is not claimed to: HasNavigationBar already holds the false
        // this page wrote to get the native bar out of the way, so a page writing false again is a
        // no-op nothing can observe. That is what ShinyNav.IsNavBarVisible is for.
        var root = Page();
        NavigationPage.SetHasNavigationBar(root, false);
        _ = new ShinyNavigationPage(root);

        BarOf(root).IsVisible.ShouldBeFalse();

        NavigationPage.SetHasNavigationBar(root, true);

        BarOf(root).IsVisible.ShouldBeTrue();

        // ...and the native bar stays off regardless, or it would draw the title a second time.
        NavigationPage.GetHasNavigationBar(root).ShouldBeFalse();
    }


    [Fact]
    public void APageDeclaringNoBarBeforeItIsPushedStillHasNone()
    {
        var root = Page();
        NavigationPage.SetHasNavigationBar(root, false);
        var nav = new ShinyNavigationPage(root);

        nav.WantsBar(root).ShouldBeFalse();
        BarOf(root).IsVisible.ShouldBeFalse();
    }


    [Fact]
    public void IsNavBarVisibleHidesEveryBar()
    {
        var root = Page();
        var nav = new ShinyNavigationPage(root);

        nav.IsNavBarVisible = false;

        BarOf(root).IsVisible.ShouldBeFalse();
    }


    [Fact]
    public void MauisOwnAttachedPropertiesAreHonoured()
    {
        var root = Page();
        var titleView = new SearchBar();
        NavigationPage.SetTitleView(root, titleView);
        NavigationPage.SetBackButtonTitle(root, "Back to inbox");
        NavigationPage.SetIconColor(root, Colors.Red);
        _ = new ShinyNavigationPage(root);

        var bar = BarOf(root);
        bar.TitleView.ShouldBe(titleView);
        bar.BackButtonText.ShouldBe("Back to inbox");
        bar.IconColor.ShouldBe(Colors.Red);
    }


    [Fact]
    public void BarColoursFallFromTheNavigationPageToThePage()
    {
        var root = Page();
        var nav = new ShinyNavigationPage(root) { BarBackgroundColor = Colors.Blue, BarTextColor = Colors.White };

        BarOf(root).BarBackgroundColor.ShouldBe(Colors.Blue);
        BarOf(root).BarTextColor.ShouldBe(Colors.White);

        ShinyNav.SetBarBackgroundColor(root, Colors.Green);
        BarOf(root).BarBackgroundColor.ShouldBe(Colors.Green);
    }


    [Fact]
    public void APageOverridesTheNavigationPagesLargeTitleChoice()
    {
        var root = Page();
        var nav = new ShinyNavigationPage(root) { LargeTitleDisplay = LargeTitleDisplay.Collapsing };

        BarOf(root).LargeTitleDisplay.ShouldBe(LargeTitleDisplay.Collapsing);

        ShinyNav.SetLargeTitleDisplay(root, LargeTitleDisplay.None);
        BarOf(root).LargeTitleDisplay.ShouldBe(LargeTitleDisplay.None);
    }


    [Fact]
    public void ACollapsingTitleFindsThePagesScrollViewOnItsOwn()
    {
        var scroll = new ScrollView { Content = new Label() };
        var root = new ContentPage { Title = "Inbox", Content = scroll };
        _ = new ShinyNavigationPage(root) { LargeTitleDisplay = LargeTitleDisplay.Collapsing };

        BarOf(root).ScrollSource.ShouldBe(scroll);
    }


    [Fact]
    public void ANominatedScrollSourceWinsOverTheFirstOneFound()
    {
        var first = new ScrollView { Content = new Label() };
        var wanted = new CollectionView();
        var root = new ContentPage
        {
            Title = "Inbox",
            Content = new Grid { Children = { first, wanted } }
        };
        ShinyNav.SetScrollSource(root, wanted);
        _ = new ShinyNavigationPage(root) { LargeTitleDisplay = LargeTitleDisplay.Collapsing };

        BarOf(root).ScrollSource.ShouldBe(wanted);
    }


    [Fact]
    public void NothingIsAttachedWhenTheTitleDoesNotCollapse()
    {
        var scroll = new ScrollView { Content = new Label() };
        var root = new ContentPage { Title = "Inbox", Content = scroll };
        _ = new ShinyNavigationPage(root);

        BarOf(root).ScrollSource.ShouldBeNull();
    }


    [Fact]
    public void TheSubtitleReachesTheBar()
    {
        var root = Page("Inbox");
        ShinyNav.SetSubtitle(root, "12 unread");
        _ = new ShinyNavigationPage(root);

        BarOf(root).Subtitle.ShouldBe("12 unread");
    }


    [Fact]
    public void APageWithNoContentYetIsWrappedOnceItHasSome()
    {
        // Declaring the bar's attached properties in markup puts them ahead of the page's content, so
        // at install time there is often nothing to wrap.
        var root = new ContentPage { Title = "Root" };
        var nav = new ShinyNavigationPage(root);

        ShinyNavigationPage.GetNavBar(root).ShouldBeNull();

        var body = new Label { Text = "late" };
        root.Content = body;

        var host = nav.HostFor(root).ShouldNotBeNull();
        host.Children.ShouldContain(body);
        ShinyNavigationPage.GetNavBar(root).ShouldNotBeNull();
    }


    [Fact]
    public void APageThatIsNotAContentPageIsLeftAlone()
    {
        var tabbed = new TabbedPage { Title = "Tabs" };
        var nav = new ShinyNavigationPage(tabbed);

        ShinyNavigationPage.GetNavBar(tabbed).ShouldBeNull();

        // ...and its native bar is untouched, because its own chrome is not ours to replace.
        NavigationPage.GetHasNavigationBar(tabbed).ShouldBeTrue();
        nav.CurrentNavBar.ShouldBeNull();
    }


    [Fact]
    public void ItemInvokedBubblesFromAnyOfThePagesBars()
    {
        var root = Page();
        var item = new NavBarItem { Text = "Go" };
        ShinyNav.GetRightItems(root).Add(item);
        var nav = new ShinyNavigationPage(root);

        ToolbarItem? seen = null;
        nav.NavBarItemInvoked += (_, e) => seen = e.Item;

        var button = BarOf(root).TrailingHost.Children.OfType<Border>().Single();
        button.GestureRecognizers.OfType<TapGestureRecognizer>().Single().Command!.Execute(null);

        seen.ShouldBe(item);
    }


    [Fact]
    public void AppearanceSetOnTheNavigationPageReachesEveryBar()
    {
        var root = Page();
        var nav = new ShinyNavigationPage(root)
        {
            BarHeight = 72,
            MaxVisibleItems = 1,
            HasSeparator = true,
            HasShadow = false
        };

        var bar = BarOf(root);
        bar.BarHeight.ShouldBe(72);
        bar.MaxVisibleItems.ShouldBe(1);
        bar.HasSeparator.ShouldBeTrue();
        bar.HasShadow.ShouldBeFalse();
    }


    [Fact]
    public void TheBarsBackgroundRunsThroughTheStatusBarWhileItsContentDoesNot()
    {
        var root = Page();
        var nav = new ShinyNavigationPage(root);
        var bar = BarOf(root);

        // On the surface's content, not on the surface: the inset offsets whatever carries it, so
        // on the Border it would push the background down and leave the strip above it in the page's
        // colour. On the content the background stays at the top and grows by the inset instead.
        bar.SurfaceContent.SafeAreaEdges.Top.ShouldBe(SafeAreaRegions.Container);
        bar.Surface.SafeAreaEdges.Top.ShouldBe(SafeAreaRegions.None);

        // And the host must not inset first, or the surface never reaches the top edge to begin with.
        // A Grid defaults to Container, so this is a deliberate value rather than the default.
        nav.HostFor(root).ShouldNotBeNull().SafeAreaEdges.Top.ShouldBe(SafeAreaRegions.None);
    }


    [Fact]
    public void TurningTheSafeAreaOffPutsTheBarUnderTheStatusBar()
    {
        var root = Page();
        var nav = new ShinyNavigationPage(root) { RespectSafeArea = false };

        BarOf(root).SurfaceContent.SafeAreaEdges.Top.ShouldBe(SafeAreaRegions.None);
    }


    [Fact]
    public void ADarkBarAsksForALightStatusBarAndALightBarForADarkOne()
    {
        var root = Page();
        var nav = new ShinyNavigationPage(root) { BarBackgroundColor = Colors.Black };

        nav.ResolvedStatusBarStyle(root).ShouldBe(StatusBarStyle.LightContent);

        nav.BarBackgroundColor = Colors.White;
        nav.ResolvedStatusBarStyle(root).ShouldBe(StatusBarStyle.DarkContent);
    }


    [Fact]
    public void LuminanceIsWeightedNotHslLightness()
    {
        // The case HSL gets wrong: pure blue and pure yellow are both "50% light", and only one of
        // them can carry a black clock.
        ShinyNavigationPage.IsDark(Colors.Blue).ShouldBeTrue();
        ShinyNavigationPage.IsDark(Colors.Yellow).ShouldBeFalse();
    }


    [Fact]
    public void APageOverridesTheNavigationPagesStatusBarStyle()
    {
        var root = Page();
        var nav = new ShinyNavigationPage(root) { BarBackgroundColor = Colors.White };

        ShinyNav.SetStatusBarStyle(root, StatusBarStyle.LightContent);
        nav.ResolvedStatusBarStyle(root).ShouldBe(StatusBarStyle.LightContent);

        // Inherit is the "not answered" value, so a page can go back to following the host.
        ShinyNav.SetStatusBarStyle(root, StatusBarStyle.Inherit);
        nav.ResolvedStatusBarStyle(root).ShouldBe(StatusBarStyle.DarkContent);
    }


    [Fact]
    public void APerPageBarColourDrivesTheStatusBarToo()
    {
        var root = Page();
        var nav = new ShinyNavigationPage(root) { BarBackgroundColor = Colors.White };

        ShinyNav.SetBarBackgroundColor(root, Colors.DarkSlateBlue);

        nav.ResolvedStatusBarStyle(root).ShouldBe(StatusBarStyle.LightContent);
    }


    [Fact]
    public void AGradientBarIsReadAtTheStopTheStatusBarSitsOver()
    {
        var root = Page();
        var nav = new ShinyNavigationPage(root)
        {
            BarBackground = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Colors.White, 1),
                    new GradientStop(Colors.Black, 0)
                },
                new Point(0, 0),
                new Point(0, 1))
        };

        // Offset 0 is the top of the bar - the end the status bar is over - regardless of the order
        // the stops were declared in.
        BarOf(root).EffectiveBarColor.ShouldBe(Colors.Black);
        nav.ResolvedStatusBarStyle(root).ShouldBe(StatusBarStyle.LightContent);
    }


    [Fact]
    public void StatusBarStyleNoneLeavesThePlatformAlone()
    {
        var root = Page();
        var nav = new ShinyNavigationPage(root)
        {
            BarBackgroundColor = Colors.Black,
            StatusBarStyle = StatusBarStyle.None
        };

        nav.ResolvedStatusBarStyle(root).ShouldBe(StatusBarStyle.None);
    }


    [Fact]
    public void ConstructionLeavesNoGuardedCallbackQueued()
    {
        // Anything still queued is a styled or XAML-set value that silently never applied.
        var root = Page();
        var nav = new ShinyNavigationPage(root);

        Shiny.Maui.Controls.Infrastructure.StyleGuard.HasPending(nav).ShouldBeFalse();
        Shiny.Maui.Controls.Infrastructure.StyleGuard.HasPending(BarOf(root)).ShouldBeFalse();
    }
}
