using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Document;

/// <summary>Anything that can appear at block level in a document body or table cell.</summary>
public abstract record DocumentBlock;

/// <summary>Paragraph-level formatting, flattened from the style chain and direct formatting.</summary>
public sealed record ParagraphFormat
{
    public static readonly ParagraphFormat Default = new();

    public TextAlignment Alignment { get; init; } = TextAlignment.Left;

    /// <summary>Left indent in pixels.</summary>
    public double IndentLeft { get; init; }

    public double IndentRight { get; init; }

    /// <summary>Extra indent applied to the first line only; negative for a hanging indent.</summary>
    public double IndentFirstLine { get; init; }

    public double SpaceBefore { get; init; }
    public double SpaceAfter { get; init; }

    /// <summary>Line height multiplier. 1 is single spacing.</summary>
    public double LineSpacing { get; init; } = 1.0;

    public ArgbColor? Shading { get; init; }

    /// <summary>Heading level, 1-9, or 0 when this is body text. Drives outline and navigation.</summary>
    public int OutlineLevel { get; init; }

    /// <summary>The paragraph's borders (<c>w:pBdr</c>), or null when it has none.</summary>
    public ParagraphBorders? Borders { get; init; }

    /// <summary>
    /// The section break this paragraph ends with, when its <c>w:pPr</c> carries a <c>w:sectPr</c>.
    /// </summary>
    public SectionBreakKind SectionBreak { get; init; }

    /// <summary>True when the paragraph must open a new page (<c>w:pageBreakBefore</c>).</summary>
    public bool PageBreakBefore { get; init; }
}


/// <summary>Which edges of a paragraph carry a rule, and in what colour.</summary>
public sealed record ParagraphBorders
{
    public bool Top { get; init; }
    public bool Bottom { get; init; }
    public bool Left { get; init; }
    public bool Right { get; init; }

    /// <summary>Line weight in pixels.</summary>
    public double Width { get; init; } = 1;

    public ArgbColor Color { get; init; } = new(255, 0, 0, 0);

    public bool Any => this.Top || this.Bottom || this.Left || this.Right;
}


/// <summary>What kind of section break a paragraph closes a section with.</summary>
public enum SectionBreakKind
{
    None,

    /// <summary>The next section starts on a new page — the common kind.</summary>
    NextPage,

    /// <summary>The next section carries on on the same page.</summary>
    Continuous,
    EvenPage,
    OddPage
}

/// <summary>A paragraph's reference into <c>numbering.xml</c> — which list it is in, and how deep.</summary>
public sealed record ListNumbering(int NumId, int Level);

/// <summary>A list label, and the reference the number in it was worked out from.</summary>
/// <remarks>
/// <see cref="Text"/> is filled in by a pass over the finished block list, not by the reader. The
/// number in front of a paragraph is a function of every numbered paragraph before it, so a paragraph
/// read on its own — which is what happens after every edit — cannot know it.
/// </remarks>
public sealed record ListLabel(string Text, TextStyle Style, double Indent, double HangingIndent)
{
    /// <summary>The list this label belongs to, or null for one the editor synthesised.</summary>
    public ListNumbering? Numbering { get; init; }

    /// <summary>
    /// True for a bullet, false for a number.
    /// </summary>
    /// <remarks>
    /// Recorded here rather than being inferred from <see cref="Text"/>, which cannot tell the two
    /// apart: a level whose <c>lvlText</c> is a literal <c>-</c> produces exactly the same string a
    /// bullet does, and a toolbar deciding which of its two buttons to light up needs the answer to
    /// come from the definition rather than from the label it rendered.
    /// </remarks>
    public bool IsBullet { get; init; }
}

public sealed record DocumentParagraph(IReadOnlyList<StyledRun> Runs, ParagraphFormat Format) : DocumentBlock
{
    /// <summary>
    /// The OOXML element this paragraph was read from.
    /// </summary>
    /// <remarks>
    /// The anchor for surgical editing: commands mutate this element and re-project the paragraph from
    /// it, so parts of the document the editor does not model are never rebuilt and never lost.
    /// Null for a paragraph the editor synthesised but has not yet attached.
    /// </remarks>
    internal DocumentFormat.OpenXml.Wordprocessing.Paragraph? Element { get; init; }

    public ListLabel? List { get; init; }

    /// <summary>The named style this paragraph came from, useful for outline extraction.</summary>
    public string? StyleName { get; init; }

    /// <summary>
    /// The paragraph's text in the offset space every <see cref="DocumentPosition"/> uses.
    /// </summary>
    /// <remarks>
    /// An inline object or other atomic mark contributes one placeholder character
    /// (<c>U+FFFC</c>) rather than nothing: the caret steps over it as one character, so a text that
    /// left it out was one short for every picture in the paragraph — and a caret could never reach the
    /// end of a line ending in an image.
    /// </remarks>
    public string PlainText => string.Concat(this.Runs.Where(x => !x.IsBreak).Select(TextOf));

    /// <summary>
    /// What a reader sees: the text with object placeholders and tracked deletions left out.
    /// </summary>
    public string VisibleText => string.Concat(this.Runs
        .Where(x => !x.IsBreak && x.Inline is null && x.SourceLength < 0 && x.Style.Revision != TextRevision.Deleted)
        .Select(x => x.Text));

    static string TextOf(StyledRun run)
        => run.Inline is not null || run.SourceLength >= 0
            ? new string(WordParagraphEditor.ObjectPlaceholder[0], run.Length)
            : run.Text;
}

public sealed record DocumentTableCell(IReadOnlyList<DocumentBlock> Blocks)
{
    /// <summary>How many grid columns this cell spans.</summary>
    public int ColumnSpan { get; init; } = 1;

    /// <summary>
    /// True when this cell is continuing a vertical merge from above, in which case it renders as part
    /// of the cell that started the merge rather than as a cell of its own.
    /// </summary>
    public bool IsVerticalContinuation { get; init; }

    public ArgbColor? Shading { get; init; }

    /// <summary>Explicit width in pixels, or null to share the table's width evenly.</summary>
    public double? Width { get; init; }
}

public sealed record DocumentTableRow(IReadOnlyList<DocumentTableCell> Cells)
{
    /// <summary>True for a row marked to repeat as a header when the table breaks across pages.</summary>
    public bool IsHeader { get; init; }
}

public sealed record DocumentTable(IReadOnlyList<DocumentTableRow> Rows) : DocumentBlock
{
    /// <summary>
    /// The <c>w:tbl</c> this table was read from, or null for one the editor has not yet attached.
    /// </summary>
    /// <remarks>
    /// The same anchor <see cref="DocumentParagraph.Element"/> is, and for the same reason: a table
    /// carries conditional formatting, cell margins and a style reference that the projection above
    /// flattens away, so an edit that rebuilt the table from this model would save a plainer table
    /// than the one it opened.
    /// </remarks>
    internal DocumentFormat.OpenXml.Wordprocessing.Table? Element { get; init; }

    /// <summary>Column widths in pixels, from the table grid. Empty when the grid is absent.</summary>
    public IReadOnlyList<double> ColumnWidths { get; init; } = [];

    public bool HasBorders { get; init; } = true;
}

/// <summary>A horizontal rule.</summary>
public sealed record DocumentRule : DocumentBlock;

/// <summary>
/// The four page margins, plus the header and footer distances that sit inside the top and bottom ones.
/// </summary>
/// <remarks>
/// <para>
/// Pixels at 96 dpi, the unit every other measurement in this model uses. <see cref="FromInches"/> and
/// <see cref="FromPoints"/> exist because nobody sets a margin in pixels — a page margin is an inch or
/// three quarters of one, and doing that conversion at every call site is how one of them ends up in
/// points.
/// </para>
/// <para>
/// Separate from <see cref="PageSetup"/> rather than being it: the paper size, the header layout flags
/// and the margins are read together but set apart, and a caller changing the margins should not have
/// to restate the page size to do it.
/// </para>
/// </remarks>
public sealed record PageMargins
{
    /// <summary>Word's Normal: one inch all round.</summary>
    public static readonly PageMargins Normal = FromInches(1, 1, 1, 1);

    /// <summary>Word's Narrow: half an inch all round.</summary>
    public static readonly PageMargins Narrow = FromInches(0.5, 0.5, 0.5, 0.5);

    /// <summary>Word's Moderate: an inch top and bottom, three quarters at the sides.</summary>
    public static readonly PageMargins Moderate = FromInches(0.75, 1, 0.75, 1);

    /// <summary>Word's Wide: an inch top and bottom, two inches at the sides.</summary>
    public static readonly PageMargins Wide = FromInches(2, 1, 2, 1);

    public double Left { get; init; } = 96;
    public double Top { get; init; } = 96;
    public double Right { get; init; } = 96;
    public double Bottom { get; init; } = 96;

    /// <summary>
    /// Distance from the top of the page to the top of the header.
    /// </summary>
    /// <remarks>
    /// Inside <see cref="Top"/>, not added to it — the header sits in the top margin, above where body
    /// text starts, which is why a header can be present without moving the body at all.
    /// </remarks>
    public double Header { get; init; } = 48;

    /// <summary>Distance from the bottom of the page to the bottom of the footer.</summary>
    public double Footer { get; init; } = 48;

    /// <summary>The same margin on all four sides, in pixels.</summary>
    public static PageMargins Uniform(double pixels) => new()
    {
        Left = pixels,
        Top = pixels,
        Right = pixels,
        Bottom = pixels
    };

    /// <summary>Margins in inches, which is how a page is actually described.</summary>
    public static PageMargins FromInches(double left, double top, double right, double bottom) => new()
    {
        Left = OoxmlUnits.InchesToPixels(left),
        Top = OoxmlUnits.InchesToPixels(top),
        Right = OoxmlUnits.InchesToPixels(right),
        Bottom = OoxmlUnits.InchesToPixels(bottom),
        Header = OoxmlUnits.InchesToPixels(0.5),
        Footer = OoxmlUnits.InchesToPixels(0.5)
    };

    public static PageMargins FromPoints(double left, double top, double right, double bottom) => new()
    {
        Left = OoxmlUnits.PointsToPixels(left),
        Top = OoxmlUnits.PointsToPixels(top),
        Right = OoxmlUnits.PointsToPixels(right),
        Bottom = OoxmlUnits.PointsToPixels(bottom),
        Header = OoxmlUnits.PointsToPixels(36),
        Footer = OoxmlUnits.PointsToPixels(36)
    };

    /// <summary>True when every measurement is a finite, non-negative length.</summary>
    public bool IsValid =>
        IsLength(this.Left) && IsLength(this.Top) && IsLength(this.Right) && IsLength(this.Bottom)
        && IsLength(this.Header) && IsLength(this.Footer);

    static bool IsLength(double value) => Double.IsFinite(value) && value >= 0;
}


/// <summary>One entry in the margin gallery both editing toolbars offer.</summary>
/// <param name="Name">What the entry is called, matching Word's own names.</param>
/// <param name="Description">The measurements, so the name is not the only thing to go on.</param>
/// <param name="Margins">What choosing it applies.</param>
public sealed record PageMarginPreset(string Name, string Description, PageMargins Margins);


/// <summary>
/// The margin presets the MAUI and Blazor toolbars offer, in one place so they cannot differ.
/// </summary>
/// <remarks>
/// Word's own four, with Word's own names and measurements. A preset offered on one host and not the
/// other would be a difference with nothing behind it, and a gallery invented here would be one more
/// set of numbers for a reader to learn.
/// </remarks>
/// <summary>Which way round the paper is.</summary>
public enum PageOrientation
{
    Portrait,
    Landscape
}


public static class PageMarginPresets
{
    public static IReadOnlyList<PageMarginPreset> All { get; } =
    [
        new("Normal", "1\" all round", PageMargins.Normal),
        new("Narrow", "0.5\" all round", PageMargins.Narrow),
        new("Moderate", "1\" top and bottom, 0.75\" sides", PageMargins.Moderate),
        new("Wide", "1\" top and bottom, 2\" sides", PageMargins.Wide)
    ];
}


/// <summary>Page geometry, from the section properties.</summary>
/// <remarks>
/// Used two ways. In reflow it only supplies a sensible measure; in print layout it is the actual
/// paper — the page drawn on screen is this size, and content is inset by these margins.
/// </remarks>
public sealed record PageSetup
{
    public static readonly PageSetup Letter = new();

    /// <summary>Page width in pixels.</summary>
    public double Width { get; init; } = 816;   // 8.5in at 96dpi

    public double Height { get; init; } = 1056; // 11in at 96dpi
    public double MarginLeft { get; init; } = 96;
    public double MarginRight { get; init; } = 96;
    public double MarginTop { get; init; } = 96;
    public double MarginBottom { get; init; } = 96;

    /// <summary>Distance from the top of the page to the top of the header.</summary>
    /// <remarks>
    /// <c>w:pgMar/@w:header</c>, and unrelated to <see cref="MarginTop"/> — the header sits in the
    /// top margin, above where body text starts, which is why a header can be present without moving
    /// the body at all.
    /// </remarks>
    public double HeaderDistance { get; init; } = 48;   // 0.5in

    /// <summary>Distance from the bottom of the page to the bottom of the footer.</summary>
    public double FooterDistance { get; init; } = 48;

    /// <summary>
    /// Which way round the paper is.
    /// </summary>
    /// <remarks>
    /// Carried separately from <see cref="Width"/> and <see cref="Height"/> rather than inferred from
    /// which is larger, because <c>w:orient</c> is its own attribute and Word writes it: a landscape
    /// section has the two swapped <i>and</i> says so, and a document that dropped the attribute would
    /// come back portrait on the next open even though the dimensions were right.
    /// </remarks>
    public PageOrientation Orientation { get; init; } = PageOrientation.Portrait;

    /// <summary>The same setup with the paper turned, swapping the two dimensions with it.</summary>
    public PageSetup WithOrientation(PageOrientation orientation)
    {
        if (orientation == this.Orientation)
            return this;

        // The margins are deliberately left alone. Word keeps them as they were when the page turns,
        // and recomputing them would silently rewrite a page setup the user chose.
        return this with
        {
            Orientation = orientation,
            Width = this.Height,
            Height = this.Width
        };
    }

    /// <summary>True when the section declares a distinct first-page header and footer (<c>w:titlePg</c>).</summary>
    public bool DifferentFirstPage { get; init; }

    /// <summary>True when the document declares distinct even-page headers and footers.</summary>
    public bool DifferentOddAndEvenPages { get; init; }

    /// <summary>The margins on their own, for reading back what a document is currently set to.</summary>
    public PageMargins Margins => new()
    {
        Left = this.MarginLeft,
        Top = this.MarginTop,
        Right = this.MarginRight,
        Bottom = this.MarginBottom,
        Header = this.HeaderDistance,
        Footer = this.FooterDistance
    };

    /// <summary>This setup with different margins and the same paper.</summary>
    public PageSetup WithMargins(PageMargins margins)
    {
        ArgumentNullException.ThrowIfNull(margins);

        return this with
        {
            MarginLeft = margins.Left,
            MarginTop = margins.Top,
            MarginRight = margins.Right,
            MarginBottom = margins.Bottom,
            HeaderDistance = margins.Header,
            FooterDistance = margins.Footer
        };
    }

    public double ContentWidth => Math.Max(1, this.Width - this.MarginLeft - this.MarginRight);

    /// <summary>Height available to body text on one page.</summary>
    public double ContentHeight => Math.Max(1, this.Height - this.MarginTop - this.MarginBottom);

    /// <summary>Which header and footer a given one-based page number uses.</summary>
    public DocumentPageKind KindOf(int pageNumber)
    {
        if (this.DifferentFirstPage && pageNumber == 1)
            return DocumentPageKind.First;

        if (this.DifferentOddAndEvenPages && pageNumber % 2 == 0)
            return DocumentPageKind.Even;

        return DocumentPageKind.Default;
    }
}
