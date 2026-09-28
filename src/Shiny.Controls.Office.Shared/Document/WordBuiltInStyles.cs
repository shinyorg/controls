using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Shiny.Controls.Office.Document;

/// <summary>
/// Word's built-in styles, as the definitions Word itself writes, for documents that do not have them.
/// </summary>
/// <remarks>
/// <para>
/// A paragraph pointed at <c>Heading1</c> in a document whose <c>styles.xml</c> has no such style is
/// drawn — by Word as well as here — in Normal. So applying a style from the gallery has to make sure
/// the definition exists first, and a document created by some other tool very often has only Normal.
/// </para>
/// <para>
/// The definitions carry Word's own ids, names, <c>basedOn</c> chains and priorities, which is what
/// makes Word recognise them as its built-ins after a round trip — they show in Word's gallery under
/// their usual names rather than as unknown custom styles.
/// </para>
/// </remarks>
static class WordBuiltInStyles
{
    /// <summary>The paragraph styles the gallery offers, in Word's order.</summary>
    public static IReadOnlyList<string> Gallery { get; } =
    [
        "Normal", "NoSpacing", "Heading1", "Heading2", "Heading3", "Title", "Subtitle", "Quote",
        "IntenseQuote", "ListParagraph"
    ];

    /// <summary>The definition for a built-in style id, or null for one this does not know.</summary>
    public static Style? Create(string styleId) => styleId switch
    {
        "Normal" => Paragraph("Normal", "Normal", null, priority: 0, isDefault: true),

        "NoSpacing" => Paragraph("NoSpacing", "No Spacing", null, priority: 1,
            paragraph: [new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }]),

        "Heading1" => Heading(1, "32", "2F5496", before: "240", after: "0"),
        "Heading2" => Heading(2, "26", "2F5496", before: "40", after: "0"),
        "Heading3" => Heading(3, "24", "1F3763", before: "40", after: "0"),

        "Title" => Paragraph("Title", "Title", "Normal", priority: 10,
            paragraph: [new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }, new ContextualSpacing()],
            run: [new RunFonts { AsciiTheme = ThemeFontValues.MajorHighAnsi, HighAnsiTheme = ThemeFontValues.MajorHighAnsi }, new Spacing { Val = -10 }, new Kern { Val = 28U }, new FontSize { Val = "56" }, new FontSizeComplexScript { Val = "56" }]),

        "Subtitle" => Paragraph("Subtitle", "Subtitle", "Normal", priority: 11,
            run: [new Color { Val = "5A5A5A" }, new Spacing { Val = 15 }]),

        "Quote" => Paragraph("Quote", "Quote", "Normal", priority: 29,
            paragraph: [new SpacingBetweenLines { Before = "200", After = "160" }, new Indentation { Left = "864", Right = "864" }, new Justification { Val = JustificationValues.Center }],
            run: [new Italic(), new ItalicComplexScript(), new Color { Val = "404040" }]),

        "IntenseQuote" => Paragraph("IntenseQuote", "Intense Quote", "Normal", priority: 30,
            paragraph:
            [
                new DocumentFormat.OpenXml.Wordprocessing.ParagraphBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4U, Space = 10U, Color = "4472C4" },
                    new BottomBorder { Val = BorderValues.Single, Size = 4U, Space = 10U, Color = "4472C4" }),
                new SpacingBetweenLines { Before = "360", After = "360" },
                new Indentation { Left = "864", Right = "864" },
                new Justification { Val = JustificationValues.Center }
            ],
            run: [new Italic(), new ItalicComplexScript(), new Color { Val = "4472C4" }]),

        "ListParagraph" => Paragraph("ListParagraph", "List Paragraph", "Normal", priority: 34,
            paragraph: [new Indentation { Left = "720" }, new ContextualSpacing()]),

        "TOCHeading" => Paragraph("TOCHeading", "TOC Heading", "Heading1", priority: 39,
            paragraph: [new OutlineLevel { Val = 9 }]),

        "TOC1" => Paragraph("TOC1", "toc 1", "Normal", priority: 39, paragraph: [new SpacingBetweenLines { After = "100" }]),
        "TOC2" => Paragraph("TOC2", "toc 2", "Normal", priority: 39, paragraph: [new SpacingBetweenLines { After = "100" }, new Indentation { Left = "220" }]),
        "TOC3" => Paragraph("TOC3", "toc 3", "Normal", priority: 39, paragraph: [new SpacingBetweenLines { After = "100" }, new Indentation { Left = "440" }]),

        "FootnoteText" => Paragraph("FootnoteText", "footnote text", "Normal", priority: 99,
            paragraph: [new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }],
            run: [new FontSize { Val = "20" }, new FontSizeComplexScript { Val = "20" }]),

        "CommentText" => Paragraph("CommentText", "annotation text", "Normal", priority: 99,
            paragraph: [new SpacingBetweenLines { Line = "240", LineRule = LineSpacingRuleValues.Auto }],
            run: [new FontSize { Val = "20" }, new FontSizeComplexScript { Val = "20" }]),

        "Header" => Paragraph("Header", "header", "Normal", priority: 99,
            paragraph: [new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }]),

        "Hyperlink" => Character("Hyperlink", "Hyperlink", priority: 99,
            run: [new Color { Val = "0563C1", ThemeColor = ThemeColorValues.Hyperlink }, new Underline { Val = UnderlineValues.Single }]),

        "FootnoteReference" => Character("FootnoteReference", "footnote reference", priority: 99,
            run: [new VerticalTextAlignment { Val = VerticalPositionValues.Superscript }]),

        "CommentReference" => Character("CommentReference", "annotation reference", priority: 99,
            run: [new FontSize { Val = "16" }, new FontSizeComplexScript { Val = "16" }]),

        _ => null
    };

    /// <summary>
    /// Adds the built-in definition for <paramref name="styleId"/> to the document when it is missing,
    /// together with whatever it is based on.
    /// </summary>
    /// <returns>True when <c>styles.xml</c> was changed.</returns>
    public static bool Ensure(MainDocumentPart main, string styleId)
    {
        var part = main.StyleDefinitionsPart ?? main.AddNewPart<StyleDefinitionsPart>();
        part.Styles ??= new Styles();

        return EnsureIn(part.Styles, styleId, depth: 0);
    }

    static bool EnsureIn(Styles styles, string styleId, int depth)
    {
        if (depth > 8 || styles.Elements<Style>().Any(x => string.Equals(x.StyleId?.Value, styleId, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (Create(styleId) is not { } style)
            return false;

        // A basedOn pointing at nothing is valid but reads as Normal; the chain is created with it so
        // TOC Heading actually looks like a heading.
        if (style.BasedOn?.Val?.Value is { } parent)
            EnsureIn(styles, parent, depth + 1);

        // A document with no default paragraph style gets Normal marked as one; one that has a default
        // under another id keeps it, and the new Normal is just another style.
        if (styleId == "Normal" && styles.Elements<Style>().Any(x => x.Default?.Value == true && x.Type?.Value == StyleValues.Paragraph))
            style.Default = null;

        styles.AppendChild(style);
        return true;
    }

    static Style Heading(int level, string halfPoints, string color, string before, string after)
        => Paragraph($"Heading{level}", $"heading {level}", "Normal", priority: 9,
            paragraph:
            [
                new KeepNext(),
                new KeepLines(),
                new SpacingBetweenLines { Before = before, After = after },
                new OutlineLevel { Val = level - 1 }
            ],
            run:
            [
                new RunFonts { AsciiTheme = ThemeFontValues.MajorHighAnsi, HighAnsiTheme = ThemeFontValues.MajorHighAnsi },
                new Color { Val = color },
                new FontSize { Val = halfPoints },
                new FontSizeComplexScript { Val = halfPoints }
            ],
            next: "Normal");

    static Style Paragraph(
        string id,
        string name,
        string? basedOn,
        int priority,
        bool isDefault = false,
        OpenXmlElement[]? paragraph = null,
        OpenXmlElement[]? run = null,
        string? next = null)
    {
        var style = new Style { Type = StyleValues.Paragraph, StyleId = id };

        if (isDefault)
            style.Default = true;

        // CT_Style is a sequence: name, aliases, basedOn, next, link, autoRedefine, hidden, uiPriority,
        // semiHidden, unhideWhenUsed, qFormat, locked, personal*, rsid, pPr, rPr, tblPr, trPr, tcPr.
        style.AppendChild(new StyleName { Val = name });

        if (basedOn is not null)
            style.AppendChild(new BasedOn { Val = basedOn });

        if (next is not null)
            style.AppendChild(new NextParagraphStyle { Val = next });

        style.AppendChild(new UIPriority { Val = priority });

        if (priority >= 99)
        {
            style.AppendChild(new SemiHidden());
            style.AppendChild(new UnhideWhenUsed());
        }
        else
        {
            style.AppendChild(new PrimaryStyle());
        }

        if (paragraph is { Length: > 0 })
            style.AppendChild(new StyleParagraphProperties(paragraph));

        if (run is { Length: > 0 })
            style.AppendChild(new StyleRunProperties(run));

        return style;
    }

    static Style Character(string id, string name, int priority, OpenXmlElement[] run)
    {
        var style = new Style { Type = StyleValues.Character, StyleId = id };
        style.AppendChild(new StyleName { Val = name });
        style.AppendChild(new BasedOn { Val = "DefaultParagraphFont" });
        style.AppendChild(new UIPriority { Val = priority });
        style.AppendChild(new UnhideWhenUsed());
        style.AppendChild(new StyleRunProperties(run));
        return style;
    }
}
