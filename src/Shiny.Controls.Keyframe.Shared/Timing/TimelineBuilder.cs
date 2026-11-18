namespace Shiny.Controls.Keyframe;

/// <summary>
/// Fluent construction of a <see cref="Timeline"/>.
/// </summary>
/// <remarks>
/// Property access is expressed as explicit setter and getter lambdas rather than an
/// <c>Expression&lt;Func&lt;&gt;&gt;</c>. Compiling an expression tree at runtime is exactly the kind
/// of thing that breaks under Native AOT, and the cost here is one extra lambda at the call site.
/// </remarks>
public sealed class TimelineBuilder
{
    readonly Timing timing = new();
    readonly List<ITrack> tracks = [];
    string? name;

    /// <summary>Starts a builder.</summary>
    /// <param name="duration">Length of a single iteration.</param>
    public static TimelineBuilder Create(TimeSpan duration) => new TimelineBuilder().Duration(duration);

    /// <summary>Starts a builder with the duration given in milliseconds.</summary>
    public static TimelineBuilder Create(double milliseconds)
        => Create(TimeSpan.FromMilliseconds(milliseconds));

    /// <summary>Labels the timeline for diagnostics.</summary>
    public TimelineBuilder Named(string value)
    {
        name = value;
        return this;
    }

    /// <summary>Sets the length of a single iteration.</summary>
    public TimelineBuilder Duration(TimeSpan value)
    {
        timing.Duration = value;
        return this;
    }

    /// <summary>Delays the start. Negative values begin the animation partway through.</summary>
    public TimelineBuilder Delay(TimeSpan value)
    {
        timing.Delay = value;
        return this;
    }

    /// <summary>Pads the end, delaying when the timeline reports as finished.</summary>
    public TimelineBuilder EndDelay(TimeSpan value)
    {
        timing.EndDelay = value;
        return this;
    }

    /// <summary>Sets a pause held between consecutive iterations.</summary>
    public TimelineBuilder IterationDelay(TimeSpan value)
    {
        timing.IterationDelay = value;
        return this;
    }

    /// <summary>Repeats a set number of times. Fractional counts truncate the final pass.</summary>
    public TimelineBuilder Repeat(double count)
    {
        timing.Iterations = count;
        return this;
    }

    /// <summary>Repeats indefinitely.</summary>
    public TimelineBuilder RepeatForever()
    {
        timing.Iterations = double.PositiveInfinity;
        return this;
    }

    /// <summary>Sets which way each iteration runs.</summary>
    public TimelineBuilder Direction(PlaybackDirection value)
    {
        timing.Direction = value;
        return this;
    }

    /// <summary>Shorthand for <see cref="PlaybackDirection.Alternate"/> — ping-pong playback.</summary>
    public TimelineBuilder PingPong() => Direction(PlaybackDirection.Alternate);

    /// <summary>Sets what happens to targets outside the active window.</summary>
    public TimelineBuilder Fill(FillMode value)
    {
        timing.Fill = value;
        return this;
    }

    /// <summary>Holds the final value once the animation finishes.</summary>
    public TimelineBuilder HoldEnd() => Fill(FillMode.Forwards);

    /// <summary>Applies an easing curve across each whole iteration.</summary>
    public TimelineBuilder Easing(EasingFunction value)
    {
        timing.Easing = value;
        return this;
    }

    /// <summary>Offsets into the first iteration, measured in iterations.</summary>
    public TimelineBuilder StartAtIteration(double value)
    {
        timing.IterationStart = value;
        return this;
    }

    /// <summary>Adds a track for any value type, given an explicit interpolator.</summary>
    /// <param name="target">The object to animate. Held weakly by the resulting track.</param>
    /// <param name="setter">Writes the value to the target.</param>
    /// <param name="interpolator">Blends between keyframe values.</param>
    /// <param name="keys">Builds the keyframe list.</param>
    /// <param name="getter">Reads the target's current value; required for implicit keyframes.</param>
    /// <param name="trackName">Optional label for diagnostics.</param>
    public TimelineBuilder Animate<TTarget, TValue>(
        TTarget target,
        Action<TTarget, TValue> setter,
        IInterpolator<TValue> interpolator,
        Action<TrackBuilder<TValue>> keys,
        Func<TTarget, TValue>? getter = null,
        string? trackName = null)
        where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(keys);

        var builder = new TrackBuilder<TValue>();
        keys(builder);

        tracks.Add(new Track<TTarget, TValue>(target, setter, builder.Keys, interpolator, getter, trackName));
        return this;
    }

    /// <summary>Adds a track for a <see cref="double"/> property — the common case.</summary>
    public TimelineBuilder Animate<TTarget>(
        TTarget target,
        Action<TTarget, double> setter,
        Action<TrackBuilder<double>> keys,
        Func<TTarget, double>? getter = null,
        string? trackName = null)
        where TTarget : class
        => Animate(target, setter, DoubleInterpolator.Instance, keys, getter, trackName);

    /// <summary>
    /// Adds a track for an angle in degrees, taking the shortest arc between values so a wrap from
    /// 350 to 10 turns forward 20 degrees rather than back 340.
    /// </summary>
    public TimelineBuilder AnimateAngle<TTarget>(
        TTarget target,
        Action<TTarget, double> setter,
        Action<TrackBuilder<double>> keys,
        Func<TTarget, double>? getter = null,
        string? trackName = null)
        where TTarget : class
        => Animate(target, setter, AngleInterpolator.Degrees, keys, getter, trackName);

    /// <summary>Adds an already-constructed track.</summary>
    public TimelineBuilder Add(ITrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        tracks.Add(track);
        return this;
    }

    /// <summary>Produces the timeline.</summary>
    public Timeline Build()
    {
        var timeline = new Timeline(timing.Clone()) { Name = name };
        return timeline.AddRange(tracks);
    }
}
