using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.DataGrid;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// An <c>Auto</c> column used to be <see cref="GridLength.Auto"/> in every row Grid, so each row -
/// and the header - sized it to its own content and nothing lined up. The grid now resolves one
/// absolute width per Auto column and hands that to every row. A headless host cannot measure text,
/// so the measurer is faked: ten points per character of the widest label in the measured view.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class DataGridAutoWidthTests
{
    class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    static double FakeMeasure(View view)
        => Labels(view).Select(l => (l.Text?.Length ?? 0) * 10d).DefaultIfEmpty(0).Max();

    static IEnumerable<Label> Labels(Element root)
    {
        if (root is Label label)
            yield return label;

        var children = root switch
        {
            Layout layout => layout.Children.OfType<Element>(),
            ContentView content when content.Content is not null => [content.Content],
            Border border when border.Content is not null => [border.Content],
            _ => Enumerable.Empty<Element>()
        };

        foreach (var child in children)
        foreach (var nested in Labels(child))
            yield return nested;
    }

    static (DataGrid.DataGrid Grid, DataGridColumn Name) Build(IEnumerable<Person> people, int? sample = null)
    {
        _ = new Application();

        var grid = new DataGrid.DataGrid { AutoWidthMeasurer = FakeMeasure };
        if (sample is { } size)
            grid.AutoWidthSampleSize = size;

        var name = new DataGridColumn { Title = "Nm", PropertyName = nameof(Person.Name), Width = GridLength.Auto };
        grid.Columns.Add(name);
        grid.Columns.Add(new DataGridColumn { Title = "Age", PropertyName = nameof(Person.Age), Width = new GridLength(80) });
        grid.ItemsSource = people.ToList();
        return (grid, name);
    }

    static GridLength RowWidth(DataGrid.DataGrid grid, object item, int column)
    {
        var container = (Grid)grid.BuildDisplayItemView(item);
        var rowGrid = container.Children.OfType<Grid>().First();
        return rowGrid.ColumnDefinitions[column].Width;
    }

    static readonly Person[] People =
    [
        new() { Name = "Al", Age = 3 },
        new() { Name = "Bartholomew", Age = 40 },
        new() { Name = "Cy", Age = 22 }
    ];


    [Fact]
    public void EveryRowAndTheHeaderShareOneAbsoluteWidth()
    {
        var (grid, _) = Build(People);

        var header = grid.HeaderColumnWidths[0];
        header.IsAbsolute.ShouldBeTrue();

        // "Bartholomew" is the widest thing in the column - the header "Nm" and the short names all
        // take its width rather than their own.
        header.Value.ShouldBe(110);

        foreach (var row in grid.DisplayItems)
            RowWidth(grid, row, 0).ShouldBe(header);
    }


    [Fact]
    public void AWideHeaderWinsOverNarrowCells()
    {
        var (grid, name) = Build([new Person { Name = "A" }]);
        name.Title = "A very long header";
        grid.Columns.Add(new DataGridColumn { Title = "x", PropertyName = nameof(Person.Age) });

        grid.HeaderColumnWidths[0].Value.ShouldBe(180);
        RowWidth(grid, grid.DisplayItems[0], 0).Value.ShouldBe(180);
    }


    [Fact]
    public void TheFooterSharesTheWidth()
    {
        var (grid, _) = Build(People);
        var total = new DataGridSummaryRow();
        total.Cells.Add(new DataGridSummaryCell { Column = nameof(Person.Name), Text = "Everyone, in total" });
        grid.SummaryRows.Add(total);

        var width = grid.HeaderColumnWidths[0];
        width.Value.ShouldBe(180);

        var footer = (Grid)grid.FooterViews.Single();
        footer.ColumnDefinitions[0].Width.ShouldBe(width);
        foreach (var row in grid.DisplayItems)
            RowWidth(grid, row, 0).ShouldBe(width);
    }


    [Fact]
    public void ReResolvesWhenTheSourceChanges()
    {
        var (grid, _) = Build(People);
        grid.HeaderColumnWidths[0].Value.ShouldBe(110);

        grid.ItemsSource = new List<Person> { new() { Name = "Maximilian the Third" } };

        grid.HeaderColumnWidths[0].Value.ShouldBe(200);
        RowWidth(grid, grid.DisplayItems[0], 0).Value.ShouldBe(200);
    }


    [Fact]
    public void OnlyTheSampleIsMeasured()
    {
        var people = Enumerable.Range(0, 50).Select(i => new Person { Name = "Bo" })
            .Append(new Person { Name = "Way past the sample" })
            .ToList();

        var (grid, _) = Build(people, sample: 10);

        // The long name sits beyond the first ten items, so it is ellipsized rather than widening
        // the column - that is the cap doing its job for a large source.
        grid.HeaderColumnWidths[0].Value.ShouldBe(20);
    }


    [Fact]
    public void ColumnBoundsStillApply()
    {
        var (grid, name) = Build(People);
        name.MaxWidth = 60;
        grid.Columns.Add(new DataGridColumn { Title = "y", PropertyName = nameof(Person.Age) });

        grid.HeaderColumnWidths[0].Value.ShouldBe(60);
    }


    [Fact]
    public void FrozenPanesUseTheSharedWidth()
    {
        var (grid, name) = Build(People);
        grid.HorizontalScroll = true;
        name.Frozen = DataGridFrozen.Start;
        grid.Columns.Add(new DataGridColumn { Title = "z", PropertyName = nameof(Person.Age) });

        var width = grid.HeaderColumnWidths[0];
        width.ShouldBe(new GridLength(110));
        RowWidth(grid, grid.DisplayItems[0], 0).ShouldBe(width);
    }


    [Fact]
    public void WithoutAHandlerOrMeasurerTheColumnStaysProvisional()
    {
        _ = new Application();
        var grid = new DataGrid.DataGrid();
        grid.Columns.Add(new DataGridColumn { Title = "Nm", PropertyName = nameof(Person.Name), Width = GridLength.Auto });
        grid.ItemsSource = People.ToList();

        // Nothing can be measured before the grid is on screen; it re-resolves once the handler lands.
        grid.HeaderColumnWidths[0].IsAuto.ShouldBeTrue();
    }
}
