using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Document;

/// <summary>
/// Host-independent state for the document viewer: layout at the current width, scrolling and zoom.
/// </summary>
/// <remarks>
/// Layout is the expensive part and only depends on width, so it is cached and rebuilt on resize or
/// zoom rather than on every scroll or repaint.
/// </remarks>
public partial class DocumentController
{
    readonly DocumentLayoutEngine engine;
    readonly Dictionary<(bool Header, int Page), DocumentChromeLayout?> chrome = new();
    DocumentLayoutResult? layout;
    DocumentPagination? pagination;
    double laidOutWidth = -1;
    int laidOutFontGeneration = -1;
    double zoom = 1.0;
    DocumentPageLayout pageLayout = DocumentPageLayout.Reflow;

    public DocumentController(WordDocument document, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(measurer);

        this.Document = document;
        this.Measurer = measurer;
        this.engine = new DocumentLayoutEngine(measurer);
        this.Viewport = new DocumentViewport();
    }

    public WordDocument Document { get; }

    public DocumentViewport Viewport { get; }

    /// <summary>
    /// Zoom factor, and it means two different things by design.
    /// </summary>
    /// <remarks>
    /// In <see cref="DocumentPageLayout.Reflow"/> it changes the measure, so zooming in re-wraps the
    /// text wider — which is what you want when the page is notional. In
    /// <see cref="DocumentPageLayout.Print"/> it is a straight scale, because the page is a real sheet
    /// of paper and re-wrapping it at a different width would move every page break as you zoomed.
    /// </remarks>
    public double Zoom
    {
        get => this.zoom;
        set
        {
            var clamped = Math.Clamp(value, 0.25, 4.0);
            if (Math.Abs(clamped - this.zoom) < 0.001)
                return;

            this.zoom = clamped;
            this.laidOutWidth = -1;
            this.ApplyViewport();
            this.ZoomChanged?.Invoke(this, EventArgs.Empty);
            this.Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Continuous column, or discrete pages with the document's own headers and footers.</summary>
    public DocumentPageLayout PageLayout
    {
        get => this.pageLayout;
        set
        {
            if (this.pageLayout == value)
                return;

            this.pageLayout = value;
            this.laidOutWidth = -1;
            this.ApplyViewport();
            this.Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>True when pages, headers, footers and page numbers are being drawn.</summary>
    public bool IsPaginated => this.pageLayout == DocumentPageLayout.Print;

    /// <summary>Vertical space drawn between one sheet of paper and the next.</summary>
    public double PageGap { get; set; } = 24;

    /// <summary>
    /// The scale the painter applies on top of the device scale, so that layout units and control
    /// units are not the same thing.
    /// </summary>
    /// <remarks>
    /// 1 in reflow, where zoom has already been spent on the measure. The zoom factor in print, where
    /// it has not.
    /// </remarks>
    public double ViewScale => this.IsPaginated ? this.zoom : 1.0;

    /// <summary>
    /// Maximum page width in pixels. The page is centred and never stretched past this, so a document
    /// on a wide monitor reads like a page rather than one very long line.
    /// </summary>
    public double MaxPageWidth { get; set; } = 900;

    /// <summary>Horizontal padding inside the page, added to the document's own margins.</summary>
    public double PagePadding { get; set; } = 24;

    public event EventHandler? Changed;

    /// <summary>The measurer, exposed so a subclass can do its own hit-testing.</summary>
    protected ITextMeasurer Measurer { get; }

    /// <summary>Forces a re-layout on the next access, after the document's content has changed.</summary>
    protected void InvalidateLayout() => this.laidOutWidth = -1;

    protected void RaiseChanged() => this.Changed?.Invoke(this, EventArgs.Empty);

    public IReadOnlyList<LaidOutBlock> Blocks => this.EnsureLayout().Blocks;

    /// <summary>The flow sliced into pages. A single unbroken page in reflow.</summary>
    public DocumentPagination Pagination
    {
        get
        {
            this.EnsureLayout();
            return this.pagination!;
        }
    }

    /// <summary>The page panel's width, in layout units.</summary>
    public double PageWidth => this.IsPaginated
        ? this.Document.Page.Width
        : Math.Min(this.MaxPageWidth * this.zoom, Math.Max(100, this.Viewport.Width));

    /// <summary>The page panel's height, or the whole viewport in reflow where there is no paper.</summary>
    public double PageHeight => this.IsPaginated ? this.Document.Page.Height : this.Viewport.Height;

    /// <summary>The page panel's left edge, centring it in the viewport.</summary>
    /// <summary>
    /// The page's left edge in the viewport: centred when there is room, and pulled left by the
    /// horizontal scroll when there is not.
    /// </summary>
    /// <remarks>
    /// Every other horizontal coordinate is derived from this one - <see cref="ContentX"/>, the
    /// painter's origin, and the inverse in <see cref="ToFlow"/> - so subtracting the scroll here is
    /// what makes hit-testing, the caret and the painted page all move together. Doing it in the
    /// painter alone would draw a scrolled page that still answered taps at its old position.
    /// </remarks>
    public double PageX => Math.Max(0, (this.Viewport.Width - this.PageWidth) / 2) - this.Viewport.ScrollX;

    /// <summary>How far content is inset from the page's left edge.</summary>
    /// <remarks>The document's own left margin in print; a cosmetic gutter in reflow.</remarks>
    public double ContentInset => this.IsPaginated ? this.Document.Page.MarginLeft : this.PagePadding;

    /// <summary>The left edge of content, in layout units. Not the same as <see cref="PageX"/>.</summary>
    public double ContentX => this.PageX + this.ContentInset;

    /// <summary>Width available to content inside the page.</summary>
    public double ContentWidth => this.IsPaginated
        ? this.Document.Page.ContentWidth
        : Math.Max(50, this.PageWidth - this.PagePadding * 2);

    double controlWidth = 800;
    double controlHeight = 600;

    /// <summary>
    /// Resizes to a control measured in device-independent pixels.
    /// </summary>
    /// <remarks>
    /// The viewport is kept in <em>layout</em> units, which in print are the paper's, so everything
    /// downstream — centring, hit-testing, scrolling — works in one coordinate space and only the
    /// painter and the pointer entry points convert.
    /// </remarks>
    public void Resize(double width, double height)
    {
        this.controlWidth = width;
        this.controlHeight = height;
        this.ApplyViewport();
        this.EnsureLayout();
        this.OnViewportChanged();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Re-derives the viewport from the control's size at the current scale.
    /// </summary>
    /// <remarks>
    /// Zooming changes how many layout units fit in the same control, but the control itself has not
    /// resized — so nothing calls <see cref="Resize"/> and the viewport would keep the previous
    /// scale's units. What that looks like is a page that drifts off-centre as you zoom, because the
    /// width it is being centred in is the wrong one.
    /// </remarks>
    void ApplyViewport()
    {
        var scale = this.ViewScale;
        this.Viewport.Width = this.controlWidth / scale;
        this.Viewport.Height = this.controlHeight / scale;
    }

    /// <summary>Scrolls by a delta given in control pixels.</summary>
    /// <remarks>
    /// A wheel notch is the same physical distance whatever the zoom, so the delta is converted
    /// rather than passed through — otherwise scrolling covers half the document per notch at 50%.
    /// </remarks>
    public void ScrollByControlPixels(double delta) => this.Scroll(delta / this.ViewScale);

    /// <summary>Turns a point in control coordinates into one in the continuous flow.</summary>
    public (double X, double Y) ToFlow(double x, double y)
    {
        var scale = this.ViewScale;
        var viewY = (y / scale) + this.Viewport.ScrollY;
        return ((x / scale) - this.ContentX, this.Pagination.ViewToFlow(viewY));
    }

    /// <summary>Scrolls the page sideways, in layout units.</summary>
    /// <remarks>
    /// Separate from <see cref="Scroll"/> rather than a second parameter on it: the vertical axis is
    /// driven by a wheel and by the caret moving, neither of which has a horizontal component to pass.
    /// </remarks>
    public void ScrollHorizontally(double delta)
    {
        this.EnsureLayout();
        this.Viewport.ScrollByX(delta);
        this.OnViewportChanged();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Scrolls sideways to an absolute offset, in layout units.</summary>
    public void ScrollToHorizontal(double x)
    {
        this.EnsureLayout();
        this.Viewport.ScrollToX(x);
        this.OnViewportChanged();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Scroll(double delta)
    {
        this.EnsureLayout();
        this.Viewport.ScrollBy(delta);
        this.OnViewportChanged();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ScrollTo(double y)
    {
        this.EnsureLayout();
        this.Viewport.ScrollTo(y);
        this.OnViewportChanged();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Called after the visible region moves or resizes.
    /// </summary>
    /// <remarks>
    /// A viewer has nothing to do here; the editor uses it to spell-check the paragraphs that have
    /// just come on screen, since work is deliberately confined to what is actually visible.
    /// </remarks>
    protected virtual void OnViewportChanged()
    {
    }

    /// <summary>Scrolls to a heading from <see cref="WordDocument.Outline"/>, by its index in that list.</summary>
    public void ScrollToHeading(int outlineIndex)
    {
        var result = this.EnsureLayout();
        var headings = result.Blocks
            .OfType<LaidOutParagraph>()
            .Where(x => x.Format.OutlineLevel > 0)
            .ToList();

        if (outlineIndex < 0 || outlineIndex >= headings.Count)
            return;

        // Headings are in flow coordinates; scrolling happens in the paginated view's.
        this.Viewport.ScrollTo(this.Pagination.FlowToView(headings[outlineIndex].Y) - 8);
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    DocumentLayoutResult EnsureLayout()
    {
        var width = this.ContentWidth;
        var generation = this.Measurer.FontGeneration;

        // Width alone is not enough of a key. Fonts can arrive after the first layout — they are
        // fetched over the network on WebAssembly — and in print the width never changes, so a
        // layout measured against the fallback face would otherwise be kept for the life of the view.
        if (this.layout is not null
            && this.pagination is not null
            && generation == this.laidOutFontGeneration
            && Math.Abs(width - this.laidOutWidth) < 0.5)
        {
            return this.layout;
        }

        this.layout = this.engine.Layout(this.Document.Blocks, width);
        this.laidOutWidth = width;
        this.IndexStory(this.layout);
        this.laidOutFontGeneration = generation;
        this.chrome.Clear();

        this.pagination = this.IsPaginated
            ? DocumentPagination.Paginate(this.layout.Blocks, this.layout.Height, this.Document.Page, this.PageGap, this.FootnoteReservations())
            : DocumentPagination.Reflowed(this.layout.Height, this.Viewport.Height);

        this.Viewport.ContentHeight = this.pagination.ViewHeight;
        this.Viewport.ContentWidth = this.PageWidth;

        // A width change can leave either scroll offset past the new end of the document. Re-applying
        // both re-clamps them: widening the viewport past the page has to bring the sideways offset
        // back to zero, or the page stays pushed off-centre with nothing left to scroll to.
        this.Viewport.ScrollTo(this.Viewport.ScrollY);
        this.Viewport.ScrollToX(this.Viewport.ScrollX);
        return this.layout;
    }

    /// <summary>
    /// The pages currently on screen, each with its header and footer laid out.
    /// </summary>
    /// <remarks>
    /// Empty in reflow, which is also how the painter is told to draw one continuous panel instead of
    /// sheets — the two modes are told apart by whether there are pages, not by a flag that could
    /// disagree with them.
    /// </remarks>
    public IReadOnlyList<DocumentPageView> VisiblePages()
    {
        if (!this.IsPaginated)
            return [];

        var pagination = this.Pagination;
        var views = new List<DocumentPageView>();

        foreach (var page in pagination.Visible(this.Viewport.ScrollY, this.Viewport.Height))
            views.Add(new DocumentPageView(page, this.HeaderFor(page), this.FooterFor(page)) { Footnotes = this.FootnotesFor(page) });

        return views;
    }

    /// <summary>The laid-out header for a page, or null when there is none to draw.</summary>
    public DocumentChromeLayout? HeaderFor(DocumentPage page) => this.ChromeFor(page, header: true);

    /// <summary>The laid-out footer for a page, or null when there is none to draw.</summary>
    public DocumentChromeLayout? FooterFor(DocumentPage page) => this.ChromeFor(page, header: false);

    DocumentChromeLayout? ChromeFor(DocumentPage page, bool header)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (!this.IsPaginated)
            return null;

        var key = (header, page.Index);
        if (this.chrome.TryGetValue(key, out var cached))
            return cached;

        var setup = this.Document.Page;
        var kind = setup.KindOf(page.Number);
        var source = header
            ? this.Document.HeadersFooters.Header(kind)
            : this.Document.HeadersFooters.Footer(kind);

        DocumentChromeLayout? result = null;

        if (source is { IsEmpty: false })
        {
            // Laid out per page rather than once, because a PAGE field's text differs on every one
            // and a number that grew a digit would otherwise be measured at the old width.
            var resolved = DocumentFields.Resolve(source.Blocks, page.Number, this.Pagination.Count);
            var laidOut = this.engine.Layout(resolved, setup.ContentWidth);
            result = new DocumentChromeLayout(laidOut.Blocks, laidOut.Height);
        }

        this.chrome[key] = result;
        return result;
    }
}


/// <summary>A header or footer, laid out for one page.</summary>
public sealed record DocumentChromeLayout(IReadOnlyList<LaidOutBlock> Blocks, double Height);
