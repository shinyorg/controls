using Shiny.Controls.Office.Notebook;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Skia;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using Shouldly;
using SkiaSharp;
using Xunit;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// Rasterises the notebook canvas headlessly.
/// </summary>
/// <remarks>
/// Deliberately not pixel snapshots — those break on every font update. These assert the rules that
/// fail <i>silently</i>: ink and text that come out the same colour as what is behind them are still
/// there in the model, still counted as edits, and completely invisible, which is the one class of
/// defect a unit test over the model can never see.
/// </remarks>
public class NotebookPainterTests
{
    const int Width = 640;
    const int Height = 480;

    static SKBitmap Paint(NotebookPage page, NotebookTheme theme)
    {
        var bitmap = new SKBitmap(Width, Height);
        using var canvas = new SKCanvas(bitmap);
        using var measurer = new SkiaTextMeasurer();
        using var painter = new NotebookPainter(measurer);

        painter.Paint(canvas, new NotebookPaintRequest
        {
            Page = page,
            Zoom = 1,
            ScrollX = 0,
            ScrollY = 0,
            ViewportWidth = Width,
            ViewportHeight = Height,
            Theme = theme
        });

        return bitmap;
    }

    /// <summary>How far the most distant pixel in a region gets from the ground it sits on.</summary>
    /// <remarks>
    /// A contrast floor rather than an exact colour: the glyphs are antialiased, so almost every pixel
    /// is a blend, and asserting on one would be asserting on the font's hinting.
    /// </remarks>
    static double PeakContrast(SKBitmap bitmap, SKRectI region, SKColor ground)
    {
        var peak = 0d;

        for (var y = region.Top; y < region.Bottom && y < bitmap.Height; y++)
        {
            for (var x = region.Left; x < region.Right && x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                peak = Math.Max(peak, Math.Abs(Luma(pixel) - Luma(ground)));
            }
        }

        return peak;
    }

    static double Luma(SKColor c) => (c.Red * 0.299 + c.Green * 0.587 + c.Blue * 0.114) / 255;

    static NotebookPage PageWith(params NoteItem[] items)
    {
        var page = new NotebookPage("p", "Page") { MinWidth = Width, MinHeight = Height, Padding = 0 };
        page.Items.AddRange(items);

        return page;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextNobodyColouredIsReadableOnEitherPage(bool dark)
    {
        var theme = dark ? NotebookTheme.Dark : NotebookTheme.Light;
        var page = PageWith(NotebookDocument.NewTextItem(20, 20, 400, "Readable either way"));

        using var bitmap = Paint(page, theme);

        var ground = new SKColor(theme.Paper.R, theme.Paper.G, theme.Paper.B);

        // The whole point: black is where TextStyle.Default starts, not a decision, so on a dark page
        // it has to follow the theme rather than disappear into it.
        PeakContrast(bitmap, new SKRectI(20, 20, 420, 60), ground).ShouldBeGreaterThan(0.4);
    }

    [Fact]
    public void TextOnAPaleShapeStaysDarkEvenOnADarkPage()
    {
        var pale = new ArgbColor(255, 0xE8, 0xF2, 0xFF);

        var shape = NotebookDocument.NewShapeItem(ShapeGeometry.Rectangle, 40, 40, 260, 120, pale) with
        {
            Text = new ShapeTextBody([new ShapeParagraph([new StyledRun("Capture", NotebookDocument.DefaultTextStyle)])
            {
                Alignment = TextAlignment.Center
            }])
            { Anchor = TextAnchor.Middle }
        };

        using var bitmap = Paint(PageWith(shape), NotebookTheme.Dark);

        // Following the *page* alone would put pale ink on a pale fill. The ground for a glyph is
        // whatever is directly behind it.
        PeakContrast(bitmap, new SKRectI(45, 45, 295, 155), new SKColor(pale.R, pale.G, pale.B))
            .ShouldBeGreaterThan(0.4);
    }

    [Fact]
    public void AnAuthoredColourIsHonouredRatherThanCorrected()
    {
        var red = new ArgbColor(255, 0xD1, 0x3A, 0x3A);

        var item = NotebookDocument.NewTextItem(20, 20, 400) with
        {
            Text = new ShapeTextBody(
                [new ShapeParagraph([new StyledRun("Chosen", NotebookDocument.DefaultTextStyle with { Color = red, FontSize = 40 })])])
        };

        using var bitmap = Paint(PageWith(item), NotebookTheme.Dark);

        var found = false;
        for (var y = 20; y < 80 && !found; y++)
        {
            for (var x = 20; x < 420 && !found; x++)
            {
                var p = bitmap.GetPixel(x, y);

                // The exact authored colour survives somewhere in the glyph body: substituting for it
                // would be repainting a decision somebody made.
                found = p.Red > 190 && p.Green < 90 && p.Blue < 90;
            }
        }

        found.ShouldBeTrue();
    }

    [Fact]
    public void AHighlighterIsPaintedUnderTheWordsItMarks()
    {
        var text = NotebookDocument.NewTextItem(40, 40, 400, "Marked up");

        var band = NotebookDocument.NewInkItem(new InkStroke
        {
            Points = [.. Enumerable.Range(0, 40).Select(i => new InkPoint(45 + i * 8, 52))],
            Color = new ArgbColor(160, 0xFF, 0xE0, 0x3B),
            Width = 22,
            Tool = InkTool.Highlighter
        });

        // Added *after* the text, so z-order alone would put it on top.
        using var bitmap = Paint(PageWith(text, band), NotebookTheme.Light);

        var glyphs = 0;
        for (var y = 40; y < 70; y++)
        {
            for (var x = 45; x < 360; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (Luma(p) < 0.35)
                    glyphs++;
            }
        }

        // Dark glyph pixels survive: a highlighter drawn over them would have washed every one of
        // them towards yellow, which is exactly what a highlighter must not do.
        glyphs.ShouldBeGreaterThan(20);
    }

    [Fact]
    public void APageRulePaintsAndBlankDoesNot()
    {
        var blank = Paint(PageWith(), NotebookTheme.Light);
        var lined = Paint(new NotebookPage("p", "Page")
        {
            MinWidth = Width,
            MinHeight = Height,
            Padding = 0,
            Rule = PageRule.Lines
        }, NotebookTheme.Light);

        using (blank)
        using (lined)
        {
            var ground = new SKColor(255, 255, 255);
            Marks(blank, ground).ShouldBe(0);
            Marks(lined, ground).ShouldBeGreaterThan(0);
        }

        static int Marks(SKBitmap bitmap, SKColor ground)
        {
            var count = 0;
            for (var y = 0; y < bitmap.Height; y += 2)
                for (var x = 0; x < bitmap.Width; x += 2)
                {
                    if (bitmap.GetPixel(x, y) != ground)
                        count++;
                }

            return count;
        }
    }
}
