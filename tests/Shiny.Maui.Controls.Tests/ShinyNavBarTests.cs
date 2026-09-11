using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The parts of <see cref="ShinyNavBar"/> that are logic rather than pixels: which side an item lands
/// on, what overflows, what the title does as the page scrolls, and what a tap actually runs.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ShinyNavBarTests
{
    public ShinyNavBarTests()
    {
        TestDispatcherProvider.Install();

        // A fresh Application per test, not `Application.Current ?? new` - Application.Current is
        // process-wide, so anything one test merges would leak into the rest of the collection.
        _ = new Application();
    }

    static ShinyNavBar Build() => new() { AnimationDuration = 0 };

    static IEnumerable<Element> Descendants(Element root)
    {
        var children = root switch
        {
            Layout layout => layout.Children.OfType<Element>(),
            ContentView content when content.Content is not null => new Element[] { content.Content },
            Border border when border.Content is not null => new Element[] { border.Content },
            _ => Enumerable.Empty<Element>()
        };

        foreach (var child in children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    static int Buttons(Layout host) => host.Children.OfType<Border>().Count();


    [Fact]
    public void LeftAndRightItemsLandOnTheirOwnSides()
    {
        var bar = Build();
        bar.LeftItems.Add(new NavBarItem { Text = "Cancel" });
        bar.RightItems.Add(new NavBarItem { Text = "Save" });

        Descendants(bar.LeadingHost).OfType<Label>().Select(l => l.Text).ShouldContain("Cancel");
        Descendants(bar.TrailingHost).OfType<Label>().Select(l => l.Text).ShouldContain("Save");

        Descendants(bar.LeadingHost).OfType<Label>().Select(l => l.Text).ShouldNotContain("Save");
    }


    [Fact]
    public void APlainToolbarItemRendersJustLikeANavBarItem()
    {
        // The whole point of deriving from ToolbarItem: a page's existing toolbar does not have to be
        // rewritten to adopt the bar.
        var bar = Build();
        bar.RightItems.Add(new ToolbarItem { Text = "Refresh" });

        Descendants(bar.TrailingHost).OfType<Label>().Select(l => l.Text).ShouldContain("Refresh");
    }


    [Fact]
    public void TappingAnItemRunsItsCommandAndRaisesClicked()
    {
        var bar = Build();
        var clicked = 0;
        var executed = 0;

        var item = new NavBarItem { Text = "Go", Command = new Command(() => executed++) };
        item.Clicked += (_, _) => clicked++;
        bar.RightItems.Add(item);
        var invoked = 0;
        bar.ItemInvoked += (_, _) => invoked++;

        Tap(bar.TrailingHost.Children.OfType<Border>().Single());

        executed.ShouldBe(1);
        clicked.ShouldBe(1);
        invoked.ShouldBe(1);
    }


    [Fact]
    public void ADisabledItemIsNotTappable()
    {
        var bar = Build();
        var executed = 0;
        bar.RightItems.Add(new NavBarItem { Text = "Go", IsEnabled = false, Command = new Command(() => executed++) });

        var button = bar.TrailingHost.Children.OfType<Border>().Single();

        button.GestureRecognizers.ShouldBeEmpty();
        button.Opacity.ShouldBeLessThan(1);
        executed.ShouldBe(0);
    }


    [Fact]
    public void SecondaryItemsOverflowNoMatterHowMuchRoomThereIs()
    {
        var bar = Build();
        bar.MaxVisibleItems = 10;
        bar.RightItems.Add(new NavBarItem { Text = "One" });
        bar.RightItems.Add(new NavBarItem { Text = "Two", Order = ToolbarItemOrder.Secondary });

        bar.OverflowFor(NavBarSide.Right).Select(i => i.Text).ShouldBe(new[] { "Two" });

        // One item plus the overflow button.
        Buttons(bar.TrailingHost).ShouldBe(2);
    }


    [Fact]
    public void ItemsPastTheLimitOverflowInDeclaredOrder()
    {
        var bar = Build();
        bar.MaxVisibleItems = 2;
        foreach (var text in new[] { "One", "Two", "Three", "Four" })
            bar.RightItems.Add(new NavBarItem { Text = text });

        bar.OverflowFor(NavBarSide.Right).Select(i => i.Text).ShouldBe(new[] { "Three", "Four" });
    }


    [Fact]
    public void PriorityOrdersItemsWithinTheirSide()
    {
        var bar = Build();
        bar.RightItems.Add(new NavBarItem { Text = "Last", Priority = 5 });
        bar.RightItems.Add(new NavBarItem { Text = "First", Priority = 1 });

        Descendants(bar.TrailingHost).OfType<Label>().Select(l => l.Text).ShouldBe(new[] { "First", "Last" });
    }


    [Fact]
    public void NoOverflowButtonWhenNothingOverflows()
    {
        var bar = Build();
        bar.RightItems.Add(new NavBarItem { Text = "One" });

        Buttons(bar.TrailingHost).ShouldBe(1);
        bar.OverflowFor(NavBarSide.Right).ShouldBeEmpty();
    }


    [Fact]
    public void AnInvisibleItemIsNotDrawnAndDoesNotOverflow()
    {
        var bar = Build();
        bar.MaxVisibleItems = 1;
        bar.RightItems.Add(new NavBarItem { Text = "Shown" });
        bar.RightItems.Add(new NavBarItem { Text = "Hidden", IsVisible = false });

        Buttons(bar.TrailingHost).ShouldBe(1);
        bar.OverflowFor(NavBarSide.Right).ShouldBeEmpty();
    }


    [Fact]
    public void ChangingAnItemsPropertyRebuildsTheBar()
    {
        // The collection never changes here - only the item does. Without the per-item subscription
        // a badge bound to a count would simply never appear.
        var bar = Build();
        var item = new NavBarItem { Text = "One" };
        bar.RightItems.Add(item);

        item.Text = "Two";

        Descendants(bar.TrailingHost).OfType<Label>().Select(l => l.Text).ShouldContain("Two");
    }


    [Fact]
    public void ABadgedItemGetsABadgeView()
    {
        var bar = Build();
        bar.RightItems.Add(new NavBarItem { Icon = "bell", Badge = "3" });

        var badge = Descendants(bar.TrailingHost).OfType<BadgeView>().Single();
        badge.Text.ShouldBe("3");
        badge.IsDot.ShouldBeFalse();
    }


    [Fact]
    public void AnEmptyBadgeIsADot()
    {
        var bar = Build();
        bar.RightItems.Add(new NavBarItem { Icon = "bell", Badge = "" });

        Descendants(bar.TrailingHost).OfType<BadgeView>().Single().IsDot.ShouldBeTrue();
    }


    [Fact]
    public void TheShadowCanBeTurnedOffAndBackOn()
    {
        // The shadow is a dynamic resource, so it is only a Shadow once a theme is merged.
        Application.Current!.Resources.MergedDictionaries.Add(new Themes.BasicLightTheme());

        var bar = Build();
        bar.Surface.Shadow.ShouldNotBeNull();

        // ClearValue does not clear a value that arrived from a dynamic resource, so this used to
        // leave the bar casting the shadow it had just been told to drop.
        bar.HasShadow = false;
        bar.Surface.Shadow.ShouldBeNull();

        bar.HasShadow = true;
        bar.Surface.Shadow.ShouldNotBeNull();
    }


    [Fact]
    public void APlainToolbarItemCanBeBadgedWithTheAttachedProperty()
    {
        var bar = Build();
        var item = new ToolbarItem { Text = "Alerts" };
        ShinyNav.SetBadge(item, "7");
        bar.RightItems.Add(item);

        var badge = Descendants(bar.TrailingHost).OfType<BadgeView>().Single();
        badge.Text.ShouldBe("7");
        badge.IsDot.ShouldBeFalse();
    }


    [Fact]
    public void TheAttachedBadgeIsHonouredOnANavBarItemThatHasNoneOfItsOwn()
    {
        var bar = Build();
        var item = new NavBarItem { Icon = "bell" };
        ShinyNav.SetBadge(item, "2");
        bar.RightItems.Add(item);

        Descendants(bar.TrailingHost).OfType<BadgeView>().Single().Text.ShouldBe("2");
    }


    [Fact]
    public void ANavBarItemsOwnBadgeWinsOverTheAttachedOne()
    {
        var bar = Build();
        var item = new NavBarItem { Icon = "bell", Badge = "3" };
        ShinyNav.SetBadge(item, "99");
        bar.RightItems.Add(item);

        Descendants(bar.TrailingHost).OfType<BadgeView>().Single().Text.ShouldBe("3");
    }


    [Fact]
    public void ChangingTheAttachedBadgeRedrawsTheItem()
    {
        var bar = Build();
        var item = new ToolbarItem { Text = "Alerts" };
        bar.RightItems.Add(item);

        Descendants(bar.TrailingHost).OfType<BadgeView>().ShouldBeEmpty();

        ShinyNav.SetBadge(item, "1");

        Descendants(bar.TrailingHost).OfType<BadgeView>().Single().Text.ShouldBe("1");
    }


    [Fact]
    public void TheAttachedBadgeColourReachesTheBadge()
    {
        var bar = Build();
        var item = new ToolbarItem { Text = "Alerts" };
        ShinyNav.SetBadge(item, "1");
        ShinyNav.SetBadgeColor(item, Colors.Purple);
        bar.RightItems.Add(item);

        Descendants(bar.TrailingHost).OfType<BadgeView>().Single().BadgeColor.ShouldBe(Colors.Purple);
    }


    [Fact]
    public void TheOverflowButtonShowsADotWhileAHiddenItemIsBadged()
    {
        var bar = Build();
        bar.MaxVisibleItems = 1;
        bar.RightItems.Add(new NavBarItem { Text = "One" });
        bar.RightItems.Add(new NavBarItem { Text = "Two", Badge = "4" });

        var badge = Descendants(bar.TrailingHost).OfType<BadgeView>().Single();
        badge.IsDot.ShouldBeTrue();
    }


    [Fact]
    public void TheOverflowButtonHasNoDotWhenNothingBehindItIsBadged()
    {
        var bar = Build();
        bar.MaxVisibleItems = 1;
        bar.RightItems.Add(new NavBarItem { Text = "One" });
        bar.RightItems.Add(new NavBarItem { Text = "Two" });

        Descendants(bar.TrailingHost).OfType<BadgeView>().ShouldBeEmpty();
    }


    [Fact]
    public void TheBackButtonIsFirstInTheLeadingGroup()
    {
        var bar = Build();
        bar.LeftItems.Add(new NavBarItem { Text = "Menu" });
        bar.IsBackButtonVisible = true;

        bar.BackButton.ShouldNotBeNull();
        bar.LeadingHost.Children[0].ShouldBe(bar.BackButton);
    }


    [Fact]
    public void TheBackButtonRunsItsCommandInsteadOfTheHostAction()
    {
        var bar = Build();
        var popped = 0;
        var commanded = 0;

        bar.BackAction = () => popped++;
        bar.BackButtonCommand = new Command(() => commanded++);
        bar.IsBackButtonVisible = true;

        Tap((Border)bar.BackButton!);

        commanded.ShouldBe(1);
        popped.ShouldBe(0);
    }


    [Fact]
    public void CancellingBackButtonPressedStopsEverything()
    {
        var bar = Build();
        var popped = 0;

        bar.BackAction = () => popped++;
        bar.BackButtonPressed += (_, e) => e.Cancel = true;
        bar.IsBackButtonVisible = true;

        Tap((Border)bar.BackButton!);

        popped.ShouldBe(0);
    }


    [Fact]
    public void BackFallsThroughToTheHostWhenNothingInterceptsIt()
    {
        var bar = Build();
        var popped = 0;
        bar.BackAction = () => popped++;
        bar.IsBackButtonVisible = true;

        Tap((Border)bar.BackButton!);

        popped.ShouldBe(1);
    }


    [Fact]
    public void ScrollingCollapsesTheLargeTitleAndFadesTheInlineOneIn()
    {
        var bar = Build();
        bar.Title = "Inbox";
        bar.LargeTitleDisplay = LargeTitleDisplay.Collapsing;
        bar.LargeTitleCollapseDistance = 100;
        bar.LargeTitleHeight = 50;

        bar.SimulateScroll(0);
        bar.CollapseProgress.ShouldBe(0);
        bar.LargeTitleHost.HeightRequest.ShouldBe(50);
        bar.LargeTitleHost.IsVisible.ShouldBeTrue();
        bar.TitleRow.Opacity.ShouldBe(0);

        bar.SimulateScroll(100);
        bar.CollapseProgress.ShouldBe(1);
        bar.LargeTitleHost.HeightRequest.ShouldBe(0);
        bar.LargeTitleLabel.Opacity.ShouldBe(0);
        bar.TitleRow.Opacity.ShouldBe(1);

        // A zero HeightRequest is not enough: the host keeps its padding and an Auto row still
        // measures the label inside it, which leaves a dead band under the bar.
        bar.LargeTitleHost.IsVisible.ShouldBeFalse();
        bar.LargeTitleHost.Padding.Bottom.ShouldBe(0);
    }


    [Fact]
    public void ScrollingPastTheDistanceDoesNotOvershoot()
    {
        var bar = Build();
        bar.Title = "Inbox";
        bar.LargeTitleDisplay = LargeTitleDisplay.Collapsing;
        bar.LargeTitleCollapseDistance = 50;

        bar.SimulateScroll(5000);

        bar.CollapseProgress.ShouldBe(1);
        bar.LargeTitleHost.HeightRequest.ShouldBe(0);
        bar.LargeTitleHost.IsVisible.ShouldBeFalse();
    }


    [Fact]
    public void AnAlwaysLargeTitleNeverCollapses()
    {
        var bar = Build();
        bar.Title = "Inbox";
        bar.LargeTitleDisplay = LargeTitleDisplay.Always;
        bar.LargeTitleHeight = 50;

        bar.SimulateScroll(500);

        bar.LargeTitleHost.HeightRequest.ShouldBe(50);
        bar.LargeTitleLabel.Opacity.ShouldBe(1);
        bar.TitleRow.Opacity.ShouldBe(0);
    }


    [Fact]
    public void NoLargeTitleLeavesTheInlineOneAlone()
    {
        var bar = Build();
        bar.Title = "Inbox";

        bar.LargeTitleHost.IsVisible.ShouldBeFalse();
        bar.TitleRow.Opacity.ShouldBe(1);
    }


    [Fact]
    public void TheLargeTitleFallsBackToTheTitle()
    {
        var bar = Build();
        bar.LargeTitleDisplay = LargeTitleDisplay.Always;
        bar.Title = "Inbox";

        bar.LargeTitleLabel.Text.ShouldBe("Inbox");

        bar.LargeTitle = "All mail";
        bar.LargeTitleLabel.Text.ShouldBe("All mail");
    }


    [Fact]
    public void ACentredTitleSpansTheBarSoItStaysPutAsItemsChange()
    {
        var bar = Build();
        bar.TitleAlignment = NavBarTitleAlignment.Center;

        Grid.GetColumn(bar.TitleHost).ShouldBe(0);
        Grid.GetColumnSpan(bar.TitleHost).ShouldBe(3);

        bar.TitleAlignment = NavBarTitleAlignment.Start;

        Grid.GetColumn(bar.TitleHost).ShouldBe(1);
        Grid.GetColumnSpan(bar.TitleHost).ShouldBe(1);
    }


    [Fact]
    public void ACentredTitleDoesNotSwallowTapsMeantForTheItems()
    {
        var bar = Build();
        bar.TitleAlignment = NavBarTitleAlignment.Center;

        bar.TitleHost.InputTransparent.ShouldBeTrue();
    }


    [Fact]
    public void ATitleViewTakesTapsBack()
    {
        var bar = Build();
        bar.TitleAlignment = NavBarTitleAlignment.Center;
        bar.TitleView = new SearchBar();

        bar.TitleHost.InputTransparent.ShouldBeFalse();
        bar.TitleHost.Children.ShouldContain(bar.TitleView!);
        bar.TitleHost.Children.ShouldNotContain(bar.TitleRow);
    }


    [Fact]
    public void ClearingTheTitleViewPutsTheTitleBack()
    {
        var bar = Build();
        bar.TitleView = new SearchBar();
        bar.TitleView = null;

        bar.TitleHost.Children.ShouldContain(bar.TitleRow);
        bar.TitleHost.InputTransparent.ShouldBeTrue();
    }


    [Fact]
    public void TheSubtitleIsHiddenWhenEmpty()
    {
        var bar = Build();
        bar.Title = "Inbox";

        bar.SubtitleLabel.IsVisible.ShouldBeFalse();

        bar.Subtitle = "12 unread";
        bar.SubtitleLabel.IsVisible.ShouldBeTrue();
        bar.SubtitleLabel.Text.ShouldBe("12 unread");
    }


    [Fact]
    public void ASeparatorItemIsSkippedOnTheBarButKeptForTheMenu()
    {
        var bar = Build();
        bar.MaxVisibleItems = 10;
        bar.RightItems.Add(new NavBarItem { Text = "One" });
        bar.RightItems.Add(new NavBarItem { IsSeparator = true, Order = ToolbarItemOrder.Secondary });
        bar.RightItems.Add(new NavBarItem { Text = "Two", Order = ToolbarItemOrder.Secondary });

        // "One", plus the overflow button - the separator draws nothing here.
        Buttons(bar.TrailingHost).ShouldBe(2);
        bar.OverflowFor(NavBarSide.Right).Count.ShouldBe(2);
    }


    [Fact]
    public void TheScrollSourceIsOnlyAttachedForACollapsingTitle()
    {
        var bar = Build();
        var scroll = new ScrollView();

        bar.AttachScrollSource(scroll);
        bar.ScrollSource.ShouldBe(scroll);

        bar.AttachScrollSource(null);
        bar.ScrollSource.ShouldBeNull();
    }


    [Fact]
    public void TheFirstScrollableInThePageIsFound()
    {
        var scroll = new ScrollView { Content = new Label() };
        var root = new Grid { Children = { new Label(), new VerticalStackLayout { Children = { scroll } } } };

        ShinyNavBar.FindScrollSource(root).ShouldBe(scroll);
    }


    [Fact]
    public void ACollectionViewCountsAsAScrollable()
    {
        var list = new CollectionView();
        var root = new Grid { Children = { list } };

        ShinyNavBar.FindScrollSource(root).ShouldBe(list);
    }


    static void Tap(Border button)
    {
        // The recognizer's Command is what the bar puts its action on, precisely so a tap can be
        // driven without a platform gesture.
        var tap = button.GestureRecognizers.OfType<TapGestureRecognizer>().Single();
        tap.Command!.Execute(null);
    }
}
