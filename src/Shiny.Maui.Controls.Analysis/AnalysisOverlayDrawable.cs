namespace Shiny.Maui.Controls.Analysis;

/// <summary>
/// An <see cref="IDrawable"/> that paints analyzer-supplied <see cref="OverlayBox"/>es (and their captions) and the
/// analyzer's scan window over a camera preview or a video. Each box may carry its own stroke/text/fill color and
/// stroke width; whatever it leaves unset falls back to this drawable's defaults.
/// </summary>
/// <remarks>
/// Boxes arrive in normalized upright image space; this maps them onto the part of the view the picture actually
/// covers, using <see cref="ImageAspect"/> and <see cref="ScaleMode"/> (or <see cref="Stretch"/>). Usually you don't
/// use it directly: a <c>MediaElement</c> draws one itself, and a <c>CameraView</c> pairs with <c>CameraOverlayView</c>.
/// </remarks>
public class AnalysisOverlayDrawable : IDrawable
{
    /// <summary>Boxes to draw (normalized, upright image space).</summary>
    public IReadOnlyList<OverlayBox> Boxes { get; set; } = [];

    /// <summary>The analyzer's scan window (normalized, upright image space), or null when it scans the full frame.</summary>
    public RectF? ScanWindow { get; set; }

    /// <summary>Scrim color dimming the area outside <see cref="ScanWindow"/>; null draws no scrim.</summary>
    public Color? ScanWindowScrimColor { get; set; } = Color.FromRgba(0, 0, 0, 110);

    /// <summary>Viewfinder reticle color framing <see cref="ScanWindow"/>; null draws no outline.</summary>
    public Color? ScanWindowColor { get; set; } = Colors.White;

    /// <summary>Upright image aspect ratio (width / height) of the analyzed frame.</summary>
    public float ImageAspect { get; set; } = 1f;

    /// <summary>How the picture fills its view (must match the host's own scaling).</summary>
    public PreviewScaleMode ScaleMode { get; set; } = PreviewScaleMode.AspectFill;

    /// <summary>
    /// <c>true</c> when the picture is stretched to the view, ignoring its aspect (a <c>MediaElement</c> with
    /// <c>Aspect="Fill"</c>); overrides <see cref="ScaleMode"/>.
    /// </summary>
    public bool Stretch { get; set; }

    /// <summary>Default box stroke color used when an <see cref="OverlayBox.StrokeColor"/> is null.</summary>
    public Color BoxColor { get; set; } = Color.FromArgb("#22D3EE");

    /// <summary>Default caption color used when an <see cref="OverlayBox.TextColor"/> is null.</summary>
    public Color LabelColor { get; set; } = Colors.White;

    /// <summary>Default box stroke width used when an <see cref="OverlayBox.StrokeWidth"/> is 0.</summary>
    public float StrokeWidth { get; set; } = 3f;

    /// <summary>Default fill used when an <see cref="OverlayBox.FillColor"/> is null (null = no fill).</summary>
    public Color? FillColor { get; set; }

    /// <inheritdoc/>
    public virtual void Draw(ICanvas canvas, RectF dirtyRect)
    {
        this.DrawScanWindow(canvas, dirtyRect);
        this.DrawBoxes(canvas, dirtyRect);
    }

    /// <summary>Map a normalized upright rect into view pixels for a view of <paramref name="dirtyRect"/>'s size.</summary>
    protected RectF MapToView(RectF upright, RectF dirtyRect)
    {
        if (this.Stretch)
        {
            return new RectF(
                upright.X * dirtyRect.Width,
                upright.Y * dirtyRect.Height,
                upright.Width * dirtyRect.Width,
                upright.Height * dirtyRect.Height
            );
        }
        return CoordinateTransform.MapToView(upright, dirtyRect.Width, dirtyRect.Height, this.ImageAspect, this.ScaleMode);
    }

    void DrawBoxes(ICanvas canvas, RectF dirtyRect)
    {
        var boxes = this.Boxes;
        if (boxes.Count == 0)
            return;

        foreach (var b in boxes)
        {
            var r = this.MapToView(b.Rect, dirtyRect);

            if ((b.FillColor ?? this.FillColor) is { } fill)
            {
                canvas.FillColor = fill;
                canvas.FillRectangle(r);
            }

            canvas.StrokeColor = b.StrokeColor ?? this.BoxColor;
            canvas.StrokeSize = b.StrokeWidth > 0 ? b.StrokeWidth : this.StrokeWidth;
            canvas.DrawRectangle(r);

            if (!string.IsNullOrEmpty(b.Text))
            {
                canvas.FontColor = b.TextColor ?? this.LabelColor;
                canvas.FontSize = 14;
                canvas.DrawString(b.Text, r.X, Math.Max(0, r.Y - 18), r.Width <= 0 ? 200 : r.Width + 80, 18,
                    HorizontalAlignment.Left, VerticalAlignment.Top);
            }
        }
    }

    // Dim everything outside the analyzer's scan window and frame it with a viewfinder reticle.
    void DrawScanWindow(ICanvas canvas, RectF dirtyRect)
    {
        if (this.ScanWindow is not { } window)
            return;

        var w = this.MapToView(window, dirtyRect);

        if (this.ScanWindowScrimColor is { } scrim)
        {
            canvas.FillColor = scrim;
            // four bands around the window (top, bottom, left, right) — leaves the window itself clear
            canvas.FillRectangle(0, 0, dirtyRect.Width, w.Top);
            canvas.FillRectangle(0, w.Bottom, dirtyRect.Width, dirtyRect.Height - w.Bottom);
            canvas.FillRectangle(0, w.Top, w.Left, w.Height);
            canvas.FillRectangle(w.Right, w.Top, dirtyRect.Width - w.Right, w.Height);
        }

        if (this.ScanWindowColor is { } outline)
        {
            canvas.StrokeColor = outline;
            canvas.StrokeSize = 2f;
            canvas.DrawRectangle(w);
        }
    }
}
