using System.Globalization;
using System.Security;
using System.Text;
using System.Xml.Linq;
using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Spreadsheet.Calc;

namespace Shiny.Controls.Office.Spreadsheet;

/// <summary>The chart types the model reads, writes and draws.</summary>
public enum ChartKind
{
    Column,
    Bar,
    Line,
    Pie,
    Area,
    Scatter,

    /// <summary>A chart this model cannot draw — kept in the file, shown as a placeholder.</summary>
    Unknown
}

/// <summary>One series: its name, its category labels and its values, each a reference into the sheet.</summary>
public sealed record ChartSeries(string? NameFormula, string? CategoriesFormula, string? ValuesFormula)
{
    /// <summary>The name the file cached, used when the reference cannot be evaluated.</summary>
    public string? CachedName { get; init; }
    public IReadOnlyList<string> CachedCategories { get; init; } = [];
    public IReadOnlyList<double?> CachedValues { get; init; } = [];
    public ArgbColor? Color { get; init; }
}

/// <summary>
/// Where a chart sits: the cell its top-left corner is in and how far into it, and the same for the
/// bottom-right. Offsets are in pixels.
/// </summary>
/// <remarks>
/// Anchored to cells rather than to coordinates because that is how the file stores it and how Excel
/// behaves: widen a column under a chart and the chart stretches with it.
/// </remarks>
public readonly record struct ChartAnchor(CellRef From, double FromDx, double FromDy, CellRef To, double ToDx, double ToDy);

/// <summary>A chart floating over a sheet.</summary>
/// <param name="Id">The chart part's relationship id within the sheet's drawing — how commands name it.</param>
public sealed record SheetChart(string Id, ChartKind Kind, ChartAnchor Anchor)
{
    public string? Title { get; init; }
    public IReadOnlyList<ChartSeries> Series { get; init; } = [];
    public bool Stacked { get; init; }
    public bool ShowLegend { get; init; } = true;
}

/// <summary>A series' values as the painter needs them — evaluated against the live workbook.</summary>
public sealed record ChartSeriesData(string Name, IReadOnlyList<string> Categories, IReadOnlyList<double?> Values, ArgbColor? Color);

/// <summary>Reads, builds and evaluates DrawingML charts.</summary>
public static class SheetCharts
{
    public const string ChartNamespace = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    public const string DrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main";
    public const string SpreadsheetDrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

    /// <summary>English Metric Units per pixel at 96 DPI — the unit every DrawingML offset is in.</summary>
    public const double EmuPerPixel = 9525;

    static readonly XNamespace C = ChartNamespace;
    static readonly XNamespace A = DrawingNamespace;

    /// <summary>The chart types Insert ▸ Charts offers.</summary>
    public static IReadOnlyList<(ChartKind Kind, string Name)> Gallery { get; } =
    [
        (ChartKind.Column, "Clustered Column"),
        (ChartKind.Bar, "Clustered Bar"),
        (ChartKind.Line, "Line"),
        (ChartKind.Pie, "Pie"),
        (ChartKind.Area, "Area")
    ];

    // ---- reading ----

    /// <summary>Reads the chart part's <c>c:chartSpace</c>.</summary>
    public static SheetChart Parse(string id, ChartAnchor anchor, string chartSpaceXml)
    {
        var root = XElement.Parse(chartSpaceXml);
        var chart = root.Element(C + "chart");
        var plot = chart?.Element(C + "plotArea");

        var kind = ChartKind.Unknown;
        XElement? typeElement = null;
        var stacked = false;

        foreach (var element in plot?.Elements() ?? [])
        {
            var local = element.Name.LocalName;
            var found = local switch
            {
                "barChart" or "bar3DChart" => element.Element(C + "barDir")?.Attribute("val")?.Value == "bar" ? ChartKind.Bar : ChartKind.Column,
                "lineChart" or "line3DChart" => ChartKind.Line,
                "pieChart" or "pie3DChart" or "doughnutChart" => ChartKind.Pie,
                "areaChart" or "area3DChart" => ChartKind.Area,
                "scatterChart" => ChartKind.Scatter,
                _ => ChartKind.Unknown
            };

            if (found == ChartKind.Unknown)
                continue;

            kind = found;
            typeElement = element;
            stacked = element.Element(C + "grouping")?.Attribute("val")?.Value is "stacked" or "percentStacked";
            break;
        }

        var series = new List<ChartSeries>();
        foreach (var ser in typeElement?.Elements(C + "ser") ?? [])
        {
            var nameRef = ser.Element(C + "tx")?.Element(C + "strRef");
            var categories = ser.Element(C + "cat") ?? ser.Element(C + "xVal");
            var values = ser.Element(C + "val") ?? ser.Element(C + "yVal");

            series.Add(new ChartSeries(
                nameRef?.Element(C + "f")?.Value,
                FormulaOf(categories),
                FormulaOf(values))
            {
                CachedName = nameRef?.Descendants(C + "v").FirstOrDefault()?.Value ?? ser.Element(C + "tx")?.Element(C + "v")?.Value,
                CachedCategories = categories?.Descendants(C + "pt").Select(x => x.Element(C + "v")?.Value ?? string.Empty).ToList() ?? [],
                CachedValues = values?.Descendants(C + "pt").Select(x => double.TryParse(x.Element(C + "v")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (double?)null).ToList() ?? [],
                Color = ColorOf(ser.Element(C + "spPr"))
            });
        }

        var title = chart?.Element(C + "title");
        var titleText = title is null
            ? null
            : string.Concat(title.Descendants(A + "t").Select(x => x.Value)) is { Length: > 0 } text
                ? text
                : title.Descendants(C + "f").FirstOrDefault()?.Value;

        // An auto title - a single series' name - when the file asks for one and gives no text.
        if (titleText is null && chart?.Element(C + "autoTitleDeleted")?.Attribute("val")?.Value is not ("1" or "true") && series.Count == 1)
            titleText = series[0].CachedName;

        return new SheetChart(id, kind, anchor)
        {
            Title = titleText,
            Series = series,
            Stacked = stacked,
            ShowLegend = chart?.Element(C + "legend") is not null
        };
    }

    static string? FormulaOf(XElement? element)
        => element?.Descendants(C + "f").FirstOrDefault()?.Value;

    static ArgbColor? ColorOf(XElement? spPr)
    {
        var hex = spPr?.Element(A + "solidFill")?.Element(A + "srgbClr")?.Attribute("val")?.Value;
        if (hex is { Length: 6 } && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return ArgbColor.FromUInt32(0xFF000000u | rgb);

        return null;
    }

    /// <summary>Reads a <c>twoCellAnchor</c>'s from/to markers.</summary>
    public static ChartAnchor ParseAnchor(XElement anchor)
    {
        XNamespace xdr = SpreadsheetDrawingNamespace;

        (CellRef Cell, double Dx, double Dy) Marker(XElement? marker)
        {
            int Int(string name) => int.TryParse(marker?.Element(xdr + name)?.Value, out var v) ? v : 0;
            double Emu(string name) => long.TryParse(marker?.Element(xdr + name)?.Value, out var v) ? v / EmuPerPixel : 0;

            return (new CellRef(Int("col"), Int("row")), Emu("colOff"), Emu("rowOff"));
        }

        var from = Marker(anchor.Element(xdr + "from"));
        var to = Marker(anchor.Element(xdr + "to"));
        return new ChartAnchor(from.Cell, from.Dx, from.Dy, to.Cell, to.Dx, to.Dy);
    }

    // ---- building ----

    /// <summary>
    /// Series for a chart over <paramref name="range"/>, the way Excel reads a selection: a text first
    /// column is the category labels, a text first row is the series names, and the series run down the
    /// columns unless the block is wider than it is tall.
    /// </summary>
    public static IReadOnlyList<ChartSeries> SeriesFromRange(Worksheet sheet, CellRange range)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        bool IsText(CellRef cell) => sheet.GetDisplayValue(cell) is { Kind: CellValueKind.Text or CellValueKind.Blank };

        var firstRowIsHeader = range.RowCount > 1 && Enumerable.Range(range.Left, range.ColumnCount)
            .Skip(1).All(c => IsText(new CellRef(c, range.Top)));
        var firstColumnIsLabels = range.ColumnCount > 1 && Enumerable.Range(range.Top, range.RowCount)
            .Skip(firstRowIsHeader ? 1 : 0).All(r => IsText(new CellRef(range.Left, r)));

        var dataTop = range.Top + (firstRowIsHeader ? 1 : 0);
        var dataLeft = range.Left + (firstColumnIsLabels ? 1 : 0);

        if (dataTop > range.Bottom || dataLeft > range.Right)
            return [];

        var byColumns = (range.Bottom - dataTop + 1) >= (range.Right - dataLeft + 1);
        var sheetRef = FormulaSheetRenamer.RequiresQuoting(sheet.Name) ? FormulaSheetRenamer.Quote(sheet.Name) : sheet.Name;

        string Abs(CellRange r) => $"{sheetRef}!{Absolute(r.TopLeft)}" + (r.IsSingleCell ? string.Empty : $":{Absolute(r.BottomRight)}");

        var series = new List<ChartSeries>();

        if (byColumns)
        {
            var categories = firstColumnIsLabels ? Abs(new CellRange(new CellRef(range.Left, dataTop), new CellRef(range.Left, range.Bottom))) : null;

            for (var column = dataLeft; column <= range.Right; column++)
            {
                series.Add(new ChartSeries(
                    firstRowIsHeader ? Abs(new CellRange(new CellRef(column, range.Top))) : null,
                    categories,
                    Abs(new CellRange(new CellRef(column, dataTop), new CellRef(column, range.Bottom)))));
            }
        }
        else
        {
            var categories = firstRowIsHeader ? Abs(new CellRange(new CellRef(dataLeft, range.Top), new CellRef(range.Right, range.Top))) : null;

            for (var row = dataTop; row <= range.Bottom; row++)
            {
                series.Add(new ChartSeries(
                    firstColumnIsLabels ? Abs(new CellRange(new CellRef(range.Left, row))) : null,
                    categories,
                    Abs(new CellRange(new CellRef(dataLeft, row), new CellRef(range.Right, row)))));
            }
        }

        return series;
    }

    static string Absolute(CellRef cell) => $"${CellRef.ColumnName(cell.Column)}${cell.Row + 1}";

    /// <summary>A <c>c:chartSpace</c> for a new chart — the minimum Excel opens without a repair.</summary>
    public static string ChartSpaceXml(ChartKind kind, string? title, IReadOnlyList<ChartSeries> series, Worksheet sheet)
    {
        var xml = new StringBuilder();
        xml.Append($"<c:chartSpace xmlns:c=\"{ChartNamespace}\" xmlns:a=\"{DrawingNamespace}\" xmlns:r=\"{SheetXml.RelationshipNamespace}\">");
        xml.Append("<c:roundedCorners val=\"0\"/><c:chart>");

        if (!string.IsNullOrEmpty(title))
        {
            xml.Append("<c:title><c:tx><c:rich><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>")
               .Append(SecurityElement.Escape(title))
               .Append("</a:t></a:r></a:p></c:rich></c:tx><c:overlay val=\"0\"/></c:title><c:autoTitleDeleted val=\"0\"/>");
        }
        else
        {
            xml.Append("<c:autoTitleDeleted val=\"").Append(series.Count == 1 ? "0" : "1").Append("\"/>");
        }

        xml.Append("<c:plotArea><c:layout/>");

        var element = kind switch
        {
            ChartKind.Line => "lineChart",
            ChartKind.Pie => "pieChart",
            ChartKind.Area => "areaChart",
            _ => "barChart"
        };

        xml.Append($"<c:{element}>");

        if (kind is ChartKind.Column or ChartKind.Bar)
            xml.Append($"<c:barDir val=\"{(kind == ChartKind.Bar ? "bar" : "col")}\"/><c:grouping val=\"clustered\"/>");
        else if (kind is ChartKind.Line or ChartKind.Area)
            xml.Append("<c:grouping val=\"standard\"/>");

        xml.Append($"<c:varyColors val=\"{(kind == ChartKind.Pie ? "1" : "0")}\"/>");

        for (var i = 0; i < series.Count; i++)
        {
            var s = series[i];
            xml.Append($"<c:ser><c:idx val=\"{i}\"/><c:order val=\"{i}\"/>");

            if (s.NameFormula is { } nameFormula)
            {
                var name = EvaluateText(sheet, nameFormula);
                xml.Append("<c:tx><c:strRef><c:f>").Append(SecurityElement.Escape(nameFormula)).Append("</c:f>")
                   .Append("<c:strCache><c:ptCount val=\"1\"/><c:pt idx=\"0\"><c:v>").Append(SecurityElement.Escape(name)).Append("</c:v></c:pt></c:strCache>")
                   .Append("</c:strRef></c:tx>");
            }

            if (kind == ChartKind.Bar || kind == ChartKind.Column)
                xml.Append("<c:invertIfNegative val=\"0\"/>");

            if (kind == ChartKind.Line)
                xml.Append("<c:marker><c:symbol val=\"none\"/></c:marker>");

            if (s.CategoriesFormula is { } categories)
            {
                var labels = sheet.Workbook.EvaluateValues(categories, sheet.Name, default);
                xml.Append("<c:cat><c:strRef><c:f>").Append(SecurityElement.Escape(categories)).Append("</c:f>")
                   .Append($"<c:strCache><c:ptCount val=\"{labels.Count}\"/>");

                for (var p = 0; p < labels.Count; p++)
                    xml.Append($"<c:pt idx=\"{p}\"><c:v>").Append(SecurityElement.Escape(Coercion.ToText(labels[p]))).Append("</c:v></c:pt>");

                xml.Append("</c:strCache></c:strRef></c:cat>");
            }

            if (s.ValuesFormula is { } values)
            {
                var numbers = sheet.Workbook.EvaluateValues(values, sheet.Name, default);
                xml.Append("<c:val><c:numRef><c:f>").Append(SecurityElement.Escape(values)).Append("</c:f>")
                   .Append($"<c:numCache><c:formatCode>General</c:formatCode><c:ptCount val=\"{numbers.Count}\"/>");

                for (var p = 0; p < numbers.Count; p++)
                {
                    if (numbers[p].Kind == CellValueKind.Number)
                        xml.Append($"<c:pt idx=\"{p}\"><c:v>").Append(numbers[p].AsNumber().ToString("R", CultureInfo.InvariantCulture)).Append("</c:v></c:pt>");
                }

                xml.Append("</c:numCache></c:numRef></c:val>");
            }

            if (kind == ChartKind.Line)
                xml.Append("<c:smooth val=\"0\"/>");

            xml.Append("</c:ser>");
        }

        switch (kind)
        {
            case ChartKind.Pie:
                xml.Append("<c:firstSliceAng val=\"0\"/>");
                break;

            case ChartKind.Line:
                xml.Append("<c:marker val=\"1\"/><c:axId val=\"500000001\"/><c:axId val=\"500000002\"/>");
                break;

            case ChartKind.Area:
                xml.Append("<c:axId val=\"500000001\"/><c:axId val=\"500000002\"/>");
                break;

            default:
                xml.Append("<c:gapWidth val=\"150\"/><c:axId val=\"500000001\"/><c:axId val=\"500000002\"/>");
                break;
        }

        xml.Append($"</c:{element}>");

        if (kind != ChartKind.Pie)
        {
            var (catPos, valPos) = kind == ChartKind.Bar ? ("l", "b") : ("b", "l");

            xml.Append("<c:catAx><c:axId val=\"500000001\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling>")
               .Append($"<c:delete val=\"0\"/><c:axPos val=\"{catPos}\"/><c:numFmt formatCode=\"General\" sourceLinked=\"1\"/>")
               .Append("<c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/>")
               .Append("<c:crossAx val=\"500000002\"/><c:crosses val=\"autoZero\"/><c:auto val=\"1\"/><c:lblAlgn val=\"ctr\"/><c:lblOffset val=\"100\"/><c:noMultiLvlLbl val=\"0\"/></c:catAx>");

            xml.Append("<c:valAx><c:axId val=\"500000002\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling>")
               .Append($"<c:delete val=\"0\"/><c:axPos val=\"{valPos}\"/><c:majorGridlines/><c:numFmt formatCode=\"General\" sourceLinked=\"1\"/>")
               .Append("<c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/>")
               .Append("<c:crossAx val=\"500000001\"/><c:crosses val=\"autoZero\"/><c:crossBetween val=\"between\"/></c:valAx>");
        }

        xml.Append("</c:plotArea>");
        xml.Append("<c:legend><c:legendPos val=\"").Append(kind == ChartKind.Pie ? "r" : "b").Append("\"/><c:overlay val=\"0\"/></c:legend>");
        xml.Append("<c:plotVisOnly val=\"1\"/><c:dispBlanksAs val=\"gap\"/></c:chart></c:chartSpace>");

        return xml.ToString();
    }

    static string EvaluateText(Worksheet sheet, string formula)
    {
        var values = sheet.Workbook.EvaluateValues(formula, sheet.Name, default);
        return values.Count == 0 ? string.Empty : Coercion.ToText(values[0]);
    }

    /// <summary>
    /// A <c>xdr:twoCellAnchor</c> holding a chart frame. <c>{RID}</c> stands for the chart part's
    /// relationship id, which does not exist until the part is added.
    /// </summary>
    public static string AnchorXml(ChartAnchor anchor, uint shapeId, string name)
    {
        static string Marker(string tag, CellRef cell, double dx, double dy)
            => $"<xdr:{tag}><xdr:col>{cell.Column}</xdr:col><xdr:colOff>{(long)Math.Round(dx * EmuPerPixel)}</xdr:colOff>" +
               $"<xdr:row>{cell.Row}</xdr:row><xdr:rowOff>{(long)Math.Round(dy * EmuPerPixel)}</xdr:rowOff></xdr:{tag}>";

        return $"<xdr:twoCellAnchor xmlns:xdr=\"{SpreadsheetDrawingNamespace}\" xmlns:a=\"{DrawingNamespace}\">" +
               Marker("from", anchor.From, anchor.FromDx, anchor.FromDy) +
               Marker("to", anchor.To, anchor.ToDx, anchor.ToDy) +
               "<xdr:graphicFrame macro=\"\"><xdr:nvGraphicFramePr>" +
               $"<xdr:cNvPr id=\"{shapeId}\" name=\"{SecurityElement.Escape(name)}\"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr>" +
               "<xdr:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/></xdr:xfrm>" +
               "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\">" +
               $"<c:chart xmlns:c=\"{ChartNamespace}\" xmlns:r=\"{SheetXml.RelationshipNamespace}\" r:id=\"{{RID}}\"/>" +
               "</a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor>";
    }

    /// <summary>
    /// A series' data from the live workbook, falling back to what the file cached when a reference
    /// cannot be read — a chart over an external workbook, say.
    /// </summary>
    public static IReadOnlyList<ChartSeriesData> Evaluate(Worksheet sheet, SheetChart chart)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var result = new List<ChartSeriesData>();

        for (var i = 0; i < chart.Series.Count; i++)
        {
            var series = chart.Series[i];

            var name = series.NameFormula is { } nf
                ? Values(sheet, nf).FirstOrDefault() is { IsBlank: false } v ? Coercion.ToText(v) : series.CachedName
                : series.CachedName;

            var categories = series.CategoriesFormula is { } cf
                ? Values(sheet, cf).Select(x => x.IsBlank ? string.Empty : Coercion.ToText(x)).ToList()
                : series.CachedCategories.ToList();

            var values = series.ValuesFormula is { } vf
                ? Values(sheet, vf).Select(x => x.Kind == CellValueKind.Number ? x.AsNumber() : (double?)null).ToList()
                : series.CachedValues.ToList();

            if (values.Count == 0)
                values = series.CachedValues.ToList();

            if (categories.Count == 0)
                categories = Enumerable.Range(1, values.Count).Select(x => x.ToString(CultureInfo.CurrentCulture)).ToList();

            result.Add(new ChartSeriesData(name ?? $"Series{i + 1}", categories, values, series.Color));
        }

        return result;
    }

    static IReadOnlyList<CellValue> Values(Worksheet sheet, string formula)
    {
        var values = sheet.Workbook.EvaluateValues(formula, sheet.Name, default);
        return values.Count == 1 && values[0].IsError ? [] : values;
    }
}

/// <summary>Adds a chart — its part and its anchor — to a sheet. The inverse deletes it.</summary>
public sealed class InsertChartCommand(string sheetName, string chartSpaceXml, string anchorXml, string? preferredId = null) : IEditCommand<Workbook>
{
    public string SheetName { get; } = sheetName;
    public string ChartSpaceXml { get; } = chartSpaceXml;

    /// <summary>The anchor, with <c>{RID}</c> where the chart's relationship id goes.</summary>
    public string AnchorXml { get; } = anchorXml;

    public string Name => "Insert Chart";

    /// <summary>The id the chart was given, once applied — so a caller can select what it inserted.</summary>
    public string? InsertedId { get; private set; }

    public IEditCommand<Workbook> Apply(Workbook context)
    {
        var id = context[this.SheetName].AddChart(this.ChartSpaceXml, this.AnchorXml, preferredId);
        this.InsertedId = id;
        return new DeleteChartCommand(this.SheetName, id);
    }
}

/// <summary>Deletes a chart, keeping its XML so undo can put it back.</summary>
public sealed class DeleteChartCommand(string sheetName, string chartId) : IEditCommand<Workbook>
{
    public string SheetName { get; } = sheetName;
    public string ChartId { get; } = chartId;

    public string Name => "Delete Chart";

    public IEditCommand<Workbook> Apply(Workbook context)
    {
        var removed = context[this.SheetName].RemoveChart(this.ChartId);
        return removed is { } parts
            ? new InsertChartCommand(this.SheetName, parts.ChartSpaceXml, parts.AnchorXml, this.ChartId)
            : new CompositeCommand<Workbook>(this.Name, []);
    }
}

/// <summary>Moves or resizes a chart by re-anchoring it.</summary>
public sealed class MoveChartCommand(string sheetName, string chartId, ChartAnchor anchor) : IEditCommand<Workbook>
{
    public string SheetName { get; } = sheetName;
    public string ChartId { get; } = chartId;
    public ChartAnchor Anchor { get; } = anchor;

    public string Name => "Move Chart";

    public IEditCommand<Workbook> Apply(Workbook context)
    {
        var previous = context[this.SheetName].MoveChart(this.ChartId, this.Anchor);
        return new MoveChartCommand(this.SheetName, this.ChartId, previous ?? this.Anchor);
    }
}
