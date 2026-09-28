using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Shell;
using Shiny.Controls.Office.Skia;
using Shiny.Controls.Office.Text;
using Shouldly;
using Xunit;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// The glue between the Word editor and the Office shell: headings, search results, ruler units, tab
/// stops, view modes, the built-in templates and PDF export — everything both hosts'
/// <c>DocumentEditorView</c> shells route through <see cref="WordShell"/>.
/// </summary>
public class WordShellTests
{
    sealed class Fixed : ITextMeasurer
    {
        public TextMetrics Measure(ReadOnlySpan<char> text, TextStyle style)
            => new(text.Length * 8, style.FontSize * 0.8, style.FontSize * 0.2);

        public TextMetrics LineMetrics(TextStyle style)
            => new(0, style.FontSize * 0.8, style.FontSize * 0.2);
    }

    static async Task<(WordDocument Document, DocumentEditorController Controller)> OpenAsync(byte[]? bytes = null)
    {
        var document = await WordDocument.OpenAsync(new MemoryStream(bytes ?? DocumentFixture.Build()), editable: true);
        var controller = new DocumentEditorController(document, new Fixed()) { Author = "Tester" };
        controller.Resize(900, 700);
        return (document, controller);
    }

    static List<string> SchemaErrors(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var package = WordprocessingDocument.Open(stream, false);
        return new OpenXmlValidator().Validate(package).Select(x => $"{x.Path?.XPath}: {x.Description}").ToList();
    }

    [Fact]
    public async Task HeadingsCarryTheirParagraphAndJumpThere()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var headings = WordShell.Headings(controller);
        headings.ShouldNotBeEmpty();
        headings[0].Text.ShouldBe("Quarterly Report");

        controller.Selection.MoveTo(new DocumentPosition(document.Paragraphs.Count - 1, 0));
        WordShell.GoTo(controller, headings[0]).ShouldBeTrue();
        controller.Selection.Focus.Block.ShouldBe(WordShell.ParagraphOf(headings[0])!.Value);
        WordShell.CurrentHeadingId(controller, headings).ShouldBe(headings[0].Id);
    }

    [Fact]
    public async Task SearchReturnsContextAndSelectsTheHit()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var results = WordShell.Search(controller, "wrap");
        results.Count.ShouldBe(1);
        results[0].Match.ShouldBe("wrap");
        results[0].Before.ShouldContain("should");

        WordShell.GoTo(controller, results[0]).ShouldBeTrue();
        controller.Selection.IsEmpty.ShouldBeFalse();
        controller.Find.Query.ShouldBe("wrap");

        WordShell.Search(controller, string.Empty).ShouldBeEmpty();
    }

    [Fact]
    public async Task RulerIndentsRoundTripInPoints()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;
        var body = document.Paragraphs.ToList().FindIndex(p => p.PlainText.StartsWith("Plain body text"));
        controller.Selection.MoveTo(new DocumentPosition(body, 0));

        WordShell.ApplyIndents(controller, new OfficeIndents(36, -18, 9)).ShouldBeTrue();

        var indents = WordShell.Indents(controller.CaretFormat);
        indents.Left.ShouldBe(36, 0.1);
        indents.FirstLine.ShouldBe(-18, 0.1);
        indents.Right.ShouldBe(9, 0.1);

        // The same values again change nothing, so no undo step is pushed.
        WordShell.ApplyIndents(controller, indents).ShouldBeFalse();
    }

    [Fact]
    public async Task TabStopsAreWrittenInSchemaOrderAndReadBack()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;
        var body = document.Paragraphs.ToList().FindIndex(p => p.PlainText.StartsWith("Plain body text"));
        controller.Selection.MoveTo(new DocumentPosition(body, 0));
        controller.SetParagraphShading(new Shiny.Controls.Office.Spreadsheet.ArgbColor(255, 255, 242, 204));
        controller.SetLineSpacing(1.5);

        WordShell.ApplyTabStops(controller, [new OfficeTabStop(144, OfficeTabAlignment.Center), new OfficeTabStop(72)]).ShouldBeTrue();

        var stops = WordShell.TabStops(controller);
        stops.Count.ShouldBe(2);
        stops[0].Position.ShouldBe(72, 0.1);
        stops[1].Alignment.ShouldBe(OfficeTabAlignment.Center);

        var before = SchemaErrors(DocumentFixture.Build()).Count;
        SchemaErrors(document.ToArray()).Count.ShouldBe(before);

        controller.Undo();
        WordShell.TabStops(controller).ShouldBeEmpty();
    }

    [Fact]
    public async Task RulerMarginsMoveTheSection()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        WordShell.ApplyMargins(controller, 54, 90).ShouldBeTrue();
        controller.PageMargins.Left.ShouldBe(72, 0.1);
        controller.PageMargins.Right.ShouldBe(120, 0.1);
    }

    [Theory]
    [InlineData("read", true, DocumentPageLayout.Print)]
    [InlineData("print", false, DocumentPageLayout.Print)]
    [InlineData("web", false, DocumentPageLayout.Reflow)]
    public void ViewModesMapToTheEditor(string id, bool readMode, DocumentPageLayout layout)
    {
        WordShell.FromViewModeId(id, DocumentPageLayout.Print).ShouldBe((readMode, layout));
        WordShell.ViewModeId(readMode, layout).ShouldBe(id);
    }

    [Fact]
    public async Task StylesAndDocumentInfoComeFromTheController()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var styles = WordShell.Styles(controller);
        styles.ShouldContain(x => x.Id == "Heading1" && x.OutlineLevel == 1);

        var info = WordShell.DocumentInfo(controller, "Report");
        info.Statistics.ShouldContain(x => x.Name == "Words");
        info.Author.ShouldBe("Tester");
    }

    [Theory]
    [InlineData(WordTemplates.BlankId)]
    [InlineData(WordTemplates.ReportId)]
    [InlineData(WordTemplates.LetterId)]
    public async Task TemplatesAreValidDocuments(string id)
    {
        using var stream = WordTemplates.Create(id);
        SchemaErrors(stream.ToArray()).ShouldBeEmpty();

        var template = WordTemplates.All.Single(x => x.Id == id);
        using var document = await WordTemplates.OpenAsync(template);
        document.IsEditable.ShouldBeTrue();

        if (id == WordTemplates.ReportId)
        {
            var controller = new DocumentEditorController(document, new Fixed());
            WordShell.Headings(controller).Select(x => x.Text).ShouldContain("Findings");
        }
    }

    [Fact]
    public async Task PdfExportWritesOnePagePerPrintedPage()
    {
        var (document, _) = await OpenAsync();
        using var __ = document;

        using var output = new MemoryStream();
        var pages = DocumentPdfExporter.Export(document, output, new DocumentPdfOptions { Title = "Fixture" });

        pages.ShouldBeGreaterThanOrEqualTo(1);
        var bytes = output.ToArray();
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).ShouldBe("%PDF-");
        pages.ShouldBe(DocumentPdfExporter.PageCount(document));

        var png = DocumentPdfExporter.RenderPagePng(document, 0, 0.5f);
        png.Length.ShouldBeGreaterThan(100);
        png[1].ShouldBe((byte)'P');
    }
}
