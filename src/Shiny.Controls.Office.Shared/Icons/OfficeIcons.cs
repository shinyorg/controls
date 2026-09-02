namespace Shiny.Controls.Office.Icons;

/// <summary>The icons the Word and PowerPoint editing toolbars draw.</summary>
public enum OfficeIcon
{
    Bold,
    Italic,
    Underline,
    Strikethrough,
    AlignLeft,
    AlignCenter,
    AlignRight,
    AlignJustify,
    Highlight,
    Shape,
    Table,
    Picture,
    TextBox,
    Delete,
    Indent,
    Outdent,

    /// <summary>Three dots with a rule beside each — a bulleted list.</summary>
    BulletList,

    /// <summary>The same three rules, numbered 1-2-3 instead of dotted.</summary>
    NumberedList,
    Undo,
    Redo,
    Previous,
    Next,

    /// <summary>A screen with a play mark in it — start the slide show.</summary>
    SlideShow,

    /// <summary>A magnifier — the find box's leading mark.</summary>
    Find,

    /// <summary>The chevron on a split button, saying that pressing it opens a gallery.</summary>
    Chevron,

    /// <summary>A sheet of paper with its content box drawn inside it — the page margins gallery.</summary>
    PageMargins,

    // The spreadsheet toolbar's own. Everything above is shared with the document and slide bars;
    // these have no meaning outside a grid, which is why they sit apart rather than interleaved.

    /// <summary>Sigma — the AutoSum button.</summary>
    Sum,

    /// <summary>The generic currency sign, not a dollar: the format follows the reader's culture.</summary>
    Currency,
    Percent,
    DecimalIncrease,
    DecimalDecrease,
    WrapText,

    /// <summary>A paint drop — the colour poured into a cell's background.</summary>
    FillColor,

    /// <summary>An eraser: strip the formatting, keep the contents.</summary>
    ClearFormat,

    /// <summary>Two column edges with a measure between them — fit the column to what is in it.</summary>
    ColumnWidth,
    AlignTop,
    AlignMiddle,
    AlignBottom,

    /// <summary>Scissors — cut the selection to the clipboard.</summary>
    Cut,

    /// <summary>Two offset sheets — copy the selection to the clipboard.</summary>
    Copy,

    /// <summary>A clipboard with a sheet on it — paste what is held.</summary>
    Paste,

    /// <summary>A grid with a new band opening across it, and a plus. Rows.</summary>
    InsertRow,

    /// <summary>The same, turned: a new band opening down the grid. Columns.</summary>
    InsertColumn,

    /// <summary>The same grid with a minus in the band: the row comes out.</summary>
    DeleteRow,

    /// <summary>The same, turned: the column comes out.</summary>
    DeleteColumn,

    /// <summary>An eye with a stroke through it — the selection is taken out of view.</summary>
    Hide,

    /// <summary>The same eye, open — what was hidden comes back.</summary>
    Unhide,

    /// <summary>A run of values with the mean drawn through it — AVERAGE.</summary>
    Average,

    /// <summary>A hash — COUNT, how many of them there are rather than what they add to.</summary>
    Count,

    /// <summary>An arrow down onto a floor rule — MIN.</summary>
    Min,

    /// <summary>An arrow up to a ceiling rule — MAX.</summary>
    Max,

    /// <summary>A tick over a wavy underline — the mark for the spelling pass.</summary>
    SpellCheck,

    ZoomIn,
    ZoomOut,

    /// <summary>Arrows pushing out to a page's two edges — fit the page across the screen.</summary>
    FitWidth,

    /// <summary>A page with its top band filled — the running head.</summary>
    Header,

    /// <summary>The same page with its bottom band filled.</summary>
    Footer,

    /// <summary>A page with a hash in it.</summary>
    PageNumber,

    /// <summary>Two page edges parted by a dashed rule.</summary>
    PageBreak,

    /// <summary>Sheets of paper — the print view, as against the continuous one.</summary>
    PrintLayout,

    /// <summary>Paper taller than it is wide.</summary>
    Portrait,

    /// <summary>The same sheet turned.</summary>
    Landscape,

    /// <summary>A page with a mark washed across it.</summary>
    Watermark,

    /// <summary>A page whose text block is inset by each of the four presets.</summary>
    MarginsNarrow,
    MarginsNormal,
    MarginsModerate,
    MarginsWide,

    // The notebook canvas. Every one of these is a *mode* the pointer is in rather than a command it
    // runs, which is why they sit on toggles in the bar and why the artwork is a picture of the tool
    // in the hand rather than of the mark it leaves.

    /// <summary>The arrow: select, move, resize.</summary>
    Pointer,

    /// <summary>The pen. Paired with <see cref="Highlight"/>, which doubles as the highlighter pen.</summary>
    Pen,

    Eraser,

    /// <summary>The lasso, drawn as the dashed loop it leaves on the page.</summary>
    Lasso,

    /// <summary>The open hand: drag to scroll.</summary>
    Hand,

    /// <summary>Add a page.</summary>
    NewPage,

    /// <summary>Add a section.</summary>
    NewSection,

    /// <summary>The page's rule — blank, lined, grid or dots.</summary>
    PageRule,

    BringToFront,

    SendToBack,

    Duplicate,

    Lock
}


/// <summary>
/// The one icon set behind both Office editing toolbars, on both hosts.
/// </summary>
/// <remarks>
/// <para>
/// Every button on the document and slide toolbars draws from here: one monochrome stroked set on a
/// 24x24 grid, one weight, no colour of its own. What it replaced was a mixture — styled letters for
/// bold and italic, geometric unicode for the alignment and undo controls, and emoji for the picture
/// and delete buttons. Emoji are the reason this exists rather than being a matter of taste: a font
/// paints them in colour, at its own size and its own weight, so those two buttons could not be
/// tinted, did not match the buttons beside them and looked different on every platform. The unicode
/// glyphs had the milder version of the same problem, plus tofu on Android fonts that lack them.
/// </para>
/// <para>
/// The pickers are the deliberate exception. Font, font size and the colour and highlight swatches
/// have to show what they are currently set to, so they stay as they are — a monochrome icon cannot
/// say "Calibri, 11pt, red".
/// </para>
/// </remarks>
public static class OfficeIcons
{
    /// <summary>The grid every icon is drawn on. Hosts scale it to whatever the button offers.</summary>
    public const float Grid = 24f;

    /// <summary>The stroke width on that grid, so the two hosts come out at the same weight.</summary>
    public const float StrokeWidth = 1.9f;


    /// <summary>The figures making up an icon, in draw order.</summary>
    public static IReadOnlyList<OfficeIconShape> Shapes(OfficeIcon icon) => icon switch
    {
        // Two bowls off a shared stem — the letterform, drawn rather than typeset, so it carries the
        // same weight as its neighbours instead of whatever the platform's bold face happens to be.
        OfficeIcon.Bold =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(7.5f, 4.5f),
                OfficeIconVertex.LineTo(13f, 4.5f),
                OfficeIconVertex.CurveTo(15.4f, 4.5f, 17f, 6.1f, 17f, 8.35f),
                OfficeIconVertex.CurveTo(17f, 10.6f, 15.4f, 12.2f, 13f, 12.2f),
                OfficeIconVertex.LineTo(7.5f, 12.2f),
                OfficeIconVertex.Close),
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(7.5f, 12.2f),
                OfficeIconVertex.LineTo(13.8f, 12.2f),
                OfficeIconVertex.CurveTo(16.3f, 12.2f, 18f, 13.8f, 18f, 15.85f),
                OfficeIconVertex.CurveTo(18f, 17.9f, 16.3f, 19.5f, 13.8f, 19.5f),
                OfficeIconVertex.LineTo(7.5f, 19.5f),
                OfficeIconVertex.Close)
        ],

        OfficeIcon.Italic =>
        [
            OfficeIconShape.Line(9.5f, 4.8f, 18.5f, 4.8f),
            OfficeIconShape.Line(5.5f, 19.2f, 14.5f, 19.2f),
            OfficeIconShape.Line(14f, 4.8f, 10f, 19.2f)
        ],

        OfficeIcon.Underline =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(6.5f, 4f),
                OfficeIconVertex.LineTo(6.5f, 10.5f),
                OfficeIconVertex.CurveTo(6.5f, 13.54f, 8.96f, 16f, 12f, 16f),
                OfficeIconVertex.CurveTo(15.04f, 16f, 17.5f, 13.54f, 17.5f, 10.5f),
                OfficeIconVertex.LineTo(17.5f, 4f)),
            OfficeIconShape.Line(5f, 20f, 19f, 20f)
        ],

        OfficeIcon.Strikethrough =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(16.5f, 4.7f),
                OfficeIconVertex.LineTo(9.8f, 4.7f),
                OfficeIconVertex.CurveTo(7.7f, 4.7f, 6.4f, 6.4f, 7.1f, 8.4f)),
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(13.4f, 12f),
                OfficeIconVertex.CurveTo(15.9f, 12.4f, 17.2f, 14.2f, 16.8f, 16.4f),
                OfficeIconVertex.CurveTo(16.4f, 18.4f, 14.6f, 19.4f, 12.3f, 19.4f),
                OfficeIconVertex.LineTo(6.6f, 19.4f)),
            OfficeIconShape.Line(3.5f, 12f, 20.5f, 12f)
        ],

        // The alignment set is four rules with the short ones moved about, which is the only mark
        // that reads at 22px — an arrow says "go left", not "align left".
        OfficeIcon.AlignLeft => Rules(4f, 14.5f),
        OfficeIcon.AlignCenter => Rules(6.75f, 17.25f),
        OfficeIcon.AlignRight => Rules(9.5f, 20f),
        OfficeIcon.AlignJustify => Rules(4f, 20f),

        // Letter over a filled bar: the mark Word and PowerPoint both use, and the one place a
        // toolbar host may tint the bar with the colour it would apply.
        OfficeIcon.Highlight =>
        [
            OfficeIconShape.Polyline(7.3f, 15.6f, 12f, 4.8f, 16.7f, 15.6f),
            OfficeIconShape.Line(9.1f, 12.2f, 14.9f, 12.2f),
            OfficeIconShape.Rectangle(4.5f, 18f, 15f, 3f, 0.8f).Filled()
        ],

        OfficeIcon.Shape =>
        [
            OfficeIconShape.Rectangle(3.3f, 3.3f, 11.4f, 11.4f, 1.6f),
            OfficeIconShape.Circle(15.2f, 15.2f, 5.6f)
        ],

        OfficeIcon.Table =>
        [
            OfficeIconShape.Rectangle(3.4f, 4.6f, 17.2f, 14.8f, 1.6f),
            OfficeIconShape.Line(3.4f, 9.5f, 20.6f, 9.5f),
            OfficeIconShape.Line(3.4f, 14.5f, 20.6f, 14.5f),
            OfficeIconShape.Line(9.2f, 4.6f, 9.2f, 19.4f),
            OfficeIconShape.Line(15f, 4.6f, 15f, 19.4f)
        ],

        // A portrait sheet with the content box inside it. Two rectangles and nothing else, because
        // what the icon has to say is "this inner box moves" — the corner ticks Word draws are
        // illegible at 24px and read as a table once they are thick enough to see.
        OfficeIcon.PageMargins =>
        [
            OfficeIconShape.Rectangle(5f, 2.6f, 14f, 18.8f, 1.4f),
            OfficeIconShape.Rectangle(8f, 6.2f, 8f, 11.6f)
        ],

        OfficeIcon.Picture =>
        [
            OfficeIconShape.Rectangle(3.4f, 4.6f, 17.2f, 14.8f, 2f),
            OfficeIconShape.Circle(8.6f, 9.4f, 1.7f),
            OfficeIconShape.Polyline(4.4f, 17.4f, 9.6f, 12.2f, 13.2f, 15.8f, 16.2f, 12.8f, 20f, 16.6f)
        ],

        OfficeIcon.TextBox =>
        [
            OfficeIconShape.Rectangle(3.4f, 4.6f, 17.2f, 14.8f, 2f),
            OfficeIconShape.Line(8.2f, 9.4f, 15.8f, 9.4f),
            OfficeIconShape.Line(12f, 9.4f, 12f, 15.6f)
        ],

        OfficeIcon.Delete =>
        [
            OfficeIconShape.Line(3.8f, 6.4f, 20.2f, 6.4f),
            OfficeIconShape.Polyline(9.4f, 6.4f, 9.4f, 4.2f, 14.6f, 4.2f, 14.6f, 6.4f),
            OfficeIconShape.Polyline(6.4f, 6.4f, 7.5f, 20.6f, 16.5f, 20.6f, 17.6f, 6.4f),
            OfficeIconShape.Line(10.4f, 10f, 10.4f, 17f),
            OfficeIconShape.Line(13.6f, 10f, 13.6f, 17f)
        ],

        // Three markers down the left with a rule beside each. The rules start at the same x as the
        // indent icons' short ones, so the list and indent buttons read as one family on the bar.
        OfficeIcon.BulletList =>
        [
            .. ListRules(),
            // Stroked at a radius under the stroke width so they paint as dots, the same trick the
            // decimal buttons use. Filling them would read better and would also make them the only
            // marks on the bar a host cannot tint.
            OfficeIconShape.Circle(4.7f, 6f, 0.75f),
            OfficeIconShape.Circle(4.7f, 12f, 0.75f),
            OfficeIconShape.Circle(4.7f, 18f, 0.75f)
        ],

        // The numerals are drawn as strokes rather than typeset, for the same reason the bold B is:
        // a glyph would arrive in the platform's own face at the platform's own weight.
        OfficeIcon.NumberedList =>
        [
            .. ListRules(),

            // 1 — a stem with a flag, no foot: a serif at 24px is a smudge.
            OfficeIconShape.Polyline(3.6f, 4.6f, 4.9f, 3.6f, 4.9f, 8.2f),

            // 2 — over the top, down the diagonal, along the base.
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(3.4f, 10.4f),
                OfficeIconVertex.CurveTo(3.4f, 9.3f, 4.3f, 9.1f, 4.9f, 9.4f),
                OfficeIconVertex.CurveTo(5.7f, 9.8f, 5.6f, 10.8f, 5f, 11.4f),
                OfficeIconVertex.LineTo(3.4f, 13.3f),
                OfficeIconVertex.LineTo(5.8f, 13.3f)),

            // 3 — two bowls sharing a waist.
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(3.5f, 15.6f),
                OfficeIconVertex.CurveTo(4.2f, 14.9f, 5.7f, 15.2f, 5.7f, 16.3f),
                OfficeIconVertex.CurveTo(5.7f, 17.1f, 4.9f, 17.4f, 4.4f, 17.4f),
                OfficeIconVertex.CurveTo(5.1f, 17.4f, 5.9f, 17.7f, 5.9f, 18.6f),
                OfficeIconVertex.CurveTo(5.9f, 19.8f, 4.2f, 20.1f, 3.4f, 19.3f))
        ],

        OfficeIcon.Indent =>
        [
            .. Rules(10f, 20f),
            OfficeIconShape.Polyline(3.6f, 9.2f, 7.2f, 12f, 3.6f, 14.8f)
        ],

        OfficeIcon.Outdent =>
        [
            .. Rules(10f, 20f),
            OfficeIconShape.Polyline(7.2f, 9.2f, 3.6f, 12f, 7.2f, 14.8f)
        ],

        // Arrow head plus a half-circle back the way it came, matching the undo pair the image editor
        // already draws — one undo mark across the whole product.
        OfficeIcon.Undo =>
        [
            OfficeIconShape.Polyline(9f, 14f, 4f, 9f, 9f, 4f),
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(4f, 9f),
                OfficeIconVertex.LineTo(13f, 9f),
                OfficeIconVertex.CurveTo(16.31f, 9f, 19f, 11.69f, 19f, 15f),
                OfficeIconVertex.CurveTo(19f, 18.31f, 16.31f, 21f, 13f, 21f),
                OfficeIconVertex.LineTo(9.5f, 21f))
        ],

        OfficeIcon.Redo =>
        [
            OfficeIconShape.Polyline(15f, 14f, 20f, 9f, 15f, 4f),
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(20f, 9f),
                OfficeIconVertex.LineTo(11f, 9f),
                OfficeIconVertex.CurveTo(7.69f, 9f, 5f, 11.69f, 5f, 15f),
                OfficeIconVertex.CurveTo(5f, 18.31f, 7.69f, 21f, 11f, 21f),
                OfficeIconVertex.LineTo(14.5f, 21f))
        ],

        OfficeIcon.Previous => [OfficeIconShape.Polyline(14.5f, 4.5f, 8f, 12f, 14.5f, 19.5f)],
        OfficeIcon.Next => [OfficeIconShape.Polyline(9.5f, 4.5f, 16f, 12f, 9.5f, 19.5f)],

        // A screen around the play mark, not the bare triangle a media button uses. The button sits
        // beside the previous/next chevrons in the slide group, and a lone triangle there reads as one
        // more navigation arrow rather than as starting the show.
        OfficeIcon.SlideShow =>
        [
            OfficeIconShape.Rectangle(2.5f, 4.5f, 19f, 13f, 1.5f),

            // Stroked, like every other figure in the set and like the play mark in the icon sets this
            // is drawn to match. Filling it would read heavier than the screen around it at the size a
            // toolbar draws, and the set is stroked throughout apart from the highlight bar.
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(9.8f, 7.6f),
                OfficeIconVertex.LineTo(15.6f, 11f),
                OfficeIconVertex.LineTo(9.8f, 14.4f),
                OfficeIconVertex.Close)
        ],

        // The zoom pair's magnifier with neither bar in it, which is what makes those two read as
        // zoom and this one as search - the same lens at the same size on the same grid.
        OfficeIcon.Find =>
        [
            OfficeIconShape.Circle(10.5f, 10.5f, 6.5f),
            OfficeIconShape.Line(15.5f, 15.5f, 20f, 20f)
        ],

        OfficeIcon.Chevron => [OfficeIconShape.Polyline(7.5f, 10f, 12f, 14.5f, 16.5f, 10f)],

        // ---- the spreadsheet set ----

        OfficeIcon.Sum => [OfficeIconShape.Polyline(16.6f, 4.8f, 7.4f, 4.8f, 12.7f, 12f, 7.4f, 19.2f, 16.6f, 19.2f)],

        // The international currency sign rather than a dollar. The button applies the reader's own
        // currency, and stamping one country's symbol on it would be a promise the format does not keep.
        OfficeIcon.Currency =>
        [
            OfficeIconShape.Circle(12f, 12f, 4.6f),
            OfficeIconShape.Line(8.75f, 8.75f, 6.2f, 6.2f),
            OfficeIconShape.Line(15.25f, 8.75f, 17.8f, 6.2f),
            OfficeIconShape.Line(8.75f, 15.25f, 6.2f, 17.8f),
            OfficeIconShape.Line(15.25f, 15.25f, 17.8f, 17.8f)
        ],

        OfficeIcon.Percent =>
        [
            OfficeIconShape.Circle(8f, 8f, 2.6f),
            OfficeIconShape.Circle(16f, 16f, 2.6f),
            OfficeIconShape.Line(17.8f, 5.4f, 6.2f, 18.6f)
        ],

        // An arrow over the decimal places it moves. Drawn rather than lettered because "0.00" at 20px
        // is illegible, and the direction of the arrow is the whole message anyway.
        OfficeIcon.DecimalIncrease => [.. Decimals(), OfficeIconShape.Line(7f, 10f, 16.2f, 10f), OfficeIconShape.Polyline(13f, 6.8f, 16.2f, 10f, 13f, 13.2f)],
        OfficeIcon.DecimalDecrease => [.. Decimals(), OfficeIconShape.Line(7.8f, 10f, 17f, 10f), OfficeIconShape.Polyline(11f, 6.8f, 7.8f, 10f, 11f, 13.2f)],

        OfficeIcon.WrapText =>
        [
            OfficeIconShape.Line(4f, 5.5f, 20f, 5.5f),
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(4f, 11f),
                OfficeIconVertex.LineTo(15f, 11f),
                OfficeIconVertex.CurveTo(17.6f, 11f, 19.2f, 12.5f, 19.2f, 14.5f),
                OfficeIconVertex.CurveTo(19.2f, 16.5f, 17.6f, 18f, 15f, 18f),
                OfficeIconVertex.LineTo(10.5f, 18f)),
            OfficeIconShape.Polyline(13f, 15.5f, 10.5f, 18f, 13f, 20.5f)
        ],

        OfficeIcon.FillColor =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(12f, 4f),
                OfficeIconVertex.CurveTo(12f, 4f, 6.2f, 10.6f, 6.2f, 14.4f),
                OfficeIconVertex.CurveTo(6.2f, 17.6f, 8.8f, 20.2f, 12f, 20.2f),
                OfficeIconVertex.CurveTo(15.2f, 20.2f, 17.8f, 17.6f, 17.8f, 14.4f),
                OfficeIconVertex.CurveTo(17.8f, 10.6f, 12f, 4f, 12f, 4f),
                OfficeIconVertex.Close)
        ],

        OfficeIcon.ClearFormat =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(5.5f, 15f),
                OfficeIconVertex.LineTo(11.8f, 8.7f),
                OfficeIconVertex.LineTo(18.1f, 15f),
                OfficeIconVertex.LineTo(13.6f, 19.5f),
                OfficeIconVertex.LineTo(10f, 19.5f),
                OfficeIconVertex.Close),
            OfficeIconShape.Line(8.65f, 11.85f, 14.95f, 18.15f),
            OfficeIconShape.Line(4f, 22f, 20f, 22f)
        ],

        OfficeIcon.ColumnWidth =>
        [
            OfficeIconShape.Line(6f, 4.5f, 6f, 19.5f),
            OfficeIconShape.Line(18f, 4.5f, 18f, 19.5f),
            OfficeIconShape.Line(8.2f, 12f, 15.8f, 12f),
            OfficeIconShape.Polyline(10.6f, 9.6f, 8.2f, 12f, 10.6f, 14.4f),
            OfficeIconShape.Polyline(13.4f, 9.6f, 15.8f, 12f, 13.4f, 14.4f)
        ],

        // A full-width rule at the edge the content is pulled to, with the content beside it. The
        // horizontal set says the same thing by moving short rules about; this is its other axis.
        OfficeIcon.AlignTop => [OfficeIconShape.Line(4f, 4.5f, 20f, 4.5f), .. Lines(9.5f, 14f)],
        OfficeIcon.AlignMiddle => [OfficeIconShape.Line(4f, 12f, 20f, 12f), .. Lines(6.5f, 17.5f)],
        OfficeIcon.AlignBottom => [OfficeIconShape.Line(4f, 19.5f, 20f, 19.5f), .. Lines(10f, 14.5f)],

        // Two blades crossed over their handles. Drawn as two strokes plus two rings rather than an
        // outline, because at 18px an outlined pair of scissors is a smudge.
        OfficeIcon.Cut =>
        [
            OfficeIconShape.Line(7f, 5f, 16.5f, 17.5f),
            OfficeIconShape.Line(17f, 5f, 7.5f, 17.5f),
            OfficeIconShape.Circle(6.6f, 19f, 2.1f),
            OfficeIconShape.Circle(17.4f, 19f, 2.1f)
        ],

        // The back sheet is offset up and right, and only its two visible edges are drawn - a full
        // second rectangle behind the front one reads as a frame rather than a stack.
        OfficeIcon.Copy =>
        [
            OfficeIconShape.Rectangle(4.5f, 7.5f, 11f, 12f, 1.5f),
            OfficeIconShape.Polyline(8.5f, 5.5f, 19.5f, 5.5f, 19.5f, 16f)
        ],

        // The clip at the top is what separates this from a plain page: without it the shape is the
        // same one Copy's front sheet uses.
        OfficeIcon.Paste =>
        [
            OfficeIconShape.Rectangle(5.5f, 5.5f, 13f, 14f, 1.5f),
            OfficeIconShape.Rectangle(9.5f, 3.5f, 5f, 3.5f, 1f)
        ],

        // A grid with the band being opened drawn as a gap, and a plus in it. The plus is what says
        // "insert" rather than "select": the band alone is the row-header gesture.
        OfficeIcon.InsertRow =>
        [
            OfficeIconShape.Line(4f, 6f, 20f, 6f),
            OfficeIconShape.Line(4f, 18f, 20f, 18f),
            OfficeIconShape.Line(12f, 9f, 12f, 15f),
            OfficeIconShape.Line(9f, 12f, 15f, 12f)
        ],

        OfficeIcon.InsertColumn =>
        [
            OfficeIconShape.Line(6f, 4f, 6f, 20f),
            OfficeIconShape.Line(18f, 4f, 18f, 20f),
            OfficeIconShape.Line(12f, 9f, 12f, 15f),
            OfficeIconShape.Line(9f, 12f, 15f, 12f)
        ],

        // The insert pair with the plus's upright taken away. A minus rather than a cross or a bin,
        // so insert and delete read as the same gesture in two directions - which is what they are.
        OfficeIcon.DeleteRow =>
        [
            OfficeIconShape.Line(4f, 6f, 20f, 6f),
            OfficeIconShape.Line(4f, 18f, 20f, 18f),
            OfficeIconShape.Line(9f, 12f, 15f, 12f)
        ],

        OfficeIcon.DeleteColumn =>
        [
            OfficeIconShape.Line(6f, 4f, 6f, 20f),
            OfficeIconShape.Line(18f, 4f, 18f, 20f),
            OfficeIconShape.Line(9f, 12f, 15f, 12f)
        ],

        // An eye, struck and open. The other candidate was the two column edges closing on each
        // other, which is nearer to what the command does - and all but identical to ColumnWidth,
        // two buttons away in the same group.
        OfficeIcon.Hide => [.. Eye(), OfficeIconShape.Line(4.6f, 19.4f, 19.4f, 4.6f)],
        OfficeIcon.Unhide => Eye(),

        // The aggregates. Sum is the sigma above; these four are the shape of what each one answers
        // about the range rather than a letterform, which at 18px would be three illegible capitals.

        // x-bar, the notation for a mean. Two drawings came before it: a zigzag with the mean ruled
        // through it, which at 18px was two peaks and a smudge because the line spent most of its
        // length inside a stroke going the other way; and bars under that rule, which read well and
        // were filled - and a filled figure is one the toolbar cannot tint.
        OfficeIcon.Average =>
        [
            OfficeIconShape.Line(7.5f, 6f, 16.5f, 6f),
            OfficeIconShape.Line(7.5f, 10.5f, 16.5f, 19.5f),
            OfficeIconShape.Line(16.5f, 10.5f, 7.5f, 19.5f)
        ],

        // A hash: how many, not how much.
        OfficeIcon.Count =>
        [
            OfficeIconShape.Line(9.8f, 4.5f, 7.8f, 19.5f),
            OfficeIconShape.Line(16.2f, 4.5f, 14.2f, 19.5f),
            OfficeIconShape.Line(5.4f, 9.6f, 18.6f, 9.6f),
            OfficeIconShape.Line(4.6f, 14.4f, 17.8f, 14.4f)
        ],

        // An arrow onto the floor, and the same arrow up to the ceiling.
        OfficeIcon.Min =>
        [
            OfficeIconShape.Line(4.5f, 19.5f, 19.5f, 19.5f),
            OfficeIconShape.Line(12f, 4.5f, 12f, 15.5f),
            OfficeIconShape.Polyline(8.4f, 12f, 12f, 15.5f, 15.6f, 12f)
        ],

        OfficeIcon.Max =>
        [
            OfficeIconShape.Line(4.5f, 4.5f, 19.5f, 4.5f),
            OfficeIconShape.Line(12f, 19.5f, 12f, 8.5f),
            OfficeIconShape.Polyline(8.4f, 12f, 12f, 8.5f, 15.6f, 12f)
        ],

        // A tick above the same wavy rule the editor draws under a misspelling, so the button and the
        // thing it acts on carry one mark. Two glyphs would be two ideas.
        OfficeIcon.SpellCheck =>
        [
            OfficeIconShape.Polyline(4f, 11f, 8f, 15f, 16f, 5f),
            OfficeIconShape.Polyline(3f, 19f, 5.5f, 16.5f, 8f, 19f, 10.5f, 16.5f, 13f, 19f, 15.5f, 16.5f, 18f, 19f, 20.5f, 16.5f)
        ],

        OfficeIcon.ZoomIn =>
        [
            OfficeIconShape.Circle(10.5f, 10.5f, 6.5f),
            OfficeIconShape.Line(15.5f, 15.5f, 20f, 20f),
            OfficeIconShape.Line(7.5f, 10.5f, 13.5f, 10.5f),
            OfficeIconShape.Line(10.5f, 7.5f, 10.5f, 13.5f)
        ],

        OfficeIcon.ZoomOut =>
        [
            OfficeIconShape.Circle(10.5f, 10.5f, 6.5f),
            OfficeIconShape.Line(15.5f, 15.5f, 20f, 20f),
            OfficeIconShape.Line(7.5f, 10.5f, 13.5f, 10.5f)
        ],

        // The page, and two arrows pushing out to its edges.
        OfficeIcon.FitWidth =>
        [
            OfficeIconShape.Rectangle(7f, 4f, 10f, 16f, 1f),
            OfficeIconShape.Line(2f, 12f, 5f, 12f),
            OfficeIconShape.Polyline(4f, 10f, 2f, 12f, 4f, 14f),
            OfficeIconShape.Line(19f, 12f, 22f, 12f),
            OfficeIconShape.Polyline(20f, 10f, 22f, 12f, 20f, 14f)
        ],

        OfficeIcon.Header =>
        [
            OfficeIconShape.Rectangle(5f, 3f, 14f, 18f, 1f),
            // Stroked, not filled: a filled figure cannot be tinted the way the rest of the set is,
            // which is why the highlight bar is the only one in the whole set that is.
            OfficeIconShape.Rectangle(7f, 5f, 10f, 3f, 0.5f),
            OfficeIconShape.Line(7f, 12f, 17f, 12f),
            OfficeIconShape.Line(7f, 15.5f, 17f, 15.5f)
        ],

        OfficeIcon.Footer =>
        [
            OfficeIconShape.Rectangle(5f, 3f, 14f, 18f, 1f),
            OfficeIconShape.Line(7f, 8.5f, 17f, 8.5f),
            OfficeIconShape.Line(7f, 12f, 17f, 12f),
            OfficeIconShape.Rectangle(7f, 16f, 10f, 3f, 0.5f)
        ],

        OfficeIcon.PageNumber =>
        [
            OfficeIconShape.Rectangle(5f, 3f, 14f, 18f, 1f),

            // A hash, which is the mark for "number" wherever a numeral itself would be a lie - the
            // field shows a different one on every page.
            OfficeIconShape.Line(10f, 9f, 9f, 16f),
            OfficeIconShape.Line(14f, 9f, 13f, 16f),
            OfficeIconShape.Line(8.5f, 11.5f, 15f, 11.5f),
            OfficeIconShape.Line(8f, 14f, 14.5f, 14f)
        ],

        // Two page edges parted by the break. Dashes are drawn as separate segments because an icon
        // shape carries no dash pattern.
        OfficeIcon.PageBreak =>
        [
            OfficeIconShape.Path(
                new OfficeIconVertex(OfficeIconVerb.Move, 6f, 9f),
                new OfficeIconVertex(OfficeIconVerb.Line, 6f, 4f),
                new OfficeIconVertex(OfficeIconVerb.Line, 18f, 4f),
                new OfficeIconVertex(OfficeIconVerb.Line, 18f, 9f)),

            OfficeIconShape.Line(4f, 12f, 7f, 12f),
            OfficeIconShape.Line(10f, 12f, 14f, 12f),
            OfficeIconShape.Line(17f, 12f, 20f, 12f),

            OfficeIconShape.Path(
                new OfficeIconVertex(OfficeIconVerb.Move, 6f, 15f),
                new OfficeIconVertex(OfficeIconVerb.Line, 6f, 20f),
                new OfficeIconVertex(OfficeIconVerb.Line, 18f, 20f),
                new OfficeIconVertex(OfficeIconVerb.Line, 18f, 15f))
        ],

        // Two sheets, the second behind the first: paper, as against one continuous column.
        OfficeIcon.PrintLayout =>
        [
            OfficeIconShape.Rectangle(4f, 3f, 11f, 14f, 1f),
            OfficeIconShape.Rectangle(9f, 7f, 11f, 14f, 1f)
        ],

        OfficeIcon.Portrait => [OfficeIconShape.Rectangle(7f, 3f, 10f, 18f, 1f)],
        OfficeIcon.Landscape => [OfficeIconShape.Rectangle(3f, 7f, 18f, 10f, 1f)],

        // The page, with a diagonal band across it - the shape a DRAFT stamp makes.
        OfficeIcon.Watermark =>
        [
            OfficeIconShape.Rectangle(5f, 3f, 14f, 18f, 1f),
            OfficeIconShape.Line(7.5f, 15.5f, 16.5f, 8.5f),
            OfficeIconShape.Line(7.5f, 12f, 13f, 6.5f),
            OfficeIconShape.Line(11f, 17.5f, 16.5f, 12f)
        ],

        // The four presets, each drawn as the page with its own text block inset. Labels were the
        // first attempt and they ate the bar: four buttons captioned Normal/Narrow/Moderate/Wide are
        // most of a phone's width, which pushed everything after them off the edge. The inset is the
        // whole difference between them, so drawing it is both smaller and more direct than saying it.
        // A cursor arrow, drawn as the closed figure it is rather than as an outline, so at 16px it
        // still reads as an arrow instead of as two crossing strokes.
        OfficeIcon.Pointer =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(6f, 3.5f),
                OfficeIconVertex.LineTo(18f, 12.6f),
                OfficeIconVertex.LineTo(12.4f, 13.4f),
                OfficeIconVertex.LineTo(15.6f, 19.4f),
                OfficeIconVertex.LineTo(13.1f, 20.7f),
                OfficeIconVertex.LineTo(10f, 14.7f),
                OfficeIconVertex.LineTo(6f, 18.6f),
                OfficeIconVertex.Close)
        ],

        // The barrel with a separate nib, which is what distinguishes a pen from the plain diagonal
        // that would otherwise be indistinguishable from the italic and line icons.
        OfficeIcon.Pen =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(4.5f, 19.5f),
                OfficeIconVertex.LineTo(5.6f, 15.6f),
                OfficeIconVertex.LineTo(16.2f, 5f),
                OfficeIconVertex.CurveTo(17f, 4.2f, 18.3f, 4.2f, 19.1f, 5f),
                OfficeIconVertex.CurveTo(19.9f, 5.8f, 19.9f, 7.1f, 19.1f, 7.9f),
                OfficeIconVertex.LineTo(8.5f, 18.5f),
                OfficeIconVertex.Close),
            OfficeIconShape.Line(14.6f, 6.6f, 17.5f, 9.5f)
        ],

        // A wedge block with the rubbing edge on the baseline: the block alone reads as a rotated
        // rectangle, and the line under it is what says which end does the work.
        OfficeIcon.Eraser =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(9.2f, 18.5f),
                OfficeIconVertex.LineTo(4.6f, 13.9f),
                OfficeIconVertex.CurveTo(3.9f, 13.2f, 3.9f, 12.1f, 4.6f, 11.4f),
                OfficeIconVertex.LineTo(12.3f, 3.7f),
                OfficeIconVertex.CurveTo(13f, 3f, 14.1f, 3f, 14.8f, 3.7f),
                OfficeIconVertex.LineTo(19.4f, 8.3f),
                OfficeIconVertex.CurveTo(20.1f, 9f, 20.1f, 10.1f, 19.4f, 10.8f),
                OfficeIconVertex.LineTo(11.7f, 18.5f),
                OfficeIconVertex.Close),
            OfficeIconShape.Line(4f, 20.6f, 20f, 20.6f)
        ],

        // The loop, plus the little tail where the two ends cross - without it a lasso is just an
        // ellipse, and the set already has one of those.
        OfficeIcon.Lasso =>
        [
            OfficeIconShape.Ellipse(3.6f, 4.2f, 16.8f, 11.4f),
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(8.6f, 15.3f),
                OfficeIconVertex.CurveTo(8.6f, 17.6f, 10.2f, 18.6f, 10.2f, 20.4f))
        ],

        OfficeIcon.Hand =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(8f, 12.4f),
                OfficeIconVertex.LineTo(8f, 5.6f),
                OfficeIconVertex.CurveTo(8f, 4.6f, 8.8f, 3.8f, 9.8f, 3.8f),
                OfficeIconVertex.CurveTo(10.8f, 3.8f, 11.6f, 4.6f, 11.6f, 5.6f),
                OfficeIconVertex.LineTo(11.6f, 11f)),
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(11.6f, 11f),
                OfficeIconVertex.LineTo(11.6f, 6.6f),
                OfficeIconVertex.CurveTo(11.6f, 5.6f, 12.4f, 4.8f, 13.4f, 4.8f),
                OfficeIconVertex.CurveTo(14.4f, 4.8f, 15.2f, 5.6f, 15.2f, 6.6f),
                OfficeIconVertex.LineTo(15.2f, 11.6f)),
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(15.2f, 11.6f),
                OfficeIconVertex.LineTo(15.2f, 8.4f),
                OfficeIconVertex.CurveTo(15.2f, 7.4f, 16f, 6.6f, 17f, 6.6f),
                OfficeIconVertex.CurveTo(18f, 6.6f, 18.8f, 7.4f, 18.8f, 8.4f),
                OfficeIconVertex.LineTo(18.8f, 14.6f),
                OfficeIconVertex.CurveTo(18.8f, 18f, 16.2f, 20.6f, 12.8f, 20.6f),
                OfficeIconVertex.CurveTo(9.4f, 20.6f, 8f, 18.4f, 8f, 18.4f),
                OfficeIconVertex.LineTo(5f, 13.6f),
                OfficeIconVertex.CurveTo(4.5f, 12.7f, 4.9f, 11.7f, 5.8f, 11.3f),
                OfficeIconVertex.CurveTo(6.6f, 10.9f, 7.6f, 11.4f, 8f, 12.4f))
        ],

        OfficeIcon.NewPage =>
        [
            OfficeIconShape.Rectangle(4.5f, 3f, 12f, 18f, 1.5f),
            OfficeIconShape.Line(17f, 14f, 21f, 14f),
            OfficeIconShape.Line(19f, 12f, 19f, 16f)
        ],

        // A tab with a page behind it: a section is the divider, not the sheet.
        OfficeIcon.NewSection =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(3.5f, 8f),
                OfficeIconVertex.LineTo(3.5f, 5.5f),
                OfficeIconVertex.LineTo(9.5f, 5.5f),
                OfficeIconVertex.LineTo(11f, 8f),
                OfficeIconVertex.LineTo(16.5f, 8f),
                OfficeIconVertex.LineTo(16.5f, 19f),
                OfficeIconVertex.LineTo(3.5f, 19f),
                OfficeIconVertex.Close),
            OfficeIconShape.Line(17.5f, 13.5f, 21.5f, 13.5f),
            OfficeIconShape.Line(19.5f, 11.5f, 19.5f, 15.5f)
        ],

        // The page with its rule showing, which is exactly what the button sets.
        OfficeIcon.PageRule =>
        [
            OfficeIconShape.Rectangle(4f, 3.5f, 16f, 17f, 1.5f),
            OfficeIconShape.Line(4f, 9f, 20f, 9f),
            OfficeIconShape.Line(4f, 13f, 20f, 13f),
            OfficeIconShape.Line(4f, 17f, 20f, 17f)
        ],

        // Two overlapping squares where the one in front is drawn whole and the one behind is drawn
        // only where it is not covered. Filling the front square would say it more directly, but a
        // filled figure cannot be tinted by the toolbar the way a stroked one is - see the remarks on
        // OfficeIcons - so the occlusion carries the meaning instead. The pair are mirror images,
        // which is exactly the relationship the two commands have.
        OfficeIcon.BringToFront =>
        [
            OfficeIconShape.Polyline(14.5f, 9.5f, 20.5f, 9.5f, 20.5f, 20.5f, 9.5f, 20.5f, 9.5f, 14.5f),
            OfficeIconShape.Rectangle(3.5f, 3.5f, 11f, 11f, 1f)
        ],

        OfficeIcon.SendToBack =>
        [
            OfficeIconShape.Polyline(14.5f, 9.5f, 14.5f, 3.5f, 3.5f, 3.5f, 3.5f, 14.5f, 9.5f, 14.5f),
            OfficeIconShape.Rectangle(9.5f, 9.5f, 11f, 11f, 1f)
        ],

        OfficeIcon.Duplicate =>
        [
            OfficeIconShape.Rectangle(3.5f, 3.5f, 12f, 12f, 1.5f),
            OfficeIconShape.Rectangle(8.5f, 8.5f, 12f, 12f, 1.5f)
        ],

        OfficeIcon.Lock =>
        [
            OfficeIconShape.Rectangle(4.5f, 10.5f, 15f, 10f, 1.5f),
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(7.8f, 10.5f),
                OfficeIconVertex.LineTo(7.8f, 7.4f),
                OfficeIconVertex.CurveTo(7.8f, 5.1f, 9.7f, 3.2f, 12f, 3.2f),
                OfficeIconVertex.CurveTo(14.3f, 3.2f, 16.2f, 5.1f, 16.2f, 7.4f),
                OfficeIconVertex.LineTo(16.2f, 10.5f))
        ],

        OfficeIcon.MarginsNarrow => MarginsIcon(1.5f),
        OfficeIcon.MarginsNormal => MarginsIcon(3f),
        OfficeIcon.MarginsModerate => MarginsIcon(4.5f),
        OfficeIcon.MarginsWide => MarginsIcon(6f),

        _ => []
    };


    /// <summary>A sheet with its text block inset by <paramref name="inset"/> on every side.</summary>
    static OfficeIconShape[] MarginsIcon(float inset) =>
    [
        OfficeIconShape.Rectangle(5f, 3f, 14f, 18f, 1f),
        OfficeIconShape.Rectangle(5f + inset, 3f + inset, 14f - (inset * 2), 18f - (inset * 2))
    ];


    /// <summary>
    /// The four text rules the alignment and indent icons are built from: two full-width, and the
    /// second and fourth spanning whatever the caller asks for.
    /// </summary>
    static OfficeIconShape[] Rules(float shortLeft, float shortRight) =>
    [
        OfficeIconShape.Line(4f, 5f, 20f, 5f),
        OfficeIconShape.Line(shortLeft, 9.6f, shortRight, 9.6f),
        OfficeIconShape.Line(4f, 14.4f, 20f, 14.4f),
        OfficeIconShape.Line(shortLeft, 19f, shortRight, 19f)
    ];


    /// <summary>
    /// The three text rules a list icon puts its markers beside.
    /// </summary>
    /// <remarks>
    /// Three rather than the alignment set's four: a list icon needs an odd number so the middle
    /// marker sits on the icon's centre line, which is what keeps it from looking a pixel low.
    /// </remarks>
    static OfficeIconShape[] ListRules() =>
    [
        OfficeIconShape.Line(9f, 6f, 20.5f, 6f),
        OfficeIconShape.Line(9f, 12f, 20.5f, 12f),
        OfficeIconShape.Line(9f, 18f, 20.5f, 18f)
    ];


    /// <summary>The open eye the hide and unhide icons are both built from.</summary>
    /// <remarks>
    /// Two symmetric curves rather than an ellipse, because an ellipse with a dot in it is a target
    /// rather than an eye - the pointed corners are the whole read.
    /// </remarks>
    static OfficeIconShape[] Eye() =>
    [
        OfficeIconShape.Path(
            OfficeIconVertex.MoveTo(3.5f, 12f),
            OfficeIconVertex.CurveTo(7f, 6.6f, 17f, 6.6f, 20.5f, 12f),
            OfficeIconVertex.CurveTo(17f, 17.4f, 7f, 17.4f, 3.5f, 12f),
            OfficeIconVertex.Close),
        OfficeIconShape.Circle(12f, 12f, 2.4f)
    ];


    /// <summary>Two short rules standing in for cell content, at the given heights.</summary>
    static OfficeIconShape[] Lines(float firstY, float secondY) =>
    [
        OfficeIconShape.Line(7.5f, firstY, 16.5f, firstY),
        OfficeIconShape.Line(7.5f, secondY, 16.5f, secondY)
    ];


    /// <summary>
    /// The three decimal places the decimal buttons move.
    /// </summary>
    /// <remarks>
    /// Stroked circles at a radius smaller than the stroke, so they paint as dots. Filling them would
    /// be the honest way to say it, but the set is stroked throughout so that a host can tint it.
    /// </remarks>
    static OfficeIconShape[] Decimals() =>
    [
        OfficeIconShape.Circle(8f, 17.6f, 0.6f),
        OfficeIconShape.Circle(12f, 17.6f, 0.6f),
        OfficeIconShape.Circle(16f, 17.6f, 0.6f)
    ];
}
