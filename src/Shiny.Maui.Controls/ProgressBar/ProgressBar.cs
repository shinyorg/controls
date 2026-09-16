using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

public partial class ProgressBar : ContentView, IDisposable
{
    readonly BoxView trackBackground;
    readonly BoxView trackFill;
    readonly BoxView pulseOverlay;
    readonly Label progressLabel;
    readonly Grid fillGrid;
    readonly Grid trackGrid;
    readonly Grid segmentsGrid;
    readonly List<(BoxView Track, BoxView Fill)> segments = new();

    const string FillAnimationName = "ShinyProgressFill";

    IDispatcherTimer? pulseTimer;
    bool isAnimatingPulse;
    bool isAnimatingIndeterminate;
    double trackWidth;

    /// <summary>
    /// The fill width currently on screen, which during a slide is not the width
    /// <see cref="Value"/> implies. Read as the start point of the next slide so that retargeting
    /// mid-flight continues from where the bar actually is rather than snapping to the last target.
    /// </summary>
    double currentFillWidth;

    public ProgressBar()
    {
        trackBackground = new BoxView
        {
            HeightRequest = 8,
            CornerRadius = new CornerRadius(4),
            VerticalOptions = LayoutOptions.Center
        };
        // Theme default — overridden if the consumer sets TrackColor explicitly.
        // Color is bound alongside BackgroundColor because the macOS/AppKit BoxView handler paints
        // from Color only and ignores the background brush, which left the track invisible there.
        trackBackground.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHighest);
        trackBackground.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.SurfaceContainerHighest);

        trackFill = new BoxView
        {
            HeightRequest = 8,
            CornerRadius = new CornerRadius(4),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start,
            WidthRequest = 0
        };

        pulseOverlay = new BoxView
        {
            HeightRequest = 8,
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Start,
            BackgroundColor = Colors.White,
            Opacity = 0,
            InputTransparent = true
        };

        // fillGrid clips the pulse sheen inside the fill area
        fillGrid = new Grid
        {
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start,
            IsClippedToBounds = true,
            WidthRequest = 0
        };
        fillGrid.Children.Add(trackFill);
        fillGrid.Children.Add(pulseOverlay);

        progressLabel = new Label
        {
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            IsVisible = false
        }.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);
        // Theme default — overridden if the consumer sets TextColor explicitly.
        progressLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);

        trackGrid = new Grid
        {
            VerticalOptions = LayoutOptions.Center
        };

        trackGrid.Children.Add(trackBackground);
        trackGrid.Children.Add(fillGrid);

        // Built up front (empty) rather than added on demand: AppKit never realizes a child added to
        // an already laid-out tree.
        segmentsGrid = new Grid
        {
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false
        };
        trackGrid.Children.Add(segmentsGrid);
        trackGrid.Children.Add(progressLabel);

        Content = trackGrid;

        // MAUI never calls Dispose on a view, so the repeating pulse timer and the indeterminate loop
        // have to follow the tree: left running they are rooted by the platform timer/ticker and keep
        // the bar - and the page it was on - alive forever, sweeping away behind a page nobody sees.
        Loaded += OnBarLoaded;
        Unloaded += OnBarUnloaded;

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(ProgressBar));
    }

    void OnBarLoaded(object? sender, EventArgs e)
    {
        if (PulseEnabled && pulseTimer is null)
            ConfigurePulseTimer();

        if (IsIndeterminate && !isAnimatingIndeterminate)
            StartIndeterminateAnimation();
    }

    void OnBarUnloaded(object? sender, EventArgs e)
    {
        // Pause only - the properties are left as they are so OnBarLoaded can pick them back up.
        StopPulseTimer();
        this.AbortAnimation("PulseSweep");
        isAnimatingPulse = false;

        if (isAnimatingIndeterminate)
        {
            isAnimatingIndeterminate = false;
            indeterminateRun++;
            Microsoft.Maui.Controls.ViewExtensions.CancelAnimations(trackFill);
        }
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0)
        {
            trackWidth = width;
            UpdateVisuals();
        }
    }

    void OnValueChanged(double oldValue, double newValue)
    {
        UpdateVisuals(animate: true);

        if (PulseEnabled && PulseOnValueChange && Math.Abs(newValue - oldValue) > double.Epsilon)
            TriggerPulse();

        ValueChangedCommand?.Execute(newValue);
        ValueChangedEvent?.Invoke(this, newValue);
    }

    /// <param name="animate">
    /// True only when <see cref="Value"/> (or its bounds) moved. Layout-driven refreshes pass false
    /// so the bar does not re-animate every time it is measured.
    /// </param>
    void UpdateVisuals(bool animate = false)
    {
        if (trackWidth <= 0) return;
        if (IsIndeterminate) return;

        var percent = Maximum > Minimum
            ? Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1)
            : 0;

        var fillWidth = percent * trackWidth;

        // Track
        trackBackground.HeightRequest = TrackHeight;
        trackBackground.CornerRadius = new CornerRadius(CornerRadius);

        fillGrid.HeightRequest = TrackHeight;

        // Fill bar — fill the container
        trackFill.HeightRequest = TrackHeight;
        trackFill.CornerRadius = new CornerRadius(CornerRadius);
        trackFill.HorizontalOptions = LayoutOptions.Fill;

        ApplyFillPaint();
        UpdateSegmentsLayout();

        // Fill container (clips the pulse). Last, so the slide runs against final paint and sizing.
        SetFillWidth(fillWidth, animate);


        // Text
        progressLabel.IsVisible = ShowText;
        if (ShowText)
        {
            var displayPercent = percent * 100;
            progressLabel.Text = string.Format(TextFormat, displayPercent);
        }
    }

    /// <summary>
    /// Moves the fill to <paramref name="target"/>, sliding when the change came from a value change
    /// and snapping otherwise.
    /// </summary>
    /// <remarks>
    /// The slide is symmetric on purpose: a value that drops drains back at the same rate it filled,
    /// rather than the fill-only easing most progress bars ship, which makes a downward correction
    /// read as a glitch.
    /// </remarks>
    void SetFillWidth(double target, bool animate)
    {
        this.AbortAnimation(FillAnimationName);

        var from = Math.Clamp(currentFillWidth, 0, Math.Max(trackWidth, 0));
        var shouldAnimate = animate
            && AnimateProgress
            && ProgressAnimationDuration > 0
            && Math.Abs(target - from) > 0.5;

        if (!shouldAnimate)
        {
            ApplyFillWidth(target);
            UpdatePulseOverlaySize();
            return;
        }

        new Animation(ApplyFillWidth, from, target, ProgressAnimationEasing)
            .Commit(
                this,
                FillAnimationName,
                length: (uint)ProgressAnimationDuration,
                finished: (_, _) =>
                {
                    ApplyFillWidth(target);
                    UpdatePulseOverlaySize();
                }
            );
    }

    /// <summary>
    /// Test seam. The fill's width is the only externally observable result of the slide, and it
    /// lives on a private child.
    /// </summary>
    internal double CurrentFillWidth => this.currentFillWidth;

    void ApplyFillWidth(double width)
    {
        currentFillWidth = width;
        fillGrid.WidthRequest = width;
        trackFill.WidthRequest = width;
        ApplySegmentFill(width);
    }

    bool IsSegmented => Segments > 1 && !IsIndeterminate;

    /// <summary>Test seam: how much of each segment is lit, from 0 to 1.</summary>
    internal IReadOnlyList<double> SegmentFills
        => segments.Select((s, i) => SegmentFill(currentFillWidth, i)).ToList();

    double SegmentFill(double fillWidth, int index)
        => trackWidth > 0 && segments.Count > 0
            ? Math.Clamp(fillWidth / trackWidth * segments.Count - index, 0, 1)
            : 0;

    double SegmentWidth => segments.Count > 0
        ? Math.Max((trackWidth - Math.Max(SegmentSpacing, 0) * (segments.Count - 1)) / segments.Count, 0)
        : 0;

    /// <summary>
    /// Distributes the one continuous fill width across the steps, so the slide animation runs step
    /// by step through the gaps rather than every step animating at once.
    /// </summary>
    void ApplySegmentFill(double width)
    {
        if (segments.Count == 0)
            return;

        var segmentWidth = SegmentWidth;
        for (var i = 0; i < segments.Count; i++)
            segments[i].Fill.WidthRequest = SegmentFill(width, i) * segmentWidth;
    }

    void OnSegmentsChanged()
    {
        var count = Math.Max(Segments, 0);
        if (count < 2)
            count = 0;

        if (count != segments.Count)
        {
            segmentsGrid.Children.Clear();
            segmentsGrid.ColumnDefinitions.Clear();
            segments.Clear();

            for (var i = 0; i < count; i++)
            {
                segmentsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

                var track = new BoxView { VerticalOptions = LayoutOptions.Center };
                var fill = new BoxView
                {
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Start,
                    WidthRequest = 0
                };
                Grid.SetColumn(track, i);
                Grid.SetColumn(fill, i);
                segmentsGrid.Children.Add(track);
                segmentsGrid.Children.Add(fill);
                segments.Add((track, fill));
            }
        }
        this.AbortAnimation(FillAnimationName);
        UpdateVisuals();
    }

    void UpdateSegmentsLayout()
    {
        var segmented = IsSegmented;
        segmentsGrid.IsVisible = segmented;
        trackBackground.IsVisible = !segmented;
        fillGrid.IsVisible = !segmented;
        if (!segmented)
            return;

        segmentsGrid.ColumnSpacing = Math.Max(SegmentSpacing, 0);
        segmentsGrid.HeightRequest = TrackHeight;

        var radius = new CornerRadius(CornerRadius);
        for (var i = 0; i < segments.Count; i++)
        {
            var (track, fill) = segments[i];
            track.HeightRequest = TrackHeight;
            track.CornerRadius = radius;
            fill.HeightRequest = TrackHeight;
            fill.CornerRadius = radius;

            if (TrackColor is Color trackColor)
            {
                track.BackgroundColor = trackColor;
                track.Color = trackColor;
            }
            else
            {
                track.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHighest);
                track.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.SurfaceContainerHighest);
            }

            if (UseGradient)
            {
                // Each step takes the gradient's colour at its own centre, so the run reads as one
                // gradient without every step repeating the whole ramp. Solid, which AppKit can paint.
                var t = (float)((i + 0.5) / segments.Count);
                var color = LerpColor(GradientStartColor, GradientEndColor, t);
                fill.BackgroundColor = color;
                fill.Color = color;
            }
            else if (BarColor is Color barColor)
            {
                fill.BackgroundColor = barColor;
                fill.Color = barColor;
            }
            else
            {
                fill.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Primary);
                fill.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.Primary);
            }
        }
    }

    static Color LerpColor(Color from, Color to, float t) => new(
        from.Red + (to.Red - from.Red) * t,
        from.Green + (to.Green - from.Green) * t,
        from.Blue + (to.Blue - from.Blue) * t,
        from.Alpha + (to.Alpha - from.Alpha) * t
    );

    void UpdatePulseOverlaySize()
    {
        var percent = Maximum > Minimum
            ? Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1)
            : 0;
        var fillWidth = percent * trackWidth;
        var sheenWidth = fillWidth * Math.Clamp(PulseLength, 0.05, 1.0);

        pulseOverlay.WidthRequest = Math.Max(sheenWidth, 4);
        pulseOverlay.HeightRequest = TrackHeight;
    }

    void TriggerPulse()
    {
        if (isAnimatingPulse || IsSegmented) return;
        isAnimatingPulse = true;

        var percent = Maximum > Minimum
            ? Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1)
            : 0;
        var fillWidth = percent * trackWidth;
        var sheenWidth = fillWidth * Math.Clamp(PulseLength, 0.05, 1.0);

        pulseOverlay.WidthRequest = Math.Max(sheenWidth, 4);
        pulseOverlay.HeightRequest = TrackHeight;
        pulseOverlay.BackgroundColor = PulseColor;

        // Build a gradient sheen: transparent -> color -> transparent
        pulseOverlay.Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            GradientStops =
            {
                new GradientStop(Colors.Transparent, 0),
                new GradientStop(PulseColor.WithAlpha((float)PulseOpacity), 0.5f),
                new GradientStop(Colors.Transparent, 1)
            }
        };
        pulseOverlay.Opacity = 1;

        // Start off-screen left, sweep to off-screen right
        pulseOverlay.TranslationX = -sheenWidth;

        var animation = new Animation(
            v => pulseOverlay.TranslationX = v,
            -sheenWidth,
            fillWidth,
            Easing.CubicInOut);

        animation.Commit(this, "PulseSweep",
            length: (uint)PulseSpeed,
            finished: (_, _) =>
            {
                pulseOverlay.Opacity = 0;
                pulseOverlay.TranslationX = 0;
                isAnimatingPulse = false;
            });
    }

    void OnPulseEnabledChanged(bool enabled)
    {
        if (enabled)
            ConfigurePulseTimer();
        else
            StopPulseTimer();
    }

    void ConfigurePulseTimer()
    {
        StopPulseTimer();

        if (!PulseEnabled || PulseInterval <= TimeSpan.Zero)
            return;

        pulseTimer = Dispatcher.CreateTimer();
        pulseTimer.Interval = PulseInterval;
        pulseTimer.Tick += (_, _) => TriggerPulse();
        pulseTimer.Start();
    }

    void StopPulseTimer()
    {
        pulseTimer?.Stop();
        pulseTimer = null;
    }

    void OnIndeterminateChanged(bool indeterminate)
    {
        if (indeterminate)
            StartIndeterminateAnimation();
        else
            StopIndeterminateAnimation();
    }

    void StartIndeterminateAnimation()
    {
        if (isAnimatingIndeterminate) return;
        isAnimatingIndeterminate = true;

        this.AbortAnimation(FillAnimationName);
        UpdateSegmentsLayout();
        ApplyFillWidth(trackWidth);
        fillGrid.HeightRequest = TrackHeight;

        trackFill.HeightRequest = TrackHeight;
        trackFill.CornerRadius = new CornerRadius(CornerRadius);
        trackFill.HorizontalOptions = LayoutOptions.Start;

        ApplyFillPaint();

        progressLabel.IsVisible = false;
        RunIndeterminateLoop();
    }

    /// <summary>
    /// Paints the fill bar for the current gradient/solid mode.
    /// </summary>
    /// <remarks>
    /// A gradient can only be expressed as a <c>Background</c> brush, but a solid fill must also
    /// drive <see cref="BoxView.Color"/>: the macOS/AppKit BoxView handler paints from Color alone
    /// and ignores the background brush, so a BackgroundColor-only bar was invisible there. Color is
    /// cleared in gradient mode so it cannot paint over the gradient on platforms that honour both.
    /// </remarks>
    void ApplyFillPaint()
    {
        if (UseGradient)
        {
            trackFill.ClearValue(BoxView.ColorProperty);
            trackFill.Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5),
                GradientStops =
                {
                    new GradientStop(GradientStartColor, 0),
                    new GradientStop(GradientEndColor, 1)
                }
            };
            return;
        }

        trackFill.Background = null;
        if (BarColor is Color barColor)
        {
            trackFill.BackgroundColor = barColor;
            trackFill.Color = barColor;
        }
        else
        {
            trackFill.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Primary);
            trackFill.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.Primary);
        }
    }

    // Bumped on every start/stop so a loop still awaiting its last sweep cannot carry on alongside a
    // newer one after a quick unload/reload.
    int indeterminateRun;

    async void RunIndeterminateLoop()
    {
        var run = ++indeterminateRun;
        while (isAnimatingIndeterminate && run == indeterminateRun && trackWidth > 0)
        {
            var barWidth = trackWidth * 0.3;
            trackFill.WidthRequest = barWidth;
            trackFill.TranslationX = -barWidth;

            await trackFill.TranslateToAsync(trackWidth, 0, 1200, Easing.CubicInOut);

            if (!isAnimatingIndeterminate || run != indeterminateRun) break;

            trackFill.TranslationX = -barWidth;
        }
    }

    void StopIndeterminateAnimation()
    {
        isAnimatingIndeterminate = false;
        Microsoft.Maui.Controls.ViewExtensions.CancelAnimations(trackFill);
        trackFill.TranslationX = 0;

        // Snap: the fill is currently a 30% bar parked mid-track, so sliding from there to the real
        // value would read as the bar running backwards rather than as the mode change it is.
        currentFillWidth = 0;
        UpdateVisuals();
    }

    public void Dispose()
    {
        StopPulseTimer();
        isAnimatingIndeterminate = false;
        Microsoft.Maui.Controls.ViewExtensions.CancelAnimations(trackFill);
        this.AbortAnimation("PulseSweep");
        this.AbortAnimation(FillAnimationName);
        GC.SuppressFinalize(this);
    }
}
