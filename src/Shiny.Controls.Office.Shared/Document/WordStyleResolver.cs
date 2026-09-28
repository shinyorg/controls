using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using TextAlignment = Shiny.Controls.Office.Text.TextAlignment;

namespace Shiny.Controls.Office.Document;

/// <summary>
/// Flattens Word's style chain into concrete formatting.
/// </summary>
/// <remarks>
/// Word resolves formatting in layers: document defaults, then the named style with its whole
/// <c>basedOn</c> ancestry applied outermost-first, then direct formatting on the paragraph or run.
/// Skipping the ancestry is the usual shortcut and it is why documents built on custom styles render
/// with the wrong font — a style that only overrides colour inherits everything else from its parent.
/// </remarks>
sealed class WordStyleResolver
{
    readonly Dictionary<string, Style> styles = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, TextStyle> resolvedRunStyles = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, ParagraphFormat> resolvedParagraphStyles = new(StringComparer.OrdinalIgnoreCase);
    readonly ThemeFonts themeFonts;

    TextStyle documentRunDefault = TextStyle.Default;
    ParagraphFormat documentParagraphDefault = ParagraphFormat.Default;

    public WordStyleResolver(MainDocumentPart main)
    {
        this.themeFonts = ThemeFonts.From(main.ThemePart);
        this.Reload(main);
    }

    /// <summary>
    /// Re-reads <c>styles.xml</c> after a style has been added to it.
    /// </summary>
    /// <remarks>
    /// Resolved styles are cached for the life of the document, so a paragraph pointed at a style
    /// created a moment ago would otherwise be read back with the formatting of a style that does not
    /// exist — which is Normal, and looks like the gallery doing nothing.
    /// </remarks>
    public void Reload(MainDocumentPart main)
    {
        this.styles.Clear();
        this.resolvedRunStyles.Clear();
        this.resolvedParagraphStyles.Clear();
        this.documentRunDefault = TextStyle.Default;
        this.documentParagraphDefault = ParagraphFormat.Default;

        var part = main.StyleDefinitionsPart?.Styles;
        if (part is null)
            return;

        foreach (var style in part.Elements<Style>())
        {
            if (style.StyleId?.Value is { } id)
                this.styles[id] = style;
        }

        if (part.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle is { } runDefaults)
            this.documentRunDefault = ApplyRunProperties(TextStyle.Default, runDefaults, this.themeFonts);

        if (part.DocDefaults?.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle is { } paragraphDefaults)
            this.documentParagraphDefault = ApplyParagraphProperties(ParagraphFormat.Default, paragraphDefaults);

        // Word marks one style as the default for paragraphs; it sits between doc defaults and
        // whatever style a paragraph names.
        var defaultParagraphStyle = part.Elements<Style>()
            .FirstOrDefault(x => x.Type?.Value == StyleValues.Paragraph && x.Default?.Value == true);

        if (defaultParagraphStyle?.StyleId?.Value is { } defaultId)
        {
            this.documentRunDefault = this.RunStyleFor(defaultId, this.documentRunDefault);
            this.documentParagraphDefault = this.ParagraphFormatFor(defaultId, this.documentParagraphDefault);
        }
    }

    public TextStyle DefaultRunStyle => this.documentRunDefault;

    /// <summary>Every style definition, keyed by id.</summary>
    public IReadOnlyDictionary<string, Style> Styles => this.styles;

    /// <summary>True when the document defines a style with this id.</summary>
    public bool Has(string styleId) => this.styles.ContainsKey(styleId);

    public string? StyleName(string? styleId)
        => styleId is not null && this.styles.TryGetValue(styleId, out var style)
            ? style.StyleName?.Val?.Value ?? styleId
            : styleId;

    /// <summary>Run formatting for a paragraph's named style, with the whole basedOn chain applied.</summary>
    public TextStyle RunStyleFor(string? styleId, TextStyle? baseStyle = null)
    {
        var start = baseStyle ?? this.documentRunDefault;
        if (styleId is null)
            return start;

        if (baseStyle is null && this.resolvedRunStyles.TryGetValue(styleId, out var cached))
            return cached;

        var chain = this.Chain(styleId);
        var result = start;
        foreach (var style in chain)
        {
            if (style.StyleRunProperties is { } properties)
                result = ApplyRunProperties(result, properties, this.themeFonts);
        }

        if (baseStyle is null)
            this.resolvedRunStyles[styleId] = result;

        return result;
    }

    public ParagraphFormat ParagraphFormatFor(string? styleId, ParagraphFormat? baseFormat = null)
    {
        var start = baseFormat ?? this.documentParagraphDefault;
        if (styleId is null)
            return start;

        if (baseFormat is null && this.resolvedParagraphStyles.TryGetValue(styleId, out var cached))
            return cached;

        var result = start;
        foreach (var style in this.Chain(styleId))
        {
            if (style.StyleParagraphProperties is { } properties)
                result = ApplyParagraphProperties(result, properties);
        }

        if (baseFormat is null)
            this.resolvedParagraphStyles[styleId] = result;

        return result;
    }

    /// <summary>The style's ancestry, outermost first, so nearer styles override further ones.</summary>
    List<Style> Chain(string styleId)
    {
        var chain = new List<Style>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = styleId;

        while (current is not null && visited.Add(current) && this.styles.TryGetValue(current, out var style))
        {
            chain.Add(style);
            current = style.BasedOn?.Val?.Value;
        }

        chain.Reverse();
        return chain;
    }

    public static TextStyle ApplyRunProperties(TextStyle style, OpenXmlElement properties, ThemeFonts themeFonts)
    {
        foreach (var child in properties.ChildElements)
        {
            switch (child)
            {
                case RunFonts fonts:
                    var name = fonts.Ascii?.Value
                        ?? fonts.HighAnsi?.Value
                        ?? themeFonts.Resolve(OoxmlUnits.EnumAttribute(fonts, "asciiTheme"));

                    if (!string.IsNullOrEmpty(name))
                        style = style with { FontFamily = name };

                    break;

                case FontSize size when double.TryParse(size.Val?.Value, out var halfPoints):
                    style = style with { FontSize = OoxmlUnits.HalfPointsToPixels(halfPoints) };
                    break;

                case Bold bold:
                    style = style with { Bold = IsOn(bold.Val) };
                    break;

                case Italic italic:
                    style = style with { Italic = IsOn(italic.Val) };
                    break;

                case Strike strike:
                    style = style with { Strike = IsOn(strike.Val) };
                    break;

                case Underline underline:
                    style = style with
                    {
                        Underline = underline.Val?.Value switch
                        {
                            null => UnderlineStyle.Single,
                            var v when v == UnderlineValues.None => UnderlineStyle.None,
                            var v when v == UnderlineValues.Double => UnderlineStyle.Double,
                            _ => UnderlineStyle.Single
                        }
                    };

                    break;

                case DocumentFormat.OpenXml.Wordprocessing.Color color:
                    // "auto" means "whatever contrasts with the background", which for a viewer is
                    // the default text colour rather than a colour of its own.
                    if (color.Val?.Value is { } hex && !hex.Equals("auto", StringComparison.OrdinalIgnoreCase) && TryParseHex(hex, out var parsed))
                        style = style with { Color = parsed };

                    break;

                case Highlight highlight when highlight.Val?.InnerText is { Length: > 0 } value:
                    // Read off the typed Val rather than by attribute name: w:highlight's val is in
                    // the w namespace, and asking for it in the empty one throws rather than
                    // returning null — which meant every document carrying a highlight failed to
                    // open at all.
                    style = style with { Highlight = HighlightPalette.ColorOf(value) };
                    break;

                case VerticalTextAlignment vertical when vertical.Val?.Value is { } alignment:
                    style = alignment switch
                    {
                        var v when v == VerticalPositionValues.Superscript => style with { BaselineShift = 0.33, SizeScale = 0.65 },
                        var v when v == VerticalPositionValues.Subscript => style with { BaselineShift = -0.15, SizeScale = 0.65 },
                        _ => style with { BaselineShift = 0, SizeScale = 1 }
                    };

                    break;
            }
        }

        return style;
    }

    public static ParagraphFormat ApplyParagraphProperties(ParagraphFormat format, OpenXmlElement properties)
    {
        foreach (var child in properties.ChildElements)
        {
            switch (child)
            {
                case Justification justification when justification.Val?.Value is { } value:
                    format = format with
                    {
                        Alignment = value switch
                        {
                            var v when v == JustificationValues.Center => TextAlignment.Center,
                            var v when v == JustificationValues.Right => TextAlignment.Right,
                            var v when v == JustificationValues.Both || v == JustificationValues.Distribute => TextAlignment.Justify,
                            _ => TextAlignment.Left
                        }
                    };

                    break;

                case Indentation indent:
                    if (indent.Left?.Value is { } left && double.TryParse(left, out var leftTwips))
                        format = format with { IndentLeft = OoxmlUnits.TwipsToPixels(leftTwips) };

                    if (indent.Right?.Value is { } right && double.TryParse(right, out var rightTwips))
                        format = format with { IndentRight = OoxmlUnits.TwipsToPixels(rightTwips) };

                    if (indent.FirstLine?.Value is { } first && double.TryParse(first, out var firstTwips))
                        format = format with { IndentFirstLine = OoxmlUnits.TwipsToPixels(firstTwips) };

                    // A hanging indent is a negative first-line indent.
                    if (indent.Hanging?.Value is { } hanging && double.TryParse(hanging, out var hangingTwips))
                        format = format with { IndentFirstLine = -OoxmlUnits.TwipsToPixels(hangingTwips) };

                    break;

                case SpacingBetweenLines spacing:
                    if (spacing.Before?.Value is { } before && double.TryParse(before, out var beforeTwips))
                        format = format with { SpaceBefore = OoxmlUnits.TwipsToPixels(beforeTwips) };

                    if (spacing.After?.Value is { } after && double.TryParse(after, out var afterTwips))
                        format = format with { SpaceAfter = OoxmlUnits.TwipsToPixels(afterTwips) };

                    if (spacing.Line?.Value is { } line && double.TryParse(line, out var lineValue))
                    {
                        var rule = spacing.LineRule?.Value;

                        // "auto" expresses a multiple in 240ths; the exact rules are absolute twips,
                        // which a reflow view approximates as a multiple of a 12pt line.
                        format = format with
                        {
                            LineSpacing = rule == LineSpacingRuleValues.Auto || rule is null
                                ? Math.Max(0.5, lineValue / 240d)
                                : Math.Max(0.5, OoxmlUnits.TwipsToPixels(lineValue) / 16d)
                        };
                    }

                    break;

                case DocumentFormat.OpenXml.Wordprocessing.ParagraphBorders borders:
                    format = format with { Borders = ReadBorders(borders) };
                    break;

                case SectionProperties section:
                    // A sectPr inside pPr is a section break at the end of this paragraph. Its w:type
                    // says what the *next* section does; absent, that is a new page.
                    format = format with
                    {
                        SectionBreak = OoxmlUnits.EnumAttribute(section.GetFirstChild<SectionType>(), "val") switch
                        {
                            "continuous" => SectionBreakKind.Continuous,
                            "evenPage" => SectionBreakKind.EvenPage,
                            "oddPage" => SectionBreakKind.OddPage,
                            _ => SectionBreakKind.NextPage
                        }
                    };

                    break;

                case PageBreakBefore pageBreak:
                    format = format with { PageBreakBefore = IsOn(pageBreak.Val) };
                    break;

                case OutlineLevel outline when outline.Val?.Value is { } level:
                    format = format with { OutlineLevel = level < 9 ? level + 1 : 0 };
                    break;

                case Shading shading when shading.Fill?.Value is { } fill &&
                                          !fill.Equals("auto", StringComparison.OrdinalIgnoreCase) &&
                                          TryParseHex(fill, out var shade):
                    format = format with { Shading = shade };
                    break;
            }
        }

        return format;
    }

    /// <summary>
    /// A toggle element with no <c>val</c> means on. Present-but-false is how a style turns off
    /// something an ancestor turned on.
    /// </summary>
    static bool IsOn(OnOffValue? value) => value is null || value.Value;

    /// <summary>The edges a <c>w:pBdr</c> draws, or null when every edge is off.</summary>
    static ParagraphBorders? ReadBorders(DocumentFormat.OpenXml.Wordprocessing.ParagraphBorders element)
    {
        static bool Drawn(BorderType? edge)
        {
            var value = OoxmlUnits.EnumAttribute(edge, "val");
            return value is not null && value is not ("none" or "nil");
        }

        var edges = new[] { (BorderType?)element.TopBorder, element.BottomBorder, element.LeftBorder, element.RightBorder };
        var first = edges.FirstOrDefault(Drawn);
        if (first is null)
            return null;

        var color = first.Color?.Value is { } hex && TryParseHex(hex, out var parsed)
            ? parsed
            : new ArgbColor(255, 0, 0, 0);

        return new ParagraphBorders
        {
            Top = Drawn(element.TopBorder),
            Bottom = Drawn(element.BottomBorder),
            Left = Drawn(element.LeftBorder),
            Right = Drawn(element.RightBorder),
            Color = color,
            Width = Math.Max(1, (first.Size?.Value ?? 4) / 6d)
        };
    }

    public static bool TryParseHex(string hex, out ArgbColor color)
    {
        color = default;
        var span = hex.AsSpan().TrimStart('#');
        if (span.Length != 6 || !uint.TryParse(span, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return false;

        color = ArgbColor.FromUInt32(0xFF000000u | rgb);
        return true;
    }

}

/// <summary>The major/minor font pair a theme defines, which styles reference instead of a font name.</summary>
sealed record ThemeFonts(string Major, string Minor)
{
    public static ThemeFonts From(ThemePart? part)
    {
        var scheme = part?.Theme?.ThemeElements?.FontScheme;
        return new ThemeFonts(
            scheme?.MajorFont?.LatinFont?.Typeface?.Value ?? "Calibri Light",
            scheme?.MinorFont?.LatinFont?.Typeface?.Value ?? "Calibri");
    }

    public string? Resolve(string? themeName) => themeName?.ToLowerInvariant() switch
    {
        null => null,
        var t when t.StartsWith("major") => this.Major,
        var t when t.StartsWith("minor") => this.Minor,
        _ => null
    };
}
