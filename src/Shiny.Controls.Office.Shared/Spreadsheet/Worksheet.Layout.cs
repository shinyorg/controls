using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;
using SheetElement = DocumentFormat.OpenXml.Spreadsheet.Worksheet;

namespace Shiny.Controls.Office.Spreadsheet;

/// <summary>
/// The sheet-level lists and view settings: merges, the frozen pane, gridlines and headings, hidden rows.
/// </summary>
public sealed partial class Worksheet
{
    /// <summary>The workbook this sheet belongs to.</summary>
    public Workbook Workbook => this.workbook;

    internal SheetElement Root => this.SheetElement();

    /// <summary>The outer XML of every top-level child with this local name.</summary>
    internal IReadOnlyList<string> ReadElements(string localName)
        => this.part.Worksheet is { } root ? SheetXml.Read(root, localName) : [];

    /// <summary>Replaces the top-level children with this local name, in schema order.</summary>
    internal void ReplaceElements(string localName, IReadOnlyList<string> fragments)
    {
        SheetXml.Replace(this.SheetElement(), localName, fragments);
        this.workbook.OnContentChanged();
    }

    // ---- merges ----

    /// <summary>The merge covering <paramref name="cell"/>, or null when it is not merged.</summary>
    public CellRange? MergeAt(CellRef cell)
    {
        foreach (var range in this.MergedRanges)
        {
            if (range.Contains(cell))
                return range;
        }

        return null;
    }

    /// <summary>
    /// The <c>&lt;mergeCells&gt;</c> fragment for a merge list, or none at all for an empty one.
    /// </summary>
    /// <remarks>Excel reports an empty <c>mergeCells</c> element as something to repair.</remarks>
    internal static IReadOnlyList<string> MergeFragments(IEnumerable<CellRange> ranges)
    {
        var list = ranges.Where(x => !x.IsSingleCell).Distinct().ToList();
        if (list.Count == 0)
            return [];

        var element = new MergeCells { Count = (uint)list.Count };
        foreach (var range in list)
            element.AppendChild(new MergeCell { Reference = range.ToString() });

        return [element.OuterXml];
    }

    // ---- sheet view ----

    /// <summary>The first <c>&lt;sheetView&gt;</c>, which is the one Excel shows.</summary>
    SheetView? PrimaryView
        => this.part.Worksheet?.GetFirstChild<SheetViews>()?.Elements<SheetView>().FirstOrDefault();

    /// <summary>Whether the sheet draws its gridlines. Stored per sheet, in <c>showGridLines</c>.</summary>
    public bool ShowGridLines => this.PrimaryView?.ShowGridLines?.Value ?? true;

    /// <summary>Whether the sheet shows its row and column headings, <c>showRowColHeaders</c>.</summary>
    public bool ShowHeadings => this.PrimaryView?.ShowRowColHeaders?.Value ?? true;

    /// <summary>Whether cells show their formulas rather than their results, <c>showFormulas</c>.</summary>
    public bool ShowFormulas => this.PrimaryView?.ShowFormulas?.Value ?? false;

    /// <summary>The zoom the file was saved at, as a factor — 1 for 100%.</summary>
    public double ZoomScale => (this.PrimaryView?.ZoomScale?.Value ?? 100u) / 100d;

    /// <summary>
    /// Writes one of the view flags. Not an undo step: Excel does not undo a gridline toggle either, but
    /// it does save it, and so does this.
    /// </summary>
    /// <remarks>
    /// The schema default is written as an absent attribute rather than as its value, which is what
    /// Excel does and what keeps the element matching a file that never touched the flag.
    /// </remarks>
    internal void WriteViewFlag(string flag, bool value)
    {
        var view = this.EnsurePrimaryView();

        switch (flag)
        {
            case "showGridLines":
                view.ShowGridLines = value ? null : false;
                break;

            case "showRowColHeaders":
                view.ShowRowColHeaders = value ? null : false;
                break;

            case "showFormulas":
                view.ShowFormulas = value ? true : null;
                break;
        }

        this.workbook.OnContentChanged();
    }

    SheetView EnsurePrimaryView()
    {
        var root = this.SheetElement();
        var views = root.GetFirstChild<SheetViews>() ?? SheetXml.Insert(root, new SheetViews());
        var view = views.Elements<SheetView>().FirstOrDefault();

        if (view is null)
        {
            view = new SheetView { WorkbookViewId = 0u };
            views.AppendChild(view);
        }

        return view;
    }

    /// <summary>
    /// The <c>&lt;sheetViews&gt;</c> fragment with the pane frozen at <paramref name="split"/>, or unfrozen
    /// with null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built on a copy of the current element, so the view's other settings — zoom, gridlines, the tab's
    /// selected state — come through unchanged. The pane has to be the view's first child and each
    /// pane of a split has its own <c>&lt;selection&gt;</c>, which is why the selections are rewritten
    /// alongside it rather than left for Excel to reconcile: a frozen pane whose selection names a pane
    /// that no longer exists opens as a repair.
    /// </para>
    /// <para>
    /// <c>xSplit</c> and <c>ySplit</c> count columns and rows for a frozen pane — not twips, which is
    /// what they mean for an unfrozen split.
    /// </para>
    /// </remarks>
    internal IReadOnlyList<string> FrozenPaneFragment(CellRef? split, CellRef activeCell)
    {
        var root = this.SheetElement();
        var views = (SheetViews?)root.GetFirstChild<SheetViews>()?.CloneNode(true) ?? new SheetViews();
        var view = views.Elements<SheetView>().FirstOrDefault();

        if (view is null)
        {
            view = new SheetView { WorkbookViewId = 0u };
            views.AppendChild(view);
        }

        view.RemoveAllChildren<Pane>();
        view.RemoveAllChildren<Selection>();

        var active = activeCell.Relative().ToString();

        if (split is not { } at || (at.Column == 0 && at.Row == 0))
        {
            view.AppendChild(new Selection { ActiveCell = active, SequenceOfReferences = new ListValue<StringValue> { InnerText = active } });
            return [views.OuterXml];
        }

        var columns = at.Column;
        var rows = at.Row;

        PaneValues activePane;
        if (columns > 0 && rows > 0)
            activePane = PaneValues.BottomRight;
        else if (rows > 0)
            activePane = PaneValues.BottomLeft;
        else
            activePane = PaneValues.TopRight;

        var pane = new Pane
        {
            TopLeftCell = new CellRef(at.Column, at.Row).ToString(),
            ActivePane = activePane,
            State = PaneStateValues.Frozen
        };

        if (columns > 0)
            pane.HorizontalSplit = columns;

        if (rows > 0)
            pane.VerticalSplit = rows;

        // The pane first, then one selection per pane of the split, the active one last - the order
        // Excel itself writes.
        view.InsertAt(pane, 0);

        if (columns > 0 && rows > 0)
        {
            view.AppendChild(new Selection { Pane = PaneValues.TopRight });
            view.AppendChild(new Selection { Pane = PaneValues.BottomLeft });
        }

        view.AppendChild(new Selection
        {
            Pane = activePane,
            ActiveCell = active,
            SequenceOfReferences = new ListValue<StringValue> { InnerText = active }
        });

        return [views.OuterXml];
    }

    // ---- hidden rows ----

    /// <summary>True when the row is hidden in the file — by hand or by a filter.</summary>
    public bool IsRowHidden(int row) => this.editor.FindRow(row)?.Hidden?.Value ?? false;

    /// <summary>True when the column is hidden in the file.</summary>
    public bool IsColumnHidden(int column) => ColumnSpans.Find(this.SheetElement(), column)?.Hidden ?? false;

    internal void WriteRowHidden(int row, bool hidden)
    {
        var existing = this.editor.FindRow(row);
        if (existing is null && !hidden)
            return;

        var element = existing ?? this.editor.GetOrCreateRow(row);
        element.Hidden = hidden ? true : null;
        this.workbook.OnContentChanged();
    }

    /// <summary>The raw part, for the features that keep their own parts beside the sheet.</summary>
    internal DocumentFormat.OpenXml.Packaging.WorksheetPart WorksheetPart => this.part;
}
