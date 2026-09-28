using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Spreadsheet.View;
using Shiny.Controls.Office.Text;
using Shouldly;
using Xunit;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// The find feature the three Office toolbars drive: the matcher, and the previous/next/count walk
/// over a document, a deck and a workbook.
/// </summary>
/// <remarks>
/// The behaviour worth guarding is not "does IndexOf work". It is that the three finders agree about
/// what the arrows do — that next wraps rather than going quiet, that the first press lands below the
/// caret rather than back at the top, and that the count in the toolbar is a count of things the
/// arrows can actually reach.
/// </remarks>
public class FindTests
{
    sealed class Fixed : ITextMeasurer
    {
        public TextMetrics Measure(ReadOnlySpan<char> text, TextStyle style)
            => new(text.Length * 8, style.FontSize * 0.8, style.FontSize * 0.2);

        public TextMetrics LineMetrics(TextStyle style)
            => new(0, style.FontSize * 0.8, style.FontSize * 0.2);
    }

    // ---- the matcher ----

    [Fact]
    public void MatchesAreCaseInsensitiveByDefault()
        => TextSearch.Matches("Widget widget WIDGET", "widget").Count().ShouldBe(3);

    [Fact]
    public void MatchCaseNarrowsToTheExactSpelling()
        => TextSearch.Matches("Widget widget WIDGET", "widget", new FindOptions { MatchCase = true })
            .Select(x => x.Start)
            .ShouldBe([7]);

    [Fact]
    public void WholeWordRejectsAMatchInsideALongerWord()
    {
        var options = new FindOptions { WholeWord = true };

        TextSearch.Matches("front on frontier", "on", options)
            .Select(x => x.Start)
            .ShouldBe([6]);
    }

    [Fact]
    public void ARejectedWholeWordHitDoesNotSwallowTheTextAfterIt()
    {
        // Advancing past a rejected hit by the query's length steps over the standalone word that
        // follows it inside the same run of text.
        TextSearch.Matches("cont on", "on", new FindOptions { WholeWord = true })
            .Select(x => x.Start)
            .ShouldBe([5]);
    }

    [Fact]
    public void OverlappingMatchesAreNotReported()
    {
        // "next" is meant to step through the text, not shuffle one character along inside a word.
        TextSearch.Matches("aaaa", "aa").Count().ShouldBe(2);
    }

    [Fact]
    public void AnEmptyQueryMatchesNothing()
        => TextSearch.Matches("anything", string.Empty).ShouldBeEmpty();

    // ---- documents ----

    static async Task<(WordDocument Document, DocumentEditorController Controller)> OpenDocumentAsync()
    {
        var document = await WordDocument.OpenAsync(new MemoryStream(DocumentFixture.Build()), editable: true);
        var controller = new DocumentEditorController(document, new Fixed());
        controller.Resize(800, 4000);
        return (document, controller);
    }

    [Fact]
    public async Task TypingAQuerySelectsTheFirstHitWithoutTouchingAnArrow()
    {
        var (document, controller) = await OpenDocumentAsync();
        using var _ = document;

        controller.Find.Query = "item";

        controller.Find.Count.ShouldBeGreaterThan(0);
        controller.Find.ActiveIndex.ShouldBe(0);

        // Selected rather than merely landed beside: what a person does with a word they have found is
        // act on it.
        controller.Selection.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public async Task TheSelectionCoversExactlyTheMatchedWord()
    {
        var (document, controller) = await OpenDocumentAsync();
        using var _ = document;

        controller.Find.Query = "Nested";

        var match = controller.Find.Active!.Value;
        var text = ((DocumentParagraph)document.Blocks[match.Block]).PlainText;

        text.Substring(match.Start, match.Length).ShouldBe("Nested");
        controller.Selection.Range.Start.ShouldBe(new DocumentPosition(match.Block, match.Start));
        controller.Selection.Range.End.ShouldBe(new DocumentPosition(match.Block, match.End));
    }

    [Fact]
    public async Task TheCountReadsAsOneBasedOfTotal()
    {
        var (document, controller) = await OpenDocumentAsync();
        using var _ = document;

        controller.Find.Query = "item";
        controller.Find.Status.ShouldBe($"1/{controller.Find.Count}");

        controller.Find.FindNext();
        controller.Find.Status.ShouldBe($"2/{controller.Find.Count}");
    }

    [Fact]
    public async Task NextWrapsPastTheEndRatherThanGoingQuiet()
    {
        // A "next" that stops at the last hit looks identical to one that has finished the document:
        // the user has no way to tell whether the words above were searched at all.
        var (document, controller) = await OpenDocumentAsync();
        using var _ = document;

        controller.Find.Query = "item";
        var count = controller.Find.Count;
        count.ShouldBeGreaterThan(1);

        for (var i = 1; i < count; i++)
            controller.Find.FindNext().ShouldBeTrue();

        controller.Find.ActiveIndex.ShouldBe(count - 1);

        controller.Find.FindNext().ShouldBeTrue();
        controller.Find.ActiveIndex.ShouldBe(0);
    }

    [Fact]
    public async Task PreviousWrapsPastTheStart()
    {
        var (document, controller) = await OpenDocumentAsync();
        using var _ = document;

        controller.Find.Query = "item";
        controller.Find.ActiveIndex.ShouldBe(0);

        controller.Find.FindPrevious().ShouldBeTrue();
        controller.Find.ActiveIndex.ShouldBe(controller.Find.Count - 1);
    }

    [Fact]
    public async Task TheFirstHitIsTheOneBelowTheCaretRatherThanTheTopOfTheDocument()
    {
        // A find that always restarted at the beginning takes the user away from what they were
        // reading, which is the whole reason the caret is consulted at all.
        var (document, controller) = await OpenDocumentAsync();
        using var _ = document;

        var last = document.Blocks
            .Select((block, index) => (block, index))
            .Last(x => x.block is DocumentParagraph p && p.PlainText.Contains("item"))
            .index;

        controller.Selection.MoveTo(new DocumentPosition(last, 0));
        controller.Find.Query = "item";

        controller.Find.Active!.Value.Block.ShouldBe(last);
    }

    [Fact]
    public async Task AQueryThatMatchesNothingCountsZeroAndDisablesTheArrows()
    {
        var (document, controller) = await OpenDocumentAsync();
        using var _ = document;

        controller.Find.Query = "zzzznotinhere";

        controller.Find.Count.ShouldBe(0);
        controller.Find.Status.ShouldBe("0/0");
        controller.Find.FindNext().ShouldBeFalse();
        controller.Find.FindPrevious().ShouldBeFalse();
    }

    [Fact]
    public async Task NotSearchingAndFindingNothingReadDifferently()
    {
        var (document, controller) = await OpenDocumentAsync();
        using var _ = document;

        controller.Find.Status.ShouldBe(string.Empty);

        controller.Find.Query = "zzzznotinhere";
        controller.Find.Status.ShouldBe("0/0");
    }

    [Fact]
    public async Task EditingTheDocumentRecountsTheMatches()
    {
        var (document, controller) = await OpenDocumentAsync();
        using var _ = document;

        controller.Find.Query = "item";
        var before = controller.Find.Count;

        // Typing another one in has to show up in the count, or the readout is describing a document
        // that no longer exists.
        controller.Selection.MoveTo(new DocumentPosition(1, 0));
        controller.InsertText("item ");

        controller.Find.Count.ShouldBe(before + 1);
    }

    [Fact]
    public async Task AnEditDoesNotDragTheViewOntoAMatch()
    {
        // Invalidating the match list is not the same as re-running the search: jumping the user
        // somewhere because the paragraph they are typing in gained a hit is the last thing a find
        // should do.
        var (document, controller) = await OpenDocumentAsync();
        using var _ = document;

        controller.Find.Query = "item";
        controller.Selection.MoveTo(new DocumentPosition(1, 0));
        controller.InsertText("x");

        controller.Selection.Range.Start.Block.ShouldBe(1);
    }

    [Fact]
    public async Task ClearingDropsTheSearchAndLeavesTheSelectionWhereItWas()
    {
        var (document, controller) = await OpenDocumentAsync();
        using var _ = document;

        controller.Find.Query = "Nested";
        var landed = controller.Selection.Range;

        controller.Find.Clear();

        controller.Find.IsSearching.ShouldBeFalse();
        controller.Find.Count.ShouldBe(0);
        controller.Selection.Range.ShouldBe(landed);
    }

    [Fact]
    public async Task TableCellsAreSearchedAndTheCaretLandsInTheCell()
    {
        // The fixture's table holds "North". Positions index the story - every paragraph in reading
        // order, cells included - so a hit inside a table is one the arrows can step to.
        var (document, controller) = await OpenDocumentAsync();
        using var _ = document;

        document.Blocks.OfType<DocumentTable>().ShouldNotBeEmpty();

        controller.Find.Query = "North";
        controller.Find.Count.ShouldBe(1);

        var match = controller.Find.Matches[0];
        document.CellOf(match.Block).ShouldNotBeNull();
        document.Paragraphs[match.Block].PlainText.ShouldBe("North");
        controller.Selection.Range.ShouldBe(match.Range);
    }

    // ---- decks ----

    static async Task<(SlideDeck Deck, SlideEditorController Controller)> OpenDeckAsync()
    {
        using var source = new MemoryStream(SlideFixture.Build(), writable: false);
        var deck = await SlideDeck.OpenAsync(source, editable: true);
        var controller = new SlideEditorController(deck, new Fixed());
        controller.Resize(960, 540);
        return (deck, controller);
    }

    [Fact]
    public async Task FindingInADeckOpensTheSlideTheHitIsOn()
    {
        var (deck, controller) = await OpenDeckAsync();
        using var _ = deck;

        controller.Index = 0;
        controller.Find.Query = "Top level point";

        var match = controller.Find.Active;
        match.ShouldNotBeNull();

        // Opening the slide is the first half of showing the hit; selecting the shape and the text is
        // the other half, and without either the arrows look like they did nothing.
        controller.Index.ShouldBe(match!.Value.Slide);
        controller.SelectedShape.ShouldBe(match.Value.Shape);
        controller.IsEditingText.ShouldBeTrue();
        controller.TextSelection.Normalized().IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public async Task ADeckSearchLeavesTheGridForTheSlideItLandsOn()
    {
        // A hit found while looking at thumbnails has no caret to move: the slide has to be opened
        // before anything on it can be selected.
        var (deck, controller) = await OpenDeckAsync();
        using var _ = deck;

        controller.Mode = SlideViewMode.Grid;
        controller.Find.Query = "Top level point";

        controller.Mode.ShouldBe(SlideViewMode.Single);
    }

    [Fact]
    public async Task ADeckSearchSpansEverySlide()
    {
        var (deck, controller) = await OpenDeckAsync();
        using var _ = deck;

        controller.Find.Query = "point";

        // "Top level point" and "Nested point" are on the same slide; the search must not have stopped
        // at the one the view happened to be showing.
        controller.Find.Count.ShouldBe(2);
    }

    [Fact]
    public async Task DeckMatchesWrapAtTheEnd()
    {
        var (deck, controller) = await OpenDeckAsync();
        using var _ = deck;

        controller.Find.Query = "point";
        controller.Find.ActiveIndex.ShouldBe(0);

        controller.Find.FindNext();
        controller.Find.ActiveIndex.ShouldBe(1);

        controller.Find.FindNext();
        controller.Find.ActiveIndex.ShouldBe(0);
    }

    // ---- workbooks ----

    static async Task<(Workbook Workbook, SpreadsheetController Controller)> OpenWorkbookAsync()
    {
        var workbook = await Workbook.OpenAsync(new MemoryStream(WorkbookFixture.BuildMultiSheet()));
        var controller = new SpreadsheetController(workbook, workbook["Data"]);
        controller.Resize(600, 400);
        return (workbook, controller);
    }

    [Fact]
    public async Task FindingInASheetMovesTheSelectionToTheCell()
    {
        var (workbook, controller) = await OpenWorkbookAsync();
        using var _ = workbook;

        controller.Find.Query = "Widget";

        controller.Find.Count.ShouldBe(1);
        controller.Selection.Active.ShouldBe(CellRef.Parse("A1"));
    }

    [Fact]
    public async Task ASheetSearchLooksAtFormulasTheWayExcelDoes()
    {
        // "look in: formulas" is Excel's default and the only one under which searching for a function
        // name finds the cells that use it.
        var (workbook, controller) = await OpenWorkbookAsync();
        using var _ = workbook;

        controller.Find.Query = "B1*2";

        controller.Find.Count.ShouldBe(1);
        controller.Selection.Active.ShouldBe(CellRef.Parse("C1"));
    }

    [Fact]
    public async Task ASheetSearchStaysOnTheActiveSheetByDefault()
    {
        var (workbook, controller) = await OpenWorkbookAsync();
        using var _ = workbook;

        // "Data!" appears in a Summary formula as well as being the sheet's own name; only the active
        // sheet should be walked.
        controller.Find.Query = "42";

        controller.Find.Count.ShouldBeGreaterThan(0);
        controller.Find.Matches.ShouldAllBe(x => x.Sheet == "Data");
    }

    [Fact]
    public async Task AWorkbookSearchCrossesSheetsAndSwitchesToTheOneItLandsOn()
    {
        var (workbook, controller) = await OpenWorkbookAsync();
        using var _ = workbook;

        controller.Find.SearchAllSheets = true;
        controller.Find.Query = "Q1 Sales";

        controller.Find.Count.ShouldBe(1);
        controller.Sheet.Name.ShouldBe("Summary");
    }

    [Fact]
    public async Task AWorkbookSearchNeverStepsOntoAHiddenSheet()
    {
        // A hidden sheet is one the workbook has deliberately put away; stepping onto it would show
        // the user something they cannot navigate back from.
        var (workbook, controller) = await OpenWorkbookAsync();
        using var _ = workbook;

        controller.Find.SearchAllSheets = true;
        controller.Find.Query = "1";

        controller.Find.Count.ShouldBeGreaterThan(0);
        controller.Find.Matches.ShouldAllBe(x => x.Sheet != "Scratch");
        controller.VisibleSheets.ShouldNotContain(x => x.Name == "Scratch");
    }

    [Fact]
    public async Task WorkbookMatchesAreInBookOrderRatherThanActiveSheetFirst()
    {
        // Ordering the list around whichever sheet is showing re-orders it every time "next" crosses a
        // sheet boundary, and stepping then resumes from the moved match's new index - which walks two
        // sheets forever and never reaches the third.
        var (workbook, controller) = await OpenWorkbookAsync();
        using var _ = workbook;

        controller.Find.SearchAllSheets = true;
        controller.Find.Query = "1";

        var order = controller.Find.Matches.Select(x => x.Sheet).Distinct().ToList();

        controller.SwitchSheet("Summary");

        controller.Find.Matches.Select(x => x.Sheet).Distinct().ShouldBe(order);
    }

    [Fact]
    public async Task EditingACellRecountsTheMatches()
    {
        var (workbook, controller) = await OpenWorkbookAsync();
        using var _ = workbook;

        controller.Find.Query = "Widget";
        controller.Find.Count.ShouldBe(1);

        controller.SetCellText(CellRef.Parse("A3"), "Widget");

        controller.Find.Count.ShouldBe(2);
    }

    [Fact]
    public async Task OnlyTheShowingSheetsCellsAreHandedToThePainter()
    {
        var (workbook, controller) = await OpenWorkbookAsync();
        using var _ = workbook;

        controller.Find.SearchAllSheets = true;
        controller.Find.Query = "1";

        controller.Find.Matches.Select(x => x.Sheet).Distinct().Count().ShouldBeGreaterThan(1);

        // Matches on another sheet have nothing on screen to draw; the toolbar's count is what says
        // they are there.
        controller.FindMatchCells().Count.ShouldBe(
            controller.Find.Matches.Count(x => x.Sheet == controller.Sheet.Name));
    }
}
