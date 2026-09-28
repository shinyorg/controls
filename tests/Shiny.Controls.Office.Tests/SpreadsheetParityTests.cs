using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Shiny.Controls.Office.Skia;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Spreadsheet.Calc;
using Shiny.Controls.Office.Spreadsheet.Commands;
using Shiny.Controls.Office.Spreadsheet.View;
using Shouldly;
using SkiaSharp;
using Xunit;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// The Excel-parity features: each one edited through the controller, saved, reopened, and checked for
/// what came back — and, for the ones that write new parts or elements, checked against the schema.
/// </summary>
/// <remarks>
/// The reopen is the point. Every one of these features is a place where the model and the file can
/// disagree silently: a merge the grid draws but the file never recorded, a filter whose hidden rows
/// were never written, a note with its text but no VML shape for Excel to show it in.
/// </remarks>
public class SpreadsheetParityTests
{
    static SpreadsheetController Setup(out Workbook workbook, params (string Cell, string Text)[] cells)
    {
        workbook = Workbook.Create("Data");
        var controller = new SpreadsheetController(workbook, workbook["Data"]);
        controller.Resize(800, 600);

        foreach (var (cell, text) in cells)
            controller.SetCellText(CellRef.Parse(cell), text);

        return controller;
    }

    static async Task<Workbook> ReopenAsync(Workbook workbook)
        => await Workbook.OpenAsync(new MemoryStream(workbook.ToArray()));

    static void Select(SpreadsheetController controller, string range)
        => controller.Selection.SelectRange(CellRange.Parse(range));

    static string Text(Worksheet sheet, string cell)
    {
        var value = sheet.GetDisplayValue(CellRef.Parse(cell));
        return value.IsBlank ? string.Empty : Coercion.ToText(value);
    }

    /// <summary>Schema errors in a saved workbook. Excel refuses — or "repairs" — a file with any.</summary>
    static IReadOnlyList<string> SchemaErrors(byte[] bytes)
    {
        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        return new OpenXmlValidator(FileFormatVersions.Office2019)
            .Validate(document)
            .Select(x => $"{x.Part?.Uri} {x.Path?.XPath}: {x.Description}")
            .ToList();
    }

    static readonly (string, string)[] Sales =
    [
        ("A1", "Region"), ("B1", "Units"), ("C1", "Price"),
        ("A2", "North"), ("B2", "30"), ("C2", "2"),
        ("A3", "South"), ("B3", "10"), ("C3", "5"),
        ("A4", "East"), ("B4", "20"), ("C4", "3"),
        ("A5", "West"), ("B5", "40"), ("C5", "1")
    ];

    // ---- merges ----

    [Fact]
    public async Task MergeAndCenterWritesTheMergeKeepsTheAnchorAndSurvivesAReopen()
    {
        var controller = Setup(out var workbook, ("B2", "Title"), ("C2", "lost"), ("D2", "also lost"));
        using var _ = workbook;

        Select(controller, "B2:D2");
        controller.MergeCells(MergeMode.MergeAndCenter);

        var sheet = controller.Sheet;
        sheet.MergedRanges.ShouldBe([CellRange.Parse("B2:D2")]);
        Text(sheet, "B2").ShouldBe("Title");
        Text(sheet, "C2").ShouldBeEmpty();
        controller.IsActiveCellMerged.ShouldBeTrue();

        using var reopened = await ReopenAsync(workbook);
        reopened["Data"].MergedRanges.ShouldBe([CellRange.Parse("B2:D2")]);
        reopened.Styles.Resolve(reopened["Data"].GetEffectiveStyleIndex(CellRef.Parse("B2"))).HorizontalAlignment.ShouldBe(CellHorizontalAlignment.Center);
        SchemaErrors(workbook.ToArray()).ShouldBeEmpty();

        controller.Undo();
        sheet.MergedRanges.ShouldBeEmpty();
        Text(sheet, "C2").ShouldBe("lost");
    }

    [Fact]
    public void MergeAcrossMergesEachRowAndUnmergeTakesThemAllApart()
    {
        var controller = Setup(out var workbook);
        using var _ = workbook;

        Select(controller, "A1:C3");
        controller.MergeCells(MergeMode.MergeAcross);
        controller.Sheet.MergedRanges.Count.ShouldBe(3);

        Select(controller, "B2");
        controller.UnmergeCells();
        controller.Sheet.MergedRanges.Count.ShouldBe(2);
        controller.Sheet.MergedRanges.ShouldNotContain(CellRange.Parse("A2:C2"));
    }

    // ---- freeze panes ----

    [Fact]
    public async Task FreezeTopRowIsWrittenAsAFrozenPaneAndReadBack()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        controller.FreezeTopRow();
        controller.Metrics.FrozenPane.ShouldBe(new CellRef(0, 1));
        controller.HasFrozenPanes.ShouldBeTrue();

        var bytes = workbook.ToArray();
        SchemaErrors(bytes).ShouldBeEmpty();

        using var reopened = await Workbook.OpenAsync(new MemoryStream(bytes));
        reopened["Data"].FrozenPane.ShouldBe(new CellRef(0, 1));

        controller.UnfreezePanes();
        controller.Sheet.FrozenPane.ShouldBeNull();
        controller.Metrics.FrozenPane.ShouldBe(default);

        controller.Undo();
        controller.Metrics.FrozenPane.ShouldBe(new CellRef(0, 1));
    }

    [Fact]
    public void FreezePanesAtACellFreezesTheRowsAboveAndColumnsLeftOfIt()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        controller.GoTo(CellRef.Parse("C3"));
        controller.FreezePanes();
        controller.Sheet.FrozenPane.ShouldBe(CellRef.Parse("C3"));
        SchemaErrors(workbook.ToArray()).ShouldBeEmpty();
    }

    // ---- borders ----

    [Fact]
    public async Task OutsideBordersGiveEachEdgeCellItsOwnEdges()
    {
        var controller = Setup(out var workbook);
        using var _ = workbook;

        Select(controller, "B2:C3");
        controller.ApplyBorders(BorderPreset.Outside);

        var styles = workbook.Styles;
        BorderEdge? Edge(string cell, Func<CellBorders, BorderEdge?> pick)
            => pick(styles.Resolve(controller.Sheet.GetEffectiveStyleIndex(CellRef.Parse(cell))).Borders);

        Edge("B2", x => x.Top).ShouldNotBeNull();
        Edge("B2", x => x.Left).ShouldNotBeNull();
        Edge("B2", x => x.Right).ShouldBeNull();
        Edge("C3", x => x.Bottom).ShouldNotBeNull();
        Edge("C3", x => x.Right).ShouldNotBeNull();

        var bytes = workbook.ToArray();
        SchemaErrors(bytes).ShouldBeEmpty();

        using var reopened = await Workbook.OpenAsync(new MemoryStream(bytes));
        var format = reopened.Styles.Resolve(reopened["Data"].GetEffectiveStyleIndex(CellRef.Parse("B2")));
        format.Borders.Top!.Value.Style.ShouldBe(CellBorderStyle.Thin);
        format.Borders.Left!.Value.Style.ShouldBe(CellBorderStyle.Thin);
    }

    [Fact]
    public void ThickOutsideAndNoBorderAreOneUndoStepEach()
    {
        var controller = Setup(out var workbook);
        using var _ = workbook;

        Select(controller, "A1:B2");
        controller.ApplyBorders(BorderPreset.ThickOutside);
        workbook.Styles.Resolve(controller.Sheet.GetEffectiveStyleIndex(CellRef.Parse("A1"))).Borders.Top!.Value.Style.ShouldBe(CellBorderStyle.Thick);

        controller.ApplyBorders(BorderPreset.None);
        workbook.Styles.Resolve(controller.Sheet.GetEffectiveStyleIndex(CellRef.Parse("A1"))).Borders.IsEmpty.ShouldBeTrue();

        controller.Undo();
        workbook.Styles.Resolve(controller.Sheet.GetEffectiveStyleIndex(CellRef.Parse("A1"))).Borders.Top!.Value.Style.ShouldBe(CellBorderStyle.Thick);
    }

    [Fact]
    public void BordersAreInterned()
    {
        var controller = Setup(out var workbook);
        using var _ = workbook;

        Select(controller, "A1:D10");
        controller.ApplyBorders(BorderPreset.All);

        // Forty cells, one border entry: the default plus the one they all share.
        var bytes = workbook.ToArray();
        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        document.WorkbookPart!.WorkbookStylesPart!.Stylesheet!.Borders!.Elements<DocumentFormat.OpenXml.Spreadsheet.Border>().Count().ShouldBe(2);
    }

    // ---- sort ----

    [Fact]
    public void SortDetectsTheHeaderAndMovesWholeRows()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        controller.GoTo(CellRef.Parse("B3"));
        controller.SortAscending();

        var sheet = controller.Sheet;
        Text(sheet, "A1").ShouldBe("Region");
        Text(sheet, "A2").ShouldBe("South");
        Text(sheet, "B2").ShouldBe("10");
        Text(sheet, "C2").ShouldBe("5");
        Text(sheet, "A5").ShouldBe("West");

        controller.SortDescending();
        Text(sheet, "A2").ShouldBe("West");

        controller.Undo();
        controller.Undo();
        Text(sheet, "A2").ShouldBe("North");
    }

    [Fact]
    public void SortingRebasesAFormulaThatMovesWithItsRow()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        controller.SetCellText(CellRef.Parse("D1"), "Total");
        foreach (var row in new[] { 2, 3, 4, 5 })
            controller.SetCellText(CellRef.Parse($"D{row}"), $"=B{row}*C{row}");

        controller.Sort([new SortKey(3, Descending: true)], hasHeader: true, CellRange.Parse("A1:D5"));

        // North: 30 x 2 = 60 is the largest total and lands in row 2, still multiplying its own row.
        var sheet = controller.Sheet;
        Text(sheet, "A2").ShouldBe("North");
        sheet.GetFormula(CellRef.Parse("D2")).ShouldBe("B2*C2");
        Text(sheet, "D2").ShouldBe("60");
    }

    [Fact]
    public void BlanksSortLastEitherWay()
    {
        SheetSort.CompareForSort(CellValue.Blank, CellValue.FromNumber(1), descending: false).ShouldBeGreaterThan(0);
        SheetSort.CompareForSort(CellValue.Blank, CellValue.FromNumber(1), descending: true).ShouldBeGreaterThan(0);
        SheetSort.CompareForSort(CellValue.FromNumber(5), CellValue.FromText("a"), descending: false).ShouldBeLessThan(0);
    }

    // ---- autofilter ----

    [Fact]
    public async Task AFilterHidesTheRowsItRejectsAndBothPartsAreSaved()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        controller.GoTo(CellRef.Parse("A1"));
        controller.ToggleAutoFilter();
        controller.HasAutoFilter.ShouldBeTrue();

        var filter = controller.Sheet.AutoFilter!;
        filter.Range.ShouldBe(CellRange.Parse("A1:C5"));

        controller.ApplyColumnFilter(filter, 0, ColumnFilter.ForValues(0, ["North", "East"]));

        var sheet = controller.Sheet;
        sheet.IsRowHidden(1).ShouldBeFalse();
        sheet.IsRowHidden(2).ShouldBeTrue();
        sheet.IsRowHidden(3).ShouldBeFalse();
        sheet.IsRowHidden(4).ShouldBeTrue();
        controller.Metrics.Rows.IsHidden(2).ShouldBeTrue();

        var bytes = workbook.ToArray();
        SchemaErrors(bytes).ShouldBeEmpty();

        using var reopened = await Workbook.OpenAsync(new MemoryStream(bytes));
        var read = reopened["Data"].AutoFilter!;
        read.For(0)!.Values!.ShouldBe(["East", "North"], ignoreOrder: true);
        reopened["Data"].IsRowHidden(2).ShouldBeTrue();

        controller.ClearFilters();
        sheet.IsRowHidden(2).ShouldBeFalse();
        controller.HasAutoFilter.ShouldBeTrue();

        controller.ToggleAutoFilter();
        controller.HasAutoFilter.ShouldBeFalse();
    }

    [Fact]
    public void ACustomNumberFilterKeepsWhatItCompares()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        controller.ToggleAutoFilter();
        var filter = controller.Sheet.AutoFilter!;
        controller.ApplyColumnFilter(filter, 1, ColumnFilter.ForCondition(1, new FilterCondition(FilterOperator.GreaterThan, "15")));

        var sheet = controller.Sheet;
        sheet.IsRowHidden(1).ShouldBeFalse();
        sheet.IsRowHidden(2).ShouldBeTrue();
        sheet.IsRowHidden(4).ShouldBeFalse();

        // Round-tripped through its own XML: "contains" is stored as a wildcard and read back as contains.
        var contains = new SheetAutoFilter(filter.Range, [ColumnFilter.ForCondition(0, new FilterCondition(FilterOperator.Contains, "or"))]);
        SheetAutoFilter.Parse(contains.ToXml())!.For(0)!.First!.Operator.ShouldBe(FilterOperator.Contains);
    }

    [Fact]
    public void TheStatusBarSumsOnlyTheRowsAFilterLeft()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        controller.ToggleAutoFilter();
        controller.ApplyColumnFilter(controller.Sheet.AutoFilter!, 0, ColumnFilter.ForValues(0, ["North", "West"]));

        Select(controller, "B2:B5");
        controller.SelectionStatistics.Sum.ShouldBe(70);
    }

    // ---- fill ----

    [Fact]
    public void TheFillHandleExtendsANumberSeries()
    {
        var controller = Setup(out var workbook, ("A1", "1"), ("A2", "3"));
        using var _ = workbook;

        Select(controller, "A1:A2");
        controller.AutoFillTo(CellRange.Parse("A1:A5"));

        Text(controller.Sheet, "A3").ShouldBe("5");
        Text(controller.Sheet, "A5").ShouldBe("9");
        controller.Selection.Range.ShouldBe(CellRange.Parse("A1:A5"));
    }

    [Fact]
    public void TheFillHandleContinuesWeekdaysAndNumberedText()
    {
        var controller = Setup(out var workbook, ("A1", "Mon"), ("B1", "Item 9"), ("C1", "January"));
        using var _ = workbook;

        Select(controller, "A1:C1");
        controller.AutoFillTo(CellRange.Parse("A1:C3"));

        Text(controller.Sheet, "A2").ShouldBe("Tue");
        Text(controller.Sheet, "B3").ShouldBe("Item 11");
        Text(controller.Sheet, "C3").ShouldBe("March");
    }

    [Fact]
    public void FillingAFormulaRebasesIt()
    {
        var controller = Setup(out var workbook, ("A1", "2"), ("A2", "3"), ("B1", "=A1*10"));
        using var _ = workbook;

        Select(controller, "B1");
        controller.AutoFillTo(CellRange.Parse("B1:B2"));

        controller.Sheet.GetFormula(CellRef.Parse("B2")).ShouldBe("A2*10");
        Text(controller.Sheet, "B2").ShouldBe("30");
    }

    [Fact]
    public void ASingleNumberRepeatsButASingleDateCounts()
    {
        var controller = Setup(out var workbook, ("A1", "7"));
        using var _ = workbook;

        Select(controller, "A1");
        controller.AutoFillTo(CellRange.Parse("A1:A3"));
        Text(controller.Sheet, "A3").ShouldBe("7");

        controller.SetCellText(CellRef.Parse("B1"), "45000");
        Select(controller, "B1");
        controller.SetNumberFormat(NumberFormatPreset.ShortDate);
        controller.AutoFillTo(CellRange.Parse("B1:B3"));
        controller.Sheet.GetValue(CellRef.Parse("B3")).AsNumber().ShouldBe(45002);
    }

    [Fact]
    public void FillDownAndFillRightCopyTheFirstRowAndColumn()
    {
        var controller = Setup(out var workbook, ("A1", "x"), ("B1", "=A1&\"!\""));
        using var _ = workbook;

        Select(controller, "A1:B3");
        controller.HandleKey("d", SheetKeyModifiers.Control).ShouldBeTrue();

        Text(controller.Sheet, "A3").ShouldBe("x");
        controller.Sheet.GetFormula(CellRef.Parse("B3")).ShouldBe("A3&\"!\"");

        controller.SetCellText(CellRef.Parse("D1"), "7");
        Select(controller, "D1:F1");
        controller.FillRight();
        Text(controller.Sheet, "F1").ShouldBe("7");
    }

    // ---- conditional formatting ----

    [Fact]
    public async Task AGreaterThanRuleHighlightsMatchesAndRoundTripsWithItsDxf()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        Select(controller, "B2:B5");
        controller.AddConditionalFormat(ConditionalFormatRule.CellIs(ConditionalOperator.GreaterThan, DxfFormat.LightRedFill, "25"));

        var evaluator = controller.Sheet.ConditionalEvaluator;
        evaluator.Evaluate(CellRef.Parse("B2")).Overlay!.Background.ShouldBe(DxfFormat.LightRedFill.Background);
        evaluator.Evaluate(CellRef.Parse("B3")).IsEmpty.ShouldBeTrue();

        var bytes = workbook.ToArray();
        SchemaErrors(bytes).ShouldBeEmpty();

        using var reopened = await Workbook.OpenAsync(new MemoryStream(bytes));
        var rule = reopened["Data"].ConditionalFormats.Single().Rules.Single();
        rule.Type.ShouldBe(ConditionalRuleType.CellIs);
        rule.Operator.ShouldBe(ConditionalOperator.GreaterThan);
        rule.Format!.Background.ShouldBe(DxfFormat.LightRedFill.Background);
        reopened["Data"].ConditionalEvaluator.Evaluate(CellRef.Parse("B5")).Overlay.ShouldNotBeNull();
    }

    [Fact]
    public void TopBottomDuplicatesAndAverageRulesEvaluate()
    {
        var controller = Setup(out var workbook, ("A1", "1"), ("A2", "5"), ("A3", "5"), ("A4", "9"), ("A5", "2"));
        using var _ = workbook;

        Select(controller, "A1:A5");
        controller.AddConditionalFormat(ConditionalFormatRule.TopBottom(1, percent: false, bottom: false, DxfFormat.GreenFill));
        var evaluator = controller.Sheet.ConditionalEvaluator;
        evaluator.Evaluate(CellRef.Parse("A4")).Overlay.ShouldNotBeNull();
        evaluator.Evaluate(CellRef.Parse("A2")).Overlay.ShouldBeNull();

        controller.ClearConditionalFormats();
        controller.AddConditionalFormat(ConditionalFormatRule.Duplicates(DxfFormat.YellowFill));
        controller.Sheet.ConditionalEvaluator.Evaluate(CellRef.Parse("A2")).Overlay.ShouldNotBeNull();
        controller.Sheet.ConditionalEvaluator.Evaluate(CellRef.Parse("A1")).Overlay.ShouldBeNull();

        controller.ClearConditionalFormats(entireSheet: true);
        controller.AddConditionalFormat(ConditionalFormatRule.Average(true, DxfFormat.RedText));
        controller.Sheet.ConditionalEvaluator.Evaluate(CellRef.Parse("A4")).Overlay.ShouldNotBeNull();
        controller.Sheet.ConditionalEvaluator.Evaluate(CellRef.Parse("A1")).Overlay.ShouldBeNull();
    }

    [Fact]
    public async Task DataBarsAndColorScalesAreWrittenAndScaleWithTheRange()
    {
        var controller = Setup(out var workbook, ("A1", "0"), ("A2", "50"), ("A3", "100"));
        using var _ = workbook;

        Select(controller, "A1:A3");
        controller.AddConditionalFormat(ConditionalFormatRule.Bar(ConditionalPresets.DataBars[0].Color));
        controller.AddConditionalFormat(ConditionalPresets.ColorScales[0].Rule);

        var evaluator = controller.Sheet.ConditionalEvaluator;
        evaluator.Evaluate(CellRef.Parse("A3")).Bar!.Value.Fraction.ShouldBe(1, 0.001);
        evaluator.Evaluate(CellRef.Parse("A1")).Bar!.Value.Fraction.ShouldBeLessThan(evaluator.Evaluate(CellRef.Parse("A2")).Bar!.Value.Fraction);
        evaluator.Evaluate(CellRef.Parse("A1")).ScaleFill.ShouldNotBeNull();
        evaluator.Evaluate(CellRef.Parse("A1")).ScaleFill.ShouldNotBe(evaluator.Evaluate(CellRef.Parse("A3")).ScaleFill);

        var bytes = workbook.ToArray();
        SchemaErrors(bytes).ShouldBeEmpty();

        using var reopened = await Workbook.OpenAsync(new MemoryStream(bytes));
        var rules = reopened["Data"].ConditionalFormats.SelectMany(x => x.Rules).ToList();
        rules.ShouldContain(x => x.Type == ConditionalRuleType.DataBar);
        rules.ShouldContain(x => x.Type == ConditionalRuleType.ColorScale && x.Scale.Count == 3);

        // The rule added last is at the top of the priority order.
        rules.Single(x => x.Type == ConditionalRuleType.ColorScale).Priority.ShouldBe(1);
    }

    [Fact]
    public void ClearingRulesFromSelectedCellsCutsTheSelectionOutOfTheRange()
    {
        var controller = Setup(out var workbook);
        using var _ = workbook;

        Select(controller, "A1:A10");
        controller.AddConditionalFormat(ConditionalFormatRule.Average(true, DxfFormat.RedText));

        Select(controller, "A4:A6");
        controller.ClearConditionalFormats();

        controller.Sheet.ConditionalFormats.Single().Ranges.ShouldBe([CellRange.Parse("A1:A3"), CellRange.Parse("A7:A10")]);
    }

    // ---- data validation ----

    [Fact]
    public async Task AListRuleRefusesAnythingOffTheListAndRoundTrips()
    {
        var controller = Setup(out var workbook);
        using var _ = workbook;

        var dialogs = new List<SheetDialog>();
        controller.DialogRequested += (_, d) => dialogs.Add(d);

        Select(controller, "A1:A5");
        controller.SetValidation(DataValidationRule.ForList(["Red", "Green", "Blue"]));

        controller.GoTo(CellRef.Parse("A2"));
        controller.ActiveListItems.ShouldBe(["Red", "Green", "Blue"]);

        controller.SetActiveCellText("Purple");
        controller.Sheet.GetValue(CellRef.Parse("A2")).IsBlank.ShouldBeTrue();
        dialogs.Count.ShouldBe(1);

        controller.SetActiveCellText("green");
        Text(controller.Sheet, "A2").ShouldBe("green");

        var bytes = workbook.ToArray();
        SchemaErrors(bytes).ShouldBeEmpty();

        using var reopened = await Workbook.OpenAsync(new MemoryStream(bytes));
        var rule = reopened["Data"].ValidationAt(CellRef.Parse("A4"))!;
        rule.Type.ShouldBe(ValidationType.List);
        rule.InCellDropDown.ShouldBeTrue();
        DataValidation.ListItems(reopened["Data"], rule).ShouldBe(["Red", "Green", "Blue"]);
        reopened["Data"].ValidationAt(CellRef.Parse("B1")).ShouldBeNull();
    }

    [Fact]
    public void WholeNumberAndLengthRulesCompare()
    {
        var controller = Setup(out var workbook, ("Z1", "10"));
        using var _ = workbook;

        Select(controller, "A1");
        controller.SetValidation(DataValidationRule.ForNumber(ValidationType.Whole, ValidationOperator.Between, "1", "$Z$1"));
        DataValidation.Check(controller.Sheet, CellRef.Parse("A1"), CellValue.FromNumber(5)).IsValid.ShouldBeTrue();
        DataValidation.Check(controller.Sheet, CellRef.Parse("A1"), CellValue.FromNumber(5.5)).IsValid.ShouldBeFalse();
        DataValidation.Check(controller.Sheet, CellRef.Parse("A1"), CellValue.FromNumber(11)).IsValid.ShouldBeFalse();

        Select(controller, "B1");
        controller.SetValidation(DataValidationRule.ForNumber(ValidationType.TextLength, ValidationOperator.LessThanOrEqual, "3"));
        DataValidation.Check(controller.Sheet, CellRef.Parse("B1"), CellValue.FromText("abc")).IsValid.ShouldBeTrue();
        DataValidation.Check(controller.Sheet, CellRef.Parse("B1"), CellValue.FromText("abcd")).IsValid.ShouldBeFalse();

        controller.SetValidation(null);
        controller.Sheet.ValidationAt(CellRef.Parse("B1")).ShouldBeNull();
        controller.Sheet.ValidationAt(CellRef.Parse("A1")).ShouldNotBeNull();
    }

    [Fact]
    public void TheShowDropDownFlagIsStoredTheSchemasWayRound()
    {
        var hidden = DataValidationRule.ForList(["a"]) with { Ranges = [CellRange.Parse("A1")], InCellDropDown = false };
        DataValidation.ToXml([hidden]).Single().ShouldContain("showDropDown=\"1\"");
        DataValidation.Parse(DataValidation.ToXml([hidden]).Single()).Single().InCellDropDown.ShouldBeFalse();
    }

    // ---- cell styles and tables ----

    [Fact]
    public void CellStylesApplyTheirFormatting()
    {
        var controller = Setup(out var workbook, ("A1", "ok"));
        using var _ = workbook;

        controller.ApplyCellStyle(CellStylePresets.Find("Good")!);
        var format = controller.ActiveFormat;
        format.Background.ShouldBe(ArgbColor.FromUInt32(0xFFC6EFCE));
        format.Foreground.ShouldBe(ArgbColor.FromUInt32(0xFF006100));

        controller.ApplyCellStyle(CellStylePresets.Find("Heading 1")!);
        controller.ActiveFormat.Bold.ShouldBeTrue();
        controller.ActiveFormat.Background.IsTransparent.ShouldBeTrue();
        controller.ActiveFormat.Borders.Bottom!.Value.Style.ShouldBe(CellBorderStyle.Thick);
    }

    [Fact]
    public async Task FormatAsTableWritesATablePartThatExcelAccepts()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        controller.GoTo(CellRef.Parse("B3"));
        controller.FormatAsTable("TableStyleMedium2");

        var table = controller.Sheet.Tables.Single();
        table.Range.ShouldBe(CellRange.Parse("A1:C5"));
        table.ColumnNames.ShouldBe(["Region", "Units", "Price"]);
        controller.ActiveTable.ShouldNotBeNull();
        controller.Sheet.AutoFilters.Single().TableName.ShouldBe(table.Name);

        var bytes = workbook.ToArray();
        SchemaErrors(bytes).ShouldBeEmpty();

        using var reopened = await Workbook.OpenAsync(new MemoryStream(bytes));
        var read = reopened["Data"].Tables.Single();
        read.StyleName.ShouldBe("TableStyleMedium2");
        read.ShowRowStripes.ShouldBeTrue();

        // Filtering the table writes the table's own autoFilter, not the sheet's.
        controller.ApplyColumnFilter(controller.Sheet.AutoFilters.Single(), 0, ColumnFilter.ForValues(0, ["North"]));
        controller.Sheet.AutoFilter.ShouldBeNull();
        controller.Sheet.Tables.Single().AutoFilter!.For(0).ShouldNotBeNull();
        controller.Sheet.IsRowHidden(2).ShouldBeTrue();
        SchemaErrors(workbook.ToArray()).ShouldBeEmpty();

        controller.Undo();
        controller.Undo();
        controller.Sheet.Tables.ShouldBeEmpty();
    }

    [Fact]
    public void AHeaderlessBlockGetsGeneratedColumnNames()
    {
        var controller = Setup(out var workbook, ("A1", "1"), ("B1", "2"), ("A2", "3"), ("B2", "4"));
        using var _ = workbook;

        Select(controller, "A1:B2");
        controller.FormatAsTable("TableStyleLight9", hasHeader: false);

        controller.Sheet.Tables.Single().ColumnNames.ShouldBe(["Column1", "Column2"]);
        Text(controller.Sheet, "A1").ShouldBe("Column1");
        Text(controller.Sheet, "A2").ShouldBe("1");
    }

    // ---- charts ----

    [Fact]
    public async Task AnInsertedChartIsADrawingMlChartThatReopens()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        controller.GoTo(CellRef.Parse("A1"));
        Select(controller, "A1:B5");
        var id = controller.InsertChart(ChartKind.Column, "Units");
        id.ShouldNotBeNull();
        controller.SelectedChartId.ShouldBe(id);

        var chart = controller.Sheet.Charts.Single();
        chart.Kind.ShouldBe(ChartKind.Column);
        chart.Title.ShouldBe("Units");
        chart.Series.Single().ValuesFormula.ShouldBe("Data!$B$2:$B$5");
        chart.Series.Single().CategoriesFormula.ShouldBe("Data!$A$2:$A$5");

        var data = SheetCharts.Evaluate(controller.Sheet, chart).Single();
        data.Values.ShouldBe([30d, 10d, 20d, 40d]);
        data.Categories.ShouldBe(["North", "South", "East", "West"]);

        var bytes = workbook.ToArray();
        SchemaErrors(bytes).ShouldBeEmpty();

        using var reopened = await Workbook.OpenAsync(new MemoryStream(bytes));
        var read = reopened["Data"].Charts.Single();
        read.Kind.ShouldBe(ChartKind.Column);
        read.Series.Single().CachedValues.ShouldBe([30d, 10d, 20d, 40d]);
    }

    [Fact]
    public void ChartsMoveDeleteAndComeBackOnUndo()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        Select(controller, "A1:C5");
        var id = controller.InsertChart(ChartKind.Line)!;
        var before = controller.Sheet.ChartById(id)!.Anchor;

        var moved = before with { From = before.From.Offset(1, 1), To = before.To.Offset(1, 1) };
        workbook.Execute(new MoveChartCommand("Data", id, moved));
        controller.Sheet.ChartById(id)!.Anchor.ShouldBe(moved);

        controller.SelectedChartId = id;
        controller.HandleKey("Delete").ShouldBeTrue();
        controller.Sheet.Charts.ShouldBeEmpty();

        controller.Undo();
        controller.Sheet.ChartById(id).ShouldNotBeNull();

        // The move before the delete still undoes against the chart that came back.
        controller.Undo();
        controller.Sheet.ChartById(id)!.Anchor.ShouldBe(before);
    }

    [Fact]
    public void DraggingAChartMovesItAsOneUndoStep()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        Select(controller, "A1:B5");
        var id = controller.InsertChart(ChartKind.Pie)!;
        var anchor = controller.Sheet.ChartById(id)!.Anchor;
        var rect = controller.ChartRect(anchor);

        controller.PointerDown(rect.X + 30, rect.Y + 30);
        controller.PointerMove(rect.X + 30 + 64, rect.Y + 30 + 40);
        controller.ChartPreview.ShouldNotBeNull();
        controller.PointerUp();

        var after = controller.Sheet.ChartById(id)!.Anchor;
        controller.ChartRect(after).X.ShouldBe(rect.X + 64, 0.5);
        controller.ChartRect(after).Y.ShouldBe(rect.Y + 40, 0.5);

        controller.Undo();
        controller.Sheet.ChartById(id)!.Anchor.ShouldBe(anchor);
    }

    [Fact]
    public void ThePainterDrawsChartsNotesFiltersAndDataBarsWithoutFailing()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        controller.ToggleAutoFilter();
        Select(controller, "B2:B5");
        controller.AddConditionalFormat(ConditionalFormatRule.Bar(ConditionalPresets.DataBars[1].Color));
        controller.GoTo(CellRef.Parse("C3"));
        controller.SetNote("Check this price");
        controller.ShowAllNotes = true;

        foreach (var (kind, _) in SheetCharts.Gallery)
        {
            Select(controller, "A1:C5");
            controller.InsertChart(kind);
        }

        using var bitmap = new SKBitmap(800, 600);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SpreadsheetPainter();

        painter.Paint(canvas, SpreadsheetPaintRequest.For(controller, SpreadsheetTheme.Light, 1));
        controller.Zoom = 2;
        painter.Paint(canvas, SpreadsheetPaintRequest.For(controller, SpreadsheetTheme.Dark, 1));
    }

    // ---- notes ----

    [Fact]
    public async Task ANoteIsWrittenWithTheVmlExcelNeedsToShowIt()
    {
        var controller = Setup(out var workbook, ("B2", "5"));
        using var _ = workbook;

        controller.NoteAuthor = "Allan";
        controller.GoTo(CellRef.Parse("B2"));
        controller.SetNote("Double-check this");

        controller.ActiveNote!.Text.ShouldBe("Double-check this");

        var bytes = workbook.ToArray();
        SchemaErrors(bytes).ShouldBeEmpty();

        var entries = PackageComparer.EntryNames(bytes);
        entries.ShouldContain(x => x.StartsWith("xl/comments", StringComparison.Ordinal));
        entries.ShouldContain(x => x.StartsWith("xl/drawings/vmlDrawing", StringComparison.Ordinal) || x.EndsWith(".vml", StringComparison.Ordinal));

        using var reopened = await Workbook.OpenAsync(new MemoryStream(bytes));
        var note = reopened["Data"].NoteAt(CellRef.Parse("B2"))!;
        note.Text.ShouldBe("Double-check this");
        note.Author.ShouldBe("Allan");

        controller.DeleteNote();
        controller.Sheet.Notes.ShouldBeEmpty();
        PackageComparer.EntryNames(workbook.ToArray()).ShouldNotContain(x => x.StartsWith("xl/comments", StringComparison.Ordinal));

        controller.Undo();
        controller.Sheet.NoteAt(CellRef.Parse("B2")).ShouldNotBeNull();
    }

    [Fact]
    public void HoveringACellWithANoteOpensIt()
    {
        var controller = Setup(out var workbook);
        using var _ = workbook;

        controller.GoTo(CellRef.Parse("C3"));
        controller.SetNote("hello");

        var rect = controller.Viewport.CellRect(CellRef.Parse("C3"));
        controller.PointerHover(rect.X + 5, rect.Y + 5);
        controller.VisibleNotes.Single().Text.ShouldBe("hello");

        controller.PointerExit();
        controller.VisibleNotes.ShouldBeEmpty();
    }

    // ---- hyperlinks ----

    [Fact]
    public async Task AnExternalLinkIsARelationshipAndAnInternalOneALocation()
    {
        var controller = Setup(out var workbook);
        using var _ = workbook;

        controller.GoTo(CellRef.Parse("A1"));
        controller.SetHyperlink(new CellHyperlink(CellRef.Parse("A1")) { Address = "https://shinylib.net", Display = "Shiny" });

        controller.GoTo(CellRef.Parse("A2"));
        controller.SetHyperlink(new CellHyperlink(CellRef.Parse("A2")) { Location = "Data!C10" });

        Text(controller.Sheet, "A1").ShouldBe("Shiny");
        controller.Sheet.HyperlinkAt(CellRef.Parse("A1"))!.Address.ShouldStartWith("https://shinylib.net");
        controller.GoTo(CellRef.Parse("A1"));
        controller.ActiveFormat.Underline.ShouldBeTrue();

        var bytes = workbook.ToArray();
        SchemaErrors(bytes).ShouldBeEmpty();

        using var reopened = await Workbook.OpenAsync(new MemoryStream(bytes));
        reopened["Data"].HyperlinkAt(CellRef.Parse("A1"))!.Address.ShouldStartWith("https://shinylib.net");
        reopened["Data"].HyperlinkAt(CellRef.Parse("A2"))!.Location.ShouldBe("Data!C10");

        string? opened = null;
        controller.HyperlinkActivated += (_, url) => opened = url;
        controller.OpenHyperlink().ShouldBeTrue();
        opened.ShouldStartWith("https://shinylib.net");

        controller.GoTo(CellRef.Parse("A2"));
        controller.OpenHyperlink().ShouldBeTrue();
        controller.Selection.Active.ShouldBe(CellRef.Parse("C10"));

        controller.GoTo(CellRef.Parse("A1"));
        controller.SetHyperlink(null);
        controller.Sheet.HyperlinkAt(CellRef.Parse("A1")).ShouldBeNull();
        controller.Sheet.WorksheetPart.HyperlinkRelationships.ShouldBeEmpty();
    }

    // ---- names ----

    [Fact]
    public async Task ADefinedNameCalculatesRecalculatesAndRoundTrips()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        controller.DefineName("Units", "Data!$B$2:$B$5");
        controller.SetCellText(CellRef.Parse("E1"), "=SUM(Units)");
        Text(controller.Sheet, "E1").ShouldBe("100");

        // A cell inside the named range changing reaches the formula through the name.
        controller.SetCellText(CellRef.Parse("B2"), "130");
        Text(controller.Sheet, "E1").ShouldBe("200");

        var bytes = workbook.ToArray();
        SchemaErrors(bytes).ShouldBeEmpty();

        using var reopened = await Workbook.OpenAsync(new MemoryStream(bytes));
        reopened.FindName("Units")!.Formula.ShouldBe("Data!$B$2:$B$5");
        reopened["Data"].GetDisplayValue(CellRef.Parse("E1")).AsNumber().ShouldBe(200);

        controller.DeleteName("Units");
        controller.Sheet.GetDisplayValue(CellRef.Parse("E1")).IsError.ShouldBeTrue();

        controller.Undo();
        Text(controller.Sheet, "E1").ShouldBe("200");
    }

    [Fact]
    public void TheNameBoxGoesToANameAndDefinesANewOne()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        Select(controller, "C2:C5");
        controller.GoTo("Prices").ShouldBeTrue();
        workbook.FindName("Prices")!.Formula.ShouldBe("Data!$C$2:$C$5");

        controller.GoTo(CellRef.Parse("A1"));
        controller.GoTo("Prices").ShouldBeTrue();
        controller.Selection.Range.ShouldBe(CellRange.Parse("C2:C5"));
    }

    [Theory]
    [InlineData("Sales", true)]
    [InlineData("_total", true)]
    [InlineData("A1", false)]
    [InlineData("R1C1", false)]
    [InlineData("1st", false)]
    [InlineData("has space", false)]
    [InlineData("TRUE", false)]
    public void NamesFollowExcelsRules(string name, bool valid)
        => DefinedNameRules.IsValid(name, out _).ShouldBe(valid);

    [Fact]
    public void RenamingANameKeepsWhatItRefersTo()
    {
        var controller = Setup(out var workbook, ("A1", "4"));
        using var _ = workbook;

        controller.DefineName("Old", "Data!$A$1");
        controller.RenameName("Old", null, "New", "Data!$A$1");
        workbook.FindName("Old").ShouldBeNull();
        workbook.FindName("New").ShouldNotBeNull();

        controller.Undo();
        workbook.FindName("Old").ShouldNotBeNull();
        workbook.FindName("New").ShouldBeNull();
    }

    // ---- view, zoom and statistics ----

    [Fact]
    public async Task GridlinesHeadingsAndShowFormulasAreSavedWithTheSheet()
    {
        var controller = Setup(out var workbook, ("A1", "=1+1"));
        using var _ = workbook;

        controller.ShowGridlines = false;
        controller.ShowHeadings = false;
        controller.ToggleShowFormulas();

        controller.Metrics.RowHeaderWidth.ShouldBe(0);
        controller.HandleKey("`", SheetKeyModifiers.Control);
        controller.ShowFormulas.ShouldBeFalse();
        controller.ToggleShowFormulas();

        var bytes = workbook.ToArray();
        SchemaErrors(bytes).ShouldBeEmpty();

        using var reopened = await Workbook.OpenAsync(new MemoryStream(bytes));
        reopened["Data"].ShowGridLines.ShouldBeFalse();
        reopened["Data"].ShowHeadings.ShouldBeFalse();
        reopened["Data"].ShowFormulas.ShouldBeTrue();
    }

    [Fact]
    public void ZoomIsClampedAndPointerCoordinatesAreScaledBack()
    {
        var controller = Setup(out var workbook);
        using var _ = workbook;

        var changes = new List<double>();
        controller.ZoomChanged += (_, z) => changes.Add(z);

        controller.Zoom = 9;
        controller.Zoom.ShouldBe(SpreadsheetController.MaxZoom);
        controller.Zoom = 0.01;
        controller.Zoom.ShouldBe(SpreadsheetController.MinZoom);
        controller.Zoom = 2;
        changes.ShouldBe([4, 0.1, 2]);

        controller.Viewport.Width.ShouldBe(400);

        // At 200% the host's point is twice as far out as the grid's.
        var rect = controller.Viewport.CellRect(CellRef.Parse("C4"));
        controller.PointerDown((rect.X + 5) * 2, (rect.Y + 5) * 2);
        controller.PointerUp();
        controller.Selection.Active.ShouldBe(CellRef.Parse("C4"));

        controller.ZoomIn();
        controller.Zoom.ShouldBe(3);
        controller.ZoomOut();
        controller.Zoom.ShouldBe(2);
    }

    [Fact]
    public void SelectionStatisticsAreExcelsStatusBarNumbers()
    {
        var controller = Setup(out var workbook, ("A1", "2"), ("A2", "4"), ("A3", "text"), ("A4", "9"));
        using var _ = workbook;

        var raised = 0;
        controller.SelectionStatisticsChanged += (_, _) => raised++;

        Select(controller, "A1:A5");
        controller.GoTo(CellRef.Parse("A1"));
        Select(controller, "A1:A5");

        var stats = controller.SelectionStatistics;
        stats.Count.ShouldBe(4);
        stats.NumericalCount.ShouldBe(3);
        stats.Sum.ShouldBe(15);
        stats.Average.ShouldBe(5);
        stats.Min.ShouldBe(2);
        stats.Max.ShouldBe(9);
        stats.IsMeaningful.ShouldBeTrue();
        raised.ShouldBeGreaterThan(0);

        controller.SetCellText(CellRef.Parse("A5"), "5");
        controller.SelectionStatistics.Sum.ShouldBe(20);
    }

    // ---- keyboard ----

    [Fact]
    public void ExcelShortcutsAreHandled()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        controller.GoTo(CellRef.Parse("B2"));
        controller.HandleKey("b", SheetKeyModifiers.Control).ShouldBeTrue();
        controller.ActiveFormat.Bold.ShouldBeTrue();

        controller.HandleKey(" ", SheetKeyModifiers.Control).ShouldBeTrue();
        controller.Selection.Range.ShouldBe(new CellRange(new CellRef(1, 0), new CellRef(1, CellRef.MaxRow)));

        controller.GoTo(CellRef.Parse("B2"));
        controller.HandleKey(" ", SheetKeyModifiers.Shift).ShouldBeTrue();
        controller.Selection.Range.Top.ShouldBe(1);
        controller.Selection.Range.Right.ShouldBe(CellRef.MaxColumn);

        controller.GoTo(CellRef.Parse("B3"));
        controller.HandleKey("a", SheetKeyModifiers.Control);
        controller.Selection.Range.ShouldBe(CellRange.Parse("A1:C5"));
        controller.HandleKey("a", SheetKeyModifiers.Control);
        controller.Selection.Range.Right.ShouldBe(CellRef.MaxColumn);

        controller.GoTo(CellRef.Parse("A1"));
        controller.HandleKey("ArrowDown", SheetKeyModifiers.Control);
        controller.Selection.Active.ShouldBe(CellRef.Parse("A5"));

        controller.GoTo(CellRef.Parse("B6"));
        controller.HandleKey("=", SheetKeyModifiers.Alt).ShouldBeTrue();
        controller.Sheet.GetFormula(CellRef.Parse("B6")).ShouldBe("SUM(B2:B5)");

        controller.GoTo(CellRef.Parse("E1"));
        controller.HandleKey(";", SheetKeyModifiers.Control).ShouldBeTrue();
        controller.Sheet.GetValue(CellRef.Parse("E1")).AsNumber().ShouldBe(ExcelDate.FromDateTime(DateTime.Today));

        SheetDialog? dialog = null;
        controller.DialogRequested += (_, d) => dialog = d;
        controller.HandleKey("1", SheetKeyModifiers.Control).ShouldBeTrue();
        dialog!.Title.ShouldBe("Format Cells");

        controller.HandleKey("l", SheetKeyModifiers.Control | SheetKeyModifiers.Shift).ShouldBeTrue();
        controller.HasAutoFilter.ShouldBeTrue();

        controller.HandleKey("q").ShouldBeTrue();
        controller.EditingText.ShouldBe("q");
    }

    // ---- menus and dialogs ----

    [Fact]
    public void RightClickingOutsideTheSelectionMovesItAndOffersTheCellMenu()
    {
        var controller = Setup(out var workbook);
        using var _ = workbook;

        SheetMenuRequest? menu = null;
        controller.MenuRequested += (_, m) => menu = m;

        var rect = controller.Viewport.CellRect(CellRef.Parse("D4"));
        controller.OpenContextMenu(rect.X + 5, rect.Y + 5).ShouldBeTrue();

        controller.Selection.Active.ShouldBe(CellRef.Parse("D4"));
        menu!.Items.Select(x => x.Text).ShouldContain("Format Cells...");
        menu.Items.Select(x => x.Text).ShouldContain("New Note");

        var header = controller.Viewport.CellRect(CellRef.Parse("B1"));
        controller.OpenContextMenu(header.X + 5, 5).ShouldBeTrue();
        menu.Items.Select(x => x.Text).ShouldContain("Column Width...");
        controller.Selection.Range.Bottom.ShouldBe(CellRef.MaxRow);
    }

    [Fact]
    public void FormatCellsAppliesOnlyWhatWasChanged()
    {
        var controller = Setup(out var workbook, ("A1", "1"), ("A2", "2"));
        using var _ = workbook;

        controller.GoTo(CellRef.Parse("A2"));
        controller.SetTextColor(new ArgbColor(255, 200, 0, 0));

        Select(controller, "A1:A2");
        var dialog = SpreadsheetDialogs.FormatCells(controller);
        dialog["bold"].IsChecked = true;
        dialog.Press(dialog.Primary!).ShouldBeTrue();

        workbook.Styles.Resolve(controller.Sheet.GetEffectiveStyleIndex(CellRef.Parse("A1"))).Bold.ShouldBeTrue();

        // A2's red came through: the dialog did not write the active cell's colour over it.
        var a2 = workbook.Styles.Resolve(controller.Sheet.GetEffectiveStyleIndex(CellRef.Parse("A2")));
        a2.Bold.ShouldBeTrue();
        a2.Foreground.ShouldBe(new ArgbColor(255, 200, 0, 0));
    }

    [Fact]
    public void TheFilterDialogFiltersToWhatIsTicked()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        controller.ToggleAutoFilter();
        var dialog = SpreadsheetDialogs.Filter(controller, controller.Sheet.AutoFilter!, 0);

        var values = dialog["values"];
        values.Items.Select(x => x.Text).ShouldBe(["East", "North", "South", "West"]);

        dialog["all"].IsChecked = false;
        dialog.OnFieldChanged(dialog["all"]);
        values.Items.ShouldAllBe(x => !x.IsChecked);

        values.Items.First(x => x.Text == "West").IsChecked = true;
        dialog.Press(dialog.Primary!).ShouldBeTrue();

        controller.Sheet.IsRowHidden(1).ShouldBeTrue();
        controller.Sheet.IsRowHidden(4).ShouldBeFalse();
    }

    [Fact]
    public void TheNameManagerChainsIntoItsEditor()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        var manager = SpreadsheetDialogs.NameManager(controller);
        manager.Press(manager.Buttons.First(x => x.Text == "New...")).ShouldBeTrue();

        var editor = manager.Next!;
        editor["name"].Text = "Regions";
        editor["refers"].Text = "=Data!$A$2:$A$5";
        editor.Press(editor.Primary!).ShouldBeTrue();
        workbook.FindName("Regions").ShouldNotBeNull();

        editor.Next!.Title.ShouldBe("Name Manager");
        editor.Next["names"].Options.ShouldContain("Regions");

        var duplicate = SpreadsheetDialogs.DefineName(controller, null);
        duplicate["name"].Text = "Regions";
        duplicate.Press(duplicate.Primary!).ShouldBeFalse();
        duplicate.Error.ShouldNotBeNull();
    }

    [Fact]
    public void TheInsertFunctionDialogSearchesAndInserts()
    {
        var controller = Setup(out var workbook);
        using var _ = workbook;

        var dialog = SpreadsheetDialogs.InsertFunction(controller);
        dialog["search"].Text = "loan";
        dialog.OnFieldChanged(dialog["search"]);
        dialog["functions"].Options.ShouldContain("PMT");

        dialog["functions"].SelectedIndex = dialog["functions"].Options.ToList().IndexOf("PMT");
        dialog.Press(dialog.Primary!).ShouldBeTrue();

        controller.EditingText.ShouldBe("=PMT(");
    }

    // ---- structural edits carry the sheet's ranges ----

    [Fact]
    public void InsertingRowsMovesRulesLinksNotesFiltersAndCharts()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        Select(controller, "B2:B5");
        controller.AddConditionalFormat(ConditionalFormatRule.CellIs(ConditionalOperator.GreaterThan, DxfFormat.RedText, "$B$5"));
        controller.SetValidation(DataValidationRule.ForNumber(ValidationType.Whole, ValidationOperator.GreaterThan, "0"));
        controller.GoTo(CellRef.Parse("A3"));
        controller.SetHyperlink(new CellHyperlink(CellRef.Parse("A3")) { Location = "Data!A1" });
        controller.SetNote("south");
        controller.GoTo(CellRef.Parse("A1"));
        controller.ToggleAutoFilter();
        Select(controller, "A1:B5");
        var chart = controller.InsertChart(ChartKind.Column)!;

        Select(controller, "A2");
        controller.InsertRows(2);

        var sheet = controller.Sheet;
        sheet.ConditionalFormats.Single().Ranges.ShouldBe([CellRange.Parse("B4:B7")]);
        sheet.ConditionalFormats.Single().Rules.Single().Formulas.ShouldBe(["$B$7"]);
        sheet.ValidationAt(CellRef.Parse("B7")).ShouldNotBeNull();
        sheet.ValidationAt(CellRef.Parse("B2")).ShouldBeNull();
        sheet.HyperlinkAt(CellRef.Parse("A5")).ShouldNotBeNull();
        sheet.NoteAt(CellRef.Parse("A5"))!.Text.ShouldBe("south");
        sheet.AutoFilter!.Range.ShouldBe(CellRange.Parse("A1:C7"));
        sheet.ChartById(chart)!.Series.Single().ValuesFormula.ShouldBe("Data!$B$4:$B$7");

        SchemaErrors(workbook.ToArray()).ShouldBeEmpty();
    }

    [Fact]
    public void DeletingTheRowsARuleCoveredAndUndoingPutsItBackExactly()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        Select(controller, "B3:B4");
        controller.AddConditionalFormat(ConditionalFormatRule.Average(true, DxfFormat.GreenFill));
        controller.GoTo(CellRef.Parse("C3"));
        controller.SetNote("gone");

        Select(controller, "A3:A4");
        controller.DeleteRows(2);

        controller.Sheet.ConditionalFormats.ShouldBeEmpty();
        controller.Sheet.Notes.ShouldBeEmpty();

        controller.Undo();
        controller.Sheet.ConditionalFormats.Single().Ranges.ShouldBe([CellRange.Parse("B3:B4")]);
        controller.Sheet.NoteAt(CellRef.Parse("C3"))!.Text.ShouldBe("gone");
    }

    [Theory]
    [InlineData("B2:B5", 3, 2, "B2:B7")]
    [InlineData("B2:B5", 1, 2, "B4:B7")]
    [InlineData("B2:B5", 7, 2, "B2:B5")]
    [InlineData("B2:B5", 2, -2, "B2:B3")]
    [InlineData("B2:B5", 0, -2, "B1:B3")]
    [InlineData("B2:B5", 4, -3, "B2:B4")]
    public void RangesShiftTheWayExcelMovesThem(string range, int at, int delta, string expected)
        => Worksheet.ShiftRange(CellRange.Parse(range), at, delta, rows: true).ShouldBe(CellRange.Parse(expected));

    [Fact]
    public void ARangeInsideADeletedBandIsGone()
        => Worksheet.ShiftRange(CellRange.Parse("B3:B4"), 2, -3, rows: true).ShouldBeNull();

    // ---- the file as a whole ----

    [Fact]
    public void EveryFeatureOnOneSheetStillProducesAValidFileInSchemaOrder()
    {
        var controller = Setup(out var workbook, Sales);
        using var _ = workbook;

        Select(controller, "E1:F1");
        controller.MergeCells();
        controller.GoTo(CellRef.Parse("A1"));
        controller.ToggleAutoFilter();
        Select(controller, "B2:B5");
        controller.AddConditionalFormat(ConditionalFormatRule.Bar(ConditionalPresets.DataBars[0].Color));
        controller.SetValidation(DataValidationRule.ForNumber(ValidationType.Whole, ValidationOperator.GreaterThan, "0"));
        controller.GoTo(CellRef.Parse("A7"));
        controller.SetHyperlink(new CellHyperlink(CellRef.Parse("A7")) { Address = "https://example.com" });
        controller.SetNote("note");
        controller.FreezeTopRow();
        Select(controller, "A1:B5");
        controller.InsertChart(ChartKind.Bar);
        controller.GoTo(CellRef.Parse("H1"));
        controller.SetCellText(CellRef.Parse("H1"), "Key");
        controller.SetCellText(CellRef.Parse("H2"), "1");
        controller.FormatAsTable("TableStyleLight9");
        controller.DefineName("Units", "Data!$B$2:$B$5");

        var bytes = workbook.ToArray();
        SchemaErrors(bytes).ShouldBeEmpty();

        var sheetXml = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(PackageComparer.ReadEntry(bytes, "xl/worksheets/sheet1.xml")));
        var order = sheetXml.Root!.Elements().Select(x => SheetXml.WorksheetOrder.ToList().IndexOf(x.Name.LocalName)).Where(x => x >= 0).ToList();
        order.ShouldBe(order.OrderBy(x => x).ToList());
    }

    [Fact]
    public async Task ReadingEveryFeatureLeavesAnUntouchedFileByteIdentical()
    {
        var original = WorkbookFixture.Build();
        using var workbook = await Workbook.OpenAsync(new MemoryStream(original));
        var sheet = workbook["Data"];

        _ = sheet.Charts;
        _ = sheet.Notes;
        _ = sheet.Tables;
        _ = sheet.Hyperlinks;
        _ = sheet.ConditionalFormats;
        _ = sheet.Validations;
        _ = sheet.AutoFilter;
        _ = sheet.ShowGridLines;
        _ = workbook.DefinedNames;

        var controller = new SpreadsheetController(workbook, sheet);
        controller.Resize(400, 300);
        _ = controller.SelectionStatistics;
        controller.Zoom = 1.5;

        using var bitmap = new SKBitmap(400, 300);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new SpreadsheetPainter();
        painter.Paint(canvas, SpreadsheetPaintRequest.For(controller, SpreadsheetTheme.Light, 1));

        PackageComparer.Compare(original, workbook.ToArray()).IsIdentical.ShouldBeTrue();
    }
}
