using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Shiny.Controls.Office.Editing;

namespace Shiny.Controls.Office.Document;

/// <summary>A structural change to the table the caret is in.</summary>
public enum TableEdit
{
    InsertRowAbove,
    InsertRowBelow,
    InsertColumnLeft,
    InsertColumnRight,
    DeleteRow,
    DeleteColumn,
    DeleteTable,

    /// <summary>Merges the cells between <see cref="EditTableCommand.At"/> and <see cref="EditTableCommand.To"/>.</summary>
    MergeCells,

    /// <summary>Splits a merged cell back apart, or an ordinary cell into two side by side.</summary>
    SplitCell
}


/// <summary>
/// Changes the structure of a table: rows and columns in and out, cells merged and split.
/// </summary>
/// <remarks>
/// <para>
/// Undone by putting the whole table back as it was. A table's structure is spread across its grid,
/// every row and every cell's <c>w:gridSpan</c> and <c>w:vMerge</c>, and a column insert touches all of
/// them; describing the inverse edit by edit would be a second implementation of every operation here,
/// with its own bugs.
/// </para>
/// <para>
/// Columns are addressed by <em>grid</em> position, not by cell index, because a spanned cell covers
/// several grid columns: inserting a column beside a two-column-wide cell widens the cells it runs
/// through in every other row rather than adding a stray cell to each.
/// </para>
/// </remarks>
public sealed record EditTableCommand(DocumentPosition At, TableEdit Edit, DocumentPosition? To = null) : DocumentCommand
{
    public override string Name => this.Edit switch
    {
        TableEdit.InsertRowAbove or TableEdit.InsertRowBelow => "Insert Row",
        TableEdit.InsertColumnLeft or TableEdit.InsertColumnRight => "Insert Column",
        TableEdit.DeleteRow => "Delete Row",
        TableEdit.DeleteColumn => "Delete Column",
        TableEdit.DeleteTable => "Delete Table",
        TableEdit.MergeCells => "Merge Cells",
        _ => "Split Cell"
    };

    public override IEditCommand<WordDocument> Apply(WordDocument context)
    {
        var paragraph = context.ParagraphElementAt(this.At.Block);
        var cell = paragraph?.Ancestors<TableCell>().FirstOrDefault();
        var row = cell?.Parent as TableRow;
        var table = row?.Parent as Table;

        if (paragraph is null || cell is null || row is null || table is null)
            return new NoOpCommand();

        var top = context.TopOf(this.At.Block);
        var restore = context.CaptureBlocks(top, 1);

        // Nested tables are edited in place; only a body-level table can be removed as a block.
        var isTopLevel = ReferenceEquals(context.BlockElementAt(top), table);

        var removed = this.Edit switch
        {
            TableEdit.InsertRowAbove => InsertRow(table, row, above: true),
            TableEdit.InsertRowBelow => InsertRow(table, row, above: false),
            TableEdit.InsertColumnLeft => InsertColumn(table, GridStart(row, cell)),
            TableEdit.InsertColumnRight => InsertColumn(table, GridStart(row, cell) + Span(cell)),
            TableEdit.DeleteRow => DeleteRow(table, row),
            TableEdit.DeleteColumn => DeleteColumn(table, GridStart(row, cell)),
            TableEdit.DeleteTable => true,
            TableEdit.MergeCells => this.Merge(context, table, row, cell),
            TableEdit.SplitCell => SplitCell(table, row, cell),
            _ => false
        };

        if (removed)
        {
            if (!isTopLevel)
            {
                // A nested table cannot vanish as a block; leave the cell it sat in with a paragraph.
                var host = table.Parent;
                table.Remove();
                if (host is TableCell hostCell && !hostCell.Elements<Paragraph>().Any())
                    hostCell.AppendChild(new Paragraph());

                context.ReprojectTop(top);
                return restore;
            }

            context.RemoveTopBlock(top);
            return restore with { RemovedCount = 0 };
        }

        context.ReprojectTop(top);
        return restore;
    }

    bool Merge(WordDocument context, Table table, TableRow row, TableCell cell)
    {
        var otherParagraph = this.To is { } to ? context.ParagraphElementAt(to.Block) : null;
        var other = otherParagraph?.Ancestors<TableCell>().FirstOrDefault();
        var otherRow = other?.Parent as TableRow;

        if (other is null || otherRow is null || !ReferenceEquals(otherRow.Parent, table) || ReferenceEquals(other, cell))
            return false;

        var rows = table.Elements<TableRow>().ToList();
        var r1 = rows.IndexOf(row);
        var r2 = rows.IndexOf(otherRow);
        if (r1 > r2)
            (r1, r2) = (r2, r1);

        var g1 = Math.Min(GridStart(row, cell), GridStart(otherRow, other));
        var g2 = Math.Max(GridStart(row, cell) + Span(cell), GridStart(otherRow, other) + Span(other));

        TableCell? anchor = null;

        for (var r = r1; r <= r2; r++)
        {
            var current = rows[r];
            var covered = current.Elements<TableCell>().Where(c =>
            {
                var start = GridStart(current, c);
                return start < g2 && start + Span(c) > g1;
            }).ToList();

            if (covered.Count == 0)
                continue;

            // Horizontal first: one cell per row, spanning the whole width of the region.
            var head = covered[0];
            foreach (var extra in covered.Skip(1))
            {
                MoveContent(extra, head);
                extra.Remove();
            }

            SetSpan(head, g2 - GridStart(current, head));

            if (r1 == r2)
            {
                anchor = head;
                continue;
            }

            // Then vertical: the top row restarts the merge and the rows below continue it, handing
            // their content up so nothing typed in them is lost.
            var properties = head.TableCellProperties ?? head.PrependChild(new TableCellProperties());
            properties.RemoveAllChildren<VerticalMerge>();

            if (r == r1)
            {
                anchor = head;
                InsertOrderedCell(properties, new VerticalMerge { Val = MergedCellValues.Restart });
            }
            else
            {
                if (anchor is not null)
                    MoveContent(head, anchor);

                InsertOrderedCell(properties, new VerticalMerge());
            }
        }

        return false;
    }

    // ---- rows ----

    static bool InsertRow(Table table, TableRow row, bool above)
    {
        var created = (TableRow)row.CloneNode(true);

        // A new row is a body row: it does not repeat as a header, and it starts no vertical merge.
        created.TableRowProperties?.RemoveAllChildren<TableHeader>();

        foreach (var cell in created.Elements<TableCell>())
        {
            cell.TableCellProperties?.RemoveAllChildren<VerticalMerge>();
            Empty(cell);
        }

        if (above)
            row.InsertBeforeSelf(created);
        else
            row.InsertAfterSelf(created);

        return false;
    }

    static bool DeleteRow(Table table, TableRow row)
    {
        // A merge that starts in this row has to be restarted in the row below, or the cells under it
        // become continuations of nothing.
        if (row.NextSibling<TableRow>() is { } next)
        {
            foreach (var cell in row.Elements<TableCell>())
            {
                if (OoxmlUnits.EnumAttribute(cell.TableCellProperties?.VerticalMerge, "val") != "restart")
                    continue;

                var below = CellAtGrid(next, GridStart(row, cell));
                if (below?.TableCellProperties?.VerticalMerge is { } merge)
                    merge.Val = MergedCellValues.Restart;
            }
        }

        row.Remove();
        return !table.Elements<TableRow>().Any();
    }

    // ---- columns ----

    static bool InsertColumn(Table table, int grid)
    {
        var columns = table.GetFirstChild<TableGrid>()?.Elements<GridColumn>().ToList() ?? [];
        var neighbour = columns.Count == 0 ? null : columns[Math.Clamp(grid == columns.Count ? grid - 1 : grid, 0, columns.Count - 1)];

        // The new column takes half of the one it is inserted beside, so the table keeps its width.
        string? width = null;
        if (neighbour?.Width?.Value is { } w && int.TryParse(w, out var twips) && twips > 0)
        {
            var half = Math.Max(1, twips / 2);
            neighbour.Width = (twips - half).ToString(System.Globalization.CultureInfo.InvariantCulture);
            width = half.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (table.GetFirstChild<TableGrid>() is { } tableGrid)
        {
            var column = new GridColumn { Width = width };
            if (grid < columns.Count)
                columns[grid].InsertBeforeSelf(column);
            else
                tableGrid.AppendChild(column);
        }

        foreach (var row in table.Elements<TableRow>())
        {
            var cells = row.Elements<TableCell>().ToList();
            TableCell? before = null;
            TableCell? spanning = null;

            foreach (var cell in cells)
            {
                var start = GridStart(row, cell);
                if (start == grid)
                {
                    before = cell;
                    break;
                }

                if (start < grid && start + Span(cell) > grid)
                {
                    spanning = cell;
                    break;
                }
            }

            if (spanning is not null)
            {
                // Inserted through the middle of a spanned cell: the cell just gets wider.
                SetSpan(spanning, Span(spanning) + 1);
                continue;
            }

            var template = before ?? cells.LastOrDefault();
            var created = NewCellLike(template, width);

            if (before is not null)
                before.InsertBeforeSelf(created);
            else if (cells.LastOrDefault() is { } last)
                last.InsertAfterSelf(created);
            else
                row.AppendChild(created);
        }

        EvenOutPercentageWidths(table);
        return false;
    }

    static bool DeleteColumn(Table table, int grid)
    {
        if (table.GetFirstChild<TableGrid>()?.Elements<GridColumn>().ElementAtOrDefault(grid) is { } column)
            column.Remove();

        foreach (var row in table.Elements<TableRow>().ToList())
        {
            if (CellAtGrid(row, grid) is not { } cell)
                continue;

            if (Span(cell) > 1)
                SetSpan(cell, Span(cell) - 1);
            else
                cell.Remove();

            if (!row.Elements<TableCell>().Any())
                row.Remove();
        }

        EvenOutPercentageWidths(table);
        return !table.Elements<TableRow>().Any();
    }

    // ---- split ----

    static bool SplitCell(Table table, TableRow row, TableCell cell)
    {
        var span = Span(cell);

        if (span > 1)
        {
            // Undo a horizontal merge: back to one cell per grid column.
            SetSpan(cell, 1);
            for (var i = 1; i < span; i++)
                cell.InsertAfterSelf(NewCellLike(cell, null));

            return false;
        }

        if (OoxmlUnits.EnumAttribute(cell.TableCellProperties?.VerticalMerge, "val") == "restart")
        {
            // Undo a vertical merge: every continuation below becomes a cell of its own again.
            var grid = GridStart(row, cell);
            cell.TableCellProperties!.RemoveAllChildren<VerticalMerge>();

            for (var next = row.NextSibling<TableRow>(); next is not null; next = next.NextSibling<TableRow>())
            {
                var below = CellAtGrid(next, grid);
                var merge = below?.TableCellProperties?.VerticalMerge;
                if (below is null || merge is null || OoxmlUnits.EnumAttribute(merge, "val") == "restart")
                    break;

                merge.Remove();
            }

            return false;
        }

        // An ordinary cell splits into two side by side, which widens that grid column into two for
        // every other row — each of those cells simply spans both halves.
        var at = GridStart(row, cell);
        var columns = table.GetFirstChild<TableGrid>()?.Elements<GridColumn>().ToList() ?? [];

        if (columns.ElementAtOrDefault(at) is { } column)
        {
            string? half = null;
            if (column.Width?.Value is { } w && int.TryParse(w, out var twips) && twips > 1)
            {
                half = (twips / 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
                column.Width = (twips - (twips / 2)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            column.InsertAfterSelf(new GridColumn { Width = half });
        }

        foreach (var other in table.Elements<TableRow>())
        {
            if (ReferenceEquals(other, row))
                continue;

            if (CellAtGrid(other, at) is { } covering)
                SetSpan(covering, Span(covering) + 1);
        }

        cell.InsertAfterSelf(NewCellLike(cell, null));
        EvenOutPercentageWidths(table);
        return false;
    }

    // ---- grid arithmetic ----

    /// <summary>How many grid columns a cell covers.</summary>
    internal static int Span(TableCell cell) => (int)(cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1);

    /// <summary>The grid column a cell starts at, counting any <c>w:gridBefore</c> the row leaves empty.</summary>
    internal static int GridStart(TableRow row, TableCell target)
    {
        var grid = (int)(row.TableRowProperties?.GetFirstChild<GridBefore>()?.Val?.Value ?? 0);

        foreach (var cell in row.Elements<TableCell>())
        {
            if (ReferenceEquals(cell, target))
                return grid;

            grid += Span(cell);
        }

        return grid;
    }

    /// <summary>The cell covering a grid column in a row, or null past the row's end.</summary>
    internal static TableCell? CellAtGrid(TableRow row, int grid)
    {
        foreach (var cell in row.Elements<TableCell>())
        {
            var start = GridStart(row, cell);
            if (grid >= start && grid < start + Span(cell))
                return cell;
        }

        return null;
    }

    static void SetSpan(TableCell cell, int span)
    {
        var properties = cell.TableCellProperties ?? cell.PrependChild(new TableCellProperties());
        properties.RemoveAllChildren<GridSpan>();

        if (span > 1)
            InsertOrderedCell(properties, new GridSpan { Val = span });
    }

    /// <summary>An empty cell with the same properties as <paramref name="template"/>, minus merges.</summary>
    static TableCell NewCellLike(TableCell? template, string? width)
    {
        var cell = new TableCell();

        if (template?.TableCellProperties is { } properties)
        {
            var cloned = (TableCellProperties)properties.CloneNode(true);
            cloned.RemoveAllChildren<GridSpan>();
            cloned.RemoveAllChildren<VerticalMerge>();
            cloned.RemoveAllChildren<HorizontalMerge>();

            if (width is not null && cloned.TableCellWidth is { } cellWidth &&
                OoxmlUnits.EnumAttribute(cellWidth, "type") == "dxa")
            {
                cellWidth.Width = width;
            }

            cell.AppendChild(cloned);
        }

        cell.AppendChild(EmptyParagraphLike(template?.Elements<Paragraph>().FirstOrDefault()));
        return cell;
    }

    /// <summary>
    /// A cell emptied of content, left with one paragraph carrying the formatting its first one had.
    /// </summary>
    /// <remarks>
    /// Never left with none: a <c>w:tc</c> without a paragraph is invalid, and Word's repair for it is
    /// to discard the whole table.
    /// </remarks>
    static void Empty(TableCell cell)
    {
        var first = cell.Elements<Paragraph>().FirstOrDefault();
        var seed = EmptyParagraphLike(first);

        foreach (var child in cell.ChildElements.ToList())
        {
            if (child is not TableCellProperties)
                child.Remove();
        }

        cell.AppendChild(seed);
    }

    static Paragraph EmptyParagraphLike(Paragraph? source)
    {
        var paragraph = new Paragraph();

        if (source?.ParagraphProperties is { } properties)
        {
            var cloned = (ParagraphProperties)properties.CloneNode(true);
            cloned.RemoveAllChildren<SectionProperties>();
            paragraph.AppendChild(cloned);
        }

        if (source is not null && WordParagraphEditor.RunsIn(source).FirstOrDefault()?.RunProperties is { } runProperties)
            paragraph.AppendChild(new Run((RunProperties)runProperties.CloneNode(true)));

        return paragraph;
    }

    /// <summary>Moves a cell's non-empty content into another, dropping the empty paragraphs.</summary>
    static void MoveContent(TableCell from, TableCell into)
    {
        var moved = from.ChildElements
            .Where(x => x is not TableCellProperties)
            .Where(x => x is not Paragraph p || WordParagraphEditor.LengthOf(p) > 0)
            .ToList();

        if (moved.Count == 0)
            return;

        // An empty paragraph at the end of the target is swallowed rather than left above the new text.
        if (into.Elements<Paragraph>().LastOrDefault() is { } last && WordParagraphEditor.LengthOf(last) == 0 &&
            into.Elements<Paragraph>().Count() == 1)
        {
            last.Remove();
        }

        foreach (var element in moved)
        {
            element.Remove();
            into.AppendChild(element);
        }

        if (!from.Elements<Paragraph>().Any())
            from.AppendChild(new Paragraph());
    }

    /// <summary>
    /// Re-shares percentage cell widths after the column count changed.
    /// </summary>
    /// <remarks>
    /// A table this editor inserted sizes every cell as a fiftieth-of-a-percent share of the measure.
    /// Adding a fourth column to three thirds leaves the widths summing past 100%, which Word renders by
    /// squashing the last column.
    /// </remarks>
    static void EvenOutPercentageWidths(Table table)
    {
        var columns = table.GetFirstChild<TableGrid>()?.Elements<GridColumn>().Count() ?? 0;
        if (columns == 0)
            return;

        foreach (var cell in table.Descendants<TableCell>())
        {
            if (cell.TableCellProperties?.TableCellWidth is not { } width || OoxmlUnits.EnumAttribute(width, "type") != "pct")
                continue;

            width.Width = (5000 * Span(cell) / columns).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Inserts a child of <c>w:tcPr</c> at its schema position.</summary>
    internal static void InsertOrderedCell(TableCellProperties properties, OpenXmlElement child)
    {
        static int Rank(OpenXmlElement element) => element.LocalName switch
        {
            "cnfStyle" => 0,
            "tcW" => 1,
            "gridSpan" => 2,
            "hMerge" => 3,
            "vMerge" => 4,
            "tcBorders" => 5,
            "shd" => 6,
            "noWrap" => 7,
            "tcMar" => 8,
            "textDirection" => 9,
            "tcFitText" => 10,
            "vAlign" => 11,
            "hideMark" => 12,
            _ => 50
        };

        var rank = Rank(child);
        OpenXmlElement? previous = null;

        foreach (var existing in properties.ChildElements)
        {
            if (Rank(existing) > rank)
                break;

            previous = existing;
        }

        if (previous is null)
            properties.InsertAt(child, 0);
        else
            properties.InsertAfter(child, previous);
    }
}
