using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Document;

/// <summary>How a document is presented.</summary>
public enum DocumentPageLayout
{
    /// <summary>
    /// One continuous column, wrapped to whatever width the control has.
    /// </summary>
    /// <remarks>
    /// The right default for reading, and the only sensible one on a phone: a fixed page width there
    /// means pinch-zooming to read. Headers, footers and page numbers have nowhere to go in this mode
    /// and are not drawn.
    /// </remarks>
    Reflow,

    /// <summary>
    /// Discrete pages at the document's own paper size, with its margins, headers and footers.
    /// </summary>
    Print
}


/// <summary>Which of a section's three header/footer variants applies to a page.</summary>
public enum DocumentPageKind
{
    Default = 0,
    First,
    Even
}


/// <summary>Where a page number is placed in the header or footer.</summary>
public enum PageNumberPosition
{
    Left,
    Center,
    Right
}


/// <summary>How a page number reads.</summary>
public enum PageNumberFormat
{
    /// <summary>Just the number: <c>7</c>.</summary>
    Plain,

    /// <summary><c>Page 7 of 12</c>.</summary>
    PageOfCount
}


/// <summary>Where a page number goes.</summary>
public enum PageNumberPlacement
{
    Footer,
    Header
}


/// <summary>One header or footer, read into the same block model the body uses.</summary>
public sealed record DocumentHeaderFooter(IReadOnlyList<DocumentBlock> Blocks)
{
    public static readonly DocumentHeaderFooter Empty = new([]);

    public bool IsEmpty => this.Blocks.Count == 0;
}


/// <summary>
/// A section's headers and footers, by page kind.
/// </summary>
/// <remarks>
/// Lookups fall back to <see cref="DocumentPageKind.Default"/> rather than returning nothing: a
/// document can declare a first-page header and leave the even one absent, and Word draws the default
/// there. Falling back is also what makes <c>w:titlePg</c> with an empty first-page header do the
/// right thing — an explicitly empty part is a header that draws nothing, which is different from an
/// absent one.
/// </remarks>
public sealed class DocumentHeaderFooterSet
{
    public static readonly DocumentHeaderFooterSet Empty = new();

    readonly Dictionary<DocumentPageKind, DocumentHeaderFooter> headers = new();
    readonly Dictionary<DocumentPageKind, DocumentHeaderFooter> footers = new();

    internal void SetHeader(DocumentPageKind kind, DocumentHeaderFooter value) => this.headers[kind] = value;

    internal void SetFooter(DocumentPageKind kind, DocumentHeaderFooter value) => this.footers[kind] = value;

    internal void Clear()
    {
        this.headers.Clear();
        this.footers.Clear();
    }

    public bool HasAny => this.headers.Count > 0 || this.footers.Count > 0;

    public DocumentHeaderFooter? Header(DocumentPageKind kind) => Resolve(this.headers, kind);

    public DocumentHeaderFooter? Footer(DocumentPageKind kind) => Resolve(this.footers, kind);

    static DocumentHeaderFooter? Resolve(Dictionary<DocumentPageKind, DocumentHeaderFooter> map, DocumentPageKind kind)
    {
        if (map.TryGetValue(kind, out var exact))
            return exact;

        return map.TryGetValue(DocumentPageKind.Default, out var fallback) ? fallback : null;
    }
}


/// <summary>
/// One page ready to draw, with its header and footer already laid out for it.
/// </summary>
/// <remarks>
/// Built by the controller rather than the painter because resolving a page number and laying the
/// header out is document work, and because doing it only for the pages actually on screen means a
/// hundred-page document lays out two headers per frame rather than a hundred.
/// </remarks>
public sealed record DocumentPageView(DocumentPage Page, DocumentChromeLayout? Header, DocumentChromeLayout? Footer)
{
    /// <summary>The footnotes cited on this page, laid out to sit at its foot. Null when there are none.</summary>
    public DocumentChromeLayout? Footnotes { get; init; }
}


/// <summary>One page of a paginated document.</summary>
/// <param name="Index">Zero-based page index.</param>
/// <param name="FlowTop">Where this page's slice of the continuous flow starts.</param>
/// <param name="FlowBottom">One past the end of this page's slice of the flow.</param>
/// <param name="ViewTop">Where the page's paper starts in the scrolling view.</param>
public sealed record DocumentPage(int Index, double FlowTop, double FlowBottom, double ViewTop)
{
    /// <summary>The one-based number a reader would call this page.</summary>
    public int Number => this.Index + 1;
}


/// <summary>
/// A document's flow sliced into pages, plus the mapping between the two coordinate spaces.
/// </summary>
/// <remarks>
/// <para>
/// Pagination here is <b>slicing, not re-layout</b>. The flow is laid out exactly once, continuously,
/// and pages are ranges of it. Nothing downstream has to know: hit-testing, selection rectangles, the
/// caret and spell-check spans all keep working in flow coordinates, and only painting and pointer
/// input convert. Re-laying-out per page would have meant every one of those growing a page argument.
/// </para>
/// <para>
/// The cost of slicing is that a line is never split across a boundary and never re-wrapped by one —
/// which is what you want — but a block taller than a page overflows its page rather than being
/// broken. That is an image or a table row bigger than the paper, and Word overflows it too.
/// </para>
/// </remarks>
public sealed class DocumentPagination
{
    /// <summary>The single-page view used in reflow, where the whole flow is one unbroken page.</summary>
    public static DocumentPagination Reflowed(double flowHeight, double viewportHeight)
        => new(
            [new DocumentPage(0, 0, double.PositiveInfinity, 0)],
            PageSetup.Letter,
            0,
            Math.Max(flowHeight, viewportHeight))
        {
            IsPaginated = false
        };

    DocumentPagination(IReadOnlyList<DocumentPage> pages, PageSetup setup, double gap, double viewHeight)
    {
        this.Pages = pages;
        this.Setup = setup;
        this.Gap = gap;
        this.ViewHeight = viewHeight;
    }

    public IReadOnlyList<DocumentPage> Pages { get; }

    public PageSetup Setup { get; }

    /// <summary>Vertical space between one sheet of paper and the next.</summary>
    public double Gap { get; }

    /// <summary>Total scrollable height, paper and gaps included.</summary>
    public double ViewHeight { get; }

    /// <summary>False for the synthetic single page reflow uses.</summary>
    public bool IsPaginated { get; private init; } = true;

    public int Count => this.Pages.Count;

    /// <summary>The page a point in the continuous flow falls on.</summary>
    public DocumentPage PageAtFlow(double flowY)
    {
        for (var i = 0; i < this.Pages.Count; i++)
        {
            if (flowY < this.Pages[i].FlowBottom)
                return this.Pages[i];
        }

        return this.Pages[^1];
    }

    /// <summary>The page a point in the scrolling view falls on, gaps resolving to the nearer page.</summary>
    public DocumentPage PageAtView(double viewY)
    {
        for (var i = 0; i < this.Pages.Count; i++)
        {
            if (viewY < this.Pages[i].ViewTop + this.Setup.Height + this.Gap)
                return this.Pages[i];
        }

        return this.Pages[^1];
    }

    /// <summary>Turns a Y in the continuous flow into a Y in the scrolling view.</summary>
    public double FlowToView(double flowY)
    {
        if (!this.IsPaginated)
            return flowY;

        var page = this.PageAtFlow(flowY);
        return page.ViewTop + this.Setup.MarginTop + (flowY - page.FlowTop);
    }

    /// <summary>
    /// Turns a Y in the scrolling view back into a Y in the continuous flow.
    /// </summary>
    /// <remarks>
    /// A point in a margin or in the gap between sheets has no flow position of its own, so it clamps
    /// into the nearest page's content band. That is what makes clicking in the margin place the caret
    /// on the nearest line rather than doing nothing.
    /// </remarks>
    public double ViewToFlow(double viewY)
    {
        if (!this.IsPaginated)
            return viewY;

        var page = this.PageAtView(viewY);
        var withinPage = viewY - page.ViewTop - this.Setup.MarginTop;
        var contentHeight = page.FlowBottom - page.FlowTop;

        if (Double.IsPositiveInfinity(contentHeight))
            contentHeight = this.Setup.ContentHeight;

        return page.FlowTop + Math.Clamp(withinPage, 0, Math.Max(0, contentHeight));
    }

    /// <summary>Vertical offset added to flow coordinates to draw them on <paramref name="page"/>.</summary>
    public double ContentOffsetFor(DocumentPage page)
        => this.IsPaginated ? page.ViewTop + this.Setup.MarginTop - page.FlowTop : 0;

    /// <summary>The pages whose paper intersects the visible band.</summary>
    public IEnumerable<DocumentPage> Visible(double scrollY, double viewportHeight)
    {
        var top = scrollY;
        var bottom = scrollY + viewportHeight;

        foreach (var page in this.Pages)
        {
            if (!this.IsPaginated)
            {
                yield return page;
                continue;
            }

            var pageBottom = page.ViewTop + this.Setup.Height;
            if (pageBottom < top)
                continue;

            if (page.ViewTop > bottom)
                yield break;

            yield return page;
        }
    }


    /// <summary>
    /// Slices a laid-out flow into pages.
    /// </summary>
    /// <param name="blocks">The continuous layout.</param>
    /// <param name="flowHeight">Its total height.</param>
    /// <param name="setup">Paper size and margins.</param>
    /// <param name="gap">Space drawn between sheets.</param>
    public static DocumentPagination Paginate(
        IReadOnlyList<LaidOutBlock> blocks,
        double flowHeight,
        PageSetup setup,
        double gap = 24,
        IReadOnlyList<NoteReservation>? notes = null
    )
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(setup);

        var contentHeight = setup.ContentHeight;
        var breaks = new List<double> { 0 };
        var pageTop = 0d;

        // Footnotes are drawn at the foot of the page that cites them, so the room they take is room
        // the body does not have. Each is charged to the page where its citing line lands; a line whose
        // note would not fit moves to the next page along with the note.
        var sorted = notes is { Count: > 0 } ? notes.OrderBy(x => x.FlowY).ToList() : null;
        var next = 0;
        var reserved = 0d;

        foreach (var atom in Atoms(blocks))
        {
            var pending = 0d;
            var cursor = next;

            if (sorted is not null)
            {
                while (cursor < sorted.Count && sorted[cursor].FlowY < atom.Bottom)
                {
                    pending += sorted[cursor].Height;
                    cursor++;
                }

                // The separator rule above the first note on a page.
                if (pending > 0 && reserved == 0)
                    pending += NoteSeparatorHeight;
            }

            // Nothing to do for the atom that opens the page it is already on.
            if (atom.Top <= pageTop && !atom.StartsPage)
            {
                reserved += pending;
                next = cursor;
                continue;
            }

            var forced = atom.StartsPage && atom.Top > pageTop;
            var overflows = atom.Bottom - pageTop + reserved + pending > contentHeight && atom.Top > pageTop;

            if (!forced && !overflows)
            {
                reserved += pending;
                next = cursor;
                continue;
            }

            pageTop = atom.Top;
            breaks.Add(pageTop);

            // The new page starts with only this atom's notes charged against it.
            reserved = pending > 0 && reserved > 0 ? pending + NoteSeparatorHeight : pending;
            next = cursor;
        }

        var pages = new List<DocumentPage>(breaks.Count);
        for (var i = 0; i < breaks.Count; i++)
        {
            var top = breaks[i];
            var bottom = i + 1 < breaks.Count ? breaks[i + 1] : Math.Max(flowHeight, top + contentHeight);
            pages.Add(new DocumentPage(i, top, bottom, i * (setup.Height + gap)));
        }

        var viewHeight = pages.Count * (setup.Height + gap) - gap;
        return new DocumentPagination(pages, setup, gap, viewHeight);
    }

    /// <summary>
    /// The smallest things a page boundary may fall between: a line of text, a table row, a rule.
    /// </summary>
    /// <remarks>
    /// Deliberately not "a block". Breaking only between blocks would push a paragraph longer than a
    /// page onto a page of its own and leave most of the previous one blank, which is exactly the
    /// wrong-looking pagination the reflow-only design was avoiding.
    /// </remarks>
    static IEnumerable<Atom> Atoms(IReadOnlyList<LaidOutBlock> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case LaidOutParagraph paragraph:
                    var spacing = paragraph.Format.LineSpacing;
                    for (var i = 0; i < paragraph.Lines.Count; i++)
                    {
                        var line = paragraph.Lines[i];
                        var top = paragraph.Y + line.Y;
                        yield return new Atom(
                            top,
                            top + line.Height * spacing,
                            line.StartsPage || (i == 0 && paragraph.StartsPage));
                    }

                    break;

                case LaidOutTable table:
                    // Rows are recovered from the cells' tops: the laid-out table keeps cells, not
                    // rows, and a vertically merged cell spans several of them.
                    var rowTops = table.Cells.Select(x => x.Y).Distinct().OrderBy(x => x).ToList();
                    for (var i = 0; i < rowTops.Count; i++)
                    {
                        var top = rowTops[i];
                        var bottom = i + 1 < rowTops.Count ? rowTops[i + 1] : table.Y + table.Height;
                        yield return new Atom(top, bottom, i == 0 && table.StartsPage);
                    }

                    if (rowTops.Count == 0)
                        yield return new Atom(table.Y, table.Y + table.Height, table.StartsPage);

                    break;

                default:
                    yield return new Atom(block.Y, block.Y + block.Height, block.StartsPage);
                    break;
            }
        }
    }

    readonly record struct Atom(double Top, double Bottom, bool StartsPage);

    /// <summary>The space a note separator takes above the first note on a page.</summary>
    public const double NoteSeparatorHeight = 12;
}


/// <summary>A footnote's claim on the page its citing line lands on.</summary>
/// <param name="FlowY">The top of the line the reference is on, in flow coordinates.</param>
/// <param name="Height">The note's laid-out height.</param>
public readonly record struct NoteReservation(double FlowY, double Height);
