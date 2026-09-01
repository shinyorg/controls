using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Notebook;

/// <summary>Formatting under the caret, so a toolbar can show what is active.</summary>
public readonly record struct NoteCaretFormat(
    string FontFamily,
    double FontSize,
    bool Bold,
    bool Italic,
    bool Underline,
    bool Strike,
    ArgbColor Color,
    ArgbColor? Highlight,
    TextAlignment Alignment,
    ListStyle List,
    int Level)
{
    public static readonly NoteCaretFormat Default = new(
        "Calibri", 12, false, false, false, false, new ArgbColor(255, 0, 0, 0), null, TextAlignment.Left, ListStyle.None, 0);
}

public sealed partial class NotebookEditorController
{
    /// <summary>True while a caret is inside an item's text rather than on the item itself.</summary>
    public bool IsEditingText => this.editingItemId is not null;

    public NoteItem? EditingItem => this.ItemById(this.editingItemId);

    public NotePosition Caret => this.caret;

    public NoteTextRange TextSelection => new(this.anchor, this.caret);

    public NoteCaretFormat CaretFormat { get; private set; } = NoteCaretFormat.Default;

    internal void BeginTextEditing(NoteItem item, double pageX, double pageY)
    {
        if (this.IsReadOnly || item.Text is null)
            return;

        this.editingItemId = item.Id;
        this.MoveCaretTo(item, pageX, pageY, extend: false);
    }

    /// <summary>Puts the caret in an item's text without a pointer — what the Enter key on a selection does.</summary>
    public void BeginTextEditing(string itemId, bool selectAll = false)
    {
        if (this.IsReadOnly || this.ItemById(itemId) is not { Text: not null } item)
            return;

        this.editingItemId = item.Id;

        if (selectAll)
        {
            this.SelectAllText();
            return;
        }

        var last = Math.Max(0, item.Text.Paragraphs.Count - 1);
        this.caret = this.anchor = new NotePosition(last, NoteTextEditor.LengthOf(item.Text.Paragraphs[last]));
        this.RefreshCaretFormat();
        this.RaiseChanged();
    }

    public void EndTextEditing()
    {
        if (this.editingItemId is null)
            return;

        var wasEditing = this.editingItemId;
        this.editingItemId = null;
        this.Document.Undo.BreakCoalescing();

        // A container the user typed nothing into is litter — it has no outline of its own, so it
        // would sit on the page as an invisible click-trap.
        if (this.ItemById(wasEditing) is { Kind: NoteItemKind.Text } item &&
            item.PlainText.Length == 0 &&
            this.Page is { } page)
        {
            this.Document.Execute(new DeleteItemCommand(page.Id, item.Id, "Add text"));
            this.selection.Remove(item.Id);
        }

        this.RaiseChanged();
    }

    void MoveCaretTo(NoteItem item, double pageX, double pageY, bool extend)
    {
        if (this.LayoutOf(item) is not { } layout)
            return;

        var (paragraph, offset) = ShapeTextLayout.PositionAt(
            layout, pageX - item.X, pageY - item.Y, this.Measurer);

        this.SetCaret(new NotePosition(paragraph, offset), extend);
    }

    void SetCaret(NotePosition position, bool extend)
    {
        if (this.EditingItem?.Text is { } body)
            position = NoteTextEditor.Clamp(body, position);

        this.caret = position;
        if (!extend)
            this.anchor = position;

        this.RefreshCaretFormat();
        this.RaiseChanged();
    }

    void RefreshCaretFormat()
    {
        if (this.EditingItem?.Text is not { } body || body.Paragraphs.ElementAtOrDefault(this.caret.Paragraph) is not { } paragraph)
        {
            this.CaretFormat = NoteCaretFormat.Default;
            return;
        }

        var style = NoteTextEditor.StyleAt(paragraph, this.caret.Offset);

        this.CaretFormat = new NoteCaretFormat(
            style.FontFamily,
            style.FontSize,
            style.Bold,
            style.Italic,
            style.Underline != UnderlineStyle.None,
            style.Strike,
            style.Color,
            style.Highlight,
            paragraph.Alignment,
            paragraph.List,
            paragraph.Level);
    }

    // ---- caret geometry, for the painter ----

    /// <summary>The caret rectangle in viewport coordinates, or null when nothing is being edited.</summary>
    public NoteRect? CaretRect()
    {
        if (this.EditingItem is not { } item || this.LayoutOf(item) is not { } layout)
            return null;

        if (ShapeTextLayout.CaretAt(layout, this.caret.Paragraph, this.caret.Offset, this.Measurer) is not { } caret)
            return null;

        return this.ToViewport(new NoteRect(item.X + caret.X, item.Y + caret.Y, 1.4 / this.Zoom, caret.Height));
    }

    /// <summary>The highlight rectangles behind a text selection, in viewport coordinates.</summary>
    public IEnumerable<NoteRect> TextSelectionRects()
    {
        if (this.EditingItem is not { } item || item.Text is not { } body || this.LayoutOf(item) is not { } layout)
            yield break;

        var (start, end) = this.TextSelection.Ordered;
        if (start == end)
            yield break;

        for (var i = start.Paragraph; i <= end.Paragraph && i < body.Paragraphs.Count; i++)
        {
            var from = i == start.Paragraph ? start.Offset : 0;
            var to = i == end.Paragraph ? end.Offset : NoteTextEditor.LengthOf(body.Paragraphs[i]);

            foreach (var (x, y, width, height) in ShapeTextLayout.SelectionRects(layout, i, from, to, this.Measurer))
                yield return this.ToViewport(new NoteRect(item.X + x, item.Y + y, width, height));
        }
    }

    // ---- typing ----

    /// <summary>
    /// Writes a new text body onto the item being edited, growing an auto-height container to fit.
    /// </summary>
    /// <remarks>
    /// The height is measured from the item that is about to be committed, not from the one on the
    /// page — the whole point is to know how tall the <i>new</i> text is before it is stored, and the
    /// layout cache keys on the instance, so the measurement is not thrown away when the command lands.
    /// </remarks>
    void CommitText(ShapeTextBody body, string label, bool coalesce)
    {
        if (this.Page is not { } page || this.EditingItem is not { } item)
            return;

        var updated = item with { Text = body };

        if (updated.AutoHeight)
            updated = updated with { Height = this.MeasuredHeight(updated) };

        this.Document.Execute(new ReplaceItemCommand(page.Id, updated, label, coalesce));
        this.RefreshCaretFormat();
    }

    public void InsertText(string text)
    {
        if (this.IsReadOnly || string.IsNullOrEmpty(text) || this.EditingItem?.Text is not { } body)
            return;

        if (!this.TextSelection.IsEmpty)
        {
            var (start, _) = this.TextSelection.Ordered;
            body = NoteTextEditor.DeleteRange(body, this.TextSelection);
            this.caret = this.anchor = start;
        }

        // The style comes from the caret rather than from the run the text lands in, so "bold on, then
        // type" works at a run boundary where the run after the caret is not bold.
        var style = this.CaretFormat.FontSize > 0 ? this.StyleFromCaret() : (TextStyle?)null;
        var next = NoteTextEditor.InsertText(body, this.caret, text, style);

        var caretAfter = this.caret with { Offset = this.caret.Offset + text.Length };

        // Autoformat: typing "- " or "1. " at the start of a paragraph turns it into a list, the same
        // way the document and slide editors do it.
        if (this.IsAutoFormatListEnabled && text == " " && this.caret.Offset > 0)
        {
            var prefix = NoteTextEditor.TextOf(next.Paragraphs[this.caret.Paragraph])[..this.caret.Offset];
            var detected = ListAutoFormat.Detect(prefix);

            if (detected != ListStyle.None)
            {
                // The space goes with the marker. Both were typed to ask for a list rather than to be
                // read, so leaving either behind shows the request as well as the result.
                var cleared = NoteTextEditor.WithParagraph(
                    next,
                    this.caret.Paragraph,
                    NoteTextEditor.Delete(next.Paragraphs[this.caret.Paragraph], 0, this.caret.Offset + text.Length)
                        with { List = detected });

                next = Renumber(cleared);
                caretAfter = new NotePosition(this.caret.Paragraph, 0);
            }
        }

        this.CommitText(next, "Typing", coalesce: true);
        this.SetCaret(caretAfter, extend: false);
    }

    TextStyle StyleFromCaret()
        => new()
        {
            FontFamily = this.CaretFormat.FontFamily,
            FontSize = this.CaretFormat.FontSize,
            Bold = this.CaretFormat.Bold,
            Italic = this.CaretFormat.Italic,
            Underline = this.CaretFormat.Underline ? UnderlineStyle.Single : UnderlineStyle.None,
            Strike = this.CaretFormat.Strike,
            Color = this.CaretFormat.Color,
            Highlight = this.CaretFormat.Highlight,
            SizeScale = 1
        };

    public bool IsAutoFormatListEnabled { get; set; } = true;

    public void InsertParagraph()
    {
        if (this.IsReadOnly || this.EditingItem?.Text is not { } body)
            return;

        if (!this.TextSelection.IsEmpty)
        {
            var (start, _) = this.TextSelection.Ordered;
            body = NoteTextEditor.DeleteRange(body, this.TextSelection);
            this.caret = this.anchor = start;
        }

        var split = NoteTextEditor.SplitParagraph(body, this.caret);

        this.CommitText(Renumber(split), "New line", coalesce: false);
        this.SetCaret(new NotePosition(this.caret.Paragraph + 1, 0), extend: false);
    }

    public void Backspace()
    {
        if (this.IsReadOnly || this.EditingItem?.Text is not { } body)
            return;

        if (!this.TextSelection.IsEmpty)
        {
            this.DeleteTextSelection();
            return;
        }

        if (this.caret.Offset > 0)
        {
            var paragraph = body.Paragraphs[this.caret.Paragraph];
            var next = NoteTextEditor.WithParagraph(
                body, this.caret.Paragraph, NoteTextEditor.Delete(paragraph, this.caret.Offset - 1, this.caret.Offset));

            this.CommitText(next, "Delete", coalesce: true);
            this.SetCaret(this.caret with { Offset = this.caret.Offset - 1 }, extend: false);
            return;
        }

        // At the very start of a list item, backspace leaves the list rather than joining the line
        // above — which is what every editor does, and the only way out of a list with the keyboard.
        if (body.Paragraphs[this.caret.Paragraph].List != ListStyle.None)
        {
            this.SetListStyle(ListStyle.None);
            return;
        }

        if (this.caret.Paragraph == 0)
            return;

        var previous = this.caret.Paragraph - 1;
        var joinAt = NoteTextEditor.LengthOf(body.Paragraphs[previous]);

        this.CommitText(Renumber(NoteTextEditor.MergeParagraphs(body, previous)), "Delete", coalesce: false);
        this.SetCaret(new NotePosition(previous, joinAt), extend: false);
    }

    public void Delete()
    {
        if (this.IsReadOnly || this.EditingItem?.Text is not { } body)
            return;

        if (!this.TextSelection.IsEmpty)
        {
            this.DeleteTextSelection();
            return;
        }

        var paragraph = body.Paragraphs[this.caret.Paragraph];

        if (this.caret.Offset < NoteTextEditor.LengthOf(paragraph))
        {
            var next = NoteTextEditor.WithParagraph(
                body, this.caret.Paragraph, NoteTextEditor.Delete(paragraph, this.caret.Offset, this.caret.Offset + 1));

            this.CommitText(next, "Delete", coalesce: true);
            return;
        }

        if (this.caret.Paragraph + 1 < body.Paragraphs.Count)
            this.CommitText(Renumber(NoteTextEditor.MergeParagraphs(body, this.caret.Paragraph)), "Delete", coalesce: false);
    }

    public void DeleteTextSelection()
    {
        if (this.IsReadOnly || this.TextSelection.IsEmpty || this.EditingItem?.Text is not { } body)
            return;

        var (start, _) = this.TextSelection.Ordered;

        this.CommitText(Renumber(NoteTextEditor.DeleteRange(body, this.TextSelection)), "Delete", coalesce: false);
        this.SetCaret(start, extend: false);
    }

    // ---- caret movement ----

    public void MoveLeft(bool extend = false)
    {
        if (this.EditingItem?.Text is not { } body)
            return;

        if (this.caret.Offset > 0)
            this.SetCaret(this.caret with { Offset = this.caret.Offset - 1 }, extend);
        else if (this.caret.Paragraph > 0)
            this.SetCaret(new NotePosition(this.caret.Paragraph - 1, NoteTextEditor.LengthOf(body.Paragraphs[this.caret.Paragraph - 1])), extend);
    }

    public void MoveRight(bool extend = false)
    {
        if (this.EditingItem?.Text is not { } body)
            return;

        var length = NoteTextEditor.LengthOf(body.Paragraphs[this.caret.Paragraph]);

        if (this.caret.Offset < length)
            this.SetCaret(this.caret with { Offset = this.caret.Offset + 1 }, extend);
        else if (this.caret.Paragraph + 1 < body.Paragraphs.Count)
            this.SetCaret(new NotePosition(this.caret.Paragraph + 1, 0), extend);
    }

    public void MoveUp(bool extend = false) => this.MoveVertically(-1, extend);

    public void MoveDown(bool extend = false) => this.MoveVertically(1, extend);

    /// <summary>
    /// Moves a line up or down, keeping the horizontal position.
    /// </summary>
    /// <remarks>
    /// Done geometrically — take the caret's x, step a line height, and ask the layout what offset is
    /// there — rather than by counting characters. A wrapped paragraph is several lines with one offset
    /// range, so character arithmetic moves by a paragraph where the user asked for a line.
    /// </remarks>
    void MoveVertically(int direction, bool extend)
    {
        if (this.EditingItem is not { } item || this.LayoutOf(item) is not { } layout)
            return;

        if (ShapeTextLayout.CaretAt(layout, this.caret.Paragraph, this.caret.Offset, this.Measurer) is not { } caret)
            return;

        var y = caret.Y + (direction < 0 ? -caret.Height * 0.5 : caret.Height * 1.5);
        var (paragraph, offset) = ShapeTextLayout.PositionAt(layout, caret.X, y, this.Measurer);

        this.SetCaret(new NotePosition(paragraph, offset), extend);
    }

    public void MoveToLineStart(bool extend = false) => this.SetCaret(this.caret with { Offset = 0 }, extend);

    public void MoveToLineEnd(bool extend = false)
    {
        if (this.EditingItem?.Text is not { } body)
            return;

        this.SetCaret(this.caret with { Offset = NoteTextEditor.LengthOf(body.Paragraphs[this.caret.Paragraph]) }, extend);
    }

    public void SelectAllText()
    {
        if (this.EditingItem?.Text is not { } body || body.Paragraphs.Count == 0)
            return;

        this.anchor = new NotePosition(0, 0);
        this.caret = new NotePosition(body.Paragraphs.Count - 1, NoteTextEditor.LengthOf(body.Paragraphs[^1]));
        this.RefreshCaretFormat();
        this.RaiseChanged();
    }

    public void SelectWordAt(NotePosition position)
    {
        if (this.EditingItem?.Text is not { } body || body.Paragraphs.ElementAtOrDefault(position.Paragraph) is not { } paragraph)
            return;

        var text = NoteTextEditor.TextOf(paragraph);
        var (start, end) = WordBoundaries.RangeAt(text, position.Offset);

        this.anchor = new NotePosition(position.Paragraph, start);
        this.caret = new NotePosition(position.Paragraph, end);
        this.RefreshCaretFormat();
        this.RaiseChanged();
    }

    // ---- run formatting ----

    /// <summary>
    /// Applies a run format to the selection, or to the whole item when the caret is on it.
    /// </summary>
    /// <remarks>
    /// The second half is what makes the toolbar work outside text mode: a selected shape with a label
    /// should go bold from one click, without the user first double-clicking into it and selecting the
    /// words.
    /// </remarks>
    void FormatText(Func<TextStyle, TextStyle> apply, string label)
    {
        if (this.IsReadOnly || this.Page is not { } page)
            return;

        if (this.IsEditingText && this.EditingItem?.Text is { } body)
        {
            var range = this.TextSelection.IsEmpty
                ? new NoteTextRange(new NotePosition(0, 0), new NotePosition(body.Paragraphs.Count - 1, NoteTextEditor.LengthOf(body.Paragraphs[^1])))
                : this.TextSelection;

            // With an empty selection the format lands on the whole paragraph run so the change is
            // visible; a caret-only format that only affects the next keystroke has no toolbar state
            // to show and reads as a dead button.
            this.CommitText(NoteTextEditor.FormatRange(body, range, apply), label, coalesce: false);
            return;
        }

        using (this.Document.Undo.BeginTransaction(label))
        {
            foreach (var item in this.SelectedItems().ToList())
            {
                if (item.Text is not { } text || text.Paragraphs.Count == 0)
                    continue;

                var all = new NoteTextRange(
                    new NotePosition(0, 0),
                    new NotePosition(text.Paragraphs.Count - 1, NoteTextEditor.LengthOf(text.Paragraphs[^1])));

                var updated = item with { Text = NoteTextEditor.FormatRange(text, all, apply) };
                if (updated.AutoHeight)
                    updated = updated with { Height = this.MeasuredHeight(updated) };

                this.Document.Execute(new ReplaceItemCommand(page.Id, updated, label));
            }
        }

        this.RefreshCaretFormat();
    }

    public void ToggleBold()
    {
        var on = !this.CaretFormat.Bold;
        this.FormatText(s => s with { Bold = on }, "Bold");
    }

    public void ToggleItalic()
    {
        var on = !this.CaretFormat.Italic;
        this.FormatText(s => s with { Italic = on }, "Italic");
    }

    public void ToggleUnderline()
    {
        var style = this.CaretFormat.Underline ? UnderlineStyle.None : UnderlineStyle.Single;
        this.FormatText(s => s with { Underline = style }, "Underline");
    }

    public void ToggleStrikethrough()
    {
        var on = !this.CaretFormat.Strike;
        this.FormatText(s => s with { Strike = on }, "Strikethrough");
    }

    public void SetFontSize(double points) => this.FormatText(s => s with { FontSize = points }, "Font size");

    public void SetFontFamily(string family) => this.FormatText(s => s with { FontFamily = family }, "Font");

    public void SetTextColor(ArgbColor color) => this.FormatText(s => s with { Color = color }, "Text colour");

    public void SetHighlight(ArgbColor? color) => this.FormatText(s => s with { Highlight = color }, "Highlight");

    public void ToggleHighlight(ArgbColor color)
        => this.SetHighlight(this.CaretFormat.Highlight == color ? null : color);

    // ---- paragraph formatting ----

    void FormatParagraphs(Func<ShapeParagraph, ShapeParagraph> apply, string label)
    {
        if (this.IsReadOnly || this.Page is not { } page)
            return;

        if (this.IsEditingText && this.EditingItem?.Text is { } body)
        {
            this.CommitText(Renumber(NoteTextEditor.FormatParagraphs(body, this.TextSelection, apply)), label, coalesce: false);
            return;
        }

        using (this.Document.Undo.BeginTransaction(label))
        {
            foreach (var item in this.SelectedItems().ToList())
            {
                if (item.Text is not { } text || text.Paragraphs.Count == 0)
                    continue;

                var all = new NoteTextRange(new NotePosition(0, 0), new NotePosition(text.Paragraphs.Count - 1, 0));
                var updated = item with { Text = Renumber(NoteTextEditor.FormatParagraphs(text, all, apply)) };

                if (updated.AutoHeight)
                    updated = updated with { Height = this.MeasuredHeight(updated) };

                this.Document.Execute(new ReplaceItemCommand(page.Id, updated, label));
            }
        }

        this.RefreshCaretFormat();
    }

    public void SetAlignment(TextAlignment alignment)
        => this.FormatParagraphs(p => p with { Alignment = alignment }, "Alignment");

    public void SetListStyle(ListStyle style)
        => this.FormatParagraphs(p => p with { List = style, Bullet = null }, style == ListStyle.None ? "Remove list" : "List");

    public void ToggleBulletList()
        => this.SetListStyle(this.CaretFormat.List == ListStyle.Bullet ? ListStyle.None : ListStyle.Bullet);

    public void ToggleNumberedList()
        => this.SetListStyle(this.CaretFormat.List == ListStyle.Numbered ? ListStyle.None : ListStyle.Numbered);

    public void ShiftLevel(int delta)
        => this.FormatParagraphs(p => p with { Level = Math.Clamp(p.Level + delta, 0, 8) }, delta > 0 ? "Indent" : "Outdent");

    /// <summary>Tab indents inside a list and moves between containers outside one.</summary>
    public bool HandleTab(bool shift = false)
    {
        if (!this.IsEditingText)
            return false;

        this.ShiftLevel(shift ? -1 : 1);
        return true;
    }

    /// <summary>
    /// Recomputes the marker on every list paragraph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A numbered item's marker is a function of its place in the sequence, so it cannot be stored on
    /// the paragraph and left alone — inserting a line in the middle of a list renumbers everything
    /// after it. The bullet glyph could be stored, but resolving both here means one rule for what a
    /// marker looks like rather than two.
    /// </para>
    /// <para>
    /// A run is broken by any paragraph that is not a numbered item at that level, which is what makes
    /// two lists separated by a sentence start again at one instead of continuing.
    /// </para>
    /// </remarks>
    public static ShapeTextBody Renumber(ShapeTextBody body)
    {
        var counters = new int[9];
        var paragraphs = new List<ShapeParagraph>(body.Paragraphs.Count);
        var previousLevel = -1;
        var previousList = ListStyle.None;

        foreach (var paragraph in body.Paragraphs)
        {
            var level = Math.Clamp(paragraph.Level, 0, 8);

            switch (paragraph.List)
            {
                case ListStyle.Numbered:
                {
                    // Stepping out to a shallower level and back in starts the deeper list again, the
                    // way an outline reads: 1, 1.1, 1.2, 2, 2.1 — not 2.3.
                    if (previousList != ListStyle.Numbered || level > previousLevel)
                    {
                        for (var i = level; i < counters.Length; i++)
                            counters[i] = 0;
                    }

                    counters[level]++;
                    paragraphs.Add(paragraph with { Bullet = $"{Marker(level, counters[level])}." });
                    break;
                }

                case ListStyle.Bullet:
                    paragraphs.Add(paragraph with { Bullet = BulletGlyph(level) });
                    break;

                default:
                    Array.Clear(counters);
                    paragraphs.Add(paragraph.Bullet is null ? paragraph : paragraph with { Bullet = null });
                    break;
            }

            previousLevel = level;
            previousList = paragraph.List;
        }

        return body with { Paragraphs = paragraphs };
    }

    /// <summary>Numbers, then letters, then roman — the outline convention, repeating past level five.</summary>
    static string Marker(int level, int value) => (level % 3) switch
    {
        1 => Letter(value),
        2 => Roman(value),
        _ => value.ToString()
    };

    static string Letter(int value)
    {
        var text = string.Empty;
        var n = Math.Max(1, value);

        while (n > 0)
        {
            n--;
            text = (char)('a' + n % 26) + text;
            n /= 26;
        }

        return text;
    }

    static string Roman(int value)
    {
        int[] numbers = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] glyphs = ["m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i"];

        var text = string.Empty;
        var n = Math.Max(1, value);

        for (var i = 0; i < numbers.Length && n > 0; i++)
        {
            while (n >= numbers[i])
            {
                text += glyphs[i];
                n -= numbers[i];
            }
        }

        return text;
    }

    static string BulletGlyph(int level) => (level % 3) switch
    {
        1 => "◦",
        2 => "▪",
        _ => "•"
    };
}
