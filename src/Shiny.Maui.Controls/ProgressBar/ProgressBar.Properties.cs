using System.Windows.Input;
using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

public partial class ProgressBar
{
    // Value
    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value), typeof(double), typeof(ProgressBar), 0.0,
        BindingMode.TwoWay,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(ProgressBar), () =>
            {
            var pb = (ProgressBar)b;
            pb.OnValueChanged((double)o, (double)n);
        }));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public static readonly BindableProperty MinimumProperty = BindableProperty.Create(
        nameof(Minimum), typeof(double), typeof(ProgressBar), 0.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ProgressBar), () =>
            {
                ((ProgressBar)b).UpdateVisuals();
            }));
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    public static readonly BindableProperty MaximumProperty = BindableProperty.Create(
        nameof(Maximum), typeof(double), typeof(ProgressBar), 100.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ProgressBar), () =>
            {
                ((ProgressBar)b).UpdateVisuals();
            }));
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    // Appearance
    public static readonly BindableProperty TrackColorProperty = BindableProperty.Create(
        nameof(TrackColor), typeof(Color), typeof(ProgressBar), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ProgressBar), () =>
            {
            var pb = (ProgressBar)b;
            if (n is Color c)
                pb.trackBackground.BackgroundColor = c;
            else
                pb.trackBackground.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHighest);
            pb.UpdateSegmentsLayout();
        }));
    /// <summary>Track background color. When null, the theme SurfaceContainerHighest token is used.</summary>
    public Color? TrackColor { get => (Color?)GetValue(TrackColorProperty); set => SetValue(TrackColorProperty, value); }

    public static readonly BindableProperty BarColorProperty = BindableProperty.Create(
        nameof(BarColor), typeof(Color), typeof(ProgressBar), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ProgressBar), () =>
            {
                ((ProgressBar)b).UpdateVisuals();
            }));
    /// <summary>Progress fill color. When null, the theme Primary token is used.</summary>
    public Color? BarColor { get => (Color?)GetValue(BarColorProperty); set => SetValue(BarColorProperty, value); }

    public static readonly BindableProperty TrackHeightProperty = BindableProperty.Create(
        nameof(TrackHeight), typeof(double), typeof(ProgressBar), 8.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ProgressBar), () =>
            {
                ((ProgressBar)b).UpdateVisuals();
            }));
    public double TrackHeight { get => (double)GetValue(TrackHeightProperty); set => SetValue(TrackHeightProperty, value); }

    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(
        nameof(CornerRadius), typeof(double), typeof(ProgressBar), 4.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ProgressBar), () =>
            {
                ((ProgressBar)b).UpdateVisuals();
            }));
    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    // Gradient
    public static readonly BindableProperty UseGradientProperty = BindableProperty.Create(
        nameof(UseGradient), typeof(bool), typeof(ProgressBar), false,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ProgressBar), () =>
            {
                ((ProgressBar)b).UpdateVisuals();
            }));
    public bool UseGradient { get => (bool)GetValue(UseGradientProperty); set => SetValue(UseGradientProperty, value); }

    public static readonly BindableProperty GradientStartColorProperty = BindableProperty.Create(
        nameof(GradientStartColor), typeof(Color), typeof(ProgressBar), Color.FromArgb("#3B82F6"),
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ProgressBar), () =>
            {
                ((ProgressBar)b).UpdateVisuals();
            }));
    public Color GradientStartColor { get => (Color)GetValue(GradientStartColorProperty); set => SetValue(GradientStartColorProperty, value); }

    public static readonly BindableProperty GradientEndColorProperty = BindableProperty.Create(
        nameof(GradientEndColor), typeof(Color), typeof(ProgressBar), Color.FromArgb("#8B5CF6"),
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ProgressBar), () =>
            {
                ((ProgressBar)b).UpdateVisuals();
            }));
    public Color GradientEndColor { get => (Color)GetValue(GradientEndColorProperty); set => SetValue(GradientEndColorProperty, value); }

    // Pulse (Vista-style shimmer sweep)
    public static readonly BindableProperty PulseEnabledProperty = BindableProperty.Create(
        nameof(PulseEnabled), typeof(bool), typeof(ProgressBar), false,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ProgressBar), () =>
            {
                ((ProgressBar)b).OnPulseEnabledChanged((bool)n);
            }));
    public bool PulseEnabled { get => (bool)GetValue(PulseEnabledProperty); set => SetValue(PulseEnabledProperty, value); }

    public static readonly BindableProperty PulseOnValueChangeProperty = BindableProperty.Create(
        nameof(PulseOnValueChange), typeof(bool), typeof(ProgressBar), true);
    public bool PulseOnValueChange { get => (bool)GetValue(PulseOnValueChangeProperty); set => SetValue(PulseOnValueChangeProperty, value); }

    public static readonly BindableProperty PulseIntervalProperty = BindableProperty.Create(
        nameof(PulseInterval), typeof(TimeSpan), typeof(ProgressBar), TimeSpan.Zero,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ProgressBar), () =>
            {
                ((ProgressBar)b).ConfigurePulseTimer();
            }));
    public TimeSpan PulseInterval { get => (TimeSpan)GetValue(PulseIntervalProperty); set => SetValue(PulseIntervalProperty, value); }

    public static readonly BindableProperty PulseColorProperty = BindableProperty.Create(
        nameof(PulseColor), typeof(Color), typeof(ProgressBar), Colors.White);
    public Color PulseColor { get => (Color)GetValue(PulseColorProperty); set => SetValue(PulseColorProperty, value); }

    public static readonly BindableProperty PulseOpacityProperty = BindableProperty.Create(
        nameof(PulseOpacity), typeof(double), typeof(ProgressBar), 0.4);
    public double PulseOpacity { get => (double)GetValue(PulseOpacityProperty); set => SetValue(PulseOpacityProperty, value); }

    public static readonly BindableProperty PulseLengthProperty = BindableProperty.Create(
        nameof(PulseLength), typeof(double), typeof(ProgressBar), 0.4,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ProgressBar), () =>
            {
                ((ProgressBar)b).UpdatePulseOverlaySize();
            }));
    public double PulseLength { get => (double)GetValue(PulseLengthProperty); set => SetValue(PulseLengthProperty, value); }

    public static readonly BindableProperty PulseSpeedProperty = BindableProperty.Create(
        nameof(PulseSpeed), typeof(int), typeof(ProgressBar), 800);
    public int PulseSpeed { get => (int)GetValue(PulseSpeedProperty); set => SetValue(PulseSpeedProperty, value); }

    // Fill animation
    public static readonly BindableProperty AnimateProgressProperty = BindableProperty.Create(
        nameof(AnimateProgress), typeof(bool), typeof(ProgressBar), true);
    /// <summary>
    /// Whether a change to <see cref="Value"/> slides the fill to its new width instead of snapping.
    /// Applies in both directions, so a value that drops drains rather than jumping backwards.
    /// </summary>
    /// <remarks>
    /// Only a value change animates. A width change driven by layout - the control being measured,
    /// <see cref="TrackHeight"/> changing, leaving indeterminate mode - snaps, because animating
    /// those makes the bar visibly reflow every time its container resizes.
    /// </remarks>
    public bool AnimateProgress { get => (bool)GetValue(AnimateProgressProperty); set => SetValue(AnimateProgressProperty, value); }

    public static readonly BindableProperty ProgressAnimationDurationProperty = BindableProperty.Create(
        nameof(ProgressAnimationDuration), typeof(int), typeof(ProgressBar), 250);
    /// <summary>Length of the fill slide in milliseconds. Zero or less snaps.</summary>
    public int ProgressAnimationDuration { get => (int)GetValue(ProgressAnimationDurationProperty); set => SetValue(ProgressAnimationDurationProperty, value); }

    public static readonly BindableProperty ProgressAnimationEasingProperty = BindableProperty.Create(
        nameof(ProgressAnimationEasing), typeof(Easing), typeof(ProgressBar), Easing.CubicOut);
    /// <summary>Curve the fill slide follows. Defaults to <see cref="Easing.CubicOut"/>.</summary>
    public Easing ProgressAnimationEasing { get => (Easing)GetValue(ProgressAnimationEasingProperty); set => SetValue(ProgressAnimationEasingProperty, value); }

    // Text
    public static readonly BindableProperty ShowTextProperty = BindableProperty.Create(
        nameof(ShowText), typeof(bool), typeof(ProgressBar), false,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ProgressBar), () =>
            {
                ((ProgressBar)b).UpdateVisuals();
            }));
    public bool ShowText { get => (bool)GetValue(ShowTextProperty); set => SetValue(ShowTextProperty, value); }

    public static readonly BindableProperty TextFormatProperty = BindableProperty.Create(
        nameof(TextFormat), typeof(string), typeof(ProgressBar), "{0:0}%",
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ProgressBar), () =>
            {
                ((ProgressBar)b).UpdateVisuals();
            }));
    public string TextFormat { get => (string)GetValue(TextFormatProperty); set => SetValue(TextFormatProperty, value); }

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor), typeof(Color), typeof(ProgressBar), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ProgressBar), () =>
            {
            var pb = (ProgressBar)b;
            if (n is Color c)
                pb.progressLabel.TextColor = c;
            else
                pb.progressLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);
        }));
    /// <summary>Progress text color. When null, the theme OnPrimary token is used.</summary>
    public Color? TextColor { get => (Color?)GetValue(TextColorProperty); set => SetValue(TextColorProperty, value); }

    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize), typeof(double), typeof(ProgressBar), 11.0,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ProgressBar), () =>
            {
                ((ProgressBar)b).progressLabel.FontSize = (double)n;
            }));
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

    // Segments
    public static readonly BindableProperty SegmentsProperty = BindableProperty.Create(
        nameof(Segments), typeof(int), typeof(ProgressBar), 0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ProgressBar), () =>
            {
                ((ProgressBar)b).OnSegmentsChanged();
            }));
    /// <summary>
    /// Splits the bar into this many separate steps with a gap between them. <c>0</c> or <c>1</c> draws
    /// the continuous bar. The fill runs across the steps in order, so a value part-way through a step
    /// lights that step partially - set <see cref="Maximum"/> to the segment count for whole steps.
    /// </summary>
    /// <remarks>
    /// Indeterminate mode and the pulse sheen always use the continuous bar. A gradient is spread
    /// across the steps, each step taking the colour at its centre.
    /// </remarks>
    public int Segments { get => (int)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }

    public static readonly BindableProperty SegmentSpacingProperty = BindableProperty.Create(
        nameof(SegmentSpacing), typeof(double), typeof(ProgressBar), 4.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(ProgressBar), () =>
            {
                ((ProgressBar)b).UpdateVisuals();
            }));
    /// <summary>Gap between segments.</summary>
    public double SegmentSpacing { get => (double)GetValue(SegmentSpacingProperty); set => SetValue(SegmentSpacingProperty, value); }

    // Indeterminate
    public static readonly BindableProperty IsIndeterminateProperty = BindableProperty.Create(
        nameof(IsIndeterminate), typeof(bool), typeof(ProgressBar), false,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ProgressBar), () =>
            {
                ((ProgressBar)b).OnIndeterminateChanged((bool)n);
            }));
    public bool IsIndeterminate { get => (bool)GetValue(IsIndeterminateProperty); set => SetValue(IsIndeterminateProperty, value); }

    // Commands/Events
    public static readonly BindableProperty ValueChangedCommandProperty = BindableProperty.Create(
        nameof(ValueChangedCommand), typeof(ICommand), typeof(ProgressBar));
    public ICommand? ValueChangedCommand { get => (ICommand?)GetValue(ValueChangedCommandProperty); set => SetValue(ValueChangedCommandProperty, value); }

    public event EventHandler<double>? ValueChangedEvent;
}
