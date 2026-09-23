using DocumentFormat.OpenXml;
using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using D = DocumentFormat.OpenXml.Drawing;

namespace Shiny.Controls.Office.Presentation;

/// <summary>A rectangle in viewport coordinates.</summary>
public readonly record struct SlideRect(double X, double Y, double Width, double Height)
{
    public double Right => this.X + this.Width;
    public double Bottom => this.Y + this.Height;

    public bool Contains(double x, double y)
        => x >= this.X && x <= this.Right && y >= this.Y && y <= this.Bottom;
}

/// <summary>
/// Host-independent editing behaviour for a deck: shape selection, dragging, and text editing.
/// </summary>
/// <remarks>
/// <para>
/// Two modes, and the distinction is the whole design. <b>Shape mode</b> selects a whole shape and
/// moves or resizes it. <b>Text mode</b> — entered by double-clicking a shape, exactly as PowerPoint
/// does — puts a caret inside that shape's text and routes typing there. A single click while in text
/// mode moves the caret; a single click outside the shape leaves text mode.
/// </para>
/// <para>
/// Everything is expressed in one of two coordinate spaces: <b>slide</b> coordinates, which is what
/// the model and the OOXML store, and <b>viewport</b> coordinates, which is what a pointer arrives
/// in. <see cref="ToSlide"/> and <see cref="ToViewport"/> are the only places the two meet.
/// </para>
/// </remarks>
public sealed class SlideEditorController : SlideController
{
    readonly SlideDeck deck;
    readonly ITextMeasurer measurer;

    int selected = -1;
    ShapeHandle dragging = ShapeHandle.None;
    double dragStartX;
    double dragStartY;
    SlideRect dragOrigin;

    SlidePosition caret;
    SlidePosition anchor;

    /// <summary>The table cell the caret is in, when the selection is a table being edited.</summary>
    (int Row, int Column)? activeCell;

    /// <summary>The group double-clicked into, whose children a click now reaches. Null at the top level.</summary>
    OpenXmlElement? enteredGroup;

    /// <summary>How many times the clip has been pasted onto the slide it came from, to cascade the copies.</summary>
    int pasteCount;

    public SlideEditorController(SlideDeck deck, ITextMeasurer measurer)
        : base(deck)
    {
        ArgumentNullException.ThrowIfNull(measurer);

        this.deck = deck;
        this.measurer = measurer;
        this.Find = new SlideFinder(this);

        // Edited is raised from here rather than from each editing method: every edit reaches the
        // model through Reproject, including the ones a host drives directly (a drag executes a
        // command per pointer sample), so this is the one place that sees all of them - and it never
        // fires for a command that turned out to be a no-op.
        // Weakly: the deck is the app's and outlives the view this controller paints. See WeakEvent.
        deck.ContentChanged += WeakEvent.Forward(this, deck, static c => c.OnDeckContentChanged(), static (d, h) => d.ContentChanged -= h);
        deck.SlidesChanged += WeakEvent.Forward<SlideEditorController, SlideDeck, SlidesChangedEventArgs>(
            this, deck, static (c, e) => c.OnSlidesChanged(e.Focus), static (d, h) => d.SlidesChanged -= h);
    }

    /// <summary>
    /// Slides were added, removed or reordered — by this controller, by undo, or by anything else
    /// driving the deck.
    /// </summary>
    /// <remarks>
    /// The selection is dropped rather than carried: it is an index into the slide that was showing,
    /// and after a reorder that index names a shape on a different slide. Going to where the change
    /// happened is what makes undoing a delete visibly bring the slide back instead of restoring it
    /// somewhere off screen.
    /// </remarks>
    void OnSlidesChanged(int focus)
    {
        this.dragging = ShapeHandle.None;
        this.selected = -1;
        this.IsEditingText = false;
        this.activeCell = null;
        this.enteredGroup = null;

        this.Index = focus;
        this.caret = new SlidePosition(this.Index, -1, 0, 0);
        this.anchor = this.caret;
    }

    void OnDeckContentChanged()
    {
        // The matches were collected from text that has just changed underneath them.
        this.Find.Invalidate();
        this.RefreshCaretFormat();
        this.RaiseChanged();
        this.Edited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Text search across the deck, which is what the toolbar's find box drives.
    /// </summary>
    /// <remarks>
    /// Created with the controller so a host can bind a find bar to it before anything is searched
    /// for, and the bar's readout is live from the first keystroke.
    /// </remarks>
    public SlideFinder Find { get; }

    /// <summary>Size of a resize handle, in viewport pixels.</summary>
    public double HandleSize { get; set; } = 9;

    /// <summary>Index of the selected shape within the current slide, or -1.</summary>
    public int SelectedShape => this.selected;

    public SlideShape? Selection
        => this.Current?.Shapes.ElementAtOrDefault(this.selected);

    /// <summary>True while the caret is inside a shape's text rather than on the shape itself.</summary>
    public bool IsEditingText { get; private set; }

    public bool IsDragging => this.dragging != ShapeHandle.None;

    public SlidePosition Caret => this.caret;

    /// <summary>The text selection, empty when the caret is just a caret.</summary>
    public SlideTextRange TextSelection => new(this.anchor, this.caret);

    public bool CanUndo => this.deck.Undo.CanUndo;
    public bool CanRedo => this.deck.Undo.CanRedo;

    public bool IsReadOnly { get; set; }

    /// <summary>Formatting under the caret, so a toolbar can show what is active.</summary>
    public SlideCaretFormat CaretFormat { get; private set; } = SlideCaretFormat.Default;

    /// <summary>
    /// Raised after an edit actually changes the deck.
    /// </summary>
    /// <remarks>
    /// Sourced from the deck rather than from each editing method, so it covers edits a host drives
    /// straight through the commands — a drag among them — and never fires for a no-op.
    /// </remarks>
    public event EventHandler? Edited;

    // ---- coordinate mapping ----

    /// <summary>Scale from slide coordinates to viewport ones. Zero when nothing is placed.</summary>
    public double Scale
        => this.SinglePlacement() is { } placement && this.Deck.SlideWidth > 0
            ? placement.Width / this.Deck.SlideWidth
            : 0;

    public (double X, double Y)? ToSlide(double viewportX, double viewportY)
    {
        if (this.SinglePlacement() is not { } placement || this.Scale <= 0)
            return null;

        return ((viewportX - placement.X) / this.Scale, (viewportY - placement.Y) / this.Scale);
    }

    public (double X, double Y)? ToViewport(double slideX, double slideY)
    {
        if (this.SinglePlacement() is not { } placement || this.Scale <= 0)
            return null;

        return (placement.X + slideX * this.Scale, placement.Y + slideY * this.Scale);
    }

    /// <summary>A shape's rectangle in viewport coordinates.</summary>
    public SlideRect? BoundsOf(SlideShape shape)
    {
        if (this.ToViewport(shape.X, shape.Y) is not { } origin)
            return null;

        return new SlideRect(origin.X, origin.Y, shape.Width * this.Scale, shape.Height * this.Scale);
    }

    public SlideRect? SelectionBounds()
        => this.Selection is { } shape ? this.BoundsOf(shape) : null;

    /// <summary>
    /// The eight resize handles around the selection, in viewport coordinates.
    /// </summary>
    /// <remarks>
    /// Emitted even for a shape too small to hold them, because a handle a user cannot grab is a
    /// shape they cannot resize back.
    /// </remarks>
    public IEnumerable<(ShapeHandle Handle, SlideRect Rect)> SelectionHandles()
    {
        if (this.SelectionBounds() is not { } bounds)
            yield break;

        var size = this.HandleSize;
        var half = size / 2;

        var midX = bounds.X + bounds.Width / 2;
        var midY = bounds.Y + bounds.Height / 2;

        yield return (ShapeHandle.TopLeft, Handle(bounds.X, bounds.Y));
        yield return (ShapeHandle.Top, Handle(midX, bounds.Y));
        yield return (ShapeHandle.TopRight, Handle(bounds.Right, bounds.Y));
        yield return (ShapeHandle.Right, Handle(bounds.Right, midY));
        yield return (ShapeHandle.BottomRight, Handle(bounds.Right, bounds.Bottom));
        yield return (ShapeHandle.Bottom, Handle(midX, bounds.Bottom));
        yield return (ShapeHandle.BottomLeft, Handle(bounds.X, bounds.Bottom));
        yield return (ShapeHandle.Left, Handle(bounds.X, midY));

        SlideRect Handle(double x, double y) => new(x - half, y - half, size, size);
    }

    // ---- hit testing ----

    /// <summary>
    /// The topmost editable shape under a point, or -1.
    /// </summary>
    /// <remarks>
    /// Searched back to front because later shapes paint over earlier ones, so the one a user sees
    /// under the cursor is the last one that covers it. Layout and master shapes are skipped: they
    /// belong to every slide using that layout, not to this one.
    /// </remarks>
    public int ShapeAt(double viewportX, double viewportY)
    {
        if (this.Current is not { } slide)
            return -1;

        // Inside a group that has been entered, its children are what a click reaches; a click that
        // misses all of them leaves the group, as in PowerPoint.
        if (this.enteredGroup is { } group)
        {
            var inside = Find(x => ReferenceEquals(x.Group, group));
            if (inside >= 0)
                return inside;

            this.enteredGroup = null;
        }

        // At the top level a group's children are never hit directly: the group's own entry is.
        return Find(x => !x.IsInGroup);

        int Find(Func<SlideShape, bool> eligible)
        {
            for (var i = slide.Shapes.Count - 1; i >= 0; i--)
            {
                var shape = slide.Shapes[i];
                if (!shape.IsEditable || !eligible(shape))
                    continue;

                if (this.BoundsOf(shape) is { } bounds && bounds.Contains(viewportX, viewportY))
                    return i;
            }

            return -1;
        }
    }

    /// <summary>True while the selection is a group's child reached by double-clicking into the group.</summary>
    public bool IsInsideGroup => this.enteredGroup is not null;

    /// <summary>The table cell the caret is in, or null when not editing a table.</summary>
    public (int Row, int Column)? ActiveCell => this.activeCell;

    /// <summary>The handle under a point, or <see cref="ShapeHandle.None"/>.</summary>
    public ShapeHandle HandleAt(double viewportX, double viewportY)
    {
        foreach (var (handle, rect) in this.SelectionHandles())
        {
            if (rect.Contains(viewportX, viewportY))
                return handle;
        }

        return this.SelectionBounds()?.Contains(viewportX, viewportY) == true
            ? ShapeHandle.Body
            : ShapeHandle.None;
    }

    /// <summary>The text position under a point inside the text being edited.</summary>
    public SlidePosition? TextPositionAt(double viewportX, double viewportY)
    {
        if (this.TextTarget() is not { } target || this.Scale <= 0)
            return null;

        var layout = ShapeTextLayout.Layout(target.Body, target.Width, target.Height, this.measurer);
        var localX = (viewportX - target.Bounds.X) / this.Scale;
        var localY = (viewportY - target.Bounds.Y) / this.Scale;

        var (paragraph, offset) = ShapeTextLayout.PositionAt(layout, localX, localY, this.measurer);
        return this.Position(paragraph, offset);
    }

    /// <summary>
    /// The text being edited and where it is: a shape's own text, or the active cell of a table.
    /// </summary>
    /// <remarks>
    /// Bounds are in viewport coordinates, width and height in slide units — which is what the text
    /// layout runs in. Everything that lays out, hit-tests or measures the text goes through here, so
    /// a cell and a shape are edited by exactly the same code.
    /// </remarks>
    (ShapeTextBody Body, SlideRect Bounds, double Width, double Height)? TextTarget()
    {
        if (this.Selection is not { } shape || this.BoundsOf(shape) is not { } bounds)
            return null;

        if (this.activeCell is { } cell)
        {
            if (shape.Table is not { } table ||
                table.Rows.ElementAtOrDefault(cell.Row)?.ElementAtOrDefault(cell.Column)?.Text is not { } text)
            {
                return null;
            }

            var (x, y, w, h) = table.CellBounds(cell.Row, cell.Column, shape.Width, shape.Height);
            return (text, new SlideRect(bounds.X + x * this.Scale, bounds.Y + y * this.Scale, w * this.Scale, h * this.Scale), w, h);
        }

        return shape.Text is { } body ? (body, bounds, shape.Width, shape.Height) : null;
    }

    ShapeTextBody? ActiveText => this.TextTarget()?.Body;

    SlidePosition Position(int paragraph, int offset)
        => new(this.Index, this.selected, paragraph, offset) { Cell = this.activeCell };

    /// <summary>The cell of a selected table under a point, or null.</summary>
    (int Row, int Column)? CellAt(double viewportX, double viewportY)
    {
        if (this.Selection is not { Table: { } table } shape || this.BoundsOf(shape) is not { } bounds || this.Scale <= 0)
            return null;

        return table.CellAt((viewportX - bounds.X) / this.Scale, (viewportY - bounds.Y) / this.Scale, shape.Width, shape.Height);
    }

    /// <summary>
    /// Moves the caret to the next (or previous) cell in reading order, selecting its text — Tab and
    /// Shift+Tab. Stops at either end of the table.
    /// </summary>
    public void MoveToCell(int direction)
    {
        if (this.activeCell is not { } current || this.Selection?.Table is not { } table)
            return;

        var cells = new List<(int Row, int Column)>();
        for (var r = 0; r < table.Rows.Count; r++)
        {
            for (var c = 0; c < table.Rows[r].Count; c++)
            {
                if (!table.Rows[r][c].IsMerged && table.Rows[r][c].Text is not null)
                    cells.Add((r, c));
            }
        }

        var at = cells.IndexOf(current) + Math.Sign(direction);
        if (at < 0 || at >= cells.Count)
            return;

        this.activeCell = cells[at];
        this.SelectAll();
    }

    /// <summary>A selected table can be typed into: its first cell with text, when no point says which.</summary>
    (int Row, int Column)? FirstCell()
    {
        if (this.Selection?.Table is not { } table)
            return null;

        for (var r = 0; r < table.Rows.Count; r++)
        {
            for (var c = 0; c < table.Rows[r].Count; c++)
            {
                if (!table.Rows[r][c].IsMerged && table.Rows[r][c].Text is not null)
                    return (r, c);
            }
        }

        return null;
    }

    // ---- selection ----

    public void Select(int shape)
    {
        var clamped = this.Current is { } slide && shape >= 0 && shape < slide.Shapes.Count && slide.Shapes[shape].IsEditable
            ? shape
            : -1;

        if (clamped == this.selected)
            return;

        this.selected = clamped;
        this.IsEditingText = false;
        this.activeCell = null;

        // Selecting something outside the group that was entered leaves it.
        if (this.enteredGroup is { } group && this.Selection is { } now && !ReferenceEquals(now.Group, group))
            this.enteredGroup = null;

        this.caret = new SlidePosition(this.Index, clamped, 0, 0);
        this.anchor = this.caret;

        this.RefreshCaretFormat();
        this.RaiseChanged();
    }

    public void ClearSelection() => this.Select(-1);

    /// <summary>Puts the caret inside the selected shape's text — or, for a table, the cell — at a point.</summary>
    public void BeginTextEditing(double viewportX, double viewportY)
    {
        if (this.Selection is not { } shape)
            return;

        if (shape.Table is not null)
        {
            this.activeCell = this.CellAt(viewportX, viewportY) ?? this.FirstCell();
            if (this.activeCell is null)
                return;
        }
        else if (shape.Text is null)
        {
            return;
        }

        this.IsEditingText = true;

        var position = this.TextPositionAt(viewportX, viewportY) ?? this.Position(0, 0);
        this.caret = position;
        this.anchor = position;

        this.RefreshCaretFormat();
        this.RaiseChanged();
    }

    public void EndTextEditing()
    {
        if (!this.IsEditingText)
            return;

        this.IsEditingText = false;
        this.activeCell = null;
        this.RaiseChanged();
    }

    /// <summary>Moves the caret, collapsing the selection unless extending it.</summary>
    public void MoveCaret(SlidePosition position, bool extend = false)
    {
        this.caret = position;
        if (!extend)
            this.anchor = position;

        this.RefreshCaretFormat();
        this.RaiseChanged();
    }

    // ---- pointer gestures ----

    /// <summary>
    /// Starts a pointer interaction.
    /// </summary>
    /// <remarks>
    /// Returns true when the editor took the gesture, so a host knows whether to keep tracking the
    /// pointer or let it fall through to scrolling.
    /// </remarks>
    public bool PointerDown(double x, double y, bool extendSelection = false)
    {
        if (this.Mode != SlideViewMode.Single || this.IsReadOnly)
            return false;

        if (this.IsEditingText)
        {
            // While editing text, a click inside the shape moves the caret; anywhere else leaves the
            // text and behaves as an ordinary click on the slide.
            if (this.SelectionBounds()?.Contains(x, y) == true)
            {
                // Inside a table, a click in another cell moves the caret into that cell.
                if (this.activeCell is { } current && this.CellAt(x, y) is { } cell && cell != current && !extendSelection)
                {
                    this.activeCell = cell;
                    this.anchor = this.caret = this.TextPositionAt(x, y) ?? this.Position(0, 0);
                    this.RefreshCaretFormat();
                    this.RaiseChanged();
                    this.dragging = ShapeHandle.None;
                    return true;
                }

                if (this.TextPositionAt(x, y) is { } position)
                    this.MoveCaret(position, extendSelection);

                this.dragging = ShapeHandle.None;
                return true;
            }

            this.EndTextEditing();
        }

        var handle = this.HandleAt(x, y);
        if (handle is not ShapeHandle.None && this.SelectionBounds() is { } bounds)
        {
            this.dragging = handle;
            this.dragStartX = x;
            this.dragStartY = y;
            this.dragOrigin = bounds;
            return true;
        }

        var hit = this.ShapeAt(x, y);
        this.Select(hit);

        if (hit < 0)
            return false;

        // A press on a freshly selected shape starts a move straight away, so selecting and dragging
        // are one gesture rather than two.
        this.dragging = ShapeHandle.Body;
        this.dragStartX = x;
        this.dragStartY = y;
        this.dragOrigin = this.SelectionBounds() ?? default;
        return true;
    }

    /// <summary>Extends a drag, or a text selection.</summary>
    public void PointerMove(double x, double y)
    {
        if (this.IsEditingText && this.dragging == ShapeHandle.None)
        {
            if (this.TextPositionAt(x, y) is { } position)
                this.MoveCaret(position, extend: true);

            return;
        }

        if (this.dragging == ShapeHandle.None || this.Scale <= 0)
            return;

        var dx = (x - this.dragStartX) / this.Scale;
        var dy = (y - this.dragStartY) / this.Scale;

        if (this.Selection is not { } shape)
            return;

        var slideBounds = new SlideRect(shape.X, shape.Y, shape.Width, shape.Height);
        var origin = this.ToSlide(this.dragOrigin.X, this.dragOrigin.Y);
        if (origin is null)
            return;

        var startX = origin.Value.X;
        var startY = origin.Value.Y;
        var startWidth = this.dragOrigin.Width / this.Scale;
        var startHeight = this.dragOrigin.Height / this.Scale;

        var next = this.dragging switch
        {
            ShapeHandle.Body => new SlideRect(startX + dx, startY + dy, startWidth, startHeight),
            ShapeHandle.Left => new SlideRect(startX + dx, startY, startWidth - dx, startHeight),
            ShapeHandle.Right => new SlideRect(startX, startY, startWidth + dx, startHeight),
            ShapeHandle.Top => new SlideRect(startX, startY + dy, startWidth, startHeight - dy),
            ShapeHandle.Bottom => new SlideRect(startX, startY, startWidth, startHeight + dy),
            ShapeHandle.TopLeft => new SlideRect(startX + dx, startY + dy, startWidth - dx, startHeight - dy),
            ShapeHandle.TopRight => new SlideRect(startX, startY + dy, startWidth + dx, startHeight - dy),
            ShapeHandle.BottomLeft => new SlideRect(startX + dx, startY, startWidth - dx, startHeight + dy),
            ShapeHandle.BottomRight => new SlideRect(startX, startY, startWidth + dx, startHeight + dy),
            _ => slideBounds
        };

        this.Execute(new SetShapeBoundsCommand(
            this.Index,
            this.selected,
            next.X,
            next.Y,
            Math.Max(4, next.Width),
            Math.Max(4, next.Height)));
    }

    public void PointerUp()
    {
        if (this.dragging == ShapeHandle.None)
            return;

        this.dragging = ShapeHandle.None;

        // Ends the coalescing run, so the *next* drag is a separate undo step from this one.
        this.deck.Undo.BreakCoalescing();
    }

    /// <summary>
    /// A double-click enters the shape's text, which is what PowerPoint does — or goes into a group, or
    /// into the table cell under the pointer.
    /// </summary>
    public void PointerDoubleClick(double x, double y)
    {
        if (this.Mode != SlideViewMode.Single || this.IsReadOnly)
            return;

        var hit = this.ShapeAt(x, y);
        if (hit < 0)
            return;

        // Already inside this text, so the second double-click means the word under it - the same
        // thing it means in the document editor.
        if (this.IsEditingText && this.selected == hit &&
            (this.activeCell is null || this.CellAt(x, y) == this.activeCell) &&
            this.TextPositionAt(x, y) is { } inside)
        {
            this.SelectWordAt(inside);
            return;
        }

        // Into a group: its child under the pointer becomes the selection, and a second double-click
        // goes into that child's text.
        if (this.Current?.Shapes[hit] is { IsGroup: true, Element: { } group })
        {
            this.enteredGroup = group;
            hit = this.ShapeAt(x, y);
            if (hit < 0 || this.Current.Shapes[hit].IsGroup)
            {
                this.Select(hit);
                return;
            }
        }

        this.Select(hit);

        if (this.Selection is { Table: not null } or { Text: not null })
            this.BeginTextEditing(x, y);
    }

    /// <summary>Selects the word at <paramref name="position"/> — what a double-click inside text does.</summary>
    public void SelectWordAt(SlidePosition position)
    {
        if (this.ActiveText?.Paragraphs.ElementAtOrDefault(position.Paragraph) is not { } paragraph)
            return;

        var (start, end) = WordBoundaries.RangeAt(paragraph.PlainText, position.Offset);

        this.anchor = position with { Offset = start };
        this.caret = position with { Offset = end };

        this.RefreshCaretFormat();
        this.RaiseChanged();
    }

    /// <summary>Selects the whole paragraph the position is in.</summary>
    public void SelectParagraphAt(SlidePosition position)
    {
        this.anchor = position with { Offset = 0 };
        this.caret = position with { Offset = this.LengthOf(position.Paragraph) };

        this.RefreshCaretFormat();
        this.RaiseChanged();
    }

    // ---- text editing ----

    /// <summary>Inserts text at the caret, replacing the selection first.</summary>
    public void InsertText(string text)
    {
        if (!this.CanEditText() || text.Length == 0)
            return;

        // Checked before the space is inserted, so the marker and the space both disappear into the
        // list rather than surviving as the first characters of the item.
        if (text == " " && this.TryAutoFormatList())
            return;

        // No transaction in the common case. A transaction closes as a composite, which ends the
        // coalescing run - so wrapping every keystroke in one makes each character its own undo step.
        if (this.TextSelection.IsEmpty)
        {
            var caret = this.caret;
            this.Execute(new InsertSlideTextCommand(caret, text));
            this.MoveCaret(caret with { Offset = caret.Offset + text.Length });
        }
        else
        {
            // Replacing a selection genuinely is two edits, and they undo together.
            using (this.deck.Undo.BeginTransaction("Typing"))
            {
                var at = this.DeleteSelectionCore();
                this.Execute(new InsertSlideTextCommand(at, text));
                this.MoveCaret(at with { Offset = at.Offset + text.Length });
            }
        }
    }

    /// <summary>Splits the paragraph at the caret — Enter.</summary>
    public void InsertParagraph()
    {
        if (!this.CanEditText())
            return;

        if (this.TextSelection.IsEmpty)
        {
            var caret = this.caret;
            this.Execute(new SplitSlideParagraphCommand(caret));
            this.MoveCaret(caret with { Paragraph = caret.Paragraph + 1, Offset = 0 });
        }
        else
        {
            using (this.deck.Undo.BeginTransaction("New paragraph"))
            {
                var at = this.DeleteSelectionCore();
                this.Execute(new SplitSlideParagraphCommand(at));
                this.MoveCaret(at with { Paragraph = at.Paragraph + 1, Offset = 0 });
            }
        }
    }

    /// <summary>Backspace: deletes the selection, or the character before the caret.</summary>
    public void Backspace()
    {
        if (!this.CanEditText())
            return;

        if (!this.TextSelection.IsEmpty)
        {
            this.DeleteSelection();
            return;
        }

        var at = this.caret;

        if (at.Offset > 0)
        {
            this.Execute(new DeleteSlideRangeCommand(new SlideTextRange(at with { Offset = at.Offset - 1 }, at)));
            this.MoveCaret(at with { Offset = at.Offset - 1 });
        }
        else if (at.Paragraph > 0)
        {
            // Joining onto the previous paragraph puts the caret where the join happened, which is
            // the end of what that paragraph used to be — captured before the merge, since afterwards
            // the two are one.
            var previousLength = this.LengthOf(at.Paragraph - 1);
            this.Execute(new MergeSlideParagraphCommand(at));
            this.MoveCaret(at with { Paragraph = at.Paragraph - 1, Offset = previousLength });
        }
        else
        {
            return;
        }
    }

    /// <summary>Delete: removes the selection, or the character after the caret.</summary>
    public void Delete()
    {
        if (!this.CanEditText())
            return;

        if (!this.TextSelection.IsEmpty)
        {
            this.DeleteSelection();
            return;
        }

        var at = this.caret;
        var length = this.LengthOf(at.Paragraph);

        if (at.Offset < length)
        {
            this.Execute(new DeleteSlideRangeCommand(new SlideTextRange(at, at with { Offset = at.Offset + 1 })));
        }
        else if (this.ActiveText is { } body && at.Paragraph + 1 < body.Paragraphs.Count)
        {
            this.Execute(new MergeSlideParagraphCommand(at with { Paragraph = at.Paragraph + 1 }));
        }
        else
        {
            return;
        }
    }

    public void DeleteSelection()
    {
        if (!this.CanEditText() || this.TextSelection.IsEmpty)
            return;

        var at = this.DeleteSelectionCore();
        this.MoveCaret(at);
    }

    /// <summary>Removes the selected span and returns where the caret belongs afterwards.</summary>
    SlidePosition DeleteSelectionCore()
    {
        var range = this.TextSelection.Normalized();
        if (range.IsEmpty)
            return this.caret;

        this.Execute(new DeleteSlideRangeCommand(range));
        return range.Start;
    }

    // ---- caret movement ----

    public void MoveLeft(bool extend = false)
    {
        var at = this.caret;

        if (at.Offset > 0)
            this.MoveCaret(at with { Offset = at.Offset - 1 }, extend);
        else if (at.Paragraph > 0)
            this.MoveCaret(at with { Paragraph = at.Paragraph - 1, Offset = this.LengthOf(at.Paragraph - 1) }, extend);
    }

    public void MoveRight(bool extend = false)
    {
        var at = this.caret;
        var length = this.LengthOf(at.Paragraph);

        if (at.Offset < length)
            this.MoveCaret(at with { Offset = at.Offset + 1 }, extend);
        else if (this.ActiveText is { } body && at.Paragraph + 1 < body.Paragraphs.Count)
            this.MoveCaret(at with { Paragraph = at.Paragraph + 1, Offset = 0 }, extend);
    }

    public void MoveUp(bool extend = false)
    {
        var at = this.caret;
        if (at.Paragraph <= 0)
        {
            this.MoveCaret(at with { Offset = 0 }, extend);
            return;
        }

        var target = at.Paragraph - 1;
        this.MoveCaret(at with { Paragraph = target, Offset = Math.Min(at.Offset, this.LengthOf(target)) }, extend);
    }

    public void MoveDown(bool extend = false)
    {
        var at = this.caret;
        if (this.ActiveText is not { } body || at.Paragraph + 1 >= body.Paragraphs.Count)
        {
            this.MoveCaret(at with { Offset = this.LengthOf(at.Paragraph) }, extend);
            return;
        }

        var target = at.Paragraph + 1;
        this.MoveCaret(at with { Paragraph = target, Offset = Math.Min(at.Offset, this.LengthOf(target)) }, extend);
    }

    public void MoveToLineStart(bool extend = false) => this.MoveCaret(this.caret with { Offset = 0 }, extend);

    public void MoveToLineEnd(bool extend = false)
        => this.MoveCaret(this.caret with { Offset = this.LengthOf(this.caret.Paragraph) }, extend);

    /// <summary>Selects every paragraph in the shape.</summary>
    public void SelectAll()
    {
        if (this.ActiveText is not { } body || body.Paragraphs.Count == 0)
            return;

        this.anchor = this.Position(0, 0);
        this.caret = this.Position(body.Paragraphs.Count - 1, this.LengthOf(body.Paragraphs.Count - 1));

        this.RefreshCaretFormat();
        this.RaiseChanged();
    }

    // ---- formatting ----

    public void ToggleBold() => this.FormatRuns(ShapeTextEditor.ToggleBold(!this.CaretFormat.Bold), "Bold");

    public void ToggleItalic() => this.FormatRuns(ShapeTextEditor.ToggleItalic(!this.CaretFormat.Italic), "Italic");

    public void ToggleUnderline() => this.FormatRuns(ShapeTextEditor.ToggleUnderline(!this.CaretFormat.Underline), "Underline");

    public void ToggleStrikethrough() => this.FormatRuns(ShapeTextEditor.ToggleStrike(!this.CaretFormat.Strike), "Strikethrough");

    public void SetFontSize(double points) => this.FormatRuns(ShapeTextEditor.SetFontSize(points), "Font size");

    public void SetFontFamily(string family) => this.FormatRuns(ShapeTextEditor.SetFontFamily(family), "Font");

    public void SetTextColor(ArgbColor color) => this.FormatRuns(ShapeTextEditor.SetColor(color), "Text colour");

    /// <summary>Highlights the selection, or clears it when passed null.</summary>
    public void SetHighlight(ArgbColor? color)
        => this.FormatRuns(ShapeTextEditor.SetHighlight(color), color is null ? "Remove highlight" : "Highlight");

    /// <summary>Highlights with <paramref name="color"/>, or clears when that colour is already on.</summary>
    public void ToggleHighlight(ArgbColor color)
        => this.SetHighlight(this.CaretFormat.Highlight == color ? null : color);

    public void SetAlignment(TextAlignment alignment)
        => this.FormatParagraphs(ShapeTextEditor.SetAlignment(alignment), "Alignment");

    /// <summary>
    /// Indents or outdents the paragraphs the selection touches — what Tab and Shift+Tab do.
    /// </summary>
    /// <remarks>
    /// Each paragraph moves relative to its own level. Reading one level off the first paragraph and
    /// applying it to all of them flattened a mixed selection onto a single depth, which is only
    /// invisible while the selection happens to be one line.
    /// </remarks>
    public void ShiftLevel(int delta)
    {
        if (delta == 0)
            return;

        this.FormatParagraphs(ShapeTextEditor.ShiftLevel(delta), delta > 0 ? "Indent" : "Outdent");
    }

    /// <summary>Turns the selected paragraphs into a bulleted list, or out of one when they already are.</summary>
    public void ToggleBulletList()
        => this.SetListStyle(this.CaretFormat.List == ListStyle.Bullet ? ListStyle.None : ListStyle.Bullet);

    /// <summary>Turns the selected paragraphs into a numbered list, or out of one when they already are.</summary>
    public void ToggleNumberedList()
        => this.SetListStyle(this.CaretFormat.List == ListStyle.Numbered ? ListStyle.None : ListStyle.Numbered);

    /// <summary>
    /// Sets the mark in front of every paragraph the selection touches.
    /// </summary>
    /// <remarks>
    /// <see cref="ListStyle.None"/> is written explicitly rather than by removing the bullet element:
    /// a body placeholder inherits its bullet from the master, so leaving the element out puts the
    /// inherited bullet back instead of taking it away.
    /// </remarks>
    public void SetListStyle(ListStyle style)
        => this.FormatParagraphs(ShapeTextEditor.SetBullet(style), style switch
        {
            ListStyle.Bullet => "Bulleted list",
            ListStyle.Numbered => "Numbered list",
            _ => "Remove list"
        });

    /// <summary>
    /// What the Tab key does inside a shape's text: nest the item, or un-nest it with Shift.
    /// </summary>
    /// <remarks>
    /// Unconditional, unlike the Word editor's — a shape's paragraphs all carry an outline level
    /// whether or not they are drawing a bullet, so there is no "not in a list" case to fall through
    /// to a tab character. A slide has no tab stops to speak of either.
    /// </remarks>
    /// <returns>True when the key was consumed.</returns>
    public bool HandleTab(bool shift = false)
    {
        if (!this.CanEditText())
            return false;

        // In a table Tab walks the cells, as it does in PowerPoint, rather than nesting a bullet.
        if (this.activeCell is not null)
        {
            this.MoveToCell(shift ? -1 : 1);
            return true;
        }

        this.ShiftLevel(shift ? -1 : 1);
        return true;
    }

    /// <summary>
    /// Turns a marker the user typed by hand into a real list, if that is what they typed.
    /// </summary>
    /// <remarks>
    /// The Word editor's behaviour, sharing the same detector so the two cannot drift: type <c>-</c>
    /// or <c>1.</c> at the start of a paragraph, press space, and the marker becomes the bullet.
    /// </remarks>
    /// <returns>True when a list was created and the space should not be inserted.</returns>
    bool TryAutoFormatList()
    {
        if (!this.IsAutoFormatListEnabled || !this.TextSelection.IsEmpty)
            return false;

        if (this.ActiveText?.Paragraphs.ElementAtOrDefault(this.caret.Paragraph) is not { } paragraph)
            return false;

        // Already carrying a mark: what was typed is text the user meant to keep.
        if (paragraph.List != ListStyle.None)
            return false;

        var text = paragraph.PlainText;
        if (this.caret.Offset == 0 || this.caret.Offset > text.Length)
            return false;

        // Everything before the caret, and nothing after it: text already in the paragraph becomes
        // the item's text rather than blocking the conversion.
        var style = ListAutoFormat.Detect(text[..this.caret.Offset]);
        if (style == ListStyle.None)
            return false;

        var start = this.caret with { Offset = 0 };

        using (this.deck.Undo.BeginTransaction(style == ListStyle.Bullet ? "Bulleted list" : "Numbered list"))
        {
            this.Execute(new DeleteSlideRangeCommand(new SlideTextRange(start, this.caret)));
            this.Execute(new FormatSlideParagraphsCommand(
                new SlideTextRange(start, start),
                ShapeTextEditor.SetBullet(style),
                style == ListStyle.Bullet ? "Bulleted list" : "Numbered list"));
        }

        this.MoveCaret(start);
        return true;
    }

    /// <summary>
    /// Whether typing <c>-</c>, <c>*</c> or <c>1.</c> followed by a space starts a list. On by default.
    /// </summary>
    public bool IsAutoFormatListEnabled { get; set; } = true;

    /// <remarks>
    /// A bare caret <em>inside</em> a word formats that word, as PowerPoint and Word do. Anywhere else
    /// a bare caret formats the paragraph end mark, which only shows once something is typed. Without
    /// the word case, clicking into a word and pressing Bold changed nothing on screen.
    /// </remarks>
    void FormatRuns(Action<D.RunProperties> apply, string label)
    {
        if (!this.CanEditText())
            return;

        var range = this.TextSelection.Normalized();
        if (range.IsEmpty && this.WordAroundCaret() is { } word)
            range = word;

        this.Execute(new FormatSlideRunsCommand(range, apply, label));
    }

    /// <summary>The word the caret sits strictly inside, or null at a word's edge or in whitespace.</summary>
    SlideTextRange? WordAroundCaret()
    {
        var at = this.caret;
        if (this.ActiveText?.Paragraphs.ElementAtOrDefault(at.Paragraph)?.PlainText is not { } text
            || at.Offset <= 0
            || at.Offset >= text.Length)
            return null;

        // WordBoundaries also groups runs of punctuation and spaces; only letters and digits are a word.
        if (!char.IsLetterOrDigit(text[at.Offset - 1]) || !char.IsLetterOrDigit(text[at.Offset]))
            return null;

        var (start, end) = WordBoundaries.RangeAt(text, at.Offset);
        return new SlideTextRange(at with { Offset = start }, at with { Offset = end });
    }

    void FormatParagraphs(Action<D.ParagraphProperties> apply, string label)
    {
        if (!this.CanEditText())
            return;

        this.Execute(new FormatSlideParagraphsCommand(this.TextSelection.Normalized(), apply, label));
    }

    // ---- shapes ----

    /// <summary>Removes the selected shape.</summary>
    public void DeleteSelectedShape()
    {
        if (this.IsReadOnly || this.selected < 0 || this.Selection is null)
            return;

        this.Execute(new DeleteShapeCommand(this.Index, this.selected));
        this.ClearSelection();
    }

    /// <summary>Adds an empty text box at a point in slide coordinates, and selects it.</summary>
    public void AddTextBox(double slideX, double slideY, double width = 320, double height = 64)
    {
        if (this.IsReadOnly || this.deck.TreeAt(this.Index) is null)
            return;

        this.AddElement(SlideShapeFactory.TextBox(slideX, slideY, width, height));
    }

    /// <summary>Adds a preset-geometry shape at a point in slide coordinates, and selects it.</summary>
    public void AddShape(
        ShapeGeometry geometry,
        double slideX,
        double slideY,
        double width = 200,
        double height = 150,
        ArgbColor? fill = null,
        ArgbColor? outline = null)
    {
        this.AddElement(SlideShapeFactory.Preset(
            geometry, slideX, slideY, width, height,
            fill ?? new ArgbColor(255, 0x44, 0x72, 0xC4),
            outline));
    }

    /// <summary>
    /// Adds a picture at a point in slide coordinates, and selects it.
    /// </summary>
    /// <param name="data">The encoded image, in whatever format <paramref name="contentType"/> names.</param>
    /// <param name="contentType">The MIME type, e.g. <c>image/png</c>.</param>
    /// <remarks>
    /// The bytes go in as they arrived. Re-encoding would mean choosing a quality on the user's
    /// behalf, and a deck is where people put screenshots they intend to be legible.
    /// </remarks>
    public void AddPicture(
        byte[] data,
        string contentType,
        double slideX,
        double slideY,
        double? width = null,
        double? height = null,
        string name = "Picture")
    {
        ArgumentNullException.ThrowIfNull(data);

        if (this.IsReadOnly || data.Length == 0 || this.deck.TreeAt(this.Index) is null)
            return;

        if (this.deck.AddImagePart(this.Index, data, contentType) is not { } relationshipId)
            return;

        var (w, h) = ResolveSize(width, height);
        this.AddElement(SlideShapeFactory.Image(relationshipId, slideX, slideY, w, h, name));
    }

    /// <summary>Adds an empty table at a point in slide coordinates, and selects it.</summary>
    public void AddTable(int rows, int columns, double slideX, double slideY, double width = 480, double height = 200)
        => this.AddElement(SlideShapeFactory.Table(rows, columns, slideX, slideY, width, height));

    /// <summary>
    /// Puts a prepared element into the slide's tree and selects whatever came out of it.
    /// </summary>
    /// <remarks>
    /// The command clones what it is given, so the element handed in here is not the one that ends up
    /// in the tree and cannot be compared against. The clone is appended, which makes it the tree's
    /// last child — so that is what the new shape is found by. Searching the model list by name would
    /// find the wrong shape as soon as a deck had two of anything.
    /// </remarks>
    void AddElement(OpenXmlElement element)
    {
        if (this.IsReadOnly || this.deck.TreeAt(this.Index) is not { } tree)
            return;

        // The factory writes placeholder ids; every drawing on a slide needs its own, and two shapes
        // sharing one is what PowerPoint's repair prompt is made of.
        var nextId = SlideObjects.NextShapeId(tree);
        foreach (var properties in element.Descendants<DocumentFormat.OpenXml.Presentation.NonVisualDrawingProperties>())
            properties.Id = nextId++;

        this.Execute(new InsertShapeCommand(this.Index, -1, element));

        if (tree.LastChild is not { } added)
            return;

        // Identity against the tree, not the model list: the model also carries the layout's and
        // master's shapes, which are not in this tree at all.
        var index = this.Current?.Shapes.ToList().FindIndex(x => ReferenceEquals(x.Element, added)) ?? -1;
        if (index >= 0)
            this.Select(index);
    }

    /// <summary>Fills in whichever of width and height was not given, keeping a 4:3 default.</summary>
    static (double Width, double Height) ResolveSize(double? width, double? height) => (width, height) switch
    {
        ({ } w, { } h) => (Math.Max(1, w), Math.Max(1, h)),
        ({ } w, null) => (Math.Max(1, w), Math.Max(1, w * 0.75)),
        (null, { } h) => (Math.Max(1, h * 4 / 3), Math.Max(1, h)),
        _ => (320d, 240d)
    };

    // ---- slides ----

    public bool CanMoveSlideEarlier => !this.IsReadOnly && this.Count > 1 && this.Index > 0;

    public bool CanMoveSlideLater => !this.IsReadOnly && this.Index < this.Count - 1;

    public bool CanDeleteSlide => !this.IsReadOnly && this.Count > 0;

    /// <summary>
    /// Adds an empty slide after the one being edited and opens it — PowerPoint's New Slide.
    /// </summary>
    /// <remarks>
    /// It takes the current slide's layout, except that a title slide is followed by a content slide.
    /// The placeholders arrive empty and show their "Click to add …" prompts.
    /// </remarks>
    public void NewSlide()
    {
        if (this.IsReadOnly)
            return;

        var empty = this.Count == 0;
        this.Execute(new NewSlideCommand(empty ? 0 : this.Index + 1, empty ? null : this.Index));
    }

    /// <summary>Copies the slide being edited and opens the copy, which goes straight after it.</summary>
    public void DuplicateSlide()
    {
        if (this.IsReadOnly || this.Count == 0)
            return;

        this.Execute(new DuplicateSlideCommand(this.Index));
    }

    /// <summary>
    /// Deletes a slide — the one being edited unless told otherwise. Undoable.
    /// </summary>
    /// <remarks>
    /// Does not ask. Confirmation is the view's job, because only the view knows how to ask; the
    /// toolbars on both hosts confirm before they call this.
    /// </remarks>
    public void DeleteSlide(int? index = null)
    {
        if (!this.CanDeleteSlide)
            return;

        this.Execute(new DeleteSlideCommand(index ?? this.Index));
    }

    /// <summary>Moves a slide to <paramref name="to"/>, counted with the slide already taken out. Undoable.</summary>
    public void MoveSlide(int from, int to)
    {
        if (this.IsReadOnly)
            return;

        this.Execute(new MoveSlideCommand(from, to));
    }

    /// <summary>Swaps the slide being edited with the one before it.</summary>
    public void MoveSlideEarlier()
    {
        if (this.CanMoveSlideEarlier)
            this.MoveSlide(this.Index, this.Index - 1);
    }

    /// <summary>Swaps the slide being edited with the one after it.</summary>
    public void MoveSlideLater()
    {
        if (this.CanMoveSlideLater)
            this.MoveSlide(this.Index, this.Index + 1);
    }

    // ---- nudge, arrange ----

    /// <summary>Distance an arrow key moves the selected shape, in slide pixels.</summary>
    public double NudgeDistance { get; set; } = 8;

    /// <summary>Distance a fine nudge (Ctrl/Alt + arrow) moves it.</summary>
    public double FineNudgeDistance { get; set; } = 1;

    /// <summary>
    /// Moves the selected shape by whole nudges — what the arrow keys do while a shape (not its text) is
    /// selected. Returns false when nothing moved, so a host can let the key fall through.
    /// </summary>
    /// <remarks>
    /// A run of nudges on one shape is one undo step, like a drag. Without a selection the arrows keep
    /// moving between slides.
    /// </remarks>
    public bool Nudge(int dx, int dy, bool fine = false)
    {
        if (this.IsReadOnly || this.IsEditingText || this.Selection is not { Element: not null } shape || (dx == 0 && dy == 0))
            return false;

        var step = fine ? this.FineNudgeDistance : this.NudgeDistance;
        this.Execute(new SetShapeBoundsCommand(this.Index, this.selected, shape.X + dx * step, shape.Y + dy * step, shape.Width, shape.Height));
        return true;
    }

    public bool CanArrange => !this.IsReadOnly && this.Selection is { Element: not null };

    /// <summary>Moves the selected shape in the stacking order, keeping it selected.</summary>
    public void Arrange(ShapeZOrder order)
    {
        if (!this.CanArrange || this.Selection?.Element is not { } element)
            return;

        this.EndTextEditing();
        this.Execute(new ReorderShapeCommand(this.Index, this.selected, order));
        this.Reselect(element);
    }

    public void BringToFront() => this.Arrange(ShapeZOrder.BringToFront);

    public void BringForward() => this.Arrange(ShapeZOrder.BringForward);

    public void SendBackward() => this.Arrange(ShapeZOrder.SendBackward);

    public void SendToBack() => this.Arrange(ShapeZOrder.SendToBack);

    /// <summary>Selects a shape again after an edit moved it to another index.</summary>
    void Reselect(OpenXmlElement element)
    {
        var index = this.Current?.Shapes.ToList().FindIndex(x => ReferenceEquals(x.Element, element)) ?? -1;
        this.selected = -1;
        this.Select(index);
    }

    // ---- clipboard ----

    /// <summary>
    /// What Copy and Cut put aside and Paste puts back.
    /// </summary>
    /// <remarks>
    /// Per controller, not process-wide: on Blazor Server one process serves every user, and a static
    /// clipboard would paste one user's shapes into another's deck. Two editors that should share a
    /// clipboard share it by assigning one's to the other. The system clipboard is not involved — a
    /// shape is not text, and no other app could paste it.
    /// </remarks>
    public SlideClip? Clipboard { get; set; }

    /// <summary>A shape (not text) is selected, so Copy, Cut and Duplicate have something to act on.</summary>
    public bool CanCopyShape => !this.IsEditingText && this.Selection is { Element: not null };

    public bool CanPaste => !this.IsReadOnly && this.Clipboard is not null && this.Count > 0;

    /// <summary>Copies the selected shape. Returns false when there was nothing to copy.</summary>
    public bool CopyShape()
    {
        if (!this.CanCopyShape || SlideClip.Copy(this.deck, this.Index, this.selected) is not { } clip)
            return false;

        this.Clipboard = clip;
        this.pasteCount = 0;
        this.RaiseChanged();
        return true;
    }

    /// <summary>Copies the selected shape and removes it.</summary>
    public bool CutShape()
    {
        if (this.IsReadOnly || !this.CopyShape())
            return false;

        this.DeleteSelectedShape();
        return true;
    }

    /// <summary>
    /// Pastes the clipboard onto the slide being edited and selects what arrived.
    /// </summary>
    /// <remarks>
    /// Onto the slide it was copied from, each paste steps down and right of the last so the copies do
    /// not stack invisibly on the original; onto any other slide it lands where it was.
    /// </remarks>
    public void Paste()
    {
        if (!this.CanPaste || this.Clipboard is not { } clip)
            return;

        var sameSlide = clip.Source.TryGetTarget(out var source) && ReferenceEquals(source, this.deck) && clip.SourceSlide == this.Index;
        var offset = sameSlide ? ++this.pasteCount * 16d : 0;

        this.PasteCore(clip, offset);
    }

    /// <summary>Copies the selected shape straight onto the same slide, offset — Ctrl+D.</summary>
    public void DuplicateShape()
    {
        if (this.IsReadOnly || !this.CanCopyShape || SlideClip.Copy(this.deck, this.Index, this.selected) is not { } clip)
            return;

        this.PasteCore(clip, 16);
    }

    void PasteCore(SlideClip clip, double offset)
    {
        this.EndTextEditing();
        this.enteredGroup = null;
        this.Execute(new PasteShapesCommand(this.Index, clip, offset, offset));

        if (this.deck.TreeAt(this.Index)?.LastChild is { } added)
            this.Reselect(added);
    }

    // ---- layouts ----

    /// <summary>The layouts the current slide's master offers, marking the one it uses.</summary>
    public IReadOnlyList<SlideLayoutOption> Layouts
    {
        get
        {
            var part = this.deck.PartAt(this.Index);
            var current = part?.SlideLayoutPart;
            var master = current?.SlideMasterPart ?? this.deck.PresentationPart.SlideMasterParts.FirstOrDefault();
            if (master is null)
                return [];

            // In the master's own list order, which is the order PowerPoint's gallery shows them in.
            var ordered = master.SlideMaster?.SlideLayoutIdList?.Elements<DocumentFormat.OpenXml.Presentation.SlideLayoutId>()
                .Select(x => x.RelationshipId?.Value is { } id ? master.GetPartById(id) as DocumentFormat.OpenXml.Packaging.SlideLayoutPart : null)
                .OfType<DocumentFormat.OpenXml.Packaging.SlideLayoutPart>()
                .ToList();

            if (ordered is null || ordered.Count == 0)
                ordered = master.SlideLayoutParts.ToList();

            return ordered
                .Select((layout, i) => new SlideLayoutOption(
                    layout.SlideLayout?.CommonSlideData?.Name?.Value is { Length: > 0 } name ? name : $"Layout {i + 1}",
                    i,
                    ReferenceEquals(layout, current))
                { Part = layout })
                .ToList();
        }
    }

    /// <summary>Puts the current slide on another layout, carrying its content across. Undoable.</summary>
    public void SetLayout(SlideLayoutOption layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (this.IsReadOnly || this.Count == 0 || layout.Part is null || layout.IsCurrent)
            return;

        this.EndTextEditing();
        this.ClearSelection();
        this.Execute(new SetSlideLayoutCommand(this.Index, layout.Part));
    }

    /// <summary>Adds a slide with a chosen layout after the current one.</summary>
    public void NewSlide(SlideLayoutOption layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (this.IsReadOnly)
            return;

        var at = this.Count == 0 ? 0 : this.Index + 1;
        this.Execute(new NewSlideCommand(at) { Layout = layout.Part });
    }

    // ---- notes ----

    /// <summary>The current slide's speaker notes, or null.</summary>
    public string? Notes => this.Current?.Notes;

    /// <summary>
    /// Replaces the current slide's speaker notes. Undoable as one step.
    /// </summary>
    /// <remarks>
    /// Written whole, not per keystroke: a notes box commits when it loses focus, and every character
    /// being its own undo step would bury the slide edits around it.
    /// </remarks>
    public void SetNotes(string? text)
    {
        if (this.IsReadOnly || this.Count == 0)
            return;

        // Unchanged notes are not an edit: a notes box losing focus would otherwise leave an undo step
        // that does nothing.
        static string Normal(string? value) => (value ?? string.Empty).Replace("\r\n", "\n").TrimEnd();
        if (Normal(text) == Normal(this.Notes))
            return;

        this.Execute(new SetSlideNotesCommand(this.Index, text));
    }

    // ---- undo ----

    public void Undo() => this.deck.Undo.Undo();

    public void Redo() => this.deck.Undo.Redo();

    // ---- painting support ----

    /// <summary>The caret rectangle in viewport coordinates, or null when not editing text.</summary>
    public SlideRect? CaretRect()
    {
        if (!this.IsEditingText || this.TextTarget() is not { } target)
            return null;

        var bounds = target.Bounds;
        var layout = ShapeTextLayout.Layout(target.Body, target.Width, target.Height, this.measurer);
        if (ShapeTextLayout.CaretAt(layout, this.caret.Paragraph, this.caret.Offset, this.measurer) is not { } caret)
            return null;

        return new SlideRect(
            bounds.X + caret.X * this.Scale,
            bounds.Y + caret.Y * this.Scale,
            Math.Max(1, this.Scale),
            caret.Height * this.Scale);
    }

    /// <summary>Highlight rectangles for the text selection, in viewport coordinates.</summary>
    public IEnumerable<SlideRect> TextSelectionRects()
    {
        if (!this.IsEditingText || this.TextTarget() is not { } target)
            yield break;

        var bounds = target.Bounds;
        var range = this.TextSelection.Normalized();
        if (range.IsEmpty)
            yield break;

        var layout = ShapeTextLayout.Layout(target.Body, target.Width, target.Height, this.measurer);

        for (var i = range.Start.Paragraph; i <= range.End.Paragraph; i++)
        {
            if (layout.Paragraphs.ElementAtOrDefault(i) is not { } block)
                continue;

            var from = i == range.Start.Paragraph ? range.Start.Offset : 0;
            var to = i == range.End.Paragraph ? range.End.Offset : block.Paragraph.PlainText.Length;

            foreach (var rect in ShapeTextLayout.SelectionRects(layout, i, from, to, this.measurer))
            {
                yield return new SlideRect(
                    bounds.X + rect.X * this.Scale,
                    bounds.Y + rect.Y * this.Scale,
                    rect.Width * this.Scale,
                    rect.Height * this.Scale);
            }
        }
    }

    /// <summary>
    /// Highlight rectangles for every find match on the slide being shown, in viewport coordinates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The current slide only, and only in single-slide mode. A match three slides away has no
    /// rectangle on screen to draw, and the thumbnail grid draws slides at a scale where a wash over
    /// two characters is a smudge — the readout is what says how many there are elsewhere.
    /// </para>
    /// <para>
    /// The match the text selection is sitting on is left out, so it is drawn in the selection's
    /// colour alone rather than in both. Stacking the two washes made the current hit a muddy blend
    /// and the hardest one on the slide to pick out. The test is what the selection actually covers
    /// rather than which match is active, so clicking away from a hit brings its wash back instead of
    /// leaving a gap in the highlights.
    /// </para>
    /// </remarks>
    public IEnumerable<SlideRect> FindMatchRects()
    {
        if (!this.Find.IsSearching || this.Mode != SlideViewMode.Single || this.Current is not { } slide)
            yield break;

        var current = this.Index;
        var selection = this.IsEditingText ? this.TextSelection.Normalized() : default;

        // The matches arrive in slide order, so one shape's layout can be computed once and reused for
        // every hit inside it rather than re-laid-out per match.
        var laidOutShape = -1;
        LaidOutTextBody? layout = null;
        SlideRect bounds = default;

        foreach (var match in this.Find.Matches)
        {
            if (match.Slide < current)
                continue;

            if (match.Slide > current)
                yield break;

            if (!selection.IsEmpty && selection == match.Range)
                continue;

            if (match.Shape != laidOutShape)
            {
                layout = null;

                if (slide.Shapes.ElementAtOrDefault(match.Shape) is { Text: { } body } shape
                    && this.BoundsOf(shape) is { } shapeBounds)
                {
                    layout = ShapeTextLayout.Layout(body, shape.Width, shape.Height, this.measurer);
                    bounds = shapeBounds;
                }

                laidOutShape = match.Shape;
            }

            if (layout is null)
                continue;

            foreach (var rect in ShapeTextLayout.SelectionRects(layout, match.Paragraph, match.Start, match.End, this.measurer))
            {
                yield return new SlideRect(
                    bounds.X + rect.X * this.Scale,
                    bounds.Y + rect.Y * this.Scale,
                    rect.Width * this.Scale,
                    rect.Height * this.Scale);
            }
        }
    }

    /// <summary>
    /// Opens the slide a find match is on, selects its shape and selects the matched text.
    /// </summary>
    /// <remarks>
    /// Not routed through <see cref="Select"/>, which returns early when the shape is already selected
    /// and clears <see cref="IsEditingText"/> when it is not — either of which would leave a hit found
    /// but not shown.
    /// </remarks>
    internal void SelectFindMatch(SlideFindMatch match)
    {
        // A hit found while looking at the thumbnail grid has no caret to move: the slide it is on has
        // to be opened before anything can be selected on it.
        this.Mode = SlideViewMode.Single;
        this.Index = match.Slide;

        if (this.Current?.Shapes.ElementAtOrDefault(match.Shape) is not { IsEditable: true, Text: not null })
            return;

        this.selected = match.Shape;
        this.IsEditingText = true;
        this.activeCell = null;
        this.enteredGroup = null;
        this.anchor = match.Position;
        this.caret = match.Position with { Offset = match.End };

        this.RefreshCaretFormat();
        this.RaiseChanged();
    }

    // ---- plumbing ----

    bool CanEditText()
        => !this.IsReadOnly && this.IsEditingText && this.ActiveText is not null;

    int LengthOf(int paragraph)
        => this.ActiveText?.Paragraphs.ElementAtOrDefault(paragraph)?.PlainText.Length ?? 0;

    void Execute(IEditCommand<SlideDeck> command) => this.deck.Execute(command);

    /// <summary>
    /// Reads the formatting under the caret so a toolbar can show what is active.
    /// </summary>
    /// <remarks>
    /// The character <em>before</em> the caret is what PowerPoint reports, so typing continues the run
    /// the caret just left rather than the one it is about to enter.
    /// </remarks>
    void RefreshCaretFormat()
    {
        if (this.ActiveText?.Paragraphs.ElementAtOrDefault(this.caret.Paragraph) is not { } paragraph)
        {
            this.CaretFormat = SlideCaretFormat.Default;
            return;
        }

        var target = Math.Max(0, this.caret.Offset - 1);
        var cursor = 0;
        StyledRun? found = null;

        foreach (var run in paragraph.Runs)
        {
            if (run.IsBreak)
                continue;

            found ??= run;

            if (target < cursor + run.Text.Length)
            {
                found = run;
                break;
            }

            cursor += run.Text.Length;
            found = run;
        }

        var style = found?.Style ?? TextStyle.Default;

        this.CaretFormat = new SlideCaretFormat(
            style.Bold,
            style.Italic,
            style.Underline != UnderlineStyle.None,
            style.Strike,

            // The model carries pixels; a toolbar shows points, and SetFontSize takes points. Reporting
            // pixels here makes the size box read 24 for text the user set to 18.
            OoxmlUnits.PixelsToPointsApprox(style.FontSize),
            style.FontFamily,
            style.Color,
            paragraph.Alignment,
            style.Highlight)
        {
            List = paragraph.List,
            Level = paragraph.Level
        };
    }
}
