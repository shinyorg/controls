using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using Shiny.Controls.Office.View;

namespace Shiny.Controls.Office.Notebook;

/// <summary>What a pointer press on the canvas does.</summary>
public enum NoteTool
{
    /// <summary>Click to select, drag to move, drag a handle to resize, drag empty canvas to marquee.</summary>
    Select,

    /// <summary>Click anywhere to start a new text container there.</summary>
    Text,

    /// <summary>Drag out a new shape of <see cref="NotebookEditorController.NewShapeGeometry"/>.</summary>
    Shape,

    Pen,

    Highlighter,

    Eraser,

    /// <summary>Circle a region to select everything inside it, ink included.</summary>
    Lasso,

    /// <summary>Drag to scroll the canvas, for a device with no scroll gesture of its own.</summary>
    Pan
}

/// <summary>Whether the eraser takes whole strokes or eats through them.</summary>
public enum EraseMode
{
    /// <summary>Touching any part of a stroke removes all of it. What OneNote calls the stroke eraser.</summary>
    Stroke,

    /// <summary>Removes only the points under the eraser, splitting the stroke where it passes through.</summary>
    Point
}

enum DragMode
{
    None,
    Move,
    Resize,
    Marquee,
    Lasso,
    Ink,
    Erase,
    CreateShape,
    Pan,
    SelectText
}

/// <summary>
/// Everything the notebook canvas does under a pointer and a keyboard.
/// </summary>
/// <remarks>
/// <para>
/// Three layers of state, and keeping them separate is the whole design. The <b>tool</b> decides what a
/// press starts. The <b>selection</b> is a set of item ids, because a lasso routinely catches thirty
/// strokes and one picture and they all have to move together. <b>Text editing</b> is a mode within
/// the selection: a caret inside exactly one item, entered by double-clicking it, which routes typing
/// there instead of to the canvas.
/// </para>
/// <para>
/// Every mutation goes through the undo stack, including the ones a drag produces per pointer sample.
/// Those are marked to coalesce, so a drag across the page is one undo step rather than ninety; the run
/// is broken on pointer-up so the next drag starts a step of its own.
/// </para>
/// </remarks>
public sealed partial class NotebookEditorController : NotebookController
{
    readonly List<string> selection = new();
    readonly List<InkPoint> stroke = new();
    readonly List<(double X, double Y)> lasso = new();

    DragMode drag = DragMode.None;
    ShapeHandle handle = ShapeHandle.None;
    double dragStartPageX;
    double dragStartPageY;
    double dragLastPageX;
    double dragLastPageY;
    double dragStartViewportX;
    double dragStartViewportY;
    double dragStartScrollX;
    double dragStartScrollY;
    bool dragMoved;

    /// <summary>
    /// The selected items exactly as they were when the drag began.
    /// </summary>
    /// <remarks>
    /// The whole item, not just its rectangle. A drag executes a command per pointer sample, so every
    /// sample has to be computed from the <i>original</i> state rather than from the state the last
    /// sample left behind — otherwise the transform compounds. For a shape that only shows up as
    /// rounding drift, because a rectangle can be reconstructed from its stored bounds; for ink,
    /// whose geometry <i>is</i> its points, scaling the already-scaled stroke multiplies up sample by
    /// sample and throws the stroke off the page.
    /// </remarks>
    readonly Dictionary<string, NoteItem> dragOrigins = new();

    string? editingItemId;
    NotePosition caret;
    NotePosition anchor;

    public NotebookEditorController(NotebookDocument document, ITextMeasurer measurer)
        : base(document, measurer)
        => this.Document.ContentChanged += (_, _) => this.Edited?.Invoke(this, EventArgs.Empty);

    /// <summary>Raised after an edit actually changed the notebook.</summary>
    public event EventHandler? Edited;

    public bool IsReadOnly { get; set; }

    public bool CanUndo => this.Document.Undo.CanUndo;

    public bool CanRedo => this.Document.Undo.CanRedo;

    /// <summary>Size of a resize handle, in viewport pixels.</summary>
    public double HandleSize { get; set; } = 9;

    /// <summary>How far a pointer must travel before a press counts as a drag rather than a click.</summary>
    public double DragThreshold { get; set; } = 3;

    // ---- tools ----

    NoteTool tool = NoteTool.Select;

    public NoteTool Tool
    {
        get => this.tool;
        set
        {
            if (this.tool == value)
                return;

            this.tool = value;

            // Leaving the caret in a container while the pen is selected would send the next keystroke
            // into text the user is no longer looking at.
            if (value != NoteTool.Select && value != NoteTool.Text)
                this.EndTextEditing();

            this.Document.Undo.BreakCoalescing();
            this.RaiseChanged();
        }
    }

    ArgbColor penColor = new(255, 0x1A, 0x1A, 0x1A);

    /// <summary>
    /// The pen's colour. Assigning one pins it against <see cref="ApplyDefaultInk"/>.
    /// </summary>
    public ArgbColor PenColor
    {
        get => this.penColor;
        set
        {
            this.penColor = value;
            this.HasCustomPenColor = true;
        }
    }

    /// <summary>True once a pen colour has actually been chosen.</summary>
    public bool HasCustomPenColor { get; private set; }

    /// <summary>
    /// Points the pen at the surface's default ink, unless a colour has been chosen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The notebook is the one surface here whose <i>page</i> follows the app's theme, and a pen left
    /// on its near-black default draws invisible ink on a dark one. That is not a theming decision
    /// about the user's content — existing strokes are never touched — it is that nobody picked black;
    /// black is simply where the pen starts.
    /// </para>
    /// <para>
    /// Called by the host each time it paints, so a theme flipped mid-session moves the pen with it.
    /// Assigning <see cref="PenColor"/> stops that for good, because at that point somebody has
    /// chosen, and a choice that a theme change quietly overrode would be worse than a dark pen.
    /// </para>
    /// </remarks>
    public void ApplyDefaultInk(ArgbColor ink)
    {
        if (!this.HasCustomPenColor)
            this.penColor = ink;
    }

    public double PenWidth { get; set; } = 2.2;

    public ArgbColor HighlighterColor { get; set; } = new(110, 0xFF, 0xE0, 0x3B);

    public double HighlighterWidth { get; set; } = 16;

    public EraseMode EraseMode { get; set; } = EraseMode.Stroke;

    /// <summary>Eraser radius in page pixels.</summary>
    public double EraserRadius { get; set; } = 10;

    public ShapeGeometry NewShapeGeometry { get; set; } = ShapeGeometry.Rectangle;

    public ArgbColor? NewShapeFill { get; set; }

    public ArgbColor NewShapeOutline { get; set; } = new(255, 0x33, 0x33, 0x33);

    public TextStyle NewTextStyle { get; set; } = NotebookDocument.DefaultTextStyle;

    // ---- selection ----

    public IReadOnlyList<string> SelectedIds => this.selection;

    public bool HasSelection => this.selection.Count > 0;

    public IEnumerable<NoteItem> SelectedItems()
    {
        if (this.Page is not { } page)
            yield break;

        foreach (var item in page.Items)
        {
            if (this.selection.Contains(item.Id))
                yield return item;
        }
    }

    /// <summary>The one selected item, or null when nothing or several things are selected.</summary>
    public NoteItem? SingleSelection
        => this.selection.Count == 1 ? this.ItemById(this.selection[0]) : null;

    public NoteItem? ItemById(string? id)
    {
        if (id is null || this.Page is not { } page)
            return null;

        var index = page.IndexOf(id);
        return index < 0 ? null : page.Items[index];
    }

    public void Select(string itemId, bool add = false)
    {
        if (!add)
            this.selection.Clear();

        if (!this.selection.Contains(itemId))
            this.selection.Add(itemId);

        this.EndTextEditing();
        this.Document.Undo.BreakCoalescing();
        this.RaiseChanged();
    }

    public void SelectAll()
    {
        if (this.Page is not { } page)
            return;

        this.selection.Clear();
        foreach (var item in page.Items)
        {
            if (!item.Locked)
                this.selection.Add(item.Id);
        }

        this.EndTextEditing();
        this.RaiseChanged();
    }

    public void ClearSelection()
    {
        if (this.selection.Count == 0 && this.editingItemId is null)
            return;

        this.selection.Clear();
        this.EndTextEditing();
        this.Document.Undo.BreakCoalescing();
        this.RaiseChanged();
    }

    protected override void OnPageChanged()
    {
        this.selection.Clear();
        this.editingItemId = null;
        this.drag = DragMode.None;
        this.stroke.Clear();
        this.lasso.Clear();
        this.Document.Undo.BreakCoalescing();
    }

    /// <summary>The selection's bounding box in page coordinates, across every selected item.</summary>
    public NoteRect? SelectionPageBounds()
    {
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        var any = false;

        foreach (var item in this.SelectedItems())
        {
            var (x, y, w, h) = item.Bounds();
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x + w);
            maxY = Math.Max(maxY, y + h);
            any = true;
        }

        return any ? new NoteRect(minX, minY, maxX - minX, maxY - minY) : null;
    }

    public NoteRect? SelectionBounds()
        => this.SelectionPageBounds() is { } bounds ? this.ToViewport(bounds) : null;

    /// <summary>
    /// The eight resize handles around the selection, in viewport coordinates.
    /// </summary>
    /// <remarks>
    /// Suppressed while text is being edited. The caret is inside the box at that point, and a handle
    /// sitting on the text's own corner is a target the user hits when they meant to place the caret.
    /// </remarks>
    public IEnumerable<(ShapeHandle Handle, NoteRect Rect)> SelectionHandles()
    {
        if (this.IsEditingText || this.SelectionBounds() is not { } bounds)
            yield break;

        var half = this.HandleSize / 2;
        var midX = bounds.X + bounds.Width / 2;
        var midY = bounds.Y + bounds.Height / 2;

        yield return (ShapeHandle.TopLeft, At(bounds.X, bounds.Y));
        yield return (ShapeHandle.Top, At(midX, bounds.Y));
        yield return (ShapeHandle.TopRight, At(bounds.Right, bounds.Y));
        yield return (ShapeHandle.Right, At(bounds.Right, midY));
        yield return (ShapeHandle.BottomRight, At(bounds.Right, bounds.Bottom));
        yield return (ShapeHandle.Bottom, At(midX, bounds.Bottom));
        yield return (ShapeHandle.BottomLeft, At(bounds.X, bounds.Bottom));
        yield return (ShapeHandle.Left, At(bounds.X, midY));

        NoteRect At(double x, double y) => new(x - half, y - half, this.HandleSize, this.HandleSize);
    }

    // ---- hit testing ----

    /// <summary>
    /// The topmost item under a page point, or null.
    /// </summary>
    /// <remarks>
    /// Back to front, because later items paint over earlier ones and the one a user sees under the
    /// cursor is the last one that covers it. Ink is tested against its path rather than its box: a
    /// stroke's bounding rectangle is mostly empty, and treating it as solid makes a single flourish
    /// swallow every click in that corner of the page.
    /// </remarks>
    public NoteItem? ItemAt(double pageX, double pageY)
    {
        if (this.Page is not { } page)
            return null;

        for (var i = page.Items.Count - 1; i >= 0; i--)
        {
            var item = page.Items[i];
            if (item.Locked)
                continue;

            if (item.Kind == NoteItemKind.Ink)
            {
                if (item.Stroke is { } ink && NearStroke(ink, pageX, pageY, Math.Max(6, ink.Width)))
                    return item;

                continue;
            }

            var (x, y, w, h) = item.Bounds();
            if (pageX >= x && pageX <= x + w && pageY >= y && pageY <= y + h)
                return item;
        }

        return null;
    }

    static bool NearStroke(InkStroke ink, double x, double y, double tolerance)
    {
        var squared = tolerance * tolerance;

        for (var i = 0; i < ink.Points.Count; i++)
        {
            var a = ink.Points[i];

            if (i + 1 >= ink.Points.Count)
                return Squared(a.X - x, a.Y - y) <= squared;

            if (DistanceToSegmentSquared(x, y, a, ink.Points[i + 1]) <= squared)
                return true;
        }

        return false;
    }

    static double Squared(double dx, double dy) => dx * dx + dy * dy;

    static double DistanceToSegmentSquared(double x, double y, InkPoint a, InkPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;

        if (lengthSquared < 1e-9)
            return Squared(x - a.X, y - a.Y);

        var t = Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / lengthSquared, 0, 1);

        return Squared(x - (a.X + t * dx), y - (a.Y + t * dy));
    }

    ShapeHandle HandleAt(double viewportX, double viewportY)
    {
        foreach (var (which, rect) in this.SelectionHandles())
        {
            // Grown by a couple of pixels: a nine-pixel target is a hard one for a finger, and being
            // slightly generous here costs nothing because the handles sit on empty canvas.
            var padded = new NoteRect(rect.X - 3, rect.Y - 3, rect.Width + 6, rect.Height + 6);
            if (padded.Contains(viewportX, viewportY))
                return which;
        }

        return ShapeHandle.None;
    }

    // ---- in-flight drag state, for the painter ----

    /// <summary>The stroke being drawn right now, so the canvas can paint ink as it is laid down.</summary>
    public IReadOnlyList<InkPoint> LiveStroke => this.stroke;

    public InkTool LiveStrokeTool => this.tool == NoteTool.Highlighter ? InkTool.Highlighter : InkTool.Pen;

    public ArgbColor LiveStrokeColor => this.tool == NoteTool.Highlighter ? this.HighlighterColor : this.PenColor;

    public double LiveStrokeWidth => this.tool == NoteTool.Highlighter ? this.HighlighterWidth : this.PenWidth;

    /// <summary>The lasso being drawn, in page coordinates.</summary>
    public IReadOnlyList<(double X, double Y)> LiveLasso => this.lasso;

    /// <summary>The marquee or the shape being dragged out, in page coordinates.</summary>
    public NoteRect? LiveRect
        => this.drag is DragMode.Marquee or DragMode.CreateShape
            ? NoteRect.FromCorners(this.dragStartPageX, this.dragStartPageY, this.dragLastPageX, this.dragLastPageY)
            : null;

    public bool IsCreatingShape => this.drag == DragMode.CreateShape;

    public bool IsDragging => this.drag != DragMode.None;

    /// <summary>The eraser's circle, so the canvas can show what it is about to take.</summary>
    public (double X, double Y, double Radius)? EraserCursor
        => this.drag == DragMode.Erase ? (this.dragLastPageX, this.dragLastPageY, this.EraserRadius) : null;
}
