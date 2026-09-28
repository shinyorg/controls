using System.Globalization;
using Shiny.Controls.Office.Shell;

namespace Shiny.Controls.Office.Presentation;

/// <summary>
/// What the Office shell around a slide editor shows — the status bar's words, the view-mode ids, the
/// backstage's document info — worked out once here so both hosts say the same thing.
/// </summary>
public static class SlideShell
{
    /// <summary>"Slide 3 of 12", "Slide Master" in master view, "No slides" for an empty deck.</summary>
    /// <param name="index">The current slide, zero-based.</param>
    public static string SlideText(int index, int count, SlideEditorViewMode mode = SlideEditorViewMode.Normal)
    {
        if (mode == SlideEditorViewMode.SlideMaster)
            return "Slide Master";

        return count == 0 ? "No slides" : OfficeStatusText.Slide(index + 1, count);
    }


    /// <summary>
    /// The status bar's view-mode id — <see cref="OfficeViewModes.SlideNormal"/>,
    /// <see cref="OfficeViewModes.Sorter"/> or <see cref="OfficeViewModes.Reading"/> while the deck is
    /// being presented. Outline, Notes Page and Slide Master light up Normal, as PowerPoint's does.
    /// </summary>
    public static string ViewModeId(SlideEditorViewMode mode, bool presenting = false)
    {
        if (presenting)
            return OfficeViewModes.Reading.Id;

        return mode == SlideEditorViewMode.SlideSorter ? OfficeViewModes.Sorter.Id : OfficeViewModes.SlideNormal.Id;
    }


    /// <summary>
    /// The editor view a status bar id stands for, or null for Reading View — which is not an editor view
    /// but the show, played from the current slide.
    /// </summary>
    public static SlideEditorViewMode? ParseViewMode(string? id)
    {
        if (id == OfficeViewModes.Reading.Id)
            return null;

        return id == OfficeViewModes.Sorter.Id ? SlideEditorViewMode.SlideSorter : SlideEditorViewMode.Normal;
    }


    /// <summary>
    /// The words on the slides — every editable shape's text and every table cell, not the layout's
    /// "Click to add title" prompts. Speaker notes count only when asked.
    /// </summary>
    public static int WordCount(SlideDeck deck, bool includeNotes = false)
    {
        ArgumentNullException.ThrowIfNull(deck);

        var words = 0;
        foreach (var slide in deck.Slides)
        {
            foreach (var shape in slide.Shapes)
            {
                if (!shape.IsEditable)
                    continue;

                if (shape.Text is { } body)
                    words += Count(body);

                if (shape.Table is { } table)
                {
                    foreach (var row in table.Rows)
                    foreach (var cell in row)
                    {
                        if (cell.Text is { } text)
                            words += Count(text);
                    }
                }
            }

            if (includeNotes)
                words += CountWords(slide.Notes);
        }

        return words;
    }

    static int Count(Shiny.Controls.Office.Text.ShapeTextBody body)
    {
        var words = 0;
        foreach (var paragraph in body.Paragraphs)
            words += CountWords(paragraph.PlainText);

        return words;
    }

    /// <summary>Whitespace-separated runs, the way the Info page and Word's own count see words.</summary>
    public static int CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var words = 0;
        var inWord = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                words++;
            }
        }

        return words;
    }


    /// <summary>The backstage's Info page: name, author, and the deck's statistics.</summary>
    public static OfficeDocumentInfo DocumentInfo(SlideDeck deck, string? name, string? author = null, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(deck);
        culture ??= CultureInfo.CurrentCulture;

        var hidden = deck.Slides.Count(x => x.IsHidden);
        var notes = deck.Slides.Count(x => !string.IsNullOrWhiteSpace(x.Notes));

        return new OfficeDocumentInfo
        {
            Title = name,
            Author = author,
            Location = deck.Path,
            Statistics =
            [
                new("Slides", deck.Slides.Count.ToString("N0", culture)),
                new("Hidden slides", hidden.ToString("N0", culture)),
                new("Words", WordCount(deck).ToString("N0", culture)),
                new("Slides with notes", notes.ToString("N0", culture))
            ]
        };
    }


    /// <summary>
    /// The title bar's search, when no command matched: finds the text across every slide and selects
    /// the next hit. False when there is none.
    /// </summary>
    public static bool Search(SlideEditorController controller, string? text)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var find = controller.Find;
        find.Query = text.Trim();
        return find.Count > 0 && find.FindNext();
    }


    /// <summary>
    /// The shortcut text left inside a tooltip — "Bold (Ctrl+B)" — split into the tooltip ("Bold") and
    /// the keys ("Ctrl+B"), so the ribbon and the command search can show the keys on their own.
    /// Null when the tooltip carries no shortcut.
    /// </summary>
    public static (string Tooltip, string Shortcut)? SplitShortcut(string? tooltip)
    {
        if (string.IsNullOrWhiteSpace(tooltip))
            return null;

        var match = ShortcutInTooltip.Match(tooltip);
        return match.Success
            ? (match.Groups["name"].Value + match.Groups["rest"].Value, match.Groups["keys"].Value)
            : null;
    }

    static readonly System.Text.RegularExpressions.Regex ShortcutInTooltip = new(
        @"^(?<name>.+?) \((?<keys>(?:Ctrl|Shift|Alt|Cmd|Esc|F\d{1,2})[^()]*)\)(?<rest>.*)$",
        System.Text.RegularExpressions.RegexOptions.Compiled);
}
