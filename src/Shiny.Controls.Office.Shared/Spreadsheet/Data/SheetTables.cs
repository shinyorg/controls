using System.Xml.Linq;
using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Spreadsheet.Commands;

namespace Shiny.Controls.Office.Spreadsheet;

/// <summary>An Excel table — a ListObject: a named range with a header row, a style and its own filter.</summary>
public sealed record SheetTable(string Name, CellRange Range, string StyleName)
{
    public bool ShowRowStripes { get; init; } = true;
    public bool ShowColumnStripes { get; init; }
    public bool ShowFirstColumn { get; init; }
    public bool ShowLastColumn { get; init; }
    public bool HasHeaderRow { get; init; } = true;
    public bool HasTotalsRow { get; init; }

    /// <summary>The table's filter, when it has its header row.</summary>
    public SheetAutoFilter? AutoFilter { get; init; }

    public IReadOnlyList<string> ColumnNames { get; init; } = [];

    /// <summary>The rows under the header and above the totals.</summary>
    public CellRange Body => new(
        new CellRef(this.Range.Left, this.Range.Top + (this.HasHeaderRow ? 1 : 0)),
        new CellRef(this.Range.Right, Math.Max(this.Range.Top, this.Range.Bottom - (this.HasTotalsRow ? 1 : 0))));
}

/// <summary>
/// The colours a table style paints with, derived from its name and the workbook's theme.
/// </summary>
/// <remarks>
/// Excel's sixty built-in table styles are not in the file — a table names one and every reader is
/// expected to know what "TableStyleMedium2" looks like. They fall into three families of seven
/// colours each (none, then accents one to six), and within a family the pattern is the same: a light
/// style underlines its header and tints alternate rows, a medium one fills its header with the accent,
/// a dark one fills the whole body. That structure is what this reproduces, near enough to read as the
/// style that was chosen.
/// </remarks>
public sealed record TableStylePalette(ArgbColor? HeaderFill, ArgbColor HeaderText, bool HeaderBold, ArgbColor? BandFill, ArgbColor? BodyFill, ArgbColor? BodyText, ArgbColor Rule)
{
    public static TableStylePalette For(string styleName, Func<int, ArgbColor> theme)
    {
        var (family, number) = Parse(styleName);

        // Variant 0 is the neutral one; 1-6 are the theme accents, in order.
        var variant = (Math.Max(1, number) - 1) % 7;
        var accent = variant == 0 ? new ArgbColor(255, 0x40, 0x40, 0x40) : theme(3 + variant);
        var tint = Tint(accent, 0.8);
        var black = new ArgbColor(255, 0, 0, 0);
        var white = new ArgbColor(255, 255, 255, 255);

        return family switch
        {
            "Light" when number <= 7 => new(null, black, true, Tint(accent, 0.8), null, null, accent),
            "Light" when number <= 14 => new(accent, white, true, null, null, null, accent),
            "Light" => new(null, black, true, Tint(accent, 0.8), null, null, accent),
            "Dark" => new(black, white, true, Shade(accent, 0.25), accent, white, black),
            _ when number >= 15 && number <= 21 => new(Shade(accent, 0.25), white, true, Tint(accent, 0.6), Tint(accent, 0.8), null, white),
            _ => new(accent, white, true, tint, null, null, Tint(accent, 0.4))
        };
    }

    static (string Family, int Number) Parse(string name)
    {
        foreach (var family in new[] { "Light", "Medium", "Dark" })
        {
            var prefix = "TableStyle" + family;
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(name[prefix.Length..], out var number))
                return (family, number);
        }

        return ("Medium", 2);
    }

    static ArgbColor Tint(ArgbColor color, double amount)
    {
        byte Lift(byte c) => (byte)Math.Round(c + (255 - c) * amount);
        return new ArgbColor(255, Lift(color.R), Lift(color.G), Lift(color.B));
    }

    static ArgbColor Shade(ArgbColor color, double amount)
    {
        byte Drop(byte c) => (byte)Math.Round(c * (1 - amount));
        return new ArgbColor(255, Drop(color.R), Drop(color.G), Drop(color.B));
    }

    /// <summary>The overlay a cell at this position in the table gets, under the cell's own format.</summary>
    public DxfFormat FormatAt(SheetTable table, CellRef cell)
    {
        if (table.HasHeaderRow && cell.Row == table.Range.Top)
            return new DxfFormat { Background = this.HeaderFill, Foreground = this.HeaderText, Bold = this.HeaderBold ? true : null };

        if (table.HasTotalsRow && cell.Row == table.Range.Bottom)
            return new DxfFormat { Bold = true, Borders = new CellBorders(null, null, new BorderEdge(CellBorderStyle.Double, this.Rule), null) };

        var bodyRow = cell.Row - table.Body.Top;
        var banded = table.ShowRowStripes && bodyRow % 2 == 0;
        var bandedColumn = table.ShowColumnStripes && (cell.Column - table.Range.Left) % 2 == 0;
        var fill = banded || bandedColumn ? this.BandFill ?? this.BodyFill : this.BodyFill;
        var bold = (table.ShowFirstColumn && cell.Column == table.Range.Left) || (table.ShowLastColumn && cell.Column == table.Range.Right);

        return new DxfFormat { Background = fill, Foreground = this.BodyText, Bold = bold ? true : null };
    }
}

/// <summary>Reads and writes table parts.</summary>
public static class SheetTables
{
    static readonly XNamespace Main = SheetXml.MainNamespace;

    /// <summary>The built-in styles the Format as Table gallery offers.</summary>
    public static IReadOnlyList<string> GalleryStyles { get; } =
    [
        "TableStyleLight1", "TableStyleLight8", "TableStyleLight9", "TableStyleLight10", "TableStyleLight11",
        "TableStyleMedium1", "TableStyleMedium2", "TableStyleMedium3", "TableStyleMedium4", "TableStyleMedium5",
        "TableStyleMedium6", "TableStyleMedium7", "TableStyleMedium9", "TableStyleMedium16",
        "TableStyleDark1", "TableStyleDark2", "TableStyleDark3"
    ];

    public static SheetTable? Parse(string xml)
    {
        var element = XElement.Parse(xml);
        if (element.Attribute("ref")?.Value is not { } reference || !CellRange.TryParse(reference, out var range))
            return null;

        var info = element.Element(Main + "tableStyleInfo");
        var header = element.Attribute("headerRowCount")?.Value != "0";
        var totals = element.Attribute("totalsRowCount")?.Value is { } count && count != "0";

        SheetAutoFilter? filter = null;
        if (element.Element(Main + "autoFilter") is { } af)
            filter = SheetAutoFilter.Parse(af.ToString(SaveOptions.DisableFormatting));

        var name = element.Attribute("displayName")?.Value ?? element.Attribute("name")?.Value ?? "Table";

        return new SheetTable(name, range, info?.Attribute("name")?.Value ?? "TableStyleMedium2")
        {
            ShowRowStripes = info?.Attribute("showRowStripes")?.Value is "1" or "true",
            ShowColumnStripes = info?.Attribute("showColumnStripes")?.Value is "1" or "true",
            ShowFirstColumn = info?.Attribute("showFirstColumn")?.Value is "1" or "true",
            ShowLastColumn = info?.Attribute("showLastColumn")?.Value is "1" or "true",
            HasHeaderRow = header,
            HasTotalsRow = totals,
            AutoFilter = filter is null ? null : filter with { TableName = name },
            ColumnNames = element.Element(Main + "tableColumns")?.Elements(Main + "tableColumn").Select(x => x.Attribute("name")?.Value ?? string.Empty).ToList() ?? []
        };
    }

    /// <summary>A new table part's XML.</summary>
    public static string ToXml(uint id, string name, CellRange range, IReadOnlyList<string> columns, string style)
    {
        var table = new XElement(Main + "table",
            new XAttribute("id", id),
            new XAttribute("name", name),
            new XAttribute("displayName", name),
            new XAttribute("ref", range.ToString()),
            new XAttribute("totalsRowShown", "0"),
            new XElement(Main + "autoFilter", new XAttribute("ref", range.ToString())),
            new XElement(Main + "tableColumns",
                new XAttribute("count", columns.Count),
                columns.Select((column, i) => new XElement(Main + "tableColumn", new XAttribute("id", i + 1), new XAttribute("name", column)))),
            new XElement(Main + "tableStyleInfo",
                new XAttribute("name", style),
                new XAttribute("showFirstColumn", "0"),
                new XAttribute("showLastColumn", "0"),
                new XAttribute("showRowStripes", "1"),
                new XAttribute("showColumnStripes", "0")));

        return table.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// The command that turns <paramref name="range"/> into a table styled <paramref name="style"/>.
    /// </summary>
    /// <remarks>
    /// A table's header cells <em>are</em> its column names — Excel refuses a file where a
    /// <c>tableColumn</c>'s name does not match the text in its header cell, and requires the names to be
    /// unique. Blank or repeated headers are therefore filled in as <c>Column1</c>, <c>Column2</c>… in the
    /// same undo step, exactly as Excel does when it converts a range with no header row.
    /// </remarks>
    public static IEditCommand<Workbook> Build(Worksheet sheet, CellRange range, string style, bool hasHeader)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var commands = new List<IEditCommand<Workbook>>();

        if (!hasHeader)
        {
            // No header in the data: one is inserted above it, as Excel does.
            commands.Add(new InsertRowsCommand(sheet.Name, range.Top, 1));
            range = new CellRange(range.TopLeft, new CellRef(range.Right, range.Bottom + 1));
        }

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var column = range.Left; column <= range.Right; column++)
        {
            var cell = new CellRef(column, range.Top);
            var text = hasHeader ? AutoFilterText(sheet, cell) : string.Empty;
            var name = text;
            var n = column - range.Left + 1;

            while (name.Length == 0 || !seen.Add(name))
                name = string.IsNullOrEmpty(text) ? $"Column{n++}" : $"{text}{++n}";

            names.Add(name);

            if (!string.Equals(name, text, StringComparison.Ordinal))
                commands.Add(new SetCellValueCommand(sheet.Name, cell, CellValue.FromText(name)));
        }

        var tableName = NextName(sheet.Workbook);
        commands.Add(new AddTableCommand(sheet.Name, ToXml(NextId(sheet.Workbook), tableName, range, names, style)));
        return new CompositeCommand<Workbook>("Format as Table", commands);
    }

    static string AutoFilterText(Worksheet sheet, CellRef cell)
    {
        var value = sheet.GetDisplayValue(cell);
        return value.IsBlank ? string.Empty : Calc.Coercion.ToText(value).Trim();
    }

    static uint NextId(Workbook workbook)
        => (uint)workbook.Sheets.SelectMany(x => x.TableIds()).DefaultIfEmpty(0u).Max() + 1;

    static string NextName(Workbook workbook)
    {
        var taken = new HashSet<string>(workbook.Sheets.SelectMany(x => x.Tables).Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var name in workbook.DefinedNames)
            taken.Add(name.Name);

        for (var i = 1; ; i++)
        {
            var name = "Table" + i;
            if (!taken.Contains(name))
                return name;
        }
    }
}

/// <summary>Adds a table part to a sheet. The inverse removes it.</summary>
public sealed class AddTableCommand(string sheetName, string tableXml) : IEditCommand<Workbook>
{
    public string SheetName { get; } = sheetName;
    public string TableXml { get; } = tableXml;

    public string Name => "Add Table";

    public IEditCommand<Workbook> Apply(Workbook context)
    {
        var name = context[this.SheetName].AddTablePart(this.TableXml);
        return new RemoveTableCommand(this.SheetName, name);
    }
}

/// <summary>Removes a table part — the table, not its cells — keeping its XML for undo.</summary>
public sealed class RemoveTableCommand(string sheetName, string tableName) : IEditCommand<Workbook>
{
    public string SheetName { get; } = sheetName;
    public string TableName { get; } = tableName;

    public string Name => "Convert to Range";

    public IEditCommand<Workbook> Apply(Workbook context)
    {
        var xml = context[this.SheetName].RemoveTablePart(this.TableName);
        return xml is null
            ? new CompositeCommand<Workbook>(this.Name, [])
            : new AddTableCommand(this.SheetName, xml);
    }
}
