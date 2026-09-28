namespace Shiny.Controls.Office.Shell;

/// <summary>
/// A file format the backstage offers on Save As or Export.
/// </summary>
/// <remarks>
/// The shell never writes a file. Picking a format raises an event carrying one of these, and the host
/// decides what writing it means — a download on the web, a save panel on a desktop, a share sheet on a
/// phone. <see cref="Id"/> is what a host switches on; the rest is what the backstage draws.
/// </remarks>
/// <param name="Id">A stable key — "docx", "pdf", "csv".</param>
/// <param name="Name">What the backstage calls it — "Word Document", "PDF".</param>
/// <param name="Extension">The extension including the dot — ".docx".</param>
/// <param name="Description">One line under the name.</param>
/// <param name="MimeType">The media type, for a download or share sheet.</param>
/// <param name="IsNative">True for the app's own round-tripping format.</param>
public sealed record OfficeFileFormat(
    string Id,
    string Name,
    string Extension,
    string Description,
    string MimeType,
    bool IsNative = false)
{
    /// <summary>A file name for <paramref name="documentName"/> in this format, replacing any extension it had.</summary>
    public string FileNameFor(string? documentName)
    {
        var name = string.IsNullOrWhiteSpace(documentName) ? "Document" : documentName.Trim();
        var dot = name.LastIndexOf('.');
        if (dot > 0 && name.Length - dot <= 6)
            name = name[..dot];

        return name + this.Extension;
    }
}


/// <summary>The formats the three editors offer.</summary>
public static class OfficeFileFormats
{
    public static readonly OfficeFileFormat Docx = new(
        "docx", "Word Document", ".docx", "The standard Word format, editable anywhere.",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", IsNative: true);

    public static readonly OfficeFileFormat Xlsx = new(
        "xlsx", "Excel Workbook", ".xlsx", "The standard Excel format, with every sheet and formula.",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", IsNative: true);

    public static readonly OfficeFileFormat Pptx = new(
        "pptx", "PowerPoint Presentation", ".pptx", "The standard PowerPoint format, with every slide.",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation", IsNative: true);

    public static readonly OfficeFileFormat Notebook = new(
        "notebook", "Notebook", ".json", "The notebook's own format.",
        "application/json", IsNative: true);

    public static readonly OfficeFileFormat Pdf = new(
        "pdf", "PDF", ".pdf", "Fixed layout that looks the same everywhere and cannot be edited easily.",
        "application/pdf");

    public static readonly OfficeFileFormat PlainText = new(
        "txt", "Plain Text", ".txt", "Just the words, with no formatting.",
        "text/plain");

    public static readonly OfficeFileFormat Html = new(
        "html", "Web Page", ".html", "A page that opens in any browser.",
        "text/html");

    public static readonly OfficeFileFormat Csv = new(
        "csv", "CSV (Comma delimited)", ".csv", "The current sheet's values, one row per line.",
        "text/csv");

    public static readonly OfficeFileFormat Png = new(
        "png", "PNG Image", ".png", "The current slide as a picture.",
        "image/png");

    public static readonly OfficeFileFormat Jpeg = new(
        "jpg", "JPEG Image", ".jpg", "The current slide as a compressed picture.",
        "image/jpeg");


    /// <summary>Save As: the native format first, then the ones that still carry the content.</summary>
    public static IReadOnlyList<OfficeFileFormat> SaveAsFor(OfficeApp app) => app switch
    {
        OfficeApp.Excel => [Xlsx, Csv, Pdf],
        OfficeApp.PowerPoint => [Pptx, Pdf, Png],
        OfficeApp.OneNote => [Notebook, Pdf],
        _ => [Docx, Pdf, PlainText, Html]
    };


    /// <summary>Export: everything except the native format.</summary>
    public static IReadOnlyList<OfficeFileFormat> ExportFor(OfficeApp app) => app switch
    {
        OfficeApp.Excel => [Pdf, Csv],
        OfficeApp.PowerPoint => [Pdf, Png, Jpeg],
        OfficeApp.OneNote => [Pdf],
        _ => [Pdf, PlainText, Html]
    };
}
