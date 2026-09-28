namespace Shiny.Controls.Office.Presentation;

/// <summary>The View tab's presentation views.</summary>
public enum SlideEditorViewMode
{
    /// <summary>One slide to edit, the rail beside it.</summary>
    Normal,

    /// <summary>The deck's text as an outline, a slide per entry.</summary>
    Outline,

    /// <summary>A grid of thumbnails to rearrange.</summary>
    SlideSorter,

    /// <summary>The slide on a printed page, with its notes under it.</summary>
    NotesPage,

    /// <summary>The master and its layouts, whose placeholders and backgrounds every slide inherits.</summary>
    SlideMaster
}

/// <summary>Views, the sorter's drag, and routing to the Slide Master editor.</summary>
public sealed partial class SlideEditorController
{
    SlideEditorViewMode viewMode;
    SlideMasterController? master;

    int sorterPressed = -1;
    bool sorterDragging;
    double sorterStartX;
    double sorterStartY;
    double sorterX;
    double sorterY;

    /// <summary>
    /// Which view the editor is in. The sorter is the viewer's thumbnail grid; the others keep a single
    /// slide in the controller and are laid out by the host (outline, notes page) or by
    /// <see cref="Master"/> (slide master).
    /// </summary>
    public SlideEditorViewMode ViewMode
    {
        get => this.viewMode;
        set
        {
            if (this.viewMode == value)
                return;

            this.EndTextEditing();
            this.ClearSelection();

            this.viewMode = value;
            this.Mode = value == SlideEditorViewMode.SlideSorter ? SlideViewMode.Grid : SlideViewMode.Single;

            if (value == SlideEditorViewMode.SlideMaster)
                this.Master.Open(this.Index);
            else
                this.master?.Close();

            this.RaiseChanged();
        }
    }

    /// <summary>The Slide Master view's editor — the master and layouts, their placeholders and backgrounds.</summary>
    public SlideMasterController Master => this.master ??= new SlideMasterController(this.deck, this);

    /// <summary>True in the Slide Master view.</summary>
    public bool IsEditingMaster => this.viewMode == SlideEditorViewMode.SlideMaster;

    // ---- slide sorter ----

    /// <summary>The slide being dragged in the sorter, or -1.</summary>
    public int SorterDraggedIndex => this.sorterDragging ? this.sorterPressed : -1;

    /// <summary>Where a sorter drag would drop: the insertion index 0..Count, or null.</summary>
    public int? SorterDropIndex
    {
        get
        {
            if (!this.sorterDragging)
                return null;

            var best = (Index: 0, Distance: double.MaxValue);
            foreach (var placement in this.VisibleThumbnails())
            {
                var index = this.Deck.Slides.ToList().IndexOf(placement.Slide);
                foreach (var (edge, at) in new[] { (placement.X, index), (placement.X + placement.Width, index + 1) })
                {
                    var dx = edge - this.sorterX;
                    var dy = placement.Y + placement.Height / 2 - this.sorterY;
                    var distance = dx * dx + dy * dy * 4;
                    if (distance < best.Distance)
                        best = (at, distance);
                }
            }

            return best.Index;
        }
    }

    /// <summary>The insertion line a sorter drag draws, in viewport coordinates.</summary>
    public SlideRect? SorterDropIndicator
    {
        get
        {
            if (this.SorterDropIndex is not { } drop)
                return null;

            var placements = this.VisibleThumbnails().ToList();
            var slides = this.Deck.Slides.ToList();
            var before = placements.FirstOrDefault(x => slides.IndexOf(x.Slide) == drop);
            var after = placements.FirstOrDefault(x => slides.IndexOf(x.Slide) == drop - 1);

            if (before.Slide is not null && (after.Slide is null || Math.Abs(after.Y - before.Y) > 1))
                return new SlideRect(before.X - this.ThumbnailGap / 2 - 1.5, before.Y, 3, before.Height);

            if (after.Slide is not null)
                return new SlideRect(after.X + after.Width + this.ThumbnailGap / 2 - 1.5, after.Y, 3, after.Height);

            return null;
        }
    }

    bool SorterPointerDown(double x, double y)
    {
        var hit = this.ThumbnailAt(x, y);
        if (hit < 0)
            return false;

        this.Index = hit;
        this.sorterPressed = hit;
        this.sorterDragging = false;
        this.sorterStartX = this.sorterX = x;
        this.sorterStartY = this.sorterY = y;
        this.RaiseChanged();
        return true;
    }

    void SorterPointerMove(double x, double y)
    {
        if (this.sorterPressed < 0 || this.IsReadOnly)
            return;

        this.sorterX = x;
        this.sorterY = y;

        if (!this.sorterDragging && Math.Abs(x - this.sorterStartX) + Math.Abs(y - this.sorterStartY) < 6)
            return;

        this.sorterDragging = true;

        // Near the top or bottom edge the grid scrolls itself, as the rail does.
        if (y < 24)
            this.Scroll(-12);
        else if (y > this.ViewportHeight - 24)
            this.Scroll(12);

        this.RaiseChanged();
    }

    void SorterPointerUp()
    {
        var from = this.sorterPressed;
        var drop = this.SorterDropIndex;
        var dragged = this.sorterDragging;

        this.sorterPressed = -1;
        this.sorterDragging = false;

        if (dragged && drop is { } at && from >= 0)
        {
            // Counted with the slide taken out, as MoveSlide wants.
            var to = at > from ? at - 1 : at;
            if (to != from)
                this.MoveSlide(from, to);
        }

        this.RaiseChanged();
    }

    /// <summary>A double-click on a sorter thumbnail opens that slide in the Normal view, as in PowerPoint.</summary>
    bool SorterDoubleClick(double x, double y)
    {
        var hit = this.ThumbnailAt(x, y);
        if (hit < 0)
            return false;

        this.ViewMode = SlideEditorViewMode.Normal;
        this.Index = hit;
        return true;
    }
}
