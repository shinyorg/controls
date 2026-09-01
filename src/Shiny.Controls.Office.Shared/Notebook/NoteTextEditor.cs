using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Notebook;

/// <summary>A caret position inside one item's text.</summary>
public readonly record struct NotePosition(int Paragraph, int Offset) : IComparable<NotePosition>
{
    public int CompareTo(NotePosition other)
    {
        var byParagraph = this.Paragraph.CompareTo(other.Paragraph);
        return byParagraph != 0 ? byParagraph : this.Offset.CompareTo(other.Offset);
    }

    public static bool operator <(NotePosition a, NotePosition b) => a.CompareTo(b) < 0;
    public static bool operator >(NotePosition a, NotePosition b) => a.CompareTo(b) > 0;
    public static bool operator <=(NotePosition a, NotePosition b) => a.CompareTo(b) <= 0;
    public static bool operator >=(NotePosition a, NotePosition b) => a.CompareTo(b) >= 0;
}

/// <summary>
/// A text selection, stored as the anchor and the caret rather than as start and end.
/// </summary>
/// <remarks>
/// Which end moves matters: dragging left from the middle of a word has to extend leftwards from where
/// the drag began, and a normalised pair loses that. <see cref="Ordered"/> gives the sorted form for
/// everything that only cares about the span.
/// </remarks>
public readonly record struct NoteTextRange(NotePosition Anchor, NotePosition Caret)
{
    public bool IsEmpty => this.Anchor == this.Caret;

    public (NotePosition Start, NotePosition End) Ordered
        => this.Anchor <= this.Caret ? (this.Anchor, this.Caret) : (this.Caret, this.Anchor);
}

/// <summary>
/// Editing over a <see cref="ShapeTextBody"/> as plain immutable records.
/// </summary>
/// <remarks>
/// <para>
/// The slide side does the same job against live DrawingML elements, because a deck's runs carry
/// language, spelling state and formatting its model does not represent, and rebuilding one to change
/// a character would throw all of that away. A notebook has no such hidden layer — its model <i>is</i>
/// the file — so the whole thing is written as pure functions returning new records instead.
/// </para>
/// <para>
/// That is what makes an undo step a copy of the item as it was: nothing here mutates, so no edit can
/// leak into the record the inverse is holding on to.
/// </para>
/// </remarks>
public static class NoteTextEditor
{
    public static int LengthOf(ShapeParagraph paragraph)
    {
        var length = 0;
        foreach (var run in paragraph.Runs)
            length += run.Text.Length;

        return length;
    }

    public static string TextOf(ShapeParagraph paragraph) => string.Concat(paragraph.Runs.Select(x => x.Text));

    /// <summary>The style that typing at an offset should take.</summary>
    /// <remarks>
    /// The run <i>before</i> the caret, not the one after it: typing at a bold/plain boundary continues
    /// what was just typed, which is what every editor does and what makes "turn bold on, keep typing"
    /// work at the end of a run.
    /// </remarks>
    public static TextStyle StyleAt(ShapeParagraph paragraph, int offset)
    {
        var cursor = 0;
        StyleFallback? last = null;

        foreach (var run in paragraph.Runs)
        {
            var end = cursor + run.Text.Length;

            if (run.Text.Length > 0)
            {
                last = new StyleFallback(run.Style);

                if (offset > cursor && offset <= end)
                    return run.Style;
            }

            cursor = end;
        }

        return last?.Style ?? paragraph.Runs.FirstOrDefault()?.Style ?? NotebookDocument.DefaultTextStyle;
    }

    readonly record struct StyleFallback(TextStyle Style);

    public static ShapeParagraph Insert(ShapeParagraph paragraph, int offset, string text, TextStyle? style = null)
    {
        if (string.IsNullOrEmpty(text))
            return paragraph;

        var length = LengthOf(paragraph);
        offset = Math.Clamp(offset, 0, length);

        var effective = style ?? StyleAt(paragraph, offset);
        var runs = new List<StyledRun>(paragraph.Runs.Count + 2);

        var cursor = 0;
        var placed = false;

        foreach (var run in paragraph.Runs)
        {
            var end = cursor + run.Text.Length;

            // Strictly inside this run: split it and drop the new text into the seam. The boundary
            // cases are left to the append below so the text lands after everything at that offset
            // rather than between two runs that both end there.
            if (!placed && offset > cursor && offset < end)
            {
                var local = offset - cursor;
                runs.Add(run with { Text = run.Text[..local] });
                runs.Add(new StyledRun(text, effective));
                runs.Add(run with { Text = run.Text[local..] });
                placed = true;
            }
            else
            {
                if (!placed && offset == cursor && offset == 0)
                {
                    runs.Add(new StyledRun(text, effective));
                    placed = true;
                }

                runs.Add(run);

                if (!placed && offset == end)
                {
                    runs.Add(new StyledRun(text, effective));
                    placed = true;
                }
            }

            cursor = end;
        }

        if (!placed)
            runs.Add(new StyledRun(text, effective));

        return paragraph with { Runs = Normalize(runs) };
    }

    public static ShapeParagraph Delete(ShapeParagraph paragraph, int start, int end)
    {
        var length = LengthOf(paragraph);
        start = Math.Clamp(start, 0, length);
        end = Math.Clamp(end, 0, length);

        if (end <= start)
            return paragraph;

        var runs = new List<StyledRun>(paragraph.Runs.Count);
        var cursor = 0;

        foreach (var run in paragraph.Runs)
        {
            var runEnd = cursor + run.Text.Length;

            // A zero-length run (a break) is carried through untouched; it occupies no offset, so no
            // range can be said to cover it.
            if (run.Text.Length == 0)
            {
                runs.Add(run);
                cursor = runEnd;
                continue;
            }

            var keepStart = Math.Max(0, Math.Min(run.Text.Length, start - cursor));
            var keepEnd = Math.Max(0, Math.Min(run.Text.Length, end - cursor));

            var kept = run.Text[..keepStart] + run.Text[keepEnd..];
            if (kept.Length > 0)
                runs.Add(run with { Text = kept });

            cursor = runEnd;
        }

        return paragraph with { Runs = Normalize(runs) };
    }

    /// <summary>Splits at an offset, which is what pressing Enter in the middle of a line does.</summary>
    public static (ShapeParagraph Left, ShapeParagraph Right) Split(ShapeParagraph paragraph, int offset)
    {
        var length = LengthOf(paragraph);
        offset = Math.Clamp(offset, 0, length);

        var left = Delete(paragraph, offset, length);
        var right = Delete(paragraph, 0, offset);

        // The new paragraph inherits the list state and indent of the one it came from, so pressing
        // Enter inside a bulleted list produces another bullet rather than dropping out of the list.
        return (left, right);
    }

    /// <summary>Joins two paragraphs, which is what Backspace at the start of a line does.</summary>
    /// <remarks>
    /// The target's paragraph properties win. Backspace at the start of line two pulls it up into line
    /// one, and the result is line one — so it keeps line one's alignment, indent and bullet.
    /// </remarks>
    public static ShapeParagraph Merge(ShapeParagraph target, ShapeParagraph source)
        => target with { Runs = Normalize([.. target.Runs, .. source.Runs]) };

    public static ShapeParagraph Format(ShapeParagraph paragraph, int start, int end, Func<TextStyle, TextStyle> apply)
    {
        var length = LengthOf(paragraph);
        start = Math.Clamp(start, 0, length);
        end = Math.Clamp(end, 0, length);

        if (end <= start)
            return paragraph;

        var runs = new List<StyledRun>(paragraph.Runs.Count + 2);
        var cursor = 0;

        foreach (var run in paragraph.Runs)
        {
            var runEnd = cursor + run.Text.Length;

            if (run.Text.Length == 0 || runEnd <= start || cursor >= end)
            {
                runs.Add(run);
                cursor = runEnd;
                continue;
            }

            var from = Math.Max(0, start - cursor);
            var to = Math.Min(run.Text.Length, end - cursor);

            if (from > 0)
                runs.Add(run with { Text = run.Text[..from] });

            runs.Add(new StyledRun(run.Text[from..to], apply(run.Style)) { IsBreak = run.IsBreak });

            if (to < run.Text.Length)
                runs.Add(run with { Text = run.Text[to..] });

            cursor = runEnd;
        }

        return paragraph with { Runs = Normalize(runs) };
    }

    /// <summary>
    /// Drops empty runs and folds neighbours that agree on style.
    /// </summary>
    /// <remarks>
    /// Not cosmetic. Every insert splits a run and every delete leaves fragments, so without this a
    /// paragraph accumulates one run per keystroke — which is a file that grows without bound, a layout
    /// pass that gets slower with every edit, and kerning that breaks at each seam because runs are
    /// measured separately.
    /// </remarks>
    public static IReadOnlyList<StyledRun> Normalize(IReadOnlyList<StyledRun> runs)
    {
        var result = new List<StyledRun>(runs.Count);

        foreach (var run in runs)
        {
            // A break carries no text but still means something, so it survives the empty-run cull.
            if (run.Text.Length == 0 && !run.IsBreak)
                continue;

            if (result.Count > 0 &&
                !run.IsBreak &&
                !result[^1].IsBreak &&
                result[^1].Style == run.Style &&
                result[^1].Inline is null &&
                run.Inline is null)
            {
                result[^1] = result[^1] with { Text = result[^1].Text + run.Text };
                continue;
            }

            result.Add(run);
        }

        return result;
    }

    // ---- body-level operations ----

    public static ShapeTextBody WithParagraph(ShapeTextBody body, int index, ShapeParagraph paragraph)
    {
        if (index < 0 || index >= body.Paragraphs.Count)
            return body;

        var paragraphs = body.Paragraphs.ToList();
        paragraphs[index] = paragraph;

        return body with { Paragraphs = paragraphs };
    }

    public static ShapeTextBody InsertText(ShapeTextBody body, NotePosition at, string text, TextStyle? style = null)
    {
        if (body.Paragraphs.ElementAtOrDefault(at.Paragraph) is not { } paragraph)
            return body;

        return WithParagraph(body, at.Paragraph, Insert(paragraph, at.Offset, text, style));
    }

    /// <summary>Splits a paragraph in two at the caret.</summary>
    public static ShapeTextBody SplitParagraph(ShapeTextBody body, NotePosition at)
    {
        if (body.Paragraphs.ElementAtOrDefault(at.Paragraph) is not { } paragraph)
            return body;

        var (left, right) = Split(paragraph, at.Offset);
        var paragraphs = body.Paragraphs.ToList();
        paragraphs[at.Paragraph] = left;
        paragraphs.Insert(at.Paragraph + 1, right);

        return body with { Paragraphs = paragraphs };
    }

    /// <summary>Pulls the paragraph after <paramref name="index"/> up into it.</summary>
    public static ShapeTextBody MergeParagraphs(ShapeTextBody body, int index)
    {
        if (index < 0 || index + 1 >= body.Paragraphs.Count)
            return body;

        var paragraphs = body.Paragraphs.ToList();
        paragraphs[index] = Merge(paragraphs[index], paragraphs[index + 1]);
        paragraphs.RemoveAt(index + 1);

        return body with { Paragraphs = paragraphs };
    }

    /// <summary>Removes a span that may run across several paragraphs.</summary>
    public static ShapeTextBody DeleteRange(ShapeTextBody body, NoteTextRange range)
    {
        var (start, end) = range.Ordered;

        if (start == end || body.Paragraphs.Count == 0)
            return body;

        start = Clamp(body, start);
        end = Clamp(body, end);

        if (start.Paragraph == end.Paragraph)
        {
            return WithParagraph(
                body,
                start.Paragraph,
                Delete(body.Paragraphs[start.Paragraph], start.Offset, end.Offset));
        }

        var paragraphs = body.Paragraphs.ToList();

        var head = Delete(paragraphs[start.Paragraph], start.Offset, LengthOf(paragraphs[start.Paragraph]));
        var tail = Delete(paragraphs[end.Paragraph], 0, end.Offset);

        paragraphs.RemoveRange(start.Paragraph, end.Paragraph - start.Paragraph + 1);
        paragraphs.Insert(start.Paragraph, Merge(head, tail));

        return body with { Paragraphs = paragraphs };
    }

    /// <summary>Applies a run format across a span that may cover several paragraphs.</summary>
    public static ShapeTextBody FormatRange(ShapeTextBody body, NoteTextRange range, Func<TextStyle, TextStyle> apply)
    {
        var (start, end) = range.Ordered;
        if (body.Paragraphs.Count == 0)
            return body;

        start = Clamp(body, start);
        end = Clamp(body, end);

        var paragraphs = body.Paragraphs.ToList();

        for (var i = start.Paragraph; i <= end.Paragraph && i < paragraphs.Count; i++)
        {
            var from = i == start.Paragraph ? start.Offset : 0;
            var to = i == end.Paragraph ? end.Offset : LengthOf(paragraphs[i]);
            paragraphs[i] = Format(paragraphs[i], from, to, apply);
        }

        return body with { Paragraphs = paragraphs };
    }

    /// <summary>Applies a paragraph-level change — alignment, indent, bullet — across a span.</summary>
    public static ShapeTextBody FormatParagraphs(ShapeTextBody body, NoteTextRange range, Func<ShapeParagraph, ShapeParagraph> apply)
    {
        var (start, end) = range.Ordered;
        if (body.Paragraphs.Count == 0)
            return body;

        var paragraphs = body.Paragraphs.ToList();
        var first = Math.Clamp(start.Paragraph, 0, paragraphs.Count - 1);
        var last = Math.Clamp(end.Paragraph, 0, paragraphs.Count - 1);

        for (var i = first; i <= last; i++)
            paragraphs[i] = apply(paragraphs[i]);

        return body with { Paragraphs = paragraphs };
    }

    public static NotePosition Clamp(ShapeTextBody body, NotePosition position)
    {
        if (body.Paragraphs.Count == 0)
            return new NotePosition(0, 0);

        var paragraph = Math.Clamp(position.Paragraph, 0, body.Paragraphs.Count - 1);
        var offset = Math.Clamp(position.Offset, 0, LengthOf(body.Paragraphs[paragraph]));

        return new NotePosition(paragraph, offset);
    }

    /// <summary>An empty body with one empty paragraph, which is the state a new container starts in.</summary>
    public static ShapeTextBody Empty() => new([new ShapeParagraph([])]);
}
