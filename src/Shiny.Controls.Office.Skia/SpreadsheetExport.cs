using System.Text;
using Shiny.Controls.Office.Shell;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Spreadsheet.View;
using SkiaSharp;

namespace Shiny.Controls.Office.Skia;

/// <summary>What the user asked the File backstage (or the title bar) to do with the workbook.</summary>
public enum SpreadsheetFileAction
{
    /// <summary>Save, from the title bar or the backstage — always the workbook itself (xlsx).</summary>
    Save,

    /// <summary>Save As, in the chosen format.</summary>
    SaveAs,

    /// <summary>Export, in the chosen format.</summary>
    Export,

    /// <summary>Print: the active sheet as a PDF to hand to a printer or viewer.</summary>
    Print
}


/// <summary>
/// A file the spreadsheet's shell wants written. The host decides where it goes — a path, a share
/// sheet, a download — and calls <see cref="WriteToAsync"/> or <see cref="ToBytesAsync"/> for the bytes.
/// </summary>
public sealed class SpreadsheetFileRequest(Workbook workbook, Worksheet sheet, OfficeFileFormat format, string fileName, SpreadsheetFileAction action)
{
    public Workbook Workbook { get; } = workbook;

    /// <summary>The sheet on screen — what CSV and PDF contain. xlsx always carries every sheet.</summary>
    public Worksheet Sheet { get; } = sheet;

    public OfficeFileFormat Format { get; } = format;

    /// <summary>The document name with the format's extension — "Book1.xlsx", "Budget.csv".</summary>
    public string FileName { get; } = fileName;

    public SpreadsheetFileAction Action { get; } = action;

    public Task WriteToAsync(Stream destination, CancellationToken cancellationToken = default)
        => SpreadsheetExport.WriteAsync(this.Workbook, this.Sheet, this.Format, destination, cancellationToken);

    public Task<byte[]> ToBytesAsync(CancellationToken cancellationToken = default)
        => SpreadsheetExport.ToBytesAsync(this.Workbook, this.Sheet, this.Format, cancellationToken);
}


/// <summary>
/// Writes a workbook out as the formats the Office shell's Save As and Export offer: the workbook itself
/// (<c>.xlsx</c>), the active sheet as CSV, or the active sheet as a PDF.
/// </summary>
/// <remarks>
/// Nothing here touches a file system. Each call writes to a stream the host owns, so the same code
/// serves a MAUI app writing to disk and a browser handing the bytes to a download.
/// </remarks>
public static class SpreadsheetExport
{
    /// <summary>Whether <paramref name="format"/> is one this class can write.</summary>
    public static bool CanWrite(OfficeFileFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return format.Id is "xlsx" or "csv" or "pdf";
    }

    /// <summary>Writes <paramref name="sheet"/> (or the workbook, for xlsx) in <paramref name="format"/>.</summary>
    /// <exception cref="NotSupportedException">The format is not xlsx, csv or pdf.</exception>
    public static async Task WriteAsync(Workbook workbook, Worksheet sheet, OfficeFileFormat format, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(destination);

        switch (format.Id)
        {
            case "xlsx":
                await workbook.SaveToAsync(destination, cancellationToken).ConfigureAwait(false);
                break;

            case "csv":
                // A byte-order mark, as Excel writes UTF-8 CSV: without it Excel opens the file as
                // ANSI and every accented character comes back mangled.
                var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(SpreadsheetShell.ToCsv(workbook, sheet))).ToArray();
                await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                break;

            case "pdf":
                WritePdf(workbook, sheet, destination);
                break;

            default:
                throw new NotSupportedException($"A spreadsheet cannot be written as {format.Name}.");
        }
    }

    /// <summary>The bytes of <see cref="WriteAsync"/>, for a host that hands them straight to a download.</summary>
    public static async Task<byte[]> ToBytesAsync(Workbook workbook, Worksheet sheet, OfficeFileFormat format, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await WriteAsync(workbook, sheet, format, buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }


    /// <summary>US Letter in points.</summary>
    const float PageWidth = 612f;
    const float PageHeight = 792f;

    /// <summary>Excel's Normal margins: 0.7" left and right, 0.75" top and bottom.</summary>
    const float MarginX = 0.7f * 72;
    const float MarginY = 0.75f * 72;

    /// <summary>
    /// Prints the sheet's used range onto Letter pages, down then across, with the same painter the grid
    /// uses — so a PDF looks like the sheet, formatting, merges, borders and charts included, less the
    /// headings, gridlines and selection that are the editor's and not the document's.
    /// </summary>
    /// <remarks>An empty sheet still produces one blank page: a PDF with no pages is not a valid file.</remarks>
    public static void WritePdf(Workbook workbook, Worksheet sheet, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(destination);

        var metrics = GridMetrics.FromWorksheet(sheet);
        metrics.RowHeaderWidth = 0;
        metrics.ColumnHeaderHeight = 0;
        metrics.FrozenPane = default;

        // Grid pixels are 96 dpi; a PDF page is in points.
        const float scale = 72f / 96f;
        var contentWidth = (PageWidth - 2 * MarginX) / scale;
        var contentHeight = (PageHeight - 2 * MarginY) / scale;

        var layout = SheetPagination.For(sheet, metrics, contentWidth, contentHeight);

        // Gridlines are not printed by default in Excel; the painter draws them when the sheet shows them.
        var theme = SpreadsheetTheme.Light with { GridLine = new ArgbColor(0, 0, 0, 0) };

        using var document = SKDocument.CreatePdf(destination, new SKDocumentPdfMetadata
        {
            Title = sheet.Name,
            Creator = "Shiny Controls",
            Producer = "Shiny Controls"
        });

        if (layout is null)
        {
            document.BeginPage(PageWidth, PageHeight);
            document.EndPage();
            document.Close();
            return;
        }

        using var painter = new SpreadsheetPainter();

        foreach (var page in layout.Pages())
        {
            var viewport = new GridViewport(metrics)
            {
                Width = metrics.Columns.SizeOfRange(page.Left, page.Right + 1),
                Height = metrics.Rows.SizeOfRange(page.Top, page.Bottom + 1)
            };
            viewport.ScrollTo(metrics.Columns.SizeOfRange(0, page.Left), metrics.Rows.SizeOfRange(0, page.Top));

            var canvas = document.BeginPage(PageWidth, PageHeight);
            canvas.Save();
            canvas.Translate(MarginX, MarginY);
            canvas.ClipRect(new SKRect(0, 0, (float)(viewport.Width * scale), (float)(viewport.Height * scale)));

            painter.Paint(canvas, new SpreadsheetPaintRequest
            {
                Workbook = workbook,
                Sheet = sheet,
                Viewport = viewport,
                Selection = new SpreadsheetSelection(),
                Theme = theme,
                Scale = scale,
                PrintMode = true
            });

            canvas.Restore();
            document.EndPage();
        }

        document.Close();
    }
}
