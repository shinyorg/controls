using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.DataGrid;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// How a column header divides its width between the title and the affordance glyphs (filter ▾,
/// group ⊞, reorder ‹ ›). It used to be a fixed 3*/2* Grid, which handed 40% of every header to a
/// strip that usually holds one ten-point ▾ - in the grouping sample's phone-width grid that left the
/// titles a sliver and they ellipsized down to a bare "…".
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class DataGridHeaderCellLayoutTests
{
    public DataGridHeaderCellLayoutTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }


    /// <summary>The sample's case: a ~40pt column interior, an 80pt title, one 10pt ▾.</summary>
    [Fact]
    public void ASingleGlyphTakesOnlyItsOwnWidth()
    {
        var (title, glyphs) = DataGridHeaderCellLayout.Split(available: 40, titleDesired: 80, glyphsDesired: 10, spacing: 6);

        glyphs.ShouldBe(10);
        title.ShouldBe(24);

        // What the old 3*/2* split gave the title from the same cell: (40 - 6) * 3 / 5.
        title.ShouldBeGreaterThan((40 - 6) * 3d / 5d);
    }


    [Fact]
    public void EverythingFitsWhenThereIsRoom()
    {
        var (title, glyphs) = DataGridHeaderCellLayout.Split(available: 200, titleDesired: 60, glyphsDesired: 40, spacing: 6);

        glyphs.ShouldBe(40);
        title.ShouldBe(154); // the title is arranged across the rest, so its alignment still applies
    }


    /// <summary>
    /// The concern the star split existed for: a full strip (filter, group, reorder) must not squeeze
    /// the title out of a narrow column. The title keeps its guaranteed share and the glyphs clip.
    /// </summary>
    [Fact]
    public void AWideStripCannotCrowdTheTitleOut()
    {
        var (title, glyphs) = DataGridHeaderCellLayout.Split(available: 106, titleDesired: 90, glyphsDesired: 70, spacing: 6);

        title.ShouldBe(100 * DataGridHeaderCellLayout.TitleShare);
        glyphs.ShouldBe(100 - title);
    }


    [Fact]
    public void AShortTitleLetsTheGlyphsHaveTheRest()
    {
        var (title, glyphs) = DataGridHeaderCellLayout.Split(available: 106, titleDesired: 20, glyphsDesired: 70, spacing: 6);

        glyphs.ShouldBe(70);
        title.ShouldBe(30);
    }


    [Fact]
    public void NoGlyphsMeansTheTitleGetsTheWholeCellAndNoGap()
        => DataGridHeaderCellLayout.Split(available: 50, titleDesired: 80, glyphsDesired: 0, spacing: 6)
            .ShouldBe((50d, 0d));


    [Fact]
    public void ANegativeRoomCollapsesToZeroRatherThanThrowing()
    {
        var (title, glyphs) = DataGridHeaderCellLayout.Split(available: 2, titleDesired: 80, glyphsDesired: 10, spacing: 6);

        title.ShouldBe(0);
        glyphs.ShouldBe(0);
    }


    /// <summary>The grid actually builds its default headers with the layout, not a star-split Grid.</summary>
    [Fact]
    public void DefaultHeadersUseTheSharingLayout()
    {
        var grid = new DataGrid.DataGrid();
        grid.Columns.Add(new DataGridColumn { Title = "Department", PropertyName = "Department", Width = GridLength.Star });

        var cells = Descendants(grid).OfType<DataGridHeaderCellLayout>().ToList();

        cells.Count.ShouldBe(1);
        cells[0].Glyphs.ShouldNotBeNull(); // the default Menu filter mode adds a ▾
    }


    static IEnumerable<Element> Descendants(Element root)
    {
        foreach (var child in ((IElementController)root).LogicalChildren)
        {
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }
}
