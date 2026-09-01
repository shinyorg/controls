using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Text;
using Shiny.Controls.Office.View;

namespace Shiny.Controls.Office.Notebook;

public sealed partial class NotebookEditorController
{
    /// <summary>
    /// Starts whatever the current tool does. Returns true when the press was consumed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Touch is not treated as a slow mouse. With the select tool a finger on empty canvas pans,
    /// because there is no wheel to scroll with and a marquee would leave the page unreachable — the
    /// same reasoning the spreadsheet and document surfaces already record on <see cref="PointerKind"/>.
    /// A pen ignores that and always draws, which is what a pen is for.
    /// </para>
    /// </remarks>
    public bool PointerDown(double viewportX, double viewportY, PointerKind kind = PointerKind.Mouse, bool extend = false)
    {
        if (this.Page is not { } page)
            return false;

        var (pageX, pageY) = this.ToPage(viewportX, viewportY);

        this.dragStartPageX = this.dragLastPageX = pageX;
        this.dragStartPageY = this.dragLastPageY = pageY;
        this.dragStartViewportX = viewportX;
        this.dragStartViewportY = viewportY;
        this.dragStartScrollX = this.ScrollX;
        this.dragStartScrollY = this.ScrollY;
        this.dragMoved = false;
        this.handle = ShapeHandle.None;
        this.dragOrigins.Clear();

        if (this.tool == NoteTool.Pan)
        {
            this.drag = DragMode.Pan;
            return true;
        }

        if (this.IsReadOnly)
        {
            // Read-only still gets to pan and to select, so a shared notebook can be read and copied
            // from; it just never reaches an edit command.
            this.drag = DragMode.Pan;
            return true;
        }

        switch (this.tool)
        {
            case NoteTool.Pen:
            case NoteTool.Highlighter:
                this.drag = DragMode.Ink;
                this.stroke.Clear();
                this.stroke.Add(new InkPoint(pageX, pageY, PressureOf(kind)));
                this.RaiseChanged();
                return true;

            case NoteTool.Eraser:
                this.drag = DragMode.Erase;
                this.Erase(pageX, pageY);
                return true;

            case NoteTool.Lasso:
                this.drag = DragMode.Lasso;
                this.lasso.Clear();
                this.lasso.Add((pageX, pageY));
                this.RaiseChanged();
                return true;

            case NoteTool.Shape:
                this.drag = DragMode.CreateShape;
                this.ClearSelection();
                this.RaiseChanged();
                return true;

            case NoteTool.Text:
            {
                // Landing on an existing container puts the caret in it rather than stacking a new
                // empty box on top of the words the user was aiming at.
                if (this.ItemAt(pageX, pageY) is { } existing && existing.Text is not null)
                {
                    this.Select(existing.Id);
                    this.BeginTextEditing(existing, pageX, pageY);
                }
                else
                {
                    var created = NotebookDocument.NewTextItem(pageX, pageY - 10, style: this.NewTextStyle);
                    this.Document.Execute(new InsertItemCommand(page.Id, created, Label: "Add text"));
                    this.Select(created.Id);
                    this.BeginTextEditing(this.ItemById(created.Id) ?? created, pageX, pageY);
                }

                this.Tool = NoteTool.Select;
                return true;
            }
        }

        // ---- select tool ----

        if (this.HandleAt(viewportX, viewportY) is var grabbed && grabbed != ShapeHandle.None)
        {
            this.drag = DragMode.Resize;
            this.handle = grabbed;
            this.CaptureDragOrigins();
            return true;
        }

        var hit = this.ItemAt(pageX, pageY);

        if (this.IsEditingText && hit is { } inText && inText.Id == this.editingItemId)
        {
            this.drag = DragMode.SelectText;
            this.MoveCaretTo(inText, pageX, pageY, extend);
            return true;
        }

        if (hit is null)
        {
            this.ClearSelection();

            // A finger has no other way to move the page; a mouse gets the marquee.
            this.drag = kind == PointerKind.Touch ? DragMode.Pan : DragMode.Marquee;
            this.RaiseChanged();
            return true;
        }

        if (extend)
        {
            if (this.selection.Contains(hit.Id))
                this.selection.Remove(hit.Id);
            else
                this.selection.Add(hit.Id);

            this.EndTextEditing();
            this.RaiseChanged();
        }
        else if (!this.selection.Contains(hit.Id))
        {
            this.Select(hit.Id);
        }
        else
        {
            this.EndTextEditing();
        }

        this.drag = DragMode.Move;
        this.CaptureDragOrigins();

        return true;
    }

    /// <summary>
    /// A stylus that reports no force, a finger and a mouse all sit at the middle of the range.
    /// </summary>
    /// <remarks>
    /// A host with real pressure overrides it through <see cref="PointerMoveWithPressure"/>. Defaulting
    /// to 0.5 rather than 1 is what keeps a mouse-drawn stroke the same weight as the nominal pen
    /// width, so switching input device does not change how thick the pen looks.
    /// </remarks>
    static double PressureOf(PointerKind kind) => kind == PointerKind.Pen ? 0.5 : 0.5;

    public void PointerMove(double viewportX, double viewportY)
        => this.PointerMoveWithPressure(viewportX, viewportY, 0.5);

    public void PointerMoveWithPressure(double viewportX, double viewportY, double pressure)
    {
        if (this.drag == DragMode.None)
            return;

        var (pageX, pageY) = this.ToPage(viewportX, viewportY);

        if (!this.dragMoved)
        {
            var travelled = Math.Abs(viewportX - this.dragStartViewportX) + Math.Abs(viewportY - this.dragStartViewportY);
            if (travelled < this.DragThreshold)
                return;

            this.dragMoved = true;
        }

        switch (this.drag)
        {
            case DragMode.Pan:
                // Anchored to where the drag began rather than accumulated per sample: summing deltas
                // drifts once the scroll clamps at an edge, and the page slowly slides out from under
                // the finger.
                this.ScrollTo(
                    this.dragStartScrollX - (viewportX - this.dragStartViewportX),
                    this.dragStartScrollY - (viewportY - this.dragStartViewportY));
                return;

            case DragMode.Ink:
                this.AppendStrokePoint(pageX, pageY, pressure);
                return;

            case DragMode.Erase:
                this.dragLastPageX = pageX;
                this.dragLastPageY = pageY;
                this.Erase(pageX, pageY);
                return;

            case DragMode.Lasso:
                this.dragLastPageX = pageX;
                this.dragLastPageY = pageY;
                this.lasso.Add((pageX, pageY));
                this.RaiseChanged();
                return;

            case DragMode.Marquee:
            case DragMode.CreateShape:
                this.dragLastPageX = pageX;
                this.dragLastPageY = pageY;
                this.RaiseChanged();
                return;

            case DragMode.SelectText:
                if (this.ItemById(this.editingItemId) is { } editing)
                    this.MoveCaretTo(editing, pageX, pageY, extend: true);
                return;

            case DragMode.Move:
                this.MoveSelection(pageX - this.dragStartPageX, pageY - this.dragStartPageY);
                return;

            case DragMode.Resize:
                this.ResizeSelection(pageX, pageY);
                return;
        }
    }

    public void PointerUp()
    {
        var finished = this.drag;
        this.drag = DragMode.None;
        this.handle = ShapeHandle.None;

        switch (finished)
        {
            case DragMode.Ink:
                this.CommitStroke();
                break;

            case DragMode.Lasso:
                this.CommitLasso();
                break;

            case DragMode.Marquee:
                this.CommitMarquee();
                break;

            case DragMode.CreateShape:
                this.CommitNewShape();
                break;
        }

        // Ends the coalescing run so the next drag is its own undo step rather than being folded into
        // the one that just finished.
        this.Document.Undo.BreakCoalescing();
        this.dragOrigins.Clear();
        this.RaiseChanged();
    }

    /// <summary>
    /// Double click enters the text of whatever was hit — and starts a new container when nothing was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Writing anywhere is the whole point of the page, so a double click on blank canvas has to
    /// produce somewhere to write. OneNote does it on a <i>single</i> click, which is not available
    /// here: a single click on empty canvas already means "clear the selection and start a marquee",
    /// and giving that up would leave no way to select several things at once.
    /// </para>
    /// <para>
    /// The container is placed slightly above the click so the first line of text lands under the
    /// pointer rather than below it — a box whose top edge is at the cursor puts the words somewhere
    /// the user was not looking.
    /// </para>
    /// </remarks>
    public void PointerDoubleClick(double viewportX, double viewportY)
    {
        if (this.IsReadOnly || this.Page is not { } page)
            return;

        var (pageX, pageY) = this.ToPage(viewportX, viewportY);

        if (this.ItemAt(pageX, pageY) is not { } hit)
        {
            if (this.tool != NoteTool.Select)
                return;

            // The marquee the two presses started has to be called off, or the pointer-up that ends
            // the double click would commit a selection over the container just created.
            this.drag = DragMode.None;

            var created = NotebookDocument.NewTextItem(Math.Max(0, pageX), Math.Max(0, pageY - 10), style: this.NewTextStyle);
            this.Document.Execute(new InsertItemCommand(page.Id, created, Label: "Add text"));
            this.Select(created.Id);
            this.BeginTextEditing(created.Id);

            return;
        }

        if (hit.Kind is NoteItemKind.Ink or NoteItemKind.Image)
        {
            this.Select(hit.Id);
            return;
        }

        this.Select(hit.Id);
        this.BeginTextEditing(hit, pageX, pageY);
    }

    // ---- drag helpers ----

    void CaptureDragOrigins()
    {
        foreach (var item in this.SelectedItems())
            this.dragOrigins[item.Id] = item;
    }

    void MoveSelection(double dx, double dy)
    {
        if (this.Page is not { } page || this.dragOrigins.Count == 0)
            return;

        foreach (var original in this.dragOrigins.Values.ToList())
        {
            if (page.IndexOf(original.Id) < 0)
                continue;

            var (x, y, _, _) = original.Bounds();

            // Built from the original every time, so the drag is a pure function of where the pointer
            // is now. Clamped so nothing can be pushed off the top-left, where it could not be
            // scrolled back to.
            var moved = original.Translate(Math.Max(-x, dx), Math.Max(-y, dy));
            this.Document.Execute(new ReplaceItemCommand(page.Id, moved, "Move", Coalesce: true));
        }
    }

    void ResizeSelection(double pageX, double pageY)
    {
        if (this.Page is not { } page || this.dragOrigins.Count == 0)
            return;

        if (this.SelectionOrigin() is not { } origin || origin.Width <= 0 || origin.Height <= 0)
            return;

        var left = origin.X;
        var top = origin.Y;
        var right = origin.Right;
        var bottom = origin.Bottom;

        if (this.handle is ShapeHandle.TopLeft or ShapeHandle.Left or ShapeHandle.BottomLeft)
            left = Math.Min(pageX, right - 8);

        if (this.handle is ShapeHandle.TopRight or ShapeHandle.Right or ShapeHandle.BottomRight)
            right = Math.Max(pageX, left + 8);

        if (this.handle is ShapeHandle.TopLeft or ShapeHandle.Top or ShapeHandle.TopRight)
            top = Math.Min(pageY, bottom - 8);

        if (this.handle is ShapeHandle.BottomLeft or ShapeHandle.Bottom or ShapeHandle.BottomRight)
            bottom = Math.Max(pageY, top + 8);

        var scaleX = (right - left) / origin.Width;
        var scaleY = (bottom - top) / origin.Height;

        foreach (var original in this.dragOrigins.Values.ToList())
        {
            if (page.IndexOf(original.Id) < 0)
                continue;

            var (itemX, itemY, itemWidth, itemHeight) = original.Bounds();

            NoteItem resized;

            if (original.Kind == NoteItemKind.Ink && original.Stroke is { } ink)
            {
                // Ink has no width and height of its own — its geometry is its points — so it is
                // scaled about the selection's corner instead of assigned a new box. From the stroke
                // as it was when the drag began: scaling the one the previous sample produced
                // multiplies up, and thirty samples of that put the stroke somewhere off the page.
                resized = original with
                {
                    Stroke = ink.Scale(origin.X, origin.Y, scaleX, scaleY).Translate(left - origin.X, top - origin.Y)
                };
            }
            else
            {
                resized = original with
                {
                    X = left + (itemX - origin.X) * scaleX,
                    Y = top + (itemY - origin.Y) * scaleY,
                    Width = Math.Max(8, itemWidth * scaleX),
                    Height = Math.Max(8, itemHeight * scaleY)
                };

                // A container told to be a specific height was told that by the user, so it stops
                // following its text until they ask it to again.
                if (resized.AutoHeight && this.handle is not (ShapeHandle.Left or ShapeHandle.Right))
                    resized = resized with { AutoHeight = false };
            }

            this.Document.Execute(new ReplaceItemCommand(page.Id, resized, "Resize", Coalesce: true));
        }
    }

    NoteRect? SelectionOrigin()
    {
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        var any = false;

        foreach (var original in this.dragOrigins.Values)
        {
            var (x, y, width, height) = original.Bounds();

            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x + width);
            maxY = Math.Max(maxY, y + height);
            any = true;
        }

        return any ? new NoteRect(minX, minY, maxX - minX, maxY - minY) : null;
    }

    // ---- ink ----

    void AppendStrokePoint(double x, double y, double pressure)
    {
        if (this.stroke.Count > 0)
        {
            var last = this.stroke[^1];

            // Points closer together than this add nothing a viewer can see and multiply the file
            // size; a fast flick still lands far enough apart to keep its shape.
            if (Squared(x - last.X, y - last.Y) < 1.2)
                return;
        }

        this.stroke.Add(new InkPoint(x, y, Math.Clamp(pressure, 0.05, 1)));
        this.RaiseChanged();
    }

    void CommitStroke()
    {
        if (this.Page is not { } page)
        {
            this.stroke.Clear();
            return;
        }

        // A tap with the pen down is a dot, not nothing — doubled so the painter has a segment to
        // stroke rather than a single point it would draw as empty.
        if (this.stroke.Count == 1)
            this.stroke.Add(this.stroke[0] with { X = this.stroke[0].X + 0.6 });

        if (this.stroke.Count < 2)
        {
            this.stroke.Clear();
            return;
        }

        var item = NotebookDocument.NewInkItem(new InkStroke
        {
            Points = this.stroke.ToArray(),
            Color = this.LiveStrokeColor,
            Width = this.LiveStrokeWidth,
            Tool = this.LiveStrokeTool
        });

        this.stroke.Clear();
        this.Document.Execute(new InsertItemCommand(page.Id, item, Label: "Draw"));
    }

    void Erase(double x, double y)
    {
        if (this.Page is not { } page)
            return;

        for (var i = page.Items.Count - 1; i >= 0; i--)
        {
            var item = page.Items[i];

            if (item.Locked || item.Kind != NoteItemKind.Ink || item.Stroke is not { } ink)
                continue;

            if (!NearStroke(ink, x, y, this.EraserRadius + ink.Width / 2))
                continue;

            if (this.EraseMode == EraseMode.Stroke)
            {
                this.Document.Execute(new DeleteItemCommand(page.Id, item.Id, "Erase"));
                continue;
            }

            this.ErasePoints(page, item, ink, x, y);
        }
    }

    /// <summary>
    /// Cuts the points under the eraser out of a stroke, leaving the pieces either side.
    /// </summary>
    /// <remarks>
    /// The surviving runs become new items rather than one item with a gap, because a stroke is a
    /// single path: leaving a hole in the point list would have the painter draw a straight line
    /// across the gap the user just rubbed out.
    /// </remarks>
    void ErasePoints(NotebookPage page, NoteItem item, InkStroke ink, double x, double y)
    {
        var radius = this.EraserRadius + ink.Width / 2;
        var squared = radius * radius;

        var runs = new List<List<InkPoint>>();
        List<InkPoint>? run = null;

        foreach (var point in ink.Points)
        {
            if (Squared(point.X - x, point.Y - y) <= squared)
            {
                run = null;
                continue;
            }

            if (run is null)
            {
                run = new List<InkPoint>();
                runs.Add(run);
            }

            run.Add(point);
        }

        var survivors = runs.Where(r => r.Count >= 2).ToList();

        if (survivors.Count == ink.Points.Count)
            return;

        using (this.Document.Undo.BeginTransaction("Erase"))
        {
            this.Document.Execute(new DeleteItemCommand(page.Id, item.Id, "Erase"));

            var index = page.IndexOf(item.Id);
            foreach (var piece in survivors)
            {
                this.Document.Execute(new InsertItemCommand(
                    page.Id,
                    NotebookDocument.NewInkItem(ink with { Points = piece.ToArray() }),
                    index < 0 ? int.MaxValue : index,
                    "Erase"));
            }
        }
    }

    // ---- lasso and marquee ----

    void CommitLasso()
    {
        if (this.Page is not { } page || this.lasso.Count < 3)
        {
            this.lasso.Clear();
            this.RaiseChanged();
            return;
        }

        var polygon = this.lasso.ToArray();
        this.lasso.Clear();

        this.selection.Clear();
        this.EndTextEditing();

        foreach (var item in page.Items)
        {
            if (item.Locked)
                continue;

            if (this.IsInside(item, polygon))
                this.selection.Add(item.Id);
        }

        this.RaiseChanged();
    }

    /// <summary>
    /// Whether the lasso caught an item.
    /// </summary>
    /// <remarks>
    /// Ink is judged on its points, not on its box: circling one word of a handwritten line must not
    /// take the whole line just because its bounding rectangle overlaps. Everything else is judged on
    /// its centre, which is what makes a lasso drawn roughly around a picture take it.
    /// </remarks>
    bool IsInside(NoteItem item, (double X, double Y)[] polygon)
    {
        if (item.Kind == NoteItemKind.Ink && item.Stroke is { } ink)
        {
            if (ink.Points.Count == 0)
                return false;

            var inside = ink.Points.Count(p => Contains(polygon, p.X, p.Y));
            return inside * 2 >= ink.Points.Count;
        }

        var (x, y, w, h) = item.Bounds();

        return Contains(polygon, x + w / 2, y + h / 2);
    }

    /// <summary>Even-odd point-in-polygon. The lasso is implicitly closed from its last point to its first.</summary>
    static bool Contains((double X, double Y)[] polygon, double x, double y)
    {
        var inside = false;

        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var (xi, yi) = polygon[i];
            var (xj, yj) = polygon[j];

            if (yi > y != yj > y && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                inside = !inside;
        }

        return inside;
    }

    void CommitMarquee()
    {
        if (this.Page is not { } page || !this.dragMoved)
            return;

        var rect = NoteRect.FromCorners(this.dragStartPageX, this.dragStartPageY, this.dragLastPageX, this.dragLastPageY);

        this.selection.Clear();

        foreach (var item in page.Items)
        {
            if (item.Locked)
                continue;

            var (x, y, w, h) = item.Bounds();
            if (rect.Intersects(new NoteRect(x, y, w, h)))
                this.selection.Add(item.Id);
        }

        this.RaiseChanged();
    }

    void CommitNewShape()
    {
        if (this.Page is not { } page)
            return;

        var rect = NoteRect.FromCorners(this.dragStartPageX, this.dragStartPageY, this.dragLastPageX, this.dragLastPageY);

        // A click with the shape tool means "one of these, default size", not a zero-sized shape the
        // user then has to find and resize.
        if (rect.Width < 6 || rect.Height < 6)
            rect = new NoteRect(this.dragStartPageX, this.dragStartPageY, 160, 100);

        var item = NotebookDocument.NewShapeItem(
            this.NewShapeGeometry, rect.X, rect.Y, rect.Width, rect.Height, this.NewShapeFill, this.NewShapeOutline);

        this.Document.Execute(new InsertItemCommand(page.Id, item, Label: "Add shape"));
        this.Tool = NoteTool.Select;
        this.Select(item.Id);
    }
}
