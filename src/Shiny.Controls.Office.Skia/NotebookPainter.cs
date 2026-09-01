using Shiny.Controls.Office.Notebook;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using SkiaSharp;

namespace Shiny.Controls.Office.Skia;

/// <summary>Colours for the notebook canvas. Everything an item carries is its own.</summary>
public sealed record NotebookTheme
{
    public static readonly NotebookTheme Light = new();

    public static readonly NotebookTheme Dark = new()
    {
        Paper = new ArgbColor(255, 0x1E, 0x1E, 0x21),
        Rule = new ArgbColor(255, 0x35, 0x35, 0x3A),
        DefaultInk = new ArgbColor(255, 0xE6, 0xE6, 0xE6),
        Accent = new ArgbColor(255, 0x6E, 0x9B, 0xFF)
    };

    public ArgbColor Paper { get; init; } = new(255, 255, 255, 255);

    /// <summary>The ruled lines, grid or dots. Deliberately faint: it is a writing guide, not content.</summary>
    public ArgbColor Rule { get; init; } = new(255, 0xDD, 0xE3, 0xEC);

    /// <summary>
    /// The colour ink and text fall back to.
    /// </summary>
    /// <remarks>
    /// Only reached by an item that stored no colour of its own. A stroke written in black stays black
    /// when the app flips to dark — repainting a user's ink is not theming — so this covers the empty
    /// container and the default pen, not existing content.
    /// </remarks>
    public ArgbColor DefaultInk { get; init; } = new(255, 0x1A, 0x1A, 0x1A);

    public ArgbColor Accent { get; init; } = new(255, 0x2F, 0x6F, 0xED);
}

/// <summary>The editing overlay: what is selected, where the caret is, and what is being dragged out.</summary>
public sealed record NotebookChrome
{
    public IReadOnlyList<NoteRect> SelectionFrames { get; init; } = [];

    /// <summary>The single box drawn round the whole selection, which is what carries the handles.</summary>
    public NoteRect? SelectionBounds { get; init; }

    public IReadOnlyList<NoteRect> Handles { get; init; } = [];

    public IReadOnlyList<NoteRect> TextSelection { get; init; } = [];

    public NoteRect? Caret { get; init; }

    public bool IsEditingText { get; init; }

    /// <summary>The marquee, or the shape being dragged out, in page coordinates.</summary>
    public NoteRect? RubberBand { get; init; }

    public bool RubberBandIsShape { get; init; }

    /// <summary>The lasso path so far, in page coordinates.</summary>
    public IReadOnlyList<(double X, double Y)> Lasso { get; init; } = [];

    /// <summary>The stroke being laid down right now, in page coordinates.</summary>
    public IReadOnlyList<InkPoint> LiveStroke { get; init; } = [];

    public ArgbColor LiveStrokeColor { get; init; }

    public double LiveStrokeWidth { get; init; }

    public InkTool LiveStrokeTool { get; init; }

    public (double X, double Y, double Radius)? Eraser { get; init; }

    public ArgbColor Accent { get; init; } = new(255, 0x2F, 0x6F, 0xED);

    public ArgbColor SelectionFill { get; init; } = new(70, 0x2F, 0x6F, 0xED);

    /// <summary>
    /// Reads the whole overlay off a controller.
    /// </summary>
    /// <remarks>
    /// Here rather than in each host because there is exactly one right answer for what the overlay
    /// shows, and two hosts assembling it separately is two places for a handle to go missing. The
    /// controller stays free of any drawing type; this reaches into it, not the other way round.
    /// </remarks>
    public static NotebookChrome From(NotebookEditorController controller, ArgbColor? accent = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var colour = accent ?? new ArgbColor(255, 0x2F, 0x6F, 0xED);

        // Per-item frames only when several things are selected: with one, the group frame already
        // sits exactly on it and the second rectangle just thickens the line.
        var frames = controller.SelectedIds.Count > 1
            ? controller.SelectedItems().Select(x =>
            {
                var (x0, y0, w, h) = x.Bounds();
                return controller.ToViewport(new NoteRect(x0, y0, w, h));
            }).ToList()
            : [];

        return new NotebookChrome
        {
            SelectionFrames = frames,
            SelectionBounds = controller.SelectionBounds(),
            Handles = [.. controller.SelectionHandles().Select(x => x.Rect)],
            TextSelection = [.. controller.TextSelectionRects()],
            Caret = controller.IsEditingText ? controller.CaretRect() : null,
            IsEditingText = controller.IsEditingText,
            RubberBand = controller.LiveRect,
            RubberBandIsShape = controller.IsCreatingShape,
            Lasso = controller.LiveLasso,
            LiveStroke = controller.LiveStroke,
            LiveStrokeColor = controller.LiveStrokeColor,
            LiveStrokeWidth = controller.LiveStrokeWidth,
            LiveStrokeTool = controller.LiveStrokeTool,
            Eraser = controller.EraserCursor,
            Accent = colour,
            SelectionFill = colour with { A = 70 }
        };
    }
}

public sealed record NotebookPaintRequest
{
    public required NotebookPage Page { get; init; }

    /// <summary>Page-to-viewport scale.</summary>
    public required double Zoom { get; init; }

    public required double ScrollX { get; init; }

    public required double ScrollY { get; init; }

    public required double ViewportWidth { get; init; }

    public required double ViewportHeight { get; init; }

    public NotebookTheme Theme { get; init; } = NotebookTheme.Light;

    /// <summary>Device pixel ratio, applied on top of the zoom.</summary>
    public float DeviceScale { get; init; } = 1;

    public NotebookChrome? Chrome { get; init; }
}

/// <summary>
/// Paints a notebook page and the editor's overlay.
/// </summary>
/// <remarks>
/// <para>
/// The whole page is drawn in page coordinates under one canvas transform rather than each item being
/// projected by hand. That is what lets an item's geometry, the layout engine's line boxes and the
/// controller's hit-testing all speak the same units — the only place the two spaces meet is the
/// single <c>Translate</c>/<c>Scale</c> pair here, and the controller's
/// <see cref="NotebookController.ToPage"/> which is its exact inverse.
/// </para>
/// <para>
/// Chrome is drawn afterwards in <i>viewport</i> coordinates, deliberately. A selection frame and a
/// resize handle are a constant number of pixels wide at every zoom level; scaling them with the page
/// would give a hairline frame at 25% and a slab at 400%.
/// </para>
/// </remarks>
public sealed class NotebookPainter(SkiaTextMeasurer measurer) : IDisposable
{
    readonly SKPaint fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    readonly SKPaint stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    readonly SKPaint ink = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round
    };

    readonly Dictionary<int, SKImage?> images = new();

    public void Paint(SKCanvas canvas, NotebookPaintRequest request)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(request);

        var theme = request.Theme;

        canvas.Save();
        canvas.Scale(request.DeviceScale);

        canvas.Clear(ToSk(request.Page.Background ?? theme.Paper));

        canvas.Save();
        canvas.Translate((float)-request.ScrollX, (float)-request.ScrollY);
        canvas.Scale((float)request.Zoom);

        // The page rectangle in page coordinates, so the rule and the clip both stop at the extent
        // rather than running out into the surround.
        var (extentWidth, extentHeight) = request.Page.Extent();
        var page = new SKRect(0, 0, (float)extentWidth, (float)extentHeight);

        this.PaintRule(canvas, request.Page, page, theme);

        // Highlighter under everything else, in one pass, so marking a paragraph does not grey the
        // words it marks. Within each pass the list order still decides what covers what.
        var ground = request.Page.Background ?? theme.Paper;

        foreach (var item in request.Page.Items)
        {
            if (item.PaintsBehind)
                this.PaintItem(canvas, item, theme, ground);
        }

        foreach (var item in request.Page.Items)
        {
            if (!item.PaintsBehind)
                this.PaintItem(canvas, item, theme, ground);
        }

        if (request.Chrome is { } chrome)
            this.PaintPageChrome(canvas, chrome, request.Zoom);

        canvas.Restore();

        if (request.Chrome is { } overlay)
            this.PaintViewportChrome(canvas, overlay);

        canvas.Restore();
    }

    // ---- page background ----

    void PaintRule(SKCanvas canvas, NotebookPage page, SKRect bounds, NotebookTheme theme)
    {
        if (page.Rule == PageRule.Blank)
            return;

        var spacing = (float)Math.Max(6, page.RuleSpacing);
        var color = ToSk(page.RuleColor ?? theme.Rule);

        if (page.Rule == PageRule.Dots)
        {
            this.fill.Color = color;
            this.fill.Shader = null;

            for (var y = spacing; y < bounds.Bottom; y += spacing)
            {
                for (var x = spacing; x < bounds.Right; x += spacing)
                    canvas.DrawCircle(x, y, 1.1f, this.fill);
            }

            return;
        }

        this.stroke.Color = color;
        this.stroke.StrokeWidth = 1;
        this.stroke.PathEffect = null;

        for (var y = spacing; y < bounds.Bottom; y += spacing)
            canvas.DrawLine(bounds.Left, y, bounds.Right, y, this.stroke);

        if (page.Rule != PageRule.Grid)
            return;

        for (var x = spacing; x < bounds.Right; x += spacing)
            canvas.DrawLine(x, bounds.Top, x, bounds.Bottom, this.stroke);
    }

    // ---- items ----

    void PaintItem(SKCanvas canvas, NoteItem item, NotebookTheme theme, ArgbColor pageGround)
    {
        if (item.Kind == NoteItemKind.Ink)
        {
            if (item.Stroke is { } stroke)
                this.PaintStroke(canvas, stroke.Points, stroke.Color, stroke.Width, stroke.Tool);

            return;
        }

        var bounds = new SKRect(
            (float)item.X, (float)item.Y, (float)(item.X + item.Width), (float)(item.Y + item.Height));

        canvas.Save();

        if (item.Rotation != 0)
            canvas.RotateDegrees((float)item.Rotation, bounds.MidX, bounds.MidY);

        switch (item.Kind)
        {
            case NoteItemKind.Image:
                if (item.Image is { } bytes)
                    this.DrawImage(canvas, bytes, bounds);
                break;

            case NoteItemKind.Shape:
                ShapePainting.DrawShape(
                    canvas, this.fill, this.stroke, item.Geometry, bounds, item.Fill, item.Outline, item.CornerRadius);
                break;

            case NoteItemKind.Text:
                // A text container has no frame of its own unless one was asked for. OneNote's
                // outlines are invisible until you hover them, and a page of boxed paragraphs reads
                // as a form rather than as notes.
                if (!item.Fill.IsEmpty || item.Outline is not null)
                {
                    ShapePainting.DrawShape(
                        canvas, this.fill, this.stroke, item.Geometry, bounds, item.Fill, item.Outline, item.CornerRadius);
                }

                break;
        }

        // The ink is chosen against whatever is directly behind the glyphs, which is the item's own
        // fill when it has an opaque one and the page otherwise. See ShapeTextPainter.Ink.
        if (item.Text is { } text)
        {
            var ground = item.Fill.Solid is { A: 255 } solid ? solid : pageGround;
            ShapeTextPainter.Draw(canvas, this.fill, this.stroke, measurer, text, bounds, InkOn(ground, theme));
        }

        canvas.Restore();
    }

    /// <summary>
    /// Draws a stroke as a smoothed path with pressure-varying width.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two passes over the same points, and which one runs matters. A stroke whose samples all report
    /// the same pressure — everything a mouse or a plain touchscreen produces — is one path at one
    /// width, which Skia can stroke in a single call. A pen that reports real force has to be drawn
    /// segment by segment, because a path has exactly one stroke width and a varying nib does not fit
    /// in one.
    /// </para>
    /// <para>
    /// The path is a quadratic through the midpoints rather than a polyline through the samples. A
    /// polyline shows every sample as a corner, which under a fast hand is a visibly faceted curve;
    /// the midpoint construction is what makes handwriting look written rather than plotted.
    /// </para>
    /// </remarks>
    void PaintStroke(SKCanvas canvas, IReadOnlyList<InkPoint> points, ArgbColor color, double width, InkTool tool)
    {
        if (points.Count < 2)
            return;

        this.ink.Color = ToSk(color);
        this.ink.BlendMode = SKBlendMode.SrcOver;

        // A flat cap keeps a highlighter's ends square, the way a chisel nib marks paper; a round cap
        // on a 16px band reads as a lozenge.
        this.ink.StrokeCap = tool == InkTool.Highlighter ? SKStrokeCap.Butt : SKStrokeCap.Round;

        var varying = false;
        for (var i = 1; i < points.Count && !varying; i++)
            varying = Math.Abs(points[i].Pressure - points[0].Pressure) > 0.04;

        if (!varying || tool == InkTool.Highlighter)
        {
            this.ink.StrokeWidth = (float)Math.Max(0.4, width * (tool == InkTool.Highlighter ? 1 : points[0].Pressure * 2));

            using var path = BuildStrokePath(points);
            canvas.DrawPath(path, this.ink);
            return;
        }

        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];

            this.ink.StrokeWidth = (float)Math.Max(0.4, width * (a.Pressure + b.Pressure));
            canvas.DrawLine((float)a.X, (float)a.Y, (float)b.X, (float)b.Y, this.ink);
        }
    }

    static SKPath BuildStrokePath(IReadOnlyList<InkPoint> points)
    {
        var path = new SKPath();
        path.MoveTo((float)points[0].X, (float)points[0].Y);

        for (var i = 1; i < points.Count - 1; i++)
        {
            var current = points[i];
            var next = points[i + 1];

            path.QuadTo(
                (float)current.X,
                (float)current.Y,
                (float)((current.X + next.X) / 2),
                (float)((current.Y + next.Y) / 2));
        }

        path.LineTo((float)points[^1].X, (float)points[^1].Y);

        return path;
    }

    // ---- chrome ----

    /// <summary>The parts of the overlay that live in page coordinates, because they track content.</summary>
    void PaintPageChrome(SKCanvas canvas, NotebookChrome chrome, double zoom)
    {
        var hairline = (float)(1 / Math.Max(0.01, zoom));

        if (chrome.LiveStroke.Count > 1)
            this.PaintStroke(canvas, chrome.LiveStroke, chrome.LiveStrokeColor, chrome.LiveStrokeWidth, chrome.LiveStrokeTool);

        if (chrome.RubberBand is { } band)
        {
            var rect = new SKRect((float)band.X, (float)band.Y, (float)band.Right, (float)band.Bottom);

            this.fill.Color = ToSk(chrome.SelectionFill).WithAlpha(chrome.RubberBandIsShape ? (byte)30 : (byte)40);
            this.fill.Shader = null;
            canvas.DrawRect(rect, this.fill);

            this.stroke.Color = ToSk(chrome.Accent);
            this.stroke.StrokeWidth = hairline;
            this.stroke.PathEffect = SKPathEffect.CreateDash([hairline * 4, hairline * 3], 0);
            canvas.DrawRect(rect, this.stroke);
            this.stroke.PathEffect?.Dispose();
            this.stroke.PathEffect = null;
        }

        if (chrome.Lasso.Count > 1)
        {
            using var path = new SKPath();
            path.MoveTo((float)chrome.Lasso[0].X, (float)chrome.Lasso[0].Y);

            for (var i = 1; i < chrome.Lasso.Count; i++)
                path.LineTo((float)chrome.Lasso[i].X, (float)chrome.Lasso[i].Y);

            path.Close();

            this.fill.Color = ToSk(chrome.SelectionFill).WithAlpha(24);
            this.fill.Shader = null;
            canvas.DrawPath(path, this.fill);

            this.stroke.Color = ToSk(chrome.Accent);
            this.stroke.StrokeWidth = hairline * 1.5f;
            this.stroke.PathEffect = SKPathEffect.CreateDash([hairline * 5, hairline * 4], 0);
            canvas.DrawPath(path, this.stroke);
            this.stroke.PathEffect?.Dispose();
            this.stroke.PathEffect = null;
        }

        if (chrome.Eraser is { } eraser)
        {
            this.stroke.Color = ToSk(chrome.Accent);
            this.stroke.StrokeWidth = hairline * 1.5f;
            canvas.DrawCircle((float)eraser.X, (float)eraser.Y, (float)eraser.Radius, this.stroke);
        }
    }

    /// <summary>The parts that are a fixed pixel size whatever the zoom.</summary>
    void PaintViewportChrome(SKCanvas canvas, NotebookChrome chrome)
    {
        foreach (var rect in chrome.TextSelection)
        {
            this.fill.Color = ToSk(chrome.SelectionFill);
            this.fill.Shader = null;
            canvas.DrawRect(Rect(rect), this.fill);
        }

        // A dashed hairline per item, plus a solid frame round the group: with several things selected
        // the group frame is the thing the handles belong to, and without the per-item dashes there is
        // no way to tell which of the things inside it are actually in the selection.
        if (chrome.SelectionFrames.Count > 1)
        {
            this.stroke.Color = ToSk(chrome.Accent).WithAlpha(150);
            this.stroke.StrokeWidth = 1;
            this.stroke.PathEffect = SKPathEffect.CreateDash([3, 3], 0);

            foreach (var frame in chrome.SelectionFrames)
                canvas.DrawRect(Rect(frame), this.stroke);

            this.stroke.PathEffect?.Dispose();
            this.stroke.PathEffect = null;
        }

        if (chrome.SelectionBounds is { } bounds)
        {
            this.stroke.Color = ToSk(chrome.Accent);
            this.stroke.StrokeWidth = chrome.IsEditingText ? 1 : 1.5f;
            this.stroke.PathEffect = chrome.IsEditingText ? SKPathEffect.CreateDash([4, 3], 0) : null;
            canvas.DrawRect(Rect(bounds), this.stroke);
            this.stroke.PathEffect?.Dispose();
            this.stroke.PathEffect = null;
        }

        foreach (var handle in chrome.Handles)
        {
            var rect = Rect(handle);

            this.fill.Color = SKColors.White;
            this.fill.Shader = null;
            canvas.DrawRect(rect, this.fill);

            this.stroke.Color = ToSk(chrome.Accent);
            this.stroke.StrokeWidth = 1.5f;
            canvas.DrawRect(rect, this.stroke);
        }

        if (chrome.Caret is { } caret)
        {
            this.fill.Color = ToSk(chrome.Accent);
            this.fill.Shader = null;
            canvas.DrawRect(Rect(caret), this.fill);
        }
    }

    /// <summary>
    /// The default ink to use for text sitting on <paramref name="ground"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Following the page alone is not enough: a shape with a pale fill on a dark page needs dark text,
    /// and one with a dark fill on a pale page needs light text. So the ground is the shape's own fill
    /// where it has an opaque one, and the choice is between the theme's two extremes rather than
    /// between two new colours — the paper and the ink already contrast with each other by
    /// construction, so whichever of them is further from the ground is the readable one.
    /// </para>
    /// <para>
    /// Only reached for text nobody gave a colour to. An authored colour is honoured as-is, however it
    /// reads, because second-guessing one is how a deliberately subtle caption gets repainted.
    /// </para>
    /// </remarks>
    static ArgbColor InkOn(ArgbColor ground, NotebookTheme theme)
        => Math.Abs(Luminance(theme.DefaultInk) - Luminance(ground)) >= Math.Abs(Luminance(theme.Paper) - Luminance(ground))
            ? theme.DefaultInk
            : theme.Paper;

    /// <summary>Rec. 601 luma, which is enough to tell a light ground from a dark one.</summary>
    static double Luminance(ArgbColor color)
        => (color.R * 0.299 + color.G * 0.587 + color.B * 0.114) / 255;

    static SKRect Rect(NoteRect rect)
        => new((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom);

    void DrawImage(SKCanvas canvas, byte[] data, SKRect destination)
    {
        // Keyed on a cheap fingerprint rather than the bytes, matching the slide painter: decoding a
        // photo per frame is the difference between a page that scrolls and one that stutters.
        var key = HashCode.Combine(data.Length, data.Length > 0 ? data[0] : 0, data.Length > 64 ? data[64] : 0);

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

            this.images[key] = image;
        }

        if (image is null)
            return;

        canvas.DrawImage(image, destination, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    public void Dispose()
    {
        this.fill.Dispose();
        this.stroke.Dispose();
        this.ink.Dispose();

        foreach (var image in this.images.Values)
            image?.Dispose();

        this.images.Clear();
    }

    static SKColor ToSk(ArgbColor color) => new(color.R, color.G, color.B, color.A);
}
