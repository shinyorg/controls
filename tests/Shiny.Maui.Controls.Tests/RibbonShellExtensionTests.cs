using System.Globalization;
using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.Ribbons;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The ribbon pieces the Office shell needs: shortcut text on tooltips, the command list a command
/// search reads, the in-ribbon gallery and the number box.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class RibbonShellExtensionTests
{
    static (Ribbon Ribbon, RibbonGroup Group) Build()
    {
        var group = new RibbonGroup { Title = "Font" };
        var tab = new RibbonTab { Title = "Home", Key = "home" };
        tab.Groups.Add(group);

        var ribbon = new Ribbon();
        ribbon.Tabs.Add(tab);
        return (ribbon, group);
    }


    [Fact]
    public void TheShortcutJoinsTheTooltip()
    {
        new RibbonButton { Tooltip = "Bold", Shortcut = "Ctrl+B" }.TooltipWithShortcut.ShouldBe("Bold (Ctrl+B)");
        new RibbonButton { Text = "Paste" }.TooltipWithShortcut.ShouldBe("Paste");
        new RibbonButton { Tooltip = "Undo (Ctrl+Z)", Shortcut = "Ctrl+Z" }.TooltipWithShortcut.ShouldBe("Undo (Ctrl+Z)");
    }

    [Fact]
    public void EveryTabsCommandsAreListedWithWhereTheyLive()
    {
        var (ribbon, group) = Build();
        var pressed = 0;
        var bold = new RibbonButton { Tooltip = "Bold", Shortcut = "Ctrl+B" };
        bold.Clicked += (_, _) => pressed++;
        group.Items.Add(new RibbonRow { Items = { bold } });

        var insert = new RibbonTab { Title = "Insert" };
        var tables = new RibbonGroup { Title = "Tables" };
        tables.Items.Add(new RibbonButton { Text = "Table" });
        insert.Groups.Add(tables);
        ribbon.Tabs.Add(insert);

        var commands = ribbon.GetCommands();

        commands.Select(x => x.Label).ShouldBe(["Bold", "Table"]);
        commands[0].Category.ShouldBe("Home › Font");
        commands[0].Shortcut.ShouldBe("Ctrl+B");
        commands[1].Category.ShouldBe("Insert › Tables");

        commands[0].Invoke();
        pressed.ShouldBe(1);
    }

    [Fact]
    public void DropdownLinesAndHiddenItems()
    {
        var (ribbon, group) = Build();
        var menu = new RibbonMenuButton { Text = "Page Number" };
        menu.Menu.Add(new RibbonMenuEntry { Text = "Top of Page" });
        menu.Menu.Add(new RibbonMenuEntry { IsSeparator = true });
        menu.Menu.Add(new RibbonMenuEntry { Text = "Hidden", IsVisible = false });
        group.Items.Add(menu);
        group.Items.Add(new RibbonButton { Text = "Gone", IsVisible = false });

        ribbon.GetCommands().Select(x => x.Label).ShouldBe(["Page Number › Top of Page"]);
    }

    [Fact]
    public void GalleryEntriesAreCommands()
    {
        var (ribbon, group) = Build();
        var gallery = new RibbonGallery { Text = "Styles", ItemsSource = new[] { "Normal", "Heading 1" } };
        group.Items.Add(gallery);

        var commands = ribbon.GetCommands();
        commands.Select(x => x.Label).ShouldBe(["Styles › Normal", "Styles › Heading 1"]);

        commands[1].Invoke();
        gallery.SelectedItem.ShouldBe("Heading 1");
    }

    [Fact]
    public void TheGalleryStepsARowAtATimeAndFollowsTheSelection()
    {
        var gallery = new RibbonGallery { Columns = 3, ItemsSource = Enumerable.Range(0, 10).Select(x => $"S{x}").ToList() };

        gallery.FirstVisible.ShouldBe(0);
        gallery.Scroll(1);
        gallery.FirstVisible.ShouldBe(3);
        gallery.Scroll(1);
        gallery.Scroll(1);
        gallery.Scroll(1);
        gallery.FirstVisible.ShouldBe(9);   // the last row, and no further

        gallery.SelectedItem = "S1";
        gallery.FirstVisible.ShouldBe(0);

        object? picked = null;
        gallery.ItemSelected += (_, e) => picked = e.Item;
        gallery.Pick("S4");
        picked.ShouldBe("S4");
    }

    [Fact]
    public void TheNumberBoxParsesClampsAndSteps()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var box = new RibbonNumberBox { Text = "Before:", Unit = "pt", Step = 6, Maximum = 20 };
            double? committed = null;
            box.ValueCommitted += (_, v) => committed = v;

            box.StepBy(1);
            box.Value.ShouldBe(6);
            committed.ShouldBe(6);

            box.Commit("30 pt");
            box.Value.ShouldBe(20);

            box.Commit("nonsense");
            box.Value.ShouldBe(20);

            RibbonNumberBox.Format(0.5, 1, "\"").ShouldBe("0.5\"");
            RibbonNumberBox.Format(12, 1, "pt").ShouldBe("12 pt");
            box.Size.ShouldBe(RibbonItemSize.Small);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void TheHeaderEndSlotHoldsAView()
    {
        var (ribbon, _) = Build();
        var actions = new Label { Text = "Share" };

        ribbon.HeaderEndContent = actions;

        ribbon.HeaderEndContent.ShouldBeSameAs(actions);
        actions.Parent.ShouldNotBeNull();
    }
}
