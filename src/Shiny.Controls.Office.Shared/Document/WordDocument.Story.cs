using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Shiny.Controls.Office.Document;

/// <summary>
/// The story: every paragraph the caret can reach, in reading order, whether it sits in the body or in
/// a table cell.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DocumentPosition.Block"/> indexes this list, not <see cref="WordDocument.Blocks"/>. The
/// top-level block list is what layout and pagination walk, and a table is one entry in it — which
/// left the text inside a table with no position of its own: the caret could not enter a cell, and
/// find, spelling and every formatting command skipped straight past it.
/// </para>
/// <para>
/// Flattening into reading order is what Word does too: arrow keys walk out of the last cell of a
/// row into the first of the next, and a search runs straight through a table. A vertically merged
/// continuation cell contributes nothing, matching the layout, which never lays one out.
/// </para>
/// </remarks>
public sealed partial class WordDocument
{
    readonly List<DocumentParagraph> story = new();
    readonly List<int> storyTop = new();
    readonly List<DocumentStoryCell?> storyCell = new();
    readonly Dictionary<Paragraph, int> storyIndex = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Every paragraph in reading order, including those inside table cells.
    /// </summary>
    /// <remarks>
    /// The index space <see cref="DocumentPosition.Block"/> uses. Identical to <see cref="Blocks"/> for
    /// a document with no tables in it.
    /// </remarks>
    public IReadOnlyList<DocumentParagraph> Paragraphs => this.story;

    /// <summary>The top-level block the paragraph at a story index belongs to, or -1.</summary>
    public int TopBlockOf(int paragraph)
        => paragraph >= 0 && paragraph < this.storyTop.Count ? this.storyTop[paragraph] : -1;

    internal int TopOf(int paragraph) => this.TopBlockOf(paragraph);

    /// <summary>The cell a paragraph sits in, or null for a body paragraph.</summary>
    public DocumentStoryCell? CellOf(int paragraph)
        => paragraph >= 0 && paragraph < this.storyCell.Count ? this.storyCell[paragraph] : null;

    /// <summary>The story index of the first paragraph of a top-level block, or -1 when it has none.</summary>
    public int FirstParagraphOf(int top)
    {
        for (var i = 0; i < this.storyTop.Count; i++)
        {
            if (this.storyTop[i] == top)
                return i;

            if (this.storyTop[i] > top)
                break;
        }

        return -1;
    }

    /// <summary>The story index of a live <c>w:p</c>, or -1 when it is not part of the body.</summary>
    internal int IndexOf(Paragraph? paragraph)
        => paragraph is not null && this.storyIndex.TryGetValue(paragraph, out var index) ? index : -1;

    /// <summary>True when two story paragraphs share a parent element, so they can be merged.</summary>
    internal bool ShareContainer(int a, int b)
    {
        var first = this.ParagraphElementAt(a);
        var second = this.ParagraphElementAt(b);
        return first?.Parent is not null && ReferenceEquals(first.Parent, second?.Parent);
    }

    void RebuildStory()
    {
        this.story.Clear();
        this.storyTop.Clear();
        this.storyCell.Clear();
        this.storyIndex.Clear();

        for (var top = 0; top < this.blocks.Count; top++)
            this.Collect(this.blocks[top], top, null);
    }

    void Collect(DocumentBlock block, int top, DocumentStoryCell? cell)
    {
        switch (block)
        {
            case DocumentParagraph paragraph:
                if (paragraph.Element is { } element)
                    this.storyIndex[element] = this.story.Count;

                this.story.Add(paragraph);
                this.storyTop.Add(top);
                this.storyCell.Add(cell);
                break;

            case DocumentTable table:
                for (var r = 0; r < table.Rows.Count; r++)
                {
                    var cells = table.Rows[r].Cells;
                    for (var c = 0; c < cells.Count; c++)
                    {
                        if (cells[c].IsVerticalContinuation)
                            continue;

                        // The innermost table wins for a nested one: that is the table a row or
                        // column command at this caret means.
                        var address = new DocumentStoryCell(top, r, c, table.Rows.Count, cells.Count);

                        foreach (var inner in cells[c].Blocks)
                            this.Collect(inner, top, address);
                    }
                }

                break;
        }
    }

    /// <summary>
    /// Works out, for each paragraph that ends a section, how the section after it starts.
    /// </summary>
    /// <remarks>
    /// A section's break type is recorded on the section itself — at its <em>end</em>, like every other
    /// section property — so whether a new page follows paragraph N is decided by the next
    /// <c>w:sectPr</c> in the document, not by the one in paragraph N. Only this pass can see both.
    /// </remarks>
    void ApplySectionBreaks()
    {
        var next = TypeOf(this.body?.Elements<SectionProperties>().LastOrDefault());

        for (var top = this.blocks.Count - 1; top >= 0; top--)
        {
            if (this.blocks[top] is not DocumentParagraph paragraph)
                continue;

            var own = paragraph.Element?.ParagraphProperties?.GetFirstChild<SectionProperties>();
            if (own is null)
            {
                if (paragraph.Format.SectionBreak != SectionBreakKind.None)
                    this.blocks[top] = paragraph with { Format = paragraph.Format with { SectionBreak = SectionBreakKind.None } };

                continue;
            }

            if (paragraph.Format.SectionBreak != next)
                this.blocks[top] = paragraph with { Format = paragraph.Format with { SectionBreak = next } };

            next = TypeOf(own);
        }

        static SectionBreakKind TypeOf(SectionProperties? section)
            => OoxmlUnits.EnumAttribute(section?.GetFirstChild<SectionType>(), "val") switch
            {
                "continuous" => SectionBreakKind.Continuous,
                "evenPage" => SectionBreakKind.EvenPage,
                "oddPage" => SectionBreakKind.OddPage,
                _ => SectionBreakKind.NextPage
            };
    }

    /// <summary>Removes a top-level block, its element and its projection together.</summary>
    internal void RemoveTopBlock(int top) => this.RemoveBlock(top);
}


/// <summary>Where a story paragraph sits inside a table.</summary>
/// <param name="TopBlock">The table's index in <see cref="WordDocument.Blocks"/>.</param>
/// <param name="Row">Zero-based row.</param>
/// <param name="Cell">Zero-based cell within the row — a cell, not a grid column, so a spanned cell counts once.</param>
/// <param name="RowCount">Rows in the table.</param>
/// <param name="CellCount">Cells in this row.</param>
public sealed record DocumentStoryCell(int TopBlock, int Row, int Cell, int RowCount, int CellCount);
