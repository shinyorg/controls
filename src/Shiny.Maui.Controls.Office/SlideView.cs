using Shiny.Controls.Office.Presentation;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// Displays a <c>.pptx</c>, one slide at a time or as a scrolling thumbnail grid.
/// </summary>
/// <remarks>
/// Read-only. Requires <c>UseSkiaSharp()</c> in <c>MauiProgram</c>.
/// </remarks>
public partial class SlideView : ContentView, IDisposable
{
    readonly SKCanvasView canvas;
    readonly SkiaTextMeasurer measurer = new();
    readonly SlidePainter painter;

    SlideController? controller;
    double pressX;
    double pressY;
    double lastPanY;
    bool disposed;

    public SlideView()
    {
        this.painter = new SlidePainter(this.measurer);

        this.canvas = new SKCanvasView { EnableTouchEvents = true };
        this.canvas.PaintSurface += this.OnPaintSurface;
        this.canvas.Touch += this.OnTouch;

        this.Content = this.canvas;

        // An unset Theme tracks the app's appearance, so a flip has to redraw.
        this.FollowAppTheme(static v => v.Invalidate());
    }

    public static readonly BindableProperty DeckProperty = BindableProperty.Create(
        nameof(Deck),
        typeof(SlideDeck),
        typeof(SlideView),
        propertyChanged: (b, _, _) => ((SlideView)b).Rebuild());

    public static readonly BindableProperty ThemeProperty = BindableProperty.Create(
        nameof(Theme),
        typeof(SlideTheme),
        typeof(SlideView),
        null,
        propertyChanged: (b, _, _) => ((SlideView)b).Invalidate());

    public static readonly BindableProperty ModeProperty = BindableProperty.Create(
        nameof(Mode),
        typeof(SlideViewMode),
        typeof(SlideView),
        SlideViewMode.Single,
        propertyChanged: (b, _, value) =>
        {
            if (((SlideView)b).controller is { } controller)
                controller.Mode = (SlideViewMode)value;
        });

    public static readonly BindableProperty SlideIndexProperty = BindableProperty.Create(
        nameof(SlideIndex),
        typeof(int),
        typeof(SlideView),
        0,
        BindingMode.TwoWay,
        propertyChanged: (b, _, value) =>
        {
            if (((SlideView)b).controller is { } controller)
                controller.Index = (int)value;
        });

    public SlideDeck? Deck
    {
        get => (SlideDeck?)this.GetValue(DeckProperty);
        set => this.SetValue(DeckProperty, value);
    }

    /// <summary>
    /// Chrome colours. Left unset the control follows the app's light/dark appearance; setting it
    /// pins the choice, including to <see cref="SlideTheme.Light"/>.
    /// </summary>
    public SlideTheme? Theme
    {
        get => (SlideTheme?)this.GetValue(ThemeProperty);
        set => this.SetValue(ThemeProperty, value);
    }

    // Presenting overrides the control's own theme rather than composing with it: a projected deck is
    // black-surrounded whatever the app is set to, and a Theme pinned for the inline viewer should not
    // put a grey frame on the projector.
    SlideTheme EffectiveTheme => this.IsPresenting
        ? SlideTheme.Presentation
        : this.Theme ?? OfficeScheme.DefaultSlide;

    public SlideViewMode Mode
    {
        get => (SlideViewMode)this.GetValue(ModeProperty);
        set => this.SetValue(ModeProperty, value);
    }

    public int SlideIndex
    {
        get => (int)this.GetValue(SlideIndexProperty);
        set => this.SetValue(SlideIndexProperty, value);
    }

    public SlideController? Controller => this.controller;

    /// <summary>Raised when the shown slide changes, whether by navigation or by tapping a thumbnail.</summary>
    public event EventHandler<int>? SlideChanged;

    void Rebuild()
    {
        if (this.controller is not null)
            this.controller.Changed -= this.OnControllerChanged;

        if (this.Deck is null)
        {
            this.controller = null;
            this.Invalidate();
            return;
        }

        this.controller = new SlideController(this.Deck) { Mode = this.Mode, IsPresenting = this.IsPresenting };
        this.controller.Changed += this.OnControllerChanged;

        if (this.Width > 0 && this.Height > 0)
            this.controller.Resize(this.Width, this.Height);

        this.Invalidate();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0 && height > 0)
            this.controller?.Resize(width, height);
    }

    void OnControllerChanged(object? sender, EventArgs e)
    {
        if (this.controller is { } controller && this.SlideIndex != controller.Index)
        {
            this.SlideIndex = controller.Index;
            this.SlideChanged?.Invoke(this, controller.Index);
        }

        this.Invalidate();
    }

    void Invalidate() => this.canvas.InvalidateSurface();

    public void Next() => this.controller?.Next();

    public void Previous() => this.controller?.Previous();

    void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var theme = this.EffectiveTheme;
        var canvasSurface = e.Surface.Canvas;
        canvasSurface.Clear(new SKColor(theme.Surround.R, theme.Surround.G, theme.Surround.B));

        if (this.controller is null)
            return;

        var scale = this.Width > 0 ? (float)(e.Info.Width / this.Width) : 1f;

        if (this.controller.Mode == SlideViewMode.Grid)
        {
            foreach (var placement in this.controller.VisibleThumbnails())
                this.PaintPlacement(canvasSurface, placement, theme, scale);

            return;
        }

        if (this.controller.SinglePlacement() is { } single)
            this.PaintPlacement(canvasSurface, single, theme, scale);
    }

    void PaintPlacement(SKCanvas canvas, SlidePlacement placement, SlideTheme theme, float scale)
    {
        if (this.controller is null)
            return;

        this.painter.Paint(canvas, new SlidePaintRequest
        {
            Watermark = this.Watermark,
            Slide = placement.Slide,
            SlideWidth = this.controller.Deck.SlideWidth,
            SlideHeight = this.controller.Deck.SlideHeight,
            DestinationX = placement.X,
            DestinationY = placement.Y,
            DestinationWidth = placement.Width,
            DestinationHeight = placement.Height,
            Theme = theme,
            Scale = scale,
            DrawBorder = !this.IsPresenting
        });
    }

    void OnTouch(object? sender, SKTouchEventArgs e)
    {
        if (this.controller is null)
        {
            e.Handled = true;
            return;
        }

        var scale = this.Width > 0 ? (float)(this.canvas.CanvasSize.Width / this.Width) : 1f;
        var x = e.Location.X / scale;
        var y = e.Location.Y / scale;

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                this.Interacted?.Invoke(this, EventArgs.Empty);
                this.pressX = x;
                this.pressY = y;
                this.lastPanY = y;
                break;

            case SKTouchAction.Moved when e.InContact && this.controller.Mode == SlideViewMode.Grid:
                this.controller.Scroll(this.lastPanY - y);
                this.lastPanY = y;
                break;

            case SKTouchAction.Released:
                this.OnRelease(x, y);
                break;

            case SKTouchAction.WheelChanged:
                if (this.controller.Mode == SlideViewMode.Grid)
                    this.controller.Scroll(-e.WheelDelta);
                else if (e.WheelDelta < 0)
                    this.controller.Next();
                else
                    this.controller.Previous();

                break;
        }

        e.Handled = true;
    }

    void OnRelease(double x, double y)
    {
        if (this.controller is null)
            return;

        // A short horizontal drag in single mode is a swipe between slides; anything longer vertically
        // was a scroll and should not also navigate.
        var dx = x - this.pressX;
        var dy = y - this.pressY;

        if (this.controller.Mode == SlideViewMode.Grid)
        {
            if (Math.Abs(dy) < 6)
            {
                var index = this.controller.ThumbnailAt(x, y);
                if (index >= 0)
                {
                    this.controller.Index = index;
                    this.controller.Mode = SlideViewMode.Single;
                    this.Mode = SlideViewMode.Single;
                }
            }

            return;
        }

        if (Math.Abs(dx) > 40 && Math.Abs(dx) > Math.Abs(dy))
        {
            if (dx < 0)
                this.controller.Next();
            else
                this.controller.Previous();

            return;
        }

        // A tap is only navigation while presenting. In the inline viewer it is how the surface gets
        // focus and how a host's own gesture on the control is expected to arrive.
        if (this.IsPresenting && Math.Abs(dx) < 12 && Math.Abs(dy) < 12)
        {
            if (x < this.Width * BackTapZone)
                this.controller.Previous();
            else
                this.controller.Next();
        }
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;

        if (this.controller is not null)
            this.controller.Changed -= this.OnControllerChanged;

        this.StopPresenting();
        this.canvas.PaintSurface -= this.OnPaintSurface;
        this.canvas.Touch -= this.OnTouch;
        this.painter.Dispose();
        this.measurer.Dispose();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A picture drawn behind the content — a logo, a DRAFT stamp, a company mark.
    /// </summary>
    /// <remarks>
    /// A <b>display</b> watermark: it is drawn, not written into the file. The three Office formats
    /// have no common notion of one, so persisting would mean three unrelated mechanisms where drawing
    /// means one. See <see cref="OfficeWatermark"/>.
    /// </remarks>
    public static readonly BindableProperty WatermarkProperty = BindableProperty.Create(
        nameof(Watermark),
        typeof(OfficeWatermark),
        typeof(SlideView),
        null,
        propertyChanged: (b, _, _) => ((SlideView)b).Invalidate());

    /// <inheritdoc cref="WatermarkProperty"/>
    public OfficeWatermark? Watermark
    {
        get => (OfficeWatermark?)this.GetValue(WatermarkProperty);
        set => this.SetValue(WatermarkProperty, value);
    }

}
