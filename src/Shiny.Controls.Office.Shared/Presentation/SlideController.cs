namespace Shiny.Controls.Office.Presentation;

public enum SlideViewMode
{
    /// <summary>One slide fitted to the viewport.</summary>
    Single,

    /// <summary>A scrolling grid of slide thumbnails.</summary>
    Grid
}

/// <summary>The rectangle one slide occupies in viewport coordinates.</summary>
public readonly record struct SlidePlacement(Slide Slide, double X, double Y, double Width, double Height);

/// <summary>
/// Host-independent state for the slide viewer: which slide, fitting, and thumbnail layout.
/// </summary>
public class SlideController
{
    int index;
    SlideViewMode mode = SlideViewMode.Single;
    double scrollY;
    bool isPresenting;

    public SlideController(SlideDeck deck)
    {
        ArgumentNullException.ThrowIfNull(deck);
        this.Deck = deck;
    }

    public SlideDeck Deck { get; }

    public double ViewportWidth { get; private set; } = 800;
    public double ViewportHeight { get; private set; } = 600;

    /// <summary>Margin around a single fitted slide. Ignored while <see cref="IsPresenting"/>.</summary>
    public double Margin { get; set; } = 16;

    /// <summary>
    /// The deck is being presented: the slide is fitted edge to edge with no margin around it.
    /// </summary>
    /// <remarks>
    /// Turning it on forces <see cref="SlideViewMode.Single"/> — a thumbnail wall is a way of finding a
    /// slide, not a way of showing one to a room. The hosts add the rest of the presentation (the
    /// fullscreen surface, the black surround, the chrome that fades out); the layout part lives here so
    /// both of them fit the slide identically.
    /// </remarks>
    public bool IsPresenting
    {
        get => this.isPresenting;
        set
        {
            if (this.isPresenting == value)
                return;

            this.isPresenting = value;

            if (value && this.mode != SlideViewMode.Single)
            {
                this.mode = SlideViewMode.Single;
                this.scrollY = 0;
            }

            this.Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The margin actually used when fitting: none while presenting.</summary>
    public double EffectiveMargin => this.isPresenting ? 0 : this.Margin;

    /// <summary>Thumbnail width in grid mode.</summary>
    public double ThumbnailWidth { get; set; } = 180;

    public double ThumbnailGap { get; set; } = 14;

    public event EventHandler? Changed;

    /// <summary>Raises <see cref="Changed"/>. The editor subclass fires it after every edit.</summary>
    protected void RaiseChanged() => this.Changed?.Invoke(this, EventArgs.Empty);

    public int Count => this.Deck.Slides.Count;

    /// <remarks>
    /// Clamped on read as well as on write: the deck can lose slides underneath a controller — a
    /// slide deleted in the editor while a viewer shares the deck — and an index left pointing past
    /// the end would throw from <see cref="Current"/> on the next paint.
    /// </remarks>
    public int Index
    {
        get => Math.Clamp(this.index, 0, Math.Max(0, this.Count - 1));
        set
        {
            var clamped = Math.Clamp(value, 0, Math.Max(0, this.Count - 1));
            if (clamped == this.index)
                return;

            this.index = clamped;
            this.Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public Slide? Current => this.Count == 0 ? null : this.Deck.Slides[this.Index];

    public SlideViewMode Mode
    {
        get => this.mode;
        set
        {
            if (this.mode == value)
                return;

            this.mode = value;
            this.scrollY = 0;
            this.Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public double ScrollY => this.scrollY;

    public bool CanGoNext => this.Index < this.Count - 1;
    public bool CanGoPrevious => this.Index > 0;

    public void Next() => this.Index = this.Index + 1;

    public void Previous() => this.Index = this.Index - 1;

    public void Resize(double width, double height)
    {
        this.ViewportWidth = Math.Max(1, width);
        this.ViewportHeight = Math.Max(1, height);
        this.ClampScroll();
        this.ClampPan();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Scroll(double delta)
    {
        if (this.mode != SlideViewMode.Grid)
            return;

        this.scrollY += delta;
        this.ClampScroll();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    void ClampScroll() => this.scrollY = Math.Clamp(this.scrollY, 0, Math.Max(0, this.GridHeight() - this.ViewportHeight));

    /// <summary>
    /// Fits the current slide inside the viewport, preserving its aspect ratio and centring it.
    /// </summary>
    /// <remarks>
    /// Slides are fixed-size artboards, so fitting means scaling — never re-laying-out. Letterboxing is
    /// the correct outcome when the viewport's aspect ratio differs from the deck's.
    /// </remarks>
    public SlidePlacement? SinglePlacement()
    {
        if (this.Current is not { } slide)
            return null;

        var scale = this.ScaleForZoom();
        var width = this.Deck.SlideWidth * scale;
        var height = this.Deck.SlideHeight * scale;

        // Centred on an axis the slide fits on; panned on one it overflows.
        var x = width + this.EffectiveMargin * 2 <= this.ViewportWidth
            ? (this.ViewportWidth - width) / 2
            : this.EffectiveMargin - this.panX;

        var y = height + this.EffectiveMargin * 2 <= this.ViewportHeight
            ? (this.ViewportHeight - height) / 2
            : this.EffectiveMargin - this.panY;

        return new SlidePlacement(slide, x, y, width, height);
    }

    // ---- zoom ----

    double? zoom;
    double panX;
    double panY;

    /// <summary>The smallest and largest zoom the controls offer — PowerPoint's 10% to 400%.</summary>
    public const double MinimumZoom = 0.1;

    public const double MaximumZoom = 4;

    /// <summary>
    /// The zoom, where 1 is 100% (one slide pixel per viewport pixel), or null to fit the slide to the
    /// viewport — PowerPoint's "Fit slide to current window", and the default.
    /// </summary>
    /// <remarks>Ignored while presenting: a show always fits.</remarks>
    public double? Zoom
    {
        get => this.zoom;
        set
        {
            var clamped = value is { } v ? Math.Clamp(v, MinimumZoom, MaximumZoom) : (double?)null;
            if (Nullable.Equals(clamped, this.zoom))
                return;

            this.zoom = clamped;
            this.ClampPan();
            this.Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The scale actually drawn at — the fitted one when <see cref="Zoom"/> is null.</summary>
    public double EffectiveZoom => this.Current is null ? 1 : this.ScaleForZoom();

    /// <summary>True when the slide overflows the viewport, so there is something to scroll to.</summary>
    public bool CanPan
        => !this.isPresenting && this.zoom is not null &&
           (this.Deck.SlideWidth * this.ScaleForZoom() + this.EffectiveMargin * 2 > this.ViewportWidth ||
            this.Deck.SlideHeight * this.ScaleForZoom() + this.EffectiveMargin * 2 > this.ViewportHeight);

    public double PanX => this.panX;

    public double PanY => this.panY;

    double FitScale()
    {
        var width = Math.Max(1, this.ViewportWidth - this.EffectiveMargin * 2);
        var height = Math.Max(1, this.ViewportHeight - this.EffectiveMargin * 2);
        return Math.Min(width / this.Deck.SlideWidth, height / this.Deck.SlideHeight);
    }

    double ScaleForZoom() => this.isPresenting || this.zoom is not { } z ? this.FitScale() : z;

    /// <summary>Scrolls a zoomed slide. Returns false when there was nothing to scroll.</summary>
    public bool PanBy(double dx, double dy)
    {
        if (!this.CanPan)
            return false;

        var (oldX, oldY) = (this.panX, this.panY);
        this.panX += dx;
        this.panY += dy;
        this.ClampPan();

        if (Math.Abs(oldX - this.panX) < 0.01 && Math.Abs(oldY - this.panY) < 0.01)
            return false;

        this.Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Zooms keeping the slide point under (<paramref name="viewportX"/>, <paramref name="viewportY"/>)
    /// where it is — what Ctrl+wheel and a pinch do.
    /// </summary>
    public void ZoomAt(double zoom, double viewportX, double viewportY)
    {
        if (this.SinglePlacement() is not { } before)
        {
            this.Zoom = zoom;
            return;
        }

        var oldScale = before.Width / Math.Max(1, this.Deck.SlideWidth);
        var slideX = (viewportX - before.X) / oldScale;
        var slideY = (viewportY - before.Y) / oldScale;

        this.zoom = Math.Clamp(zoom, MinimumZoom, MaximumZoom);
        var scale = this.ScaleForZoom();

        // Solve for the pan that puts slideX back under viewportX.
        this.panX = this.EffectiveMargin + slideX * scale - viewportX;
        this.panY = this.EffectiveMargin + slideY * scale - viewportY;
        this.ClampPan();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The next step in (or out) on PowerPoint's zoom ladder, from wherever the zoom is now.</summary>
    public void ZoomStep(int direction)
    {
        double[] ladder = [0.1, 0.25, 0.33, 0.5, 0.66, 0.75, 1, 1.25, 1.5, 2, 3, 4];
        var current = this.EffectiveZoom;
        var next = direction > 0
            ? ladder.FirstOrDefault(x => x > current + 0.005, MaximumZoom)
            : ladder.LastOrDefault(x => x < current - 0.005, MinimumZoom);

        this.Zoom = next;
    }

    /// <summary>Back to fitting the slide to the window.</summary>
    public void ZoomToFit() => this.Zoom = null;

    void ClampPan()
    {
        var scale = this.ScaleForZoom();
        var maxX = Math.Max(0, this.Deck.SlideWidth * scale + this.EffectiveMargin * 2 - this.ViewportWidth);
        var maxY = Math.Max(0, this.Deck.SlideHeight * scale + this.EffectiveMargin * 2 - this.ViewportHeight);
        this.panX = Math.Clamp(this.panX, 0, maxX);
        this.panY = Math.Clamp(this.panY, 0, maxY);
    }

    public int GridColumns()
    {
        var pitch = this.ThumbnailWidth + this.ThumbnailGap;
        return Math.Max(1, (int)((this.ViewportWidth - this.ThumbnailGap) / pitch));
    }

    public double GridHeight()
    {
        if (this.Count == 0)
            return 0;

        var columns = this.GridColumns();
        var rows = (int)Math.Ceiling(this.Count / (double)columns);
        var thumbnailHeight = this.ThumbnailWidth / Math.Max(0.01, this.Deck.AspectRatio);

        return rows * (thumbnailHeight + this.ThumbnailGap) + this.ThumbnailGap;
    }

    /// <summary>Thumbnails intersecting the visible band. Off-screen slides are never painted.</summary>
    public IEnumerable<SlidePlacement> VisibleThumbnails()
    {
        if (this.Count == 0)
            yield break;

        var columns = this.GridColumns();
        var thumbnailHeight = this.ThumbnailWidth / Math.Max(0.01, this.Deck.AspectRatio);
        var pitchY = thumbnailHeight + this.ThumbnailGap;

        for (var i = 0; i < this.Count; i++)
        {
            var row = i / columns;
            var column = i % columns;

            var y = this.ThumbnailGap + row * pitchY - this.scrollY;
            if (y + thumbnailHeight < 0)
                continue;

            if (y > this.ViewportHeight)
                yield break;

            var x = this.ThumbnailGap + column * (this.ThumbnailWidth + this.ThumbnailGap);
            yield return new SlidePlacement(this.Deck.Slides[i], x, y, this.ThumbnailWidth, thumbnailHeight);
        }
    }

    /// <summary>The slide index under a point in grid mode, or -1.</summary>
    public int ThumbnailAt(double x, double y)
    {
        var i = 0;
        foreach (var placement in this.VisibleThumbnails())
        {
            if (x >= placement.X && x <= placement.X + placement.Width &&
                y >= placement.Y && y <= placement.Y + placement.Height)
                return this.Deck.Slides.ToList().IndexOf(placement.Slide);

            i++;
        }

        _ = i;
        return -1;
    }
}
