namespace Shiny.Controls.Office.Icons;

/// <summary>
/// The slide editor's own ribbon icons — the Design, Transitions, Animations, Slide Show and View tabs,
/// and the Shape Format tools — on the same 24x24 stroked grid as <see cref="OfficeIcon"/>.
/// </summary>
/// <remarks>
/// A separate set rather than more <see cref="OfficeIcon"/> members: these mean nothing outside a
/// deck, and keeping them apart keeps the shared list about the commands every editor has. Both hosts
/// draw them through the same shape-list path the shapes gallery uses.
/// </remarks>
public enum SlideIcon
{
    ShapeFill,
    ShapeOutline,
    ShapeEffects,
    QuickStyles,
    Align,
    Rotate,
    RotateLeft,
    RotateRight,
    FlipHorizontal,
    FlipVertical,
    Group,
    Ungroup,
    GrowFont,
    ShrinkFont,
    ChangeCase,
    Superscript,
    Subscript,
    LineSpacing,
    TextDirection,
    AlignText,
    Hyperlink,
    Theme,
    Variants,
    SlideSize,
    FormatBackground,
    Transition,
    Preview,
    EffectOptions,
    Animation,
    AnimationPane,
    FromBeginning,
    FromCurrent,
    PresenterView,
    HideSlide,
    Chart,
    Icons,
    Video,
    Audio,
    HeaderFooter,
    DateTime,
    SlideNumber,
    NormalView,
    OutlineView,
    SlideSorter,
    NotesPage,
    SlideMaster,
    Ruler,
    Gridlines,
    Guides,
    FitToWindow,
    Section,
    Replace,
    Export,
    InsertRowAbove,
    InsertRowBelow,
    InsertColumnLeft,
    InsertColumnRight,
    MergeCells,
    SplitCells,
    BlackScreen,
    Timer,
    Close
}

/// <summary>The artwork for <see cref="SlideIcon"/>.</summary>
public static class SlideIcons
{
    static OfficeIconShape L(float x1, float y1, float x2, float y2) => OfficeIconShape.Line(x1, y1, x2, y2);

    static OfficeIconShape R(float x, float y, float w, float h, float r = 0) => OfficeIconShape.Rectangle(x, y, w, h, r);

    static OfficeIconShape P(params float[] points) => OfficeIconShape.Polyline(points);

    static OfficeIconShape C(float x, float y, float r) => OfficeIconShape.Circle(x, y, r);

    /// <summary>A slide: a 16:10 frame, the grid's usual stand-in for one.</summary>
    static OfficeIconShape Slide(float x = 3, float y = 5, float w = 18, float h = 12) => R(x, y, w, h, 1.4f);

    static OfficeIconShape[] Table(params OfficeIconShape[] extra) =>
    [
        R(3.5f, 6f, 17f, 12f, 1.2f),
        L(3.5f, 10f, 20.5f, 10f),
        L(3.5f, 14f, 20.5f, 14f),
        L(9.2f, 6f, 9.2f, 18f),
        L(14.8f, 6f, 14.8f, 18f),
        .. extra
    ];

    public static IReadOnlyList<OfficeIconShape> Shapes(SlideIcon icon) => icon switch
    {
        // A paint bucket tipping, with a bar under it for the colour.
        SlideIcon.ShapeFill =>
        [
            P(5f, 11f, 11f, 5f, 17f, 11f, 11f, 17f, 5f, 11f),
            P(17f, 11f, 18.5f, 14.5f),
            R(4f, 19.5f, 16f, 2f, 0.6f).Filled()
        ],

        // A pen nib over a colour bar.
        SlideIcon.ShapeOutline =>
        [
            P(6f, 16f, 15f, 4.5f, 18.5f, 8f, 8f, 17f, 5f, 17.5f, 6f, 16f),
            R(4f, 19.5f, 16f, 2f, 0.6f).Filled()
        ],

        // A square with a soft shadow behind it.
        SlideIcon.ShapeEffects => [R(4f, 4f, 12f, 12f, 1.4f), P(16f, 7f, 20f, 7f, 20f, 20f, 7f, 20f, 7f, 16f)],

        SlideIcon.QuickStyles => [R(3.5f, 3.5f, 7.5f, 7.5f, 1.2f).Filled(), R(13f, 3.5f, 7.5f, 7.5f, 1.2f), R(3.5f, 13f, 7.5f, 7.5f, 1.2f), R(13f, 13f, 7.5f, 7.5f, 1.2f).Filled()],

        // Two bars flush to a rule on the left.
        SlideIcon.Align => [L(4f, 3.5f, 4f, 20.5f), R(6.5f, 6f, 13f, 4f, 0.8f), R(6.5f, 14f, 8f, 4f, 0.8f)],

        SlideIcon.Rotate => [OfficeIconShape.Path(OfficeIconVertex.MoveTo(18f, 8f), OfficeIconVertex.CurveTo(16.2f, 5f, 12.8f, 3.6f, 9.6f, 4.6f), OfficeIconVertex.CurveTo(5.4f, 6f, 3.4f, 10.6f, 5f, 14.6f)), P(18.5f, 3.5f, 18.5f, 8.5f, 13.5f, 8.5f), R(8f, 12f, 9f, 8f, 1f)],

        SlideIcon.RotateLeft => [OfficeIconShape.Path(OfficeIconVertex.MoveTo(6f, 9f), OfficeIconVertex.CurveTo(8f, 5.6f, 12.4f, 4.4f, 15.8f, 6.2f), OfficeIconVertex.CurveTo(19.2f, 8f, 20.4f, 12.4f, 18.6f, 15.8f)), P(5.5f, 4f, 5.5f, 9.5f, 11f, 9.5f), R(6f, 13f, 7f, 7f, 1f)],

        SlideIcon.RotateRight => [OfficeIconShape.Path(OfficeIconVertex.MoveTo(18f, 9f), OfficeIconVertex.CurveTo(16f, 5.6f, 11.6f, 4.4f, 8.2f, 6.2f), OfficeIconVertex.CurveTo(4.8f, 8f, 3.6f, 12.4f, 5.4f, 15.8f)), P(18.5f, 4f, 18.5f, 9.5f, 13f, 9.5f), R(11f, 13f, 7f, 7f, 1f)],

        SlideIcon.FlipHorizontal => [L(12f, 3f, 12f, 21f), P(10f, 6f, 3.5f, 17f, 10f, 17f, 10f, 6f), P(14f, 6f, 20.5f, 17f, 14f, 17f, 14f, 6f)],

        SlideIcon.FlipVertical => [L(3f, 12f, 21f, 12f), P(6f, 10f, 17f, 3.5f, 17f, 10f, 6f, 10f), P(6f, 14f, 17f, 20.5f, 17f, 14f, 6f, 14f)],

        SlideIcon.Group => [R(3f, 3f, 18f, 18f, 1.4f), R(6f, 6f, 6f, 6f, 1f), R(12f, 12f, 6f, 6f, 1f)],

        SlideIcon.Ungroup => [R(4f, 4f, 8f, 8f, 1f), R(12f, 12f, 8f, 8f, 1f), L(15f, 4f, 20f, 4f), L(20f, 4f, 20f, 9f), L(4f, 15f, 4f, 20f), L(4f, 20f, 9f, 20f)],

        // A capital A with a small caret up (or down) beside it.
        SlideIcon.GrowFont => [P(3.5f, 19f, 9f, 5f, 14.5f, 19f), L(5.6f, 14f, 12.4f, 14f), P(16f, 10f, 18.5f, 6.5f, 21f, 10f)],

        SlideIcon.ShrinkFont => [P(3.5f, 19f, 9f, 5f, 14.5f, 19f), L(5.6f, 14f, 12.4f, 14f), P(16f, 7f, 18.5f, 10.5f, 21f, 7f)],

        // "Aa".
        SlideIcon.ChangeCase => [P(2.5f, 18f, 7f, 6f, 11.5f, 18f), L(4.2f, 14f, 9.8f, 14f), C(17f, 14.5f, 3.3f), L(20.3f, 11f, 20.3f, 18f)],

        SlideIcon.Superscript => [P(4f, 9f, 12f, 19f), P(12f, 9f, 4f, 19f), P(15f, 5.5f, 17f, 4f, 19f, 5.5f, 15f, 10f, 19.5f, 10f)],

        SlideIcon.Subscript => [P(4f, 5f, 12f, 15f), P(12f, 5f, 4f, 15f), P(15f, 14.5f, 17f, 13f, 19f, 14.5f, 15f, 19f, 19.5f, 19f)],

        SlideIcon.LineSpacing => [L(11f, 6f, 21f, 6f), L(11f, 12f, 21f, 12f), L(11f, 18f, 21f, 18f), L(5.5f, 4f, 5.5f, 20f), P(3f, 6.5f, 5.5f, 4f, 8f, 6.5f), P(3f, 17.5f, 5.5f, 20f, 8f, 17.5f)],

        // A rotated "A" beside an arrow down.
        SlideIcon.TextDirection => [P(4f, 4f, 16f, 8.5f, 4f, 13f), L(8f, 5.5f, 8f, 11.5f), L(19f, 5f, 19f, 19f), P(16.5f, 16.5f, 19f, 19f, 21.5f, 16.5f)],

        SlideIcon.AlignText => [R(3.5f, 3.5f, 17f, 17f, 1.4f), L(7f, 10f, 17f, 10f), L(7f, 14f, 17f, 14f)],

        // Two chain links.
        SlideIcon.Hyperlink =>
        [
            OfficeIconShape.Path(OfficeIconVertex.MoveTo(10.5f, 13.5f), OfficeIconVertex.LineTo(13.5f, 10.5f)),
            P(9f, 8.5f, 11f, 6.5f, 14f, 4.5f, 17f, 4.5f, 19.5f, 7f, 19.5f, 10f, 17.5f, 13f, 15.5f, 15f),
            P(15f, 15.5f, 13f, 17.5f, 10f, 19.5f, 7f, 19.5f, 4.5f, 17f, 4.5f, 14f, 6.5f, 11f, 8.5f, 9f)
        ],

        SlideIcon.Theme => [Slide(), R(3f, 5f, 18f, 3.5f, 1.4f).Filled(), L(6f, 12f, 14f, 12f), L(6f, 14.5f, 11f, 14.5f)],

        SlideIcon.Variants => [C(7f, 8f, 3f).Filled(), C(17f, 8f, 3f), C(7f, 17f, 3f), C(17f, 17f, 3f).Filled()],

        SlideIcon.SlideSize => [R(3f, 7f, 18f, 11f, 1.2f), P(6.5f, 10.5f, 5f, 12.5f, 6.5f, 14.5f), P(17.5f, 10.5f, 19f, 12.5f, 17.5f, 14.5f), L(5f, 12.5f, 19f, 12.5f)],

        SlideIcon.FormatBackground => [Slide(), P(5f, 15f, 9f, 10f, 12f, 13f, 15f, 9f, 19f, 15f)],

        // Two overlapping slides, the front one arriving.
        SlideIcon.Transition => [R(3f, 4f, 12f, 9f, 1.2f), R(9f, 11f, 12f, 9f, 1.2f).Filled()],

        SlideIcon.Preview => [Slide(), P(10f, 8.5f, 15f, 11f, 10f, 13.5f, 10f, 8.5f).Filled(), L(8f, 20.5f, 16f, 20.5f)],

        SlideIcon.EffectOptions => [R(4f, 4f, 16f, 16f, 1.4f), P(8f, 12f, 12f, 8f, 16f, 12f), L(12f, 8f, 12f, 17f)],

        // A star with motion lines.
        SlideIcon.Animation =>
        [
            P(15f, 4f, 16.6f, 8.2f, 21f, 8.4f, 17.6f, 11.2f, 18.8f, 15.5f, 15f, 13f, 11.2f, 15.5f, 12.4f, 11.2f, 9f, 8.4f, 13.4f, 8.2f, 15f, 4f),
            L(3f, 13f, 8f, 13f), L(4.5f, 17f, 10f, 17f), L(3f, 21f, 12f, 21f)
        ],

        SlideIcon.AnimationPane => [R(3.5f, 3.5f, 17f, 17f, 1.4f), L(12f, 3.5f, 12f, 20.5f), L(14.5f, 8f, 18f, 8f), L(14.5f, 12f, 18f, 12f), L(14.5f, 16f, 18f, 16f)],

        SlideIcon.FromBeginning => [Slide(), L(7f, 8.5f, 7f, 13.5f), P(9f, 8.5f, 14f, 11f, 9f, 13.5f, 9f, 8.5f).Filled(), L(8f, 20.5f, 16f, 20.5f)],

        SlideIcon.FromCurrent => [Slide(), P(9.5f, 8.5f, 14.5f, 11f, 9.5f, 13.5f, 9.5f, 8.5f).Filled(), L(12f, 17f, 12f, 21f)],

        // A presenter's screen: the slide, a smaller next slide and a line of notes.
        SlideIcon.PresenterView => [R(2.5f, 4f, 12f, 9f, 1.2f), R(16f, 4f, 5.5f, 4f, 0.8f), L(2.5f, 17f, 21.5f, 17f), L(2.5f, 20.5f, 15f, 20.5f)],

        SlideIcon.HideSlide => [Slide(), L(3f, 5f, 21f, 17f)],

        // Three bars.
        SlideIcon.Chart => [L(3.5f, 20.5f, 20.5f, 20.5f), R(5f, 11f, 3.5f, 9.5f, 0.6f), R(10.25f, 5f, 3.5f, 15.5f, 0.6f), R(15.5f, 14f, 3.5f, 6.5f, 0.6f)],

        // A person — the Icons gallery's familiar cover.
        SlideIcon.Icons => [C(12f, 7.5f, 3.5f), OfficeIconShape.Path(OfficeIconVertex.MoveTo(5f, 20.5f), OfficeIconVertex.CurveTo(5f, 15.5f, 8f, 13f, 12f, 13f), OfficeIconVertex.CurveTo(16f, 13f, 19f, 15.5f, 19f, 20.5f))],

        SlideIcon.Video => [R(2.5f, 6f, 13f, 12f, 1.4f), P(15.5f, 10f, 21.5f, 6.5f, 21.5f, 17.5f, 15.5f, 14f)],

        SlideIcon.Audio => [P(3.5f, 9.5f, 7.5f, 9.5f, 12.5f, 5f, 12.5f, 19f, 7.5f, 14.5f, 3.5f, 14.5f, 3.5f, 9.5f), OfficeIconShape.Path(OfficeIconVertex.MoveTo(16f, 8.5f), OfficeIconVertex.CurveTo(18f, 10.4f, 18f, 13.6f, 16f, 15.5f)), OfficeIconShape.Path(OfficeIconVertex.MoveTo(18.5f, 5.5f), OfficeIconVertex.CurveTo(22f, 9f, 22f, 15f, 18.5f, 18.5f))],

        SlideIcon.HeaderFooter => [R(4.5f, 3f, 15f, 18f, 1.4f), L(7f, 6.5f, 17f, 6.5f), L(7f, 17.5f, 17f, 17.5f)],

        // A calendar page.
        SlideIcon.DateTime => [R(3.5f, 5f, 17f, 15.5f, 1.4f), L(3.5f, 9.5f, 20.5f, 9.5f), L(8f, 3f, 8f, 6.5f), L(16f, 3f, 16f, 6.5f), R(7f, 12.5f, 3f, 3f).Filled()],

        // A slide with a "#" in its corner.
        SlideIcon.SlideNumber => [Slide(), L(14.5f, 11f, 14f, 16f), L(17.5f, 11f, 17f, 16f), L(13f, 12.5f, 19f, 12.5f), L(12.6f, 14.8f, 18.6f, 14.8f)],

        SlideIcon.NormalView => [R(3f, 4f, 18f, 16f, 1.4f), L(8f, 4f, 8f, 20f), R(10.5f, 7f, 8f, 6f, 0.8f)],

        SlideIcon.OutlineView => [L(4f, 5.5f, 20f, 5.5f), L(7f, 9.5f, 20f, 9.5f), L(7f, 13.5f, 20f, 13.5f), L(4f, 17.5f, 20f, 17.5f), L(7f, 21f, 16f, 21f)],

        SlideIcon.SlideSorter => [R(3f, 4f, 7.5f, 6f, 1f), R(13.5f, 4f, 7.5f, 6f, 1f), R(3f, 14f, 7.5f, 6f, 1f), R(13.5f, 14f, 7.5f, 6f, 1f)],

        SlideIcon.NotesPage => [R(5f, 2.5f, 14f, 19f, 1.4f), R(7.5f, 5f, 9f, 6f, 0.8f), L(7.5f, 14.5f, 16.5f, 14.5f), L(7.5f, 17.5f, 14f, 17.5f)],

        SlideIcon.SlideMaster => [R(2.5f, 3f, 12f, 9f, 1.2f), R(9.5f, 12f, 12f, 9f, 1.2f), L(2.5f, 6f, 14.5f, 6f), L(9.5f, 15f, 21.5f, 15f)],

        SlideIcon.Ruler => [R(2.5f, 8f, 19f, 8f, 1f), L(6f, 8f, 6f, 11f), L(9.5f, 8f, 9.5f, 12.5f), L(13f, 8f, 13f, 11f), L(16.5f, 8f, 16.5f, 12.5f)],

        SlideIcon.Gridlines => [R(3.5f, 3.5f, 17f, 17f, 1f), L(9.2f, 3.5f, 9.2f, 20.5f), L(14.8f, 3.5f, 14.8f, 20.5f), L(3.5f, 9.2f, 20.5f, 9.2f), L(3.5f, 14.8f, 20.5f, 14.8f)],

        SlideIcon.Guides => [Slide(), L(12f, 2.5f, 12f, 21.5f), L(1.5f, 11f, 22.5f, 11f)],

        SlideIcon.FitToWindow => [R(5f, 7f, 14f, 10f, 1f), P(2.5f, 6f, 2.5f, 2.5f, 6f, 2.5f), P(18f, 2.5f, 21.5f, 2.5f, 21.5f, 6f), P(2.5f, 18f, 2.5f, 21.5f, 6f, 21.5f), P(21.5f, 18f, 21.5f, 21.5f, 18f, 21.5f)],

        SlideIcon.Section => [R(3f, 3f, 18f, 5f, 1f).Filled(), R(6f, 10.5f, 15f, 4.5f, 1f), R(6f, 17f, 15f, 4.5f, 1f)],

        // "ab" becoming "ac": two short rules and an arrow.
        SlideIcon.Replace => [L(3f, 7f, 11f, 7f), L(13f, 17f, 21f, 17f), OfficeIconShape.Path(OfficeIconVertex.MoveTo(7f, 10f), OfficeIconVertex.CurveTo(7f, 15f, 10f, 17f, 13f, 17f)), P(10.5f, 14.5f, 13f, 17f, 10.5f, 19.5f)],

        SlideIcon.Export => [P(8f, 9f, 12f, 4.5f, 16f, 9f), L(12f, 4.5f, 12f, 15f), P(4.5f, 13f, 4.5f, 20f, 19.5f, 20f, 19.5f, 13f)],

        SlideIcon.InsertRowAbove => Table(L(21.5f, 2f, 21.5f, 6f)),
        SlideIcon.InsertRowBelow => Table(L(21.5f, 18f, 21.5f, 22f)),
        SlideIcon.InsertColumnLeft => Table(L(2f, 3f, 6f, 3f)),
        SlideIcon.InsertColumnRight => Table(L(18f, 3f, 22f, 3f)),

        SlideIcon.MergeCells => [R(3.5f, 6f, 17f, 12f, 1.2f), P(7f, 12f, 10.5f, 12f), P(9f, 10f, 11f, 12f, 9f, 14f), P(17f, 12f, 13.5f, 12f), P(15f, 10f, 13f, 12f, 15f, 14f)],

        SlideIcon.SplitCells => [R(3.5f, 6f, 17f, 12f, 1.2f), L(12f, 6f, 12f, 18f), P(9.5f, 12f, 6f, 12f), P(8f, 10f, 6f, 12f, 8f, 14f), P(14.5f, 12f, 18f, 12f), P(16f, 10f, 18f, 12f, 16f, 14f)],

        SlideIcon.BlackScreen => [R(3f, 5f, 18f, 12f, 1.4f).Filled(), L(8f, 20.5f, 16f, 20.5f)],

        SlideIcon.Timer => [C(12f, 13f, 7.5f), L(12f, 13f, 12f, 9f), L(12f, 13f, 15f, 15f), L(10f, 3f, 14f, 3f)],

        SlideIcon.Close => [L(6f, 6f, 18f, 18f), L(18f, 6f, 6f, 18f)],

        _ => []
    };
}
