using System.ComponentModel;
using System.Globalization;

namespace Shiny.Maui.Controls.Keyframe;

/// <summary>
/// A XAML-authored keyframe animation — the direct analogue of a CSS <c>@keyframes</c> rule
/// together with the <c>animation-*</c> properties that drive it.
/// </summary>
/// <remarks>
/// Two ways to write the same animation: per property, as <see cref="Track"/>s of <see cref="Key"/>s
/// (the CSS shape), or per instant, as <see cref="KeyFrame"/>s holding ordinary MAUI
/// <see cref="Setter"/>s (the WinUI/storyboard shape, which reaches any bindable property). Both can
/// sit in the same <c>Keyframes</c>.
/// </remarks>
[ContentProperty(nameof(Content))]
public sealed class Keyframes : BindableObject, IAnimationDefinition
{
    /// <summary>Creates an empty animation.</summary>
    public Keyframes() => Content = new KeyframesContent(this);

    /// <summary>Backing store for <see cref="Duration"/>.</summary>
    public static readonly BindableProperty DurationProperty =
        BindableProperty.Create(nameof(Duration), typeof(TimeSpan), typeof(Keyframes),
            TimeSpan.FromMilliseconds(300));

    /// <summary>Backing store for <see cref="Delay"/>.</summary>
    public static readonly BindableProperty DelayProperty =
        BindableProperty.Create(nameof(Delay), typeof(TimeSpan), typeof(Keyframes), TimeSpan.Zero);

    /// <summary>Backing store for <see cref="EndDelay"/>.</summary>
    public static readonly BindableProperty EndDelayProperty =
        BindableProperty.Create(nameof(EndDelay), typeof(TimeSpan), typeof(Keyframes), TimeSpan.Zero);

    /// <summary>Backing store for <see cref="IterationDelay"/>.</summary>
    public static readonly BindableProperty IterationDelayProperty =
        BindableProperty.Create(nameof(IterationDelay), typeof(TimeSpan), typeof(Keyframes), TimeSpan.Zero);

    /// <summary>Backing store for <see cref="ReducedMotion"/>.</summary>
    public static readonly BindableProperty ReducedMotionProperty =
        BindableProperty.Create(nameof(ReducedMotion), typeof(ReducedMotionBehavior), typeof(Keyframes), ReducedMotionBehavior.Auto);

    /// <summary>Backing store for <see cref="TargetName"/>.</summary>
    public static readonly BindableProperty TargetNameProperty =
        BindableProperty.Create(nameof(TargetName), typeof(string), typeof(Keyframes));

    /// <summary>Backing store for <see cref="Iterations"/>.</summary>
    public static readonly BindableProperty IterationsProperty =
        BindableProperty.Create(nameof(Iterations), typeof(double), typeof(Keyframes), 1d);

    /// <summary>Backing store for <see cref="Direction"/>.</summary>
    public static readonly BindableProperty DirectionProperty =
        BindableProperty.Create(nameof(Direction), typeof(PlaybackDirection), typeof(Keyframes),
            PlaybackDirection.Normal);

    /// <summary>Backing store for <see cref="Fill"/>.</summary>
    public static readonly BindableProperty FillProperty =
        BindableProperty.Create(nameof(Fill), typeof(FillMode), typeof(Keyframes), FillMode.None);

    /// <summary>Backing store for <see cref="Easing"/>.</summary>
    public static readonly BindableProperty EasingProperty =
        BindableProperty.Create(nameof(Easing), typeof(EasingFunction), typeof(Keyframes));

    /// <summary>Backing store for <see cref="AutoPlay"/>.</summary>
    public static readonly BindableProperty AutoPlayProperty =
        BindableProperty.Create(nameof(AutoPlay), typeof(bool), typeof(Keyframes), true);

    /// <summary>Backing store for <see cref="Speed"/>.</summary>
    public static readonly BindableProperty SpeedProperty =
        BindableProperty.Create(nameof(Speed), typeof(double), typeof(Keyframes), 1d);

    /// <summary>Length of a single iteration.</summary>
    public TimeSpan Duration
    {
        get => (TimeSpan)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    /// <summary>Delay before the first iteration. Negative values start partway through.</summary>
    public TimeSpan Delay
    {
        get => (TimeSpan)GetValue(DelayProperty);
        set => SetValue(DelayProperty, value);
    }

    /// <summary>Pause after the final iteration before the animation reports finished.</summary>
    public TimeSpan EndDelay
    {
        get => (TimeSpan)GetValue(EndDelayProperty);
        set => SetValue(EndDelayProperty, value);
    }

    /// <summary>Pause held between iterations, on the end pose of the one that just finished.</summary>
    public TimeSpan IterationDelay
    {
        get => (TimeSpan)GetValue(IterationDelayProperty);
        set => SetValue(IterationDelayProperty, value);
    }

    /// <summary>
    /// What happens under the OS reduced-motion setting. <see cref="ReducedMotionBehavior.Auto"/>
    /// (the default) jumps straight to the end state; <see cref="ReducedMotionBehavior.Play"/> animates
    /// anyway, for motion that carries information.
    /// </summary>
    public ReducedMotionBehavior ReducedMotion
    {
        get => (ReducedMotionBehavior)GetValue(ReducedMotionProperty);
        set => SetValue(ReducedMotionProperty, value);
    }

    /// <summary>
    /// Optional <c>x:Name</c> of the element every track drives by default, instead of the element
    /// the animation is attached to. A track's own <see cref="Track.TargetName"/> still wins.
    /// </summary>
    public string? TargetName
    {
        get => (string?)GetValue(TargetNameProperty);
        set => SetValue(TargetNameProperty, value);
    }

    /// <summary>
    /// How many times to repeat. Accepts <c>Infinite</c> in XAML as well as a number.
    /// </summary>
    [TypeConverter(typeof(IterationsTypeConverter))]
    public double Iterations
    {
        get => (double)GetValue(IterationsProperty);
        set => SetValue(IterationsProperty, value);
    }

    /// <summary>Which way each iteration runs.</summary>
    public PlaybackDirection Direction
    {
        get => (PlaybackDirection)GetValue(DirectionProperty);
        set => SetValue(DirectionProperty, value);
    }

    /// <summary>What happens to the target outside the active window.</summary>
    public FillMode Fill
    {
        get => (FillMode)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Easing applied across each whole iteration, on top of the per-segment curves.</summary>
    [TypeConverter(typeof(EasingFunctionTypeConverter))]
    public EasingFunction? Easing
    {
        get => (EasingFunction?)GetValue(EasingProperty);
        set => SetValue(EasingProperty, value);
    }

    /// <summary>Whether the animation starts as soon as the element is loaded.</summary>
    public bool AutoPlay
    {
        get => (bool)GetValue(AutoPlayProperty);
        set => SetValue(AutoPlayProperty, value);
    }

    /// <summary>Playback rate. 1 is real time; negative values run backwards.</summary>
    public double Speed
    {
        get => (double)GetValue(SpeedProperty);
        set => SetValue(SpeedProperty, value);
    }

    /// <summary>The animated properties, in the per-property shape.</summary>
    public IList<Track> Tracks { get; } = [];

    /// <summary>The animation in the per-instant shape: frames of setters at cue points.</summary>
    public IList<KeyFrame> Frames { get; } = [];

    /// <summary>
    /// The XAML content: <see cref="Track"/> and <see cref="KeyFrame"/> children, routed to
    /// <see cref="Tracks"/> and <see cref="Frames"/>.
    /// </summary>
    public IList<BindableObject> Content { get; }

    /// <summary>
    /// Builds a runnable timeline bound to a view. Called by the attached property when the
    /// element loads; also usable directly if you want to own playback yourself.
    /// </summary>
    /// <param name="target">The element the tracks drive by default.</param>
    /// <param name="resolveTarget">Resolves a <see cref="Track.TargetName"/> to an element.</param>
    public Timeline Build(VisualElement target, Func<string, VisualElement?>? resolveTarget = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (Tracks.Count == 0 && Frames.Count == 0)
            throw new InvalidOperationException(
                "A Keyframes animation needs at least one Track or KeyFrame.");

        var timing = new Timing
        {
            Duration = Duration,
            Delay = Delay,
            EndDelay = EndDelay,
            IterationDelay = IterationDelay,
            Iterations = Iterations,
            Direction = Direction,
            Fill = Fill,
            Easing = Easing ?? Easings.Linear
        };

        var timeline = new Timeline(timing);
        var defaultTarget = string.IsNullOrWhiteSpace(TargetName) ? target : Resolve(TargetName, resolveTarget);

        foreach (var track in Tracks)
        {
            var element = string.IsNullOrWhiteSpace(track.TargetName)
                ? defaultTarget
                : Resolve(track.TargetName, resolveTarget);

            timeline.Add(track.Build(element));
        }

        if (Frames.Count > 0)
            timeline.AddRange(KeyFrame.BuildTracks(Frames, defaultTarget, name => Resolve(name, resolveTarget)));

        return timeline;
    }

    IAnimationNode IAnimationDefinition.Build(VisualElement target, Func<string, VisualElement?>? resolveTarget)
        => Build(target, resolveTarget);

    static VisualElement Resolve(string name, Func<string, VisualElement?>? resolveTarget)
        => resolveTarget?.Invoke(name)
            ?? throw new InvalidOperationException(
                $"Could not find an element named '{name}'. Check the x:Name, " +
                "and note that the name must be in scope from the element the animation is attached to.");

    sealed class KeyframesContent(Keyframes owner) : System.Collections.ObjectModel.Collection<BindableObject>
    {
        protected override void InsertItem(int index, BindableObject item)
        {
            switch (item)
            {
                case Track track:
                    owner.Tracks.Add(track);
                    break;

                case KeyFrame frame:
                    owner.Frames.Add(frame);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"A Keyframes animation can contain Track and KeyFrame elements, not {item?.GetType().Name ?? "null"}.");
            }

            base.InsertItem(index, item);
        }
    }
}

/// <summary>Lets XAML write <c>Iterations="Infinite"</c> as well as a plain number.</summary>
public sealed class IterationsTypeConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string);

    /// <inheritdoc />
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is not string text)
            return base.ConvertFrom(context, culture, value);

        text = text.Trim();

        if (text.Equals("Infinite", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Forever", StringComparison.OrdinalIgnoreCase))
            return double.PositiveInfinity;

        return double.Parse(text, CultureInfo.InvariantCulture);
    }
}
