using global::Shiny.Controls.Keyframe;

namespace Shiny.Blazor.Controls.Keyframe.Compilation;

/// <summary>One keyframe of a track, as authored: a null <see cref="Value"/> means "the live value when playback starts".</summary>
internal sealed record KeySpec(double Offset, string? Value, string? Easing);

/// <summary>One animated property, as authored: a MAUI property name (or a raw CSS property) and its keys.</summary>
internal sealed record TrackSpec(string Property, IReadOnlyList<KeySpec> Keys);

/// <summary>The timing of one Keyframes animation, as authored. Mirrors <see cref="Timing"/>, with the easing kept as text.</summary>
internal sealed record TimingSpec
{
    public TimeSpan Duration { get; init; } = TimeSpan.FromMilliseconds(300);
    public TimeSpan Delay { get; init; }
    public TimeSpan EndDelay { get; init; }
    public TimeSpan IterationDelay { get; init; }
    public double Iterations { get; init; } = 1d;
    public double IterationStart { get; init; }
    public PlaybackDirection Direction { get; init; } = PlaybackDirection.Normal;
    public FillMode Fill { get; init; } = FillMode.None;
    public string? Easing { get; init; }

    /// <summary>The shared engine's view of the same timing, which owns the duration arithmetic.</summary>
    public Timing ToTiming() => new()
    {
        Duration = this.Duration <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : this.Duration,
        Delay = this.Delay,
        EndDelay = this.EndDelay,
        IterationDelay = this.IterationDelay < TimeSpan.Zero ? TimeSpan.Zero : this.IterationDelay,
        Iterations = double.IsNaN(this.Iterations) || this.Iterations < 0 ? 1d : this.Iterations,
        IterationStart = double.IsNaN(this.IterationStart) || this.IterationStart < 0 ? 0d : this.IterationStart,
        Direction = this.Direction,
        Fill = this.Fill
    };
}

/// <summary>A Keyframes animation as authored: its timing and its tracks.</summary>
internal sealed record AnimationSpec(TimingSpec Timing, IReadOnlyList<TrackSpec> Tracks);


/// <summary>One WAAPI keyframe: an offset, the easing of the segment that starts here, and the CSS values (kebab-case names).</summary>
internal sealed class CompiledFrame
{
    public CompiledFrame(double offset, string easingCss, EasingFunction easing, IReadOnlyDictionary<string, string> values)
    {
        this.Offset = offset;
        this.EasingCss = easingCss;
        this.Easing = easing;
        this.Values = values;
    }

    public double Offset { get; set; }
    public string EasingCss { get; set; }

    /// <summary>The managed curve behind <see cref="EasingCss"/>, kept so the timing easing can be folded into it.</summary>
    public EasingFunction Easing { get; set; }

    public IReadOnlyDictionary<string, string> Values { get; }

    public CompiledFrame CopyAt(double offset, string easingCss, EasingFunction easing) => new(offset, easingCss, easing, this.Values);
}

/// <summary>One <c>element.animate()</c> call: the keyframes for one CSS property (or one merged transform part).</summary>
internal sealed class CompiledEffect
{
    public CompiledEffect(string cssProperty, List<CompiledFrame> frames)
    {
        this.CssProperty = cssProperty;
        this.Frames = frames;
    }

    public string CssProperty { get; }
    public List<CompiledFrame> Frames { get; }
}

/// <summary>The WAAPI <c>KeyframeEffectOptions</c> for every effect of one animation.</summary>
internal sealed record CompiledTiming(
    double DurationMs,
    double DelayMs,
    double EndDelayMs,
    double Iterations,
    double IterationStart,
    string Direction,
    string Fill,
    string Easing)
{
    public CompiledTiming WithDelayOffset(double offsetMs) => this with { DelayMs = this.DelayMs + offsetMs };
}

/// <summary>A compiled Keyframes animation: the effects to start on one element, their shared timing and how long it all takes.</summary>
internal sealed class CompiledAnimation
{
    public CompiledAnimation(List<CompiledEffect> effects, CompiledTiming timing, double totalMs, double firstPassMs)
    {
        this.Effects = effects;
        this.Timing = timing;
        this.TotalMs = totalMs;
        this.FirstPassMs = firstPassMs;
    }

    public List<CompiledEffect> Effects { get; }
    public CompiledTiming Timing { get; }

    /// <summary>From start to finished, including delays. <see cref="double.PositiveInfinity"/> for a loop.</summary>
    public double TotalMs { get; }

    /// <summary>The start delay plus one pass: what a scrubber spans when the animation loops forever.</summary>
    public double FirstPassMs { get; }
}
