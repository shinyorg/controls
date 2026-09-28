using Shiny.Controls.Office.Spreadsheet;

namespace Shiny.Controls.Office.Shell;

/// <summary>
/// One entry in the style gallery — Word's "AaBbCcDd" swatches, Excel's cell styles, PowerPoint's
/// WordArt quick styles.
/// </summary>
/// <remarks>
/// A description of how the style <i>looks</i>, not the style itself: the gallery only has to draw a
/// preview in its font, size and colour and report which one was picked. Applying it is the editor's
/// job, keyed on <see cref="Id"/>.
/// </remarks>
public sealed record OfficeStyleDescriptor
{
    public OfficeStyleDescriptor(string id, string name)
    {
        this.Id = id;
        this.Name = name;
    }

    /// <summary>The key the editor applies — a style id in the document, "Heading1".</summary>
    public string Id { get; init; }

    /// <summary>The caption under the preview — "Heading 1".</summary>
    public string Name { get; init; }

    /// <summary>The text drawn in the style. Office uses "AaBbCcDd"; a heading uses "AaBbCc" to fit.</summary>
    public string Sample { get; init; } = "AaBbCcDd";

    public string? FontFamily { get; init; }

    /// <summary>The style's size in points. The gallery clamps what it draws — see <see cref="PreviewSize"/>.</summary>
    public double FontSize { get; init; } = 11;

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public bool Underline { get; init; }

    /// <summary>Text colour, or null for the theme's ink.</summary>
    public ArgbColor? Color { get; init; }

    /// <summary>Cell/shape fill for an Excel cell style or a WordArt swatch, or null for none.</summary>
    public ArgbColor? Background { get; init; }

    /// <summary>The outline level of a heading style (1–9), or 0 for body styles.</summary>
    public int OutlineLevel { get; init; }

    /// <summary>
    /// The point size a gallery preview draws at: the style's own, held between 8 and 18 so a Title's
    /// 28pt does not blow a 60px swatch apart and a 6pt footnote style is still legible.
    /// </summary>
    public double PreviewSize => Math.Clamp(this.FontSize, 8, 18);
}


/// <summary>Word's built-in quick styles, as a starting gallery an editor can replace.</summary>
public static class OfficeStyleDescriptors
{
    static readonly ArgbColor HeadingBlue = new(255, 0x0F, 0x47, 0x61);
    static readonly ArgbColor AccentBlue = new(255, 0x15, 0x60, 0x82);
    static readonly ArgbColor SubtleGrey = new(255, 0x59, 0x59, 0x59);
    static readonly ArgbColor MutedGrey = new(255, 0x40, 0x40, 0x40);

    /// <summary>
    /// Normal, No Spacing, Heading 1–3, Title, Subtitle, Subtle Emphasis, Emphasis, Intense Emphasis,
    /// Strong, Quote, Intense Quote, List Paragraph — the order Word's gallery shows them in.
    /// </summary>
    public static IReadOnlyList<OfficeStyleDescriptor> Word { get; } =
    [
        new("Normal", "Normal") { FontFamily = "Aptos", FontSize = 12 },
        new("NoSpacing", "No Spacing") { FontFamily = "Aptos", FontSize = 12 },
        new("Heading1", "Heading 1") { Sample = "AaBbCc", FontFamily = "Aptos Display", FontSize = 20, Color = HeadingBlue, OutlineLevel = 1 },
        new("Heading2", "Heading 2") { Sample = "AaBbCcD", FontFamily = "Aptos Display", FontSize = 16, Color = HeadingBlue, OutlineLevel = 2 },
        new("Heading3", "Heading 3") { Sample = "AaBbCcDd", FontFamily = "Aptos", FontSize = 14, Color = HeadingBlue, OutlineLevel = 3 },
        new("Title", "Title") { Sample = "AaB", FontFamily = "Aptos Display", FontSize = 28 },
        new("Subtitle", "Subtitle") { Sample = "AaBbCcD", FontFamily = "Aptos", FontSize = 14, Color = SubtleGrey },
        new("SubtleEmphasis", "Subtle Em...") { FontFamily = "Aptos", FontSize = 12, Italic = true, Color = MutedGrey },
        new("Emphasis", "Emphasis") { FontFamily = "Aptos", FontSize = 12, Italic = true },
        new("IntenseEmphasis", "Intense E...") { FontFamily = "Aptos", FontSize = 12, Italic = true, Color = AccentBlue },
        new("Strong", "Strong") { FontFamily = "Aptos", FontSize = 12, Bold = true },
        new("Quote", "Quote") { FontFamily = "Aptos", FontSize = 12, Italic = true, Color = MutedGrey },
        new("IntenseQuote", "Intense Q...") { FontFamily = "Aptos", FontSize = 12, Italic = true, Color = AccentBlue },
        new("ListParagraph", "List Para...") { FontFamily = "Aptos", FontSize = 12 }
    ];


    /// <summary>The index of the style with <paramref name="id"/>, or -1.</summary>
    public static int IndexOf(IReadOnlyList<OfficeStyleDescriptor> styles, string? id)
    {
        if (id is null)
            return -1;

        for (var i = 0; i < styles.Count; i++)
        {
            if (string.Equals(styles[i].Id, id, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }


    /// <summary>
    /// The first index the in-ribbon strip should show so that <paramref name="selected"/> is visible,
    /// given it shows <paramref name="visible"/> at a time starting at <paramref name="first"/>.
    /// </summary>
    public static int ScrollIntoView(int first, int selected, int visible, int count)
    {
        if (count <= visible || visible <= 0)
            return 0;

        var maxFirst = count - visible;
        if (selected < 0)
            return Math.Clamp(first, 0, maxFirst);

        if (selected < first)
            return selected;

        if (selected >= first + visible)
            return Math.Min(selected - visible + 1, maxFirst);

        return Math.Clamp(first, 0, maxFirst);
    }
}
