using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Shiny.Controls.Office.Document;

/// <summary>
/// Character-level surgery on a paragraph's OOXML runs.
/// </summary>
/// <remarks>
/// <para>
/// Everything here works in offsets into the paragraph's concatenated text and translates that into
/// run-level edits. Runs are split only where an edit actually needs a boundary, and never rebuilt
/// wholesale — a run carries formatting, language, proofing state and revision marks the editor does
/// not model, and re-creating it to change one character throws all of that away.
/// </para>
/// <para>
/// The offset space has to match what <see cref="WordBodyReader"/> projects exactly, or every caret
/// position after the first disagreement is wrong. Text (<c>w:t</c> and a tracked deletion's
/// <c>w:delText</c>) counts its length, a tab counts four, and a drawing, a symbol and a footnote
/// reference count one each. Runs are found by walking the paragraph's inline containers — hyperlinks,
/// tracked insertions and deletions, content controls — but never by descending into a run, which is
/// what keeps the text inside a text box's own paragraphs out of the host paragraph's offsets.
/// </para>
/// </remarks>
static partial class WordParagraphEditor
{
    /// <summary>
    /// What an inline object contributes to the offset space.
    /// </summary>
    /// <remarks>
    /// U+FFFC OBJECT REPLACEMENT CHARACTER, the codepoint Unicode reserves for exactly this. The value
    /// barely matters — nothing ever renders it — but the <em>length</em> does: the layout engine
    /// advances its source offset by one for an inline object.
    /// </remarks>
    public const string ObjectPlaceholder = "￼";

    /// <summary>The paragraph's text as the reader projects it, which is the offset space edits use.</summary>
    public static string TextOf(Paragraph paragraph)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var (_, text) in Segments(paragraph))
            builder.Append(text);

        return builder.ToString();
    }

    public static int LengthOf(OpenXmlElement container)
    {
        var length = 0;
        foreach (var (_, text) in Segments(container))
            length += text.Length;

        return length;
    }

    /// <summary>
    /// The runs that make up a paragraph's text, in document order.
    /// </summary>
    /// <remarks>
    /// Descends through the inline containers a paragraph can hold and stops at each run. A run's own
    /// children are leaves as far as the offset space is concerned — including a <c>w:drawing</c>, whose
    /// text box may hold whole paragraphs of runs that belong to the shape and not to this paragraph.
    /// </remarks>
    public static IEnumerable<Run> RunsIn(OpenXmlElement container)
    {
        if (container is Run self)
        {
            yield return self;
            yield break;
        }

        foreach (var child in container.ChildElements)
        {
            switch (child)
            {
                case Run run:
                    yield return run;
                    break;

                case ParagraphProperties:
                case W.Drawing:
                case Picture:
                    break;

                case OpenXmlCompositeElement composite:
                    foreach (var inner in RunsIn(composite))
                        yield return inner;

                    break;
            }
        }
    }

    /// <summary>What a run's child contributes to the offset space, or null when it contributes nothing.</summary>
    static string? SegmentText(OpenXmlElement child) => child switch
    {
        W.Text text => text.Text ?? string.Empty,
        DeletedText deleted => deleted.Text ?? string.Empty,

        // The reader projects a tab as four spaces; the offset space has to agree or every caret
        // position after a tab is wrong by three.
        TabChar => "    ",
        W.Drawing => ObjectPlaceholder,
        SymbolChar => ObjectPlaceholder,
        FootnoteReference => ObjectPlaceholder,
        EndnoteReference => ObjectPlaceholder,
        _ => null
    };

    /// <summary>Text-bearing leaves in document order, paired with the text they contribute.</summary>
    static IEnumerable<(OpenXmlElement Element, string Text)> Segments(OpenXmlElement container)
    {
        foreach (var run in RunsIn(container))
        {
            foreach (var child in run.ChildElements)
            {
                if (SegmentText(child) is { } text)
                    yield return (child, text);
            }
        }
    }

    /// <summary>A run's length in the offset space.</summary>
    static int LengthOfRun(Run run)
    {
        var length = 0;
        foreach (var child in run.ChildElements)
            length += SegmentText(child)?.Length ?? 0;

        return length;
    }

    /// <summary>Inserts text at an offset, adopting the formatting of the run it lands in.</summary>
    public static void Insert(Paragraph paragraph, int offset, string text)
    {
        if (text.Length == 0)
            return;

        var segments = Segments(paragraph).ToList();
        var cursor = 0;

        foreach (var (element, segment) in segments)
        {
            var end = cursor + segment.Length;

            if (offset <= end && element is W.Text target)
            {
                var local = Math.Clamp(offset - cursor, 0, segment.Length);
                var value = target.Text ?? string.Empty;
                target.Text = value[..local] + text + value[local..];
                Preserve(target);
                return;
            }

            cursor = end;
        }

        // The offset falls beside something that is not plain text — a picture, a tab, a footnote mark,
        // a tracked deletion. There is no w:t to grow, so a run is made for the characters and placed on
        // the correct side of that one, carrying its formatting.
        cursor = 0;
        foreach (var (element, segment) in segments)
        {
            var start = cursor;
            var end = cursor + segment.Length;

            if (offset <= end && element.Parent is Run host)
            {
                var carrier = CarrierFor(host, text);

                if (element is DeletedText && offset > start && offset < end)
                {
                    // Typing into the middle of a deletion: split it so the new text lands between.
                    var tail = SplitRun(host, offset - start);
                    if (tail is not null)
                        host.InsertAfterSelf(tail);

                    TopOfDeletion(host).InsertAfterSelf(carrier);
                    return;
                }

                var anchor = element is DeletedText ? TopOfDeletion(host) : host;

                if (offset <= start)
                    anchor.InsertBeforeSelf(carrier);
                else
                    anchor.InsertAfterSelf(carrier);

                return;
            }

            cursor = end;
        }

        // An empty paragraph, or one whose only content is not text: start a run for the text to live in.
        var run = RunsIn(paragraph).LastOrDefault(x => x.Parent is not DeletedRun);
        if (run is null)
        {
            run = new Run();
            paragraph.AppendChild(run);
        }

        var created = new W.Text(text);
        Preserve(created);
        run.AppendChild(created);
    }

    /// <summary>A run carrying <paramref name="text"/> in <paramref name="host"/>'s formatting.</summary>
    static Run CarrierFor(Run host, string text)
    {
        var carrier = new Run();
        if (host.RunProperties is { } hostProperties)
            carrier.RunProperties = (RunProperties)hostProperties.CloneNode(true);

        var value = new W.Text(text);
        Preserve(value);
        carrier.AppendChild(value);
        return carrier;
    }

    /// <summary>
    /// The outermost element of a tracked deletion a run is in, so text typed next to it goes beside
    /// the deletion rather than inside it — text inside a <c>w:del</c> is text that was removed.
    /// </summary>
    static OpenXmlElement TopOfDeletion(Run run)
        => run.Parent is DeletedRun deletion ? deletion : run;

    /// <summary>Deletes a half-open offset range, dropping runs that end up with no content.</summary>
    public static void Delete(Paragraph paragraph, int start, int end)
    {
        if (end <= start)
            return;

        var cursor = 0;
        foreach (var (element, segment) in Segments(paragraph).ToList())
        {
            var segmentStart = cursor;
            var segmentEnd = cursor + segment.Length;
            cursor = segmentEnd;

            if (segmentEnd <= start || segmentStart >= end)
                continue;

            var from = Math.Max(0, start - segmentStart);
            var to = Math.Min(segment.Length, end - segmentStart);

            if (element is TextType text)
            {
                var value = text.Text ?? string.Empty;
                text.Text = value[..from] + value[Math.Min(to, value.Length)..];

                if (text is W.Text plain)
                    Preserve(plain);
                else
                    text.Space = SpaceProcessingModeValues.Preserve;
            }
            else if (from == 0 && to == segment.Length)
            {
                // A tab, a picture, a mark: atomic, so it goes entirely or not at all.
                element.Remove();
            }
        }

        RemoveEmptyRuns(paragraph);
    }

    /// <summary>
    /// Splits runs so that exactly the range <paramref name="start"/>..<paramref name="end"/> is covered by
    /// whole runs, and returns those runs in order.
    /// </summary>
    /// <remarks>
    /// The foundation every range operation stands on: formatting a word, wrapping it in a hyperlink,
    /// marking it deleted. The tail is always split before the head, because splitting the head first
    /// shifts the offsets the tail split depends on.
    /// </remarks>
    public static List<Run> IsolateRuns(Paragraph paragraph, int start, int end)
    {
        var result = new List<Run>();
        if (end <= start)
            return result;

        foreach (var original in RunsIn(paragraph).ToList())
        {
            var length = LengthOfRun(original);
            if (length == 0)
                continue;

            var runStart = OffsetOfRun(paragraph, original);
            var runEnd = runStart + length;

            if (runEnd <= start || runStart >= end)
                continue;

            var from = Math.Max(0, start - runStart);
            var to = Math.Min(length, end - runStart);

            if (to < length && SplitRun(original, to) is { } tail)
                original.InsertAfterSelf(tail);

            var target = original;
            if (from > 0 && SplitRun(original, from) is { } head)
            {
                original.InsertAfterSelf(head);
                target = head;
            }

            result.Add(target);
        }

        return result;
    }

    /// <summary>
    /// Applies a formatting change to an offset range, splitting runs at the boundaries.
    /// </summary>
    /// <remarks>
    /// The mutation is expressed as an action on the run's <see cref="RunProperties"/> rather than as a
    /// finished style, so a run keeps every property the change does not touch.
    /// </remarks>
    public static void Format(Paragraph paragraph, int start, int end, Action<RunProperties> apply)
    {
        if (end <= start)
            return;

        foreach (var run in IsolateRuns(paragraph, start, end))
        {
            // A run with no properties gets them as its first child, which is where the schema puts them.
            if (run.RunProperties is null)
                run.InsertAt(new RunProperties(), 0);

            apply(run.RunProperties!);
        }

        RemoveEmptyRuns(paragraph);
    }

    /// <summary>
    /// Splits a run at a local offset and returns the new trailing run, <b>not yet inserted</b> anywhere.
    /// </summary>
    /// <remarks>
    /// Any number of children are handled — text, tabs, pictures in one run — and the tail carries a
    /// clone of the original's properties so nothing is lost across the boundary. Returns null when the
    /// offset is at either end and there is nothing to split.
    /// </remarks>
    public static Run? SplitRun(Run run, int localOffset)
    {
        var length = LengthOfRun(run);
        if (localOffset <= 0 || localOffset >= length)
            return null;

        var tail = new Run();
        if (run.RunProperties is { } properties)
            tail.RunProperties = (RunProperties)properties.CloneNode(true);

        var cursor = 0;
        foreach (var child in run.ChildElements.ToList())
        {
            if (child is RunProperties)
                continue;

            var segment = SegmentText(child);
            var childLength = segment?.Length ?? 0;
            var childStart = cursor;
            cursor += childLength;

            if (childStart >= localOffset)
            {
                // Zero-width children at the boundary (a break, a field char) stay with the head.
                if (childLength == 0 && childStart == localOffset)
                    continue;

                child.Remove();
                tail.AppendChild(child);
                continue;
            }

            if (childStart + childLength > localOffset && child is TextType text)
            {
                var value = text.Text ?? string.Empty;
                var cut = localOffset - childStart;

                var moved = (TextType)text.CloneNode(false);
                moved.Text = value[cut..];
                text.Text = value[..cut];

                if (moved is W.Text m)
                    Preserve(m);
                else
                    moved.Space = SpaceProcessingModeValues.Preserve;

                if (text is W.Text t)
                    Preserve(t);
                else
                    text.Space = SpaceProcessingModeValues.Preserve;

                tail.AppendChild(moved);
            }
        }

        return tail;
    }

    /// <summary>Where a run starts in the paragraph's offset space.</summary>
    static int OffsetOfRun(Paragraph paragraph, Run target)
    {
        var cursor = 0;
        foreach (var run in RunsIn(paragraph))
        {
            if (ReferenceEquals(run, target))
                return cursor;

            cursor += LengthOfRun(run);
        }

        return cursor;
    }

    /// <summary>
    /// Where an arbitrary element — a bookmark, a comment anchor — sits in the offset space.
    /// </summary>
    public static int OffsetOf(Paragraph paragraph, OpenXmlElement target)
    {
        var counted = new HashSet<Run>(RunsIn(paragraph), ReferenceEqualityComparer.Instance);
        var cursor = 0;

        foreach (var node in paragraph.Descendants())
        {
            if (ReferenceEquals(node, target))
                return cursor;

            if (node.Parent is Run run && counted.Contains(run) && SegmentText(node) is { } text)
                cursor += text.Length;
        }

        return cursor;
    }

    /// <summary>
    /// Splits a paragraph at an offset, returning the new paragraph that follows it.
    /// </summary>
    /// <remarks>
    /// The tail inherits a clone of the original's properties, so pressing Enter mid-paragraph keeps the
    /// style, indent and numbering on both halves. Hyperlinks and tracked changes that straddle the break
    /// are split along with it, each half keeping its wrapper — moving only the runs across, which is
    /// what this used to do, turned the second half of a link back into plain text.
    /// </remarks>
    public static Paragraph Split(Paragraph paragraph, int offset)
    {
        var tail = new Paragraph();
        if (paragraph.ParagraphProperties is { } properties)
        {
            var cloned = (ParagraphProperties)properties.CloneNode(true);

            // A section break belongs to the paragraph that ends the section, which is the tail's now:
            // left on both, one Enter would create a second section.
            properties.RemoveAllChildren<SectionProperties>();
            tail.ParagraphProperties = cloned;
        }

        var cursor = 0;
        foreach (var child in paragraph.ChildElements.ToList())
        {
            if (child is ParagraphProperties)
                continue;

            var length = LengthOf(child);
            var start = cursor;
            cursor += length;

            if (start >= offset && !(length == 0 && start == offset && IsOpeningMark(child)))
            {
                child.Remove();
                tail.AppendChild(child);
                continue;
            }

            if (start < offset && start + length > offset && SplitElement(child, offset - start) is { } moved)
                tail.AppendChild(moved);
        }

        // A tail with no runs still needs one carrying the caret's formatting, or the new paragraph
        // renders with document defaults and typing into it changes font unexpectedly.
        if (!RunsIn(tail).Any())
        {
            var seed = new Run();
            if (RunsIn(paragraph).LastOrDefault()?.RunProperties is { } runProperties)
                seed.RunProperties = (RunProperties)runProperties.CloneNode(true);

            tail.AppendChild(seed);
        }

        paragraph.InsertAfterSelf(tail);
        return tail;
    }

    /// <summary>
    /// True for a zero-width mark that opens something at the caret — a bookmark or comment start — and
    /// so belongs with what follows rather than what precedes.
    /// </summary>
    static bool IsOpeningMark(OpenXmlElement element)
        => element is BookmarkEnd or CommentRangeEnd;

    /// <summary>
    /// Splits an inline element at a local offset, returning the trailing part (not inserted).
    /// </summary>
    static OpenXmlElement? SplitElement(OpenXmlElement element, int local)
    {
        switch (element)
        {
            case Run run:
                return SplitRun(run, local);

            case Hyperlink or InsertedRun or DeletedRun or CustomXmlRun:
                // A shallow clone keeps the wrapper's attributes — the link target, the revision's author
                // and date — and the children after the split point move into it.
                var clone = element.CloneNode(false);
                var cursor = 0;

                foreach (var child in element.ChildElements.ToList())
                {
                    var length = LengthOf(child);
                    var start = cursor;
                    cursor += length;

                    if (start >= local)
                    {
                        child.Remove();
                        clone.AppendChild(child);
                    }
                    else if (start + length > local && SplitElement(child, local - start) is { } moved)
                    {
                        clone.AppendChild(moved);
                    }
                }

                return clone;

            default:
                // Content controls and simple fields are kept whole, on the side they start.
                return null;
        }
    }

    /// <summary>Appends one paragraph's content onto another and removes the source.</summary>
    /// <remarks>
    /// Everything but the source's properties moves — hyperlinks, bookmarks and revision wrappers
    /// included — so joining two paragraphs loses nothing either of them had.
    /// </remarks>
    public static void Merge(Paragraph target, Paragraph source)
    {
        foreach (var child in source.ChildElements.ToList())
        {
            if (child is ParagraphProperties)
                continue;

            child.Remove();
            target.AppendChild(child);
        }

        source.Remove();
        RemoveEmptyRuns(target);
    }

    /// <summary>Mutates a paragraph's properties, creating the element when it does not exist yet.</summary>
    public static void FormatParagraph(Paragraph paragraph, Action<ParagraphProperties> apply)
    {
        var properties = paragraph.ParagraphProperties;
        if (properties is null)
        {
            properties = new ParagraphProperties();

            // pPr must be the first child of w:p; appending it puts the document out of schema order.
            paragraph.InsertAt(properties, 0);
        }

        apply(properties);
    }

    static void RemoveEmptyRuns(Paragraph paragraph)
    {
        foreach (var run in RunsIn(paragraph).ToList())
        {
            // A run with no children at all is debris. One holding an empty w:t is kept only when it is
            // the paragraph's last, so an emptied paragraph still has somewhere to carry formatting.
            if (run.ChildElements.Count == 0 || run.ChildElements.All(x => x is RunProperties))
            {
                if (RunsIn(paragraph).Count() > 1)
                {
                    var parent = run.Parent;
                    run.Remove();

                    // A wrapper left holding nothing is removed with it — an empty hyperlink or
                    // revision is not an error, but it is something Word will round-trip forever.
                    if (parent is Hyperlink or InsertedRun or DeletedRun && !parent.HasChildren)
                        parent.Remove();
                }

                continue;
            }

            foreach (var text in run.Elements<W.Text>().ToList())
            {
                if (text.Text?.Length == 0 && run.Elements<W.Text>().Count() > 1)
                    text.Remove();
            }

            foreach (var text in run.Elements<DeletedText>().ToList())
            {
                if (text.Text?.Length == 0)
                    text.Remove();
            }

            // A deletion emptied of its deleted text has nothing left to say.
            if (run.Parent is DeletedRun deletion && LengthOfRun(run) == 0 && !run.Elements<W.Break>().Any())
            {
                run.Remove();
                if (!deletion.HasChildren)
                    deletion.Remove();
            }
        }
    }

    /// <summary>
    /// Marks a text element to keep its whitespace.
    /// </summary>
    /// <remarks>
    /// Without <c>xml:space="preserve"</c> Word discards leading and trailing spaces on load, so text
    /// typed with a trailing space loses it the next time the file is opened.
    /// </remarks>
    internal static void Preserve(W.Text text)
    {
        var value = text.Text;
        if (!string.IsNullOrEmpty(value) && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
            text.Space = SpaceProcessingModeValues.Preserve;
    }

    // ---- inline objects and marks ----

    /// <summary>
    /// Inserts a prepared run — a picture, a shape, a field — at an offset, splitting text around it.
    /// </summary>
    public static void InsertObject(Paragraph paragraph, int offset, OpenXmlElement element)
    {
        var cursor = 0;

        foreach (var (segmentElement, segment) in Segments(paragraph).ToList())
        {
            var start = cursor;
            var end = cursor + segment.Length;
            cursor = end;

            if (offset > end)
                continue;

            if (segmentElement.Parent is not Run host)
                continue;

            var anchor = segmentElement is DeletedText ? TopOfDeletion(host) : host;

            // Landing inside a run means splitting it, so the object sits between the two halves
            // rather than jumping to whichever end was nearer.
            if (offset > start && offset < end)
            {
                if (SplitRun(host, offset - start) is { } tail)
                    host.InsertAfterSelf(tail);

                anchor.InsertAfterSelf(element);
                return;
            }

            if (offset <= start)
                anchor.InsertBeforeSelf(element);
            else
                anchor.InsertAfterSelf(element);

            return;
        }

        // Past the last segment, or an empty paragraph: the object goes at the end.
        paragraph.AppendChild(element);
    }

    /// <summary>
    /// Resizes the inline object at an offset, returning false when there is none there.
    /// </summary>
    /// <remarks>
    /// Both extents are written: <c>wp:extent</c> on the wrapper, which decides the space the object
    /// takes in the flow, and the <c>a:ext</c> inside it, which is what the shape or picture is drawn
    /// into.
    /// </remarks>
    public static bool ResizeObject(Paragraph paragraph, int offset, double width, double height)
    {
        if (ObjectAt(paragraph, offset) is not { } drawing)
            return false;

        var cx = OoxmlUnits.PixelsToEmu(Math.Max(1, width));
        var cy = OoxmlUnits.PixelsToEmu(Math.Max(1, height));

        foreach (var extent in drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>())
        {
            extent.Cx = cx;
            extent.Cy = cy;
        }

        foreach (var extents in drawing.Descendants<DocumentFormat.OpenXml.Drawing.Extents>())
        {
            extents.Cx = cx;
            extents.Cy = cy;
        }

        return true;
    }

    /// <summary>The inline object occupying an offset, or null when that offset is text.</summary>
    public static W.Drawing? ObjectAt(Paragraph paragraph, int offset)
    {
        var cursor = 0;

        foreach (var (element, segment) in Segments(paragraph))
        {
            if (element is W.Drawing drawing && offset >= cursor && offset < cursor + segment.Length)
                return drawing;

            cursor += segment.Length;
        }

        return null;
    }

    /// <summary>The run holding the character at an offset (the one before it at a boundary), or null.</summary>
    public static Run? RunAt(Paragraph paragraph, int offset)
    {
        var cursor = 0;
        Run? last = null;

        foreach (var run in RunsIn(paragraph))
        {
            var length = LengthOfRun(run);
            if (length > 0 && offset >= cursor && offset < cursor + length)
                return run;

            if (length > 0 && cursor + length == offset)
                last = run;

            cursor += length;
        }

        return last ?? RunsIn(paragraph).FirstOrDefault();
    }

    /// <summary>
    /// Rewrites the characters in a range through <paramref name="map"/>, one for one.
    /// </summary>
    /// <remarks>
    /// Change case is the user. The mapping is character-for-character on purpose: a transform that
    /// changed the length would shift every offset after it, and runs, bookmarks and comment anchors are
    /// all positioned by offset.
    /// </remarks>
    public static void MapText(Paragraph paragraph, int start, int end, Func<int, char, char> map)
    {
        var cursor = 0;

        foreach (var (element, segment) in Segments(paragraph).ToList())
        {
            var segmentStart = cursor;
            cursor += segment.Length;

            if (element is not W.Text text || cursor <= start || segmentStart >= end)
                continue;

            var chars = (text.Text ?? string.Empty).ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var absolute = segmentStart + i;
                if (absolute >= start && absolute < end)
                    chars[i] = map(absolute, chars[i]);
            }

            text.Text = new string(chars);
            Preserve(text);
        }
    }
}
