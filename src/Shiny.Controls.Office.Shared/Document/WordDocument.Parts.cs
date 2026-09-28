using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Document;

/// <summary>
/// The parts of a document beyond the body: comments, footnotes, styles and settings.
/// </summary>
/// <remarks>
/// <para>
/// What these expose — comment anchors, revision spans, bookmark positions — is worked out from the
/// body on demand and cached until the next edit, rather than recomputed on every keystroke. A caret
/// moving does not need any of it; a balloon being painted does.
/// </para>
/// <para>
/// A part this code writes to is recorded as dirty and saved by name on flush. <c>AutoSave</c> is off
/// (so that merely opening a document does not rewrite it), which means nothing else would ever
/// serialise a part created or changed here.
/// </para>
/// </remarks>
public sealed partial class WordDocument
{
    readonly HashSet<OpenXmlPartRootElement> dirtyRoots = new(ReferenceEqualityComparer.Instance);
    Dictionary<int, int> footnoteNumbers = new();
    ReviewSnapshot? review;

    /// <summary>The style resolver, for commands that add styles and for the style gallery.</summary>
    internal WordStyleResolver StyleResolver => this.styles;

    /// <summary>The body reader, for projecting notes and header content.</summary>
    internal WordBodyReader Reader => this.reader;

    /// <summary>Marks a part's root as needing to be written on the next save.</summary>
    internal void MarkPartDirty(OpenXmlPartRootElement? root)
    {
        if (root is null)
            return;

        this.dirtyRoots.Add(root);
        this.contentChanged = true;
        this.MarkDirty();
    }

    void SaveAuxiliaryParts()
    {
        foreach (var root in this.dirtyRoots)
        {
            try
            {
                root.Save();
            }
            catch (Exception)
            {
                // A part removed after it was marked (a comment part emptied and deleted, say) has
                // nothing left to save; the package no longer references it.
            }
        }

        this.dirtyRoots.Clear();
    }

    /// <summary>Drops the cached review data so it is rebuilt from the body on next read.</summary>
    void ReadReviewParts() => this.review = null;

    /// <summary>
    /// Keeps footnote numbers in order of citation.
    /// </summary>
    /// <remarks>
    /// A footnote is numbered by where it is cited, not by its id. Inserting one before an existing
    /// reference renumbers every note after it, and the paragraphs that cite those notes have to be
    /// re-read to draw the new numbers — which is what happens here when the order changed.
    /// </remarks>
    void RefreshNoteNumbers(bool force)
    {
        if (this.body is null)
            return;

        if (!force && this.Main?.FootnotesPart is null)
            return;

        var numbers = new Dictionary<int, int>();
        foreach (var reference in this.body.Descendants<FootnoteReference>())
        {
            var id = (int)(reference.Id?.Value ?? 0);
            if (!numbers.ContainsKey(id))
                numbers[id] = numbers.Count + 1;
        }

        var endnotes = new Dictionary<int, int>();
        foreach (var reference in this.body.Descendants<EndnoteReference>())
        {
            var id = (int)(reference.Id?.Value ?? 0);
            if (!endnotes.ContainsKey(id))
                endnotes[id] = endnotes.Count + 1;
        }

        var changed = !numbers.OrderBy(x => x.Key).SequenceEqual(this.footnoteNumbers.OrderBy(x => x.Key));

        this.footnoteNumbers = numbers;
        this.reader.FootnoteNumbers = numbers;
        this.reader.EndnoteNumbers = endnotes;

        if (force || !changed)
            return;

        for (var top = 0; top < this.blocks.Count; top++)
        {
            if (this.BlockElementAt(top) is { } element && element.Descendants<FootnoteReference>().Any())
                this.blocks[top] = this.reader.RereadBlock(element);
        }
    }

    // ---- review data ----

    /// <summary>Every comment, in the order its anchor appears in the document.</summary>
    public IReadOnlyList<DocumentComment> Comments => this.Review.Comments;

    /// <summary>Every tracked insertion and deletion, in document order.</summary>
    public IReadOnlyList<DocumentRevision> Revisions => this.Review.Revisions;

    /// <summary>Every footnote, numbered in order of citation.</summary>
    public IReadOnlyList<DocumentNote> Footnotes => this.Review.Footnotes;

    /// <summary>Every bookmark, hidden ones included.</summary>
    public IReadOnlyList<DocumentBookmark> Bookmarks => this.Review.Bookmarks;

    /// <summary>Every hyperlink in the body, in document order.</summary>
    public IReadOnlyList<DocumentHyperlink> Hyperlinks => this.Review.Hyperlinks;

    ReviewSnapshot Review => this.review ??= this.BuildReview();

    sealed record ReviewSnapshot(
        IReadOnlyList<DocumentComment> Comments,
        IReadOnlyList<DocumentRevision> Revisions,
        IReadOnlyList<DocumentNote> Footnotes,
        IReadOnlyList<DocumentBookmark> Bookmarks,
        IReadOnlyList<DocumentHyperlink> Hyperlinks);

    ReviewSnapshot BuildReview()
    {
        var starts = new Dictionary<string, DocumentPosition>();
        var ends = new Dictionary<string, DocumentPosition>();
        var references = new Dictionary<string, DocumentPosition>();
        var revisions = new List<DocumentRevision>();
        var bookmarks = new List<DocumentBookmark>();
        var hyperlinks = new List<DocumentHyperlink>();
        var noteReferences = new Dictionary<int, DocumentPosition>();

        for (var index = 0; index < this.story.Count; index++)
        {
            if (this.story[index].Element is not { } paragraph)
                continue;

            foreach (var node in paragraph.Descendants())
            {
                switch (node)
                {
                    case CommentRangeStart start when start.Id?.Value is { } id:
                        starts.TryAdd(id, Position(index, paragraph, node));
                        break;

                    case CommentRangeEnd end when end.Id?.Value is { } id:
                        ends.TryAdd(id, Position(index, paragraph, node));
                        break;

                    case CommentReference reference when reference.Id?.Value is { } id:
                        references.TryAdd(id, Position(index, paragraph, node.Parent ?? node));
                        break;

                    case InsertedRun or DeletedRun:
                        var revisionStart = Position(index, paragraph, node);
                        var length = WordParagraphEditor.LengthOf(node);
                        revisions.Add(new DocumentRevision(
                            node is InsertedRun ? RevisionKind.Insertion : RevisionKind.Deletion,
                            OoxmlUnits.Attribute(node, "author") ?? string.Empty,
                            ParseDate(OoxmlUnits.Attribute(node, "date")),
                            new DocumentRange(revisionStart, revisionStart with { Offset = revisionStart.Offset + length }))
                        {
                            Element = node
                        });

                        break;

                    case BookmarkStart bookmark when bookmark.Name?.Value is { Length: > 0 } name:
                        bookmarks.Add(new DocumentBookmark(name, Position(index, paragraph, node)));
                        break;

                    case Hyperlink link:
                        var at = Position(index, paragraph, node);
                        var span = WordParagraphEditor.LengthOf(node);
                        var target = link.Anchor?.Value is { } anchor
                            ? "#" + anchor
                            : this.HyperlinkTarget(link.Id?.Value) ?? string.Empty;

                        hyperlinks.Add(new DocumentHyperlink(
                            target,
                            string.Concat(WordParagraphEditor.RunsIn(link).SelectMany(r => r.Elements<DocumentFormat.OpenXml.Wordprocessing.Text>()).Select(t => t.Text)),
                            new DocumentRange(at, at with { Offset = at.Offset + span })));

                        break;

                    case FootnoteReference note:
                        noteReferences.TryAdd((int)(note.Id?.Value ?? 0), Position(index, paragraph, node));
                        break;
                }
            }
        }

        var comments = new List<DocumentComment>();
        if (this.Main?.WordprocessingCommentsPart?.Comments is { } part)
        {
            foreach (var comment in part.Elements<Comment>())
            {
                if (comment.Id?.Value is not { } id)
                    continue;

                var anchor = starts.TryGetValue(id, out var s) ? s : references.TryGetValue(id, out var r) ? r : (DocumentPosition?)null;
                if (anchor is not { } startAt)
                    continue;

                var endAt = ends.TryGetValue(id, out var e) ? e : startAt;
                var text = string.Join(
                    Environment.NewLine,
                    comment.Elements<Paragraph>().Select(p => string.Concat(p.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text))));

                comments.Add(new DocumentComment(
                    id,
                    comment.Author?.Value ?? string.Empty,
                    comment.Initials?.Value,
                    comment.Date?.Value,
                    text,
                    new DocumentRange(startAt, endAt, true)));
            }
        }

        comments.Sort((a, b) => a.Range.Start.CompareTo(b.Range.Start));

        var notes = new List<DocumentNote>();
        if (this.Main?.FootnotesPart?.Footnotes is { } footnotes)
        {
            foreach (var footnote in footnotes.Elements<Footnote>())
            {
                var id = (int)(footnote.Id?.Value ?? 0);

                // Separators are ids -1 and 0 (or typed); only real notes are cited from the body.
                if (!this.footnoteNumbers.TryGetValue(id, out var number) || !noteReferences.TryGetValue(id, out var cited))
                    continue;

                notes.Add(new DocumentNote(id, number, NumberedNote(this.reader.ReadContainer(footnote), number), cited));
            }
        }

        notes.Sort((a, b) => a.Number.CompareTo(b.Number));

        return new ReviewSnapshot(comments, revisions, notes, bookmarks, hyperlinks);
    }

    static DocumentPosition Position(int index, Paragraph paragraph, OpenXmlElement node)
        => new(index, WordParagraphEditor.OffsetOf(paragraph, node));

    static DateTime? ParseDate(string? value)
        => DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var date)
            ? date
            : null;

    /// <summary>A footnote's paragraphs with its number drawn at the front of the first one.</summary>
    static IReadOnlyList<DocumentBlock> NumberedNote(IReadOnlyList<DocumentBlock> blocks, int number)
    {
        if (blocks.Count == 0 || blocks[0] is not DocumentParagraph first)
            return blocks;

        var style = first.Runs.FirstOrDefault(x => !x.IsBreak)?.Style ?? TextStyle.Default;
        var mark = new StyledRun(number.ToString(System.Globalization.CultureInfo.InvariantCulture), style with { BaselineShift = 0.33, SizeScale = 0.65 });

        var rebuilt = new List<DocumentBlock>(blocks) { [0] = first with { Runs = [mark, .. first.Runs] } };
        return rebuilt;
    }

    string? HyperlinkTarget(string? relationshipId)
    {
        if (relationshipId is null || this.Main is not { } main)
            return null;

        try
        {
            return main.HyperlinkRelationships.FirstOrDefault(x => x.Id == relationshipId)?.Uri?.ToString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---- settings, background and watermark ----

    /// <summary>
    /// True when the document is set to record changes (<c>w:trackRevisions</c> in its settings).
    /// </summary>
    public bool IsTrackingRevisions
        => this.Main?.DocumentSettingsPart?.Settings?.GetFirstChild<TrackRevisions>() is { } track
           && (track.Val is null || track.Val.Value);

    /// <summary>The page colour (<c>w:background</c>), or null for plain paper.</summary>
    public ArgbColor? PageColor
        => this.Main?.Document?.DocumentBackground?.Color?.Value is { } hex
           && !hex.Equals("auto", StringComparison.OrdinalIgnoreCase)
           && WordStyleResolver.TryParseHex(hex, out var color)
            ? color
            : null;

    /// <summary>
    /// The text of a Word watermark in the default header, or null when there is none.
    /// </summary>
    /// <remarks>
    /// Word stores a text watermark as a VML WordArt shape in the header, which is how every version
    /// since 2003 reads and writes one. Only the text is taken; the colour and rotation are Word's
    /// fixed defaults for the "DRAFT" family of marks, which is also what the painter draws.
    /// </remarks>
    public string? WatermarkText
    {
        get
        {
            if (this.Main is not { } main)
                return null;

            foreach (var header in main.HeaderParts)
            {
                var root = header.Header;
                if (root is null)
                    continue;

                foreach (var element in root.Descendants())
                {
                    if (element.LocalName == "textpath" && OoxmlUnits.Attribute(element, "string") is { Length: > 0 } text)
                        return text;
                }
            }

            return null;
        }
    }

    /// <summary>The body element, for commands that append at the end.</summary>
    internal Body? BodyElement => this.body;

    /// <summary>The settings part's root, created with the part when the document has none.</summary>
    internal Settings? EnsureSettings()
    {
        if (this.Main is not { } main)
            return null;

        var part = main.DocumentSettingsPart ?? main.AddNewPart<DocumentSettingsPart>();
        part.Settings ??= new Settings();
        return part.Settings;
    }

    /// <summary>Re-reads styles after one was added, and every block with them.</summary>
    internal void ReloadStyles()
    {
        if (this.Main is not { } main)
            return;

        this.styles.Reload(main);

        for (var top = 0; top < this.blocks.Count; top++)
            this.RereadTop(top);

        this.MarkChanged();
    }

    /// <summary>Re-reads every top-level block — after a change that can alter how any of them reads.</summary>
    internal void RereadAll()
    {
        for (var top = 0; top < this.blocks.Count; top++)
            this.RereadTop(top);

        this.MarkChanged();
    }
}
