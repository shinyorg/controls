namespace Shiny.Controls.Office.Icons;

/// <summary>
/// The icons the Office application shell draws — title bar, backstage rail, status bar, panes.
/// </summary>
/// <remarks>
/// A separate enum from <see cref="OfficeIcon"/> rather than more members on it: the editors' own icon
/// set is being extended by each editor independently, and the shell's chrome is a different family
/// (window furniture rather than formatting commands). A few shell icons are the same artwork as an
/// editor icon — undo, the magnifier, the lock — and those delegate rather than draw twice.
/// </remarks>
public enum OfficeShellIcon
{
    Save,
    SaveAs,
    Undo,
    Redo,
    Search,
    Help,
    Comments,
    Share,
    Editing,
    Reviewing,
    Viewing,
    Focus,
    ExitFocus,
    ReadMode,
    PrintLayout,
    WebLayout,
    NormalView,
    PageLayoutView,
    PageBreakView,
    SlideSorter,
    Back,
    Home,
    NewDocument,
    Open,
    Info,
    Print,
    Export,
    History,
    Options,
    Minus,
    Plus,
    Close,
    ChevronDown,
    ChevronUp,
    ChevronLeft,
    ChevronRight,

    /// <summary>A chevron under a rule — a gallery's "show all".</summary>
    GalleryMore,
    Headings,
    Pages,
    Results,
    Person,
    Protect,
    Inspect,
    Document,
    More,
    Menu,
    Check,
    Collapse,
    Expand,

    /// <summary>PowerPoint's "Fit slide to current window" — a slide inside four corner brackets.</summary>
    FitToWindow,

    /// <summary>The status bar's Notes toggle — a page of lines.</summary>
    Notes,

    /// <summary>Start the slide show — PowerPoint's quick access "From Beginning".</summary>
    SlideShow
}


public static partial class OfficeIcons
{
    /// <summary>The figures making up a shell icon, in draw order.</summary>
    public static IReadOnlyList<OfficeIconShape> Shapes(OfficeShellIcon icon) => icon switch
    {
        OfficeShellIcon.Undo => Shapes(OfficeIcon.Undo),
        OfficeShellIcon.Redo => Shapes(OfficeIcon.Redo),
        OfficeShellIcon.Search or OfficeShellIcon.Results => Shapes(OfficeIcon.Find),
        OfficeShellIcon.PrintLayout => Shapes(OfficeIcon.PrintLayout),
        OfficeShellIcon.PageBreakView => Shapes(OfficeIcon.PageBreak),
        OfficeShellIcon.Protect => Shapes(OfficeIcon.Lock),
        OfficeShellIcon.Viewing => Eye(),

        OfficeShellIcon.Save =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(4.5f, 4.5f),
                OfficeIconVertex.LineTo(16.5f, 4.5f),
                OfficeIconVertex.LineTo(19.5f, 7.5f),
                OfficeIconVertex.LineTo(19.5f, 19.5f),
                OfficeIconVertex.LineTo(4.5f, 19.5f),
                OfficeIconVertex.Close),
            OfficeIconShape.Polyline(8f, 4.5f, 8f, 9f, 15f, 9f, 15f, 4.5f),
            OfficeIconShape.Rectangle(7.5f, 13f, 9f, 6.5f, 0.5f)
        ],

        OfficeShellIcon.SaveAs =>
        [
            OfficeIconShape.Polyline(13.5f, 18.5f, 3.5f, 18.5f, 3.5f, 3.5f, 13.5f, 3.5f, 16.5f, 6.5f, 16.5f, 10f),
            OfficeIconShape.Polyline(6.5f, 3.5f, 6.5f, 7.5f, 12.5f, 7.5f, 12.5f, 3.5f),
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(14f, 21f),
                OfficeIconVertex.LineTo(14.6f, 18.4f),
                OfficeIconVertex.LineTo(19.8f, 13.2f),
                OfficeIconVertex.LineTo(21.8f, 15.2f),
                OfficeIconVertex.LineTo(16.6f, 20.4f),
                OfficeIconVertex.Close)
        ],

        OfficeShellIcon.Help =>
        [
            OfficeIconShape.Circle(12f, 12f, 9f),
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(9.4f, 9.3f),
                OfficeIconVertex.CurveTo(9.4f, 7.8f, 10.6f, 6.8f, 12f, 6.8f),
                OfficeIconVertex.CurveTo(13.5f, 6.8f, 14.6f, 7.8f, 14.6f, 9.2f),
                OfficeIconVertex.CurveTo(14.6f, 11.4f, 12f, 11.4f, 12f, 13.6f)),
            OfficeIconShape.Circle(12f, 16.9f, 0.6f)
        ],

        OfficeShellIcon.Comments =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(5.5f, 4.5f),
                OfficeIconVertex.LineTo(18.5f, 4.5f),
                OfficeIconVertex.CurveTo(19.6f, 4.5f, 20.5f, 5.4f, 20.5f, 6.5f),
                OfficeIconVertex.LineTo(20.5f, 14.5f),
                OfficeIconVertex.CurveTo(20.5f, 15.6f, 19.6f, 16.5f, 18.5f, 16.5f),
                OfficeIconVertex.LineTo(11f, 16.5f),
                OfficeIconVertex.LineTo(7f, 20f),
                OfficeIconVertex.LineTo(7f, 16.5f),
                OfficeIconVertex.LineTo(5.5f, 16.5f),
                OfficeIconVertex.CurveTo(4.4f, 16.5f, 3.5f, 15.6f, 3.5f, 14.5f),
                OfficeIconVertex.LineTo(3.5f, 6.5f),
                OfficeIconVertex.CurveTo(3.5f, 5.4f, 4.4f, 4.5f, 5.5f, 4.5f),
                OfficeIconVertex.Close)
        ],

        OfficeShellIcon.Share =>
        [
            OfficeIconShape.Polyline(8.5f, 7.5f, 12f, 4f, 15.5f, 7.5f),
            OfficeIconShape.Line(12f, 4f, 12f, 14.5f),
            OfficeIconShape.Polyline(8.5f, 10.5f, 5.5f, 10.5f, 5.5f, 20f, 18.5f, 20f, 18.5f, 10.5f, 15.5f, 10.5f)
        ],

        OfficeShellIcon.Editing => Pencil(0f, 0f),

        OfficeShellIcon.Reviewing =>
        [
            OfficeIconShape.Polyline(12.5f, 20.5f, 4.5f, 20.5f, 4.5f, 3.5f, 15.5f, 3.5f, 15.5f, 9f),
            OfficeIconShape.Line(7.5f, 8f, 12.5f, 8f),
            OfficeIconShape.Line(7.5f, 11.5f, 11f, 11.5f),
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(14f, 21f),
                OfficeIconVertex.LineTo(14.6f, 18.4f),
                OfficeIconVertex.LineTo(19.8f, 13.2f),
                OfficeIconVertex.LineTo(21.8f, 15.2f),
                OfficeIconVertex.LineTo(16.6f, 20.4f),
                OfficeIconVertex.Close)
        ],

        OfficeShellIcon.Focus =>
        [
            OfficeIconShape.Polyline(4f, 9f, 4f, 4f, 9f, 4f),
            OfficeIconShape.Polyline(15f, 4f, 20f, 4f, 20f, 9f),
            OfficeIconShape.Polyline(20f, 15f, 20f, 20f, 15f, 20f),
            OfficeIconShape.Polyline(9f, 20f, 4f, 20f, 4f, 15f)
        ],

        OfficeShellIcon.ExitFocus =>
        [
            OfficeIconShape.Polyline(9f, 4f, 9f, 9f, 4f, 9f),
            OfficeIconShape.Polyline(15f, 4f, 15f, 9f, 20f, 9f),
            OfficeIconShape.Polyline(20f, 15f, 15f, 15f, 15f, 20f),
            OfficeIconShape.Polyline(9f, 20f, 9f, 15f, 4f, 15f)
        ],

        OfficeShellIcon.ReadMode =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(12f, 6.5f),
                OfficeIconVertex.CurveTo(10f, 5f, 6.5f, 4.5f, 3.5f, 5f),
                OfficeIconVertex.LineTo(3.5f, 18.5f),
                OfficeIconVertex.CurveTo(6.5f, 18f, 10f, 18.5f, 12f, 20f),
                OfficeIconVertex.CurveTo(14f, 18.5f, 17.5f, 18f, 20.5f, 18.5f),
                OfficeIconVertex.LineTo(20.5f, 5f),
                OfficeIconVertex.CurveTo(17.5f, 4.5f, 14f, 5f, 12f, 6.5f),
                OfficeIconVertex.Close),
            OfficeIconShape.Line(12f, 6.5f, 12f, 20f)
        ],

        OfficeShellIcon.WebLayout =>
        [
            OfficeIconShape.Circle(12f, 12f, 8.5f),
            OfficeIconShape.Ellipse(8.5f, 3.5f, 7f, 17f),
            OfficeIconShape.Line(3.5f, 12f, 20.5f, 12f)
        ],

        OfficeShellIcon.NormalView =>
        [
            OfficeIconShape.Rectangle(3.5f, 4.5f, 17f, 15f, 1f),
            OfficeIconShape.Line(3.5f, 9.5f, 20.5f, 9.5f),
            OfficeIconShape.Line(3.5f, 14.5f, 20.5f, 14.5f),
            OfficeIconShape.Line(9.5f, 4.5f, 9.5f, 19.5f),
            OfficeIconShape.Line(15f, 4.5f, 15f, 19.5f)
        ],

        OfficeShellIcon.PageLayoutView =>
        [
            OfficeIconShape.Rectangle(7f, 7f, 12.5f, 14f, 1f),
            OfficeIconShape.Line(7f, 3.5f, 19.5f, 3.5f),
            OfficeIconShape.Line(3.5f, 7f, 3.5f, 21f),
            OfficeIconShape.Line(10f, 11f, 16.5f, 11f),
            OfficeIconShape.Line(10f, 14.5f, 16.5f, 14.5f)
        ],

        OfficeShellIcon.SlideSorter =>
        [
            OfficeIconShape.Rectangle(3.5f, 5f, 7.5f, 6f, 1f),
            OfficeIconShape.Rectangle(13f, 5f, 7.5f, 6f, 1f),
            OfficeIconShape.Rectangle(3.5f, 13f, 7.5f, 6f, 1f),
            OfficeIconShape.Rectangle(13f, 13f, 7.5f, 6f, 1f)
        ],

        OfficeShellIcon.Back =>
        [
            OfficeIconShape.Circle(12f, 12f, 9f),
            OfficeIconShape.Line(16.5f, 12f, 7.5f, 12f),
            OfficeIconShape.Polyline(11f, 8.5f, 7.5f, 12f, 11f, 15.5f)
        ],

        OfficeShellIcon.Home =>
        [
            OfficeIconShape.Polyline(3.5f, 11.5f, 12f, 4f, 20.5f, 11.5f),
            OfficeIconShape.Polyline(6f, 9.5f, 6f, 20f, 18f, 20f, 18f, 9.5f),
            OfficeIconShape.Polyline(10f, 20f, 10f, 14f, 14f, 14f, 14f, 20f)
        ],

        OfficeShellIcon.NewDocument =>
        [
            .. Page(),
            OfficeIconShape.Line(12.25f, 11f, 12.25f, 17f),
            OfficeIconShape.Line(9.25f, 14f, 15.25f, 14f)
        ],

        OfficeShellIcon.Document =>
        [
            .. Page(),
            OfficeIconShape.Line(9f, 12f, 15.5f, 12f),
            OfficeIconShape.Line(9f, 15.5f, 15.5f, 15.5f)
        ],

        OfficeShellIcon.Open =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(3.5f, 19f),
                OfficeIconVertex.LineTo(3.5f, 5.5f),
                OfficeIconVertex.LineTo(9.5f, 5.5f),
                OfficeIconVertex.LineTo(11.5f, 8f),
                OfficeIconVertex.LineTo(20.5f, 8f),
                OfficeIconVertex.LineTo(20.5f, 19f),
                OfficeIconVertex.Close),
            OfficeIconShape.Line(3.5f, 11f, 20.5f, 11f)
        ],

        OfficeShellIcon.Info =>
        [
            OfficeIconShape.Circle(12f, 12f, 9f),
            OfficeIconShape.Line(12f, 11f, 12f, 16.5f),
            OfficeIconShape.Circle(12f, 7.8f, 0.6f)
        ],

        OfficeShellIcon.Print =>
        [
            OfficeIconShape.Polyline(7f, 8.5f, 7f, 3.5f, 17f, 3.5f, 17f, 8.5f),
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(7f, 16.5f),
                OfficeIconVertex.LineTo(3.5f, 16.5f),
                OfficeIconVertex.LineTo(3.5f, 8.5f),
                OfficeIconVertex.LineTo(20.5f, 8.5f),
                OfficeIconVertex.LineTo(20.5f, 16.5f),
                OfficeIconVertex.LineTo(17f, 16.5f)),
            OfficeIconShape.Rectangle(7f, 13.5f, 10f, 7f, 0.5f)
        ],

        OfficeShellIcon.Export =>
        [
            OfficeIconShape.Polyline(13f, 3.5f, 5f, 3.5f, 5f, 20.5f, 13f, 20.5f),
            OfficeIconShape.Line(10f, 12f, 20.5f, 12f),
            OfficeIconShape.Polyline(17f, 8.5f, 20.5f, 12f, 17f, 15.5f)
        ],

        OfficeShellIcon.History =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(4.3f, 9f),
                OfficeIconVertex.CurveTo(5.6f, 5.8f, 8.6f, 3.5f, 12.2f, 3.5f),
                OfficeIconVertex.CurveTo(16.9f, 3.5f, 20.5f, 7.3f, 20.5f, 12f),
                OfficeIconVertex.CurveTo(20.5f, 16.7f, 16.9f, 20.5f, 12.2f, 20.5f),
                OfficeIconVertex.CurveTo(8.1f, 20.5f, 4.7f, 17.6f, 3.9f, 13.7f)),
            OfficeIconShape.Polyline(3.5f, 4.5f, 4.3f, 9f, 8.8f, 8.3f),
            OfficeIconShape.Polyline(12f, 7.5f, 12f, 12f, 15f, 14f)
        ],

        OfficeShellIcon.Options => Gear(),

        OfficeShellIcon.Minus => [OfficeIconShape.Line(5f, 12f, 19f, 12f)],

        OfficeShellIcon.Plus =>
        [
            OfficeIconShape.Line(5f, 12f, 19f, 12f),
            OfficeIconShape.Line(12f, 5f, 12f, 19f)
        ],

        OfficeShellIcon.Close =>
        [
            OfficeIconShape.Line(6f, 6f, 18f, 18f),
            OfficeIconShape.Line(18f, 6f, 6f, 18f)
        ],

        OfficeShellIcon.ChevronDown => [OfficeIconShape.Polyline(6f, 9.5f, 12f, 15.5f, 18f, 9.5f)],
        OfficeShellIcon.ChevronUp => [OfficeIconShape.Polyline(6f, 14.5f, 12f, 8.5f, 18f, 14.5f)],
        OfficeShellIcon.ChevronLeft => [OfficeIconShape.Polyline(14.5f, 6f, 8.5f, 12f, 14.5f, 18f)],
        OfficeShellIcon.ChevronRight => [OfficeIconShape.Polyline(9.5f, 6f, 15.5f, 12f, 9.5f, 18f)],

        OfficeShellIcon.GalleryMore =>
        [
            OfficeIconShape.Line(6f, 7f, 18f, 7f),
            OfficeIconShape.Polyline(6f, 11f, 12f, 17f, 18f, 11f)
        ],

        OfficeShellIcon.Headings =>
        [
            OfficeIconShape.Line(4f, 6f, 20f, 6f),
            OfficeIconShape.Line(8f, 10f, 20f, 10f),
            OfficeIconShape.Line(12f, 14f, 20f, 14f),
            OfficeIconShape.Line(4f, 18f, 20f, 18f)
        ],

        OfficeShellIcon.Pages =>
        [
            OfficeIconShape.Rectangle(4f, 3.5f, 7f, 8.5f, 1f),
            OfficeIconShape.Rectangle(13f, 3.5f, 7f, 8.5f, 1f),
            OfficeIconShape.Rectangle(4f, 14f, 7f, 7f, 1f),
            OfficeIconShape.Rectangle(13f, 14f, 7f, 7f, 1f)
        ],

        OfficeShellIcon.Person =>
        [
            OfficeIconShape.Circle(12f, 8.5f, 3.8f),
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(4.5f, 20f),
                OfficeIconVertex.CurveTo(5.5f, 15.8f, 8.5f, 13.8f, 12f, 13.8f),
                OfficeIconVertex.CurveTo(15.5f, 13.8f, 18.5f, 15.8f, 19.5f, 20f))
        ],

        OfficeShellIcon.Inspect =>
        [
            OfficeIconShape.Polyline(11f, 20.5f, 4.5f, 20.5f, 4.5f, 3.5f, 15.5f, 3.5f, 15.5f, 9.5f),
            OfficeIconShape.Circle(15.5f, 15.5f, 3.5f),
            OfficeIconShape.Line(18f, 18f, 21f, 21f)
        ],

        OfficeShellIcon.More =>
        [
            OfficeIconShape.Circle(6f, 12f, 0.7f),
            OfficeIconShape.Circle(12f, 12f, 0.7f),
            OfficeIconShape.Circle(18f, 12f, 0.7f)
        ],

        OfficeShellIcon.Menu =>
        [
            OfficeIconShape.Line(4f, 7f, 20f, 7f),
            OfficeIconShape.Line(4f, 12f, 20f, 12f),
            OfficeIconShape.Line(4f, 17f, 20f, 17f)
        ],

        OfficeShellIcon.Check => [OfficeIconShape.Polyline(5f, 12.5f, 10f, 17.5f, 19.5f, 7f)],

        OfficeShellIcon.Collapse => [OfficeIconShape.Polyline(9.5f, 6f, 15.5f, 12f, 9.5f, 18f)],
        OfficeShellIcon.Expand => [OfficeIconShape.Polyline(6f, 9.5f, 12f, 15.5f, 18f, 9.5f)],

        OfficeShellIcon.FitToWindow => SlideIcons.Shapes(SlideIcon.FitToWindow),
        OfficeShellIcon.Notes => Shapes(OfficeIcon.Notes),
        OfficeShellIcon.SlideShow => Shapes(OfficeIcon.SlideShow),

        _ => []
    };


    /// <summary>A page with its corner turned down.</summary>
    static OfficeIconShape[] Page() =>
    [
        OfficeIconShape.Path(
            OfficeIconVertex.MoveTo(6f, 3.5f),
            OfficeIconVertex.LineTo(14f, 3.5f),
            OfficeIconVertex.LineTo(18.5f, 8f),
            OfficeIconVertex.LineTo(18.5f, 20.5f),
            OfficeIconVertex.LineTo(6f, 20.5f),
            OfficeIconVertex.Close),
        OfficeIconShape.Polyline(14f, 3.5f, 14f, 8f, 18.5f, 8f)
    ];


    static OfficeIconShape[] Pencil(float dx, float dy) =>
    [
        OfficeIconShape.Path(
            OfficeIconVertex.MoveTo(4f + dx, 20f + dy),
            OfficeIconVertex.LineTo(5f + dx, 15.5f + dy),
            OfficeIconVertex.LineTo(15.5f + dx, 5f + dy),
            OfficeIconVertex.CurveTo(16.3f + dx, 4.2f + dy, 17.7f + dx, 4.2f + dy, 18.5f + dx, 5f + dy),
            OfficeIconVertex.LineTo(19f + dx, 5.5f + dy),
            OfficeIconVertex.CurveTo(19.8f + dx, 6.3f + dy, 19.8f + dx, 7.7f + dy, 19f + dx, 8.5f + dy),
            OfficeIconVertex.LineTo(8.5f + dx, 19f + dy),
            OfficeIconVertex.Close),
        OfficeIconShape.Line(14f + dx, 6.5f + dy, 17.5f + dx, 10f + dy)
    ];


    /// <summary>A hub with eight teeth — spokes rather than a traced outline, so it strokes at the set's weight.</summary>
    static OfficeIconShape[] Gear()
    {
        var shapes = new List<OfficeIconShape>
        {
            OfficeIconShape.Circle(12f, 12f, 2.8f),
            OfficeIconShape.Circle(12f, 12f, 6.5f)
        };

        for (var i = 0; i < 8; i++)
        {
            var angle = i * Math.PI / 4;
            var (sin, cos) = Math.SinCos(angle);
            shapes.Add(OfficeIconShape.Line(
                (float)(12 + (6.5 * cos)), (float)(12 + (6.5 * sin)),
                (float)(12 + (9.2 * cos)), (float)(12 + (9.2 * sin))));
        }

        return [.. shapes];
    }
}
