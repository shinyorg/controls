using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using W = DocumentFormat.OpenXml.Wordprocessing;
using TextAlignment = Shiny.Controls.Office.Text.TextAlignment;

namespace Shiny.Controls.Office.Document;

/// <summary>
/// The run- and paragraph-property mutations the formatting commands apply.
/// </summary>
/// <remarks>
/// <para>
/// Every child is placed at its schema position rather than appended. <c>w:rPr</c> and <c>w:pPr</c> are
/// sequences, not bags: a <c>w:color</c> after a <c>w:u</c> is a document Word reports as corrupt, and
/// appending in the order the buttons happened to be pressed produces exactly that.
/// </para>
/// <para>
/// Turning a toggle off writes an explicit <c>val="0"</c> rather than removing the element. Removing it
/// only drops the run's own formatting, so text that is bold because its style is — every heading —
/// stayed bold and the button appeared to do nothing.
/// </para>
/// </remarks>
static partial class WordParagraphEditor
{
    public static Action<RunProperties> ToggleBold(bool on) => properties =>
    {
        properties.RemoveAllChildren<Bold>();
        InsertOrdered(properties, on ? new Bold() : new Bold { Val = OnOffValue.FromBoolean(false) });
    };

    public static Action<RunProperties> ToggleItalic(bool on) => properties =>
    {
        properties.RemoveAllChildren<Italic>();
        InsertOrdered(properties, on ? new Italic() : new Italic { Val = OnOffValue.FromBoolean(false) });
    };

    public static Action<RunProperties> ToggleUnderline(bool on) => properties =>
    {
        properties.RemoveAllChildren<W.Underline>();
        InsertOrdered(properties, new W.Underline { Val = on ? UnderlineValues.Single : UnderlineValues.None });
    };

    public static Action<RunProperties> ToggleStrike(bool on) => properties =>
    {
        properties.RemoveAllChildren<Strike>();
        InsertOrdered(properties, on ? new Strike() : new Strike { Val = OnOffValue.FromBoolean(false) });
    };

    public static Action<RunProperties> SetFontFamily(string family) => properties =>
    {
        properties.RemoveAllChildren<RunFonts>();
        InsertOrdered(properties, new RunFonts { Ascii = family, HighAnsi = family, ComplexScript = family });
    };

    public static Action<RunProperties> SetFontSize(double points) => properties =>
    {
        properties.RemoveAllChildren<FontSize>();
        properties.RemoveAllChildren<FontSizeComplexScript>();

        // Word stores run size in half-points.
        var halfPoints = Math.Max(1, (int)Math.Round(points * 2)).ToString();
        InsertOrdered(properties, new FontSize { Val = halfPoints });
        InsertOrdered(properties, new FontSizeComplexScript { Val = halfPoints });
    };

    public static Action<RunProperties> SetColor(ArgbColor color) => properties =>
    {
        properties.RemoveAllChildren<W.Color>();
        InsertOrdered(properties, new W.Color { Val = $"{color.R:X2}{color.G:X2}{color.B:X2}" });
    };

    /// <summary>
    /// Sets or clears the highlight behind a run.
    /// </summary>
    /// <remarks>
    /// <c>w:highlight</c> takes a name from a closed list, not a colour, so the requested colour is
    /// resolved to the nearest one Word can express.
    /// </remarks>
    public static Action<RunProperties> SetHighlight(ArgbColor? color) => properties =>
    {
        properties.RemoveAllChildren<Highlight>();

        if (color is null)
            return;

        InsertOrdered(properties, new Highlight { Val = new EnumValue<HighlightColorValues>
        {
            InnerText = HighlightPalette.NameOf(color)
        } });
    };

    /// <summary>Superscript, subscript, or back to the baseline.</summary>
    public static Action<RunProperties> SetVerticalPosition(VerticalPosition position) => properties =>
    {
        properties.RemoveAllChildren<VerticalTextAlignment>();

        if (position == VerticalPosition.Baseline)
            return;

        InsertOrdered(properties, new VerticalTextAlignment
        {
            Val = position == VerticalPosition.Superscript
                ? VerticalPositionValues.Superscript
                : VerticalPositionValues.Subscript
        });
    };

    /// <summary>A character style, such as <c>Hyperlink</c>; null removes it.</summary>
    public static Action<RunProperties> SetRunStyle(string? styleId) => properties =>
    {
        properties.RemoveAllChildren<RunStyle>();

        if (styleId is not null)
            InsertOrdered(properties, new RunStyle { Val = styleId });
    };

    /// <summary>
    /// Clear All Formatting: every direct run property goes, except the revision marker.
    /// </summary>
    /// <remarks>
    /// A tracked format change (<c>w:rPrChange</c>) is history rather than formatting, and removing it
    /// would silently accept a change the reviewer has not accepted.
    /// </remarks>
    public static Action<RunProperties> ClearRunFormatting() => properties =>
    {
        foreach (var child in properties.ChildElements.ToList())
        {
            if (child is not RunPropertiesChange)
                child.Remove();
        }
    };

    /// <summary>Replaces a run's formatting with a copy of another's — what the format painter lays down.</summary>
    public static Action<RunProperties> CopyRunFormatting(RunProperties? source) => properties =>
    {
        foreach (var child in properties.ChildElements.ToList())
        {
            if (child is not RunPropertiesChange)
                child.Remove();
        }

        if (source is null)
            return;

        foreach (var child in source.ChildElements)
        {
            if (child is RunPropertiesChange)
                continue;

            InsertOrdered(properties, child.CloneNode(true));
        }
    };

    public static Action<ParagraphProperties> SetAlignment(TextAlignment alignment) => properties =>
    {
        properties.RemoveAllChildren<Justification>();
        InsertOrdered(properties, new Justification
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
            InsertOrdered(properties, new ParagraphStyleId { Val = styleId });
    };

    /// <summary>
    /// Sets the line spacing and the space above and below, leaving any it is not given alone.
    /// </summary>
    /// <param name="lineMultiple">1.0 for single, 2.0 for double; null leaves it.</param>
    /// <param name="beforePoints">Space above in points; null leaves it.</param>
    /// <param name="afterPoints">Space below in points; null leaves it.</param>
    public static Action<ParagraphProperties> SetSpacing(double? lineMultiple, double? beforePoints, double? afterPoints) => properties =>
    {
        var spacing = properties.GetFirstChild<SpacingBetweenLines>();
        if (spacing is null)
        {
            spacing = new SpacingBetweenLines();
            InsertOrdered(properties, spacing);
        }

        if (lineMultiple is { } multiple)
        {
            // "auto" expresses a multiple in 240ths of a line.
            spacing.Line = ((int)Math.Round(multiple * 240)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            spacing.LineRule = LineSpacingRuleValues.Auto;
        }

        if (beforePoints is { } before)
        {
            spacing.Before = ((int)Math.Round(Math.Max(0, before) * 20)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            spacing.BeforeAutoSpacing = null;
        }

        if (afterPoints is { } after)
        {
            spacing.After = ((int)Math.Round(Math.Max(0, after) * 20)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            spacing.AfterAutoSpacing = null;
        }
    };

    /// <summary>
    /// Writes the paragraph indents, in pixels. A negative first-line indent is a hanging indent.
    /// </summary>
    public static Action<ParagraphProperties> SetIndentation(double? left, double? right, double? firstLine) => properties =>
    {
        var indent = properties.GetFirstChild<Indentation>();
        if (indent is null)
        {
            indent = new Indentation();
            InsertOrdered(properties, indent);
        }

        string Twips(double pixels) => OoxmlUnits.PixelsToTwips(pixels).ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (left is { } l)
        {
            indent.Left = Twips(Math.Max(0, l));
            indent.Start = null;
        }

        if (right is { } r)
        {
            indent.Right = Twips(Math.Max(0, r));
            indent.End = null;
        }

        if (firstLine is { } first)
        {
            indent.FirstLine = null;
            indent.Hanging = null;
            indent.FirstLineChars = null;
            indent.HangingChars = null;

            if (first > 0)
                indent.FirstLine = Twips(first);
            else if (first < 0)
                indent.Hanging = Twips(-first);
        }
    };

    /// <summary>Shading behind the whole paragraph; null removes it.</summary>
    public static Action<ParagraphProperties> SetShading(ArgbColor? color) => properties =>
    {
        properties.RemoveAllChildren<Shading>();

        if (color is { } fill)
        {
            InsertOrdered(properties, new Shading
            {
                Val = ShadingPatternValues.Clear,
                Color = "auto",
                Fill = $"{fill.R:X2}{fill.G:X2}{fill.B:X2}"
            });
        }
    };

    /// <summary>
    /// The paragraph's border edges. Null, or a border with no edges, removes them.
    /// </summary>
    /// <remarks>
    /// <c>w:pBdr</c>'s own children are a sequence too — top, left, bottom, right — and are written in
    /// that order.
    /// </remarks>
    public static Action<ParagraphProperties> SetBorders(ParagraphBorders? borders) => properties =>
    {
        properties.RemoveAllChildren<W.ParagraphBorders>();

        if (borders is not { Any: true })
            return;

        var hex = $"{borders.Color.R:X2}{borders.Color.G:X2}{borders.Color.B:X2}";
        var size = (uint)Math.Clamp(Math.Round(borders.Width * 6), 2, 96);   // eighths of a point

        T Edge<T>() where T : BorderType, new()
            => new() { Val = BorderValues.Single, Size = size, Space = 1U, Color = hex };

        var element = new W.ParagraphBorders();

        if (borders.Top)
            element.AppendChild(Edge<TopBorder>());

        if (borders.Left)
            element.AppendChild(Edge<LeftBorder>());

        if (borders.Bottom)
            element.AppendChild(Edge<BottomBorder>());

        if (borders.Right)
            element.AppendChild(Edge<RightBorder>());

        InsertOrdered(properties, element);
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
    /// The direct indent goes with it. A level definition carries its own indent, and the reader only
    /// applies it when the paragraph has none of its own — so a paragraph indented by hand would keep
    /// that indent, which looks like the nesting silently not working.
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
    public static Action<ParagraphProperties> ShiftListLevel(int delta) => properties =>
    {
        if (properties.NumberingProperties is not { } numbering)
            return;

        var current = numbering.NumberingLevelReference?.Val?.Value ?? 0;
        var target = Math.Clamp(current + delta, 0, WordListDefinitions.Levels - 1);

        numbering.RemoveAllChildren<NumberingLevelReference>();

        // w:ilvl precedes w:numId in w:numPr, and unlike most of pPr this pair really is checked.
        numbering.InsertAt(new NumberingLevelReference { Val = target }, 0);

        properties.RemoveAllChildren<Indentation>();
    };

    // ---- schema order ----

    /// <summary>Inserts a child of <c>w:pPr</c> or <c>w:rPr</c> at its schema position.</summary>
    /// <remarks>
    /// Anything unranked sorts after everything ranked except the change-tracking records, which keeps
    /// a paragraph's <c>w:rPr</c> and <c>w:sectPr</c> — both of which really do belong at the end —
    /// where they were.
    /// </remarks>
    internal static void InsertOrdered(OpenXmlCompositeElement properties, OpenXmlElement child)
    {
        var order = properties is ParagraphProperties ? ParagraphOrder : RunOrder;
        var rank = Rank(order, child);
        OpenXmlElement? previous = null;

        foreach (var existing in properties.ChildElements)
        {
            if (Rank(order, existing) > rank)
                break;

            previous = existing;
        }

        if (previous is null)
            properties.InsertAt(child, 0);
        else
            properties.InsertAfter(child, previous);
    }

    static int Rank(IReadOnlyDictionary<string, int> order, OpenXmlElement element)
        => order.TryGetValue(element.LocalName, out var rank) ? rank : 500;

    /// <summary><c>CT_PPr</c>'s sequence, by local name.</summary>
    static readonly Dictionary<string, int> ParagraphOrder = Ranks(
        "pStyle", "keepNext", "keepLines", "pageBreakBefore", "framePr", "widowControl", "numPr",
        "suppressLineNumbers", "pBdr", "shd", "tabs", "suppressAutoHyphens", "kinsoku", "wordWrap",
        "overflowPunct", "topLinePunct", "autoSpaceDE", "autoSpaceDN", "bidi", "adjustRightInd",
        "snapToGrid", "spacing", "ind", "contextualSpacing", "mirrorIndents", "suppressOverlap", "jc",
        "textDirection", "textAlignment", "textboxTightWrap", "outlineLvl", "divId", "cnfStyle");

    /// <summary><c>CT_RPr</c>'s sequence, by local name.</summary>
    static readonly Dictionary<string, int> RunOrder = Ranks(
        "rStyle", "rFonts", "b", "bCs", "i", "iCs", "caps", "smallCaps", "strike", "dstrike", "outline",
        "shadow", "emboss", "imprint", "noProof", "snapToGrid", "vanish", "webHidden", "color", "spacing",
        "w", "kern", "position", "sz", "szCs", "highlight", "u", "effect", "bdr", "shd", "fitText",
        "vertAlign", "rtl", "cs", "em", "lang", "eastAsianLayout", "specVanish", "oMath");

    static Dictionary<string, int> Ranks(params string[] names)
    {
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < names.Length; i++)
            ranks[names[i]] = i;

        // These close the sequence whatever else a producer put in it.
        ranks["rPr"] = 900;
        ranks["sectPr"] = 910;
        ranks["pPrChange"] = 920;
        ranks["rPrChange"] = 920;
        return ranks;
    }
}


/// <summary>Where a run sits relative to the baseline.</summary>
public enum VerticalPosition
{
    Baseline,
    Superscript,
    Subscript
}
