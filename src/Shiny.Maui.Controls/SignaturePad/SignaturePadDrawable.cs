namespace Shiny.Maui.Controls.SignaturePad;

internal sealed class SignaturePadDrawable : IDrawable
{
    // Deliberately not themed: the guide sits on the signature surface, which is paper (white by
    // default, and still white in dark mode because the signature is captured as an image).
    static readonly Color GuideColor = Color.FromArgb("#9CA3AF");

    readonly List<List<PointF>> committedStrokes = new();
    List<PointF>? activeStroke;

    // The on-screen canvas size (captured on each live Draw) so the export can scale the strokes — which
    // are recorded in canvas coordinates — into the export bitmap instead of clipping them.
    float canvasWidth;
    float canvasHeight;

    public Color BackgroundColor { get; set; } = Colors.White;
    public Color StrokeColor { get; set; } = Colors.Black;
    public float StrokeWidth { get; set; } = 3f;

    /// <summary>Draws a baseline to sign on. On screen only, never exported.</summary>
    public bool ShowSignatureLine { get; set; }

    /// <summary>Shown over the baseline while nothing is drawn. On screen only, never exported.</summary>
    public string? PlaceholderText { get; set; }

    public bool HasSignature => committedStrokes.Count > 0;

    public int StrokeCount => committedStrokes.Count;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        // Remember the live canvas size so ExportToPng can map canvas coordinates → export bitmap.
        if (dirtyRect.Width > 0 && dirtyRect.Height > 0)
        {
            canvasWidth = dirtyRect.Width;
            canvasHeight = dirtyRect.Height;
        }

        canvas.FillColor = BackgroundColor;
        canvas.FillRectangle(dirtyRect);

        DrawGuide(canvas, dirtyRect);
        DrawStrokes(canvas, committedStrokes, 1f, 0f, 0f, StrokeWidth);

        if (activeStroke is { Count: >= 2 })
            DrawSingleStroke(canvas, activeStroke, 1f, 0f, 0f, StrokeWidth);
    }

    void DrawGuide(ICanvas canvas, RectF rect)
    {
        var lineY = rect.Top + rect.Height * 0.72f;
        var inset = Math.Min(24f, rect.Width * 0.08f);

        if (ShowSignatureLine)
        {
            canvas.StrokeColor = GuideColor;
            canvas.StrokeSize = 1;
            canvas.DrawLine(rect.Left + inset, lineY, rect.Right - inset, lineY);

            canvas.FontColor = GuideColor;
            canvas.FontSize = 16;
            canvas.DrawString("✕", rect.Left + inset, lineY - 24, 20, 20, HorizontalAlignment.Left, VerticalAlignment.Bottom);
        }

        if (!HasSignature && activeStroke is null && !String.IsNullOrEmpty(PlaceholderText))
        {
            canvas.FontColor = GuideColor;
            canvas.FontSize = 14;
            var textTop = ShowSignatureLine ? lineY + 6 : rect.Top;
            var textHeight = ShowSignatureLine ? 20 : rect.Height;
            var vertical = ShowSignatureLine ? VerticalAlignment.Top : VerticalAlignment.Center;
            canvas.DrawString(PlaceholderText, rect.Left + inset, textTop, rect.Width - inset * 2, textHeight, HorizontalAlignment.Center, vertical);
        }
    }

    public void BeginStroke(PointF point)
    {
        activeStroke = new List<PointF> { point };
    }

    public void AddPoint(PointF point)
    {
        activeStroke?.Add(point);
    }

    /// <returns>True when the stroke was long enough to keep.</returns>
    public bool EndStroke()
    {
        var kept = activeStroke is { Count: >= 2 };
        if (kept)
            committedStrokes.Add(activeStroke!);

        activeStroke = null;
        return kept;
    }

    public void CancelStroke() => activeStroke = null;

    public bool Undo()
    {
        if (committedStrokes.Count == 0)
            return false;

        committedStrokes.RemoveAt(committedStrokes.Count - 1);
        return true;
    }

    public void Clear()
    {
        committedStrokes.Clear();
        activeStroke = null;
    }

    /// <param name="exportService">
    /// Draws the bitmap on targets without a built-in platform context (plain <c>net10.0</c>, which
    /// browser and desktop backends run on). Ignored on iOS, Mac Catalyst and Android.
    /// </param>
    public Stream ExportToPng(int width, int height, IBitmapExportService? exportService = null)
    {
        using var context = CreateExportContext(width, height, exportService);
        if (context is null)
            return Stream.Null;

        var canvas = context.Canvas;

        canvas.FillColor = BackgroundColor;
        canvas.FillRectangle(0, 0, width, height);

        // Strokes are stored in the on-screen canvas coordinate space, which is usually larger (and a
        // different aspect ratio) than the export bitmap. Scale them uniformly to fit and center, so the
        // whole signature is captured without clipping the bottom and without distorting the aspect ratio.
        var srcWidth = canvasWidth > 0 ? canvasWidth : width;
        var srcHeight = canvasHeight > 0 ? canvasHeight : height;
        var scale = Math.Min(width / srcWidth, height / srcHeight);
        var offsetX = (width - srcWidth * scale) / 2f;
        var offsetY = (height - srcHeight * scale) / 2f;
        var strokeWidth = Math.Max(StrokeWidth * scale, 1f);

        DrawStrokes(canvas, committedStrokes, scale, offsetX, offsetY, strokeWidth);

        return context.Image.AsStream(Microsoft.Maui.Graphics.ImageFormat.Png);
    }

    static BitmapExportContext? CreateExportContext(int width, int height, IBitmapExportService? exportService)
    {
#if IOS || MACCATALYST || ANDROID
        return new Microsoft.Maui.Graphics.Platform.PlatformBitmapExportContext(width, height, 1f);
#else
        // No platform context on this target; a backend that can draw bitmaps registers the service.
        return exportService?.CreateContext(width, height, 1f);
#endif
    }

    void DrawStrokes(ICanvas canvas, List<List<PointF>> strokes, float scale, float offsetX, float offsetY, float strokeWidth)
    {
        foreach (var stroke in strokes)
        {
            if (stroke.Count >= 2)
                DrawSingleStroke(canvas, stroke, scale, offsetX, offsetY, strokeWidth);
        }
    }

    void DrawSingleStroke(ICanvas canvas, IList<PointF> points, float scale, float offsetX, float offsetY, float strokeWidth)
    {
        canvas.StrokeColor = StrokeColor;
        canvas.StrokeSize = strokeWidth;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;

        var path = new PathF();
        path.MoveTo(points[0].X * scale + offsetX, points[0].Y * scale + offsetY);
        for (var i = 1; i < points.Count; i++)
            path.LineTo(points[i].X * scale + offsetX, points[i].Y * scale + offsetY);

        canvas.DrawPath(path);
    }
}
