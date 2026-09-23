using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Skia;
using Shiny.Controls.Office.Spreadsheet;
using Shouldly;
using Xunit;
using TextAlignment = Shiny.Controls.Office.Text.TextAlignment;

namespace Shiny.Controls.Office.Tests;

public class DocumentEditingTests
{
    static async Task<WordDocument> OpenAsync()
        => await WordDocument.OpenAsync(new MemoryStream(DocumentFixture.Build()), editable: true);

    static string TextOf(WordDocument document, int block)
        => ((DocumentParagraph)document.Blocks[block]).PlainText;

    /// <summary>Index of the first plain body paragraph in the fixture.</summary>
    static int BodyBlock(WordDocument document)
        => document.Blocks.ToList().FindIndex(x => x is DocumentParagraph p && p.PlainText.StartsWith("Plain body text"));

    [Fact]
    public async Task ReadOnlyDocumentsRefuseEdits()
    {
        using var document = await WordDocument.OpenAsync(new MemoryStream(DocumentFixture.Build()));

        Should.Throw<InvalidOperationException>(
            () => document.Execute(new InsertTextCommand(DocumentPosition.Start, "x")));
    }

    [Fact]
    public async Task InsertingTextLandsAtTheOffset()
    {
        using var document = await OpenAsync();
        var block = BodyBlock(document);
        var before = TextOf(document, block);

        document.Execute(new InsertTextCommand(new DocumentPosition(block, 5), "XYZ"));

        TextOf(document, block).ShouldBe(before[..5] + "XYZ" + before[5..]);
    }

    [Fact]
    public async Task InsertingUndoesCleanly()
    {
        using var document = await OpenAsync();
        var block = BodyBlock(document);
        var before = TextOf(document, block);

        document.Execute(new InsertTextCommand(new DocumentPosition(block, 0), "Hello "));
        document.Undo.Undo();

        TextOf(document, block).ShouldBe(before);
    }

    [Fact]
    public async Task DeletingWithinAParagraph()
    {
        using var document = await OpenAsync();
        var block = BodyBlock(document);
        var before = TextOf(document, block);

        document.Execute(new DeleteRangeCommand(new DocumentRange(
            new DocumentPosition(block, 0),
            new DocumentPosition(block, 6))));

        TextOf(document, block).ShouldBe(before[6..]);

        document.Undo.Undo();
        TextOf(document, block).ShouldBe(before);
    }

    [Fact]
    public async Task SplittingAParagraphKeepsBothHalves()
    {
        using var document = await OpenAsync();
        var block = BodyBlock(document);
        var before = TextOf(document, block);
        var count = document.Blocks.Count;

        document.Execute(new SplitParagraphCommand(new DocumentPosition(block, 5)));

        document.Blocks.Count.ShouldBe(count + 1);
        TextOf(document, block).ShouldBe(before[..5]);
        TextOf(document, block + 1).ShouldBe(before[5..]);
    }

    [Fact]
    public async Task SplittingUndoesBackToOneParagraph()
    {
        using var document = await OpenAsync();
        var block = BodyBlock(document);
        var before = TextOf(document, block);
        var count = document.Blocks.Count;

        document.Execute(new SplitParagraphCommand(new DocumentPosition(block, 5)));
        document.Undo.Undo();

        document.Blocks.Count.ShouldBe(count);
        TextOf(document, block).ShouldBe(before);
    }

    [Fact]
    public async Task SplittingCarriesTheParagraphStyleToBothHalves()
    {
        // Pressing Enter inside a heading must leave two headings, not a heading and a Normal.
        using var document = await OpenAsync();
        var heading = document.Blocks.ToList().FindIndex(x => x is DocumentParagraph { Format.OutlineLevel: > 0 });

        document.Execute(new SplitParagraphCommand(new DocumentPosition(heading, 4)));

        ((DocumentParagraph)document.Blocks[heading]).Format.OutlineLevel.ShouldBe(1);
        ((DocumentParagraph)document.Blocks[heading + 1]).Format.OutlineLevel.ShouldBe(1);
    }

    [Fact]
    public async Task DeletingAcrossParagraphsJoinsThem()
    {
        using var document = await OpenAsync();
        var block = BodyBlock(document);
        var count = document.Blocks.Count;

        var first = TextOf(document, block);
        var second = TextOf(document, block + 1);

        document.Execute(new DeleteRangeCommand(new DocumentRange(
            new DocumentPosition(block, 4),
            new DocumentPosition(block + 1, 3))));

        document.Blocks.Count.ShouldBe(count - 1);
        TextOf(document, block).ShouldBe(first[..4] + second[3..]);
    }

    [Fact]
    public async Task DeletingAcrossParagraphsUndoesBothBack()
    {
        using var document = await OpenAsync();
        var block = BodyBlock(document);
        var count = document.Blocks.Count;
        var first = TextOf(document, block);
        var second = TextOf(document, block + 1);

        document.Execute(new DeleteRangeCommand(new DocumentRange(
            new DocumentPosition(block, 4),
            new DocumentPosition(block + 1, 3))));

        document.Undo.Undo();

        document.Blocks.Count.ShouldBe(count);
        TextOf(document, block).ShouldBe(first);
        TextOf(document, block + 1).ShouldBe(second);
    }

    [Fact]
    public async Task FormattingSplitsRunsAtTheBoundaries()
    {
        using var document = await OpenAsync();
        var block = BodyBlock(document);

        document.Execute(new FormatRunsCommand(
            new DocumentRange(new DocumentPosition(block, 6), new DocumentPosition(block, 10)),
            RunFormatChange.Bold(true)));

        var paragraph = (DocumentParagraph)document.Blocks[block];

        // Exactly the requested span turns bold, and the surrounding text does not.
        var runs = paragraph.Runs.Where(x => !x.IsBreak).ToList();
        runs.Count.ShouldBeGreaterThan(1);
        runs.Any(x => x.Style.Bold).ShouldBeTrue();
        runs.Any(x => !x.Style.Bold).ShouldBeTrue();
    }

    [Fact]
    public async Task FormattingLeavesUntouchedPropertiesAlone()
    {
        // The run already carries a font and size from the style chain. Turning on bold must not
        // flatten them, which is what handing over a whole style rather than a mutation would do.
        using var document = await OpenAsync();
        var block = document.Blocks.ToList().FindIndex(x => x is DocumentParagraph p && p.PlainText.StartsWith("Text using a style"));

        var before = ((DocumentParagraph)document.Blocks[block]).Runs[0].Style;

        document.Execute(new FormatRunsCommand(
            new DocumentRange(new DocumentPosition(block, 0), new DocumentPosition(block, 4)),
            RunFormatChange.Bold(true)));

        var after = ((DocumentParagraph)document.Blocks[block]).Runs[0].Style;

        after.Bold.ShouldBeTrue();
        after.FontFamily.ShouldBe(before.FontFamily);
        after.Italic.ShouldBe(before.Italic);
        after.Color.ShouldBe(before.Color);
    }

    [Fact]
    public async Task FormattingUndoesToTheOriginalRuns()
    {
        using var document = await OpenAsync();
        var block = BodyBlock(document);
        var before = TextOf(document, block);

        document.Execute(new FormatRunsCommand(
            new DocumentRange(new DocumentPosition(block, 6), new DocumentPosition(block, 10)),
            RunFormatChange.Bold(true)));

        document.Undo.Undo();

        var paragraph = (DocumentParagraph)document.Blocks[block];
        paragraph.PlainText.ShouldBe(before);
        paragraph.Runs.Any(x => x.Style.Bold).ShouldBeFalse();
    }

    [Fact]
    public async Task ParagraphAlignmentApplies()
    {
        using var document = await OpenAsync();
        var block = BodyBlock(document);

        document.Execute(new FormatParagraphsCommand(
            new DocumentRange(new DocumentPosition(block, 0), new DocumentPosition(block, 0)),
            ParagraphFormatChange.Alignment(TextAlignment.Center)));

        ((DocumentParagraph)document.Blocks[block]).Format.Alignment.ShouldBe(TextAlignment.Center);
    }

    [Fact]
    public async Task FontSizeIsWrittenInHalfPoints()
    {
        using var document = await OpenAsync();
        var block = BodyBlock(document);

        document.Execute(new FormatRunsCommand(
            new DocumentRange(new DocumentPosition(block, 0), new DocumentPosition(block, 5)),
            RunFormatChange.FontSize(18)));

        var style = ((DocumentParagraph)document.Blocks[block]).Runs[0].Style;
        style.FontSize.ShouldBe(OoxmlUnits.PointsToPixels(18), 0.01);
    }

    [Fact]
    public async Task TypedTrailingSpacesSurviveARoundTrip()
    {
        // Without xml:space="preserve" Word drops them on load, so text typed with a trailing space
        // silently loses it the next time the file is opened.
        using var document = await OpenAsync();
        var block = BodyBlock(document);
        var length = TextOf(document, block).Length;

        document.Execute(new InsertTextCommand(new DocumentPosition(block, length), "  tail  "));

        var saved = document.ToArray();
        using var reopened = await WordDocument.OpenAsync(new MemoryStream(saved));

        TextOf(reopened, block).ShouldEndWith("  tail  ");
    }

    [Fact]
    public async Task AnUneditedDocumentStillSavesByteIdentical()
    {
        // Opening editable must not be enough to rewrite the file.
        var original = DocumentFixture.Build();
        using var document = await WordDocument.OpenAsync(new MemoryStream(original), editable: true);

        PackageComparer.Compare(original, document.ToArray()).IsIdentical.ShouldBeTrue();
    }

    [Fact]
    public async Task EditingOneParagraphLeavesUnrelatedPartsAlone()
    {
        var original = DocumentFixture.Build();
        using var document = await WordDocument.OpenAsync(new MemoryStream(original), editable: true);

        document.Execute(new InsertTextCommand(new DocumentPosition(BodyBlock(document), 0), "Edited "));

        var diff = PackageComparer.Compare(original, document.ToArray());
        diff.Removed.ShouldBeEmpty();

        foreach (var part in diff.Changed)
            part.ShouldBe("word/document.xml", $"'{part}' was rewritten by a text edit. {diff}");
    }

    [Fact]
    public async Task RedoReappliesAnEdit()
    {
        using var document = await OpenAsync();
        var block = BodyBlock(document);
        var before = TextOf(document, block);

        document.Execute(new InsertTextCommand(new DocumentPosition(block, 0), "Hi "));
        document.Undo.Undo();
        TextOf(document, block).ShouldBe(before);

        document.Undo.Redo();
        TextOf(document, block).ShouldBe("Hi " + before);
    }
}

public class DocumentEditorControllerTests
{
    sealed class Fixed : Shiny.Controls.Office.Text.ITextMeasurer
    {
        public const double CharWidth = 8;

        public Shiny.Controls.Office.Text.TextMetrics Measure(ReadOnlySpan<char> text, Shiny.Controls.Office.Text.TextStyle style)
            => new(text.Length * CharWidth, style.FontSize * 0.8, style.FontSize * 0.2);

        public Shiny.Controls.Office.Text.TextMetrics LineMetrics(Shiny.Controls.Office.Text.TextStyle style)
            => new(0, style.FontSize * 0.8, style.FontSize * 0.2);
    }

    static async Task<(WordDocument Document, DocumentEditorController Controller)> SetupAsync()
    {
        var document = await WordDocument.OpenAsync(new MemoryStream(DocumentFixture.Build()), editable: true);
        var controller = new DocumentEditorController(document, new Fixed());
        controller.Resize(800, 400);
        return (document, controller);
    }

    static int BodyBlock(WordDocument document)
        => document.Blocks.ToList().FindIndex(x => x is DocumentParagraph p && p.PlainText.StartsWith("Plain body text"));

    // ---- selecting by word and paragraph ----
    //
    // Every formatting command needs a range to act on, so the gesture that makes one is part of the
    // formatting feature rather than a nicety on top of it: without a way to select a word by pointing
    // at it, the only route to bolding one is a careful drag from edge to edge.

    static string SelectedText(WordDocument document, DocumentEditorController controller)
    {
        var range = controller.Selection.Range;
        if (range.Start.Block != range.End.Block)
            return "<multiple blocks>";

        var text = ((DocumentParagraph)document.Blocks[range.Start.Block]).PlainText;
        return text[range.Start.Offset..range.End.Offset];
    }

    [Fact]
    public async Task DoubleClickingAWordSelectsExactlyThatWord()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        var text = ((DocumentParagraph)document.Blocks[block]).PlainText;
        var at = text.IndexOf("body", StringComparison.Ordinal);

        // Landing anywhere inside the word has to give the same span, so the middle is what is tested.
        controller.SelectWordAt(new DocumentPosition(block, at + 2));

        SelectedText(document, controller).ShouldBe("body");
    }

    [Fact]
    public async Task AWordSelectionStopsAtPunctuation()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        var paragraph = (DocumentParagraph)document.Blocks[block];
        var text = paragraph.PlainText;

        // A full stop is not a word character, so double-clicking the last word must not swallow it.
        var stop = text.IndexOf('.');
        if (stop <= 0)
            return;

        controller.SelectWordAt(new DocumentPosition(block, stop - 1));
        SelectedText(document, controller).ShouldNotContain(".");
    }

    [Fact]
    public async Task ClickingBetweenWordsSelectsTheGapRatherThanNothing()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        var text = ((DocumentParagraph)document.Blocks[block]).PlainText;
        var space = text.IndexOf(' ');

        controller.SelectWordAt(new DocumentPosition(block, space));

        // Whitespace is a span of its own: selecting nothing at all would look like the gesture failed.
        SelectedText(document, controller).ShouldBe(" ");
    }

    [Fact]
    public async Task TripleClickingSelectsTheWholeParagraph()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        var text = ((DocumentParagraph)document.Blocks[block]).PlainText;

        controller.SelectParagraphAt(new DocumentPosition(block, 4));

        controller.Selection.Range.Start.ShouldBe(new DocumentPosition(block, 0));
        controller.Selection.Range.End.ShouldBe(new DocumentPosition(block, text.Length));
    }

    [Fact]
    public async Task SelectingAWordThenFormattingAppliesToThatWordOnly()
    {
        // The whole point of the gesture, end to end.
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        var text = ((DocumentParagraph)document.Blocks[block]).PlainText;
        var at = text.IndexOf("body", StringComparison.Ordinal);

        controller.SelectWordAt(new DocumentPosition(block, at + 2));
        controller.ToggleBold();

        StyleAt(document, block, at).Bold.ShouldBeTrue();
        StyleAt(document, block, at + 3).Bold.ShouldBeTrue();

        // The space before it is outside the word, and outside the change.
        StyleAt(document, block, at - 1).Bold.ShouldBeFalse();
    }

    [Fact]
    public async Task SelectingAWordInAnEmptyParagraphIsHarmless()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var empty = document.Blocks
            .Select((block, index) => (block, index))
            .FirstOrDefault(x => x.block is DocumentParagraph { PlainText.Length: 0 });

        if (empty.block is null)
            return;

        Should.NotThrow(() => controller.SelectWordAt(new DocumentPosition(empty.index, 0)));
        controller.Selection.Range.Start.ShouldBe(controller.Selection.Range.End);
    }

    // ---- formatting chosen at a bare caret ----
    //
    // Word applies a format picked with nothing selected to whatever is typed next. Without that, the
    // toolbar has no range to act on and the click silently does nothing, which is indistinguishable
    // from a broken toolbar.

    /// <summary>The style of the run covering an offset, which is what a format has to land on.</summary>
    static Shiny.Controls.Office.Text.TextStyle StyleAt(WordDocument document, int block, int offset)
    {
        var paragraph = (DocumentParagraph)document.Blocks[block];
        var cursor = 0;

        foreach (var run in paragraph.Runs)
        {
            if (run.IsBreak)
                continue;

            if (offset < cursor + run.Text.Length)
                return run.Style;

            cursor += run.Text.Length;
        }

        return paragraph.Runs.Count > 0 ? paragraph.Runs[^1].Style : Shiny.Controls.Office.Text.TextStyle.Default;
    }

    [Fact]
    public async Task AFormatChosenAtABareCaretShowsOnTheToolbarBeforeAnythingIsTyped()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        controller.Selection.MoveTo(new DocumentPosition(BodyBlock(document), 3));
        controller.SetFontFamily("Courier New");

        // Nothing to read it back from in the document yet, so the toolbar has to be told directly -
        // and it has to survive the Changed that the choice itself raises.
        controller.CaretFormat.FontFamily.ShouldBe("Courier New");
    }

    [Fact]
    public async Task AFormatChosenAtABareCaretAppliesToTheNextTypedText()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.SetFontFamily("Courier New");
        controller.SetFontSize(24);
        controller.SetTextColor(new ArgbColor(255, 0xC0, 0x00, 0x00));

        controller.InsertText("XY");

        var style = StyleAt(document, block, 0);
        style.FontFamily.ShouldBe("Courier New");
        style.Color.ShouldBe(new ArgbColor(255, 0xC0, 0x00, 0x00));

        // The text around it is untouched - a pending format applies to what was typed, not the run
        // it was typed into.
        StyleAt(document, block, 5).FontFamily.ShouldNotBe("Courier New");
    }

    [Fact]
    public async Task APendingFormatIsSpentOnce()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.SetFontFamily("Courier New");
        controller.InsertText("A");

        // Moving on and typing elsewhere must not pick the format up again.
        controller.Selection.MoveTo(new DocumentPosition(block, 6));
        controller.InsertText("B");

        StyleAt(document, block, 6).FontFamily.ShouldNotBe("Courier New");
    }

    [Fact]
    public async Task MovingTheCaretAbandonsAPendingFormat()
    {
        // Otherwise it is a trap: pick a colour, change your mind, click elsewhere, and the next thing
        // typed - somewhere unrelated, possibly much later - comes out in that colour.
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.SetFontFamily("Courier New");

        controller.Selection.MoveTo(new DocumentPosition(block, 6));
        controller.CaretFormat.FontFamily.ShouldNotBe("Courier New");

        controller.InsertText("Z");
        StyleAt(document, block, 6).FontFamily.ShouldNotBe("Courier New");
    }

    [Fact]
    public async Task ChoosingTwoSizesAtOneCaretKeepsTheSecond()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.SetFontSize(12);
        controller.SetFontSize(30);

        controller.CaretFormat.FontSize.ShouldBe(30);

        controller.InsertText("Q");

        // Styles carry pixels; the picker speaks points, which is the round trip that has to survive.
        Shiny.Controls.Office.OoxmlUnits
            .PixelsToPointsApprox(StyleAt(document, block, 0).FontSize)
            .ShouldBe(30);
    }

    [Fact]
    public async Task TypingWithAPendingFormatUndoesAsOneStep()
    {
        // The insert and the format are one action to the user. An undo that took the characters away
        // and left the formatting behind would leave the caret carrying a format nobody chose.
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        var before = ((DocumentParagraph)document.Blocks[block]).PlainText;

        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.SetFontFamily("Courier New");
        controller.InsertText("XY");

        controller.Undo();

        ((DocumentParagraph)document.Blocks[block]).PlainText.ShouldBe(before);
        StyleAt(document, block, 0).FontFamily.ShouldNotBe("Courier New");
    }

    [Fact]
    public async Task FormattingWithASelectionStillAppliesImmediately()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.Selection.ExtendTo(new DocumentPosition(block, 5));
        controller.SetFontFamily("Courier New");

        StyleAt(document, block, 0).FontFamily.ShouldBe("Courier New");
    }

    [Fact]
    public async Task TypingInsertsAndAdvancesTheCaret()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.InsertText("abc");

        ((DocumentParagraph)document.Blocks[block]).PlainText.ShouldStartWith("abc");
        controller.Selection.Focus.ShouldBe(new DocumentPosition(block, 3));
    }

    [Fact]
    public async Task TypingOverASelectionReplacesIt()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        var before = ((DocumentParagraph)document.Blocks[block]).PlainText;

        controller.Selection.Select(new DocumentPosition(block, 0), new DocumentPosition(block, 5));
        controller.InsertText("X");

        ((DocumentParagraph)document.Blocks[block]).PlainText.ShouldBe("X" + before[5..]);
    }

    [Fact]
    public async Task BackspaceAtTheStartOfAParagraphJoinsItToTheOneAbove()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        var previous = ((DocumentParagraph)document.Blocks[block - 1]).PlainText;
        var current = ((DocumentParagraph)document.Blocks[block]).PlainText;
        var count = document.Blocks.Count;

        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.DeleteBackward();

        document.Blocks.Count.ShouldBe(count - 1);
        ((DocumentParagraph)document.Blocks[block - 1]).PlainText.ShouldBe(previous + current);
        controller.Selection.Focus.ShouldBe(new DocumentPosition(block - 1, previous.Length));
    }

    [Fact]
    public async Task BackspaceAtTheVeryStartOfTheDocumentDoesNothing()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var count = document.Blocks.Count;
        controller.Selection.MoveTo(DocumentPosition.Start);
        controller.DeleteBackward();

        document.Blocks.Count.ShouldBe(count);
    }

    [Fact]
    public async Task EnterSplitsAndPutsTheCaretOnTheNewParagraph()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 5));
        controller.InsertParagraph();

        controller.Selection.Focus.ShouldBe(new DocumentPosition(block + 1, 0));
    }

    [Fact]
    public async Task ArrowKeysCrossParagraphBoundaries()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.Move(CaretMove.Left);

        controller.Selection.Focus.Block.ShouldBe(block - 1);
        controller.Selection.Focus.Offset.ShouldBe(((DocumentParagraph)document.Blocks[block - 1]).PlainText.Length);
    }

    [Fact]
    public async Task ShiftArrowExtendsFromTheAnchor()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 2));
        controller.Move(CaretMove.Right, extend: true);
        controller.Move(CaretMove.Right, extend: true);

        controller.Selection.Anchor.ShouldBe(new DocumentPosition(block, 2));
        controller.Selection.Range.End.ShouldBe(new DocumentPosition(block, 4));
    }

    [Fact]
    public async Task WordMovementSkipsWholeWords()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        var text = ((DocumentParagraph)document.Blocks[block]).PlainText;

        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.Move(CaretMove.WordRight);

        controller.Selection.Focus.Offset.ShouldBe(text.IndexOf(' '));
    }

    [Fact]
    public async Task ClickingMapsAPointBackToAPosition()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        var target = new DocumentPosition(block, 4);

        // Round-trip: the caret rect for a position must hit-test back to that position.
        var rect = controller.CaretRect(target);
        var hit = controller.PositionAt(
            rect.X + controller.PageX + controller.PagePadding + 1,
            rect.Y - controller.Viewport.ScrollY + rect.Height / 2);

        hit.ShouldNotBeNull();
        hit!.Value.Block.ShouldBe(block);
        // One character of tolerance: the hit lands on whichever boundary the point is nearer.
        Math.Abs(hit.Value.Offset - 4).ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task SelectionProducesRectanglesToPaint()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        controller.Selection.Select(new DocumentPosition(block, 0), new DocumentPosition(block, 8));

        var rects = controller.SelectionRects().ToList();
        rects.ShouldNotBeEmpty();
        rects[0].Width.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task AnEmptySelectionPaintsNothing()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        controller.SelectionRects().ShouldBeEmpty();
    }

    [Fact]
    public async Task CaretFormatReflectsWhatIsUnderTheCaret()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var formatted = document.Blocks.ToList().FindIndex(x => x is DocumentParagraph p && p.PlainText.StartsWith("Bold "));

        // Inside the bold run.
        controller.Selection.MoveTo(new DocumentPosition(formatted, 3));
        controller.CaretFormat.Bold.ShouldBeTrue();

        // Inside the italic run that follows it.
        controller.Selection.MoveTo(new DocumentPosition(formatted, 8));
        controller.CaretFormat.Italic.ShouldBeTrue();
    }

    [Fact]
    public async Task BoldOnASelectionEditsTheDocument()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        controller.Selection.Select(new DocumentPosition(block, 0), new DocumentPosition(block, 5));
        controller.ToggleBold();

        ((DocumentParagraph)document.Blocks[block]).Runs[0].Style.Bold.ShouldBeTrue();
    }

    [Fact]
    public async Task BoldWithNoSelectionOnlyUpdatesTheToolbarState()
    {
        // Word applies it to what is typed next. Carrying that through the XML needs a pending-format
        // concept the editor does not have, so the document must be left alone rather than half-changed.
        var (document, controller) = await SetupAsync();
        using var _ = document;

        // Offset 5 is the end of "Plain" - a word's edge, where there is no word to mean. Inside a word
        // the change applies to the word instead; see BoldWithACaretInsideAWordBoldsTheWord.
        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 5));
        controller.ToggleBold();

        controller.CaretFormat.Bold.ShouldBeTrue();
        document.Undo.CanUndo.ShouldBeFalse("nothing should have been written to the document");
    }

    [Fact]
    public async Task BoldWithACaretInsideAWordBoldsTheWord()
    {
        // What Word does, and what people expect: click into a word, press Bold, the word goes bold.
        // Holding it as a pending format instead changed nothing on screen and read as a dead button.
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 3));
        controller.ToggleBold();

        StyleAt(document, block, 0).Bold.ShouldBeTrue();
        StyleAt(document, block, 4).Bold.ShouldBeTrue();
        StyleAt(document, block, 6).Bold.ShouldBeFalse("the next word is outside the change");
        controller.Selection.IsEmpty.ShouldBeTrue("the caret stays a caret");
        controller.CaretFormat.Bold.ShouldBeTrue();
    }

    [Fact]
    public async Task ColourWithACaretInsideAWordColoursTheWord()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var red = new ArgbColor(255, 0xC0, 0x00, 0x00);
        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 8));
        controller.SetTextColor(red);

        StyleAt(document, block, 6).Color.ShouldBe(red);
        StyleAt(document, block, 9).Color.ShouldBe(red);
        StyleAt(document, block, 0).Color.ShouldNotBe(red);
    }

    [Fact]
    public async Task UnboldingAHeadingOverridesItsStyle()
    {
        // The heading is bold through Heading1, not through its own run properties. Removing the run's
        // w:b left the style's bold in force, so the button toggled and the text never changed.
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = document.Blocks.ToList().FindIndex(x => x is DocumentParagraph p && p.PlainText == "Quarterly Report");
        StyleAt(document, block, 0).Bold.ShouldBeTrue("precondition: the heading style is bold");

        controller.Selection.Select(new DocumentPosition(block, 0), new DocumentPosition(block, 9));
        controller.ToggleBold();

        StyleAt(document, block, 0).Bold.ShouldBeFalse();
        StyleAt(document, block, 8).Bold.ShouldBeFalse();
        StyleAt(document, block, 10).Bold.ShouldBeTrue("outside the selection the style still applies");
    }

    [Fact]
    public async Task UndoThroughTheControllerKeepsTheSelectionValid()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.InsertText("hello");
        controller.Undo();

        controller.Selection.Focus.Block.ShouldBeLessThan(document.Blocks.Count);
        controller.Selection.Focus.Offset.ShouldBeLessThanOrEqualTo(
            ((DocumentParagraph)document.Blocks[controller.Selection.Focus.Block]).PlainText.Length);
    }

    [Fact]
    public async Task SelectAllSpansTheWholeDocument()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        controller.SelectAll();

        controller.Selection.Range.Start.ShouldBe(DocumentPosition.Start);
        controller.Selection.Range.End.Block.ShouldBe(document.Blocks.Count - 1);
    }

    [Fact]
    public async Task TypingReLaysOutSoTheViewSeesTheChange()
    {
        var (document, controller) = await SetupAsync();
        using var _ = document;

        var block = BodyBlock(document);
        var before = controller.Blocks;

        controller.Selection.MoveTo(new DocumentPosition(block, 0));
        controller.InsertText("some new text ");

        controller.Blocks.ShouldNotBeSameAs(before, "an edit must invalidate the cached layout");
    }
}

public class CrossParagraphUndoTests
{
    static int BodyBlock(WordDocument document)
        => document.Blocks.ToList().FindIndex(x => x is DocumentParagraph p && p.PlainText.StartsWith("Plain body text"));

    [Fact]
    public async Task DeletingAcrossParagraphsRedoesAsWellAsUndoes()
    {
        // The trap: the restore snapshot is taken before the edit, when the span was two paragraphs,
        // but the edit collapses it to one. Replacing "two" on undo eats an innocent third paragraph.
        using var document = await WordDocument.OpenAsync(new MemoryStream(DocumentFixture.Build()), editable: true);

        var block = BodyBlock(document);
        var count = document.Blocks.Count;
        var texts = document.Blocks.OfType<DocumentParagraph>().Select(x => x.PlainText).ToList();

        document.Execute(new DeleteRangeCommand(new DocumentRange(
            new DocumentPosition(block, 4),
            new DocumentPosition(block + 1, 3))));

        var merged = ((DocumentParagraph)document.Blocks[block]).PlainText;

        document.Undo.Undo();
        document.Blocks.Count.ShouldBe(count);
        document.Blocks.OfType<DocumentParagraph>().Select(x => x.PlainText).ShouldBe(texts);

        document.Undo.Redo();
        document.Blocks.Count.ShouldBe(count - 1);
        ((DocumentParagraph)document.Blocks[block]).PlainText.ShouldBe(merged);
    }
}
