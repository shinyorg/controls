using System.Globalization;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Spreadsheet;
using D = DocumentFormat.OpenXml.Drawing;

namespace Shiny.Controls.Office.Presentation;

/// <summary>The chart types the editor inserts and draws.</summary>
public enum SlideChartKind
{
    /// <summary>Vertical bars — PowerPoint's "Clustered Column", the default chart.</summary>
    Column,

    /// <summary>Horizontal bars.</summary>
    Bar,
    Line,
    Pie
}

/// <summary>One series of a chart: a name and one value per category.</summary>
public sealed record SlideChartSeries(string Name, IReadOnlyList<double> Values)
{
    /// <summary>The series colour the file gives it, or null to take the theme's accent in turn.</summary>
    public ArgbColor? Color { get; init; }
}

/// <summary>
/// A simple chart's data — categories down the side, a series per column — which is the grid a
/// chart's data sheet shows and the shape every chart the editor can draw fits into.
/// </summary>
public sealed record SlideChart(SlideChartKind Kind, IReadOnlyList<string> Categories, IReadOnlyList<SlideChartSeries> Series)
{
    public string? Title { get; init; }

    public bool ShowLegend { get; init; } = true;

    /// <summary>PowerPoint's own sample: four categories, three series.</summary>
    public static SlideChart Sample(SlideChartKind kind) => new(
        kind,
        ["Category 1", "Category 2", "Category 3", "Category 4"],
        kind == SlideChartKind.Pie
            ? [new SlideChartSeries("Sales", [8.2, 3.2, 1.4, 1.2])]
            :
            [
                new SlideChartSeries("Series 1", [4.3, 2.5, 3.5, 4.5]),
                new SlideChartSeries("Series 2", [2.4, 4.4, 1.8, 2.8]),
                new SlideChartSeries("Series 3", [2, 2, 3, 5])
            ])
    {
        Title = kind == SlideChartKind.Pie ? "Sales" : "Chart Title"
    };

    /// <summary>The largest value across every series — the top of a value axis.</summary>
    public double MaxValue => this.Series.SelectMany(x => x.Values).DefaultIfEmpty(0).Max();

    public double MinValue => this.Series.SelectMany(x => x.Values).DefaultIfEmpty(0).Min();
}

/// <summary>
/// Reads and writes <c>c:chartSpace</c> — the chart part a graphic frame points at.
/// </summary>
/// <remarks>
/// <para>
/// Read by local name, not through the SDK's chart classes: a chart part from PowerPoint, Excel or a
/// generator varies in which of <c>strRef</c>/<c>strLit</c>/<c>numRef</c>/<c>numLit</c> it uses, and
/// the cached points are all this needs.
/// </para>
/// <para>
/// Written with its data in the reference caches and no embedded workbook. PowerPoint opens and draws
/// such a chart from the caches; what it cannot do is "Edit Data in Excel", because there is no
/// workbook to edit — the editor's own data grid is how the numbers change here.
/// </para>
/// </remarks>
static class SlideChartXml
{
    public const string ChartUri = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    const string C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static ChartPart? PartOf(GraphicFrame frame, OpenXmlPart owner)
    {
        var reference = frame.Graphic?.GraphicData?.ChildElements.FirstOrDefault(x => x.LocalName == "chart");
        var id = reference?.GetAttributes().FirstOrDefault(x => x.LocalName == "id" && x.NamespaceUri == R).Value;

        return id is not null && owner.Parts.FirstOrDefault(x => x.RelationshipId == id).OpenXmlPart is ChartPart part
            ? part
            : null;
    }

    public static SlideChart? Read(ChartPart part, Func<OpenXmlElement?, ArgbColor?> color)
    {
        var root = part.ChartSpace;
        if (root is null)
            return null;

        var chart = root.ChildElements.FirstOrDefault(x => x.LocalName == "chart");
        var plot = chart?.ChildElements.FirstOrDefault(x => x.LocalName == "plotArea");
        var body = plot?.ChildElements.FirstOrDefault(x => x.LocalName is "barChart" or "bar3DChart" or "lineChart" or "line3DChart" or "pieChart" or "pie3DChart" or "doughnutChart" or "areaChart");
        if (body is null)
            return null;

        var kind = body.LocalName switch
        {
            "barChart" or "bar3DChart" => Child(body, "barDir")?.GetAttributes().FirstOrDefault(x => x.LocalName == "val").Value == "bar"
                ? SlideChartKind.Bar
                : SlideChartKind.Column,
            "lineChart" or "line3DChart" or "areaChart" => SlideChartKind.Line,
            _ => SlideChartKind.Pie
        };

        var categories = new List<string>();
        var series = new List<SlideChartSeries>();

        foreach (var ser in body.ChildElements.Where(x => x.LocalName == "ser"))
        {
            var name = Strings(Child(ser, "tx")).FirstOrDefault() ?? $"Series {series.Count + 1}";
            var values = Strings(Child(ser, "val"))
                .Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0)
                .ToList();

            if (categories.Count == 0)
                categories.AddRange(Strings(Child(ser, "cat")));

            var fill = Child(Child(ser, "spPr"), "solidFill");
            series.Add(new SlideChartSeries(name, values) { Color = color(fill) });
        }

        while (categories.Count < series.Select(x => x.Values.Count).DefaultIfEmpty(0).Max())
            categories.Add($"Category {categories.Count + 1}");

        var titleElement = Child(chart, "title");
        var title = titleElement is null ? null : string.Concat(titleElement.Descendants().Where(x => x.LocalName == "t").Select(x => x.InnerText));
        var autoDeleted = Child(chart, "autoTitleDeleted")?.GetAttributes().FirstOrDefault(x => x.LocalName == "val").Value is "1" or "true";

        return new SlideChart(kind, categories, series)
        {
            Title = string.IsNullOrWhiteSpace(title) ? (autoDeleted || series.Count != 1 ? null : series[0].Name) : title,
            ShowLegend = Child(chart, "legend") is not null
        };
    }

    static OpenXmlElement? Child(OpenXmlElement? element, string localName)
        => element?.ChildElements.FirstOrDefault(x => x.LocalName == localName);

    /// <summary>The cached points of a <c>tx</c>, <c>cat</c> or <c>val</c>, whichever flavour it is in.</summary>
    static IEnumerable<string> Strings(OpenXmlElement? container)
    {
        if (container is null)
            return [];

        // A bare c:v (a series name written inline) or the points of a cache or literal.
        if (Child(container, "v") is { } inline)
            return [inline.InnerText];

        var cache = container.Descendants().FirstOrDefault(x => x.LocalName is "strCache" or "numCache" or "strLit" or "numLit");
        if (cache is null)
            return [];

        var points = cache.ChildElements
            .Where(x => x.LocalName == "pt")
            .Select(x => (Index: int.TryParse(x.GetAttributes().FirstOrDefault(a => a.LocalName == "idx").Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0, Value: Child(x, "v")?.InnerText ?? string.Empty))
            .ToList();

        var count = int.TryParse(Child(cache, "ptCount")?.GetAttributes().FirstOrDefault(x => x.LocalName == "val").Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : points.Select(x => x.Index + 1).DefaultIfEmpty(0).Max();

        var result = Enumerable.Repeat(string.Empty, count).ToArray();
        foreach (var (index, value) in points)
        {
            if (index >= 0 && index < result.Length)
                result[index] = value;
        }

        return result;
    }

    /// <summary>The whole chart part, from the data.</summary>
    public static string Write(SlideChart chart)
    {
        var xml = new StringBuilder();
        xml.Append($"<c:chartSpace xmlns:c=\"{C}\" xmlns:a=\"{A}\" xmlns:r=\"{R}\">");
        xml.Append("<c:roundedCorners val=\"0\"/><c:chart>");

        if (!string.IsNullOrWhiteSpace(chart.Title))
        {
            xml.Append("<c:title><c:tx><c:rich><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>");
            xml.Append(SecurityElement.Escape(chart.Title));
            xml.Append("</a:t></a:r></a:p></c:rich></c:tx><c:overlay val=\"0\"/></c:title><c:autoTitleDeleted val=\"0\"/>");
        }
        else
        {
            xml.Append("<c:autoTitleDeleted val=\"1\"/>");
        }

        xml.Append("<c:plotArea><c:layout/>");

        var columnLetter = (int i) => ((char)('B' + i)).ToString();
        var rows = chart.Categories.Count;

        string SeriesXml(int index, SlideChartSeries series)
        {
            var s = new StringBuilder();
            s.Append($"<c:ser><c:idx val=\"{index}\"/><c:order val=\"{index}\"/>");
            s.Append($"<c:tx><c:strRef><c:f>Sheet1!${columnLetter(index)}$1</c:f><c:strCache><c:ptCount val=\"1\"/><c:pt idx=\"0\"><c:v>{SecurityElement.Escape(series.Name)}</c:v></c:pt></c:strCache></c:strRef></c:tx>");

            if (series.Color is { } color)
                s.Append($"<c:spPr><a:solidFill><a:srgbClr val=\"{color.R:X2}{color.G:X2}{color.B:X2}\"/></a:solidFill></c:spPr>");

            if (chart.Kind == SlideChartKind.Line)
                s.Append("<c:marker><c:symbol val=\"none\"/></c:marker>");

            s.Append($"<c:cat><c:strRef><c:f>Sheet1!$A$2:$A${rows + 1}</c:f><c:strCache><c:ptCount val=\"{rows}\"/>");
            for (var i = 0; i < rows; i++)
                s.Append($"<c:pt idx=\"{i}\"><c:v>{SecurityElement.Escape(chart.Categories[i])}</c:v></c:pt>");

            s.Append("</c:strCache></c:strRef></c:cat>");
            s.Append($"<c:val><c:numRef><c:f>Sheet1!${columnLetter(index)}$2:${columnLetter(index)}${rows + 1}</c:f><c:numCache><c:formatCode>General</c:formatCode><c:ptCount val=\"{rows}\"/>");
            for (var i = 0; i < rows; i++)
            {
                var value = i < series.Values.Count ? series.Values[i] : 0;
                s.Append($"<c:pt idx=\"{i}\"><c:v>{value.ToString("R", CultureInfo.InvariantCulture)}</c:v></c:pt>");
            }

            s.Append("</c:numCache></c:numRef></c:val>");

            if (chart.Kind == SlideChartKind.Line)
                s.Append("<c:smooth val=\"0\"/>");

            s.Append("</c:ser>");
            return s.ToString();
        }

        const string axes = "<c:axId val=\"500000001\"/><c:axId val=\"500000002\"/>";

        switch (chart.Kind)
        {
            case SlideChartKind.Pie:
                xml.Append("<c:pieChart><c:varyColors val=\"1\"/>");
                for (var i = 0; i < chart.Series.Count; i++)
                    xml.Append(SeriesXml(i, chart.Series[i]));

                xml.Append("<c:firstSliceAng val=\"0\"/></c:pieChart>");
                break;

            case SlideChartKind.Line:
                xml.Append("<c:lineChart><c:grouping val=\"standard\"/><c:varyColors val=\"0\"/>");
                for (var i = 0; i < chart.Series.Count; i++)
                    xml.Append(SeriesXml(i, chart.Series[i]));

                xml.Append("<c:marker val=\"1\"/>" + axes + "</c:lineChart>");
                break;

            default:
                xml.Append($"<c:barChart><c:barDir val=\"{(chart.Kind == SlideChartKind.Bar ? "bar" : "col")}\"/><c:grouping val=\"clustered\"/><c:varyColors val=\"0\"/>");
                for (var i = 0; i < chart.Series.Count; i++)
                    xml.Append(SeriesXml(i, chart.Series[i]));

                xml.Append("<c:gapWidth val=\"219\"/><c:overlap val=\"-27\"/>" + axes + "</c:barChart>");
                break;
        }

        if (chart.Kind != SlideChartKind.Pie)
        {
            var categoryPosition = chart.Kind == SlideChartKind.Bar ? "l" : "b";
            var valuePosition = chart.Kind == SlideChartKind.Bar ? "b" : "l";

            xml.Append($"<c:catAx><c:axId val=\"500000001\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"{categoryPosition}\"/><c:numFmt formatCode=\"General\" sourceLinked=\"1\"/><c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/><c:crossAx val=\"500000002\"/><c:crosses val=\"autoZero\"/><c:auto val=\"1\"/><c:lblAlgn val=\"ctr\"/><c:lblOffset val=\"100\"/><c:noMultiLvlLbl val=\"0\"/></c:catAx>");
            xml.Append($"<c:valAx><c:axId val=\"500000002\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"{valuePosition}\"/><c:majorGridlines/><c:numFmt formatCode=\"General\" sourceLinked=\"1\"/><c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/><c:crossAx val=\"500000001\"/><c:crosses val=\"autoZero\"/><c:crossBetween val=\"between\"/></c:valAx>");
        }

        xml.Append("</c:plotArea>");

        if (chart.ShowLegend)
            xml.Append("<c:legend><c:legendPos val=\"b\"/><c:overlay val=\"0\"/></c:legend>");

        xml.Append("<c:plotVisOnly val=\"1\"/><c:dispBlanksAs val=\"gap\"/></c:chart></c:chartSpace>");
        return xml.ToString();
    }

    /// <summary>Replaces a chart part's content with a written chart.</summary>
    /// <remarks>
    /// Through the DOM rather than <c>FeedData</c>: a part whose root the reader has already loaded is
    /// re-serialised from that cached root on save, which would quietly write the old chart back over
    /// the fed bytes.
    /// </remarks>
    public static void Feed(ChartPart part, string xml)
        => part.ChartSpace = new DocumentFormat.OpenXml.Drawing.Charts.ChartSpace(xml);

    /// <summary>The graphic frame that shows a chart part on a slide.</summary>
    public static GraphicFrame Frame(string relationshipId, double x, double y, double width, double height, string name)
        => new(
            new NonVisualGraphicFrameProperties(
                new NonVisualDrawingProperties { Id = 2U, Name = name },
                new NonVisualGraphicFrameDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new Transform
            {
                Offset = new D.Offset { X = OoxmlUnits.PixelsToEmu(x), Y = OoxmlUnits.PixelsToEmu(y) },
                Extents = new D.Extents { Cx = OoxmlUnits.PixelsToEmu(width), Cy = OoxmlUnits.PixelsToEmu(height) }
            },
            new D.Graphic(new D.GraphicData(new DocumentFormat.OpenXml.Drawing.Charts.ChartReference { Id = relationshipId }) { Uri = ChartUri }));
}

/// <summary>
/// Replaces the data behind a chart, restoring the previous chart part content on undo.
/// </summary>
public sealed record SetChartDataCommand(int Slide, int Shape, SlideChart Chart) : SlideCommand
{
    public override string Name => "Edit chart data";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (ShapeAt(context, this.Slide, this.Shape) is not { Element: GraphicFrame frame, Chart: { } previous } ||
            context.PartAt(this.Slide) is not { } part ||
            SlideChartXml.PartOf(frame, part) is not { } chartPart)
        {
            return new NoOpSlideCommand();
        }

        var before = chartPart.ChartSpace?.OuterXml;
        SlideChartXml.Feed(chartPart, SlideChartXml.Write(this.Chart));
        context.MarkPartDirty(chartPart);
        context.Reproject(this.Slide);

        return before is null
            ? new SetChartDataCommand(this.Slide, this.Shape, previous)
            : new RestoreChartXmlCommand(this.Slide, this.Shape, before, previous);
    }
}

/// <summary>Puts a chart part's exact XML back — the undo of a data edit.</summary>
sealed record RestoreChartXmlCommand(int Slide, int Shape, string Xml, SlideChart Chart) : SlideCommand
{
    public override string Name => "Edit chart data";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (ShapeAt(context, this.Slide, this.Shape) is not { Element: GraphicFrame frame, Chart: { } current } ||
            context.PartAt(this.Slide) is not { } part ||
            SlideChartXml.PartOf(frame, part) is not { } chartPart)
        {
            return new NoOpSlideCommand();
        }

        var before = chartPart.ChartSpace?.OuterXml ?? SlideChartXml.Write(current);

        // A reloaded DOM: FeedData replaces the bytes, and the cached root has to be dropped with it.
        SlideChartXml.Feed(chartPart, this.Xml);
        context.MarkPartDirty(chartPart);
        context.Reproject(this.Slide);
        return new RestoreChartXmlCommand(this.Slide, this.Shape, before, current);
    }
}
