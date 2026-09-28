using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Shell;
using Shiny.Controls.Office.Skia;
using Shiny.Controls.Office.Spreadsheet.View;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// The built-in Word and Excel templates with pictures, drawn once per process off the UI thread the
/// first time a backstage opens. Until it lands (and for good, if it fails) the plain list shows.
/// </summary>
static class TemplateThumbnailCache
{
    static Task<IReadOnlyList<OfficeTemplate>>? word;
    static Task<IReadOnlyList<OfficeTemplate>>? spreadsheet;

    public static Task<IReadOnlyList<OfficeTemplate>> Word
        => word ??= Render(() => OfficeTemplateThumbnails.Word(), WordTemplates.All);

    public static Task<IReadOnlyList<OfficeTemplate>> Spreadsheet
        => spreadsheet ??= Render(() => OfficeTemplateThumbnails.Spreadsheet(), SpreadsheetTemplates.All);

    /// <summary>The finished Word list, or null while it is still drawing (or was never asked for).</summary>
    public static IReadOnlyList<OfficeTemplate>? WordReady => Ready(word);

    public static IReadOnlyList<OfficeTemplate>? SpreadsheetReady => Ready(spreadsheet);

    static IReadOnlyList<OfficeTemplate>? Ready(Task<IReadOnlyList<OfficeTemplate>>? task)
        => task is { IsCompletedSuccessfully: true } ? task.Result : null;

    static async Task<IReadOnlyList<OfficeTemplate>> Render(Func<IReadOnlyList<OfficeTemplate>> render, IReadOnlyList<OfficeTemplate> fallback)
    {
        try
        {
            return await Task.Run(render).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Shiny.Office] Template thumbnails failed: {ex.Message}");
            return fallback;
        }
    }
}
