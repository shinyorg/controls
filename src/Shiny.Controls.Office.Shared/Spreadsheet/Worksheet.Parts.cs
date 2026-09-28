using System.Security;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing.Charts;
using DocumentFormat.OpenXml.Drawing.Spreadsheet;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Shiny.Controls.Office.Spreadsheet;

/// <summary>
/// The features a sheet keeps in lists and in parts of their own: filters, conditional formatting,
/// validation, hyperlinks, notes, tables and charts.
/// </summary>
/// <remarks>
/// Every reader here is cached against <see cref="Workbook.Revision"/>. The painter asks for all of them
/// every frame, and re-parsing a sheet's conditional formatting on every scroll would be the whole cost
/// of scrolling.
/// </remarks>
public sealed partial class Worksheet
{
    readonly Dictionary<string, (long Revision, object? Value)> features = new(StringComparer.Ordinal);

    T Feature<T>(string key, Func<T> build)
    {
        var revision = this.workbook.Revision;
        if (this.features.TryGetValue(key, out var entry) && entry.Revision == revision)
            return (T)entry.Value!;

        var value = build();
        this.features[key] = (revision, value);
        return value;
    }

    // ---- filters ----

    /// <summary>The sheet's own AutoFilter, or null when it has none.</summary>
    public SheetAutoFilter? AutoFilter
        => this.Feature("autoFilter", () => SheetAutoFilter.Parse(this.ReadElements("autoFilter").FirstOrDefault()));

    /// <summary>Every filter on the sheet — its own and each table's — for drawing the header arrows.</summary>
    public IReadOnlyList<SheetAutoFilter> AutoFilters
        => this.Feature("autoFilters", () =>
        {
            var list = new List<SheetAutoFilter>();
            if (this.AutoFilter is { } own)
                list.Add(own);

            list.AddRange(this.Tables.Select(x => x.AutoFilter).OfType<SheetAutoFilter>());
            return (IReadOnlyList<SheetAutoFilter>)list;
        });

    /// <summary>The filter whose header row holds <paramref name="cell"/>, if any.</summary>
    public SheetAutoFilter? FilterWithHeaderAt(CellRef cell)
        => this.AutoFilters.FirstOrDefault(x => x.Range.Top == cell.Row && cell.Column >= x.Range.Left && cell.Column <= x.Range.Right);

    /// <summary>The filter covering <paramref name="cell"/>, if any.</summary>
    public SheetAutoFilter? FilterAt(CellRef cell) => this.AutoFilters.FirstOrDefault(x => x.Range.Contains(cell));

    // ---- conditional formatting ----

    public IReadOnlyList<ConditionalFormat> ConditionalFormats
        => this.Feature("cf", () => (IReadOnlyList<ConditionalFormat>)this.ReadElements("conditionalFormatting")
            .Select(x => ConditionalFormatting.Parse(x, this.workbook.Styles))
            .OfType<ConditionalFormat>()
            .ToList());

    /// <summary>Evaluates the rules for the painter, with per-rule statistics cached until the next edit.</summary>
    public ConditionalFormatting.Evaluator ConditionalEvaluator
        => this.Feature("cfEval", () => new ConditionalFormatting.Evaluator(this, this.ConditionalFormats));

    // ---- validation ----

    public IReadOnlyList<DataValidationRule> Validations
        => this.Feature("dv", () => DataValidation.Parse(this.ReadElements("dataValidations").FirstOrDefault()));

    public DataValidationRule? ValidationAt(CellRef cell)
    {
        cell = cell.Relative();
        return this.Validations.LastOrDefault(x => x.Covers(cell));
    }

    // ---- tables ----

    public IReadOnlyList<SheetTable> Tables
        => this.Feature("tables", () => (IReadOnlyList<SheetTable>)this.part.TableDefinitionParts
            .Select(x => x.Table?.OuterXml)
            .OfType<string>()
            .Select(SheetTables.Parse)
            .OfType<SheetTable>()
            .ToList());

    public SheetTable? TableAt(CellRef cell) => this.Tables.FirstOrDefault(x => x.Range.Contains(cell.Relative()));

    internal IEnumerable<uint> TableIds()
        => this.part.TableDefinitionParts.Select(x => x.Table?.Id?.Value ?? 0u);

    TableDefinitionPart? TablePart(string name)
        => this.part.TableDefinitionParts.FirstOrDefault(x =>
            string.Equals(x.Table?.DisplayName?.Value, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.Table?.Name?.Value, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds a table part and its <c>&lt;tablePart&gt;</c> entry. Returns the table's name.</summary>
    internal string AddTablePart(string tableXml)
    {
        var tablePart = this.part.AddNewPart<TableDefinitionPart>();
        tablePart.Table = new Table(tableXml);
        var id = this.part.GetIdOfPart(tablePart);

        var root = this.SheetElement();
        var list = root.GetFirstChild<TableParts>() ?? SheetXml.Insert(root, new TableParts());
        list.AppendChild(new TablePart { Id = id });
        list.Count = (uint)list.Elements<TablePart>().Count();

        this.workbook.OnContentChanged();
        return tablePart.Table.DisplayName?.Value ?? tablePart.Table.Name?.Value ?? "Table";
    }

    /// <summary>Removes a table part, returning its XML.</summary>
    internal string? RemoveTablePart(string name)
    {
        if (this.TablePart(name) is not { } tablePart)
            return null;

        var xml = tablePart.Table?.OuterXml;
        var id = this.part.GetIdOfPart(tablePart);

        var list = this.SheetElement().GetFirstChild<TableParts>();
        list?.Elements<TablePart>().FirstOrDefault(x => x.Id?.Value == id)?.Remove();

        if (list is not null)
        {
            if (list.Elements<TablePart>().Any())
                list.Count = (uint)list.Elements<TablePart>().Count();
            else
                list.Remove();
        }

        this.part.DeletePart(tablePart);
        this.workbook.OnContentChanged();
        return xml;
    }

    /// <summary>A table's part XML, without touching it.</summary>
    internal string? TableXml(string name) => this.TablePart(name)?.Table?.OuterXml;

    internal string? ReadTableAutoFilter(string name)
        => this.TablePart(name)?.Table?.GetFirstChild<AutoFilter>()?.OuterXml;

    /// <summary>
    /// Replaces a table's own filter. A table always keeps an <c>autoFilter</c> — removing its criteria
    /// leaves the arrows, which is what Clear Filter means for a table.
    /// </summary>
    internal void WriteTableAutoFilter(string name, string? xml)
    {
        if (this.TablePart(name)?.Table is not { } table)
            return;

        var existing = table.GetFirstChild<AutoFilter>();
        var reference = table.Reference?.Value ?? existing?.Reference?.Value;

        var replacement = xml is null
            ? new AutoFilter { Reference = reference }
            : (AutoFilter)SheetXml.Parse(xml);

        if (existing is not null)
            table.ReplaceChild(replacement, existing);
        else
            table.InsertAt(replacement, 0);

        this.workbook.OnContentChanged();
    }

    // ---- hyperlinks ----

    public IReadOnlyList<CellHyperlink> Hyperlinks
        => this.Feature("links", () =>
        {
            var result = new List<CellHyperlink>();
            foreach (var link in this.SheetElement().GetFirstChild<Hyperlinks>()?.Elements<Hyperlink>() ?? [])
            {
                if (link.Reference?.Value is not { } reference || !CellRange.TryParse(reference, out var range))
                    continue;

                string? address = null;
                if (link.Id?.Value is { } rid)
                    address = this.part.HyperlinkRelationships.FirstOrDefault(x => x.Id == rid)?.Uri.OriginalString;

                // A ref may cover a range; every cell in it carries the link.
                foreach (var cell in range.CellCount <= 4096 ? range.Cells() : [range.TopLeft])
                {
                    result.Add(new CellHyperlink(cell)
                    {
                        Address = address,
                        Location = link.Location?.Value,
                        Display = link.Display?.Value,
                        Tooltip = link.Tooltip?.Value
                    });
                }
            }

            return (IReadOnlyList<CellHyperlink>)result;
        });

    public CellHyperlink? HyperlinkAt(CellRef cell)
    {
        cell = cell.Relative();
        return this.Hyperlinks.FirstOrDefault(x => x.Cell == cell);
    }

    /// <summary>Sets or removes a cell's link, returning what was there.</summary>
    internal CellHyperlink? WriteHyperlink(CellRef cell, CellHyperlink? link)
    {
        cell = cell.Relative();
        var previous = this.HyperlinkAt(cell);
        var root = this.SheetElement();
        var list = root.GetFirstChild<Hyperlinks>();

        // Remove whatever names exactly this cell. A link over a range that merely contains it is left:
        // splitting it is not something a single-cell edit should do.
        foreach (var existing in list?.Elements<Hyperlink>().ToList() ?? [])
        {
            if (existing.Reference?.Value is not { } reference || !CellRange.TryParse(reference, out var range) || range != new CellRange(cell))
                continue;

            if (existing.Id?.Value is { } rid && this.part.HyperlinkRelationships.Any(x => x.Id == rid))
                this.part.DeleteReferenceRelationship(rid);

            existing.Remove();
        }

        if (link is not null)
        {
            list ??= SheetXml.Insert(root, new Hyperlinks());

            var element = new Hyperlink { Reference = cell.ToString() };

            if (!string.IsNullOrEmpty(link.Address))
            {
                var uri = Uri.TryCreate(link.Address, UriKind.Absolute, out var absolute)
                    ? absolute
                    : new Uri(link.Address, UriKind.Relative);

                element.Id = this.part.AddHyperlinkRelationship(uri, true).Id;
            }

            if (!string.IsNullOrEmpty(link.Location))
                element.Location = link.Location;

            if (!string.IsNullOrEmpty(link.Display))
                element.Display = link.Display;

            if (!string.IsNullOrEmpty(link.Tooltip))
                element.Tooltip = link.Tooltip;

            list.AppendChild(element);
        }

        if (list is not null && !list.Elements<Hyperlink>().Any())
            list.Remove();

        this.workbook.OnContentChanged();
        return previous;
    }

    // ---- notes ----

    public IReadOnlyList<CellNote> Notes
        => this.Feature("notes", () =>
        {
            var comments = this.part.WorksheetCommentsPart?.Comments;
            if (comments is null)
                return (IReadOnlyList<CellNote>)[];

            var authors = comments.Authors?.Elements<Author>().Select(x => x.Text ?? string.Empty).ToList() ?? [];
            var result = new List<CellNote>();

            foreach (var comment in comments.CommentList?.Elements<Comment>() ?? [])
            {
                if (comment.Reference?.Value is not { } reference || !CellRef.TryParse(reference, out var cell))
                    continue;

                var author = comment.AuthorId?.Value is { } id && id < authors.Count ? authors[(int)id] : string.Empty;
                var text = string.Concat(comment.CommentText?.Descendants<DocumentFormat.OpenXml.Spreadsheet.Text>().Select(x => x.Text) ?? []);
                result.Add(new CellNote(cell.Relative(), text, author));
            }

            return result;
        });

    public CellNote? NoteAt(CellRef cell)
    {
        cell = cell.Relative();
        return this.Notes.FirstOrDefault(x => x.Cell == cell);
    }

    /// <summary>
    /// Sets or removes a cell's note, returning what was there.
    /// </summary>
    /// <remarks>
    /// Notes are two parts in the file, and Excel needs both: the comments part holds the text, and a
    /// VML drawing holds the yellow box Excel shows — a note with no VML shape is kept, but never
    /// displayed. Both are rewritten whole from the note list, which is what keeps their row and
    /// column references in step with each other.
    /// </remarks>
    internal CellNote? WriteNote(CellRef cell, CellNote? note)
    {
        cell = cell.Relative();
        var previous = this.NoteAt(cell);

        var notes = this.Notes.Where(x => x.Cell != cell).ToList();
        if (note is not null)
            notes.Add(note with { Cell = cell });

        notes = notes.OrderBy(x => x.Cell.Row).ThenBy(x => x.Cell.Column).ToList();

        var root = this.SheetElement();

        if (notes.Count == 0)
        {
            if (this.part.WorksheetCommentsPart is { } commentsPart)
                this.part.DeletePart(commentsPart);

            if (root.GetFirstChild<LegacyDrawing>() is { } legacy)
            {
                if (legacy.Id?.Value is { } rid && this.part.TryGetPartById(rid, out var vml))
                    this.part.DeletePart(vml);

                legacy.Remove();
            }

            this.workbook.OnContentChanged();
            return previous;
        }

        var authors = notes.Select(x => x.Author).Distinct(StringComparer.Ordinal).ToList();
        var comments = new Comments(
            new Authors(authors.Select(x => new Author(x))),
            new CommentList(notes.Select(x => new Comment(
                new CommentText(new Run(new DocumentFormat.OpenXml.Spreadsheet.Text(x.Text) { Space = SpaceProcessingModeValues.Preserve })))
            {
                Reference = x.Cell.ToString(),
                AuthorId = (uint)authors.IndexOf(x.Author)
            })));

        var part = this.part.WorksheetCommentsPart ?? this.part.AddNewPart<WorksheetCommentsPart>();
        part.Comments = comments;

        VmlDrawingPart? drawing = null;
        var legacyDrawing = root.GetFirstChild<LegacyDrawing>();
        if (legacyDrawing?.Id?.Value is { } existingId && this.part.TryGetPartById(existingId, out var found))
            drawing = found as VmlDrawingPart;

        if (drawing is null)
        {
            drawing = this.part.AddNewPart<VmlDrawingPart>();
            legacyDrawing?.Remove();
            SheetXml.Insert(root, new LegacyDrawing { Id = this.part.GetIdOfPart(drawing) });
        }

        var index = this.workbook.Sheets.ToList().IndexOf(this) + 1;
        using (var stream = drawing.GetStream(FileMode.Create, FileAccess.Write))
        {
            var bytes = Encoding.UTF8.GetBytes(NotesVml(notes, Math.Max(1, index)));
            stream.Write(bytes, 0, bytes.Length);
        }

        this.workbook.OnContentChanged();
        return previous;
    }

    /// <summary>The VML Excel needs to show each note: one hidden text-box shape per note.</summary>
    static string NotesVml(IReadOnlyList<CellNote> notes, int idmap)
    {
        var xml = new StringBuilder();
        xml.Append("<xml xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\">");
        xml.Append($"<o:shapelayout v:ext=\"edit\"><o:idmap v:ext=\"edit\" data=\"{idmap}\"/></o:shapelayout>");
        xml.Append("<v:shapetype id=\"_x0000_t202\" coordsize=\"21600,21600\" o:spt=\"202\" path=\"m,l,21600r21600,l21600,xe\">");
        xml.Append("<v:stroke joinstyle=\"miter\"/><v:path gradientshapeok=\"t\" o:connecttype=\"rect\"/></v:shapetype>");

        for (var i = 0; i < notes.Count; i++)
        {
            var cell = notes[i].Cell;
            var id = idmap * 1024 + 1 + i;

            xml.Append($"<v:shape id=\"_x0000_s{id}\" type=\"#_x0000_t202\" style=\"position:absolute;margin-left:59.25pt;margin-top:1.5pt;width:108pt;height:59.25pt;z-index:{i + 1};visibility:hidden\" fillcolor=\"#ffffe1\" o:insetmode=\"auto\">");
            xml.Append("<v:fill color2=\"#ffffe1\"/><v:shadow on=\"t\" color=\"black\" obscured=\"t\"/><v:path o:connecttype=\"none\"/>");
            xml.Append("<v:textbox style=\"mso-direction-alt:auto\"><div style=\"text-align:left\"></div></v:textbox>");
            xml.Append("<x:ClientData ObjectType=\"Note\"><x:MoveWithCells/><x:SizeWithCells/>");
            xml.Append($"<x:Anchor>{cell.Column + 1}, 15, {cell.Row}, 2, {cell.Column + 3}, 15, {cell.Row + 4}, 16</x:Anchor>");
            xml.Append($"<x:AutoFill>False</x:AutoFill><x:Row>{cell.Row}</x:Row><x:Column>{cell.Column}</x:Column></x:ClientData>");
            xml.Append("</v:shape>");
        }

        xml.Append("</xml>");
        return xml.ToString();
    }

    // ---- charts ----

    public IReadOnlyList<SheetChart> Charts
        => this.Feature("charts", () =>
        {
            var drawing = this.part.DrawingsPart;
            var root = drawing?.WorksheetDrawing;
            if (drawing is null || root is null)
                return (IReadOnlyList<SheetChart>)[];

            var result = new List<SheetChart>();
            foreach (var anchor in root.ChildElements.Where(x => x.LocalName == "twoCellAnchor"))
            {
                var element = XElement.Parse(anchor.OuterXml);
                if (ChartRelationshipId(element) is not { } rid)
                    continue;

                if (!drawing.TryGetPartById(rid, out var found) || found is not ChartPart chartPart || chartPart.ChartSpace is null)
                    continue;

                result.Add(SheetCharts.Parse(rid, SheetCharts.ParseAnchor(element), chartPart.ChartSpace.OuterXml));
            }

            return result;
        });

    static string? ChartRelationshipId(XElement anchor)
    {
        XNamespace r = SheetXml.RelationshipNamespace;
        return anchor.Descendants().FirstOrDefault(x => x.Name.LocalName == "chart")?.Attribute(r + "id")?.Value;
    }

    public SheetChart? ChartById(string id) => this.Charts.FirstOrDefault(x => x.Id == id);

    OpenXmlElement? AnchorOf(string chartId)
        => this.part.DrawingsPart?.WorksheetDrawing?.ChildElements
            .FirstOrDefault(x => x.LocalName == "twoCellAnchor" && ChartRelationshipId(XElement.Parse(x.OuterXml)) == chartId);

    /// <summary>Adds a chart part and its anchor, returning the chart's relationship id.</summary>
    internal string AddChart(string chartSpaceXml, string anchorXml, string? preferredId)
    {
        var drawing = this.part.DrawingsPart;
        if (drawing is null)
        {
            drawing = this.part.AddNewPart<DrawingsPart>();
            var wsDr = new WorksheetDrawing();
            wsDr.AddNamespaceDeclaration("xdr", SheetCharts.SpreadsheetDrawingNamespace);
            wsDr.AddNamespaceDeclaration("a", SheetCharts.DrawingNamespace);
            drawing.WorksheetDrawing = wsDr;

            var root = this.SheetElement();
            root.GetFirstChild<DocumentFormat.OpenXml.Spreadsheet.Drawing>()?.Remove();
            SheetXml.Insert(root, new DocumentFormat.OpenXml.Spreadsheet.Drawing { Id = this.part.GetIdOfPart(drawing) });
        }

        drawing.WorksheetDrawing ??= new WorksheetDrawing();

        ChartPart chartPart;
        if (preferredId is not null && !drawing.Parts.Any(x => x.RelationshipId == preferredId) && !drawing.ExternalRelationships.Any(x => x.Id == preferredId))
            chartPart = drawing.AddNewPart<ChartPart>(preferredId);
        else
            chartPart = drawing.AddNewPart<ChartPart>();

        chartPart.ChartSpace = new ChartSpace(chartSpaceXml);
        var rid = drawing.GetIdOfPart(chartPart);

        drawing.WorksheetDrawing.AppendChild(new TwoCellAnchor(anchorXml.Replace("{RID}", rid, StringComparison.Ordinal)));

        this.workbook.OnContentChanged();
        return rid;
    }

    /// <summary>Removes a chart, returning what undo needs to put it back.</summary>
    internal (string ChartSpaceXml, string AnchorXml)? RemoveChart(string chartId)
    {
        var drawing = this.part.DrawingsPart;
        if (drawing is null || this.AnchorOf(chartId) is not { } anchor)
            return null;

        if (!drawing.TryGetPartById(chartId, out var found) || found is not ChartPart chartPart)
            return null;

        var chartXml = chartPart.ChartSpace?.OuterXml ?? string.Empty;
        var anchorXml = anchor.OuterXml.Replace($"\"{chartId}\"", "\"{RID}\"", StringComparison.Ordinal);

        anchor.Remove();
        drawing.DeletePart(chartPart);

        this.workbook.OnContentChanged();
        return (chartXml, anchorXml);
    }

    /// <summary>Re-anchors a chart, returning where it was.</summary>
    internal ChartAnchor? MoveChart(string chartId, ChartAnchor anchor)
    {
        if (this.AnchorOf(chartId) is not TwoCellAnchor element)
            return null;

        var previous = SheetCharts.ParseAnchor(XElement.Parse(element.OuterXml));

        // FromMarker and ToMarker share a shape but no usable base type in the SDK, so the four children
        // are rewritten as a sequence - col, colOff, row, rowOff, the order CT_Marker demands.
        static void Write(OpenXmlCompositeElement? marker, CellRef cell, double dx, double dy)
        {
            if (marker is null)
                return;

            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            marker.RemoveAllChildren();
            marker.Append(
                new ColumnId(cell.Column.ToString(invariant)),
                new ColumnOffset(((long)Math.Round(dx * SheetCharts.EmuPerPixel)).ToString(invariant)),
                new RowId(cell.Row.ToString(invariant)),
                new RowOffset(((long)Math.Round(dy * SheetCharts.EmuPerPixel)).ToString(invariant)));
        }

        Write(element.FromMarker, anchor.From, anchor.FromDx, anchor.FromDy);
        Write(element.ToMarker, anchor.To, anchor.ToDx, anchor.ToDy);

        this.workbook.OnContentChanged();
        return previous;
    }

    /// <summary>The next free shape id in the sheet's drawing, for a new chart frame.</summary>
    internal uint NextDrawingShapeId()
    {
        var root = this.part.DrawingsPart?.WorksheetDrawing;
        if (root is null)
            return 2;

        var highest = root.Descendants()
            .Where(x => x.LocalName == "cNvPr")
            .Select(x => x.GetAttributes().FirstOrDefault(a => a.LocalName == "id").Value)
            .Select(x => uint.TryParse(x, out var v) ? v : 0u)
            .DefaultIfEmpty(1u)
            .Max();

        return highest + 1;
    }
}
