using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Notebook;

/// <summary>What is ruled onto a page's background.</summary>
public enum PageRule
{
    Blank,
    Lines,
    Grid,
    Dots
}

/// <summary>What a note item is, which decides how it is hit-tested, painted and edited.</summary>
/// <remarks>
/// A discriminator on one record rather than a type hierarchy, for the same reason
/// <see cref="Shiny.Controls.Office.Presentation.SlideShape"/> carries its picture, its table and its
/// text body side by side: every item has the same bounds, the same handles and the same z-order, and
/// the commands that move and resize one work on all of them without knowing which they hold.
/// </remarks>
public enum NoteItemKind
{
    /// <summary>A resizable text container — OneNote's "outline".</summary>
    Text,

    /// <summary>A drawn shape, optionally with text in it.</summary>
    Shape,

    /// <summary>A picture.</summary>
    Image,

    /// <summary>One freehand stroke.</summary>
    Ink
}

/// <summary>Which pen laid a stroke down, which decides how it is painted and what erases it.</summary>
public enum InkTool
{
    Pen,

    /// <summary>Translucent and wide, painted under everything else so it does not hide the text it marks.</summary>
    Highlighter
}

/// <summary>One sampled point of a stroke, in page coordinates.</summary>
/// <remarks>
/// <para>
/// <paramref name="Pressure"/> is normalised to 0..1, where 0.5 is "no idea" — the value a mouse, a
/// finger on a screen with no force sensor, and a stylus mid-flick all report. It is a multiplier on
/// the stroke width rather than the width itself, so a stroke recorded with a pressure-capable pen and
/// one recorded with a mouse are the same stroke at different fidelities, not different models.
/// </para>
/// </remarks>
public readonly record struct InkPoint(double X, double Y, double Pressure = 0.5);

/// <summary>A freehand stroke: the points, and the pen that drew them.</summary>
public sealed record InkStroke
{
    public required IReadOnlyList<InkPoint> Points { get; init; }

    public ArgbColor Color { get; init; } = new(255, 0x1A, 0x1A, 0x1A);

    /// <summary>Nominal width in page pixels, before pressure is applied.</summary>
    public double Width { get; init; } = 2;

    public InkTool Tool { get; init; } = InkTool.Pen;

    /// <summary>
    /// The stroke's tight bounding box, ignoring the width the pen paints either side of the path.
    /// </summary>
    /// <remarks>
    /// The width is added by whoever needs it. Hit-testing wants it (a hairline stroke is otherwise
    /// almost impossible to tap), the canvas extent wants it, and a stored bound would go stale the
    /// moment a stroke is recoloured to a thicker pen.
    /// </remarks>
    public (double X, double Y, double Width, double Height) Bounds()
    {
        if (this.Points.Count == 0)
            return (0, 0, 0, 0);

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        foreach (var point in this.Points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return (minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>Moves every point, which is how a selected stroke is dragged.</summary>
    public InkStroke Translate(double dx, double dy)
        => this with { Points = [.. this.Points.Select(p => p with { X = p.X + dx, Y = p.Y + dy })] };

    /// <summary>
    /// Scales every point about <paramref name="originX"/>/<paramref name="originY"/>, and the pen
    /// width with it.
    /// </summary>
    /// <remarks>
    /// The width scales by the smaller of the two factors rather than by either one alone. A stroke
    /// stretched only horizontally is still drawn by a round nib, so widening the nib to match would
    /// fatten the vertical parts of it that were never stretched.
    /// </remarks>
    public InkStroke Scale(double originX, double originY, double scaleX, double scaleY)
        => this with
        {
            Points = [.. this.Points.Select(p => p with
            {
                X = originX + (p.X - originX) * scaleX,
                Y = originY + (p.Y - originY) * scaleY
            })],
            Width = this.Width * Math.Min(Math.Abs(scaleX), Math.Abs(scaleY))
        };
}

/// <summary>
/// One thing on a page, positioned freely in page coordinates.
/// </summary>
/// <remarks>
/// <para>
/// Immutable, and replaced wholesale by the edit commands rather than mutated in place. That is what
/// makes the inverse of every edit a copy of the record as it was, which in turn is why undo needs no
/// per-property bookkeeping — the same trade the slide commands make with their XML snapshots, minus
/// the XML.
/// </para>
/// <para>
/// Z-order is the item's index in <see cref="NotebookPage.Items"/>, not a field. A field would let two
/// items claim the same layer and would need renumbering on every insert; the list already orders
/// them, and painting back-to-front is what makes the topmost one the one a click finds.
/// </para>
/// </remarks>
public sealed record NoteItem
{
    public required string Id { get; init; }
    public required NoteItemKind Kind { get; init; }

    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }

    /// <summary>Rotation in degrees, clockwise about the item's centre.</summary>
    public double Rotation { get; init; }

    public ShapeGeometry Geometry { get; init; } = ShapeGeometry.Rectangle;
    public ShapeFill Fill { get; init; } = ShapeFill.None;
    public ShapeOutline? Outline { get; init; }

    /// <summary>Text inside a <see cref="NoteItemKind.Text"/> container or a <see cref="NoteItemKind.Shape"/>.</summary>
    public ShapeTextBody? Text { get; init; }

    /// <summary>Encoded image bytes for a <see cref="NoteItemKind.Image"/>.</summary>
    public byte[]? Image { get; init; }

    /// <summary>The image's content type, so it can be written back to the package under the right extension.</summary>
    public string? ImageContentType { get; init; }

    /// <summary>The stroke for a <see cref="NoteItemKind.Ink"/> item.</summary>
    public InkStroke? Stroke { get; init; }

    /// <summary>Corner radius as a fraction of the smaller side, for rounded rectangles.</summary>
    public double CornerRadius { get; init; } = 0.16;

    /// <summary>
    /// True when the container's height follows its text rather than the user's drag.
    /// </summary>
    /// <remarks>
    /// On by default for text containers and off for shapes, which is the split OneNote and PowerPoint
    /// respectively use: a note outline grows as you type and you only ever set its width, whereas a
    /// shape is a fixed box that text has to fit inside.
    /// </remarks>
    public bool AutoHeight { get; init; }

    /// <summary>A locked item is painted but cannot be selected, moved or erased.</summary>
    public bool Locked { get; init; }

    /// <summary>
    /// True for the highlighter, which paints beneath every other item.
    /// </summary>
    /// <remarks>
    /// Highlighter over text would be ink over the words rather than under them — even at 40% alpha
    /// that greys the glyphs, which is exactly the thing a highlighter is not supposed to do. Kept as a
    /// derived flag rather than a separate list so z-order stays one ordering, sorted at paint time.
    /// </remarks>
    public bool PaintsBehind => this.Kind == NoteItemKind.Ink && this.Stroke?.Tool == InkTool.Highlighter;

    /// <summary>
    /// The item's bounds, taken from the stroke for ink and from the fields for everything else.
    /// </summary>
    /// <remarks>
    /// Ink is the exception because its geometry <i>is</i> its points. Storing a stroke's box in
    /// X/Y/Width/Height as well would be two sources of truth for one rectangle, and the erase-by-point
    /// tool rewrites points without ever touching the fields.
    /// </remarks>
    public (double X, double Y, double Width, double Height) Bounds()
    {
        if (this.Kind != NoteItemKind.Ink || this.Stroke is not { } stroke)
            return (this.X, this.Y, this.Width, this.Height);

        var (x, y, w, h) = stroke.Bounds();
        var pad = stroke.Width * (stroke.Tool == InkTool.Highlighter ? 4 : 1);

        return (x - pad / 2, y - pad / 2, w + pad, h + pad);
    }

    public NoteItem Translate(double dx, double dy)
        => this.Kind == NoteItemKind.Ink && this.Stroke is { } stroke
            ? this with { Stroke = stroke.Translate(dx, dy) }
            : this with { X = this.X + dx, Y = this.Y + dy };

    public string PlainText => this.Text?.PlainText ?? string.Empty;
}

/// <summary>One page, which is a free canvas rather than a fixed artboard.</summary>
public sealed class NotebookPage
{
    public NotebookPage(string id, string title)
    {
        this.Id = id;
        this.Title = title;
    }

    public string Id { get; }

    public string Title { get; set; }

    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset Modified { get; set; } = DateTimeOffset.UtcNow;

    public List<NoteItem> Items { get; } = new();

    public PageRule Rule { get; set; } = PageRule.Blank;

    /// <summary>Spacing between ruled lines or grid squares, in page pixels.</summary>
    public double RuleSpacing { get; set; } = 24;

    public ArgbColor? RuleColor { get; set; }

    public ArgbColor? Background { get; set; }

    /// <summary>
    /// The smallest canvas the page ever shows, so an empty page is still a page.
    /// </summary>
    /// <remarks>
    /// The real extent is this unioned with the content — see <see cref="Extent"/>. A page with one
    /// note in the corner should not be a viewport-sized scroll region, and a page with a diagram
    /// running off the bottom should not clip it.
    /// </remarks>
    public double MinWidth { get; set; } = 1100;

    public double MinHeight { get; set; } = 850;

    /// <summary>Blank space kept past the furthest content, so there is always somewhere to write next.</summary>
    public double Padding { get; set; } = 240;

    /// <summary>The canvas size: the minimum, grown to hold everything on the page plus room to keep going.</summary>
    public (double Width, double Height) Extent()
    {
        var width = this.MinWidth;
        var height = this.MinHeight;

        foreach (var item in this.Items)
        {
            var (x, y, w, h) = item.Bounds();
            width = Math.Max(width, x + w + this.Padding);
            height = Math.Max(height, y + h + this.Padding);
        }

        return (width, height);
    }

    public int IndexOf(string itemId)
    {
        for (var i = 0; i < this.Items.Count; i++)
        {
            if (this.Items[i].Id == itemId)
                return i;
        }

        return -1;
    }
}

/// <summary>A tab of pages.</summary>
public sealed class NotebookSection
{
    public NotebookSection(string id, string title)
    {
        this.Id = id;
        this.Title = title;
    }

    public string Id { get; }

    public string Title { get; set; }

    /// <summary>The tab's colour. Null takes the control's accent, which is what a new section gets.</summary>
    public ArgbColor? Color { get; set; }

    public List<NotebookPage> Pages { get; } = new();
}
