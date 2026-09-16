using Shiny.Maui.Controls.MotionIcons;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Images;

/// <summary>
/// The circular loader <see cref="ShinyImage"/> shows while an image is on its way.
/// </summary>
/// <remarks>
/// One control covers both modes on purpose. Set <see cref="Percent"/> and the ring fills to that
/// fraction with a label; leave it null - which is what a queued request or a response without a
/// <c>Content-Length</c> produces - and the same ring spins instead. Swapping between two controls
/// would make the size flicker at the moment a queued download starts.
/// </remarks>
public class LoadingRing : GraphicsView
{
    /// <summary>How long one full rotation of the indeterminate arc takes.</summary>
    const double RotationSeconds = 1.1;

    readonly RingDrawable ringDrawable;
    MotionTicker? ticker;
    bool isSubscribed;

    /// <summary>Creates the ring.</summary>
    public LoadingRing()
    {
        this.ringDrawable = new RingDrawable(this);
        this.Drawable = this.ringDrawable;
        this.HeightRequest = 48;
        this.WidthRequest = 48;
        this.HorizontalOptions = LayoutOptions.Center;
        this.VerticalOptions = LayoutOptions.Center;

        // The theme colours resolve through this view, not the application's resources, so a palette
        // scoped over the ring is honoured and a swap re-fires the callback (which redraws).
        this.SetDynamicResource(ThemeRingColorProperty, ShinyThemeKeys.Color.Primary);
        this.SetDynamicResource(ThemeTrackColorProperty, ShinyThemeKeys.Color.SurfaceContainerHighest);
        this.SetDynamicResource(ThemeTextColorProperty, ShinyThemeKeys.Color.OnSurface);

        this.Loaded += (_, _) => this.SyncTicker();
        this.Unloaded += (_, _) => this.Unsubscribe();
    }


    /// <summary>
    /// Completion from 0-1, or null to spin. Null is the normal state for a queued request or a
    /// server that sent no content length.
    /// </summary>
    public static readonly BindableProperty PercentProperty = BindableProperty.Create(
        nameof(Percent), typeof(double?), typeof(LoadingRing), null,
        propertyChanged: (b, _, _) => ((LoadingRing)b).OnVisualChanged()
    );

    /// <inheritdoc cref="PercentProperty" />
    public double? Percent
    {
        get => (double?)this.GetValue(PercentProperty);
        set => this.SetValue(PercentProperty, value);
    }


    /// <summary>The arc colour. Null uses the theme's Primary token.</summary>
    public static readonly BindableProperty RingColorProperty = BindableProperty.Create(
        nameof(RingColor), typeof(Color), typeof(LoadingRing), null,
        propertyChanged: (b, _, _) => ((LoadingRing)b).Invalidate()
    );

    /// <inheritdoc cref="RingColorProperty" />
    public Color? RingColor
    {
        get => (Color?)this.GetValue(RingColorProperty);
        set => this.SetValue(RingColorProperty, value);
    }


    /// <summary>The unfilled track colour. Null uses the theme's SurfaceContainerHighest token.</summary>
    public static readonly BindableProperty TrackColorProperty = BindableProperty.Create(
        nameof(TrackColor), typeof(Color), typeof(LoadingRing), null,
        propertyChanged: (b, _, _) => ((LoadingRing)b).Invalidate()
    );

    /// <inheritdoc cref="TrackColorProperty" />
    public Color? TrackColor
    {
        get => (Color?)this.GetValue(TrackColorProperty);
        set => this.SetValue(TrackColorProperty, value);
    }


    /// <summary>The percentage label colour. Null uses the theme's OnSurface token.</summary>
    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor), typeof(Color), typeof(LoadingRing), null,
        propertyChanged: (b, _, _) => ((LoadingRing)b).Invalidate()
    );

    /// <inheritdoc cref="TextColorProperty" />
    public Color? TextColor
    {
        get => (Color?)this.GetValue(TextColorProperty);
        set => this.SetValue(TextColorProperty, value);
    }


    /// <summary>Whether the percentage is drawn in the middle. Ignored when indeterminate.</summary>
    public static readonly BindableProperty ShowPercentTextProperty = BindableProperty.Create(
        nameof(ShowPercentText), typeof(bool), typeof(LoadingRing), true,
        propertyChanged: (b, _, _) => ((LoadingRing)b).Invalidate()
    );

    /// <inheritdoc cref="ShowPercentTextProperty" />
    public bool ShowPercentText
    {
        get => (bool)this.GetValue(ShowPercentTextProperty);
        set => this.SetValue(ShowPercentTextProperty, value);
    }


    /// <summary>Thickness of the ring stroke.</summary>
    public static readonly BindableProperty StrokeWidthProperty = BindableProperty.Create(
        nameof(StrokeWidth), typeof(double), typeof(LoadingRing), 4.0,
        propertyChanged: (b, _, _) => ((LoadingRing)b).Invalidate()
    );

    /// <inheritdoc cref="StrokeWidthProperty" />
    public double StrokeWidth
    {
        get => (double)this.GetValue(StrokeWidthProperty);
        set => this.SetValue(StrokeWidthProperty, value);
    }


    // Theme tokens, resolved on this element. The defaults are what draws before a theme merges.
    static readonly Color RingFallback = Colors.RoyalBlue;
    static readonly Color TrackFallback = Colors.LightGray;
    static readonly Color TextFallback = Colors.Black;

    internal static readonly BindableProperty ThemeRingColorProperty = BindableProperty.Create(
        "ThemeRingColor", typeof(Color), typeof(LoadingRing), RingFallback,
        propertyChanged: (b, _, _) => ((LoadingRing)b).Invalidate()
    );

    internal static readonly BindableProperty ThemeTrackColorProperty = BindableProperty.Create(
        "ThemeTrackColor", typeof(Color), typeof(LoadingRing), TrackFallback,
        propertyChanged: (b, _, _) => ((LoadingRing)b).Invalidate()
    );

    internal static readonly BindableProperty ThemeTextColorProperty = BindableProperty.Create(
        "ThemeTextColor", typeof(Color), typeof(LoadingRing), TextFallback,
        propertyChanged: (b, _, _) => ((LoadingRing)b).Invalidate()
    );

    /// <summary>The arc colour actually drawn: the explicit <see cref="RingColor"/>, else the theme's.</summary>
    internal Color EffectiveRingColor => this.RingColor ?? (Color)this.GetValue(ThemeRingColorProperty);

    /// <summary>The track colour actually drawn.</summary>
    internal Color EffectiveTrackColor => this.TrackColor ?? (Color)this.GetValue(ThemeTrackColorProperty);

    /// <summary>The percentage label colour actually drawn.</summary>
    internal Color EffectiveTextColor => this.TextColor ?? (Color)this.GetValue(ThemeTextColorProperty);


    void OnVisualChanged()
    {
        this.SyncTicker();
        this.Invalidate();
    }


    /// <inheritdoc />
    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        // Visibility has to be part of the decision, not just attachment. A ShinyImage that has
        // finished loading leaves its ring in the tree with a null Percent - which reads as
        // "indeterminate" - so keying only off IsLoaded would leave the frame timer running on every
        // page holding a loaded image, forever.
        if (propertyName == nameof(this.IsVisible))
            this.SyncTicker();
    }


    /// <summary>
    /// Subscribes to the frame clock only while actually spinning: attached, visible, and with no
    /// percentage to draw.
    /// </summary>
    /// <remarks>
    /// The ticker is the one <see cref="MotionIcons"/> already shares - a single dispatcher timer per
    /// window, running only while something is listening. That is what makes a list of fifty loading
    /// thumbnails cost one timer instead of fifty, and a page where everything has loaded cost none.
    /// A determinate ring redraws when its percentage changes and needs no clock at all.
    /// </remarks>
    void SyncTicker()
    {
        if (this.Percent is null && this.IsLoaded && this.IsVisible)
            this.Subscribe();
        else
            this.Unsubscribe();
    }


    void Subscribe()
    {
        if (this.isSubscribed)
            return;

        this.ticker ??= MotionTicker.For(this);
        if (this.ticker is null)
            return;

        this.ticker.Tick += this.OnTick;
        this.isSubscribed = true;
    }


    void Unsubscribe()
    {
        if (!this.isSubscribed || this.ticker is null)
            return;

        this.ticker.Tick -= this.OnTick;
        this.isSubscribed = false;
    }


    void OnTick(TimeSpan delta)
    {
        this.ringDrawable.Advance(delta);
        this.Invalidate();
    }


    sealed class RingDrawable(LoadingRing owner) : IDrawable
    {
        float rotation;

        public void Advance(TimeSpan delta)
        {
            this.rotation = (float)((this.rotation + delta.TotalSeconds / RotationSeconds * 360) % 360);
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var stroke = (float)owner.StrokeWidth;
            var size = Math.Min(dirtyRect.Width, dirtyRect.Height) - stroke;
            if (size <= 0)
                return;

            var bounds = new RectF(
                dirtyRect.Center.X - size / 2,
                dirtyRect.Center.Y - size / 2,
                size,
                size
            );

            canvas.StrokeSize = stroke;
            canvas.StrokeLineCap = LineCap.Round;

            canvas.StrokeColor = owner.EffectiveTrackColor;
            canvas.DrawEllipse(bounds);

            canvas.StrokeColor = owner.EffectiveRingColor;

            var percent = owner.Percent;
            if (percent is null)
            {
                // DrawArc measures counter-clockwise from 3 o'clock; negating turns the sweep the way
                // every other spinner on the platform turns.
                var start = -this.rotation;
                canvas.DrawArc(bounds, start, start - 90, false, false);
                return;
            }

            var value = Math.Clamp(percent.Value, 0, 1);
            if (value > 0)
                canvas.DrawArc(bounds, 90, 90 - (float)(value * 360), true, false);

            if (!owner.ShowPercentText)
                return;

            canvas.FontColor = owner.EffectiveTextColor;
            canvas.FontSize = Math.Max(9, size * 0.26f);
            canvas.DrawString(
                $"{value * 100:0}%",
                bounds,
                HorizontalAlignment.Center,
                VerticalAlignment.Center
            );
        }

    }
}
