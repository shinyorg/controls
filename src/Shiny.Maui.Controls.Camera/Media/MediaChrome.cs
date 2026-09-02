using System.Windows.Input;

namespace Shiny.Maui.Controls.Camera.Media;

/// <summary>The glyphs the modal camera's chrome draws.</summary>
enum MediaIcon
{
    Close,
    FlipCamera,
    TorchOn,
    TorchOff,
    FlashOn,
    FlashOff,
    FlashAuto,
    Check
}


/// <summary>
/// Draws the modal camera's chrome icons as vector paths rather than font glyphs or emoji.
/// </summary>
/// <remarks>
/// A glyph would need a font this package does not ship and cannot assume, and an emoji renders at the
/// mercy of the platform's colour font — the same icon comes out a different size, weight and hue on each
/// head. Drawing them keeps one look everywhere and lets them tint against the frame behind them.
/// </remarks>
sealed class MediaIconDrawable : IDrawable
{
    public MediaIcon Icon { get; set; }

    public Color Color { get; set; } = Colors.White;

    public void Draw(ICanvas canvas, RectF rect)
    {
        // work in a normalized 24x24 box so every icon is authored at one scale
        var scale = Math.Min(rect.Width, rect.Height) / 24f;
        canvas.SaveState();
        canvas.Translate(rect.X + (rect.Width - 24f * scale) / 2f, rect.Y + (rect.Height - 24f * scale) / 2f);
        canvas.Scale(scale, scale);

        canvas.StrokeColor = this.Color;
        canvas.FillColor = this.Color;
        canvas.StrokeSize = 2f;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;

        switch (this.Icon)
        {
            case MediaIcon.Close:
                canvas.DrawLine(6, 6, 18, 18);
                canvas.DrawLine(18, 6, 6, 18);
                break;

            case MediaIcon.Check:
                canvas.DrawLine(5, 13, 10, 18);
                canvas.DrawLine(10, 18, 19, 6);
                break;

            case MediaIcon.FlipCamera:
                // a camera body with a circular arrow through it
                canvas.DrawRoundedRectangle(3, 7, 18, 13, 3);
                canvas.DrawLine(8, 7, 10, 4);
                canvas.DrawLine(10, 4, 14, 4);
                canvas.DrawLine(14, 4, 16, 7);
                canvas.DrawArc(9, 10, 6, 6, 200, -20, true, false);
                canvas.FillPath(Arrow(9.5f, 10.5f, -1, -1));
                canvas.DrawArc(9, 10, 6, 6, 20, -200, true, false);
                canvas.FillPath(Arrow(14.5f, 16.5f, 1, 1));
                break;

            case MediaIcon.TorchOn:
            case MediaIcon.TorchOff:
                // a bulb: dome + neck + filament, with rays only when lit
                canvas.DrawPath(Bulb());
                canvas.DrawLine(10, 18, 14, 18);
                canvas.DrawLine(10.5f, 20.5f, 13.5f, 20.5f);
                if (this.Icon == MediaIcon.TorchOn)
                {
                    canvas.DrawLine(12, 1, 12, 3);
                    canvas.DrawLine(3.5f, 5, 5, 6.2f);
                    canvas.DrawLine(20.5f, 5, 19, 6.2f);
                }
                else
                {
                    canvas.DrawLine(3, 3, 21, 21);
                }
                break;

            case MediaIcon.FlashOn:
            case MediaIcon.FlashOff:
            case MediaIcon.FlashAuto:
                canvas.FillPath(Bolt());
                if (this.Icon == MediaIcon.FlashOff)
                    canvas.DrawLine(4, 3, 20, 21);
                else if (this.Icon == MediaIcon.FlashAuto)
                {
                    // a small "A" hung off the bolt
                    canvas.StrokeSize = 1.6f;
                    canvas.DrawLine(16, 21, 18.5f, 14);
                    canvas.DrawLine(18.5f, 14, 21, 21);
                    canvas.DrawLine(16.9f, 18.5f, 20.1f, 18.5f);
                }
                break;
        }
        canvas.RestoreState();
    }

    static PathF Bulb()
    {
        var p = new PathF();
        p.MoveTo(8, 15.5f);
        p.CurveTo(5.5f, 13.5f, 5, 10.5f, 6.5f, 8.2f);
        p.CurveTo(8.2f, 5.6f, 12.4f, 4.8f, 15.2f, 6.6f);
        p.CurveTo(18.4f, 8.6f, 18.8f, 12.9f, 16, 15.5f);
        p.LineTo(16, 17.5f);
        p.LineTo(8, 17.5f);
        p.Close();
        return p;
    }

    static PathF Bolt()
    {
        var p = new PathF();
        p.MoveTo(13.5f, 2);
        p.LineTo(6, 13.2f);
        p.LineTo(11, 13.2f);
        p.LineTo(10, 22);
        p.LineTo(17.5f, 10.4f);
        p.LineTo(12.5f, 10.4f);
        p.Close();
        return p;
    }

    static PathF Arrow(float x, float y, float dx, float dy)
    {
        var p = new PathF();
        p.MoveTo(x, y);
        p.LineTo(x + dx * 2.6f, y);
        p.LineTo(x, y + dy * 2.6f);
        p.Close();
        return p;
    }
}


/// <summary>
/// A round, translucent chrome button for the modal camera — a drawn <see cref="MediaIcon"/> over a scrim
/// disc, so it reads against both a bright sky and a dark room.
/// </summary>
/// <remarks>
/// Built from a <see cref="Border"/> plus a tap gesture rather than a <see cref="Button"/> on purpose: a
/// MAUI <c>Button</c> ignores gesture recognizers, brings platform chrome that has to be undone on every
/// head, and cannot host a drawable as its content.
/// </remarks>
sealed class MediaIconButton : Border
{
    readonly MediaIconDrawable drawable = new();
    readonly GraphicsView graphics;

    public MediaIconButton(MediaIcon icon, string automationId, double size = 42)
    {
        this.drawable.Icon = icon;
        this.graphics = new GraphicsView
        {
            Drawable = this.drawable,
            InputTransparent = true,
            HeightRequest = size * 0.55,
            WidthRequest = size * 0.55,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        this.AutomationId = automationId;
        this.WidthRequest = size;
        this.HeightRequest = size;
        this.Padding = 0;
        this.StrokeThickness = 0;
        this.BackgroundColor = Color.FromRgba(0, 0, 0, 110);
        this.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = size / 2 };
        this.Content = this.graphics;

        this.Tapped = new Command(() => this.Clicked?.Invoke(this, EventArgs.Empty));
        this.GestureRecognizers.Add(new TapGestureRecognizer { Command = this.Tapped });
    }

    /// <summary>
    /// The command the tap gesture runs. Exposed because a <see cref="TapGestureRecognizer"/>'s
    /// <c>Tapped</c> event cannot be raised from a test, while its <c>Command</c> can.
    /// </summary>
    public ICommand Tapped { get; }

    public event EventHandler? Clicked;

    public MediaIcon Icon
    {
        get => this.drawable.Icon;
        set
        {
            this.drawable.Icon = value;
            this.graphics.Invalidate();
        }
    }

    public void SetTint(Color color)
    {
        this.drawable.Color = color;
        this.graphics.Invalidate();
    }
}


/// <summary>The shutter / record control: a white ring with a fill that changes shape by mode and state.</summary>
sealed class MediaShutterDrawable : IDrawable
{
    public bool IsVideo { get; set; }

    public bool IsRecording { get; set; }

    public void Draw(ICanvas canvas, RectF rect)
    {
        var cx = rect.Center.X;
        var cy = rect.Center.Y;
        var outer = Math.Min(rect.Width, rect.Height) / 2f - 2f;

        canvas.StrokeColor = Colors.White;
        canvas.StrokeSize = 3.5f;
        canvas.DrawCircle(cx, cy, outer);

        canvas.FillColor = this.IsVideo ? Color.FromArgb("#EF4444") : Colors.White;
        if (this.IsVideo && this.IsRecording)
        {
            // the universal "recording — tap to stop" square, inset well inside the ring
            var half = outer * 0.42f;
            canvas.FillRoundedRectangle(cx - half, cy - half, half * 2, half * 2, half * 0.35f);
        }
        else
        {
            canvas.FillCircle(cx, cy, outer * 0.78f);
        }
    }
}


/// <summary>The shutter button — a <see cref="GraphicsView"/> with a tap gesture, sized for a thumb.</summary>
sealed class MediaShutterButton : GraphicsView
{
    readonly MediaShutterDrawable shutter = new();

    public MediaShutterButton(bool isVideo)
    {
        this.shutter.IsVideo = isVideo;
        this.Drawable = this.shutter;
        this.WidthRequest = 74;
        this.HeightRequest = 74;
        this.AutomationId = isVideo ? "shiny.media.record" : "shiny.media.shutter";
        this.HorizontalOptions = LayoutOptions.Center;

        this.Tapped = new Command(() => this.Clicked?.Invoke(this, EventArgs.Empty));
        this.GestureRecognizers.Add(new TapGestureRecognizer { Command = this.Tapped });
    }

    /// <inheritdoc cref="MediaIconButton.Tapped"/>
    public ICommand Tapped { get; }

    public event EventHandler? Clicked;

    public bool IsRecording
    {
        get => this.shutter.IsRecording;
        set
        {
            this.shutter.IsRecording = value;
            this.Invalidate();
        }
    }
}
