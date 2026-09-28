using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Shiny.Controls.Office.Document;

/// <summary>A part outside the body that an edit can change and undo has to put back.</summary>
public enum DocumentPartKind
{
    Comments,
    Footnotes,
    Settings,

    /// <summary>The <c>w:document</c> element's own children other than the body — its background.</summary>
    Background
}


/// <summary>
/// Whole-part snapshots, for the edits that reach outside the body.
/// </summary>
/// <remarks>
/// A comment is two anchors in the body and an entry in <c>comments.xml</c>; a footnote is a reference
/// in the body and a note in <c>footnotes.xml</c>. Undo has to put both halves back together, so
/// those commands capture the part along with the paragraphs, and restore both.
/// </remarks>
public sealed partial class WordDocument
{
    /// <summary>A deep copy of a part's root, or null when the document does not have the part.</summary>
    internal OpenXmlElement? CapturePart(DocumentPartKind kind) => kind switch
    {
        DocumentPartKind.Comments => this.Main?.WordprocessingCommentsPart?.Comments?.CloneNode(true),
        DocumentPartKind.Footnotes => this.Main?.FootnotesPart?.Footnotes?.CloneNode(true),
        DocumentPartKind.Settings => this.Main?.DocumentSettingsPart?.Settings?.CloneNode(true),
        DocumentPartKind.Background => this.Main?.Document?.DocumentBackground?.CloneNode(true),
        _ => null
    };

    /// <summary>Puts a part back to a captured state; null takes it back to not existing.</summary>
    internal void RestorePart(DocumentPartKind kind, OpenXmlElement? snapshot)
    {
        if (this.Main is not { } main)
            return;

        switch (kind)
        {
            case DocumentPartKind.Comments:
                if (snapshot is null)
                {
                    if (main.WordprocessingCommentsPart is { } comments)
                        main.DeletePart(comments);
                }
                else
                {
                    var part = main.WordprocessingCommentsPart ?? main.AddNewPart<WordprocessingCommentsPart>();
                    part.Comments = (Comments)snapshot.CloneNode(true);
                    this.MarkPartDirty(part.Comments);
                }

                break;

            case DocumentPartKind.Footnotes:
                if (snapshot is null)
                {
                    if (main.FootnotesPart is { } footnotes)
                        main.DeletePart(footnotes);
                }
                else
                {
                    var part = main.FootnotesPart ?? main.AddNewPart<FootnotesPart>();
                    part.Footnotes = (Footnotes)snapshot.CloneNode(true);
                    this.MarkPartDirty(part.Footnotes);
                }

                break;

            case DocumentPartKind.Settings:
                if (snapshot is not null)
                {
                    var part = main.DocumentSettingsPart ?? main.AddNewPart<DocumentSettingsPart>();
                    part.Settings = (Settings)snapshot.CloneNode(true);
                    this.MarkPartDirty(part.Settings);
                }

                break;

            case DocumentPartKind.Background:
                if (main.Document is { } document)
                {
                    document.DocumentBackground?.Remove();

                    // w:background is the document's first child, ahead of the body.
                    if (snapshot is not null)
                        document.PrependChild((DocumentBackground)snapshot.CloneNode(true));
                }

                break;
        }

        this.MarkChanged();
    }

    /// <summary>
    /// The whole body, cloned — for edits whose reach is the whole document.
    /// </summary>
    /// <remarks>
    /// The body element itself rather than its blocks: content controls project their paragraphs as
    /// separate blocks, and a restore that put blocks back one by one would bring them back outside
    /// the control they came from.
    /// </remarks>
    internal RestoreBodyCommand CaptureAll() => new(this.body?.CloneNode(true));

    /// <summary>Replaces the body's content with a captured copy and re-reads everything.</summary>
    internal void RestoreBody(OpenXmlElement snapshot)
    {
        if (this.body is null)
            return;

        this.body.RemoveAllChildren();
        foreach (var child in snapshot.ChildElements)
            this.body.AppendChild(child.CloneNode(true));

        this.RereadAllFromBody();
    }

    /// <summary>Rebuilds the whole projection from the body, after an edit that added or removed blocks.</summary>
    internal void RereadAllFromBody()
    {
        this.blocks.Clear();
        this.blocks.AddRange(this.reader.ReadBody(this.body));

        // The section properties may be a different element now, and they are what the page is read from.
        this.RereadHeadersFooters();
        this.MarkChanged();
    }

    /// <summary>Raises the change notifications after an edit that touched no block.</summary>
    internal void NotifyChanged() => this.MarkChanged();

    /// <summary>A new id no existing element of the given kinds uses.</summary>
    internal int NextId(params Type[] kinds)
    {
        var max = 0;

        IEnumerable<OpenXmlElement> Scan()
        {
            if (this.body is not null)
            {
                foreach (var element in this.body.Descendants())
                    yield return element;
            }

            if (this.Main?.WordprocessingCommentsPart?.Comments is { } comments)
            {
                foreach (var element in comments.Elements())
                    yield return element;
            }

            if (this.Main?.FootnotesPart?.Footnotes is { } footnotes)
            {
                foreach (var element in footnotes.Elements())
                    yield return element;
            }
        }

        foreach (var element in Scan())
        {
            if (!kinds.Contains(element.GetType()))
                continue;

            if (int.TryParse(OoxmlUnits.Attribute(element, "id"), out var id) && id > max)
                max = id;
        }

        return max + 1;
    }

    /// <summary>
    /// Replaces the projection of one top-level block with re-reads of the body elements now occupying
    /// its place — after an edit that turned one paragraph into several.
    /// </summary>
    internal void ResyncTop(int top, IReadOnlyList<OpenXmlElement> elements)
    {
        if (top < 0 || top > this.blocks.Count)
            return;

        if (top < this.blocks.Count)
            this.blocks.RemoveAt(top);

        for (var i = 0; i < elements.Count; i++)
            this.blocks.Insert(top + i, this.reader.RereadBlock(elements[i]));

        this.MarkChanged();
    }

    /// <summary>Makes sure a built-in style exists, reloading the resolver when one was added.</summary>
    /// <remarks>
    /// Deliberately outside the undo stack. Creating a style is idempotent and harmless to keep — Word
    /// keeps a style you applied and then undid too — and undoing it would re-read every block.
    /// </remarks>
    internal void EnsureStyle(string styleId)
    {
        if (this.Main is not { } main || this.styles.Has(styleId))
            return;

        if (!WordBuiltInStyles.Ensure(main, styleId))
            return;

        this.MarkPartDirty(main.StyleDefinitionsPart?.Styles);
        this.styles.Reload(main);
    }
}


/// <summary>Puts the whole body back to a captured state. The inverse of the document-wide edits.</summary>
public sealed record RestoreBodyCommand(OpenXmlElement? Snapshot) : DocumentCommand
{
    public override string Name => "Undo";

    public override Shiny.Controls.Office.Editing.IEditCommand<WordDocument> Apply(WordDocument context)
    {
        if (this.Snapshot is null)
            return new NoOpCommand();

        var current = context.CaptureAll();
        context.RestoreBody(this.Snapshot);
        return current;
    }
}
