using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Spreadsheet.Commands;
using Shiny.Controls.Office.Skia;

namespace Sample.Features.Office;

public partial class SpreadsheetPage : ContentPage
{
    readonly Workbook workbook;
    bool dark;
    int nextRow = 6;

    public SpreadsheetPage()
    {
        this.InitializeComponent();
        SampleSourceCode.Attach(this);

        // Built in memory rather than shipped as a binary fixture, so the demo also exercises the
        // "create a new workbook from nothing" path.
        this.workbook = Workbook.Create("Budget");
        this.Seed();

        this.Sheet.Workbook = this.workbook;
        this.UpdateFormulaBar();

        if (this.Sheet.Controller is { } controller)
            controller.Selection.Changed += (_, _) => this.UpdateFormulaBar();

        this.Sheet.SelectionStatisticsChanged += (_, _) => this.UpdateStatusBar();
        this.Sheet.ZoomChanged += (_, _) => this.UpdateStatusBar();
        this.UpdateStatusBar();
    }

    /// <summary>Excel's status bar: the selection's aggregates when there is something to aggregate, and the zoom.</summary>
    void UpdateStatusBar()
    {
        var stats = this.Sheet.SelectionStatistics;
        var parts = new List<string>();

        if (stats.IsMeaningful)
        {
            if (stats.Average is { } average)
                parts.Add($"Average: {average:#,##0.##}");

            parts.Add($"Count: {stats.Count}");

            if (stats.NumericalCount > 0)
                parts.Add($"Sum: {stats.Sum:#,##0.##}");
        }

        parts.Add($"Zoom: {this.Sheet.Zoom:P0}");
        this.StatusBar.Text = string.Join("    ", parts);
    }

    void Seed()
    {
        this.Set("A1", CellValue.FromText("Item"));
        this.Set("B1", CellValue.FromText("Qty"));
        this.Set("C1", CellValue.FromText("Unit"));
        this.Set("D1", CellValue.FromText("Total"));

        this.AddItem(2, "Widget", 4, 12.50);
        this.AddItem(3, "Gadget", 2, 42.00);
        this.AddItem(4, "Doohickey", 7, 3.25);

        this.Set("A5", CellValue.FromText("Total"));
        this.workbook.Execute(new SetCellFormulaCommand("Budget", CellRef.Parse("D5"), "SUM(D2:D4)"));

        // A second sheet, so the tab strip has somewhere to go and the cross-sheet formula below has
        // something to read. Renaming Budget from its tab rewrites this formula with it.
        this.workbook.Execute(new AddSheetCommand("Summary", 1));
        this.workbook.Execute(new SetCellValueCommand("Summary", CellRef.Parse("A1"), CellValue.FromText("Budget total")));
        this.workbook.Execute(new SetCellFormulaCommand("Summary", CellRef.Parse("B1"), "Budget!D5"));
    }

    void Set(string reference, CellValue value)
        => this.workbook.Execute(new SetCellValueCommand("Budget", CellRef.Parse(reference), value));

    void AddItem(int row, string name, double quantity, double unit)
    {
        this.Set($"A{row}", CellValue.FromText(name));
        this.Set($"B{row}", CellValue.FromNumber(quantity));
        this.Set($"C{row}", CellValue.FromNumber(unit));
        this.workbook.Execute(new SetCellFormulaCommand("Budget", CellRef.Parse($"D{row}"), $"B{row}*C{row}"));
    }

    void OnAddRow(object? sender, EventArgs e)
    {
        this.AddItem(this.nextRow, $"Item {this.nextRow}", this.nextRow, 5);

        // The total has to grow with the table; nothing rewrites ranges for us yet.
        this.workbook.Execute(new SetCellFormulaCommand("Budget", CellRef.Parse("D5"), $"SUM(D2:D{this.nextRow})"));
        this.nextRow++;
        this.UpdateFormulaBar();
    }

    void OnUndo(object? sender, EventArgs e)
    {
        this.Sheet.Undo();
        this.UpdateFormulaBar();
    }

    void OnRedo(object? sender, EventArgs e)
    {
        this.Sheet.Redo();
        this.UpdateFormulaBar();
    }

    void OnToggleTheme(object? sender, EventArgs e)
    {
        this.dark = !this.dark;
        // null, not SpreadsheetTheme.Light: unset means "follow the app appearance", which is the
        // behaviour worth demoing. Passing Light would pin it and hide that.
        this.Sheet.Theme = this.dark ? SpreadsheetTheme.Dark : null;
    }

    void OnCellChanged(object? sender, CellRef cell) => this.UpdateFormulaBar();

    void UpdateFormulaBar()
    {
        var controller = this.Sheet.Controller;
        this.FormulaBar.Text = controller is null
            ? string.Empty
            : $"{controller.Selection.Active.Relative()}   {controller.ActiveCellText}";
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (this.Handler is null)
            this.workbook.Dispose();
    }
}
