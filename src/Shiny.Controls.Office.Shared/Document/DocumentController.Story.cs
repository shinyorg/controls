using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Document;

/// <summary>
/// The laid-out story — every paragraph the caret can reach, table cells included — and the per-page
/// extras that come with pagination: footnotes and headings.
/// </summary>
public partial class DocumentController
{
    readonly List<LaidOutParagraph> storyLayout = new();
    readonly List<GridRectLike?> storyCells = new();
    readonly Dictionary<int, DocumentChromeLayout?> noteLayouts = new();

    /// <summary>Raised when <see cref="Zoom"/> changes, so a status-bar slider can follow it.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>
    /// Every laid-out paragraph in story order, parallel to <see cref="WordDocument.Paragraphs"/>.
    /// </summary>
    /// <remarks>
    /// A paragraph in a table cell is here too, at the position its cell's layout put it. This is what
    /// the caret, hit-testing and the selection wash use — the top-level <see cref="Blocks"/> are what
    /// the painter walks.
    /// </remarks>
    public IReadOnlyList<LaidOutParagraph> StoryParagraphs
    {
        get
        {
            _ = this.Blocks;
            return this.storyLayout;
        }
    }

    /// <summary>The cell rectangle a story paragraph sits in, or null for a body paragraph.</summary>
    public GridRectLike? StoryCellBounds(int paragraph)
    {
        _ = this.Blocks;
        return paragraph >= 0 && paragraph < this.storyCells.Count ? this.storyCells[paragraph] : null;
    }

    void IndexStory(DocumentLayoutResult result)
    {
        this.storyLayout.Clear();
        this.storyCells.Clear();
        this.noteLayouts.Clear();

        foreach (var block in result.Blocks)
            this.Collect(block, null);

        // Anything past the story - the notes appended to a reflowed layout - is not caret territory.
        var count = this.Document.Paragraphs.Count;
        if (this.storyLayout.Count > count)
        {
            this.storyLayout.RemoveRange(count, this.storyLayout.Count - count);
            this.storyCells.RemoveRange(count, this.storyCells.Count - count);
        }
    }

    void Collect(LaidOutBlock block, GridRectLike? cell)
    {
        switch (block)
        {
            case LaidOutParagraph paragraph:
                this.storyLayout.Add(paragraph);
                this.storyCells.Add(cell);
                break;

            case LaidOutTable table:
                foreach (var laidOutCell in table.Cells)
                {
                    var rect = new GridRectLike(laidOutCell.X, laidOutCell.Y, laidOutCell.Width, laidOutCell.Height);
                    foreach (var inner in laidOutCell.Blocks)
                        this.Collect(inner, rect);
                }

                break;
        }
    }

    /// <summary>The flow Y of the line a story position is on.</summary>
    protected double LineTopOf(DocumentPosition position)
    {
        if (position.Block < 0 || position.Block >= this.storyLayout.Count)
            return 0;

        var paragraph = this.storyLayout[position.Block];
        var line = paragraph.Lines.LastOrDefault(l => position.Offset >= l.SourceOffset) ?? paragraph.Lines[0];
        return paragraph.Y + line.Y;
    }

    /// <summary>
    /// What each footnote will take from the page that cites it.
    /// </summary>
    IReadOnlyList<NoteReservation>? FootnoteReservations()
    {
        var notes = this.Document.Footnotes;
        if (notes.Count == 0)
            return null;

        var reservations = new List<NoteReservation>(notes.Count);

        foreach (var note in notes)
        {
            if (this.NoteLayout(note) is not { } laidOut)
                continue;

            reservations.Add(new NoteReservation(this.LineTopOf(note.Reference), laidOut.Height));
        }

        return reservations;
    }

    DocumentChromeLayout? NoteLayout(DocumentNote note)
    {
        if (this.noteLayouts.TryGetValue(note.Id, out var cached))
            return cached;

        var laidOut = this.engine.Layout(note.Blocks, this.Document.Page.ContentWidth);
        var result = laidOut.Blocks.Count == 0 ? null : new DocumentChromeLayout(laidOut.Blocks, laidOut.Height);
        this.noteLayouts[note.Id] = result;
        return result;
    }

    /// <summary>
    /// The footnotes cited on a page, stacked in citation order, or null when it cites none.
    /// </summary>
    public DocumentChromeLayout? FootnotesFor(DocumentPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (!this.IsPaginated)
            return null;

        var blocks = new List<LaidOutBlock>();
        var y = 0d;

        foreach (var note in this.Document.Footnotes)
        {
            var top = this.LineTopOf(note.Reference);
            if (top < page.FlowTop || top >= page.FlowBottom)
                continue;

            if (this.NoteLayout(note) is not { } laidOut)
                continue;

            foreach (var block in laidOut.Blocks)
                blocks.Add(Shift(block, y));

            y += laidOut.Height;
        }

        return blocks.Count == 0 ? null : new DocumentChromeLayout(blocks, y);
    }

    /// <summary>Moves a laid-out block down by <paramref name="dy"/>.</summary>
    static LaidOutBlock Shift(LaidOutBlock block, double dy) => block switch
    {
        LaidOutParagraph paragraph => paragraph with { Y = paragraph.Y + dy },
        LaidOutTable table => table with
        {
            Y = table.Y + dy,
            Cells = table.Cells.Select(c => c with { Y = c.Y + dy, Blocks = c.Blocks.Select(b => Shift(b, dy)).ToList() }).ToList()
        },
        LaidOutRule rule => rule with { Y = rule.Y + dy },
        _ => block
    };

    /// <summary>
    /// The document's headings in order, for a navigation pane.
    /// </summary>
    public IReadOnlyList<DocumentHeading> Headings()
    {
        var headings = new List<DocumentHeading>();
        var paragraphs = this.Document.Paragraphs;

        for (var i = 0; i < paragraphs.Count; i++)
        {
            var paragraph = paragraphs[i];
            if (paragraph.Format.OutlineLevel <= 0)
                continue;

            var text = paragraph.VisibleText.Trim();
            if (text.Length > 0)
                headings.Add(new DocumentHeading(paragraph.Format.OutlineLevel, text, i));
        }

        return headings;
    }

    /// <summary>The page a flow Y lands on, one-based. Always 1 in reflow.</summary>
    public int PageNumberAt(double flowY)
        => this.IsPaginated ? this.Pagination.PageAtFlow(flowY).Number : 1;
}
