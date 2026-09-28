using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SheetElement = DocumentFormat.OpenXml.Spreadsheet.Worksheet;

namespace Shiny.Controls.Office.Spreadsheet;

/// <summary>
/// One sheet of a <see cref="Workbook"/>, read and written directly against its OOXML element tree.
/// </summary>
public sealed partial class Worksheet
{
    readonly Workbook workbook;
    readonly WorksheetPart part;
    readonly SheetDataEditor editor;

    internal Worksheet(Workbook workbook, WorksheetPart part, Sheet entry, string name, uint sheetId, bool visible)
    {
        this.workbook = workbook;
        this.part = part;
        this.Entry = entry;
        this.Name = name;
        this.SheetId = sheetId;
        this.IsVisible = visible;

        var sheetElement = part.Worksheet ??= new SheetElement();
        var sheetData = sheetElement.GetFirstChild<SheetData>();
        if (sheetData is null)
        {
            sheetData = new SheetData();
            sheetElement.AppendChild(sheetData);
        }

        this.editor = new SheetDataEditor(sheetData);
    }

    /// <summary>
    /// The sheet's name. Only <see cref="Workbook"/> may change it, and only through a rename, which
    /// has to rewrite every formula that refers to the sheet in the same step.
    /// </summary>
    public string Name { get; internal set; }

    public uint SheetId { get; }

    /// <summary>False for a sheet Excel is hiding. Hidden sheets are read, calculated and saved as normal.</summary>
    public bool IsVisible { get; internal set; }

    /// <summary>The part holding this sheet's XML, so the workbook can clone or delete it.</summary>
    internal WorksheetPart Part => this.part;

    /// <summary>
    /// This sheet's entry in the workbook's <c>&lt;sheets&gt;</c> list.
    /// </summary>
    /// <remarks>
    /// Name, visibility and sheet order all live on this element rather than in the sheet part, and
    /// the element's position in its parent <em>is</em> the tab order — so structural edits move this
    /// around rather than rewriting anything inside the sheet itself.
    /// </remarks>
    internal Sheet Entry { get; }

    /// <summary>The bounding box of every populated cell, or null for an empty sheet.</summary>
    public CellRange? UsedRange => this.editor.UsedRange();

    /// <summary>
    /// Every cell the sheet stores, in reading order — rows down, columns across.
    /// </summary>
    /// <remarks>
    /// What a search walks. Rows and cells are already required to be in ascending order in the file —
    /// Excel reports one that is not as corrupt rather than repairing it — so this is reading order
    /// without a sort.
    /// </remarks>
    public IEnumerable<CellRef> PopulatedCells() => this.editor.PopulatedCells();

    public CellValue GetValue(CellRef reference)
    {
        var cell = this.editor.FindCell(reference);
        return cell is null ? CellValue.Blank : this.ReadCell(cell);
    }

    /// <summary>The formula text without its leading <c>=</c>, or null when the cell holds a literal.</summary>
    public string? GetFormula(CellRef reference)
        => this.editor.FindCell(reference)?.CellFormula?.Text;

    /// <summary>The style index (into cellXfs) applied to the cell, or null when it uses the default.</summary>
    public uint? GetStyleIndex(CellRef reference)
        => this.editor.FindCell(reference)?.StyleIndex?.Value;

    /// <summary>Reads the cached result of a formula cell without recomputing it.</summary>
    internal CellValue ReadCell(Cell cell)
    {
        var raw = cell.CellValue?.Text;
        var type = cell.DataType?.Value;

        if (type == CellValues.SharedString)
        {
            return int.TryParse(raw, out var index)
                ? CellValue.FromText(this.workbook.SharedStrings[index])
                : CellValue.Blank;
        }

        if (type == CellValues.InlineString)
        {
            var inline = cell.GetFirstChild<InlineString>();
            return CellValue.FromText(inline?.Text?.Text ?? inline?.InnerText ?? string.Empty);
        }

        if (type == CellValues.Boolean)
            return CellValue.FromBoolean(raw == "1");

        if (type == CellValues.Error)
            return CellValue.TryParseError(raw, out var error) ? CellValue.FromError(error) : CellValue.FromError(CellError.Value);

        // t="str" is a formula that produced text.
        if (type == CellValues.String)
            return CellValue.FromText(raw ?? string.Empty);

        if (string.IsNullOrEmpty(raw))
            return CellValue.Blank;

        return double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? CellValue.FromNumber(number)
            : CellValue.FromText(raw);
    }

    /// <summary>
    /// Writes a literal value, clearing any formula that was there.
    /// </summary>
    /// <remarks>
    /// Style is deliberately untouched: replacing the contents of a formatted cell must not strip its
    /// formatting, which is what a naive create-new-cell implementation does.
    /// </remarks>
    internal void WriteValue(CellRef reference, CellValue value)
    {
        if (value.IsBlank)
        {
            var existing = this.editor.FindCell(reference);
            if (existing is null)
                return;

            // Keep the element when it carries a style, or the formatting silently disappears.
            if (existing.StyleIndex is not null)
            {
                existing.CellFormula = null;
                existing.CellValue = null;
                existing.DataType = null;
                existing.RemoveAllChildren<InlineString>();
            }
            else
            {
                existing.Remove();
            }

            this.workbook.OnCellChanged(this, reference);
            return;
        }

        var cell = this.editor.GetOrCreateCell(reference);
        cell.CellFormula = null;
        cell.RemoveAllChildren<InlineString>();

        switch (value.Kind)
        {
            case CellValueKind.Number:
                cell.DataType = null; // n is the default and Excel omits it
                cell.CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(
                    value.AsNumber().ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                break;

            case CellValueKind.Text:
                cell.DataType = CellValues.SharedString;
                cell.CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(
                    this.workbook.SharedStrings.GetOrAdd(value.AsText()).ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;

            case CellValueKind.Boolean:
                cell.DataType = CellValues.Boolean;
                cell.CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(value.AsBoolean() ? "1" : "0");
                break;

            case CellValueKind.Error:
                cell.DataType = CellValues.Error;
                cell.CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(CellValue.ErrorText(value.AsError()));
                break;
        }

        this.workbook.OnCellChanged(this, reference);
    }

    /// <summary>
    /// Writes a formula. The cached value is removed, so the workbook is marked for recalculation on
    /// load — until the calc engine lands, Excel itself computes the result when the file is opened.
    /// </summary>
    internal void WriteFormula(CellRef reference, string formula)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formula);
        var text = formula.StartsWith('=') ? formula[1..] : formula;

        var cell = this.editor.GetOrCreateCell(reference);
        cell.RemoveAllChildren<InlineString>();
        cell.DataType = null;
        cell.CellFormula = new CellFormula(text);
        cell.CellValue = null;

        this.workbook.OnCellChanged(this, reference);
    }

    /// <summary>
    /// The style a cell actually renders with, following Excel's fallback: the cell's own style, then
    /// its row's, then its column's.
    /// </summary>
    /// <remarks>
    /// This is what a renderer wants, and <see cref="GetStyleIndex"/> is not. Formatting a whole column
    /// writes one style onto the column element and touches no cells at all, so a painter reading only
    /// the cell's own index shows an unformatted column and the operation appears to have done nothing.
    /// </remarks>
    public uint? GetEffectiveStyleIndex(CellRef reference)
        => this.GetStyleIndex(reference)
           ?? this.GetRowStyleIndex(reference.Row)
           ?? this.GetColumnStyleIndex(reference.Column);

    /// <summary>The style applied to a whole row, or null when the row carries none.</summary>
    /// <remarks>
    /// <c>customFormat</c> is the gate here, unlike on a cell: a row element routinely carries a style
    /// index it does not mean, left behind by a format that was applied and then removed.
    /// </remarks>
    public uint? GetRowStyleIndex(int row)
    {
        var element = this.editor.FindRow(row);
        return element?.CustomFormat?.Value == true ? element.StyleIndex?.Value : null;
    }

    /// <summary>The style applied to a whole column, or null when the column carries none.</summary>
    public uint? GetColumnStyleIndex(int column)
        => ColumnSpans.Find(this.SheetElement(), column)?.Style;

    /// <summary>The width a column is stored with, in characters, or null when it uses the sheet default.</summary>
    public double? GetColumnWidth(int column)
    {
        var span = ColumnSpans.Find(this.SheetElement(), column);
        return span?.CustomWidth == true ? span.Width : null;
    }

    internal void WriteStyleIndex(CellRef reference, uint? styleIndex)
    {
        var cell = this.editor.GetOrCreateCell(reference);
        cell.StyleIndex = styleIndex is null ? null : UInt32Value.FromUInt32(styleIndex.Value);
        this.workbook.OnContentChanged();
    }

    internal void WriteRowStyle(int row, uint? styleIndex)
    {
        var element = this.editor.GetOrCreateRow(row);

        if (styleIndex is { } style)
        {
            element.StyleIndex = style;
            element.CustomFormat = true;
        }
        else
        {
            element.StyleIndex = null;
            element.CustomFormat = null;
        }

        this.workbook.OnContentChanged();
    }

    /// <summary>
    /// Walks a column range as maximal runs that share a style, so a caller can act on each run once.
    /// </summary>
    /// <remarks>
    /// The alternative is asking column by column, and a selection made from a column header is 16,384
    /// columns wide. Each of those questions has to search the span list, so the per-column loop is
    /// quadratic in a case that happens on a single click.
    /// </remarks>
    internal IEnumerable<(int First, int Last, uint? Style)> ColumnStyleRuns(int first, int last)
        => this.ColumnRuns(first, last, span => span?.Style);

    /// <summary>The same, for the width a column is stored with. Null means the sheet default.</summary>
    internal IEnumerable<(int First, int Last, double? Width)> ColumnWidthRuns(int first, int last)
        => this.ColumnRuns(first, last, span => span?.CustomWidth == true ? span.Width : null);

    IEnumerable<(int First, int Last, T Value)> ColumnRuns<T>(int first, int last, Func<ColumnSpans.Span?, T> select)
    {
        first = Math.Clamp(first, 0, CellRef.MaxColumn);
        last = Math.Clamp(last, first, CellRef.MaxColumn);

        var spans = ColumnSpans.Read(this.SheetElement());
        var comparer = EqualityComparer<T>.Default;

        var runStart = first;
        var runValue = select(SpanAt(spans, first));

        for (var column = first + 1; column <= last; column++)
        {
            var value = select(SpanAt(spans, column));
            if (comparer.Equals(value, runValue))
                continue;

            yield return (runStart, column - 1, runValue);
            runStart = column;
            runValue = value;
        }

        yield return (runStart, last, runValue);
    }

    static ColumnSpans.Span? SpanAt(IReadOnlyList<ColumnSpans.Span> spans, int column)
    {
        foreach (var span in spans)
        {
            if (column >= span.First && column <= span.Last)
                return span;
        }

        return null;
    }

    internal void WriteColumnStyle(int first, int last, uint? styleIndex)
    {
        if (ColumnSpans.Apply(this.SheetElement(), first, last, span => span with { Style = styleIndex }))
            this.workbook.OnContentChanged();
    }

    /// <summary>Sets a column's width in characters, or clears it back to the sheet default with null.</summary>
    internal void WriteColumnWidth(int first, int last, double? characters)
    {
        var changed = ColumnSpans.Apply(this.SheetElement(), first, last, span => span with
        {
            Width = characters,
            CustomWidth = characters is not null,

            // bestFit means "Excel chose this width by auto-fitting"; a width the user dragged out or
            // typed is not that, and leaving the flag set makes Excel re-fit it on the next edit.
            BestFit = false
        });

        if (changed)
            this.workbook.OnContentChanged();
    }

    /// <summary>The height a row is stored with, in points, or null when it uses the sheet default.</summary>
    public double? GetRowHeight(int row)
    {
        var element = this.editor.FindRow(row);
        return element?.CustomHeight?.Value == true ? element.Height?.Value : null;
    }

    internal void WriteRowHeight(int row, double? points)
    {
        var element = this.editor.GetOrCreateRow(row);

        if (points is { } height)
        {
            element.Height = height;
            element.CustomHeight = true;
        }
        else
        {
            element.Height = null;
            element.CustomHeight = null;
        }

        this.workbook.OnContentChanged();
    }

    internal void WriteColumnHidden(int first, int last, bool hidden)
    {
        if (ColumnSpans.Apply(this.SheetElement(), first, last, span => span with { Hidden = hidden }))
            this.workbook.OnContentChanged();
    }

    // ---- structural row and column edits ----
    //
    // Everything below moves cells that already exist rather than changing what one of them says, so
    // each of these has three jobs, not one: renumber the sheetData, carry the per-row and per-column
    // properties along with the band, and repoint the merged ranges. Formulas are the workbook's job,
    // because a formula on any other sheet can name this one.

    /// <summary>Pushes <paramref name="count"/> blank rows in at <paramref name="at"/>.</summary>
    internal void InsertRows(int at, int count)
    {
        this.editor.InsertRows(at, count);
        this.ShiftMerges(at, count, rows: true);
        this.ShiftSheetRanges(at, count, rows: true);
        this.workbook.OnContentChanged();
    }

    /// <summary>Removes <paramref name="count"/> rows at <paramref name="at"/>, closing the gap.</summary>
    internal void DeleteRows(int at, int count)
    {
        this.editor.RemoveRows(at, count);
        this.ShiftMerges(at, -count, rows: true);
        this.ShiftSheetRanges(at, -count, rows: true);
        this.workbook.OnContentChanged();
    }

    /// <summary>Pushes <paramref name="count"/> blank columns in at <paramref name="at"/>.</summary>
    internal void InsertColumns(int at, int count)
    {
        this.editor.InsertColumns(at, count);
        ColumnSpans.Shift(this.SheetElement(), at, count);
        this.ShiftMerges(at, count, rows: false);
        this.ShiftSheetRanges(at, count, rows: false);
        this.workbook.OnContentChanged();
    }

    /// <summary>Removes <paramref name="count"/> columns at <paramref name="at"/>, closing the gap.</summary>
    internal void DeleteColumns(int at, int count)
    {
        this.editor.RemoveColumns(at, count);
        ColumnSpans.Shift(this.SheetElement(), at, -count);
        this.ShiftMerges(at, -count, rows: false);
        this.ShiftSheetRanges(at, -count, rows: false);
        this.workbook.OnContentChanged();
    }

    /// <summary>
    /// Moves the merged ranges along with an inserted or deleted band.
    /// </summary>
    /// <remarks>
    /// A merge that straddles the insertion point grows rather than moves, which is why the two edges
    /// are shifted independently. One that falls entirely inside a deleted band has nothing left to
    /// merge and is dropped — leaving it behind is a reference to cells that no longer exist, and
    /// Excel reports that as a corrupt file rather than ignoring it.
    /// </remarks>
    void ShiftMerges(int at, int delta, bool rows)
    {
        var merges = this.part.Worksheet?.GetFirstChild<MergeCells>();
        if (merges is null)
            return;

        foreach (var merge in merges.Elements<MergeCell>().ToList())
        {
            if (merge.Reference?.Value is not { } text || !CellRange.TryParse(text, out var range))
                continue;

            var first = Edge(rows ? range.Top : range.Left);
            var last = Edge(rows ? range.Bottom : range.Right);

            var moved = first is null || last is null
                ? (CellRange?)null
                : rows
                    ? new CellRange(new CellRef(range.Left, first.Value), new CellRef(range.Right, last.Value))
                    : new CellRange(new CellRef(first.Value, range.Top), new CellRef(last.Value, range.Bottom));

            // A merge the delete reduced to one cell is no longer a merge, and Excel rejects a
            // <mergeCell> whose reference names a single cell.
            if (moved is not { } result || result.IsSingleCell)
            {
                merge.Remove();
                continue;
            }

            if (result != range)
                merge.Reference = result.ToString();
        }

        if (!merges.Elements<MergeCell>().Any())
        {
            merges.Remove();
        }
        else
        {
            merges.Count = (uint)merges.Elements<MergeCell>().Count();
        }

        int? Edge(int index)
        {
            if (index < at)
                return index;

            if (delta > 0)
            {
                var shifted = index + delta;
                var limit = rows ? CellRef.MaxRow : CellRef.MaxColumn;
                return shifted > limit ? null : shifted;
            }

            // Inside a deleted band there is no cell left to point at, so the edge collapses onto the
            // row or column that now occupies the position.
            return index < at - delta ? at : index + delta;
        }
    }

    /// <summary>The worksheet's root element. Present for every sheet the model loaded.</summary>
    SheetElement SheetElement()
        => this.part.Worksheet ?? throw new InvalidOperationException($"Sheet '{this.Name}' has no content.");


    /// <summary>Every cell on the sheet that holds a formula, with the formula text.</summary>
    internal IEnumerable<(CellRef Cell, string Formula)> Formulas()
    {
        foreach (var row in this.part.Worksheet?.GetFirstChild<SheetData>()?.Elements<Row>() ?? Enumerable.Empty<Row>())
        {
            var rowIndex = (int)(row.RowIndex?.Value ?? 0) - 1;
            if (rowIndex < 0)
                continue;

            foreach (var cell in row.Elements<Cell>())
            {
                var formula = cell.CellFormula?.Text;
                if (!string.IsNullOrEmpty(formula))
                    yield return (new CellRef(SheetDataEditor.ColumnOf(cell), rowIndex), formula);
            }
        }
    }

    /// <summary>
    /// Rewrites every formula on the sheet through <paramref name="rewrite"/>, leaving cells whose text
    /// comes back unchanged completely untouched.
    /// </summary>
    /// <remarks>
    /// Used by a sheet rename. Skipping the unchanged ones is not an optimisation: assigning the same
    /// text back still marks the element dirty, and a rename would then rewrite the XML of every sheet
    /// in the workbook rather than of the few that actually mentioned the renamed one.
    /// </remarks>
    internal bool RewriteFormulas(Func<string, string> rewrite)
    {
        var changed = false;

        foreach (var row in this.part.Worksheet?.GetFirstChild<SheetData>()?.Elements<Row>() ?? Enumerable.Empty<Row>())
        {
            foreach (var cell in row.Elements<Cell>())
            {
                if (cell.CellFormula?.Text is not { Length: > 0 } formula)
                    continue;

                var rewritten = rewrite(formula);
                if (string.Equals(rewritten, formula, StringComparison.Ordinal))
                    continue;

                cell.CellFormula.Text = rewritten;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Updates a formula cell's cached result without disturbing the formula itself.
    /// </summary>
    /// <remarks>
    /// Every reader other than Excel — and Excel itself before it recalculates — shows this cached value,
    /// so leaving it stale after an edit means the file displays numbers that are simply wrong.
    /// </remarks>
    internal void WriteCachedValue(CellRef reference, CellValue value)
    {
        var cell = this.editor.FindCell(reference);
        if (cell?.CellFormula is null)
            return;

        cell.RemoveAllChildren<InlineString>();

        switch (value.Kind)
        {
            case CellValueKind.Number:
                cell.DataType = null;
                cell.CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(
                    value.AsNumber().ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                break;

            case CellValueKind.Text:
                // t="str" is a formula that produced text, distinct from a shared string.
                cell.DataType = CellValues.String;
                cell.CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(value.AsText());
                break;

            case CellValueKind.Boolean:
                cell.DataType = CellValues.Boolean;
                cell.CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(value.AsBoolean() ? "1" : "0");
                break;

            case CellValueKind.Error:
                cell.DataType = CellValues.Error;
                cell.CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(CellValue.ErrorText(value.AsError()));
                break;

            default:
                cell.DataType = null;
                cell.CellValue = null;
                break;
        }
    }

    /// <summary>The value to display: the freshly computed result for a formula, otherwise the literal.</summary>
    public CellValue GetDisplayValue(CellRef reference)
        => this.workbook.GetEffectiveValue(this.Name, reference);


    /// <summary>Column width definitions from <c>&lt;cols&gt;</c>, as inclusive zero-based spans.</summary>
    internal IEnumerable<(int First, int Last, double? Width, bool Hidden)> ColumnDefinitions()
    {
        var columns = this.part.Worksheet?.GetFirstChild<Columns>();
        if (columns is null)
            yield break;

        foreach (var column in columns.Elements<Column>())
        {
            var min = (int)(column.Min?.Value ?? 1) - 1;
            var max = (int)(column.Max?.Value ?? 1) - 1;
            if (min < 0)
                continue;

            // customWidth distinguishes "this is the width" from "this is the default, recorded anyway".
            var width = column.CustomWidth?.Value == true ? column.Width?.Value : null;
            yield return (min, max, width, column.Hidden?.Value ?? false);
        }
    }

    /// <summary>Row height definitions, for rows that override the sheet default.</summary>
    internal IEnumerable<(int Row, double? Height, bool Hidden)> RowDefinitions()
    {
        foreach (var row in this.part.Worksheet?.GetFirstChild<SheetData>()?.Elements<Row>() ?? Enumerable.Empty<Row>())
        {
            var index = (int)(row.RowIndex?.Value ?? 0) - 1;
            if (index < 0)
                continue;

            var height = row.CustomHeight?.Value == true ? row.Height?.Value : null;
            var hidden = row.Hidden?.Value ?? false;

            if (height is not null || hidden)
                yield return (index, height, hidden);
        }
    }

    /// <summary>Ranges merged on this sheet. Read-only for now; editing them arrives with structural edits.</summary>
    public IReadOnlyList<CellRange> MergedRanges
    {
        get
        {
            var merges = this.part.Worksheet?.GetFirstChild<MergeCells>();
            if (merges is null)
                return Array.Empty<CellRange>();

            var result = new List<CellRange>();
            foreach (var merge in merges.Elements<MergeCell>())
            {
                if (merge.Reference?.Value is { } text && CellRange.TryParse(text, out var range))
                    result.Add(range);
            }

            return result;
        }
    }

    /// <summary>The frozen pane split, expressed as the first non-frozen cell, or null when nothing is frozen.</summary>
    public CellRef? FrozenPane
    {
        get
        {
            var pane = this.part.Worksheet?
                .GetFirstChild<SheetViews>()?
                .Elements<SheetView>().FirstOrDefault()?
                .GetFirstChild<Pane>();

            if (pane?.State?.Value != PaneStateValues.Frozen)
                return null;

            var columns = (int)(pane.HorizontalSplit?.Value ?? 0);
            var rows = (int)(pane.VerticalSplit?.Value ?? 0);
            return columns == 0 && rows == 0 ? null : new CellRef(columns, rows);
        }
    }
}
