using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Spreadsheet.View;
using Shouldly;
using Xunit;

namespace Shiny.Controls.Office.Tests;

public class SpreadsheetControllerTests
{
    static async Task<(Workbook Workbook, SpreadsheetController Controller)> SetupAsync()
    {
        var workbook = await Workbook.OpenAsync(new MemoryStream(WorkbookFixture.Build()));
        var controller = new SpreadsheetController(workbook, workbook["Data"]);
        controller.Resize(600, 400);
        return (workbook, controller);
    }

    [Theory]
    [InlineData("42", CellValueKind.Number)]
    [InlineData("-3.5", CellValueKind.Number)]
    [InlineData("TRUE", CellValueKind.Boolean)]
    [InlineData("false", CellValueKind.Boolean)]
    [InlineData("#N/A", CellValueKind.Error)]
    [InlineData("hello", CellValueKind.Text)]
    [InlineData("", CellValueKind.Blank)]
    public void TypedInputIsInterpretedTheWayExcelWould(string input, CellValueKind expected)
        => SpreadsheetController.ParseInput(input).Kind.ShouldBe(expected);

    [Fact]
    public void APercentageStoresTheFraction()
        => SpreadsheetController.ParseInput("50%").AsNumber().ShouldBe(0.5);

    [Fact]
    public void TextThatMerelyLooksLikeADateStaysText()
    {
        // Silently converting it would change what the user typed.
        SpreadsheetController.ParseInput("2026-08-24").Kind.ShouldBe(CellValueKind.Text);
    }

    [Fact]
    public async Task ClickingSelectsTheCellUnderThePointer()
    {
        var (workbook, controller) = await SetupAsync();
        using var _ = workbook;

        var target = CellRef.Parse("C4");
        var rect = controller.Viewport.CellRect(target);
        controller.PointerDown(rect.X + 3, rect.Y + 3);

        controller.Selection.Active.ShouldBe(target);
    }

    [Fact]
    public async Task DraggingExtendsTheSelection()
    {
        var (workbook, controller) = await SetupAsync();
        using var _ = workbook;

        var from = controller.Viewport.CellRect(CellRef.Parse("B2"));
        var to = controller.Viewport.CellRect(CellRef.Parse("D5"));

        // Inside the cell rather than on its corner: the corner of A1, which is where the press would
        // otherwise land, carries the fill handle.
        controller.PointerDown(from.X + 8, from.Y + 8);
        controller.PointerMove(to.X + 2, to.Y + 2);
        controller.PointerUp();

        controller.Selection.Range.ShouldBe(CellRange.Parse("B2:D5"));
    }

    [Fact]
    public async Task DraggingAHeaderDividerResizesTheColumn()
    {
        var (workbook, controller) = await SetupAsync();
        using var _ = workbook;

        var before = controller.Metrics.Columns.SizeOf(0);
        var rect = controller.Viewport.CellRect(CellRef.Parse("A1"));

        controller.PointerDown(rect.Right - 1, 5);
        controller.PointerMove(rect.Right + 40, 5);
        controller.PointerUp();

        controller.Metrics.Columns.SizeOf(0).ShouldBe(before + 41, 1.5);
    }

    [Fact]
    public async Task ClickingAColumnHeaderSelectsTheColumn()
    {
        var (workbook, controller) = await SetupAsync();
        using var _ = workbook;

        var rect = controller.Viewport.CellRect(CellRef.Parse("B1"));
        controller.PointerDown(rect.X + 10, 5);

        controller.Selection.Range.Left.ShouldBe(1);
        controller.Selection.Range.RowCount.ShouldBe(CellRef.MaxRow + 1);
    }

    [Fact]
    public async Task DoubleClickingOpensTheEditorOnTheCellsContent()
    {
        var (workbook, controller) = await SetupAsync();
        using var _ = workbook;

        var rect = controller.Viewport.CellRect(CellRef.Parse("A1"));
        controller.DoubleClick(rect.X + 3, rect.Y + 3);

        controller.EditingCell.ShouldBe(CellRef.Parse("A1"));
        controller.EditingText.ShouldBe("Widget");
    }

    [Fact]
    public async Task TheEditorShowsAFormulaRatherThanItsResult()
    {
        var (workbook, controller) = await SetupAsync();
        using var _ = workbook;

        controller.Selection.MoveTo(CellRef.Parse("C1"));
        controller.BeginEdit();

        controller.EditingText.ShouldBe("=B1*2", "editing a formula cell must show the formula");
    }

    [Fact]
    public async Task CommittingWritesThroughTheUndoStack()
    {
        var (workbook, controller) = await SetupAsync();
        using var _ = workbook;

        controller.Selection.MoveTo(CellRef.Parse("E5"));
        controller.BeginEdit("123");
        controller.CommitEdit(EditCommitDirection.Down);

        workbook["Data"].GetValue(CellRef.Parse("E5")).AsNumber().ShouldBe(123);
        controller.Selection.Active.ShouldBe(CellRef.Parse("E6"), "Enter moves down after committing");

        workbook.Undo.Undo();
        workbook["Data"].GetValue(CellRef.Parse("E5")).IsBlank.ShouldBeTrue();
    }

    [Fact]
    public async Task CommittingAFormulaComputesIt()
    {
        var (workbook, controller) = await SetupAsync();
        using var _ = workbook;

        controller.Selection.MoveTo(CellRef.Parse("E1"));
        controller.BeginEdit("=B1+8");
        controller.CommitEdit(EditCommitDirection.None);

        workbook.GetEffectiveValue("Data", CellRef.Parse("E1")).AsNumber().ShouldBe(50);
    }

    [Fact]
    public async Task CancellingLeavesTheCellAlone()
    {
        var (workbook, controller) = await SetupAsync();
        using var _ = workbook;

        controller.Selection.MoveTo(CellRef.Parse("B1"));
        controller.BeginEdit("999");
        controller.CancelEdit();

        controller.EditingCell.ShouldBeNull();
        workbook["Data"].GetValue(CellRef.Parse("B1")).AsNumber().ShouldBe(42);
    }

    [Fact]
    public async Task MovingWhileEditingCommitsFirst()
    {
        var (workbook, controller) = await SetupAsync();
        using var _ = workbook;

        controller.Selection.MoveTo(CellRef.Parse("E2"));
        controller.BeginEdit("7");
        controller.Move(MoveDirection.Down);

        workbook["Data"].GetValue(CellRef.Parse("E2")).AsNumber().ShouldBe(7);
        controller.EditingCell.ShouldBeNull();
    }

    [Fact]
    public async Task DeleteClearsTheSelectionAsOneUndoStep()
    {
        var (workbook, controller) = await SetupAsync();
        using var _ = workbook;

        controller.Selection.SelectRange(CellRange.Parse("A1:B1"));
        controller.ClearSelection();

        workbook["Data"].GetValue(CellRef.Parse("A1")).IsBlank.ShouldBeTrue();
        workbook["Data"].GetValue(CellRef.Parse("B1")).IsBlank.ShouldBeTrue();

        controller.Undo();

        workbook["Data"].GetValue(CellRef.Parse("A1")).AsText().ShouldBe("Widget");
    }

    [Fact]
    public async Task TheFormulaBarTextTracksTheActiveCell()
    {
        var (workbook, controller) = await SetupAsync();
        using var _ = workbook;

        controller.Selection.MoveTo(CellRef.Parse("A1"));
        controller.ActiveCellText.ShouldBe("Widget");

        controller.Selection.MoveTo(CellRef.Parse("C1"));
        controller.ActiveCellText.ShouldBe("=B1*2");

        controller.Selection.MoveTo(CellRef.Parse("Z50"));
        controller.ActiveCellText.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task MovingKeepsTheActiveCellInView()
    {
        var (workbook, controller) = await SetupAsync();
        using var _ = workbook;

        for (var i = 0; i < 40; i++)
            controller.Move(MoveDirection.Down);

        var rect = controller.Viewport.CellRect(controller.Selection.Active);
        rect.Bottom.ShouldBeLessThanOrEqualTo(controller.Viewport.Height + 0.5);
        rect.Y.ShouldBeGreaterThanOrEqualTo(controller.Metrics.ColumnHeaderHeight - 0.5);
    }

    [Fact]
    public async Task ChangedFiresSoTheHostKnowsToRepaint()
    {
        var (workbook, controller) = await SetupAsync();
        using var _ = workbook;

        var count = 0;
        controller.Changed += (_, _) => count++;

        controller.Selection.MoveTo(CellRef.Parse("B2"));
        controller.Scroll(10, 10);

        count.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task SwitchingSheetsRebuildsTheLayout()
    {
        using var workbook = await Workbook.OpenAsync(new MemoryStream(WorkbookFixture.BuildMultiSheet()));
        var controller = new SpreadsheetController(workbook, workbook["Data"]);
        controller.Resize(600, 400);

        controller.Selection.MoveTo(CellRef.Parse("C3"));
        controller.BeginEdit();
        controller.SwitchSheet(workbook["Summary"]);

        // A sheet nobody has been on yet starts at A1, and an open editor does not follow the switch.
        controller.Sheet.Name.ShouldBe("Summary");
        controller.Selection.Active.ShouldBe(new CellRef(0, 0));
        controller.EditingCell.ShouldBeNull();
    }

    [Fact]
    public async Task SwitchingToTheSheetAlreadyShowingChangesNothing()
    {
        // Clicking the tab you are already on must not throw away where you were - which is what
        // rebuilding the layout unconditionally used to do.
        var (workbook, controller) = await SetupAsync();
        using var _ = workbook;

        controller.Selection.MoveTo(CellRef.Parse("C3"));
        controller.SwitchSheet(workbook["Data"]);

        controller.Selection.Active.ShouldBe(CellRef.Parse("C3"));
    }
}
