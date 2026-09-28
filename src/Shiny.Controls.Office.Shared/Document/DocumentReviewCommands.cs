using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Shiny.Controls.Office.Editing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Shiny.Controls.Office.Document;

/// <summary>Puts a part outside the body back to a captured state. The undo half of the review edits.</summary>
public sealed record RestorePartCommand(DocumentPartKind Part, OpenXmlElement? Snapshot) : DocumentCommand
{
    public override string Name => "Undo";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var current = context.CapturePart(this.Part);
        context.RestorePart(this.Part, this.Snapshot);
        return new RestorePartCommand(this.Part, current);
    }
}


/// <summary>
/// Adds a comment anchored to a range: <c>w:commentRangeStart</c>, <c>w:commentRangeEnd</c>, a
/// reference run, and the comment itself in <c>comments.xml</c>.
/// </summary>
/// <remarks>
/// The markers are zero-width, so inserting them moves no offsets — which is why the end is placed
/// first and the start second without any adjustment in between, and why the range still means the
/// same text afterwards.
/// </remarks>
public sealed record AddCommentCommand(DocumentRange Range, string Id, string Author, string Initials, string Text, DateTime Date) : DocumentCommand
{
    public override string Name => "New Comment";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var first = context.ParagraphElementAt(this.Range.Start.Block);
        var last = context.ParagraphElementAt(this.Range.End.Block);

        if (first is null || last is null || context.Main is not { } main)
            return new NoOpCommand();

        var blocks = context.CaptureRange(this.Range);
        var part = context.CapturePart(DocumentPartKind.Comments);

        context.EnsureStyle("CommentText");
        context.EnsureStyle("CommentReference");

        var commentsPart = main.WordprocessingCommentsPart ?? main.AddNewPart<DocumentFormat.OpenXml.Packaging.WordprocessingCommentsPart>();
        commentsPart.Comments ??= new Comments();

        var comment = new Comment
        {
            Id = this.Id,
            Author = this.Author,
            Initials = this.Initials,
            Date = this.Date
        };

        foreach (var (line, index) in this.Text.Replace("\r\n", "\n").Split('\n').Select((x, i) => (x, i)))
        {
            var paragraph = new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "CommentText" }));

            // The annotation mark opens the comment's first paragraph; Word draws the reviewer's
            // initials there in its own balloons.
            if (index == 0)
                paragraph.AppendChild(new Run(new RunProperties(new RunStyle { Val = "CommentReference" }), new AnnotationReferenceMark()));

            paragraph.AppendChild(new Run(new W.Text(line) { Space = SpaceProcessingModeValues.Preserve }));
            comment.AppendChild(paragraph);
        }

        commentsPart.Comments.AppendChild(comment);
        context.MarkPartDirty(commentsPart.Comments);

        var end = new CommentRangeEnd { Id = this.Id };
        WordParagraphEditor.InsertObject(last, this.Range.End.Offset, end);
        end.InsertAfterSelf(new Run(
            new RunProperties(new RunStyle { Val = "CommentReference" }),
            new CommentReference { Id = this.Id }));

        WordParagraphEditor.InsertObject(first, this.Range.Start.Offset, new CommentRangeStart { Id = this.Id });

        context.ReprojectTop(context.TopOf(this.Range.Start.Block));
        if (context.TopOf(this.Range.End.Block) != context.TopOf(this.Range.Start.Block))
            context.ReprojectTop(context.TopOf(this.Range.End.Block));

        return new CompositeCommand<WordDocument>(this.Name, [new RestorePartCommand(DocumentPartKind.Comments, part), blocks]);
    }
}


/// <summary>Removes one comment, or every comment when <see cref="Id"/> is null.</summary>
public sealed record DeleteCommentCommand(string? Id) : DocumentCommand
{
    public override string Name => this.Id is null ? "Delete All Comments" : "Delete Comment";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        if (context.BodyElement is not { } body)
            return new NoOpCommand();

        var blocks = context.CaptureAll();
        var part = context.CapturePart(DocumentPartKind.Comments);

        bool Matches(string? id) => this.Id is null || id == this.Id;

        foreach (var element in body.Descendants<CommentRangeStart>().Where(x => Matches(x.Id?.Value)).ToList())
            element.Remove();

        foreach (var element in body.Descendants<CommentRangeEnd>().Where(x => Matches(x.Id?.Value)).ToList())
            element.Remove();

        foreach (var reference in body.Descendants<CommentReference>().Where(x => Matches(x.Id?.Value)).ToList())
        {
            // The reference lives in a run of its own; the run goes with it.
            if (reference.Parent is Run run && run.ChildElements.All(x => x is RunProperties or CommentReference))
                run.Remove();
            else
                reference.Remove();
        }

        if (context.Main?.WordprocessingCommentsPart?.Comments is { } comments)
        {
            foreach (var comment in comments.Elements<Comment>().Where(x => Matches(x.Id?.Value)).ToList())
                comment.Remove();

            context.MarkPartDirty(comments);
        }

        context.RereadAll();

        return new CompositeCommand<WordDocument>(this.Name,
        [
            new RestorePartCommand(DocumentPartKind.Comments, part),
            blocks
        ]);
    }
}


/// <summary>Turns recording of changes on or off, persisted as <c>w:trackRevisions</c> in the settings.</summary>
public sealed record SetTrackRevisionsCommand(bool On) : DocumentCommand
{
    public override string Name => this.On ? "Track Changes" : "Stop Tracking Changes";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var previous = context.CapturePart(DocumentPartKind.Settings);

        if (context.EnsureSettings() is not { } settings)
            return new NoOpCommand();

        settings.RemoveAllChildren<TrackRevisions>();

        if (this.On)
            WordSettingsOrder.Insert(settings, new TrackRevisions());

        context.MarkPartDirty(settings);
        context.NotifyChanged();

        return new RestorePartCommand(DocumentPartKind.Settings, previous);
    }
}


/// <summary>
/// Types text as a tracked insertion: the text lands inside a <c>w:ins</c> carrying the author and date.
/// </summary>
/// <remarks>
/// Consecutive keystrokes by the same author at the same place grow one <c>w:ins</c> rather than
/// stacking a new one per character, both in the XML — which is what Word writes — and on the undo stack,
/// where the typing run coalesces into one step.
/// </remarks>
public sealed record TrackedInsertTextCommand(DocumentPosition At, string Text, string Author, DateTime Date, int RevisionId)
    : DocumentCommand, IMergeableCommand<WordDocument>
{
    public override string Name => "Typing";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var paragraph = context.ParagraphElementAt(this.At.Block);
        if (paragraph is null || this.Text.Length == 0)
            return new NoOpCommand();

        var restore = context.CaptureRange(new DocumentRange(this.At, this.At));

        WordParagraphEditor.Insert(paragraph, this.At.Offset, this.Text);
        WordRevisions.WrapAsInsertion(paragraph, this.At.Offset, this.At.Offset + this.Text.Length, this.Author, this.Date, this.RevisionId);

        context.Reproject(this.At.Block);
        return restore;
    }

    public bool TryMerge(IEditCommand<WordDocument> next, out IEditCommand<WordDocument> merged)
    {
        merged = this;

        if (next is not TrackedInsertTextCommand other ||
            other.At.Block != this.At.Block ||
            other.At.Offset != this.At.Offset + this.Text.Length ||
            other.Author != this.Author ||
            this.Text.EndsWith(' '))
        {
            return false;
        }

        merged = this with { Text = this.Text + other.Text };
        return true;
    }
}


/// <summary>
/// Deletes a range as a tracked change: the text stays, struck through inside a <c>w:del</c>.
/// </summary>
/// <remarks>
/// Text that is itself an unaccepted insertion is removed outright rather than marked — deleting what
/// you just typed is not a change anyone needs to review, and Word does the same. Paragraph marks are
/// left alone: joining two paragraphs as a tracked change is recorded on the mark itself, which this
/// does not model, so a multi-paragraph deletion strikes the text in each and keeps the breaks.
/// </remarks>
public sealed record TrackedDeleteCommand(DocumentRange Range, string Author, DateTime Date, int RevisionId) : DocumentCommand
{
    public override string Name => "Delete";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        if (this.Range.IsEmpty)
            return new NoOpCommand();

        var restore = context.CaptureRange(this.Range);

        for (var block = this.Range.Start.Block; block <= this.Range.End.Block; block++)
        {
            if (context.ParagraphElementAt(block) is not { } paragraph)
                continue;

            var length = WordParagraphEditor.LengthOf(paragraph);
            var from = block == this.Range.Start.Block ? this.Range.Start.Offset : 0;
            var to = block == this.Range.End.Block ? this.Range.End.Offset : length;

            WordRevisions.MarkDeleted(paragraph, from, Math.Min(to, length), this.Author, this.Date, this.RevisionId);
            context.Reproject(block);
        }

        return restore;
    }
}


/// <summary>
/// Accepts or rejects the tracked changes that intersect a range — or every one, when the range is null.
/// </summary>
public sealed record ResolveRevisionsCommand(DocumentRange? Range, bool Accept) : DocumentCommand
{
    public override string Name => this.Accept ? "Accept Change" : "Reject Change";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        if (context.BodyElement is not { } body)
            return new NoOpCommand();

        DocumentCommand restore = this.Range is { } range ? context.CaptureRange(range) : context.CaptureAll();
        var changed = false;

        IEnumerable<OpenXmlElement> Candidates()
        {
            if (this.Range is not { } span)
                return body.Descendants().Where(x => x is InsertedRun or DeletedRun).ToList();

            return context.Revisions
                .Where(x => x.Element is not null && x.Range.Start <= span.End && x.Range.End >= span.Start)
                .Select(x => x.Element!)
                .ToList();
        }

        foreach (var element in Candidates())
        {
            if (element.Parent is null)
                continue;

            WordRevisions.Resolve(element, this.Accept);
            changed = true;
        }

        if (!changed)
            return new NoOpCommand();

        context.RereadAll();
        return restore;
    }
}


/// <summary>
/// Inserts a footnote: a reference mark at the caret and the note itself in <c>footnotes.xml</c>.
/// </summary>
/// <remarks>
/// The footnotes part is created with Word's two separator notes (ids -1 and 0) when the document has
/// none. Word expects them; a footnotes part without its separators opens with a repair prompt.
/// </remarks>
public sealed record InsertFootnoteCommand(DocumentPosition At, int Id, string Text) : DocumentCommand
{
    public override string Name => "Insert Footnote";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var paragraph = context.ParagraphElementAt(this.At.Block);
        if (paragraph is null || context.Main is not { } main)
            return new NoOpCommand();

        var blocks = context.CaptureRange(new DocumentRange(this.At, this.At));
        var part = context.CapturePart(DocumentPartKind.Footnotes);

        context.EnsureStyle("FootnoteText");
        context.EnsureStyle("FootnoteReference");

        var footnotesPart = main.FootnotesPart ?? main.AddNewPart<DocumentFormat.OpenXml.Packaging.FootnotesPart>();
        if (footnotesPart.Footnotes is null)
        {
            footnotesPart.Footnotes = new Footnotes(
                new Footnote(new Paragraph(
                    new ParagraphProperties(new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }),
                    new Run(new SeparatorMark())))
                { Type = FootnoteEndnoteValues.Separator, Id = -1 },
                new Footnote(new Paragraph(
                    new ParagraphProperties(new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }),
                    new Run(new ContinuationSeparatorMark())))
                { Type = FootnoteEndnoteValues.ContinuationSeparator, Id = 0 });
        }

        footnotesPart.Footnotes.AppendChild(new Footnote(
            new Paragraph(
                new ParagraphProperties(new ParagraphStyleId { Val = "FootnoteText" }),
                new Run(new RunProperties(new RunStyle { Val = "FootnoteReference" }), new FootnoteReferenceMark()),
                new Run(new W.Text(" " + this.Text) { Space = SpaceProcessingModeValues.Preserve })))
        {
            Id = this.Id
        });

        context.MarkPartDirty(footnotesPart.Footnotes);

        WordParagraphEditor.InsertObject(paragraph, this.At.Offset, new Run(
            new RunProperties(new RunStyle { Val = "FootnoteReference" }),
            new FootnoteReference { Id = this.Id }));

        context.Reproject(this.At.Block);

        return new CompositeCommand<WordDocument>(this.Name, [new RestorePartCommand(DocumentPartKind.Footnotes, part), blocks]);
    }
}


/// <summary>
/// Wraps a range in a hyperlink. An external target becomes a relationship; <c>#name</c> an anchor.
/// </summary>
/// <remarks>
/// Any link already covering part of the range is taken off first, so editing a link's address is this
/// same command applied over it. The runs take Word's <c>Hyperlink</c> character style, which is what
/// makes the link blue and underlined in Word as well as here.
/// </remarks>
public sealed record InsertHyperlinkCommand(DocumentRange Range, string Target) : DocumentCommand
{
    public override string Name => "Insert Hyperlink";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        if (this.Range.IsEmpty || !this.Range.IsWithinOneBlock || context.Main is not { } main)
            return new NoOpCommand();

        var paragraph = context.ParagraphElementAt(this.Range.Start.Block);
        if (paragraph is null)
            return new NoOpCommand();

        var restore = context.CaptureRange(this.Range);
        context.EnsureStyle("Hyperlink");

        WordHyperlinks.Unwrap(paragraph, this.Range.Start.Offset, this.Range.End.Offset);

        string? relationship = null;
        string? anchor = null;

        if (this.Target.StartsWith('#'))
        {
            anchor = this.Target[1..];
        }
        else if (Uri.TryCreate(this.Target, UriKind.RelativeOrAbsolute, out var uri))
        {
            relationship = main.AddHyperlinkRelationship(uri, isExternal: true).Id;
        }
        else
        {
            return new NoOpCommand();
        }

        var runs = WordParagraphEditor.IsolateRuns(paragraph, this.Range.Start.Offset, this.Range.End.Offset);

        // One w:hyperlink per group of sibling runs: a range that crosses into a tracked insertion
        // cannot be wrapped by a single element without breaking the revision in two.
        foreach (var group in Groups(runs))
        {
            var link = new Hyperlink { History = OnOffValue.FromBoolean(true) };
            if (relationship is not null)
                link.Id = relationship;

            if (anchor is not null)
                link.Anchor = anchor;

            group[0].InsertBeforeSelf(link);

            foreach (var run in group)
            {
                run.Remove();
                if (run.RunProperties is null)
                    run.InsertAt(new RunProperties(), 0);

                WordParagraphEditor.SetRunStyle("Hyperlink")(run.RunProperties!);
                link.AppendChild(run);
            }
        }

        context.Reproject(this.Range.Start.Block);
        return restore;
    }

    static IEnumerable<List<Run>> Groups(List<Run> runs)
    {
        var group = new List<Run>();

        foreach (var run in runs)
        {
            if (group.Count > 0 && (!ReferenceEquals(group[^1].Parent, run.Parent) || !ReferenceEquals(group[^1].NextSibling(), run)))
            {
                yield return group;
                group = new List<Run>();
            }

            group.Add(run);
        }

        if (group.Count > 0)
            yield return group;
    }
}


/// <summary>Takes any hyperlink off a range, leaving the text as plain text.</summary>
public sealed record RemoveHyperlinkCommand(DocumentRange Range) : DocumentCommand
{
    public override string Name => "Remove Hyperlink";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var paragraph = context.ParagraphElementAt(this.Range.Start.Block);
        if (paragraph is null)
            return new NoOpCommand();

        var restore = context.CaptureRange(this.Range);

        if (!WordHyperlinks.Unwrap(paragraph, this.Range.Start.Offset, Math.Max(this.Range.End.Offset, this.Range.Start.Offset + 1)))
            return new NoOpCommand();

        context.Reproject(this.Range.Start.Block);
        return restore;
    }
}


/// <summary>Adds a bookmark around a range, replacing any bookmark already using the name.</summary>
public sealed record InsertBookmarkCommand(DocumentRange Range, string BookmarkName, int Id) : DocumentCommand
{
    public override string Name => "Bookmark";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var first = context.ParagraphElementAt(this.Range.Start.Block);
        var last = context.ParagraphElementAt(this.Range.End.Block);
        if (first is null || last is null || context.BodyElement is not { } body)
            return new NoOpCommand();

        var restore = context.CaptureAll();

        foreach (var existing in body.Descendants<BookmarkStart>().Where(x => x.Name?.Value == this.BookmarkName).ToList())
        {
            var id = existing.Id?.Value;
            existing.Remove();

            foreach (var end in body.Descendants<BookmarkEnd>().Where(x => x.Id?.Value == id).ToList())
                end.Remove();
        }

        var idText = this.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        WordParagraphEditor.InsertObject(last, this.Range.End.Offset, new BookmarkEnd { Id = idText });
        WordParagraphEditor.InsertObject(first, this.Range.Start.Offset, new BookmarkStart { Id = idText, Name = this.BookmarkName });

        context.RereadAll();
        return restore;
    }
}


/// <summary>One line of a table of contents.</summary>
/// <param name="Level">1-3.</param>
/// <param name="Text">The heading's text.</param>
/// <param name="Page">The page it is on, as the TOC shows it.</param>
/// <param name="Paragraph">The heading's story index, so it can be bookmarked.</param>
public sealed record TableOfContentsEntry(int Level, string Text, int Page, int Paragraph);


/// <summary>
/// Inserts a table of contents before a top-level block, or replaces the one already in the document.
/// </summary>
/// <remarks>
/// <para>
/// Written the way Word writes one: a <c>TOC \o "1-3" \h \z \u</c> field whose result is a paragraph
/// per heading, each a hyperlink to a hidden <c>_Toc</c> bookmark on its heading, with a right tab and a
/// dotted leader before the page number. Word regenerates the result from the field when it updates
/// fields, and until then shows exactly this — so the table reads correctly in both.
/// </para>
/// <para>
/// Update replaces the field's paragraphs in place rather than inserting a second table, which is
/// how the same command serves both Insert and Update.
/// </para>
/// </remarks>
public sealed record InsertTableOfContentsCommand(int BeforeTop, IReadOnlyList<TableOfContentsEntry> Entries, string Title = "Contents") : DocumentCommand
{
    public override string Name => "Table of Contents";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        if (context.BodyElement is not { } body)
            return new NoOpCommand();

        var restore = context.CaptureAll();

        context.EnsureStyle("TOCHeading");
        context.EnsureStyle("TOC1");
        context.EnsureStyle("TOC2");
        context.EnsureStyle("TOC3");
        context.EnsureStyle("Hyperlink");

        // Bookmark every heading the table links to, reusing a _Toc bookmark it already has.
        var names = new List<string>();
        var nextId = context.NextId(typeof(BookmarkStart));

        foreach (var entry in this.Entries)
        {
            var heading = context.ParagraphElementAt(entry.Paragraph);
            var existing = heading?.Elements<BookmarkStart>().FirstOrDefault(x => x.Name?.Value?.StartsWith("_Toc", StringComparison.Ordinal) == true);

            if (heading is null)
            {
                names.Add(string.Empty);
                continue;
            }

            if (existing?.Name?.Value is { } name)
            {
                names.Add(name);
                continue;
            }

            var id = (nextId++).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var created = "_Toc" + (100000000 + Random.Shared.Next(900000000)).ToString(System.Globalization.CultureInfo.InvariantCulture);

            WordParagraphEditor.InsertObject(heading, WordParagraphEditor.LengthOf(heading), new BookmarkEnd { Id = id });
            WordParagraphEditor.InsertObject(heading, 0, new BookmarkStart { Id = id, Name = created });
            names.Add(created);
        }

        var paragraphs = this.Build(names);

        // Replace an existing table in place; otherwise insert before the requested block.
        var (start, end) = WordFields.FindTableOfContents(body);
        OpenXmlElement? anchor;

        if (start is not null && end is not null)
        {
            anchor = start.PreviousSibling();
            if (anchor is Paragraph title && title.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "TOCHeading")
                anchor = title.PreviousSibling();

            var remove = new List<OpenXmlElement>();
            for (var element = anchor?.NextSibling() ?? body.FirstChild; element is not null; element = element.NextSibling())
            {
                remove.Add(element);
                if (ReferenceEquals(element, end))
                    break;
            }

            foreach (var element in remove)
                element.Remove();
        }
        else
        {
            var before = context.BlockElementAt(Math.Clamp(this.BeforeTop, 0, Math.Max(0, context.Blocks.Count - 1)));
            anchor = before?.PreviousSibling();
        }

        foreach (var paragraph in paragraphs)
        {
            if (anchor is null)
                body.PrependChild(paragraph);
            else
                anchor.InsertAfterSelf(paragraph);

            anchor = paragraph;
        }

        context.RereadAllFromBody();
        return restore;
    }

    List<Paragraph> Build(IReadOnlyList<string> bookmarks)
    {
        var paragraphs = new List<Paragraph>
        {
            new(new ParagraphProperties(new ParagraphStyleId { Val = "TOCHeading" }),
                new Run(new W.Text(this.Title)))
        };

        var fieldBegin = new OpenXmlElement[]
        {
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new Run(new FieldCode(" TOC \\o \"1-3\" \\h \\z \\u ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Separate })
        };

        if (this.Entries.Count == 0)
        {
            var empty = new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "TOC1" }));
            empty.Append(fieldBegin);
            empty.AppendChild(new Run(new W.Text("No table of contents entries found.")));
            empty.AppendChild(new Run(new FieldChar { FieldCharType = FieldCharValues.End }));
            paragraphs.Add(empty);
            return paragraphs;
        }

        for (var i = 0; i < this.Entries.Count; i++)
        {
            var entry = this.Entries[i];
            var level = Math.Clamp(entry.Level, 1, 3);

            var paragraph = new Paragraph(new ParagraphProperties(
                new ParagraphStyleId { Val = $"TOC{level}" },
                new Tabs(new TabStop { Val = TabStopValues.Right, Leader = TabStopLeaderCharValues.Dot, Position = 9350 })));

            if (i == 0)
                paragraph.Append(fieldBegin.Select(x => x.CloneNode(true)));

            var link = new Hyperlink { History = OnOffValue.FromBoolean(true) };
            if (bookmarks.ElementAtOrDefault(i) is { Length: > 0 } name)
                link.Anchor = name;

            link.Append(
                new Run(new W.Text(entry.Text) { Space = SpaceProcessingModeValues.Preserve }),
                new Run(new TabChar()),
                new Run(new W.Text(entry.Page.ToString(System.Globalization.CultureInfo.InvariantCulture))));

            paragraph.AppendChild(link);

            if (i == this.Entries.Count - 1)
                paragraph.AppendChild(new Run(new FieldChar { FieldCharType = FieldCharValues.End }));

            paragraphs.Add(paragraph);
        }

        return paragraphs;
    }
}


/// <summary>Revision surgery: turning ranges into tracked insertions and deletions, and resolving them.</summary>
static class WordRevisions
{
    static string Stamp(DateTime date) => date.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Makes sure every run in a range sits inside a <c>w:ins</c> by this author.</summary>
    public static void WrapAsInsertion(Paragraph paragraph, int start, int end, string author, DateTime date, int id)
    {
        foreach (var run in WordParagraphEditor.IsolateRuns(paragraph, start, end))
        {
            if (run.Parent is InsertedRun existing && OoxmlUnits.Attribute(existing, "author") == author)
                continue;

            // Joined onto a neighbouring insertion by the same author where there is one, so a word
            // typed a keystroke at a time is one revision rather than one per letter.
            if (run.PreviousSibling() is InsertedRun previous && OoxmlUnits.Attribute(previous, "author") == author)
            {
                run.Remove();
                previous.AppendChild(run);
                continue;
            }

            if (run.NextSibling() is InsertedRun following && OoxmlUnits.Attribute(following, "author") == author)
            {
                run.Remove();
                following.PrependChild(run);
                continue;
            }

            var insertion = new InsertedRun
            {
                Id = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Author = author,
                Date = date
            };

            run.InsertBeforeSelf(insertion);
            run.Remove();
            insertion.AppendChild(run);
        }
    }

    /// <summary>Marks a range deleted, removing outright any of it that is an unaccepted insertion.</summary>
    public static void MarkDeleted(Paragraph paragraph, int start, int end, string author, DateTime date, int id)
    {
        foreach (var run in WordParagraphEditor.IsolateRuns(paragraph, start, end))
        {
            switch (run.Parent)
            {
                case DeletedRun:
                    continue;

                case InsertedRun insertion:
                    run.Remove();
                    if (!insertion.HasChildren)
                        insertion.Remove();

                    continue;
            }

            foreach (var text in run.Elements<W.Text>().ToList())
            {
                text.InsertBeforeSelf(new DeletedText(text.Text ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve });
                text.Remove();
            }

            if (run.PreviousSibling() is DeletedRun previous && OoxmlUnits.Attribute(previous, "author") == author)
            {
                run.Remove();
                previous.AppendChild(run);
                continue;
            }

            var deletion = new DeletedRun
            {
                Id = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Author = author,
                Date = date
            };

            run.InsertBeforeSelf(deletion);
            run.Remove();
            deletion.AppendChild(run);
        }

        // Paragraph markers and wrappers left empty by removed insertions go too.
        foreach (var insertion in paragraph.Descendants<InsertedRun>().Where(x => !x.HasChildren).ToList())
            insertion.Remove();
    }

    /// <summary>Accepts or rejects one <c>w:ins</c> or <c>w:del</c>.</summary>
    public static void Resolve(OpenXmlElement revision, bool accept)
    {
        var keep = revision is InsertedRun ? accept : !accept;

        if (!keep)
        {
            revision.Remove();
            return;
        }

        // Kept: unwrap, and for a rejected deletion turn the deleted text back into text.
        foreach (var child in revision.ChildElements.ToList())
        {
            child.Remove();

            if (revision is DeletedRun && child is Run run)
            {
                foreach (var deleted in run.Elements<DeletedText>().ToList())
                {
                    var text = new W.Text(deleted.Text ?? string.Empty);
                    WordParagraphEditor.Preserve(text);
                    deleted.InsertBeforeSelf(text);
                    deleted.Remove();
                }
            }

            revision.InsertBeforeSelf(child);
        }

        revision.Remove();
    }
}


/// <summary>Hyperlink surgery shared by insert, edit and remove.</summary>
static class WordHyperlinks
{
    /// <summary>
    /// Unwraps every hyperlink touching the range, returning its runs to plain text.
    /// </summary>
    /// <returns>True when a link was removed.</returns>
    public static bool Unwrap(Paragraph paragraph, int start, int end)
    {
        var removed = false;
        var cursor = 0;

        foreach (var child in paragraph.ChildElements.ToList())
        {
            var length = WordParagraphEditor.LengthOf(child);
            var childStart = cursor;
            cursor += length;

            if (child is not Hyperlink link || childStart >= end || childStart + length <= start)
                continue;

            foreach (var inner in link.ChildElements.ToList())
            {
                inner.Remove();

                if (inner is Run run && run.RunProperties?.RunStyle?.Val?.Value == "Hyperlink")
                    run.RunProperties.RunStyle.Remove();

                link.InsertBeforeSelf(inner);
            }

            link.Remove();
            removed = true;
        }

        return removed;
    }
}


/// <summary>Finding the table of contents' field in a body.</summary>
static class WordFields
{
    /// <summary>
    /// The paragraph a TOC field begins in and the one it ends in, or nulls when the body has none.
    /// </summary>
    public static (Paragraph? Start, Paragraph? End) FindTableOfContents(Body body)
    {
        Paragraph? start = null;
        var depth = 0;

        foreach (var paragraph in body.Elements<Paragraph>())
        {
            foreach (var run in paragraph.Descendants<Run>())
            {
                if (start is null)
                {
                    if (run.Elements<FieldCode>().Any(x => x.Text?.TrimStart().StartsWith("TOC", StringComparison.OrdinalIgnoreCase) == true))
                    {
                        start = paragraph;
                        depth = 1;
                    }

                    continue;
                }

                var type = OoxmlUnits.EnumAttribute(run.GetFirstChild<FieldChar>(), "fldCharType");
                if (type == "begin")
                    depth++;
                else if (type == "end" && --depth == 0)
                    return (start, paragraph);
            }
        }

        return (start, start is null ? null : start);
    }
}


/// <summary>Places children of <c>w:settings</c> at their schema position.</summary>
static class WordSettingsOrder
{
    static readonly string[] Order =
    [
        "writeProtection", "view", "zoom", "removePersonalInformation", "removeDateAndTime",
        "doNotDisplayPageBoundaries", "displayBackgroundShape", "printPostScriptOverText",
        "printFractionalCharacterWidth", "printFormsData", "embedTrueTypeFonts", "embedSystemFonts",
        "saveSubsetFonts", "saveFormsData", "mirrorMargins", "alignBordersAndEdges",
        "bordersDoNotSurroundHeader", "bordersDoNotSurroundFooter", "gutterAtTop", "hideSpellingErrors",
        "hideGrammaticalErrors", "activeWritingStyle", "proofState", "formsDesign", "attachedTemplate",
        "linkStyles", "stylePaneFormatFilter", "stylePaneSortMethod", "documentType", "mailMerge",
        "revisionView", "trackRevisions", "doNotTrackMoves", "doNotTrackFormatting", "documentProtection",
        "autoFormatOverride", "styleLockTheme", "styleLockQFSet", "defaultTabStop"
    ];

    public static void Insert(Settings settings, OpenXmlElement child)
    {
        var rank = Array.IndexOf(Order, child.LocalName);
        OpenXmlElement? previous = null;

        foreach (var existing in settings.ChildElements)
        {
            var existingRank = Array.IndexOf(Order, existing.LocalName);

            // Anything unranked is later in the sequence than everything ranked here.
            if (existingRank < 0 || existingRank > rank)
                break;

            previous = existing;
        }

        if (previous is null)
            settings.PrependChild(child);
        else
            previous.InsertAfterSelf(child);
    }
}
