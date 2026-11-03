using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.Cells;
using Shouldly;
using Xunit;
using Slider = Shiny.Maui.Controls.Slider;
using SwitchCell = Shiny.Maui.Controls.Cells.SwitchCell;
using TvTableSection = Shiny.Maui.Controls.Sections.TableSection;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// A cell only carries a recognizer for something it will act on: the tap while it can be selected, and
/// CustomCell's double-tap while it has a LongCommand. On the AppKit head every one of them is an
/// NSClickGestureRecognizer over the whole cell that takes the mouse-down from the controls inside it - a
/// SwitchCell's switch, or a slider in a CustomCell, which then could not be dragged at all.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class CellTapGestureTests
{
    // Every concrete cell, found rather than listed, so a new cell type is held to the same rule.
    public static TheoryData<Type> CellTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var type in typeof(CellBase).Assembly.GetTypes()
                     .Where(t => typeof(CellBase).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
                     .OrderBy(t => t.Name))
            data.Add(type);
        return data;
    }

    static CellBase Create(Type type)
    {
        new Application();
        return (CellBase)Activator.CreateInstance(type)!;
    }

    [Theory]
    [MemberData(nameof(CellTypes))]
    public void ASelectableCellHasOnlyItsTapRecognizer(Type type)
    {
        var cell = Create(type);

        // On the AppKit head a TextPickerCell's click goes straight to its NSPopUpButton (see
        // NativeControlTakesClicks); a recognizer there would take it, and the menu never opened.
        if (type == typeof(TextPickerCell) && OperatingSystem.IsMacOS())
        {
            cell.GestureRecognizers.ShouldBeEmpty();
            return;
        }

        var tap = cell.GestureRecognizers.ShouldHaveSingleItem().ShouldBeOfType<TapGestureRecognizer>();
        tap.NumberOfTapsRequired.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(CellTypes))]
    public void ACellThatCantBeSelectedHasNoRecognizers(Type type)
    {
        var cell = Create(type);
        cell.IsSelectable = false;

        cell.GestureRecognizers.ShouldBeEmpty();
    }

    [Fact]
    public void TheRecognizerFollowsIsSelectable()
    {
        new Application();

        var cell = new SwitchCell { IsSelectable = false };
        cell.IsSelectable = true;
        cell.IsSelectable = true;
        cell.GestureRecognizers.OfType<TapGestureRecognizer>().Count().ShouldBe(1);

        cell.IsSelectable = false;
        cell.GestureRecognizers.ShouldBeEmpty();
    }

    [Fact]
    public void ACustomCellWithALongCommandHasTheDoubleTap()
    {
        new Application();

        var cell = new CustomCell { IsSelectable = false, LongCommand = new Command(() => { }) };

        cell.GestureRecognizers.ShouldHaveSingleItem().ShouldBeOfType<TapGestureRecognizer>().NumberOfTapsRequired.ShouldBe(2);
    }

    [Fact]
    public void TheDoubleTapFollowsLongCommand()
    {
        new Application();

        var cell = new CustomCell { IsSelectable = false };
        cell.LongCommand = new Command(() => { });
        cell.LongCommand = new Command(() => { });
        cell.GestureRecognizers.Count.ShouldBe(1);

        cell.LongCommand = null;
        cell.GestureRecognizers.ShouldBeEmpty();
    }

    [Fact]
    public void TheDoubleTapStillRunsTheLongCommand()
    {
        new Application();

        var ran = 0;
        var cell = new CustomCell { LongCommand = new Command(() => ran++) };

        var doubleTap = cell.GestureRecognizers.OfType<TapGestureRecognizer>().Single(x => x.NumberOfTapsRequired == 2);
        // SendTapped is internal; the platform heads reach it the same way.
        var sendTapped = typeof(TapGestureRecognizer).GetMethod("SendTapped", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        sendTapped.Invoke(doubleTap, sendTapped.GetParameters().Length == 1 ? [cell] : [cell, null]);

        ran.ShouldBe(1);
    }

    /// <summary>
    /// The case that broke: a settings row with a slider in it. Nothing between the slider and the table may
    /// carry a recognizer, or on AppKit the drag never reaches the slider's own pan.
    /// </summary>
    [Fact]
    public void ASliderInACellThatCantBeSelectedHasNoRecognizerAboveIt()
    {
        // TableView renders through the dispatcher; the test one runs it inline.
        TestDispatcherProvider.Install();
        new Application();

        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 50 };
        var table = new TableView();
        var section = new TvTableSection("Notch");
        section.Cells.Add(new CustomCell { Title = "Width", IsSelectable = false, CustomContent = new HorizontalStackLayout { Children = { slider } } });
        table.Root.Sections.Add(section);

        var ancestors = new List<Element>();
        for (var element = slider.Parent; element != null && element != table; element = element.Parent)
            ancestors.Add(element);

        ancestors.ShouldContain(x => x is CustomCell, "the slider should be inside the cell in the table's tree");
        ancestors.OfType<View>().Where(x => x.GestureRecognizers.Count > 0)
            .Select(x => x.GetType().Name)
            .ShouldBeEmpty();
    }
}
