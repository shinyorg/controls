using System.Xml.Linq;
using DocumentFormat.OpenXml.Drawing.Charts;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Shiny.Controls.Office.Spreadsheet.Calc;

namespace Shiny.Controls.Office.Spreadsheet;

/// <summary>
/// Carries the sheet's own ranges along with an inserted or deleted band: conditional formatting,
/// validation, links, the filter, notes, tables and chart anchors.
/// </summary>
/// <remarks>
/// Each of these names cells by address, and each goes quietly wrong when a row is pushed in above it:
/// the highlight lands on the row below the one it was for, the dropdown moves off its column, the note
/// hangs on a neighbour. Formulas and merges were already moved; this is the rest of the sheet.
/// </remarks>
public sealed partial class Worksheet
{
    /// <summary>The local names whose XML a delete snapshots, so undo can restore them exactly.</summary>
    internal static readonly string[] BandSensitiveElements = ["autoFilter", "mergeCells", "conditionalFormatting", "dataValidations", "hyperlinks"];

    /// <summary>
    /// A range moved by a band edit, or null when the band swallowed it whole.
    /// </summary>
    /// <remarks>
    /// Straddling ranges grow on an insert and shrink on a delete. The two edges collapse differently
    /// into a deleted band — the first onto the row that now follows it, the last onto the one before —
    /// which is what keeps a range that loses its middle from also losing a row at its end.
    /// </remarks>
    internal static CellRange? ShiftRange(CellRange range, int at, int delta, bool rows)
    {
        var first = rows ? range.Top : range.Left;
        var last = rows ? range.Bottom : range.Right;
        var limit = rows ? CellRef.MaxRow : CellRef.MaxColumn;

        int? newFirst, newLast;

        if (delta > 0)
        {
            newFirst = first >= at ? first + delta : first;
            newLast = last >= at ? last + delta : last;

            if (newFirst > limit)
                return null;

            newLast = Math.Min(limit, newLast.Value);
        }
        else
        {
            var end = at - delta; // one past the deleted band

            if (first >= at && last < end)
                return null;

            newFirst = first < at ? first : first < end ? at : first + delta;
            newLast = last < at ? last : last < end ? at - 1 : last + delta;

            if (newLast < newFirst)
                return null;
        }

        return rows
            ? new CellRange(new CellRef(range.Left, newFirst.Value), new CellRef(range.Right, newLast.Value))
            : new CellRange(new CellRef(newFirst.Value, range.Top), new CellRef(newLast.Value, range.Bottom));
    }

    /// <summary>A single cell moved by a band edit, or null when it was deleted.</summary>
    internal static CellRef? ShiftCell(CellRef cell, int at, int delta, bool rows)
        => ShiftRange(new CellRange(cell), at, delta, rows)?.TopLeft;

    string ShiftFormula(string formula, int at, int delta, bool rows) => (rows, delta > 0) switch
    {
        (true, true) => FormulaReferenceShifter.ForInsertedRows(formula, this.Name, this.Name, at, delta),
        (true, false) => FormulaReferenceShifter.ForDeletedRows(formula, this.Name, this.Name, at, -delta),
        (false, true) => FormulaReferenceShifter.ForInsertedColumns(formula, this.Name, this.Name, at, delta),
        _ => FormulaReferenceShifter.ForDeletedColumns(formula, this.Name, this.Name, at, -delta)
    };

    string? ShiftSqref(string? sqref, int at, int delta, bool rows)
    {
        var ranges = ConditionalFormatting.ParseSqref(sqref)
            .Select(x => ShiftRange(x, at, delta, rows))
            .OfType<CellRange>()
            .ToList();

        return ranges.Count == 0 ? null : string.Join(' ', ranges.Select(x => x.ToString()));
    }

    /// <summary>Moves everything on the sheet that names cells by address, after the cells themselves moved.</summary>
    internal void ShiftSheetRanges(int at, int delta, bool rows)
    {
        this.ShiftListElements("conditionalFormatting", at, delta, rows);
        this.ShiftValidations(at, delta, rows);
        this.ShiftRefChildren("hyperlinks", "hyperlink", at, delta, rows);
        this.ShiftAutoFilter(at, delta, rows);
        this.ShiftNotes(at, delta, rows);
        this.ShiftTables(at, delta, rows);
        this.ShiftChartAnchors(at, delta, rows);
    }

    /// <summary>Blocks carrying their own <c>sqref</c> — each conditionalFormatting — and the formulas in them.</summary>
    void ShiftListElements(string localName, int at, int delta, bool rows)
    {
        var fragments = this.ReadElements(localName);
        if (fragments.Count == 0)
            return;

        var result = new List<string>();
        var changed = false;

        foreach (var fragment in fragments)
        {
            var element = XElement.Parse(fragment);
            var sqref = element.Attribute("sqref")?.Value;
            var shifted = this.ShiftSqref(sqref, at, delta, rows);

            if (shifted is null)
            {
                changed = true;
                continue;
            }

            if (shifted != sqref)
            {
                element.SetAttributeValue("sqref", shifted);
                changed = true;
            }

            foreach (var formula in element.Descendants().Where(x => x.Name.LocalName == "formula"))
            {
                var text = this.ShiftFormula(formula.Value, at, delta, rows);
                if (text != formula.Value)
                {
                    formula.Value = text;
                    changed = true;
                }
            }

            result.Add(element.ToString(SaveOptions.DisableFormatting));
        }

        if (changed)
            SheetXml.Replace(this.SheetElement(), localName, result);
    }

    void ShiftValidations(int at, int delta, bool rows)
    {
        var fragment = this.ReadElements("dataValidations").FirstOrDefault();
        if (fragment is null)
            return;

        var root = XElement.Parse(fragment);
        var changed = false;

        foreach (var rule in root.Elements().Where(x => x.Name.LocalName == "dataValidation").ToList())
        {
            var sqref = rule.Attribute("sqref")?.Value;
            var shifted = this.ShiftSqref(sqref, at, delta, rows);

            if (shifted is null)
            {
                rule.Remove();
                changed = true;
                continue;
            }

            if (shifted != sqref)
            {
                rule.SetAttributeValue("sqref", shifted);
                changed = true;
            }

            foreach (var formula in rule.Elements().Where(x => x.Name.LocalName is "formula1" or "formula2"))
            {
                // A quoted list is not a reference, and the shifter already leaves string literals alone.
                var text = this.ShiftFormula(formula.Value, at, delta, rows);
                if (text != formula.Value)
                {
                    formula.Value = text;
                    changed = true;
                }
            }
        }

        if (!changed)
            return;

        var remaining = root.Elements().Count(x => x.Name.LocalName == "dataValidation");
        root.SetAttributeValue("count", remaining);
        SheetXml.Replace(this.SheetElement(), "dataValidations", remaining == 0 ? [] : [root.ToString(SaveOptions.DisableFormatting)]);
    }

    /// <summary>Children with a <c>ref</c> — each hyperlink. A child whose cells were deleted is dropped.</summary>
    void ShiftRefChildren(string localName, string childName, int at, int delta, bool rows)
    {
        var fragment = this.ReadElements(localName).FirstOrDefault();
        if (fragment is null)
            return;

        var root = XElement.Parse(fragment);
        var changed = false;

        foreach (var child in root.Elements().Where(x => x.Name.LocalName == childName).ToList())
        {
            if (child.Attribute("ref")?.Value is not { } reference || !CellRange.TryParse(reference, out var range))
                continue;

            // Relationship ids are left in place when a link goes: an orphaned relationship is legal, and
            // an undo that restores the element needs the one it names to still be there.
            if (ShiftRange(range, at, delta, rows) is not { } moved)
            {
                child.Remove();
                changed = true;
            }
            else if (moved != range)
            {
                child.SetAttributeValue("ref", moved.ToString());
                changed = true;
            }
        }

        if (!changed)
            return;

        SheetXml.Replace(this.SheetElement(), localName, root.Elements().Any() ? [root.ToString(SaveOptions.DisableFormatting)] : []);
    }

    void ShiftAutoFilter(int at, int delta, bool rows)
    {
        var fragment = this.ReadElements("autoFilter").FirstOrDefault();
        if (fragment is null)
            return;

        var element = XElement.Parse(fragment);
        if (element.Attribute("ref")?.Value is not { } reference || !CellRange.TryParse(reference, out var range))
            return;

        var moved = ShiftRange(range, at, delta, rows);
        if (moved == range)
            return;

        if (moved is null)
        {
            SheetXml.Replace(this.SheetElement(), "autoFilter", []);
            return;
        }

        element.SetAttributeValue("ref", moved.Value.ToString());

        // A column edit inside the filter moves its column ids; criteria for a column that is gone go too.
        if (!rows)
        {
            foreach (var column in element.Elements().Where(x => x.Name.LocalName == "filterColumn").ToList())
            {
                if (!int.TryParse(column.Attribute("colId")?.Value, out var id))
                    continue;

                var absolute = range.Left + id;
                if (ShiftCell(new CellRef(absolute, range.Top), at, delta, rows: false) is not { } cell)
                {
                    column.Remove();
                    continue;
                }

                column.SetAttributeValue("colId", cell.Column - moved.Value.Left);
            }
        }

        SheetXml.Replace(this.SheetElement(), "autoFilter", [element.ToString(SaveOptions.DisableFormatting)]);
    }

    void ShiftNotes(int at, int delta, bool rows)
    {
        var notes = this.Notes;
        if (notes.Count == 0)
            return;

        var moved = notes
            .Select(x => ShiftCell(x.Cell, at, delta, rows) is { } cell ? x with { Cell = cell } : null)
            .OfType<CellNote>()
            .ToList();

        if (moved.SequenceEqual(notes))
            return;

        this.WriteNotes(moved);
    }

    void ShiftTables(int at, int delta, bool rows)
    {
        foreach (var tablePart in this.part.TableDefinitionParts.ToList())
        {
            if (tablePart.Table is not { } table || table.Reference?.Value is not { } reference || !CellRange.TryParse(reference, out var range))
                continue;

            var moved = ShiftRange(range, at, delta, rows);
            if (moved == range)
                continue;

            if (moved is null || (rows && moved.Value.RowCount < 2))
            {
                // Nothing left of it but, at most, its header: the table goes, as it does in Excel.
                this.RemoveTablePart(table.DisplayName?.Value ?? table.Name?.Value ?? string.Empty);
                continue;
            }

            if (!rows)
                this.ResizeTableColumns(table, range, moved.Value, at, delta);

            table.Reference = moved.Value.ToString();

            if (table.GetFirstChild<AutoFilter>() is { } filter)
                table.ReplaceChild(new AutoFilter { Reference = moved.Value.ToString() }, filter);
        }
    }

    /// <summary>
    /// Adds or removes table columns for a column edit inside a table — each needs a unique name, and its
    /// header cell has to say it.
    /// </summary>
    void ResizeTableColumns(Table table, CellRange before, CellRange after, int at, int delta)
    {
        var columns = table.TableColumns;
        if (columns is null)
            return;

        var list = columns.Elements<TableColumn>().ToList();

        if (delta < 0)
        {
            var end = at - delta;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var absolute = before.Left + i;
                if (absolute >= at && absolute < end)
                    list[i].Remove();
            }
        }
        else if (at > before.Left && at <= before.Right)
        {
            var names = new HashSet<string>(list.Select(x => x.Name?.Value ?? string.Empty), StringComparer.OrdinalIgnoreCase);
            var nextId = list.Select(x => x.Id?.Value ?? 0u).DefaultIfEmpty(0u).Max() + 1;
            var anchor = list[at - before.Left - 1];

            for (var i = 0; i < delta; i++)
            {
                var n = 1;
                string name;
                do
                {
                    name = $"Column{n++}";
                }
                while (!names.Add(name));

                var created = new TableColumn { Id = nextId++, Name = name };
                columns.InsertAfter(created, anchor);
                anchor = created;

                // The header cell is the column's name; Excel refuses a table where they disagree.
                var cell = this.editor.GetOrCreateCell(new CellRef(at + i, after.Top));
                cell.DataType = CellValues.SharedString;
                cell.CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(
                    this.workbook.SharedStrings.GetOrAdd(name).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        columns.Count = (uint)columns.Elements<TableColumn>().Count();
    }

    void ShiftChartAnchors(int at, int delta, bool rows)
    {
        foreach (var chart in this.Charts)
        {
            var anchor = chart.Anchor;
            var from = ShiftCell(anchor.From, at, delta, rows) ?? (rows ? anchor.From with { Row = at } : anchor.From with { Column = at });
            var to = ShiftCell(anchor.To, at, delta, rows) ?? (rows ? anchor.To with { Row = Math.Max(from.Row, at) } : anchor.To with { Column = Math.Max(from.Column, at) });

            if (from != anchor.From || to != anchor.To)
                this.MoveChart(chart.Id, anchor with { From = from, To = to });
        }
    }

    /// <summary>
    /// Rewrites the reference formulas in every chart on this sheet — the series, categories and names —
    /// leaving charts whose text comes back unchanged untouched.
    /// </summary>
    /// <remarks>Workbook-wide, like cell formulas: a chart here may plot another sheet's rows.</remarks>
    internal void RewriteChartFormulas(Func<string, string> rewrite)
    {
        var drawing = this.part.DrawingsPart;
        if (drawing is null)
            return;

        foreach (var chartPart in drawing.ChartParts)
        {
            if (chartPart.ChartSpace?.OuterXml is not { } xml)
                continue;

            var root = XElement.Parse(xml);
            var changed = false;

            foreach (var f in root.Descendants().Where(x => x.Name.LocalName == "f"))
            {
                var text = rewrite(f.Value);
                if (text != f.Value)
                {
                    f.Value = text;
                    changed = true;
                }
            }

            if (changed)
                chartPart.ChartSpace = new ChartSpace(root.ToString(SaveOptions.DisableFormatting));
        }
    }
}
