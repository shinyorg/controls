using System.Globalization;
using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Shell;

/// <summary>
/// Translates between the Word editor's controller and the Office shell's parts — headings, styles,
/// search results, ruler geometry, status text, document info and view modes.
/// </summary>
/// <remarks>
/// <para>
/// Both hosts' <c>DocumentEditorView</c> dress themselves in the shell through this one class, so the
/// MAUI and Blazor chrome cannot disagree on what a heading's id is, which view mode is which, or how a
/// ruler drag becomes an indent.
/// </para>
/// <para>
/// Units: the controller measures in pixels at 96 dpi; the ruler measures in points. The conversions
/// here are the only place the two meet.
/// </para>
/// </remarks>
public static class WordShell
{
    /// <summary>Points per controller pixel (72 / 96).</summary>
    public const double PointsPerPixel = 72d / 96d;

    public static double ToPoints(double pixels) => pixels * PointsPerPixel;

    public static double ToPixels(double points) => points / PointsPerPixel;

    // ---- navigation ----

    /// <summary>The document's headings as the navigation pane lists them. The id is the paragraph index.</summary>
    public static IReadOnlyList<OfficeHeading> Headings(DocumentEditorController? controller)
    {
        if (controller is null)
            return [];

        return controller.Headings()
            .Select(x => new OfficeHeading(x.Paragraph.ToString(CultureInfo.InvariantCulture), x.Level, x.Text))
            .ToList();
    }

    /// <summary>The heading whose section holds the caret — the last one at or above it.</summary>
    public static string? CurrentHeadingId(DocumentEditorController? controller, IReadOnlyList<OfficeHeading> headings)
    {
        if (controller is null || headings.Count == 0)
            return null;

        var caret = controller.Selection.Focus.Block;
        return OfficeHeadingTree.Current(headings, x => ParagraphOf(x) is { } p && p <= caret)?.Id;
    }

    /// <summary>The paragraph a heading id names, or null for one this class did not produce.</summary>
    public static int? ParagraphOf(OfficeHeading heading)
        => int.TryParse(heading.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var paragraph) ? paragraph : null;

    /// <summary>Puts the caret on a heading. False when the heading is not this document's.</summary>
    public static bool GoTo(DocumentEditorController? controller, OfficeHeading heading)
    {
        if (controller is null || ParagraphOf(heading) is not { } paragraph || paragraph >= controller.Document.Paragraphs.Count)
            return false;

        controller.GoToParagraph(paragraph);
        return true;
    }

    /// <summary>
    /// Every hit for <paramref name="query"/> with a little text either side, for the navigation pane's
    /// Results tab. Also points the editor's finder at the query, so the hits are washed on the page.
    /// </summary>
    public static IReadOnlyList<OfficeSearchResult> Search(DocumentEditorController? controller, string? query, int max = 500, int context = 28)
    {
        if (controller is null)
            return [];

        query ??= string.Empty;
        controller.Find.Query = query;

        if (query.Length == 0)
            return [];

        var results = new List<OfficeSearchResult>();
        var paragraphs = controller.Document.Paragraphs;

        for (var block = 0; block < paragraphs.Count && results.Count < max; block++)
        {
            var text = paragraphs[block].PlainText;

            foreach (var match in TextSearch.Matches(text, query, controller.Find.Options))
            {
                var beforeStart = Math.Max(0, match.Start - context);
                var afterEnd = Math.Min(text.Length, match.Start + match.Length + context);

                results.Add(new OfficeSearchResult(
                    string.Create(CultureInfo.InvariantCulture, $"{block}:{match.Start}:{match.Length}"),
                    (beforeStart > 0 ? "…" : string.Empty) + Clean(text[beforeStart..match.Start]),
                    Clean(text.Substring(match.Start, match.Length)),
                    Clean(text[(match.Start + match.Length)..afterEnd]) + (afterEnd < text.Length ? "…" : string.Empty)));

                if (results.Count >= max)
                    break;
            }
        }

        return results;
    }

    /// <summary>Selects a search result on the page. False for a result this class did not produce.</summary>
    public static bool GoTo(DocumentEditorController? controller, OfficeSearchResult result)
    {
        if (controller is null)
            return false;

        var parts = result.Id.Split(':');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var block) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var start) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) ||
            block >= controller.Document.Paragraphs.Count)
        {
            return false;
        }

        controller.SelectFindMatch(new DocumentFindMatch(block, start, length));
        return true;
    }

    static string Clean(string text) => text.Replace("￼", string.Empty).Replace('\t', ' ');

    // ---- styles ----

    /// <summary>The controller's style set as gallery tiles, each previewed in its own formatting.</summary>
    public static IReadOnlyList<OfficeStyleDescriptor> Styles(DocumentEditorController? controller)
    {
        if (controller is null)
            return OfficeStyleDescriptors.Word;

        return controller.AvailableStyles
            .Select(x => new OfficeStyleDescriptor(x.Id, x.Name)
            {
                FontFamily = x.FontFamily,
                FontSize = x.FontSize,
                Bold = x.Bold,
                Italic = x.Italic,
                Color = x.Color,
                OutlineLevel = x.OutlineLevel
            })
            .ToList();
    }

    // ---- ruler ----

    /// <summary>The caret paragraph's indents in points, as the ruler takes them.</summary>
    public static OfficeIndents Indents(CaretFormat format)
        => new(ToPoints(format.IndentLeft), ToPoints(format.IndentFirstLine), ToPoints(format.IndentRight));

    /// <summary>Applies a ruler's indents (points) to the selected paragraphs. No-op when nothing moved.</summary>
    public static bool ApplyIndents(DocumentEditorController? controller, OfficeIndents indents)
    {
        if (controller is null)
            return false;

        var current = Indents(controller.CaretFormat);
        if (Near(current.Left, indents.Left) && Near(current.FirstLine, indents.FirstLine) && Near(current.Right, indents.Right))
            return false;

        controller.SetIndents(ToPixels(indents.Left), ToPixels(indents.Right), ToPixels(indents.FirstLine));
        return true;
    }

    /// <summary>The caret paragraph's tab stops in points from the left margin.</summary>
    public static IReadOnlyList<OfficeTabStop> TabStops(DocumentEditorController? controller)
        => controller is null
            ? []
            : controller.CurrentTabStops.Select(x => new OfficeTabStop(ToPoints(x.Position), (OfficeTabAlignment)(int)x.Alignment)).ToList();

    /// <summary>Applies a ruler's tab stops to the selected paragraphs. No-op when nothing changed.</summary>
    public static bool ApplyTabStops(DocumentEditorController? controller, IReadOnlyList<OfficeTabStop>? stops)
    {
        if (controller is null)
            return false;

        stops ??= [];
        var current = TabStops(controller);
        if (current.Count == stops.Count && current.Zip(stops.OrderBy(x => x.Position)).All(p => Near(p.First.Position, p.Second.Position) && p.First.Alignment == p.Second.Alignment))
            return false;

        controller.SetTabStops(stops.Select(x => new DocumentTabStop(ToPixels(x.Position), (DocumentTabAlignment)(int)x.Alignment)).ToList());
        return true;
    }

    /// <summary>Applies a ruler's margin drag (points) to the section. No-op when nothing moved.</summary>
    public static bool ApplyMargins(DocumentEditorController? controller, double leftPoints, double rightPoints)
    {
        if (controller is null)
            return false;

        var margins = controller.PageMargins;
        if (Near(ToPoints(margins.Left), leftPoints) && Near(ToPoints(margins.Right), rightPoints))
            return false;

        controller.SetPageMargins(ToPixels(leftPoints), margins.Top, ToPixels(rightPoints), margins.Bottom);
        return true;
    }

    static bool Near(double a, double b) => Math.Abs(a - b) < 0.05;

    // ---- status bar ----

    /// <summary>"Page 2 of 5".</summary>
    public static string PageText(DocumentStatistics statistics) => OfficeStatusText.Page(statistics.CurrentPage, statistics.Pages);

    /// <summary>"197 words", or "12 of 197 words" with a selection.</summary>
    public static string WordsText(DocumentStatistics statistics)
        => OfficeStatusText.Words(statistics.Words, statistics.SelectedWords > 0 ? statistics.SelectedWords : null);

    /// <summary>The proofing language to show, from the current culture — "English (United States)".</summary>
    public static string LanguageText(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentUICulture;
        if (culture.Name.Length == 0)
            culture = CultureInfo.GetCultureInfo("en-US");

        return culture.EnglishName;
    }

    // ---- view modes ----

    /// <summary>The status bar's view mode id for the editor's state.</summary>
    public static string ViewModeId(bool readMode, DocumentPageLayout layout)
        => readMode ? OfficeViewModes.Read.Id
            : layout == DocumentPageLayout.Print ? OfficeViewModes.Print.Id
            : OfficeViewModes.Web.Id;

    /// <summary>What a view mode id means for the editor: read mode, print layout, or one continuous column.</summary>
    public static (bool ReadMode, DocumentPageLayout Layout) FromViewModeId(string? id, DocumentPageLayout current)
    {
        if (id == OfficeViewModes.Read.Id)
            return (true, current);

        if (id == OfficeViewModes.Web.Id)
            return (false, DocumentPageLayout.Reflow);

        return (false, DocumentPageLayout.Print);
    }

    // ---- backstage ----

    /// <summary>The Info page's properties and statistics.</summary>
    public static OfficeDocumentInfo DocumentInfo(
        DocumentEditorController? controller,
        string? title,
        string? location = null,
        DateTimeOffset? created = null,
        DateTimeOffset? modified = null,
        long? sizeBytes = null,
        CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var s = controller?.Statistics ?? DocumentStatistics.Empty;

        return new OfficeDocumentInfo
        {
            Title = title,
            Author = controller?.Author,
            Location = location,
            Created = created,
            Modified = modified,
            SizeBytes = sizeBytes,
            Statistics =
            [
                new("Pages", s.Pages.ToString("N0", culture)),
                new("Words", s.Words.ToString("N0", culture)),
                new("Characters", s.CharactersWithSpaces.ToString("N0", culture)),
                new("Paragraphs", s.Paragraphs.ToString("N0", culture)),
                new("Lines", s.Lines.ToString("N0", culture)),
                new("Comments", (controller?.Comments.Count ?? 0).ToString("N0", culture))
            ]
        };
    }

    /// <summary>A comment's date as the comments pane shows it.</summary>
    public static string CommentDate(DocumentComment comment, DateTimeOffset now, CultureInfo? culture = null)
        => comment.Date is { } date ? OfficeBackstageText.Relative(new DateTimeOffset(date), now, culture) : string.Empty;
}
