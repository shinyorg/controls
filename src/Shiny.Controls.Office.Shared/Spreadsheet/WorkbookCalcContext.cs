using Shiny.Controls.Office.Spreadsheet.Calc;

namespace Shiny.Controls.Office.Spreadsheet;

/// <summary>
/// Bridges the calculation engine to a live workbook.
/// </summary>
/// <remarks>
/// Reads go through the engine's computed values first, so a formula that depends on another formula
/// sees the freshly calculated result rather than the value cached in the file — which is stale the
/// moment anything upstream is edited.
/// </remarks>
sealed class WorkbookCalcContext(Workbook workbook, TimeProvider time) : ICalcContext
{
    public string CurrentSheet { get; set; } = workbook.Sheets.Count > 0 ? workbook.Sheets[0].Name : string.Empty;

    public CellRef CurrentCell { get; set; }

    public DateTime Now => time.GetLocalNow().DateTime;

    public bool SheetExists(string sheet)
        => workbook.Sheets.Any(x => string.Equals(x.Name, sheet, StringComparison.OrdinalIgnoreCase));

    public CellValue GetValue(string? sheet, CellRef cell)
    {
        var name = sheet ?? this.CurrentSheet;
        var address = new CellAddress(name, cell.Relative());

        if (workbook.Calc.TryGetComputed(address, out var computed))
            return computed;

        var worksheet = workbook.Sheets.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        return worksheet is null ? CellValue.Blank : worksheet.GetValue(cell);
    }

    public FormulaNode? ResolveName(string name, string? sheet) => workbook.ResolveNameNode(name, sheet ?? this.CurrentSheet);

    public bool IsRowHidden(string? sheet, int row)
        => workbook.Find(sheet ?? this.CurrentSheet)?.IsRowHidden(row) ?? false;

    public string? GetFormula(string? sheet, CellRef cell)
        => workbook.Find(sheet ?? this.CurrentSheet)?.GetFormula(cell);
}
