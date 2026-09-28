namespace Shiny.Controls.Office.Document;

public sealed partial class DocumentEditorController
{
    /// <summary>
    /// Selects a comment's anchored text and scrolls it into view — what clicking a comment in a
    /// comments pane does. False when there is no comment with that id.
    /// </summary>
    public bool GoToComment(string id)
    {
        var comment = this.document.Comments.FirstOrDefault(x => x.Id == id);
        if (comment is null)
            return false;

        this.ClearObjectSelection();

        if (comment.Range.IsEmpty)
            this.Selection.MoveTo(comment.Range.Start);
        else
            this.Selection.Select(comment.Range.Start, comment.Range.End);

        this.ScrollCaretIntoView();
        this.RaiseChanged();
        return true;
    }

    /// <summary>The text a comment is anchored to, trimmed to <paramref name="max"/> characters.</summary>
    public string CommentedText(DocumentComment comment, int max = 80)
    {
        ArgumentNullException.ThrowIfNull(comment);

        var range = comment.Range;
        var paragraphs = this.document.Paragraphs;
        if (range.IsEmpty || range.Start.Block >= paragraphs.Count)
            return string.Empty;

        var parts = new List<string>();
        for (var block = range.Start.Block; block <= Math.Min(range.End.Block, paragraphs.Count - 1); block++)
        {
            var text = paragraphs[block].PlainText;
            var from = block == range.Start.Block ? Math.Min(range.Start.Offset, text.Length) : 0;
            var to = block == range.End.Block ? Math.Min(range.End.Offset, text.Length) : text.Length;
            if (to > from)
                parts.Add(text[from..to]);
        }

        var joined = string.Join(" ", parts).Replace("￼", string.Empty).Trim();
        return joined.Length > max ? joined[..max].TrimEnd() + "…" : joined;
    }
}
