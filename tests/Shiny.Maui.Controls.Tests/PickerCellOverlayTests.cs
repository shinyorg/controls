using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Maui.Controls.Cells;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The picker cells keep a real Picker / TimePicker / DatePicker behind their own labels. On Android (and for
/// TextPickerCell everywhere) it lies over the whole cell at 1% opacity so it still takes the tap - but at 1% it
/// still draws its text, a faint copy of the value behind the title. Its text has to be transparent.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class PickerCellOverlayTests
{
    static T FindPicker<T>(CellBase cell) where T : View
        => ((IVisualTreeElement)cell).GetVisualTreeDescendants().OfType<T>().ShouldHaveSingleItem();

    [Fact]
    public void TextPickerCellOverlayDrawsNoText()
    {
        new Application();
        var cell = new TextPickerCell
        {
            ItemsSource = new[] { "A rather long value that reaches under the title" },
            SelectedIndex = 0,
            PickerTitle = "Pick one"
        };

        var picker = FindPicker<Picker>(cell);
        picker.Opacity.ShouldBeGreaterThan(0); // still takes the tap
        picker.TextColor.ShouldBe(Colors.Transparent);
        picker.TitleColor.ShouldBe(Colors.Transparent);
    }

    [Fact]
    public void TimePickerCellPickerDrawsNoText()
    {
        new Application();
        var picker = FindPicker<TimePicker>(new TimePickerCell { Time = new TimeSpan(7, 30, 0) });
        picker.TextColor.ShouldBe(Colors.Transparent);
    }

    [Fact]
    public void DatePickerCellPickerDrawsNoText()
    {
        new Application();
        var picker = FindPicker<DatePicker>(new DatePickerCell { Date = new DateTime(2026, 11, 4) });
        picker.TextColor.ShouldBe(Colors.Transparent);
    }
}
