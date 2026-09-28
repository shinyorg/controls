using DocumentFormat.OpenXml.Wordprocessing;
using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Document;

/// <summary>The Review tab: comments, tracked changes and the word count.</summary>
public sealed partial class DocumentEditorController
{
    int nextRevisionId;
    DocumentStatistics? statistics;
    int statisticsPage = -1;

    /// <summary>Raised when the caret moves onto a paragraph of a different style.</summary>
    public event EventHandler? CurrentStyleChanged;

    /// <summary>
    /// Raised when the word count or the page the caret is on may have changed — what a status bar
    /// listens to.
    /// </summary>
    public event EventHandler? StatisticsChanged;

    /// <summary>Raised when a hyperlink is activated (Ctrl+click) whose target is outside the document.</summary>
    public event EventHandler<DocumentLinkEventArgs>? LinkActivated;

    /// <summary>
    /// The name recorded on comments and tracked changes.
    /// </summary>
    /// <remarks>
    /// Defaults to the signed-in user's name where the platform has one. A host with a user of its own
    /// should set it — on the web it is otherwise whatever the browser sandbox reports.
    /// </remarks>
    public string Author { get; set; } = string.IsNullOrWhiteSpace(Environment.UserName) ? "Author" : Environment.UserName;

    /// <summary>The author's initials, for comment balloons.</summary>
    public string AuthorInitials => new string(this.Author
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => char.ToUpperInvariant(x[0]))
        .Take(3)
        .ToArray());

    // ---- tracked changes ----

    /// <summary>
    /// Whether edits are recorded as tracked changes. Stored in the document (<c>w:trackRevisions</c>),
    /// so it is still on when the file is reopened — here or in Word.
    /// </summary>
    public bool IsTrackingChanges
    {
        get => this.document.IsTrackingRevisions;
        set
        {
            if (value == this.document.IsTrackingRevisions || this.IsReadOnlyDocument)
                return;

            this.document.Execute(new SetTrackRevisionsCommand(value));
            this.AfterEdit();
        }
    }

    /// <summary>Every tracked change in the document.</summary>
    public IReadOnlyList<DocumentRevision> Revisions => this.document.Revisions;

    /// <summary>The tracked change under the caret, or null.</summary>
    public DocumentRevision? CurrentRevision
    {
        get
        {
            var caret = this.Selection.Focus;
            var range = this.Selection.Range;

            return this.document.Revisions.FirstOrDefault(x =>
                (x.Range.Start <= caret && x.Range.End >= caret) ||
                (!range.IsEmpty && x.Range.Start < range.End && x.Range.End > range.Start));
        }
    }

    /// <summary>Accepts the change at the caret (or in the selection) and moves to the next one.</summary>
    public void AcceptChange() => this.ResolveCurrent(accept: true);

    /// <summary>Rejects the change at the caret (or in the selection) and moves to the next one.</summary>
    public void RejectChange() => this.ResolveCurrent(accept: false);

    /// <summary>Accepts every tracked change in the document.</summary>
    public void AcceptAllChanges() => this.ResolveAll(accept: true);

    /// <summary>Rejects every tracked change in the document.</summary>
    public void RejectAllChanges() => this.ResolveAll(accept: false);

    void ResolveCurrent(bool accept)
    {
        if (this.IsReadOnlyDocument || this.CurrentRevision is not { } revision)
            return;

        var range = this.Selection.IsEmpty ? revision.Range : this.Selection.Range;
        this.document.Execute(new ResolveRevisionsCommand(range, accept));

        this.ClampSelection();
        this.AfterEdit();
        this.NextChange();
    }

    void ResolveAll(bool accept)
    {
        if (this.IsReadOnlyDocument || this.document.Revisions.Count == 0)
            return;

        this.document.Execute(new ResolveRevisionsCommand(null, accept));
        this.ClampSelection();
        this.AfterEdit();
    }

    /// <summary>Selects the next tracked change after the caret, wrapping. False when there are none.</summary>
    public bool NextChange() => this.StepTo(this.document.Revisions.Select(x => x.Range).ToList(), forward: true);

    /// <summary>Selects the previous tracked change, wrapping.</summary>
    public bool PreviousChange() => this.StepTo(this.document.Revisions.Select(x => x.Range).ToList(), forward: false);

    /// <summary>Selects the next or previous of a set of ranges relative to the caret, wrapping.</summary>
    bool StepTo(IReadOnlyList<DocumentRange> ranges, bool forward)
    {
        if (ranges.Count == 0)
            return false;

        var caret = forward ? this.Selection.Range.End : this.Selection.Range.Start;

        var target = forward
            ? ranges.FirstOrDefault(x => x.Start >= caret && !(x == this.Selection.Range), ranges[0])
            : ranges.LastOrDefault(x => x.End <= caret && !(x == this.Selection.Range), ranges[^1]);

        this.ClearObjectSelection();

        if (target.IsEmpty)
            this.Selection.MoveTo(target.Start);
        else
            this.Selection.Select(target.Start, target.End);

        this.ScrollCaretIntoView();
        this.RaiseChanged();
        return true;
    }

    int NextRevisionId()
    {
        if (this.nextRevisionId == 0)
            this.nextRevisionId = this.document.NextId(typeof(InsertedRun), typeof(DeletedRun));

        return this.nextRevisionId++;
    }

    /// <summary>Types text, as a tracked insertion when changes are being tracked.</summary>
    void ExecuteInsert(DocumentPosition at, string text)
    {
        if (this.document.IsTrackingRevisions)
            this.document.Execute(new TrackedInsertTextCommand(at, text, this.Author, DateTime.UtcNow, this.NextRevisionId()));
        else
            this.document.Execute(new InsertTextCommand(at, text));
    }

    // ---- comments ----

    /// <summary>Every comment, in the order its anchor appears.</summary>
    public IReadOnlyList<DocumentComment> Comments => this.document.Comments;

    /// <summary>The comment the caret is in, or null.</summary>
    public DocumentComment? CurrentComment
    {
        get
        {
            var caret = this.Selection.Focus;
            return this.document.Comments.FirstOrDefault(x => x.Range.Start <= caret && x.Range.End >= caret);
        }
    }

    /// <summary>
    /// Adds a comment to the selection — or, with nothing selected, to the word at the caret.
    /// </summary>
    public DocumentComment? AddComment(string text)
    {
        if (this.IsReadOnlyDocument || string.IsNullOrWhiteSpace(text))
            return null;

        var range = this.Selection.IsEmpty && this.WordRangeAt(this.Selection.Focus) is { IsEmpty: false } word
            ? word
            : this.Selection.Range;

        var id = this.document.NextId(typeof(Comment)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        this.document.Execute(new AddCommentCommand(range, id, this.Author, this.AuthorInitials, text, DateTime.UtcNow));
        this.AfterEdit();

        return this.document.Comments.FirstOrDefault(x => x.Id == id);
    }

    /// <summary>Deletes a comment — the one at the caret when no id is given.</summary>
    public void DeleteComment(string? id = null)
    {
        id ??= this.CurrentComment?.Id;
        if (this.IsReadOnlyDocument || id is null)
            return;

        this.document.Execute(new DeleteCommentCommand(id));
        this.ClampSelection();
        this.AfterEdit();
    }

    /// <summary>Deletes every comment in the document.</summary>
    public void DeleteAllComments()
    {
        if (this.IsReadOnlyDocument || this.document.Comments.Count == 0)
            return;

        this.document.Execute(new DeleteCommentCommand(null));
        this.ClampSelection();
        this.AfterEdit();
    }

    /// <summary>Selects the next comment's anchor, wrapping. False when there are none.</summary>
    public bool NextComment() => this.StepTo(this.document.Comments.Select(x => x.Range).ToList(), forward: true);

    /// <summary>Selects the previous comment's anchor, wrapping.</summary>
    public bool PreviousComment() => this.StepTo(this.document.Comments.Select(x => x.Range).ToList(), forward: false);

    /// <summary>
    /// Whether comment balloons are drawn in the margin. On by default; the anchors stay washed either way.
    /// </summary>
    public bool ShowComments
    {
        get => this.showComments;
        set
        {
            if (this.showComments == value)
                return;

            this.showComments = value;
            this.RaiseChanged();
        }
    }

    bool showComments = true;

    /// <summary>
    /// Where each comment's anchor and balloon go, in flow coordinates — for the painter.
    /// </summary>
    /// <remarks>
    /// Balloons are placed beside their anchor's line in the margin to the right of the text, pushed
    /// down where two would overlap, which is how Word's markup area stacks them. In reflow there is no
    /// margin wide enough, so only the anchors are marked.
    /// </remarks>
    public IReadOnlyList<DocumentCommentMark> CommentMarks()
    {
        var comments = this.document.Comments;
        if (comments.Count == 0)
            return [];

        var marks = new List<DocumentCommentMark>(comments.Count);
        var current = this.CurrentComment?.Id;
        var nextFree = double.NegativeInfinity;

        foreach (var comment in comments)
        {
            var anchor = this.RangeRects(comment.Range).ToList();
            if (anchor.Count == 0)
                anchor.Add(this.CaretRect(comment.Range.Start));

            GridRectLike? balloon = null;
            if (this.showComments && this.IsPaginated)
            {
                var top = Math.Max(anchor[0].Y, nextFree);
                var height = BalloonHeight(comment);
                balloon = new GridRectLike(this.ContentWidth + BalloonGap, top, BalloonWidth, height);
                nextFree = top + height + 6;
            }

            marks.Add(new DocumentCommentMark(comment, anchor, balloon, comment.Id == current));
        }

        return marks;
    }

    /// <summary>Width of a comment balloon, in layout units.</summary>
    public const double BalloonWidth = 170;

    /// <summary>Gap between the text column and the balloons.</summary>
    public const double BalloonGap = 24;

    static double BalloonHeight(DocumentComment comment)
    {
        // A rough fit: about thirty characters a line at the balloon's width, plus the author line.
        var lines = Math.Max(1, (int)Math.Ceiling(comment.Text.Length / 30d)) + comment.Text.Count(c => c == '\n');
        return 20 + (Math.Min(lines, 8) * 14);
    }

    // ---- statistics ----

    /// <summary>
    /// Word count, character count, paragraphs and pages — what the status bar and the Word Count dialog
    /// show. Cached until the next edit.
    /// </summary>
    public DocumentStatistics Statistics
    {
        get
        {
            var page = this.CurrentPageNumber();

            if (this.statistics is { } cached)
                return cached with { CurrentPage = page, SelectedWords = this.SelectedWordCount() };

            var words = 0;
            var withSpaces = 0;
            var withoutSpaces = 0;
            var paragraphs = 0;
            var lines = 0;

            foreach (var paragraph in this.Document.Paragraphs)
            {
                var text = paragraph.VisibleText;
                if (text.Length == 0)
                    continue;

                paragraphs++;
                words += DocumentStatistics.CountWords(text);
                withSpaces += text.Length;
                withoutSpaces += text.Count(c => !char.IsWhiteSpace(c));
            }

            foreach (var laidOut in this.StoryParagraphs)
                lines += laidOut.Lines.Count;

            this.statistics = new DocumentStatistics
            {
                Words = words,
                CharactersWithSpaces = withSpaces,
                CharactersNoSpaces = withoutSpaces,
                Paragraphs = paragraphs,
                Lines = lines,
                Pages = this.IsPaginated ? this.Pagination.Count : this.EstimatePages()
            };

            return this.statistics with { CurrentPage = page, SelectedWords = this.SelectedWordCount() };
        }
    }

    int SelectedWordCount()
    {
        if (this.Selection.IsEmpty)
            return 0;

        var range = this.Selection.Range;
        var count = 0;

        for (var block = range.Start.Block; block <= range.End.Block; block++)
        {
            var text = this.TextOf(block);
            var from = block == range.Start.Block ? Math.Min(range.Start.Offset, text.Length) : 0;
            var to = block == range.End.Block ? Math.Min(range.End.Offset, text.Length) : text.Length;
            count += DocumentStatistics.CountWords(text[from..Math.Max(from, to)]);
        }

        return count;
    }

    int CurrentPageNumber()
    {
        if (!this.IsPaginated)
            return 1;

        return this.PageNumberAt(this.CaretRect(this.Selection.Focus).Y);
    }

    /// <summary>
    /// How many pages the document would print on, while it is being shown as a continuous column.
    /// </summary>
    int EstimatePages()
    {
        var engine = new DocumentLayoutEngine(this.Measurer);
        var laidOut = engine.Layout(this.Document.Blocks, this.Document.Page.ContentWidth);
        return DocumentPagination.Paginate(laidOut.Blocks, laidOut.Height, this.Document.Page, this.PageGap).Count;
    }

    void InvalidateStatistics()
    {
        this.statistics = null;
        this.statisticsPage = -1;
        this.StatisticsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raises <see cref="StatisticsChanged"/> when the caret has moved onto a different page.</summary>
    void NotifyPageIfMoved()
    {
        if (this.StatisticsChanged is null)
            return;

        var page = this.CurrentPageNumber();
        if (page == this.statisticsPage)
            return;

        this.statisticsPage = page;
        this.StatisticsChanged?.Invoke(this, EventArgs.Empty);
    }
}


/// <summary>A comment's anchor and balloon, placed for painting.</summary>
/// <param name="Comment">The comment.</param>
/// <param name="Anchor">The rectangles its commented text covers, one per line, in flow coordinates.</param>
/// <param name="Balloon">
/// Where its balloon goes, with <c>X</c> measured from the content's left edge, or null when balloons
/// are not drawn.
/// </param>
/// <param name="IsCurrent">True for the comment the caret is in, which is drawn emphasised.</param>
public sealed record DocumentCommentMark(DocumentComment Comment, IReadOnlyList<GridRectLike> Anchor, GridRectLike? Balloon, bool IsCurrent);


/// <summary>A hyperlink being followed.</summary>
public sealed class DocumentLinkEventArgs(DocumentHyperlink link) : EventArgs
{
    public DocumentHyperlink Link { get; } = link;

    /// <summary>The address to open.</summary>
    public string Target => this.Link.Target;
}
