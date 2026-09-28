using System.Globalization;
using System.Text;
using Shiny.Controls.Office.Shell;
using Shiny.Controls.Office.Spreadsheet.Commands;

namespace Shiny.Controls.Office.Spreadsheet.View;

/// <summary>One note, as the Comments pane lists it: where it is and what it says.</summary>
public sealed record SpreadsheetNoteEntry(string Sheet, CellRef Cell, string Text, string Author)
{
    /// <summary>"Budget!B4" — quoted when the sheet name needs it, as a formula would write it.</summary>
    public string Address => $"{Calc.FormulaSheetRenamer.Quote(this.Sheet)}!{this.Cell.Relative()}";
}


/// <summary>
/// What the Office shell around a spreadsheet shows — the status bar's words, the comments pane's list,
/// the backstage's document info — computed once here so both hosts say the same thing.
/// </summary>
public static class SpreadsheetShell
{
    /// <summary>"Ready", "Enter" or "Edit".</summary>
    public static string ModeText(SheetEditMode mode) => mode switch
    {
        SheetEditMode.Enter => "Enter",
        SheetEditMode.Edit => "Edit",
        _ => "Ready"
    };


    /// <summary>
    /// Excel's selection summary — "Average: 4  Count: 3  Sum: 12" — or null for a selection that has
    /// nothing to summarise (a single cell, or fewer than two cells holding anything).
    /// </summary>
    public static string? Aggregates(SelectionStatistics statistics, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        culture ??= CultureInfo.CurrentCulture;

        if (!statistics.IsMeaningful)
            return null;

        var count = statistics.Count.ToString("N0", culture);
        if (statistics.NumericalCount == 0 || statistics.Average is not { } average)
            return $"Count: {count}";

        return $"Average: {OfficeStatusText.Number(average, culture)}  Count: {count}  Sum: {OfficeStatusText.Number(statistics.Sum, culture)}";
    }


    /// <summary>The status bar's view-mode id for a mode — the ids <see cref="OfficeViewModes"/> uses for Excel.</summary>
    public static string ViewModeId(SheetViewMode mode) => mode switch
    {
        SheetViewMode.PageLayout => OfficeViewModes.PageLayout.Id,
        SheetViewMode.PageBreakPreview => OfficeViewModes.PageBreak.Id,
        _ => OfficeViewModes.Normal.Id
    };


    /// <summary>The mode a status bar view-mode id stands for. Unknown ids are Normal.</summary>
    public static SheetViewMode ParseViewMode(string? id)
    {
        if (id == OfficeViewModes.PageLayout.Id)
            return SheetViewMode.PageLayout;

        if (id == OfficeViewModes.PageBreak.Id)
            return SheetViewMode.PageBreakPreview;

        return SheetViewMode.Normal;
    }


    /// <summary>Every note in the workbook, sheet by sheet in book order, then by row and column.</summary>
    public static IReadOnlyList<SpreadsheetNoteEntry> Notes(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var list = new List<SpreadsheetNoteEntry>();
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var note in sheet.Notes.OrderBy(x => x.Cell.Row).ThenBy(x => x.Cell.Column))
                list.Add(new SpreadsheetNoteEntry(sheet.Name, note.Cell, note.Text, note.Author));
        }

        return list;
    }


    /// <summary>Selects a note's cell, switching to its sheet first — the Comments pane's click.</summary>
    public static void GoTo(SpreadsheetController controller, SpreadsheetNoteEntry note)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(note);

        if (controller.Sheet.Name != note.Sheet)
        {
            var target = controller.Workbook.Sheets.FirstOrDefault(x => x.Name == note.Sheet);
            if (target is null || !target.IsVisible)
                return;

            controller.SwitchSheet(target);
        }

        controller.GoTo(note.Cell);
    }


    /// <summary>
    /// The title bar's search, when no command matched: finds the text across every visible sheet and
    /// selects the next hit. False when there is none.
    /// </summary>
    public static bool Search(SpreadsheetController controller, string? text)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var find = controller.Find;
        find.SearchAllSheets = true;
        find.Query = text.Trim();
        return find.Count > 0 && find.FindNext();
    }


    /// <summary>The backstage's Info page: name, where it lives, and the workbook's statistics.</summary>
    public static OfficeDocumentInfo DocumentInfo(Workbook workbook, string? name, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        culture ??= CultureInfo.CurrentCulture;

        var cells = 0;
        var formulas = 0;
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var cell in sheet.PopulatedCells())
            {
                cells++;
                if (sheet.GetFormula(cell) is not null)
                    formulas++;
            }
        }

        var notes = workbook.Sheets.Sum(x => x.Notes.Count);

        return new OfficeDocumentInfo
        {
            Title = name,
            Location = workbook.Path,
            Statistics =
            [
                new("Sheets", workbook.Sheets.Count.ToString("N0", culture)),
                new("Cells with data", cells.ToString("N0", culture)),
                new("Formulas", formulas.ToString("N0", culture)),
                new("Notes", notes.ToString("N0", culture))
            ]
        };
    }


    /// <summary>
    /// A sheet as CSV, the way Excel's "CSV (Comma delimited)" writes one: from A1 to the end of the
    /// used range, each cell's <em>formatted</em> value, fields quoted when they hold a comma, a quote
    /// or a line break.
    /// </summary>
    public static string ToCsv(Workbook workbook, Worksheet sheet)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);

        var builder = new StringBuilder();
        if (sheet.UsedRange is not { } used)
            return string.Empty;

        var styles = workbook.Styles;
        for (var row = 0; row <= used.Bottom; row++)
        {
            for (var column = 0; column <= used.Right; column++)
            {
                if (column > 0)
                    builder.Append(',');

                var cell = new CellRef(column, row);
                var value = sheet.GetDisplayValue(cell);
                if (value.IsBlank)
                    continue;

                var text = styles.Format(value, styles.Resolve(sheet.GetEffectiveStyleIndex(cell)));
                builder.Append(Quote(text));
            }

            builder.Append("\r\n");
        }

        return builder.ToString();
    }

    static string Quote(string field)
        => field.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + field.Replace("\"", "\"\"") + "\""
            : field;
}


/// <summary>
/// The workbooks the backstage's New page offers: a blank one and a few built in code, so there is
/// nothing to ship or download.
/// </summary>
public static class SpreadsheetTemplates
{
    public static readonly OfficeTemplate BlankWorkbook = OfficeTemplate.Blank(OfficeApp.Excel);

    public static readonly OfficeTemplate Budget = new("budget", "Monthly budget")
    {
        Description = "Planned against actual spending, with the differences totalled",
        Category = "Personal"
    };

    public static readonly OfficeTemplate Invoice = new("invoice", "Invoice")
    {
        Description = "Line items, subtotal, tax and amount due",
        Category = "Business"
    };

    public static readonly OfficeTemplate Schedule = new("schedule", "Weekly schedule")
    {
        Description = "Half-hour slots from 8:00 to 17:00, Monday to Friday",
        Category = "Planning"
    };

    /// <summary>Every template, blank first.</summary>
    public static IReadOnlyList<OfficeTemplate> All { get; } = [BlankWorkbook, Budget, Invoice, Schedule];


    /// <summary>
    /// Builds the workbook a template stands for. Unknown ids — a host's own templates — get a blank
    /// workbook; open those through <see cref="OfficeTemplate.Open"/> instead.
    /// </summary>
    /// <remarks>The undo history is cleared: a new workbook starts with nothing to undo.</remarks>
    public static Workbook Create(OfficeTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        Workbook workbook;
        switch (template.Id)
        {
            case "budget":
                workbook = Workbook.Create("Budget");
                BuildBudget(workbook);
                break;

            case "invoice":
                workbook = Workbook.Create("Invoice");
                BuildInvoice(workbook);
                break;

            case "schedule":
                workbook = Workbook.Create("Schedule");
                BuildSchedule(workbook);
                break;

            default:
                return Workbook.Create();
        }

        workbook.Undo.Clear();
        return workbook;
    }


    static readonly ArgbColor HeaderFill = new(255, 0x10, 0x7C, 0x41);
    static readonly ArgbColor HeaderInk = new(255, 0xFF, 0xFF, 0xFF);
    static readonly ArgbColor BandFill = new(255, 0xE2, 0xEF, 0xDA);

    static void BuildBudget(Workbook workbook)
    {
        var s = new Builder(workbook, "Budget");
        s.Text("A1", "Monthly Budget");
        s.Text("A3", "Category").Text("B3", "Planned").Text("C3", "Actual").Text("D3", "Difference");

        string[] categories = ["Rent", "Utilities", "Groceries", "Transport", "Insurance", "Entertainment", "Savings"];
        double[] planned = [1500, 220, 600, 180, 140, 200, 500];
        double[] actual = [1500, 245.30, 642.15, 151.80, 140, 260.45, 400];

        for (var i = 0; i < categories.Length; i++)
        {
            var row = i + 4;
            s.Text($"A{row}", categories[i]).Number($"B{row}", planned[i]).Number($"C{row}", actual[i]).Formula($"D{row}", $"B{row}-C{row}");
        }

        var total = categories.Length + 4;
        s.Text($"A{total}", "Total")
         .Formula($"B{total}", $"SUM(B4:B{total - 1})")
         .Formula($"C{total}", $"SUM(C4:C{total - 1})")
         .Formula($"D{total}", $"SUM(D4:D{total - 1})");

        s.Select("A1").Bold().FontSize(16).Height(24);
        s.Select("A3:D3").Bold().Fill(HeaderFill).Ink(HeaderInk);
        s.Select($"B4:D{total}").Format(NumberFormatPreset.Currency);
        s.Select($"A{total}:D{total}").Bold().Fill(BandFill);
        s.Select("A1").Width(150);
        s.Select("B1:D1").Width(100);
        s.Select("A4");
    }

    static void BuildInvoice(Workbook workbook)
    {
        var s = new Builder(workbook, "Invoice");
        s.Text("A1", "INVOICE");
        s.Text("A3", "Invoice #").Text("B3", "INV-0001");
        s.Text("A4", "Date").Formula("B4", "TODAY()");
        s.Text("A5", "Bill to").Text("B5", "Customer name");

        s.Text("A7", "Description").Text("B7", "Qty").Text("C7", "Unit price").Text("D7", "Amount");

        (string Item, double Qty, double Price)[] lines =
        [
            ("Consulting (hours)", 12, 95),
            ("Design review", 1, 450),
            ("Hosting (monthly)", 3, 29.99)
        ];

        for (var i = 0; i < lines.Length; i++)
        {
            var row = i + 8;
            s.Text($"A{row}", lines[i].Item).Number($"B{row}", lines[i].Qty).Number($"C{row}", lines[i].Price).Formula($"D{row}", $"B{row}*C{row}");
        }

        var last = lines.Length + 7;
        s.Text($"C{last + 2}", "Subtotal").Formula($"D{last + 2}", $"SUM(D8:D{last})");
        s.Text($"C{last + 3}", "Tax rate").Number($"D{last + 3}", 0.08);
        s.Text($"C{last + 4}", "Tax").Formula($"D{last + 4}", $"D{last + 2}*D{last + 3}");
        s.Text($"C{last + 5}", "Amount due").Formula($"D{last + 5}", $"D{last + 2}+D{last + 4}");

        s.Select("A1").Bold().FontSize(20).Ink(HeaderFill).Height(30);
        s.Select("B4").Format(NumberFormatPreset.ShortDate);
        s.Select("A7:D7").Bold().Fill(HeaderFill).Ink(HeaderInk);
        s.Select($"C8:D{last}").Format(NumberFormatPreset.Currency);
        s.Select($"D{last + 2}").Format(NumberFormatPreset.Currency);
        s.Select($"D{last + 3}").Format(NumberFormatPreset.Percent);
        s.Select($"D{last + 4}:D{last + 5}").Format(NumberFormatPreset.Currency);
        s.Select($"C{last + 5}:D{last + 5}").Bold().Fill(BandFill);
        s.Select("A1").Width(190);
        s.Select("B1:D1").Width(100);
        s.Select("A8");
    }

    static void BuildSchedule(Workbook workbook)
    {
        var s = new Builder(workbook, "Schedule");
        s.Text("A1", "Weekly Schedule");

        string[] days = ["Time", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday"];
        for (var i = 0; i < days.Length; i++)
            s.Text($"{CellRef.ColumnName(i)}3", days[i]);

        for (var slot = 0; slot < 18; slot++)
        {
            // Times are day fractions, which the Time format shows as 8:00, 8:30 …
            s.Number($"A{slot + 4}", (8 + slot * 0.5) / 24);
        }

        s.Select("A1").Bold().FontSize(16).Height(24);
        s.Select("A3:F3").Bold().Fill(HeaderFill).Ink(HeaderInk);
        s.Select("A4:A21").Format(NumberFormatPreset.Time).Bold();
        s.Select("A1").Width(70);
        s.Select("B1:F1").Width(120);
        s.Select("B4");
    }


    /// <summary>Writes cells through commands and formats them through a throwaway controller.</summary>
    sealed class Builder(Workbook workbook, string sheet)
    {
        readonly SpreadsheetController controller = new(workbook, workbook.Sheets.First(x => x.Name == sheet));

        public Builder Text(string cell, string text)
        {
            workbook.Execute(new SetCellValueCommand(sheet, CellRef.Parse(cell), CellValue.FromText(text)));
            return this;
        }

        public Builder Number(string cell, double value)
        {
            workbook.Execute(new SetCellValueCommand(sheet, CellRef.Parse(cell), CellValue.FromNumber(value)));
            return this;
        }

        public Builder Formula(string cell, string formula)
        {
            workbook.Execute(new SetCellFormulaCommand(sheet, CellRef.Parse(cell), formula));
            return this;
        }

        public Builder Select(string range)
        {
            var parts = range.Split(':');
            var a = CellRef.Parse(parts[0]);
            var b = parts.Length > 1 ? CellRef.Parse(parts[1]) : a;
            this.controller.Selection.SelectRange(new CellRange(a, b));
            return this;
        }

        public Builder Bold()
        {
            if (!this.controller.ActiveFormat.Bold)
                this.controller.ToggleBold();
            return this;
        }

        public Builder FontSize(double size)
        {
            this.controller.SetFontSize(size);
            return this;
        }

        public Builder Fill(ArgbColor color)
        {
            this.controller.SetFillColor(color);
            return this;
        }

        public Builder Ink(ArgbColor color)
        {
            this.controller.SetTextColor(color);
            return this;
        }

        public Builder Format(NumberFormatPreset preset)
        {
            this.controller.SetNumberFormat(preset);
            return this;
        }

        /// <summary>A title in a large font needs a taller row; Excel grows it on typing, a builder has to ask.</summary>
        public Builder Height(double points)
        {
            this.controller.SetRowHeight(points);
            return this;
        }

        public Builder Width(double pixels)
        {
            this.controller.SetColumnWidth(pixels);
            return this;
        }
    }
}
