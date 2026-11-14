using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Maui.Controls.Themes;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// A SecurityPin with no colours set drew cells with no outline and no fill: every colour defaulted to
/// null, and a null was simply not written, so nothing but an app-wide implicit Border style could make
/// the cells visible. (#31)
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class SecurityPinTests
{
    readonly ResourceDictionary theme = new BasicLightTheme();

    public SecurityPinTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }


    SecurityPin Pin()
    {
        var pin = new SecurityPin { Length = 4 };
        pin.Resources.MergedDictionaries.Add(this.theme);
        return pin;
    }

    Color Token(string key) => this.theme[key] switch
    {
        Color color => color,
        SolidColorBrush brush => brush.Color,
        var other => throw new InvalidOperationException($"{key} is a {other?.GetType().Name}")
    };

    static List<Border> Cells(SecurityPin pin) => Find<Border>(pin).ToList();

    static IEnumerable<T> Find<T>(IVisualTreeElement element)
    {
        if (element is T match)
            yield return match;

        foreach (var child in element.GetVisualChildren())
        {
            foreach (var found in Find<T>(child))
                yield return found;
        }
    }

    static Color? StrokeOf(Border border) => (border.Stroke as SolidColorBrush)?.Color;


    [Fact]
    public void WithNoColoursSet_TheCellsFollowTheTheme()
    {
        var pin = Pin();
        var cells = Cells(pin);

        cells.Count.ShouldBe(4);
        foreach (var cell in cells)
        {
            StrokeOf(cell).ShouldBe(this.Token(ShinyThemeKeys.Brush.Outline));
            cell.BackgroundColor.ShouldBe(this.Token(ShinyThemeKeys.Color.SurfaceContainerLow));
        }

        foreach (var label in Find<Label>(pin).Where(x => x.Parent is Border))
            label.TextColor.ShouldBe(this.Token(ShinyThemeKeys.Color.OnSurface));
    }


    [Fact]
    public void AnExplicitColour_WinsOverTheTheme()
    {
        var pin = Pin();
        pin.CellBorderColor = Colors.Red;
        pin.CellBackgroundColor = Colors.Yellow;
        pin.CellTextColor = Colors.Blue;

        foreach (var cell in Cells(pin))
        {
            StrokeOf(cell).ShouldBe(Colors.Red);
            cell.BackgroundColor.ShouldBe(Colors.Yellow);
        }

        foreach (var label in Find<Label>(pin).Where(x => x.Parent is Border))
            label.TextColor.ShouldBe(Colors.Blue);
    }


    [Fact]
    public void ClearingAColour_HandsTheCellBackToTheTheme()
    {
        var pin = Pin();
        pin.CellBorderColor = Colors.Red;
        pin.CellBackgroundColor = Colors.Yellow;

        pin.CellBorderColor = null;
        pin.CellBackgroundColor = null;

        foreach (var cell in Cells(pin))
        {
            StrokeOf(cell).ShouldBe(this.Token(ShinyThemeKeys.Brush.Outline));
            cell.BackgroundColor.ShouldBe(this.Token(ShinyThemeKeys.Color.SurfaceContainerLow));
        }
    }


    [Fact]
    public void ChangingTheLength_BuildsThemedCells()
    {
        var pin = Pin();
        pin.Length = 6;

        var cells = Cells(pin);
        cells.Count.ShouldBe(6);
        StrokeOf(cells[5]).ShouldBe(this.Token(ShinyThemeKeys.Brush.Outline));
    }


    // A test host never gives an Entry real focus. IView.IsFocused is the setter the handlers use,
    // and it raises Focused / Unfocused just as the platform would.
    static void FocusEntry(SecurityPin pin, bool focused = true)
        => ((Microsoft.Maui.IView)Find<Entry>(pin).Single()).IsFocused = focused;


    [Fact]
    public void TheCurrentCell_TakesTheFocusedAppearance()
    {
        var pin = Pin();
        pin.Value = "1";
        pin.CellFocusedBorderColor = Colors.Orange;
        pin.CellFocusedBackgroundColor = Colors.Beige;
        pin.CellFocusedTextColor = Colors.Brown;
        pin.CellFocusedBorderThickness = 3;
        pin.CellBorderStyle = SecurityPinBorderStyle.Dashed;
        pin.CellFocusedBorderStyle = SecurityPinBorderStyle.Solid;

        FocusEntry(pin);

        var cells = Cells(pin);
        var current = cells[1];
        StrokeOf(current).ShouldBe(Colors.Orange);
        current.BackgroundColor.ShouldBe(Colors.Beige);
        current.StrokeThickness.ShouldBe(3);
        current.StrokeDashArray.ShouldBeEmpty();
        ((Label)current.Content!).TextColor.ShouldBe(Colors.Brown);

        var other = cells[2];
        StrokeOf(other).ShouldBe(this.Token(ShinyThemeKeys.Brush.Outline));
        other.StrokeThickness.ShouldNotBe(3);
        other.StrokeDashArray.ShouldNotBeEmpty();
    }


    [Fact]
    public void LosingFocus_ReturnsTheCurrentCellToNormal()
    {
        var pin = Pin();
        pin.CellFocusedBorderThickness = 3;
        pin.CellFocusedBackgroundColor = Colors.Beige;

        FocusEntry(pin);
        FocusEntry(pin, false);

        var first = Cells(pin)[0];
        first.StrokeThickness.ShouldNotBe(3);
        first.BackgroundColor.ShouldBe(this.Token(ShinyThemeKeys.Color.SurfaceContainerLow));
    }


    [Fact]
    public void HighlightCurrentCellOff_DrawsEveryCellAlike()
    {
        var pin = Pin();
        pin.HighlightCurrentCell = false;
        pin.CellFocusedBackgroundColor = Colors.Beige;

        FocusEntry(pin);

        var first = Cells(pin)[0];
        StrokeOf(first).ShouldBe(this.Token(ShinyThemeKeys.Brush.Outline));
        first.BackgroundColor.ShouldBe(this.Token(ShinyThemeKeys.Color.SurfaceContainerLow));
    }


    [Fact]
    public void BorderStyleNone_DrawsNoStroke()
    {
        var pin = Pin();
        pin.CellBorderThickness = 2;
        pin.CellBorderStyle = SecurityPinBorderStyle.None;

        foreach (var cell in Cells(pin))
            cell.StrokeThickness.ShouldBe(0);

        pin.CellBorderStyle = SecurityPinBorderStyle.Solid;
        foreach (var cell in Cells(pin))
            cell.StrokeThickness.ShouldBe(2);
    }
}
