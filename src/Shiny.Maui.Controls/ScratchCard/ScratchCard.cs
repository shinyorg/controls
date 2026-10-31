using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

/// <summary>
/// A scratch-off card: a cover view sits over the hidden content and is scratched away under the
/// user's finger (or mouse) until enough is gone to reveal the rest.
/// </summary>
/// <example>
/// <code>
/// &lt;shiny:ScratchCard ThresholdPercent="55" ThresholdReached="OnThresholdReached"&gt;
///     &lt;shiny:ScratchCard.Cover&gt;
///         &lt;Image Source="foil.png" Aspect="AspectFill" /&gt;
///     &lt;/shiny:ScratchCard.Cover&gt;
///     &lt;Label Text="You won!" HorizontalOptions="Center" VerticalOptions="Center" /&gt;
/// &lt;/shiny:ScratchCard&gt;
/// </code>
/// </example>
/// <remarks>
/// <para>
/// Both layers are ordinary live views - the cover is not flattened to a bitmap, so an image, a
/// gradient, a label or a whole layout all work. Scratching clips the cover with
/// <see cref="VisualElement.Clip"/> to whatever has not been scratched yet, tracked on a grid of
/// small cells (<see cref="ScratchMask"/>); the cell size follows <see cref="StrokeWidth"/> so the
/// edge stays fine at any brush size.
/// </para>
/// <para>
/// Inside a <see cref="ScrollView"/> the scroller is held off for the length of a stroke, so a
/// scratch never scrolls the page. Nothing under the cover takes a touch until
/// <see cref="ThresholdPercent"/> is reached (or the card is revealed from code); from then on the
/// scratch surface steps aside and the content takes touches - a "Claim" button under the cover
/// cannot be pressed through a hole, only once the card is earned.
/// </para>
/// </remarks>
[ContentProperty(nameof(HiddenContent))]
public class ScratchCard : ContentView
{
    /// <summary>Names the surface that takes the scratch strokes.</summary>
    public const string SurfaceAutomationIdSuffix = "_Surface";

    /// <summary>Names the cover layer.</summary>
    public const string CoverAutomationIdSuffix = "_Cover";

    readonly ScratchMask mask = new();
    readonly ContentView hiddenHost;
    readonly ContentView coverHost;
    readonly GraphicsView surface;
#if IOS
    readonly DragTouchHook touchHook;
#endif
    View? defaultCover;
    Point lastPoint;
    bool stroking;
    bool syncing;
    bool revealed;
    bool thresholdReached;

    public ScratchCard()
    {
        // Locked until the threshold: nothing under the cover can be touched before it is earned.
        this.hiddenHost = new ContentView { InputTransparent = true, CascadeInputTransparent = true };
        this.coverHost = new ContentView { InputTransparent = true, CascadeInputTransparent = true };
        this.surface = new GraphicsView
        {
            Drawable = NothingDrawable.Instance,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        SemanticProperties.SetHint(this.surface, "Scratch to reveal");

        this.surface.StartInteraction += this.OnStartInteraction;
        this.surface.DragInteraction += this.OnDragInteraction;
        this.surface.EndInteraction += this.OnEndInteraction;
        this.surface.CancelInteraction += (_, _) => this.EndStroke();

#if IOS
        // UIScrollView's pan starts as soon as the finger moves, so the scroller is locked from the raw
        // touch-down, before the first drag sample. Not on Android: the hook listens through the native
        // Touch event, and a listener there reports every touch handled by default - which on a
        // GraphicsView (where MAUI adds no listener of its own) stops OnTouchEvent, and with it every
        // Start/Drag interaction, from ever running. Android locks from StartInteraction instead.
        this.touchHook = new DragTouchHook(this.surface)
        {
            Pressed = () =>
            {
                if (this.CanScratch)
                    this.touchHook!.LockScroller(true);
            },
            Released = () => this.touchHook!.LockScroller(false)
        };
#endif

        this.coverHost.SizeChanged += (_, _) => this.OnCoverSized();
        this.surface.SizeChanged += (_, _) => this.UpdateEdgeGestures();
        this.surface.HandlerChanged += (_, _) => this.UpdateEdgeGestures();

        AutomationIds.Link(this, this.surface, SurfaceAutomationIdSuffix);
        AutomationIds.Link(this, this.coverHost, CoverAutomationIdSuffix);

        this.Content = new Grid
        {
            IsClippedToBounds = true,
            Children = { this.hiddenHost, this.coverHost, this.surface }
        };

        this.ApplyCover();
        StyleGuard.MarkReady(this, typeof(ScratchCard));
    }


    // ---------------------------------------------------------------------------------------------
    // Properties
    // ---------------------------------------------------------------------------------------------

    public static readonly BindableProperty HiddenContentProperty = BindableProperty.Create(
        nameof(HiddenContent), typeof(View), typeof(ScratchCard), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ScratchCard), () => ((ScratchCard)b).hiddenHost.Content = (View?)n));

    /// <summary>The view underneath, revealed as the cover is scratched away. The XAML content property.</summary>
    public View? HiddenContent
    {
        get => (View?)this.GetValue(HiddenContentProperty);
        set => this.SetValue(HiddenContentProperty, value);
    }

    public static readonly BindableProperty CoverProperty = BindableProperty.Create(
        nameof(Cover), typeof(View), typeof(ScratchCard), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ScratchCard), () => ((ScratchCard)b).ApplyCover()));

    /// <summary>
    /// The view on top that gets scratched off - an <see cref="Image"/>, a gradient, a layout, anything.
    /// Null draws a plain silver foil.
    /// </summary>
    public View? Cover
    {
        get => (View?)this.GetValue(CoverProperty);
        set => this.SetValue(CoverProperty, value);
    }

    /// <summary>The default <see cref="StrokeWidth"/>: about a fingertip, the same 44 units as a minimum touch target.</summary>
    public const double DefaultStrokeWidth = 44;

    public static readonly BindableProperty StrokeWidthProperty = BindableProperty.Create(
        nameof(StrokeWidth), typeof(double), typeof(ScratchCard), DefaultStrokeWidth,
        coerceValue: (_, v) => Math.Max(2d, (double)v),
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ScratchCard), () => ((ScratchCard)b).OnCoverSized()));

    /// <summary>
    /// How wide a scratch stroke is, in device-independent units. Defaults to 44 - about the width of
    /// a fingertip.
    /// </summary>
    public double StrokeWidth
    {
        get => (double)this.GetValue(StrokeWidthProperty);
        set => this.SetValue(StrokeWidthProperty, value);
    }

    public static readonly BindableProperty ThresholdPercentProperty = BindableProperty.Create(
        nameof(ThresholdPercent), typeof(double), typeof(ScratchCard), 60d,
        coerceValue: (_, v) => Math.Clamp((double)v, 0d, 100d));

    /// <summary>
    /// The percentage of the cover (0 to 100) that, once scratched, raises <see cref="ThresholdReached"/> -
    /// and clears the rest of the cover when <see cref="AutoReveal"/> is on. Defaults to 60.
    /// </summary>
    public double ThresholdPercent
    {
        get => (double)this.GetValue(ThresholdPercentProperty);
        set => this.SetValue(ThresholdPercentProperty, value);
    }

    public static readonly BindableProperty ThresholdReachedCommandProperty = BindableProperty.Create(
        nameof(ThresholdReachedCommand), typeof(ICommand), typeof(ScratchCard));

    /// <summary>Executed alongside <see cref="ThresholdReached"/>, with the scratched percentage as its parameter.</summary>
    public ICommand? ThresholdReachedCommand
    {
        get => (ICommand?)this.GetValue(ThresholdReachedCommandProperty);
        set => this.SetValue(ThresholdReachedCommandProperty, value);
    }

    public static readonly BindableProperty AutoRevealProperty = BindableProperty.Create(
        nameof(AutoReveal), typeof(bool), typeof(ScratchCard), true);

    /// <summary>
    /// Clears the rest of the cover once <see cref="ThresholdPercent"/> is reached. On by default. Off
    /// still raises <see cref="ThresholdReached"/> and still hands touches to the content, but leaves
    /// what is left of the cover painted - call <see cref="Reveal"/> to clear it later.
    /// </summary>
    public bool AutoReveal
    {
        get => (bool)this.GetValue(AutoRevealProperty);
        set => this.SetValue(AutoRevealProperty, value);
    }

    public static readonly BindableProperty IsScratchEnabledProperty = BindableProperty.Create(
        nameof(IsScratchEnabled), typeof(bool), typeof(ScratchCard), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ScratchCard), () => ((ScratchCard)b).UpdateSurface()));

    /// <summary>Whether strokes scratch. False holds the cover as it is (and lets touches through to nothing).</summary>
    public bool IsScratchEnabled
    {
        get => (bool)this.GetValue(IsScratchEnabledProperty);
        set => this.SetValue(IsScratchEnabledProperty, value);
    }

    public static readonly BindableProperty RevealDurationProperty = BindableProperty.Create(
        nameof(RevealDuration), typeof(TimeSpan), typeof(ScratchCard), TimeSpan.FromMilliseconds(300));

    /// <summary>How long the remaining cover takes to fade on reveal. Zero snaps it away.</summary>
    public TimeSpan RevealDuration
    {
        get => (TimeSpan)this.GetValue(RevealDurationProperty);
        set => this.SetValue(RevealDurationProperty, value);
    }

    public static readonly BindableProperty IsRevealedProperty = BindableProperty.Create(
        nameof(IsRevealed), typeof(bool), typeof(ScratchCard), false, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ScratchCard), () => ((ScratchCard)b).OnIsRevealedChanged((bool)n)));

    /// <summary>
    /// Whether the cover has cleared. Two-way: becomes true when the user scratches past the
    /// threshold; set it true to reveal from code, false to put a fresh cover back.
    /// </summary>
    public bool IsRevealed
    {
        get => (bool)this.GetValue(IsRevealedProperty);
        set => this.SetValue(IsRevealedProperty, value);
    }

    static readonly BindablePropertyKey ScratchedPercentPropertyKey = BindableProperty.CreateReadOnly(
        nameof(ScratchedPercent), typeof(double), typeof(ScratchCard), 0d);

    public static readonly BindableProperty ScratchedPercentProperty = ScratchedPercentPropertyKey.BindableProperty;

    /// <summary>How much of the cover has been scratched away, 0 to 100. Jumps to 100 on reveal.</summary>
    public double ScratchedPercent => (double)this.GetValue(ScratchedPercentProperty);

    public static readonly BindableProperty RevealedCommandProperty = BindableProperty.Create(
        nameof(RevealedCommand), typeof(ICommand), typeof(ScratchCard));

    /// <summary>Executed when the card reveals, alongside <see cref="Revealed"/>.</summary>
    public ICommand? RevealedCommand
    {
        get => (ICommand?)this.GetValue(RevealedCommandProperty);
        set => this.SetValue(RevealedCommandProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // Events and methods
    // ---------------------------------------------------------------------------------------------

    /// <summary>A stroke started on the cover.</summary>
    public event EventHandler? ScratchStarted;

    /// <summary><see cref="ScratchedPercent"/> moved.</summary>
    public event EventHandler<ScratchProgressEventArgs>? ScratchProgressChanged;

    /// <summary>
    /// The scratched share passed <see cref="ThresholdPercent"/>. Raised once per cover - again only
    /// after a <see cref="Reset"/> - whether or not <see cref="AutoReveal"/> then clears the rest.
    /// </summary>
    public event EventHandler<ScratchProgressEventArgs>? ThresholdReached;

    /// <summary>The cover cleared - by <see cref="AutoReveal"/> at the threshold, or revealed from code.</summary>
    public event EventHandler? Revealed;


    /// <summary>Clears the cover without scratching.</summary>
    public void Reveal(bool animate = true)
    {
        if (this.revealed)
            return;

        this.revealed = true;
        this.EndStroke();
        this.SetIsRevealed(true);
        this.SetPercent(100);
        this.UpdateSurface();

        var duration = animate ? (uint)Math.Max(0, this.RevealDuration.TotalMilliseconds) : 0;
        if (duration == 0 || this.coverHost.Handler is null)
        {
            this.coverHost.Opacity = 0;
            this.coverHost.IsVisible = false;
        }
        else
        {
            _ = this.FadeCoverAsync(duration);
        }

        this.Revealed?.Invoke(this, EventArgs.Empty);
        if (this.RevealedCommand?.CanExecute(null) == true)
            this.RevealedCommand.Execute(null);
    }


    async Task FadeCoverAsync(uint duration)
    {
        await this.coverHost.FadeToAsync(0, duration, Easing.CubicOut);

        // A Reset during the fade cancels it and puts the cover back; leave that cover showing.
        if (this.revealed)
            this.coverHost.IsVisible = false;
    }


    /// <summary>Puts a fresh, unscratched cover back.</summary>
    public void Reset()
    {
        this.coverHost.CancelAnimations();
        this.revealed = false;
        this.thresholdReached = false;
        this.EndStroke();
        this.mask.Clear();
        this.coverHost.Opacity = 1;
        this.coverHost.IsVisible = true;
        this.UpdateClip();
        this.SetIsRevealed(false);
        this.SetPercent(0);
        this.UpdateSurface();
    }


    // ---------------------------------------------------------------------------------------------
    // Internals
    // ---------------------------------------------------------------------------------------------

    internal ScratchMask Mask => this.mask;

    internal View CoverHost => this.coverHost;

    internal View HiddenHost => this.hiddenHost;

    internal View Surface => this.surface;

    /// <summary>The threshold was reached or the card revealed: the content takes touches and scratching is over.</summary>
    internal bool IsUnlocked => this.revealed || this.thresholdReached;

    bool CanScratch => this.IsScratchEnabled && !this.IsUnlocked;

    // About 1.25 units at the default stroke: ~3 pixels on a 3x screen, so the edge reads as round. A
    // 320x180 card is then ~37k cells (a bool each, a microsecond-scale scan per move) and a few hundred
    // clip rectangles while it is being scratched.
    internal double CellSize => Math.Clamp(this.StrokeWidth / 36d, 1.25d, 2d);


    /// <summary>Scratches along a stroke, in card coordinates. The touch handlers call it; so do tests.</summary>
    internal void ScratchTo(Point point, bool begin)
    {
        if (!this.CanScratch)
            return;

        if (begin)
        {
            this.stroking = true;
            this.lastPoint = point;
            this.ScratchStarted?.Invoke(this, EventArgs.Empty);
        }
        else if (!this.stroking)
        {
            return;
        }

        var changed = this.mask.Scratch(this.lastPoint, point, this.StrokeWidth / 2d);
        this.lastPoint = point;
        if (!changed)
            return;

        this.UpdateClip();
        var percent = this.mask.ScratchedFraction * 100d;
        this.SetPercent(percent);

        if (this.thresholdReached || percent < this.ThresholdPercent)
            return;

        // Scratching is done: the surface steps aside and the content starts taking touches - with
        // AutoReveal off too, where the rest of the cover stays painted but no longer catches anything.
        this.thresholdReached = true;
        this.EndStroke();
        this.UpdateSurface();

        var args = new ScratchProgressEventArgs(percent);
        this.ThresholdReached?.Invoke(this, args);
        if (this.ThresholdReachedCommand?.CanExecute(percent) == true)
            this.ThresholdReachedCommand.Execute(percent);

        // A handler may have revealed or reset the card already.
        if (this.AutoReveal && !this.revealed && this.thresholdReached)
            this.Reveal();
    }


    void OnStartInteraction(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0 || !this.CanScratch)
            return;

        this.LockScroller(true);
        this.ScratchTo(e.Touches[0], true);
    }


    void OnDragInteraction(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length > 0)
            this.ScratchTo(e.Touches[0], false);
    }


    void OnEndInteraction(object? sender, TouchEventArgs e) => this.EndStroke();


    void EndStroke()
    {
        this.stroking = false;
        this.LockScroller(false);
    }


    /// <summary>
    /// Keeps an enclosing scroller off the stroke in flight. Android's request walks every ancestor and
    /// the framework clears it on the next touch-down, so it only ever covers this one gesture.
    /// </summary>
    void LockScroller(bool locked)
    {
#if IOS
        this.touchHook.LockScroller(locked);
#elif ANDROID
        (this.surface.Handler?.PlatformView as Android.Views.View)?.Parent?.RequestDisallowInterceptTouchEvent(locked);
#endif
    }


    void OnCoverSized()
    {
        var width = this.coverHost.Width;
        var height = this.coverHost.Height;
        if (width <= 0 || height <= 0)
            return;

        this.mask.Resize(width, height, this.CellSize);
        this.UpdateClip();
    }


    /// <summary>
    /// Clips the cover to what is left of it. Always a geometry, never null, even before the first
    /// scratch: setting the first clip wraps the native view in a container, and doing that while a
    /// stroke is in flight would re-parent the view under the finger.
    /// </summary>
    void UpdateClip()
    {
        if (this.mask.IsEmpty)
            return;

        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var rect in this.mask.RemainingRects())
            group.Children.Add(new RectangleGeometry(rect));

        this.coverHost.Clip = group;
    }


    void ApplyCover()
    {
        if (this.Cover is { } cover)
        {
            this.coverHost.Content = cover;
            return;
        }

        this.defaultCover ??= CreateDefaultCover();
        this.coverHost.Content = this.defaultCover;
    }


    void UpdateSurface()
    {
        this.surface.InputTransparent = !this.CanScratch;
        this.hiddenHost.InputTransparent = !this.IsUnlocked;
        this.UpdateEdgeGestures();
    }


    /// <summary>
    /// Android 10+ takes a swipe that starts near the left or right screen edge as Back, which on a
    /// card laid out with the usual 16-unit page margin is a stroke starting on the card's own edge:
    /// the app would navigate away mid-scratch. Excluding the surface from the system gestures while
    /// it can scratch keeps the stroke; the exclusion goes again once the card unlocks. Android caps
    /// exclusions at 200dp of height per edge, which covers any sensible card.
    /// </summary>
    void UpdateEdgeGestures()
    {
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(29) || this.surface.Handler?.PlatformView is not Android.Views.View native)
            return;

        // From the MAUI size: SizeChanged arrives before the native view's own layout pass.
        var width = (int)Math.Ceiling(Microsoft.Maui.Platform.ContextExtensions.ToPixels(native.Context!, this.surface.Width));
        var height = (int)Math.Ceiling(Microsoft.Maui.Platform.ContextExtensions.ToPixels(native.Context!, this.surface.Height));
        native.SystemGestureExclusionRects = this.CanScratch && width > 0 && height > 0
            ? [new Android.Graphics.Rect(0, 0, width, height)]
            : [];
#endif
    }


    void OnIsRevealedChanged(bool value)
    {
        if (this.syncing)
            return;

        if (value)
            this.Reveal();
        else
            this.Reset();
    }


    void SetIsRevealed(bool value)
    {
        if (this.IsRevealed == value)
            return;

        this.syncing = true;
        try
        {
            this.SetValue(IsRevealedProperty, value);
        }
        finally
        {
            this.syncing = false;
        }
    }


    void SetPercent(double value)
    {
        if (Math.Abs(this.ScratchedPercent - value) < 0.01)
            return;

        this.SetValue(ScratchedPercentPropertyKey, value);
        this.ScratchProgressChanged?.Invoke(this, new ScratchProgressEventArgs(value));
    }


    static View CreateDefaultCover() => new Grid
    {
        Background = new LinearGradientBrush(
            [
                new GradientStop(Color.FromArgb("#D7DBE0"), 0f),
                new GradientStop(Color.FromArgb("#AEB4BC"), 0.45f),
                new GradientStop(Color.FromArgb("#E9ECEF"), 0.55f),
                new GradientStop(Color.FromArgb("#9EA5AE"), 1f)
            ],
            new Point(0, 0),
            new Point(1, 1)),
        Children =
        {
            new Label
            {
                Text = "Scratch here",
                TextColor = Color.FromArgb("#4B5563"),
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        }
    };


    sealed class NothingDrawable : IDrawable
    {
        public static readonly NothingDrawable Instance = new();
        public void Draw(ICanvas canvas, RectF dirtyRect) { }
    }
}


/// <summary>How much of a <see cref="ScratchCard"/>'s cover is gone.</summary>
public sealed class ScratchProgressEventArgs(double percent) : EventArgs
{
    /// <summary>The scratched share of the cover, 0 to 100.</summary>
    public double Percent { get; } = percent;
}
