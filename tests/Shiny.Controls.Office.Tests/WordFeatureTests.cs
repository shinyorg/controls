using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using Shouldly;
using Xunit;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// The Word feature push: tables you can type in, replace, the rich clipboard, the Font and Paragraph
/// groups, styles, links, the table of contents, footnotes, comments and tracked changes.
/// </summary>
/// <remarks>
/// Every feature that writes XML is saved, schema-validated and reopened. Validation is what catches a
/// child written out of sequence — a <c>w:color</c> after a <c>w:u</c>, a <c>w:cols</c> after
/// <c>w:titlePg</c> — which Word reports as a corrupt file while every in-memory assertion still passes.
/// </remarks>
public class WordFeatureTests
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

    static int Body(WordDocument document)
        => document.Paragraphs.ToList().FindIndex(p => p.PlainText.StartsWith("Plain body text"));

    static int Cell(WordDocument document, string text)
        => document.Paragraphs.ToList().FindIndex(p => p.PlainText == text && document.CellOf(document.Paragraphs.ToList().IndexOf(p)) is not null);

    /// <summary>Schema errors in <paramref name="saved"/> that the untouched fixture does not have.</summary>
    static List<string> NewSchemaErrors(byte[] saved)
    {
        static List<string> Errors(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            using var package = WordprocessingDocument.Open(stream, false);
            return new OpenXmlValidator().Validate(package)
                .Select(x => $"{x.Part?.Uri} {x.Path?.XPath} || {x.Description}")
                .ToList();
        }

        // Compared by description alone: the fixture's own out-of-order run moves down the body as
        // paragraphs are inserted above it, which changes its path but not what is wrong with it.
        static string Description(string error) => error[(error.IndexOf(" || ", StringComparison.Ordinal) + 4)..];

        // As a multiset: a new error of the same kind as one the fixture already has still counts.
        var baseline = Errors(DocumentFixture.Build()).GroupBy(Description).ToDictionary(x => x.Key, x => x.Count());
        var fresh = new List<string>();

        foreach (var group in Errors(saved).GroupBy(Description))
        {
            var allowed = baseline.TryGetValue(group.Key, out var count) ? count : 0;
            fresh.AddRange(group.Skip(allowed));
        }

        return fresh;
    }

    static async Task<WordDocument> SaveAndReopenAsync(WordDocument document)
    {
        var saved = document.ToArray();
        var errors = NewSchemaErrors(saved);
        errors.ShouldBeEmpty(string.Join(Environment.NewLine, errors));
        return await WordDocument.OpenAsync(new MemoryStream(saved, writable: false));
    }

    // ---- tables ----

    [Fact]
    public async Task TheCaretCanEnterATableCellAndTypeInIt()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var cell = Cell(document, "North");
        cell.ShouldBeGreaterThan(0);

        controller.Selection.MoveTo(new DocumentPosition(cell, 5));
        controller.InsertText("east");

        document.Paragraphs[cell].PlainText.ShouldBe("Northeast");
        controller.IsInTable.ShouldBeTrue();
        controller.CaretFormat.IsInTable.ShouldBeTrue();

        using var reopened = await SaveAndReopenAsync(document);
        reopened.Paragraphs.ShouldContain(p => p.PlainText == "Northeast");
    }

    [Fact]
    public async Task EnterInACellSplitsTheParagraphInsideTheCell()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var cell = Cell(document, "North");
        var tables = document.Blocks.Count;

        controller.Selection.MoveTo(new DocumentPosition(cell, 3));
        controller.InsertParagraph();

        document.Blocks.Count.ShouldBe(tables);
        document.Paragraphs[cell].PlainText.ShouldBe("Nor");
        document.Paragraphs[cell + 1].PlainText.ShouldBe("th");
        document.CellOf(cell + 1).ShouldBe(document.CellOf(cell));

        controller.Undo();
        document.Paragraphs[cell].PlainText.ShouldBe("North");
    }

    [Fact]
    public async Task TabMovesToTheNextCellAndSelectsItsText()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var cell = Cell(document, "North");
        controller.Selection.MoveTo(new DocumentPosition(cell, 0));

        controller.HandleTab().ShouldBeTrue();

        var selected = document.Paragraphs[controller.Selection.Range.Start.Block].PlainText;
        selected.ShouldBe("100");
        controller.Selection.Range.End.Offset.ShouldBe(3);

        controller.HandleTab(shift: true).ShouldBeTrue();
        document.Paragraphs[controller.Selection.Range.Start.Block].PlainText.ShouldBe("North");
    }

    [Fact]
    public async Task TabInTheLastCellAddsARow()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var last = Cell(document, "405");
        var table = (DocumentTable)document.Blocks[document.TopBlockOf(last)];
        var rows = table.Rows.Count;

        controller.Selection.MoveTo(new DocumentPosition(last, 0));
        controller.HandleTab().ShouldBeTrue();

        ((DocumentTable)document.Blocks[document.TopBlockOf(last)]).Rows.Count.ShouldBe(rows + 1);
        controller.CurrentCell!.Row.ShouldBe(rows);
    }

    [Fact]
    public async Task RowsAndColumnsInsertDeleteAndUndo()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var cell = Cell(document, "100");
        var top = document.TopBlockOf(cell);
        DocumentTable Table() => (DocumentTable)document.Blocks[top];

        var rows = Table().Rows.Count;
        var cells = Table().Rows[1].Cells.Count;

        controller.Selection.MoveTo(new DocumentPosition(cell, 0));
        controller.InsertTableRowAbove();
        Table().Rows.Count.ShouldBe(rows + 1);

        // The caret stays in its own cell, which is now a row further down.
        document.Paragraphs[controller.Selection.Focus.Block].PlainText.ShouldBe("100");

        controller.InsertTableColumnRight();
        Table().Rows[2].Cells.Count.ShouldBe(cells + 1);

        controller.DeleteTableColumn();
        Table().Rows[2].Cells.Count.ShouldBe(cells);

        controller.DeleteTableRow();
        Table().Rows.Count.ShouldBe(rows);

        controller.Undo();
        controller.Undo();
        controller.Undo();
        controller.Undo();

        Table().Rows.Count.ShouldBe(rows);
        Table().Rows[1].Cells.Count.ShouldBe(cells);

        using var reopened = await SaveAndReopenAsync(document);
    }

    [Fact]
    public async Task InsertingAColumnBesideASpannedCellWidensIt()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        // "Total" sits beside a cell spanning two grid columns.
        var cell = Cell(document, "Q1");
        controller.Selection.MoveTo(new DocumentPosition(cell, 0));
        controller.InsertTableColumnRight();

        var table = (DocumentTable)document.Blocks[document.TopBlockOf(cell)];
        table.Rows[0].Cells.Count.ShouldBe(4);
        table.Rows[3].Cells.Last().ColumnSpan.ShouldBe(3);

        using var reopened = await SaveAndReopenAsync(document);
    }

    [Fact]
    public async Task MergingAndSplittingCells()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var from = Cell(document, "100");
        var to = Cell(document, "120");
        var top = document.TopBlockOf(from);

        controller.Selection.Select(new DocumentPosition(from, 0), new DocumentPosition(to, 3));
        controller.CanMergeTableCells.ShouldBeTrue();
        controller.MergeTableCells();

        var row = ((DocumentTable)document.Blocks[top]).Rows[1];
        row.Cells.Count.ShouldBe(2);
        row.Cells[1].ColumnSpan.ShouldBe(2);
        row.Cells[1].Blocks.OfType<DocumentParagraph>().Select(x => x.PlainText).ShouldBe(["100", "120"]);

        var merged = document.Paragraphs.ToList().FindIndex(p => p.PlainText == "100");
        controller.Selection.MoveTo(new DocumentPosition(merged, 0));
        controller.SplitTableCell();

        ((DocumentTable)document.Blocks[top]).Rows[1].Cells.Count.ShouldBe(3);

        using var reopened = await SaveAndReopenAsync(document);
    }

    [Fact]
    public async Task DeletingATableUndoesBackToIt()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var tables = document.Blocks.OfType<DocumentTable>().Count();
        controller.Selection.MoveTo(new DocumentPosition(Cell(document, "North"), 0));
        controller.DeleteTable();

        document.Blocks.OfType<DocumentTable>().Count().ShouldBe(tables - 1);
        controller.IsInTable.ShouldBeFalse();

        controller.Undo();
        document.Blocks.OfType<DocumentTable>().Count().ShouldBe(tables);
    }

    [Fact]
    public async Task BackspaceAtTheStartOfACellDoesNotJoinAcrossCells()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var cell = Cell(document, "100");
        controller.Selection.MoveTo(new DocumentPosition(cell, 0));
        controller.DeleteBackward();

        document.Paragraphs[cell].PlainText.ShouldBe("100");
        document.Paragraphs[cell - 1].PlainText.ShouldBe("North");
    }

    [Fact]
    public async Task ClickingInsideACellPlacesTheCaretInThatCell()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var cell = Cell(document, "120");
        var laidOut = controller.StoryParagraphs[cell];
        var bounds = controller.StoryCellBounds(cell)!.Value;

        // A point inside the cell's box, in control coordinates.
        var viewY = controller.Pagination.FlowToView(laidOut.Y + 2) - controller.Viewport.ScrollY;
        var x = (bounds.X + (bounds.Width / 2) + controller.ContentX) * controller.ViewScale;
        var position = controller.PositionAt(x, viewY * controller.ViewScale);

        position.ShouldNotBeNull();
        position!.Value.Block.ShouldBe(cell);
    }

    // ---- find and replace ----

    [Fact]
    public async Task ReplaceAllIsOneUndoStep()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var before = document.PlainText;
        var count = controller.ReplaceAll("item", "entry");

        count.ShouldBe(3);
        document.PlainText.ShouldNotContain("item");
        document.PlainText.ShouldContain("First entry");

        controller.Undo();
        document.PlainText.ShouldBe(before);
    }

    [Fact]
    public async Task ReplaceAllHonoursMatchCaseAndWholeWord()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        controller.ReplaceAll("Item", "X", new FindOptions { MatchCase = true }).ShouldBe(0);
        controller.ReplaceAll("ite", "X", new FindOptions { WholeWord = true }).ShouldBe(0);
        controller.ReplaceAll("North", "South").ShouldBe(1);
        document.Paragraphs.ShouldContain(p => p.PlainText == "South");
    }

    [Fact]
    public async Task ReplaceStepsThroughMatches()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        controller.Find.Query = "item";
        controller.Find.Count.ShouldBe(3);

        controller.ReplaceCurrent("entry").ShouldBeTrue();
        controller.Find.Count.ShouldBe(2);
        controller.ReplaceCurrent("entry").ShouldBeTrue();
        controller.ReplaceCurrent("entry").ShouldBeTrue();
        controller.Find.Count.ShouldBe(0);
    }

    [Fact]
    public async Task ReplacementKeepsTheFormattingItReplaces()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        controller.ReplaceAll("underlined", "UNDER");

        var paragraph = document.Paragraphs.First(p => p.PlainText.Contains("UNDER"));
        paragraph.Runs.First(r => r.Text.Contains("UNDER")).Style.Underline.ShouldBe(UnderlineStyle.Single);
    }

    // ---- clipboard and format painter ----

    [Fact]
    public async Task CopyAndPasteKeepsFormatting()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var source = document.Paragraphs.ToList().FindIndex(p => p.PlainText.StartsWith("Bold italic"));
        controller.Selection.Select(new DocumentPosition(source, 0), new DocumentPosition(source, 4));

        var text = controller.Copy();
        text.ShouldBe("Bold");

        var body = Body(document);
        controller.Selection.MoveTo(new DocumentPosition(body, 0));
        controller.Paste(text);

        var pasted = document.Paragraphs[body];
        pasted.PlainText.ShouldStartWith("BoldPlain");
        pasted.Runs[0].Style.Bold.ShouldBeTrue();
        controller.Selection.Focus.Offset.ShouldBe(4);

        controller.Undo();
        document.Paragraphs[body].PlainText.ShouldStartWith("Plain");
    }

    [Fact]
    public async Task PastingTextFromElsewhereIsPlainAndSplitsLines()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var body = Body(document);
        controller.Copy();
        controller.Selection.MoveTo(new DocumentPosition(body, 0));
        controller.Paste("one\ntwo ");

        document.Paragraphs[body].PlainText.ShouldBe("one");
        document.Paragraphs[body + 1].PlainText.ShouldStartWith("two Plain");
    }

    [Fact]
    public async Task CutRemovesTheSelection()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var body = Body(document);
        controller.Selection.Select(new DocumentPosition(body, 0), new DocumentPosition(body, 6));

        controller.Cut().ShouldBe("Plain ");
        document.Paragraphs[body].PlainText.ShouldStartWith("body");
    }

    [Fact]
    public async Task MultiParagraphPasteLandsAsParagraphs()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var first = document.Paragraphs.ToList().FindIndex(p => p.PlainText == "First item");
        controller.Selection.Select(new DocumentPosition(first, 0), new DocumentPosition(first + 1, 6));
        controller.Copy();

        var body = Body(document);
        var count = document.Paragraphs.Count;
        controller.Selection.MoveTo(new DocumentPosition(body, 5));
        controller.Paste();

        document.Paragraphs.Count.ShouldBe(count + 1);
        document.Paragraphs[body].PlainText.ShouldBe("PlainFirst item");
        document.Paragraphs[body + 1].PlainText.ShouldStartWith("Second body text");

        using var reopened = await SaveAndReopenAsync(document);
    }

    [Fact]
    public async Task TheFormatPainterCopiesCharacterFormatting()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var source = document.Paragraphs.ToList().FindIndex(p => p.PlainText.StartsWith("Bold italic"));
        controller.Selection.MoveTo(new DocumentPosition(source, 2));
        controller.CopyFormatting();
        controller.IsFormatPainterActive.ShouldBeTrue();

        var body = Body(document);
        controller.Selection.Select(new DocumentPosition(body, 0), new DocumentPosition(body, 5));
        controller.CompletePointerGesture();

        document.Paragraphs[body].Runs[0].Style.Bold.ShouldBeTrue();
        controller.IsFormatPainterActive.ShouldBeFalse();
    }

    // ---- font group ----

    [Fact]
    public async Task GrowAndShrinkStepThroughWordsSizes()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var body = Body(document);
        controller.Selection.Select(new DocumentPosition(body, 0), new DocumentPosition(body, 5));

        controller.CaretFormat.FontSize.ShouldBe(11);
        controller.GrowFont();
        controller.CaretFormat.FontSize.ShouldBe(12);
        controller.GrowFont();
        controller.CaretFormat.FontSize.ShouldBe(14);
        controller.ShrinkFont();
        controller.CaretFormat.FontSize.ShouldBe(12);
    }

    [Theory]
    [InlineData(TextCase.Upper, "PLAIN")]
    [InlineData(TextCase.Lower, "plain")]
    [InlineData(TextCase.Toggle, "pLAIN")]
    public async Task ChangeCaseRewritesTheSelection(TextCase mode, string expected)
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var body = Body(document);
        controller.Selection.Select(new DocumentPosition(body, 0), new DocumentPosition(body, 5));
        controller.ChangeCase(mode);

        document.Paragraphs[body].PlainText[..5].ShouldBe(expected);
    }

    [Fact]
    public void SentenceAndCapitalizeCaseFollowWordBoundaries()
    {
        ChangeCaseCommand.Transform("hello WORLD. again", TextCase.Sentence).ShouldBe("Hello world. Again");
        ChangeCaseCommand.Transform("hello WORLD", TextCase.Capitalize).ShouldBe("Hello World");
    }

    [Fact]
    public async Task SuperscriptAndSubscriptRoundTrip()
    {
        var (document, controller) = await OpenAsync();

        var body = Body(document);
        controller.Selection.Select(new DocumentPosition(body, 0), new DocumentPosition(body, 2));
        controller.ToggleSuperscript();
        controller.CaretFormat.Superscript.ShouldBeTrue();

        controller.Selection.Select(new DocumentPosition(body, 3), new DocumentPosition(body, 4));
        controller.ToggleSubscript();

        using var reopened = await SaveAndReopenAsync(document);
        document.Dispose();

        var runs = reopened.Paragraphs[body].Runs;
        runs[0].Style.BaselineShift.ShouldBeGreaterThan(0);
        runs.ShouldContain(x => x.Style.BaselineShift < 0);
    }

    [Fact]
    public async Task ClearFormattingRemovesDirectFormatting()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var source = document.Paragraphs.ToList().FindIndex(p => p.PlainText.StartsWith("Bold italic"));
        controller.Selection.Select(new DocumentPosition(source, 0), new DocumentPosition(source, document.Paragraphs[source].PlainText.Length));
        controller.ClearFormatting();

        document.Paragraphs[source].Runs.ShouldAllBe(x => !x.Style.Bold && !x.Style.Italic);
        document.Paragraphs[source].Format.Alignment.ShouldBe(Office.Text.TextAlignment.Left);
    }

    [Fact]
    public async Task RunPropertiesStayInSchemaOrder()
    {
        // Colour after underline is the classic out-of-sequence rPr: set them in the "wrong" order.
        var (document, controller) = await OpenAsync();

        var body = Body(document);
        controller.Selection.Select(new DocumentPosition(body, 0), new DocumentPosition(body, 5));
        controller.ToggleUnderline();
        controller.SetTextColor(new ArgbColor(255, 200, 0, 0));
        controller.SetHighlight(new ArgbColor(255, 255, 255, 0));
        controller.ToggleBold();
        controller.SetFontSize(18);
        controller.ToggleSuperscript();

        using var reopened = await SaveAndReopenAsync(document);
        document.Dispose();
    }

    // ---- paragraph group ----

    [Fact]
    public async Task ParagraphFormattingRoundTrips()
    {
        var (document, controller) = await OpenAsync();

        var body = Body(document);
        controller.Selection.MoveTo(new DocumentPosition(body, 0));

        controller.SetLineSpacing(1.5);
        controller.SetParagraphSpacing(12, 6);
        controller.ChangeIndent(1);
        controller.SetIndents(null, 24, -24);
        controller.SetParagraphShading(new ArgbColor(255, 0xDD, 0xEE, 0xFF));
        controller.SetParagraphBorders(ParagraphBorderPreset.Bottom);

        var format = controller.CaretFormat;
        format.LineSpacing.ShouldBe(1.5);
        format.SpaceBefore.ShouldBe(12);
        format.SpaceAfter.ShouldBe(6);
        format.IndentLeft.ShouldBe(DocumentEditorController.IndentStep);
        format.IndentFirstLine.ShouldBe(-24, 0.5);
        format.Shading.ShouldNotBeNull();
        format.Borders!.Bottom.ShouldBeTrue();

        using var reopened = await SaveAndReopenAsync(document);
        document.Dispose();

        var paragraph = reopened.Paragraphs[body];
        paragraph.Format.LineSpacing.ShouldBe(1.5, 0.01);
        paragraph.Format.IndentLeft.ShouldBe(DocumentEditorController.IndentStep, 0.5);
        paragraph.Format.Borders!.Bottom.ShouldBeTrue();
        paragraph.Format.Shading.ShouldBe(new ArgbColor(255, 0xDD, 0xEE, 0xFF));
    }

    [Fact]
    public async Task DecreaseIndentStopsAtZero()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        controller.Selection.MoveTo(new DocumentPosition(Body(document), 0));
        controller.ChangeIndent(-1);
        controller.CaretFormat.IndentLeft.ShouldBe(0);
    }

    // ---- styles ----

    [Fact]
    public async Task TheGalleryOffersWordsBuiltInStylesWithPreviews()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var styles = controller.AvailableStyles;
        styles.Select(x => x.Id).ShouldContain("Normal");
        styles.Select(x => x.Id).ShouldContain("Heading1");
        styles.Select(x => x.Id).ShouldContain("Title");

        var heading = styles.First(x => x.Id == "Heading1");
        heading.Bold.ShouldBeTrue();
        heading.FontSize.ShouldBe(16);
        heading.OutlineLevel.ShouldBe(1);

        styles.First(x => x.Id == "Title").FontSize.ShouldBe(28);
    }

    [Fact]
    public async Task ApplyingAMissingBuiltInStyleCreatesIt()
    {
        var (document, controller) = await OpenAsync();

        var body = Body(document);
        controller.Selection.MoveTo(new DocumentPosition(body, 0));

        var changed = 0;
        controller.CurrentStyleChanged += (_, _) => changed++;
        controller.ApplyStyle("Title");

        controller.CurrentStyleId.ShouldBe("Title");
        changed.ShouldBeGreaterThan(0);
        document.Paragraphs[body].Runs[0].Style.FontSize.ShouldBe(OoxmlUnits.PointsToPixels(28), 0.1);

        using var reopened = await SaveAndReopenAsync(document);
        document.Dispose();
        reopened.Paragraphs[body].StyleName.ShouldBe("Title");
    }

    // ---- insert ----

    [Fact]
    public async Task HyperlinksAreWrittenAsRelationshipsAndCanBeRemoved()
    {
        var (document, controller) = await OpenAsync();

        var body = Body(document);
        controller.Selection.Select(new DocumentPosition(body, 0), new DocumentPosition(body, 5));
        controller.InsertHyperlink("https://shinylib.net/");

        controller.Selection.MoveTo(new DocumentPosition(body, 2));
        controller.CurrentHyperlink!.Target.ShouldBe("https://shinylib.net/");
        controller.CaretFormat.Hyperlink.ShouldBe("https://shinylib.net/");

        var saved = document.ToArray();
        NewSchemaErrors(saved).ShouldBeEmpty();

        using (var package = WordprocessingDocument.Open(new MemoryStream(saved), false))
        {
            var link = package.MainDocumentPart!.Document!.Body!.Descendants<W.Hyperlink>().Single();
            package.MainDocumentPart.HyperlinkRelationships.Single(x => x.Id == link.Id).Uri.ToString().ShouldBe("https://shinylib.net/");
        }

        controller.RemoveHyperlink();
        controller.CurrentHyperlink.ShouldBeNull();
        document.Dispose();
    }

    [Fact]
    public async Task ALinkToABookmarkJumpsThere()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var target = document.Paragraphs.ToList().FindIndex(p => p.PlainText == "Second item");
        controller.Selection.MoveTo(new DocumentPosition(target, 0));
        controller.InsertBookmark("second").ShouldBeTrue();
        controller.Bookmarks.Select(x => x.Name).ShouldContain("second");

        var body = Body(document);
        controller.Selection.Select(new DocumentPosition(body, 0), new DocumentPosition(body, 5));
        controller.InsertHyperlink("#second");

        controller.FollowLink(controller.HyperlinkAt(new DocumentPosition(body, 1))!);
        controller.Selection.Focus.Block.ShouldBe(target);
    }

    [Fact]
    public async Task InsertingALinkWithNothingSelectedTypesItsText()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var body = Body(document);
        controller.Selection.MoveTo(new DocumentPosition(body, 0));
        controller.InsertHyperlink("https://example.com", "Example");

        document.Paragraphs[body].PlainText.ShouldStartWith("ExamplePlain");
        document.Paragraphs[body].Runs[0].Style.Link.ShouldBe("https://example.com/");
    }

    [Fact]
    public async Task SectionBreaksColumnsPageSizeAndColourRoundTrip()
    {
        var (document, controller) = await OpenAsync();

        controller.Selection.MoveTo(new DocumentPosition(Body(document), 5));
        controller.InsertSectionBreak(SectionBreakType.NextPage);
        controller.SetColumns(2);
        controller.SetPaperSize(PaperSize.A4);
        controller.SetPageColor(new ArgbColor(255, 0xFF, 0xF0, 0xD0));
        controller.SetWatermarkText("DRAFT");

        controller.ColumnCount.ShouldBe(2);
        controller.PaperSize.ShouldBe(PaperSize.A4);
        controller.WatermarkText.ShouldBe("DRAFT");

        using var reopened = await SaveAndReopenAsync(document);
        document.Dispose();

        reopened.Paragraphs.ShouldContain(p => p.Format.SectionBreak == SectionBreakKind.NextPage);
        reopened.PageColor.ShouldBe(new ArgbColor(255, 0xFF, 0xF0, 0xD0));
        reopened.WatermarkText.ShouldBe("DRAFT");
        reopened.Page.Width.ShouldBe(PaperSize.A4.Width, 1);
    }

    [Fact]
    public async Task ANextPageSectionBreakStartsANewPage()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        controller.PageLayout = DocumentPageLayout.Print;
        var pages = controller.Pagination.Count;

        controller.Selection.MoveTo(new DocumentPosition(Body(document), 5));
        controller.InsertSectionBreak();

        controller.Pagination.Count.ShouldBe(pages + 1);
    }

    [Fact]
    public async Task HorizontalLineBlankPageDateAndSymbols()
    {
        var (document, controller) = await OpenAsync();

        var body = Body(document);
        controller.Selection.MoveTo(new DocumentPosition(body, 0));
        controller.InsertSymbol("©");
        controller.InsertDateTime("yyyy");
        document.Paragraphs[body].PlainText.ShouldStartWith("©" + DateTime.Now.Year);

        controller.InsertHorizontalLine();
        document.Paragraphs[body + 1].Format.Borders!.Bottom.ShouldBeTrue();

        controller.InsertBlankPage();
        controller.InsertPageNumberField();

        using var reopened = await SaveAndReopenAsync(document);
        document.Dispose();
    }

    // ---- references ----

    [Fact]
    public async Task ATableOfContentsListsHeadingsAndUpdatesInPlace()
    {
        var (document, controller) = await OpenAsync();

        controller.Selection.MoveTo(new DocumentPosition(0, 0));
        controller.InsertTableOfContents();

        controller.HasTableOfContents.ShouldBeTrue();
        document.Paragraphs.ShouldContain(p => p.PlainText.StartsWith("Quarterly Report") && p.StyleName == "toc 1");

        // The heading is still there, now bookmarked, and linked from its entry.
        var entry = document.Paragraphs.First(p => p.StyleName == "toc 1");
        entry.Runs[0].Style.Link.ShouldNotBeNull();
        entry.Runs[0].Style.Link!.ShouldStartWith("#_Toc");

        var count = document.Paragraphs.Count;
        controller.UpdateTableOfContents();
        document.Paragraphs.Count.ShouldBe(count);

        using var reopened = await SaveAndReopenAsync(document);
        document.Dispose();
        reopened.Paragraphs.Count(p => p.StyleName == "toc 1").ShouldBe(1);
    }

    [Fact]
    public async Task FootnotesAreNumberedInOrderAndSaved()
    {
        var (document, controller) = await OpenAsync();

        var body = Body(document);
        controller.Selection.MoveTo(new DocumentPosition(body, 5));
        controller.InsertFootnote("Second note");

        controller.Selection.MoveTo(new DocumentPosition(body, 0));
        controller.InsertFootnote("First note");

        var notes = controller.Footnotes;
        notes.Count.ShouldBe(2);
        notes[0].Text.ShouldBe("1 First note");
        notes[1].Text.ShouldBe("2 Second note");

        // The reference marks are drawn as their numbers, in document order.
        var marks = document.Paragraphs[body].Runs.Where(x => x.FootnoteId is not null).Select(x => x.Text).ToList();
        marks.ShouldBe(["1", "2"]);

        controller.PageLayout = DocumentPageLayout.Print;
        controller.VisiblePages()[0].Footnotes.ShouldNotBeNull();

        using var reopened = await SaveAndReopenAsync(document);
        document.Dispose();
        reopened.Footnotes.Count.ShouldBe(2);
    }

    // ---- review ----

    [Fact]
    public async Task CommentsAreAnchoredSavedAndDeleted()
    {
        var (document, controller) = await OpenAsync();

        var body = Body(document);
        controller.Selection.Select(new DocumentPosition(body, 0), new DocumentPosition(body, 5));
        var comment = controller.AddComment("Check this");

        comment.ShouldNotBeNull();
        comment!.Author.ShouldBe("Tester");
        comment.Range.ShouldBe(new DocumentRange(new DocumentPosition(body, 0), new DocumentPosition(body, 5)));
        document.Paragraphs[body].PlainText.ShouldStartWith("Plain body");

        controller.PageLayout = DocumentPageLayout.Print;
        controller.CommentMarks().Single().Balloon.ShouldNotBeNull();

        var saved = document.ToArray();
        NewSchemaErrors(saved).ShouldBeEmpty();

        using (var reopened = await WordDocument.OpenAsync(new MemoryStream(saved)))
        {
            reopened.Comments.Single().Text.ShouldBe("Check this");
            reopened.Comments.Single().Range.End.Offset.ShouldBe(5);
        }

        controller.Selection.MoveTo(new DocumentPosition(body, 2));
        controller.DeleteComment();
        controller.Comments.ShouldBeEmpty();

        controller.Undo();
        controller.Comments.Count.ShouldBe(1);
        document.Dispose();
    }

    [Fact]
    public async Task TrackedTypingAndDeletingCanBeAcceptedOrRejected()
    {
        var (document, controller) = await OpenAsync();

        var body = Body(document);
        controller.IsTrackingChanges = true;
        document.IsTrackingRevisions.ShouldBeTrue();

        controller.Selection.MoveTo(new DocumentPosition(body, 0));
        controller.InsertText("New ");

        controller.Selection.Select(new DocumentPosition(body, 4), new DocumentPosition(body, 10));
        controller.DeleteForward();

        controller.Revisions.Count.ShouldBe(2);
        controller.Revisions[0].Kind.ShouldBe(RevisionKind.Insertion);
        controller.Revisions[0].Author.ShouldBe("Tester");
        controller.Revisions[1].Kind.ShouldBe(RevisionKind.Deletion);

        // Deleted text stays visible, struck through, until the change is accepted.
        document.Paragraphs[body].PlainText.ShouldStartWith("New Plain body");
        document.Paragraphs[body].VisibleText.ShouldStartWith("New body");

        var saved = document.ToArray();
        NewSchemaErrors(saved).ShouldBeEmpty();

        using (var reopened = await WordDocument.OpenAsync(new MemoryStream(saved), editable: true))
        {
            reopened.Revisions.Count.ShouldBe(2);
            reopened.IsTrackingRevisions.ShouldBeTrue();
        }

        controller.AcceptAllChanges();
        controller.Revisions.ShouldBeEmpty();
        document.Paragraphs[body].PlainText.ShouldStartWith("New body");

        controller.Undo();
        controller.Revisions.Count.ShouldBe(2);

        controller.RejectAllChanges();
        document.Paragraphs[body].PlainText.ShouldStartWith("Plain body");
        document.Dispose();
    }

    [Fact]
    public async Task TrackedTypingCoalescesIntoOneInsertion()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var body = Body(document);
        controller.IsTrackingChanges = true;
        controller.Selection.MoveTo(new DocumentPosition(body, 0));

        foreach (var c in "word")
            controller.InsertText(c.ToString());

        controller.Revisions.Count.ShouldBe(1);
        controller.Revisions[0].Range.End.Offset.ShouldBe(4);

        controller.Undo();
        controller.Revisions.ShouldBeEmpty();
    }

    [Fact]
    public async Task NextChangeAndCommentNavigation()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var body = Body(document);
        controller.Selection.Select(new DocumentPosition(body, 0), new DocumentPosition(body, 5));
        controller.AddComment("One");

        var list = document.Paragraphs.ToList().FindIndex(p => p.PlainText == "Second item");
        controller.Selection.Select(new DocumentPosition(list, 0), new DocumentPosition(list, 6));
        controller.AddComment("Two");

        controller.Selection.MoveTo(DocumentPosition.Start);
        controller.NextComment().ShouldBeTrue();
        controller.Selection.Range.Start.Block.ShouldBe(body);
        controller.NextComment().ShouldBeTrue();
        controller.Selection.Range.Start.Block.ShouldBe(list);
        controller.PreviousComment().ShouldBeTrue();
        controller.Selection.Range.Start.Block.ShouldBe(body);
    }

    [Fact]
    public async Task StatisticsCountWordsCharactersAndPages()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var stats = controller.Statistics;
        stats.Words.ShouldBeGreaterThan(20);
        stats.CharactersWithSpaces.ShouldBeGreaterThan(stats.CharactersNoSpaces);
        stats.Paragraphs.ShouldBeGreaterThan(5);
        stats.Pages.ShouldBe(1);

        var raised = 0;
        controller.StatisticsChanged += (_, _) => raised++;
        controller.Selection.MoveTo(new DocumentPosition(Body(document), 0));
        controller.InsertText("extra words here ");

        raised.ShouldBeGreaterThan(0);
        controller.Statistics.Words.ShouldBe(stats.Words + 3);

        DocumentStatistics.CountWords("don't e-mail  me").ShouldBe(3);
    }

    // ---- view ----

    [Fact]
    public async Task HeadingsFeedANavigationPane()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var headings = controller.Headings();
        headings.Single().Text.ShouldBe("Quarterly Report");

        controller.GoToParagraph(headings[0].Paragraph);
        controller.Selection.Focus.Block.ShouldBe(headings[0].Paragraph);
    }

    [Fact]
    public async Task ZoomRaisesItsOwnEvent()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        var raised = 0;
        controller.ZoomChanged += (_, _) => raised++;
        controller.Zoom = 1.5;
        raised.ShouldBe(1);
    }

    // ---- shortcuts ----

    [Theory]
    [InlineData("b", false, false, WordCommand.Bold)]
    [InlineData("E", false, false, WordCommand.AlignCenter)]
    [InlineData(">", true, false, WordCommand.GrowFont)]
    [InlineData("<", true, false, WordCommand.ShrinkFont)]
    [InlineData("=", false, false, WordCommand.Subscript)]
    [InlineData("+", true, false, WordCommand.Superscript)]
    [InlineData("Enter", false, false, WordCommand.PageBreak)]
    [InlineData("5", false, false, WordCommand.LineSpacingOneAndHalf)]
    [InlineData("2", false, true, WordCommand.Heading2)]
    [InlineData("h", false, false, WordCommand.Replace)]
    [InlineData("k", false, false, WordCommand.Hyperlink)]
    public void ShortcutsResolveToWordsCommands(string key, bool shift, bool alt, WordCommand expected)
        => WordShortcuts.Resolve(key, command: true, shift, alt).ShouldBe(expected);

    [Fact]
    public async Task ShortcutsRunEngineCommandsAndLeaveHostOnesToTheHost()
    {
        var (document, controller) = await OpenAsync();
        using var _ = document;

        controller.Selection.Select(new DocumentPosition(Body(document), 0), new DocumentPosition(Body(document), 5));

        controller.HandleShortcut("b", command: true, shift: false, alt: false, out var handled).ShouldBe(WordCommand.Bold);
        handled.ShouldBeTrue();
        controller.CaretFormat.Bold.ShouldBeTrue();

        controller.HandleShortcut("k", command: true, shift: false, alt: false, out handled).ShouldBe(WordCommand.Hyperlink);
        handled.ShouldBeFalse();
    }

    // ---- round trip ----

    [Fact]
    public async Task AnUnmodifiedDocumentSavesByteIdentical()
    {
        var original = DocumentFixture.Build();
        using var document = await WordDocument.OpenAsync(new MemoryStream(original), editable: true);
        using var controller = new DisposableController(new DocumentEditorController(document, new Fixed()));

        // Reading everything the new features read must not dirty the package.
        _ = document.Comments;
        _ = document.Footnotes;
        _ = document.Revisions;
        _ = controller.Controller.AvailableStyles;
        _ = controller.Controller.Statistics;

        document.ToArray().ShouldBe(original);
    }

    sealed class DisposableController(DocumentEditorController controller) : IDisposable
    {
        public DocumentEditorController Controller { get; } = controller;

        public void Dispose()
        {
        }
    }
}
