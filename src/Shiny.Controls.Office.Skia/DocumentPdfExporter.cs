using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Spreadsheet;
using SkiaSharp;

namespace Shiny.Controls.Office.Skia;

/// <summary>
/// Writes a Word document as a PDF — one PDF page per printed page, drawn by the same
/// <see cref="DocumentPainter"/> the editors paint the screen with.
/// </summary>
/// <remarks>
/// <para>
/// The document is laid out afresh in print layout at 100%, whatever the editor on screen is showing,
/// so the PDF's page breaks are the print layout's — the ones Word would print. Headers, footers,
/// footnotes, page colour and the text watermark come along; the editing overlays (caret, selection,
/// find hits, spelling squiggles, comment balloons) do not.
/// </para>
/// <para>
/// Text is drawn as glyphs with embedded fonts, so it stays selectable and searchable in a PDF reader.
/// Both hosts use this: the Blazor editor streams it to the browser to download or print, MAUI hands
/// the stream to the host.
/// </para>
/// </remarks>
public static class DocumentPdfExporter
{
    /// <summary>A theme for paper: a white page and no screen-only chrome around it.</summary>
    public static DocumentTheme PrintTheme { get; } = DocumentTheme.Light with
    {
        SurroundBackground = new ArgbColor(255, 255, 255, 255),
        PageBorder = new ArgbColor(0, 0, 0, 0),
        PageShadow = new ArgbColor(0, 0, 0, 0)
    };

    /// <summary>Writes <paramref name="document"/> to <paramref name="output"/> as a PDF. Returns the page count.</summary>
    public static int Export(WordDocument document, Stream output, DocumentPdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);
        options ??= new DocumentPdfOptions();

        using var measurer = new SkiaTextMeasurer(options.Fonts);
        using var painter = new DocumentPainter(measurer);
        var controller = Layout(document, measurer);

        var metadata = new SKDocumentPdfMetadata
        {
            Title = options.Title ?? string.Empty,
            Author = options.Author ?? string.Empty,
            Creator = "Shiny Controls",
            Producer = "Shiny Controls",
            RasterDpi = 150,
            EncodingQuality = 90,
            Creation = DateTime.Now,
            Modified = DateTime.Now
        };

        using var pdf = SKDocument.CreatePdf(output, metadata)
            ?? throw new InvalidOperationException("This SkiaSharp build has no PDF backend.");

        var setup = document.Page;
        var pageWidthPoints = (float)(setup.Width * 72 / 96);
        var pageHeightPoints = (float)(setup.Height * 72 / 96);
        var count = 0;

        foreach (var page in controller.Pagination.Pages)
        {
            var canvas = pdf.BeginPage(pageWidthPoints, pageHeightPoints);
            PaintPage(painter, controller, page, canvas, 72f / 96f, options.Theme ?? PrintTheme);
            pdf.EndPage();
            count++;
        }

        pdf.Close();
        return count;
    }

    /// <summary>Renders one page as a PNG — the backstage's print preview. <paramref name="scale"/> is device pixels per layout pixel.</summary>
    public static byte[] RenderPagePng(WordDocument document, int pageIndex = 0, float scale = 1f, DocumentPdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= new DocumentPdfOptions();

        using var measurer = new SkiaTextMeasurer(options.Fonts);
        using var painter = new DocumentPainter(measurer);
        var controller = Layout(document, measurer);

        var pages = controller.Pagination.Pages;
        var page = pages[Math.Clamp(pageIndex, 0, pages.Count - 1)];
        var setup = document.Page;

        var info = new SKImageInfo(
            Math.Max(1, (int)Math.Ceiling(setup.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(setup.Height * scale)));

        using var surface = SKSurface.Create(info);
        PaintPage(painter, controller, page, surface.Canvas, scale, options.Theme ?? PrintTheme);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    /// <summary>How many pages the print layout makes.</summary>
    public static int PageCount(WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var measurer = new SkiaTextMeasurer();
        return Layout(document, measurer).Pagination.Pages.Count;
    }

    /// <summary>A controller of its own, in print at 100% with a viewport exactly one page wide.</summary>
    static DocumentController Layout(WordDocument document, SkiaTextMeasurer measurer)
    {
        var controller = new DocumentController(document, measurer)
        {
            PageLayout = DocumentPageLayout.Print,
            Zoom = 1.0
        };

        controller.Resize(document.Page.Width, document.Page.Height);
        return controller;
    }

    static void PaintPage(DocumentPainter painter, DocumentController controller, DocumentPage page, SKCanvas canvas, float scale, DocumentTheme theme)
    {
        // The painter places each sheet at its ViewTop in the scrolling view; shifting the canvas up by
        // that much puts this one at the top of the output page.
        canvas.Save();
        canvas.Scale(scale);
        canvas.Translate(0, (float)-page.ViewTop);

        var view = new DocumentPageView(page, controller.HeaderFor(page), controller.FooterFor(page))
        {
            Footnotes = controller.FootnotesFor(page)
        };

        painter.Paint(canvas, new DocumentPaintRequest
        {
            Blocks = controller.Blocks,
            Viewport = controller.Viewport,
            Theme = theme,
            Scale = 1f,
            PageX = 0,
            ContentX = controller.Document.Page.MarginLeft,
            PageWidth = controller.Document.Page.Width,
            PageHeight = controller.Document.Page.Height,
            Pages = [view],
            Setup = controller.Document.Page,
            PageColor = controller.Document.PageColor,
            WatermarkText = controller.Document.WatermarkText
        });

        canvas.Restore();
    }
}

/// <summary>Options for <see cref="DocumentPdfExporter"/>.</summary>
public sealed class DocumentPdfOptions
{
    /// <summary>The PDF's title metadata.</summary>
    public string? Title { get; init; }

    /// <summary>The PDF's author metadata.</summary>
    public string? Author { get; init; }

    /// <summary>The paper theme. Defaults to <see cref="DocumentPdfExporter.PrintTheme"/>.</summary>
    public DocumentTheme? Theme { get; init; }

    /// <summary>Fonts to consult before the platform's — on WebAssembly, where there are no system fonts.</summary>
    public OfficeFontRegistry? Fonts { get; init; }
}
