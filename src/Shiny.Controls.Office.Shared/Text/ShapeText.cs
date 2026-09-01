using DocumentFormat.OpenXml;
using D = DocumentFormat.OpenXml.Drawing;

namespace Shiny.Controls.Office.Text;


/// <summary>A paragraph inside a shape's text body.</summary>
public sealed record ShapeParagraph(IReadOnlyList<StyledRun> Runs)
{
    public TextAlignment Alignment { get; init; } = TextAlignment.Left;

    /// <summary>Nesting level, 0-8, which drives indent and the bullet glyph.</summary>
    public int Level { get; init; }

    /// <summary>The mark drawn in front of the paragraph — a glyph, or a resolved number.</summary>
    public string? Bullet { get; init; }

    /// <summary>
    /// Which kind of list the paragraph is in.
    /// </summary>
    /// <remarks>
    /// Not inferable from <see cref="Bullet"/>: an auto-numbered paragraph arrives here with its
    /// number already resolved to text, and "1." is a perfectly good literal bullet glyph. A toolbar
    /// deciding which of its two buttons is lit needs the answer from the properties.
    /// </remarks>
    public ListStyle List { get; init; }

    public double SpaceBefore { get; init; }
    public double SpaceAfter { get; init; }
    public double LineSpacing { get; init; } = 1.0;

    public string PlainText => string.Concat(this.Runs.Where(x => !x.IsBreak).Select(x => x.Text));

    /// <summary>
    /// The <c>a:p</c> this was read from, or null for a paragraph that came from a layout or master.
    /// </summary>
    /// <remarks>
    /// Edits go straight into this element rather than into a rebuilt paragraph, for the same reason
    /// the Word editor keeps its runs: a run carries language, spelling state and formatting the model
    /// does not represent, and rebuilding one to change a character throws all of that away.
    /// </remarks>
    internal D.Paragraph? Element { get; init; }
}

public enum TextAnchor
{
    Top,
    Middle,
    Bottom
}

public sealed record ShapeTextBody(IReadOnlyList<ShapeParagraph> Paragraphs)
{
    public TextAnchor Anchor { get; init; } = TextAnchor.Top;
    public double InsetLeft { get; init; } = 9.6;
    public double InsetRight { get; init; } = 9.6;
    public double InsetTop { get; init; } = 4.8;
    public double InsetBottom { get; init; } = 4.8;
    public bool WordWrap { get; init; } = true;

    /// <summary>
    /// Scale PowerPoint recorded for shrink-on-overflow autofit. Honoured rather than recomputed,
    /// because recomputing it needs the exact font metrics PowerPoint used.
    /// </summary>
    public double FontScale { get; init; } = 1.0;

    public double LineSpaceReduction { get; init; }

    public string PlainText => string.Join(Environment.NewLine, this.Paragraphs.Select(x => x.PlainText));

    /// <summary>
    /// The text body this was read from, when the shape lives on the slide itself.
    /// </summary>
    /// <remarks>
    /// Typed as the base rather than <c>D.TextBody</c> because a shape's is <c>p:txBody</c> and a
    /// table cell's is <c>a:txBody</c> — two unrelated classes with the same <c>a:p</c> children.
    /// </remarks>
    internal OpenXmlCompositeElement? Element { get; init; }
}
