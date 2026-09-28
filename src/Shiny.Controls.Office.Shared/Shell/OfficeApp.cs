using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Theming;

namespace Shiny.Controls.Office.Shell;

/// <summary>Which Office application an <c>OfficeShell</c> is dressed as.</summary>
/// <remarks>
/// The shell's structure is the same for all of them — title bar, ribbon, status bar, backstage. What
/// the app changes is the accent, the default document name, the native
/// file format and the three view-mode buttons on the status bar.
/// </remarks>
public enum OfficeApp
{
    /// <summary>The document editor. Word blue.</summary>
    Word,

    /// <summary>The spreadsheet editor. Excel green.</summary>
    Excel,

    /// <summary>The slide editor. PowerPoint red.</summary>
    PowerPoint,

    /// <summary>The notebook canvas. OneNote purple.</summary>
    OneNote
}


/// <summary>What an <see cref="OfficeApp"/> looks like in the shell's chrome.</summary>
/// <param name="App">The app described.</param>
/// <param name="Name">The product name, e.g. "Word".</param>
/// <param name="Letter">The app's single-letter mark (W / X / P). The title bar no longer draws a tile; kept for hosts that show their own.</param>
/// <param name="Accent">The accent the title bar, backstage rail and ribbon header wear.</param>
/// <param name="DefaultDocumentName">What an unsaved document is called — "Document1", "Book1".</param>
/// <param name="DocumentNoun">What the app calls the thing it edits — "document", "workbook".</param>
/// <param name="NativeFormat">The format Save writes.</param>
public sealed record OfficeAppInfo(
    OfficeApp App,
    string Name,
    string Letter,
    OfficeAccent Accent,
    string DefaultDocumentName,
    string DocumentNoun,
    OfficeFileFormat NativeFormat)
{
    public static readonly OfficeAppInfo Word = new(
        OfficeApp.Word, "Word", "W", OfficeAccent.Document, "Document1", "document", OfficeFileFormats.Docx);

    public static readonly OfficeAppInfo Excel = new(
        OfficeApp.Excel, "Excel", "X", OfficeAccent.Spreadsheet, "Book1", "workbook", OfficeFileFormats.Xlsx);

    public static readonly OfficeAppInfo PowerPoint = new(
        OfficeApp.PowerPoint, "PowerPoint", "P", OfficeAccent.Presentation, "Presentation1", "presentation", OfficeFileFormats.Pptx);

    public static readonly OfficeAppInfo OneNote = new(
        OfficeApp.OneNote, "OneNote", "N", OfficeAccent.Notebook, "Notebook1", "notebook", OfficeFileFormats.Notebook);


    /// <summary>The descriptor for an app.</summary>
    public static OfficeAppInfo For(OfficeApp app) => app switch
    {
        OfficeApp.Excel => Excel,
        OfficeApp.PowerPoint => PowerPoint,
        OfficeApp.OneNote => OneNote,
        _ => Word
    };


    /// <summary>The accent colour as <c>#RRGGBB</c>, for hosts that take CSS or hex.</summary>
    public string AccentHex => OfficeColorText.Hex(this.Accent.Color);

    /// <summary>The ink on the accent as <c>#RRGGBB</c>.</summary>
    public string AccentInkHex => OfficeColorText.Hex(this.Accent.Ink);

    /// <summary>
    /// A deeper shade of the accent — the backstage rail's selected entry and the title bar's pressed
    /// state, which have to read as the accent while still standing apart from it.
    /// </summary>
    public ArgbColor AccentDark => OfficeColorText.Shade(this.Accent.Color, 0.78);

    /// <summary><see cref="AccentDark"/> as <c>#RRGGBB</c>.</summary>
    public string AccentDarkHex => OfficeColorText.Hex(this.AccentDark);

    /// <summary>The formats offered by Save As, native first.</summary>
    public IReadOnlyList<OfficeFileFormat> SaveAsFormats => OfficeFileFormats.SaveAsFor(this.App);

    /// <summary>The formats offered by Export — the ones that do not round-trip.</summary>
    public IReadOnlyList<OfficeFileFormat> ExportFormats => OfficeFileFormats.ExportFor(this.App);

    /// <summary>The three view-mode buttons the status bar offers for this app.</summary>
    public IReadOnlyList<OfficeViewMode> ViewModes => OfficeViewModes.For(this.App);
}


/// <summary>Colour helpers the shell uses to turn the host-agnostic colour into CSS / a MAUI colour.</summary>
public static class OfficeColorText
{
    /// <summary><c>#RRGGBB</c>, alpha dropped.</summary>
    public static string Hex(ArgbColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>Scales each channel by <paramref name="factor"/> — below 1 darkens, above 1 lightens towards white.</summary>
    public static ArgbColor Shade(ArgbColor color, double factor)
    {
        byte Channel(byte c)
            => factor <= 1
                ? (byte)Math.Clamp(Math.Round(c * factor), 0, 255)
                : (byte)Math.Clamp(Math.Round(c + ((255 - c) * (factor - 1))), 0, 255);

        return new ArgbColor(color.A, Channel(color.R), Channel(color.G), Channel(color.B));
    }

    /// <summary>
    /// The initials for a person's name — "Allan Ritchie" is "AR", "cher" is "C". What the avatar in the
    /// title bar shows when there is no picture.
    /// </summary>
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "?";

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1)
            return parts[0][..1].ToUpperInvariant();

        return string.Concat(parts[0][..1], parts[^1][..1]).ToUpperInvariant();
    }
}
