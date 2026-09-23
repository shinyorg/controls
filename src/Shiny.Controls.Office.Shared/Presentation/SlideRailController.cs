namespace Shiny.Controls.Office.Presentation;

/// <summary>One thumbnail in the rail, in the rail's viewport coordinates.</summary>
public readonly record struct SlideRailItem(int Index, Slide Slide, double X, double Y, double Width, double Height, bool IsSelected);

/// <summary>
/// The slide rail beside the editor: a scrolling column of thumbnails that picks the slide being edited
/// and reorders the deck by drag.
/// </summary>
/// <remarks>
/// <para>
/// Host-independent, like the editor's controller: layout, hit-testing, scrolling and the drag all live
/// here, and a host forwards pointer events and paints what <see cref="VisibleItems"/> returns. Both
/// hosts therefore reorder identically, and the behaviour is testable without a canvas.
/// </para>
/// <para>
/// A press only becomes a drag once the pointer has moved <see cref="DragThreshold"/>, so a tap still
/// selects a slide on a touch screen where every press wobbles. Dropping moves the slide through the
/// editor's <see cref="SlideEditorController.MoveSlide"/>, so it is one undo step like the buttons.
/// </para>
/// </remarks>
public sealed class SlideRailController
{
    readonly SlideEditorController editor;

    double scrollY;
    int pressed = -1;
    double pressX;
    double pressY;
    double pointerY;
    bool dragging;

    public SlideRailController(SlideEditorController editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        this.editor = editor;
    }

    public SlideEditorController Editor => this.editor;

    public double ViewportWidth { get; private set; } = 180;

    public double ViewportHeight { get; private set; } = 600;

    /// <summary>Space around and between thumbnails.</summary>
    public double Gap { get; set; } = 12;

    /// <summary>The column on the left that carries each slide's number.</summary>
    public double NumberWidth { get; set; } = 22;

    /// <summary>How far a press must travel before it is a drag rather than a tap.</summary>
    public double DragThreshold { get; set; } = 6;

    public double ScrollY => this.scrollY;

    public bool IsDragging => this.dragging;

    /// <summary>The slide being dragged, or -1.</summary>
    public int DraggedIndex => this.dragging ? this.pressed : -1;

    /// <summary>Raised whenever the rail should repaint.</summary>
    public event EventHandler? Changed;

    public double ThumbnailWidth => Math.Max(24, this.ViewportWidth - this.Gap * 2 - this.NumberWidth);

    public double ThumbnailHeight => this.ThumbnailWidth / Math.Max(0.01, this.editor.Deck.AspectRatio);

    double Pitch => this.ThumbnailHeight + this.Gap;

    public double ContentHeight => this.editor.Count * this.Pitch + this.Gap;

    public void Resize(double width, double height)
    {
        this.ViewportWidth = Math.Max(1, width);
        this.ViewportHeight = Math.Max(1, height);
        this.ClampScroll();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Scroll(double delta)
    {
        var before = this.scrollY;
        this.scrollY += delta;
        this.ClampScroll();

        if (before != this.scrollY)
            this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Scrolls just enough to bring a slide's thumbnail fully into view.</summary>
    public void EnsureVisible(int index)
    {
        if (index < 0 || index >= this.editor.Count)
            return;

        var top = this.Gap + index * this.Pitch;
        var bottom = top + this.ThumbnailHeight;

        if (top - this.Gap < this.scrollY)
            this.scrollY = top - this.Gap;
        else if (bottom + this.Gap > this.scrollY + this.ViewportHeight)
            this.scrollY = bottom + this.Gap - this.ViewportHeight;
        else
            return;

        this.ClampScroll();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    void ClampScroll()
        => this.scrollY = Math.Clamp(this.scrollY, 0, Math.Max(0, this.ContentHeight - this.ViewportHeight));

    /// <summary>The thumbnails intersecting the viewport. Off-screen slides are never painted.</summary>
    public IEnumerable<SlideRailItem> VisibleItems()
    {
        var width = this.ThumbnailWidth;
        var height = this.ThumbnailHeight;
        var x = this.Gap + this.NumberWidth;
        var slides = this.editor.Deck.Slides;

        for (var i = 0; i < slides.Count; i++)
        {
            var y = this.Gap + i * this.Pitch - this.scrollY;
            if (y + height < 0)
                continue;

            if (y > this.ViewportHeight)
                yield break;

            yield return new SlideRailItem(i, slides[i], x, y, width, height, i == this.editor.Index);
        }
    }

    /// <summary>The slide whose row is under a point — the number column counts — or -1.</summary>
    public int ItemAt(double x, double y)
    {
        if (x < 0 || x > this.ViewportWidth)
            return -1;

        var content = y + this.scrollY - this.Gap / 2;
        if (content < 0)
            return -1;

        var index = (int)(content / this.Pitch);
        return index < this.editor.Count ? index : -1;
    }

    /// <summary>
    /// Where a dragged slide would land: the gap between two thumbnails, 0 to <c>Count</c>. Null when
    /// no drag is under way.
    /// </summary>
    public int? DropGap
    {
        get
        {
            if (!this.dragging)
                return null;

            var content = this.pointerY + this.scrollY - this.Gap / 2;
            return Math.Clamp((int)Math.Round(content / this.Pitch), 0, this.editor.Count);
        }
    }

    /// <summary>The y of the insertion line for <see cref="DropGap"/>, in viewport coordinates.</summary>
    public double? DropIndicatorY
        => this.DropGap is { } gap ? this.Gap / 2 + gap * this.Pitch - this.scrollY : null;

    /// <summary>
    /// Whether dropping here would move anything. Dropping a slide into the gap on either side of
    /// itself leaves it where it is, so no line is drawn there.
    /// </summary>
    public bool IsDropMeaningful
        => this.DropGap is { } gap && gap != this.pressed && gap != this.pressed + 1;

    // ---- pointer ----

    /// <summary>Starts a press. Returns true when it landed on a slide.</summary>
    public bool PointerDown(double x, double y)
    {
        this.pressed = this.ItemAt(x, y);
        this.pressX = x;
        this.pressY = y;
        this.pointerY = y;
        this.dragging = false;
        return this.pressed >= 0;
    }

    public void PointerMove(double x, double y)
    {
        if (this.pressed < 0)
            return;

        this.pointerY = y;

        if (!this.dragging)
        {
            if (this.editor.IsReadOnly || this.editor.Count < 2 ||
                Math.Abs(y - this.pressY) < this.DragThreshold && Math.Abs(x - this.pressX) < this.DragThreshold)
            {
                return;
            }

            this.dragging = true;
        }

        // Near an edge the rail scrolls itself, so a slide can be dragged further than the viewport.
        var edge = Math.Min(32, this.ViewportHeight / 4);
        if (y < edge)
            this.Scroll(-(edge - y) / 2);
        else if (y > this.ViewportHeight - edge)
            this.Scroll((y - (this.ViewportHeight - edge)) / 2);

        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ends a press: a tap opens the slide, a drag moves it.</summary>
    public void PointerUp()
    {
        var from = this.pressed;
        var wasDragging = this.dragging;
        var gap = this.DropGap;

        this.pressed = -1;
        this.dragging = false;

        if (from < 0)
            return;

        if (!wasDragging)
        {
            this.editor.ClearSelection();
            this.editor.Index = from;
        }
        else if (gap is { } g && g != from && g != from + 1)
        {
            // The gap is counted with the slide still in place; MoveSlide counts with it taken out.
            this.editor.MoveSlide(from, g > from ? g - 1 : g);
        }

        this.EnsureVisible(this.editor.Index);
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Abandons a press or drag without moving anything.</summary>
    public void PointerCancel()
    {
        this.pressed = -1;
        this.dragging = false;
        this.Changed?.Invoke(this, EventArgs.Empty);
    }
}
