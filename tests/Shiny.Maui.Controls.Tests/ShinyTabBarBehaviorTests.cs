using System.Windows.Input;
using Microsoft.Maui.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The Shell half: that the bar's tabs are the Shell's own, that per-tab chrome is read off the
/// Shell elements (where it is available before the page exists), and that a tap goes back through
/// <c>CurrentItem</c> rather than around it.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ShinyTabBarBehaviorTests
{
    public ShinyTabBarBehaviorTests()
    {
        TestDispatcherProvider.Install();

        // A fresh Application per test, not `Application.Current ?? new` - Application.Current is
        // process-wide, so anything one test merges would leak into the rest of the collection.
        _ = new Application();
    }

    static Shell BuildShell(out ShinyTabBarBehavior behavior, int sectionCount = 3)
    {
        var shell = new Shell();
        var tabBar = new TabBar();

        for (var i = 0; i < sectionCount; i++)
        {
            var section = new Tab { Title = $"Tab{i}", Route = $"tab{i}" };
            section.Items.Add(new ShellContent
            {
                Title = $"Tab{i}",
                ContentTemplate = new DataTemplate(() => new ContentPage())
            });
            tabBar.Items.Add(section);
        }

        shell.Items.Add(tabBar);

        behavior = new ShinyTabBarBehavior { Transition = StateTransition.None };
        shell.Behaviors.Add(behavior);
        return shell;
    }


    [Fact]
    public void TheShellsSectionsBecomeTheTabs()
    {
        _ = BuildShell(out var behavior);

        behavior.Bar.Items.Count.ShouldBe(3);
        behavior.Bar.Items.Select(i => i.Title).ShouldBe(["Tab0", "Tab1", "Tab2"]);
    }


    [Fact]
    public void TheNativeBarIsHiddenOnce()
    {
        var shell = BuildShell(out _);

        // Set on the Shell rather than per page: the attached property is inherited, so this holds
        // for every page without racing each navigation.
        Shell.GetTabBarIsVisible(shell).ShouldBeFalse();
    }


    [Fact]
    public void DetachingPutsTheNativeBarBack()
    {
        var shell = BuildShell(out var behavior);

        shell.Behaviors.Remove(behavior);

        Shell.GetTabBarIsVisible(shell).ShouldBeTrue();
    }


    [Fact]
    public void ChromeIsReadOffTheShellElement()
    {
        var shell = new Shell();
        var tabBar = new TabBar();
        var section = new Tab { Title = "Inbox", Route = "inbox" };
        ShinyTabs.SetIcon(section, "mail");
        ShinyTabs.SetBadge(section, "12");
        section.Items.Add(new ShellContent { ContentTemplate = new DataTemplate(() => new ContentPage()) });
        tabBar.Items.Add(section);

        var other = new Tab { Title = "Me", Route = "me" };
        other.Items.Add(new ShellContent { ContentTemplate = new DataTemplate(() => new ContentPage()) });
        tabBar.Items.Add(other);
        shell.Items.Add(tabBar);

        var behavior = new ShinyTabBarBehavior();
        shell.Behaviors.Add(behavior);

        var tab = behavior.Bar.Items[0];
        tab.Title.ShouldBe("Inbox");
        tab.Icon.ShouldBe("mail");

        // A badge on the shell element rather than the page is what a tab the user has never opened
        // needs - there is no page to ask yet.
        tab.Badge.ShouldBe("12");
    }


    [Fact]
    public void ABadgeChangeOnTheShellElementReachesTheTab()
    {
        var shell = BuildShell(out var behavior);
        var section = shell.Items[0].Items[1];

        ShinyTabs.SetBadge(section, "5");

        behavior.Bar.Items[1].Badge.ShouldBe("5");
    }


    [Fact]
    public void TheCentreButtonFollowsTheShellPagesActions()
    {
        _ = BuildShell(out var behavior);
        var bar = behavior.Bar;
        bar.AnimationDuration = 0;
        bar.CenterButton = new TabCenterButton { Mode = TabCenterMode.Menu };

        // The behaviour hands the bar the Shell's current page as its PageContext; that handoff is
        // all the menu decision reads, so it is driven directly here rather than through a Shell
        // navigation a headless host never performs.
        var page = new ContentPage { Content = new Label() };
        bar.PageContext = page;

        bar.IsCenterButtonActive.ShouldBeFalse();
        bar.OpenMenu();
        bar.IsMenuOpen.ShouldBeFalse();

        ShinyTabs.GetActions(page).Add(new TabAction { Text = "Compose" });
        bar.IsCenterButtonActive.ShouldBeTrue();

        bar.PageContext = new ContentPage();
        bar.IsCenterButtonActive.ShouldBeFalse();
    }


    static ShellContent ContentOf(Shell shell, int section) => shell.Items[0].Items[section].Items[0];


    static Border CenterCircle(ShinyTabBar bar)
        => Descendants(bar.CenterHost).OfType<Border>().First(b => b.AutomationId == "tab-center");


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
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }


    [Fact]
    public void ActionsDeclaredOnTheShellContentReachTheCentreButton()
    {
        var shell = BuildShell(out var behavior);
        var bar = behavior.Bar;
        bar.AnimationDuration = 0;
        bar.CenterButton = new TabCenterButton { Mode = TabCenterMode.Menu };

        // Before the fix only the page's own Actions were read, so rows on the ShellContent - the
        // documented place for a tab whose page is built lazily - left the button dimmed and inert.
        ShinyTabs.GetActions(ContentOf(shell, 0)).Add(new TabAction { Text = "Compose" });

        bar.IsCenterButtonActive.ShouldBeTrue();
        bar.ResolveMenuActions().Single().Text.ShouldBe("Compose");
    }


    [Fact]
    public void ThePagesOwnActionsBeatTheShellContents()
    {
        var shell = BuildShell(out var behavior);
        var bar = behavior.Bar;
        bar.CenterButton = new TabCenterButton { Mode = TabCenterMode.Menu };
        ShinyTabs.GetActions(ContentOf(shell, 0)).Add(new TabAction { Text = "from shell" });

        var page = new ContentPage();
        ShinyTabs.GetActions(page).Add(new TabAction { Text = "from page" });
        bar.PageContext = page;

        // Replaces, never merges - the same rule a page's badge follows over its tab's.
        bar.ResolveMenuActions().Select(a => a.Text).ShouldBe(["from page"]);

        ShinyTabs.GetActions(page).Clear();
        bar.ResolveMenuActions().Select(a => a.Text).ShouldBe(["from shell"]);
    }


    [Fact]
    public void ShellContentActionsBindAgainstTheShellContentsContext()
    {
        var shell = BuildShell(out var behavior);
        var bar = behavior.Bar;
        bar.CenterButton = new TabCenterButton { Mode = TabCenterMode.Menu };

        var model = new ShellModel();
        shell.BindingContext = model;

        var action = new TabAction { Text = "Save" };
        action.SetBinding(TabAction.CommandProperty, new Binding(nameof(ShellModel.SaveCommand)));
        ShinyTabs.GetActions(ContentOf(shell, 0)).Add(action);

        // The action lives in an attached-property collection, off every element chain - without
        // the bar seeding the ShellContent's (Shell-inherited) context the binding resolves to null.
        bar.ResolveMenuActions().Single().Command.ShouldNotBeNull();
        bar.ResolveMenuActions().Single().Invoke();
        model.Saved.ShouldBe(1);
    }


    [Fact]
    public void TheButtonFollowsTheShellContentsCollectionAndTheCurrentTab()
    {
        var shell = BuildShell(out var behavior);
        var bar = behavior.Bar;
        bar.AnimationDuration = 0;
        bar.CenterButton = new TabCenterButton { Mode = TabCenterMode.Menu };

        CenterCircle(bar).Opacity.ShouldBe(0.5);

        var actions = ShinyTabs.GetActions(ContentOf(shell, 1));
        actions.Add(new TabAction { Text = "Second tab only" });

        // Not the current tab yet - its rows must not light the button on the first one.
        bar.IsCenterButtonActive.ShouldBeFalse();
        CenterCircle(bar).Opacity.ShouldBe(0.5);

        bar.SelectedIndex = 1;
        bar.IsCenterButtonActive.ShouldBeTrue();
        CenterCircle(bar).Opacity.ShouldBe(1);

        actions.Clear();
        CenterCircle(bar).Opacity.ShouldBe(0.5);

        actions.Add(new TabAction { Text = "Back again" });
        CenterCircle(bar).Opacity.ShouldBe(1);

        bar.SelectedIndex = 0;
        CenterCircle(bar).Opacity.ShouldBe(0.5);
    }


    [Fact]
    public void DetachingForgetsTheShellContents()
    {
        var shell = BuildShell(out var behavior);
        var bar = behavior.Bar;
        bar.CenterButton = new TabCenterButton { Mode = TabCenterMode.Menu };
        ShinyTabs.GetActions(ContentOf(shell, 0)).Add(new TabAction { Text = "Compose" });
        bar.IsCenterButtonActive.ShouldBeTrue();

        shell.Behaviors.Remove(behavior);

        bar.IsCenterButtonActive.ShouldBeFalse();
    }


    sealed class ShellModel
    {
        public ShellModel() => this.SaveCommand = new Command(() => this.Saved++);

        public ICommand SaveCommand { get; }

        public int Saved { get; private set; }
    }


    [Fact]
    public void SelectingATabSetsTheShellsCurrentItem()
    {
        var shell = BuildShell(out var behavior);

        behavior.Bar.SelectedIndex = 2;

        shell.CurrentItem!.CurrentItem.ShouldBe(shell.Items[0].Items[2]);
    }


    [Fact]
    public void TopLevelItemsAreUsedWhenThereIsOnlyOneSection()
    {
        var shell = new Shell();
        foreach (var name in new[] { "One", "Two" })
        {
            var item = new FlyoutItem { Title = name, Route = name.ToLowerInvariant() };
            item.Items.Add(new ShellContent { ContentTemplate = new DataTemplate(() => new ContentPage()) });
            shell.Items.Add(item);
        }

        var behavior = new ShinyTabBarBehavior();
        shell.Behaviors.Add(behavior);

        behavior.Bar.Items.Select(i => i.Title).ShouldBe(["One", "Two"]);

        behavior.Bar.SelectedIndex = 1;
        shell.CurrentItem.ShouldBe(shell.Items[1]);
    }
}
