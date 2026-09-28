using System.Xml.Linq;
using Shiny.Controls.Office.Spreadsheet.Calc;

namespace Shiny.Controls.Office.Spreadsheet;

/// <summary>What a validation rule allows — Excel's <c>ST_DataValidationType</c>.</summary>
public enum ValidationType
{
    Any,
    Whole,
    Decimal,
    List,
    Date,
    Time,
    TextLength,
    Custom
}

/// <summary>The comparison a whole, decimal, date or length rule makes.</summary>
public enum ValidationOperator
{
    Between,
    NotBetween,
    Equal,
    NotEqual,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual
}

/// <summary>What happens to input that breaks a rule.</summary>
public enum ValidationErrorStyle
{
    /// <summary>Rejected outright. The default, and the only one that actually stops anything.</summary>
    Stop,

    /// <summary>The user is warned and may keep it.</summary>
    Warning,

    /// <summary>The user is told and it is kept.</summary>
    Information
}

/// <summary>One <c>&lt;dataValidation&gt;</c>: a rule, and the ranges it applies to.</summary>
public sealed record DataValidationRule(ValidationType Type)
{
    public IReadOnlyList<CellRange> Ranges { get; init; } = [];

    public ValidationOperator Operator { get; init; } = ValidationOperator.Between;

    /// <summary>
    /// The first bound — or, for a list, the items: a quoted comma list (<c>"Red,Green,Blue"</c>) or a
    /// reference (<c>$A$1:$A$5</c>). No leading <c>=</c>.
    /// </summary>
    public string? Formula1 { get; init; }

    public string? Formula2 { get; init; }

    public bool AllowBlank { get; init; } = true;

    /// <summary>
    /// Whether a list rule offers its in-cell dropdown.
    /// </summary>
    /// <remarks>
    /// Stored inverted in the file: <c>showDropDown="1"</c> means <em>hide</em> it, one of the schema's
    /// better-known traps. This property is the sensible way round, and the reader and writer flip it.
    /// </remarks>
    public bool InCellDropDown { get; init; } = true;

    public bool ShowErrorMessage { get; init; } = true;
    public bool ShowInputMessage { get; init; } = true;
    public ValidationErrorStyle ErrorStyle { get; init; } = ValidationErrorStyle.Stop;
    public string? ErrorTitle { get; init; }
    public string? Error { get; init; }
    public string? PromptTitle { get; init; }
    public string? Prompt { get; init; }

    public bool Covers(CellRef cell) => this.Ranges.Any(x => x.Contains(cell));

    /// <summary>A list rule over literal items.</summary>
    public static DataValidationRule ForList(IEnumerable<string> items)
        => new(ValidationType.List) { Formula1 = "\"" + string.Join(",", items).Replace("\"", "\"\"") + "\"" };

    /// <summary>A list rule whose items are read from a range, e.g. <c>$A$1:$A$5</c>.</summary>
    public static DataValidationRule ForListRange(string reference)
        => new(ValidationType.List) { Formula1 = reference.TrimStart('=') };

    public static DataValidationRule ForNumber(ValidationType type, ValidationOperator op, string formula1, string? formula2 = null)
        => new(type) { Operator = op, Formula1 = formula1, Formula2 = formula2 };

    public static DataValidationRule ForCustom(string formula)
        => new(ValidationType.Custom) { Formula1 = formula.TrimStart('=') };

    /// <summary>The message shown when input is refused — the rule's own, or Excel's default wording.</summary>
    public string ErrorMessageText => string.IsNullOrWhiteSpace(this.Error)
        ? "This value doesn't match the data validation restrictions defined for this cell."
        : this.Error!;

    public string ErrorTitleText => string.IsNullOrWhiteSpace(this.ErrorTitle) ? "Microsoft Excel" : this.ErrorTitle!;
}

/// <summary>The result of checking a value against a cell's validation.</summary>
public sealed record ValidationOutcome(bool IsValid, DataValidationRule? Rule)
{
    public static readonly ValidationOutcome Valid = new(true, null);

    /// <summary>True when the value breaks a rule that refuses it, rather than one that only warns.</summary>
    public bool IsRejected => !this.IsValid && this.Rule is { ErrorStyle: ValidationErrorStyle.Stop, ShowErrorMessage: true };
}

/// <summary>Reads, writes and checks data validation.</summary>
public static class DataValidation
{
    static readonly XNamespace Main = SheetXml.MainNamespace;

    public static IReadOnlyList<DataValidationRule> Parse(string? xml)
    {
        if (string.IsNullOrEmpty(xml))
            return [];

        var root = XElement.Parse(xml);
        var rules = new List<DataValidationRule>();

        foreach (var element in root.Elements(Main + "dataValidation"))
        {
            var type = element.Attribute("type")?.Value switch
            {
                "whole" => ValidationType.Whole,
                "decimal" => ValidationType.Decimal,
                "list" => ValidationType.List,
                "date" => ValidationType.Date,
                "time" => ValidationType.Time,
                "textLength" => ValidationType.TextLength,
                "custom" => ValidationType.Custom,
                _ => ValidationType.Any
            };

            rules.Add(new DataValidationRule(type)
            {
                Ranges = ConditionalFormatting.ParseSqref(element.Attribute("sqref")?.Value),
                Operator = element.Attribute("operator")?.Value switch
                {
                    "notBetween" => ValidationOperator.NotBetween,
                    "equal" => ValidationOperator.Equal,
                    "notEqual" => ValidationOperator.NotEqual,
                    "greaterThan" => ValidationOperator.GreaterThan,
                    "lessThan" => ValidationOperator.LessThan,
                    "greaterThanOrEqual" => ValidationOperator.GreaterThanOrEqual,
                    "lessThanOrEqual" => ValidationOperator.LessThanOrEqual,
                    _ => ValidationOperator.Between
                },
                Formula1 = element.Element(Main + "formula1")?.Value,
                Formula2 = element.Element(Main + "formula2")?.Value,
                AllowBlank = IsTrue(element.Attribute("allowBlank")),
                InCellDropDown = !IsTrue(element.Attribute("showDropDown")),
                ShowErrorMessage = IsTrue(element.Attribute("showErrorMessage")),
                ShowInputMessage = IsTrue(element.Attribute("showInputMessage")),
                ErrorStyle = element.Attribute("errorStyle")?.Value switch
                {
                    "warning" => ValidationErrorStyle.Warning,
                    "information" => ValidationErrorStyle.Information,
                    _ => ValidationErrorStyle.Stop
                },
                ErrorTitle = element.Attribute("errorTitle")?.Value,
                Error = element.Attribute("error")?.Value,
                PromptTitle = element.Attribute("promptTitle")?.Value,
                Prompt = element.Attribute("prompt")?.Value
            });
        }

        return rules;
    }

    static bool IsTrue(XAttribute? attribute) => attribute?.Value is "1" or "true";

    /// <summary>The <c>&lt;dataValidations&gt;</c> element, or nothing for an empty list.</summary>
    public static IReadOnlyList<string> ToXml(IReadOnlyList<DataValidationRule> rules)
    {
        var kept = rules.Where(x => x.Ranges.Count > 0).ToList();
        if (kept.Count == 0)
            return [];

        var root = new XElement(Main + "dataValidations", new XAttribute("count", kept.Count));

        foreach (var rule in kept)
        {
            var element = new XElement(Main + "dataValidation");

            if (rule.Type != ValidationType.Any)
            {
                element.Add(new XAttribute("type", rule.Type switch
                {
                    ValidationType.Whole => "whole",
                    ValidationType.Decimal => "decimal",
                    ValidationType.List => "list",
                    ValidationType.Date => "date",
                    ValidationType.Time => "time",
                    ValidationType.TextLength => "textLength",
                    _ => "custom"
                }));
            }

            if (rule.ErrorStyle != ValidationErrorStyle.Stop)
                element.Add(new XAttribute("errorStyle", rule.ErrorStyle == ValidationErrorStyle.Warning ? "warning" : "information"));

            if (rule.Type is not (ValidationType.List or ValidationType.Custom or ValidationType.Any) && rule.Operator != ValidationOperator.Between)
            {
                element.Add(new XAttribute("operator", rule.Operator switch
                {
                    ValidationOperator.NotBetween => "notBetween",
                    ValidationOperator.Equal => "equal",
                    ValidationOperator.NotEqual => "notEqual",
                    ValidationOperator.GreaterThan => "greaterThan",
                    ValidationOperator.LessThan => "lessThan",
                    ValidationOperator.GreaterThanOrEqual => "greaterThanOrEqual",
                    _ => "lessThanOrEqual"
                }));
            }

            if (rule.AllowBlank)
                element.Add(new XAttribute("allowBlank", "1"));

            if (rule.Type == ValidationType.List && !rule.InCellDropDown)
                element.Add(new XAttribute("showDropDown", "1"));

            if (rule.ShowInputMessage)
                element.Add(new XAttribute("showInputMessage", "1"));

            if (rule.ShowErrorMessage)
                element.Add(new XAttribute("showErrorMessage", "1"));

            if (!string.IsNullOrEmpty(rule.ErrorTitle))
                element.Add(new XAttribute("errorTitle", rule.ErrorTitle));

            if (!string.IsNullOrEmpty(rule.Error))
                element.Add(new XAttribute("error", rule.Error));

            if (!string.IsNullOrEmpty(rule.PromptTitle))
                element.Add(new XAttribute("promptTitle", rule.PromptTitle));

            if (!string.IsNullOrEmpty(rule.Prompt))
                element.Add(new XAttribute("prompt", rule.Prompt));

            element.Add(new XAttribute("sqref", string.Join(' ', rule.Ranges.Select(x => x.ToString()))));

            if (rule.Formula1 is not null)
                element.Add(new XElement(Main + "formula1", rule.Formula1));

            if (rule.Formula2 is not null)
                element.Add(new XElement(Main + "formula2", rule.Formula2));

            root.Add(element);
        }

        return [root.ToString(SaveOptions.DisableFormatting)];
    }

    /// <summary>The rules with <paramref name="area"/> cut out of each, dropping any left empty.</summary>
    public static IReadOnlyList<DataValidationRule> WithoutArea(IReadOnlyList<DataValidationRule> rules, CellRange area)
        => rules
            .Select(rule => rule with { Ranges = rule.Ranges.SelectMany(x => ConditionalFormatting.Subtract(x, area)).ToList() })
            .Where(rule => rule.Ranges.Count > 0)
            .ToList();

    /// <summary>The items a list rule offers, evaluated against the live workbook.</summary>
    public static IReadOnlyList<string> ListItems(Worksheet sheet, DataValidationRule rule)
    {
        var formula = rule.Formula1?.Trim();
        if (string.IsNullOrEmpty(formula))
            return [];

        if (formula.StartsWith('"'))
        {
            return formula.Trim('"')
                .Replace("\"\"", "\"")
                .Split(',')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();
        }

        var origin = rule.Ranges.Count > 0 ? rule.Ranges[0].TopLeft : default;
        return sheet.Workbook.EvaluateValues(formula, sheet.Name, origin)
            .Where(x => !x.IsBlank)
            .Select(Coercion.ToText)
            .Distinct(StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>Checks a proposed value against whatever rule covers <paramref name="cell"/>.</summary>
    public static ValidationOutcome Check(Worksheet sheet, CellRef cell, CellValue value)
    {
        var rule = sheet.ValidationAt(cell);
        if (rule is null || rule.Type == ValidationType.Any)
            return ValidationOutcome.Valid;

        if (value.IsBlank)
            return rule.AllowBlank ? ValidationOutcome.Valid : new ValidationOutcome(false, rule);

        var origin = rule.Ranges.FirstOrDefault(x => x.Contains(cell)).TopLeft;

        CellValue Bound(string? formula)
        {
            if (string.IsNullOrWhiteSpace(formula))
                return CellValue.Blank;

            // Relative references in a rule are relative to the top-left of the range it covers.
            var shifted = FormulaReferenceShifter.Translate(formula, cell.Column - origin.Column, cell.Row - origin.Row);
            return sheet.Workbook.Evaluate(shifted, sheet.Name, cell);
        }

        bool ok;
        switch (rule.Type)
        {
            case ValidationType.List:
                var text = Coercion.ToText(value);
                ok = ListItems(sheet, rule).Any(x => string.Equals(x, text, StringComparison.CurrentCultureIgnoreCase));
                break;

            case ValidationType.Custom:
                var result = Bound(rule.Formula1);
                ok = Coercion.TryToBoolean(result, out var truth, out _) && truth;
                break;

            case ValidationType.TextLength:
                ok = Compare(Coercion.ToText(value).Length, rule, Bound);
                break;

            default:
                if (value.Kind != CellValueKind.Number)
                {
                    ok = false;
                    break;
                }

                var number = value.AsNumber();
                ok = (rule.Type != ValidationType.Whole || Math.Abs(number - Math.Round(number)) < 1e-9) && Compare(number, rule, Bound);
                break;
        }

        return ok ? ValidationOutcome.Valid : new ValidationOutcome(false, rule);
    }

    static bool Compare(double number, DataValidationRule rule, Func<string?, CellValue> bound)
    {
        static double? Num(CellValue value) => Coercion.TryToNumber(value, out var n, out _) ? n : null;

        var a = Num(bound(rule.Formula1));
        var b = Num(bound(rule.Formula2));

        if (a is null)
            return true;

        return rule.Operator switch
        {
            ValidationOperator.Between => b is null || (number >= Math.Min(a.Value, b.Value) && number <= Math.Max(a.Value, b.Value)),
            ValidationOperator.NotBetween => b is not null && (number < Math.Min(a.Value, b.Value) || number > Math.Max(a.Value, b.Value)),
            ValidationOperator.Equal => number == a,
            ValidationOperator.NotEqual => number != a,
            ValidationOperator.GreaterThan => number > a,
            ValidationOperator.LessThan => number < a,
            ValidationOperator.GreaterThanOrEqual => number >= a,
            _ => number <= a
        };
    }
}
