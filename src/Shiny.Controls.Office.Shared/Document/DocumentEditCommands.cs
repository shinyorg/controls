using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using TextAlignment = Shiny.Controls.Office.Text.TextAlignment;

namespace Shiny.Controls.Office.Document;

/// <summary>
/// Base for document edits.
/// </summary>
/// <remarks>
/// Every command captures whatever it needs to reverse itself while it runs, exactly as the
/// spreadsheet's do — most edits cannot describe their inverse until they see what was there.
/// </remarks>
public abstract record DocumentCommand : IEditCommand<WordDocument>
{
    public abstract string Name { get; }

    public abstract IEditCommand<WordDocument> Apply(WordDocument context);
}

/// <summary>Inserts text at a position.</summary>
public sealed record InsertTextCommand(DocumentPosition At, string Text) : DocumentCommand
{
    public override string Name => "Typing";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var paragraph = context.ParagraphElementAt(this.At.Block);
        if (paragraph is null)
            return new NoOpCommand();

        WordParagraphEditor.Insert(paragraph, this.At.Offset, this.Text);
        context.Reproject(this.At.Block);

        return new DeleteRangeCommand(new DocumentRange(
            this.At,
            this.At with { Offset = this.At.Offset + this.Text.Length }));
    }
}

/// <summary>Deletes a range, which may span paragraphs.</summary>
public sealed record DeleteRangeCommand(DocumentRange Range) : DocumentCommand
{
    public override string Name => "Delete";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var range = this.Range;
        if (range.IsEmpty)
            return new NoOpCommand();

        // The whole affected span is captured before anything moves, because after the edit the
        // paragraphs it came from may no longer exist.
        var restore = context.CaptureRange(range);

        if (range.IsWithinOneBlock)
        {
            var paragraph = context.ParagraphElementAt(range.Start.Block);
            if (paragraph is null)
                return new NoOpCommand();

            WordParagraphEditor.Delete(paragraph, range.Start.Offset, range.End.Offset);
            context.Reproject(range.Start.Block);
            return restore;
        }

        var first = context.ParagraphElementAt(range.Start.Block);
        var last = context.ParagraphElementAt(range.End.Block);
        if (first is null || last is null)
            return new NoOpCommand();

        var firstTop = context.TopOf(range.Start.Block);
        var lastTop = context.TopOf(range.End.Block);

        if (ReferenceEquals(first.Parent, last.Parent))
        {
            // Trim both ends, drop everything between - paragraphs, tables, anything - then join the
            // survivors. The same whether the two ends are body paragraphs or share a table cell.
            WordParagraphEditor.Delete(first, range.Start.Offset, WordParagraphEditor.LengthOf(first));
            WordParagraphEditor.Delete(last, 0, range.End.Offset);

            while (first.NextSibling() is { } between && !ReferenceEquals(between, last))
                between.Remove();

            WordParagraphEditor.Merge(first, last);

            if (context.Blocks[firstTop] is DocumentParagraph)
            {
                // Body level: every top-level block after the first, up to and including the last, is
                // gone from the XML; the projection has to lose them too.
                for (var top = lastTop; top > firstTop; top--)
                    context.RemoveProjection(top);
            }

            context.ReprojectTop(firstTop);

            // The span the snapshot came from was several blocks; the edit collapsed it to one. Undo
            // has to replace what is there *now*, not what was captured.
            return restore with { RemovedCount = 1 };
        }

        // The two ends are in different containers - body text on one side of a table and a cell on
        // the other, or two different cells. Nothing can be joined across that boundary, so the text
        // in range is removed and the structure is left standing, which is what Word does too.
        WordParagraphEditor.Delete(first, range.Start.Offset, WordParagraphEditor.LengthOf(first));
        WordParagraphEditor.Delete(last, 0, range.End.Offset);

        for (var paragraph = range.Start.Block + 1; paragraph < range.End.Block; paragraph++)
        {
            var top = context.TopOf(paragraph);
            if (top > firstTop && top < lastTop)
                continue;

            if (context.ParagraphElementAt(paragraph) is { } inner)
                WordParagraphEditor.Delete(inner, 0, WordParagraphEditor.LengthOf(inner));
        }

        // Whole top-level blocks strictly between the two ends go entirely.
        for (var top = lastTop - 1; top > firstTop; top--)
            context.RemoveTopBlock(top);

        context.ReprojectTop(firstTop);
        if (lastTop != firstTop)
            context.ReprojectTop(firstTop + 1);

        return restore with { RemovedCount = lastTop == firstTop ? 1 : 2 };
    }
}

/// <summary>Splits a paragraph in two — what Enter does.</summary>
public sealed record SplitParagraphCommand(DocumentPosition At) : DocumentCommand
{
    public override string Name => "New Paragraph";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var paragraph = context.ParagraphElementAt(this.At.Block);
        if (paragraph is null)
            return new NoOpCommand();

        var tail = WordParagraphEditor.Split(paragraph, this.At.Offset);
        context.ParagraphInserted(this.At.Block, tail);
        context.Reproject(this.At.Block);

        return new MergeParagraphCommand(this.At.Block);
    }
}

/// <summary>Joins a paragraph with the one after it — what Backspace at offset zero does.</summary>
public sealed record MergeParagraphCommand(int Block) : DocumentCommand
{
    public override string Name => "Merge Paragraphs";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var first = context.ParagraphElementAt(this.Block);
        var second = context.ParagraphElementAt(this.Block + 1);
        if (first is null || second is null || !ReferenceEquals(first.Parent, second.Parent))
            return new NoOpCommand();

        var joinAt = new DocumentPosition(this.Block, WordParagraphEditor.LengthOf(first));

        WordParagraphEditor.Merge(first, second);
        context.ParagraphRemoved(this.Block + 1);
        context.Reproject(this.Block);

        return new SplitParagraphCommand(joinAt);
    }
}

/// <summary>Applies run-level formatting to a range.</summary>
public sealed record FormatRunsCommand(DocumentRange Range, RunFormatChange Change) : DocumentCommand
{
    public override string Name => this.Change.Name;

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        if (this.Range.IsEmpty)
            return new NoOpCommand();

        // Formatting is reversed by restoring the affected paragraphs wholesale. Inverting a property
        // change per run would need the prior value of every run the range touches, and runs get split
        // and merged by the change itself.
        var restore = context.CaptureRange(this.Range);

        for (var block = this.Range.Start.Block; block <= this.Range.End.Block; block++)
        {
            var paragraph = context.ParagraphElementAt(block);
            if (paragraph is null)
                continue;

            var length = WordParagraphEditor.LengthOf(paragraph);
            var from = block == this.Range.Start.Block ? this.Range.Start.Offset : 0;
            var to = block == this.Range.End.Block ? this.Range.End.Offset : length;

            WordParagraphEditor.Format(paragraph, from, Math.Min(to, length), this.Change.Apply);
            context.Reproject(block);
        }

        return restore;
    }
}

/// <summary>A named run-property mutation, so a command can describe itself in the undo menu.</summary>
/// <summary>Which run attribute a <see cref="RunFormatChange"/> sets.</summary>
/// <remarks>
/// Needed so a change chosen at a bare caret can replace an earlier one of the same attribute rather
/// than queueing behind it: picking 12pt and then 14pt has to leave 14pt, not both in the order they
/// were clicked.
/// </remarks>
public enum RunFormatKind
{
    Other,
    Bold,
    Italic,
    Underline,
    Strike,
    FontFamily,
    FontSize,
    Color,
    Highlight
}

public sealed record RunFormatChange(string Name, Action<RunProperties> Apply)
{
    /// <summary>Which attribute this sets. See <see cref="RunFormatKind"/>.</summary>
    public RunFormatKind Kind { get; init; } = RunFormatKind.Other;

    /// <summary>
    /// The same change expressed against <see cref="CaretFormat"/> — how it looks to a toolbar.
    /// </summary>
    /// <remarks>
    /// A format chosen with nothing selected has not reached the document yet, so there is nothing in
    /// the document to read it back from. The toolbar needs the change described in its own terms or
    /// it would go on showing the format of whatever the caret happens to be sitting in.
    /// </remarks>
    public Func<CaretFormat, CaretFormat>? PreviewCaret { get; init; }

    public static RunFormatChange Bold(bool on) => new(on ? "Bold" : "Remove Bold", WordParagraphEditor.ToggleBold(on))
        { Kind = RunFormatKind.Bold, PreviewCaret = f => f with { Bold = on } };

    public static RunFormatChange Italic(bool on) => new(on ? "Italic" : "Remove Italic", WordParagraphEditor.ToggleItalic(on))
        { Kind = RunFormatKind.Italic, PreviewCaret = f => f with { Italic = on } };

    public static RunFormatChange Underline(bool on) => new(on ? "Underline" : "Remove Underline", WordParagraphEditor.ToggleUnderline(on))
        { Kind = RunFormatKind.Underline, PreviewCaret = f => f with { Underline = on } };

    public static RunFormatChange Strike(bool on) => new(on ? "Strikethrough" : "Remove Strikethrough", WordParagraphEditor.ToggleStrike(on))
        { Kind = RunFormatKind.Strike, PreviewCaret = f => f with { Strike = on } };

    public static RunFormatChange FontFamily(string family) => new("Font", WordParagraphEditor.SetFontFamily(family))
        { Kind = RunFormatKind.FontFamily, PreviewCaret = f => f with { FontFamily = family } };

    public static RunFormatChange FontSize(double points) => new("Font Size", WordParagraphEditor.SetFontSize(points))
        { Kind = RunFormatKind.FontSize, PreviewCaret = f => f with { FontSize = points } };

    public static RunFormatChange Color(ArgbColor color) => new("Text Colour", WordParagraphEditor.SetColor(color))
        { Kind = RunFormatKind.Color, PreviewCaret = f => f with { Color = color } };

    public static RunFormatChange Highlight(ArgbColor? color) => new(color is null ? "Remove Highlight" : "Highlight", WordParagraphEditor.SetHighlight(color))
        { Kind = RunFormatKind.Highlight, PreviewCaret = f => f with { Highlight = color } };
}

/// <summary>Applies paragraph-level formatting to every paragraph a range touches.</summary>
public sealed record FormatParagraphsCommand(DocumentRange Range, ParagraphFormatChange Change) : DocumentCommand
{
    public override string Name => this.Change.Name;

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var restore = context.CaptureRange(this.Range with { End = this.Range.End });

        for (var block = this.Range.Start.Block; block <= this.Range.End.Block; block++)
        {
            var paragraph = context.ParagraphElementAt(block);
            if (paragraph is null)
                continue;

            WordParagraphEditor.FormatParagraph(paragraph, this.Change.Apply);
            context.Reproject(block);
        }

        return restore;
    }
}

public sealed record ParagraphFormatChange(string Name, Action<ParagraphProperties> Apply)
{
    public static ParagraphFormatChange Alignment(TextAlignment alignment)
        => new("Alignment", WordParagraphEditor.SetAlignment(alignment));

    public static ParagraphFormatChange Style(string? styleId)
        => new("Paragraph Style", WordParagraphEditor.SetStyle(styleId));
}

/// <summary>
/// Puts every paragraph a range touches into a bulleted or numbered list, or takes them out of one.
/// </summary>
/// <remarks>
/// <para>
/// This is not a <see cref="FormatParagraphsCommand"/> with a different mutation, because the
/// mutation cannot be described until the document has been asked for a <c>numId</c> — and asking
/// may create the definitions, and with them the numbering part itself. A paragraph pointed at a
/// definition that does not exist carries a list reference and renders as ordinary text.
/// </para>
/// <para>
/// Each paragraph keeps the level it already had, so switching a nested list from bullets to numbers
/// preserves its shape instead of flattening it.
/// </para>
/// </remarks>
public sealed record SetListCommand(DocumentRange Range, ListStyle Style) : DocumentCommand
{
    public override string Name => this.Style switch
    {
        ListStyle.Bullet => "Bulleted List",
        ListStyle.Numbered => "Numbered List",
        _ => "Remove List"
    };

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var numId = this.Style == ListStyle.None ? 0 : context.EnsureListNumbering(this.Style);

        // A document with no main part cannot gain a numbering part either. Better to do nothing than
        // to stamp a numId of zero onto the paragraphs, which reads as "explicitly not a list".
        if (this.Style != ListStyle.None && numId == 0)
            return new NoOpCommand();

        var restore = context.CaptureRange(this.Range);

        for (var block = this.Range.Start.Block; block <= this.Range.End.Block; block++)
        {
            if (context.ParagraphElementAt(block) is not { } paragraph)
                continue;

            var level = WordParagraphEditor.ListLevelOf(paragraph);

            WordParagraphEditor.FormatParagraph(
                paragraph,
                this.Style == ListStyle.None
                    ? WordParagraphEditor.ClearList()
                    : WordParagraphEditor.SetList(numId, level));

            context.Reproject(block);
        }

        return restore;
    }
}

/// <summary>
/// Moves the list items a range touches in or out one level — what Tab and Shift+Tab do.
/// </summary>
/// <remarks>
/// Paragraphs that are not in a list are skipped rather than indented, so a Tab that catches a
/// heading along with three list items nests the items and leaves the heading where it was.
/// </remarks>
public sealed record ShiftListLevelCommand(DocumentRange Range, int Delta) : DocumentCommand
{
    public override string Name => this.Delta > 0 ? "Indent List" : "Outdent List";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        if (this.Delta == 0)
            return new NoOpCommand();

        var restore = context.CaptureRange(this.Range);
        var changed = false;

        for (var block = this.Range.Start.Block; block <= this.Range.End.Block; block++)
        {
            if (context.ParagraphElementAt(block) is not { } paragraph)
                continue;

            if (!WordParagraphEditor.IsListItem(paragraph))
                continue;

            WordParagraphEditor.FormatParagraph(paragraph, WordParagraphEditor.ShiftListLevel(this.Delta));
            context.Reproject(block);
            changed = true;
        }

        return changed ? restore : new NoOpCommand();
    }
}

/// <summary>
/// Inserts a tab at a position.
/// </summary>
/// <remarks>
/// Its own command rather than an <see cref="InsertInlineObjectCommand"/> because a tab is four
/// characters wide in the offset space and an inline object is one — an inverse that deleted a single
/// character would leave three spaces behind on undo.
/// </remarks>
public sealed record InsertTabCommand(DocumentPosition At) : DocumentCommand
{
    /// <summary>How much of the offset space a tab occupies. Must match what the reader projects.</summary>
    public const int Width = 4;

    public override string Name => "Tab";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var paragraph = context.ParagraphElementAt(this.At.Block);
        if (paragraph is null)
            return new NoOpCommand();

        WordParagraphEditor.InsertObject(paragraph, this.At.Offset, new Run(new TabChar()));
        context.Reproject(this.At.Block);

        return new DeleteRangeCommand(new DocumentRange(
            this.At,
            this.At with { Offset = this.At.Offset + Width }));
    }
}

/// <summary>
/// Restores a span of paragraphs to a previously captured state.
/// </summary>
/// <remarks>
/// The general-purpose inverse. Cloned XML is kept rather than a description of the change, because
/// most edits split, merge or delete runs and there is no property-level undo that survives that.
/// </remarks>
public sealed record RestoreBlocksCommand(int Start, int RemovedCount, IReadOnlyList<OpenXmlElement> Snapshot) : DocumentCommand
{
    public override string Name => "Undo";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var inverse = context.CaptureBlocks(this.Start, this.RemovedCount);
        context.ReplaceBlocks(this.Start, this.RemovedCount, this.Snapshot);

        // Redo must remove however many blocks this restore just put in.
        return inverse with { RemovedCount = this.Snapshot.Count };
    }
}

/// <summary>
/// Inserts an inline object — a picture or a shape — at the caret.
/// </summary>
/// <remarks>
/// The run is built before the command is constructed, so anything that can fail (decoding an image,
/// adding a part to the package) has already failed by the time the undo stack is involved. A command
/// that could fail halfway would leave a redo that no longer works.
/// </remarks>
public sealed record InsertInlineObjectCommand(DocumentPosition At, Run Element) : DocumentCommand
{
    public override string Name => "Insert Object";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var paragraph = context.ParagraphElementAt(this.At.Block);
        if (paragraph is null)
            return new NoOpCommand();

        WordParagraphEditor.InsertObject(paragraph, this.At.Offset, this.Element);
        context.Reproject(this.At.Block);

        // One character wide, like every inline object.
        return new DeleteRangeCommand(new DocumentRange(
            this.At,
            this.At with { Offset = this.At.Offset + 1 }));
    }
}

/// <summary>Resizes the inline object at a position.</summary>
/// <remarks>
/// Mergeable, so a drag that reports fifty pointer moves collapses into one undo step rather than
/// fifty. The merge keeps this command's starting size as the thing to undo to, which is what makes
/// a single Ctrl+Z put the object back to where the drag began.
/// </remarks>
public sealed record ResizeInlineObjectCommand(DocumentPosition At, double Width, double Height)
    : DocumentCommand, IMergeableCommand<WordDocument>
{
    public override string Name => "Resize";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var paragraph = context.ParagraphElementAt(this.At.Block);
        if (paragraph is null)
            return new NoOpCommand();

        // The size before the change is the undo, and it has to be read before the write.
        var before = context.Paragraphs.ElementAtOrDefault(this.At.Block) is { } projected
            ? SizeAt(projected, this.At.Offset)
            : null;

        if (!WordParagraphEditor.ResizeObject(paragraph, this.At.Offset, this.Width, this.Height))
            return new NoOpCommand();

        context.Reproject(this.At.Block);

        return before is { } size
            ? new ResizeInlineObjectCommand(this.At, size.Width, size.Height)
            : new NoOpCommand();
    }

    public bool TryMerge(IEditCommand<WordDocument> next, out IEditCommand<WordDocument> merged)
    {
        merged = this;

        if (next is not ResizeInlineObjectCommand other || other.At != this.At)
            return false;

        // The later size wins; the earlier undo is the one that matters.
        merged = other;
        return true;
    }

    /// <summary>The size of the inline object at an offset, from the projection.</summary>
    static (double Width, double Height)? SizeAt(DocumentParagraph paragraph, int offset)
    {
        var cursor = 0;

        foreach (var run in paragraph.Runs)
        {
            if (run.IsBreak)
                continue;

            var length = run.Length;

            if (run.Inline is { } inline && offset >= cursor && offset < cursor + length)
                return (inline.Width, inline.Height);

            cursor += length;
        }

        return null;
    }
}

/// <summary>Inserts a table as a new block after top-level block <paramref name="Block"/>.</summary>
/// <remarks>
/// <paramref name="Block"/> indexes <see cref="WordDocument.Blocks"/>, not the story: a table goes
/// between two body-level blocks, never inside a paragraph.
/// </remarks>
/// <remarks>
/// After rather than at, and followed by an empty paragraph, because a table needs a paragraph on the
/// far side of it to be reachable: with nothing after it there is no caret position below the table
/// and no way to type past the end of the document.
/// </remarks>
public sealed record InsertTableCommand(int Block, int Rows, int Columns) : DocumentCommand
{
    public override string Name => "Insert Table";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        if (context.BlockElementAt(this.Block) is not { } anchor)
            return new NoOpCommand();

        var table = WordContentFactory.Table(this.Rows, this.Columns);
        var trailing = WordContentFactory.EmptyParagraph();

        anchor.InsertAfterSelf(table);
        table.InsertAfterSelf(trailing);

        context.InsertBlockAfter(this.Block, table);
        context.InsertBlockAfter(this.Block + 1, trailing);

        return new RemoveBlocksCommand(this.Block + 1, 2);
    }
}

/// <summary>Removes a span of blocks, restoring them on undo.</summary>
public sealed record RemoveBlocksCommand(int Start, int Count) : DocumentCommand
{
    public override string Name => "Delete";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var restore = context.CaptureBlocks(this.Start, this.Count);

        for (var i = 0; i < this.Count; i++)
            context.RemoveBlock(this.Start);

        return restore;
    }
}

/// <summary>Does nothing, and undoes to nothing. Returned when a command finds no work to do.</summary>
public sealed record NoOpCommand : DocumentCommand
{
    public override string Name => "No Change";

    public override IEditCommand<WordDocument> Apply(WordDocument context) => this;
}
