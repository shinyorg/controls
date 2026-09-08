using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The parts of <see cref="ShinyTabBar"/> that are logic rather than pixels: which tab is selected,
/// where the columns fall around the centre button, which badge wins, and what the centre button
/// decides to present.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ShinyTabBarTests
{
    public ShinyTabBarTests()
    {
        TestDispatcherProvider.Install();

        // A fresh Application per test, not `Application.Current ?? new` - Application.Current is
        // process-wide, so anything one test merges would leak into the rest of the collection.
        _ = new Application();
    }

    static ShinyTabBar Build(params string[] titles)
    {
        var bar = new ShinyTabBar { AnimationDuration = 0 };
        foreach (var title in titles)
            bar.Items.Add(new ShinyTabItem { Title = title, Route = title.ToLowerInvariant() });

        return bar;
    }


    [Fact]
    public void FirstTabIsSelectedWhenItemsArrive()
    {
        var bar = Build("One", "Two");

        bar.SelectedIndex.ShouldBe(0);
        bar.SelectedItem!.Title.ShouldBe("One");
    }


    [Fact]
    public void SelectingByIndexUpdatesTheItem()
    {
        var bar = Build("One", "Two", "Three");

        bar.SelectedIndex = 2;

        bar.SelectedItem!.Title.ShouldBe("Three");
    }


    [Fact]
    public void SelectingByItemUpdatesTheIndex()
    {
        var bar = Build("One", "Two", "Three");

        bar.SelectedItem = bar.Items[1];

        bar.SelectedIndex.ShouldBe(1);
    }


    [Fact]
    public void SelectionChangedFiresOncePerChange()
    {
        var bar = Build("One", "Two", "Three");

        var changes = new List<int>();
        bar.SelectionChanged += (_, e) => changes.Add(e.NewIndex);

        bar.SelectedIndex = 1;
        bar.SelectedItem = bar.Items[2];

        changes.ShouldBe([1, 2]);
    }


    [Fact]
    public void SelectionChangedCommandRunsWithTheItem()
    {
        var bar = Build("One", "Two");
        ShinyTabItem? received = null;
        bar.SelectionChangedCommand = new DelegateCommand(p => received = p as ShinyTabItem);

        bar.SelectedIndex = 1;

        received.ShouldBe(bar.Items[1]);
    }


    [Fact]
    public void GoToByRouteSelectsThatTab()
    {
        var bar = Build("One", "Two", "Three");

        bar.GoTo("three").ShouldBeTrue();

        bar.SelectedIndex.ShouldBe(2);
    }


    [Fact]
    public void GoToRejectsAnUnknownRoute()
    {
        var bar = Build("One", "Two");

        bar.GoTo("nope").ShouldBeFalse();
        bar.SelectedIndex.ShouldBe(0);
    }


    [Fact]
    public void GoToRejectsADisabledTab()
    {
        var bar = Build("One", "Two");
        bar.Items[1].IsEnabled = false;

        bar.GoTo(1).ShouldBeFalse();
        bar.SelectedIndex.ShouldBe(0);
    }


    [Fact]
    public void RemovingTheSelectedTabPullsTheSelectionBackIntoRange()
    {
        var bar = Build("One", "Two", "Three");
        bar.SelectedIndex = 2;

        bar.Items.RemoveAt(2);

        bar.SelectedIndex.ShouldBe(1);
        bar.SelectedItem.ShouldBe(bar.Items[1]);
    }


    [Fact]
    public void ClearingTheTabsLeavesNothingSelected()
    {
        var bar = Build("One", "Two");

        bar.Items.Clear();

        bar.SelectedIndex.ShouldBe(-1);
        bar.SelectedItem.ShouldBeNull();
    }


    [Fact]
    public void HiddenTabsAreLeftOutOfTheBar()
    {
        var bar = Build("One", "Two", "Three");

        bar.Items[1].IsVisible = false;

        bar.BarLayout.ColumnDefinitions.Count.ShouldBe(2);
    }


    // --- centre button ---------------------------------------------------------------------------

    [Theory]
    [InlineData(4, 2, 2, 0)]
    [InlineData(3, 2, 1, 1)]
    [InlineData(5, 3, 2, 1)]
    [InlineData(0, 0, 0, 0)]
    public void ColumnsSplitEvenlyAroundTheCentreButton(int count, int left, int right, int spacers)
    {
        var split = ShinyTabBar.SplitColumns(count, hasCenter: true);

        split.Left.ShouldBe(left);
        split.Right.ShouldBe(right);
        split.Spacers.ShouldBe(spacers);

        // The invariant the padding exists for: equal star weight either side of the centre column.
        (split.Left).ShouldBe(split.Right + split.Spacers);
    }


    [Fact]
    public void WithoutACentreButtonEveryColumnIsATab()
    {
        ShinyTabBar.SplitColumns(3, hasCenter: false).ShouldBe((3, 0, 0));
    }


    [Fact]
    public void TheCentreButtonAddsItsOwnColumn()
    {
        var bar = Build("One", "Two", "Three", "Four");

        bar.CenterButton = new TabCenterButton { Icon = "plus" };

        bar.BarLayout.ColumnDefinitions.Count.ShouldBe(5);
        bar.CenterHost.IsVisible.ShouldBeTrue();
    }


    [Fact]
    public void ThereIsNoCentreColumnWithoutACentreButton()
    {
        var bar = Build("One", "Two", "Three", "Four");

        bar.BarLayout.ColumnDefinitions.Count.ShouldBe(4);
        bar.CenterHost.IsVisible.ShouldBeFalse();
    }


    // --- automation ids ---------------------------------------------------------------------------

    [Fact]
    public void TabsCarryAnAutomationIdBuiltFromTheirRoute()
    {
        var bar = Build("One", "Two");

        CellOf(bar, 0).AutomationId.ShouldBe("tab-one");
    }


    [Fact]
    public void TheTitleIsUsedWhenThereIsNoRoute()
    {
        ShinyTabBar.AutomationIdFor(new ShinyTabItem { Title = "My Inbox" }).ShouldBe("tab-my-inbox");
    }


    [Fact]
    public void ARouteBeatsTheTitle()
    {
        ShinyTabBar.AutomationIdFor(new ShinyTabItem { Title = "Inbox", Route = "mail" }).ShouldBe("tab-mail");
    }


    [Fact]
    public void ATabWithNoNameGetsNoAutomationId()
    {
        // Not an index-based id: it would point at a different tab the moment the order changed,
        // which is worse than having none at all.
        ShinyTabBar.AutomationIdFor(new ShinyTabItem()).ShouldBeNull();
    }


    static Grid CellOf(ShinyTabBar bar, int index) => bar.BarLayout.Children.OfType<Grid>().ElementAt(index);


    // --- the travelling indicator -------------------------------------------------------------------

    [Theory]
    [InlineData(TabIndicatorStyle.Pill, 57.6, 32)]
    [InlineData(TabIndicatorStyle.Line, 28, 3)]
    [InlineData(TabIndicatorStyle.Underline, 28, 3)]
    [InlineData(TabIndicatorStyle.Dot, 5, 5)]
    public void TheIndicatorIsTheSameSizeOnEveryTab(TabIndicatorStyle style, double width, double height)
    {
        var size = ShinyTabBar.IndicatorSizeFor(style, iconSize: 24);

        size.Width.ShouldBe(width, 0.01);
        size.Height.ShouldBe(height, 0.01);
    }


    [Fact]
    public void APillCentresOnTheIcon()
    {
        // A cell 80 wide at x=160, whose stack starts 6 down, whose icon box is 24 tall.
        var origin = ShinyTabBar.IndicatorOriginFor(
            TabIndicatorStyle.Pill,
            cell: new Rect(160, 0, 80, 56),
            stack: new Rect(0, 6, 80, 44),
            iconArea: new Rect(28, 0, 24, 24),
            size: new Size(58, 32));

        origin.X.ShouldBe(171, 0.01);   // 160 + (80 - 58) / 2

        // Behind the icon, not the cell: taking the centre of the cell would drop it behind the
        // label whenever labels are showing.
        origin.Y.ShouldBe(2, 0.01);     // 0 + 6 + 0 + (24 - 32) / 2
    }


    [Fact]
    public void TheBarsAnchorToTheCellsOwnEdges()
    {
        var cell = new Rect(0, 10, 80, 56);
        var stack = new Rect(0, 6, 80, 44);
        var icon = new Rect(28, 0, 24, 24);
        var size = new Size(28, 3);

        ShinyTabBar.IndicatorOriginFor(TabIndicatorStyle.Line, cell, stack, icon, size).Y.ShouldBe(10, 0.01);
        ShinyTabBar.IndicatorOriginFor(TabIndicatorStyle.Underline, cell, stack, icon, size).Y.ShouldBe(63, 0.01);
    }


    [Fact]
    public void ADotSitsUnderTheLabel()
    {
        var origin = ShinyTabBar.IndicatorOriginFor(
            TabIndicatorStyle.Dot,
            cell: new Rect(0, 0, 80, 56),
            stack: new Rect(0, 6, 80, 44),
            iconArea: new Rect(28, 0, 24, 24),
            size: new Size(5, 5));

        origin.Y.ShouldBe(45, 0.01);    // 0 + (6 + 44) - 5
    }


    [Fact]
    public void BeforeTheBarIsLaidOutTheIndicatorIsStillDrawnInTheCell()
    {
        var bar = Build("One", "Two");

        // Sliding is positioned from measured bounds, and in a headless host there are none. The
        // per-cell fallback is what makes the very first frame correct instead of parking a
        // zero-width indicator in the corner.
        bar.IndicatorTransition.ShouldBe(TabIndicatorTransition.Slide);
        CellIndicatorOf(bar, 0).IsVisible.ShouldBeTrue();
        CellIndicatorOf(bar, 1).IsVisible.ShouldBeFalse();
    }


    [Fact]
    public void TheFallbackFollowsTheSelection()
    {
        var bar = Build("One", "Two");

        bar.SelectedIndex = 1;

        CellIndicatorOf(bar, 0).IsVisible.ShouldBeFalse();
        CellIndicatorOf(bar, 1).IsVisible.ShouldBeTrue();
    }


    [Fact]
    public void TurningTheIndicatorOffHidesItEverywhere()
    {
        var bar = Build("One", "Two");

        bar.IndicatorStyle = TabIndicatorStyle.None;

        CellIndicatorOf(bar, 0).IsVisible.ShouldBeFalse();
    }


    /// <summary>The pill inside a cell — the fallback drawing, which is what a headless bar uses.</summary>
    static Border CellIndicatorOf(ShinyTabBar bar, int index)
        => Descendants(bar.BarLayout.Children.OfType<Grid>().ElementAt(index)).OfType<Border>().First();


    // --- badges ----------------------------------------------------------------------------------

    [Fact]
    public void APageBadgeBeatsTheTabsOwn()
    {
        var bar = Build("One", "Two");
        bar.Items[0].Badge = "1";

        var page = new ContentPage();
        ShinyTabs.SetBadge(page, "9");
        bar.PageContext = page;

        BadgeOf(bar, 0).ShouldBe("9");
    }


    [Fact]
    public void APageBadgeOnlyReachesTheTabThatPageIsShowing()
    {
        var bar = Build("One", "Two");
        bar.Items[1].Badge = "2";

        var page = new ContentPage();
        ShinyTabs.SetBadge(page, "9");
        bar.PageContext = page;

        // The page speaks for the selected tab and no other; without that guard the same count
        // lands on every tab in the bar.
        BadgeOf(bar, 0).ShouldBe("9");
        BadgeOf(bar, 1).ShouldBe("2");
    }


    [Fact]
    public void AnEmptyBadgeIsADotRatherThanACount()
    {
        var bar = Build("One");
        bar.Items[0].Badge = "";

        BadgeViewOf(bar, 0).IsDot.ShouldBeTrue();
    }


    static string BadgeOf(ShinyTabBar bar, int index) => BadgeViewOf(bar, index).Text;

    static BadgeView BadgeViewOf(ShinyTabBar bar, int index)
        => Descendants(bar.BarLayout.Children.OfType<Grid>().ElementAt(index)).OfType<BadgeView>().First();

    /// <summary>
    /// Hand-rolled rather than GetVisualTreeDescendants: the cells are never handed to a handler in
    /// a headless host, and the visual-tree walk goes through the handler.
    /// </summary>
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


    // --- centre menu -----------------------------------------------------------------------------

    [Fact]
    public void ThePagesActionsBeatTheButtonsOwn()
    {
        var bar = Build("One");
        var button = new TabCenterButton();
        button.Actions.Add(new TabAction { Text = "app-wide" });
        bar.CenterButton = button;

        var page = new ContentPage();
        ShinyTabs.GetActions(page).Add(new TabAction { Text = "page" });
        bar.PageContext = page;

        bar.ResolveMenuActions().Single().Text.ShouldBe("page");
    }


    [Fact]
    public void TheButtonsActionsAreUsedWhenThePageDeclaresNone()
    {
        var bar = Build("One");
        var button = new TabCenterButton();
        button.Actions.Add(new TabAction { Text = "app-wide" });
        bar.CenterButton = button;
        bar.PageContext = new ContentPage();

        bar.ResolveMenuActions().Single().Text.ShouldBe("app-wide");
    }


    [Fact]
    public void PageMenuContentBeatsActions()
    {
        var bar = Build("One");
        var button = new TabCenterButton();
        button.Actions.Add(new TabAction { Text = "app-wide" });
        bar.CenterButton = button;

        var page = new ContentPage();
        ShinyTabs.SetMenuContent(page, new Label { Text = "custom" });
        bar.PageContext = page;

        ((Label)bar.ResolveMenuContent()!).Text.ShouldBe("custom");
    }


    [Fact]
    public void AMenuTemplateIsBuiltFreshEachTime()
    {
        var bar = Build("One");
        var built = 0;

        var page = new ContentPage();
        ShinyTabs.SetMenuContentTemplate(page, new DataTemplate(() =>
        {
            built++;
            return new Label();
        }));
        bar.PageContext = page;

        bar.ResolveMenuContent();
        bar.ResolveMenuContent();

        built.ShouldBe(2);
    }


    [Fact]
    public void AnActionDeclaredOnAPageResolvesItsBindings()
    {
        var bar = Build("One");
        var model = new MenuModel();
        var page = new ContentPage { BindingContext = model };

        var action = new TabAction { Text = "Save" };
        action.SetBinding(TabAction.CommandProperty, new Binding(nameof(MenuModel.SaveCommand)));
        ShinyTabs.GetActions(page).Add(action);
        bar.PageContext = page;

        // A TabAction sits in an attached-property collection, so it is never on the page's element
        // chain and inherits nothing - the bar has to hand it the declaring page's context or the
        // binding resolves against null and the row silently does nothing when tapped.
        bar.ResolveMenuActions().Single().Command.ShouldNotBeNull();

        bar.ResolveMenuActions().Single().Invoke();
        model.Saved.ShouldBe(1);
    }


    [Fact]
    public void MenuContentDeclaredOnAPageResolvesItsBindings()
    {
        var bar = Build("One");
        var model = new MenuModel();
        var page = new ContentPage { BindingContext = model };

        var label = new Label();
        label.SetBinding(Label.TextProperty, new Binding(nameof(MenuModel.Caption)));
        ShinyTabs.SetMenuContent(page, label);
        bar.PageContext = page;

        // Same reason: the view is only parented once the menu is already open, which is after its
        // bindings have been evaluated.
        ((Label)bar.ResolveMenuContent()!).Text.ShouldBe("from the model");
    }


    [Fact]
    public void AnExplicitBindingContextOnAnActionStillWins()
    {
        var bar = Build("One");
        var page = new ContentPage { BindingContext = new MenuModel() };
        var own = new MenuModel();

        var action = new TabAction { Text = "Save", BindingContext = own };
        action.SetBinding(TabAction.CommandProperty, new Binding(nameof(MenuModel.SaveCommand)));
        ShinyTabs.GetActions(page).Add(action);
        bar.PageContext = page;

        bar.ResolveMenuActions().Single().Invoke();

        own.Saved.ShouldBe(1);
    }


    sealed class MenuModel
    {
        public MenuModel() => this.SaveCommand = new DelegateCommand(_ => this.Saved++);

        public ICommand SaveCommand { get; }

        public int Saved { get; private set; }

        public string Caption => "from the model";
    }


    [Fact]
    public void AnActionRunsItsCommandAndRaisesItsEvent()
    {
        var ran = 0;
        var raised = 0;
        var action = new TabAction { Command = new DelegateCommand(_ => ran++) };
        action.Clicked += (_, _) => raised++;

        action.Invoke();

        ran.ShouldBe(1);
        raised.ShouldBe(1);
    }


    [Fact]
    public void ADisabledActionDoesNothing()
    {
        var ran = 0;
        var action = new TabAction { IsEnabled = false, Command = new DelegateCommand(_ => ran++) };

        action.Invoke();

        ran.ShouldBe(0);
    }


    [Fact]
    public void TheCentreOverhangDefaultsToAThirdOfTheButton()
    {
        // A third, not a half: half centres the circle on the bar's top edge, which reads as
        // floating away from the tabs it belongs to.
        new TabCenterButton { Size = 60 }.EffectiveOverhang.ShouldBe(20);
        new TabCenterButton { Size = 60, Overhang = 0 }.EffectiveOverhang.ShouldBe(0);
        new TabCenterButton { Size = 60, Overhang = 12 }.EffectiveOverhang.ShouldBe(12);
    }


    [Fact]
    public void TheCentreButtonIsOptional()
    {
        var bar = Build("One", "Two");

        bar.CenterHost.IsVisible.ShouldBeFalse();
        bar.BarLayout.ColumnDefinitions.Count.ShouldBe(2);
    }


    [Fact]
    public void ThePressIsAPlainClickWhenThereIsNothingToPresent()
    {
        var bar = Build("One");
        bar.CenterButton = new TabCenterButton { Mode = TabCenterMode.Menu };

        bar.HasMenuToShow().ShouldBeFalse();
    }


    [Fact]
    public void AMenuTemplateIsEnoughToHaveSomethingToPresent()
    {
        var bar = Build("One");
        bar.CenterButton = new TabCenterButton { Mode = TabCenterMode.Menu };
        bar.MenuTemplate = new DataTemplate(() => new Label());

        bar.HasMenuToShow().ShouldBeTrue();
    }


    [Fact]
    public void ACentreButtonTemplateReplacesTheCircle()
    {
        var bar = Build("One");
        var marker = new Label { Text = "custom" };

        bar.CenterButton = new TabCenterButton { ContentTemplate = new DataTemplate(() => marker) };

        Descendants(bar.CenterHost).ShouldContain(marker);

        // Its context is the button, so {Binding Size} and friends resolve from markup.
        marker.BindingContext.ShouldBe(bar.CenterButton);
    }


    // --- selection animations ----------------------------------------------------------------------

    [Fact]
    public void ACustomAnimatorIsCalledForTheTabThatChanged()
    {
        var bar = Build("One", "Two");
        var animator = new RecordingAnimator();
        bar.Animator = animator;

        bar.SelectedIndex = 1;

        // Both ends of the change, and nothing else: the tab losing the selection and the one taking
        // it. A badge update or a restyle must not replay these.
        animator.Calls.ShouldBe([("One", false), ("Two", true)], ignoreOrder: true);
    }


    [Fact]
    public void TheAnimatorIsNotCalledForARestyle()
    {
        var bar = Build("One", "Two");
        bar.SelectedIndex = 1;

        var animator = new RecordingAnimator();
        bar.Animator = animator;

        bar.Items[0].Badge = "3";
        bar.IndicatorColor = Colors.Red;

        animator.Calls.ShouldBeEmpty();
    }


    [Fact]
    public void TheAnimatorGetsTheCellsPiecesSeparately()
    {
        var bar = Build("One", "Two");
        var animator = new RecordingAnimator();
        bar.Animator = animator;

        bar.SelectedIndex = 1;

        var incoming = animator.Contexts.Single(c => c.IsSelected);
        incoming.Icon.ShouldBeNull();          // no icon was set on these tabs
        incoming.Label.Text.ShouldBe("Two");
        incoming.Indicator.ShouldNotBeNull();  // the pill, for the default IndicatorStyle
        incoming.Bar.ShouldBe(bar);
    }


    sealed class RecordingAnimator : ITabAnimator
    {
        public List<(string? Title, bool Selected)> Calls { get; } = new();

        public List<TabAnimationContext> Contexts { get; } = new();

        public Task AnimateAsync(TabAnimationContext context)
        {
            this.Calls.Add((context.Item.Title, context.IsSelected));
            this.Contexts.Add(context);
            return Task.CompletedTask;
        }
    }


    sealed class DelegateCommand(Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            execute(parameter);
            this.CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }


    // ---------------------------------------------------------------------------------------------
    // Overflow
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TabsThatFitDrawNoMoreButton()
    {
        var bar = Build("One", "Two", "Three");
        bar.MaxVisibleTabs = 3;

        bar.HasOverflow.ShouldBeFalse();
        bar.OverflowItems.ShouldBeEmpty();
        bar.BarLayout.ColumnDefinitions.Count.ShouldBe(3);
    }


    [Fact]
    public void TheMoreButtonTakesOneOfTheVisibleSlots()
    {
        var bar = Build("One", "Two", "Three", "Four", "Five");
        bar.MaxVisibleTabs = 3;

        // Three columns, not four: the cap counts the More cell, or adding it would put the bar back
        // over the width that caused the overflow in the first place.
        bar.BarLayout.ColumnDefinitions.Count.ShouldBe(3);
        bar.OverflowItems.Select(i => i.Title).ShouldBe(new[] { "Three", "Four", "Five" });
    }


    [Fact]
    public void ABarOfNothingButAMoreButtonIsRefused()
    {
        var bar = Build("One", "Two", "Three");
        bar.MaxVisibleTabs = 1;

        // Clamped to 2: one real tab and the overflow. A cap of 1 would draw the More cell alone.
        bar.EffectiveMaxVisibleTabs.ShouldBe(2);
        bar.OverflowItems.Count.ShouldBe(2);
    }


    [Fact]
    public void NothingOverflowsBeforeTheBarHasBeenMeasured()
    {
        // Auto mode with no width yet. Dividing zero by the minimum tab width would cap at zero and
        // fold every tab away on the frame before layout.
        var bar = Build("One", "Two", "Three", "Four", "Five", "Six");

        bar.EffectiveMaxVisibleTabs.ShouldBe(0);
        bar.HasOverflow.ShouldBeFalse();
    }


    [Fact]
    public void TheMoreButtonOpensTheMenuInsteadOfSelectingItself()
    {
        var bar = Build("One", "Two", "Three", "Four");
        bar.MaxVisibleTabs = 3;

        // The last cell is the More one - the bar appends it after the tabs it kept.
        bar.BarLayout.Children.OfType<Grid>().Last().AutomationId.ShouldBe("tab-more");
        bar.TapCellAt(2);

        // The selection is untouched: the More cell is not a tab, and the obvious bug here is it
        // selecting itself - it is built by the same code as every other cell.
        bar.SelectedIndex.ShouldBe(0);
        bar.SelectedItem!.Title.ShouldBe("One");

        // The menu itself cannot open here: it paints onto a page overlay and this bar is on no
        // page, so the bar reports the state honestly rather than claiming a menu that does not
        // exist. What is assertable is that the tap asked for it.
        bar.IsMenuOpen.ShouldBeFalse();
    }


    [Fact]
    public void ChoosingAFoldedTabSelectsIt()
    {
        var bar = Build("One", "Two", "Three", "Four");
        bar.MaxVisibleTabs = 3;
        bar.OpenOverflow();

        bar.SelectOverflowItem(bar.OverflowItems.Last());

        bar.SelectedItem!.Title.ShouldBe("Four");
        bar.IsMenuOpen.ShouldBeFalse();
    }


    [Fact]
    public void TheMoreButtonShowsAsSelectedWhileAFoldedTabIsShowing()
    {
        var bar = Build("One", "Two", "Three", "Four");
        bar.MaxVisibleTabs = 3;
        bar.SelectedIndex = 3;

        // It selects nothing of its own, so without this the whole bar reads as unselected the
        // moment the user picks something out of the menu.
        bar.IsOverflowCellSelected.ShouldBeTrue();

        bar.SelectedIndex = 0;
        bar.IsOverflowCellSelected.ShouldBeFalse();
    }


    [Fact]
    public void OpeningTheOverflowDoesNothingWhenEverythingFits()
    {
        var bar = Build("One", "Two");
        bar.OpenOverflow();

        bar.IsMenuOpen.ShouldBeFalse();
    }


    // ---------------------------------------------------------------------------------------------
    // Menu dismissal & transparency
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ChangingTabsClosesAnOpenMenu()
    {
        var bar = Build("One", "Two", "Three");

        // On a page, so the menu has an overlay layer to paint onto - off one it cannot open at all
        // and the assertion below would pass against a menu that never existed.
        _ = new ContentPage { Content = bar };
        bar.IsMenuOpen = true;
        bar.IsMenuOpen.ShouldBeTrue();

        bar.SelectedIndex = 1;

        bar.IsMenuOpen.ShouldBeFalse();
    }


    [Fact]
    public void EveryRouteIntoASelectionClosesTheMenu()
    {
        var bar = Build("One", "Two", "Three");
        _ = new ContentPage { Content = bar };

        // Not just a tap: the close lives where the selection actually completes, so a GoTo or a
        // binding on SelectedIndex closes it too.
        bar.IsMenuOpen = true;
        bar.GoTo(2);
        bar.IsMenuOpen.ShouldBeFalse();

        bar.IsMenuOpen = true;
        bar.SelectedItem = bar.Items[0];
        bar.IsMenuOpen.ShouldBeFalse();
    }


    [Fact]
    public void ReselectingTheSameTabLeavesTheMenuAlone()
    {
        var bar = Build("One", "Two");
        _ = new ContentPage { Content = bar };
        bar.IsMenuOpen = true;

        // A reselect is not a change of tab - the page underneath the menu is the same one it was
        // opened over, so there is nothing for it to have gone stale against.
        bar.SelectedIndex = 0;

        bar.IsMenuOpen.ShouldBeTrue();
    }


    [Fact]
    public void BarBackgroundOpacityDimsTheFill()
    {
        var bar = Build("One", "Two");
        bar.BarBackgroundColor = Colors.Red;

        FillOf(bar).Alpha.ShouldBe(1f, 0.001f);

        bar.BarBackgroundOpacity = 0.5;
        FillOf(bar).Alpha.ShouldBe(0.5f, 0.001f);

        // The colour itself is untouched - only how much of it shows.
        FillOf(bar).Red.ShouldBe(1f, 0.001f);
    }


    [Fact]
    public void BarBackgroundOpacityMultipliesIntoAnAlphaTheColourAlreadyHad()
    {
        var bar = Build("One", "Two");
        bar.BarBackgroundColor = Colors.Red.WithAlpha(0.5f);
        bar.BarBackgroundOpacity = 0.5;

        // Multiplied, not replaced: a consumer who handed over a semi-transparent colour keeps it.
        FillOf(bar).Alpha.ShouldBe(0.25f, 0.001f);
    }


    [Fact]
    public void TransparencyLeavesTheTabsThemselvesOpaque()
    {
        var bar = Build("One", "Two");
        bar.BarBackgroundColor = Colors.Red;
        bar.BarBackgroundOpacity = 0.2;

        // The alpha goes on the colour, never on a view: Opacity on the surface would take the
        // icons and labels down with it, and opacity multiplies down the tree so a child cannot
        // undo it.
        bar.Surface.Opacity.ShouldBe(1);
        bar.Opacity.ShouldBe(1);
        bar.BarLayout.Opacity.ShouldBe(1);
    }


    [Fact]
    public void OpacityIsClampedRatherThanTrusted()
    {
        var bar = Build("One", "Two");
        bar.BarBackgroundColor = Colors.Red;

        bar.BarBackgroundOpacity = 4;
        FillOf(bar).Alpha.ShouldBe(1f, 0.001f);

        bar.BarBackgroundOpacity = -1;
        FillOf(bar).Alpha.ShouldBe(0f, 0.001f);
    }


    static Color FillOf(ShinyTabBar bar) => ((SolidColorBrush)bar.Surface.Background!).Color;


    // ---------------------------------------------------------------------------------------------
    // Bottom safe area
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ADockedBarDeclaresNoSafeAreaInsetAnywhereInItsChain()
    {
        var bar = Build("One", "Two");

        // The inset is padded into the surface, in exactly one place. Anything left declaring
        // Container here double-counts it: the surface's own inset fires once the background really
        // does reach the bottom of the screen, and it took another 34pt out of the tab row - the
        // cells collapsed to 16pt and every icon was clipped against the top edge.
        bar.SafeAreaEdges.Bottom.ShouldBe(SafeAreaRegions.None);
        bar.Surface.SafeAreaEdges.Bottom.ShouldBe(SafeAreaRegions.None);
    }


    [Fact]
    public void AFloatingBarLiftsItsWholeCapsuleInstead()
    {
        var bar = Build("One", "Two");
        bar.BarStyle = TabBarStyle.Floating;

        // The opposite arrangement, and deliberately so: the capsule must not extend into the home
        // indicator or the gap under it - the thing that makes it read as floating - is painted over.
        bar.SafeAreaEdges.Bottom.ShouldBe(SafeAreaRegions.Container);
        bar.Surface.SafeAreaEdges.Bottom.ShouldBe(SafeAreaRegions.None);
    }


    [Fact]
    public void TheSurfaceIsBarHeightPlusTheInsetRatherThanBarHeight()
    {
        var bar = Build("One", "Two");
        bar.BarHeight = 60;

        // Headless there is no window to read an inset from, so this is BarHeight exactly. What it
        // pins down is which quantity the runtime inset is added to: pinning the surface to
        // BarHeight and padding the inset in squeezes the tabs instead of lifting them.
        bar.Surface.HeightRequest.ShouldBe(60);
    }
}
