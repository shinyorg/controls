using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Shiny.Controls.Office.Document;

/// <summary>Rewrites the letters in a range to another case — Home ▸ Change Case.</summary>
public sealed record ChangeCaseCommand(DocumentRange Range, TextCase Case) : DocumentCommand
{
    public override string Name => "Change Case";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        if (this.Range.IsEmpty)
            return new NoOpCommand();

        var restore = context.CaptureRange(this.Range);

        for (var block = this.Range.Start.Block; block <= this.Range.End.Block; block++)
        {
            if (context.ParagraphElementAt(block) is not { } paragraph)
                continue;

            var text = WordParagraphEditor.TextOf(paragraph);
            var from = block == this.Range.Start.Block ? this.Range.Start.Offset : 0;
            var to = block == this.Range.End.Block ? Math.Min(this.Range.End.Offset, text.Length) : text.Length;

            var mapped = Transform(text, this.Case);
            WordParagraphEditor.MapText(paragraph, from, to, (index, c) => index < mapped.Length ? mapped[index] : c);
            context.Reproject(block);
        }

        return restore;
    }

    /// <summary>
    /// The whole paragraph's text in the new case, character for character.
    /// </summary>
    /// <remarks>
    /// Worked out over the whole paragraph, not the selection, because sentence case needs to know
    /// whether the selection starts a sentence — and one character in never changes length, which
    /// is what keeps every offset after the edit where it was.
    /// </remarks>
    internal static string Transform(string text, TextCase mode)
    {
        var chars = text.ToCharArray();
        var startOfSentence = true;
        var startOfWord = true;

        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];

            chars[i] = mode switch
            {
                TextCase.Upper => char.ToUpperInvariant(c),
                TextCase.Lower => char.ToLowerInvariant(c),
                TextCase.Toggle => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c),
                TextCase.Capitalize => startOfWord ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c),
                TextCase.Sentence => startOfSentence && char.IsLetter(c) ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c),
                _ => c
            };

            if (char.IsLetter(c))
                startOfSentence = false;

            if (c is '.' or '!' or '?')
                startOfSentence = true;

            startOfWord = char.IsWhiteSpace(c) || c is '-' or '(' or '"' or '“';
        }

        return new string(chars);
    }
}


/// <summary>
/// Pastes copied paragraphs at a position, keeping their formatting.
/// </summary>
/// <remarks>
/// <para>
/// The first copied paragraph's content joins the paragraph the caret is in, the last one's joins
/// whatever followed the caret, and any in between arrive as paragraphs of their own — which is the
/// only reading of "paste three paragraphs into the middle of a fourth" that loses nothing.
/// </para>
/// <para>
/// Undone by putting back the block the caret was in, which is one paragraph — or, inside a table,
/// the whole table, since the pasted paragraphs went into the cell.
/// </para>
/// </remarks>
public sealed record PasteFragmentCommand(DocumentPosition At, IReadOnlyList<Paragraph> Fragment) : DocumentCommand
{
    public override string Name => "Paste";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var paragraph = context.ParagraphElementAt(this.At.Block);
        if (paragraph is null || this.Fragment.Count == 0)
            return new NoOpCommand();

        var top = context.TopOf(this.At.Block);
        var restore = context.CaptureBlocks(top, 1);
        var isTopLevel = context.Blocks[top] is DocumentParagraph;

        var tail = WordParagraphEditor.Split(paragraph, this.At.Offset);

        Append(paragraph, this.Fragment[0]);

        var anchor = (OpenXmlElement)paragraph;
        var created = new List<OpenXmlElement> { paragraph };

        for (var i = 1; i < this.Fragment.Count - 1; i++)
        {
            var middle = (Paragraph)this.Fragment[i].CloneNode(true);
            anchor.InsertAfterSelf(middle);
            anchor = middle;
            created.Add(middle);
        }

        if (this.Fragment.Count > 1)
        {
            // The last piece leads the tail, which keeps the tail's own paragraph formatting — the text
            // after the caret was already in that paragraph.
            Prepend(tail, this.Fragment[^1]);
            created.Add(tail);
        }
        else
        {
            WordParagraphEditor.Merge(paragraph, tail);
        }

        if (isTopLevel)
            context.ResyncTop(top, created);
        else
            context.ReprojectTop(top);

        return restore with { RemovedCount = isTopLevel ? created.Count : 1 };
    }

    static void Append(Paragraph target, Paragraph source)
    {
        foreach (var child in source.ChildElements)
        {
            if (child is not ParagraphProperties)
                target.AppendChild(child.CloneNode(true));
        }
    }

    static void Prepend(Paragraph target, Paragraph source)
    {
        OpenXmlElement? after = target.ParagraphProperties;

        foreach (var child in source.ChildElements)
        {
            if (child is ParagraphProperties)
                continue;

            var clone = child.CloneNode(true);
            if (after is null)
                target.PrependChild(clone);
            else
                after.InsertAfterSelf(clone);

            after = clone;
        }
    }
}


/// <summary>Inserts whole paragraphs after a top-level block. Undone by removing them.</summary>
public sealed record InsertParagraphsCommand(int AfterTop, IReadOnlyList<Paragraph> Paragraphs) : DocumentCommand
{
    public override string Name => "Insert";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        if (context.BlockElementAt(this.AfterTop) is not { } anchor || this.Paragraphs.Count == 0)
            return new NoOpCommand();

        var top = this.AfterTop;
        foreach (var paragraph in this.Paragraphs)
        {
            var clone = paragraph.CloneNode(true);
            anchor.InsertAfterSelf(clone);
            context.InsertBlockAfter(top, clone);
            anchor = clone;
            top++;
        }

        return new RemoveBlocksCommand(this.AfterTop + 1, this.Paragraphs.Count);
    }
}


/// <summary>
/// Inserts a field — a page number, a date — as a <c>w:fldSimple</c> with a cached result.
/// </summary>
public sealed record InsertFieldCommand(DocumentPosition At, string Instruction, string Cached) : DocumentCommand
{
    public override string Name => "Insert Field";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var paragraph = context.ParagraphElementAt(this.At.Block);
        if (paragraph is null)
            return new NoOpCommand();

        var restore = context.CaptureRange(new DocumentRange(this.At, this.At));
        var field = new SimpleField(new Run(new W.Text(this.Cached))) { Instruction = this.Instruction };

        WordParagraphEditor.InsertObject(paragraph, this.At.Offset, field);
        context.Reproject(this.At.Block);
        return restore;
    }
}


/// <summary>
/// Ends a section at a position: the paragraph splits there, and the first half carries a
/// <c>w:sectPr</c> copied from the section it was in.
/// </summary>
/// <remarks>
/// <para>
/// OOXML stores a section's properties at its <em>end</em>, and the break type of a section — whether it
/// starts on a new page — on the section itself. So the new break's type is written on the section that
/// now follows it, and the copy placed at the break keeps the type the original section already had.
/// </para>
/// <para>
/// Per-section page setup is written (each section carries its own size and margins) but only the last
/// section's is drawn: the paginator lays every page out on one sheet size.
/// </para>
/// </remarks>
public sealed record InsertSectionBreakCommand(DocumentPosition At, SectionBreakType Type) : DocumentCommand
{
    public override string Name => "Section Break";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var paragraph = context.ParagraphElementAt(this.At.Block);
        if (paragraph is null || context.BodyElement is not { } body || !ReferenceEquals(paragraph.Parent, body))
            return new NoOpCommand();

        var restore = context.CaptureAll();

        // The section this paragraph is in is closed by the next sectPr after it: a paragraph's own, or
        // the body's final one.
        var following = paragraph.ElementsAfter()
            .OfType<Paragraph>()
            .Select(x => x.ParagraphProperties?.GetFirstChild<SectionProperties>())
            .FirstOrDefault(x => x is not null)
            ?? context.SectionProperties(create: true);

        if (following is null)
            return new NoOpCommand();

        var tail = WordParagraphEditor.Split(paragraph, this.At.Offset);
        _ = tail;

        var copy = (SectionProperties)following.CloneNode(true);

        // Headers and footers are inherited by the next section when it names none, so the copy keeps
        // its references and the section after it keeps pointing at the same parts.
        WordParagraphEditor.FormatParagraph(paragraph, properties =>
        {
            properties.RemoveAllChildren<SectionProperties>();
            WordParagraphEditor.InsertOrdered(properties, copy);
        });

        following.RemoveAllChildren<SectionType>();
        WordSectionOrder.Insert(following, new SectionType
        {
            Val = this.Type == SectionBreakType.Continuous ? SectionMarkValues.Continuous : SectionMarkValues.NextPage
        });

        context.RereadAllFromBody();
        return restore;
    }
}


/// <summary>Sets the number of text columns for the document's last section (<c>w:cols</c>).</summary>
public sealed record SetColumnsCommand(int Count, double Spacing = 48) : DocumentCommand
{
    public override string Name => "Columns";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var section = context.SectionProperties(create: true);
        if (section is null)
            return new NoOpCommand();

        var previous = section.GetFirstChild<Columns>()?.CloneNode(true);

        section.RemoveAllChildren<Columns>();
        if (this.Count > 1)
        {
            WordSectionOrder.Insert(section, new Columns
            {
                ColumnCount = (short)Math.Clamp(this.Count, 1, 10),
                Space = OoxmlUnits.PixelsToTwips(this.Spacing).ToString(System.Globalization.CultureInfo.InvariantCulture),
                EqualWidth = true
            });
        }

        context.MarkPageSetupChanged();
        return new RestoreSectionChildCommand("cols", previous);
    }
}


/// <summary>Sets the paper size, keeping the orientation.</summary>
public sealed record SetPageSizeCommand(PaperSize Size) : DocumentCommand
{
    public override string Name => "Page Size";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var section = context.SectionProperties(create: true);
        if (section is null)
            return new NoOpCommand();

        var previous = section.GetFirstChild<PageSize>()?.CloneNode(true);
        var landscape = context.Page.Orientation == PageOrientation.Landscape;

        var width = landscape ? this.Size.Height : this.Size.Width;
        var height = landscape ? this.Size.Width : this.Size.Height;

        var size = section.GetFirstChild<PageSize>();
        if (size is null)
        {
            size = new PageSize();
            WordSectionOrder.Insert(section, size);
        }

        size.Width = (uint)OoxmlUnits.PixelsToTwips(width);
        size.Height = (uint)OoxmlUnits.PixelsToTwips(height);

        if (landscape)
            size.Orient = PageOrientationValues.Landscape;

        context.MarkPageSetupChanged();
        return new RestoreSectionChildCommand("pgSz", previous);
    }
}


/// <summary>Puts one child of the last <c>w:sectPr</c> back as it was — or removes it.</summary>
public sealed record RestoreSectionChildCommand(string LocalName, OpenXmlElement? Previous) : DocumentCommand
{
    public override string Name => "Undo";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var section = context.SectionProperties(create: this.Previous is not null);
        if (section is null)
            return new NoOpCommand();

        var current = section.ChildElements.FirstOrDefault(x => x.LocalName == this.LocalName);
        var inverse = new RestoreSectionChildCommand(this.LocalName, current?.CloneNode(true));

        current?.Remove();
        if (this.Previous is not null)
            WordSectionOrder.Insert(section, this.Previous.CloneNode(true));

        context.MarkPageSetupChanged();
        return inverse;
    }
}


/// <summary>Sets the page colour (<c>w:background</c>), or clears it.</summary>
/// <remarks>
/// Word only shows a background when the settings ask it to (<c>w:displayBackgroundShape</c>), so that
/// is written too — without it the colour is saved and then invisible in Word.
/// </remarks>
public sealed record SetPageColorCommand(ArgbColor? Color) : DocumentCommand
{
    public override string Name => "Page Color";

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        if (context.Main?.Document is not { } document)
            return new NoOpCommand();

        var background = context.CapturePart(DocumentPartKind.Background);
        var settingsBefore = context.CapturePart(DocumentPartKind.Settings);

        document.DocumentBackground?.Remove();

        if (this.Color is { } color)
        {
            document.PrependChild(new DocumentBackground { Color = $"{color.R:X2}{color.G:X2}{color.B:X2}" });

            if (context.EnsureSettings() is { } settings && settings.GetFirstChild<DisplayBackgroundShape>() is null)
            {
                WordSettingsOrder.Insert(settings, new DisplayBackgroundShape());
                context.MarkPartDirty(settings);
            }
        }

        context.NotifyChanged();

        return new CompositeCommand<WordDocument>(this.Name,
        [
            new RestorePartCommand(DocumentPartKind.Settings, settingsBefore),
            new RestorePartCommand(DocumentPartKind.Background, background)
        ]);
    }
}


/// <summary>A text watermark, written the way Word writes one: VML WordArt in the default header.</summary>
static class WordWatermark
{
    const string ShapePrefix = "PowerPlusWaterMarkObject";

    /// <summary>True for a header paragraph that holds nothing but a watermark.</summary>
    public static bool IsWatermarkParagraph(OpenXmlElement element)
        => element is Paragraph && element.Descendants().Any(x => x.LocalName == "shape" && OoxmlUnits.Attribute(x, "id")?.StartsWith(ShapePrefix, StringComparison.Ordinal) == true);

    /// <summary>The paragraph carrying a watermark reading <paramref name="text"/>.</summary>
    public static Paragraph Build(string text)
    {
        var escaped = System.Security.SecurityElement.Escape(text);

        var xml =
            "<w:p xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
            "xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" " +
            "xmlns:w10=\"urn:schemas-microsoft-com:office:word\">" +
            "<w:pPr><w:pStyle w:val=\"Header\"/></w:pPr>" +
            "<w:r><w:rPr><w:noProof/></w:rPr><w:pict>" +
            "<v:shapetype id=\"_x0000_t136\" coordsize=\"21600,21600\" o:spt=\"136\" adj=\"10800\" path=\"m@7,l@8,m@5,21600l@6,21600e\">" +
            "<v:formulas><v:f eqn=\"sum #0 0 10800\"/><v:f eqn=\"prod #0 2 1\"/><v:f eqn=\"sum 21600 0 @1\"/><v:f eqn=\"sum 0 0 @2\"/>" +
            "<v:f eqn=\"sum 21600 0 @3\"/><v:f eqn=\"if @0 @3 0\"/><v:f eqn=\"if @0 21600 @1\"/><v:f eqn=\"if @0 0 @2\"/>" +
            "<v:f eqn=\"if @0 @4 21600\"/><v:f eqn=\"mid @5 @6\"/><v:f eqn=\"mid @8 @5\"/><v:f eqn=\"mid @7 @8\"/>" +
            "<v:f eqn=\"mid @6 @7\"/><v:f eqn=\"sum @6 0 @5\"/></v:formulas>" +
            "<v:path textpathok=\"t\" o:connecttype=\"custom\" o:connectlocs=\"@9,0;@10,10800;@11,21600;@12,10800\" o:connectangles=\"270,180,90,0\"/>" +
            "<v:textpath on=\"t\" fitshape=\"t\"/><v:handles><v:h position=\"#0,bottomRight\" xrange=\"6629,14971\"/></v:handles>" +
            "<o:lock v:ext=\"edit\" text=\"t\" shapetype=\"t\"/></v:shapetype>" +
            $"<v:shape id=\"{ShapePrefix}1\" o:spid=\"_x0000_s2049\" type=\"#_x0000_t136\" " +
            "style=\"position:absolute;margin-left:0;margin-top:0;width:412.4pt;height:137.45pt;rotation:315;z-index:-251657216;" +
            "mso-position-horizontal:center;mso-position-horizontal-relative:margin;mso-position-vertical:center;mso-position-vertical-relative:margin\" " +
            "o:allowincell=\"f\" fillcolor=\"silver\" stroked=\"f\">" +
            "<v:fill opacity=\".5\"/>" +
            $"<v:textpath style=\"font-family:&quot;Calibri&quot;;font-size:1pt\" string=\"{escaped}\"/>" +
            "<w10:wrap anchorx=\"margin\" anchory=\"margin\"/></v:shape></w:pict></w:r></w:p>";

        return new Paragraph(xml);
    }
}


/// <summary>Places children of <c>w:sectPr</c> at their schema position.</summary>
static class WordSectionOrder
{
    static readonly string[] Order =
    [
        "headerReference", "footerReference", "footnotePr", "endnotePr", "type", "pgSz", "pgMar",
        "paperSrc", "pgBorders", "lnNumType", "pgNumType", "cols", "formProt", "vAlign", "noEndnote",
        "titlePg", "textDirection", "bidi", "rtlGutter", "docGrid", "printerSettings", "sectPrChange"
    ];

    public static void Insert(SectionProperties section, OpenXmlElement child)
    {
        var rank = Array.IndexOf(Order, child.LocalName);
        OpenXmlElement? previous = null;

        foreach (var existing in section.ChildElements)
        {
            var existingRank = Array.IndexOf(Order, existing.LocalName);
            if (existingRank > rank)
                break;

            previous = existing;
        }

        if (previous is null)
            section.PrependChild(child);
        else
            previous.InsertAfterSelf(child);
    }
}
