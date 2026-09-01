namespace Shiny.Controls.Office.Text;

/// <summary>One laid-out paragraph inside a shape's text body.</summary>
public sealed record LaidOutShapeParagraph(
    ShapeParagraph Paragraph,
    IReadOnlyList<LaidOutLine> Lines,
    double Indent,
    string? Bullet,
    TextStyle BulletStyle,
    double BulletAdvance)
{
    /// <summary>Top of the paragraph, relative to the shape's origin.</summary>
    public double Y { get; init; }

    /// <summary>Total height including the space above and below it.</summary>
    public double Height { get; init; }

    /// <summary>Effective line spacing, after the body's autofit reduction.</summary>
    public double LineSpacing { get; init; } = 1.0;
}

/// <summary>
/// A shape's text body, laid out inside its bounds.
/// </summary>
/// <remarks>
/// X and Y are relative to the shape's top-left corner, so one layout serves both the painter (which
/// translates it onto the canvas) and the editor (which hit-tests it against a click).
/// </remarks>
public sealed record LaidOutTextBody(IReadOnlyList<LaidOutShapeParagraph> Paragraphs)
{
    /// <summary>Left edge of the text column, relative to the shape.</summary>
    public double Left { get; init; }

    /// <summary>Top of the first paragraph, after vertical anchoring.</summary>
    public double Top { get; init; }

    public double Width { get; init; }
}

/// <summary>
/// Lays out the text inside a shape.
/// </summary>
/// <remarks>
/// <para>
/// Shared deliberately. The painter needs this to draw and the editor needs the identical result to
/// place a caret — and if the two computed it separately the caret would land in the right place
/// right up until one of them was changed.
/// </para>
/// <para>
/// This is layout only: nothing here draws, so it lives in the kernel with no Skia dependency and can
/// be tested against a stub measurer.
/// </para>
/// </remarks>
public static class ShapeTextLayout
{
    /// <summary>Gap between a bullet glyph and the text it introduces.</summary>
    public const double BulletGap = 6;

    /// <summary>Indent added per outline level.</summary>
    public const double LevelIndent = 24;

    public static LaidOutTextBody Layout(
        ShapeTextBody body,
        double shapeWidth,
        double shapeHeight,
        ITextMeasurer measurer)
    {
        var engine = new TextLayoutEngine(measurer);
        var width = shapeWidth - (body.InsetLeft + body.InsetRight);
        var paragraphs = new List<LaidOutShapeParagraph>();

        if (width <= 1)
            return new LaidOutTextBody(paragraphs);

        // Measured in full before anything is positioned, because vertical anchoring needs the total
        // height and that is not known until the last paragraph has wrapped.
        var total = 0d;
        foreach (var paragraph in body.Paragraphs)
        {
            var scaled = Scale(paragraph.Runs, body.FontScale);
            var spacing = Math.Max(0.5, paragraph.LineSpacing - body.LineSpaceReduction);
            var bulletStyle = scaled.Count > 0 ? scaled[0].Style : TextStyle.Default;
            var bullet = paragraph.PlainText.Length > 0 ? paragraph.Bullet : null;

            // The bullet's advance is reserved *inside* the text box. Hanging it to the left of the
            // body inset puts it outside the shape, where the clip swallows it without a trace.
            var bulletAdvance = bullet is null ? 0 : measurer.Measure(bullet, bulletStyle).Width + BulletGap;
            var indent = paragraph.Level * LevelIndent + bulletAdvance;

            var lines = engine.Layout(scaled, Math.Max(1, width - indent), paragraph.Alignment, spacing);
            var height = TextLayoutEngine.HeightOf(lines, spacing);

            paragraphs.Add(new LaidOutShapeParagraph(paragraph, lines, indent, bullet, bulletStyle, bulletAdvance)
            {
                Y = total + paragraph.SpaceBefore,
                Height = height,
                LineSpacing = spacing
            });

            total += paragraph.SpaceBefore + height + paragraph.SpaceAfter;
        }

        var available = shapeHeight - (body.InsetTop + body.InsetBottom);
        var top = body.InsetTop + body.Anchor switch
        {
            TextAnchor.Middle => Math.Max(0, (available - total) / 2),
            TextAnchor.Bottom => Math.Max(0, available - total),
            _ => 0
        };

        return new LaidOutTextBody(paragraphs)
        {
            Left = body.InsetLeft,
            Top = top,
            Width = width
        };
    }

    /// <summary>Applies PowerPoint's recorded autofit shrink to a paragraph's runs.</summary>
    public static IReadOnlyList<StyledRun> Scale(IReadOnlyList<StyledRun> runs, double factor)
    {
        if (Math.Abs(factor - 1) < 0.001)
            return runs;

        return runs
            .Select(x => x with { Style = x.Style with { FontSize = x.Style.FontSize * factor } })
            .ToList();
    }

    /// <summary>
    /// The caret rectangle for an offset in a paragraph, relative to the shape.
    /// </summary>
    /// <remarks>
    /// Returns null when the paragraph index is out of range, which happens routinely while an edit
    /// is in flight and the model has not been reprojected yet.
    /// </remarks>
    public static (double X, double Y, double Height)? CaretAt(
        LaidOutTextBody layout,
        int paragraph,
        int offset,
        ITextMeasurer measurer)
    {
        if (layout.Paragraphs.ElementAtOrDefault(paragraph) is not { } block)
            return null;

        var line = LineFor(block, offset);
        if (line is null)
            return (layout.Left + block.Indent, layout.Top + block.Y, 12);

        var x = layout.Left + block.Indent + OffsetToX(line, offset, measurer);
        return (x, layout.Top + block.Y + line.Y, Math.Max(4, line.Height));
    }

    /// <summary>
    /// The position a point maps to, relative to the shape.
    /// </summary>
    /// <remarks>
    /// Clamps rather than failing: a click below the last line means the end of the text, which is
    /// what dragging past the bottom of a text box has to do.
    /// </remarks>
    public static (int Paragraph, int Offset) PositionAt(
        LaidOutTextBody layout,
        double x,
        double y,
        ITextMeasurer measurer)
    {
        if (layout.Paragraphs.Count == 0)
            return (0, 0);

        var index = 0;
        for (var i = 0; i < layout.Paragraphs.Count; i++)
        {
            var block = layout.Paragraphs[i];
            index = i;

            if (y < layout.Top + block.Y + block.Height)
                break;
        }

        var target = layout.Paragraphs[index];
        var localY = y - layout.Top - target.Y;
        var localX = x - layout.Left - target.Indent;

        LaidOutLine? found = null;
        foreach (var line in target.Lines)
        {
            found = line;
            if (localY < line.Y + line.Height)
                break;
        }

        if (found is null)
            return (index, 0);

        return (index, XToOffset(found, localX, measurer));
    }

    /// <summary>The line an offset falls on — the last one that starts at or before it.</summary>
    public static LaidOutLine? LineFor(LaidOutShapeParagraph block, int offset)
    {
        LaidOutLine? found = null;

        foreach (var line in block.Lines)
        {
            if (line.SourceOffset > offset)
                break;

            found = line;
        }

        return found ?? block.Lines.FirstOrDefault();
    }

    /// <summary>Horizontal position of an offset within a line.</summary>
    public static double OffsetToX(LaidOutLine line, int offset, ITextMeasurer measurer)
    {
        foreach (var run in line.Runs)
        {
            var start = run.SourceOffset;
            var end = start + run.Text.Length;

            if (offset < start)
                return run.X;

            if (offset <= end)
            {
                var local = offset - start;
                return run.X + measurer.Measure(run.Text.AsSpan(0, local), run.Style).Width;
            }
        }

        return line.Runs.Count == 0
            ? 0
            : line.Runs[^1].X + line.Runs[^1].Width;
    }

    /// <summary>
    /// The offset nearest a horizontal position on a line.
    /// </summary>
    /// <remarks>
    /// Measures cumulatively from the start of each run rather than per character: the width of a
    /// character depends on the ones before it once kerning is involved, so summing individual widths
    /// drifts across a long line.
    /// </remarks>
    public static int XToOffset(LaidOutLine line, double x, ITextMeasurer measurer)
    {
        if (line.Runs.Count == 0)
            return line.SourceOffset;

        foreach (var run in line.Runs)
        {
            if (x > run.X + run.Width)
                continue;

            var local = 0;
            var previous = 0d;

            for (var i = 1; i <= run.Text.Length; i++)
            {
                var width = measurer.Measure(run.Text.AsSpan(0, i), run.Style).Width;

                // Past the midpoint of a character means the caret belongs after it, which is what
                // makes clicking the right half of a letter put the caret to its right.
                if (run.X + (previous + width) / 2 > x)
                    break;

                local = i;
                previous = width;
            }

            return run.SourceOffset + local;
        }

        var last = line.Runs[^1];
        return last.SourceOffset + last.Text.Length;
    }

    /// <summary>Selection rectangles for a span within one paragraph, relative to the shape.</summary>
    public static IEnumerable<(double X, double Y, double Width, double Height)> SelectionRects(
        LaidOutTextBody layout,
        int paragraph,
        int start,
        int end,
        ITextMeasurer measurer)
    {
        if (layout.Paragraphs.ElementAtOrDefault(paragraph) is not { } block || end <= start)
            yield break;

        foreach (var line in block.Lines)
        {
            var from = Math.Max(start, line.SourceOffset);
            var to = Math.Min(end, line.SourceEnd);
            if (to <= from)
                continue;

            var left = OffsetToX(line, from, measurer);
            var right = OffsetToX(line, to, measurer);

            yield return (
                layout.Left + block.Indent + left,
                layout.Top + block.Y + line.Y,
                Math.Max(1, right - left),
                Math.Max(4, line.Height));
        }
    }
}
