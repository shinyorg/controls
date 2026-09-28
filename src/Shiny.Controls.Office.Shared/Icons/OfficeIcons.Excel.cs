namespace Shiny.Controls.Office.Icons;

/// <summary>
/// The spreadsheet ribbon's Excel-parity marks — merge, borders, freeze, sort and filter, conditional
/// formatting, tables, charts, notes, links, the formula tools and the view toggles.
/// </summary>
/// <remarks>
/// A file of its own so the Word and PowerPoint sets can grow beside it without the three editing the
/// same switch. Same rules as the rest of the set: 24x24 grid, one stroke weight, no colour of its own,
/// commands as data rather than path strings.
/// </remarks>
public static partial class OfficeIcons
{
    static IReadOnlyList<OfficeIconShape> ExcelShapes(OfficeIcon icon) => icon switch
    {
        OfficeIcon.MergeCells =>
        [
            OfficeIconShape.Rectangle(3f, 5f, 18f, 14f, 1.5f),
            OfficeIconShape.Line(4.8f, 12f, 10f, 12f),
            OfficeIconShape.Polyline(7f, 9.5f, 4.8f, 12f, 7f, 14.5f),
            OfficeIconShape.Line(14f, 12f, 19.2f, 12f),
            OfficeIconShape.Polyline(17f, 9.5f, 19.2f, 12f, 17f, 14.5f)
        ],

        OfficeIcon.BorderBottom => [.. Dots(top: true, bottom: false, left: true, right: true, inside: true), OfficeIconShape.Line(4f, 20f, 20f, 20f)],
        OfficeIcon.BorderAll =>
        [
            OfficeIconShape.Rectangle(4f, 4f, 16f, 16f),
            OfficeIconShape.Line(12f, 4f, 12f, 20f),
            OfficeIconShape.Line(4f, 12f, 20f, 12f)
        ],
        OfficeIcon.BorderOutside => [OfficeIconShape.Rectangle(4f, 4f, 16f, 16f), .. Dots(top: false, bottom: false, left: false, right: false, inside: true)],
        OfficeIcon.BorderNone => Dots(top: true, bottom: true, left: true, right: true, inside: true),

        OfficeIcon.FreezePanes =>
        [
            OfficeIconShape.Rectangle(3f, 4f, 18f, 16f, 1.5f),
            OfficeIconShape.Line(3f, 9f, 21f, 9f),
            OfficeIconShape.Line(8f, 4f, 8f, 20f)
        ],

        OfficeIcon.SortAscending => [.. SortArrow(), .. Bars(3f, 5.5f, 8f, 10f)],
        OfficeIcon.SortDescending => [.. SortArrow(), .. Bars(10f, 8f, 5.5f, 3f)],

        OfficeIcon.Filter => [Funnel(4f, 5f, 20f, 19f)],
        OfficeIcon.FilterClear =>
        [
            Funnel(3f, 5f, 15f, 17f),
            OfficeIconShape.Line(15.5f, 13.5f, 20.5f, 18.5f),
            OfficeIconShape.Line(20.5f, 13.5f, 15.5f, 18.5f)
        ],

        OfficeIcon.ConditionalFormat =>
        [
            OfficeIconShape.Rectangle(3f, 4f, 18f, 16f, 1.5f),
            OfficeIconShape.Rectangle(6f, 7f, 9f, 2.5f),
            OfficeIconShape.Rectangle(6f, 10.75f, 12f, 2.5f),
            OfficeIconShape.Rectangle(6f, 14.5f, 5f, 2.5f)
        ],

        OfficeIcon.FormatAsTable =>
        [
            OfficeIconShape.Rectangle(3f, 4f, 18f, 16f, 1f),
            OfficeIconShape.Rectangle(3f, 4f, 18f, 4f),
            OfficeIconShape.Line(3f, 12f, 21f, 12f),
            OfficeIconShape.Line(3f, 16f, 21f, 16f),
            OfficeIconShape.Line(9f, 8f, 9f, 20f),
            OfficeIconShape.Line(15f, 8f, 15f, 20f)
        ],

        OfficeIcon.CellStyles =>
        [
            OfficeIconShape.Rectangle(3f, 4f, 11f, 7f, 1f),
            OfficeIconShape.Rectangle(10f, 13f, 11f, 7f, 1f),
            OfficeIconShape.Line(13f, 16.5f, 18f, 16.5f)
        ],

        OfficeIcon.ChartColumn =>
        [
            OfficeIconShape.Line(3f, 20f, 21f, 20f),
            OfficeIconShape.Rectangle(5f, 12f, 3.5f, 8f),
            OfficeIconShape.Rectangle(10.25f, 5f, 3.5f, 15f),
            OfficeIconShape.Rectangle(15.5f, 9f, 3.5f, 11f)
        ],

        OfficeIcon.ChartBar =>
        [
            OfficeIconShape.Line(4f, 3f, 4f, 21f),
            OfficeIconShape.Rectangle(4f, 5f, 9f, 3.5f),
            OfficeIconShape.Rectangle(4f, 10.25f, 15f, 3.5f),
            OfficeIconShape.Rectangle(4f, 15.5f, 12f, 3.5f)
        ],

        OfficeIcon.ChartLine =>
        [
            OfficeIconShape.Polyline(3f, 4f, 3f, 20f, 21f, 20f),
            OfficeIconShape.Polyline(5f, 16f, 9.5f, 10f, 13.5f, 13f, 19.5f, 6f)
        ],

        OfficeIcon.ChartPie =>
        [
            OfficeIconShape.Circle(12f, 12f, 8.5f),
            OfficeIconShape.Line(12f, 12f, 12f, 3.5f),
            OfficeIconShape.Line(12f, 12f, 19.5f, 15.5f)
        ],

        OfficeIcon.ChartArea =>
        [
            OfficeIconShape.Polyline(3f, 4f, 3f, 20f, 21f, 20f),
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(4.5f, 19f),
                OfficeIconVertex.LineTo(4.5f, 14f),
                OfficeIconVertex.LineTo(9f, 9f),
                OfficeIconVertex.LineTo(13.5f, 13f),
                OfficeIconVertex.LineTo(19.5f, 6.5f),
                OfficeIconVertex.LineTo(19.5f, 19f),
                OfficeIconVertex.Close)
        ],

        OfficeIcon.NewNote => [Note(), OfficeIconShape.Line(9f, 12.5f, 15f, 12.5f), OfficeIconShape.Line(12f, 9.5f, 12f, 15.5f)],
        OfficeIcon.DeleteNote => [Note(), OfficeIconShape.Line(9.5f, 10f, 14.5f, 15f), OfficeIconShape.Line(14.5f, 10f, 9.5f, 15f)],
        OfficeIcon.ShowNotes =>
        [
            OfficeIconShape.Rectangle(3f, 3f, 12f, 12f, 1f),
            OfficeIconShape.Rectangle(9f, 9f, 12f, 12f, 1f),
            OfficeIconShape.Line(12f, 13.5f, 18f, 13.5f),
            OfficeIconShape.Line(12f, 16.5f, 16f, 16.5f)
        ],

        OfficeIcon.Hyperlink =>
        [
            OfficeIconShape.Rectangle(2.5f, 9f, 11f, 6f, 3f),
            OfficeIconShape.Rectangle(10.5f, 9f, 11f, 6f, 3f)
        ],

        OfficeIcon.Function =>
        [
            OfficeIconShape.Path(
                OfficeIconVertex.MoveTo(14f, 4.5f),
                OfficeIconVertex.CurveTo(11f, 3.5f, 10f, 5f, 9.5f, 8f),
                OfficeIconVertex.LineTo(8f, 19.5f)),
            OfficeIconShape.Line(6.5f, 9f, 12.5f, 9f),
            OfficeIconShape.Line(13.5f, 12f, 19.5f, 19f),
            OfficeIconShape.Line(19.5f, 12f, 13.5f, 19f)
        ],

        OfficeIcon.NameManager =>
        [
            Tag(3f, 8f, 20f, 16f),
            OfficeIconShape.Circle(7f, 12f, 1.2f)
        ],

        OfficeIcon.DefineName =>
        [
            Tag(3f, 5f, 16f, 13f),
            OfficeIconShape.Line(14.5f, 19f, 20.5f, 19f),
            OfficeIconShape.Line(17.5f, 16f, 17.5f, 22f)
        ],

        OfficeIcon.Calculate =>
        [
            OfficeIconShape.Rectangle(5f, 3f, 14f, 18f, 2f),
            OfficeIconShape.Rectangle(8f, 6f, 8f, 4f),
            OfficeIconShape.Circle(9f, 14f, 0.6f),
            OfficeIconShape.Circle(12f, 14f, 0.6f),
            OfficeIconShape.Circle(15f, 14f, 0.6f),
            OfficeIconShape.Circle(9f, 17.5f, 0.6f),
            OfficeIconShape.Circle(12f, 17.5f, 0.6f),
            OfficeIconShape.Circle(15f, 17.5f, 0.6f)
        ],

        OfficeIcon.Gridlines =>
        [
            OfficeIconShape.Rectangle(3f, 3f, 18f, 18f),
            OfficeIconShape.Line(9f, 3f, 9f, 21f),
            OfficeIconShape.Line(15f, 3f, 15f, 21f),
            OfficeIconShape.Line(3f, 9f, 21f, 9f),
            OfficeIconShape.Line(3f, 15f, 21f, 15f)
        ],

        OfficeIcon.Headings =>
        [
            OfficeIconShape.Rectangle(3f, 3f, 18f, 18f),
            OfficeIconShape.Rectangle(3f, 3f, 18f, 4.5f),
            OfficeIconShape.Rectangle(3f, 3f, 4.5f, 18f)
        ],

        OfficeIcon.FormulaBar =>
        [
            OfficeIconShape.Rectangle(2.5f, 7f, 19f, 10f, 1.5f),
            OfficeIconShape.Line(9f, 7f, 9f, 17f),
            OfficeIconShape.Line(11.5f, 12f, 19f, 12f),
            OfficeIconShape.Line(4.5f, 12f, 7f, 12f)
        ],

        OfficeIcon.ShowFormulas =>
        [
            OfficeIconShape.Rectangle(3f, 5f, 18f, 14f, 1.5f),
            OfficeIconShape.Line(8f, 10f, 16f, 10f),
            OfficeIconShape.Line(8f, 14f, 16f, 14f)
        ],

        OfficeIcon.DataValidation =>
        [
            OfficeIconShape.Polyline(3.5f, 11.5f, 7.5f, 15.5f, 14.5f, 6.5f),
            OfficeIconShape.Line(15.5f, 14f, 20.5f, 19f),
            OfficeIconShape.Line(20.5f, 14f, 15.5f, 19f)
        ],

        OfficeIcon.FillDown =>
        [
            OfficeIconShape.Line(12f, 3f, 12f, 16.5f),
            OfficeIconShape.Polyline(7.5f, 12f, 12f, 16.5f, 16.5f, 12f),
            OfficeIconShape.Line(5f, 20.5f, 19f, 20.5f)
        ],

        OfficeIcon.FormatCells =>
        [
            OfficeIconShape.Rectangle(3f, 4f, 18f, 16f, 1.5f),
            OfficeIconShape.Line(3f, 9f, 21f, 9f),
            OfficeIconShape.Line(9f, 9f, 9f, 20f),
            OfficeIconShape.Rectangle(12f, 12f, 6f, 5f)
        ],

        OfficeIcon.Zoom100 =>
        [
            OfficeIconShape.Circle(10f, 10f, 6.5f),
            OfficeIconShape.Line(14.8f, 14.8f, 20.5f, 20.5f),
            OfficeIconShape.Line(10f, 7f, 10f, 13f)
        ],

        OfficeIcon.GoTo =>
        [
            OfficeIconShape.Rectangle(10f, 4f, 10.5f, 16f, 1.5f),
            OfficeIconShape.Line(3f, 12f, 15f, 12f),
            OfficeIconShape.Polyline(11.5f, 8.5f, 15f, 12f, 11.5f, 15.5f)
        ],

        OfficeIcon.RowHeight =>
        [
            OfficeIconShape.Line(3f, 4.5f, 21f, 4.5f),
            OfficeIconShape.Line(3f, 19.5f, 21f, 19.5f),
            OfficeIconShape.Line(12f, 7.5f, 12f, 16.5f),
            OfficeIconShape.Polyline(9.5f, 10f, 12f, 7.5f, 14.5f, 10f),
            OfficeIconShape.Polyline(9.5f, 14f, 12f, 16.5f, 14.5f, 14f)
        ],

        OfficeIcon.CustomSort =>
        [
            OfficeIconShape.Line(4f, 6f, 16f, 6f),
            OfficeIconShape.Line(4f, 11f, 12f, 11f),
            OfficeIconShape.Line(4f, 16f, 8f, 16f),
            OfficeIconShape.Circle(17f, 16f, 3.5f),
            OfficeIconShape.Line(17f, 14.5f, 17f, 16f),
            OfficeIconShape.Line(17f, 16f, 18.5f, 17f)
        ],

        _ => WordIcons.Shapes(icon) ?? []
    };

    /// <summary>A dotted cell border — Excel's own way of drawing "no line here" in its border marks.</summary>
    static OfficeIconShape[] Dots(bool top, bool bottom, bool left, bool right, bool inside)
    {
        var dots = new List<OfficeIconShape>();
        float[] steps = [4f, 8f, 12f, 16f, 20f];

        foreach (var s in steps)
        {
            if (top) dots.Add(OfficeIconShape.Circle(s, 4f, 0.5f));
            if (bottom) dots.Add(OfficeIconShape.Circle(s, 20f, 0.5f));
            if (left && s is > 4f and < 20f) dots.Add(OfficeIconShape.Circle(4f, s, 0.5f));
            if (right && s is > 4f and < 20f) dots.Add(OfficeIconShape.Circle(20f, s, 0.5f));
            if (inside && s is > 4f and < 20f)
            {
                dots.Add(OfficeIconShape.Circle(12f, s, 0.5f));
                if (s != 12f)
                    dots.Add(OfficeIconShape.Circle(s, 12f, 0.5f));
            }
        }

        return dots.ToArray();
    }

    /// <summary>The down arrow the two sort marks share, beside their bars.</summary>
    static OfficeIconShape[] SortArrow() =>
    [
        OfficeIconShape.Line(6f, 4f, 6f, 19.5f),
        OfficeIconShape.Polyline(3.5f, 17f, 6f, 19.5f, 8.5f, 17f)
    ];

    /// <summary>Four bars of the given lengths, stacked down the right of a sort mark.</summary>
    static OfficeIconShape[] Bars(float a, float b, float c, float d) =>
    [
        OfficeIconShape.Line(11f, 5f, 11f + a, 5f),
        OfficeIconShape.Line(11f, 9.5f, 11f + b, 9.5f),
        OfficeIconShape.Line(11f, 14f, 11f + c, 14f),
        OfficeIconShape.Line(11f, 18.5f, 11f + d, 18.5f)
    ];

    static OfficeIconShape Funnel(float left, float top, float right, float bottom)
    {
        var mid = (left + right) / 2;
        var neck = (right - left) / 8;
        var waist = top + (bottom - top) * 0.45f;

        return OfficeIconShape.Path(
            OfficeIconVertex.MoveTo(left, top),
            OfficeIconVertex.LineTo(right, top),
            OfficeIconVertex.LineTo(mid + neck, waist),
            OfficeIconVertex.LineTo(mid + neck, bottom),
            OfficeIconVertex.LineTo(mid - neck, bottom - 2f),
            OfficeIconVertex.LineTo(mid - neck, waist),
            OfficeIconVertex.Close);
    }

    /// <summary>A note: a sheet with its corner turned down.</summary>
    static OfficeIconShape Note()
        => OfficeIconShape.Path(
            OfficeIconVertex.MoveTo(4f, 4f),
            OfficeIconVertex.LineTo(15.5f, 4f),
            OfficeIconVertex.LineTo(20f, 8.5f),
            OfficeIconVertex.LineTo(20f, 20f),
            OfficeIconVertex.LineTo(4f, 20f),
            OfficeIconVertex.Close);

    /// <summary>A label tag, point to the right.</summary>
    static OfficeIconShape Tag(float left, float top, float right, float bottom)
    {
        var point = right - (bottom - top) / 2;
        return OfficeIconShape.Path(
            OfficeIconVertex.MoveTo(left, top),
            OfficeIconVertex.LineTo(point, top),
            OfficeIconVertex.LineTo(right, (top + bottom) / 2),
            OfficeIconVertex.LineTo(point, bottom),
            OfficeIconVertex.LineTo(left, bottom),
            OfficeIconVertex.Close);
    }
}
