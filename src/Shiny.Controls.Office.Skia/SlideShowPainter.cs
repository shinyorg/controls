using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Theming;
using SkiaSharp;

namespace Shiny.Controls.Office.Skia;

/// <summary>
/// Paints one frame of a slide show: the slide with its animations where they are, the transition from
/// the last slide while it runs, the black and white screens, and the end screen.
/// </summary>
/// <remarks>
/// Both hosts' shows draw through this, so a Push looks the same in a browser and on a phone. It takes
/// a <see cref="SlideShowFrame"/> — a moment the <see cref="SlideShowController"/> has already worked
/// out — and draws only that.
/// </remarks>
public sealed class SlideShowPainter(SlidePainter slides) : IDisposable
{
    readonly SKPaint fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

    /// <summary>The text on the end screen, PowerPoint's own.</summary>
    public string EndText { get; set; } = "End of slide show, click to exit.";

    /// <param name="width">Viewport width in logical pixels.</param>
    /// <param name="height">Viewport height in logical pixels.</param>
    /// <param name="scale">Device pixels per logical pixel.</param>
    public void Paint(SKCanvas canvas, SlideShowFrame? frame, SlideDeck deck, double width, double height, float scale, OfficeWatermark? watermark = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(deck);

        canvas.Clear(SKColors.Black);
        if (frame is null)
            return;

        canvas.Save();
        canvas.Scale(scale);
        this.PaintFrame(canvas, frame, deck, width, height, watermark);
        canvas.Restore();
    }

    /// <summary>
    /// A frame inside a rectangle of a larger surface — the editor's slide area during a Preview — in
    /// logical pixels, with the canvas already scaled to them.
    /// </summary>
    public void PaintInto(SKCanvas canvas, SlideShowFrame frame, SlideDeck deck, SKRect area, OfficeWatermark? watermark = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(frame);

        canvas.Save();
        canvas.ClipRect(area);
        this.fill.Color = SKColors.Black;
        canvas.DrawRect(area, this.fill);
        canvas.Translate(area.Left, area.Top);
        this.PaintFrame(canvas, frame, deck, area.Width, area.Height, watermark);
        canvas.Restore();
    }

    void PaintFrame(SKCanvas canvas, SlideShowFrame frame, SlideDeck deck, double width, double height, OfficeWatermark? watermark)
    {
        canvas.Save();

        if (frame.IsEnd || frame.Screen != SlideShowScreen.Slide)
        {
            if (frame.Screen == SlideShowScreen.White)
                canvas.Clear(SKColors.White);

            if (frame.IsEnd && frame.Screen == SlideShowScreen.Slide)
            {
                using var font = new SKFont(SKTypeface.Default, 16);
                this.fill.Color = new SKColor(0xC8, 0xC8, 0xC8);
                canvas.DrawText(this.EndText, (float)width / 2, 36, SKTextAlign.Center, font, this.fill);
            }

            canvas.Restore();
            return;
        }

        var fit = Math.Min(width / deck.SlideWidth, height / deck.SlideHeight);
        var w = deck.SlideWidth * fit;
        var h = deck.SlideHeight * fit;
        var destination = new SKRect((float)((width - w) / 2), (float)((height - h) / 2), (float)((width + w) / 2), (float)((height + h) / 2));

        if (frame.Previous is { } previous && frame.Transition is { } transition && frame.TransitionProgress < 1)
            this.PaintTransition(canvas, frame, previous, transition, destination, deck, watermark);
        else
            this.PaintSlide(canvas, frame.Slide, frame.Shapes, destination, deck, watermark);

        canvas.Restore();
    }

    void PaintSlide(SKCanvas canvas, Slide slide, IReadOnlyDictionary<int, ShapeAnimationState> states, SKRect destination, SlideDeck deck, OfficeWatermark? watermark)
        => slides.Paint(canvas, new SlidePaintRequest
        {
            Slide = slide,
            Watermark = watermark,
            SlideWidth = deck.SlideWidth,
            SlideHeight = deck.SlideHeight,
            DestinationX = destination.Left,
            DestinationY = destination.Top,
            DestinationWidth = destination.Width,
            DestinationHeight = destination.Height,
            Theme = SlideTheme.Presentation,
            DrawBorder = false,
            Animation = states
        });

    void PaintTransition(SKCanvas canvas, SlideShowFrame frame, Slide previous, SlideTransition transition, SKRect destination, SlideDeck deck, OfficeWatermark? watermark)
    {
        var p = (float)Ease(frame.TransitionProgress);
        var w = destination.Width;
        var h = destination.Height;

        // The direction the incoming slide travels from, as a unit vector: from bottom means it moves up.
        var (dx, dy) = transition.Direction switch
        {
            SlideTransitionDirection.FromTop => (0f, -1f),
            SlideTransitionDirection.FromLeft => (-1f, 0f),
            SlideTransitionDirection.FromRight => (1f, 0f),
            _ => (0f, 1f)
        };

        void From() => this.PaintSlide(canvas, previous, frame.PreviousShapes, destination, deck, watermark);

        void To() => this.PaintSlide(canvas, frame.Slide, frame.Shapes, destination, deck, watermark);

        canvas.Save();
        canvas.ClipRect(destination);

        switch (transition.Kind)
        {
            case SlideTransitionKind.Push:
                canvas.Save();
                canvas.Translate(-dx * w * p, -dy * h * p);
                From();
                canvas.Restore();

                canvas.Save();
                canvas.Translate(dx * w * (1 - p), dy * h * (1 - p));
                To();
                canvas.Restore();
                break;

            case SlideTransitionKind.Cover:
                From();
                canvas.Save();
                canvas.Translate(dx * w * (1 - p), dy * h * (1 - p));
                To();
                canvas.Restore();
                break;

            case SlideTransitionKind.Reveal:
                To();
                canvas.Save();

                // The old slide slides away the opposite way to where the new one would come from.
                canvas.Translate(-dx * w * p, -dy * h * p);
                using (var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * (1 - p * 0.6f))) })
                {
                    canvas.SaveLayer(layer);
                    From();
                    canvas.Restore();
                }

                canvas.Restore();
                break;

            case SlideTransitionKind.Wipe:
                From();
                canvas.Save();
                canvas.ClipRect(transition.Direction switch
                {
                    SlideTransitionDirection.FromTop => new SKRect(destination.Left, destination.Top, destination.Right, destination.Top + h * p),
                    SlideTransitionDirection.FromLeft => new SKRect(destination.Left, destination.Top, destination.Left + w * p, destination.Bottom),
                    SlideTransitionDirection.FromRight => new SKRect(destination.Right - w * p, destination.Top, destination.Right, destination.Bottom),
                    _ => new SKRect(destination.Left, destination.Bottom - h * p, destination.Right, destination.Bottom)
                });
                To();
                canvas.Restore();
                break;

            case SlideTransitionKind.Split:
            {
                var vertical = transition.Direction is SlideTransitionDirection.VerticalIn or SlideTransitionDirection.VerticalOut;
                var outward = transition.Direction is SlideTransitionDirection.VerticalOut or SlideTransitionDirection.HorizontalOut;

                if (outward)
                {
                    From();
                    canvas.Save();
                    canvas.ClipRect(vertical
                        ? new SKRect(destination.MidX - w * p / 2, destination.Top, destination.MidX + w * p / 2, destination.Bottom)
                        : new SKRect(destination.Left, destination.MidY - h * p / 2, destination.Right, destination.MidY + h * p / 2));
                    To();
                    canvas.Restore();
                }
                else
                {
                    To();
                    canvas.Save();
                    var keep = 1 - p;
                    canvas.ClipRect(vertical
                        ? new SKRect(destination.MidX - w * keep / 2, destination.Top, destination.MidX + w * keep / 2, destination.Bottom)
                        : new SKRect(destination.Left, destination.MidY - h * keep / 2, destination.Right, destination.MidY + h * keep / 2));
                    From();
                    canvas.Restore();
                }

                break;
            }

            case SlideTransitionKind.Zoom:
                if (transition.Direction == SlideTransitionDirection.Out)
                {
                    To();
                    canvas.Save();
                    var grow = 1 + 0.5f * p;
                    canvas.Scale(grow, grow, destination.MidX, destination.MidY);
                    using var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * (1 - p))) };
                    canvas.SaveLayer(layer);
                    From();
                    canvas.Restore();
                    canvas.Restore();
                }
                else
                {
                    From();
                    canvas.Save();
                    var shrink = 0.3f + 0.7f * p;
                    canvas.Scale(shrink, shrink, destination.MidX, destination.MidY);
                    using var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * p)) };
                    canvas.SaveLayer(layer);
                    To();
                    canvas.Restore();
                    canvas.Restore();
                }

                break;

            default:
                // Fade, Morph and anything the editor does not offer: a cross-fade.
                From();
                using (var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * p)) })
                {
                    canvas.SaveLayer(layer);
                    To();
                    canvas.Restore();
                }

                break;
        }

        canvas.Restore();
    }

    static double Ease(double t) => t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t);

    public void Dispose() => this.fill.Dispose();
}
