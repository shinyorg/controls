using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Presentation;
using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using D = DocumentFormat.OpenXml.Drawing;

namespace Shiny.Controls.Office.Presentation;

/// <summary>A built-in table style the Table Design tab offers.</summary>
public sealed record SlideTableStyleOption(string Id, string Name, int Accent);

/// <summary>Which of a table's style bands are switched on — the Table Style Options checkboxes.</summary>
public readonly record struct SlideTableStyleFlags(bool HeaderRow, bool BandedRows, bool FirstColumn, bool TotalRow, bool LastColumn, bool BandedColumns);

/// <summary>
/// The built-in table styles, and an approximation of how they paint.
/// </summary>
/// <remarks>
/// <para>
/// A table style is a separate part (<c>tableStyles.xml</c>) describing a dozen bands. PowerPoint's
/// built-in ones are referenced by GUID and defined in PowerPoint itself, so a deck frequently names a
/// style whose definition it does not carry. The Medium Style 2 family — PowerPoint's default, and what
/// the editor inserts — is drawn from its known shape instead: an accent-coloured header row with white
/// bold text, and rows banded in two tints of the same accent.
/// </para>
/// </remarks>
public static class SlideTableStyles
{
    /// <summary>The styles the Table Design gallery offers, PowerPoint's default first.</summary>
    public static IReadOnlyList<SlideTableStyleOption> Gallery { get; } =
    [
        new("{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}", "Medium Style 2 - Accent 1", 1),
        new("{21E4AEA4-8DFA-4A89-87EB-49C32662AFE8}", "Medium Style 2 - Accent 2", 2),
        new("{F5AB1C69-6EDB-4FF4-983F-18BD219EF322}", "Medium Style 2 - Accent 3", 3),
        new("{00A15C55-8517-42AA-B614-E9B94910E393}", "Medium Style 2 - Accent 4", 4),
        new("{7DF18680-E054-41AD-8BC1-D1AEF772440D}", "Medium Style 2 - Accent 5", 5),
        new("{93296810-A885-4BE3-A3E7-6D5BEEA58F35}", "Medium Style 2 - Accent 6", 6),
        new("{073A0DAA-6AF3-43AB-8588-CEC1D06C72B9}", "Medium Style 2", 0),
        new("{5940675A-B579-460E-94D1-54222C63F5DA}", "No Style, Table Grid", -1)
    ];

    internal sealed record Resolved(ArgbColor Header, ArgbColor Band1, ArgbColor Band2, ArgbColor HeaderInk, SlideTableStyleFlags Flags, bool Plain)
    {
        public bool IsHeader(int row, int column)
            => !this.Plain && this.Flags.HeaderRow && row == 0;

        public ArgbColor? FillFor(int row, int column)
        {
            if (this.Plain)
                return null;

            if (this.Flags.HeaderRow && row == 0)
                return this.Header;

            var bodyRow = this.Flags.HeaderRow ? row - 1 : row;
            if (this.Flags.BandedRows)
                return bodyRow % 2 == 0 ? this.Band1 : this.Band2;

            return this.Band2;
        }
    }

    internal static Resolved? Resolve(D.Table table, ThemeColors colors)
    {
        var properties = table.TableProperties;
        var id = properties?.GetFirstChild<D.TableStyleId>()?.Text;
        var flags = FlagsOf(properties);

        var option = Gallery.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (option is null)
            return null;

        if (option.Accent < 0)
            return new Resolved(default, default, default, default, flags, Plain: true);

        var accent = colors.Resolve(option.Accent == 0 ? "dk1" : $"accent{option.Accent}") ?? new ArgbColor(255, 0x44, 0x72, 0xC4);
        var white = colors.Resolve("lt1") ?? new ArgbColor(255, 255, 255, 255);

        return new Resolved(
            accent,
            Tint(accent, 0.4),
            Tint(accent, 0.2),
            white,
            flags,
            Plain: false);
    }

    internal static SlideTableStyleFlags FlagsOf(D.TableProperties? properties) => new(
        properties?.FirstRow?.Value ?? false,
        properties?.BandRow?.Value ?? false,
        properties?.FirstColumn?.Value ?? false,
        properties?.LastRow?.Value ?? false,
        properties?.LastColumn?.Value ?? false,
        properties?.BandColumn?.Value ?? false);

    /// <summary>A colour mixed toward white, keeping <paramref name="amount"/> of it — DrawingML's tint.</summary>
    static ArgbColor Tint(ArgbColor color, double amount) => new(
        color.A,
        (byte)Math.Round(color.R * amount + 255 * (1 - amount)),
        (byte)Math.Round(color.G * amount + 255 * (1 - amount)),
        (byte)Math.Round(color.B * amount + 255 * (1 - amount)));

    /// <summary>A header cell's text: bold, in the header ink unless a run chose its own colour.</summary>
    internal static ShapeTextBody Header(ShapeTextBody body, ArgbColor ink) => body with
    {
        Paragraphs = body.Paragraphs.Select(p => p with
        {
            Runs = p.Runs.Select(r => r with
            {
                Style = r.Style with
                {
                    Bold = true,
                    Color = r.Style.Color == TextStyle.Default.Color ? ink : r.Style.Color
                }
            }).ToList()
        }).ToList()
    };
}

/// <summary>What a table-layout command does.</summary>
public enum SlideTableEdit
{
    InsertRowAbove,
    InsertRowBelow,
    InsertColumnLeft,
    InsertColumnRight,
    DeleteRow,
    DeleteColumn,
    MergeCells,
    SplitCells
}

/// <summary>
/// Edits a table's structure — rows, columns and merges — restoring the whole frame on undo.
/// </summary>
/// <remarks>
/// <para>
/// A table keeps one <c>a:tc</c> per grid column in every row even where cells are merged (the covered
/// ones carry <c>hMerge</c>/<c>vMerge</c>), so a column is an index into each row's cells and inserting
/// one is an insert at that index in every row, plus a <c>a:gridCol</c>.
/// </para>
/// <para>
/// The frame grows with the table: its extent is what the table is drawn at, and a row added inside an
/// unchanged frame would squeeze every other row to make room.
/// </para>
/// </remarks>
public sealed record EditSlideTableCommand(int Slide, int Shape, SlideTableEdit Edit, int Row, int Column, int ToRow = -1, int ToColumn = -1) : SlideCommand
{
    public override string Name => this.Edit switch
    {
        SlideTableEdit.InsertRowAbove or SlideTableEdit.InsertRowBelow => "Insert row",
        SlideTableEdit.InsertColumnLeft or SlideTableEdit.InsertColumnRight => "Insert column",
        SlideTableEdit.DeleteRow => "Delete row",
        SlideTableEdit.DeleteColumn => "Delete column",
        SlideTableEdit.MergeCells => "Merge cells",
        _ => "Split cells"
    };

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (ShapeAt(context, this.Slide, this.Shape) is not { Element: GraphicFrame frame } ||
            frame.Graphic?.GraphicData?.GetFirstChild<D.Table>() is not { } table)
        {
            return new NoOpSlideCommand();
        }

        var inverse = CaptureShape(context, this.Slide, this.Shape);
        var rows = table.Elements<D.TableRow>().ToList();
        var grid = table.TableGrid?.Elements<D.GridColumn>().ToList() ?? [];

        if (this.Row < 0 || this.Row >= rows.Count || this.Column < 0 || this.Column >= grid.Count)
            return new NoOpSlideCommand();

        switch (this.Edit)
        {
            case SlideTableEdit.InsertRowAbove:
            case SlideTableEdit.InsertRowBelow:
            {
                var source = rows[this.Row];
                var fresh = (D.TableRow)source.CloneNode(true);
                foreach (var cell in fresh.Elements<D.TableCell>())
                {
                    cell.RowSpan = null;
                    cell.VerticalMerge = null;
                    Empty(cell);
                }

                if (this.Edit == SlideTableEdit.InsertRowAbove)
                    source.InsertBeforeSelf(fresh);
                else
                    source.InsertAfterSelf(fresh);

                Grow(frame, 0, source.Height?.Value ?? 0);
                break;
            }

            case SlideTableEdit.InsertColumnLeft:
            case SlideTableEdit.InsertColumnRight:
            {
                var at = this.Edit == SlideTableEdit.InsertColumnLeft ? this.Column : this.Column + 1;
                var width = grid[this.Column].Width?.Value ?? OoxmlUnits.PixelsToEmu(120);
                var column = new D.GridColumn { Width = width };

                if (at < grid.Count)
                    grid[at].InsertBeforeSelf(column);
                else
                    grid[^1].InsertAfterSelf(column);

                foreach (var row in rows)
                {
                    var cells = row.Elements<D.TableCell>().ToList();
                    var template = cells[Math.Min(this.Column, cells.Count - 1)];
                    var fresh = (D.TableCell)template.CloneNode(true);
                    fresh.GridSpan = null;
                    fresh.HorizontalMerge = null;
                    fresh.RowSpan = null;
                    fresh.VerticalMerge = null;
                    Empty(fresh);

                    if (at < cells.Count)
                        cells[at].InsertBeforeSelf(fresh);
                    else
                        cells[^1].InsertAfterSelf(fresh);
                }

                Grow(frame, width, 0);
                break;
            }

            case SlideTableEdit.DeleteRow:
            {
                if (rows.Count == 1)
                    return this.DeleteFrame(context, inverse);

                var height = rows[this.Row].Height?.Value ?? 0;
                rows[this.Row].Remove();
                Grow(frame, 0, -height);
                break;
            }

            case SlideTableEdit.DeleteColumn:
            {
                if (grid.Count == 1)
                    return this.DeleteFrame(context, inverse);

                var width = grid[this.Column].Width?.Value ?? 0;
                grid[this.Column].Remove();
                foreach (var row in rows)
                    row.Elements<D.TableCell>().ElementAtOrDefault(this.Column)?.Remove();

                Grow(frame, -width, 0);
                break;
            }

            case SlideTableEdit.MergeCells:
            {
                var (r1, r2) = Order(this.Row, this.ToRow < 0 ? this.Row : this.ToRow);
                var (c1, c2) = Order(this.Column, this.ToColumn < 0 ? this.Column : this.ToColumn);
                r2 = Math.Min(r2, rows.Count - 1);
                c2 = Math.Min(c2, grid.Count - 1);
                if (r1 == r2 && c1 == c2)
                    return new NoOpSlideCommand();

                Unmerge(rows, r1, r2, c1, c2);
                var anchor = rows[r1].Elements<D.TableCell>().ElementAt(c1);

                for (var r = r1; r <= r2; r++)
                {
                    var cells = rows[r].Elements<D.TableCell>().ToList();
                    for (var c = c1; c <= c2; c++)
                    {
                        var cell = cells[c];
                        if (ReferenceEquals(cell, anchor))
                            continue;

                        // The covered cells' text moves into the merged cell rather than disappearing.
                        if (!string.IsNullOrWhiteSpace(cell.TextBody?.InnerText) && anchor.TextBody is { } target)
                        {
                            foreach (var paragraph in cell.TextBody!.Elements<D.Paragraph>().ToList())
                                target.AppendChild(paragraph.CloneNode(true));

                            Empty(cell);
                        }

                        if (c > c1)
                            cell.HorizontalMerge = true;

                        if (r > r1)
                            cell.VerticalMerge = true;
                    }
                }

                if (c2 > c1)
                    anchor.GridSpan = c2 - c1 + 1;

                if (r2 > r1)
                    anchor.RowSpan = r2 - r1 + 1;

                break;
            }

            case SlideTableEdit.SplitCells:
            {
                var (r1, r2) = Order(this.Row, this.ToRow < 0 ? this.Row : this.ToRow);
                var (c1, c2) = Order(this.Column, this.ToColumn < 0 ? this.Column : this.ToColumn);

                // Splitting a merged cell undoes its whole merge, wherever the span reaches.
                var anchor = rows[r1].Elements<D.TableCell>().ElementAt(c1);
                r2 = Math.Max(r2, r1 + (int)(anchor.RowSpan?.Value ?? 1) - 1);
                c2 = Math.Max(c2, c1 + (int)(anchor.GridSpan?.Value ?? 1) - 1);
                Unmerge(rows, r1, Math.Min(r2, rows.Count - 1), c1, Math.Min(c2, grid.Count - 1));
                break;
            }
        }

        context.Reproject(this.Slide);
        return inverse;
    }

    IEditCommand<SlideDeck> DeleteFrame(SlideDeck context, IEditCommand<SlideDeck> inverse)
        => new DeleteShapeCommand(this.Slide, this.Shape).Apply(context);

    static (int, int) Order(int a, int b) => a <= b ? (a, b) : (b, a);

    static void Unmerge(List<D.TableRow> rows, int r1, int r2, int c1, int c2)
    {
        for (var r = r1; r <= r2; r++)
        {
            var cells = rows[r].Elements<D.TableCell>().ToList();
            for (var c = c1; c <= c2 && c < cells.Count; c++)
            {
                cells[c].GridSpan = null;
                cells[c].RowSpan = null;
                cells[c].HorizontalMerge = null;
                cells[c].VerticalMerge = null;
            }
        }
    }

    /// <summary>One empty paragraph, keeping the first one's properties and end mark so typing into it matches.</summary>
    static void Empty(D.TableCell cell)
    {
        if (cell.TextBody is not { } body)
            return;

        var first = body.Elements<D.Paragraph>().FirstOrDefault();
        foreach (var paragraph in body.Elements<D.Paragraph>().ToList())
            paragraph.Remove();

        var fresh = new D.Paragraph();
        if (first?.ParagraphProperties?.CloneNode(true) is D.ParagraphProperties properties)
            fresh.AppendChild(properties);

        fresh.AppendChild(first?.GetFirstChild<D.EndParagraphRunProperties>()?.CloneNode(true) as D.EndParagraphRunProperties
            ?? new D.EndParagraphRunProperties { Language = "en-US" });

        body.AppendChild(fresh);
    }

    static void Grow(GraphicFrame frame, long dx, long dy)
    {
        if (frame.Transform?.Extents is not { } extents)
            return;

        extents.Cx = Math.Max(1, (extents.Cx?.Value ?? 0) + dx);
        extents.Cy = Math.Max(1, (extents.Cy?.Value ?? 0) + dy);
    }
}

/// <summary>Fills a block of table cells, or clears their fill.</summary>
public sealed record SetTableCellFillCommand(int Slide, int Shape, int Row, int Column, int ToRow, int ToColumn, ArgbColor? Color) : SlideCommand
{
    public override string Name => "Cell shading";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (ShapeAt(context, this.Slide, this.Shape) is not { Element: GraphicFrame frame } ||
            frame.Graphic?.GraphicData?.GetFirstChild<D.Table>() is not { } table)
        {
            return new NoOpSlideCommand();
        }

        var inverse = CaptureShape(context, this.Slide, this.Shape);
        var rows = table.Elements<D.TableRow>().ToList();

        for (var r = Math.Min(this.Row, this.ToRow); r <= Math.Max(this.Row, this.ToRow) && r < rows.Count; r++)
        {
            var cells = rows[r].Elements<D.TableCell>().ToList();
            for (var c = Math.Min(this.Column, this.ToColumn); c <= Math.Max(this.Column, this.ToColumn) && c < cells.Count; c++)
            {
                var properties = cells[c].TableCellProperties ??= new D.TableCellProperties();
                foreach (var fill in properties.ChildElements.Where(x => x.LocalName is "noFill" or "solidFill" or "gradFill" or "blipFill" or "pattFill" or "grpFill").ToList())
                    fill.Remove();

                if (this.Color is { } color)
                {
                    // After the borders and cell3D, before headers and extLst.
                    var fillElement = new D.SolidFill(new D.RgbColorModelHex { Val = SetShapeFillCommand.Hex(color) });
                    var before = properties.ChildElements.FirstOrDefault(x => x.LocalName is "headers" or "extLst");
                    if (before is not null)
                        properties.InsertBefore(fillElement, before);
                    else
                        properties.AppendChild(fillElement);
                }
            }
        }

        context.Reproject(this.Slide);
        return inverse;
    }
}

/// <summary>Sets a table's style, or one of its style option flags.</summary>
public sealed record SetTableStyleCommand(int Slide, int Shape, string? StyleId, SlideTableStyleFlags? Flags) : SlideCommand
{
    public override string Name => "Table style";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (ShapeAt(context, this.Slide, this.Shape) is not { Element: GraphicFrame frame } ||
            frame.Graphic?.GraphicData?.GetFirstChild<D.Table>() is not { } table)
        {
            return new NoOpSlideCommand();
        }

        var inverse = CaptureShape(context, this.Slide, this.Shape);
        var properties = table.TableProperties;
        if (properties is null)
        {
            properties = new D.TableProperties();
            table.PrependChild(properties);
        }

        if (this.StyleId is { } id)
        {
            foreach (var existing in properties.Elements<D.TableStyleId>().ToList())
                existing.Remove();

            properties.AppendChild(new D.TableStyleId { Text = id });
        }

        if (this.Flags is { } flags)
        {
            properties.FirstRow = flags.HeaderRow ? true : null;
            properties.BandRow = flags.BandedRows ? true : null;
            properties.FirstColumn = flags.FirstColumn ? true : null;
            properties.LastRow = flags.TotalRow ? true : null;
            properties.LastColumn = flags.LastColumn ? true : null;
            properties.BandColumn = flags.BandedColumns ? true : null;
        }

        context.Reproject(this.Slide);
        return inverse;
    }
}
