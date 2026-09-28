using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Spreadsheet.Calc;
using Shiny.Controls.Office.Spreadsheet.Commands;

namespace Shiny.Controls.Office.Spreadsheet;

/// <summary>One level of a sort: which column, and which way.</summary>
/// <param name="Column">The sheet's column index — absolute, not an offset into the range.</param>
/// <param name="Descending">Z to A, largest to smallest.</param>
public sealed record SortKey(int Column, bool Descending = false);

/// <summary>
/// Sorting rows, and the two guesses Excel makes before it does: which block of cells the user meant,
/// and whether its first row is a header.
/// </summary>
public static class SheetSort
{
    /// <summary>How far the current-region walk will grow before giving up — a guard, not a rule.</summary>
    const int MaxRegionCells = 2_000_000;

    /// <summary>
    /// The block of data around <paramref name="cell"/>: grown in every direction until it is ringed by
    /// blank rows and columns. Excel's <c>CurrentRegion</c>, which is what a sort, a filter or a table
    /// made from a single selected cell operates on.
    /// </summary>
    public static CellRange CurrentRegion(Worksheet sheet, CellRef cell)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (sheet.UsedRange is not { } used)
            return new CellRange(cell);

        var left = cell.Column;
        var right = cell.Column;
        var top = cell.Row;
        var bottom = cell.Row;

        bool Filled(int column, int row)
            => column >= 0 && row >= 0 && column <= CellRef.MaxColumn && row <= CellRef.MaxRow
               && !sheet.GetValue(new CellRef(column, row)).IsBlank;

        bool AnyInRow(int row, int from, int to)
        {
            if (row < used.Top || row > used.Bottom)
                return false;

            for (var column = Math.Max(from, used.Left); column <= Math.Min(to, used.Right); column++)
            {
                if (Filled(column, row))
                    return true;
            }

            return false;
        }

        bool AnyInColumn(int column, int from, int to)
        {
            if (column < used.Left || column > used.Right)
                return false;

            for (var row = Math.Max(from, used.Top); row <= Math.Min(to, used.Bottom); row++)
            {
                if (Filled(column, row))
                    return true;
            }

            return false;
        }

        var grew = true;
        while (grew && (long)(right - left + 1) * (bottom - top + 1) < MaxRegionCells)
        {
            grew = false;

            // Diagonal neighbours count, which is why each probe spans one past the current edges.
            if (top > 0 && AnyInRow(top - 1, left - 1, right + 1))
            {
                top--;
                grew = true;
            }

            if (bottom < CellRef.MaxRow && AnyInRow(bottom + 1, left - 1, right + 1))
            {
                bottom++;
                grew = true;
            }

            if (left > 0 && AnyInColumn(left - 1, top - 1, bottom + 1))
            {
                left--;
                grew = true;
            }

            if (right < CellRef.MaxColumn && AnyInColumn(right + 1, top - 1, bottom + 1))
            {
                right++;
                grew = true;
            }
        }

        return new CellRange(new CellRef(Math.Max(0, left), Math.Max(0, top)), new CellRef(right, bottom));
    }

    /// <summary>
    /// Whether the first row of <paramref name="range"/> reads as a header.
    /// </summary>
    /// <remarks>
    /// Excel's own heuristic, near enough: a header row is all text, with no blanks, over data that is
    /// not all text in the same way — a column of numbers under a word, or a bold row over plain ones.
    /// A block of names over names has no way to tell, and Excel guesses "no header" there as well.
    /// </remarks>
    public static bool DetectHeader(Worksheet sheet, CellRange range)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (range.RowCount < 2)
            return false;

        var styles = sheet.Workbook.Styles;
        var allText = true;
        var boldHeader = true;

        for (var column = range.Left; column <= range.Right; column++)
        {
            var cell = new CellRef(column, range.Top);
            var value = sheet.GetDisplayValue(cell);
            if (value.Kind != CellValueKind.Text)
                allText = false;

            if (!styles.Resolve(sheet.GetEffectiveStyleIndex(cell)).Bold)
                boldHeader = false;
        }

        if (!allText)
            return false;

        var bodyBold = true;
        for (var column = range.Left; column <= range.Right; column++)
        {
            var below = new CellRef(column, range.Top + 1);
            var value = sheet.GetDisplayValue(below);

            if (value.Kind is CellValueKind.Number or CellValueKind.Boolean)
                return true;

            if (!styles.Resolve(sheet.GetEffectiveStyleIndex(below)).Bold)
                bodyBold = false;
        }

        return boldHeader && !bodyBold;
    }

    /// <summary>
    /// The command that sorts the rows of <paramref name="range"/> by <paramref name="keys"/>, as one
    /// undo step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rows move whole — every column of the range, formatting included — and a formula moving with its
    /// row is rebased the way a copy would be, so <c>=B5*2</c> that lands in row 2 reads <c>=B2*2</c>.
    /// That is what Excel does, and it is what keeps a row-wise calculation attached to its row.
    /// </para>
    /// <para>
    /// The sort is stable, and blanks go last whichever way it runs. Ascending order is Excel's: numbers,
    /// then text case-insensitively, then logicals, then errors.
    /// </para>
    /// </remarks>
    public static IEditCommand<Workbook> Build(Worksheet sheet, CellRange range, IReadOnlyList<SortKey> keys, bool hasHeader)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(keys);

        range = Clip(sheet, range);
        var data = hasHeader && range.RowCount > 1
            ? new CellRange(new CellRef(range.Left, range.Top + 1), range.BottomRight)
            : range;

        if (data.RowCount < 2 || keys.Count == 0)
            return new CompositeCommand<Workbook>("Sort", []);

        var snapshot = SpreadsheetClipboardContent.Capture(sheet, data, SpreadsheetClipboardOperation.Copy);

        var rows = Enumerable.Range(0, data.RowCount).ToList();
        var sortValues = keys
            .Select(key => rows.Select(row => sheet.GetDisplayValue(new CellRef(key.Column, data.Top + row))).ToArray())
            .ToArray();

        var order = rows
            .OrderBy(x => x, Comparer<int>.Create((a, b) =>
            {
                for (var k = 0; k < keys.Count; k++)
                {
                    var result = CompareForSort(sortValues[k][a], sortValues[k][b], keys[k].Descending);
                    if (result != 0)
                        return result;
                }

                return a.CompareTo(b);
            }))
            .ToList();

        // order[newRow] = oldRow. Invert it so each captured cell knows where it is going.
        var destination = new int[order.Count];
        for (var newRow = 0; newRow < order.Count; newRow++)
            destination[order[newRow]] = newRow;

        var moved = snapshot.Cells
            .Select(cell =>
            {
                var target = destination[cell.Row];
                var formula = cell.Formula is { Length: > 0 } text
                    ? FormulaReferenceShifter.Translate(text, 0, target - cell.Row)
                    : cell.Formula;

                return cell with { Row = target, Formula = formula };
            })
            .ToList();

        var sorted = snapshot with { Cells = moved };
        return new CompositeCommand<Workbook>("Sort", [new PasteClipboardCommand(sorted, sheet.Name, data.TopLeft)]);
    }

    /// <summary>A whole-column or whole-row selection means "the data in it".</summary>
    static CellRange Clip(Worksheet sheet, CellRange range)
    {
        if (range.CellCount <= 250_000 || sheet.UsedRange is not { } used || !used.Intersects(range))
            return range;

        return new CellRange(
            new CellRef(Math.Max(range.Left, used.Left), Math.Max(range.Top, used.Top)),
            new CellRef(Math.Min(range.Right, used.Right), Math.Min(range.Bottom, used.Bottom)));
    }

    /// <summary>Excel's sort order. Blanks are last in both directions, which is why this takes the direction.</summary>
    public static int CompareForSort(CellValue a, CellValue b, bool descending)
    {
        if (a.IsBlank || b.IsBlank)
            return a.IsBlank == b.IsBlank ? 0 : a.IsBlank ? 1 : -1;

        var rankA = Rank(a);
        var rankB = Rank(b);
        int result;

        if (rankA != rankB)
        {
            result = rankA.CompareTo(rankB);
        }
        else
        {
            result = a.Kind switch
            {
                CellValueKind.Number => a.AsNumber().CompareTo(b.AsNumber()),
                CellValueKind.Text => string.Compare(a.AsText(), b.AsText(), StringComparison.CurrentCultureIgnoreCase),
                CellValueKind.Boolean => a.AsBoolean().CompareTo(b.AsBoolean()),
                _ => 0
            };
        }

        return descending ? -result : result;

        static int Rank(CellValue value) => value.Kind switch
        {
            CellValueKind.Number => 0,
            CellValueKind.Text => 1,
            CellValueKind.Boolean => 2,
            _ => 3
        };
    }
}
