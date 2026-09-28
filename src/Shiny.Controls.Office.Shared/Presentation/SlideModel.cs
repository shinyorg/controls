using DocumentFormat.OpenXml;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using D = DocumentFormat.OpenXml.Drawing;

namespace Shiny.Controls.Office.Presentation;


/// <summary>One shape on a slide, positioned in slide coordinates.</summary>
public sealed record SlideShape
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }

    public ShapeGeometry Geometry { get; init; } = ShapeGeometry.Rectangle;
    public ShapeFill Fill { get; init; } = ShapeFill.None;
    public ShapeOutline? Outline { get; init; }
    public ShapeTextBody? Text { get; init; }

    /// <summary>Rotation in degrees, clockwise.</summary>
    public double Rotation { get; init; }

    public bool FlipHorizontal { get; init; }
    public bool FlipVertical { get; init; }

    /// <summary>Image bytes when this shape is a picture.</summary>
    public byte[]? Image { get; init; }

    public string? Name { get; init; }

    /// <summary>Corner radius as a fraction of the smaller side, for rounded rectangles.</summary>
    public double CornerRadius { get; init; } = 0.16;

    /// <summary>
    /// What an empty placeholder says while it is empty — "Click to add title" — laid out in the
    /// placeholder's own formatting. Null for everything else.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Text"/>, which stays empty: the prompt is editing chrome, drawn by the
    /// editor and never by the viewer or a show, and it is not text a search or a caret can land in.
    /// Without it a new slide is a blank white rectangle with nothing saying where to click.
    /// </remarks>
    public ShapeTextBody? Prompt { get; init; }

    /// <summary>A table's laid-out cells, when this shape is a graphic frame holding one.</summary>
    public SlideTable? Table { get; init; }

    /// <summary>
    /// False for shapes painted from the layout or master.
    /// </summary>
    /// <remarks>
    /// Those are template decoration shared by every slide using that layout, not this slide's
    /// content. Letting a click select one would let a user drag the company logo off every slide in
    /// the deck at once, which is never what they meant.
    /// </remarks>
    public bool IsEditable { get; init; }

    /// <summary>
    /// True for a group's own entry: its bounds, and nothing painted.
    /// </summary>
    /// <remarks>
    /// A group is listed after its children, so a click finds it first and selects the whole group —
    /// PowerPoint's behaviour. Double-clicking goes into it and selects the child under the pointer.
    /// </remarks>
    public bool IsGroup { get; init; }

    /// <summary>The drawing's <c>cNvPr</c> id — what an animation's <c>p:spTgt spid</c> names it by.</summary>
    public uint Id { get; init; }

    /// <summary>A drop shadow, when the shape's effect list carries an outer shadow.</summary>
    public ShapeShadow? Shadow { get; init; }

    /// <summary>What clicking the shape itself does in a slide show — <c>a:hlinkClick</c> on its <c>cNvPr</c>.</summary>
    public SlideHyperlink? Hyperlink { get; init; }

    /// <summary>A chart's series and categories, when this is a graphic frame holding one.</summary>
    public SlideChart? Chart { get; init; }

    /// <summary>The embedded audio or video, when this picture is a media poster frame.</summary>
    public SlideMedia? Media { get; init; }

    /// <summary>The placeholder type (<c>title</c>, <c>body</c>, <c>ftr</c>, ...), or null for a free shape.</summary>
    public string? PlaceholderType { get; init; }

    /// <summary>How the shape's text reacts to overflowing it.</summary>
    public TextAutofit Autofit { get; init; }

    /// <summary>Which way the text runs inside the shape.</summary>
    public ShapeTextDirection TextDirection { get; init; }

    /// <summary>True for a shape inside a group. It is only hit once the group has been entered.</summary>
    public bool IsInGroup => this.Group is not null;

    /// <summary>The element this was read from — <c>p:sp</c>, <c>p:pic</c> or <c>p:graphicFrame</c>.</summary>
    internal OpenXmlElement? Element { get; init; }

    /// <summary>The <c>p:grpSp</c> this shape sits directly inside, or null at the top level.</summary>
    internal OpenXmlElement? Group { get; init; }

    /// <summary>
    /// Maps the coordinate space the shape's own transform is written in onto slide coordinates.
    /// </summary>
    /// <remarks>
    /// Identity at the top level. Inside a group it is the group's <c>chOff</c>/<c>chExt</c> to
    /// <c>off</c>/<c>ext</c> mapping — composed through every enclosing group — which is what a move
    /// has to invert, or a child dragged ten pixels lands wherever the group's scale puts it.
    /// </remarks>
    internal ChildSpace Space { get; init; } = ChildSpace.Identity;
}

/// <summary>A per-axis scale and offset from a group's child space to the slide.</summary>
readonly record struct ChildSpace(double ScaleX, double OffsetX, double ScaleY, double OffsetY)
{
    public static readonly ChildSpace Identity = new(1, 0, 1, 0);

    public (double X, double Y, double Width, double Height) ToSlide(double x, double y, double width, double height)
        => (x * this.ScaleX + this.OffsetX, y * this.ScaleY + this.OffsetY, width * this.ScaleX, height * this.ScaleY);

    public (double X, double Y, double Width, double Height) FromSlide(double x, double y, double width, double height)
    {
        var sx = this.ScaleX == 0 ? 1 : this.ScaleX;
        var sy = this.ScaleY == 0 ? 1 : this.ScaleY;
        return ((x - this.OffsetX) / sx, (y - this.OffsetY) / sy, width / sx, height / sy);
    }

    /// <summary>This space, then <paramref name="outer"/>: a child's space inside a nested group.</summary>
    public ChildSpace Then(ChildSpace outer) => new(
        this.ScaleX * outer.ScaleX,
        this.OffsetX * outer.ScaleX + outer.OffsetX,
        this.ScaleY * outer.ScaleY,
        this.OffsetY * outer.ScaleY + outer.OffsetY);
}

public sealed record SlideTableCell(ShapeTextBody? Text, ArgbColor? Fill, int ColumnSpan = 1, int RowSpan = 1, bool IsMerged = false);

public sealed record SlideTable(
    IReadOnlyList<double> ColumnWidths,
    IReadOnlyList<double> RowHeights,
    IReadOnlyList<IReadOnlyList<SlideTableCell>> Rows)
{
    /// <summary>The table style's GUID (<c>a:tableStyleId</c>), or null for none.</summary>
    public string? StyleId { get; init; }

    /// <summary>Which of the style's bands are switched on.</summary>
    public SlideTableStyleFlags StyleFlags { get; init; }

    /// <summary>
    /// A cell's rectangle relative to the table's top-left, with the table drawn at
    /// <paramref name="width"/> x <paramref name="height"/>.
    /// </summary>
    /// <remarks>
    /// The one place a cell's box is worked out, shared by the painter and the editor, so the caret
    /// and the glyphs inside a cell cannot disagree. Stored track sizes are scaled to the frame, and a
    /// cell spanning columns or rows takes all of them.
    /// </remarks>
    public (double X, double Y, double Width, double Height) CellBounds(int row, int column, double width, double height)
    {
        var columns = Scale(this.ColumnWidths, width);
        var rows = Scale(this.RowHeights, height);
        var cell = this.Rows.ElementAtOrDefault(row)?.ElementAtOrDefault(column);
        var columnSpan = Math.Max(1, cell?.ColumnSpan ?? 1);
        var rowSpan = Math.Max(1, cell?.RowSpan ?? 1);

        return (
            columns.Take(column).Sum(),
            rows.Take(row).Sum(),
            columns.Skip(column).Take(columnSpan).Sum(),
            rows.Skip(row).Take(rowSpan).Sum());
    }

    /// <summary>The cell under a point relative to the table's top-left, or null.</summary>
    public (int Row, int Column)? CellAt(double x, double y, double width, double height)
    {
        for (var r = 0; r < this.Rows.Count; r++)
        {
            for (var c = 0; c < this.Rows[r].Count; c++)
            {
                if (this.Rows[r][c].IsMerged)
                    continue;

                var (cx, cy, cw, ch) = this.CellBounds(r, c, width, height);
                if (x >= cx && x < cx + cw && y >= cy && y < cy + ch)
                    return (r, c);
            }
        }

        return null;
    }

    static List<double> Scale(IReadOnlyList<double> sizes, double available)
    {
        if (sizes.Count == 0)
            return [];

        var total = sizes.Sum();
        if (total <= 0)
            return Enumerable.Repeat(available / sizes.Count, sizes.Count).ToList();

        var scale = available / total;
        return sizes.Select(x => x * scale).ToList();
    }
}

/// <summary>One slide, with its shapes already resolved through the layout and master.</summary>
public sealed record Slide
{
    public required int Number { get; init; }
    public required IReadOnlyList<SlideShape> Shapes { get; init; }

    public ShapeFill Background { get; init; } = ShapeFill.None;

    /// <summary>Speaker notes as plain text, or null when the slide has none.</summary>
    public string? Notes { get; init; }

    /// <summary>The slide's title, taken from its title placeholder.</summary>
    public string? Title { get; init; }

    /// <summary>
    /// Hidden from the slide show — <c>show="0"</c> on the slide. Still edited, still in the rail.
    /// </summary>
    public bool IsHidden { get; init; }

    /// <summary>How the slide arrives in a slide show, or null for a cut.</summary>
    public SlideTransition? Transition { get; init; }

    /// <summary>The slide's animation sequence, in play order.</summary>
    public IReadOnlyList<SlideAnimation> Animations { get; init; } = [];

    /// <summary>The background as written on the slide itself, rather than inherited. Null when it inherits.</summary>
    public ShapeFill? OwnBackground { get; init; }
}

/// <summary>How a shape's text reacts to overflowing it — <c>a:bodyPr</c>'s autofit choice.</summary>
public enum TextAutofit
{
    /// <summary>Text overflows the shape (<c>a:noAutofit</c>, or nothing written).</summary>
    None,

    /// <summary>The font shrinks to fit (<c>a:normAutofit</c>).</summary>
    ShrinkOnOverflow,

    /// <summary>The shape grows to fit its text (<c>a:spAutoFit</c>).</summary>
    ResizeShape
}

/// <summary>Which way a shape's text runs — <c>a:bodyPr vert</c>.</summary>
public enum ShapeTextDirection
{
    Horizontal,

    /// <summary>Rotated a quarter turn clockwise (<c>vert</c>).</summary>
    Rotate90,

    /// <summary>Rotated a quarter turn anticlockwise (<c>vert270</c>).</summary>
    Rotate270
}

/// <summary>
/// Where a hyperlink goes: a web address, another slide, or one of PowerPoint's show actions.
/// </summary>
/// <param name="Url">An external address, for an ordinary link.</param>
/// <param name="Slide">A zero-based slide index, for a jump to a slide in this deck.</param>
/// <param name="Action">
/// The raw <c>action</c> attribute — <c>ppaction://hlinkshowjump?jump=nextslide</c> and friends — when
/// the link is one of PowerPoint's built-in show jumps.
/// </param>
public sealed record SlideHyperlink(string? Url, int? Slide = null, string? Action = null)
{
    /// <summary>A tooltip PowerPoint shows on hover (<c>tooltip</c>), if any.</summary>
    public string? Tooltip { get; init; }

    /// <summary>What the link does, for a status line or a tooltip.</summary>
    public override string ToString()
        => this.Url ?? (this.Slide is { } slide ? $"Slide {slide + 1}" : this.Action ?? string.Empty);
}

/// <summary>A layout a slide can be put on, as the current slide's master offers it.</summary>
public sealed record SlideLayoutOption(string Name, int Index, bool IsCurrent)
{
    internal DocumentFormat.OpenXml.Packaging.SlideLayoutPart? Part { get; init; }
}
