using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Shell;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Spreadsheet.View;
using SkiaSharp;

namespace Shiny.Controls.Office.Skia;

/// <summary>
/// Pictures for the built-in templates on the backstage's Home and New pages - the first page of a Word
/// template, the top of an Excel one, the title slide of a PowerPoint one - as <c>data:</c> URIs both
/// hosts load as they are.
/// </summary>
/// <remarks>
/// Each is drawn by the painter the editor itself uses, so the picture is what the template opens as.
/// A template that fails to render keeps its plain tile rather than failing the list; blank templates
/// are never drawn (the backstage paints an empty page for them).
/// </remarks>
public static class OfficeTemplateThumbnails
{
    /// <summary>Word's built-in templates (<see cref="WordTemplates.All"/>) with their first page drawn.</summary>
    public static IReadOnlyList<OfficeTemplate> Word(int width = 240)
        => With(WordTemplates.All, t => RenderWord(t, width));

    /// <summary>Excel's built-in templates (<see cref="SpreadsheetTemplates.All"/>) with the top of their first sheet drawn.</summary>
    public static IReadOnlyList<OfficeTemplate> Spreadsheet(int width = 240)
        => With(SpreadsheetTemplates.All, t => RenderSpreadsheet(t, width));

    /// <summary>PowerPoint's built-in templates (<see cref="SlideTemplates.All"/>) with their first slide drawn.</summary>
    public static IReadOnlyList<OfficeTemplate> Slides(int width = 320)
        => With(SlideTemplates.All, t => RenderSlides(t, width));


    /// <summary>A copy of each template with <see cref="OfficeTemplate.Thumbnail"/> set from <paramref name="render"/>.</summary>
    public static IReadOnlyList<OfficeTemplate> With(IEnumerable<OfficeTemplate> templates, Func<OfficeTemplate, byte[]?> render)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(render);

        var list = new List<OfficeTemplate>();
        foreach (var template in templates)
        {
            if (template.IsBlank || !string.IsNullOrWhiteSpace(template.Thumbnail))
            {
                list.Add(template);
                continue;
            }

            byte[]? png;
            try
            {
                png = render(template);
            }
            catch (Exception ex)
            {
                // A picture is a nicety; the named tile still works without one.
                Console.Error.WriteLine($"[Shiny.Office] Thumbnail for template '{template.Id}' failed: {ex.Message}");
                png = null;
            }

            list.Add(png is null ? template : new OfficeTemplate(template.Id, template.Name)
            {
                Description = template.Description,
                Category = template.Category,
                IsBlank = template.IsBlank,
                Open = template.Open,
                Tag = template.Tag,
                Thumbnail = "data:image/png;base64," + Convert.ToBase64String(png)
            });
        }

        return list;
    }


    static byte[] RenderWord(OfficeTemplate template, int width)
    {
        using var document = WordTemplates.OpenAsync(template).GetAwaiter().GetResult();
        var scale = (float)(width / Math.Max(1, document.Page.Width));
        return DocumentPdfExporter.RenderPagePng(document, 0, scale);
    }


    static byte[] RenderSlides(OfficeTemplate template, int width)
    {
        using var stream = SlideTemplates.Create(template.Id);
        using var deck = SlideDeck.OpenAsync(stream).GetAwaiter().GetResult();
        return SlideExporter.ToPng(deck, 0, width);
    }


    /// <summary>
    /// The top-left of the first sheet, the shape of the backstage's Excel tile (about 4:3), with no
    /// headings, gridlines or selection - what a printed page of it would look like.
    /// </summary>
    static byte[]? RenderSpreadsheet(OfficeTemplate template, int width)
    {
        var workbook = SpreadsheetTemplates.Create(template);
        using (workbook)
        {
            var sheet = workbook.VisibleSheets.FirstOrDefault();
            if (sheet is null)
                return null;

            var metrics = GridMetrics.FromWorksheet(sheet);
            metrics.RowHeaderWidth = 0;
            metrics.ColumnHeaderHeight = 0;
            metrics.FrozenPane = default;

            // Enough of the sheet to read as a table: its used width when narrower, else ~6 columns.
            var used = sheet.UsedRange;
            var gridWidth = used is { } r ? metrics.Columns.SizeOfRange(0, r.Right + 1) : 480;
            gridWidth = Math.Clamp(gridWidth, 320, 560);
            var gridHeight = gridWidth * 0.75;

            var scale = (float)(width / gridWidth);
            var height = Math.Max(1, (int)Math.Round(gridHeight * scale));
            var viewport = new GridViewport(metrics) { Width = gridWidth, Height = gridHeight };

            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            surface.Canvas.Clear(SKColors.White);

            using var painter = new SpreadsheetPainter();
            painter.Paint(surface.Canvas, new SpreadsheetPaintRequest
            {
                Workbook = workbook,
                Sheet = sheet,
                Viewport = viewport,
                Selection = new SpreadsheetSelection(),
                Theme = SpreadsheetTheme.Light with { GridLine = new ArgbColor(0, 0, 0, 0) },
                Scale = scale,
                PrintMode = true
            });

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            return data.ToArray();
        }
    }
}
