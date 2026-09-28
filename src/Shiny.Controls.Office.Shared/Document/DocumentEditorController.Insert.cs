using DocumentFormat.OpenXml.Wordprocessing;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Document;

/// <summary>
/// The Insert, Design, Layout and References tabs: links, bookmarks, fields, breaks, page setup,
/// styles, the table of contents and footnotes.
/// </summary>
public sealed partial class DocumentEditorController
{
    // ---- hyperlinks ----

    /// <summary>The hyperlink at the caret, or null.</summary>
    public DocumentHyperlink? HyperlinkAt(DocumentPosition position)
        => this.document.Hyperlinks.FirstOrDefault(x =>
            x.Range.Start.Block == position.Block && x.Range.Start.Offset <= position.Offset && x.Range.End.Offset >= position.Offset && !x.Range.IsEmpty);

    /// <summary>The hyperlink under the caret, or null — what Edit Link opens with.</summary>
    public DocumentHyperlink? CurrentHyperlink => this.HyperlinkAt(this.Selection.Focus);

    /// <summary>
    /// Inserts a hyperlink (Ctrl+K). With a selection the selected text becomes the link; with none,
    /// <paramref name="displayText"/> is typed and linked — or the address itself when that is empty.
    /// </summary>
    /// <param name="target">An absolute URL, or <c>#bookmark</c> for a place in this document.</param>
    /// <param name="displayText">The text to show, used only when nothing is selected.</param>
    public void InsertHyperlink(string target, string? displayText = null)
    {
        if (this.IsReadOnlyDocument || string.IsNullOrWhiteSpace(target))
            return;

        target = target.Trim();

        using (this.document.Undo.BeginTransaction("Insert Hyperlink"))
        {
            DocumentRange range;

            if (this.CurrentHyperlink is { } existing && this.Selection.IsEmpty)
            {
                // Editing a link: the same text, a new address — or new text too.
                range = existing.Range;

                if (!string.IsNullOrEmpty(displayText) && displayText != existing.Text)
                {
                    this.document.Execute(new DeleteRangeCommand(range));
                    this.document.Execute(new InsertTextCommand(range.Start, displayText));
                    range = new DocumentRange(range.Start, range.Start with { Offset = range.Start.Offset + displayText.Length });
                }
            }
            else if (this.Selection.IsEmpty || !this.Selection.Range.IsWithinOneBlock)
            {
                var text = string.IsNullOrEmpty(displayText) ? target.TrimStart('#') : displayText;
                var at = this.DeleteSelectionIfAny();
                this.document.Execute(new InsertTextCommand(at, text));
                range = new DocumentRange(at, at with { Offset = at.Offset + text.Length });
            }
            else
            {
                range = this.Selection.Range;
            }

            this.document.Execute(new InsertHyperlinkCommand(range, target));
            this.Selection.MoveTo(range.End);
        }

        this.AfterEdit();
    }

    /// <summary>Removes the hyperlink under the caret or in the selection, keeping its text.</summary>
    public void RemoveHyperlink()
    {
        if (this.IsReadOnlyDocument)
            return;

        var range = this.CurrentHyperlink?.Range ?? this.Selection.Range;
        this.document.Execute(new RemoveHyperlinkCommand(range));
        this.AfterEdit();
    }

    /// <summary>
    /// Follows the hyperlink under a point (what Ctrl+click does): a bookmark link moves the caret, an
    /// external one raises <see cref="LinkActivated"/> for the host to open.
    /// </summary>
    /// <returns>True when there was a link under the point.</returns>
    public bool ActivateLinkAt(double x, double y)
    {
        if (this.PositionAt(x, y) is not { } position || this.HyperlinkAt(position) is not { } link)
            return false;

        this.FollowLink(link);
        return true;
    }

    /// <summary>Follows a link: jumps to a bookmark, or raises <see cref="LinkActivated"/>.</summary>
    public void FollowLink(DocumentHyperlink link)
    {
        if (link.IsInternal)
        {
            this.GoToBookmark(link.Target[1..]);
            return;
        }

        this.LinkActivated?.Invoke(this, new DocumentLinkEventArgs(link));
    }

    // ---- bookmarks ----

    /// <summary>The bookmarks a user made — Word's hidden <c>_Toc</c> ones are left out.</summary>
    public IReadOnlyList<DocumentBookmark> Bookmarks => this.document.Bookmarks.Where(x => !x.IsHidden).ToList();

    /// <summary>Adds a bookmark at the selection, replacing any other of the same name.</summary>
    /// <returns>False when the name is not one Word accepts.</returns>
    public bool InsertBookmark(string name)
    {
        if (this.IsReadOnlyDocument || !IsValidBookmarkName(name))
            return false;

        var id = this.document.NextId(typeof(BookmarkStart));
        this.document.Execute(new InsertBookmarkCommand(this.Selection.Range, name, id));
        this.AfterEdit();
        return true;
    }

    /// <summary>
    /// Word's rule for bookmark names: starts with a letter, then letters, digits and underscores, at
    /// most 40 characters.
    /// </summary>
    public static bool IsValidBookmarkName(string? name)
        => !string.IsNullOrEmpty(name) && name.Length <= 40 && char.IsLetter(name[0]) && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>Moves the caret to a bookmark. False when there is none by that name.</summary>
    public bool GoToBookmark(string name)
    {
        var bookmark = this.document.Bookmarks.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (bookmark is null)
            return false;

        this.ClearObjectSelection();
        this.Selection.MoveTo(bookmark.Position);
        this.ScrollCaretIntoView();
        this.RaiseChanged();
        return true;
    }

    // ---- text and fields ----

    /// <summary>The date and time formats Insert ▸ Date &amp; Time offers, rendered for <paramref name="when"/>.</summary>
    public static IReadOnlyList<string> DateTimeFormats(DateTime when) =>
    [
        when.ToString("d"),
        when.ToString("D"),
        when.ToString("MMMM d, yyyy"),
        when.ToString("yyyy-MM-dd"),
        when.ToString("dd MMMM yyyy"),
        when.ToString("MMM. d, yy"),
        when.ToString("t"),
        when.ToString("T"),
        when.ToString("g"),
        when.ToString("yyyy-MM-dd HH:mm")
    ];

    /// <summary>Types today's date (and time) at the caret, in the given .NET format.</summary>
    public void InsertDateTime(string format = "D")
        => this.InsertText(DateTime.Now.ToString(string.IsNullOrEmpty(format) ? "D" : format));

    /// <summary>The characters Insert ▸ Symbol offers without opening a full character map.</summary>
    public static IReadOnlyList<string> CommonSymbols { get; } =
    [
        "©", "®", "™", "§", "¶", "†", "‡", "•", "…", "—", "–", "°", "±", "×", "÷", "≠", "≤", "≥", "≈", "∞",
        "√", "∑", "π", "µ", "Ω", "α", "β", "γ", "δ", "€", "£", "¥", "¢", "←", "→", "↑", "↓", "↔", "✓", "✗",
        "★", "☆", "♠", "♣", "♥", "♦", "½", "¼", "¾", "¹", "²", "³", "‰", "«", "»", "“", "”", "‘", "’", "¿", "¡"
    ];

    /// <summary>Inserts a symbol (or any string) at the caret, as typing.</summary>
    public void InsertSymbol(string symbol) => this.InsertText(symbol);

    /// <summary>
    /// Inserts a PAGE field in the body — the number of the page it lands on, as of when it was inserted.
    /// </summary>
    public void InsertPageNumberField()
    {
        if (this.IsReadOnlyDocument)
            return;

        var at = this.DeleteSelectionIfAny();
        var page = this.CurrentPageNumber().ToString(System.Globalization.CultureInfo.InvariantCulture);

        this.document.Execute(new InsertFieldCommand(at, " PAGE ", page));
        this.Selection.MoveTo(at with { Offset = at.Offset + page.Length });
        this.AfterEdit();
    }

    /// <summary>
    /// A horizontal line: an empty paragraph with a bottom border after the caret's paragraph — what
    /// Word's Horizontal Line and the <c>---</c> autoformat produce.
    /// </summary>
    public void InsertHorizontalLine()
    {
        if (this.IsReadOnlyDocument)
            return;

        var top = this.document.TopBlockOf(this.Selection.Focus.Block);
        if (top < 0)
            return;

        var line = new Paragraph(new ParagraphProperties(new DocumentFormat.OpenXml.Wordprocessing.ParagraphBorders(
            new BottomBorder { Val = BorderValues.Single, Size = 6U, Space = 1U, Color = "auto" })));

        this.document.Execute(new InsertParagraphsCommand(top, [line, new Paragraph()]));

        var after = this.Document.FirstParagraphOf(top + 2);
        if (after >= 0)
            this.Selection.MoveTo(new DocumentPosition(after, 0));

        this.AfterEdit();
    }

    /// <summary>Insert ▸ Blank Page: two page breaks with an empty page between them.</summary>
    public void InsertBlankPage()
    {
        if (this.IsReadOnlyDocument)
            return;

        using (this.document.Undo.BeginTransaction("Blank Page"))
        {
            var at = this.DeleteSelectionIfAny();
            this.document.Execute(new InsertPageBreakCommand(at));
            this.document.Execute(new InsertPageBreakCommand(at));
        }

        this.AfterEdit();
    }

    /// <summary>
    /// Inserts a section break at the caret. Only between body paragraphs — a section cannot end
    /// inside a table cell.
    /// </summary>
    public void InsertSectionBreak(SectionBreakType type = SectionBreakType.NextPage)
    {
        if (this.IsReadOnlyDocument || this.IsInTable)
            return;

        var at = this.Selection.Focus;
        this.document.Execute(new InsertSectionBreakCommand(at, type));

        var next = Math.Min(at.Block + 1, this.Document.Paragraphs.Count - 1);
        this.Selection.MoveTo(new DocumentPosition(next, 0));
        this.AfterEdit();
    }

    // ---- page setup and design ----

    /// <summary>The number of text columns in the last section, 1 when it has none.</summary>
    public int ColumnCount
        => (int)(this.document.SectionProperties(create: false)?.GetFirstChild<Columns>()?.ColumnCount?.Value ?? 1);

    /// <summary>Layout ▸ Columns: one, two or three columns for the document's last section.</summary>
    /// <remarks>
    /// Written to the document and saved; the page view still lays text out in one column, so the
    /// change is seen when the file is opened in Word or printed from it.
    /// </remarks>
    public void SetColumns(int count)
    {
        if (this.IsReadOnlyDocument || count == this.ColumnCount)
            return;

        this.document.Execute(new SetColumnsCommand(Math.Clamp(count, 1, 3)));
        this.AfterPageSetupEdit();
    }

    /// <summary>The paper size preset closest to the current page, or null for a custom size.</summary>
    public PaperSize? PaperSize
    {
        get
        {
            var page = this.Document.Page;
            var (w, h) = page.Orientation == PageOrientation.Landscape ? (page.Height, page.Width) : (page.Width, page.Height);
            return Shiny.Controls.Office.Document.PaperSize.Presets.FirstOrDefault(x => Math.Abs(x.Width - w) < 3 && Math.Abs(x.Height - h) < 3);
        }
    }

    /// <summary>Layout ▸ Size: sets the paper, keeping the orientation.</summary>
    public void SetPaperSize(PaperSize size)
    {
        ArgumentNullException.ThrowIfNull(size);

        if (this.IsReadOnlyDocument)
            return;

        this.document.Execute(new SetPageSizeCommand(size));
        this.AfterPageSetupEdit();
    }

    /// <summary>The page colour, or null for plain paper.</summary>
    public ArgbColor? PageColor => this.document.PageColor;

    /// <summary>Design ▸ Page Color; null goes back to no colour.</summary>
    public void SetPageColor(ArgbColor? color)
    {
        if (this.IsReadOnlyDocument)
            return;

        this.document.Execute(new SetPageColorCommand(color));
        this.AfterEdit();
    }

    /// <summary>The document's own text watermark, or null.</summary>
    public string? WatermarkText => this.document.WatermarkText;

    /// <summary>
    /// Design ▸ Watermark: a diagonal text mark written into the document's header the way Word writes
    /// one, so Word shows it too. Null removes it.
    /// </summary>
    public void SetWatermarkText(string? text)
    {
        if (this.IsReadOnlyDocument)
            return;

        var content = this.document.ChromeElements(header: true, DocumentPageKind.Default)
            .Where(x => !WordWatermark.IsWatermarkParagraph(x))
            .ToList();

        if (!string.IsNullOrWhiteSpace(text))
        {
            this.document.EnsureStyle("Header");
            content.Insert(0, WordWatermark.Build(text.Trim()));
        }

        this.document.Execute(new SetHeaderFooterCommand(true, DocumentPageKind.Default, content.Count == 0 ? null : content));
        this.AfterChromeEdit();
    }

    // ---- styles ----

    /// <summary>
    /// The paragraph styles a style gallery offers: Word's built-in set followed by the document's own,
    /// each with the formatting to preview it in.
    /// </summary>
    public IReadOnlyList<DocumentStyleInfo> AvailableStyles
    {
        get
        {
            var resolver = this.document.StyleResolver;
            var styles = new List<DocumentStyleInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var id in WordBuiltInStyles.Gallery)
            {
                seen.Add(id);

                if (resolver.Styles.ContainsKey(id))
                    styles.Add(this.Describe(id, isBuiltIn: true));
                else if (WordBuiltInStyles.Create(id) is { } definition)
                    styles.Add(this.DescribeDefinition(definition));
            }

            foreach (var (id, style) in resolver.Styles)
            {
                if (seen.Contains(id) || OoxmlUnits.EnumAttribute(style, "type") != "paragraph")
                    continue;

                // Hidden and semi-hidden styles are Word's plumbing - TOC levels, footnote text - and
                // do not belong in a gallery.
                if (style.GetFirstChild<SemiHidden>() is not null || style.GetFirstChild<StyleHidden>() is not null)
                    continue;

                if (style.GetFirstChild<PrimaryStyle>() is null && style.GetFirstChild<UnhideWhenUsed>() is not null)
                    continue;

                styles.Add(this.Describe(id, isBuiltIn: false));
            }

            return styles;
        }
    }

    /// <summary>The style id of the caret's paragraph — what the gallery shows selected.</summary>
    public string CurrentStyleId => this.CaretFormat.StyleId;

    /// <summary>
    /// Applies a paragraph style to the selected paragraphs, creating Word's definition first when it
    /// is a built-in the document lacks.
    /// </summary>
    public void ApplyStyle(string styleId)
    {
        if (this.IsReadOnlyDocument || string.IsNullOrEmpty(styleId))
            return;

        this.document.EnsureStyle(styleId);
        this.SetParagraphStyle(styleId == "Normal" && !this.document.StyleResolver.Has("Normal") ? null : styleId);
    }

    DocumentStyleInfo Describe(string id, bool isBuiltIn)
    {
        var resolver = this.document.StyleResolver;
        var run = resolver.RunStyleFor(id);
        var paragraph = resolver.ParagraphFormatFor(id);

        return new DocumentStyleInfo(id, DisplayName(resolver.StyleName(id) ?? id))
        {
            FontFamily = run.FontFamily,
            FontSize = OoxmlUnits.PixelsToPointsApprox(run.FontSize),
            Color = run.Color,
            Bold = run.Bold,
            Italic = run.Italic,
            OutlineLevel = paragraph.OutlineLevel,
            IsBuiltIn = isBuiltIn
        };
    }

    DocumentStyleInfo DescribeDefinition(Style definition)
    {
        var resolver = this.document.StyleResolver;
        var run = resolver.RunStyleFor(definition.BasedOn?.Val?.Value);

        if (definition.StyleRunProperties is { } properties)
            run = WordStyleResolver.ApplyRunProperties(run, properties, resolver.ThemeFonts);

        var paragraph = definition.StyleParagraphProperties is { } pPr
            ? WordStyleResolver.ApplyParagraphProperties(ParagraphFormat.Default, pPr)
            : ParagraphFormat.Default;

        var id = definition.StyleId?.Value ?? string.Empty;

        return new DocumentStyleInfo(id, DisplayName(definition.StyleName?.Val?.Value ?? id))
        {
            FontFamily = run.FontFamily,
            FontSize = OoxmlUnits.PixelsToPointsApprox(run.FontSize),
            Color = run.Color,
            Bold = run.Bold,
            Italic = run.Italic,
            OutlineLevel = paragraph.OutlineLevel,
            IsBuiltIn = true
        };
    }

    /// <summary>Word's gallery capitalises its lower-case built-in names: "heading 1" shows as "Heading 1".</summary>
    static string DisplayName(string name) => name.Length > 0 && char.IsLower(name[0])
        ? char.ToUpperInvariant(name[0]) + name[1..]
        : name;

    /// <summary>Ctrl+Alt+1..3: Heading 1 to 3. Ctrl+Shift+N: Normal.</summary>
    public void ApplyHeading(int level)
        => this.ApplyStyle(level is >= 1 and <= 9 ? $"Heading{level}" : "Normal");

    // ---- references ----

    /// <summary>
    /// Inserts a table of contents built from Heading 1 to 3 before the caret's paragraph — or, when the
    /// document already has one, rebuilds that one in place.
    /// </summary>
    public void InsertTableOfContents()
    {
        if (this.IsReadOnlyDocument)
            return;

        var top = Math.Max(0, this.document.TopBlockOf(this.Selection.Focus.Block));
        this.document.Execute(new InsertTableOfContentsCommand(top, this.TableOfContentsEntries()));
        this.ClampSelection();
        this.AfterEdit();
    }

    /// <summary>References ▸ Update Table: re-reads the headings and their page numbers.</summary>
    public void UpdateTableOfContents()
    {
        if (this.IsReadOnlyDocument || !this.HasTableOfContents)
            return;

        this.InsertTableOfContents();
    }

    /// <summary>True when the document has a TOC field.</summary>
    public bool HasTableOfContents
        => this.document.BodyElement is { } body && WordFields.FindTableOfContents(body).Start is not null;

    /// <summary>The headings a table of contents lists, with the page each prints on.</summary>
    IReadOnlyList<TableOfContentsEntry> TableOfContentsEntries()
    {
        // Paginated at the paper's measure whatever the view is showing: a TOC's page numbers are the
        // printed page's, and a reflowed column has no pages to count.
        var engine = new DocumentLayoutEngine(this.Measurer);
        var laidOut = engine.Layout(this.Document.Blocks, this.Document.Page.ContentWidth);
        var pagination = DocumentPagination.Paginate(laidOut.Blocks, laidOut.Height, this.Document.Page, this.PageGap);

        var tops = new List<LaidOutParagraph>();
        foreach (var block in laidOut.Blocks)
            CollectParagraphs(block, tops);

        var entries = new List<TableOfContentsEntry>();
        var paragraphs = this.Document.Paragraphs;

        for (var i = 0; i < paragraphs.Count; i++)
        {
            var paragraph = paragraphs[i];
            var level = paragraph.Format.OutlineLevel;

            if (level is < 1 or > 3 || paragraph.StyleName?.StartsWith("TOC", StringComparison.OrdinalIgnoreCase) == true)
                continue;

            var text = paragraph.VisibleText.Trim();
            if (text.Length == 0)
                continue;

            var y = i < tops.Count ? tops[i].Y : 0;
            entries.Add(new TableOfContentsEntry(level, text, pagination.PageAtFlow(y).Number, i));
        }

        return entries;

        static void CollectParagraphs(LaidOutBlock block, List<LaidOutParagraph> into)
        {
            switch (block)
            {
                case LaidOutParagraph paragraph:
                    into.Add(paragraph);
                    break;

                case LaidOutTable table:
                    foreach (var cell in table.Cells)
                    {
                        foreach (var inner in cell.Blocks)
                            CollectParagraphs(inner, into);
                    }

                    break;
            }
        }
    }

    /// <summary>Every footnote, in order.</summary>
    public IReadOnlyList<DocumentNote> Footnotes => this.document.Footnotes;

    /// <summary>
    /// Inserts a footnote at the caret with the given text. Drawn at the foot of its page in print layout.
    /// </summary>
    public void InsertFootnote(string text)
    {
        if (this.IsReadOnlyDocument)
            return;

        var at = this.DeleteSelectionIfAny();
        var id = Math.Max(1, this.document.NextId(typeof(Footnote), typeof(FootnoteReference)));

        this.document.Execute(new InsertFootnoteCommand(at, id, text ?? string.Empty));
        this.Selection.MoveTo(at with { Offset = at.Offset + 1 });
        this.AfterEdit();
    }

    /// <summary>Selects the next footnote reference after the caret, wrapping.</summary>
    public bool NextFootnote()
        => this.StepTo(this.document.Footnotes.Select(x => new DocumentRange(x.Reference, x.Reference with { Offset = x.Reference.Offset + 1 })).ToList(), forward: true);

    // ---- navigation ----

    /// <summary>Moves the caret to a story paragraph and brings it on screen — a navigation pane's jump.</summary>
    public void GoToParagraph(int paragraph)
    {
        if (paragraph < 0 || paragraph >= this.Document.Paragraphs.Count)
            return;

        this.ClearObjectSelection();
        this.Selection.MoveTo(new DocumentPosition(paragraph, 0));

        // To the top of the view, not merely into it: jumping to a heading and leaving it on the last line
        // hides the section it introduces.
        var caret = this.CaretRect(this.Selection.Focus);
        this.ScrollTo(this.Pagination.FlowToView(caret.Y) - 8);
        this.RaiseChanged();
    }
}
