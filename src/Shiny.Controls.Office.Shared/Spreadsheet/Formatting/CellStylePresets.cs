namespace Shiny.Controls.Office.Spreadsheet;

/// <summary>One entry in the Cell Styles gallery.</summary>
/// <param name="Name">Excel's name for it.</param>
/// <param name="Group">The gallery section it sits under.</param>
/// <param name="Change">What applying it does to a cell.</param>
public sealed record CellStylePreset(string Name, string Group, CellFormatChange Change)
{
    /// <summary>How the gallery tile previews it — the format the style produces on a default cell.</summary>
    public ResolvedFormat Preview => this.Change.ApplyTo(ResolvedFormat.Default);
}

/// <summary>
/// Excel's built-in cell styles.
/// </summary>
/// <remarks>
/// Applied as direct formatting rather than as named styles in <c>cellStyleXfs</c>: the cell ends up
/// looking exactly as Excel's style would make it, and the file stays one Excel opens cleanly, but the
/// cell does not remember that it was "Good" — restyling the workbook's Good style later will not
/// reach it. Every property a style defines is set, including the ones it sets back to plain, so
/// applying Heading 1 over Title really does replace it rather than layering over it.
/// </remarks>
public static class CellStylePresets
{
    static ArgbColor C(uint argb) => ArgbColor.FromUInt32(argb);

    static CellFormatChange Plain(
        ArgbColor? fill = null,
        ArgbColor? ink = null,
        bool bold = false,
        bool italic = false,
        double size = 11,
        string font = "Calibri",
        BorderEdge? all = null,
        BorderEdge? top = null,
        BorderEdge? bottom = null) => new()
        {
            Background = fill ?? ArgbColor.Transparent,
            Foreground = ink ?? ResolvedFormat.Default.Foreground,
            Bold = bold,
            Italic = italic,
            Underline = false,
            Strike = false,
            FontSize = size,
            FontName = font,
            BorderLeft = all ?? BorderEdge.None,
            BorderRight = all ?? BorderEdge.None,
            BorderTop = top ?? all ?? BorderEdge.None,
            BorderBottom = bottom ?? all ?? BorderEdge.None
        };

    public const string GoodBadNeutral = "Good, Bad and Neutral";
    public const string DataAndModel = "Data and Model";
    public const string TitlesAndHeadings = "Titles and Headings";
    public const string Themed = "Themed Cell Styles";

    public static IReadOnlyList<CellStylePreset> All { get; } =
    [
        new("Normal", GoodBadNeutral, CellFormatChange.Clear),
        new("Bad", GoodBadNeutral, Plain(C(0xFFFFC7CE), C(0xFF9C0006))),
        new("Good", GoodBadNeutral, Plain(C(0xFFC6EFCE), C(0xFF006100))),
        new("Neutral", GoodBadNeutral, Plain(C(0xFFFFEB9C), C(0xFF9C5700))),

        new("Calculation", DataAndModel, Plain(C(0xFFF2F2F2), C(0xFFFA7D00), bold: true, all: BorderEdge.Thin(C(0xFF7F7F7F)))),
        new("Check Cell", DataAndModel, Plain(C(0xFFA5A5A5), C(0xFFFFFFFF), bold: true, all: new BorderEdge(CellBorderStyle.Double, C(0xFF3F3F3F)))),
        new("Explanatory Text", DataAndModel, Plain(ink: C(0xFF7F7F7F), italic: true)),
        new("Input", DataAndModel, Plain(C(0xFFFFCC99), C(0xFF3F3F76), all: BorderEdge.Thin(C(0xFF7F7F7F)))),
        new("Linked Cell", DataAndModel, Plain(ink: C(0xFFFA7D00), bottom: new BorderEdge(CellBorderStyle.Double, C(0xFFFF8001)))),
        new("Note", DataAndModel, Plain(C(0xFFFFFFCC), all: BorderEdge.Thin(C(0xFFB2B2B2)))),
        new("Output", DataAndModel, Plain(C(0xFFF2F2F2), C(0xFF3F3F3F), bold: true, all: BorderEdge.Thin(C(0xFF3F3F3F)))),
        new("Warning Text", DataAndModel, Plain(ink: C(0xFFFF0000))),

        new("Title", TitlesAndHeadings, Plain(ink: C(0xFF44546A), size: 18, font: "Calibri Light")),
        new("Heading 1", TitlesAndHeadings, Plain(ink: C(0xFF44546A), bold: true, size: 15, bottom: new BorderEdge(CellBorderStyle.Thick, C(0xFF4472C4)))),
        new("Heading 2", TitlesAndHeadings, Plain(ink: C(0xFF44546A), bold: true, size: 13, bottom: new BorderEdge(CellBorderStyle.Thick, C(0xFFA2B8E1)))),
        new("Heading 3", TitlesAndHeadings, Plain(ink: C(0xFF44546A), bold: true, bottom: new BorderEdge(CellBorderStyle.Medium, C(0xFF8EA9DB)))),
        new("Heading 4", TitlesAndHeadings, Plain(ink: C(0xFF44546A), bold: true)),
        new("Total", TitlesAndHeadings, Plain(bold: true, top: BorderEdge.Thin(C(0xFF4472C4)), bottom: new BorderEdge(CellBorderStyle.Double, C(0xFF4472C4)))),

        new("Accent1", Themed, Plain(C(0xFF4472C4), C(0xFFFFFFFF))),
        new("Accent2", Themed, Plain(C(0xFFED7D31), C(0xFFFFFFFF))),
        new("Accent3", Themed, Plain(C(0xFFA5A5A5), C(0xFFFFFFFF))),
        new("Accent4", Themed, Plain(C(0xFFFFC000), C(0xFFFFFFFF))),
        new("Accent5", Themed, Plain(C(0xFF5B9BD5), C(0xFFFFFFFF))),
        new("Accent6", Themed, Plain(C(0xFF70AD47), C(0xFFFFFFFF))),
        new("20% - Accent1", Themed, Plain(C(0xFFD9E1F2))),
        new("20% - Accent2", Themed, Plain(C(0xFFFCE4D6))),
        new("20% - Accent6", Themed, Plain(C(0xFFE2EFDA)))
    ];

    public static CellStylePreset? Find(string name)
        => All.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
}
