using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using SkiaSharp;
using Shiny.Controls.Office.Theming;

namespace Shiny.Controls.Office.Skia;

public sealed record DocumentTheme
{
    public static readonly DocumentTheme Light = new();

    public static readonly DocumentTheme Dark = new()
    {
        PageBackground = new ArgbColor(255, 0x25, 0x25, 0x25),
        SurroundBackground = new ArgbColor(255, 0x18, 0x18, 0x18),
        PageBorder = new ArgbColor(255, 0x3A, 0x3A, 0x3A),
        PageShadow = new ArgbColor(90, 0, 0, 0),
        Text = new ArgbColor(255, 0xE4, 0xE4, 0xE4),
        Rule = new ArgbColor(255, 0x55, 0x55, 0x55),
        TableBorder = new ArgbColor(255, 0x55, 0x55, 0x55),
        Link = new ArgbColor(255, 0x6C, 0xB6, 0xFF),
        SelectionFill = new ArgbColor(80, 0x4C, 0x9A, 0xFF),
        FindMatchFill = new ArgbColor(110, 0xFF, 0xC1, 0x07),
        Caret = new ArgbColor(255, 0xEE, 0xEE, 0xEE),
        TouchHandle = new ArgbColor(255, 0x4C, 0x9A, 0xFF),

        // The ring takes the page's own ground rather than staying white, which on a dark page would
        // be a brighter mark than the handle it is meant to separate.
        TouchHandleRing = new ArgbColor(255, 0x1E, 0x1E, 0x1E)
    };

    public ArgbColor PageBackground { get; init; } = new(255, 255, 255, 255);
    public ArgbColor SurroundBackground { get; init; } = new(255, 0xF0, 0xF0, 0xF2);

    /// <summary>Hairline around a sheet of paper. Only drawn in print layout.</summary>
    public ArgbColor PageBorder { get; init; } = new(255, 0xD4, 0xD4, 0xD8);

    /// <summary>The sheet's drop shadow — what makes a page read as paper and not as a white box.</summary>
    public ArgbColor PageShadow { get; init; } = new(40, 0, 0, 0);
    public ArgbColor Text { get; init; } = new(255, 0x1A, 0x1A, 0x1A);
    public ArgbColor Rule { get; init; } = new(255, 0xC8, 0xC8, 0xC8);
    public ArgbColor TableBorder { get; init; } = new(255, 0xBB, 0xBB, 0xBB);
    public ArgbColor Link { get; init; } = new(255, 0x06, 0x5F, 0xD8);

    /// <summary>Selection wash. Alpha matters: the text has to stay readable underneath it.</summary>
    public ArgbColor SelectionFill { get; init; } = new(70, 0x21, 0x7A, 0xD8);

    /// <summary>
    /// Wash over every find match.
    /// </summary>
    /// <remarks>
    /// Deliberately not the selection's colour. The match the arrows are on is drawn as the selection
    /// as well, so the two have to be told apart at a glance — one hit is where you are, the rest are
    /// where you could go. Amber is what every find box has used for the other hits since browsers had
    /// one.
    /// </remarks>
    public ArgbColor FindMatchFill { get; init; } = new(120, 0xFF, 0xC1, 0x07);

    public ArgbColor Caret { get; init; } = new(255, 0x11, 0x11, 0x11);

    /// <summary>The grab handles drawn on a touch selection.</summary>
    public ArgbColor TouchHandle { get; init; } = new(255, 0x21, 0x7A, 0xD8);

    /// <summary>Keeps a handle readable over the text and the selection wash underneath it.</summary>
    public ArgbColor TouchHandleRing { get; init; } = new(255, 0xFF, 0xFF, 0xFF);

    public double TouchHandleRadius { get; init; } = 7;

    public double TouchHandleRingWidth { get; init; } = 2;

    /// <summary>The spelling squiggle. Red by convention, and the one place a document may not override.</summary>
    public ArgbColor SpellingUnderline { get; init; } = new(255, 0xD1, 0x34, 0x38);

    /// <summary>
    /// When true the document's own colours are discarded in favour of <see cref="Text"/>.
    /// </summary>
    /// <remarks>
    /// The blunt instrument. It guarantees legibility but flattens every authored colour to one, so a
    /// document's blue headings and red warnings all come out the same grey. Prefer
    /// <see cref="AdaptDocumentColors"/>.
    /// </remarks>
    public bool OverrideDocumentColors { get; init; }

    /// <summary>
    /// Lightens or darkens authored colours that do not contrast with the page, keeping their hue.
    /// </summary>
    /// <remarks>
    /// A document authored for white paper is black text. Shown on a dark page it is invisible, and
    /// the alternative — replacing every colour with the theme's — throws away the distinction between
    /// a heading, a hyperlink and a red warning. Adapting the lightness while preserving hue keeps both
    /// the legibility and the meaning.
    /// </remarks>
    public bool AdaptDocumentColors { get; init; } = true;
}

public sealed record DocumentPaintRequest
{
    public required IReadOnlyList<LaidOutBlock> Blocks { get; init; }
    public required DocumentViewport Viewport { get; init; }
    public DocumentTheme Theme { get; init; } = DocumentTheme.Light;
    public float Scale { get; init; } = 1f;

    /// <summary>Left edge of the page panel within the viewport, for centring a fixed-width page.</summary>
    public double PageX { get; init; }

    public double PageWidth { get; init; }

    /// <summary>
    /// Left edge of the content inside the page — <see cref="PageX"/> plus the page's own padding.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="PageX"/> because they are two different things and folding them
    /// together is silently wrong in both directions: the page panel ends up offset from centre by
    /// the padding, and the content sits flush against its left edge with double the padding on the
    /// right. Defaults to <see cref="PageX"/>, which is the unpadded case.
    /// </remarks>
    public double ContentX { get; init; } = double.NaN;

    internal double ResolvedContentX => Double.IsNaN(this.ContentX) ? this.PageX : this.ContentX;

    /// <summary>Selection rectangles in document coordinates, painted under the text.</summary>
    public IReadOnlyList<GridRectLike> Selection { get; init; } = [];

    /// <summary>
    /// Find matches in document coordinates, washed under the text.
    /// </summary>
    /// <remarks>
    /// Drawn beneath the selection rather than instead of it, so the match the arrows are sitting on
    /// carries both washes and reads as the current one.
    /// </remarks>
    public IReadOnlyList<GridRectLike> FindMatches { get; init; } = [];

    /// <summary>The caret, or null when the view is not focused or the caret is blinking off.</summary>
    public GridRectLike? Caret { get; init; }

    /// <summary>
    /// Grab handles for the selection, in document coordinates. Empty for a mouse.
    /// </summary>
    /// <remarks>
    /// Only meaningful under touch, where dragging pans the page rather than extending the selection -
    /// these are the only way left to adjust one, so they have to be visible rather than implied.
    /// </remarks>
    public IReadOnlyList<GridRectLike> TouchHandles { get; init; } = [];

    /// <summary>A picture drawn behind the page, under everything on it.</summary>
    public OfficeWatermark? Watermark { get; init; }

    /// <summary>Spans to underline as misspelled, in document coordinates.</summary>
    public IReadOnlyList<GridRectLike> Spelling { get; init; } = [];

    /// <summary>The frame and handles around a selected inline object, or null when none is selected.</summary>
    public DocumentObjectChrome? ObjectChrome { get; init; }

    /// <summary>
    /// The pages to draw, already narrowed to the visible ones.
    /// </summary>
    /// <remarks>
    /// Empty means reflow: one panel filling the viewport, no paper edges, no header or footer. The
    /// two modes share every line of the content painting below and differ only in what they wrap it
    /// in, which is the whole reason pagination was built as slicing rather than as a second layout.
    /// </remarks>
    public IReadOnlyList<DocumentPageView> Pages { get; init; } = [];

    /// <summary>Paper geometry. Only read when <see cref="Pages"/> is non-empty.</summary>
    public PageSetup Setup { get; init; } = PageSetup.Letter;

    /// <summary>Height of one sheet, in the same units as the layout.</summary>
    public double PageHeight { get; init; }
}


/// <summary>
/// The selection frame and resize handles around an inline object.
/// </summary>
/// <remarks>
/// The document counterpart of the slide editor's chrome, and deliberately smaller: an inline object
/// cannot be moved to an arbitrary position — it sits in the text flow, and the caret is what moves it
/// — so there is a resize frame and nothing else. Coordinates are the document's, like everything else
/// in the paint request.
/// </remarks>
public sealed record DocumentObjectChrome
{
    public required GridRectLike Frame { get; init; }

    public IReadOnlyList<GridRectLike> Handles { get; init; } = [];

    public ArgbColor Accent { get; init; } = new(255, 0x2F, 0x6F, 0xED);
}

/// <summary>
/// Paints a laid-out Word document.
/// </summary>
public sealed class DocumentPainter(SkiaTextMeasurer measurer) : IDisposable
{
    readonly SKPaint fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    readonly SKPaint stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
    readonly Dictionary<int, SKImage> images = new();

    public void Paint(SKCanvas canvas, DocumentPaintRequest request)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(request);

        var theme = request.Theme;
        canvas.Save();
        canvas.Scale(request.Scale);
        canvas.Clear(ToSk(theme.SurroundBackground));

        if (request.Pages.Count == 0)
            this.PaintReflowed(canvas, request, theme);
        else
            this.PaintPaginated(canvas, request, theme);

        canvas.Restore();
    }

    /// <summary>One panel filling the viewport, with the whole flow drawn into it.</summary>
    void PaintReflowed(SKCanvas canvas, DocumentPaintRequest request, DocumentTheme theme)
    {
        // The page is a lighter panel inside the surround, so a narrow measure still reads as a
        // document rather than as text floating on a background.
        var panel = new SKRect(
            Snap(request.PageX, request.Scale),
            0,
            Snap(request.PageX + request.PageWidth, request.Scale),
            (float)request.Viewport.Height);

        this.fill.Color = ToSk(theme.PageBackground);
        canvas.DrawRect(panel, this.fill);

        // Behind the text and pinned to the panel rather than to the flow: reflow has no pages, so a
        // mark that scrolled with the content would slide away and leave most of the document unmarked.
        WatermarkPainter.Draw(canvas, panel, request.Watermark);

        canvas.Save();
        canvas.Translate((float)request.ResolvedContentX, (float)-request.Viewport.ScrollY);
        this.PaintFlow(canvas, request, theme, Double.NegativeInfinity, Double.PositiveInfinity);
        canvas.Restore();
    }

    /// <summary>Sheets of paper, each showing its own slice of the flow plus its header and footer.</summary>
    void PaintPaginated(SKCanvas canvas, DocumentPaintRequest request, DocumentTheme theme)
    {
        var setup = request.Setup;
        var scrollY = request.Viewport.ScrollY;

        foreach (var entry in request.Pages)
        {
            var top = entry.Page.ViewTop - scrollY;
            var paper = new SKRect(
                Snap(request.PageX, request.Scale),
                Snap(top, request.Scale),
                Snap(request.PageX + request.PageWidth, request.Scale),
                Snap(top + request.PageHeight, request.Scale));

            this.PaintPaper(canvas, paper, theme, request.Watermark);

            // Clipped to the sheet: a paragraph whose last line straddles the boundary is drawn on
            // both pages, and without the clip the overhang would spill into the gap below.
            canvas.Save();
            canvas.ClipRect(paper);
            canvas.Translate(
                (float)request.ResolvedContentX,
                (float)(top + setup.MarginTop - entry.Page.FlowTop));

            this.PaintFlow(canvas, request, theme, entry.Page.FlowTop, entry.Page.FlowBottom);
            canvas.Restore();

            if (entry.Header is { } header)
            {
                canvas.Save();
                canvas.ClipRect(paper);
                canvas.Translate((float)request.ResolvedContentX, (float)(top + setup.HeaderDistance));

                foreach (var block in header.Blocks)
                    this.PaintBlock(canvas, block, theme, theme.PageBackground);

                canvas.Restore();
            }

            if (entry.Footer is { } footer)
            {
                canvas.Save();
                canvas.ClipRect(paper);

                // Measured up from the bottom edge, which is what w:pgMar/@w:footer means — so a
                // two-line footer grows upwards and its last line stays the same distance from the
                // paper's edge.
                canvas.Translate(
                    (float)request.ResolvedContentX,
                    (float)(top + request.PageHeight - setup.FooterDistance - footer.Height));

                foreach (var block in footer.Blocks)
                    this.PaintBlock(canvas, block, theme, theme.PageBackground);

                canvas.Restore();
            }
        }
    }

    /// <summary>A sheet of paper: a soft drop shadow, the page itself, and a hairline edge.</summary>
    /// <remarks>
    /// The shadow is blurred and horizontally centred, with only a slight downward bias. An offset
    /// hard-edged rectangle was the first attempt and it reads as a defect rather than as depth:
    /// the side it is offset towards gains a second, darker line against the surround, so the page
    /// looks like it has a 2px border on one side and a 1px border on the other.
    /// </remarks>
    void PaintPaper(SKCanvas canvas, SKRect paper, DocumentTheme theme, OfficeWatermark? watermark)
    {
        using (var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3f))
        {
            this.fill.Color = ToSk(theme.PageShadow);
            this.fill.MaskFilter = blur;
            canvas.DrawRect(paper with { Top = paper.Top + 1, Bottom = paper.Bottom + 3 }, this.fill);
            this.fill.MaskFilter = null;
        }

        this.fill.Color = ToSk(theme.PageBackground);
        canvas.DrawRect(paper, this.fill);

        // Per page, under the text. Drawn on the paper rather than across the surround so it lands the
        // same way on every sheet and does not run between them.
        WatermarkPainter.Draw(canvas, paper, watermark);

        // Inset by half the stroke so the hairline lands inside the paper rather than straddling its
        // edge — a centred stroke on a snapped edge still spills half a pixel into the surround.
        this.stroke.Color = ToSk(theme.PageBorder);
        this.stroke.StrokeWidth = 1;
        canvas.DrawRect(paper with { Left = paper.Left + 0.5f, Top = paper.Top + 0.5f, Right = paper.Right - 0.5f, Bottom = paper.Bottom - 0.5f }, this.stroke);
    }

    /// <summary>
    /// Rounds a layout coordinate to a whole device pixel.
    /// </summary>
    /// <remarks>
    /// The page is centred by halving whatever is left over, which lands on a half unit whenever the
    /// viewport and the page differ by an odd amount. Drawn unsnapped, one edge of the sheet
    /// antialiases across two device pixels and the other does not — which looks exactly like a
    /// gutter that is a pixel wider on one side.
    /// </remarks>
    static float Snap(double value, float scale)
        => scale > 0 ? (float)(Math.Round(value * scale) / scale) : (float)value;

    /// <summary>
    /// Draws the flow between two Y positions, in flow coordinates.
    /// </summary>
    /// <remarks>
    /// The one path both modes share. Selection, spelling and the caret arrive in flow coordinates
    /// too, so they need no page arithmetic of their own — they are simply filtered to the slice and
    /// drawn under the same translation as the text they belong to.
    /// </remarks>
    void PaintFlow(SKCanvas canvas, DocumentPaintRequest request, DocumentTheme theme, double flowTop, double flowBottom)
    {
        // Find matches go under the selection, which goes under the text: painting either over the
        // glyphs would wash out the words they are meant to point at.
        if (request.FindMatches.Count > 0)
        {
            this.fill.Color = ToSk(theme.FindMatchFill);
            foreach (var rect in request.FindMatches)
            {
                if (rect.Bottom < flowTop || rect.Y > flowBottom)
                    continue;

                canvas.DrawRect(new SKRect((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom), this.fill);
            }
        }

        // Selection goes under the text: painting it over would wash out the glyphs it is meant to
        // highlight.
        if (request.Selection.Count > 0)
        {
            this.fill.Color = ToSk(theme.SelectionFill);
            foreach (var rect in request.Selection)
            {
                if (rect.Bottom < flowTop || rect.Y > flowBottom)
                    continue;

                canvas.DrawRect(new SKRect((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom), this.fill);
            }
        }

        foreach (var block in request.Blocks)
        {
            if (block.Y + block.Height < flowTop)
                continue;

            if (block.Y > flowBottom)
                break;

            this.PaintBlock(canvas, block, theme, theme.PageBackground);
        }

        // Squiggles go over the text: they mark it rather than sit behind it, and a wash underneath
        // would be invisible against a descender.
        foreach (var rect in request.Spelling)
        {
            if (rect.Bottom < flowTop || rect.Y > flowBottom)
                continue;

            this.PaintSquiggle(canvas, rect, theme);
        }

        if (request.Caret is { } caret && caret.Bottom >= flowTop && caret.Y <= flowBottom)
        {
            this.fill.Color = ToSk(theme.Caret);
            canvas.DrawRect(
                new SKRect((float)caret.X, (float)caret.Y, (float)(caret.X + Math.Max(1, caret.Width)), (float)caret.Bottom),
                this.fill);
        }

        // Over the caret, because a handle marks the same edge and the two coincide on a collapsed
        // selection - a caret drawn on top would show through the middle of the dot.
        if (request.TouchHandles.Count > 0)
        {
            var radius = (float)theme.TouchHandleRadius;
            this.fill.Color = ToSk(theme.TouchHandle);
            this.stroke.Color = ToSk(theme.TouchHandleRing);
            this.stroke.StrokeWidth = (float)theme.TouchHandleRingWidth;

            foreach (var handle in request.TouchHandles)
            {
                if (handle.Bottom < flowTop || handle.Y > flowBottom)
                    continue;

                var cx = (float)(handle.X + (handle.Width / 2));
                var cy = (float)(handle.Y + (handle.Height / 2));

                canvas.DrawCircle(cx, cy, radius, this.fill);
                canvas.DrawCircle(cx, cy, radius, this.stroke);
            }

            this.stroke.StrokeWidth = 1;
        }

        // Last, over everything: the frame surrounds an object drawn a moment ago, and drawing it
        // any earlier would let the object paint over its own selection.
        if (request.ObjectChrome is { } objectChrome && objectChrome.Frame.Bottom >= flowTop && objectChrome.Frame.Y <= flowBottom)
            this.PaintObjectChrome(canvas, objectChrome);
    }

    /// <summary>
    /// Draws the wavy underline used for a misspelling.
    /// </summary>
    /// <remarks>
    /// Drawn as an actual wave rather than a straight red line, because a straight one is easily
    /// mistaken for the document's own underline formatting — which the editor also draws.
    /// </remarks>
    void PaintSquiggle(SKCanvas canvas, GridRectLike rect, DocumentTheme theme)
    {
        const float Period = 4f;
        const float Amplitude = 1.6f;

        using var path = new SKPath();
        var y = (float)rect.Y;
        var x = (float)rect.X;
        var right = (float)rect.Right;

        path.MoveTo(x, y);
        var up = true;
        while (x < right)
        {
            var next = Math.Min(x + Period, right);
            path.QuadTo((x + next) / 2, y + (up ? -Amplitude : Amplitude), next, y);
            x = next;
            up = !up;
        }

        this.stroke.Color = ToSk(theme.SpellingUnderline);
        this.stroke.StrokeWidth = 1.1f;
        canvas.DrawPath(path, this.stroke);
        this.stroke.StrokeWidth = 1;
    }

    /// <param name="ground">
    /// What the block is painted on: the page, or the shading of the table cell it sits in. Ink is made
    /// legible against this rather than against the page - see <see cref="InkContrast"/>.
    /// </param>
    void PaintBlock(SKCanvas canvas, LaidOutBlock block, DocumentTheme theme, ArgbColor ground)
    {
        switch (block)
        {
            case LaidOutParagraph paragraph:
                this.PaintParagraph(canvas, paragraph, theme, ground);
                break;

            case LaidOutTable table:
                this.PaintTable(canvas, table, theme, ground);
                break;

            case LaidOutRule rule:
                this.stroke.Color = ToSk(theme.Rule);
                this.stroke.StrokeWidth = 1;
                canvas.DrawLine((float)rule.X, (float)rule.Y, (float)(rule.X + rule.Width), (float)rule.Y, this.stroke);
                break;
        }
    }

    void PaintParagraph(SKCanvas canvas, LaidOutParagraph paragraph, DocumentTheme theme, ArgbColor ground)
    {
        if (paragraph.Format.Shading is { } shading)
        {
            ground = InkContrast.Over(shading, ground);

            this.fill.Color = ToSk(shading);
            canvas.DrawRect(
                new SKRect((float)paragraph.X, (float)paragraph.Y, (float)(paragraph.X + paragraph.Width), (float)(paragraph.Y + paragraph.Height)),
                this.fill);
        }

        foreach (var line in paragraph.Lines)
        {
            var baseline = paragraph.Y + line.Y + line.Ascent;

            foreach (var run in line.Runs)
                this.PaintRun(canvas, run, paragraph.X, baseline, theme, ground);
        }

        // The list label sits on the first line's baseline, outside the text body.
        if (paragraph.LabelText is { } label && paragraph.Lines.Count > 0)
        {
            var first = paragraph.Lines[0];
            var baseline = paragraph.Y + first.Y + first.Ascent;
            var style = paragraph.LabelStyle;

            this.fill.Color = ToSk(Ink(style.Color, theme, ground));
            canvas.DrawText(label, (float)paragraph.LabelX, (float)baseline, SKTextAlign.Left, measurer.GetFont(style), this.fill);
        }
    }

    /// <summary>Draws the frame and handles around the selected inline object.</summary>
    void PaintObjectChrome(SKCanvas canvas, DocumentObjectChrome chrome)
    {
        this.stroke.Color = ToSk(chrome.Accent);
        this.stroke.StrokeWidth = 1.5f;
        canvas.DrawRect(ToRect(chrome.Frame), this.stroke);

        foreach (var handle in chrome.Handles)
        {
            var rect = ToRect(handle);

            // White fill under an accent border, so a handle stays visible over a dark picture as
            // well as over the page.
            this.fill.Color = SKColors.White;
            canvas.DrawRect(rect, this.fill);

            this.stroke.Color = ToSk(chrome.Accent);
            canvas.DrawRect(rect, this.stroke);
        }
    }

    static SKRect ToRect(GridRectLike r)
        => new((float)r.X, (float)r.Y, (float)r.Right, (float)r.Bottom);

    void PaintRun(SKCanvas canvas, LaidOutRun run, double originX, double baseline, DocumentTheme theme, ArgbColor ground)
    {
        var x = originX + run.X;

        // An inline object sits on the baseline rather than straddling it, so its box runs upward
        // from there by its own height — which is what the layout engine reserved for it.
        if (run.Inline is { } inline)
        {
            var box = new SKRect(
                (float)x,
                (float)(baseline - inline.Height),
                (float)(x + inline.Width),
                (float)baseline);

            switch (inline)
            {
                case InlineImage image:
                    this.DrawImage(canvas, image.Data, box);
                    break;

                case InlineShape shape:
                    ShapePainting.DrawShape(
                        canvas, this.fill, this.stroke, shape.Geometry, box, shape.Fill, shape.Outline, shape.CornerRadius);

                    if (shape.Text.Count > 0)
                    {
                        // A gradient has no single colour to measure against; its text keeps the ground
                        // the shape itself sits on.
                        var shapeGround = shape.Fill?.Solid is { } solid ? InkContrast.Over(solid, ground) : ground;
                        this.PaintShapeText(canvas, shape, box, theme, shapeGround);
                    }

                    break;
            }

            return;
        }

        if (run.Text.Length == 0)
            return;

        var style = run.Style;
        var font = measurer.GetFont(style);

        // Superscript and subscript raise or lower the baseline by a fraction of the font size.
        var y = baseline - style.BaselineShift * style.FontSize;

        if (style.Highlight is { } highlight)
        {
            this.fill.Color = ToSk(highlight);
            ground = InkContrast.Over(highlight, ground);
            var metrics = font.Metrics;
            canvas.DrawRect(
                new SKRect((float)x, (float)(y + metrics.Ascent), (float)(x + run.Width), (float)(y + metrics.Descent)),
                this.fill);
        }

        var color = style.Link is not null
            ? InkContrast.Legible(theme.Link, ground)
            : Ink(style.Color, theme, ground);

        this.fill.Color = ToSk(color);
        canvas.DrawText(run.Text, (float)x, (float)y, SKTextAlign.Left, font, this.fill);

        var underline = style.Underline != UnderlineStyle.None || style.Link is not null;
        if (underline || style.Strike)
        {
            this.stroke.Color = ToSk(color);
            this.stroke.StrokeWidth = Math.Max(1, (float)(style.FontSize / 14));

            if (underline)
            {
                var offset = (float)(y + style.FontSize * 0.12);
                canvas.DrawLine((float)x, offset, (float)(x + run.Width), offset, this.stroke);

                if (style.Underline == UnderlineStyle.Double)
                {
                    var second = offset + this.stroke.StrokeWidth * 2;
                    canvas.DrawLine((float)x, second, (float)(x + run.Width), second, this.stroke);
                }
            }

            if (style.Strike)
            {
                var middle = (float)(y - style.FontSize * 0.28);
                canvas.DrawLine((float)x, middle, (float)(x + run.Width), middle, this.stroke);
            }
        }
    }

    /// <summary>
    /// Draws a shape's label, wrapped to the shape and centred in it.
    /// </summary>
    /// <remarks>
    /// Laid out here rather than during document layout because it does not affect the flow: the
    /// shape's box is a fixed size whatever its text does, so measuring the text earlier would buy
    /// nothing and would put a second text layout in the cached layout tree. Text taller than the
    /// shape is clipped to it, which is what Word does for a text box with autofit off.
    /// </remarks>
    void PaintShapeText(SKCanvas canvas, InlineShape shape, SKRect box, DocumentTheme theme, ArgbColor ground)
    {
        var engine = new TextLayoutEngine(measurer);
        var inset = 4f;
        var width = Math.Max(1, box.Width - (inset * 2));
        var lines = engine.Layout(shape.Text, width, shape.TextAlignment);
        var height = TextLayoutEngine.HeightOf(lines);

        canvas.Save();
        canvas.ClipRect(box);

        var top = box.Top + Math.Max(0, (box.Height - (float)height) / 2);

        foreach (var line in lines)
        {
            var baseline = top + (float)(line.Y + line.Ascent);

            foreach (var piece in line.Runs)
            {
                if (piece.Text.Length == 0)
                    continue;

                this.fill.Color = ToSk(Ink(piece.Style.Color, theme, ground));
                canvas.DrawText(
                    piece.Text,
                    box.Left + inset + (float)piece.X,
                    baseline,
                    SKTextAlign.Left,
                    measurer.GetFont(piece.Style),
                    this.fill);
            }
        }

        canvas.Restore();
    }

    void PaintTable(SKCanvas canvas, LaidOutTable table, DocumentTheme theme, ArgbColor ground)
    {
        foreach (var cell in table.Cells)
        {
            var rect = new SKRect((float)cell.X, (float)cell.Y, (float)(cell.X + cell.Width), (float)(cell.Y + cell.Height));
            var cellGround = ground;

            if (cell.Shading is { } shading)
            {
                this.fill.Color = ToSk(shading);
                canvas.DrawRect(rect, this.fill);

                // The cell's text sits on its shading, not on the page. Measured against the page, a
                // light header fill in a dark theme got its text lifted to white on top of the fill.
                cellGround = InkContrast.Over(shading, ground);
            }

            foreach (var block in cell.Blocks)
                this.PaintBlock(canvas, block, theme, cellGround);

            if (!table.HasBorders)
                continue;

            this.stroke.Color = ToSk(theme.TableBorder);
            this.stroke.StrokeWidth = 1;

            // Half-pixel offset keeps a hairline on one device pixel rather than blurring across two.
            canvas.DrawRect(
                new SKRect(rect.Left + 0.5f, rect.Top + 0.5f, rect.Right - 0.5f, rect.Bottom - 0.5f),
                this.stroke);
        }
    }

    void DrawImage(SKCanvas canvas, byte[] data, SKRect destination)
    {
        // Decoded images are cached by content hash: the same logo repeated on every page would
        // otherwise be decoded once per paint.
        var key = System.HashCode.Combine(data.Length, data.Length > 0 ? data[0] : 0, data.Length > 64 ? data[64] : 0);

        if (!this.images.TryGetValue(key, out var image))
        {
            try
            {
                image = SKImage.FromEncodedData(data);
            }
            catch (Exception)
            {
                image = null;
            }

            this.images[key] = image!;
        }

        if (image is null)
        {
            // An undecodable image still occupies its space; an outline says something belongs here.
            this.stroke.Color = new SKColor(0x99, 0x99, 0x99);
            this.stroke.StrokeWidth = 1;
            canvas.DrawRect(destination, this.stroke);
            return;
        }

        canvas.DrawImage(image, destination, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    public void Dispose()
    {
        foreach (var image in this.images.Values)
            image?.Dispose();

        this.images.Clear();
        this.fill.Dispose();
        this.stroke.Dispose();
    }

    /// <summary>
    /// The colour a run is painted in: the authored colour (or the theme's, when overriding), made to
    /// read against the ground it actually sits on.
    /// </summary>
    static ArgbColor Ink(ArgbColor authored, DocumentTheme theme, ArgbColor ground)
    {
        if (theme.OverrideDocumentColors)
            return InkContrast.Legible(theme.Text, ground);

        return theme.AdaptDocumentColors ? InkContrast.Legible(authored, ground) : authored;
    }

    /// <summary>
    /// Adjusts an authored colour so it reads against the page, keeping its hue and saturation.
    /// </summary>
    /// <remarks>
    /// Only moves a colour that is genuinely on the wrong side of the page's lightness — a red that
    /// already contrasts is left exactly as authored, so the adaptation is invisible on a light theme
    /// and only does work where it is needed. Text on a shaded cell, a shaded paragraph or a highlight
    /// is measured against that fill instead; see <see cref="Ink"/>.
    /// </remarks>
    static ArgbColor Legible(ArgbColor color, DocumentTheme theme)
        => theme.AdaptDocumentColors ? InkContrast.Legible(color, theme.PageBackground) : color;

    static SKColor ToSk(ArgbColor color) => new(color.R, color.G, color.B, color.A);
}
