using Shiny.Controls.Office.Shell;
using Shiny.Controls.Office.Skia;

namespace Shiny.Blazor.Controls.Office;

/// <summary>
/// The built-in Word and Excel templates with pictures, drawn once per app the first time a backstage
/// opens - never at start-up. Synchronous, as PowerPoint's are: WebAssembly has one thread to draw on.
/// </summary>
static class TemplateThumbnailCache
{
    static IReadOnlyList<OfficeTemplate>? word;
    static IReadOnlyList<OfficeTemplate>? spreadsheet;

    /// <summary>The Word list with pictures, or null until <see cref="EnsureWord"/> has run.</summary>
    public static IReadOnlyList<OfficeTemplate>? Word => word;

    public static IReadOnlyList<OfficeTemplate>? Spreadsheet => spreadsheet;

    // OfficeTemplateThumbnails.With keeps a template's plain tile when its picture fails, so neither
    // throws; the list is only ever built once.
    public static void EnsureWord() => word ??= OfficeTemplateThumbnails.Word();

    public static void EnsureSpreadsheet() => spreadsheet ??= OfficeTemplateThumbnails.Spreadsheet();
}
