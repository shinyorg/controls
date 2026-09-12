using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.Ribbons;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// Everything about the ribbon that is not pixels: which tab it lands on, what happens when the tab it
/// was on disappears, and that pressing a button runs exactly one thing.
/// </summary>
/// <remarks>
/// The bar is driven through <see cref="Ribbon.Invoke"/> rather than through a gesture, which is the
/// point of that method existing: a <c>TapGestureRecognizer</c> cannot be raised from a test, and a
/// drawn button and a keyboard shortcut should go down one path anyway.
/// </remarks>
// No Application is created here (see Build), but Application.Current is process-wide and these
// tests now live alongside the ones that install implicit styles into it. Joining their collection
// serializes against those rather than racing them - without it, an unrelated control's animation
// probe failed intermittently depending on interleaving.
[Collection(ApplicationResourcesCollection.Name)]
public class RibbonTests
{
    static RibbonButton Button(string text, Action? onClick = null)
    {
        var button = new RibbonButton { Text = text };
        if (onClick is not null)
            button.Clicked += (_, _) => onClick();

        return button;
    }


    /// <summary>
    /// Builds a ribbon with no <see cref="Application"/> behind it, deliberately.
    /// </summary>
    /// <remarks>
    /// The bar resolves its colours through <c>SetDynamicResource</c>, which simply finds nothing
    /// without one — fine here, since none of this is about colour. Creating one would not be free:
    /// <c>Application.Current</c> is process-wide, and an application with no dispatcher makes
    /// <c>FileDropService.Dispatch</c> throw, so a stray <c>new Application()</c> here fails the file
    /// drop tests that run after it in this same assembly.
    /// </remarks>
    static Ribbon Build(params RibbonTab[] tabs)
    {
        var ribbon = new Ribbon();
        foreach (var tab in tabs)
            ribbon.Tabs.Add(tab);

        return ribbon;
    }


    static RibbonTab Tab(string title, params RibbonItem[] items)
    {
        var group = new RibbonGroup { Title = title + " group" };
        foreach (var item in items)
            group.Items.Add(item);

        var tab = new RibbonTab { Title = title, Key = title.ToLowerInvariant() };
        tab.Groups.Add(group);
        return tab;
    }


    [Fact]
    public void LandsOnTheFirstTab()
    {
        var ribbon = Build(Tab("Home"), Tab("Insert"));

        ribbon.SelectedIndex.ShouldBe(0);
        ribbon.SelectedTab!.Title.ShouldBe("Home");
    }


    [Fact]
    public void SelectsByKey()
    {
        var ribbon = Build(Tab("Home"), Tab("Insert"));

        ribbon.SelectTab("insert").ShouldBeTrue();
        ribbon.SelectedTab!.Title.ShouldBe("Insert");

        ribbon.SelectTab("nope").ShouldBeFalse();
        ribbon.SelectedTab!.Title.ShouldBe("Insert");
    }


    [Fact]
    public void SkipsAHiddenTab()
    {
        var hidden = Tab("Insert");
        hidden.IsVisible = false;

        var ribbon = Build(Tab("Home"), hidden, Tab("View"));

        ribbon.SelectTab(hidden).ShouldBeFalse();
        ribbon.SelectedTab!.Title.ShouldBe("Home");
    }


    /// <summary>
    /// The contextual tab case, and the reason the fallback exists: a tab bound to "is a table
    /// selected" simply disappears, and the ribbon has to land somewhere real rather than show an
    /// empty body.
    /// </summary>
    [Fact]
    public void FallsBackWhenTheSelectedTabIsHidden()
    {
        var contextual = Tab("Table");
        contextual.ContextTitle = "Table Tools";

        var ribbon = Build(Tab("Home"), Tab("Insert"), contextual);
        var reasons = new List<RibbonTabChangeReason>();
        ribbon.TabChanged += (_, e) => reasons.Add(e.Reason);

        ribbon.SelectTab("table").ShouldBeTrue();
        ribbon.SelectedTab.ShouldBe(contextual);
        contextual.IsContextual.ShouldBeTrue();

        contextual.IsVisible = false;

        ribbon.SelectedTab.ShouldNotBe(contextual);
        ribbon.SelectedTab!.IsVisible.ShouldBeTrue();
        reasons.ShouldContain(RibbonTabChangeReason.Fallback);
    }


    [Fact]
    public void PressingAButtonRunsItOnce()
    {
        var runs = 0;
        var button = Button("Paste", () => runs++);
        var ribbon = Build(Tab("Home", button));

        var invoked = new List<RibbonItem>();
        ribbon.ItemInvoked += (_, e) => invoked.Add(e.Item);

        ribbon.Invoke(button);

        runs.ShouldBe(1);
        invoked.ShouldHaveSingleItem().ShouldBe(button);
    }


    [Fact]
    public void ADisabledButtonDoesNothing()
    {
        var runs = 0;
        var button = Button("Paste", () => runs++);
        button.IsEnabled = false;

        var ribbon = Build(Tab("Home", button));
        ribbon.Invoke(button);

        runs.ShouldBe(0);
    }


    /// <summary>A group can dim its whole contents without each item having to be bound.</summary>
    [Fact]
    public void ADisabledGroupDeadensItsItems()
    {
        var runs = 0;
        var button = Button("Paste", () => runs++);
        var tab = Tab("Home", button);
        tab.Groups[0].IsEnabled = false;

        var ribbon = Build(tab);
        ribbon.Invoke(button);

        runs.ShouldBe(0);
    }


    [Fact]
    public void AToggleFlipsAndReportsTheNewState()
    {
        var states = new List<bool>();
        var toggle = new RibbonToggleButton { Text = "Bold" };
        toggle.CheckedChanged += (_, e) => states.Add(e.IsChecked);

        var ribbon = Build(Tab("Home", toggle));

        ribbon.Invoke(toggle);
        toggle.IsChecked.ShouldBeTrue();

        ribbon.Invoke(toggle);
        toggle.IsChecked.ShouldBeFalse();

        states.ShouldBe(new[] { true, false });
    }


    /// <summary>
    /// A toggle with no explicit parameter hands its command the new state, because that is what a
    /// toggle's command almost always wants to know.
    /// </summary>
    [Fact]
    public void AToggleCommandReceivesTheNewState()
    {
        object? seen = null;
        var toggle = new RibbonToggleButton
        {
            Text = "Bold",
            Command = new Command(p => seen = p)
        };

        var ribbon = Build(Tab("Home", toggle));
        ribbon.Invoke(toggle);

        seen.ShouldBe(true);
    }


    /// <summary>The face of a split button runs the default action; it does not open the menu.</summary>
    [Fact]
    public void ASplitButtonFaceRunsTheDefaultAction()
    {
        var runs = 0;
        var split = new RibbonSplitButton { Text = "Paste", Command = new Command(() => runs++) };
        split.Menu.Add(new RibbonMenuEntry { Text = "Text only" });

        var ribbon = Build(Tab("Home", split));
        ribbon.Invoke(split);

        runs.ShouldBe(1);
        ribbon.IsMenuOpen.ShouldBeFalse();
    }


    [Fact]
    public void TheDialogLauncherReachesTheRibbon()
    {
        var tab = Tab("Home", Button("Paste"));
        var group = tab.Groups[0];
        group.ShowDialogLauncher = true;

        var ribbon = Build(tab);
        var launched = new List<RibbonGroup>();
        ribbon.GroupDialogLauncherClicked += (_, e) => launched.Add(e.Group);

        group.InvokeDialogLauncher();

        launched.ShouldHaveSingleItem().ShouldBe(group);
    }


    /// <summary>
    /// Items are not in the visual tree, so nothing hands them a binding context on its own — a
    /// <c>{Binding}</c> on a ribbon button would silently resolve against null and never fire.
    /// </summary>
    [Fact]
    public void TheBindingContextReachesItemsAndMenuEntries()
    {
        var entry = new RibbonMenuEntry { Text = "Text only" };
        var menu = new RibbonMenuButton { Text = "Paste" };
        menu.Menu.Add(entry);

        var button = Button("Cut");
        var ribbon = Build(Tab("Home", button, menu));

        var context = new object();
        ribbon.BindingContext = context;

        button.BindingContext.ShouldBe(context);
        menu.BindingContext.ShouldBe(context);
        entry.BindingContext.ShouldBe(context);
    }


    [Fact]
    public void CollapsingHidesTheBodyAndComesBack()
    {
        var ribbon = Build(Tab("Home", Button("Paste")));

        ribbon.DisplayMode.ShouldBe(RibbonDisplayMode.Expanded);

        ribbon.ToggleCollapsed();
        ribbon.DisplayMode.ShouldBe(RibbonDisplayMode.Collapsed);

        ribbon.ToggleCollapsed();
        ribbon.DisplayMode.ShouldBe(RibbonDisplayMode.Expanded);
    }


    [Fact]
    public void CollapseCanBeRefused()
    {
        var ribbon = Build(Tab("Home", Button("Paste")));
        ribbon.AllowCollapse = false;

        ribbon.ToggleCollapsed();

        ribbon.DisplayMode.ShouldBe(RibbonDisplayMode.Expanded);
    }


    /// <summary>The application button is a command like any other and is testable the same way.</summary>
    [Fact]
    public void TheApplicationButtonRunsItsCommand()
    {
        var runs = 0;
        var ribbon = Build(Tab("Home"));
        ribbon.ApplicationButtonText = "File";
        ribbon.ApplicationButtonCommand = new Command(() => runs++);

        var clicked = 0;
        ribbon.ApplicationButtonClicked += (_, _) => clicked++;

        ribbon.InvokeApplicationButton();

        runs.ShouldBe(1);
        clicked.ShouldBe(1);
    }


    [Fact]
    public void HiddenItemsAndGroupsAreNotDrawn()
    {
        var shown = Button("Cut");
        var hidden = Button("Copy");
        hidden.IsVisible = false;

        var tab = Tab("Home", shown, hidden);
        var ribbon = Build(tab);

        tab.Groups[0].VisibleItems.ShouldBe(new RibbonItem[] { shown });
        ribbon.VisibleTabs.Count.ShouldBe(1);
    }


    // ---------------------------------------------------------------------------------------------
    // Rows
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Rows are the signal, not a mode. A group holding them lays them out as rows; a group of loose
    /// items keeps filling columns, which is what every existing bar relies on.
    /// </summary>
    [Fact]
    public void ARowIsNotAnInteractiveItem()
    {
        var row = new RibbonRow();
        row.Items.Add(Button("Bold"));
        row.Items.Add(Button("Italic"));

        row.Items.Count.ShouldBe(2);
        row.IsVisible.ShouldBeTrue();
    }


    [Fact]
    public void AHiddenItemLeavesItsRow()
    {
        var row = new RibbonRow();
        var bold = Button("Bold");
        var italic = Button("Italic");
        row.Items.Add(bold);
        row.Items.Add(italic);

        italic.IsVisible = false;

        row.VisibleItems.ShouldBe([bold]);
    }


    /// <summary>
    /// The group redraws when a row's contents change, not only when its own do. Without this a row
    /// built after the group was added to a tab would never reach the bar.
    /// </summary>
    [Fact]
    public void AddingToARowRedrawsTheGroup()
    {
        var row = new RibbonRow();
        var group = new RibbonGroup { Title = "Font" };
        group.Items.Add(row);

        var changed = 0;
        group.Changed += (_, _) => changed++;

        row.Items.Add(Button("Bold"));

        changed.ShouldBeGreaterThan(0);
    }
}
