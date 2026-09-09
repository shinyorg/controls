namespace Shiny.Maui.Controls;

/// <summary>
/// Pinch, pan and double-tap zoom over an arbitrary <see cref="View"/>.
/// </summary>
/// <remarks>
/// Nothing in here knows what it is transforming. The limits are worked out from the target's
/// laid-out box and the result is written as <see cref="VisualElement.Scale"/> plus
/// <see cref="VisualElement.TranslationX"/>/<see cref="VisualElement.TranslationY"/> — which is what
/// lets one implementation drive both <see cref="ImageViewer"/>'s full-screen overlay and
/// <see cref="ZoomPanView"/>'s arbitrary content.
/// <para>
/// The pan recognizer is attached only while there is somewhere to pan to. A
/// <see cref="PanGestureRecognizer"/> on a container competes with everything inside it, so at rest
/// the content keeps its own gestures and only starts sharing them once it is zoomed.
/// </para>
/// </remarks>
sealed class ZoomPanController
{
    const double Epsilon = 0.001;

    /// <summary>The content's natural size. Reset and double-tap-out both land here.</summary>
    const double RestingZoom = 1;

    readonly View target;
    readonly PinchGestureRecognizer pinch;
    readonly PanGestureRecognizer pan;
    readonly TapGestureRecognizer doubleTap;

    double scale = RestingZoom;
    double pinchStartScale = RestingZoom;
    double xOffset;
    double yOffset;
    double panStartX;
    double panStartY;
    bool doubleTapToZoom = true;
    bool isPinching;
    bool isAnimating;
    bool isAttached;


    public ZoomPanController(View target)
    {
        this.target = target;

        this.pinch = new PinchGestureRecognizer();
        this.pinch.PinchUpdated += this.OnPinchUpdated;

        this.pan = new PanGestureRecognizer();
        this.pan.PanUpdated += this.OnPanUpdated;

        this.doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        this.doubleTap.Tapped += this.OnDoubleTapped;
    }


    /// <summary>Floor for the scale. Below 1 the content may sit smaller than its box and stay there.</summary>
    public double MinZoom { get; set; } = 1;

    /// <summary>Ceiling for the scale.</summary>
    public double MaxZoom { get; set; } = 5;

    /// <summary>Where a double tap zooms to, capped by <see cref="MaxZoom"/>.</summary>
    public double DoubleTapZoom { get; set; } = 2.5;

    /// <summary>Length of the double-tap and programmatic animations, in milliseconds. Zero snaps.</summary>
    public uint AnimationLength { get; set; } = 250;

    /// <summary>The scale currently on the target.</summary>
    public double Zoom => this.scale;

    /// <summary>Scaled past its natural size — which is the same thing as "there is something to pan".</summary>
    public bool IsZoomed => this.scale > RestingZoom + Epsilon;

    /// <summary>Raised on every change of scale, mid-pinch included.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>Called before a double tap acts, for a haptic or a sound.</summary>
    public Action? DoubleTapping { get; set; }


    /// <summary>Double tap to zoom in and back out. Toggling this adds or removes the recognizer.</summary>
    public bool DoubleTapToZoom
    {
        get => this.doubleTapToZoom;
        set
        {
            this.doubleTapToZoom = value;
            this.SyncDoubleTap();
        }
    }


    // ---------------------------------------------------------------------------------------------
    // Wiring
    // ---------------------------------------------------------------------------------------------

    /// <summary>Puts the recognizers on the target.</summary>
    public void Attach()
    {
        if (this.isAttached)
            return;

        this.isAttached = true;
        this.target.GestureRecognizers.Add(this.pinch);
        this.SyncDoubleTap();
        this.SyncPan();
    }


    /// <summary>Takes every recognizer back off, leaving the target's own gestures alone.</summary>
    public void Detach()
    {
        if (!this.isAttached)
            return;

        this.isAttached = false;
        this.target.GestureRecognizers.Remove(this.pinch);
        this.SyncDoubleTap();
        this.SyncPan();
    }


    void SyncDoubleTap() => Sync(this.target, this.doubleTap, this.isAttached && this.doubleTapToZoom);

    void SyncPan() => Sync(this.target, this.pan, this.isAttached && this.IsZoomed);

    static void Sync(View view, IGestureRecognizer recognizer, bool wanted)
    {
        var present = view.GestureRecognizers.Contains(recognizer);

        if (wanted && !present)
            view.GestureRecognizers.Add(recognizer);
        else if (!wanted && present)
            view.GestureRecognizers.Remove(recognizer);
    }


    // ---------------------------------------------------------------------------------------------
    // Transform
    // ---------------------------------------------------------------------------------------------

    /// <summary>Drops the transform with no animation, and stops panning.</summary>
    public void Reset()
    {
        this.scale = RestingZoom;
        this.xOffset = 0;
        this.yOffset = 0;
        this.target.Scale = RestingZoom;
        this.target.TranslationX = 0;
        this.target.TranslationY = 0;
        this.SyncPan();
        this.ZoomChanged?.Invoke(this, EventArgs.Empty);
    }


    /// <summary>Animates back to the content's natural size and position.</summary>
    public Task ResetAsync() => this.ZoomToAsync(RestingZoom);


    /// <summary>
    /// Animates to <paramref name="zoom"/>. A <paramref name="focus"/> in the target's own
    /// coordinates keeps that point where it is; without one the zoom is about the centre.
    /// </summary>
    public async Task ZoomToAsync(double zoom, Point? focus = null)
    {
        if (this.isAnimating)
            return;

        var next = Math.Clamp(zoom, this.MinZoom, this.MaxZoom);
        double tx, ty;

        // The focal formula below is written against a resting target. Starting from an already
        // zoomed one it would need the current offset unwound first, so instead the existing offset
        // is carried over and re-clamped — the content keeps looking at roughly the same place.
        if (focus is { } point && !this.IsZoomed)
        {
            tx = -(point.X - this.target.Width / 2) * (next - RestingZoom);
            ty = -(point.Y - this.target.Height / 2) * (next - RestingZoom);
        }
        else
        {
            tx = this.xOffset;
            ty = this.yOffset;
        }

        tx = Limit(tx, this.target.Width, next);
        ty = Limit(ty, this.target.Height, next);

        this.isAnimating = true;
        try
        {
            // The state is committed before the animation rather than after it: a gesture that lands
            // mid-flight has to read where the target is going, not where it came from.
            this.scale = next;
            this.xOffset = tx;
            this.yOffset = ty;
            this.SyncPan();
            this.ZoomChanged?.Invoke(this, EventArgs.Empty);

            // Zero snaps. It is also the only path open to a target with no handler — MAUI's
            // animations resolve an IAnimationManager off one and throw without it.
            if (this.AnimationLength == 0)
            {
                this.target.Scale = next;
                this.target.TranslationX = tx;
                this.target.TranslationY = ty;
                return;
            }

            await Task.WhenAll(
                this.target.ScaleToAsync(next, this.AnimationLength, Easing.CubicOut),
                this.target.TranslateToAsync(tx, ty, this.AnimationLength, Easing.CubicOut)
            );
        }
        finally
        {
            this.isAnimating = false;
        }
    }


    /// <summary>
    /// How far the target may be moved on one axis: half the overhang the scale creates. At or below
    /// natural size there is no overhang and nothing to pan — and the limit would come out negative,
    /// which <see cref="Math.Clamp(double, double, double)"/> reads as min &gt; max and throws on.
    /// </summary>
    static double Limit(double value, double extent, double scale)
    {
        var max = extent * (scale - RestingZoom) / 2;

        return max <= 0 ? 0 : Math.Clamp(value, -max, max);
    }


    double LimitX(double x) => Limit(x, this.target.Width, this.scale);

    double LimitY(double y) => Limit(y, this.target.Height, this.scale);


    // ---------------------------------------------------------------------------------------------
    // Gestures
    // ---------------------------------------------------------------------------------------------

    void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        switch (e.Status)
        {
            case GestureStatus.Started:
                this.isPinching = true;
                this.pinchStartScale = this.scale;
                break;

            case GestureStatus.Running:
                this.scale = Math.Clamp(
                    this.scale + (e.Scale - 1) * this.pinchStartScale,
                    this.MinZoom,
                    this.MaxZoom
                );

                // ScaleOrigin is a 0..1 point in the target, so it has to be turned into an offset
                // from the centre before it can move the translation.
                var pinchX = (e.ScaleOrigin.X - 0.5) * this.target.Width;
                var pinchY = (e.ScaleOrigin.Y - 0.5) * this.target.Height;
                var delta = this.scale - this.pinchStartScale;

                this.target.TranslationX = this.LimitX(this.xOffset - pinchX * delta);
                this.target.TranslationY = this.LimitY(this.yOffset - pinchY * delta);
                this.target.Scale = this.scale;
                this.ZoomChanged?.Invoke(this, EventArgs.Empty);
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                this.isPinching = false;
                this.xOffset = this.target.TranslationX;
                this.yOffset = this.target.TranslationY;
                this.SyncPan();
                break;
        }
    }


    void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        // A pinch moves both fingers, which the pan recognizer also reports; without this the two
        // fight over the translation for the length of every pinch.
        if (this.isPinching || !this.IsZoomed)
            return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                this.panStartX = this.xOffset;
                this.panStartY = this.yOffset;
                break;

            case GestureStatus.Running:
                this.target.TranslationX = this.LimitX(this.panStartX + e.TotalX);
                this.target.TranslationY = this.LimitY(this.panStartY + e.TotalY);
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                this.xOffset = this.target.TranslationX;
                this.yOffset = this.target.TranslationY;
                break;
        }
    }


    void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (this.isAnimating)
            return;

        this.DoubleTapping?.Invoke();

        if (this.IsZoomed)
        {
            _ = this.ResetAsync();
        }
        else
        {
            var to = Math.Min(this.DoubleTapZoom, this.MaxZoom);
            _ = this.ZoomToAsync(to, e.GetPosition(this.target));
        }
    }
}
