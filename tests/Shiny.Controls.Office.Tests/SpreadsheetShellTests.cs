using System.Globalization;
using System.Text;
using Shiny.Controls.Office.Shell;
using Shiny.Controls.Office.Skia;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Spreadsheet.Commands;
using Shiny.Controls.Office.Spreadsheet.View;
using Shouldly;
using Xunit;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// The Excel shell's host-agnostic half: the status bar's words, the comments pane's list, the
/// backstage's templates and info, pagination for the page views, and CSV/PDF export.
/// </summary>
public class SpreadsheetShellTests
{
    static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    static Workbook Book()
    {
        var book = Workbook.Create("Data");
        book.Execute(new SetCellValueCommand("Data", CellRef.Parse("A1"), CellValue.FromText("Name, first")));
        book.Execute(new SetCellValueCommand("Data", CellRef.Parse("B1"), CellValue.FromNumber(4)));
        book.Execute(new SetCellValueCommand("Data", CellRef.Parse("B2"), CellValue.FromNumber(8)));
        book.Execute(new SetCellFormulaCommand("Data", CellRef.Parse("B3"), "B1+B2"));
        book.Execute(new SetCellValueCommand("Data", CellRef.Parse("A2"), CellValue.FromText("say \"hi\"")));
        return book;
    }

    [Fact]
    public void Aggregates_NullForSingleCell_AndExcelWordsForARange()
    {
        using var book = Book();
        var controller = new SpreadsheetController(book, book.Sheets[0]);

        controller.Selection.MoveTo(CellRef.Parse("B1"));
        SpreadsheetShell.Aggregates(controller.SelectionStatistics, En).ShouldBeNull();

        controller.Selection.SelectRange(new CellRange(CellRef.Parse("B1"), CellRef.Parse("B3")));
        SpreadsheetShell.Aggregates(controller.SelectionStatistics, En).ShouldBe("Average: 8  Count: 3  Sum: 24");

        controller.Selection.SelectRange(new CellRange(CellRef.Parse("A1"), CellRef.Parse("A2")));
        SpreadsheetShell.Aggregates(controller.SelectionStatistics, En).ShouldBe("Count: 2");
    }

    [Fact]
    public void EditMode_ReadyEnterEdit()
    {
        using var book = Book();
        var controller = new SpreadsheetController(book, book.Sheets[0]);
        controller.EditMode.ShouldBe(SheetEditMode.Ready);

        controller.BeginEdit("x");
        controller.EditMode.ShouldBe(SheetEditMode.Enter);
        controller.CancelEdit();

        controller.BeginEdit();
        controller.EditMode.ShouldBe(SheetEditMode.Edit);
        SpreadsheetShell.ModeText(controller.EditMode).ShouldBe("Edit");
    }

    [Fact]
    public void ViewModeIds_RoundTrip()
    {
        foreach (var mode in Enum.GetValues<SheetViewMode>())
            SpreadsheetShell.ParseViewMode(SpreadsheetShell.ViewModeId(mode)).ShouldBe(mode);

        SpreadsheetShell.ParseViewMode("nonsense").ShouldBe(SheetViewMode.Normal);
    }

    [Fact]
    public void PageLayout_OnlyInPageViews_AndBreaksAtWholeColumns()
    {
        using var book = Book();
        for (var column = 0; column < 30; column++)
            book.Execute(new SetCellValueCommand("Data", new CellRef(column, 100), CellValue.FromNumber(column)));

        var controller = new SpreadsheetController(book, book.Sheets[0]);
        controller.PageLayout.ShouldBeNull();

        controller.ViewMode = SheetViewMode.PageBreakPreview;
        var layout = controller.PageLayout.ShouldNotBeNull();

        layout.ColumnStarts[0].ShouldBe(0);
        layout.ColumnStarts.Count.ShouldBeGreaterThan(1);
        layout.RowStarts.Count.ShouldBeGreaterThan(1);
        layout.Pages().Count().ShouldBe(layout.PageCount);
        layout.Range.Right.ShouldBe(29);
    }

    [Fact]
    public void Notes_ListEveryNoteInBookOrder_AndGoToSwitchesSheet()
    {
        using var book = Book();
        book.Execute(new AddSheetCommand("Other", 1));
        book.Execute(new SetNoteCommand("Other", CellRef.Parse("C5"), new CellNote(CellRef.Parse("C5"), "second", "Ann")));
        book.Execute(new SetNoteCommand("Data", CellRef.Parse("B2"), new CellNote(CellRef.Parse("B2"), "first", "Bob")));

        var notes = SpreadsheetShell.Notes(book);
        notes.Select(x => x.Address).ShouldBe(["Data!B2", "Other!C5"]);

        var controller = new SpreadsheetController(book, book.Sheets[0]);
        SpreadsheetShell.GoTo(controller, notes[1]);
        controller.Sheet.Name.ShouldBe("Other");
        controller.Selection.Active.ShouldBe(CellRef.Parse("C5"));
    }

    [Fact]
    public void Search_FindsAcrossSheets()
    {
        using var book = Book();
        book.Execute(new AddSheetCommand("Other", 1));
        book.Execute(new SetCellValueCommand("Other", CellRef.Parse("D4"), CellValue.FromText("needle")));

        var controller = new SpreadsheetController(book, book.Sheets[0]);
        SpreadsheetShell.Search(controller, "needle").ShouldBeTrue();
        controller.Sheet.Name.ShouldBe("Other");
        controller.Selection.Active.ShouldBe(CellRef.Parse("D4"));

        SpreadsheetShell.Search(controller, "absent").ShouldBeFalse();
    }

    [Fact]
    public void DocumentInfo_CountsSheetsCellsFormulas()
    {
        using var book = Book();
        var info = SpreadsheetShell.DocumentInfo(book, "Book1", En);

        info.Title.ShouldBe("Book1");
        info.Statistics.Single(x => x.Name == "Sheets").Value.ShouldBe("1");
        info.Statistics.Single(x => x.Name == "Cells with data").Value.ShouldBe("5");
        info.Statistics.Single(x => x.Name == "Formulas").Value.ShouldBe("1");
    }

    [Fact]
    public void Csv_QuotesAndWritesComputedValues()
    {
        using var book = Book();
        var csv = SpreadsheetShell.ToCsv(book, book.Sheets[0]);

        csv.ShouldBe("\"Name, first\",4\r\n\"say \"\"hi\"\"\",8\r\n,12\r\n");
    }

    [Theory]
    [InlineData("blank")]
    [InlineData("budget")]
    [InlineData("invoice")]
    [InlineData("schedule")]
    public void Templates_BuildWithNothingToUndo(string id)
    {
        var template = SpreadsheetTemplates.All.Single(x => x.Id == id);
        using var book = SpreadsheetTemplates.Create(template);

        book.Undo.CanUndo.ShouldBeFalse();
        book.Sheets.Count.ShouldBe(1);

        if (!template.IsBlank)
            book.Sheets[0].UsedRange.ShouldNotBeNull();
    }

    [Fact]
    public void Budget_TotalsItsColumns()
    {
        using var book = SpreadsheetTemplates.Create(SpreadsheetTemplates.Budget);
        var sheet = book.Sheets[0];

        // Planned total, B11.
        sheet.GetDisplayValue(CellRef.Parse("B11")).AsNumber().ShouldBe(3340);
    }

    [Fact]
    public async Task Export_WritesXlsxCsvAndPdf()
    {
        using var book = Book();
        var sheet = book.Sheets[0];

        var xlsx = await SpreadsheetExport.ToBytesAsync(book, sheet, OfficeFileFormats.Xlsx);
        Encoding.ASCII.GetString(xlsx, 0, 2).ShouldBe("PK");

        var csv = await SpreadsheetExport.ToBytesAsync(book, sheet, OfficeFileFormats.Csv);
        csv.Take(3).ShouldBe(Encoding.UTF8.GetPreamble());

        var pdf = await SpreadsheetExport.ToBytesAsync(book, sheet, OfficeFileFormats.Pdf);
        Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");

        SpreadsheetExport.CanWrite(OfficeFileFormats.Png).ShouldBeFalse();
    }

    [Fact]
    public async Task Pdf_EmptySheetIsStillAValidDocument()
    {
        using var book = Workbook.Create();
        var pdf = await SpreadsheetExport.ToBytesAsync(book, book.Sheets[0], OfficeFileFormats.Pdf);
        Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
    }
}
