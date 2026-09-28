using System.Globalization;
using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Spreadsheet.Calc;
using Shiny.Controls.Office.Spreadsheet.Commands;

namespace Shiny.Controls.Office.Spreadsheet.View;

/// <summary>The input dialogs behind Conditional Formatting's Highlight Cells and Top/Bottom rules.</summary>
public enum ConditionalDialogKind
{
    GreaterThan,
    LessThan,
    Between,
    EqualTo,
    TextContains,
    DuplicateValues,
    Top10Items,
    Top10Percent,
    Bottom10Items,
    Bottom10Percent,
    AboveAverage,
    BelowAverage
}

/// <summary>
/// Every dialog the spreadsheet opens, built as <see cref="SheetDialog"/> data with its validation and
/// the command it runs. Hosts only render them.
/// </summary>
public static class SpreadsheetDialogs
{
    static SheetDialogButton Ok(Func<SheetDialog, bool> action) => new("OK", action) { IsPrimary = true };

    static readonly SheetDialogButton Cancel = new("Cancel");

    static SheetField Text(string key, string label, string value = "", string? placeholder = null)
        => new(key, label, SheetFieldKind.Text) { Text = value, Placeholder = placeholder };

    static SheetField Choice(string key, string label, IReadOnlyList<string> options, int selected = 0)
        => new(key, label, SheetFieldKind.Choice) { Options = options, SelectedIndex = selected };

    static SheetField Check(string key, string label, bool value)
        => new(key, label, SheetFieldKind.Check) { IsChecked = value };

    static SheetField Label(string key, string text) => new(key, text, SheetFieldKind.Label);

    static string Invariant(double value) => value.ToString("0.##########", CultureInfo.CurrentCulture);

    // ---- filter ----

    static readonly string[] ConditionNames =
    [
        "(none)", "equals", "does not equal", "contains", "does not contain", "begins with", "ends with",
        "is greater than", "is greater than or equal to", "is less than", "is less than or equal to"
    ];

    static readonly FilterOperator[] ConditionOperators =
    [
        FilterOperator.Equal, FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.Contains, FilterOperator.DoesNotContain,
        FilterOperator.BeginsWith, FilterOperator.EndsWith, FilterOperator.GreaterThan, FilterOperator.GreaterThanOrEqual,
        FilterOperator.LessThan, FilterOperator.LessThanOrEqual
    ];

    const string Blanks = "(Blanks)";

    /// <summary>A column's filter dropdown: sort, a checklist of its values, and a text or number condition.</summary>
    public static SheetDialog Filter(SpreadsheetController controller, SheetAutoFilter filter, int column)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var sheet = controller.Sheet;
        var values = filter.DistinctValues(sheet, column, out var hasBlanks).ToList();
        var current = filter.For(column);

        bool Checked(string value)
            => current?.Values is not { } kept || (value == Blanks ? current.IncludeBlanks : kept.Contains(value));

        var all = values.Select(x => new SheetChecklistItem(x, Checked(x))).ToList();
        if (hasBlanks)
            all.Add(new SheetChecklistItem(Blanks, Checked(Blanks)));

        var header = SheetAutoFilter.DisplayText(sheet, new CellRef(column, filter.Range.Top));
        var conditionIndex = current?.First is { } first ? Array.IndexOf(ConditionOperators, first.Operator, 1) : 0;

        var fields = new List<SheetField>
        {
            Text("search", "Search", placeholder: "Search"),
            Check("all", "(Select All)", all.All(x => x.IsChecked)),
            new("values", "Values", SheetFieldKind.Checklist) { Items = all },
            Choice("condition", "Or show rows where the value", ConditionNames, Math.Max(0, conditionIndex)),
            Text("value", "Value", current?.First?.Value ?? string.Empty)
        };

        return new SheetDialog(
            string.IsNullOrEmpty(header) ? "Filter" : $"Filter: {header}",
            fields,
            [
                new SheetDialogButton("Sort A to Z", _ =>
                {
                    controller.Sort([new SortKey(column)], hasHeader: true, filter.Range);
                    return true;
                }),
                new SheetDialogButton("Sort Z to A", _ =>
                {
                    controller.Sort([new SortKey(column, Descending: true)], hasHeader: true, filter.Range);
                    return true;
                }),
                new SheetDialogButton("Clear Filter", _ =>
                {
                    controller.ApplyColumnFilter(filter, column, null);
                    return true;
                }),
                Ok(dialog =>
                {
                    var condition = dialog["condition"].SelectedIndex;
                    var value = dialog["value"].Text;

                    if (condition > 0)
                    {
                        controller.ApplyColumnFilter(filter, column, ColumnFilter.ForCondition(column, new FilterCondition(ConditionOperators[condition], value)));
                        return true;
                    }

                    var search = dialog["search"].Text;
                    var shown = dialog["values"].Items;

                    // With a search, the list holds only what it found, so OK keeps the ticked matches -
                    // Excel's rule - rather than every value ticked before the search.
                    var picked = shown.Where(x => x.IsChecked).Select(x => x.Text).ToList();

                    if (picked.Count == 0)
                    {
                        dialog.Error = "Select at least one value.";
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(search) && picked.Count == all.Count)
                    {
                        controller.ApplyColumnFilter(filter, column, null);
                        return true;
                    }

                    var blanks = picked.Remove(Blanks);
                    controller.ApplyColumnFilter(filter, column, ColumnFilter.ForValues(column, picked, blanks));
                    return true;
                }),
                Cancel
            ])
        {
            Width = 320,
            FieldChanged = (dialog, field) =>
            {
                var list = dialog["values"];

                switch (field.Key)
                {
                    case "all":
                        foreach (var item in list.Items)
                            item.IsChecked = field.IsChecked;

                        break;

                    case "search":
                        var query = field.Text.Trim();
                        list.Items = query.Length == 0
                            ? all
                            : all.Where(x => x.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                                .Select(x => new SheetChecklistItem(x.Text, true))
                                .ToList();

                        dialog["all"].IsChecked = list.Items.All(x => x.IsChecked);
                        break;

                    case "values":
                        dialog["all"].IsChecked = list.Items.All(x => x.IsChecked);
                        break;
                }
            }
        };
    }

    // ---- conditional formatting ----

    public static string TitleOf(ConditionalDialogKind kind) => kind switch
    {
        ConditionalDialogKind.GreaterThan => "Greater Than",
        ConditionalDialogKind.LessThan => "Less Than",
        ConditionalDialogKind.Between => "Between",
        ConditionalDialogKind.EqualTo => "Equal To",
        ConditionalDialogKind.TextContains => "Text That Contains",
        ConditionalDialogKind.DuplicateValues => "Duplicate Values",
        ConditionalDialogKind.Top10Items => "Top 10 Items",
        ConditionalDialogKind.Top10Percent => "Top 10%",
        ConditionalDialogKind.Bottom10Items => "Bottom 10 Items",
        ConditionalDialogKind.Bottom10Percent => "Bottom 10%",
        ConditionalDialogKind.AboveAverage => "Above Average",
        _ => "Below Average"
    };

    /// <summary>A Highlight Cells or Top/Bottom rule's dialog: its value or values, and the format to apply.</summary>
    public static SheetDialog Conditional(SpreadsheetController controller, ConditionalDialogKind kind)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var styles = DxfFormat.Presets.Select(x => x.Name).ToList();
        var stats = controller.SelectionStatistics;
        var suggestion = stats.Average is { } average ? Invariant(Math.Round(average, 2)) : string.Empty;

        var fields = new List<SheetField>();
        var title = TitleOf(kind);

        switch (kind)
        {
            case ConditionalDialogKind.GreaterThan:
                fields.Add(Label("intro", "Format cells that are GREATER THAN:"));
                fields.Add(Text("value1", "Value", suggestion));
                break;

            case ConditionalDialogKind.LessThan:
                fields.Add(Label("intro", "Format cells that are LESS THAN:"));
                fields.Add(Text("value1", "Value", suggestion));
                break;

            case ConditionalDialogKind.EqualTo:
                fields.Add(Label("intro", "Format cells that are EQUAL TO:"));
                fields.Add(Text("value1", "Value", suggestion));
                break;

            case ConditionalDialogKind.Between:
                fields.Add(Label("intro", "Format cells that are BETWEEN:"));
                fields.Add(Text("value1", "From", stats.Min is { } min ? Invariant(min) : string.Empty));
                fields.Add(Text("value2", "To", stats.Max is { } max ? Invariant(max) : string.Empty));
                break;

            case ConditionalDialogKind.TextContains:
                fields.Add(Label("intro", "Format cells that contain the text:"));
                fields.Add(Text("value1", "Text"));
                break;

            case ConditionalDialogKind.DuplicateValues:
                fields.Add(Choice("which", "Format cells that contain", ["Duplicate", "Unique"]));
                break;

            case ConditionalDialogKind.Top10Items or ConditionalDialogKind.Bottom10Items:
                fields.Add(Label("intro", kind == ConditionalDialogKind.Top10Items ? "Format cells that rank in the TOP:" : "Format cells that rank in the BOTTOM:"));
                fields.Add(new SheetField("rank", "Items", SheetFieldKind.Number) { Text = "10" });
                break;

            case ConditionalDialogKind.Top10Percent or ConditionalDialogKind.Bottom10Percent:
                fields.Add(Label("intro", kind == ConditionalDialogKind.Top10Percent ? "Format cells that rank in the TOP:" : "Format cells that rank in the BOTTOM:"));
                fields.Add(new SheetField("rank", "Percent", SheetFieldKind.Number) { Text = "10" });
                break;

            default:
                fields.Add(Label("intro", kind == ConditionalDialogKind.AboveAverage
                    ? "Format cells that are ABOVE AVERAGE for the selected range."
                    : "Format cells that are BELOW AVERAGE for the selected range."));
                break;
        }

        fields.Add(Choice("style", "with", styles));

        return new SheetDialog(title, fields, [Ok(dialog =>
        {
            var format = DxfFormat.Presets[Math.Max(0, dialog["style"].SelectedIndex)].Format;

            string Operand(string key)
            {
                var text = dialog[key].Text.Trim();
                if (text.StartsWith('='))
                    return text[1..];

                return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number)
                    ? number.ToString("R", CultureInfo.InvariantCulture)
                    : "\"" + text.Replace("\"", "\"\"") + "\"";
            }

            int Rank()
            {
                var rank = (int)(dialog["rank"].Number ?? 0);
                if (rank < 1 || rank > 1000)
                    throw new ArgumentException("Enter a whole number between 1 and 1000.");

                return rank;
            }

            if (dialog.Find("value1") is { } first && string.IsNullOrWhiteSpace(first.Text))
            {
                dialog.Error = "Enter a value.";
                return false;
            }

            var rule = kind switch
            {
                ConditionalDialogKind.GreaterThan => ConditionalFormatRule.CellIs(ConditionalOperator.GreaterThan, format, Operand("value1")),
                ConditionalDialogKind.LessThan => ConditionalFormatRule.CellIs(ConditionalOperator.LessThan, format, Operand("value1")),
                ConditionalDialogKind.EqualTo => ConditionalFormatRule.CellIs(ConditionalOperator.Equal, format, Operand("value1")),
                ConditionalDialogKind.Between => ConditionalFormatRule.CellIs(ConditionalOperator.Between, format, Operand("value1"), Operand("value2")),
                ConditionalDialogKind.TextContains => ConditionalFormatRule.TextContains(dialog["value1"].Text, format),
                ConditionalDialogKind.DuplicateValues => ConditionalFormatRule.Duplicates(format, unique: dialog["which"].SelectedIndex == 1),
                ConditionalDialogKind.Top10Items => ConditionalFormatRule.TopBottom(Rank(), percent: false, bottom: false, format),
                ConditionalDialogKind.Top10Percent => ConditionalFormatRule.TopBottom(Rank(), percent: true, bottom: false, format),
                ConditionalDialogKind.Bottom10Items => ConditionalFormatRule.TopBottom(Rank(), percent: false, bottom: true, format),
                ConditionalDialogKind.Bottom10Percent => ConditionalFormatRule.TopBottom(Rank(), percent: true, bottom: true, format),
                ConditionalDialogKind.AboveAverage => ConditionalFormatRule.Average(true, format),
                _ => ConditionalFormatRule.Average(false, format)
            };

            controller.AddConditionalFormat(rule);
            return true;
        }), Cancel]);
    }

    // ---- data validation ----

    static readonly string[] AllowNames = ["Any value", "Whole number", "Decimal", "List", "Date", "Time", "Text length", "Custom"];
    static readonly ValidationType[] AllowTypes = [ValidationType.Any, ValidationType.Whole, ValidationType.Decimal, ValidationType.List, ValidationType.Date, ValidationType.Time, ValidationType.TextLength, ValidationType.Custom];
    static readonly string[] OperatorNames = ["between", "not between", "equal to", "not equal to", "greater than", "less than", "greater than or equal to", "less than or equal to"];
    static readonly ValidationOperator[] Operators = [ValidationOperator.Between, ValidationOperator.NotBetween, ValidationOperator.Equal, ValidationOperator.NotEqual, ValidationOperator.GreaterThan, ValidationOperator.LessThan, ValidationOperator.GreaterThanOrEqual, ValidationOperator.LessThanOrEqual];

    /// <summary>Data ▸ Data Validation: settings, input message and error alert.</summary>
    public static SheetDialog DataValidation(SpreadsheetController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var rule = controller.ActiveValidation ?? new DataValidationRule(ValidationType.Any);

        var settings = new List<SheetField>
        {
            Choice("allow", "Allow", AllowNames, Array.IndexOf(AllowTypes, rule.Type)),
            Choice("operator", "Data", OperatorNames, Array.IndexOf(Operators, rule.Operator)),
            Text("min", "Minimum", rule.Formula1 ?? string.Empty),
            Text("max", "Maximum", rule.Formula2 ?? string.Empty),
            Text("source", "Source", rule.Type == ValidationType.List ? (rule.Formula1 ?? string.Empty).Trim('"') : string.Empty, "Red,Green,Blue or =$A$1:$A$5"),
            Text("formula", "Formula", rule.Type == ValidationType.Custom ? "=" + rule.Formula1 : string.Empty, "=A1>0"),
            Check("blank", "Ignore blank", rule.AllowBlank),
            Check("dropdown", "In-cell dropdown", rule.InCellDropDown)
        };

        var input = new List<SheetField>
        {
            Check("showInput", "Show input message when cell is selected", rule.ShowInputMessage),
            Text("promptTitle", "Title", rule.PromptTitle ?? string.Empty),
            new("prompt", "Input message", SheetFieldKind.MultilineText) { Text = rule.Prompt ?? string.Empty }
        };

        var error = new List<SheetField>
        {
            Check("showError", "Show error alert after invalid data is entered", rule.ShowErrorMessage),
            Choice("errorStyle", "Style", ["Stop", "Warning", "Information"], (int)rule.ErrorStyle),
            Text("errorTitle", "Title", rule.ErrorTitle ?? string.Empty),
            new("error", "Error message", SheetFieldKind.MultilineText) { Text = rule.Error ?? string.Empty }
        };

        void Layout(SheetDialog dialog)
        {
            var type = AllowTypes[Math.Max(0, dialog["allow"].SelectedIndex)];
            var op = Operators[Math.Max(0, dialog["operator"].SelectedIndex)];
            var ranged = type is ValidationType.Whole or ValidationType.Decimal or ValidationType.Date or ValidationType.Time or ValidationType.TextLength;

            dialog["operator"].IsVisible = ranged;
            dialog["min"].IsVisible = ranged;
            dialog["max"].IsVisible = ranged && op is ValidationOperator.Between or ValidationOperator.NotBetween;
            dialog["min"].Label = ranged && op is ValidationOperator.Between or ValidationOperator.NotBetween ? "Minimum" : "Value";
            dialog["source"].IsVisible = type == ValidationType.List;
            dialog["dropdown"].IsVisible = type == ValidationType.List;
            dialog["formula"].IsVisible = type == ValidationType.Custom;
            dialog["blank"].IsVisible = type != ValidationType.Any;
        }

        var dialog = new SheetDialog("Data Validation",
            [new SheetDialogTab("Settings", settings), new SheetDialogTab("Input Message", input), new SheetDialogTab("Error Alert", error)],
            [
                new SheetDialogButton("Clear All", _ =>
                {
                    controller.SetValidation(null);
                    return true;
                }),
                Ok(d =>
                {
                    var type = AllowTypes[Math.Max(0, d["allow"].SelectedIndex)];
                    if (type == ValidationType.Any && string.IsNullOrWhiteSpace(d["prompt"].Text))
                    {
                        controller.SetValidation(null);
                        return true;
                    }

                    var op = Operators[Math.Max(0, d["operator"].SelectedIndex)];
                    string? formula1 = null, formula2 = null;

                    switch (type)
                    {
                        case ValidationType.List:
                            var source = d["source"].Text.Trim();
                            if (source.Length == 0)
                            {
                                d.Error = "Enter the list's items, separated by commas, or a reference to them.";
                                return false;
                            }

                            formula1 = source.StartsWith('=') ? source[1..] : "\"" + source.Replace("\"", "\"\"") + "\"";
                            break;

                        case ValidationType.Custom:
                            var formula = d["formula"].Text.Trim();
                            if (formula.Length == 0)
                            {
                                d.Error = "Enter a formula.";
                                return false;
                            }

                            formula1 = formula.TrimStart('=');
                            break;

                        case ValidationType.Any:
                            break;

                        default:
                            formula1 = Bound(d["min"].Text);
                            if (formula1 is null)
                            {
                                d.Error = "Enter a value.";
                                return false;
                            }

                            if (op is ValidationOperator.Between or ValidationOperator.NotBetween)
                            {
                                formula2 = Bound(d["max"].Text);
                                if (formula2 is null)
                                {
                                    d.Error = "Enter a maximum.";
                                    return false;
                                }
                            }

                            break;
                    }

                    controller.SetValidation(new DataValidationRule(type)
                    {
                        Operator = op,
                        Formula1 = formula1,
                        Formula2 = formula2,
                        AllowBlank = d["blank"].IsChecked,
                        InCellDropDown = d["dropdown"].IsChecked,
                        ShowInputMessage = d["showInput"].IsChecked,
                        PromptTitle = NullIfEmpty(d["promptTitle"].Text),
                        Prompt = NullIfEmpty(d["prompt"].Text),
                        ShowErrorMessage = d["showError"].IsChecked,
                        ErrorStyle = (ValidationErrorStyle)Math.Max(0, d["errorStyle"].SelectedIndex),
                        ErrorTitle = NullIfEmpty(d["errorTitle"].Text),
                        Error = NullIfEmpty(d["error"].Text)
                    });

                    return true;
                }),
                Cancel
            ])
        {
            Width = 420,
            FieldChanged = (d, _) => Layout(d)
        };

        Layout(dialog);
        return dialog;

        static string? Bound(string text)
        {
            text = text.Trim();
            if (text.Length == 0)
                return null;

            if (text.StartsWith('='))
                return text[1..];

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number))
                return number.ToString("R", CultureInfo.InvariantCulture);

            // Dates typed as text become their serial number, the way Excel stores a date bound.
            return ExcelDate.TryParse(text, out var serial)
                ? serial.ToString("R", CultureInfo.InvariantCulture)
                : text;
        }
    }

    static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text;

    // ---- hyperlink ----

    static readonly string[] LinkKinds = ["Existing web page or file", "Place in this document", "E-mail address"];

    /// <summary>Insert ▸ Link: what the cell shows, and where it goes.</summary>
    public static SheetDialog Hyperlink(SpreadsheetController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var existing = controller.ActiveHyperlink;
        var sheets = controller.Workbook.Sheets.Select(x => x.Name).ToList();
        var display = existing?.Display ?? (controller.Sheet.GetValue(controller.Selection.Active) is { IsBlank: false } value ? Coercion.ToText(value) : string.Empty);

        var kind = existing is null ? 0 : existing.IsInternal ? 1 : existing.Address!.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? 2 : 0;

        string place = existing?.Location ?? string.Empty;
        var placeSheet = sheets.IndexOf(controller.Sheet.Name);
        var placeCell = "A1";
        if (place.LastIndexOf('!') is var bang and > 0)
        {
            placeSheet = Math.Max(0, sheets.FindIndex(x => string.Equals(x, place[..bang].Trim('\''), StringComparison.OrdinalIgnoreCase)));
            placeCell = place[(bang + 1)..];
        }

        var fields = new List<SheetField>
        {
            Choice("kind", "Link to", LinkKinds, kind),
            Text("display", "Text to display", display),
            Text("address", "Address", kind == 0 ? existing?.Address ?? string.Empty : string.Empty, "https://"),
            Choice("sheet", "Sheet", sheets, Math.Max(0, placeSheet)),
            Text("cell", "Cell reference", placeCell),
            Text("email", "E-mail address", kind == 2 ? existing!.Address![7..] : string.Empty, "someone@example.com"),
            Text("tooltip", "ScreenTip", existing?.Tooltip ?? string.Empty)
        };

        void Layout(SheetDialog d)
        {
            var k = d["kind"].SelectedIndex;
            d["address"].IsVisible = k == 0;
            d["sheet"].IsVisible = k == 1;
            d["cell"].IsVisible = k == 1;
            d["email"].IsVisible = k == 2;
        }

        var buttons = new List<SheetDialogButton>();
        if (existing is not null)
        {
            buttons.Add(new SheetDialogButton("Remove Link", _ =>
            {
                controller.SetHyperlink(null);
                return true;
            }));
        }

        buttons.Add(Ok(d =>
        {
            var k = d["kind"].SelectedIndex;
            var link = new CellHyperlink(controller.Selection.Active)
            {
                Display = NullIfEmpty(d["display"].Text),
                Tooltip = NullIfEmpty(d["tooltip"].Text)
            };

            switch (k)
            {
                case 1:
                    var cell = d["cell"].Text.Trim();
                    if (!CellRef.TryParse(cell.Replace("$", string.Empty, StringComparison.Ordinal), out _) && controller.Workbook.FindName(cell) is null)
                    {
                        d.Error = "Enter a cell reference or a defined name.";
                        return false;
                    }

                    var sheetName = d["sheet"].SelectedOption ?? controller.Sheet.Name;
                    var quoted = FormulaSheetRenamer.RequiresQuoting(sheetName) ? FormulaSheetRenamer.Quote(sheetName) : sheetName;
                    link = link with { Location = CellRef.TryParse(cell.Replace("$", string.Empty, StringComparison.Ordinal), out _) ? $"{quoted}!{cell}" : cell };
                    break;

                case 2:
                    var email = d["email"].Text.Trim();
                    if (email.Length == 0)
                    {
                        d.Error = "Enter an e-mail address.";
                        return false;
                    }

                    link = link with { Address = "mailto:" + email };
                    break;

                default:
                    var address = d["address"].Text.Trim();
                    if (address.Length == 0)
                    {
                        d.Error = "Enter an address.";
                        return false;
                    }

                    // A bare host name is a web address, which is what Excel assumes too.
                    if (!address.Contains("://", StringComparison.Ordinal) && !address.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) && address.Contains('.'))
                        address = "https://" + address;

                    link = link with { Address = address };
                    break;
            }

            controller.SetHyperlink(link);
            return true;
        }));

        buttons.Add(Cancel);

        var dialog = new SheetDialog(existing is null ? "Insert Hyperlink" : "Edit Hyperlink", fields, buttons)
        {
            Width = 420,
            FieldChanged = (d, _) => Layout(d)
        };

        Layout(dialog);
        return dialog;
    }

    // ---- notes ----

    /// <summary>Review ▸ New Note / Edit Note.</summary>
    public static SheetDialog Note(SpreadsheetController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var existing = controller.ActiveNote;
        var buttons = new List<SheetDialogButton>();

        if (existing is not null)
        {
            buttons.Add(new SheetDialogButton("Delete", _ =>
            {
                controller.DeleteNote();
                return true;
            }));
        }

        buttons.Add(Ok(d =>
        {
            controller.SetNote(d["text"].Text);
            return true;
        }));

        buttons.Add(Cancel);

        return new SheetDialog(
            existing is null ? "New Note" : "Edit Note",
            [
                Label("author", existing?.Author ?? controller.NoteAuthor),
                new SheetField("text", "Note", SheetFieldKind.MultilineText) { Text = existing?.Text ?? string.Empty }
            ],
            buttons);
    }

    // ---- functions ----

    const string AllCategories = "All";

    /// <summary>Formulas ▸ Insert Function: search, or pick a category, then a function.</summary>
    public static SheetDialog InsertFunction(SpreadsheetController controller, string? category = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var categories = new List<string> { AllCategories };
        categories.AddRange(FunctionCatalog.Categories);

        var list = new SheetField("functions", "Select a function", SheetFieldKind.List);
        var description = Label("description", string.Empty);

        void Fill(SheetDialog? d, string search, string pick)
        {
            var found = FunctionCatalog.Search(search, pick == AllCategories ? null : pick);
            list.Options = found.Select(x => x.Name).ToList();
            list.Details = found.Select(x => x.Description).ToList();
            list.SelectedIndex = found.Count > 0 ? 0 : -1;
            Describe();
        }

        void Describe()
            => description.Label = list.SelectedOption is { } name && FunctionCatalog.Find(name) is { } info
                ? $"{info.Signature}\n{info.Description}"
                : string.Empty;

        var categoryField = Choice("category", "Or select a category", categories, Math.Max(0, categories.IndexOf(category ?? AllCategories)));
        Fill(null, string.Empty, categoryField.SelectedOption ?? AllCategories);

        return new SheetDialog("Insert Function",
            [
                Text("search", "Search for a function", placeholder: "Type a brief description of what you want to do"),
                categoryField,
                list,
                description
            ],
            [
                Ok(d =>
                {
                    if (d["functions"].SelectedOption is not { } name)
                    {
                        d.Error = "Select a function.";
                        return false;
                    }

                    controller.InsertFunction(name);
                    return true;
                }),
                Cancel
            ])
        {
            Width = 460,
            FieldChanged = (d, field) =>
            {
                switch (field.Key)
                {
                    case "search":
                        d["category"].SelectedIndex = 0;
                        Fill(d, field.Text, AllCategories);
                        break;

                    case "category":
                        Fill(d, d["search"].Text, field.SelectedOption ?? AllCategories);
                        break;

                    case "functions":
                        Describe();
                        break;
                }
            }
        };
    }

    // ---- names ----

    /// <summary>Formulas ▸ Name Manager: the workbook's names, with New, Edit and Delete.</summary>
    public static SheetDialog NameManager(SpreadsheetController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var list = new SheetField("names", "Names", SheetFieldKind.List);

        void Refresh()
        {
            var names = controller.Workbook.VisibleNames.ToList();
            list.Options = names.Select(x => x.Name).ToList();
            list.Details = names.Select(x => $"={x.Formula}   ({x.Scope ?? "Workbook"})").ToList();
            list.SelectedIndex = Math.Min(Math.Max(0, list.SelectedIndex), names.Count - 1);
        }

        Refresh();

        DefinedNameInfo? Selected()
        {
            var names = controller.Workbook.VisibleNames.ToList();
            return list.SelectedIndex >= 0 && list.SelectedIndex < names.Count ? names[list.SelectedIndex] : null;
        }

        return new SheetDialog("Name Manager",
            [list],
            [
                new SheetDialogButton("New...", d =>
                {
                    d.Next = DefineName(controller, null, reopenManager: true);
                    return true;
                }),
                new SheetDialogButton("Edit...", d =>
                {
                    if (Selected() is not { } name)
                    {
                        d.Error = "Select a name to edit.";
                        return false;
                    }

                    d.Next = DefineName(controller, name, reopenManager: true);
                    return true;
                }),
                new SheetDialogButton("Delete", d =>
                {
                    if (Selected() is not { } name)
                    {
                        d.Error = "Select a name to delete.";
                        return false;
                    }

                    controller.DeleteName(name.Name, name.Scope);
                    Refresh();
                    return false;
                }),
                new SheetDialogButton("Close", _ => true) { IsPrimary = true }
            ])
        {
            Width = 480
        };
    }

    /// <summary>Formulas ▸ Define Name, and Name Manager's New and Edit.</summary>
    public static SheetDialog DefineName(SpreadsheetController controller, DefinedNameInfo? existing, bool reopenManager = false)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var scopes = new List<string> { "Workbook" };
        scopes.AddRange(controller.Workbook.Sheets.Select(x => x.Name));

        var suggested = existing?.Name ?? SuggestName(controller);

        return new SheetDialog(existing is null ? "New Name" : "Edit Name",
            [
                Text("name", "Name", suggested),
                Choice("scope", "Scope", scopes, existing?.Scope is { } scope ? Math.Max(0, scopes.IndexOf(scope)) : 0),
                Text("comment", "Comment", existing?.Comment ?? string.Empty),
                Text("refers", "Refers to", "=" + (existing?.Formula ?? controller.SelectionReference))
            ],
            [
                Ok(d =>
                {
                    var name = d["name"].Text.Trim();
                    var refers = d["refers"].Text.Trim().TrimStart('=');
                    var scope = d["scope"].SelectedIndex <= 0 ? null : d["scope"].SelectedOption;

                    if (refers.Length == 0 || !FormulaParser.TryParse(refers, out _, out _))
                    {
                        d.Error = "The reference isn't valid.";
                        return false;
                    }

                    if (existing is null)
                    {
                        if (controller.Workbook.DefinedNames.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Scope, scope, StringComparison.OrdinalIgnoreCase)))
                        {
                            d.Error = $"The name '{name}' already exists. Enter a unique name.";
                            return false;
                        }

                        controller.DefineName(name, refers, scope, NullIfEmpty(d["comment"].Text));
                    }
                    else if (!string.Equals(existing.Scope, scope, StringComparison.OrdinalIgnoreCase))
                    {
                        controller.DeleteName(existing.Name, existing.Scope);
                        controller.DefineName(name, refers, scope, NullIfEmpty(d["comment"].Text));
                    }
                    else
                    {
                        controller.RenameName(existing.Name, existing.Scope, name, refers, NullIfEmpty(d["comment"].Text));
                    }

                    if (reopenManager)
                        d.Next = NameManager(controller);

                    return true;
                }),
                new SheetDialogButton("Cancel", d =>
                {
                    if (reopenManager)
                        d.Next = NameManager(controller);

                    return true;
                })
            ]);
    }

    /// <summary>Excel suggests the text above or to the left of the selection as its name.</summary>
    static string SuggestName(SpreadsheetController controller)
    {
        var active = controller.Selection.Range.TopLeft;
        foreach (var cell in new[] { active, active.Offset(0, -1), active.Offset(-1, 0) })
        {
            if (!cell.IsValid)
                continue;

            var value = controller.Sheet.GetDisplayValue(cell);
            if (value.Kind != CellValueKind.Text)
                continue;

            var candidate = new string(value.AsText().Trim().Select(c => char.IsLetterOrDigit(c) || c is '_' or '.' ? c : '_').ToArray());
            if (candidate.Length > 0 && char.IsDigit(candidate[0]))
                candidate = "_" + candidate;

            if (DefinedNameRules.IsValid(candidate, out _))
                return candidate;
        }

        return string.Empty;
    }

    // ---- sizes ----

    /// <summary>Home ▸ Format ▸ Row Height.</summary>
    public static SheetDialog RowHeight(SpreadsheetController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        var current = GridMetrics.PixelsToPoints(controller.Metrics.Rows.SizeOf(controller.Selection.Active.Row));

        return new SheetDialog("Row Height",
            [new SheetField("height", "Row height", SheetFieldKind.Number) { Text = Invariant(Math.Round(current, 2)) }],
            [Ok(d =>
            {
                if (d["height"].Number is not { } points || points < 0 || points > 409)
                {
                    d.Error = "Enter a height between 0 and 409 points.";
                    return false;
                }

                controller.SetRowHeight(points);
                return true;
            }), Cancel]);
    }

    /// <summary>Home ▸ Format ▸ Column Width.</summary>
    public static SheetDialog ColumnWidth(SpreadsheetController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        var current = GridMetrics.PixelsToWidth(controller.Metrics.Columns.SizeOf(controller.Selection.Active.Column));

        return new SheetDialog("Column Width",
            [new SheetField("width", "Column width", SheetFieldKind.Number) { Text = Invariant(current) }],
            [Ok(d =>
            {
                if (d["width"].Number is not { } width || width < 0 || width > 255)
                {
                    d.Error = "Enter a width between 0 and 255 characters.";
                    return false;
                }

                controller.SetColumnWidthCharacters(width);
                return true;
            }), Cancel]);
    }

    // ---- custom sort ----

    /// <summary>Data ▸ Sort: up to three levels, each a column and a direction.</summary>
    public static SheetDialog CustomSort(SpreadsheetController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var region = controller.Sheet.FilterAt(controller.Selection.Active)?.Range ?? controller.DataRegion;
        var header = SheetSort.DetectHeader(controller.Sheet, region);

        List<string> ColumnNames(bool useHeader)
            => Enumerable.Range(region.Left, region.ColumnCount)
                .Select(c => useHeader
                    ? SheetAutoFilter.DisplayText(controller.Sheet, new CellRef(c, region.Top)) is { Length: > 0 } text ? text : $"Column {CellRef.ColumnName(c)}"
                    : $"Column {CellRef.ColumnName(c)}")
                .ToList();

        var names = ColumnNames(header);
        var withNone = new List<string> { "(none)" };
        withNone.AddRange(names);
        var orders = new[] { "A to Z (Smallest to Largest)", "Z to A (Largest to Smallest)" };

        var activeIndex = Math.Clamp(controller.Selection.Active.Column - region.Left, 0, names.Count - 1);

        var fields = new List<SheetField>
        {
            Check("header", "My data has headers", header),
            Choice("by1", "Sort by", names, activeIndex),
            Choice("order1", "Order", orders),
            Choice("by2", "Then by", withNone),
            Choice("order2", "Order", orders),
            Choice("by3", "Then by", withNone),
            Choice("order3", "Order", orders)
        };

        return new SheetDialog("Sort", fields,
            [Ok(d =>
            {
                var keys = new List<SortKey>
                {
                    new(region.Left + Math.Max(0, d["by1"].SelectedIndex), d["order1"].SelectedIndex == 1)
                };

                for (var level = 2; level <= 3; level++)
                {
                    var by = d[$"by{level}"].SelectedIndex;
                    if (by > 0)
                        keys.Add(new SortKey(region.Left + by - 1, d[$"order{level}"].SelectedIndex == 1));
                }

                controller.Sort(keys, d["header"].IsChecked, region);
                return true;
            }), Cancel])
        {
            FieldChanged = (d, field) =>
            {
                if (field.Key != "header")
                    return;

                var renamed = ColumnNames(field.IsChecked);
                d["by1"].Options = renamed;
                d["by2"].Options = ["(none)", .. renamed];
                d["by3"].Options = ["(none)", .. renamed];
            }
        };
    }

    // ---- tables ----

    /// <summary>Format as Table's confirmation: the range, and whether its first row is headers.</summary>
    public static SheetDialog CreateTable(SpreadsheetController controller, string style)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var region = controller.DataRegion;
        static string Abs(CellRef c) => $"${CellRef.ColumnName(c.Column)}${c.Row + 1}";

        return new SheetDialog("Create Table",
            [
                Text("range", "Where is the data for your table?", $"={Abs(region.TopLeft)}:{Abs(region.BottomRight)}"),
                Check("header", "My table has headers", SheetSort.DetectHeader(controller.Sheet, region) || region.RowCount > 1)
            ],
            [Ok(d =>
            {
                var text = d["range"].Text.Trim().TrimStart('=').Replace("$", string.Empty, StringComparison.Ordinal);
                if (text.LastIndexOf('!') is var bang and >= 0)
                    text = text[(bang + 1)..];

                if (!CellRange.TryParse(text, out var range))
                {
                    d.Error = "The reference isn't valid.";
                    return false;
                }

                controller.Selection.SelectRange(range);
                controller.FormatAsTable(style, d["header"].IsChecked);
                return true;
            }), Cancel]);
    }

    // ---- go to / zoom ----

    /// <summary>Home ▸ Find &amp; Select ▸ Go To (Ctrl+G, F5).</summary>
    public static SheetDialog GoTo(SpreadsheetController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var names = controller.Workbook.VisibleNames.Select(x => x.Name).ToList();
        return new SheetDialog("Go To",
            [
                new SheetField("names", "Go to", SheetFieldKind.List) { Options = names, SelectedIndex = -1 },
                Text("reference", "Reference", placeholder: "A1, Sheet2!B5 or a name")
            ],
            [Ok(d =>
            {
                var target = NullIfEmpty(d["reference"].Text) ?? d["names"].SelectedOption;
                if (target is null || !controller.GoTo(target))
                {
                    d.Error = "Reference isn't valid.";
                    return false;
                }

                return true;
            }), Cancel])
        {
            FieldChanged = (d, field) =>
            {
                if (field.Key == "names" && field.SelectedOption is { } name)
                    d["reference"].Text = name;
            }
        };
    }

    static readonly string[] ZoomNames = ["200%", "100%", "75%", "50%", "25%", "Fit selection", "Custom"];
    static readonly double[] ZoomValues = [2, 1, 0.75, 0.5, 0.25, 0, 0];

    /// <summary>View ▸ Zoom.</summary>
    public static SheetDialog Zoom(SpreadsheetController controller, double viewWidth, double viewHeight)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var index = Array.FindIndex(ZoomValues, x => x > 0 && Math.Abs(x - controller.Zoom) < 0.001);
        return new SheetDialog("Zoom",
            [
                Choice("magnification", "Magnification", ZoomNames, index < 0 ? 6 : index),
                new SheetField("custom", "Custom (%)", SheetFieldKind.Number) { Text = Invariant(Math.Round(controller.Zoom * 100)) }
            ],
            [Ok(d =>
            {
                var pick = d["magnification"].SelectedIndex;
                if (pick == 5)
                {
                    var rect = controller.Viewport.RangeRect(controller.Selection.Range);
                    var available = Math.Max(1, viewWidth - controller.Metrics.RowHeaderWidth * controller.Zoom);
                    var tall = Math.Max(1, viewHeight - controller.Metrics.ColumnHeaderHeight * controller.Zoom);
                    controller.Zoom = Math.Min(available / Math.Max(1, rect.Width), tall / Math.Max(1, rect.Height));
                    controller.GoTo(controller.Selection.Range.TopLeft);
                    return true;
                }

                if (pick == 6)
                {
                    if (d["custom"].Number is not { } percent || percent < 10 || percent > 400)
                    {
                        d.Error = "Enter a whole number between 10 and 400.";
                        return false;
                    }

                    controller.Zoom = percent / 100;
                    return true;
                }

                controller.Zoom = ZoomValues[Math.Max(0, pick)];
                return true;
            }), Cancel]);
    }

    // ---- format cells ----

    static readonly string[] NumberCategories = ["General", "Number", "Currency", "Date", "Time", "Percentage", "Fraction", "Scientific", "Text", "Custom"];
    static readonly string[] DateCodes = ["m/d/yyyy", "d-mmm-yy", "d-mmm", "mmm-yy", "dddd, mmmm d, yyyy", "yyyy-mm-dd"];
    static readonly string[] TimeCodes = ["h:mm:ss", "h:mm AM/PM", "h:mm:ss AM/PM", "h:mm", "[h]:mm:ss"];
    static readonly string[] HorizontalNames = ["General", "Left", "Center", "Right", "Fill", "Justify", "Center Across Selection", "Distributed"];
    static readonly string[] VerticalNames = ["Top", "Center", "Bottom", "Justify", "Distributed"];
    static readonly string[] BorderPresetNames = ["(unchanged)", "None", "Outline", "Inside", "Outline and Inside"];
    static readonly string[] LineStyleNames = ["Thin", "Medium", "Thick", "Dashed", "Dotted", "Double", "Hair"];
    static readonly CellBorderStyle[] LineStyles = [CellBorderStyle.Thin, CellBorderStyle.Medium, CellBorderStyle.Thick, CellBorderStyle.Dashed, CellBorderStyle.Dotted, CellBorderStyle.Double, CellBorderStyle.Hair];
    static readonly string[] FontNames = ["Calibri", "Calibri Light", "Cambria", "Arial", "Times New Roman", "Georgia", "Verdana", "Courier New", "Consolas", "Segoe UI"];

    /// <summary>Format Cells (Ctrl+1): Number, Alignment, Font, Border and Fill.</summary>
    public static SheetDialog FormatCells(SpreadsheetController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var format = controller.ActiveFormat;
        var code = format.NumberFormatCode;
        var category = CategoryOf(code);
        var decimals = NumberFormats.DecimalsOf(code);

        var number = new List<SheetField>
        {
            Choice("category", "Category", NumberCategories, category),
            new("decimals", "Decimal places", SheetFieldKind.Number) { Text = decimals.ToString(CultureInfo.CurrentCulture) },
            Check("separator", "Use 1000 separator (,)", code.Contains(',')),
            Choice("dateType", "Type", DateCodes, Math.Max(0, Array.IndexOf(DateCodes, code))),
            Choice("timeType", "Type", TimeCodes, Math.Max(0, Array.IndexOf(TimeCodes, code))),
            Text("custom", "Type", string.IsNullOrEmpty(code) ? "General" : code),
            Label("sample", string.Empty)
        };

        var alignment = new List<SheetField>
        {
            Choice("horizontal", "Horizontal", HorizontalNames, (int)format.HorizontalAlignment),
            Choice("vertical", "Vertical", VerticalNames, (int)format.VerticalAlignment),
            new("indent", "Indent", SheetFieldKind.Number) { Text = format.Indent.ToString(CultureInfo.CurrentCulture) },
            Check("wrap", "Wrap text", format.WrapText),
            Check("merge", "Merge cells", controller.IsActiveCellMerged)
        };

        var fontIndex = Array.FindIndex(FontNames, x => string.Equals(x, format.FontName, StringComparison.OrdinalIgnoreCase));
        var fontOptions = fontIndex >= 0 ? FontNames : [format.FontName, .. FontNames];

        var font = new List<SheetField>
        {
            Choice("font", "Font", fontOptions, Math.Max(0, fontIndex)),
            new("size", "Size", SheetFieldKind.Number) { Text = Invariant(format.FontSize) },
            Check("bold", "Bold", format.Bold),
            Check("italic", "Italic", format.Italic),
            Check("underline", "Underline", format.Underline),
            Check("strike", "Strikethrough", format.Strike),
            new("fontColor", "Color", SheetFieldKind.Color) { Color = format.Foreground == ResolvedFormat.Default.Foreground ? null : format.Foreground }
        };

        var border = new List<SheetField>
        {
            Choice("borderPreset", "Presets", BorderPresetNames),
            Choice("lineStyle", "Line style", LineStyleNames),
            new("lineColor", "Color", SheetFieldKind.Color) { Color = null }
        };

        var fill = new List<SheetField>
        {
            new("fill", "Background color", SheetFieldKind.Color) { Color = format.Background.IsTransparent ? null : format.Background }
        };

        var dialog = new SheetDialog("Format Cells",
            [
                new SheetDialogTab("Number", number),
                new SheetDialogTab("Alignment", alignment),
                new SheetDialogTab("Font", font),
                new SheetDialogTab("Border", border),
                new SheetDialogTab("Fill", fill)
            ],
            [Ok(d =>
            {
                var code = CodeFrom(d);
                var size = d["size"].Number;
                if (size is not { } s || s < 1 || s > 409)
                {
                    d.Error = "Enter a font size between 1 and 409.";
                    return false;
                }

                // Only what was changed in the dialog is applied, so a mixed selection keeps what each cell
                // had in every property the user did not touch - as Excel's Format Cells does.
                static T? Changed<T>(T value, T original) where T : struct
                    => EqualityComparer<T>.Default.Equals(value, original) ? null : value;

                var indent = Math.Clamp((int)(d["indent"].Number ?? 0), 0, 250);
                var foreground = d["fontColor"].Color ?? ResolvedFormat.Default.Foreground;
                var background = d["fill"].Color ?? ArgbColor.Transparent;
                var fontName = d["font"].SelectedOption;

                var change = new CellFormatChange
                {
                    NumberFormatCode = string.Equals(code, format.NumberFormatCode, StringComparison.Ordinal) ? null : code,
                    HorizontalAlignment = Changed((CellHorizontalAlignment)Math.Max(0, d["horizontal"].SelectedIndex), format.HorizontalAlignment),
                    VerticalAlignment = Changed((CellVerticalAlignment)Math.Max(0, d["vertical"].SelectedIndex), format.VerticalAlignment),
                    Indent = Changed(indent, format.Indent),
                    WrapText = Changed(d["wrap"].IsChecked, format.WrapText),
                    FontName = string.Equals(fontName, format.FontName, StringComparison.OrdinalIgnoreCase) ? null : fontName,
                    FontSize = Math.Abs(s - format.FontSize) < 0.01 ? null : s,
                    Bold = Changed(d["bold"].IsChecked, format.Bold),
                    Italic = Changed(d["italic"].IsChecked, format.Italic),
                    Underline = Changed(d["underline"].IsChecked, format.Underline),
                    Strike = Changed(d["strike"].IsChecked, format.Strike),
                    Foreground = Changed(foreground, format.Foreground),
                    Background = Changed(background, format.Background)
                };

                var commands = new List<IEditCommand<Workbook>>();
                if (!change.IsEmpty)
                    commands.Add(new FormatRangeCommand(controller.Sheet.Name, controller.Selection.Range, change));

                var line = new BorderEdge(LineStyles[Math.Max(0, d["lineStyle"].SelectedIndex)], d["lineColor"].Color ?? ArgbColor.Transparent);
                var range = controller.Selection.Range;

                switch (d["borderPreset"].SelectedIndex)
                {
                    case 1:
                        commands.Add(BorderPresets.Build(controller.Sheet.Name, range, BorderPreset.None, line));
                        break;
                    case 2:
                        commands.Add(BorderPresets.Build(controller.Sheet.Name, range, BorderPreset.Outside, line));
                        break;
                    case 3:
                        commands.Add(BorderPresets.Build(controller.Sheet.Name, range, BorderPreset.InsideHorizontal, line));
                        commands.Add(BorderPresets.Build(controller.Sheet.Name, range, BorderPreset.InsideVertical, line));
                        break;
                    case 4:
                        commands.Add(BorderPresets.Build(controller.Sheet.Name, range, BorderPreset.All, line));
                        break;
                }

                if (commands.Count > 0)
                    controller.Run(new CompositeCommand<Workbook>("Format Cells", commands));

                var merge = d["merge"].IsChecked;
                if (merge && !controller.IsActiveCellMerged && !range.IsSingleCell)
                    controller.MergeCells(MergeMode.MergeCells);
                else if (!merge && controller.IsActiveCellMerged)
                    controller.UnmergeCells();

                return true;
            }), Cancel])
        {
            Width = 460,
            FieldChanged = (d, _) => LayoutNumber(d, controller)
        };

        LayoutNumber(dialog, controller);
        return dialog;
    }

    static int CategoryOf(string code)
    {
        if (string.IsNullOrEmpty(code) || code == "General")
            return 0;

        if (code == "@")
            return 8;

        if (code.Contains('%'))
            return 5;

        if (code.Contains("E+", StringComparison.OrdinalIgnoreCase))
            return 7;

        if (code.Contains('?') && code.Contains('/'))
            return 6;

        if (Array.IndexOf(DateCodes, code) >= 0)
            return 3;

        if (Array.IndexOf(TimeCodes, code) >= 0)
            return 4;

        if (NumberFormats.PresetOf(code) == NumberFormatPreset.Currency || code.Contains('$') || code.Contains('€') || code.Contains('£') || code.Contains("[$", StringComparison.Ordinal))
            return 2;

        if (code.Replace("#", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal).Replace("0", string.Empty, StringComparison.Ordinal).Replace(".", string.Empty, StringComparison.Ordinal).Length == 0)
            return 1;

        return 9;
    }

    static string CodeFrom(SheetDialog d)
    {
        var decimals = Math.Clamp((int)(d["decimals"].Number ?? 2), 0, 30);
        string Decimals(string code) => NumberFormats.AdjustDecimals(code, decimals - NumberFormats.DecimalsOf(code));

        return Math.Max(0, d["category"].SelectedIndex) switch
        {
            1 => Decimals(d["separator"].IsChecked ? "#,##0.00" : "0.00"),
            2 => Decimals(NumberFormats.CodeOf(NumberFormatPreset.Currency)),
            3 => d["dateType"].SelectedOption ?? DateCodes[0],
            4 => d["timeType"].SelectedOption ?? TimeCodes[0],
            5 => Decimals("0.00%"),
            6 => "# ?/?",
            7 => Decimals("0.00E+00"),
            8 => "@",
            9 => d["custom"].Text.Trim() is { Length: > 0 } custom && custom != "General" ? custom : string.Empty,
            _ => string.Empty
        };
    }

    static void LayoutNumber(SheetDialog d, SpreadsheetController controller)
    {
        var category = Math.Max(0, d["category"].SelectedIndex);
        d["decimals"].IsVisible = category is 1 or 2 or 5 or 7;
        d["separator"].IsVisible = category == 1;
        d["dateType"].IsVisible = category == 3;
        d["timeType"].IsVisible = category == 4;
        d["custom"].IsVisible = category == 9;

        // The sample is the active cell's value through the format being built, via the grid's own
        // formatter - so it is what the cell will actually show.
        var value = controller.Sheet.GetDisplayValue(controller.Selection.Active);
        var sample = value.IsBlank ? CellValue.FromNumber(1234.5) : value;
        var code = CodeFrom(d);
        d["sample"].Label = "Sample: " + controller.Workbook.Styles.Format(sample, ResolvedFormat.Default with { NumberFormatCode = code });
    }
}
