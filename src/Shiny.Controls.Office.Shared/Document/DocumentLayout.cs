using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Spreadsheet.View;
using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Document;

/// <summary>A block positioned in the flow, ready to paint.</summary>
public abstract record LaidOutBlock(double Y, double Height)
{
    /// <summary>
    /// True when a page break lands immediately before this block, so it must open a page.
    /// </summary>
    /// <remarks>
    /// A page break at the very end of a paragraph has no line of its own to carry it, so it is
    /// handed to whatever comes next. Ignored in reflow, where it has already been honoured as a
    /// line break.
    /// </remarks>
    public bool StartsPage { get; init; }
}

public sealed record LaidOutParagraph(
    double Y,
    double Height,
    IReadOnlyList<LaidOutLine> Lines,
    ParagraphFormat Format,
    double X,
    double Width) : LaidOutBlock(Y, Height)
{
    public LaidOutLine? LabelLine { get; init; }

    /// <summary>Where a list label is drawn, to the left of the text body.</summary>
    public double LabelX { get; init; }

    public string? LabelText { get; init; }

    public TextStyle LabelStyle { get; init; }
}

public sealed record LaidOutTableCell(
    double X,
    double Y,
    double Width,
    double Height,
    IReadOnlyList<LaidOutBlock> Blocks,
    ArgbColor? Shading);

public sealed record LaidOutTable(
    double Y,
    double Height,
    double X,
    double Width,
    IReadOnlyList<LaidOutTableCell> Cells,
    bool HasBorders) : LaidOutBlock(Y, Height);

public sealed record LaidOutRule(double Y, double Height, double X, double Width) : LaidOutBlock(Y, Height);

/// <summary>A whole document laid out at one width.</summary>
public sealed record DocumentLayoutResult(IReadOnlyList<LaidOutBlock> Blocks, double Width, double Height);

/// <summary>
/// Turns the block model into positioned geometry at a given width.
/// </summary>
/// <remarks>
/// <para>
/// This is a **reflow** engine, not a pagination engine: content is laid out as one continuous column.
/// Pagination needs widow/orphan control, footnote placement, floating-object collision and repeating
/// table headers, and a half-implementation of it puts page breaks in the wrong place — which looks
/// like a rendering bug rather than a missing feature.
/// </para>
/// <para>
/// Layout is pure and cached by width, so scrolling never re-measures and only a resize does.
/// </para>
/// </remarks>
public sealed class DocumentLayoutEngine(ITextMeasurer measurer)
{
    readonly TextLayoutEngine text = new(measurer);

    /// <summary>Set when a paragraph ended on a page break, and consumed by the next block.</summary>
    bool pendingPageBreak;

    /// <summary>Padding inside a table cell.</summary>
    public double CellPadding { get; init; } = 5;

    /// <summary>Gap between a list label and the text it introduces.</summary>
    public double LabelGap { get; init; } = 6;

    public DocumentLayoutResult Layout(IReadOnlyList<DocumentBlock> blocks, double width)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var laidOut = new List<LaidOutBlock>();
        this.pendingPageBreak = false;
        var y = this.LayoutInto(blocks, laidOut, 0, 0, Math.Max(1, width));
        return new DocumentLayoutResult(laidOut, width, y);
    }

    double LayoutInto(IReadOnlyList<DocumentBlock> blocks, List<LaidOutBlock> output, double originX, double startY, double width)
    {
        var y = startY;

        foreach (var block in blocks)
        {
            var first = output.Count;

            switch (block)
            {
                case DocumentParagraph paragraph:
                    y = this.LayoutParagraph(paragraph, output, originX, y, width);
                    break;

                case DocumentTable table:
                    y = this.LayoutTable(table, output, originX, y, width);
                    break;

                case DocumentRule:
                    output.Add(new LaidOutRule(y + 4, 1, originX, width));
                    y += 12;
                    break;
            }

            // A page break the previous block ended on belongs to this one — it had no line of its
            // own to sit on. Applied after the fact rather than passed in, because the block that
            // consumes it does not know it is coming until it has already been laid out.
            if (this.pendingPageBreak && output.Count > first)
            {
                output[first] = output[first] with { StartsPage = true };
                this.pendingPageBreak = false;
            }
        }

        return y;
    }

    double LayoutParagraph(DocumentParagraph paragraph, List<LaidOutBlock> output, double originX, double y, double width)
    {
        var format = paragraph.Format;
        y += format.SpaceBefore;

        var indentLeft = format.IndentLeft;
        var contentWidth = Math.Max(1, width - indentLeft - format.IndentRight);
        var firstLineIndent = format.IndentFirstLine;

        double labelX = 0;
        string? labelText = null;
        var labelStyle = TextStyle.Default;

        if (paragraph.List is { } label)
        {
            labelText = label.Text;
            labelStyle = label.Style;

            // The label sits in the hanging indent, to the left of where the text starts.
            var labelWidth = measurer.Measure(label.Text, label.Style).Width;
            var hanging = label.HangingIndent > 0 ? label.HangingIndent : labelWidth + this.LabelGap;

            labelX = originX + indentLeft - hanging;
            if (labelX < originX)
                labelX = originX;

            // A list paragraph's first line starts at the text position, not in the hanging indent.
            firstLineIndent = 0;
        }

        var lines = this.text.Layout(paragraph.Runs, contentWidth, format.Alignment, format.LineSpacing, firstLineIndent);
        var height = TextLayoutEngine.HeightOf(lines, format.LineSpacing);

        if (this.text.TrailingPageBreak)
            this.pendingPageBreak = true;

        // A section that starts on a new page puts a page break after the paragraph ending the one
        // before it; a continuous section flows straight on.
        if (format.SectionBreak is SectionBreakKind.NextPage or SectionBreakKind.EvenPage or SectionBreakKind.OddPage)
            this.pendingPageBreak = true;

        output.Add(new LaidOutParagraph(y, height, lines, format, originX + indentLeft, contentWidth)
        {
            LabelX = labelX,
            LabelText = labelText,
            LabelStyle = labelStyle,
            StartsPage = format.PageBreakBefore
        });

        return y + height + format.SpaceAfter;
    }

    double LayoutTable(DocumentTable table, List<LaidOutBlock> output, double originX, double y, double width)
    {
        var columnCount = table.Rows.Count == 0
            ? 0
            : table.Rows.Max(r => r.Cells.Sum(c => Math.Max(1, c.ColumnSpan)));

        if (columnCount == 0)
            return y;

        var widths = ResolveColumnWidths(table, columnCount, width);
        var cells = new List<LaidOutTableCell>();
        var rowY = y;

        // A vertically merged cell is laid out by the row that starts it; continuation rows leave the
        // slot empty and the started cell is stretched afterwards.
        var openMerges = new Dictionary<int, (int Index, double StartY)>();

        foreach (var row in table.Rows)
        {
            var column = 0;
            var rowHeight = 0d;
            var rowStart = cells.Count;

            foreach (var cell in row.Cells)
            {
                var span = Math.Max(1, cell.ColumnSpan);
                var cellWidth = 0d;
                for (var i = column; i < Math.Min(column + span, widths.Count); i++)
                    cellWidth += widths[i];

                var x = originX + widths.Take(column).Sum();

                if (cell.IsVerticalContinuation)
                {
                    // Nothing is laid out here; the cell above will grow to cover this row.
                    column += span;
                    continue;
                }

                var inner = new List<LaidOutBlock>();
                var innerHeight = this.LayoutInto(
                    cell.Blocks,
                    inner,
                    x + this.CellPadding,
                    rowY + this.CellPadding,
                    Math.Max(1, cellWidth - this.CellPadding * 2));

                var contentHeight = innerHeight - rowY + this.CellPadding;
                rowHeight = Math.Max(rowHeight, contentHeight);

                openMerges[column] = (cells.Count, rowY);
                cells.Add(new LaidOutTableCell(x, rowY, cellWidth, contentHeight, inner, cell.Shading));
                column += span;
            }

            // Every cell in the row gets the row's full height, so borders line up.
            for (var i = rowStart; i < cells.Count; i++)
                cells[i] = cells[i] with { Height = rowHeight };

            // Stretch any cell still spanning down into this row.
            foreach (var (start, info) in openMerges.ToList())
            {
                var covered = row.Cells.Any(c => c.IsVerticalContinuation);
                if (!covered || info.Index >= cells.Count)
                    continue;

                var existing = cells[info.Index];
                if (existing.Y < rowY)
                    cells[info.Index] = existing with { Height = rowY + rowHeight - existing.Y };

                _ = start;
            }

            rowY += rowHeight;
        }

        var tableHeight = rowY - y;
        output.Add(new LaidOutTable(y, tableHeight, originX, widths.Sum(), cells, table.HasBorders));
        return rowY + 8;
    }

    /// <summary>
    /// Column widths from the table grid, scaled to the available width. A grid wider than the view is
    /// squeezed rather than allowed to overflow, because a reflow view has nowhere to scroll sideways.
    /// </summary>
    static List<double> ResolveColumnWidths(DocumentTable table, int columnCount, double width)
    {
        var widths = new List<double>(table.ColumnWidths);

        while (widths.Count < columnCount)
            widths.Add(0);

        if (widths.Count > columnCount)
            widths = widths.Take(columnCount).ToList();

        var total = widths.Sum();
        if (total <= 0)
            return Enumerable.Repeat(width / columnCount, columnCount).ToList();

        // Any column the grid left at zero takes an equal share of what is unaccounted for.
        var zeroes = widths.Count(x => x <= 0);
        if (zeroes > 0)
        {
            var share = Math.Max(20, (width - total) / zeroes);
            for (var i = 0; i < widths.Count; i++)
            {
                if (widths[i] <= 0)
                    widths[i] = share;
            }

            total = widths.Sum();
        }

        var scale = width / total;
        return widths.Select(x => x * scale).ToList();
    }
}

/// <summary>Scroll state for a laid-out document.</summary>
public sealed class DocumentViewport
{
    public double Width { get; set; } = 800;
    public double Height { get; set; } = 600;
    public double ScrollY { get; private set; }
    public double ContentHeight { get; set; }

    /// <summary>
    /// How far the page is scrolled sideways.
    /// </summary>
    /// <remarks>
    /// A page is a fixed width - that is what makes it a page - so on anything narrower than one it is
    /// clipped, and without this axis the right-hand side of every line simply could not be reached.
    /// It stays zero on a viewport wide enough for the page, where the page is centred instead.
    /// </remarks>
    public double ScrollX { get; private set; }

    /// <summary>The laid-out width, which is the page's rather than the viewport's.</summary>
    public double ContentWidth { get; set; }

    public void ScrollTo(double y)
        => this.ScrollY = Math.Clamp(y, 0, Math.Max(0, this.ContentHeight - this.Height));

    public void ScrollToX(double x)
        => this.ScrollX = Math.Clamp(x, 0, Math.Max(0, this.ContentWidth - this.Width));

    public void ScrollBy(double delta) => this.ScrollTo(this.ScrollY + delta);

    public void ScrollByX(double delta) => this.ScrollToX(this.ScrollX + delta);

    /// <summary>Blocks intersecting the visible band. Everything else is skipped entirely.</summary>
    public IEnumerable<LaidOutBlock> Visible(IReadOnlyList<LaidOutBlock> blocks)
    {
        var top = this.ScrollY;
        var bottom = this.ScrollY + this.Height;

        foreach (var block in blocks)
        {
            if (block.Y + block.Height < top)
                continue;

            if (block.Y > bottom)
                yield break;

            yield return block;
        }
    }
}
