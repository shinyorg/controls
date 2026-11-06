using System.Collections.Specialized;
using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.Cells;
using Shiny.Maui.Controls.DataGrid;
using Shiny.Maui.Controls.Ribbons;
using Shouldly;
using Xunit;
using TvTableSection = Shiny.Maui.Controls.Sections.TableSection;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// Fixes found while building a desktop app on the macOS AppKit head (ShinyCommander), covered headlessly.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class AppKitLayoutFixesTests
{
    /// <summary>
    /// A section's SelectedValue binding resolves before the section has rendered its cells; the change
    /// handler only reached cells that already existed, so the bound choice showed no check at all.
    /// </summary>
    [Fact]
    public void RadioGroupValueSetBeforeCellsRenderIsChecked()
    {
        TestDispatcherProvider.Install();
        new Application();
        var section = new TvTableSection("Theme");
        RadioCell.SetSelectedValue(section, "Dark");   // what the XAML attribute/binding does first
        section.Cells.Add(new RadioCell { Title = "Light", Value = "Light" });
        section.Cells.Add(new RadioCell { Title = "Dark", Value = "Dark" });

        var table = new TableView();
        table.Root.Sections.Add(section);

        Checked(section.Cells[0]).ShouldBeFalse();
        Checked(section.Cells[1]).ShouldBeTrue("the group's value was there before the cell was rendered");
        RadioCell.GetSelectedValue(section).ShouldBe("Dark", "syncing must not write a different value back");
    }

    [Fact]
    public void RadioGroupWithoutValueLeavesCellsUnchecked()
    {
        TestDispatcherProvider.Install();
        new Application();
        var section = new TvTableSection("Theme");
        section.Cells.Add(new RadioCell { Title = "Light", Value = "Light" });
        var table = new TableView();
        table.Root.Sections.Add(section);

        Checked(section.Cells[0]).ShouldBeFalse();
        section.IsSet(RadioCell.SelectedValueProperty).ShouldBeFalse();
    }

    static bool Checked(Element cell) => cell.GetVisualTreeDescendants().OfType<RadioButton>().Single().IsChecked;

    /// <summary>
    /// The DataGrid rewrites its rows by clearing and re-adding each one. Raised per row, that is one full
    /// native reload per row on the AppKit CollectionView - a bulk rewrite must raise a single Reset.
    /// </summary>
    [Fact]
    public void BatchRaisesOneResetForABulkRewrite()
    {
        var items = new BatchingObservableCollection<int> { 1, 2, 3 };
        var events = new List<NotifyCollectionChangedAction>();
        items.CollectionChanged += (_, e) => events.Add(e.Action);

        using (items.Batch())
        {
            items.Clear();
            for (var i = 0; i < 100; i++)
                items.Add(i);
        }

        events.ShouldBe([NotifyCollectionChangedAction.Reset]);
        items.Count.ShouldBe(100);
    }

    [Fact]
    public void NestedBatchesRaiseOnceAtTheOuterEnd_AndAnEmptyBatchRaisesNothing()
    {
        var items = new BatchingObservableCollection<int>();
        var count = 0;
        items.CollectionChanged += (_, _) => count++;

        using (items.Batch()) { }
        count.ShouldBe(0);

        using (items.Batch())
        {
            using (items.Batch())
                items.Add(1);
            count.ShouldBe(0, "still inside the outer batch");
            items.Add(2);
        }
        count.ShouldBe(1);

        items.Add(3);
        count.ShouldBe(2, "outside a batch it behaves like any ObservableCollection");
    }

    /// <summary>
    /// The AppKit ScrollView handler has no desired-size measure, so a horizontal ScrollView reported a
    /// height of 0 and the ribbon's (Auto) body row collapsed. The scroller falls back to its content.
    /// </summary>
    [Fact]
    public void RibbonScrollerTakesItsContentHeightWhenThePlatformReportsNone()
    {
        var scroller = new RibbonScroller
        {
            Orientation = ScrollOrientation.Horizontal,
            Content = new FixedSizeView(300, 72)
        };

        var size = scroller.Measure(200, double.PositiveInfinity);
        size.Height.ShouldBe(72);
        size.Width.ShouldBeLessThanOrEqualTo(200);
    }

    /// <summary>Headless views have no handler to measure them, so this one reports a fixed size itself.</summary>
    sealed class FixedSizeView(double width, double height) : ContentView
    {
        protected override Size MeasureOverride(double widthConstraint, double heightConstraint) => new(width, height);
    }
}
