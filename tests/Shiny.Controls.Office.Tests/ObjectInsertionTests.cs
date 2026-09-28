using DocumentFormat.OpenXml.Packaging;
using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using Shouldly;
using Xunit;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// Inserting shapes, pictures and tables, and the highlight formatting that came with them.
/// </summary>
/// <remarks>
/// The assertions that matter most here are the ones about <b>offsets</b>. An inline object occupies
/// one character in the layout engine's source offsets, and the paragraph editor's offset space has to
/// agree — when it did not, every caret position after a picture was wrong by one, silently, in a way
/// no test noticed because no fixture had a picture in it.
/// </remarks>
public class ObjectInsertionTests
{
    sealed class Fixed : ITextMeasurer
    {
        public TextMetrics Measure(ReadOnlySpan<char> text, TextStyle style)
            => new(text.Length * 8, style.FontSize * 0.8, style.FontSize * 0.2);

        public TextMetrics LineMetrics(TextStyle style)
            => new(0, style.FontSize * 0.8, style.FontSize * 0.2);
    }

    /// <summary>A one-pixel PNG, so the image path can be exercised without a fixture file.</summary>
    static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    static async Task<(WordDocument Document, DocumentEditorController Controller)> WordAsync()
    {
        var document = await WordDocument.OpenAsync(new MemoryStream(DocumentFixture.Build()), editable: true);
        var controller = new DocumentEditorController(document, new Fixed());
        controller.Resize(800, 400);
        return (document, controller);
    }

    static int BodyBlock(WordDocument document)
        => document.Blocks.ToList().FindIndex(x => x is DocumentParagraph p && p.PlainText.StartsWith("Plain body text"));

    static DocumentParagraph ParagraphAt(WordDocument document, int block)
        => (DocumentParagraph)document.Blocks[block];

    static async Task<SlideDeck> DeckAsync()
    {
        using var source = new MemoryStream(SlideFixture.Build(), writable: false);
        return await SlideDeck.OpenAsync(source, editable: true);
    }

    static SlideEditorController SlideController(SlideDeck deck, int slide = 1)
    {
        var controller = new SlideEditorController(deck, new Fixed());
        controller.Resize(960, 540);
        controller.Index = slide;
        return controller;
    }

    // ---- Word: shapes ----

    [Fact]
    public async Task InsertingAShapePutsAnInlineShapeInTheParagraph()
    {
        var (document, controller) = await WordAsync();
        using var _ = document;

        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.InsertShape(ShapeGeometry.Ellipse, 100, 80);

        var shape = ParagraphAt(document, block).Runs
            .Select(x => x.Inline)
            .OfType<InlineShape>()
            .ShouldHaveSingleItem();

        shape.Geometry.ShouldBe(ShapeGeometry.Ellipse);
        shape.Width.ShouldBe(100, 0.5);
        shape.Height.ShouldBe(80, 0.5);
    }

    [Fact]
    public async Task AnInsertedShapeSurvivesASaveAndReopen()
    {
        var (document, controller) = await WordAsync();

        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.InsertShape(ShapeGeometry.Star5, 120, 120);

        var saved = document.ToArray();
        document.Dispose();

        using var reopened = await WordDocument.OpenAsync(new MemoryStream(saved, writable: false));

        reopened.Blocks
            .OfType<DocumentParagraph>()
            .SelectMany(x => x.Runs)
            .Select(x => x.Inline)
            .OfType<InlineShape>()
            .ShouldContain(x => x.Geometry == ShapeGeometry.Star5);
    }

    [Fact]
    public async Task AnInsertedShapeUndoesAway()
    {
        var (document, controller) = await WordAsync();
        using var _ = document;

        var block = BodyBlock(document);
        var before = ParagraphAt(document, block).PlainText;

        controller.Selection.MoveTo(new DocumentPosition(block, 3));
        controller.InsertShape(ShapeGeometry.Diamond);
        controller.Undo();

        ParagraphAt(document, block).Runs.ShouldAllBe(x => x.Inline == null);
        ParagraphAt(document, block).PlainText.ShouldBe(before);
    }

    // ---- Word: the offset space ----

    [Fact]
    public async Task AnInlineObjectCountsAsOneCharacterForTyping()
    {
        var (document, controller) = await WordAsync();
        using var _ = document;

        var block = BodyBlock(document);

        // Object at offset 0, so everything after it is shifted by exactly one.
        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.InsertShape(ShapeGeometry.Rectangle);

        var before = ParagraphAt(document, block).PlainText;

        // Offset 1 is immediately after the object; the text must land at the very start of the text.
        controller.Selection.MoveTo(new DocumentPosition(block, 1));
        controller.InsertText("XY");

        // The object is one placeholder character in the offset space, and the text follows it.
        ParagraphAt(document, block).PlainText.ShouldBe(before[..1] + "XY" + before[1..]);
    }

    [Fact]
    public async Task DeletingOverAnInlineObjectRemovesTheWholeThing()
    {
        var (document, controller) = await WordAsync();
        using var _ = document;

        var block = BodyBlock(document);
        var before = ParagraphAt(document, block).PlainText;

        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.InsertShape(ShapeGeometry.Rectangle);

        // The caret sits just after the object, so one backspace takes it.
        controller.DeleteBackward();

        ParagraphAt(document, block).Runs.ShouldAllBe(x => x.Inline == null);
        ParagraphAt(document, block).PlainText.ShouldBe(before);
    }

    [Fact]
    public async Task TypingAfterAPictureLandsAfterIt()
    {
        var (document, controller) = await WordAsync();
        using var _ = document;

        var block = BodyBlock(document);
        var before = ParagraphAt(document, block).PlainText;

        // Object in the middle of the text: offsets on both sides have to stay honest.
        controller.Selection.MoveTo(new DocumentPosition(block, 5));
        controller.InsertImage(Png(), "image/png", 40, 40);

        controller.Selection.MoveTo(new DocumentPosition(block, 6));
        controller.InsertText("|");

        ParagraphAt(document, block).PlainText.ShouldBe(before[..5] + WordParagraphEditorPlaceholder + "|" + before[5..]);
    }

    const string WordParagraphEditorPlaceholder = "\uFFFC";

    // ---- Word: pictures ----

    [Fact]
    public async Task InsertingAPictureAddsAnImagePartAndReferencesIt()
    {
        var (document, controller) = await WordAsync();

        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.InsertImage(Png(), "image/png", 64, 64, "Logo");

        var saved = document.ToArray();
        document.Dispose();

        using var package = WordprocessingDocument.Open(new MemoryStream(saved, writable: false), false);

        package.MainDocumentPart!.ImageParts.ShouldNotBeEmpty();

        var blip = package.MainDocumentPart.Document!.Body!
            .Descendants<DocumentFormat.OpenXml.Drawing.Blip>()
            .ShouldHaveSingleItem();

        // The reference has to resolve, or Word shows a red X where the picture should be.
        package.MainDocumentPart.GetPartById(blip.Embed!.Value!).ShouldBeOfType<ImagePart>();
    }

    [Fact]
    public async Task AnInsertedPictureComesBackAsAnInlineImage()
    {
        var (document, controller) = await WordAsync();

        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.InsertImage(Png(), "image/png", 64, 48);

        var saved = document.ToArray();
        document.Dispose();

        using var reopened = await WordDocument.OpenAsync(new MemoryStream(saved, writable: false));

        var image = reopened.Blocks
            .OfType<DocumentParagraph>()
            .SelectMany(x => x.Runs)
            .Select(x => x.Inline)
            .OfType<InlineImage>()
            .ShouldHaveSingleItem();

        image.Width.ShouldBe(64, 1);
        image.Height.ShouldBe(48, 1);
    }

    // ---- Word: resizing ----

    [Fact]
    public async Task ResizingAnInlineObjectChangesItsExtent()
    {
        var (document, controller) = await WordAsync();
        using var _ = document;

        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.InsertShape(ShapeGeometry.Rectangle, 100, 100);

        document.Execute(new ResizeInlineObjectCommand(new DocumentPosition(block, 0), 250, 60));

        var shape = ParagraphAt(document, block).Runs
            .Select(x => x.Inline)
            .OfType<InlineShape>()
            .ShouldHaveSingleItem();

        shape.Width.ShouldBe(250, 1);
        shape.Height.ShouldBe(60, 1);
    }

    [Fact]
    public async Task ResizingUndoesToTheOriginalSize()
    {
        var (document, controller) = await WordAsync();
        using var _ = document;

        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.InsertShape(ShapeGeometry.Rectangle, 100, 100);

        document.Execute(new ResizeInlineObjectCommand(new DocumentPosition(block, 0), 300, 300));
        document.Undo.Undo();

        var shape = ParagraphAt(document, block).Runs
            .Select(x => x.Inline)
            .OfType<InlineShape>()
            .ShouldHaveSingleItem();

        shape.Width.ShouldBe(100, 1);
        shape.Height.ShouldBe(100, 1);
    }

    // ---- Word: tables ----

    [Fact]
    public async Task InsertingATableAddsATableBlock()
    {
        var (document, controller) = await WordAsync();
        using var _ = document;

        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.InsertTable(3, 4);

        // Immediately after the block the caret was in — the fixture has a table of its own further
        // down, so "the only table" and "the last table" would both find the wrong one.
        var table = document.Blocks[block + 1].ShouldBeOfType<DocumentTable>();

        table.Rows.Count.ShouldBe(3);
        table.Rows[0].Cells.Count.ShouldBe(4);
    }

    [Fact]
    public async Task AnInsertedTableSurvivesASaveAndReopen()
    {
        var (document, controller) = await WordAsync();

        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.InsertTable(2, 3);

        var saved = document.ToArray();
        document.Dispose();

        using var reopened = await WordDocument.OpenAsync(new MemoryStream(saved, writable: false));

        var table = reopened.Blocks[block + 1].ShouldBeOfType<DocumentTable>();

        table.Rows.Count.ShouldBe(2);
        table.Rows[0].Cells.Count.ShouldBe(3);
    }

    [Fact]
    public async Task AnInsertedTableUndoesAwayWithItsTrailingParagraph()
    {
        var (document, controller) = await WordAsync();
        using var _ = document;

        var blocksBefore = document.Blocks.Count;
        var tablesBefore = document.Blocks.OfType<DocumentTable>().Count();

        controller.Selection.MoveTo(new DocumentPosition(BodyBlock(document), 0));
        controller.InsertTable(2, 2);
        controller.Undo();

        document.Blocks.Count.ShouldBe(blocksBefore);
        document.Blocks.OfType<DocumentTable>().Count().ShouldBe(tablesBefore);
    }

    // ---- Word: highlight ----

    [Fact]
    public async Task HighlightingWritesANamedWordHighlight()
    {
        var (document, controller) = await WordAsync();
        using var _ = document;

        var block = BodyBlock(document);
        controller.Selection.Select(new DocumentPosition(block, 0), new DocumentPosition(block, 5));
        controller.SetHighlight(new ArgbColor(255, 255, 255, 0));

        var element = document.Blocks[block].ShouldBeOfType<DocumentParagraph>();

        element.Runs.ShouldContain(x => x.Style.Highlight == new ArgbColor(255, 255, 255, 0));
    }

    [Fact]
    public async Task ClearingAHighlightRemovesIt()
    {
        var (document, controller) = await WordAsync();
        using var _ = document;

        var block = BodyBlock(document);
        controller.Selection.Select(new DocumentPosition(block, 0), new DocumentPosition(block, 5));
        controller.SetHighlight(new ArgbColor(255, 255, 255, 0));
        controller.SetHighlight(null);

        ParagraphAt(document, block).Runs.ShouldAllBe(x => x.Style.Highlight == null);
    }

    [Fact]
    public async Task AHighlightSurvivesASaveAndReopen()
    {
        var (document, controller) = await WordAsync();

        var block = BodyBlock(document);
        controller.Selection.Select(new DocumentPosition(block, 0), new DocumentPosition(block, 5));
        controller.SetHighlight(new ArgbColor(255, 0, 255, 255));

        var saved = document.ToArray();
        document.Dispose();

        using var reopened = await WordDocument.OpenAsync(new MemoryStream(saved, writable: false));

        reopened.Blocks
            .OfType<DocumentParagraph>()
            .SelectMany(x => x.Runs)
            .ShouldContain(x => x.Style.Highlight == new ArgbColor(255, 0, 255, 255));
    }

    [Theory]
    [InlineData(255, 255, 0, "yellow")]
    [InlineData(0, 255, 255, "cyan")]
    [InlineData(250, 250, 10, "yellow")]     // near-yellow snaps to the nameable one
    [InlineData(0, 0, 139, "darkBlue")]
    public void HighlightNamesResolveToTheNearestWordValue(byte r, byte g, byte b, string expected)
        => HighlightPalette.NameOf(new ArgbColor(255, r, g, b)).ShouldBe(expected);

    [Fact]
    public void ClearingAHighlightIsNamedNone()
        => HighlightPalette.NameOf(null).ShouldBe("none");

    [Fact]
    public void EveryPaletteSwatchRoundTripsThroughItsName()
    {
        foreach (var swatch in HighlightPalette.Swatches)
        {
            HighlightPalette.NameOf(swatch.Color).ShouldBe(swatch.Name);
            HighlightPalette.ColorOf(swatch.Name).ShouldBe(swatch.Color);
        }
    }

    // ---- PowerPoint ----

    [Fact]
    public async Task AddingAShapeToASlidePutsItInTheTree()
    {
        using var deck = await DeckAsync();
        var controller = SlideController(deck);

        var before = deck.Slides[1].Shapes.Count;
        controller.AddShape(ShapeGeometry.Hexagon, 100, 100, 200, 150);

        deck.Slides[1].Shapes.Count.ShouldBe(before + 1);
        controller.Selection!.Geometry.ShouldBe(ShapeGeometry.Hexagon);
    }

    [Fact]
    public async Task AShapeAddedToASlideSurvivesASaveAndReopen()
    {
        using var deck = await DeckAsync();
        var controller = SlideController(deck);
        controller.AddShape(ShapeGeometry.Cloud, 50, 50, 300, 200);

        var saved = deck.ToArray();

        using var reopened = await SlideDeck.OpenAsync(new MemoryStream(saved, writable: false));

        reopened.Slides[1].Shapes.ShouldContain(x => x.Geometry == ShapeGeometry.Cloud && x.IsEditable);
    }

    [Fact]
    public async Task AddingAPictureToASlideReferencesAnImagePart()
    {
        using var deck = await DeckAsync();
        var controller = SlideController(deck);

        controller.AddPicture(Png(), "image/png", 20, 20, 120, 90);

        var saved = deck.ToArray();

        using var reopened = await SlideDeck.OpenAsync(new MemoryStream(saved, writable: false));

        reopened.Slides[1].Shapes.ShouldContain(x => x.Image != null && x.Image.Length > 0);
    }

    [Fact]
    public async Task AddingATableToASlideProducesTheRightGrid()
    {
        using var deck = await DeckAsync();
        var controller = SlideController(deck);

        controller.AddTable(3, 4, 40, 40, 400, 200);

        var table = deck.Slides[1].Shapes.Select(x => x.Table).OfType<SlideTable>().ShouldHaveSingleItem();

        table.Rows.Count.ShouldBe(3);
        table.Rows[0].Count.ShouldBe(4);
        table.ColumnWidths.Count.ShouldBe(4);
    }

    [Fact]
    public async Task AddingAShapeToASlideUndoesAway()
    {
        using var deck = await DeckAsync();
        var controller = SlideController(deck);

        var before = deck.Slides[1].Shapes.Count;
        controller.AddShape(ShapeGeometry.Diamond, 10, 10);
        controller.Undo();

        deck.Slides[1].Shapes.Count.ShouldBe(before);
    }

    [Fact]
    public async Task HighlightingSlideTextWritesAndReadsBack()
    {
        using var deck = await DeckAsync();
        var controller = SlideController(deck);

        var shape = deck.Slides[1].Shapes.ToList().FindIndex(x => x.Text?.PlainText.Contains("Top level point") == true);
        controller.Select(shape);
        controller.BeginTextEditing(0, 0);
        controller.SelectAll();
        controller.SetHighlight(new ArgbColor(255, 255, 255, 0));

        deck.Slides[1].Shapes[shape].Text!.Paragraphs
            .SelectMany(x => x.Runs)
            .ShouldContain(x => x.Style.Highlight == new ArgbColor(255, 255, 255, 0));
    }

    [Fact]
    public async Task ASlideHighlightSurvivesASaveAndReopen()
    {
        using var deck = await DeckAsync();
        var controller = SlideController(deck);

        var shape = deck.Slides[1].Shapes.ToList().FindIndex(x => x.Text?.PlainText.Contains("Top level point") == true);
        controller.Select(shape);
        controller.BeginTextEditing(0, 0);
        controller.SelectAll();
        controller.SetHighlight(new ArgbColor(255, 0, 255, 0));

        var saved = deck.ToArray();

        using var reopened = await SlideDeck.OpenAsync(new MemoryStream(saved, writable: false));

        reopened.Slides[1].Shapes
            .Where(x => x.Text is not null)
            .SelectMany(x => x.Text!.Paragraphs)
            .SelectMany(x => x.Runs)
            .ShouldContain(x => x.Style.Highlight == new ArgbColor(255, 0, 255, 0));
    }

    [Fact]
    public async Task StrikethroughSurvivesASaveAndReopenOnASlide()
    {
        using var deck = await DeckAsync();
        var controller = SlideController(deck);

        var shape = deck.Slides[1].Shapes.ToList().FindIndex(x => x.Text?.PlainText.Contains("Top level point") == true);
        controller.Select(shape);
        controller.BeginTextEditing(0, 0);
        controller.SelectAll();
        controller.ToggleStrikethrough();

        var saved = deck.ToArray();

        using var reopened = await SlideDeck.OpenAsync(new MemoryStream(saved, writable: false));

        reopened.Slides[1].Shapes
            .Where(x => x.Text is not null)
            .SelectMany(x => x.Text!.Paragraphs)
            .SelectMany(x => x.Runs)
            .ShouldContain(x => x.Style.Strike);
    }
}
