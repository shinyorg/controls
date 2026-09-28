using System.Globalization;
using System.Xml.Linq;
using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Spreadsheet.Calc;

namespace Shiny.Controls.Office.Spreadsheet;

/// <summary>The comparisons a custom filter offers.</summary>
public enum FilterOperator
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Contains,
    DoesNotContain,
    BeginsWith,
    EndsWith
}

/// <summary>One condition of a custom filter.</summary>
public sealed record FilterCondition(FilterOperator Operator, string Value);

/// <summary>
/// What one column of a filter keeps: a list of values, or up to two conditions.
/// </summary>
/// <param name="Column">The sheet column, absolute.</param>
public sealed record ColumnFilter(int Column)
{
    /// <summary>The displayed values to keep, or null for a condition filter.</summary>
    public IReadOnlySet<string>? Values { get; init; }

    /// <summary>Keep rows whose cell in this column is blank, for a value filter.</summary>
    public bool IncludeBlanks { get; init; }

    public FilterCondition? First { get; init; }
    public FilterCondition? Second { get; init; }

    /// <summary>Both conditions must hold, rather than either.</summary>
    public bool MatchAll { get; init; } = true;

    public static ColumnFilter ForValues(int column, IEnumerable<string> values, bool includeBlanks = false)
        => new(column) { Values = new HashSet<string>(values, StringComparer.OrdinalIgnoreCase), IncludeBlanks = includeBlanks };

    public static ColumnFilter ForCondition(int column, FilterCondition first, FilterCondition? second = null, bool matchAll = true)
        => new(column) { First = first, Second = second, MatchAll = matchAll };
}

/// <summary>
/// A sheet's AutoFilter: the range carrying the dropdown arrows, and what each filtered column keeps.
/// </summary>
/// <param name="Range">Header row included, the way the file's <c>ref</c> is.</param>
public sealed record SheetAutoFilter(CellRange Range, IReadOnlyList<ColumnFilter> Columns)
{
    /// <summary>The table whose filter this is, or null for the sheet's own.</summary>
    public string? TableName { get; init; }

    public ColumnFilter? For(int column) => this.Columns.FirstOrDefault(x => x.Column == column);

    public bool IsFiltering => this.Columns.Count > 0;

    /// <summary>The same filter with <paramref name="column"/> replaced, or removed with null.</summary>
    public SheetAutoFilter With(int column, ColumnFilter? filter)
    {
        var columns = this.Columns.Where(x => x.Column != column).ToList();
        if (filter is not null)
            columns.Add(filter);

        return this with { Columns = columns.OrderBy(x => x.Column).ToList() };
    }

    // ---- reading and writing ----

    static readonly XNamespace Main = SheetXml.MainNamespace;

    /// <summary>Reads an <c>&lt;autoFilter&gt;</c> fragment.</summary>
    public static SheetAutoFilter? Parse(string? xml)
    {
        if (string.IsNullOrEmpty(xml))
            return null;

        var element = XElement.Parse(xml);
        if (element.Attribute("ref")?.Value is not { } reference || !CellRange.TryParse(reference, out var range))
            return null;

        var columns = new List<ColumnFilter>();

        foreach (var filterColumn in element.Elements(Main + "filterColumn"))
        {
            if (!int.TryParse(filterColumn.Attribute("colId")?.Value, out var offset))
                continue;

            var column = range.Left + offset;

            if (filterColumn.Element(Main + "filters") is { } filters)
            {
                var values = filters.Elements(Main + "filter").Select(x => x.Attribute("val")?.Value ?? string.Empty);
                var blanks = filters.Attribute("blank")?.Value is "1" or "true";
                columns.Add(ColumnFilter.ForValues(column, values, blanks));
            }
            else if (filterColumn.Element(Main + "customFilters") is { } custom)
            {
                var conditions = custom.Elements(Main + "customFilter")
                    .Select(x => ReadCondition(x.Attribute("operator")?.Value, x.Attribute("val")?.Value ?? string.Empty))
                    .ToList();

                if (conditions.Count == 0)
                    continue;

                columns.Add(ColumnFilter.ForCondition(
                    column,
                    conditions[0],
                    conditions.ElementAtOrDefault(1),
                    custom.Attribute("and")?.Value is "1" or "true"));
            }
        }

        return new SheetAutoFilter(range, columns);
    }

    /// <summary>
    /// Wildcards are how the file spells "contains": <c>equal</c> to <c>*abc*</c>. Reading them back
    /// into the operator the user picked keeps the dialog showing what they chose.
    /// </summary>
    static FilterCondition ReadCondition(string? op, string value)
    {
        var starts = value.StartsWith('*');
        var ends = value.EndsWith('*') && value.Length > 1;
        var inner = value.Trim('*');

        return op switch
        {
            "notEqual" when starts && ends => new FilterCondition(FilterOperator.DoesNotContain, inner),
            "notEqual" => new FilterCondition(FilterOperator.NotEqual, value),
            "greaterThan" => new FilterCondition(FilterOperator.GreaterThan, value),
            "greaterThanOrEqual" => new FilterCondition(FilterOperator.GreaterThanOrEqual, value),
            "lessThan" => new FilterCondition(FilterOperator.LessThan, value),
            "lessThanOrEqual" => new FilterCondition(FilterOperator.LessThanOrEqual, value),
            _ when starts && ends => new FilterCondition(FilterOperator.Contains, inner),
            _ when ends && !starts => new FilterCondition(FilterOperator.BeginsWith, inner),
            _ when starts && !ends => new FilterCondition(FilterOperator.EndsWith, inner),
            _ => new FilterCondition(FilterOperator.Equal, value)
        };
    }

    /// <summary>The <c>&lt;autoFilter&gt;</c> element as the file stores it.</summary>
    public string ToXml()
    {
        var element = new XElement(Main + "autoFilter", new XAttribute("ref", this.Range.ToString()));

        foreach (var column in this.Columns.OrderBy(x => x.Column))
        {
            var filterColumn = new XElement(Main + "filterColumn", new XAttribute("colId", column.Column - this.Range.Left));

            if (column.Values is { } values)
            {
                var filters = new XElement(Main + "filters");
                if (column.IncludeBlanks)
                    filters.Add(new XAttribute("blank", "1"));

                foreach (var value in values.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
                    filters.Add(new XElement(Main + "filter", new XAttribute("val", value)));

                filterColumn.Add(filters);
            }
            else if (column.First is { } first)
            {
                var custom = new XElement(Main + "customFilters");
                if (column.Second is not null && column.MatchAll)
                    custom.Add(new XAttribute("and", "1"));

                custom.Add(WriteCondition(first));
                if (column.Second is { } second)
                    custom.Add(WriteCondition(second));

                filterColumn.Add(custom);
            }

            element.Add(filterColumn);
        }

        return element.ToString(SaveOptions.DisableFormatting);
    }

    static XElement WriteCondition(FilterCondition condition)
    {
        var (op, value) = condition.Operator switch
        {
            FilterOperator.NotEqual => ("notEqual", condition.Value),
            FilterOperator.GreaterThan => ("greaterThan", condition.Value),
            FilterOperator.GreaterThanOrEqual => ("greaterThanOrEqual", condition.Value),
            FilterOperator.LessThan => ("lessThan", condition.Value),
            FilterOperator.LessThanOrEqual => ("lessThanOrEqual", condition.Value),
            FilterOperator.Contains => (null, $"*{condition.Value}*"),
            FilterOperator.DoesNotContain => ("notEqual", $"*{condition.Value}*"),
            FilterOperator.BeginsWith => (null, $"{condition.Value}*"),
            FilterOperator.EndsWith => (null, $"*{condition.Value}"),
            _ => ((string?)null, condition.Value)
        };

        var element = new XElement(Main + "customFilter", new XAttribute("val", value));
        if (op is not null)
            element.Add(new XAttribute("operator", op));

        return element;
    }

    // ---- evaluation ----

    /// <summary>Whether a data row passes every column's filter.</summary>
    public bool Keeps(Worksheet sheet, int row)
    {
        foreach (var column in this.Columns)
        {
            if (!Passes(sheet, column, new CellRef(column.Column, row)))
                return false;
        }

        return true;
    }

    /// <summary>The text a value filter compares against: what the cell shows, as Excel matches it.</summary>
    public static string DisplayText(Worksheet sheet, CellRef cell)
    {
        var value = sheet.GetDisplayValue(cell);
        if (value.IsBlank)
            return string.Empty;

        var styles = sheet.Workbook.Styles;
        return styles.Format(value, styles.Resolve(sheet.GetEffectiveStyleIndex(cell)));
    }

    static bool Passes(Worksheet sheet, ColumnFilter filter, CellRef cell)
    {
        var text = DisplayText(sheet, cell);

        if (filter.Values is { } values)
            return text.Length == 0 ? filter.IncludeBlanks : values.Contains(text);

        if (filter.First is not { } first)
            return true;

        var value = sheet.GetDisplayValue(cell);
        var a = Test(first, value, text);

        if (filter.Second is not { } second)
            return a;

        var b = Test(second, value, text);
        return filter.MatchAll ? a && b : a || b;
    }

    static bool Test(FilterCondition condition, CellValue value, string text)
    {
        var target = condition.Value ?? string.Empty;
        var numeric = double.TryParse(target, NumberStyles.Float, CultureInfo.CurrentCulture, out var number)
                      || double.TryParse(target, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

        int Compare()
        {
            if (numeric && value.Kind == CellValueKind.Number)
                return value.AsNumber().CompareTo(number);

            return string.Compare(text, target, StringComparison.CurrentCultureIgnoreCase);
        }

        bool Wild(string pattern) => ConditionalFunctions.WildcardToRegex(pattern).IsMatch(text);

        return condition.Operator switch
        {
            FilterOperator.Equal => target.Contains('*') || target.Contains('?') ? Wild(target) : Compare() == 0,
            FilterOperator.NotEqual => target.Contains('*') || target.Contains('?') ? !Wild(target) : Compare() != 0,
            FilterOperator.GreaterThan => !value.IsBlank && Compare() > 0,
            FilterOperator.GreaterThanOrEqual => !value.IsBlank && Compare() >= 0,
            FilterOperator.LessThan => !value.IsBlank && Compare() < 0,
            FilterOperator.LessThanOrEqual => !value.IsBlank && Compare() <= 0,
            FilterOperator.Contains => text.Contains(target, StringComparison.CurrentCultureIgnoreCase),
            FilterOperator.DoesNotContain => !text.Contains(target, StringComparison.CurrentCultureIgnoreCase),
            FilterOperator.BeginsWith => text.StartsWith(target, StringComparison.CurrentCultureIgnoreCase),
            FilterOperator.EndsWith => text.EndsWith(target, StringComparison.CurrentCultureIgnoreCase),
            _ => true
        };
    }

    /// <summary>The distinct displayed values in a column of the filter's data, for the value checklist.</summary>
    public IReadOnlyList<string> DistinctValues(Worksheet sheet, int column, out bool hasBlanks)
    {
        var set = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        hasBlanks = false;

        for (var row = this.Range.Top + 1; row <= this.Range.Bottom; row++)
        {
            var text = DisplayText(sheet, new CellRef(column, row));
            if (text.Length == 0)
                hasBlanks = true;
            else
                set.Add(text);
        }

        return set.ToList();
    }
}

/// <summary>
/// Sets a sheet's (or a table's) AutoFilter and hides the rows it rejects, as one undo step.
/// </summary>
/// <remarks>
/// <para>
/// A filter is two things in the file, and both have to move together: the <c>&lt;autoFilter&gt;</c>
/// element that says what is being kept, and <c>hidden="1"</c> on every row that is not. Excel does not
/// re-run the filter when it opens a file — it trusts the hidden rows — so writing one without the
/// other opens as a filter that has not filtered anything.
/// </para>
/// <para>
/// The inverse is the same command, holding the element and the per-row states it replaced, so undo
/// restores both exactly.
/// </para>
/// </remarks>
public sealed class AutoFilterStateCommand : IEditCommand<Workbook>
{
    public AutoFilterStateCommand(string sheetName, string? tableName, string? autoFilterXml, IReadOnlyDictionary<int, bool> rowHidden, string? name = null)
    {
        this.SheetName = sheetName;
        this.TableName = tableName;
        this.AutoFilterXml = autoFilterXml;
        this.RowHidden = rowHidden;
        this.Name = name ?? "Filter";
    }

    public string SheetName { get; }

    /// <summary>The table whose filter this is, or null for the sheet's own.</summary>
    public string? TableName { get; }

    /// <summary>The element to write, or null to take the filter away.</summary>
    public string? AutoFilterXml { get; }

    public IReadOnlyDictionary<int, bool> RowHidden { get; }

    public string Name { get; }

    /// <summary>
    /// The command that puts <paramref name="filter"/> on the sheet and hides what it rejects — or,
    /// given null for the filter, removes the one at <paramref name="scope"/> and shows its rows again.
    /// </summary>
    public static AutoFilterStateCommand For(Worksheet sheet, SheetAutoFilter? filter, CellRange? scope = null, string? tableName = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var hidden = new Dictionary<int, bool>();
        var range = filter?.Range ?? scope;

        if (range is { } r)
        {
            for (var row = r.Top + 1; row <= r.Bottom; row++)
                hidden[row] = filter is not null && !filter.Keeps(sheet, row);
        }

        return new AutoFilterStateCommand(sheet.Name, tableName ?? filter?.TableName, filter?.ToXml(), hidden, filter is null ? "Remove Filter" : "Filter");
    }

    public IEditCommand<Workbook> Apply(Workbook context)
    {
        var sheet = context[this.SheetName];

        string? previousXml;
        if (this.TableName is { } table)
        {
            previousXml = sheet.ReadTableAutoFilter(table);
            sheet.WriteTableAutoFilter(table, this.AutoFilterXml);
        }
        else
        {
            previousXml = sheet.ReadElements("autoFilter").FirstOrDefault();
            sheet.ReplaceElements("autoFilter", this.AutoFilterXml is null ? [] : [this.AutoFilterXml]);
        }

        var previousHidden = new Dictionary<int, bool>(this.RowHidden.Count);
        foreach (var (row, hidden) in this.RowHidden)
        {
            var was = sheet.IsRowHidden(row);
            previousHidden[row] = was;

            if (was != hidden)
                sheet.WriteRowHidden(row, hidden);
        }

        return new AutoFilterStateCommand(this.SheetName, this.TableName, previousXml, previousHidden, this.Name);
    }
}
