using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Skia;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using Shouldly;
using SkiaSharp;
using Xunit;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// Ink is made legible against the fill it sits on, not against the page behind the fill.
/// </summary>
/// <remarks>
/// The bug: in <see cref="DocumentTheme.Dark"/> a table header with a light authored fill kept that
/// fill while its default black text was lifted to near-white - because the text was measured against
/// the dark page, which it was not sitting on. The same fault made white header text on a dark fill
/// come out grey in the light theme, and made default spreadsheet ink white on a light cell fill.
/// </remarks>
public class ShadedInkContrastTests
{
    const int Width = 480;
    const int Height = 200;

    static readonly ArgbColor LightFill = new(255, 0xD9, 0xD9, 0xD9);
    static readonly ArgbColor NavyFill = new(255, 0x1F, 0x38, 0x64);

    static double Luminance(SKColor p) => 0.299 * p.Red + 0.587 * p.Green + 0.114 * p.Blue;

    /// <summary>Lays out a one-row table - a shaded header cell beside a plain one - and paints it.</summary>
    static (SKBitmap Bitmap, SKRect ShadedCell) Paint(DocumentTheme theme, ArgbColor fill, ArgbColor ink)
    {
        using var measurer = new SkiaTextMeasurer();

        var style = TextStyle.Default with { Color = ink, FontSize = 20, Bold = true };
        DocumentBlock Cell(string text) => new DocumentParagraph([new StyledRun(text, style)], ParagraphFormat.Default);

        var table = new DocumentTable(
        [
            new DocumentTableRow(
            [
                new DocumentTableCell([Cell("HEADER WWW")]) { Shading = fill },
                new DocumentTableCell([Cell("plain")])
            ])
        ]);

        var layout = new DocumentLayoutEngine(measurer).Layout([table], Width - 40);
        var laidOut = layout.Blocks.OfType<LaidOutTable>().Single();
        var shaded = laidOut.Cells.First(x => x.Shading is not null);

        var bitmap = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        using (var painter = new DocumentPainter(measurer))
        {
            painter.Paint(canvas, new DocumentPaintRequest
            {
                Blocks = layout.Blocks,
                Viewport = new DocumentViewport { Width = Width, Height = Height, ContentHeight = layout.Height },
                Theme = theme,
                PageX = 20,
                PageWidth = Width - 40
            });
        }

        // Inset past the border hairline so only the fill and the glyphs are sampled.
        var rect = new SKRect(
            (float)(20 + shaded.X + 3),
            (float)(shaded.Y + 3),
            (float)(20 + shaded.X + shaded.Width - 3),
            (float)(shaded.Y + shaded.Height - 3));

        return (bitmap, rect);
    }

    static (double Darkest, double Lightest) Range(SKBitmap bitmap, SKRect area)
    {
        var darkest = 255d;
        var lightest = 0d;

        for (var y = (int)area.Top; y < (int)area.Bottom; y++)
            for (var x = (int)area.Left; x < (int)area.Right; x++)
            {
                var l = Luminance(bitmap.GetPixel(x, y));
                darkest = Math.Min(darkest, l);
                lightest = Math.Max(lightest, l);
            }

        return (darkest, lightest);
    }

    [Fact]
    public void DarkThemeKeepsDefaultTextDarkOnALightHeaderFill()
    {
        var (bitmap, cell) = Paint(DocumentTheme.Dark, LightFill, TextStyle.Default.Color);
        using var _ = bitmap;

        var (darkest, _) = Range(bitmap, cell);

        // Unfixed, the text was lifted to ~#B8B8B8 on a #D9D9D9 fill - the darkest thing in the cell was
        // barely 30 levels below the fill.
        darkest.ShouldBeLessThan(Luminance(new SKColor(LightFill.R, LightFill.G, LightFill.B)) - 120,
            "header text must contrast with the light fill it is painted on");
    }

    [Fact]
    public void LightThemeKeepsWhiteTextWhiteOnADarkHeaderFill()
    {
        var white = new ArgbColor(255, 255, 255, 255);
        var (bitmap, cell) = Paint(DocumentTheme.Light, NavyFill, white);
        using var _ = bitmap;

        var (_, lightest) = Range(bitmap, cell);

        // Unfixed, white measured against the white page was pushed down to a mid grey on the navy band.
        lightest.ShouldBeGreaterThan(230, "authored white text on a dark fill already contrasts and must be left alone");
    }

    [Fact]
    public void DarkThemeStillLiftsUnshadedText()
    {
        // The page case is unchanged: black body text on the dark page is still lifted.
        var (bitmap, _) = Paint(DocumentTheme.Dark, LightFill, TextStyle.Default.Color);
        using var __ = bitmap;

        var page = DocumentTheme.Dark.PageBackground;
        var lightest = 0d;
        for (var y = 0; y < Height; y++)
            for (var x = Width / 2 + 10; x < Width - 30; x++)
                lightest = Math.Max(lightest, Luminance(bitmap.GetPixel(x, y)));

        lightest.ShouldBeGreaterThan(0.299 * page.R + 0.587 * page.G + 0.114 * page.B + 100);
    }

    static ArgbColor CellInk(ResolvedFormat format, SpreadsheetTheme theme)
    {
        var method = typeof(SpreadsheetPainter).GetMethod(
            "CellInk",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        return (ArgbColor)method.Invoke(null, [format, theme])!;
    }

    [Fact]
    public void SpreadsheetDefaultInkReadsOnALightFillInTheDarkTheme()
    {
        var ink = CellInk(ResolvedFormat.Default with { Background = LightFill }, SpreadsheetTheme.Dark);

        var fillLight = 0.299 * LightFill.R + 0.587 * LightFill.G + 0.114 * LightFill.B;
        var inkLight = 0.299 * ink.R + 0.587 * ink.G + 0.114 * ink.B;

        (fillLight - inkLight).ShouldBeGreaterThan(100);
    }

    [Fact]
    public void SpreadsheetInkIsUntouchedWhereItAlreadyReads()
    {
        // No fill: the theme's ink as before.
        CellInk(ResolvedFormat.Default, SpreadsheetTheme.Dark).ShouldBe(SpreadsheetTheme.Dark.CellText);

        // A colour the author chose is their own pairing with their own fill.
        var red = new ArgbColor(255, 0xC0, 0, 0);
        CellInk(ResolvedFormat.Default with { Foreground = red, Background = LightFill }, SpreadsheetTheme.Dark).ShouldBe(red);

        // A dark fill under the dark theme's light ink already contrasts.
        CellInk(ResolvedFormat.Default with { Background = NavyFill }, SpreadsheetTheme.Dark).ShouldBe(SpreadsheetTheme.Dark.CellText);
    }
}
