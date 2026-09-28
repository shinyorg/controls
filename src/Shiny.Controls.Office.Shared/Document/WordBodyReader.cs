using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Shiny.Controls.Office.Packaging;
using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Wps = DocumentFormat.OpenXml.Office2010.Word.DrawingShape;

namespace Shiny.Controls.Office.Document;

/// <summary>
/// Walks a document body and produces the reflowable block model.
/// </summary>
sealed class WordBodyReader(
    MainDocumentPart main,
    WordStyleResolver styles,
    WordNumbering numbering,
    IUnsupportedFeatureSink unsupported)
{
    /// <summary>
    /// The DrawingML reader, sharing the document's theme.
    /// </summary>
    /// <remarks>
    /// The same class the slide side uses. A shape in a Word drawing carries exactly the DrawingML a
    /// shape on a slide does — <c>a:solidFill</c>, <c>a:ln</c>, <c>a:schemeClr</c> — so resolving it
    /// with anything other than that reader would be a second, worse implementation of the same
    /// vocabulary. Built once because it holds the resolved theme palette.
    /// </remarks>
    readonly DrawingReader drawing = new(ThemeColors.From(main.ThemePart));

    /// <summary>The leader of the paragraph being read's right tab stop, while it is being read.</summary>
    char? rightTabLeader;

    /// <summary>Re-projects one paragraph after its XML has been edited.</summary>
    public DocumentParagraph Reread(Paragraph paragraph) => this.ReadParagraph(paragraph);

    /// <summary>
    /// Re-projects any body-level element after its XML has been edited.
    /// </summary>
    /// <remarks>
    /// Anything that is not a paragraph or a table becomes an empty paragraph rather than nothing, so
    /// the block list stays the same length as the body it mirrors — an index that skips an element
    /// puts every caret position after it in the wrong block.
    /// </remarks>
    public DocumentBlock RereadBlock(OpenXmlElement element) => element switch
    {
        Paragraph paragraph => this.ReadParagraph(paragraph),
        Table table => this.ReadTable(table),
        _ => new DocumentParagraph([], ParagraphFormat.Default)
    };

    public IReadOnlyList<DocumentBlock> ReadBody(Body? body) => this.ReadContainer(body);

    /// <summary>Reads any block container — a body, or a header or footer part's root.</summary>
    public IReadOnlyList<DocumentBlock> ReadContainer(OpenXmlElement? container)
    {
        var blocks = new List<DocumentBlock>();
        if (container is null)
            return blocks;

        foreach (var block in this.ReadBlocks(container))
            blocks.Add(block);

        return blocks;
    }

    IEnumerable<DocumentBlock> ReadBlocks(OpenXmlElement container)
    {
        foreach (var child in container.ChildElements)
        {
            switch (child)
            {
                case Paragraph paragraph:
                    yield return this.ReadParagraph(paragraph);
                    break;

                case Table table:
                    yield return this.ReadTable(table);
                    break;

                case SdtBlock structured:
                    // A content control is a wrapper; its content is ordinary blocks underneath.
                    var content = structured.Descendants<SdtContentBlock>().FirstOrDefault();
                    if (content is null)
                        break;

                    foreach (var inner in this.ReadBlocks(content))
                        yield return inner;

                    break;
            }
        }
    }

    DocumentParagraph ReadParagraph(Paragraph paragraph)
    {
        var properties = paragraph.ParagraphProperties;
        var styleId = properties?.ParagraphStyleId?.Val?.Value;

        var format = styles.ParagraphFormatFor(styleId);
        var runStyle = styles.RunStyleFor(styleId);

        if (properties is not null)
        {
            format = WordStyleResolver.ApplyParagraphProperties(format, properties);

            // rPr inside pPr is the paragraph mark's formatting, and Word uses it as the baseline for
            // runs that carry no formatting of their own.
            if (properties.ParagraphMarkRunProperties is { } markProperties)
                runStyle = WordStyleResolver.ApplyRunProperties(runStyle, markProperties, ThemeFonts.From(main.ThemePart));
        }

        var label = this.ReadListLabel(paragraph, properties, styleId, runStyle, ref format);

        // A right tab stop turns the paragraph's tabs into ones that push what follows them to the
        // right margin — the shape of every table-of-contents line.
        var rightTab = properties?.Tabs?.Elements<TabStop>()
            .FirstOrDefault(x => OoxmlUnits.EnumAttribute(x, "val") == "right");

        this.rightTabLeader = rightTab is null
            ? null
            : OoxmlUnits.EnumAttribute(rightTab, "leader") switch
            {
                "dot" => '.',
                "hyphen" => '-',
                "underscore" => '_',
                "middleDot" => '\u00B7',
                _ => ' '
            };

        List<StyledRun> runs;
        try
        {
            runs = this.ReadInlines(paragraph, runStyle).ToList();
        }
        finally
        {
            this.rightTabLeader = null;
        }

        return new DocumentParagraph(runs, format)
        {
            Element = paragraph,
            List = label,
            StyleName = styles.StyleName(styleId)
        };
    }

    ListLabel? ReadListLabel(Paragraph paragraph, ParagraphProperties? properties, string? styleId, TextStyle runStyle, ref ParagraphFormat format)
    {
        var numberingProperties = properties?.NumberingProperties;
        var numId = numberingProperties?.NumberingId?.Val?.Value;
        var levelIndex = numberingProperties?.NumberingLevelReference?.Val?.Value ?? 0;

        if (numId is null || numId == 0 || numbering.IsEmpty)
            return null;

        var level = numbering.Level(numId.Value, levelIndex);
        if (level is null || level.IsNone)
            return null;

        // The text is deliberately left empty. The number depends on every numbered paragraph before
        // this one, which a paragraph read on its own cannot see, so NumberingSequencer fills it in
        // once the whole block list exists.
        var label = new ListLabel(string.Empty, runStyle, level.Indent, level.Hanging)
        {
            Numbering = new ListNumbering(numId.Value, levelIndex),
            IsBullet = level.IsBullet
        };

        // The level's own indent applies unless the paragraph overrides it directly.
        if (format.IndentLeft == 0 && label.Indent > 0)
            format = format with { IndentLeft = label.Indent };

        return label;
    }

    /// <summary>Footnote id to the number it is drawn with, in order of first reference.</summary>
    /// <remarks>
    /// Supplied by the document rather than worked out here: the number is a function of every
    /// reference before this one, and a paragraph re-read on its own after an edit cannot see them.
    /// </remarks>
    public IReadOnlyDictionary<int, int> FootnoteNumbers { get; set; } = new Dictionary<int, int>();

    /// <summary>Endnote id to its number, the same way.</summary>
    public IReadOnlyDictionary<int, int> EndnoteNumbers { get; set; } = new Dictionary<int, int>();

    IEnumerable<StyledRun> ReadInlines(OpenXmlElement container, TextStyle inherited)
        => this.ReadInlines(container, inherited, null, TextRevision.None);

    IEnumerable<StyledRun> ReadInlines(OpenXmlElement container, TextStyle inherited, string? link, TextRevision revision)
    {
        // A complex field is spread across sibling runs — begin, the instruction, separate, the
        // cached result, end — so reading one means carrying state across the loop rather than
        // looking at any run on its own.
        var phase = FieldPhase.None;
        var instruction = new System.Text.StringBuilder();
        var emittedField = false;

        foreach (var child in container.ChildElements)
        {
            switch (child)
            {
                case Run run:
                    switch (FieldCharOf(run))
                    {
                        case "begin":
                            phase = FieldPhase.Instruction;
                            instruction.Clear();
                            emittedField = false;
                            continue;

                        case "separate":
                            phase = FieldPhase.Result;
                            continue;

                        case "end":
                            phase = FieldPhase.None;
                            continue;
                    }

                    if (phase == FieldPhase.Instruction)
                    {
                        foreach (var code in run.Elements<FieldCode>())
                            instruction.Append(code.Text);

                        continue;
                    }

                    if (phase == FieldPhase.Result)
                    {
                        var kind = FieldKindOf(instruction.ToString());
                        if (kind != DocumentFieldKind.None)
                        {
                            // One run stands for the whole field; the rest of the cached result is
                            // dropped, because what replaces it is computed per page.
                            if (!emittedField)
                            {
                                emittedField = true;
                                var cached = String.Concat(run.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(x => x.Text));
                                var style = this.ReadRun(run, inherited, link, revision).FirstOrDefault()?.Style ?? inherited;
                                yield return new StyledRun(cached, style) { Field = kind };
                            }

                            continue;
                        }

                        // An instruction this does not resolve keeps the result Word last wrote,
                        // which is what a reader would have seen anyway.
                    }

                    foreach (var piece in this.ReadRun(run, inherited, link, revision))
                        yield return piece;

                    break;

                case Hyperlink hyperlink:
                    foreach (var piece in this.ReadInlines(hyperlink, inherited, this.ResolveHyperlink(hyperlink) ?? link, revision))
                        yield return piece;

                    break;

                case InsertedRun inserted:
                    foreach (var piece in this.ReadInlines(inserted, inherited, link, TextRevision.Inserted))
                        yield return piece;

                    break;

                case DeletedRun deleted:
                    // Shown struck through rather than hidden: a reviewer has to be able to see what was
                    // taken out before they can accept or reject it.
                    foreach (var piece in this.ReadInlines(deleted, inherited, link, TextRevision.Deleted))
                        yield return piece;

                    break;

                case SdtRun structured:
                    if (structured.SdtContentRun is { } content)
                    {
                        foreach (var piece in this.ReadInlines(content, inherited, link, revision))
                            yield return piece;
                    }

                    break;

                case CustomXmlRun:
                    foreach (var piece in this.ReadInlines(child, inherited, link, revision))
                        yield return piece;

                    break;

                case SimpleField field:
                    var simpleKind = FieldKindOf(field.Instruction?.Value);
                    if (simpleKind != DocumentFieldKind.None)
                    {
                        // The cached result is kept as the run's text: it is what the document last
                        // showed, so a field nothing resolves still measures and draws sensibly.
                        var cached = String.Concat(field.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(x => x.Text));
                        var fieldStyle = field.Descendants<Run>().FirstOrDefault() is { } styled
                            ? this.ReadRun(styled, inherited, link, revision).FirstOrDefault()?.Style ?? inherited
                            : inherited;

                        yield return new StyledRun(cached, fieldStyle) { Field = simpleKind };
                        break;
                    }

                    foreach (var piece in this.ReadInlines(field, inherited, link, revision))
                        yield return piece;

                    break;
            }
        }
    }

    enum FieldPhase
    {
        None,
        Instruction,
        Result
    }

    /// <summary>The <c>w:fldCharType</c> of a run that is a field marker, or null for an ordinary run.</summary>
    static string? FieldCharOf(Run run)
        => run.Elements<FieldChar>().FirstOrDefault() is { } marker
            ? OoxmlUnits.EnumAttribute(marker, "fldCharType")
            : null;

    /// <summary>
    /// Which computed field an instruction is, or <see cref="DocumentFieldKind.None"/>.
    /// </summary>
    /// <remarks>
    /// Only the field name is looked at. Switches like <c>\* MERGEFORMAT</c> and
    /// <c>\* ARABIC</c> follow it and change formatting rather than the value, and honouring them
    /// would mean implementing Word's numeric picture grammar for a gain of almost nothing.
    /// </remarks>
    static DocumentFieldKind FieldKindOf(string? instruction)
    {
        if (String.IsNullOrWhiteSpace(instruction))
            return DocumentFieldKind.None;

        var name = instruction.TrimStart().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        return name?.ToUpperInvariant() switch
        {
            "PAGE" => DocumentFieldKind.Page,
            "NUMPAGES" => DocumentFieldKind.PageCount,
            _ => DocumentFieldKind.None
        };
    }

    IEnumerable<StyledRun> ReadRun(Run run, TextStyle inherited, string? link, TextRevision revision = TextRevision.None)
    {
        var style = inherited;

        if (run.RunProperties?.RunStyle?.Val?.Value is { } runStyleId)
            style = styles.RunStyleFor(runStyleId, style);

        if (run.RunProperties is not null)
            style = WordStyleResolver.ApplyRunProperties(style, run.RunProperties, ThemeFonts.From(main.ThemePart));

        if (link is not null)
            style = style with { Link = link };

        if (revision != TextRevision.None)
            style = style with { Revision = revision };

        foreach (var child in run.ChildElements)
        {
            switch (child)
            {
                case DocumentFormat.OpenXml.Wordprocessing.Text text:
                    yield return new StyledRun(text.Text ?? string.Empty, style);
                    break;

                case DeletedText deleted:
                    yield return new StyledRun(deleted.Text ?? string.Empty, style with { Revision = TextRevision.Deleted });
                    break;

                case FootnoteReference footnote:
                    // Drawn as its number but one character to the caret: the number is computed from
                    // the footnote's place in the document, and it is one mark however many digits.
                    var footnoteId = (int)(footnote.Id?.Value ?? 0);
                    var footnoteNumber = this.FootnoteNumbers.TryGetValue(footnoteId, out var fn) ? fn : footnoteId;
                    yield return new StyledRun(footnoteNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), Superscript(style))
                    {
                        SourceLength = 1,
                        FootnoteId = footnoteId
                    };

                    break;

                case EndnoteReference endnote:
                    var endnoteId = (int)(endnote.Id?.Value ?? 0);
                    var endnoteNumber = this.EndnoteNumbers.TryGetValue(endnoteId, out var en) ? en : endnoteId;
                    yield return new StyledRun(ToRoman(endnoteNumber), Superscript(style)) { SourceLength = 1 };
                    break;

                case TabChar:
                    // A real tab needs tab stops the reflow view does not model; four spaces is the
                    // honest approximation and keeps indented text from collapsing. A right-aligned
                    // stop is the exception, and the one that matters: it is how a TOC line ends.
                    yield return this.rightTabLeader is { } leader
                        ? new StyledRun("    ", style) { SourceLength = 4, RightTabLeader = leader }
                        : new StyledRun("    ", style);

                    break;

                case Break br:
                    // The type attribute is read as a string rather than compared against
                    // BreakValues: OpenXml 3.x models these as record structs, and getting that
                    // comparison subtly wrong fails silently as a page break that only breaks a line.
                    yield return new StyledRun(string.Empty, style)
                    {
                        IsBreak = true,
                        IsPageBreak = OoxmlUnits.EnumAttribute(br, "type") == "page"
                    };

                    break;

                case DocumentFormat.OpenXml.Wordprocessing.Drawing drawing:
                    // A w:drawing is the wrapper for both pictures and shapes; which one it holds is
                    // decided by what is under a:graphicData, so the picture is tried first and the
                    // shape only if there was no blip to find.
                    if (this.ReadInlineObject(drawing) is { } inline)
                        yield return new StyledRun(string.Empty, style) { Inline = inline };

                    break;

                case Picture picture:
                    // A header's VML WordArt is a watermark, which the painter draws from the document's
                    // WatermarkText; anything else in VML is not drawn and is said so.
                    if (!picture.Descendants().Any(x => x.LocalName == "textpath"))
                        unsupported.Report(new UnsupportedFeature("document", "VML picture", UnsupportedSeverity.NotRendered));

                    break;

                case SymbolChar symbol:
                    // Always exactly one character, matching the editor's offset space: a symbol whose
                    // code does not parse still occupies its position.
                    var glyph = symbol.Char?.Value is { } code &&
                                int.TryParse(code, System.Globalization.NumberStyles.HexNumber, null, out var value)
                        ? (char)(value & 0xFF)
                        : '\u25A1';

                    yield return new StyledRun(glyph.ToString(), style) { SourceLength = 1 };
                    break;
            }
        }
    }

    /// <summary>The raised, smaller form Word draws a note reference in.</summary>
    static TextStyle Superscript(TextStyle style)
        => style.BaselineShift > 0 ? style : style with { BaselineShift = 0.33, SizeScale = 0.65 };

    static string ToRoman(int number)
    {
        if (number <= 0 || number > 3999)
            return number.ToString(System.Globalization.CultureInfo.InvariantCulture);

        (int Value, string Numeral)[] table =
        [
            (1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"),
            (50, "l"), (40, "xl"), (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i")
        ];

        var builder = new System.Text.StringBuilder();
        foreach (var (v, n) in table)
        {
            while (number >= v)
            {
                builder.Append(n);
                number -= v;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whatever a <c>w:drawing</c> holds, or null when it is something the viewer cannot draw.
    /// </summary>
    /// <remarks>
    /// Anchored (floating) drawings are read too, and land in the flow at the point they are anchored
    /// from. That is not where Word puts them — a floating shape has an absolute position and text
    /// wraps around it — but the alternative in a reflow view is to drop them, and a diagram in
    /// roughly the right paragraph is far more use than no diagram at all. The substitution is
    /// reported so the note above the document says so.
    /// </remarks>
    InlineObject? ReadInlineObject(DocumentFormat.OpenXml.Wordprocessing.Drawing drawing)
    {
        if (drawing.GetFirstChild<Drawing.Wordprocessing.Anchor>() is not null)
        {
            unsupported.Report(new UnsupportedFeature(
                "document", "Floating drawing", UnsupportedSeverity.NotEditable,
                "Shown in the text flow; the reflow view has no fixed positions to anchor it to."));
        }

        // A blip is what makes it a picture. Shapes have geometry instead.
        if (drawing.Descendants<Drawing.Blip>().Any())
            return this.ReadImage(drawing);

        return this.ReadShape(drawing);
    }

    /// <summary>
    /// The <c>wps:wsp</c> inside a drawing, as a shape.
    /// </summary>
    /// <remarks>
    /// Looked up by descendant rather than by walking <c>a:graphic</c> / <c>a:graphicData</c>, because
    /// the same shape reaches here under at least three different <c>graphicData</c> URIs depending on
    /// which Word wrote it, and a group wraps it in another layer again. The element is what matters,
    /// not the route to it.
    /// </remarks>
    InlineShape? ReadShape(DocumentFormat.OpenXml.Wordprocessing.Drawing drawing)
    {
        var wsp = drawing.Descendants<Wps.WordprocessingShape>().FirstOrDefault();
        if (wsp is null)
            return null;

        var properties = wsp.GetFirstChild<Wps.ShapeProperties>();
        var geometry = DrawingReader.MapGeometry(
            OoxmlUnits.EnumAttribute(properties?.GetFirstChild<Drawing.PresetGeometry>(), "prst"));

        // The extent on the wrapper is the authoritative size — a:xfrm inside the shape agrees with it
        // when present and is absent altogether for a shape Word sized from its wrapper.
        var (width, height) = ExtentOf(drawing);

        var fill = this.drawing.ReadFill(properties);
        var outline = this.drawing.ReadOutline(properties);

        // Neither fill nor outline means an invisible shape. Word's default for a drawn shape is a
        // themed fill, so an empty one is far more likely to be a shape whose theme colours could not
        // be resolved than a deliberately invisible one — an outline keeps it findable either way.
        if (fill.IsEmpty && outline is null)
            outline = new ShapeOutline(new ArgbColor(255, 0x44, 0x72, 0xC4), 1);

        return new InlineShape(geometry, width, height, DescriptionOf(drawing))
        {
            Fill = fill,
            Outline = outline,
            Text = this.ReadShapeText(wsp)
        };
    }

    /// <summary>The text inside a shape's text box, flattened to runs.</summary>
    /// <remarks>
    /// Flattened rather than kept as paragraphs: the shape is painted with its text centred as a
    /// single block, and a text box with enough content for the paragraph structure to matter wants a
    /// nested editor, which is a different feature.
    /// </remarks>
    IReadOnlyList<StyledRun> ReadShapeText(Wps.WordprocessingShape wsp)
    {
        var box = wsp.GetFirstChild<Wps.TextBoxInfo2>()?.TextBoxContent;
        if (box is null)
            return [];

        var runs = new List<StyledRun>();

        foreach (var paragraph in box.Elements<Paragraph>())
        {
            if (runs.Count > 0)
                runs.Add(new StyledRun(string.Empty, TextStyle.Default) { IsBreak = true });

            foreach (var run in paragraph.Descendants<Run>())
            {
                var style = TextStyle.Default;
                if (run.RunProperties is not null)
                    style = WordStyleResolver.ApplyRunProperties(style, run.RunProperties, ThemeFonts.From(main.ThemePart));

                foreach (var text in run.Elements<DocumentFormat.OpenXml.Wordprocessing.Text>())
                    runs.Add(new StyledRun(text.Text ?? string.Empty, style));
            }
        }

        return runs;
    }

    /// <summary>The drawing's stated size in pixels, falling back to a square when it has none.</summary>
    static (double Width, double Height) ExtentOf(DocumentFormat.OpenXml.Wordprocessing.Drawing drawing)
    {
        var inline = drawing.Descendants<Drawing.Wordprocessing.Extent>().FirstOrDefault();

        var width = inline?.Cx is { } cx ? OoxmlUnits.EmuToPixels(cx) : 0;
        var height = inline?.Cy is { } cy ? OoxmlUnits.EmuToPixels(cy) : 0;

        return width > 0 && height > 0 ? (width, height) : (120, 120);
    }

    static string? DescriptionOf(DocumentFormat.OpenXml.Wordprocessing.Drawing drawing)
        => drawing.Descendants<Drawing.Wordprocessing.DocProperties>().FirstOrDefault()?.Description;

    InlineImage? ReadImage(DocumentFormat.OpenXml.Wordprocessing.Drawing drawing)
    {
        var blip = drawing.Descendants<Drawing.Blip>().FirstOrDefault();
        var extent = drawing.Descendants<Drawing.Wordprocessing.Extent>().FirstOrDefault();

        if (blip?.Embed?.Value is not { } relationshipId)
            return null;

        if (main.GetPartById(relationshipId) is not ImagePart part)
            return null;

        try
        {
            using var stream = part.GetStream();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);

            var width = extent?.Cx is { } cx ? OoxmlUnits.EmuToPixels(cx) : 0;
            var height = extent?.Cy is { } cy ? OoxmlUnits.EmuToPixels(cy) : 0;

            // An image with no stated extent still has to occupy something, or it vanishes.
            if (width <= 0 || height <= 0)
            {
                width = 200;
                height = 150;
            }

            return new InlineImage(copy.ToArray(), width, height, drawing.Descendants<Drawing.Wordprocessing.DocProperties>().FirstOrDefault()?.Description);
        }
        catch (Exception ex)
        {
            unsupported.Report(new UnsupportedFeature("media", "Image", UnsupportedSeverity.NotRendered, ex.Message));
            return null;
        }
    }

    string? ResolveHyperlink(Hyperlink hyperlink)
    {
        if (hyperlink.Anchor?.Value is { } anchor)
            return "#" + anchor;

        if (hyperlink.Id?.Value is not { } id)
            return null;

        try
        {
            return main.HyperlinkRelationships.FirstOrDefault(x => x.Id == id)?.Uri?.ToString();
        }
        catch (Exception)
        {
            // A malformed relationship should cost the link, not the paragraph.
            return null;
        }
    }

    DocumentTable ReadTable(Table table)
    {
        var rows = new List<DocumentTableRow>();

        foreach (var row in table.Elements<TableRow>())
        {
            var cells = new List<DocumentTableCell>();

            foreach (var cell in row.Elements<TableCell>())
            {
                var properties = cell.TableCellProperties;
                var merge = properties?.VerticalMerge;

                // vMerge with no val - or val="continue" - means this cell continues the one above.
                var isContinuation = merge is not null &&
                    (merge.Val?.Value is null || merge.Val.Value == MergedCellValues.Continue);

                var shading = properties?.Shading?.Fill?.Value is { } fill &&
                              !fill.Equals("auto", StringComparison.OrdinalIgnoreCase) &&
                              WordStyleResolver.TryParseHex(fill, out var color)
                    ? color
                    : (Spreadsheet.ArgbColor?)null;

                double? width = null;
                if (properties?.TableCellWidth?.Width?.Value is { } widthValue &&
                    properties.TableCellWidth.Type?.Value == TableWidthUnitValues.Dxa &&
                    double.TryParse(widthValue, out var twips))
                    width = OoxmlUnits.TwipsToPixels(twips);

                cells.Add(new DocumentTableCell(this.ReadBlocks(cell).ToList())
                {
                    ColumnSpan = (int)(properties?.GridSpan?.Val?.Value ?? 1),
                    IsVerticalContinuation = isContinuation,
                    Shading = shading,
                    Width = width
                });
            }

            var isHeader = row.TableRowProperties?.Elements<TableHeader>().Any() == true;
            rows.Add(new DocumentTableRow(cells) { IsHeader = isHeader });
        }

        var widths = table.Elements<TableGrid>()
            .FirstOrDefault()?
            .Elements<GridColumn>()
            .Select(x => x.Width?.Value is { } w && double.TryParse(w, out var twips) ? OoxmlUnits.TwipsToPixels(twips) : 0)
            .ToList() ?? [];

        var borders = table.GetFirstChild<TableProperties>()?.TableBorders;
        var hasBorders = borders is null || borders.TopBorder?.Val?.Value != BorderValues.None;

        return new DocumentTable(rows)
        {
            Element = table,
            ColumnWidths = widths,
            HasBorders = hasBorders
        };
    }
}
