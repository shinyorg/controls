using DocumentFormat.OpenXml.Packaging;
using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using Shouldly;
using Xunit;
using D = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using Package = DocumentFormat.OpenXml.Packaging.PresentationDocument;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// The run-property inheritance chain for slide text, and scheme colours through the colour map.
/// </summary>
/// <remarks>
/// The fixture's text states no colour anywhere — not on the run, the layout placeholder or the
/// master's text styles — which is exactly the deck that drew black text on Midnight's navy.
/// </remarks>
public class SlideTextColorInheritanceTests
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

    static SlideEditorController Controller(SlideDeck deck)
    {
        var controller = new SlideEditorController(deck, new Fixed()) { Margin = 0 };
        controller.Resize(deck.SlideWidth, deck.SlideHeight);
        return controller;
    }

    static byte[] Edit(Action<Package> change)
    {
        var buffer = new MemoryStream();
        buffer.Write(SlideFixture.Build());
        using (var document = Package.Open(buffer, true))
        {
            change(document);
            document.Save();
        }

        return buffer.ToArray();
    }

    static SlideMasterPart Master(Package document) => document.PresentationPart!.SlideMasterParts.First();

    static StyledRun TitleRun(SlideDeck deck) => deck.Slides[0].Shapes.First(x => x.Text?.PlainText == "Deck Title").Text!.Paragraphs[0].Runs[0];

    static StyledRun BodyRun(SlideDeck deck) => deck.Slides[1].Shapes.First(x => x.Text?.PlainText.StartsWith("Top level", StringComparison.Ordinal) == true).Text!.Paragraphs[0].Runs[0];

    static ArgbColor Hex(string value) => DrawingReader.ParseHex(value);

    [Fact]
    public async Task UncolouredTextIsTheThemesTextOneColour()
    {
        using var deck = await OpenAsync();

        TitleRun(deck).Style.Color.ShouldBe(Hex("000000"));
        BodyRun(deck).Style.Color.ShouldBe(Hex("000000"));
    }

    [Fact]
    public async Task ApplyingMidnightTurnsUncolouredTextLightOnItsNavyBackground()
    {
        using var deck = await OpenAsync();
        var midnight = SlideThemeDefinition.BuiltIn.First(x => x.Name == "Midnight");

        Controller(deck).ApplyTheme(midnight);

        // tx1 → dk1, which Midnight makes white; bg1 → lt1, its navy.
        TitleRun(deck).Style.Color.ShouldBe(midnight.Colors.Dark1);
        BodyRun(deck).Style.Color.ShouldBe(midnight.Colors.Dark1);
        deck.Slides[1].Background.Solid.ShouldBe(midnight.Colors.Light1);

        // The slideshow reads the same slides.
        var show = new SlideShowController(deck, 2);
        show.Current!.Shapes.First(x => x.Text?.PlainText.StartsWith("Top level", StringComparison.Ordinal) == true)
            .Text!.Paragraphs[0].Runs[0].Style.Color.ShouldBe(midnight.Colors.Dark1);
    }

    [Fact]
    public async Task ADarkVariantFollowsTheSwappedScheme()
    {
        using var deck = await OpenAsync();
        var dark = SlideThemeDefinition.BuiltIn[0].Variants[3];

        Controller(deck).ApplyColorVariant(dark);

        BodyRun(deck).Style.Color.ShouldBe(dark.Dark1);
        deck.Slides[1].Background.Solid.ShouldBe(dark.Light1);
    }

    [Fact]
    public async Task TheMastersColourMapDecidesWhatTextOneMeans()
    {
        var bytes = Edit(document =>
        {
            var map = Master(document).SlideMaster!.ColorMap!;
            map.Text1 = D.ColorSchemeIndexValues.Light1;
            map.Background1 = D.ColorSchemeIndexValues.Dark1;
        });

        using var deck = await OpenAsync(bytes);

        BodyRun(deck).Style.Color.ShouldBe(Hex("FFFFFF"));
        deck.Slides[1].Background.Solid.ShouldBe(Hex("000000"));
    }

    [Fact]
    public async Task ASlidesColourMapOverrideWinsOverTheMasters()
    {
        var bytes = Edit(document =>
        {
            var slide = document.PresentationPart!.SlideParts.First(x => x.Slide!.InnerText.Contains("Deck Title", StringComparison.Ordinal));
            slide.Slide!.ColorMapOverride = new P.ColorMapOverride(new D.OverrideColorMapping
            {
                Background1 = D.ColorSchemeIndexValues.Dark1,
                Text1 = D.ColorSchemeIndexValues.Light1,
                Background2 = D.ColorSchemeIndexValues.Dark2,
                Text2 = D.ColorSchemeIndexValues.Light2,
                Accent1 = D.ColorSchemeIndexValues.Accent1,
                Accent2 = D.ColorSchemeIndexValues.Accent2,
                Accent3 = D.ColorSchemeIndexValues.Accent3,
                Accent4 = D.ColorSchemeIndexValues.Accent4,
                Accent5 = D.ColorSchemeIndexValues.Accent5,
                Accent6 = D.ColorSchemeIndexValues.Accent6,
                Hyperlink = D.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = D.ColorSchemeIndexValues.FollowedHyperlink
            });
        });

        using var deck = await OpenAsync(bytes);

        TitleRun(deck).Style.Color.ShouldBe(Hex("FFFFFF"));

        // The other slide keeps the master's map.
        BodyRun(deck).Style.Color.ShouldBe(Hex("000000"));
    }

    [Fact]
    public async Task TheMastersTitleStyleColourReachesThroughALayoutThatOnlySetsTheSize()
    {
        var bytes = Edit(document =>
        {
            var title = Master(document).SlideMaster!.TextStyles!.TitleStyle!.GetFirstChild<D.Level1ParagraphProperties>()!;
            title.GetFirstChild<D.DefaultRunProperties>()!.AppendChild(new D.SolidFill(new D.SchemeColor { Val = D.SchemeColorValues.Accent2 }));
        });

        using var deck = await OpenAsync(bytes);
        var run = TitleRun(deck);

        run.Style.Color.ShouldBe(Hex("ED7D31"));

        // The layout's own size and weight still win over the master's.
        run.Style.Bold.ShouldBeTrue();
        run.Style.FontSize.ShouldBe(OoxmlUnits.HundredthPointsToPixels(4400));
    }

    [Fact]
    public async Task TheMasterPlaceholdersListStyleSitsBetweenTheLayoutAndTheTextStyles()
    {
        var bytes = Edit(document =>
        {
            // A master body placeholder whose list style colours level one accent6.
            var tree = Master(document).SlideMaster!.CommonSlideData!.ShapeTree!;
            tree.AppendChild(new P.Shape(
                new P.NonVisualShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 20U, Name = "Master body" },
                    new P.NonVisualShapeDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties(new P.PlaceholderShape { Type = P.PlaceholderValues.Body, Index = 1U })),
                new P.ShapeProperties(),
                new P.TextBody(
                    new D.BodyProperties(),
                    new D.ListStyle(new D.Level1ParagraphProperties(
                        new D.DefaultRunProperties(new D.SolidFill(new D.SchemeColor { Val = D.SchemeColorValues.Accent6 })))),
                    new D.Paragraph())));
        });

        using var deck = await OpenAsync(bytes);

        BodyRun(deck).Style.Color.ShouldBe(Hex("70AD47"));
    }

    [Fact]
    public async Task ThePresentationsDefaultTextStyleIsTheLastLink()
    {
        var bytes = Edit(document =>
        {
            document.PresentationPart!.Presentation!.DefaultTextStyle = new P.DefaultTextStyle(
                new D.Level1ParagraphProperties(
                    new D.DefaultRunProperties(new D.SolidFill(new D.SchemeColor { Val = D.SchemeColorValues.Text2 }))));
        });

        using var deck = await OpenAsync(bytes);

        // tx2 → dk2 in the fixture's theme.
        BodyRun(deck).Style.Color.ShouldBe(Hex("44546A"));
    }

    [Fact]
    public async Task AnExplicitRunColourStillWins()
    {
        var bytes = Edit(document =>
        {
            var slide = document.PresentationPart!.SlideParts.First(x => x.Slide!.InnerText.Contains("Deck Title", StringComparison.Ordinal));
            var run = slide.Slide!.Descendants<D.Run>().First();
            run.RunProperties!.AppendChild(new D.SolidFill(new D.RgbColorModelHex { Val = "C00000" }));
        });

        using var deck = await OpenAsync(bytes);
        var midnight = SlideThemeDefinition.BuiltIn.First(x => x.Name == "Midnight");
        Controller(deck).ApplyTheme(midnight);

        TitleRun(deck).Style.Color.ShouldBe(Hex("C00000"));
    }

    /// <summary>
    /// Every template on every built-in theme and variant keeps its placeholder text readable against
    /// the slide background — the regression the Design tab exposed.
    /// </summary>
    [Fact]
    public async Task EveryTemplateStaysReadableOnEveryThemeAndVariant()
    {
        foreach (var template in SlideTemplates.All)
        {
            foreach (var theme in SlideThemeDefinition.BuiltIn)
            {
                foreach (var variant in theme.Variants)
                {
                    using var deck = await SlideTemplates.OpenAsync(template);
                    var controller = Controller(deck);
                    controller.ApplyTheme(theme);
                    controller.ApplyColorVariant(variant);

                    foreach (var slide in deck.Slides)
                    {
                        if (slide.Background.Solid is not { } ground)
                            continue;

                        foreach (var shape in slide.Shapes.Where(x => x.PlaceholderType is not null && x.Fill.IsEmpty && x.Text is not null))
                        {
                            foreach (var run in shape.Text!.Paragraphs.SelectMany(x => x.Runs).Where(x => x.Text.Trim().Length > 0))
                            {
                                Contrast(run.Style.Color, ground).ShouldBeGreaterThan(
                                    2.5,
                                    $"{template.Id} / {variant.Name} slide {slide.Number}: '{run.Text}' {run.Style.Color} on {ground}");
                            }
                        }
                    }
                }
            }
        }
    }

    static double Contrast(ArgbColor a, ArgbColor b)
    {
        static double Channel(byte c)
        {
            var v = c / 255d;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        static double Luminance(ArgbColor c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

        var (l1, l2) = (Luminance(a), Luminance(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }
}
