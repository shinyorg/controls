using System.Globalization;
using Microsoft.AspNetCore.Components;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// The ribbon pieces the Office shell needs: shortcut text on tooltips, the command index a command
/// search reads, the in-ribbon gallery's stepping, and the number box's parsing.
/// </summary>
public class RibbonShellExtensionTests
{
    static EventCallback Callback(Action action) => new(null, action);


    [Fact]
    public void RenderedButtonsAreIndexedWithTheirTabGroupAndShortcut()
    {
        var ribbon = new Ribbon();
        var tab = new RibbonTab { Title = "Home", Key = "home", Ribbon = ribbon };
        var group = new RibbonGroup { Title = "Font", Ribbon = ribbon, Tab = tab };
        var pressed = 0;
        var bold = new RibbonButton { Tooltip = "Bold", Shortcut = "Ctrl+B", Ribbon = ribbon, Group = group, Clicked = Callback(() => pressed++) };

        var changed = 0;
        ribbon.CommandsChanged += (_, _) => changed++;
        ribbon.IndexCommand(bold);

        var command = ribbon.GetCommands().Single();
        command.Label.ShouldBe("Bold");
        command.Shortcut.ShouldBe("Ctrl+B");
        command.Category.ShouldBe("Home › Font");
        command.TabKey.ShouldBe("home");
        changed.ShouldBe(1);

        command.InvokeAsync();
        pressed.ShouldBe(1);
    }

    [Fact]
    public void ReIndexingAnUnchangedItemIsSilent()
    {
        var ribbon = new Ribbon();
        var button = new RibbonButton { Text = "Paste", Ribbon = ribbon };
        var changed = 0;
        ribbon.CommandsChanged += (_, _) => changed++;

        ribbon.IndexCommand(button);
        ribbon.IndexCommand(button);
        button.Disabled = true;
        ribbon.IndexCommand(button);

        changed.ShouldBe(2);
        ribbon.GetCommands().Single().IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void DropdownLinesAreIndexedUnderTheirButton()
    {
        var ribbon = new Ribbon();
        var menu = new RibbonMenuButton
        {
            Text = "Page Number",
            Ribbon = ribbon,
            Menu =
            [
                new RibbonMenuEntry { Text = "Top of Page" },
                new RibbonMenuEntry { IsSeparator = true },
                new RibbonMenuEntry { Text = "Format", Children = [new RibbonMenuEntry { Text = "Roman" }] }
            ]
        };

        ribbon.IndexCommand(menu);

        ribbon.GetCommands().Select(x => x.Label).ShouldBe(["Page Number › Top of Page", "Page Number › Format › Roman"]);
    }

    [Fact]
    public void GalleryEntriesAreIndexedByName()
    {
        var ribbon = new Ribbon();
        var gallery = new RibbonGallery<string>
        {
            Text = "Styles",
            Ribbon = ribbon,
            Items = ["Normal", "Heading 1"],
            ItemText = x => x
        };

        ribbon.IndexCommand(gallery);

        ribbon.GetCommands().Select(x => x.Label).ShouldBe(["Styles › Normal", "Styles › Heading 1"]);
    }

    [Theory]
    [InlineData(10, 5, 5)]      // two full rows of five
    [InlineData(12, 5, 10)]     // the last row starts at 10
    [InlineData(4, 5, 0)]       // fits
    [InlineData(12, 10, 5)]     // two-row strip: the last window starts a row before the last row
    public void TheStripStopsAtTheLastRow(int count, int pageSize, int expected)
        => RibbonGallery<string>.MaxFirst(pageSize, 5, count).ShouldBe(expected);

    [Theory]
    [InlineData(0, 7, 5, 5)]
    [InlineData(10, 3, 5, 0)]
    [InlineData(5, 6, 5, 5)]
    public void TheSelectionIsScrolledIntoViewARowAtATime(int first, int selected, int pageSize, int expected)
        => RibbonGallery<string>.ScrollIntoView(first, selected, pageSize, 5, 20).ShouldBe(expected);

    [Fact]
    public void TheNumberBoxReadsAndWritesItsUnit()
    {
        using var culture = new CultureScope("en-US");

        RibbonNumberBox.Format(0, 1, "pt").ShouldBe("0 pt");
        RibbonNumberBox.Format(0.5, 1, "\"").ShouldBe("0.5\"");
        RibbonNumberBox.Format(12.25, 1, null).ShouldBe("12.2");

        RibbonNumberBox.TryParse("12 pt", "pt", out var a).ShouldBeTrue();
        a.ShouldBe(12);
        RibbonNumberBox.TryParse("1.5\"", "\"", out var b).ShouldBeTrue();
        b.ShouldBe(1.5);
        RibbonNumberBox.TryParse("lots", "pt", out _).ShouldBeFalse();

        RibbonNumberBox.Clamp(-4, 0, 100, 1).ShouldBe(0);
        RibbonNumberBox.Clamp(3.14159, 0, 100, 2).ShouldBe(3.14);
    }


    sealed class CultureScope : IDisposable
    {
        readonly CultureInfo previous = CultureInfo.CurrentCulture;

        public CultureScope(string name) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);

        public void Dispose() => CultureInfo.CurrentCulture = this.previous;
    }
}
