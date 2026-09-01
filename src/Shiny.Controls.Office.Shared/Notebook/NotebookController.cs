using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Notebook;

/// <summary>A rectangle in viewport coordinates.</summary>
public readonly record struct NoteRect(double X, double Y, double Width, double Height)
{
    public double Right => this.X + this.Width;
    public double Bottom => this.Y + this.Height;

    public bool Contains(double x, double y)
        => x >= this.X && x <= this.Right && y >= this.Y && y <= this.Bottom;

    public bool Intersects(NoteRect other)
        => this.X < other.Right && other.X < this.Right && this.Y < other.Bottom && other.Y < this.Bottom;

    public static NoteRect FromCorners(double x1, double y1, double x2, double y2)
        => new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
}

/// <summary>
/// Host-independent view state for a notebook: which page, how far scrolled, and at what zoom.
/// </summary>
/// <remarks>
/// <para>
/// A page here is a scrolling canvas rather than a fitted artboard, which is the one real difference
/// from the deck. A slide is a fixed rectangle and the viewer's job is to letterbox it; a notebook page
/// has no edges — it grows to hold whatever has been written on it — so the viewer's job is to scroll
/// around it, and zoom is the user's choice rather than a fit.
/// </para>
/// <para>
/// Scroll is held in <i>content</i> pixels, meaning already multiplied by the zoom. That keeps the
/// scrollbar arithmetic the same at every zoom level, and it is what makes a zoom about the pointer
/// a two-line adjustment rather than a change of units.
/// </para>
/// </remarks>
public class NotebookController
{
    PageAddress address = new(0, 0);
    double zoom = 1;
    double scrollX;
    double scrollY;

    public NotebookController(NotebookDocument document, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(measurer);

        this.Document = document;
        this.Measurer = measurer;

        document.ContentChanged += this.OnDocumentChanged;
        document.StructureChanged += this.OnStructureChanged;
    }

    public NotebookDocument Document { get; }

    public ITextMeasurer Measurer { get; }

    public double ViewportWidth { get; private set; } = 800;

    public double ViewportHeight { get; private set; } = 600;

    public double MinZoom { get; set; } = 0.25;

    public double MaxZoom { get; set; } = 4;

    /// <summary>Raised whenever anything a view paints from has changed.</summary>
    public event EventHandler? Changed;

    protected void RaiseChanged() => this.Changed?.Invoke(this, EventArgs.Empty);

    void OnDocumentChanged(object? sender, EventArgs e)
    {
        this.InvalidateLayout();
        this.RaiseChanged();
    }

    void OnStructureChanged(object? sender, EventArgs e)
    {
        // A delete can leave the address pointing past the end of a section, or at a section that is
        // no longer there. Re-clamping here rather than in each command is what keeps every structural
        // edit — including the ones undo replays — from having to think about it.
        this.address = this.Clamp(this.address);
        this.InvalidateLayout();
        this.RaiseChanged();
    }

    // ---- current page ----

    public PageAddress Address
    {
        get => this.address;
        set
        {
            var clamped = this.Clamp(value);
            if (clamped == this.address)
                return;

            this.address = clamped;
            this.scrollX = 0;
            this.scrollY = 0;
            this.OnPageChanged();
            this.RaiseChanged();
        }
    }

    /// <summary>Called when the view moves to a different page, so an editor can drop its selection.</summary>
    protected virtual void OnPageChanged()
    {
    }

    public NotebookPage? Page => this.Document.PageAt(this.address);

    public NotebookSection? Section => this.Document.SectionAt(this.address.Section);

    PageAddress Clamp(PageAddress value)
    {
        if (this.Document.Sections.Count == 0)
            return PageAddress.None;

        var section = Math.Clamp(value.Section, 0, this.Document.Sections.Count - 1);
        var pages = this.Document.Sections[section].Pages;

        return new PageAddress(section, pages.Count == 0 ? 0 : Math.Clamp(value.Page, 0, pages.Count - 1));
    }

    public void GoToPage(string pageId)
    {
        var found = this.Document.Locate(pageId);
        if (found.IsValid)
            this.Address = found;
    }

    public bool CanGoNext => this.Section is { } s && this.address.Page < s.Pages.Count - 1;

    public bool CanGoPrevious => this.address.Page > 0;

    public void NextPage() => this.Address = this.address with { Page = this.address.Page + 1 };

    public void PreviousPage() => this.Address = this.address with { Page = this.address.Page - 1 };

    // ---- viewport ----

    public void Resize(double width, double height)
    {
        this.ViewportWidth = Math.Max(1, width);
        this.ViewportHeight = Math.Max(1, height);
        this.ClampScroll();
        this.RaiseChanged();
    }

    public double Zoom
    {
        get => this.zoom;
        set => this.SetZoom(value, this.ViewportWidth / 2, this.ViewportHeight / 2);
    }

    /// <summary>
    /// Zooms about a point in the viewport, which is what a pinch and a ctrl-wheel both need.
    /// </summary>
    /// <remarks>
    /// Zooming about the viewport centre instead is the difference between "the thing under my fingers
    /// got bigger" and "the page jumped somewhere else and got bigger" — the anchor is the whole
    /// gesture.
    /// </remarks>
    public void SetZoom(double value, double anchorX, double anchorY)
    {
        var next = Math.Clamp(value, this.MinZoom, this.MaxZoom);
        if (Math.Abs(next - this.zoom) < 0.0001)
            return;

        // The page point under the anchor has to stay under it, so solve the scroll that keeps it there.
        var pageX = (anchorX + this.scrollX) / this.zoom;
        var pageY = (anchorY + this.scrollY) / this.zoom;

        this.zoom = next;
        this.scrollX = pageX * next - anchorX;
        this.scrollY = pageY * next - anchorY;

        this.ClampScroll();
        this.RaiseChanged();
    }

    public double ScrollX => this.scrollX;

    public double ScrollY => this.scrollY;

    public void ScrollBy(double dx, double dy)
    {
        this.scrollX += dx;
        this.scrollY += dy;
        this.ClampScroll();
        this.RaiseChanged();
    }

    public void ScrollTo(double x, double y)
    {
        this.scrollX = x;
        this.scrollY = y;
        this.ClampScroll();
        this.RaiseChanged();
    }

    /// <summary>The canvas size in viewport pixels — the page's extent at the current zoom.</summary>
    public (double Width, double Height) ContentSize()
    {
        if (this.Page is not { } page)
            return (this.ViewportWidth, this.ViewportHeight);

        var (width, height) = page.Extent();
        return (width * this.zoom, height * this.zoom);
    }

    protected void ClampScroll()
    {
        var (width, height) = this.ContentSize();

        // A canvas smaller than the viewport pins to the origin rather than floating: a page that
        // drifts when there is nothing to scroll reads as a rendering fault.
        this.scrollX = Math.Clamp(this.scrollX, 0, Math.Max(0, width - this.ViewportWidth));
        this.scrollY = Math.Clamp(this.scrollY, 0, Math.Max(0, height - this.ViewportHeight));
    }

    /// <summary>Scrolls the smallest amount that brings a page rectangle fully into view.</summary>
    public void ScrollIntoView(double pageX, double pageY, double width, double height, double padding = 24)
    {
        var left = pageX * this.zoom - padding;
        var top = pageY * this.zoom - padding;
        var right = (pageX + width) * this.zoom + padding;
        var bottom = (pageY + height) * this.zoom + padding;

        if (left < this.scrollX)
            this.scrollX = left;
        else if (right > this.scrollX + this.ViewportWidth)
            this.scrollX = right - this.ViewportWidth;

        if (top < this.scrollY)
            this.scrollY = top;
        else if (bottom > this.scrollY + this.ViewportHeight)
            this.scrollY = bottom - this.ViewportHeight;

        this.ClampScroll();
        this.RaiseChanged();
    }

    // ---- coordinates ----

    public (double X, double Y) ToPage(double viewportX, double viewportY)
        => ((viewportX + this.scrollX) / this.zoom, (viewportY + this.scrollY) / this.zoom);

    public (double X, double Y) ToViewport(double pageX, double pageY)
        => (pageX * this.zoom - this.scrollX, pageY * this.zoom - this.scrollY);

    public NoteRect ToViewport(NoteRect page)
        => new(
            page.X * this.zoom - this.scrollX,
            page.Y * this.zoom - this.scrollY,
            page.Width * this.zoom,
            page.Height * this.zoom);

    public NoteRect BoundsOf(NoteItem item)
    {
        var (x, y, width, height) = item.Bounds();
        return new NoteRect(x, y, width, height);
    }

    // ---- text layout ----

    readonly Dictionary<string, CachedLayout> layouts = new();

    readonly record struct CachedLayout(NoteItem Item, int FontGeneration, LaidOutTextBody Layout);

    /// <summary>
    /// The laid-out text of an item, cached until the item or the fonts change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyed on the item <i>instance</i>, not on its id. Items are immutable, so an edit produces a new
    /// record and the reference comparison invalidates the entry for free — no revision counter to keep
    /// in step, and no way for a stale layout to survive an edit.
    /// </para>
    /// <para>
    /// The font generation is part of the key for the reason the measurer documents: on WebAssembly the
    /// bundled faces arrive after the first render, so anything laid out before they land was measured
    /// against a fallback with entirely different advances.
    /// </para>
    /// </remarks>
    public LaidOutTextBody? LayoutOf(NoteItem item)
    {
        if (item.Text is not { } body)
            return null;

        var generation = this.Measurer.FontGeneration;

        if (this.layouts.TryGetValue(item.Id, out var cached) &&
            ReferenceEquals(cached.Item, item) &&
            cached.FontGeneration == generation)
        {
            return cached.Layout;
        }

        var layout = ShapeTextLayout.Layout(body, item.Width, item.Height, this.Measurer);
        this.layouts[item.Id] = new CachedLayout(item, generation, layout);

        return layout;
    }

    /// <summary>The height the item's text actually needs, which is what an auto-height container takes.</summary>
    public double MeasuredHeight(NoteItem item)
    {
        if (item.Text is not { } body || this.LayoutOf(item) is not { } layout)
            return item.Height;

        var total = 0d;
        foreach (var paragraph in layout.Paragraphs)
            total = Math.Max(total, paragraph.Y + paragraph.Height);

        return Math.Max(20, total + body.InsetTop + body.InsetBottom);
    }

    protected void InvalidateLayout()
    {
        // Bounded rather than cleared: the entries are keyed by item id and an item that is gone will
        // never be asked for again, but a page of a thousand strokes should not re-lay-out every text
        // container because one of them moved.
        if (this.layouts.Count > 512)
            this.layouts.Clear();
    }
}
