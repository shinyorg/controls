using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using SkiaSharp;
using Shiny.Controls.Office.Theming;

namespace Shiny.Controls.Office.Skia;

public sealed record SlideTheme
{
    public static readonly SlideTheme Light = new();

    /// <summary>
    /// Dark mode darkens the surround only.
    /// </summary>
    /// <remarks>
    /// A slide is a fixed artboard with authored colours, like a photograph — inverting it would show
    /// the deck's own black text on a dark background and misrepresent what the author made. PowerPoint's
    /// own dark mode leaves slides alone for the same reason. <see cref="SlideBackground"/> stays light
    /// because it is only the fallback for a deck that specifies no background of its own.
    /// </remarks>
    public static readonly SlideTheme Dark = new()
    {
        Surround = new ArgbColor(255, 0x14, 0x14, 0x14),
        Border = new ArgbColor(255, 0x44, 0x44, 0x44)
    };

    public ArgbColor Surround { get; init; } = new(255, 0x2B, 0x2B, 0x2E);
    public ArgbColor SlideBackground { get; init; } = new(255, 255, 255, 255);
    public ArgbColor DefaultText { get; init; } = new(255, 0x1A, 0x1A, 0x1A);
    public ArgbColor Border { get; init; } = new(255, 0xBB, 0xBB, 0xBB);
}

public sealed record SlidePaintRequest
{
    /// <summary>A picture drawn behind the slide, under everything on it.</summary>
    public OfficeWatermark? Watermark { get; init; }

    public required Slide Slide { get; init; }

    /// <summary>Slide dimensions in slide coordinates, before fitting.</summary>
    public required double SlideWidth { get; init; }

    public required double SlideHeight { get; init; }

    /// <summary>Destination rectangle in viewport coordinates.</summary>
    public required double DestinationX { get; init; }

    public required double DestinationY { get; init; }
    public required double DestinationWidth { get; init; }
    public required double DestinationHeight { get; init; }

    public SlideTheme Theme { get; init; } = SlideTheme.Light;
    public float Scale { get; init; } = 1f;
    public bool DrawBorder { get; init; } = true;

    /// <summary>
    /// The editor's chrome: selection frame, resize handles, text selection and caret.
    /// </summary>
    /// <remarks>
    /// All in viewport coordinates, and drawn <em>outside</em> the slide's fit transform. Drawing a
    /// handle inside it would scale the handle with the slide, so a zoomed-out deck would have grab
    /// targets too small to hit.
    /// </remarks>
    public SlideEditorChrome? Chrome { get; init; }
}

/// <summary>The editor's overlay, in viewport coordinates.</summary>
public sealed record SlideEditorChrome
{
    public (double X, double Y, double Width, double Height)? SelectionFrame { get; init; }

    public IReadOnlyList<(double X, double Y, double Width, double Height)> Handles { get; init; } = [];

    public IReadOnlyList<(double X, double Y, double Width, double Height)> TextSelection { get; init; } = [];

    /// <summary>
    /// Find matches on the slide being shown, in viewport coordinates.
    /// </summary>
    /// <remarks>
    /// Drawn under the text selection, so the hit the arrows are on carries both washes and reads as
    /// the current one.
    /// </remarks>
    public IReadOnlyList<(double X, double Y, double Width, double Height)> FindMatches { get; init; } = [];

    public (double X, double Y, double Width, double Height)? Caret { get; init; }

    /// <summary>Drawn dashed when the shape is selected but its text is not being edited.</summary>
    public bool IsEditingText { get; init; }

    public ArgbColor Accent { get; init; } = new(255, 0x2F, 0x6F, 0xED);

    public ArgbColor SelectionFill { get; init; } = new(90, 0x2F, 0x6F, 0xED);

    /// <summary>
    /// Wash over every find match.
    /// </summary>
    /// <remarks>
    /// Not the selection's colour, because the match the arrows are on is drawn as the selection too:
    /// one hit is where you are and the rest are where you could go, and one colour cannot say both.
    /// </remarks>
    public ArgbColor FindMatchFill { get; init; } = new(120, 0xFF, 0xC1, 0x07);
}

/// <summary>
/// Paints one slide, scaled to fit a destination rectangle.
/// </summary>
/// <remarks>
/// Slides are fixed-size artboards, so unlike the reflowing document view this scales rather than
/// re-lays-out. Everything inside is drawn in slide coordinates and a single transform does the fit,
/// which keeps text proportions exactly as authored at any zoom.
/// </remarks>
public sealed class SlidePainter(SkiaTextMeasurer measurer) : IDisposable
{
    readonly TextLayoutEngine layout = new(measurer);
    readonly SKPaint fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    readonly SKPaint stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    readonly Dictionary<int, SKImage?> images = new();

    /// <summary>Space between a bullet glyph and the text it introduces.</summary>

    public void Paint(SKCanvas canvas, SlidePaintRequest request)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(request);

        var theme = request.Theme;
        canvas.Save();
        canvas.Scale(request.Scale);

        var destination = new SKRect(
            (float)request.DestinationX,
            (float)request.DestinationY,
            (float)(request.DestinationX + request.DestinationWidth),
            (float)(request.DestinationY + request.DestinationHeight));

        canvas.Save();
        canvas.ClipRect(destination);
        canvas.Translate(destination.Left, destination.Top);

        var scaleX = request.SlideWidth <= 0 ? 1 : request.DestinationWidth / request.SlideWidth;
        var scaleY = request.SlideHeight <= 0 ? 1 : request.DestinationHeight / request.SlideHeight;
        canvas.Scale((float)scaleX, (float)scaleY);

        this.PaintBackground(canvas, request, theme);

        foreach (var shape in request.Slide.Shapes)
            this.PaintShape(canvas, shape, theme);

        canvas.Restore();

        if (request.DrawBorder)
        {
            this.stroke.Color = ToSk(theme.Border);
            this.stroke.StrokeWidth = 1;
            canvas.DrawRect(destination, this.stroke);
        }

        if (request.Chrome is { } chrome)
            this.PaintChrome(canvas, chrome);

        canvas.Restore();
    }

    /// <summary>Draws the editor's selection frame, handles, text highlight and caret.</summary>
    void PaintChrome(SKCanvas canvas, SlideEditorChrome chrome)
    {
        // Text highlight goes underneath the frame so the frame stays legible over it.
        this.fill.Shader = null;

        this.fill.Color = ToSk(chrome.FindMatchFill);
        foreach (var rect in chrome.FindMatches)
            canvas.DrawRect(Rect(rect), this.fill);

        this.fill.Color = ToSk(chrome.SelectionFill);

        foreach (var rect in chrome.TextSelection)
            canvas.DrawRect(Rect(rect), this.fill);

        if (chrome.SelectionFrame is { } frame)
        {
            this.stroke.Color = ToSk(chrome.Accent);
            this.stroke.StrokeWidth = 1.5f;

            // Dashed while the shape itself is selected, solid once the caret is inside its text -
            // the same distinction PowerPoint draws, and the only cue that typing will go somewhere.
            this.stroke.PathEffect?.Dispose();
            this.stroke.PathEffect = chrome.IsEditingText
                ? null
                : SKPathEffect.CreateDash([4f, 3f], 0);

            canvas.DrawRect(Rect(frame), this.stroke);

            this.stroke.PathEffect?.Dispose();
            this.stroke.PathEffect = null;
        }

        foreach (var handle in chrome.Handles)
        {
            var rect = Rect(handle);

            this.fill.Color = SKColors.White;
            canvas.DrawRect(rect, this.fill);

            this.stroke.Color = ToSk(chrome.Accent);
            this.stroke.StrokeWidth = 1.5f;
            canvas.DrawRect(rect, this.stroke);
        }

        if (chrome.Caret is { } caret)
        {
            this.fill.Color = ToSk(chrome.Accent);
            canvas.DrawRect(Rect(caret), this.fill);
        }

        static SKRect Rect((double X, double Y, double Width, double Height) r)
            => new((float)r.X, (float)r.Y, (float)(r.X + r.Width), (float)(r.Y + r.Height));
    }

    void PaintBackground(SKCanvas canvas, SlidePaintRequest request, SlideTheme theme)
    {
        var bounds = new SKRect(0, 0, (float)request.SlideWidth, (float)request.SlideHeight);

        this.fill.Color = ToSk(theme.SlideBackground);
        this.fill.Shader = null;
        canvas.DrawRect(bounds, this.fill);

        if (request.Slide.Background.IsEmpty)
        {
            // Under the shapes, over the slide's own ground: a watermark marks the slide, and anything
            // authored on it belongs in front.
            WatermarkPainter.Draw(canvas, bounds, request.Watermark);
            return;
        }

        ShapePainting.ApplyFill(this.fill, request.Slide.Background, bounds);
        canvas.DrawRect(bounds, this.fill);
        this.fill.Shader = null;

        WatermarkPainter.Draw(canvas, bounds, request.Watermark);
    }

    void PaintShape(SKCanvas canvas, SlideShape shape, SlideTheme theme)
    {
        var bounds = new SKRect((float)shape.X, (float)shape.Y, (float)(shape.X + shape.Width), (float)(shape.Y + shape.Height));

        canvas.Save();

        if (shape.Rotation != 0)
            canvas.RotateDegrees((float)shape.Rotation, bounds.MidX, bounds.MidY);

        // Flips are expressed as a scale about the shape's own centre.
        if (shape.FlipHorizontal || shape.FlipVertical)
        {
            canvas.Translate(bounds.MidX, bounds.MidY);
            canvas.Scale(shape.FlipHorizontal ? -1 : 1, shape.FlipVertical ? -1 : 1);
            canvas.Translate(-bounds.MidX, -bounds.MidY);
        }

        if (shape.Image is { } image)
        {
            this.DrawImage(canvas, image, bounds);
        }
        else if (shape.Table is { } table)
        {
            this.PaintTable(canvas, table, bounds, theme);
        }
        else
        {
            ShapePainting.DrawShape(
                canvas, this.fill, this.stroke, shape.Geometry, bounds, shape.Fill, shape.Outline, shape.CornerRadius);
        }

        if (shape.Text is { } text)
            ShapeTextPainter.Draw(canvas, this.fill, this.stroke, measurer, text, bounds);

        canvas.Restore();
    }


    void PaintTable(SKCanvas canvas, SlideTable table, SKRect bounds, SlideTheme theme)
    {
        var columnWidths = Distribute(table.ColumnWidths, bounds.Width);
        var rowHeights = Distribute(table.RowHeights, bounds.Height);

        var y = bounds.Top;
        for (var r = 0; r < table.Rows.Count && r < rowHeights.Count; r++)
        {
            var x = bounds.Left;
            var row = table.Rows[r];

            for (var c = 0; c < row.Count && c < columnWidths.Count; c++)
            {
                var cell = row[c];
                var span = Math.Max(1, cell.ColumnSpan);
                var cellWidth = 0f;
                for (var i = c; i < Math.Min(c + span, columnWidths.Count); i++)
                    cellWidth += (float)columnWidths[i];

                var rect = new SKRect(x, y, x + cellWidth, y + (float)rowHeights[r]);

                if (!cell.IsMerged)
                {
                    if (cell.Fill is { } cellFill)
                    {
                        this.fill.Color = ToSk(cellFill);
                        this.fill.Shader = null;
                        canvas.DrawRect(rect, this.fill);
                    }

                    if (cell.Text is { } text)
                        ShapeTextPainter.Draw(canvas, this.fill, this.stroke, measurer, text, rect);

                    this.stroke.Color = ToSk(theme.Border);
                    this.stroke.StrokeWidth = 1;
                    canvas.DrawRect(rect, this.stroke);
                }

                x += cellWidth;
            }

            y += (float)rowHeights[r];
        }
    }

    /// <summary>Scales stored track sizes to the destination, sharing evenly when none are recorded.</summary>
    static List<double> Distribute(IReadOnlyList<double> sizes, double available)
    {
        if (sizes.Count == 0)
            return [];

        var total = sizes.Sum();
        if (total <= 0)
            return Enumerable.Repeat(available / sizes.Count, sizes.Count).ToList();

        var scale = available / total;
        return sizes.Select(x => x * scale).ToList();
    }

    void DrawImage(SKCanvas canvas, byte[] data, SKRect destination)
    {
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

            this.images[key] = image;
        }

        if (image is null)
            return;

        canvas.DrawImage(image, destination, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    public void Dispose()
    {
        foreach (var image in this.images.Values)
            image?.Dispose();

        this.images.Clear();
        this.fill.Shader?.Dispose();
        this.fill.Dispose();
        this.stroke.Dispose();
    }

    static SKColor ToSk(ArgbColor color) => new(color.R, color.G, color.B, color.A);
}
