using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Notebook;

public sealed partial class NotebookEditorController
{
    // ---- inserting ----

    /// <summary>Adds a text container and puts the caret in it.</summary>
    public NoteItem? AddTextBox(double pageX, double pageY, double width = 320, string? text = null)
    {
        if (this.IsReadOnly || this.Page is not { } page)
            return null;

        var item = NotebookDocument.NewTextItem(pageX, pageY, width, text, this.NewTextStyle);
        this.Document.Execute(new InsertItemCommand(page.Id, item, Label: "Add text"));
        this.Select(item.Id);
        this.BeginTextEditing(item.Id, selectAll: text is not null);

        return item;
    }

    public NoteItem? AddShape(ShapeGeometry geometry, double pageX, double pageY, double width = 180, double height = 110)
    {
        if (this.IsReadOnly || this.Page is not { } page)
            return null;

        var item = NotebookDocument.NewShapeItem(geometry, pageX, pageY, width, height, this.NewShapeFill, this.NewShapeOutline);
        this.Document.Execute(new InsertItemCommand(page.Id, item, Label: "Add shape"));
        this.Select(item.Id);

        return item;
    }

    /// <summary>
    /// Places a picture, scaled down to fit the page's width when it is larger than that.
    /// </summary>
    /// <remarks>
    /// A phone photo is four thousand pixels across. Dropped at its natural size it lands as a wall the
    /// user has to zoom out to find the corner of, so it is fitted on the way in — and only ever
    /// shrunk, because blowing a small image up to fill the page is not what dropping it meant either.
    /// </remarks>
    public NoteItem? AddImage(byte[] bytes, string contentType, double pageX, double pageY, double pixelWidth, double pixelHeight)
    {
        if (this.IsReadOnly || this.Page is not { } page)
            return null;

        var width = Math.Max(1, pixelWidth);
        var height = Math.Max(1, pixelHeight);
        var limit = Math.Max(120, page.MinWidth * 0.6);

        if (width > limit)
        {
            height *= limit / width;
            width = limit;
        }

        var item = NotebookDocument.NewImageItem(bytes, contentType, pageX, pageY, width, height);
        this.Document.Execute(new InsertItemCommand(page.Id, item, Label: "Add picture"));
        this.Select(item.Id);

        return item;
    }

    // ---- selection operations ----

    public void DeleteSelection()
    {
        if (this.IsReadOnly || this.Page is not { } page || this.selection.Count == 0)
            return;

        var ids = this.selection.ToList();
        this.selection.Clear();
        this.editingItemId = null;

        using (this.Document.Undo.BeginTransaction(ids.Count == 1 ? "Delete" : "Delete items"))
        {
            foreach (var id in ids)
                this.Document.Execute(new DeleteItemCommand(page.Id, id));
        }

        this.RaiseChanged();
    }

    /// <summary>Copies the selection a little down and to the right, and selects the copies.</summary>
    public void DuplicateSelection(double offsetX = 16, double offsetY = 16)
    {
        if (this.IsReadOnly || this.Page is not { } page || this.selection.Count == 0)
            return;

        var copies = new List<NoteItem>();

        using (this.Document.Undo.BeginTransaction("Duplicate"))
        {
            foreach (var item in this.SelectedItems().ToList())
            {
                // A new id, because the id is what the selection, the layout cache and the media entry
                // are all keyed by — a duplicate that kept it would be the same item twice.
                var copy = (item with { Id = NotebookDocument.NewId() }).Translate(offsetX, offsetY);
                copies.Add(copy);
                this.Document.Execute(new InsertItemCommand(page.Id, copy, Label: "Duplicate"));
            }
        }

        this.selection.Clear();
        foreach (var copy in copies)
            this.selection.Add(copy.Id);

        this.RaiseChanged();
    }

    public void BringToFront() => this.Reorder(toFront: true, allTheWay: true);

    public void SendToBack() => this.Reorder(toFront: false, allTheWay: true);

    public void BringForward() => this.Reorder(toFront: true, allTheWay: false);

    public void SendBackward() => this.Reorder(toFront: false, allTheWay: false);

    void Reorder(bool toFront, bool allTheWay)
    {
        if (this.IsReadOnly || this.Page is not { } page || this.selection.Count == 0)
            return;

        // Front-most first when moving forward, so the relative order of the moved items survives.
        var ordered = this.SelectedItems().Select(x => x.Id).ToList();
        if (toFront)
            ordered.Reverse();

        using (this.Document.Undo.BeginTransaction(toFront ? "Bring forward" : "Send backward"))
        {
            foreach (var id in ordered)
            {
                var index = page.IndexOf(id);
                if (index < 0)
                    continue;

                var target = allTheWay
                    ? (toFront ? page.Items.Count - 1 : 0)
                    : index + (toFront ? 1 : -1);

                this.Document.Execute(new ReorderItemCommand(page.Id, id, target, toFront ? "Bring forward" : "Send backward"));
            }
        }
    }

    /// <summary>Nudges the selection, which is what the arrow keys do outside text mode.</summary>
    public void NudgeSelection(double dx, double dy)
    {
        if (this.IsReadOnly || this.Page is not { } page || this.selection.Count == 0)
            return;

        using (this.Document.Undo.BeginTransaction("Move"))
        {
            foreach (var item in this.SelectedItems().ToList())
            {
                var (x, y, _, _) = item.Bounds();
                this.Document.Execute(new ReplaceItemCommand(
                    page.Id,
                    item.Translate(Math.Max(-x, dx), Math.Max(-y, dy)),
                    "Move"));
            }
        }
    }

    public void SetSelectionFill(ArgbColor? color)
        => this.UpdateSelection(item => item.Kind == NoteItemKind.Ink
            ? item
            : item with { Fill = color is { } c ? new ShapeFill { Solid = c } : ShapeFill.None }, "Fill");

    public void SetSelectionOutline(ArgbColor? color, double width = 1.5)
        => this.UpdateSelection(item => item.Kind == NoteItemKind.Ink
            ? item
            : item with { Outline = color is { } c ? new ShapeOutline(c, width) : null }, "Outline");

    /// <summary>Recolours ink, which is the only thing fill and outline do not reach.</summary>
    public void SetSelectionInkColor(ArgbColor color)
        => this.UpdateSelection(item => item.Stroke is { } ink ? item with { Stroke = ink with { Color = color } } : item, "Ink colour");

    public void SetSelectionGeometry(ShapeGeometry geometry)
        => this.UpdateSelection(item => item.Kind == NoteItemKind.Shape ? item with { Geometry = geometry } : item, "Shape");

    public void SetSelectionLocked(bool locked)
        => this.UpdateSelection(item => item with { Locked = locked }, locked ? "Lock" : "Unlock");

    void UpdateSelection(Func<NoteItem, NoteItem> apply, string label)
    {
        if (this.IsReadOnly || this.Page is not { } page || this.selection.Count == 0)
            return;

        using (this.Document.Undo.BeginTransaction(label))
        {
            foreach (var item in this.SelectedItems().ToList())
            {
                var updated = apply(item);
                if (!ReferenceEquals(updated, item))
                    this.Document.Execute(new ReplaceItemCommand(page.Id, updated, label));
            }
        }

        this.RaiseChanged();
    }

    // ---- page and section ----

    public void SetPageRule(PageRule rule, double? spacing = null, ArgbColor? color = null)
    {
        if (this.IsReadOnly || this.Page is not { } page)
            return;

        this.Document.Execute(new SetPageSettingsCommand(
            page.Id, rule, spacing ?? page.RuleSpacing, color ?? page.RuleColor, page.Background));
    }

    public void SetPageBackground(ArgbColor? color)
    {
        if (this.IsReadOnly || this.Page is not { } page)
            return;

        this.Document.Execute(new SetPageSettingsCommand(page.Id, page.Rule, page.RuleSpacing, page.RuleColor, color));
    }

    public void RenamePage(string title)
    {
        if (this.IsReadOnly || this.Page is not { } page)
            return;

        this.Document.Execute(new RenamePageCommand(page.Id, title));
    }

    /// <summary>Adds a page after the current one and navigates to it.</summary>
    public NotebookPage? AddPage(string title = "Untitled page")
    {
        if (this.IsReadOnly || this.Section is not { } section)
            return null;

        var page = new NotebookPage(NotebookDocument.NewId(), title);
        this.Document.Execute(new AddPageCommand(section.Id, page, this.Address.Page + 1));
        this.GoToPage(page.Id);

        return page;
    }

    public void DeletePage()
    {
        if (this.IsReadOnly || this.Page is not { } page)
            return;

        this.Document.Execute(new DeletePageCommand(page.Id));
    }

    public void MovePage(string pageId, string targetSectionId, int index)
    {
        if (!this.IsReadOnly)
            this.Document.Execute(new MovePageCommand(pageId, targetSectionId, index));
    }

    public NotebookSection? AddSection(string title = "New section")
    {
        if (this.IsReadOnly)
            return null;

        var section = NotebookDocument.NewSection(title);
        this.Document.Execute(new AddSectionCommand(section));
        this.Address = new PageAddress(this.Document.Sections.Count - 1, 0);

        return section;
    }

    public void DeleteSection()
    {
        if (this.IsReadOnly || this.Section is not { } section)
            return;

        this.Document.Execute(new DeleteSectionCommand(section.Id));
    }

    public void RenameSection(string title, ArgbColor? color = null)
    {
        if (this.IsReadOnly || this.Section is not { } section)
            return;

        this.Document.Execute(new RenameSectionCommand(section.Id, title, color ?? section.Color));
    }

    // ---- history ----

    public void Undo()
    {
        this.Document.Undo.Undo();
        this.PruneSelection();
    }

    public void Redo()
    {
        this.Document.Undo.Redo();
        this.PruneSelection();
    }

    /// <summary>
    /// Drops selected ids that no longer exist.
    /// </summary>
    /// <remarks>
    /// Undoing an insert removes the item that is selected, and undoing a delete brings one back that
    /// is not. Left alone the selection frame goes on being drawn around nothing, and the next
    /// keystroke edits an item that is not on the page.
    /// </remarks>
    void PruneSelection()
    {
        if (this.Page is not { } page)
        {
            this.selection.Clear();
            this.editingItemId = null;
            this.RaiseChanged();
            return;
        }

        this.selection.RemoveAll(id => page.IndexOf(id) < 0);

        if (this.editingItemId is { } editing && page.IndexOf(editing) < 0)
            this.editingItemId = null;

        if (this.EditingItem?.Text is { } body)
            this.caret = this.anchor = NoteTextEditor.Clamp(body, this.caret);

        this.RefreshCaretFormat();
        this.RaiseChanged();
    }

    // ---- keyboard ----

    /// <summary>
    /// The one keyboard entry point, because MAUI has no portable key-down event.
    /// </summary>
    /// <remarks>
    /// A desktop host wires its own platform key hook and calls in; Blazor calls it straight from
    /// <c>@onkeydown</c>. Keys are named the way the browser names them — <c>ArrowLeft</c>,
    /// <c>Backspace</c>, <c>Escape</c> — so the web host passes them through untranslated and the MAUI
    /// hosts translate once, into the names that already have a written-down spelling.
    /// </remarks>
    public bool HandleKey(string key, bool shift = false, bool control = false)
    {
        if (control)
        {
            switch (key.ToLowerInvariant())
            {
                case "z":
                    if (shift)
                        this.Redo();
                    else
                        this.Undo();
                    return true;

                case "y":
                    this.Redo();
                    return true;

                case "a":
                    if (this.IsEditingText)
                        this.SelectAllText();
                    else
                        this.SelectAll();
                    return true;

                case "b":
                    this.ToggleBold();
                    return true;

                case "i":
                    this.ToggleItalic();
                    return true;

                case "u":
                    this.ToggleUnderline();
                    return true;

                case "d":
                    this.DuplicateSelection();
                    return true;
            }

            return false;
        }

        switch (key)
        {
            case "ArrowLeft":
                if (this.IsEditingText)
                    this.MoveLeft(shift);
                else
                    this.NudgeSelection(shift ? -10 : -1, 0);
                return true;

            case "ArrowRight":
                if (this.IsEditingText)
                    this.MoveRight(shift);
                else
                    this.NudgeSelection(shift ? 10 : 1, 0);
                return true;

            case "ArrowUp":
                if (this.IsEditingText)
                    this.MoveUp(shift);
                else
                    this.NudgeSelection(0, shift ? -10 : -1);
                return true;

            case "ArrowDown":
                if (this.IsEditingText)
                    this.MoveDown(shift);
                else
                    this.NudgeSelection(0, shift ? 10 : 1);
                return true;

            case "Home":
                if (this.IsEditingText)
                    this.MoveToLineStart(shift);
                return this.IsEditingText;

            case "End":
                if (this.IsEditingText)
                    this.MoveToLineEnd(shift);
                return this.IsEditingText;

            case "Backspace":
                if (this.IsEditingText)
                    this.Backspace();
                else
                    this.DeleteSelection();
                return true;

            case "Delete":
                if (this.IsEditingText)
                    this.Delete();
                else
                    this.DeleteSelection();
                return true;

            case "Enter":
                if (this.IsEditingText)
                {
                    this.InsertParagraph();
                    return true;
                }

                // Enter on a selected item opens it for typing, which is how a shape gets a label
                // without reaching for the mouse.
                if (this.SingleSelection is { Text: not null } single)
                {
                    this.BeginTextEditing(single.Id, selectAll: true);
                    return true;
                }

                return false;

            case "Tab":
                return this.HandleTab(shift);

            case "Escape":
                if (this.IsEditingText)
                    this.EndTextEditing();
                else if (this.Tool != NoteTool.Select)
                    this.Tool = NoteTool.Select;
                else
                    this.ClearSelection();
                return true;
        }

        return false;
    }
}
