using Shiny.Controls.Office.Editing;

namespace Shiny.Controls.Office.Spreadsheet.Commands;

/// <summary>Hides or shows rows, restoring each one's own previous state on undo.</summary>
public sealed class SetRowsHiddenCommand(string sheetName, IReadOnlyDictionary<int, bool> rows, string? name = null)
    : IEditCommand<Workbook>
{
    public string SheetName { get; } = sheetName;

    /// <summary>Row index to the hidden state it should end up in.</summary>
    public IReadOnlyDictionary<int, bool> Rows { get; } = rows;

    public string Name { get; } = name ?? "Hide Rows";

    public static SetRowsHiddenCommand Range(string sheetName, int first, int last, bool hidden)
    {
        var rows = new Dictionary<int, bool>();
        for (var row = first; row <= last; row++)
            rows[row] = hidden;

        return new SetRowsHiddenCommand(sheetName, rows, hidden ? "Hide Rows" : "Unhide Rows");
    }

    public IEditCommand<Workbook> Apply(Workbook context)
    {
        var sheet = context[this.SheetName];
        var previous = new Dictionary<int, bool>(this.Rows.Count);

        foreach (var (row, hidden) in this.Rows)
        {
            var was = sheet.IsRowHidden(row);
            if (was == hidden)
                continue;

            previous[row] = was;
            sheet.WriteRowHidden(row, hidden);
        }

        return new SetRowsHiddenCommand(this.SheetName, previous, this.Name);
    }
}

/// <summary>The border buttons Excel's Borders dropdown offers.</summary>
public enum BorderPreset
{
    Bottom,
    Top,
    Left,
    Right,
    None,
    All,
    Outside,
    ThickOutside,
    ThickBottom,
    DoubleBottom,
    TopAndBottom,
    InsideHorizontal,
    InsideVertical
}

/// <summary>
/// Turns a border preset into the formatting it means for each cell of a range.
/// </summary>
/// <remarks>
/// A border preset is not one format applied to every cell: "outside borders" gives the top row a top
/// edge, the left column a left edge and so on, and the corner cells two edges each. It is expressed as
/// a run of <see cref="FormatRangeCommand"/>s over the range's bands, each folding its own edge into
/// whatever the cell already has — so an outside border drawn over a cell with a bottom edge keeps it.
/// </remarks>
public static class BorderPresets
{
    public static IReadOnlyList<(BorderPreset Preset, string Name)> All { get; } =
    [
        (BorderPreset.Bottom, "Bottom Border"),
        (BorderPreset.Top, "Top Border"),
        (BorderPreset.Left, "Left Border"),
        (BorderPreset.Right, "Right Border"),
        (BorderPreset.None, "No Border"),
        (BorderPreset.All, "All Borders"),
        (BorderPreset.Outside, "Outside Borders"),
        (BorderPreset.ThickOutside, "Thick Outside Borders"),
        (BorderPreset.ThickBottom, "Thick Bottom Border"),
        (BorderPreset.DoubleBottom, "Bottom Double Border"),
        (BorderPreset.TopAndBottom, "Top and Bottom Border"),
        (BorderPreset.InsideHorizontal, "Inside Horizontal Borders"),
        (BorderPreset.InsideVertical, "Inside Vertical Borders")
    ];

    public static string NameOf(BorderPreset preset) => All.First(x => x.Preset == preset).Name;

    /// <summary>The command a preset is, over <paramref name="range"/>, drawn in <paramref name="line"/>.</summary>
    /// <param name="line">
    /// The line style and colour chosen in the dropdown. Its style is overridden by the presets that
    /// name one — thick outside, double bottom.
    /// </param>
    public static IEditCommand<Workbook> Build(string sheetName, CellRange range, BorderPreset preset, BorderEdge line)
    {
        var commands = new List<IEditCommand<Workbook>>();
        var edge = line.IsVisible ? line : BorderEdge.Thin(line.Color);
        var thick = edge with { Style = CellBorderStyle.Thick };
        var doubled = edge with { Style = CellBorderStyle.Double };

        CellRange Row(int row) => new(new CellRef(range.Left, row), new CellRef(range.Right, row));
        CellRange Column(int column) => new(new CellRef(column, range.Top), new CellRef(column, range.Bottom));

        void Add(CellRange target, CellFormatChange change)
            => commands.Add(new FormatRangeCommand(sheetName, target, change));

        switch (preset)
        {
            case BorderPreset.Bottom:
                Add(Row(range.Bottom), new CellFormatChange { BorderBottom = edge });
                break;

            case BorderPreset.ThickBottom:
                Add(Row(range.Bottom), new CellFormatChange { BorderBottom = thick });
                break;

            case BorderPreset.DoubleBottom:
                Add(Row(range.Bottom), new CellFormatChange { BorderBottom = doubled });
                break;

            case BorderPreset.Top:
                Add(Row(range.Top), new CellFormatChange { BorderTop = edge });
                break;

            case BorderPreset.TopAndBottom:
                Add(Row(range.Top), new CellFormatChange { BorderTop = edge });
                Add(Row(range.Bottom), new CellFormatChange { BorderBottom = edge });
                break;

            case BorderPreset.Left:
                Add(Column(range.Left), new CellFormatChange { BorderLeft = edge });
                break;

            case BorderPreset.Right:
                Add(Column(range.Right), new CellFormatChange { BorderRight = edge });
                break;

            case BorderPreset.None:
                Add(range, new CellFormatChange
                {
                    BorderLeft = BorderEdge.None,
                    BorderRight = BorderEdge.None,
                    BorderTop = BorderEdge.None,
                    BorderBottom = BorderEdge.None
                });
                break;

            case BorderPreset.All:
                Add(range, new CellFormatChange { BorderLeft = edge, BorderRight = edge, BorderTop = edge, BorderBottom = edge });
                break;

            case BorderPreset.Outside:
            case BorderPreset.ThickOutside:
                var outer = preset == BorderPreset.ThickOutside ? thick : edge;
                Add(Row(range.Top), new CellFormatChange { BorderTop = outer });
                Add(Row(range.Bottom), new CellFormatChange { BorderBottom = outer });
                Add(Column(range.Left), new CellFormatChange { BorderLeft = outer });
                Add(Column(range.Right), new CellFormatChange { BorderRight = outer });
                break;

            case BorderPreset.InsideHorizontal:
                if (range.RowCount > 1)
                    Add(new CellRange(range.TopLeft, new CellRef(range.Right, range.Bottom - 1)), new CellFormatChange { BorderBottom = edge });

                break;

            case BorderPreset.InsideVertical:
                if (range.ColumnCount > 1)
                    Add(new CellRange(range.TopLeft, new CellRef(range.Right - 1, range.Bottom)), new CellFormatChange { BorderRight = edge });

                break;
        }

        return new CompositeCommand<Workbook>(NameOf(preset), commands);
    }
}
