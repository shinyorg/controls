using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Validation;
using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Text;
using Shouldly;
using Xunit;
using P14 = DocumentFormat.OpenXml.Office2010.PowerPoint;
using Package = DocumentFormat.OpenXml.Packaging.PresentationDocument;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// Adding, duplicating, deleting and reordering slides: the running order, the parts behind it, the
/// undo history across a save, and a file PowerPoint opens without offering to repair it.
/// </summary>
public class SlideStructureTests
{
    sealed class Fixed : ITextMeasurer
    {
        public TextMetrics Measure(ReadOnlySpan<char> text, TextStyle style)
            => new(text.Length * 8, style.FontSize * 0.8, style.FontSize * 0.2);

        public TextMetrics LineMetrics(TextStyle style)
            => new(0, style.FontSize * 0.8, style.FontSize * 0.2);
    }

    static Task<SlideDeck> OpenAsync(byte[]? bytes = null)
        => SlideDeck.OpenAsync(new MemoryStream(bytes ?? SlideFixture.Build(), writable: false), editable: true);

    static SlideEditorController Controller(SlideDeck deck, int slide = 0)
    {
        var controller = new SlideEditorController(deck, new Fixed());
        controller.Resize(960, 540);
        controller.Index = slide;
        return controller;
    }

    static async Task<byte[]> SaveAsync(SlideDeck deck)
    {
        using var saved = new MemoryStream();
        await deck.SaveToAsync(saved);
        return saved.ToArray();
    }

    static string[] Titles(SlideDeck deck) => deck.Slides.Select(x => x.Title ?? "").ToArray();

    /// <summary>Schema errors in a saved deck, less any the fixture already had.</summary>
    static IReadOnlyList<string> NewValidationErrors(byte[] saved)
    {
        static List<string> Errors(byte[] bytes)
        {
            using var document = Package.Open(new MemoryStream(bytes), false);
            return new OpenXmlValidator(FileFormatVersions.Office2019)
                .Validate(document)
                .Select(x => $"{x.Part?.Uri} {x.Path?.XPath}: {x.Description}")
                .ToList();
        }

        var baseline = Errors(SlideFixture.Build()).Select(x => x.Split(' ', 2)[1]).ToHashSet();
        return Errors(saved).Where(x => !baseline.Contains(x.Split(' ', 2)[1])).ToList();
    }

    static IReadOnlyList<string> SlideEntries(byte[] package)
        => PackageComparer.EntryNames(package).Where(x => x.StartsWith("ppt/slides/slide", StringComparison.Ordinal)).ToList();

    // ---- new slide ----

    [Fact]
    public async Task NewSlideGoesAfterTheCurrentOneAndOpensIt()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 0);
        var before = Titles(deck);

        c.NewSlide();

        deck.Slides.Count.ShouldBe(3);
        c.Index.ShouldBe(1);
        Titles(deck).ShouldBe([before[0], "", before[1]]);
        deck.Slides.Select(x => x.Number).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task ANewSlideCarriesTheLayoutsPlaceholdersEmptyWithTheirPrompts()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);

        c.NewSlide();

        var editable = deck.Slides[2].Shapes.Where(x => x.IsEditable).ToList();
        editable.Count.ShouldBe(2);
        editable.ShouldAllBe(x => x.Text != null && x.Text.PlainText == "");

        // Positioned by the layout, not stacked at the origin.
        editable[1].Y.ShouldBeGreaterThan(editable[0].Y);

        editable[0].Prompt!.PlainText.ShouldBe("Click to add title");
        editable[1].Prompt!.PlainText.ShouldBe("Click to add text");

        // In the placeholder's own formatting: title-sized, and the body prompt keeps its bullet.
        editable[0].Prompt!.Paragraphs[0].Runs[0].Style.FontSize.ShouldBeGreaterThan(
            editable[1].Prompt!.Paragraphs[0].Runs[0].Style.FontSize);
        editable[1].Prompt!.Paragraphs[0].Bullet.ShouldNotBeNull();

        // A prompt is chrome, never content.
        deck.Slides[2].Title.ShouldBeNull();
    }

    [Fact]
    public async Task TypingIntoANewSlidesPlaceholderWorksAndTheSlideSaves()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);

        c.NewSlide();
        var title = deck.Slides[2].Shapes.ToList().FindIndex(x => x.IsEditable);
        c.Select(title);
        c.BeginTextEditing(0, 0);
        c.InsertText("Roadmap");

        deck.Slides[2].Title.ShouldBe("Roadmap");
        deck.Slides[2].Shapes[title].Prompt.ShouldBeNull();

        var saved = await SaveAsync(deck);
        using var reopened = await OpenAsync(saved);
        reopened.Slides.Count.ShouldBe(3);
        reopened.Slides[2].Title.ShouldBe("Roadmap");
        NewValidationErrors(saved).ShouldBeEmpty();
    }

    [Fact]
    public async Task UndoingANewSlideTakesItOutOfTheSavedFileAndRedoPutsTheSameSlideBack()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 0);

        c.NewSlide();
        c.Undo();

        deck.Slides.Count.ShouldBe(2);
        var saved = await SaveAsync(deck);
        SlideEntries(saved).Count.ShouldBe(2);

        c.Redo();
        deck.Slides.Count.ShouldBe(3);
        c.Index.ShouldBe(1);
        SlideEntries(await SaveAsync(deck)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task NewSlideIntoAnEmptyDeckUsesTheMastersLayout()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 0);

        c.DeleteSlide();
        c.DeleteSlide();
        deck.Slides.Count.ShouldBe(0);
        c.Current.ShouldBeNull();

        c.NewSlide();

        deck.Slides.Count.ShouldBe(1);
        deck.Slides[0].Shapes.Count(x => x.IsEditable).ShouldBe(2);
        NewValidationErrors(await SaveAsync(deck)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ANewSlidesIdIsNeverOneTheUndoHistoryStillHolds()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);

        // Delete the slide holding the highest id, add one, then walk everything back: a new slide that
        // had reused 257 would put two 257s in the list the moment the delete is undone.
        c.DeleteSlide(1);
        c.NewSlide();
        c.Undo();
        c.Undo();
        c.Redo();
        c.Redo();

        using var document = Package.Open(new MemoryStream(await SaveAsync(deck)), false);
        var ids = document.PresentationPart!.Presentation.SlideIdList!.Elements<SlideId>().Select(x => x.Id!.Value).ToList();
        ids.Distinct().Count().ShouldBe(ids.Count);
    }

    // ---- delete ----

    [Fact]
    public async Task DeletingASlideRemovesItAndItsNotesFromTheSavedFile()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);
        var first = deck.Slides[0].Title;

        c.DeleteSlide();

        deck.Slides.Count.ShouldBe(1);
        c.Index.ShouldBe(0);

        var saved = await SaveAsync(deck);
        SlideEntries(saved).Count.ShouldBe(1);
        PackageComparer.EntryNames(saved).ShouldNotContain(x => x.StartsWith("ppt/notesSlides/", StringComparison.Ordinal));
        NewValidationErrors(saved).ShouldBeEmpty();

        using var reopened = await OpenAsync(saved);
        reopened.Slides.Single().Title.ShouldBe(first);
    }

    [Fact]
    public async Task UndoingADeleteBringsTheSameSlideBackEvenAfterASave()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);
        var notes = deck.Slides[1].Notes;
        var titles = Titles(deck);

        c.DeleteSlide();
        _ = await SaveAsync(deck);

        c.Undo();

        Titles(deck).ShouldBe(titles);
        deck.Slides[1].Notes.ShouldBe(notes);
        c.Index.ShouldBe(1);

        var saved = await SaveAsync(deck);
        using var reopened = await OpenAsync(saved);
        reopened.Slides[1].Notes.ShouldBe(notes);
        NewValidationErrors(saved).ShouldBeEmpty();
    }

    [Fact]
    public async Task DeletingTheLastSlideLeavesTheEditorOnTheNewLastOne()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);

        c.DeleteSlide();

        c.Index.ShouldBe(0);
        c.Current.ShouldNotBeNull();
    }

    [Fact]
    public async Task AViewerSharingTheDeckSurvivesItsSlideBeingDeleted()
    {
        using var deck = await OpenAsync();
        var viewer = new SlideController(deck) { Index = 1 };
        var editor = Controller(deck, 1);

        editor.DeleteSlide();

        viewer.Index.ShouldBe(0);
        viewer.Current.ShouldBe(deck.Slides[0]);
    }

    [Fact]
    public async Task ReadOnlyRefusesEveryStructuralEdit()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 0);
        c.IsReadOnly = true;

        c.NewSlide();
        c.DuplicateSlide();
        c.DeleteSlide();
        c.MoveSlideLater();

        deck.Slides.Count.ShouldBe(2);
        c.CanUndo.ShouldBeFalse();
    }

    // ---- move ----

    [Fact]
    public async Task MovingASlideReordersTheSavedFileAndFollowsTheSlide()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 0);
        var titles = Titles(deck);

        c.MoveSlideLater();

        Titles(deck).ShouldBe([titles[1], titles[0]]);
        c.Index.ShouldBe(1);
        c.CanMoveSlideLater.ShouldBeFalse();
        c.CanMoveSlideEarlier.ShouldBeTrue();

        using var reopened = await OpenAsync(await SaveAsync(deck));
        Titles(reopened).ShouldBe([titles[1], titles[0]]);

        c.Undo();
        Titles(deck).ShouldBe(titles);
        c.Index.ShouldBe(0);
    }

    [Fact]
    public async Task MovingDropsTheShapeSelectionBecauseItNamedAShapeOnAnotherSlide()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);
        c.Select(deck.Slides[1].Shapes.ToList().FindIndex(x => x.IsEditable));
        c.SelectedShape.ShouldBeGreaterThanOrEqualTo(0);

        c.MoveSlideEarlier();

        c.SelectedShape.ShouldBe(-1);
        c.IsEditingText.ShouldBeFalse();
    }

    // ---- duplicate ----

    [Fact]
    public async Task DuplicatingCopiesTheSlideAndGivesTheCopyItsOwnNotesPage()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);
        var original = deck.Slides[1];

        c.DuplicateSlide();

        deck.Slides.Count.ShouldBe(3);
        c.Index.ShouldBe(2);
        deck.Slides[2].Title.ShouldBe(original.Title);
        deck.Slides[2].Notes.ShouldBe(original.Notes);
        deck.Slides[2].Shapes.Select(x => x.Text?.PlainText).ShouldBe(original.Shapes.Select(x => x.Text?.PlainText));

        var saved = await SaveAsync(deck);
        PackageComparer.EntryNames(saved).Count(x => x.StartsWith("ppt/notesSlides/notesSlide", StringComparison.Ordinal)).ShouldBe(2);
        NewValidationErrors(saved).ShouldBeEmpty();
    }

    [Fact]
    public async Task EditingADuplicateLeavesTheOriginalAlone()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);
        var original = deck.Slides[1].Shapes.Select(x => x.Text?.PlainText).ToList();

        c.DuplicateSlide();
        var shape = deck.Slides[2].Shapes.ToList().FindIndex(x => x.IsEditable && x.Text is not null);
        c.Select(shape);
        c.BeginTextEditing(0, 0);
        c.InsertText("Changed ");

        deck.Slides[1].Shapes.Select(x => x.Text?.PlainText).ShouldBe(original);

        using var reopened = await OpenAsync(await SaveAsync(deck));
        reopened.Slides[1].Shapes.Select(x => x.Text?.PlainText).ShouldBe(original);
    }

    // ---- sections ----

    /// <summary>The fixture with its two slides in two sections.</summary>
    static byte[] WithSections()
    {
        var buffer = new MemoryStream();
        buffer.Write(SlideFixture.Build());
        buffer.Position = 0;

        using (var document = Package.Open(buffer, true, new OpenSettings { AutoSave = false }))
        {
            var presentation = document.PresentationPart!.Presentation;
            var sections = new P14.SectionList(
                new P14.Section(new P14.SectionSlideIdList(new P14.SectionSlideIdListEntry { Id = 256U }))
                    { Name = "Intro", Id = "{11111111-1111-1111-1111-111111111111}" },
                new P14.Section(new P14.SectionSlideIdList(new P14.SectionSlideIdListEntry { Id = 257U }))
                    { Name = "Body", Id = "{22222222-2222-2222-2222-222222222222}" });

            var extension = new PresentationExtension(sections) { Uri = "{521415D9-36F7-43E2-AB2F-B90AF26B5E84}" };
            presentation.PresentationExtensionList = new PresentationExtensionList(extension);
            presentation.Save();
            document.Save();
        }

        return buffer.ToArray();
    }

    static List<List<uint>> Sections(byte[] saved)
    {
        using var document = Package.Open(new MemoryStream(saved), false);
        return document.PresentationPart!.Presentation.Descendants<P14.Section>()
            .Select(s => s.Descendants<P14.SectionSlideIdListEntry>().Select(x => x.Id!.Value).ToList())
            .ToList();
    }

    static List<uint> RunningOrder(byte[] saved)
    {
        using var document = Package.Open(new MemoryStream(saved), false);
        return document.PresentationPart!.Presentation.SlideIdList!.Elements<SlideId>().Select(x => x.Id!.Value).ToList();
    }

    [Fact]
    public async Task EverySlideStaysInExactlyOneSectionInRunningOrder()
    {
        using var deck = await OpenAsync(WithSections());
        var c = Controller(deck, 0);

        c.NewSlide();          // into "Intro", after slide 1
        c.DuplicateSlide();    // the copy lands beside it, still in "Intro"
        c.Index = 3;
        c.MoveSlide(3, 0);     // "Body"'s only slide moves to the front, into "Intro"

        var saved = await SaveAsync(deck);
        var sections = Sections(saved);

        sections.SelectMany(x => x).ShouldBe(RunningOrder(saved));
        NewValidationErrors(saved).ShouldBeEmpty();

        c.DeleteSlide(0);
        saved = await SaveAsync(deck);
        Sections(saved).SelectMany(x => x).ShouldBe(RunningOrder(saved));
    }

    [Fact]
    public async Task UndoingAMovePutsTheSlideBackInTheSectionItLeft()
    {
        var original = WithSections();
        using var deck = await OpenAsync(original);
        var c = Controller(deck, 1);

        c.MoveSlideEarlier();
        Sections(await SaveAsync(deck)).ShouldBe([[257U, 256U], []]);

        c.Undo();
        Sections(await SaveAsync(deck)).ShouldBe(Sections(original));
    }
}
