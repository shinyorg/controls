using Shiny.Controls.Office.Packaging;
using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Text;
using Shiny.Controls.Office.Shapes;
using Shouldly;
using Xunit;

namespace Shiny.Controls.Office.Tests;

public class SlideViewerTests
{
    static async Task<SlideDeck> OpenAsync(IUnsupportedFeatureSink? sink = null)
    {
        using var source = new MemoryStream(SlideFixture.Build(), writable: false);
        return await SlideDeck.OpenAsync(source, sink);
    }

    [Fact]
    public async Task SlidesComeBackInPresentationOrderNotPackageOrder()
    {
        // The fixture creates the content slide first and lists it second on purpose.
        using var deck = await OpenAsync();

        deck.Slides.Count.ShouldBe(2);
        deck.Slides[0].Title.ShouldBe("Deck Title");
        deck.Slides[0].Number.ShouldBe(1);
        deck.Slides[1].Number.ShouldBe(2);
    }

    [Fact]
    public async Task SlideSizeIsReadFromThePresentation()
    {
        using var deck = await OpenAsync();

        deck.SlideWidth.ShouldBe(1280, 0.5);
        deck.SlideHeight.ShouldBe(720, 0.5);
        deck.AspectRatio.ShouldBe(16d / 9, 0.01);
    }

    [Fact]
    public async Task APlaceholderInheritsItsGeometryFromTheLayout()
    {
        // The slide's title shape has an empty spPr. Without walking to the layout it has no position
        // and no size, and the whole deck renders stacked in the corner.
        using var deck = await OpenAsync();

        var title = deck.Slides[0].Shapes.First(x => x.Text?.PlainText == "Deck Title");

        title.X.ShouldBe(OoxmlUnits.EmuToPixels(838200), 0.5);
        title.Y.ShouldBe(OoxmlUnits.EmuToPixels(365125), 0.5);
        title.Width.ShouldBe(OoxmlUnits.EmuToPixels(10515600), 0.5);
        title.Height.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task APlaceholderInheritsRunFormattingFromTheLayoutsListStyle()
    {
        using var deck = await OpenAsync();

        var title = deck.Slides[0].Shapes.First(x => x.Text?.PlainText == "Deck Title");
        var run = title.Text!.Paragraphs[0].Runs[0];

        run.Style.Bold.ShouldBeTrue("the layout's level-1 defaults say bold");
        run.Style.FontSize.ShouldBe(OoxmlUnits.HundredthPointsToPixels(4400), 0.5);
    }

    [Fact]
    public async Task DecorativeMasterShapesAppearOnEverySlide()
    {
        using var deck = await OpenAsync();

        foreach (var slide in deck.Slides)
            slide.Shapes.Any(x => x.Name == "Master stripe").ShouldBeTrue();
    }

    [Fact]
    public async Task MasterPlaceholdersAreNotTreatedAsContent()
    {
        // A placeholder on the layout is a template, not something to draw. Copying them onto the
        // slide produces duplicated, empty boxes over the real content.
        using var deck = await OpenAsync();

        deck.Slides[0].Shapes.Count(x => x.Name == "Title Placeholder").ShouldBe(0);
        deck.Slides[0].Shapes.Count(x => x.Name == "Body Placeholder").ShouldBe(0);
    }

    [Fact]
    public async Task OutlineLevelsAndBulletsSurvive()
    {
        using var deck = await OpenAsync();

        var body = deck.Slides[1].Shapes.First(x => x.Text?.PlainText.Contains("Top level point") == true);
        var paragraphs = body.Text!.Paragraphs;

        paragraphs[0].Level.ShouldBe(0);
        paragraphs[1].Level.ShouldBe(1);
        paragraphs[0].Bullet.ShouldNotBeNull();

        // Level 2 in the layout is a smaller size than level 1.
        paragraphs[1].Runs[0].Style.FontSize.ShouldBeLessThan(paragraphs[0].Runs[0].Style.FontSize);
    }

    [Fact]
    public async Task ThemeColoursResolveWithTheirModifiersApplied()
    {
        // The callout is accent1 with lumMod 60% and lumOff 40% - "Accent 1, Lighter 40%". Ignoring
        // the modifiers renders it at full saturation, which is the most visible way a deck looks wrong.
        using var deck = await OpenAsync();

        var callout = deck.Slides[1].Shapes.First(x => x.Name == "Callout");
        var accent = Shiny.Controls.Office.Spreadsheet.ArgbColor.FromUInt32(
            0xFF000000u | uint.Parse(SlideFixture.ThemeAccent1, System.Globalization.NumberStyles.HexNumber));

        callout.Fill.Solid.ShouldNotBeNull();
        callout.Fill.Solid!.Value.ShouldNotBe(accent, "the luminance modifiers must have been applied");

        // Lightened, so every channel should have risen.
        callout.Fill.Solid.Value.R.ShouldBeGreaterThan(accent.R);
        callout.Fill.Solid.Value.G.ShouldBeGreaterThan(accent.G);
    }

    [Fact]
    public async Task ShapeGeometryOutlineAndTextAreRead()
    {
        using var deck = await OpenAsync();
        var callout = deck.Slides[1].Shapes.First(x => x.Name == "Callout");

        callout.Geometry.ShouldBe(ShapeGeometry.RoundedRectangle);
        callout.Outline.ShouldNotBeNull();
        callout.Outline!.Width.ShouldBe(OoxmlUnits.EmuToPixels(12700), 0.1);
        callout.Text!.Anchor.ShouldBe(TextAnchor.Middle);
        callout.Text.Paragraphs[0].Alignment.ShouldBe(Shiny.Controls.Office.Text.TextAlignment.Center);
        callout.Text.Paragraphs[0].Runs[0].Style.Bold.ShouldBeTrue();
    }

    [Fact]
    public async Task AnUnsupportedPresetFallsBackAndIsReported()
    {
        var collector = new UnsupportedFeatureCollector();
        using var deck = await OpenAsync(collector);

        var exotic = deck.Slides[1].Shapes.First(x => x.Name == "Exotic");
        exotic.Geometry.ShouldBe(ShapeGeometry.Rectangle, "an unknown preset is drawn as its bounding box");

        collector.Features.ShouldContain(x => x.Feature == "Preset shape");
        collector.HasLossy.ShouldBeFalse("a viewer never loses anything; it just does not draw it");
    }

    [Fact]
    public async Task SpeakerNotesAreExtracted()
    {
        using var deck = await OpenAsync();

        deck.Slides[0].Notes.ShouldBeNull();
        deck.Slides[1].Notes.ShouldContain("Remember to mention the numbers");
    }

    [Fact]
    public async Task OpeningIsNonDestructive()
    {
        var original = SlideFixture.Build();

        using var source = new MemoryStream(original, writable: false);
        using var deck = await SlideDeck.OpenAsync(source);

        PackageComparer.Compare(original, deck.ToArray()).IsIdentical.ShouldBeTrue();
    }
}

public class SlideTextStyleFallbackTests
{
    [Fact]
    public async Task APlainShapeDoesNotInheritTheBodyBullet()
    {
        // A shape that is not a placeholder is not a list. Falling back to the master's body style
        // gives every plain text box a bullet, and the space reserved for it also pushes centred
        // text off centre.
        using var deck = await SlideDeck.OpenAsync(new MemoryStream(SlideFixture.Build()));

        var callout = deck.Slides[1].Shapes.First(x => x.Name == "Callout");
        callout.Text!.Paragraphs[0].Bullet.ShouldBeNull();
    }

    [Fact]
    public async Task APlaceholderStillGetsItsBullet()
    {
        using var deck = await SlideDeck.OpenAsync(new MemoryStream(SlideFixture.Build()));

        var body = deck.Slides[1].Shapes.First(x => x.Text?.PlainText.Contains("Top level point") == true);
        body.Text!.Paragraphs[0].Bullet.ShouldNotBeNull();
    }
}
