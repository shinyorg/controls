using Shiny.Controls.Office.Presentation;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// The column of slide thumbnails beside the editor: tap one to open it, drag one to move it.
/// </summary>
/// <remarks>
/// All of the behaviour is <see cref="SlideRailController"/>'s, shared with Blazor; this forwards the
/// touches and paints. <see cref="SlideEditorView"/> places one beside its slide.
/// </remarks>
public class SlideRail : ContentView
{
    readonly SKCanvasView canvas;
    readonly SkiaTextMeasurer measurer = new();
    readonly SlidePainter slides;
    readonly SlideRailPainter painter;

    SlideEditorController? controller;
    SlideRailController? rail;
    int lastIndex = -1;

    public SlideRail()
    {
        this.slides = new SlidePainter(this.measurer);
        this.painter = new SlideRailPainter(this.slides);

        this.canvas = new SKCanvasView { EnableTouchEvents = true };
        this.canvas.PaintSurface += this.OnPaintSurface;
        this.canvas.Touch += this.OnTouch;
        this.Content = this.canvas;

        // The rail is chrome and follows the app's appearance. Application raises this through a weak
        // event manager, so the subscription does not keep the rail alive.
        if (Application.Current is { } app)
            app.RequestedThemeChanged += (_, _) => this.canvas.InvalidateSurface();
    }

    /// <summary>The editor whose deck the rail shows and whose slide it follows.</summary>
    public SlideEditorController? Controller
    {
        get => this.controller;
        set
        {
            if (ReferenceEquals(this.controller, value))
                return;

            if (this.controller is not null)
                this.controller.Changed -= this.OnEditorChanged;

            this.controller = value;
            this.rail = null;

            if (value is not null)
            {
                this.rail = new SlideRailController(value);
                this.rail.Changed += (_, _) => this.canvas.InvalidateSurface();
                value.Changed += this.OnEditorChanged;
            }

            this.canvas.InvalidateSurface();
        }
    }

    /// <summary>Raised after a tap or a drag ends, so a host can put the keyboard focus back on the editor.</summary>
    public event EventHandler? Interacted;

    /// <summary>The slide canvas colours, forwarded from the editor. Null follows the app.</summary>
    public SlideTheme? Theme { get; set; }

    /// <summary>The rail's own controller, for a host that wants to scroll it or read its layout.</summary>
    public SlideRailController? Rail => this.rail;

    void OnEditorChanged(object? sender, EventArgs e)
    {
        if (this.rail is { } rail && this.controller is { } controller && controller.Index != this.lastIndex)
        {
            this.lastIndex = controller.Index;
            rail.EnsureVisible(controller.Index);
        }

        this.canvas.InvalidateSurface();
    }

    float Scale => this.Width > 0 ? (float)(this.canvas.CanvasSize.Width / this.Width) : 1f;

    void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        if (this.rail is not { } rail || this.Width <= 0)
        {
            e.Surface.Canvas.Clear(SKColors.Transparent);
            return;
        }

        if (Math.Abs(this.Width - rail.ViewportWidth) > 0.5 || Math.Abs(this.Height - rail.ViewportHeight) > 0.5)
        {
            rail.Resize(this.Width, this.Height);
            rail.EnsureVisible(rail.Editor.Index);
        }

        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        this.painter.Paint(
            e.Surface.Canvas,
            rail,
            dark ? SlideRailTheme.Dark : SlideRailTheme.Light,
            this.Theme ?? OfficeScheme.DefaultSlide,
            (float)(e.Info.Width / this.Width));
    }

    void OnTouch(object? sender, SKTouchEventArgs e)
    {
        e.Handled = true;

        if (this.rail is not { } rail)
            return;

        var scale = this.Scale;
        var x = e.Location.X / scale;
        var y = e.Location.Y / scale;

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                rail.PointerDown(x, y);
                break;

            case SKTouchAction.Moved when e.InContact:
                rail.PointerMove(x, y);
                break;

            case SKTouchAction.Released:
                rail.PointerUp();
                this.Interacted?.Invoke(this, EventArgs.Empty);
                break;

            case SKTouchAction.Cancelled:
                rail.PointerCancel();
                break;

            case SKTouchAction.WheelChanged:
                rail.Scroll(-e.WheelDelta);
                break;
        }
    }
}
