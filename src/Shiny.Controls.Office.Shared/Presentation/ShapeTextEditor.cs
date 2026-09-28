using DocumentFormat.OpenXml;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using D = DocumentFormat.OpenXml.Drawing;

namespace Shiny.Controls.Office.Presentation;

/// <summary>
/// Surgical edits to the runs inside one DrawingML paragraph (<c>a:p</c>).
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to the Word editor's paragraph editor, and it follows the same rule: a run is
/// split only where an edit genuinely needs a boundary, and is never re-created. A run's
/// <c>a:rPr</c> carries language, hyperlinks, effects and theme-derived fills the model does not
/// represent — rebuilding one to change a character discards all of it.
/// </para>
/// <para>
/// DrawingML is fussier than WordprocessingML about child order: <c>a:rPr</c>'s children are a
/// sequence, not a set, and PowerPoint refuses to open a file whose run properties are out of order.
/// That is what <see cref="InsertOrdered"/> exists for.
/// </para>
/// </remarks>
static class ShapeTextEditor
{
    /// <summary>The paragraph's text as the reader projects it — the offset space edits use.</summary>
    public static string TextOf(D.Paragraph paragraph)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var (_, text) in Segments(paragraph))
            builder.Append(text);

        return builder.ToString();
    }

    public static int LengthOf(D.Paragraph paragraph)
    {
        var length = 0;
        foreach (var (_, text) in Segments(paragraph))
            length += text.Length;

        return length;
    }

    /// <summary>
    /// Text-bearing leaves in order, paired with the text they contribute.
    /// </summary>
    /// <remarks>
    /// A field (<c>a:fld</c>) — a slide number or date — contributes its cached text so the caret
    /// walks past it correctly, but is never edited: its text is PowerPoint's to regenerate.
    /// A line break contributes nothing, matching how the reader projects it.
    /// </remarks>
    static IEnumerable<(OpenXmlElement Element, string Text)> Segments(D.Paragraph paragraph)
    {
        foreach (var child in paragraph.ChildElements)
        {
            switch (child)
            {
                case D.Run run:
                    yield return (run, run.Text?.Text ?? string.Empty);
                    break;

                case D.Field field:
                    yield return (field, field.Text?.Text ?? string.Empty);
                    break;
            }
        }
    }

    /// <summary>Inserts text at an offset, adopting the formatting of the run it lands in.</summary>
    public static void Insert(D.Paragraph paragraph, int offset, string text)
    {
        if (text.Length == 0)
            return;

        var cursor = 0;
        foreach (var (element, segment) in Segments(paragraph).ToList())
        {
            var end = cursor + segment.Length;

            if (offset <= end && element is D.Run run)
            {
                var local = Math.Clamp(offset - cursor, 0, segment.Length);
                var value = run.Text?.Text ?? string.Empty;
                SetText(run, value[..local] + text + value[local..]);
                return;
            }

            cursor = end;
        }

        // An empty paragraph, or one whose only content is a field: start a run for the text. Its
        // properties are cloned from the paragraph's end-of-paragraph mark, which is where PowerPoint
        // records the formatting a user chose for text they have not typed yet.
        var created = new D.Run(new D.Text(text));
        if (paragraph.GetFirstChild<D.EndParagraphRunProperties>() is { } endProperties)
            created.RunProperties = CloneAsRunProperties(endProperties);

        // a:endParaRPr must stay last, so a new run goes in front of it rather than after.
        if (paragraph.GetFirstChild<D.EndParagraphRunProperties>() is { } mark)
            paragraph.InsertBefore(created, mark);
        else
            paragraph.AppendChild(created);
    }

    /// <summary>Deletes a half-open offset range, dropping runs left with no text.</summary>
    public static void Delete(D.Paragraph paragraph, int start, int end)
    {
        if (end <= start)
            return;

        // A line break is zero-width at the offset of the text after it, so a caret "after" it and one
        // "at the start of the next line" are the same offset. A deleted span takes only the breaks
        // strictly inside it: deleting the whole of one line's text must not also join it to the next.
        // Backspace over a break is the controller's, which removes the break alone.
        var position = 0;
        foreach (var child in paragraph.ChildElements.ToList())
        {
            switch (child)
            {
                case D.Run run:
                    position += (run.Text?.Text ?? string.Empty).Length;
                    break;

                case D.Field field:
                    position += (field.Text?.Text ?? string.Empty).Length;
                    break;

                case D.Break when position > start && position < end:
                    child.Remove();
                    break;
            }
        }

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

            if (element is D.Run run)
            {
                var value = run.Text?.Text ?? string.Empty;
                SetText(run, value[..from] + value[Math.Min(to, value.Length)..]);
            }
            else if (from == 0 && to == segment.Length)
            {
                // A field is atomic — half a slide number is meaningless — so it goes whole or not
                // at all.
                element.Remove();
            }
        }

        RemoveEmptyRuns(paragraph);
    }

    /// <summary>
    /// Applies a formatting change over an offset range, splitting runs at the boundaries.
    /// </summary>
    /// <remarks>
    /// The change is an action on the run's properties rather than a finished style, so everything it
    /// does not touch survives — turning on bold leaves the font, size, colour and italics alone.
    /// </remarks>
    public static void Format(D.Paragraph paragraph, int start, int end, Action<D.RunProperties> apply)
    {
        if (end <= start)
            return;

        // Split first, then format: the second pass sees runs whose boundaries already line up with
        // the range, so every run is wholly in or wholly out.
        SplitRunAt(paragraph, start);
        SplitRunAt(paragraph, end);

        var cursor = 0;
        foreach (var (element, segment) in Segments(paragraph).ToList())
        {
            var segmentStart = cursor;
            cursor += segment.Length;

            if (element is not D.Run run)
                continue;

            if (segmentStart >= end || cursor <= start)
                continue;

            apply(EnsureRunProperties(run));
        }
    }

    /// <summary>
    /// Formatting for text that does not exist yet.
    /// </summary>
    /// <remarks>
    /// With an empty paragraph there is no run to format, so the choice goes on the end-of-paragraph
    /// mark — which is exactly where PowerPoint keeps it, and where <see cref="Insert"/> reads it back
    /// from when the user finally types.
    /// </remarks>
    public static void FormatEndMark(D.Paragraph paragraph, Action<D.RunProperties> apply)
    {
        var end = paragraph.GetFirstChild<D.EndParagraphRunProperties>();
        if (end is null)
        {
            // It is the paragraph's own last child, not a child of a:pPr - a mistake that compiles
            // fine against the typed API of the wrong element and produces an unopenable file.
            end = new D.EndParagraphRunProperties();
            paragraph.AppendChild(end);
        }

        // The two types have identical children and neither derives from the other, so the change is
        // applied to a stand-in and the result copied across.
        var stand_in = new D.RunProperties();
        foreach (var child in end.ChildElements)
            stand_in.AppendChild(child.CloneNode(true));

        foreach (var attribute in end.GetAttributes())
            stand_in.SetAttribute(attribute);

        apply(stand_in);

        end.RemoveAllChildren();
        end.ClearAllAttributes();

        foreach (var attribute in stand_in.GetAttributes())
            end.SetAttribute(attribute);

        foreach (var child in stand_in.ChildElements)
            end.AppendChild(child.CloneNode(true));
    }

    /// <summary>Splits the run containing an offset, so a boundary exists exactly there.</summary>
    static void SplitRunAt(D.Paragraph paragraph, int offset)
    {
        var cursor = 0;
        foreach (var (element, segment) in Segments(paragraph).ToList())
        {
            var segmentStart = cursor;
            cursor += segment.Length;

            if (offset <= segmentStart || offset >= cursor)
                continue;

            if (element is not D.Run run)
                return;

            var local = offset - segmentStart;
            var value = run.Text?.Text ?? string.Empty;

            var tail = (D.Run)run.CloneNode(true);
            SetText(tail, value[local..]);
            SetText(run, value[..local]);

            paragraph.InsertAfter(tail, run);
            return;
        }
    }

    /// <summary>
    /// Splits a paragraph at an offset, returning the new one that follows it.
    /// </summary>
    /// <remarks>
    /// The tail inherits the original's paragraph properties — level, bullet, alignment and spacing —
    /// because pressing Enter inside a bulleted list is meant to produce another bullet at the same
    /// level, not an unformatted paragraph.
    /// </remarks>
    public static D.Paragraph Split(D.Paragraph paragraph, int offset)
    {
        var tail = new D.Paragraph();

        if (paragraph.ParagraphProperties is { } properties)
            tail.ParagraphProperties = (D.ParagraphProperties)properties.CloneNode(true);

        SplitRunAt(paragraph, offset);

        var cursor = 0;
        var moving = new List<OpenXmlElement>();

        foreach (var child in paragraph.ChildElements.ToList())
        {
            if (child is D.ParagraphProperties or D.EndParagraphRunProperties)
                continue;

            var length = child switch
            {
                D.Run run => (run.Text?.Text ?? string.Empty).Length,
                D.Field field => (field.Text?.Text ?? string.Empty).Length,
                _ => 0
            };

            // A zero-length child at the split point (a break) belongs to whichever side the caret is
            // not on, so it moves with the tail.
            if (cursor >= offset)
                moving.Add(child);

            cursor += length;
        }

        foreach (var child in moving)
        {
            child.Remove();
            tail.AppendChild(child);
        }

        RemoveEmptyRuns(paragraph);
        RemoveEmptyRuns(tail);
        return tail;
    }

    /// <summary>Appends one paragraph's content onto another, which is what Backspace at offset 0 does.</summary>
    public static void Merge(D.Paragraph target, D.Paragraph source)
    {
        // In front of the target's end mark, which must stay the paragraph's last child.
        var mark = target.GetFirstChild<D.EndParagraphRunProperties>();

        foreach (var child in source.ChildElements.ToList())
        {
            if (child is D.ParagraphProperties or D.EndParagraphRunProperties)
                continue;

            child.Remove();
            if (mark is not null)
                target.InsertBefore(child, mark);
            else
                target.AppendChild(child);
        }

        source.Remove();
    }

    public static void FormatParagraph(D.Paragraph paragraph, Action<D.ParagraphProperties> apply)
        => apply(paragraph.ParagraphProperties ??= new D.ParagraphProperties());

    // ---- element plumbing ----

    static D.RunProperties EnsureRunProperties(D.Run run)
    {
        if (run.RunProperties is { } existing)
            return existing;

        var properties = new D.RunProperties();

        // a:rPr is the *first* child of a:r; appending it would put it after a:t, which PowerPoint
        // rejects outright rather than ignoring.
        run.InsertAt(properties, 0);
        return properties;
    }

    static D.RunProperties CloneAsRunProperties(D.EndParagraphRunProperties source)
    {
        var properties = new D.RunProperties();

        foreach (var attribute in source.GetAttributes())
            properties.SetAttribute(attribute);

        foreach (var child in source.ChildElements)
            properties.AppendChild(child.CloneNode(true));

        return properties;
    }

    /// <summary>
    /// Sets a run's text.
    /// </summary>
    /// <remarks>
    /// No <c>xml:space="preserve"</c> here, unlike the Word editor: <c>a:t</c> is a plain string in the
    /// DrawingML schema and never has its whitespace collapsed, so a run ending in a space keeps it.
    /// </remarks>
    static void SetText(D.Run run, string value)
    {
        var text = run.Text ??= new D.Text();
        text.Text = value;
    }

    /// <summary>
    /// Drops runs with no text left.
    /// </summary>
    /// <remarks>
    /// An empty run is not harmless: PowerPoint shows it in the run list and it keeps the paragraph
    /// looking non-empty, so the end-of-paragraph formatting is never consulted.
    /// </remarks>
    static void RemoveEmptyRuns(D.Paragraph paragraph)
    {
        foreach (var run in paragraph.Elements<D.Run>().ToList())
        {
            if (string.IsNullOrEmpty(run.Text?.Text))
                run.Remove();
        }
    }

    /// <summary>
    /// Inserts a child into <c>a:rPr</c> at its schema position.
    /// </summary>
    /// <remarks>
    /// The sequence is <c>ln, fill, effectLst, highlight, uLn, uFill, latin, ea, cs, sym, hlink*,
    /// rtl, extLst</c>. Appending instead — which works fine in WordprocessingML — produces a file
    /// PowerPoint reports as corrupt and refuses to repair.
    /// </remarks>
    static void InsertOrdered(D.RunProperties properties, OpenXmlElement child)
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

    /// <summary>
    /// Where a child sits in <c>a:rPr</c>'s schema sequence.
    /// </summary>
    /// <remarks>
    /// Matched on the XML local name rather than the CLR type: the SDK does not surface a distinct
    /// type for every one of these (<c>a:uLn</c> among them), and the local names are what the schema
    /// is actually written in.
    /// </remarks>
    internal static int OrderOf(OpenXmlElement element) => element.LocalName switch
    {
        "ln" => 0,
        "noFill" or "solidFill" or "gradFill" or "blipFill" or "pattFill" or "grpFill" => 1,
        "effectLst" or "effectDag" => 2,
        "highlight" => 3,
        "uLnTx" or "uLn" => 4,
        "uFillTx" or "uFill" => 5,
        "latin" => 6,
        "ea" => 7,
        "cs" => 8,
        "sym" => 9,
        "hlinkClick" => 10,
        "hlinkMouseOver" => 11,
        "rtl" => 12,
        _ => 13
    };

    // ---- the mutations a toolbar applies ----

    public static Action<D.RunProperties> ToggleBold(bool on) => properties => properties.Bold = on;

    public static Action<D.RunProperties> ToggleItalic(bool on) => properties => properties.Italic = on;

    public static Action<D.RunProperties> ToggleUnderline(bool on) => properties =>
        properties.Underline = on ? D.TextUnderlineValues.Single : D.TextUnderlineValues.None;

    public static Action<D.RunProperties> ToggleStrike(bool on) => properties =>
        properties.Strike = on ? D.TextStrikeValues.SingleStrike : D.TextStrikeValues.NoStrike;

    /// <summary>Font size, in hundredths of a point — DrawingML's unit, unlike Word's half-points.</summary>
    public static Action<D.RunProperties> SetFontSize(double points) => properties =>
        properties.FontSize = (int)Math.Round(Math.Clamp(points, 1, 400) * 100);

    public static Action<D.RunProperties> SetFontFamily(string family) => properties =>
    {
        foreach (var existing in properties.Elements<D.LatinFont>().ToList())
            existing.Remove();

        InsertOrdered(properties, new D.LatinFont { Typeface = family });
    };

    public static Action<D.RunProperties> SetColor(ArgbColor color) => properties =>
    {
        // Every fill kind occupies the same slot, so the old one has to go rather than sit alongside
        // the new: two fills is not "the last one wins", it is invalid.
        foreach (var existing in properties.ChildElements.Where(IsFill).ToList())
            existing.Remove();

        InsertOrdered(properties, new D.SolidFill(
            new D.RgbColorModelHex { Val = $"{color.R:X2}{color.G:X2}{color.B:X2}" }));
    };

    /// <summary>A theme colour for the text (<c>a:schemeClr</c>), so it follows the theme.</summary>
    public static Action<D.RunProperties> SetThemeColor(string scheme) => properties =>
    {
        foreach (var existing in properties.ChildElements.Where(IsFill).ToList())
            existing.Remove();

        InsertOrdered(properties, new D.SolidFill(SlideSchemeColor.Build(scheme)));
    };

    /// <summary>
    /// Sets or clears the highlight behind a run.
    /// </summary>
    /// <remarks>
    /// Unlike Word's, DrawingML's <c>a:highlight</c> holds a real colour rather than a name from a
    /// closed list, so the requested colour goes in exactly as asked. It has a fixed slot in the run
    /// properties (after the effect list, before the underline fills), which is what
    /// <see cref="InsertOrdered"/> is for — appending it lands it after <c>a:latin</c> and produces a
    /// file PowerPoint refuses to open.
    /// </remarks>
    public static Action<D.RunProperties> SetHighlight(ArgbColor? color) => properties =>
    {
        foreach (var existing in properties.Elements<D.Highlight>().ToList())
            existing.Remove();

        if (color is not { } value)
            return;

        InsertOrdered(properties, new D.Highlight(
            new D.RgbColorModelHex { Val = $"{value.R:X2}{value.G:X2}{value.B:X2}" }));
    };

    static bool IsFill(OpenXmlElement element) => OrderOf(element) == 1;

    /// <summary>
    /// Links a run, or unlinks it when <paramref name="relationshipId"/> and <paramref name="action"/>
    /// are both null.
    /// </summary>
    /// <remarks>
    /// <c>a:hlinkClick</c> has its own slot near the end of <c>a:rPr</c>, after the fonts. An empty
    /// <c>r:id</c> is legal and is what a show-jump action ("next slide") carries.
    /// </remarks>
    public static Action<D.RunProperties> SetHyperlink(string? relationshipId, string? action) => properties =>
    {
        foreach (var existing in properties.Elements<D.HyperlinkOnClick>().ToList())
            existing.Remove();

        if (relationshipId is null && action is null)
            return;

        var link = new D.HyperlinkOnClick { Id = relationshipId ?? string.Empty };
        if (action is not null)
            link.Action = action;

        InsertOrdered(properties, link);
    };

    /// <summary>Superscript (positive), subscript (negative) or neither (zero), as a percentage of the size.</summary>
    public static Action<D.RunProperties> SetBaseline(int percent) => properties =>
        properties.Baseline = percent == 0 ? null : percent * 1000;

    /// <summary>Character spacing in points — <c>spc</c>, stored in hundredths of a point.</summary>
    public static Action<D.RunProperties> SetCharacterSpacing(double points) => properties =>
        properties.Spacing = Math.Abs(points) < 0.01 ? null : (int)Math.Round(points * 100);

    /// <summary>
    /// Clears Font ▸ Clear All Formatting: every run property but the language, and the colour, font,
    /// highlight and link children.
    /// </summary>
    /// <remarks>
    /// The language stays because it is not formatting — it is what spelling and hyphenation read —
    /// and a link stays because PowerPoint's Clear Formatting does not remove links either.
    /// </remarks>
    public static Action<D.RunProperties> ClearFormatting() => properties =>
    {
        var language = properties.Language?.Value;
        var alternative = properties.AlternativeLanguage?.Value;
        var links = properties.Elements<D.HyperlinkOnClick>().Select(x => (OpenXmlElement)x.CloneNode(true)).ToList();

        properties.ClearAllAttributes();
        properties.RemoveAllChildren();

        if (language is not null)
            properties.Language = language;

        if (alternative is not null)
            properties.AlternativeLanguage = alternative;

        foreach (var link in links)
            InsertOrdered(properties, link);
    };

    /// <summary>Line spacing as a multiple of single — <c>a:lnSpc/a:spcPct</c>.</summary>
    public static Action<D.ParagraphProperties> SetLineSpacing(double multiple) => properties =>
    {
        foreach (var existing in properties.Elements<D.LineSpacing>().ToList())
            existing.Remove();

        InsertOrdered(properties, new D.LineSpacing(new D.SpacingPercent { Val = (int)Math.Round(Math.Clamp(multiple, 0.5, 10) * 100000) }));
    };

    /// <summary>Space before or after a paragraph, in points — <c>a:spcBef</c>/<c>a:spcAft</c>.</summary>
    public static Action<D.ParagraphProperties> SetParagraphSpacing(double? before, double? after) => properties =>
    {
        if (before is { } b)
        {
            foreach (var existing in properties.Elements<D.SpaceBefore>().ToList())
                existing.Remove();

            InsertOrdered(properties, new D.SpaceBefore(new D.SpacingPoints { Val = (int)Math.Round(Math.Max(0, b) * 100) }));
        }

        if (after is { } a)
        {
            foreach (var existing in properties.Elements<D.SpaceAfter>().ToList())
                existing.Remove();

            InsertOrdered(properties, new D.SpaceAfter(new D.SpacingPoints { Val = (int)Math.Round(Math.Max(0, a) * 100) }));
        }
    };

    /// <summary>
    /// Inserts a soft line break (<c>a:br</c>) at an offset — Shift+Enter.
    /// </summary>
    /// <remarks>
    /// The break takes a copy of the run properties either side of it, which is what PowerPoint writes:
    /// an <c>a:br</c> carries its own <c>a:rPr</c> so the empty line it opens has a height.
    /// </remarks>
    public static void InsertBreak(D.Paragraph paragraph, int offset)
    {
        SplitRunAt(paragraph, offset);

        var cursor = 0;
        OpenXmlElement? after = null;
        D.RunProperties? model = null;

        foreach (var child in paragraph.ChildElements)
        {
            if (child is D.ParagraphProperties)
            {
                after = child;
                continue;
            }

            var length = child switch
            {
                D.Run run => (run.Text?.Text ?? string.Empty).Length,
                D.Field field => (field.Text?.Text ?? string.Empty).Length,
                _ => 0
            };

            if (child is D.EndParagraphRunProperties || cursor + length > offset)
                break;

            // Everything wholly before the offset, and any breaks sitting exactly at it: a second break
            // at the same offset goes after the first.
            cursor += length;
            after = child;

            if (child is D.Run { RunProperties: { } runProperties })
                model = runProperties;
        }

        var properties = model?.CloneNode(true) as D.RunProperties
            ?? (paragraph.GetFirstChild<D.EndParagraphRunProperties>() is { } end ? CloneAsRunProperties(end) : new D.RunProperties { Language = "en-US" });

        var lineBreak = new D.Break(properties);

        if (after is null)
            paragraph.PrependChild(lineBreak);
        else
            paragraph.InsertAfter(lineBreak, after);
    }

    /// <summary>The breaks sitting at an offset — zero-width, so several can share one.</summary>
    public static IReadOnlyList<D.Break> BreaksAt(D.Paragraph paragraph, int offset)
    {
        var result = new List<D.Break>();
        var cursor = 0;

        foreach (var child in paragraph.ChildElements)
        {
            switch (child)
            {
                case D.Run run:
                    cursor += (run.Text?.Text ?? string.Empty).Length;
                    break;

                case D.Field field:
                    cursor += (field.Text?.Text ?? string.Empty).Length;
                    break;

                case D.Break lineBreak when cursor == offset:
                    result.Add(lineBreak);
                    break;
            }

            if (cursor > offset)
                break;
        }

        return result;
    }

    /// <summary>
    /// Rewrites the case of the text in a range — Change Case — run by run, so no formatting moves.
    /// </summary>
    public static void ChangeCase(D.Paragraph paragraph, int start, int end, Func<string, int, string> transform)
    {
        if (end <= start)
            return;

        SplitRunAt(paragraph, start);
        SplitRunAt(paragraph, end);

        // The paragraph's text before the change, so a sentence-case rule can see what precedes the
        // piece it is rewriting.
        var cursor = 0;
        foreach (var (element, segment) in Segments(paragraph).ToList())
        {
            var segmentStart = cursor;
            cursor += segment.Length;

            if (element is not D.Run run || segmentStart >= end || cursor <= start)
                continue;

            SetText(run, transform(run.Text?.Text ?? string.Empty, segmentStart));
        }
    }

    public static Action<D.ParagraphProperties> SetAlignment(TextAlignment alignment) => properties =>
        properties.Alignment = alignment switch
        {
            TextAlignment.Center => D.TextAlignmentTypeValues.Center,
            TextAlignment.Right => D.TextAlignmentTypeValues.Right,
            TextAlignment.Justify => D.TextAlignmentTypeValues.Justified,
            _ => D.TextAlignmentTypeValues.Left
        };

    /// <summary>Outline level, 0-8. This is what makes a bullet nest.</summary>
    public static Action<D.ParagraphProperties> SetLevel(int level) => properties =>
        properties.Level = Math.Clamp(level, 0, 8);

    /// <summary>
    /// Moves a paragraph in or out one outline level.
    /// </summary>
    /// <remarks>
    /// Reads the level off the properties rather than taking it as an argument, so a Tab over a
    /// selection spanning two levels moves both relative to where they were instead of flattening
    /// them onto the first one's level.
    /// </remarks>
    public static Action<D.ParagraphProperties> ShiftLevel(int delta) => properties =>
        properties.Level = Math.Clamp((properties.Level?.Value ?? 0) + delta, 0, 8);

    /// <summary>
    /// Sets the mark in front of a paragraph — a bullet glyph, an auto number, or nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All three are written explicitly, <see cref="ListStyle.None"/> included. A paragraph in a body
    /// placeholder inherits a bullet from the master's list style, so removing the element would put
    /// the inherited bullet back rather than take the bullet away; <c>a:buNone</c> is the only way to
    /// say no.
    /// </para>
    /// <para>
    /// The bullet font goes with the glyph. <c>a:buChar</c> is a code point in whatever face
    /// <c>a:buFont</c> names, so an Arial bullet written without one renders in the run's font — which
    /// for the Symbol code points PowerPoint normally uses is a wrong character or a blank.
    /// </para>
    /// </remarks>
    public static Action<D.ParagraphProperties> SetBullet(ListStyle style) => properties =>
    {
        foreach (var existing in properties.ChildElements.Where(IsBulletChoice).ToList())
            existing.Remove();

        foreach (var font in properties.Elements<D.BulletFont>().ToList())
            font.Remove();

        switch (style)
        {
            case ListStyle.Bullet:
                InsertOrdered(properties, new D.BulletFont { Typeface = "Arial" });
                InsertOrdered(properties, new D.CharacterBullet { Char = "\u2022" });
                break;

            case ListStyle.Numbered:
                InsertOrdered(properties, new D.AutoNumberedBullet
                {
                    Type = D.TextAutoNumberSchemeValues.ArabicPeriod,
                    StartAt = 1
                });
                break;

            default:
                InsertOrdered(properties, new D.NoBullet());
                break;
        }
    };

    /// <summary>The three mutually exclusive bullet elements — only one may be present.</summary>
    static bool IsBulletChoice(OpenXmlElement element)
        => element is D.NoBullet or D.CharacterBullet or D.AutoNumberedBullet or D.PictureBullet;

    /// <summary>
    /// Inserts a child into <c>a:pPr</c> at its schema position.
    /// </summary>
    /// <remarks>
    /// The same rule as <c>a:rPr</c>, and the same consequence for breaking it: <c>a:pPr</c>'s
    /// children are a sequence, so a <c>a:buChar</c> appended after the <c>a:defRPr</c> that was
    /// already there produces a file PowerPoint reports as corrupt rather than repairing.
    /// </remarks>
    static void InsertOrdered(D.ParagraphProperties properties, OpenXmlElement child)
    {
        var rank = ParagraphOrderOf(child);
        OpenXmlElement? previous = null;

        foreach (var existing in properties.ChildElements)
        {
            if (ParagraphOrderOf(existing) > rank)
                break;

            previous = existing;
        }

        if (previous is null)
            properties.InsertAt(child, 0);
        else
            properties.InsertAfter(child, previous);
    }

    /// <summary>Where a child sits in <c>a:pPr</c>'s schema sequence, by XML local name.</summary>
    static int ParagraphOrderOf(OpenXmlElement element) => element.LocalName switch
    {
        "lnSpc" => 0,
        "spcBef" => 1,
        "spcAft" => 2,
        "buClrTx" or "buClr" => 3,
        "buSzTx" or "buSzPct" or "buSzPts" => 4,
        "buFontTx" or "buFont" => 5,
        "buNone" or "buAutoNum" or "buChar" or "buBlip" => 6,
        "tabLst" => 7,
        "defRPr" => 8,
        "extLst" => 9,
        _ => 10
    };
}
