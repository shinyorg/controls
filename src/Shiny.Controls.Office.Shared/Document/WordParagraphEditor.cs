using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using W = DocumentFormat.OpenXml.Wordprocessing;
using TextAlignment = Shiny.Controls.Office.Text.TextAlignment;

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
/// Only <see cref="W.Text"/>, <see cref="TabChar"/> and <see cref="W.Drawing"/> contribute to the
/// offset space, matching what the reader projects. Anything else in a run is left strictly alone.
/// </para>
/// </remarks>
static class WordParagraphEditor
{
    /// <summary>
    /// What an inline object contributes to the offset space.
    /// </summary>
    /// <remarks>
    /// U+FFFC OBJECT REPLACEMENT CHARACTER, the codepoint Unicode reserves for exactly this. The
    /// value barely matters — nothing ever renders it — but the <em>length</em> does: the layout
    /// engine advances its source offset by one for an inline object, so anything here that was not
    /// one character long would put every caret position after a picture in the wrong place.
    /// </remarks>
    public const string ObjectPlaceholder = "\uFFFC";
    /// <summary>The paragraph's text as the reader projects it, which is the offset space edits use.</summary>
    public static string TextOf(Paragraph paragraph)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var (_, text) in Segments(paragraph))
            builder.Append(text);

        return builder.ToString();
    }

    public static int LengthOf(Paragraph paragraph)
    {
        var length = 0;
        foreach (var (_, text) in Segments(paragraph))
            length += text.Length;

        return length;
    }

    /// <summary>Text-bearing leaves in document order, paired with the text they contribute.</summary>
    static IEnumerable<(OpenXmlElement Element, string Text)> Segments(Paragraph paragraph)
    {
        foreach (var run in paragraph.Descendants<Run>())
        {
            foreach (var child in run.ChildElements)
            {
                switch (child)
                {
                    case W.Text text:
                        yield return (text, text.Text ?? string.Empty);
                        break;

                    case TabChar:
                        // The reader projects a tab as four spaces; the offset space has to agree or
                        // every caret position after a tab is wrong by three.
                        yield return (child, "    ");
                        break;

                    case W.Drawing:
                        // One character, matching the layout engine. A drawing that contributed
                        // nothing here would still occupy a position on screen, so the caret would
                        // drift by one for every picture above it.
                        yield return (child, ObjectPlaceholder);
                        break;
                }
            }
        }
    }

    /// <summary>Inserts text at an offset, adopting the formatting of the run it lands in.</summary>
    public static void Insert(Paragraph paragraph, int offset, string text)
    {
        if (text.Length == 0)
            return;

        var cursor = 0;
        foreach (var (element, segment) in Segments(paragraph).ToList())
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

        // The offset falls on an inline object rather than in text — typing immediately before or
        // after a picture. There is no W.Text to grow, so a run is made for the character and placed
        // on the correct side of the object's run.
        var cursorBeforeObject = 0;
        foreach (var (element, segment) in Segments(paragraph).ToList())
        {
            var end = cursorBeforeObject + segment.Length;

            if (offset <= end && element is W.Drawing drawing && drawing.Parent is Run host)
            {
                var carrier = new Run();
                if (host.RunProperties is { } hostProperties)
                    carrier.RunProperties = (RunProperties)hostProperties.CloneNode(true);

                var value = new W.Text(text);
                Preserve(value);
                carrier.AppendChild(value);

                if (offset <= cursorBeforeObject)
                    host.InsertBeforeSelf(carrier);
                else
                    host.InsertAfterSelf(carrier);

                return;
            }

            cursorBeforeObject = end;
        }

        // An empty paragraph, or one whose only content is not text: start a run for the text to live in.
        var run = paragraph.Descendants<Run>().LastOrDefault();
        if (run is null)
        {
            run = new Run();
            paragraph.AppendChild(run);
        }

        var created = new W.Text(text);
        Preserve(created);
        run.AppendChild(created);
    }

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

            if (element is W.Text text)
            {
                var value = text.Text ?? string.Empty;
                text.Text = value[..from] + value[Math.Min(to, value.Length)..];
                Preserve(text);
            }
            else if (from == 0 && to == segment.Length)
            {
                // A tab is atomic: it goes entirely or not at all.
                element.Remove();
            }
        }

        RemoveEmptyRuns(paragraph);
    }

    /// <summary>
    /// Applies a formatting change to an offset range, splitting runs at the boundaries.
    /// </summary>
    /// <remarks>
    /// The mutation is expressed as an action on the run's <see cref="RunProperties"/> rather than as a
    /// finished style, so a run keeps every property the change does not touch. Handing over a whole
    /// style would flatten italics and colours the user never asked to change.
    /// </remarks>
    public static void Format(Paragraph paragraph, int start, int end, Action<RunProperties> apply)
    {
        if (end <= start)
            return;

        foreach (var original in paragraph.Descendants<Run>().ToList())
        {
            var text = original.GetFirstChild<W.Text>();
            if (text is null)
                continue;

            var runStart = OffsetOf(paragraph, text);
            var value = text.Text ?? string.Empty;
            var runEnd = runStart + value.Length;

            if (runEnd <= start || runStart >= end)
                continue;

            var from = Math.Max(0, start - runStart);
            var to = Math.Min(value.Length, end - runStart);

            // Split off the tail first: splitting the head would shift the offsets the tail split
            // depends on, and the second split would land in the wrong place.
            if (to < value.Length)
                SplitAt(original, to);

            var target = from > 0 ? SplitAt(original, from) ?? original : original;
            apply(target.RunProperties ??= new RunProperties());
        }

        RemoveEmptyRuns(paragraph);
    }

    /// <summary>
    /// Splits a run at a local offset and returns the new trailing run, which carries a clone of the
    /// original's properties so nothing is lost across the boundary.
    /// </summary>
    static Run? SplitAt(Run run, int localOffset)
    {
        var text = run.GetFirstChild<W.Text>();
        if (text is null)
            return null;

        var value = text.Text ?? string.Empty;
        if (localOffset <= 0 || localOffset >= value.Length)
            return null;

        var tail = new Run();
        if (run.RunProperties is { } properties)
            tail.RunProperties = (RunProperties)properties.CloneNode(true);

        var tailText = new W.Text(value[localOffset..]);
        Preserve(tailText);
        tail.AppendChild(tailText);

        text.Text = value[..localOffset];
        Preserve(text);

        run.InsertAfterSelf(tail);
        return tail;
    }

    static int OffsetOf(Paragraph paragraph, OpenXmlElement target)
    {
        var cursor = 0;
        foreach (var (element, segment) in Segments(paragraph))
        {
            if (ReferenceEquals(element, target))
                return cursor;

            cursor += segment.Length;
        }

        return cursor;
    }

    /// <summary>
    /// Splits a paragraph at an offset, returning the new paragraph that follows it.
    /// </summary>
    /// <remarks>
    /// The tail inherits a clone of the original's properties, so pressing Enter mid-paragraph keeps
    /// the style, indent and numbering on both halves rather than dropping the second into Normal.
    /// </remarks>
    public static Paragraph Split(Paragraph paragraph, int offset)
    {
        var tail = new Paragraph();
        if (paragraph.ParagraphProperties is { } properties)
            tail.ParagraphProperties = (ParagraphProperties)properties.CloneNode(true);

        var text = TextOf(paragraph);
        var trailing = offset >= text.Length ? string.Empty : text[offset..];

        // Move the trailing runs across, splitting the one the caret sits inside. The cursor counts
        // everything the offset space counts — a run holding a picture is one character wide even
        // though it has no W.Text — or a paragraph split after an image puts the break in the wrong
        // place.
        var cursor = 0;
        foreach (var run in paragraph.Descendants<Run>().ToList())
        {
            var runText = run.GetFirstChild<W.Text>();
            var runLength = LengthOfRun(run);
            var runStart = cursor;
            cursor += runLength;

            if (runStart >= offset)
            {
                run.Remove();
                tail.AppendChild(run);
                continue;
            }

            // Only text can be split part-way. An object-bearing run is atomic and has already been
            // placed by the test above, on whichever side of the break it started.
            if (runStart + runLength > offset && runText is not null)
            {
                var local = offset - runStart;
                var moved = SplitAt(run, local);
                if (moved is not null)
                {
                    moved.Remove();
                    tail.AppendChild(moved);
                }
            }
        }

        // A tail with no runs still needs one carrying the caret's formatting, or the new paragraph
        // renders with document defaults and typing into it changes font unexpectedly.
        if (!tail.Descendants<Run>().Any())
        {
            var seed = new Run();
            if (paragraph.Descendants<Run>().LastOrDefault()?.RunProperties is { } runProperties)
                seed.RunProperties = (RunProperties)runProperties.CloneNode(true);

            tail.AppendChild(seed);
        }

        _ = trailing;
        paragraph.InsertAfterSelf(tail);
        return tail;
    }

    /// <summary>Appends one paragraph's runs onto another and removes the source.</summary>
    public static void Merge(Paragraph target, Paragraph source)
    {
        foreach (var run in source.Descendants<Run>().ToList())
        {
            run.Remove();
            target.AppendChild(run);
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

            // pPr must be the first child of w:p; appending it puts the document out of schema order
            // and Word reports the file as corrupt.
            paragraph.InsertAt(properties, 0);
        }

        apply(properties);
    }

    static void RemoveEmptyRuns(Paragraph paragraph)
    {
        foreach (var run in paragraph.Descendants<Run>().ToList())
        {
            // A run with no children at all is debris. One holding an empty w:t is kept only when it is
            // the paragraph's last, so an emptied paragraph still has somewhere to carry formatting.
            if (run.ChildElements.Count == 0 || run.ChildElements.All(x => x is RunProperties))
            {
                if (paragraph.Descendants<Run>().Count() > 1)
                    run.Remove();

                continue;
            }

            foreach (var text in run.Elements<W.Text>().ToList())
            {
                if (text.Text?.Length == 0 && run.Elements<W.Text>().Count() > 1)
                    text.Remove();
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
    static void Preserve(W.Text text)
    {
        var value = text.Text;
        if (!string.IsNullOrEmpty(value) && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
            text.Space = SpaceProcessingModeValues.Preserve;
    }

    /// <summary>Builds the run-property mutation for a formatting toggle.</summary>
    /// <remarks>
    /// Turning a toggle off writes an explicit <c>val="0"</c> rather than just removing the element.
    /// Removing it only drops the run's own formatting, so text that is bold because its style is —
    /// every heading — stayed bold and the button appeared to do nothing. The explicit off is what
    /// Word writes, and on text with no inherited bold it is simply redundant.
    /// </remarks>
    public static Action<RunProperties> ToggleBold(bool on) => properties =>
    {
        properties.RemoveAllChildren<Bold>();
        properties.InsertAt(on ? new Bold() : new Bold { Val = OnOffValue.FromBoolean(false) }, 0);
    };

    public static Action<RunProperties> ToggleItalic(bool on) => properties =>
    {
        properties.RemoveAllChildren<Italic>();
        properties.InsertAt(on ? new Italic() : new Italic { Val = OnOffValue.FromBoolean(false) }, 0);
    };

    public static Action<RunProperties> ToggleUnderline(bool on) => properties =>
    {
        properties.RemoveAllChildren<W.Underline>();
        properties.AppendChild(new W.Underline { Val = on ? UnderlineValues.Single : UnderlineValues.None });
    };

    public static Action<RunProperties> ToggleStrike(bool on) => properties =>
    {
        properties.RemoveAllChildren<Strike>();
        properties.AppendChild(on ? new Strike() : new Strike { Val = OnOffValue.FromBoolean(false) });
    };

    public static Action<RunProperties> SetFontFamily(string family) => properties =>
    {
        properties.RemoveAllChildren<RunFonts>();
        properties.InsertAt(new RunFonts { Ascii = family, HighAnsi = family, ComplexScript = family }, 0);
    };

    public static Action<RunProperties> SetFontSize(double points) => properties =>
    {
        properties.RemoveAllChildren<FontSize>();
        properties.RemoveAllChildren<FontSizeComplexScript>();

        // Word stores run size in half-points.
        var halfPoints = Math.Max(1, (int)Math.Round(points * 2)).ToString();
        properties.AppendChild(new FontSize { Val = halfPoints });
        properties.AppendChild(new FontSizeComplexScript { Val = halfPoints });
    };

    public static Action<RunProperties> SetColor(ArgbColor color) => properties =>
    {
        properties.RemoveAllChildren<W.Color>();
        properties.AppendChild(new W.Color { Val = $"{color.R:X2}{color.G:X2}{color.B:X2}" });
    };

    /// <summary>
    /// Sets or clears the highlight behind a run.
    /// </summary>
    /// <remarks>
    /// <c>w:highlight</c> takes a name from a closed list, not a colour, so the requested colour is
    /// resolved to the nearest one Word can express. Clearing writes nothing rather than
    /// <c>val="none"</c>: an explicit none is only needed to override a highlight inherited from a
    /// character style, and leaving the element out is what Word itself does for a run with no
    /// highlight at all.
    /// </remarks>
    public static Action<RunProperties> SetHighlight(ArgbColor? color) => properties =>
    {
        properties.RemoveAllChildren<Highlight>();

        if (color is null)
            return;

        properties.AppendChild(new Highlight { Val = new EnumValue<HighlightColorValues>
        {
            InnerText = HighlightPalette.NameOf(color)
        } });
    };

    public static Action<ParagraphProperties> SetAlignment(TextAlignment alignment) => properties =>
    {
        properties.RemoveAllChildren<Justification>();
        properties.AppendChild(new Justification
        {
            Val = alignment switch
            {
                TextAlignment.Center => JustificationValues.Center,
                TextAlignment.Right => JustificationValues.Right,
                TextAlignment.Justify => JustificationValues.Both,
                _ => JustificationValues.Left
            }
        });
    };

    public static Action<ParagraphProperties> SetStyle(string? styleId) => properties =>
    {
        properties.RemoveAllChildren<ParagraphStyleId>();
        if (styleId is not null)
            properties.InsertAt(new ParagraphStyleId { Val = styleId }, 0);
    };

    // ---- lists ----

    /// <summary>The paragraph's outline level within its list, or zero when it is not in one.</summary>
    public static int ListLevelOf(Paragraph paragraph)
        => paragraph.ParagraphProperties?.NumberingProperties?.NumberingLevelReference?.Val?.Value ?? 0;

    /// <summary>True when the paragraph points at a list definition.</summary>
    public static bool IsListItem(Paragraph paragraph)
        => paragraph.ParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value is > 0;

    /// <summary>
    /// Puts a paragraph into a list, at a level.
    /// </summary>
    /// <remarks>
    /// The direct indent goes with it. A level definition carries its own indent and hanging indent,
    /// and the reader only applies those when the paragraph has none of its own — so a paragraph that
    /// had been indented by hand would keep that indent and ignore the one its level asks for, which
    /// looks like the nesting silently not working.
    /// </remarks>
    public static Action<ParagraphProperties> SetList(int numId, int level) => properties =>
    {
        properties.RemoveAllChildren<NumberingProperties>();
        properties.RemoveAllChildren<Indentation>();

        InsertOrdered(properties, new NumberingProperties(
            new NumberingLevelReference { Val = Math.Clamp(level, 0, WordListDefinitions.Levels - 1) },
            new NumberingId { Val = numId }));
    };

    /// <summary>Takes a paragraph out of its list, leaving everything else about it alone.</summary>
    public static Action<ParagraphProperties> ClearList() => properties =>
    {
        properties.RemoveAllChildren<NumberingProperties>();
        properties.RemoveAllChildren<Indentation>();
    };

    /// <summary>
    /// Moves a list item in or out one level, doing nothing to a paragraph that is not in a list.
    /// </summary>
    /// <remarks>
    /// Reads the current level from the properties rather than taking it as an argument, so a
    /// selection spanning several levels shifts each item relative to its own — Tab over a mixed
    /// selection is meant to move the whole shape of the list, not flatten it.
    /// </remarks>
    public static Action<ParagraphProperties> ShiftListLevel(int delta) => properties =>
    {
        if (properties.NumberingProperties is not { } numbering)
            return;

        var current = numbering.NumberingLevelReference?.Val?.Value ?? 0;
        var target = Math.Clamp(current + delta, 0, WordListDefinitions.Levels - 1);

        numbering.RemoveAllChildren<NumberingLevelReference>();

        // w:ilvl precedes w:numId in w:numPr, and unlike most of pPr this pair really is checked.
        numbering.InsertAt(new NumberingLevelReference { Val = target }, 0);

        // The level's own indent only reaches a paragraph with no indent of its own, so a leftover
        // direct indent would pin an outdented item at the depth it used to be.
        properties.RemoveAllChildren<Indentation>();
    };

    /// <summary>
    /// Inserts a child of <c>w:pPr</c> at its schema position.
    /// </summary>
    /// <remarks>
    /// <c>w:pPr</c>'s children are a sequence, not a set. Only the elements this editor writes are
    /// ranked; anything unranked sorts last, which keeps a paragraph's <c>w:rPr</c> and
    /// <c>w:sectPr</c> — both of which really do belong at the end — where they were.
    /// </remarks>
    static void InsertOrdered(ParagraphProperties properties, OpenXmlElement child)
    {
        var rank = OrderOf(child);
        OpenXmlElement? previous = null;

        foreach (var existing in properties.ChildElements)
        {
            if (OrderOf(existing) > rank)
                break;

            previous = existing;
        }

        if (previous is null)
            properties.InsertAt(child, 0);
        else
            properties.InsertAfter(child, previous);
    }

    /// <summary>Where a child sits in <c>w:pPr</c>'s schema sequence, by XML local name.</summary>
    static int OrderOf(OpenXmlElement element) => element.LocalName switch
    {
        "pStyle" => 0,
        "keepNext" => 1,
        "keepLines" => 2,
        "pageBreakBefore" => 3,
        "framePr" => 4,
        "widowControl" => 5,
        "numPr" => 6,
        "pBdr" => 8,
        "shd" => 9,
        "tabs" => 10,
        "spacing" => 20,
        "ind" => 21,
        "contextualSpacing" => 22,
        "jc" => 30,
        "outlineLvl" => 40,
        "rPr" => 90,
        "sectPr" => 91,
        _ => 50
    };

    // ---- inline objects ----

    /// <summary>How much of the offset space one run occupies.</summary>
    static int LengthOfRun(Run run)
    {
        var length = 0;

        foreach (var child in run.ChildElements)
        {
            length += child switch
            {
                W.Text text => (text.Text ?? string.Empty).Length,
                TabChar => 4,
                W.Drawing => ObjectPlaceholder.Length,
                _ => 0
            };
        }

        return length;
    }

    /// <summary>
    /// Inserts a prepared run — a picture or a shape — at an offset, splitting text around it.
    /// </summary>
    /// <remarks>
    /// The run arrives already built by <see cref="WordContentFactory"/> rather than being described
    /// here, because the only thing this has to get right is <em>where</em> it goes; what a valid
    /// <c>w:drawing</c> looks like is the factory's problem.
    /// </remarks>
    public static void InsertObject(Paragraph paragraph, int offset, Run element)
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

            // Landing inside a text run means splitting it, so the object sits between the two
            // halves rather than jumping to whichever end was nearer.
            if (segmentElement is W.Text && offset > start && offset < end)
            {
                SplitAt(host, offset - start);
                host.InsertAfterSelf(element);
                return;
            }

            if (offset <= start)
                host.InsertBeforeSelf(element);
            else
                host.InsertAfterSelf(element);

            return;
        }

        // Past the last segment, or an empty paragraph: the object goes at the end.
        paragraph.AppendChild(element);
    }

    /// <summary>
    /// Resizes the inline object at an offset, returning false when there is none there.
    /// </summary>
    /// <remarks>
    /// Both extents are written: <c>wp:extent</c> on the wrapper, which is what decides the space the
    /// object takes in the flow, and the <c>a:ext</c> inside it, which is what the shape or picture
    /// is drawn into. Writing only the first gives an object that reserves the new size and still
    /// draws at the old one.
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
}
