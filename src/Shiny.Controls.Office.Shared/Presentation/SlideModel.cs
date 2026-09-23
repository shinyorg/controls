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
}

/// <summary>A layout a slide can be put on, as the current slide's master offers it.</summary>
public sealed record SlideLayoutOption(string Name, int Index, bool IsCurrent)
{
    internal DocumentFormat.OpenXml.Packaging.SlideLayoutPart? Part { get; init; }
}
