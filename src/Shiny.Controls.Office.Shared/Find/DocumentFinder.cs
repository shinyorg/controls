using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Document;

/// <summary>One hit in a document: which block it is in, and where in that block's text.</summary>
/// <remarks>
/// Offsets are into the paragraph's concatenated text, the same space <see cref="DocumentPosition"/>
/// uses — so a match converts to a selection without knowing anything about runs.
/// </remarks>
public readonly record struct DocumentFindMatch(int Block, int Start, int Length)
{
    public int End => this.Start + this.Length;

    /// <summary>The match as a selectable range.</summary>
    public DocumentRange Range => new(new DocumentPosition(this.Block, this.Start), new DocumentPosition(this.Block, this.End));
}


/// <summary>
/// Finds text in a <see cref="WordDocument"/> and drives the editor's selection onto each hit.
/// </summary>
/// <remarks>
/// <para>
/// Walks <see cref="WordDocument.Paragraphs"/> — the story, every paragraph in reading order,
/// including the ones inside table cells — which is the same index space the caret moves through, so
/// every hit counted is one the arrows can step to and select.
/// </para>
/// <para>
/// A hit is <em>selected</em> rather than merely scrolled to. Everything a person does after finding a
/// word — replace it, restyle it, delete it — operates on the word, not on a caret sitting beside it.
/// </para>
/// </remarks>
public sealed class DocumentFinder(DocumentEditorController controller) : FindController<DocumentFindMatch>
{
    /// <inheritdoc/>
    protected override IReadOnlyList<DocumentFindMatch> Collect(string query, FindOptions options)
    {
        var blocks = controller.Document.Paragraphs;
        var results = new List<DocumentFindMatch>();

        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is not { } paragraph)
                continue;

            foreach (var match in TextSearch.Matches(paragraph.PlainText, query, options))
                results.Add(new DocumentFindMatch(i, match.Start, match.Length));
        }

        return results;
    }

    /// <inheritdoc/>
    protected override void MoveTo(DocumentFindMatch match)
        => controller.SelectFindMatch(match);

    /// <inheritdoc/>
    protected override int IndexAtOrAfterCaret(IReadOnlyList<DocumentFindMatch> matches)
    {
        // The start of the selection rather than the focus: a hit is left selected, so measuring from
        // the focus would put the caret at the match's end and make "next" skip the one it is on.
        var caret = controller.Selection.Range.Start;

        for (var i = 0; i < matches.Count; i++)
        {
            var start = new DocumentPosition(matches[i].Block, matches[i].Start);
            if (start >= caret)
                return i;
        }

        return matches.Count;
    }
}
