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

    /// <summary>
    /// What a slide show is projected onto: black, edge to edge.
    /// </summary>
    /// <remarks>
    /// Not <see cref="Dark"/> with a smaller margin. A near-black surround is right for a viewer, where
    /// the chrome around the slide is still part of an app; on a projector any lift at all reads as a
    /// grey frame around the deck, and the border that separates a slide from its surround has nothing
    /// left to separate it from.
    /// </remarks>
    public static readonly SlideTheme Presentation = new()
    {
        Surround = new ArgbColor(255, 0, 0, 0),
        Border = new ArgbColor(255, 0, 0, 0)
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

    /// <summary>
    /// Draw each empty placeholder's prompt — "Click to add title" — inside a dashed outline.
    /// </summary>
    /// <remarks>
    /// The editor's to turn on. A viewer and a show leave it off, which is PowerPoint's behaviour: the
    /// prompts say where to click, and nobody is clicking in a slide show.
    /// </remarks>
    public bool ShowPlaceholderPrompts { get; init; }

    /// <summary>The shape whose prompt is suppressed because the caret is inside it, or -1.</summary>
    public int PromptHiddenShape { get; init; } = -1;

    /// <summary>
    /// Shapes mid-animation in a slide show — hidden, faded, flown, scaled — by index. Absent shapes
    /// are at rest.
    /// </summary>
    public IReadOnlyDictionary<int, ShapeAnimationState>? Animation { get; init; }

    /// <summary>
    /// Draw the play mark over audio and video. On in the editor and a show's still frames; a host that
    /// plays the clip in place turns it off while it plays.
    /// </summary>
    public bool ShowMediaOverlay { get; init; } = true;
}

/// <summary>The editor's overlay, in viewport coordinates.</summary>
public sealed record SlideEditorChrome
{
    public (double X, double Y, double Width, double Height)? SelectionFrame { get; init; }

    /// <summary>The primary selection's rotation in degrees, which the frame and handles follow.</summary>
    public double SelectionRotation { get; init; }

    public IReadOnlyList<(double X, double Y, double Width, double Height)> Handles { get; init; } = [];

    /// <summary>The round grip that turns the selection, when it can turn.</summary>
    public (double X, double Y, double Width, double Height)? RotationHandle { get; init; }

    /// <summary>The frames of the rest of a multi-selection, each with its rotation.</summary>
    public IReadOnlyList<((double X, double Y, double Width, double Height) Frame, double Rotation)> OtherFrames { get; init; } = [];

    /// <summary>A marquee being dragged out.</summary>
    public (double X, double Y, double Width, double Height)? Marquee { get; init; }

    /// <summary>Smart guides: lines from (X1, Y1) to (X2, Y2).</summary>
    public IReadOnlyList<(double X1, double Y1, double X2, double Y2)> Guides { get; init; } = [];

    /// <summary>The slide's rectangle, for gridlines, static guides and rulers.</summary>
    public (double X, double Y, double Width, double Height)? SlideBounds { get; init; }

    /// <summary>Gridline spacing in viewport pixels; zero for none.</summary>
    public double GridSpacing { get; init; }

    public bool ShowStaticGuides { get; init; }

    /// <summary>Viewport pixels per inch, for the ruler's ticks; zero for no ruler.</summary>
    public double RulerPixelsPerInch { get; init; }

    /// <summary>The numbered animation markers beside animated shapes.</summary>
    public IReadOnlyList<(string Label, (double X, double Y, double Width, double Height) Rect)> AnimationMarkers { get; init; } = [];

    /// <summary>Selected table cells, washed in the selection colour.</summary>
    public IReadOnlyList<(double X, double Y, double Width, double Height)> CellSelection { get; init; } = [];

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

    /// <summary>The smart guides' colour — PowerPoint's red-orange.</summary>
    public ArgbColor GuideColor { get; init; } = new(255, 0xE8, 0x4C, 0x3D);
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
    readonly SlideChartPainter charts = new(measurer);

    /// <summary>The measurer this painter lays text out with.</summary>
    public SkiaTextMeasurer Measurer => measurer;

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

        var shapes = request.Slide.Shapes;
        for (var i = 0; i < shapes.Count; i++)
        {
            var state = request.Animation is { } animation && animation.TryGetValue(i, out var s) ? s : ShapeAnimationState.Rest;
            if (!state.Visible)
                continue;

            this.PaintAnimated(canvas, shapes[i], theme, state, request);

            if (request.ShowPlaceholderPrompts && shapes[i].Prompt is { } prompt && i != request.PromptHiddenShape)
                this.PaintPrompt(canvas, shapes[i], prompt, scaleX);
        }

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

    /// <summary>A shape with its animation state applied: faded, moved, scaled, turned, wiped.</summary>
    void PaintAnimated(SKCanvas canvas, SlideShape shape, SlideTheme theme, ShapeAnimationState state, SlidePaintRequest request)
    {
        if (state.IsRest)
        {
            this.PaintShape(canvas, shape, theme, request);
            return;
        }

        var cx = (float)(shape.X + shape.Width / 2);
        var cy = (float)(shape.Y + shape.Height / 2);

        canvas.Save();

        if (state.Reveal < 1)
        {
            var reveal = (float)Math.Clamp(state.Reveal, 0, 1);
            var (x, y, w, h) = ((float)shape.X, (float)shape.Y, (float)shape.Width, (float)shape.Height);

            var clip = state.RevealFrom switch
            {
                SlideTransitionDirection.FromTop => new SKRect(x, y, x + w, y + h * reveal),
                SlideTransitionDirection.FromLeft => new SKRect(x, y, x + w * reveal, y + h),
                SlideTransitionDirection.FromRight => new SKRect(x + w * (1 - reveal), y, x + w, y + h),
                _ => new SKRect(x, y + h * (1 - reveal), x + w, y + h)
            };

            // Generous so a shadow or a thick outline is not cut at the far edges.
            clip.Inflate(clip.Width == w ? 20 : 0, clip.Height == h ? 20 : 0);
            canvas.ClipRect(clip);
        }

        canvas.Translate((float)state.OffsetX, (float)state.OffsetY);

        if (Math.Abs(state.Scale - 1) > 0.0001)
            canvas.Scale((float)state.Scale, (float)state.Scale, cx, cy);

        if (state.Rotation != 0)
            canvas.RotateDegrees((float)state.Rotation, cx, cy);

        if (state.Opacity < 0.999)
        {
            using var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(state.Opacity, 0, 1) * 255)) };
            canvas.SaveLayer(layer);
            this.PaintShape(canvas, shape, theme, request);
            canvas.Restore();
        }
        else
        {
            this.PaintShape(canvas, shape, theme, request);
        }

        canvas.Restore();
    }

    /// <summary>Draws the editor's selection frame, handles, text highlight and caret.</summary>
    void PaintChrome(SKCanvas canvas, SlideEditorChrome chrome)
    {
        this.fill.Shader = null;

        if (chrome.SlideBounds is { } slideBounds)
            this.PaintGridAndGuides(canvas, chrome, Rect(slideBounds));

        // Text highlight goes underneath the frame so the frame stays legible over it.
        this.fill.Color = ToSk(chrome.FindMatchFill);
        foreach (var rect in chrome.FindMatches)
            canvas.DrawRect(Rect(rect), this.fill);

        this.fill.Color = ToSk(chrome.SelectionFill);

        foreach (var rect in chrome.CellSelection)
            canvas.DrawRect(Rect(rect), this.fill);

        foreach (var rect in chrome.TextSelection)
            canvas.DrawRect(Rect(rect), this.fill);

        this.stroke.Color = ToSk(chrome.Accent);
        this.stroke.StrokeWidth = 1.5f;

        // The rest of a multi-selection: dashed frames, no handles.
        foreach (var (frame, rotation) in chrome.OtherFrames)
        {
            this.stroke.PathEffect?.Dispose();
            this.stroke.PathEffect = SKPathEffect.CreateDash([4f, 3f], 0);
            this.DrawRotatedRect(canvas, Rect(frame), rotation);
        }

        this.stroke.PathEffect?.Dispose();
        this.stroke.PathEffect = null;

        if (chrome.SelectionFrame is { } selection)
        {
            var frame = Rect(selection);

            // Dashed while the shape itself is selected, solid once the caret is inside its text -
            // the same distinction PowerPoint draws, and the only cue that typing will go somewhere.
            this.stroke.PathEffect = chrome.IsEditingText ? null : SKPathEffect.CreateDash([4f, 3f], 0);
            this.DrawRotatedRect(canvas, frame, chrome.SelectionRotation);
            this.stroke.PathEffect?.Dispose();
            this.stroke.PathEffect = null;

            if (chrome.RotationHandle is { } rotate)
            {
                // A stalk from the top edge to the grip, in the frame's rotation.
                var r = Rect(rotate);
                canvas.Save();
                canvas.RotateDegrees((float)chrome.SelectionRotation, frame.MidX, frame.MidY);
                canvas.DrawLine(frame.MidX, frame.Top, frame.MidX, frame.Top - (float)SlideEditorController.RotateHandleOffset + r.Height / 2, this.stroke);
                canvas.Restore();

                this.fill.Color = SKColors.White;
                canvas.DrawOval(r, this.fill);
                canvas.DrawOval(r, this.stroke);

                // A small turning arrow inside the grip.
                using var arc = new SKPath();
                var inner = SKRect.Inflate(r, -r.Width * 0.28f, -r.Height * 0.28f);
                arc.AddArc(inner, -60, 280);
                canvas.DrawPath(arc, this.stroke);
            }
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

        // Smart guides, over everything but the caret.
        if (chrome.Guides.Count > 0)
        {
            this.stroke.Color = ToSk(chrome.GuideColor);
            this.stroke.StrokeWidth = 1;
            this.stroke.PathEffect = SKPathEffect.CreateDash([5f, 3f], 0);

            foreach (var (x1, y1, x2, y2) in chrome.Guides)
                canvas.DrawLine((float)x1, (float)y1, (float)x2, (float)y2, this.stroke);

            this.stroke.PathEffect.Dispose();
            this.stroke.PathEffect = null;
        }

        if (chrome.Marquee is { } marquee)
        {
            var rect = Rect(marquee);
            this.fill.Color = ToSk(chrome.SelectionFill).WithAlpha(40);
            canvas.DrawRect(rect, this.fill);

            this.stroke.Color = ToSk(chrome.Accent);
            this.stroke.StrokeWidth = 1;
            this.stroke.PathEffect = SKPathEffect.CreateDash([3f, 3f], 0);
            canvas.DrawRect(rect, this.stroke);
            this.stroke.PathEffect.Dispose();
            this.stroke.PathEffect = null;
        }

        if (chrome.AnimationMarkers.Count > 0)
        {
            using var font = new SKFont(SKTypeface.Default, 10);
            foreach (var (label, bounds) in chrome.AnimationMarkers)
            {
                var rect = Rect(bounds);
                this.fill.Color = new SKColor(0xF2, 0xF2, 0xF2);
                canvas.DrawRect(rect, this.fill);

                this.stroke.Color = new SKColor(0x70, 0x70, 0x70);
                this.stroke.StrokeWidth = 1;
                canvas.DrawRect(rect, this.stroke);

                this.fill.Color = new SKColor(0x30, 0x30, 0x30);
                canvas.DrawText(label, rect.MidX, rect.MidY + 3.5f, SKTextAlign.Center, font, this.fill);
            }
        }

        if (chrome.Caret is { } caret)
        {
            this.fill.Color = ToSk(chrome.Accent);
            canvas.DrawRect(Rect(caret), this.fill);
        }

        static SKRect Rect((double X, double Y, double Width, double Height) r)
            => new((float)r.X, (float)r.Y, (float)(r.X + r.Width), (float)(r.Y + r.Height));
    }

    void DrawRotatedRect(SKCanvas canvas, SKRect rect, double rotation)
    {
        if (rotation == 0)
        {
            canvas.DrawRect(rect, this.stroke);
            return;
        }

        canvas.Save();
        canvas.RotateDegrees((float)rotation, rect.MidX, rect.MidY);
        canvas.DrawRect(rect, this.stroke);
        canvas.Restore();
    }

    /// <summary>Gridlines, the static centre guides and the rulers, all under the selection.</summary>
    void PaintGridAndGuides(SKCanvas canvas, SlideEditorChrome chrome, SKRect slide)
    {
        if (chrome.GridSpacing > 2)
        {
            this.stroke.Color = new SKColor(0x80, 0x80, 0x80, 90);
            this.stroke.StrokeWidth = 1;
            this.stroke.PathEffect = SKPathEffect.CreateDash([1f, 3f], 0);

            var step = (float)chrome.GridSpacing;
            for (var x = slide.Left + step; x < slide.Right - 0.5f; x += step)
                canvas.DrawLine(x, slide.Top, x, slide.Bottom, this.stroke);

            for (var y = slide.Top + step; y < slide.Bottom - 0.5f; y += step)
                canvas.DrawLine(slide.Left, y, slide.Right, y, this.stroke);

            this.stroke.PathEffect.Dispose();
            this.stroke.PathEffect = null;
        }

        if (chrome.ShowStaticGuides)
        {
            this.stroke.Color = new SKColor(0x9A, 0x9A, 0x9A, 200);
            this.stroke.StrokeWidth = 1;
            this.stroke.PathEffect = SKPathEffect.CreateDash([4f, 4f], 0);
            canvas.DrawLine(slide.MidX, slide.Top, slide.MidX, slide.Bottom, this.stroke);
            canvas.DrawLine(slide.Left, slide.MidY, slide.Right, slide.MidY, this.stroke);
            this.stroke.PathEffect.Dispose();
            this.stroke.PathEffect = null;
        }

        if (chrome.RulerPixelsPerInch > 1)
            this.PaintRulers(canvas, slide, chrome.RulerPixelsPerInch);
    }

    /// <summary>
    /// PowerPoint's rulers: inches out from the slide's centre, along the top and down the left.
    /// </summary>
    void PaintRulers(SKCanvas canvas, SKRect slide, double pixelsPerInch)
    {
        const float thickness = 16;
        var clip = canvas.LocalClipBounds;

        this.fill.Color = new SKColor(0xF3, 0xF3, 0xF3);
        canvas.DrawRect(new SKRect(clip.Left, clip.Top, clip.Right, clip.Top + thickness), this.fill);
        canvas.DrawRect(new SKRect(clip.Left, clip.Top, clip.Left + thickness, clip.Bottom), this.fill);

        this.fill.Color = new SKColor(0xFF, 0xFF, 0xFF);
        canvas.DrawRect(new SKRect(slide.Left, clip.Top + 2, slide.Right, clip.Top + thickness - 2), this.fill);
        canvas.DrawRect(new SKRect(clip.Left + 2, slide.Top, clip.Left + thickness - 2, slide.Bottom), this.fill);

        this.stroke.Color = new SKColor(0x60, 0x60, 0x60);
        this.stroke.StrokeWidth = 1;
        using var font = new SKFont(SKTypeface.Default, 8);
        this.fill.Color = new SKColor(0x40, 0x40, 0x40);

        var eighth = (float)(pixelsPerInch / 8);
        var halfWidth = (int)Math.Ceiling(slide.Width / 2 / eighth);
        for (var i = -halfWidth; i <= halfWidth; i++)
        {
            var x = slide.MidX + i * eighth;
            if (x < slide.Left - 0.5f || x > slide.Right + 0.5f)
                continue;

            var length = i % 8 == 0 ? 6f : i % 4 == 0 ? 4f : 2f;
            canvas.DrawLine(x, clip.Top + thickness - 2 - length, x, clip.Top + thickness - 2, this.stroke);

            if (i % 8 == 0 && i != 0)
                canvas.DrawText(Math.Abs(i / 8).ToString(System.Globalization.CultureInfo.InvariantCulture), x, clip.Top + 9, SKTextAlign.Center, font, this.fill);
        }

        var halfHeight = (int)Math.Ceiling(slide.Height / 2 / eighth);
        for (var i = -halfHeight; i <= halfHeight; i++)
        {
            var y = slide.MidY + i * eighth;
            if (y < slide.Top - 0.5f || y > slide.Bottom + 0.5f)
                continue;

            var length = i % 8 == 0 ? 6f : i % 4 == 0 ? 4f : 2f;
            canvas.DrawLine(clip.Left + thickness - 2 - length, y, clip.Left + thickness - 2, y, this.stroke);

            if (i % 8 == 0 && i != 0)
                canvas.DrawText(Math.Abs(i / 8).ToString(System.Globalization.CultureInfo.InvariantCulture), clip.Left + 6, y + 3, SKTextAlign.Center, font, this.fill);
        }
    }

    void PaintBackground(SKCanvas canvas, SlidePaintRequest request, SlideTheme theme)
    {
        var bounds = new SKRect(0, 0, (float)request.SlideWidth, (float)request.SlideHeight);

        this.fill.Color = ToSk(theme.SlideBackground);
        this.fill.Shader = null;
        canvas.DrawRect(bounds, this.fill);

        var background = request.Slide.Background;
        if (background.IsEmpty)
        {
            // Under the shapes, over the slide's own ground: a watermark marks the slide, and anything
            // authored on it belongs in front.
            WatermarkPainter.Draw(canvas, bounds, request.Watermark);
            return;
        }

        if (background.Image is { } picture)
        {
            this.DrawImage(canvas, picture, bounds);
        }
        else
        {
            ShapePainting.ApplyFill(this.fill, background, bounds);
            canvas.DrawRect(bounds, this.fill);
            this.fill.Shader?.Dispose();
            this.fill.Shader = null;
        }

        WatermarkPainter.Draw(canvas, bounds, request.Watermark);
    }

    void PaintShape(SKCanvas canvas, SlideShape shape, SlideTheme theme, SlidePaintRequest request)
    {
        // A group's own entry is bounds for selecting the whole group; its children paint themselves.
        if (shape.IsGroup)
            return;

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

        // The shadow first, as a layer of its own under the shape: drawing the shape once through a
        // shadow-only filter gives the exact outline, for a picture as for a star.
        if (shape.Shadow is { } shadow)
        {
            var sigma = (float)Math.Max(0.1, shadow.Blur / 2);
            using var shadowPaint = new SKPaint
            {
                ImageFilter = SKImageFilter.CreateDropShadowOnly((float)shadow.OffsetX, (float)shadow.OffsetY, sigma, sigma, ToSk(shadow.Color))
            };

            canvas.SaveLayer(shadowPaint);
            this.PaintBody(canvas, shape, bounds, theme);
            canvas.Restore();
        }

        this.PaintBody(canvas, shape, bounds, theme);

        if (shape.Media is { } media && request.ShowMediaOverlay)
            this.PaintMediaMark(canvas, bounds, media.IsVideo);

        if (shape.Text is { } text)
        {
            if (shape.TextDirection == ShapeTextDirection.Horizontal)
            {
                ShapeTextPainter.Draw(canvas, this.fill, this.stroke, measurer, text, bounds);
            }
            else
            {
                // Vertical text is the text of a shape turned a quarter: laid out across the height.
                canvas.Save();
                canvas.RotateDegrees(shape.TextDirection == ShapeTextDirection.Rotate90 ? 90 : 270, bounds.MidX, bounds.MidY);
                var turned = new SKRect(bounds.MidX - bounds.Height / 2, bounds.MidY - bounds.Width / 2, bounds.MidX + bounds.Height / 2, bounds.MidY + bounds.Width / 2);
                ShapeTextPainter.Draw(canvas, this.fill, this.stroke, measurer, text, turned);
                canvas.Restore();
            }
        }

        canvas.Restore();
    }

    /// <summary>The shape's own artwork — picture, table, chart or geometry — with no text.</summary>
    void PaintBody(SKCanvas canvas, SlideShape shape, SKRect bounds, SlideTheme theme)
    {
        if (shape.Image is { } image)
        {
            this.DrawImage(canvas, image, bounds);

            if (shape.Outline is { } outline)
                ShapePainting.DrawShape(canvas, this.fill, this.stroke, ShapeGeometry.Rectangle, bounds, null, outline);
        }
        else if (shape.Table is { } table)
        {
            this.PaintTable(canvas, table, bounds, theme);
        }
        else if (shape.Chart is { } chart)
        {
            this.charts.Paint(canvas, chart, bounds);
        }
        else
        {
            ShapePainting.DrawShape(
                canvas, this.fill, this.stroke, shape.Geometry, bounds, shape.Fill, shape.Outline, shape.CornerRadius);
        }
    }

    /// <summary>The play mark over a video's poster, or a speaker for audio.</summary>
    void PaintMediaMark(SKCanvas canvas, SKRect bounds, bool video)
    {
        var radius = Math.Min(bounds.Width, bounds.Height) * (video ? 0.14f : 0.32f);
        radius = Math.Clamp(radius, 10, 48);

        this.fill.Shader = null;
        this.fill.Color = new SKColor(0, 0, 0, 140);
        canvas.DrawCircle(bounds.MidX, bounds.MidY, radius, this.fill);

        this.fill.Color = SKColors.White;
        using var mark = new SKPath();
        if (video)
        {
            mark.MoveTo(bounds.MidX - radius * 0.35f, bounds.MidY - radius * 0.5f);
            mark.LineTo(bounds.MidX + radius * 0.55f, bounds.MidY);
            mark.LineTo(bounds.MidX - radius * 0.35f, bounds.MidY + radius * 0.5f);
        }
        else
        {
            mark.MoveTo(bounds.MidX - radius * 0.5f, bounds.MidY - radius * 0.2f);
            mark.LineTo(bounds.MidX - radius * 0.2f, bounds.MidY - radius * 0.2f);
            mark.LineTo(bounds.MidX + radius * 0.25f, bounds.MidY - radius * 0.55f);
            mark.LineTo(bounds.MidX + radius * 0.25f, bounds.MidY + radius * 0.55f);
            mark.LineTo(bounds.MidX - radius * 0.2f, bounds.MidY + radius * 0.2f);
            mark.LineTo(bounds.MidX - radius * 0.5f, bounds.MidY + radius * 0.2f);
        }

        mark.Close();
        canvas.DrawPath(mark, this.fill);
    }

    /// <summary>An empty placeholder's dashed outline and prompt text, in slide coordinates.</summary>
    void PaintPrompt(SKCanvas canvas, SlideShape shape, ShapeTextBody prompt, double scale)
    {
        var bounds = new SKRect((float)shape.X, (float)shape.Y, (float)(shape.X + shape.Width), (float)(shape.Y + shape.Height));

        // A hairline on screen at any zoom: the canvas is scaled to the slide, so the stroke is
        // divided back out.
        var hairline = (float)(1 / Math.Max(0.01, scale));

        this.stroke.Color = new SKColor(0x9A, 0x9A, 0x9A);
        this.stroke.StrokeWidth = hairline;
        this.stroke.PathEffect?.Dispose();
        this.stroke.PathEffect = SKPathEffect.CreateDash([4 * hairline, 3 * hairline], 0);
        canvas.DrawRect(bounds, this.stroke);
        this.stroke.PathEffect.Dispose();
        this.stroke.PathEffect = null;

        ShapeTextPainter.Draw(canvas, this.fill, this.stroke, measurer, prompt, bounds);
    }

    void PaintTable(SKCanvas canvas, SlideTable table, SKRect bounds, SlideTheme theme)
    {
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];

            for (var c = 0; c < row.Count; c++)
            {
                var cell = row[c];
                if (cell.IsMerged)
                    continue;

                // The shared cell geometry, the same the editor lays its caret out in. Summing widths
                // per cell here used to advance past a spanned column twice, shifting every cell after a
                // merge to the right.
                var (x, y, w, h) = table.CellBounds(r, c, bounds.Width, bounds.Height);
                var rect = new SKRect(bounds.Left + (float)x, bounds.Top + (float)y, bounds.Left + (float)(x + w), bounds.Top + (float)(y + h));

                if (cell.Fill is { } cellFill)
                {
                    this.fill.Color = ToSk(cellFill);
                    this.fill.Shader = null;
                    canvas.DrawRect(rect, this.fill);
                }

                if (cell.Text is { } text)
                    ShapeTextPainter.Draw(canvas, this.fill, this.stroke, measurer, text, rect);

                this.stroke.Color = cell.Fill is not null && table.StyleId is not null ? SKColors.White : ToSk(theme.Border);
                this.stroke.StrokeWidth = 1;
                canvas.DrawRect(rect, this.stroke);
            }
        }
    }

    void DrawImage(SKCanvas canvas, byte[] data, SKRect destination)
    {
        var key = System.HashCode.Combine(data.Length, data.Length > 0 ? data[0] : 0, data.Length > 64 ? data[64] : 0, data.Length > 512 ? data[512] : 0);

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
        this.charts.Dispose();
    }

    static SKColor ToSk(ArgbColor color) => new(color.R, color.G, color.B, color.A);
}
