using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Spreadsheet.Calc;
using Shiny.Controls.Office.Spreadsheet.View;
using Shouldly;
using Xunit;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// XLOOKUP and XMATCH, SUBTOTAL and AGGREGATE, the *IFS aggregates, the financial functions, and the
/// catalogue that describes them to the Insert Function dialog and to autocomplete.
/// </summary>
/// <remarks>Expected values are Excel's own, checked against Excel for the financial ones.</remarks>
public class ModernFunctionTests
{
    static CalcTestGrid Lookup()
    {
        var grid = new CalcTestGrid();
        grid.Set("A1", "apple").Set("A2", "banana").Set("A3", "cherry").Set("A4", "banana");
        grid.Set("B1", 1d).Set("B2", 2d).Set("B3", 3d).Set("B4", 4d);
        grid.Set("D1", 10d).Set("D2", 20d).Set("D3", 30d).Set("D4", 40d);
        return grid;
    }

    [Theory]
    [InlineData("XLOOKUP(\"banana\",A1:A4,B1:B4)", 2)]
    [InlineData("XLOOKUP(\"banana\",A1:A4,B1:B4,0,0,-1)", 4)]
    [InlineData("XLOOKUP(25,D1:D4,B1:B4,0,-1)", 2)]
    [InlineData("XLOOKUP(25,D1:D4,B1:B4,0,1)", 3)]
    [InlineData("XLOOKUP(\"ch*\",A1:A4,B1:B4,0,2)", 3)]
    [InlineData("XMATCH(\"cherry\",A1:A4)", 3)]
    [InlineData("XMATCH(35,D1:D4,1)", 4)]
    [InlineData("XMATCH(35,D1:D4,-1)", 3)]
    public void XLookupAndXMatch(string formula, double expected)
        => Lookup().Number(formula).ShouldBe(expected);

    [Fact]
    public void XLookupNotFoundUsesItsFallbackOrNA()
    {
        var grid = Lookup();
        grid.Text("XLOOKUP(\"kiwi\",A1:A4,B1:B4,\"none\")").ShouldBe("none");
        grid.Error("XLOOKUP(\"kiwi\",A1:A4,B1:B4)").ShouldBe(CellError.NotAvailable);
    }

    [Theory]
    [InlineData("AVERAGEIFS(D1:D4,A1:A4,\"banana\")", 30)]
    [InlineData("MAXIFS(D1:D4,A1:A4,\"banana\")", 40)]
    [InlineData("MINIFS(D1:D4,B1:B4,\">1\")", 20)]
    [InlineData("SUBTOTAL(9,D1:D4)", 100)]
    [InlineData("SUBTOTAL(1,D1:D4)", 25)]
    [InlineData("SUBTOTAL(4,D1:D4)", 40)]
    [InlineData("AGGREGATE(9,6,D1:D4)", 100)]
    [InlineData("AGGREGATE(14,6,D1:D4,2)", 30)]
    [InlineData("AGGREGATE(12,6,D1:D4)", 25)]
    [InlineData("STDEV.S(B1:B4)", 1.2909944487358056)]
    public void AggregatesAndIfs(string formula, double expected)
        => Lookup().Number(formula).ShouldBe(expected, 1e-9);

    [Fact]
    public void AggregateCanIgnoreErrors()
    {
        var grid = Lookup().SetError("D5", CellError.Div0);
        grid.Error("SUM(D1:D5)").ShouldBe(CellError.Div0);
        grid.Number("AGGREGATE(9,6,D1:D5)").ShouldBe(100);
    }

    [Theory]
    [InlineData("PMT(0.05/12,360,200000)", -1073.6432460242781)]
    [InlineData("PMT(0,12,1200)", -100)]
    [InlineData("FV(0.06/12,10,-200,-500,1)", 2581.4033740601)]
    [InlineData("PV(0.08/12,240,500)", -59777.14585118)]
    [InlineData("NPER(0.12/12,-100,-1000,10000,1)", 59.6738656742946)]
    [InlineData("RATE(48,-200,8000)", 0.0077014724882)]
    [InlineData("NPV(0.1,-10000,3000,4200,6800)", 1188.44341233535)]
    [InlineData("IPMT(0.1/12,1,36,8000)", -66.6666666666667)]
    [InlineData("PPMT(0.1/12,1,24,2000)", -75.6231860083)]
    [InlineData("SLN(30000,7500,10)", 2250)]
    public void FinancialFunctionsMatchExcel(string formula, double expected)
        => new CalcTestGrid().Number(formula).ShouldBe(expected, Math.Abs(expected) * 1e-7 + 1e-9);

    [Fact]
    public void IrrConverges()
    {
        var grid = new CalcTestGrid();
        grid.Set("A1", -70000d).Set("A2", 12000d).Set("A3", 15000d).Set("A4", 18000d).Set("A5", 21000d).Set("A6", 26000d);
        grid.Number("IRR(A1:A6)").ShouldBe(0.0866309480, 1e-8);
        grid.Error("IRR(A2:A6)").ShouldBe(CellError.Num);
    }

    [Fact]
    public void SubtotalSkipsOtherSubtotalsAndHiddenRowsWhenAsked()
    {
        using var workbook = Workbook.Create("Data");
        var controller = new SpreadsheetController(workbook, workbook["Data"]);

        controller.SetCellText(CellRef.Parse("A1"), "10");
        controller.SetCellText(CellRef.Parse("A2"), "20");
        controller.SetCellText(CellRef.Parse("A3"), "=SUBTOTAL(9,A1:A2)");
        controller.SetCellText(CellRef.Parse("A4"), "5");
        controller.SetCellText(CellRef.Parse("A5"), "=SUBTOTAL(9,A1:A4)");
        controller.SetCellText(CellRef.Parse("A6"), "=SUBTOTAL(109,A1:A4)");

        workbook["Data"].GetDisplayValue(CellRef.Parse("A5")).AsNumber().ShouldBe(35);

        controller.Selection.SelectRange(CellRange.Parse("A2"));
        controller.SetRowsHidden(true);
        controller.CalculateNow();

        workbook["Data"].GetDisplayValue(CellRef.Parse("A5")).AsNumber().ShouldBe(35);
        workbook["Data"].GetDisplayValue(CellRef.Parse("A6")).AsNumber().ShouldBe(15);
    }

    [Fact]
    public void EveryRegisteredFunctionIsCatalogued()
    {
        var missing = FunctionRegistry.Default.Names.Where(x => FunctionCatalog.Find(x) is null).ToList();
        missing.ShouldBeEmpty();

        FunctionCatalog.All.Where(x => !FunctionRegistry.Default.Contains(x.Name)).ShouldBeEmpty();
        FunctionCatalog.Categories.ShouldAllBe(x => FunctionCatalog.InCategory(x).Count > 0);
        FunctionRegistry.Default.Count.ShouldBeGreaterThanOrEqualTo(135);
    }

    [Fact]
    public void AutocompleteSuggestsFunctionsAndNamesAndTracksTheArgument()
    {
        var state = FormulaAssist.Analyze("=SU", 3, ["Sales"]);
        state.Suggestions.Select(x => x.Name).ShouldContain("SUM");
        state.Suggestions.Select(x => x.Name).ShouldContain("SUBTOTAL");
        state.Token.ShouldBe("SU");

        var names = FormulaAssist.Analyze("=SUM(Sa", 7, ["Sales"]);
        names.Suggestions.First().Name.ShouldBe("Sales");
        names.ActiveFunction!.Name.ShouldBe("SUM");

        var (text, caret) = FormulaAssist.Accept("=SU", state, state.Suggestions.First(x => x.Name == "SUM"));
        text.ShouldBe("=SUM(");
        caret.ShouldBe(5);

        var tip = FormulaAssist.Analyze("=IF(A1>0,\"a,b\",", 16);
        tip.ActiveFunction!.Name.ShouldBe("IF");
        tip.ArgumentIndex.ShouldBe(2);

        FormulaAssist.Analyze("Sales", 5).HasSuggestions.ShouldBeFalse();
        FormulaAssist.Analyze("=\"SU", 4).HasSuggestions.ShouldBeFalse();
        FormulaAssist.Analyze("=A1+B", 5).Suggestions.ShouldAllBe(x => x.Name.StartsWith('B'));
    }
}
