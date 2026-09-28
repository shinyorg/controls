using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Spreadsheet.View;
using Shiny.Controls.Office.Text;
using SkiaSharp;
using Shiny.Controls.Office.Theming;

namespace Shiny.Controls.Office.Skia;

/// <summary>
/// Everything the painter needs for one frame.
/// </summary>
public sealed record SpreadsheetPaintRequest
{
    public required Workbook Workbook { get; init; }
    public required Worksheet Sheet { get; init; }
    public required GridViewport Viewport { get; init; }
    public required SpreadsheetSelection Selection { get; init; }
    public SpreadsheetTheme Theme { get; init; } = SpreadsheetTheme.Light;

    /// <summary>Device pixels per logical pixel. The canvas is scaled by this before anything is drawn.</summary>
    public float Scale { get; init; } = 1f;

    /// <summary>
    /// The controller's zoom. Multiplied into <see cref="Scale"/>; the viewport is already sized in grid
    /// units, so nothing else changes.
    /// </summary>
    public float Zoom { get; init; } = 1f;

    /// <summary>Hides the active cell's content while an editor is overlaid on it.</summary>
    public CellRef? EditingCell { get; init; }

    /// <summary>
    /// The range a pending cut or copy was taken from, drawn with a dashed marching-ants border.
    /// </summary>
    /// <remarks>
    /// Comes from <c>SpreadsheetController.ClipboardRange</c>, which is already null when the capture
    /// belongs to another sheet — the painter draws whatever it is handed and does not check.
    /// </remarks>
    public CellRange? ClipboardRange { get; init; }

    /// <summary>
    /// Cells holding a find match, washed so the count in the toolbar has something to point at.
    /// </summary>
    public IReadOnlyList<CellRef> FindMatches { get; init; } = [];

    /// <summary>
    /// Draw the selection's grab handles, which are how a touch user extends a selection.
    /// </summary>
    public bool ShowTouchHandles { get; init; }

    /// <summary>A picture drawn behind the grid, under the cells and the rules.</summary>
    public OfficeWatermark? Watermark { get; init; }

    /// <summary>How far the dashes have marched, in pixels.</summary>
    public float ClipboardDashPhase { get; init; }

    /// <summary>The range a fill-handle drag is about to fill, outlined while the drag lasts.</summary>
    public CellRange? FillPreview { get; init; }

    /// <summary>The selected chart, drawn with its handles.</summary>
    public string? SelectedChartId { get; init; }

    /// <summary>A chart being dragged, drawn where it is going rather than where it is.</summary>
    public (string Id, ChartAnchor Anchor)? ChartPreview { get; init; }

    /// <summary>The notes to draw open — the one under the pointer, or all of them.</summary>
    public IReadOnlyList<CellNote> OpenNotes { get; init; } = [];

    /// <summary>Where the active cell's list arrow goes, in grid units, or null.</summary>
    public GridRect? ListButton { get; init; }

    /// <summary>The printed pages to outline — Page Layout and Page Break Preview. Null draws none.</summary>
    public SheetPageLayout? Pages { get; init; }

    /// <summary>How <see cref="Pages"/> is drawn: dashed edges, or Page Break Preview's solid ones.</summary>
    public SheetViewMode ViewMode { get; init; }

    /// <summary>Leaves out the selection, the list arrow and open notes — for printing and export.</summary>
    public bool PrintMode { get; init; }

    /// <summary>
    /// Builds the request for what a controller is showing, so both hosts ask for the same frame.
    /// </summary>
    public static SpreadsheetPaintRequest For(SpreadsheetController controller, SpreadsheetTheme theme, float scale)
    {
        ArgumentNullException.ThrowIfNull(controller);

        return new SpreadsheetPaintRequest
        {
            Workbook = controller.Workbook,
            Sheet = controller.Sheet,
            Viewport = controller.Viewport,
            Selection = controller.Selection,
            Theme = theme,
            Scale = scale,
            Zoom = (float)controller.Zoom,
            EditingCell = controller.EditingCell,
            ClipboardRange = controller.ClipboardRange,
            FindMatches = controller.FindMatchCells(),
            ShowTouchHandles = controller.UsesTouch,
            FillPreview = controller.FillPreview,
            SelectedChartId = controller.SelectedChartId,
            ChartPreview = controller.ChartPreview,
            OpenNotes = controller.VisibleNotes,
            ListButton = controller.ListButtonRect,
            Pages = controller.PageLayout,
            ViewMode = controller.ViewMode
        };
    }
}

/// <summary>
/// Draws the spreadsheet grid onto an <see cref="SKCanvas"/>.
/// </summary>
/// <remarks>
/// This is the single paint routine both hosts use: MAUI hands it a Skia-backed drawable and Blazor
/// hands it an <c>SKCanvasView</c> surface. Keeping it here rather than in either host package is what
/// makes the two genuinely the same renderer instead of two implementations kept in step by hand.
/// </remarks>
public sealed class SpreadsheetPainter : IDisposable
{
    readonly SkiaTextMeasurer measurer;
    readonly bool ownsMeasurer;
    readonly ChartPainter charts;
    readonly SKPaint fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    readonly SKPaint stroke = new() { IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
    readonly Dictionary<string, TableStylePalette> tablePalettes = new(StringComparer.OrdinalIgnoreCase);

    public SpreadsheetPainter()
        : this(null)
    {
    }

    /// <summary>
    /// Shares a measurer with the rest of the app, rather than resolving fonts on its own.
    /// </summary>
    public SpreadsheetPainter(SkiaTextMeasurer? measurer)
    {
        this.ownsMeasurer = measurer is null;
        this.measurer = measurer ?? new SkiaTextMeasurer();
        this.charts = new ChartPainter(this.measurer);
    }

    /// <summary>The application-supplied faces this painter resolves against.</summary>
    public OfficeFontRegistry Fonts => this.measurer.Registry;

    public void Paint(SKCanvas canvas, SpreadsheetPaintRequest request)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(request);

        canvas.Save();
        canvas.Scale(request.Scale * Math.Max(0.05f, request.Zoom));

        var viewport = request.Viewport;
        var theme = request.Theme;

        canvas.Clear(ToSk(theme.Background));

        WatermarkPainter.Draw(
            canvas,
            new SKRect(0, 0, (float)viewport.Width, (float)viewport.Height),
            request.Watermark);

        var (firstColumn, lastColumn) = viewport.VisibleColumns();
        var (firstRow, lastRow) = viewport.VisibleRows();
        var frozen = viewport.Metrics.FrozenPane;

        // Four panes, each clipped to its own band so a scrolled cell cannot paint over a pinned one.
        this.PaintPane(canvas, request, frozen.Column, lastColumn, frozen.Row, lastRow, firstColumn, firstRow, PaneKind.Scrollable);

        if (frozen.Column > 0)
            this.PaintPane(canvas, request, 0, frozen.Column - 1, frozen.Row, lastRow, 0, firstRow, PaneKind.FrozenColumns);

        if (frozen.Row > 0)
            this.PaintPane(canvas, request, frozen.Column, lastColumn, 0, frozen.Row - 1, firstColumn, 0, PaneKind.FrozenRows);

        if (frozen.Column > 0 && frozen.Row > 0)
            this.PaintPane(canvas, request, 0, frozen.Column - 1, 0, frozen.Row - 1, 0, 0, PaneKind.Corner);

        this.PaintCharts(canvas, request);

        if (!request.PrintMode)
        {
            this.PaintListButton(canvas, request);
            this.PaintOpenNotes(canvas, request);
        }

        this.PaintPages(canvas, request);

        this.PaintHeaders(canvas, request, firstColumn, lastColumn, firstRow, lastRow);
        this.PaintFrozenDividers(canvas, request);

        canvas.Restore();
    }

    void PaintPane(
        SKCanvas canvas,
        SpreadsheetPaintRequest request,
        int columnStart,
        int columnEnd,
        int rowStart,
        int rowEnd,
        int firstColumn,
        int firstRow,
        PaneKind pane)
    {
        var viewport = request.Viewport;
        var metrics = viewport.Metrics;

        var clipLeft = pane.HasFlag(PaneKind.FrozenColumns) ? metrics.RowHeaderWidth : viewport.ContentOriginX;
        var clipRight = pane.HasFlag(PaneKind.FrozenColumns) ? viewport.ContentOriginX : viewport.Width;
        var clipTop = pane.HasFlag(PaneKind.FrozenRows) ? metrics.ColumnHeaderHeight : viewport.ContentOriginY;
        var clipBottom = pane.HasFlag(PaneKind.FrozenRows) ? viewport.ContentOriginY : viewport.Height;

        if (clipRight <= clipLeft || clipBottom <= clipTop)
            return;

        var actualColumnStart = pane.HasFlag(PaneKind.FrozenColumns) ? columnStart : firstColumn;
        var actualRowStart = pane.HasFlag(PaneKind.FrozenRows) ? rowStart : firstRow;

        canvas.Save();
        canvas.ClipRect(new SKRect((float)clipLeft, (float)clipTop, (float)clipRight, (float)clipBottom));

        // Gridlines first, so a filled cell covers them the way it does in Excel.
        if (request.Sheet.ShowGridLines)
            this.PaintGridLines(canvas, request, actualColumnStart, columnEnd, actualRowStart, rowEnd);

        this.PaintCells(canvas, request, actualColumnStart, columnEnd, actualRowStart, rowEnd);
        this.PaintBorders(canvas, request, actualColumnStart, columnEnd, actualRowStart, rowEnd);
        // A printed page carries the cells and their borders, not the editing furniture drawn over them.
        if (!request.PrintMode)
        {
            this.PaintNoteMarkers(canvas, request, actualColumnStart, columnEnd, actualRowStart, rowEnd);
            this.PaintFilterButtons(canvas, request, actualColumnStart, columnEnd, actualRowStart, rowEnd);
            this.PaintFindMatches(canvas, request, actualColumnStart, columnEnd, actualRowStart, rowEnd);
            this.PaintSelection(canvas, request, actualColumnStart, columnEnd, actualRowStart, rowEnd);
            this.PaintFillPreview(canvas, request);
            this.PaintClipboardMarquee(canvas, request, actualColumnStart, columnEnd, actualRowStart, rowEnd);
        }

        canvas.Restore();
    }

    // ---- cells ----

    void PaintCells(SKCanvas canvas, SpreadsheetPaintRequest request, int columnStart, int columnEnd, int rowStart, int rowEnd)
    {
        var viewport = request.Viewport;
        var sheet = request.Sheet;
        var styles = request.Workbook.Styles;
        var merges = sheet.MergedRanges;
        var conditional = sheet.ConditionalEvaluator;
        var tables = sheet.Tables;

        for (var row = rowStart; row <= rowEnd; row++)
        {
            if (viewport.Metrics.Rows.IsHidden(row))
                continue;

            for (var column = columnStart; column <= columnEnd; column++)
            {
                if (viewport.Metrics.Columns.IsHidden(column))
                    continue;

                var cell = new CellRef(column, row);

                // A merged region paints once, across its whole span, from the first of its cells this
                // pane can see - which is its anchor unless the anchor has scrolled away.
                CellRange? merge = null;
                foreach (var m in merges)
                {
                    if (m.Contains(cell))
                    {
                        merge = m;
                        break;
                    }
                }

                if (merge is { } region && cell != new CellRef(Math.Max(region.Left, columnStart), Math.Max(region.Top, rowStart)))
                    continue;

                var source = merge?.TopLeft ?? cell;
                var bounds = merge is { } span ? viewport.RangeRect(span) : viewport.CellRect(cell);
                var format = this.FormatOf(request, source, styles, conditional, tables, out var bar);

                this.PaintCell(canvas, request, source, bounds, format, bar, merge is not null, columnEnd);
            }
        }
    }

    /// <summary>
    /// The format a cell is drawn with: its own, over its table's style, under its conditional format.
    /// </summary>
    ResolvedFormat FormatOf(
        SpreadsheetPaintRequest request,
        CellRef cell,
        StyleResolver styles,
        ConditionalFormatting.Evaluator conditional,
        IReadOnlyList<SheetTable> tables,
        out DataBarFill? bar)
    {
        var format = styles.Resolve(request.Sheet.GetEffectiveStyleIndex(cell));
        bar = null;

        foreach (var table in tables)
        {
            if (!table.Range.Contains(cell))
                continue;

            var palette = this.PaletteOf(table.StyleName, styles);
            var layer = palette.FormatAt(table, cell);

            // The table style sits under the cell's own formatting: an explicit fill or font colour wins.
            format = format with
            {
                Background = format.Background.IsTransparent && layer.Background is { } background ? background : format.Background,
                Foreground = format.Foreground == ResolvedFormat.Default.Foreground && layer.Foreground is { } ink ? ink : format.Foreground,
                Bold = format.Bold || layer.Bold == true,
                Borders = format.Borders.IsEmpty && layer.Borders is { IsEmpty: false } rule ? rule : format.Borders
            };

            break;
        }

        if (!conditional.IsEmpty)
        {
            var result = conditional.Evaluate(cell);
            if (!result.IsEmpty)
            {
                if (result.ScaleFill is { } scale)
                    format = format with { Background = scale };

                if (result.Overlay is { } overlay)
                    format = overlay.ApplyTo(format);

                bar = result.Bar;
            }
        }

        return format;
    }

    TableStylePalette PaletteOf(string style, StyleResolver styles)
    {
        if (!this.tablePalettes.TryGetValue(style, out var palette))
        {
            palette = TableStylePalette.For(style, styles.ThemeColor);
            this.tablePalettes[style] = palette;
        }

        return palette;
    }

    void PaintCell(SKCanvas canvas, SpreadsheetPaintRequest request, CellRef cell, GridRect bounds, ResolvedFormat format, DataBarFill? bar, bool merged, int columnEnd)
    {
        var rect = ToSk(bounds);

        if (!format.Background.IsTransparent)
        {
            this.fill.Color = ToSk(format.Background);
            canvas.DrawRect(rect, this.fill);
        }
        else if (merged && request.Sheet.ShowGridLines)
        {
            // A merge has no gridlines inside it. Inset by one so its own outline survives.
            this.fill.Color = ToSk(request.Theme.Background);
            canvas.DrawRect(new SKRect(rect.Left + 1, rect.Top + 1, rect.Right, rect.Bottom), this.fill);
        }

        if (bar is { } dataBar)
            this.PaintDataBar(canvas, rect, dataBar);

        if (request.EditingCell == cell)
            return;

        var sheet = request.Sheet;
        var styles = request.Workbook.Styles;
        string text;
        CellValueKind kind;

        if (sheet.ShowFormulas && sheet.GetFormula(cell) is { } formula)
        {
            text = "=" + formula;
            kind = CellValueKind.Text;
        }
        else
        {
            var value = sheet.GetDisplayValue(cell);
            if (value.IsBlank)
                return;

            text = styles.Format(value, format);
            kind = value.Kind;
        }

        if (text.Length == 0)
            return;

        var theme = request.Theme;
        var font = this.GetFont(format.FontName, (float)format.FontSize, format.Bold, format.Italic);
        var ink = ToSk(CellInk(format, theme));
        this.fill.Color = ink;

        var padding = (float)theme.CellPadding;
        var indent = (float)(format.Indent * theme.IndentWidth);
        var alignment = sheet.ShowFormulas && kind == CellValueKind.Text ? CellHorizontalAlignment.Left : format.EffectiveAlignment(kind);
        var metrics = font.Metrics;

        if (format.WrapText)
        {
            this.PaintWrapped(canvas, rect, text, font, format, alignment, padding, indent);
            return;
        }

        var measured = font.MeasureText(text);

        // A number that does not fit its column shows as ####, as in Excel — clipping the digits would
        // show a different number. General numbers are already shortened by the formatter instead.
        if (kind == CellValueKind.Number && measured > rect.Width - padding * 2 && !string.IsNullOrEmpty(format.NumberFormatCode) && format.NumberFormatCode != "General")
        {
            var hash = font.MeasureText("#");
            var count = Math.Max(1, (int)((rect.Width - padding * 2) / Math.Max(1, hash)));
            text = new string('#', count);
            measured = font.MeasureText(text);
        }

        var x = alignment switch
        {
            CellHorizontalAlignment.Right => rect.Right - padding - measured - indent,
            CellHorizontalAlignment.Center or CellHorizontalAlignment.CenterContinuous => rect.MidX - measured / 2,
            _ => rect.Left + padding + indent
        };

        var y = format.VerticalAlignment switch
        {
            CellVerticalAlignment.Top => rect.Top + padding - metrics.Ascent,
            CellVerticalAlignment.Center => rect.MidY - (metrics.Ascent + metrics.Descent) / 2,
            _ => rect.Bottom - padding - metrics.Descent
        };

        // Left-aligned text runs on over empty neighbours, as it does in Excel; anything else is kept
        // to its own cell.
        var clip = rect;
        if (!merged && kind == CellValueKind.Text && alignment == CellHorizontalAlignment.Left && x + measured > rect.Right)
            clip.Right = this.OverflowRight(request, cell, rect.Right, x + measured, columnEnd);

        canvas.Save();
        canvas.ClipRect(clip);
        canvas.DrawText(text, x, y, SKTextAlign.Left, font, this.fill);

        if (format.Underline)
            this.Rule(canvas, x, y + Math.Max(1.5f, metrics.UnderlinePosition ?? 1.5f), measured, ink, font.Size / 14f);

        if (format.Strike)
            this.Rule(canvas, x, y - font.Size * 0.3f, measured, ink, font.Size / 14f);

        canvas.Restore();
    }

    void Rule(SKCanvas canvas, float x, float y, float width, SKColor color, float thickness)
    {
        this.stroke.Color = color;
        this.stroke.StrokeWidth = Math.Max(1, thickness);
        canvas.DrawLine(x, y, x + width, y, this.stroke);
        this.stroke.StrokeWidth = 1;
    }

    /// <summary>How far right text starting in <paramref name="cell"/> may run: across blank neighbours only.</summary>
    float OverflowRight(SpreadsheetPaintRequest request, CellRef cell, float right, float needed, int columnEnd)
    {
        var sheet = request.Sheet;
        var viewport = request.Viewport;

        for (var column = cell.Column + 1; column <= Math.Min(columnEnd + 1, CellRef.MaxColumn) && right < needed; column++)
        {
            var next = new CellRef(column, cell.Row);
            if (!sheet.GetValue(next).IsBlank || sheet.GetFormula(next) is not null || sheet.MergeAt(next) is not null)
                break;

            right = (float)viewport.CellRect(next).Right;
        }

        return right;
    }

    void PaintWrapped(SKCanvas canvas, SKRect rect, string text, SKFont font, ResolvedFormat format, CellHorizontalAlignment alignment, float padding, float indent)
    {
        var width = Math.Max(4, rect.Width - padding * 2 - indent);
        var lines = new List<string>();

        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = string.Empty;
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && font.MeasureText(candidate) > width)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = candidate;
                }
            }

            lines.Add(line);
        }

        var metrics = font.Metrics;
        var lineHeight = font.Spacing;
        var total = lineHeight * lines.Count;

        var top = format.VerticalAlignment switch
        {
            CellVerticalAlignment.Top => rect.Top + padding,
            CellVerticalAlignment.Center => rect.MidY - total / 2,
            _ => rect.Bottom - padding - total
        };

        canvas.Save();
        canvas.ClipRect(rect);

        for (var i = 0; i < lines.Count; i++)
        {
            var measured = font.MeasureText(lines[i]);
            var x = alignment switch
            {
                CellHorizontalAlignment.Right => rect.Right - padding - measured,
                CellHorizontalAlignment.Center or CellHorizontalAlignment.CenterContinuous => rect.MidX - measured / 2,
                _ => rect.Left + padding + indent
            };

            canvas.DrawText(lines[i], x, top + lineHeight * i - metrics.Ascent, SKTextAlign.Left, font, this.fill);
        }

        canvas.Restore();
    }

    /// <summary>A data bar: Excel 2010's gradient, from the colour into white, with a solid edge.</summary>
    void PaintDataBar(SKCanvas canvas, SKRect rect, DataBarFill bar)
    {
        var inset = new SKRect(rect.Left + 2, rect.Top + 2, rect.Right - 2, rect.Bottom - 2);
        if (inset.Width <= 0 || inset.Height <= 0)
            return;

        var right = inset.Left + (float)(inset.Width * Math.Clamp(bar.Fraction, 0, 1));
        var color = new SKColor(bar.Color.R, bar.Color.G, bar.Color.B);
        var barRect = new SKRect(inset.Left, inset.Top, right, inset.Bottom);

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(barRect.Left, 0),
            new SKPoint(Math.Max(barRect.Left + 1, barRect.Right), 0),
            [color, new SKColor(255, 255, 255)],
            SKShaderTileMode.Clamp);

        this.fill.Shader = shader;
        canvas.DrawRect(barRect, this.fill);
        this.fill.Shader = null;

        this.stroke.Color = color;
        canvas.DrawRect(barRect, this.stroke);
    }

    /// <summary>
    /// The colour a cell's text is painted in.
    /// </summary>
    /// <remarks>
    /// Text nobody coloured takes the theme's ink, which in a dark theme is near-white. On a cell the
    /// author filled - a light header band, say - that is white on a light fill, so the theme's ink is
    /// made to read against the fill it actually sits on. A colour the author chose alongside their own
    /// fill is their pairing and is left exactly as authored.
    /// </remarks>
    internal static ArgbColor CellInk(ResolvedFormat format, SpreadsheetTheme theme)
    {
        if (format.Foreground != ResolvedFormat.Default.Foreground)
            return format.Foreground;

        return format.Background.IsTransparent
            ? theme.CellText
            : InkContrast.Legible(theme.CellText, InkContrast.Over(format.Background, theme.Background));
    }

    // ---- borders ----

    void PaintBorders(SKCanvas canvas, SpreadsheetPaintRequest request, int columnStart, int columnEnd, int rowStart, int rowEnd)
    {
        var viewport = request.Viewport;
        var sheet = request.Sheet;
        var styles = request.Workbook.Styles;
        var conditional = sheet.ConditionalEvaluator;
        var tables = sheet.Tables;

        // One row and one column beyond the visible edge: a neighbour's edge is drawn on the shared line.
        for (var row = Math.Max(0, rowStart - 1); row <= Math.Min(CellRef.MaxRow, rowEnd); row++)
        {
            if (viewport.Metrics.Rows.IsHidden(row))
                continue;

            for (var column = Math.Max(0, columnStart - 1); column <= Math.Min(CellRef.MaxColumn, columnEnd); column++)
            {
                if (viewport.Metrics.Columns.IsHidden(column))
                    continue;

                var cell = new CellRef(column, row);
                var format = this.FormatOf(request, cell, styles, conditional, tables, out _);
                if (format.Borders.IsEmpty)
                    continue;

                var rect = ToSk(sheet.MergeAt(cell) is { } merge ? viewport.RangeRect(merge) : viewport.CellRect(cell));
                var borders = format.Borders;

                if (borders.Top is { } top)
                    this.Edge(canvas, request, top, rect.Left, rect.Top, rect.Right, rect.Top);

                if (borders.Bottom is { } bottom)
                    this.Edge(canvas, request, bottom, rect.Left, rect.Bottom, rect.Right, rect.Bottom);

                if (borders.Left is { } left)
                    this.Edge(canvas, request, left, rect.Left, rect.Top, rect.Left, rect.Bottom);

                if (borders.Right is { } right)
                    this.Edge(canvas, request, right, rect.Right, rect.Top, rect.Right, rect.Bottom);
            }
        }
    }

    void Edge(SKCanvas canvas, SpreadsheetPaintRequest request, BorderEdge edge, float x1, float y1, float x2, float y2)
    {
        var color = edge.Color.IsTransparent ? request.Theme.CellText : edge.Color;
        this.stroke.Color = ToSk(color);
        this.stroke.StrokeWidth = (float)(edge.Style == CellBorderStyle.Double ? 1 : edge.Width);

        var horizontal = Math.Abs(y1 - y2) < 0.01;

        // Pixel-aligned: a 1px line on the half pixel, a 2px line on the whole one.
        var offset = this.stroke.StrokeWidth % 2 == 1 ? 0.5f : 0f;
        if (horizontal)
        {
            y1 = (float)Math.Floor(y1) + offset;
            y2 = y1;
        }
        else
        {
            x1 = (float)Math.Floor(x1) + offset;
            x2 = x1;
        }

        float[]? dash = edge.Style switch
        {
            CellBorderStyle.Dashed or CellBorderStyle.MediumDashed => [4, 2],
            CellBorderStyle.Dotted or CellBorderStyle.Hair => [1, 1],
            CellBorderStyle.DashDot or CellBorderStyle.MediumDashDot or CellBorderStyle.SlantDashDot => [5, 2, 1, 2],
            CellBorderStyle.DashDotDot or CellBorderStyle.MediumDashDotDot => [5, 2, 1, 2, 1, 2],
            _ => null
        };

        using var effect = dash is null ? null : SKPathEffect.CreateDash(dash, 0);
        this.stroke.PathEffect = effect;

        if (edge.Style == CellBorderStyle.Double)
        {
            // Two hairlines a pixel apart, straddling the cell edge.
            if (horizontal)
            {
                canvas.DrawLine(x1, y1 - 1, x2, y2 - 1, this.stroke);
                canvas.DrawLine(x1, y1 + 1, x2, y2 + 1, this.stroke);
            }
            else
            {
                canvas.DrawLine(x1 - 1, y1, x2 - 1, y2, this.stroke);
                canvas.DrawLine(x1 + 1, y1, x2 + 1, y2, this.stroke);
            }
        }
        else
        {
            canvas.DrawLine(x1, y1, x2, y2, this.stroke);
        }

        this.stroke.PathEffect = null;
        this.stroke.StrokeWidth = 1;
    }

    void PaintGridLines(SKCanvas canvas, SpreadsheetPaintRequest request, int columnStart, int columnEnd, int rowStart, int rowEnd)
    {
        var viewport = request.Viewport;
        this.stroke.Color = ToSk(request.Theme.GridLine);
        this.stroke.StrokeWidth = 1;

        for (var column = columnStart; column <= columnEnd + 1; column++)
        {
            var bounds = viewport.CellRect(new CellRef(Math.Min(column, CellRef.MaxColumn), rowStart));

            // Half-pixel offset keeps a 1px line on a device pixel instead of blurring across two.
            var x = (float)Math.Floor(column > CellRef.MaxColumn ? bounds.Right : bounds.X) + 0.5f;
            canvas.DrawLine(x, (float)viewport.Metrics.ColumnHeaderHeight, x, (float)viewport.Height, this.stroke);
        }

        for (var row = rowStart; row <= rowEnd + 1; row++)
        {
            var bounds = viewport.CellRect(new CellRef(columnStart, Math.Min(row, CellRef.MaxRow)));
            var y = (float)Math.Floor(row > CellRef.MaxRow ? bounds.Bottom : bounds.Y) + 0.5f;
            canvas.DrawLine((float)viewport.Metrics.RowHeaderWidth, y, (float)viewport.Width, y, this.stroke);
        }
    }

    // ---- markers ----

    /// <summary>The red triangle in the top-right corner of a cell with a note.</summary>
    void PaintNoteMarkers(SKCanvas canvas, SpreadsheetPaintRequest request, int columnStart, int columnEnd, int rowStart, int rowEnd)
    {
        var notes = request.Sheet.Notes;
        if (notes.Count == 0)
            return;

        this.fill.Color = new SKColor(0xE0, 0x1F, 0x1F);

        foreach (var note in notes)
        {
            if (note.Cell.Column < columnStart || note.Cell.Column > columnEnd || note.Cell.Row < rowStart || note.Cell.Row > rowEnd)
                continue;

            // A hidden (filtered-out) row or column has no height or width, and its marker would land
            // on the corner of the next visible cell - flagging a note that cell does not have.
            if (request.Viewport.Metrics.Rows.IsHidden(note.Cell.Row) || request.Viewport.Metrics.Columns.IsHidden(note.Cell.Column))
                continue;

            var rect = ToSk(request.Viewport.CellRect(note.Cell));
            using var path = new SKPath();
            path.MoveTo(rect.Right - 6, rect.Top + 1);
            path.LineTo(rect.Right - 1, rect.Top + 1);
            path.LineTo(rect.Right - 1, rect.Top + 6);
            path.Close();
            canvas.DrawPath(path, this.fill);
        }
    }

    /// <summary>The dropdown arrows along a filter's header row — and a funnel where a column is filtered.</summary>
    void PaintFilterButtons(SKCanvas canvas, SpreadsheetPaintRequest request, int columnStart, int columnEnd, int rowStart, int rowEnd)
    {
        var filters = request.Sheet.AutoFilters;
        if (filters.Count == 0)
            return;

        foreach (var filter in filters)
        {
            var row = filter.Range.Top;
            if (row < rowStart || row > rowEnd || request.Viewport.Metrics.Rows.IsHidden(row))
                continue;

            for (var column = Math.Max(filter.Range.Left, columnStart); column <= Math.Min(filter.Range.Right, columnEnd); column++)
            {
                if (request.Viewport.Metrics.Columns.IsHidden(column))
                    continue;

                var cell = request.Viewport.CellRect(new CellRef(column, row));
                var size = (float)Math.Min(SpreadsheetController.FilterButtonSize, cell.Height - 2);
                var rect = new SKRect((float)cell.Right - size - 1, (float)cell.Bottom - size - 1, (float)cell.Right - 1, (float)cell.Bottom - 1);

                this.DropButton(canvas, request.Theme, rect, filtered: filter.For(column) is not null);
            }
        }
    }

    void DropButton(SKCanvas canvas, SpreadsheetTheme theme, SKRect rect, bool filtered)
    {
        this.fill.Color = ToSk(theme.HeaderBackground);
        canvas.DrawRect(rect, this.fill);

        this.stroke.Color = ToSk(theme.HeaderBorder);
        canvas.DrawRect(new SKRect(rect.Left + 0.5f, rect.Top + 0.5f, rect.Right - 0.5f, rect.Bottom - 0.5f), this.stroke);

        this.fill.Color = ToSk(theme.HeaderText);
        using var path = new SKPath();

        if (filtered)
        {
            // A funnel over a small arrow: the column is filtered.
            var cx = rect.MidX - 2;
            path.MoveTo(cx - 4, rect.Top + 4);
            path.LineTo(cx + 4, rect.Top + 4);
            path.LineTo(cx + 1, rect.Top + 8);
            path.LineTo(cx + 1, rect.Bottom - 4);
            path.LineTo(cx - 1, rect.Bottom - 5);
            path.LineTo(cx - 1, rect.Top + 8);
            path.Close();
            canvas.DrawPath(path, this.fill);

            using var arrow = new SKPath();
            arrow.MoveTo(rect.Right - 6, rect.Bottom - 6);
            arrow.LineTo(rect.Right - 2, rect.Bottom - 6);
            arrow.LineTo(rect.Right - 4, rect.Bottom - 3.5f);
            arrow.Close();
            canvas.DrawPath(arrow, this.fill);
            return;
        }

        path.MoveTo(rect.MidX - 3.5f, rect.MidY - 1.5f);
        path.LineTo(rect.MidX + 3.5f, rect.MidY - 1.5f);
        path.LineTo(rect.MidX, rect.MidY + 2.5f);
        path.Close();
        canvas.DrawPath(path, this.fill);
    }

    void PaintListButton(SKCanvas canvas, SpreadsheetPaintRequest request)
    {
        if (request.ListButton is not { } button)
            return;

        canvas.Save();
        canvas.ClipRect(ContentRect(request));
        this.DropButton(canvas, request.Theme, ToSk(button), filtered: false);
        canvas.Restore();
    }

    /// <summary>
    /// Washes the cells a search matched, whole cells rather than the matched characters.
    /// </summary>
    void PaintFindMatches(SKCanvas canvas, SpreadsheetPaintRequest request, int columnStart, int columnEnd, int rowStart, int rowEnd)
    {
        if (request.FindMatches.Count == 0)
            return;

        this.fill.Color = ToSk(request.Theme.FindMatchFill);

        foreach (var cell in request.FindMatches)
        {
            if (cell.Column < columnStart || cell.Column > columnEnd || cell.Row < rowStart || cell.Row > rowEnd)
                continue;

            canvas.DrawRect(ToSk(request.Viewport.CellRect(cell)), this.fill);
        }
    }

    // ---- selection ----

    /// <summary>The selection grown to take in every merge it touches — a merged cell is selected whole.</summary>
    static CellRange Expanded(CellRange range, IReadOnlyList<CellRange> merges)
    {
        if (merges.Count == 0)
            return range;

        bool grew;
        do
        {
            grew = false;
            foreach (var merge in merges)
            {
                if (!merge.Intersects(range))
                    continue;

                var union = new CellRange(
                    new CellRef(Math.Min(range.Left, merge.Left), Math.Min(range.Top, merge.Top)),
                    new CellRef(Math.Max(range.Right, merge.Right), Math.Max(range.Bottom, merge.Bottom)));

                if (union != range)
                {
                    range = union;
                    grew = true;
                }
            }
        }
        while (grew);

        return range;
    }

    void PaintSelection(SKCanvas canvas, SpreadsheetPaintRequest request, int columnStart, int columnEnd, int rowStart, int rowEnd)
    {
        if (request.SelectedChartId is not null)
            return;

        var merges = request.Sheet.MergedRanges;
        var range = Expanded(request.Selection.Range, merges);
        if (range.Left > columnEnd || range.Right < columnStart || range.Top > rowEnd || range.Bottom < rowStart)
            return;

        var viewport = request.Viewport;
        var theme = request.Theme;
        var rect = ToSk(viewport.RangeRect(range));

        var active = request.Selection.Active;
        var activeMerge = request.Sheet.MergeAt(active);
        var singleArea = activeMerge is { } am ? am == range : range.IsSingleCell;

        if (!singleArea)
        {
            this.fill.Color = ToSk(theme.SelectionFill);
            canvas.DrawRect(rect, this.fill);

            // The active cell stays clear inside the wash, as in Excel.
            var activeRect = ToSk(activeMerge is { } merge ? viewport.RangeRect(merge) : viewport.CellRect(active));
            var format = request.Workbook.Styles.Resolve(request.Sheet.GetEffectiveStyleIndex(activeMerge?.TopLeft ?? active));
            this.fill.Color = ToSk(format.Background.IsTransparent ? theme.Background : format.Background);
            canvas.Save();
            canvas.ClipRect(activeRect);
            canvas.DrawRect(new SKRect(activeRect.Left + 1, activeRect.Top + 1, activeRect.Right - 1, activeRect.Bottom - 1), this.fill);
            canvas.Restore();

            this.PaintCell(canvas, request, activeMerge?.TopLeft ?? active, activeMerge is { } m2 ? viewport.RangeRect(m2) : viewport.CellRect(active), format, null, activeMerge is not null, active.Column);
        }

        this.stroke.Color = ToSk(theme.SelectionBorder);
        this.stroke.StrokeWidth = (float)theme.SelectionBorderWidth;
        canvas.DrawRect(rect, this.stroke);
        this.stroke.StrokeWidth = 1;

        if (request.ShowTouchHandles)
        {
            this.PaintTouchHandles(canvas, request, rect);
            return;
        }

        // The fill handle sits on the outside corner, the way Excel draws it.
        var handle = (float)theme.FillHandleSize;
        this.fill.Color = ToSk(theme.Background);
        canvas.DrawRect(new SKRect(rect.Right - handle / 2 - 1, rect.Bottom - handle / 2 - 1, rect.Right + handle / 2 + 1, rect.Bottom + handle / 2 + 1), this.fill);
        this.fill.Color = ToSk(theme.SelectionBorder);
        canvas.DrawRect(new SKRect(rect.Right - handle / 2, rect.Bottom - handle / 2, rect.Right + handle / 2, rect.Bottom + handle / 2), this.fill);
    }

    /// <summary>Draws the two round handles a finger drags to extend the selection.</summary>
    void PaintTouchHandles(SKCanvas canvas, SpreadsheetPaintRequest request, SKRect rect)
    {
        var theme = request.Theme;
        var radius = (float)theme.TouchHandleRadius;

        this.fill.Color = ToSk(theme.SelectionBorder);
        this.stroke.Color = ToSk(theme.TouchHandleRing);
        this.stroke.StrokeWidth = (float)theme.TouchHandleRingWidth;

        foreach (var (cx, cy) in new[] { (rect.Left, rect.Top), (rect.Right, rect.Bottom) })
        {
            canvas.DrawCircle(cx, cy, radius, this.fill);
            canvas.DrawCircle(cx, cy, radius, this.stroke);
        }

        this.stroke.StrokeWidth = 1;
    }

    /// <summary>The grey dashed outline of where a fill-handle drag will fill.</summary>
    void PaintFillPreview(SKCanvas canvas, SpreadsheetPaintRequest request)
    {
        if (request.FillPreview is not { } range || range == request.Selection.Range)
            return;

        var rect = ToSk(request.Viewport.RangeRect(range));
        using var effect = SKPathEffect.CreateDash([3, 3], 0);

        this.stroke.Color = new SKColor(0x70, 0x70, 0x70);
        this.stroke.StrokeWidth = 1.5f;
        this.stroke.PathEffect = effect;
        canvas.DrawRect(rect, this.stroke);
        this.stroke.PathEffect = null;
        this.stroke.StrokeWidth = 1;
    }

    /// <summary>
    /// Draws the dashed border around whatever is on the clipboard.
    /// </summary>
    void PaintClipboardMarquee(SKCanvas canvas, SpreadsheetPaintRequest request, int columnStart, int columnEnd, int rowStart, int rowEnd)
    {
        if (request.ClipboardRange is not { } range)
            return;

        if (range.Left > columnEnd || range.Right < columnStart || range.Top > rowEnd || range.Bottom < rowStart)
            return;

        var viewport = request.Viewport;
        var theme = request.Theme;

        var bounds = viewport.RangeRect(range);
        var margin = 64f;
        var rect = new SKRect(
            (float)Math.Max(bounds.X, -margin),
            (float)Math.Max(bounds.Y, -margin),
            (float)Math.Min(bounds.Right, viewport.Width + margin),
            (float)Math.Min(bounds.Bottom, viewport.Height + margin));

        if (rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            return;

        var dash = (float)Math.Max(1, theme.ClipboardDashLength);

        using var effect = SKPathEffect.CreateDash([dash, dash], request.ClipboardDashPhase);

        this.stroke.Color = ToSk(theme.ClipboardBorder);
        this.stroke.StrokeWidth = (float)theme.ClipboardBorderWidth;
        this.stroke.PathEffect = effect;
        this.stroke.IsAntialias = true;

        canvas.DrawRect(rect, this.stroke);

        // The stroke paint is shared with the grid lines and the headers, both of which draw solid
        // hairlines; leaving the dash on it would turn every one of them into a dotted line.
        this.stroke.PathEffect = null;
        this.stroke.IsAntialias = false;
        this.stroke.StrokeWidth = 1;
    }

    // ---- floating things ----

    static SKRect ContentRect(SpreadsheetPaintRequest request)
    {
        var metrics = request.Viewport.Metrics;
        return new SKRect((float)metrics.RowHeaderWidth, (float)metrics.ColumnHeaderHeight, (float)request.Viewport.Width, (float)request.Viewport.Height);
    }

    /// <summary>The content area less the frozen band for anything anchored past the freeze.</summary>
    static SKRect ScrollingClip(SpreadsheetPaintRequest request, SKRect content, CellRef from)
    {
        var viewport = request.Viewport;
        var frozen = viewport.Metrics.FrozenPane;
        var clip = content;

        if (frozen.Row > 0 && from.Row >= frozen.Row)
            clip.Top = Math.Max(clip.Top, (float)viewport.ContentOriginY);

        if (frozen.Column > 0 && from.Column >= frozen.Column)
            clip.Left = Math.Max(clip.Left, (float)viewport.ContentOriginX);

        return clip;
    }

    static SKRect ChartRect(GridViewport viewport, ChartAnchor anchor)
    {
        var from = viewport.CellRect(anchor.From);
        var to = viewport.CellRect(anchor.To);
        var x = from.X + anchor.FromDx;
        var y = from.Y + anchor.FromDy;
        return new SKRect((float)x, (float)y, (float)Math.Max(x + 4, to.X + anchor.ToDx), (float)Math.Max(y + 4, to.Y + anchor.ToDy));
    }

    void PaintCharts(SKCanvas canvas, SpreadsheetPaintRequest request)
    {
        var sheetCharts = request.Sheet.Charts;
        if (sheetCharts.Count == 0)
            return;

        var content = ContentRect(request);
        canvas.Save();
        canvas.ClipRect(content);

        foreach (var chart in sheetCharts)
        {
            var anchor = request.ChartPreview is { } preview && preview.Id == chart.Id ? preview.Anchor : chart.Anchor;
            var rect = ChartRect(request.Viewport, anchor);

            // A chart anchored in the scrolling pane scrolls under the frozen rows and columns, as in
            // Excel; unclipped, one scrolled out of view left a sliver across the frozen band.
            var clip = ScrollingClip(request, content, anchor.From);
            if (!rect.IntersectsWith(clip))
                continue;

            canvas.Save();
            canvas.ClipRect(clip);

            this.charts.Paint(canvas, rect, chart, SheetCharts.Evaluate(request.Sheet, chart));

            if (chart.Id == request.SelectedChartId)
                this.ChartHandles(canvas, request.Theme, rect);

            canvas.Restore();
        }

        canvas.Restore();
    }

    void ChartHandles(SKCanvas canvas, SpreadsheetTheme theme, SKRect rect)
    {
        this.stroke.Color = ToSk(theme.SelectionBorder);
        this.stroke.StrokeWidth = 1.5f;
        canvas.DrawRect(rect, this.stroke);

        var half = (float)SpreadsheetController.ChartHandleSize / 2;
        foreach (var (x, y) in new[] { (rect.Left, rect.Top), (rect.Right, rect.Top), (rect.Left, rect.Bottom), (rect.Right, rect.Bottom) })
        {
            var handle = new SKRect(x - half, y - half, x + half, y + half);
            this.fill.Color = SKColors.White;
            canvas.DrawRect(handle, this.fill);
            canvas.DrawRect(handle, this.stroke);
        }

        this.stroke.StrokeWidth = 1;
    }

    /// <summary>The yellow note boxes, beside their cells, with a leader line back to the corner.</summary>
    void PaintOpenNotes(SKCanvas canvas, SpreadsheetPaintRequest request)
    {
        if (request.OpenNotes.Count == 0)
            return;

        var content = ContentRect(request);
        canvas.Save();
        canvas.ClipRect(content);

        var font = this.GetFont("Segoe UI", 9, bold: false, italic: false);
        var bold = this.GetFont("Segoe UI", 9, bold: true, italic: false);
        const float width = 150;

        foreach (var note in request.OpenNotes)
        {
            var cell = ToSk(request.Viewport.CellRect(note.Cell));
            if (!cell.IntersectsWith(content))
                continue;

            var lines = new List<(string Text, SKFont Font)>();
            if (!string.IsNullOrEmpty(note.Author))
                lines.Add((note.Author + ":", bold));

            foreach (var paragraph in note.Text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = string.Empty;
                foreach (var word in paragraph.Split(' '))
                {
                    var candidate = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0 && font.MeasureText(candidate) > width - 10)
                    {
                        lines.Add((line, font));
                        line = word;
                    }
                    else
                    {
                        line = candidate;
                    }
                }

                lines.Add((line, font));
            }

            var height = Math.Max(40, lines.Count * font.Spacing + 10);
            var box = new SKRect(cell.Right + 12, cell.Top - 4, cell.Right + 12 + width, cell.Top - 4 + height);

            this.stroke.IsAntialias = true;
            this.stroke.Color = new SKColor(0x40, 0x40, 0x40);
            canvas.DrawLine(cell.Right - 1, cell.Top + 1, box.Left, box.Top + 6, this.stroke);

            this.fill.Color = new SKColor(0, 0, 0, 40);
            canvas.DrawRect(box.Left + 2, box.Top + 2, box.Width, box.Height, this.fill);

            this.fill.Color = new SKColor(0xFF, 0xFF, 0xE1);
            canvas.DrawRect(box, this.fill);
            canvas.DrawRect(box, this.stroke);
            this.stroke.IsAntialias = false;

            this.fill.Color = new SKColor(0x1A, 0x1A, 0x1A);
            var y = box.Top + 5 - font.Metrics.Ascent;
            foreach (var (text, lineFont) in lines)
            {
                canvas.DrawText(text, box.Left + 5, y, SKTextAlign.Left, lineFont, this.fill);
                y += font.Spacing;
            }
        }

        canvas.Restore();
    }

    // ---- headers ----

    void PaintHeaders(SKCanvas canvas, SpreadsheetPaintRequest request, int firstColumn, int lastColumn, int firstRow, int lastRow)
    {
        var viewport = request.Viewport;
        var metrics = viewport.Metrics;
        var theme = request.Theme;

        // Headings turned off: the bands have no size and there is nothing to draw.
        if (metrics.ColumnHeaderHeight <= 0 && metrics.RowHeaderWidth <= 0)
            return;

        var selection = Expanded(request.Selection.Range, request.Sheet.MergedRanges);
        var font = this.GetFont(theme.FontFamily, (float)theme.FontSize, bold: false, italic: false);
        var frozen = metrics.FrozenPane;

        // Column strip.
        canvas.Save();
        canvas.ClipRect(new SKRect((float)metrics.RowHeaderWidth, 0, (float)viewport.Width, (float)metrics.ColumnHeaderHeight));
        this.fill.Color = ToSk(theme.HeaderBackground);
        canvas.DrawRect(new SKRect((float)metrics.RowHeaderWidth, 0, (float)viewport.Width, (float)metrics.ColumnHeaderHeight), this.fill);

        foreach (var column in HeaderIndexes(frozen.Column, firstColumn, lastColumn))
        {
            if (metrics.Columns.IsHidden(column))
                continue;

            var bounds = viewport.CellRect(new CellRef(column, firstRow));
            var rect = new SKRect((float)bounds.X, 0, (float)bounds.Right, (float)metrics.ColumnHeaderHeight);

            // A scrolled column half under the frozen ones must not paint over their headers.
            var scrolledColumn = frozen.Column > 0 && column >= frozen.Column;
            if (scrolledColumn)
            {
                canvas.Save();
                canvas.ClipRect(new SKRect((float)viewport.ContentOriginX, 0, (float)viewport.Width, (float)metrics.ColumnHeaderHeight));
            }

            if (column >= selection.Left && column <= selection.Right)
            {
                this.fill.Color = ToSk(theme.HeaderSelectedBackground);
                canvas.DrawRect(rect, this.fill);
            }

            this.stroke.Color = ToSk(theme.HeaderBorder);
            var edge = (float)Math.Floor(rect.Right) - 0.5f;
            canvas.DrawLine(edge, 0, edge, (float)metrics.ColumnHeaderHeight, this.stroke);

            this.fill.Color = ToSk(theme.HeaderText);
            DrawCentred(canvas, CellRef.ColumnName(column), rect, font, this.fill);

            if (scrolledColumn)
                canvas.Restore();
        }

        this.stroke.Color = ToSk(theme.HeaderBorder);
        canvas.DrawLine((float)metrics.RowHeaderWidth, (float)metrics.ColumnHeaderHeight - 0.5f, (float)viewport.Width, (float)metrics.ColumnHeaderHeight - 0.5f, this.stroke);
        canvas.Restore();

        // Row gutter.
        canvas.Save();
        canvas.ClipRect(new SKRect(0, (float)metrics.ColumnHeaderHeight, (float)metrics.RowHeaderWidth, (float)viewport.Height));
        this.fill.Color = ToSk(theme.HeaderBackground);
        canvas.DrawRect(new SKRect(0, (float)metrics.ColumnHeaderHeight, (float)metrics.RowHeaderWidth, (float)viewport.Height), this.fill);

        foreach (var row in HeaderIndexes(frozen.Row, firstRow, lastRow))
        {
            if (metrics.Rows.IsHidden(row))
                continue;

            var bounds = viewport.CellRect(new CellRef(firstColumn, row));
            var rect = new SKRect(0, (float)bounds.Y, (float)metrics.RowHeaderWidth, (float)bounds.Bottom);

            // A scrolled row half under the frozen ones must not paint over their numbers ("1" read
            // as a garbled "22" with the top row frozen).
            var scrolledRow = frozen.Row > 0 && row >= frozen.Row;
            if (scrolledRow)
            {
                canvas.Save();
                canvas.ClipRect(new SKRect(0, (float)viewport.ContentOriginY, (float)metrics.RowHeaderWidth, (float)viewport.Height));
            }

            if (row >= selection.Top && row <= selection.Bottom)
            {
                this.fill.Color = ToSk(theme.HeaderSelectedBackground);
                canvas.DrawRect(rect, this.fill);
            }

            this.stroke.Color = ToSk(theme.HeaderBorder);
            var edge = (float)Math.Floor(rect.Bottom) - 0.5f;
            canvas.DrawLine(0, edge, (float)metrics.RowHeaderWidth, edge, this.stroke);

            this.fill.Color = ToSk(theme.HeaderText);
            DrawCentred(canvas, (row + 1).ToString(), rect, font, this.fill);

            if (scrolledRow)
                canvas.Restore();
        }

        this.stroke.Color = ToSk(theme.HeaderBorder);
        canvas.DrawLine((float)metrics.RowHeaderWidth - 0.5f, (float)metrics.ColumnHeaderHeight, (float)metrics.RowHeaderWidth - 0.5f, (float)viewport.Height, this.stroke);
        canvas.Restore();

        // Select-all corner, with Excel's triangle in it.
        var corner = new SKRect(0, 0, (float)metrics.RowHeaderWidth, (float)metrics.ColumnHeaderHeight);
        this.fill.Color = ToSk(theme.HeaderBackground);
        canvas.DrawRect(corner, this.fill);

        if (corner.Width > 12 && corner.Height > 12)
        {
            this.fill.Color = ToSk(theme.HeaderBorder);
            using var triangle = new SKPath();
            triangle.MoveTo(corner.Right - 4, corner.Bottom - 12);
            triangle.LineTo(corner.Right - 4, corner.Bottom - 4);
            triangle.LineTo(corner.Right - 12, corner.Bottom - 4);
            triangle.Close();
            canvas.DrawPath(triangle, this.fill);
        }
    }

    /// <summary>Frozen indexes first, then the scrolled ones — the order the header strip needs.</summary>
    static IEnumerable<int> HeaderIndexes(int frozenCount, int first, int last)
    {
        for (var i = 0; i < frozenCount; i++)
            yield return i;

        for (var i = Math.Max(first, frozenCount); i <= last; i++)
            yield return i;
    }

    void PaintFrozenDividers(SKCanvas canvas, SpreadsheetPaintRequest request)
    {
        var viewport = request.Viewport;
        var metrics = viewport.Metrics;
        if (!metrics.HasFrozenColumns && !metrics.HasFrozenRows)
            return;

        this.stroke.Color = ToSk(request.Theme.FrozenDivider);
        this.stroke.StrokeWidth = 1;

        if (metrics.HasFrozenColumns)
        {
            var x = (float)viewport.ContentOriginX - 0.5f;
            canvas.DrawLine(x, 0, x, (float)viewport.Height, this.stroke);
        }

        if (metrics.HasFrozenRows)
        {
            var y = (float)viewport.ContentOriginY - 0.5f;
            canvas.DrawLine(0, y, (float)viewport.Width, y, this.stroke);
        }
    }

    static void DrawCentred(SKCanvas canvas, string text, SKRect rect, SKFont font, SKPaint paint)
    {
        var width = font.MeasureText(text);
        var metrics = font.Metrics;
        canvas.DrawText(text, rect.MidX - width / 2, rect.MidY - (metrics.Ascent + metrics.Descent) / 2, SKTextAlign.Left, font, paint);
    }

    /// <summary>
    /// The font for a cell, resolved and cached by the shared measurer.
    /// </summary>
    SKFont GetFont(string family, float size, bool bold, bool italic)
        => this.measurer.GetFont(TextStyle.Default with
        {
            FontFamily = family,
            FontSize = size,
            Bold = bold,
            Italic = italic
        });

    // ---- page views ----

    static readonly SKColor PageBreakBlue = new(0x1F, 0x4E, 0xD8);

    /// <summary>
    /// Where the printed pages fall. Page Layout dashes each page's edges in grey; Page Break Preview
    /// draws them solid blue, washes everything off the pages and writes "Page n" over each one.
    /// </summary>
    void PaintPages(SKCanvas canvas, SpreadsheetPaintRequest request)
    {
        if (request.Pages is not { } pages || request.ViewMode == SheetViewMode.Normal || request.PrintMode)
            return;

        var viewport = request.Viewport;
        var content = ContentRect(request);
        var preview = request.ViewMode == SheetViewMode.PageBreakPreview;

        canvas.Save();
        canvas.ClipRect(content);

        var area = ToSk(viewport.RangeRect(pages.Range));

        if (preview)
        {
            // Off the print area: the grey Excel draws around the pages.
            using var wash = new SKPath { FillType = SKPathFillType.EvenOdd };
            wash.AddRect(content);
            wash.AddRect(area);
            this.fill.Color = new SKColor(0x80, 0x80, 0x80, 0x40);
            canvas.DrawPath(wash, this.fill);

            var font = this.GetFont("Segoe UI", 36, bold: true, italic: false);
            this.fill.Color = new SKColor(0x80, 0x80, 0x80, 0x66);
            var number = 1;
            foreach (var page in pages.Pages())
            {
                var rect = ToSk(viewport.RangeRect(page));
                if (rect.IntersectsWith(content))
                    canvas.DrawText($"Page {number}", rect.MidX, rect.MidY + 12, SKTextAlign.Center, font, this.fill);

                number++;
            }
        }

        this.stroke.Color = preview ? PageBreakBlue : new SKColor(0x70, 0x70, 0x70);
        this.stroke.StrokeWidth = preview ? 2.5f : 1f;
        this.stroke.PathEffect = preview ? null : SKPathEffect.CreateDash([4f, 3f], 0);

        foreach (var column in pages.ColumnStarts.Skip(1))
        {
            var x = (float)Math.Floor(viewport.CellRect(new CellRef(column, 0)).X) + 0.5f;
            canvas.DrawLine(x, area.Top, x, area.Bottom, this.stroke);
        }

        foreach (var row in pages.RowStarts.Skip(1))
        {
            var y = (float)Math.Floor(viewport.CellRect(new CellRef(0, row)).Y) + 0.5f;
            canvas.DrawLine(area.Left, y, area.Right, y, this.stroke);
        }

        canvas.DrawRect(area, this.stroke);

        this.stroke.PathEffect = null;
        this.stroke.StrokeWidth = 1;
        canvas.Restore();
    }

    static SKColor ToSk(ArgbColor color) => new(color.R, color.G, color.B, color.A);

    static SKRect ToSk(GridRect rect) => new((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom);

    public void Dispose()
    {
        this.charts.Dispose();

        if (this.ownsMeasurer)
            this.measurer.Dispose();

        this.fill.Dispose();
        this.stroke.Dispose();
    }
}
