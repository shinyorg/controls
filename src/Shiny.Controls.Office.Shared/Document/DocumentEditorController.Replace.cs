using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Document;

/// <summary>Find and Replace (Ctrl+H).</summary>
public sealed partial class DocumentEditorController
{
    /// <summary>
    /// Replaces the match the selection is on, then steps to the next one — Word's Replace button.
    /// </summary>
    /// <returns>
    /// True when a match was replaced. When the selection is not on a match nothing is replaced and the
    /// find steps to the next one, which is also what Word does on the first press.
    /// </returns>
    public bool ReplaceCurrent(string replacement)
    {
        if (this.IsReadOnlyDocument || !this.Find.IsSearching)
            return false;

        replacement ??= string.Empty;

        var selection = this.Selection.Range;
        var hit = this.Find.Matches.Cast<DocumentFindMatch?>().FirstOrDefault(x => x!.Value.Range == selection);

        if (hit is not { } match)
        {
            this.Find.FindNext();
            return false;
        }

        using (this.document.Undo.BeginTransaction("Replace"))
            this.ReplaceRange(match.Range, replacement);

        this.Selection.MoveTo(match.Range.Start with { Offset = match.Range.Start.Offset + replacement.Length });
        this.AfterEdit();

        // The match list was dropped by the edit; the next step re-collects it from the caret, which now
        // sits after the replacement - so the text just written is never matched again.
        this.Find.FindNext();
        return true;
    }

    /// <summary>
    /// Replaces every match of the current find query. One undo step for the lot.
    /// </summary>
    /// <returns>How many were replaced.</returns>
    public int ReplaceAll(string replacement)
        => this.Find.IsSearching ? this.ReplaceAll(this.Find.Query, replacement, this.Find.Options) : 0;

    /// <summary>Replaces every occurrence of <paramref name="query"/>. One undo step for the lot.</summary>
    /// <returns>How many were replaced.</returns>
    public int ReplaceAll(string query, string replacement, FindOptions? options = null)
    {
        if (this.IsReadOnlyDocument || string.IsNullOrEmpty(query))
            return 0;

        replacement ??= string.Empty;
        options ??= FindOptions.Default;

        var matches = new List<DocumentRange>();
        var paragraphs = this.Document.Paragraphs;

        for (var i = 0; i < paragraphs.Count; i++)
        {
            foreach (var match in TextSearch.Matches(paragraphs[i].PlainText, query, options))
                matches.Add(new DocumentRange(new DocumentPosition(i, match.Start), new DocumentPosition(i, match.End)));
        }

        if (matches.Count == 0)
            return 0;

        // Last to first, so each replacement leaves the offsets of the ones still to do where they were.
        using (this.document.Undo.BeginTransaction("Replace All"))
        {
            for (var i = matches.Count - 1; i >= 0; i--)
                this.ReplaceRange(matches[i], replacement);
        }

        this.ClampSelection();
        this.AfterEdit();
        return matches.Count;
    }

    /// <summary>
    /// Swaps a range's text for another, keeping the formatting it had.
    /// </summary>
    /// <remarks>
    /// The new text goes in at the end of the old before the old is removed, so it adopts the formatting
    /// of the last character it replaces — inserting first at the start would take the formatting of
    /// whatever preceded the match instead.
    /// </remarks>
    void ReplaceRange(DocumentRange range, string replacement)
    {
        if (replacement.Length > 0)
            this.ExecuteInsert(range.End, replacement);

        if (this.document.IsTrackingRevisions)
            this.document.Execute(new TrackedDeleteCommand(range, this.Author, DateTime.UtcNow, this.NextRevisionId()));
        else
            this.document.Execute(new DeleteRangeCommand(range));
    }
}
