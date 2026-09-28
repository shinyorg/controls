namespace Shiny.Controls.Office.Icons;

/// <summary>
/// The Word ribbon's own icons — the Clipboard, Font, Paragraph, Styles, Insert, References and Review
/// commands the other two bars have no use for.
/// </summary>
/// <remarks>
/// <para>
/// Typed as <see cref="OfficeIcon"/> so every host API that takes an icon takes these, but numbered from
/// 1000 rather than added to the enum. The spreadsheet and slide bars grow their own sets at the same
/// time, and three sets of additions to the end of one enum is a merge conflict every time; values out
/// here cannot collide with anything added to it.
/// </para>
/// <para>
/// Same grid, same weight, stroked throughout, like the rest of the set — see <see cref="OfficeIcons"/>.
/// </para>
/// </remarks>
public static class WordIcons
{
    public const OfficeIcon FormatPainter = (OfficeIcon)1000;
    public const OfficeIcon GrowFont = (OfficeIcon)1001;
    public const OfficeIcon ShrinkFont = (OfficeIcon)1002;
    public const OfficeIcon ChangeCase = (OfficeIcon)1003;
    public const OfficeIcon Superscript = (OfficeIcon)1004;
    public const OfficeIcon Subscript = (OfficeIcon)1005;
    public const OfficeIcon LineSpacing = (OfficeIcon)1006;
    public const OfficeIcon Borders = (OfficeIcon)1007;
    public const OfficeIcon ShowMarks = (OfficeIcon)1008;
    public const OfficeIcon MultilevelList = (OfficeIcon)1009;
    public const OfficeIcon Styles = (OfficeIcon)1010;
    public const OfficeIcon Replace = (OfficeIcon)1011;
    public const OfficeIcon Hyperlink = (OfficeIcon)1012;
    public const OfficeIcon Bookmark = (OfficeIcon)1013;
    public const OfficeIcon DateTime = (OfficeIcon)1014;
    public const OfficeIcon Symbol = (OfficeIcon)1015;
    public const OfficeIcon HorizontalLine = (OfficeIcon)1016;
    public const OfficeIcon BlankPage = (OfficeIcon)1017;
    public const OfficeIcon SectionBreak = (OfficeIcon)1018;
    public const OfficeIcon Columns = (OfficeIcon)1019;
    public const OfficeIcon PageSize = (OfficeIcon)1020;
    public const OfficeIcon PageColor = (OfficeIcon)1021;
    public const OfficeIcon TableOfContents = (OfficeIcon)1022;
    public const OfficeIcon Footnote = (OfficeIcon)1023;
    public const OfficeIcon Comment = (OfficeIcon)1024;
    public const OfficeIcon DeleteComment = (OfficeIcon)1025;
    public const OfficeIcon TrackChanges = (OfficeIcon)1026;
    public const OfficeIcon Accept = (OfficeIcon)1027;
    public const OfficeIcon Reject = (OfficeIcon)1028;
    public const OfficeIcon WordCount = (OfficeIcon)1029;
    public const OfficeIcon ReadMode = (OfficeIcon)1030;
    public const OfficeIcon NavigationPane = (OfficeIcon)1031;
    public const OfficeIcon MergeCells = (OfficeIcon)1032;
    public const OfficeIcon SplitCells = (OfficeIcon)1033;
    public const OfficeIcon DeleteTable = (OfficeIcon)1034;
    public const OfficeIcon SelectAll = (OfficeIcon)1035;
    public const OfficeIcon UpdateTable = (OfficeIcon)1036;
    public const OfficeIcon RemoveLink = (OfficeIcon)1037;

    /// <summary>Every icon in this set, for guards that walk the whole of it.</summary>
    public static IReadOnlyList<OfficeIcon> All { get; } =
        [.. Enumerable.Range(1000, 38).Select(x => (OfficeIcon)x)];

    /// <summary>The figures for one of these icons, or null for any other value.</summary>
    internal static IReadOnlyList<OfficeIconShape>? Shapes(OfficeIcon icon) => icon switch
    {
        FormatPainter =>
        [
            OfficeIconShape.Rectangle(3.5f, 3f, 13f, 6f, 1.5f),
            OfficeIconShape.Polyline(16.5f, 6f, 19.5f, 6f, 19.5f, 10.5f, 11f, 10.5f, 11f, 13f),
            OfficeIconShape.Rectangle(9.5f, 13f, 3f, 8f, 1f)
        ],

        GrowFont => [.. Letter(), OfficeIconShape.Line(18.5f, 15f, 18.5f, 5f), OfficeIconShape.Polyline(15.5f, 8f, 18.5f, 5f, 21.5f, 8f)],
        ShrinkFont => [.. Letter(), OfficeIconShape.Line(18.5f, 5f, 18.5f, 15f), OfficeIconShape.Polyline(15.5f, 12f, 18.5f, 15f, 21.5f, 12f)],

        ChangeCase =>
        [
            OfficeIconShape.Polyline(2.5f, 19f, 7f, 6f, 11.5f, 19f),
            OfficeIconShape.Line(4.3f, 14.5f, 9.7f, 14.5f),
            OfficeIconShape.Circle(16.5f, 15.5f, 3.2f),
            OfficeIconShape.Line(19.7f, 12f, 19.7f, 19f)
        ],

        Superscript =>
        [
            OfficeIconShape.Line(3f, 10f, 11f, 20f),
            OfficeIconShape.Line(11f, 10f, 3f, 20f),
            Two(15f, 3.5f)
        ],

        Subscript =>
        [
            OfficeIconShape.Line(3f, 4f, 11f, 14f),
            OfficeIconShape.Line(11f, 4f, 3f, 14f),
            Two(15f, 13.5f)
        ],

        LineSpacing =>
        [
            OfficeIconShape.Line(10f, 6f, 21f, 6f),
            OfficeIconShape.Line(10f, 12f, 21f, 12f),
            OfficeIconShape.Line(10f, 18f, 21f, 18f),
            OfficeIconShape.Line(5f, 4f, 5f, 20f),
            OfficeIconShape.Polyline(2.5f, 6.5f, 5f, 4f, 7.5f, 6.5f),
            OfficeIconShape.Polyline(2.5f, 17.5f, 5f, 20f, 7.5f, 17.5f)
        ],

        // A cell drawn in dashes with its bottom edge solid: the border the button adds.
        Borders =>
        [
            OfficeIconShape.Line(4f, 20f, 20f, 20f),
            OfficeIconShape.Line(4f, 4f, 6f, 4f),
            OfficeIconShape.Line(9f, 4f, 11f, 4f),
            OfficeIconShape.Line(13f, 4f, 15f, 4f),
            OfficeIconShape.Line(18f, 4f, 20f, 4f),
            OfficeIconShape.Line(4f, 8f, 4f, 10f),
            OfficeIconShape.Line(4f, 13f, 4f, 15f),
            OfficeIconShape.Line(20f, 8f, 20f, 10f),
            OfficeIconShape.Line(20f, 13f, 20f, 15f),
            OfficeIconShape.Line(10f, 12f, 14f, 12f)
        ],

        ShowMarks =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(17f, 4f),
                OfficeIconVertex.LineTo(9.5f, 4f),
                OfficeIconVertex.CurveTo(6f, 4f, 4.5f, 6.5f, 4.5f, 8.5f),
                OfficeIconVertex.CurveTo(4.5f, 10.5f, 6f, 13f, 9.5f, 13f),
                OfficeIconVertex.LineTo(11f, 13f)),
            OfficeIconShape.Line(11f, 4f, 11f, 20f),
            OfficeIconShape.Line(16f, 4f, 16f, 20f)
        ],

        MultilevelList =>
        [
            OfficeIconShape.Line(7f, 5f, 21f, 5f),
            OfficeIconShape.Line(10f, 10f, 21f, 10f),
            OfficeIconShape.Line(13f, 15f, 21f, 15f),
            OfficeIconShape.Line(10f, 20f, 21f, 20f),
            OfficeIconShape.Circle(3.5f, 5f, 0.9f),
            OfficeIconShape.Circle(6.5f, 10f, 0.9f),
            OfficeIconShape.Circle(9.5f, 15f, 0.9f),
            OfficeIconShape.Circle(6.5f, 20f, 0.9f)
        ],

        Styles =>
        [
            OfficeIconShape.Polyline(3f, 16f, 7.5f, 4f, 12f, 16f),
            OfficeIconShape.Line(4.7f, 11.5f, 10.3f, 11.5f),
            OfficeIconShape.Line(15f, 6f, 21f, 6f),
            OfficeIconShape.Line(15f, 11f, 21f, 11f),
            OfficeIconShape.Line(3f, 20.5f, 21f, 20.5f)
        ],

        Replace =>
        [
            OfficeIconShape.Rectangle(3f, 3f, 9f, 6.5f, 1f),
            OfficeIconShape.Rectangle(12.5f, 14.5f, 8.5f, 6.5f, 1f),
            OfficeIconShape.Polyline(7.5f, 9.5f, 7.5f, 17.75f, 10.5f, 17.75f),
            OfficeIconShape.Polyline(8.75f, 15.75f, 10.75f, 17.75f, 8.75f, 19.75f)
        ],

        // Two links of chain overlapping.
        Hyperlink =>
        [
            OfficeIconShape.Rectangle(2.5f, 8.5f, 11f, 7f, 3.5f),
            OfficeIconShape.Rectangle(10.5f, 8.5f, 11f, 7f, 3.5f)
        ],

        RemoveLink =>
        [
            OfficeIconShape.Rectangle(2.5f, 5.5f, 9f, 6f, 3f),
            OfficeIconShape.Rectangle(12.5f, 12.5f, 9f, 6f, 3f),
            OfficeIconShape.Line(4f, 20f, 8f, 16f),
            OfficeIconShape.Line(16f, 8f, 20f, 4f)
        ],

        Bookmark =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(6f, 3f),
                OfficeIconVertex.LineTo(18f, 3f),
                OfficeIconVertex.LineTo(18f, 21f),
                OfficeIconVertex.LineTo(12f, 16.5f),
                OfficeIconVertex.LineTo(6f, 21f),
                OfficeIconVertex.Close)
        ],

        DateTime =>
        [
            OfficeIconShape.Rectangle(3.5f, 5f, 17f, 15.5f, 2f),
            OfficeIconShape.Line(3.5f, 9.5f, 20.5f, 9.5f),
            OfficeIconShape.Line(8f, 3f, 8f, 7f),
            OfficeIconShape.Line(16f, 3f, 16f, 7f),
            OfficeIconShape.Circle(8f, 14f, 0.6f),
            OfficeIconShape.Circle(12f, 14f, 0.6f),
            OfficeIconShape.Circle(16f, 14f, 0.6f)
        ],

        // Omega: the one mark everyone reads as "special characters".
        Symbol =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(4f, 20f),
                OfficeIconVertex.LineTo(8.5f, 20f),
                OfficeIconVertex.LineTo(8.5f, 17.5f),
                OfficeIconVertex.CurveTo(5.5f, 16f, 4f, 13.5f, 4f, 10.5f),
                OfficeIconVertex.CurveTo(4f, 6.4f, 7.6f, 3.5f, 12f, 3.5f),
                OfficeIconVertex.CurveTo(16.4f, 3.5f, 20f, 6.4f, 20f, 10.5f),
                OfficeIconVertex.CurveTo(20f, 13.5f, 18.5f, 16f, 15.5f, 17.5f),
                OfficeIconVertex.LineTo(15.5f, 20f),
                OfficeIconVertex.LineTo(20f, 20f))
        ],

        HorizontalLine =>
        [
            OfficeIconShape.Line(3f, 6f, 13f, 6f),
            OfficeIconShape.Line(3f, 12f, 21f, 12f),
            OfficeIconShape.Line(3f, 18f, 13f, 18f)
        ],

        BlankPage =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(5f, 3f),
                OfficeIconVertex.LineTo(14f, 3f),
                OfficeIconVertex.LineTo(19f, 8f),
                OfficeIconVertex.LineTo(19f, 21f),
                OfficeIconVertex.LineTo(5f, 21f),
                OfficeIconVertex.Close),
            OfficeIconShape.Polyline(14f, 3f, 14f, 8f, 19f, 8f)
        ],

        SectionBreak =>
        [
            OfficeIconShape.Polyline(6f, 9f, 6f, 4f, 18f, 4f, 18f, 9f),
            OfficeIconShape.Line(4f, 11.25f, 20f, 11.25f),
            OfficeIconShape.Line(4f, 13.75f, 20f, 13.75f),
            OfficeIconShape.Polyline(6f, 16f, 6f, 20f, 18f, 20f, 18f, 16f)
        ],

        Columns =>
        [
            OfficeIconShape.Line(3f, 5f, 10f, 5f),
            OfficeIconShape.Line(3f, 10f, 10f, 10f),
            OfficeIconShape.Line(3f, 15f, 10f, 15f),
            OfficeIconShape.Line(3f, 20f, 8f, 20f),
            OfficeIconShape.Line(14f, 5f, 21f, 5f),
            OfficeIconShape.Line(14f, 10f, 21f, 10f),
            OfficeIconShape.Line(14f, 15f, 21f, 15f),
            OfficeIconShape.Line(14f, 20f, 19f, 20f)
        ],

        PageSize =>
        [
            OfficeIconShape.Rectangle(8f, 4f, 12f, 16f, 1f),
            OfficeIconShape.Line(3.5f, 5.5f, 3.5f, 18.5f),
            OfficeIconShape.Polyline(2f, 7f, 3.5f, 5.5f, 5f, 7f),
            OfficeIconShape.Polyline(2f, 17f, 3.5f, 18.5f, 5f, 17f)
        ],

        PageColor =>
        [
            OfficeIconShape.Rectangle(3.5f, 3f, 12f, 17f, 1f),
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(18.5f, 12.5f),
                OfficeIconVertex.CurveTo(18.5f, 12.5f, 21f, 15.5f, 21f, 17.5f),
                OfficeIconVertex.CurveTo(21f, 18.9f, 19.9f, 20f, 18.5f, 20f),
                OfficeIconVertex.CurveTo(17.1f, 20f, 16f, 18.9f, 16f, 17.5f),
                OfficeIconVertex.CurveTo(16f, 15.5f, 18.5f, 12.5f, 18.5f, 12.5f),
                OfficeIconVertex.Close)
        ],

        // Entries with their page numbers held off to the right.
        TableOfContents =>
        [
            OfficeIconShape.Line(3f, 5f, 13f, 5f),
            OfficeIconShape.Line(18f, 5f, 21f, 5f),
            OfficeIconShape.Line(6f, 10f, 13f, 10f),
            OfficeIconShape.Line(18f, 10f, 21f, 10f),
            OfficeIconShape.Line(6f, 15f, 13f, 15f),
            OfficeIconShape.Line(18f, 15f, 21f, 15f),
            OfficeIconShape.Line(3f, 20f, 13f, 20f),
            OfficeIconShape.Line(18f, 20f, 21f, 20f)
        ],

        UpdateTable =>
        [
            OfficeIconShape.Line(3f, 5f, 12f, 5f),
            OfficeIconShape.Line(3f, 10f, 10f, 10f),
            OfficeIconShape.Line(3f, 15f, 8f, 15f),
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(20.5f, 14f),
                OfficeIconVertex.CurveTo(20.5f, 17.6f, 18f, 20.5f, 15f, 20.5f),
                OfficeIconVertex.CurveTo(12f, 20.5f, 11f, 18.5f, 11f, 18.5f)),
            OfficeIconShape.Polyline(18.5f, 12f, 20.5f, 14f, 22.5f, 12f)
        ],

        Footnote =>
        [
            OfficeIconShape.Line(3f, 13f, 14f, 13f),
            OfficeIconShape.Line(3f, 17f, 14f, 17f),
            OfficeIconShape.Line(3f, 21f, 9f, 21f),
            OfficeIconShape.Polyline(16.5f, 6f, 18.5f, 4f, 18.5f, 11f),
            OfficeIconShape.Line(16.5f, 11f, 20.5f, 11f)
        ],

        Comment => Bubble(),
        DeleteComment => [.. Bubble(), OfficeIconShape.Line(9.5f, 8f, 14.5f, 13f), OfficeIconShape.Line(14.5f, 8f, 9.5f, 13f)],

        TrackChanges =>
        [
            OfficeIconShape.Polyline(14f, 20f, 4f, 20f, 4f, 3f, 16f, 3f, 16f, 9f),
            OfficeIconShape.Line(7f, 8f, 13f, 8f),
            OfficeIconShape.Line(7f, 12f, 11f, 12f),
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(11.5f, 21f),
                OfficeIconVertex.LineTo(12.3f, 18.3f),
                OfficeIconVertex.LineTo(19f, 11.6f),
                OfficeIconVertex.LineTo(21.4f, 14f),
                OfficeIconVertex.LineTo(14.7f, 20.7f),
                OfficeIconVertex.Close)
        ],

        Accept => [OfficeIconShape.Polyline(4f, 12.5f, 9.5f, 18f, 20f, 6f)],
        Reject => [OfficeIconShape.Line(5f, 5f, 19f, 19f), OfficeIconShape.Line(19f, 5f, 5f, 19f)],

        WordCount =>
        [
            OfficeIconShape.Line(3f, 5f, 21f, 5f),
            OfficeIconShape.Line(3f, 10f, 21f, 10f),
            OfficeIconShape.Line(3f, 15f, 10f, 15f),
            OfficeIconShape.Line(15.5f, 13f, 14.5f, 21f),
            OfficeIconShape.Line(19.5f, 13f, 18.5f, 21f),
            OfficeIconShape.Line(13.5f, 15.5f, 21f, 15.5f),
            OfficeIconShape.Line(13f, 18.5f, 20.5f, 18.5f)
        ],

        ReadMode =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(12f, 6f),
                OfficeIconVertex.CurveTo(9f, 4f, 5f, 4f, 2.5f, 5f),
                OfficeIconVertex.LineTo(2.5f, 19f),
                OfficeIconVertex.CurveTo(5f, 18f, 9f, 18f, 12f, 20f)),
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(12f, 6f),
                OfficeIconVertex.CurveTo(15f, 4f, 19f, 4f, 21.5f, 5f),
                OfficeIconVertex.LineTo(21.5f, 19f),
                OfficeIconVertex.CurveTo(19f, 18f, 15f, 18f, 12f, 20f)),
            OfficeIconShape.Line(12f, 6f, 12f, 20f)
        ],

        NavigationPane =>
        [
            OfficeIconShape.Rectangle(3f, 4f, 18f, 16f, 1.5f),
            OfficeIconShape.Line(9f, 4f, 9f, 20f),
            OfficeIconShape.Line(4.8f, 8f, 7.2f, 8f),
            OfficeIconShape.Line(4.8f, 11f, 7.2f, 11f),
            OfficeIconShape.Line(4.8f, 14f, 7.2f, 14f)
        ],

        MergeCells =>
        [
            OfficeIconShape.Rectangle(3f, 5f, 18f, 14f, 1f),
            OfficeIconShape.Line(12f, 5f, 12f, 8f),
            OfficeIconShape.Line(12f, 16f, 12f, 19f),
            OfficeIconShape.Polyline(6.5f, 9.5f, 9f, 12f, 6.5f, 14.5f),
            OfficeIconShape.Polyline(17.5f, 9.5f, 15f, 12f, 17.5f, 14.5f)
        ],

        SplitCells =>
        [
            OfficeIconShape.Rectangle(3f, 5f, 18f, 14f, 1f),
            OfficeIconShape.Line(12f, 5f, 12f, 19f),
            OfficeIconShape.Polyline(8.5f, 9.5f, 6f, 12f, 8.5f, 14.5f),
            OfficeIconShape.Polyline(15.5f, 9.5f, 18f, 12f, 15.5f, 14.5f)
        ],

        DeleteTable =>
        [
            OfficeIconShape.Rectangle(3f, 3f, 13f, 13f, 1f),
            OfficeIconShape.Line(3f, 7.5f, 16f, 7.5f),
            OfficeIconShape.Line(3f, 11.5f, 16f, 11.5f),
            OfficeIconShape.Line(9.5f, 3f, 9.5f, 16f),
            OfficeIconShape.Line(15.5f, 15.5f, 21f, 21f),
            OfficeIconShape.Line(21f, 15.5f, 15.5f, 21f)
        ],

        SelectAll =>
        [
            OfficeIconShape.Polyline(3f, 7f, 3f, 3f, 7f, 3f),
            OfficeIconShape.Polyline(17f, 3f, 21f, 3f, 21f, 7f),
            OfficeIconShape.Polyline(21f, 17f, 21f, 21f, 17f, 21f),
            OfficeIconShape.Polyline(7f, 21f, 3f, 21f, 3f, 17f),
            OfficeIconShape.Line(7.5f, 9f, 16.5f, 9f),
            OfficeIconShape.Line(7.5f, 12f, 16.5f, 12f),
            OfficeIconShape.Line(7.5f, 15f, 13.5f, 15f)
        ],

        _ => null
    };

    /// <summary>A capital A, left of centre, for the grow and shrink pair.</summary>
    static OfficeIconShape[] Letter() =>
    [
        OfficeIconShape.Polyline(2.5f, 20f, 8f, 5f, 13.5f, 20f),
        OfficeIconShape.Line(4.6f, 14.5f, 11.4f, 14.5f)
    ];

    /// <summary>A small figure 2 with its top-left corner at the given point.</summary>
    static OfficeIconShape Two(float x, float y) => OfficeIconShape.Path(
        OfficeIconVertex.MoveTo(x, y + 2f),
        OfficeIconVertex.CurveTo(x, y, x + 5f, y, x + 5f, y + 2f),
        OfficeIconVertex.CurveTo(x + 5f, y + 3.5f, x, y + 6f, x, y + 7.5f),
        OfficeIconVertex.LineTo(x + 5.5f, y + 7.5f));

    /// <summary>A speech bubble with its tail at the lower left.</summary>
    static OfficeIconShape[] Bubble() =>
    [
        OfficeIconShape.Path(
            OfficeIconVertex.MoveTo(5.5f, 4f),
            OfficeIconVertex.LineTo(18.5f, 4f),
            OfficeIconVertex.CurveTo(19.9f, 4f, 21f, 5.1f, 21f, 6.5f),
            OfficeIconVertex.LineTo(21f, 14.5f),
            OfficeIconVertex.CurveTo(21f, 15.9f, 19.9f, 17f, 18.5f, 17f),
            OfficeIconVertex.LineTo(10f, 17f),
            OfficeIconVertex.LineTo(6f, 20.5f),
            OfficeIconVertex.LineTo(6f, 17f),
            OfficeIconVertex.LineTo(5.5f, 17f),
            OfficeIconVertex.CurveTo(4.1f, 17f, 3f, 15.9f, 3f, 14.5f),
            OfficeIconVertex.LineTo(3f, 6.5f),
            OfficeIconVertex.CurveTo(3f, 5.1f, 4.1f, 4f, 5.5f, 4f),
            OfficeIconVertex.Close)
    ];
}
