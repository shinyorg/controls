namespace Shiny.Controls.Office.Presentation;

/// <summary>One thumbnail in the rail, in the rail's viewport coordinates.</summary>
public readonly record struct SlideRailItem(int Index, Slide Slide, double X, double Y, double Width, double Height, bool IsSelected)
{
    /// <summary>The slide is hidden from the show — the rail dims it and strikes its number.</summary>
    public bool IsHidden => this.Slide.IsHidden;
}

/// <summary>A section header in the rail: its name, whether it is folded, and where it is drawn.</summary>
public readonly record struct SlideRailSection(int Index, SlideSection Section, double X, double Y, double Width, double Height, bool IsCollapsed);

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
/// <para>
/// A deck with sections shows a header above each one; tapping it folds the section away, as
/// PowerPoint's rail does. The fold is view state, not something saved in the file.
/// </para>
/// </remarks>
public sealed class SlideRailController
{
    readonly SlideEditorController editor;
    readonly HashSet<string> collapsed = [];

    double scrollY;
    int pressed = -1;
    int pressedSection = -1;
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

    /// <summary>A section header's height.</summary>
    public double SectionHeight { get; set; } = 24;

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

    /// <summary>One row of the rail's layout, in content coordinates (before scrolling).</summary>
    readonly record struct Row(bool IsSection, int Index, double Top, double Height);

    /// <summary>
    /// The rail's rows top to bottom: a header per section, then the section's slides unless it is
    /// folded. A deck without sections is only slides.
    /// </summary>
    List<Row> Rows()
    {
        var rows = new List<Row>();
        var y = this.Gap;

        // The Slide Master view lists the master and its layouts instead, the layouts indented and
        // smaller under their master, as PowerPoint's does.
        if (this.editor.IsEditingMaster)
        {
            var pages = this.editor.Master.Pages;
            for (var i = 0; i < pages.Count; i++)
            {
                var height = pages[i].IsMaster ? this.ThumbnailHeight : this.ThumbnailHeight * this.LayoutScale;
                rows.Add(new Row(false, i, y, height));
                y += height + this.Gap;
            }

            return rows;
        }

        var sections = this.editor.Deck.Sections;
        var count = this.editor.Count;

        if (sections.Count == 0)
        {
            for (var i = 0; i < count; i++)
            {
                rows.Add(new Row(false, i, y, this.ThumbnailHeight));
                y += this.Pitch;
            }

            return rows;
        }

        var placed = new HashSet<int>();
        for (var s = 0; s < sections.Count; s++)
        {
            rows.Add(new Row(true, s, y, this.SectionHeight));
            y += this.SectionHeight + this.Gap / 2;

            var section = sections[s];
            if (section.FirstSlide < 0)
                continue;

            for (var i = section.FirstSlide; i < section.FirstSlide + section.SlideCount && i < count; i++)
            {
                placed.Add(i);
                if (this.IsCollapsed(s))
                    continue;

                rows.Add(new Row(false, i, y, this.ThumbnailHeight));
                y += this.Pitch;
            }
        }

        // A slide no section lists still has to be reachable.
        for (var i = 0; i < count; i++)
        {
            if (placed.Add(i))
            {
                rows.Add(new Row(false, i, y, this.ThumbnailHeight));
                y += this.Pitch;
            }
        }

        return rows;
    }

    public double ContentHeight
    {
        get
        {
            var rows = this.Rows();
            return rows.Count == 0 ? this.Gap : rows[^1].Top + rows[^1].Height + this.Gap;
        }
    }

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

    /// <summary>Scrolls just enough to bring a slide's thumbnail fully into view, unfolding its section.</summary>
    public void EnsureVisible(int index)
    {
        if (index < 0 || index >= this.editor.Count)
            return;

        var sections = this.editor.Deck.Sections;
        for (var s = 0; s < sections.Count; s++)
        {
            if (sections[s].Contains(index) && this.IsCollapsed(s))
                this.collapsed.Remove(Key(sections[s], s));
        }

        if (this.Rows().FirstOrDefault(x => !x.IsSection && x.Index == index) is not { Height: > 0 } row)
            return;

        var top = row.Top;
        var bottom = top + row.Height;

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

    /// <summary>How big a layout's thumbnail is beside its master's, in the Slide Master view.</summary>
    public double LayoutScale { get; set; } = 0.8;

    /// <summary>The thumbnails intersecting the viewport. Off-screen slides are never painted.</summary>
    public IEnumerable<SlideRailItem> VisibleItems()
    {
        var width = this.ThumbnailWidth;
        var x = this.Gap + this.NumberWidth;
        var slides = this.editor.Deck.Slides;
        var master = this.editor.IsEditingMaster ? this.editor.Master : null;

        foreach (var row in this.Rows())
        {
            if (row.IsSection)
                continue;

            var y = row.Top - this.scrollY;
            if (y + row.Height < 0)
                continue;

            if (y > this.ViewportHeight)
                yield break;

            if (master is not null)
            {
                if (master.PageModel(row.Index) is not { } page)
                    continue;

                var indent = master.Pages[row.Index].IsMaster ? 0 : width * (1 - this.LayoutScale);
                yield return new SlideRailItem(row.Index, page, x + indent, y, width - indent, row.Height, row.Index == master.PageIndex);
                continue;
            }

            yield return new SlideRailItem(row.Index, slides[row.Index], x, y, width, row.Height, row.Index == this.editor.Index);
        }
    }

    /// <summary>The section headers intersecting the viewport.</summary>
    public IEnumerable<SlideRailSection> VisibleSections()
    {
        var sections = this.editor.Deck.Sections;

        foreach (var row in this.Rows())
        {
            if (!row.IsSection)
                continue;

            var y = row.Top - this.scrollY;
            if (y + row.Height < 0 || y > this.ViewportHeight)
                continue;

            yield return new SlideRailSection(row.Index, sections[row.Index], this.Gap / 2, y, this.ViewportWidth - this.Gap, row.Height, this.IsCollapsed(row.Index));
        }
    }

    /// <summary>Whether a section is folded away.</summary>
    public bool IsCollapsed(int section)
        => this.editor.Deck.Sections.ElementAtOrDefault(section) is { } value && this.collapsed.Contains(Key(value, section));

    /// <summary>Folds a section away, or unfolds it.</summary>
    public void ToggleSection(int section)
    {
        if (this.editor.Deck.Sections.ElementAtOrDefault(section) is not { } value)
            return;

        var key = Key(value, section);
        if (!this.collapsed.Remove(key))
            this.collapsed.Add(key);

        this.ClampScroll();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Folds or unfolds every section — PowerPoint's Collapse All / Expand All.</summary>
    public void SetAllCollapsed(bool collapse)
    {
        this.collapsed.Clear();
        if (collapse)
        {
            var sections = this.editor.Deck.Sections;
            for (var i = 0; i < sections.Count; i++)
                this.collapsed.Add(Key(sections[i], i));
        }

        this.ClampScroll();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    static string Key(SlideSection section, int index) => section.Id ?? $"#{index}";

    /// <summary>The slide whose row is under a point — the number column counts — or -1.</summary>
    public int ItemAt(double x, double y)
    {
        if (x < 0 || x > this.ViewportWidth)
            return -1;

        var content = y + this.scrollY;
        foreach (var row in this.Rows())
        {
            if (!row.IsSection && content >= row.Top - this.Gap / 2 && content < row.Top + row.Height + this.Gap / 2)
                return row.Index;
        }

        return -1;
    }

    /// <summary>The section whose header is under a point, or -1.</summary>
    public int SectionAt(double x, double y)
    {
        if (x < 0 || x > this.ViewportWidth)
            return -1;

        var content = y + this.scrollY;
        foreach (var row in this.Rows())
        {
            if (row.IsSection && content >= row.Top && content < row.Top + row.Height)
                return row.Index;
        }

        return -1;
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

            var content = this.pointerY + this.scrollY;
            var slides = this.Rows().Where(x => !x.IsSection).ToList();
            if (slides.Count == 0)
                return 0;

            // The gap nearest the pointer: before a row whose middle is below it, or after the last.
            foreach (var row in slides)
            {
                if (content < row.Top + row.Height / 2)
                    return row.Index;
            }

            return slides[^1].Index + 1;
        }
    }

    /// <summary>The y of the insertion line for <see cref="DropGap"/>, in viewport coordinates.</summary>
    public double? DropIndicatorY
    {
        get
        {
            if (this.DropGap is not { } gap)
                return null;

            var slides = this.Rows().Where(x => !x.IsSection).ToList();
            var before = slides.FirstOrDefault(x => x.Index == gap);
            if (before.Height > 0)
                return before.Top - this.Gap / 2 - this.scrollY;

            var last = slides.LastOrDefault();
            return last.Top + last.Height + this.Gap / 2 - this.scrollY;
        }
    }

    /// <summary>
    /// Whether dropping here would move anything. Dropping a slide into the gap on either side of
    /// itself leaves it where it is, so no line is drawn there.
    /// </summary>
    public bool IsDropMeaningful
        => this.DropGap is { } gap && gap != this.pressed && gap != this.pressed + 1;

    // ---- pointer ----

    /// <summary>Starts a press. Returns true when it landed on a slide or a section header.</summary>
    public bool PointerDown(double x, double y)
    {
        this.pressedSection = this.SectionAt(x, y);
        this.pressed = this.pressedSection >= 0 ? -1 : this.ItemAt(x, y);
        this.pressX = x;
        this.pressY = y;
        this.pointerY = y;
        this.dragging = false;
        return this.pressed >= 0 || this.pressedSection >= 0;
    }

    public void PointerMove(double x, double y)
    {
        if (this.pressed < 0)
            return;

        this.pointerY = y;

        if (!this.dragging)
        {
            if (this.editor.IsReadOnly || this.editor.Count < 2 || this.editor.IsEditingMaster ||
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

    /// <summary>Ends a press: a tap opens the slide (or folds a section), a drag moves it.</summary>
    public void PointerUp()
    {
        if (this.pressedSection >= 0)
        {
            var section = this.pressedSection;
            this.pressedSection = -1;
            this.ToggleSection(section);
            return;
        }

        var from = this.pressed;
        var wasDragging = this.dragging;
        var gap = this.DropGap;

        this.pressed = -1;
        this.dragging = false;

        if (from < 0)
            return;

        if (this.editor.IsEditingMaster)
        {
            // Pages are not reordered; a tap opens one.
            this.editor.Master.PageIndex = from;
            this.Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

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
        this.pressedSection = -1;
        this.dragging = false;
        this.Changed?.Invoke(this, EventArgs.Empty);
    }
}
