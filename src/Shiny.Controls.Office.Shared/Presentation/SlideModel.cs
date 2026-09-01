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

    /// <summary>The element this was read from — <c>p:sp</c>, <c>p:pic</c> or <c>p:graphicFrame</c>.</summary>
    internal OpenXmlElement? Element { get; init; }
}

public sealed record SlideTableCell(ShapeTextBody? Text, ArgbColor? Fill, int ColumnSpan = 1, int RowSpan = 1, bool IsMerged = false);

public sealed record SlideTable(
    IReadOnlyList<double> ColumnWidths,
    IReadOnlyList<double> RowHeights,
    IReadOnlyList<IReadOnlyList<SlideTableCell>> Rows);

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
