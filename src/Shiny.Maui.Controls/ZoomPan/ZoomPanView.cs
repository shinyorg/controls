using System.Windows.Input;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

/// <summary>
/// Wraps any content in pinch-to-zoom, pan and double-tap zoom — a <c>StackLayout</c>, a chart, a
/// form, a <c>Grid</c> of anything at all, not only a picture.
/// </summary>
/// <remarks>
/// This is <see cref="ImageViewer"/>'s zoom machinery with the picture taken out: the same
/// <see cref="ZoomPanController"/> drives both, so the clamps and the gesture arbitration are one
/// implementation rather than two. The difference is what it is for — <see cref="ImageViewer"/> is a
/// full-screen lightbox you open, and this is an inline surface that stays where you put it and
/// clips its content to its own bounds.
/// <para>
/// Zoom is a render transform: the content keeps its laid-out size, so nothing re-flows and no
/// measure pass runs while a pinch is in progress. What is inside stays live and interactive —
/// buttons still press, entries still take text — because the pan recognizer is only attached while
/// the content is actually zoomed. At rest the content owns its gestures outright.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// &lt;shiny:ZoomPanView MaxZoom="4"&gt;
///     &lt;VerticalStackLayout&gt;
///         &lt;Label Text="Pinch me" /&gt;
///         &lt;Button Text="Still clickable" /&gt;
///     &lt;/VerticalStackLayout&gt;
/// &lt;/shiny:ZoomPanView&gt;
/// </code>
/// </example>
public class ZoomPanView : ContentView
{
    readonly ZoomPanController controller;

    /// <summary>
    /// What actually carries the transform. Scaling the <see cref="ZoomPanView"/> itself would scale
    /// its layout slot along with its content — the control would grow out of the space it was given
    /// and paint over its neighbours, rather than zooming content inside a fixed frame. The outer
    /// view keeps its size and clips; this inner one moves.
    /// </summary>
    readonly ContentView surface = new();

    /// <summary>
    /// A <see cref="Layout"/> purely to do the clipping. <c>IsClippedToBounds</c> is honoured by
    /// layouts; on a <see cref="ContentView"/> it is not, and neither is a <c>Clip</c> geometry — a
    /// 2x surface painted straight over the neighbouring content on iOS either way.
    /// </summary>
    readonly Grid clipHost;

    /// <summary>Guards the two-way loop between <see cref="ZoomLevel"/> and the controller.</summary>
    bool syncingZoom;

    /// <summary>Guards the re-entrant <see cref="Content"/> write that installs the surface.</summary>
    bool wrapping;


    public ZoomPanView()
    {
        this.controller = new ZoomPanController(this.surface)
        {
            DoubleTapping = () =>
            {
                if (this.UseFeedback)
                    FeedbackHelper.Execute(this, "DoubleTapped");
            }
        };
        this.controller.ZoomChanged += this.OnControllerZoomChanged;

        // Zoom is a transform, so the content draws outside its box the moment it grows. On iOS
        // IsClippedToBounds alone does not hold it in - a 2x surface painted straight over the
        // neighbouring layout - so an explicit Clip geometry is tracked against the control's size.
        this.IsClippedToBounds = true;
        this.clipHost = new Grid { IsClippedToBounds = true, Children = { this.surface } };
        base.Content = this.clipHost;

        this.controller.Attach();

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(ZoomPanView));
    }


    /// <summary>The transformed view, for tests and for anything that needs the real target.</summary>
    internal View Surface => this.surface;


    /// <summary>
    /// Keeps the clip rectangle the same size as the control it is clipping.
    /// </summary>
    /// <remarks>
    /// Hooked from <see cref="OnSizeAllocated"/> rather than <c>SizeChanged</c>: the event fires
    /// before the arrange that gives the control its final box, so a clip built from it is the wrong
    /// size and the zoomed content spills over the neighbouring layout anyway.
    /// </remarks>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        this.Clip = width > 0 && height > 0
            ? new Microsoft.Maui.Controls.Shapes.RectangleGeometry(new Rect(0, 0, width, height))
            : null;
    }


    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName == nameof(this.Content))
            this.WrapContent();
    }


    /// <summary>
    /// Moves whatever was assigned as <see cref="Content"/> inside the surface, so callers keep
    /// writing the property they expect while the transform still lands on an inner view.
    /// </summary>
    void WrapContent()
    {
        // The comparison, not the flag alone, is what makes this safe: MAUI can defer a SetValue
        // raised from inside a propertyChanged, so the write can land after the guard is released.
        if (this.wrapping || this.Content is null || ReferenceEquals(this.Content, this.clipHost))
            return;

        this.wrapping = true;
        try
        {
            this.surface.Content = this.Content;
            base.Content = this.clipHost;
        }
        finally
        {
            this.wrapping = false;
        }
    }


    // ---------------------------------------------------------------------------------------------
    // Limits
    // ---------------------------------------------------------------------------------------------

    /// <summary>Floor for <see cref="ZoomLevel"/>. Below 1 the content may sit smaller than its box.</summary>
    public static readonly BindableProperty MinZoomProperty = BindableProperty.Create(
        nameof(MinZoom),
        typeof(double),
        typeof(ZoomPanView),
        1.0,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ZoomPanView>(b, ctrl => ctrl.controller.MinZoom = (double)n)
    );

    /// <inheritdoc cref="MinZoomProperty" />
    public double MinZoom
    {
        get => (double)this.GetValue(MinZoomProperty);
        set => this.SetValue(MinZoomProperty, value);
    }


    /// <summary>Ceiling for <see cref="ZoomLevel"/>.</summary>
    public static readonly BindableProperty MaxZoomProperty = BindableProperty.Create(
        nameof(MaxZoom),
        typeof(double),
        typeof(ZoomPanView),
        5.0,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ZoomPanView>(b, ctrl => ctrl.controller.MaxZoom = (double)n)
    );

    /// <inheritdoc cref="MaxZoomProperty" />
    public double MaxZoom
    {
        get => (double)this.GetValue(MaxZoomProperty);
        set => this.SetValue(MaxZoomProperty, value);
    }


    /// <summary>
    /// The scale on the content. Two-way bindable: writing it zooms, and every gesture reports back
    /// through it.
    /// </summary>
    public static readonly BindableProperty ZoomLevelProperty = BindableProperty.Create(
        nameof(ZoomLevel),
        typeof(double),
        typeof(ZoomPanView),
        1.0,
        BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ZoomPanView>(b, ctrl => ctrl.OnZoomLevelChanged((double)n))
    );

    /// <inheritdoc cref="ZoomLevelProperty" />
    public double ZoomLevel
    {
        get => (double)this.GetValue(ZoomLevelProperty);
        set => this.SetValue(ZoomLevelProperty, value);
    }


    static readonly BindablePropertyKey IsZoomedPropertyKey = BindableProperty.CreateReadOnly(
        nameof(IsZoomed),
        typeof(bool),
        typeof(ZoomPanView),
        false
    );

    /// <summary>True while the content is scaled past its natural size — which is when it pans.</summary>
    public static readonly BindableProperty IsZoomedProperty = IsZoomedPropertyKey.BindableProperty;

    /// <inheritdoc cref="IsZoomedProperty" />
    public bool IsZoomed => (bool)this.GetValue(IsZoomedProperty);


    // ---------------------------------------------------------------------------------------------
    // Behaviour
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Turns every gesture off without unbinding anything, handing the content's own gestures
    /// straight back. Any zoom already applied is dropped.
    /// </summary>
    public static readonly BindableProperty IsZoomEnabledProperty = BindableProperty.Create(
        nameof(IsZoomEnabled),
        typeof(bool),
        typeof(ZoomPanView),
        true,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ZoomPanView>(b, ctrl => ctrl.OnZoomEnabledChanged((bool)n))
    );

    /// <inheritdoc cref="IsZoomEnabledProperty" />
    public bool IsZoomEnabled
    {
        get => (bool)this.GetValue(IsZoomEnabledProperty);
        set => this.SetValue(IsZoomEnabledProperty, value);
    }


    /// <summary>
    /// Double tap to zoom in, and again to come back out. Off when the content wants double taps of
    /// its own.
    /// </summary>
    public static readonly BindableProperty DoubleTapToZoomProperty = BindableProperty.Create(
        nameof(DoubleTapToZoom),
        typeof(bool),
        typeof(ZoomPanView),
        true,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ZoomPanView>(b, ctrl => ctrl.controller.DoubleTapToZoom = (bool)n)
    );

    /// <inheritdoc cref="DoubleTapToZoomProperty" />
    public bool DoubleTapToZoom
    {
        get => (bool)this.GetValue(DoubleTapToZoomProperty);
        set => this.SetValue(DoubleTapToZoomProperty, value);
    }


    /// <summary>Where a double tap zooms to, capped by <see cref="MaxZoom"/>.</summary>
    public static readonly BindableProperty DoubleTapZoomProperty = BindableProperty.Create(
        nameof(DoubleTapZoom),
        typeof(double),
        typeof(ZoomPanView),
        2.5,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ZoomPanView>(b, ctrl => ctrl.controller.DoubleTapZoom = (double)n)
    );

    /// <inheritdoc cref="DoubleTapZoomProperty" />
    public double DoubleTapZoom
    {
        get => (double)this.GetValue(DoubleTapZoomProperty);
        set => this.SetValue(DoubleTapZoomProperty, value);
    }


    /// <summary>Length of the double-tap and programmatic animations, in milliseconds. Zero snaps.</summary>
    public static readonly BindableProperty AnimationLengthProperty = BindableProperty.Create(
        nameof(AnimationLength),
        typeof(int),
        typeof(ZoomPanView),
        250,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ZoomPanView>(b, ctrl => ctrl.controller.AnimationLength = (uint)Math.Max(0, (int)n))
    );

    /// <inheritdoc cref="AnimationLengthProperty" />
    public int AnimationLength
    {
        get => (int)this.GetValue(AnimationLengthProperty);
        set => this.SetValue(AnimationLengthProperty, value);
    }


    /// <summary>Play the platform's feedback on a double tap.</summary>
    public static readonly BindableProperty UseFeedbackProperty = BindableProperty.Create(
        nameof(UseFeedback),
        typeof(bool),
        typeof(ZoomPanView),
        false
    );

    /// <inheritdoc cref="UseFeedbackProperty" />
    public bool UseFeedback
    {
        get => (bool)this.GetValue(UseFeedbackProperty);
        set => this.SetValue(UseFeedbackProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // Notifications
    // ---------------------------------------------------------------------------------------------

    /// <summary>Raised on every change of scale, mid-pinch included.</summary>
    public event EventHandler<ZoomPanChangedEventArgs>? ZoomChanged;

    /// <summary>Command counterpart of <see cref="ZoomChanged"/>, handed the new scale.</summary>
    public static readonly BindableProperty ZoomChangedCommandProperty = BindableProperty.Create(
        nameof(ZoomChangedCommand),
        typeof(ICommand),
        typeof(ZoomPanView)
    );

    /// <inheritdoc cref="ZoomChangedCommandProperty" />
    public ICommand? ZoomChangedCommand
    {
        get => (ICommand?)this.GetValue(ZoomChangedCommandProperty);
        set => this.SetValue(ZoomChangedCommandProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // API
    // ---------------------------------------------------------------------------------------------

    /// <summary>Animates the content back to its natural size and position.</summary>
    public Task ResetZoomAsync() => this.controller.ResetAsync();

    /// <summary>
    /// Animates to <paramref name="zoom"/>, clamped into <see cref="MinZoom"/>..<see cref="MaxZoom"/>.
    /// A <paramref name="focus"/> in this view's own coordinates keeps that point where it is.
    /// </summary>
    public Task ZoomToAsync(double zoom, Point? focus = null) => this.controller.ZoomToAsync(zoom, focus);


    // ---------------------------------------------------------------------------------------------
    // Plumbing
    // ---------------------------------------------------------------------------------------------

    void OnControllerZoomChanged(object? sender, EventArgs e)
    {
        this.SetValue(IsZoomedPropertyKey, this.controller.IsZoomed);

        this.syncingZoom = true;
        try
        {
            this.ZoomLevel = this.controller.Zoom;
        }
        finally
        {
            this.syncingZoom = false;
        }

        var args = new ZoomPanChangedEventArgs(this.controller.Zoom, this.controller.IsZoomed);
        this.ZoomChanged?.Invoke(this, args);

        if (this.ZoomChangedCommand?.CanExecute(args) == true)
            this.ZoomChangedCommand.Execute(args);
    }


    void OnZoomLevelChanged(double value)
    {
        // The comparison, not the flag, is what makes this safe: MAUI can defer a SetValue raised
        // from inside a propertyChanged, so the write can land after the guard has been released.
        // Two values that already agree are a no-op either way.
        if (this.syncingZoom || Math.Abs(value - this.controller.Zoom) < 0.001)
            return;

        _ = this.controller.ZoomToAsync(value);
    }


    void OnZoomEnabledChanged(bool enabled)
    {
        if (enabled)
        {
            this.controller.Attach();
        }
        else
        {
            this.controller.Reset();
            this.controller.Detach();
        }
    }
}


/// <summary>Carries the scale a <see cref="ZoomPanView"/> has arrived at.</summary>
/// <param name="ZoomLevel">The scale now on the content.</param>
/// <param name="IsZoomed">Whether that is past the content's natural size.</param>
public record ZoomPanChangedEventArgs(double ZoomLevel, bool IsZoomed);
