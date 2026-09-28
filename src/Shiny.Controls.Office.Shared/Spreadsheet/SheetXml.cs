using DocumentFormat.OpenXml;
using Shiny.Controls.Office.Editing;
using SheetElement = DocumentFormat.OpenXml.Spreadsheet.Worksheet;

namespace Shiny.Controls.Office.Spreadsheet;

/// <summary>
/// Schema-order bookkeeping for the children of a <c>&lt;worksheet&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// CT_Worksheet is a <em>sequence</em>, not a bag: <c>mergeCells</c> must come after <c>autoFilter</c>
/// and before <c>conditionalFormatting</c>, <c>hyperlinks</c> before <c>pageMargins</c>, <c>drawing</c>
/// before <c>tableParts</c>. Appending a new element to the end of the sheet saves without complaint,
/// round-trips through the SDK, and then makes Excel declare the file corrupt. Every element a
/// feature adds goes through <see cref="Insert"/>, which finds its slot from this list.
/// </para>
/// <para>
/// Children that are not in the list — an <c>mc:AlternateContent</c> wrapper, an extension from a newer
/// schema — are stepped over rather than treated as anchors, so they keep the position they had.
/// </para>
/// </remarks>
public static class SheetXml
{
    public const string MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>CT_Worksheet, in the order the schema demands.</summary>
    public static readonly IReadOnlyList<string> WorksheetOrder =
    [
        "sheetPr", "dimension", "sheetViews", "sheetFormatPr", "cols", "sheetData", "sheetCalcPr",
        "sheetProtection", "protectedRanges", "scenarios", "autoFilter", "sortState", "dataConsolidate",
        "customSheetViews", "mergeCells", "phoneticPr", "conditionalFormatting", "dataValidations",
        "hyperlinks", "printOptions", "pageMargins", "pageSetup", "headerFooter", "rowBreaks", "colBreaks",
        "customProperties", "cellWatches", "ignoredErrors", "smartTags", "drawing", "legacyDrawing",
        "legacyDrawingHF", "drawingHF", "picture", "oleObjects", "controls", "webPublishItems", "tableParts",
        "extLst"
    ];

    static int OrderOf(string localName)
    {
        for (var i = 0; i < WorksheetOrder.Count; i++)
        {
            if (string.Equals(WorksheetOrder[i], localName, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Puts <paramref name="element"/> into <paramref name="sheet"/> at the position the schema gives
    /// it — after the last element of the same kind if there are some already.
    /// </summary>
    public static T Insert<T>(OpenXmlCompositeElement sheet, T element) where T : OpenXmlElement
    {
        var order = OrderOf(element.LocalName);
        if (order < 0)
        {
            sheet.AppendChild(element);
            return element;
        }

        foreach (var child in sheet.ChildElements)
        {
            if (child.NamespaceUri != MainNamespace)
                continue;

            var childOrder = OrderOf(child.LocalName);
            if (childOrder > order)
            {
                sheet.InsertBefore(element, child);
                return element;
            }
        }

        sheet.AppendChild(element);
        return element;
    }

    /// <summary>The outer XML of every top-level child with this local name, in document order.</summary>
    public static IReadOnlyList<string> Read(OpenXmlCompositeElement sheet, string localName)
        => sheet.ChildElements
            .Where(x => x.LocalName == localName && x.NamespaceUri == MainNamespace)
            .Select(x => x.OuterXml)
            .ToList();

    /// <summary>
    /// Replaces every top-level child named <paramref name="localName"/> with the given fragments.
    /// </summary>
    /// <remarks>
    /// New fragments go where the first old one was when there was one — which is exactly where the file
    /// had it, including relative to anything unmodelled — and into their schema slot otherwise.
    /// </remarks>
    public static void Replace(OpenXmlCompositeElement sheet, string localName, IReadOnlyList<string> fragments)
    {
        var existing = sheet.ChildElements
            .Where(x => x.LocalName == localName && x.NamespaceUri == MainNamespace)
            .ToList();

        var parsed = fragments.Select(Parse).ToList();

        if (existing.Count > 0)
        {
            var anchor = existing[0];
            foreach (var element in parsed)
                sheet.InsertBefore(element, anchor);

            foreach (var old in existing)
                old.Remove();

            return;
        }

        foreach (var element in parsed)
            Insert(sheet, element);
    }

    /// <summary>
    /// Turns a fragment's outer XML back into a typed element.
    /// </summary>
    /// <remarks>
    /// Wrapped in a worksheet declaring the namespaces a fragment may use without declaring them itself —
    /// the SDK writes <c>r:id</c> attributes inside a <c>&lt;drawing&gt;</c> without restating the
    /// relationship prefix when the declaration lived on the root it was read from.
    /// </remarks>
    public static OpenXmlElement Parse(string fragment)
    {
        var wrapped =
            $"<x:worksheet xmlns:x=\"{MainNamespace}\" xmlns=\"{MainNamespace}\" xmlns:r=\"{RelationshipNamespace}\" " +
            "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" " +
            "xmlns:x14ac=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/ac\" " +
            "xmlns:x14=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/main\" " +
            "xmlns:xm=\"http://schemas.microsoft.com/office/excel/2006/main\" " +
            "xmlns:xr=\"http://schemas.microsoft.com/office/spreadsheetml/2014/revision\">" +
            fragment +
            "</x:worksheet>";

        var root = new SheetElement(wrapped);
        var child = root.FirstChild ?? throw new InvalidOperationException("Empty worksheet fragment.");
        return child.CloneNode(true);
    }
}

/// <summary>
/// Replaces every top-level worksheet child of one kind — all the <c>conditionalFormatting</c> blocks,
/// the <c>mergeCells</c> list, the <c>sheetViews</c> — with a new set, as one undo step.
/// </summary>
/// <remarks>
/// <para>
/// The workhorse behind merges, frozen panes, filters, conditional formatting, validation and hyperlinks.
/// Each of those is a small list the file keeps in one place, and "replace the list" is both the whole
/// of the edit and exactly its own inverse: the command captures the old fragments as it applies, so
/// undo puts back the XML that was there to the byte, including anything the model did not understand.
/// </para>
/// <para>
/// An empty fragment list removes the element, which is what a sheet with no merges should have — an
/// empty <c>&lt;mergeCells count="0"/&gt;</c> is something Excel reports as a repair.
/// </para>
/// </remarks>
public sealed class ReplaceSheetElementsCommand(string sheetName, string localName, IReadOnlyList<string> fragments, string? name = null)
    : IEditCommand<Workbook>
{
    public string SheetName { get; } = sheetName;
    public string LocalName { get; } = localName;
    public IReadOnlyList<string> Fragments { get; } = fragments;

    public string Name { get; } = name ?? "Edit";

    public IEditCommand<Workbook> Apply(Workbook context)
    {
        var sheet = context[this.SheetName];
        var previous = sheet.ReadElements(this.LocalName);
        sheet.ReplaceElements(this.LocalName, this.Fragments);
        return new ReplaceSheetElementsCommand(this.SheetName, this.LocalName, previous, this.Name);
    }
}
