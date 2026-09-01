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

    public int Index
    {
        get => this.index;
        set
        {
            var clamped = Math.Clamp(value, 0, Math.Max(0, this.Count - 1));
            if (clamped == this.index)
                return;

            this.index = clamped;
            this.Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public Slide? Current => this.Count == 0 ? null : this.Deck.Slides[this.index];

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

    public bool CanGoNext => this.index < this.Count - 1;
    public bool CanGoPrevious => this.index > 0;

    public void Next() => this.Index = this.index + 1;

    public void Previous() => this.Index = this.index - 1;

    public void Resize(double width, double height)
    {
        this.ViewportWidth = Math.Max(1, width);
        this.ViewportHeight = Math.Max(1, height);
        this.ClampScroll();
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

        var available = new
        {
            Width = Math.Max(1, this.ViewportWidth - this.EffectiveMargin * 2),
            Height = Math.Max(1, this.ViewportHeight - this.EffectiveMargin * 2)
        };

        var scale = Math.Min(available.Width / this.Deck.SlideWidth, available.Height / this.Deck.SlideHeight);
        var width = this.Deck.SlideWidth * scale;
        var height = this.Deck.SlideHeight * scale;

        return new SlidePlacement(
            slide,
            (this.ViewportWidth - width) / 2,
            (this.ViewportHeight - height) / 2,
            width,
            height);
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
