namespace Shiny.Controls.Office.Text;

/// <summary>A run of text sharing one style, as it comes out of a document.</summary>
public sealed record StyledRun(string Text, TextStyle Style)
{
    /// <summary>A run that forces a line break rather than carrying text.</summary>
    public bool IsBreak { get; init; }

    /// <summary>
    /// True for a break that starts a new page rather than a new line.
    /// </summary>
    /// <remarks>
    /// Always set together with <see cref="IsBreak"/>: in a reflow view a page break has to fall back
    /// to a line break, which is the closest thing a continuous column has to one. In print layout the
    /// paginator reads this and starts a page instead.
    /// </remarks>
    public bool IsPageBreak { get; init; }

    /// <summary>
    /// Non-null when this run's text is computed rather than authored — a page number, say.
    /// </summary>
    /// <remarks>
    /// <see cref="StyledRun.Text"/> still carries the document's last-saved result, so a field that
    /// nothing resolves still measures and draws as something sensible.
    /// </remarks>
    public DocumentFieldKind Field { get; init; }

    /// <summary>Non-null when this run is an inline object — a picture or a shape — rather than text.</summary>
    public InlineObject? Inline { get; init; }

    /// <summary>
    /// How much of the paragraph's offset space the run occupies, when that is not its text's length.
    /// </summary>
    /// <remarks>
    /// -1, the default, means <see cref="Text"/>'s length. Anything else makes the run atomic: it is laid
    /// out as one unbreakable piece and the caret steps over it in one go, the way it steps over a
    /// picture. A footnote reference is the case — it is drawn as its number, which can be two digits,
    /// but it is one mark in the document and one character to the caret.
    /// </remarks>
    public int SourceLength { get; init; } = -1;

    /// <summary>The <c>w:id</c> of the footnote this run references, or null for an ordinary run.</summary>
    public int? FootnoteId { get; init; }

    /// <summary>
    /// Set on a tab that runs to a right-aligned tab stop: the leader character drawn across it
    /// (<c>'.'</c> for dots), or a space for none.
    /// </summary>
    public char? RightTabLeader { get; init; }

    /// <summary>How much of the offset space the run covers — see <see cref="SourceLength"/>.</summary>
    public int Length => this.IsBreak ? 0 : this.Inline is not null ? 1 : this.SourceLength >= 0 ? this.SourceLength : this.Text.Length;
}


/// <summary>The computed fields the document layer can resolve.</summary>
/// <remarks>
/// Deliberately two. A general field engine means parsing the whole instruction grammar — merge
/// fields, cross-references, formulas, date formats — and the ones that matter for a page are these.
/// Everything else keeps the result Word last wrote, which is what a reader would have seen anyway.
/// </remarks>
public enum DocumentFieldKind
{
    None = 0,

    /// <summary>The one-based number of the page this run is drawn on.</summary>
    Page,

    /// <summary>How many pages the document has.</summary>
    PageCount
}

/// <summary>A run positioned on a line.</summary>
public sealed record LaidOutRun(string Text, TextStyle Style, double X, double Width, InlineObject? Inline = null)
{
    public double Height { get; init; }

    /// <summary>
    /// Character index of this piece within the paragraph's concatenated text.
    /// </summary>
    /// <remarks>
    /// Wrapping splits a run into pieces and drops nothing, but the pieces no longer line up with the
    /// source. Carrying the offset is what lets a click be turned back into a caret position, and it
    /// has to be recorded during layout because afterwards the mapping is gone.
    /// </remarks>
    public int SourceOffset { get; init; }

    /// <summary>Offset-space length when it differs from the text's — see <see cref="StyledRun.SourceLength"/>.</summary>
    public int SourceLength { get; init; } = -1;

    /// <summary>True when the caret treats this piece as one indivisible character.</summary>
    public bool IsAtomic => this.Inline is not null || this.SourceLength >= 0;

    /// <summary>How much of the offset space the piece covers.</summary>
    public int Length => this.Inline is not null ? 1 : this.SourceLength >= 0 ? this.SourceLength : this.Text.Length;
}

/// <summary>One line of a laid-out paragraph.</summary>
public sealed record LaidOutLine(IReadOnlyList<LaidOutRun> Runs, double Y, double Width, double Ascent, double Descent)
{
    /// <summary>True when a page break immediately precedes this line, so it must open a page.</summary>
    /// <remarks>Ignored in reflow, where the break has already been honoured as a line break.</remarks>
    public bool StartsPage { get; init; }

    public double Height => this.Ascent + this.Descent;

    /// <summary>Character index of the line's first character within the paragraph.</summary>
    public int SourceOffset { get; init; }

    /// <summary>One past the line's last character. Trailing whitespace is included.</summary>
    public int SourceEnd { get; init; }
}

/// <summary>
/// Breaks styled runs into lines that fit a width.
/// </summary>
/// <remarks>
/// <para>
/// A greedy word-wrapper: it accumulates whole words and breaks at the last opportunity that still
/// fits. That is what Word and PowerPoint do too — neither uses Knuth-Plass — so matching them here is
/// both simpler and more faithful than being cleverer.
/// </para>
/// <para>
/// Break opportunities are whitespace, plus after a hyphen. A single word longer than the line is
/// broken mid-word rather than allowed to overflow, because a table cell one character wide would
/// otherwise paint across the whole page.
/// </para>
/// </remarks>
public sealed class TextLayoutEngine(ITextMeasurer measurer)
{
    public ITextMeasurer Measurer { get; } = measurer;

    /// <summary>
    /// True when the paragraph just laid out ended with a page break that no line consumed.
    /// </summary>
    /// <remarks>
    /// Valid only until the next <see cref="Layout"/> call. A field rather than an out parameter
    /// because every caller but the paginating one ignores it, and reflow is the common path.
    /// </remarks>
    public bool TrailingPageBreak { get; private set; }

    /// <summary>
    /// Lays out a paragraph's runs into lines no wider than <paramref name="width"/>.
    /// </summary>
    /// <param name="lineSpacing">Multiplier applied to each line's natural height.</param>
    public IReadOnlyList<LaidOutLine> Layout(
        IReadOnlyList<StyledRun> runs,
        double width,
        TextAlignment alignment = TextAlignment.Left,
        double lineSpacing = 1.0,
        double firstLineIndent = 0)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var lines = new List<LaidOutLine>();
        var current = new List<PendingPiece>();
        var y = 0d;
        var indent = firstLineIndent;

        // Running character index into the paragraph's concatenated text.
        var sourceOffset = 0;

        // Set when a page break has been seen; consumed by the next line committed, which is the
        // first line of the new page. Carried rather than applied immediately because the break
        // itself has no line of its own.
        var startsPage = false;

        void Flush(bool lastLineOfParagraph)
        {
            if (current.Count == 0 && !lastLineOfParagraph)
                return;

            var line = Commit(current, y, width - indent, indent, alignment, lastLineOfParagraph, this.Measurer);
            if (startsPage)
            {
                line = line with { StartsPage = true };
                startsPage = false;
            }

            lines.Add(line);
            y += line.Height * lineSpacing;
            current.Clear();
            indent = 0;
        }

        for (var runIndex = 0; runIndex < runs.Count; runIndex++)
        {
            var run = runs[runIndex];

            if (run.IsBreak)
            {
                Flush(lastLineOfParagraph: true);

                if (run.IsPageBreak)
                    startsPage = true;

                continue;
            }

            if (run.Inline is { } inline)
            {
                var available = width - indent - current.Sum(p => p.Width);

                // An object too wide for what is left of the line starts a new one. It is never
                // scaled to fit: the size is the author's, and a picture quietly shrunk to the
                // margin is a different picture.
                if (inline.Width > available && current.Count > 0)
                    Flush(lastLineOfParagraph: false);

                // Ascent is the whole height, descent zero: an inline object sits on the baseline
                // rather than straddling it, which is where Word puts one.
                current.Add(new PendingPiece(string.Empty, run.Style, inline.Width, inline.Height, 0, inline, sourceOffset, 1));

                // One character, so caret arithmetic after it stays right.
                sourceOffset++;
                continue;
            }

            if (run.RightTabLeader is { } leader)
            {
                // A right-aligned tab stop: the tab stretches so whatever follows it on the line ends at
                // the right margin — the page number at the end of a table-of-contents line.
                var rest = 0d;
                for (var j = runIndex + 1; j < runs.Count && !runs[j].IsBreak; j++)
                    rest += runs[j].Inline?.Width ?? this.Measurer.Measure(runs[j].Text, runs[j].Style).Width;

                var used = current.Sum(p => p.Width);
                var space = this.Measurer.Measure(" ", run.Style).Width;
                var tabWidth = Math.Max(space, width - indent - used - rest - 1);

                var fill = string.Empty;
                if (leader != ' ')
                {
                    var dot = Math.Max(1, this.Measurer.Measure(leader.ToString(), run.Style).Width);
                    var count = (int)Math.Floor((tabWidth - (space * 2)) / dot);
                    fill = count > 0 ? new string(leader, count) : string.Empty;
                }

                var metrics = this.Measurer.LineMetrics(run.Style);
                current.Add(new PendingPiece(fill, run.Style, tabWidth, metrics.Ascent, metrics.Descent, null, sourceOffset, run.Length));
                sourceOffset += run.Length;
                continue;
            }

            if (run.SourceLength >= 0)
            {
                // Atomic text: one piece, never wrapped inside, occupying its declared length.
                var atomic = this.Measurer.Measure(run.Text, run.Style);
                if (atomic.Width > width - indent - current.Sum(p => p.Width) && current.Count > 0)
                    Flush(lastLineOfParagraph: false);

                current.Add(new PendingPiece(run.Text, run.Style, atomic.Width, atomic.Ascent, atomic.Descent, null, sourceOffset, run.SourceLength));
                sourceOffset += run.SourceLength;
                continue;
            }

            foreach (var piece in Split(run.Text))
            {
                var pieceOffset = sourceOffset;
                sourceOffset += piece.Length;
                var metrics = this.Measurer.Measure(piece, run.Style);
                var used = current.Sum(p => p.Width);
                var available = width - indent;

                if (used + metrics.Width > available && current.Count > 0)
                {
                    // A trailing space is allowed to hang past the edge rather than forcing a break.
                    if (!IsWhitespace(piece))
                    {
                        Flush(lastLineOfParagraph: false);
                        used = 0;
                        available = width;
                    }
                }

                // A single piece wider than the whole line has to be broken mid-word.
                if (metrics.Width > available && current.Count == 0 && piece.Length > 1)
                {
                    var fragmentOffset = pieceOffset;
                    foreach (var fragment in this.BreakOversized(piece, run.Style, available))
                    {
                        var fragmentMetrics = this.Measurer.Measure(fragment, run.Style);
                        if (current.Sum(p => p.Width) + fragmentMetrics.Width > available && current.Count > 0)
                            Flush(lastLineOfParagraph: false);

                        current.Add(new PendingPiece(fragment, run.Style, fragmentMetrics.Width, fragmentMetrics.Ascent, fragmentMetrics.Descent, null, fragmentOffset, fragment.Length));
                        fragmentOffset += fragment.Length;
                    }

                    continue;
                }

                current.Add(new PendingPiece(piece, run.Style, metrics.Width, metrics.Ascent, metrics.Descent, null, pieceOffset, piece.Length));
            }
        }

        Flush(lastLineOfParagraph: true);

        // A page break as the very last thing in a paragraph belongs to whatever comes next, and
        // there is no line here to hang it on. TrailingPageBreak is how the caller learns of it.
        this.TrailingPageBreak = startsPage;

        // A paragraph with no content at all still occupies one empty line.
        if (lines.Count == 0)
        {
            var style = runs.Count > 0 ? runs[0].Style : TextStyle.Default;
            var metrics = this.Measurer.LineMetrics(style);
            lines.Add(new LaidOutLine([], 0, 0, metrics.Ascent, metrics.Descent));
        }

        return lines;
    }

    /// <summary>Total height of a laid-out paragraph.</summary>
    public static double HeightOf(IReadOnlyList<LaidOutLine> lines, double lineSpacing = 1.0)
        => lines.Count == 0 ? 0 : lines[^1].Y + lines[^1].Height * lineSpacing;

    readonly record struct PendingPiece(string Text, TextStyle Style, double Width, double Ascent, double Descent, InlineObject? Inline, int SourceOffset, int Length);

    static LaidOutLine Commit(
        List<PendingPiece> pieces,
        double y,
        double contentWidth,
        double indent,
        TextAlignment alignment,
        bool lastLine,
        ITextMeasurer measurer)
    {
        // Trailing whitespace never participates in width or alignment.
        var end = pieces.Count;
        while (end > 0 && IsWhitespace(pieces[end - 1].Text))
            end--;

        var used = 0d;
        for (var i = 0; i < end; i++)
            used += pieces[i].Width;

        var ascent = 0d;
        var descent = 0d;
        for (var i = 0; i < end; i++)
        {
            ascent = Math.Max(ascent, pieces[i].Inline is { } inline ? inline.Height : pieces[i].Ascent);
            descent = Math.Max(descent, pieces[i].Inline is null ? pieces[i].Descent : 0);
        }

        if (end == 0)
        {
            var fallback = measurer.LineMetrics(pieces.Count > 0 ? pieces[0].Style : TextStyle.Default);
            ascent = fallback.Ascent;
            descent = fallback.Descent;
        }

        var slack = Math.Max(0, contentWidth - used);
        var x = indent + alignment switch
        {
            TextAlignment.Center => slack / 2,
            TextAlignment.Right => slack,
            _ => 0
        };

        // Justification stretches the gaps, not the words - and never on the last line of a paragraph,
        // which is what stops a two-word closing line being spread across the full measure.
        var gapExtra = 0d;
        if (alignment == TextAlignment.Justify && !lastLine)
        {
            var gaps = 0;
            for (var i = 0; i < end; i++)
            {
                if (IsWhitespace(pieces[i].Text))
                    gaps++;
            }

            if (gaps > 0)
                gapExtra = slack / gaps;
        }

        var runs = new List<LaidOutRun>(end);
        for (var i = 0; i < end; i++)
        {
            var piece = pieces[i];
            runs.Add(new LaidOutRun(piece.Text, piece.Style, x, piece.Width, piece.Inline)
            {
                Height = piece.Inline?.Height ?? piece.Ascent + piece.Descent,
                SourceOffset = piece.SourceOffset,
                SourceLength = piece.Inline is null && piece.Length != piece.Text.Length ? piece.Length : -1
            });

            x += piece.Width;
            if (gapExtra > 0 && IsWhitespace(piece.Text))
                x += gapExtra;
        }

        // The line spans from its first piece to past its last, including the trailing whitespace that
        // was trimmed for width - a caret clicked past the end of a line belongs after that space.
        var lineStart = pieces.Count > 0 ? pieces[0].SourceOffset : 0;
        var lineEnd = pieces.Count > 0
            ? pieces[^1].SourceOffset + pieces[^1].Length
            : lineStart;

        return new LaidOutLine(runs, y, used, ascent, descent)
        {
            SourceOffset = lineStart,
            SourceEnd = lineEnd
        };
    }

    /// <summary>
    /// Splits text into wrapping pieces: words, and the whitespace runs between them kept as their own
    /// pieces so they can hang past the right edge instead of forcing a break.
    /// </summary>
    static IEnumerable<string> Split(string text)
    {
        if (text.Length == 0)
            yield break;

        var start = 0;
        var inWhitespace = char.IsWhiteSpace(text[0]);

        for (var i = 1; i <= text.Length; i++)
        {
            var atEnd = i == text.Length;
            var isWhitespace = !atEnd && char.IsWhiteSpace(text[i]);

            // A hyphen is a break opportunity after it, not before.
            var afterHyphen = !atEnd && !isWhitespace && !inWhitespace && text[i - 1] == '-';

            if (atEnd || isWhitespace != inWhitespace || afterHyphen)
            {
                yield return text[start..i];
                if (atEnd)
                    yield break;

                start = i;
                inWhitespace = isWhitespace;
            }
        }
    }

    /// <summary>Chops a word too long for the line into as many fragments as it takes.</summary>
    IEnumerable<string> BreakOversized(string word, TextStyle style, double width)
    {
        var start = 0;
        while (start < word.Length)
        {
            var length = 1;
            while (start + length < word.Length &&
                   this.Measurer.Measure(word.AsSpan(start, length + 1), style).Width <= width)
                length++;

            yield return word.Substring(start, length);
            start += length;
        }
    }

    static bool IsWhitespace(string text) => text.Length > 0 && char.IsWhiteSpace(text[0]);
}
