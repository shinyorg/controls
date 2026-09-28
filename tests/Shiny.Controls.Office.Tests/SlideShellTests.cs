using System.Globalization;
using System.IO.Compression;
using System.Text;
using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Shell;
using Shiny.Controls.Office.Skia;
using Shiny.Controls.Office.Text;
using Shouldly;
using Xunit;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// The PowerPoint shell's host-agnostic half: the status bar's words and view ids, the backstage's
/// templates and info, command-search helpers, and pptx/PDF/picture export.
/// </summary>
public class SlideShellTests
{
    static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    sealed class Fixed : ITextMeasurer
    {
        public TextMetrics Measure(ReadOnlySpan<char> text, TextStyle style)
            => new(text.Length * 8, style.FontSize * 0.8, style.FontSize * 0.2);

        public TextMetrics LineMetrics(TextStyle style)
            => new(0, style.FontSize * 0.8, style.FontSize * 0.2);
    }

    [Fact]
    public void SlideText_CountsFromOne_AndNamesTheSpecialViews()
    {
        SlideShell.SlideText(2, 12).ShouldBe("Slide 3 of 12");
        SlideShell.SlideText(0, 0).ShouldBe("No slides");
        SlideShell.SlideText(4, 12, SlideEditorViewMode.SlideMaster).ShouldBe("Slide Master");
    }

    [Fact]
    public void ViewModeIds_RoundTrip_AndReadingIsTheShow()
    {
        SlideShell.ViewModeId(SlideEditorViewMode.Normal).ShouldBe(OfficeViewModes.SlideNormal.Id);
        SlideShell.ViewModeId(SlideEditorViewMode.SlideSorter).ShouldBe(OfficeViewModes.Sorter.Id);
        SlideShell.ViewModeId(SlideEditorViewMode.Outline).ShouldBe(OfficeViewModes.SlideNormal.Id);
        SlideShell.ViewModeId(SlideEditorViewMode.Normal, presenting: true).ShouldBe(OfficeViewModes.Reading.Id);

        SlideShell.ParseViewMode(OfficeViewModes.Sorter.Id).ShouldBe(SlideEditorViewMode.SlideSorter);
        SlideShell.ParseViewMode(OfficeViewModes.SlideNormal.Id).ShouldBe(SlideEditorViewMode.Normal);
        SlideShell.ParseViewMode(OfficeViewModes.Reading.Id).ShouldBeNull();
    }

    [Theory]
    [InlineData("Bold (Ctrl+B)", "Bold", "Ctrl+B")]
    [InlineData("Add a slide after this one (Ctrl+M)", "Add a slide after this one", "Ctrl+M")]
    [InlineData("Decrease list level (Shift+Tab)", "Decrease list level", "Shift+Tab")]
    public void SplitShortcut_MovesTheKeysOutOfTheTooltip(string tooltip, string name, string keys)
    {
        var split = SlideShell.SplitShortcut(tooltip);
        split.ShouldNotBeNull();
        split.Value.Tooltip.ShouldBe(name);
        split.Value.Shortcut.ShouldBe(keys);
    }

    [Fact]
    public void SplitShortcut_LeavesAPlainTooltipAlone()
    {
        SlideShell.SplitShortcut("Insert a shape").ShouldBeNull();
        SlideShell.SplitShortcut("Line spacing (1.5)").ShouldBeNull();
    }

    [Theory]
    [InlineData(SlideTemplates.BlankId, 1)]
    [InlineData(SlideTemplates.ProjectId, 6)]
    [InlineData(SlideTemplates.PitchId, 8)]
    [InlineData(SlideTemplates.LessonId, 6)]
    public async Task Templates_OpenAsEditableDecks(string id, int slides)
    {
        var template = SlideTemplates.All.Single(x => x.Id == id);
        using var deck = await SlideTemplates.OpenAsync(template);

        deck.IsEditable.ShouldBeTrue();
        deck.Slides.Count.ShouldBe(slides);
        deck.Undo.CanUndo.ShouldBeFalse();
        deck.SlideWidth.ShouldBe(1280, 0.5);
    }

    [Fact]
    public async Task Templates_CarryTheirThemeAndTheFiveLayouts()
    {
        var pitch = SlideTemplates.All.Single(x => x.Id == SlideTemplates.PitchId);
        using var deck = await SlideTemplates.OpenAsync(pitch);
        var controller = new SlideEditorController(deck, new Fixed());

        controller.ThemeName.ShouldBe("Midnight");
        controller.Layouts.Select(x => x.Name).ShouldBe(["Title Slide", "Title and Content", "Section Header", "Title Only", "Blank"]);
        deck.Slides[0].Title.ShouldBe("Company Name");

        // New Slide after the title slide is a content slide, PowerPoint's rule.
        controller.Index = 0;
        controller.NewSlide();
        controller.Layouts.Single(x => x.IsCurrent).Name.ShouldBe("Title and Content");
    }

    [Fact]
    public async Task BlankTemplate_ShowsPrompts_AndCountsNoWords()
    {
        using var deck = await SlideTemplates.OpenAsync(SlideTemplates.All[0]);

        SlideShell.WordCount(deck).ShouldBe(0);
        deck.Slides[0].Shapes.Where(x => x.Prompt is not null).Select(x => x.Prompt!.PlainText.Trim()).ShouldContain("Click to add title");
    }

    [Fact]
    public async Task DocumentInfo_CountsSlidesHiddenSlidesAndWords()
    {
        using var deck = await SlideTemplates.OpenAsync(SlideTemplates.All.Single(x => x.Id == SlideTemplates.ProjectId));
        var controller = new SlideEditorController(deck, new Fixed());
        controller.Index = 1;
        controller.ToggleHideSlide();

        var info = SlideShell.DocumentInfo(deck, "Status", "Allan", En);
        var stats = info.Statistics.ToDictionary(x => x.Name, x => x.Value);

        stats["Slides"].ShouldBe("6");
        stats["Hidden slides"].ShouldBe("1");
        int.Parse(stats["Words"], NumberStyles.AllowThousands, En).ShouldBeGreaterThan(40);
        info.Author.ShouldBe("Allan");
        info.Title.ShouldBe("Status");
    }

    [Fact]
    public async Task Search_FindsTextOnALaterSlide()
    {
        using var deck = await SlideTemplates.OpenAsync(SlideTemplates.All.Single(x => x.Id == SlideTemplates.PitchId));
        var controller = new SlideEditorController(deck, new Fixed());
        controller.Resize(960, 540);

        SlideShell.Search(controller, "Unit economics").ShouldBeTrue();
        controller.Index.ShouldBe(4);
        SlideShell.Search(controller, "nowhere to be found").ShouldBeFalse();
    }

    [Fact]
    public void CountWords_SplitsOnWhitespace()
    {
        SlideShell.CountWords("  One two\tthree\nfour ").ShouldBe(4);
        SlideShell.CountWords(null).ShouldBe(0);
    }

    [Fact]
    public async Task Export_WritesPptxPdfPictureAndZip()
    {
        using var deck = await SlideTemplates.OpenAsync(SlideTemplates.All.Single(x => x.Id == SlideTemplates.LessonId));

        var pptx = await SlideExport.ToBytesAsync(deck, 0, OfficeFileFormats.Pptx);
        Encoding.ASCII.GetString(pptx, 0, 2).ShouldBe("PK");

        var pdf = await SlideExport.ToBytesAsync(deck, 0, OfficeFileFormats.Pdf);
        Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");

        var png = await SlideExport.ToBytesAsync(deck, 2, OfficeFileFormats.Png);
        png.Take(4).ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var jpg = await SlideExport.ToBytesAsync(deck, 2, OfficeFileFormats.Jpeg);
        jpg.Take(2).ShouldBe(new byte[] { 0xFF, 0xD8 });

        var zip = await SlideExport.ToBytesAsync(deck, 0, SlideExport.AllSlidesPng);
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        archive.Entries.Select(x => x.Name).ShouldBe(["Slide1.png", "Slide2.png", "Slide3.png", "Slide4.png", "Slide5.png", "Slide6.png"]);

        SlideExport.CanWrite(OfficeFileFormats.Csv).ShouldBeFalse();
        SlideExport.ExportFormats.ShouldContain(SlideExport.AllSlidesPng);
    }

    [Fact]
    public async Task SavedTemplate_ReopensWithTheSameSlides()
    {
        using var deck = await SlideTemplates.OpenAsync(SlideTemplates.All.Single(x => x.Id == SlideTemplates.ProjectId));
        var bytes = await SlideExport.ToBytesAsync(deck, 0, OfficeFileFormats.Pptx);

        using var reopened = await SlideDeck.OpenAsync(new MemoryStream(bytes));
        reopened.Slides.Select(x => x.Title).ShouldBe(deck.Slides.Select(x => x.Title));
        reopened.Slides[0].Notes.ShouldNotBeNullOrWhiteSpace();
    }
}
