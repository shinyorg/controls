namespace Shiny.Controls.Keyframe;

/// <summary>
/// The result of resolving a point in time against a <see cref="Timing"/> configuration.
/// </summary>
/// <param name="ShouldApply">Whether the timeline should write to its targets at all. False means
/// the timeline is outside its active window and fill mode says to leave targets untouched.</param>
/// <param name="Progress">Direction-adjusted, eased progress through the current iteration, 0..1.
/// Only meaningful when <paramref name="ShouldApply"/> is true.</param>
/// <param name="Iteration">Zero-based index of the iteration this sample falls in.</param>
/// <param name="IsFinished">Whether the timeline has run past its active duration.</param>
public readonly record struct TimelineSample(
    bool ShouldApply,
    double Progress,
    int Iteration,
    bool IsFinished)
{
    /// <summary>A sample that writes nothing.</summary>
    public static readonly TimelineSample Inactive = new(false, 0d, 0, false);
}

/// <summary>
/// The timing configuration of a timeline: how long, how many times, which way, and what happens
/// at the edges. Separated from <see cref="Timeline"/> so the arithmetic can be tested in isolation
/// and reused by the export pipeline without dragging tracks along.
/// </summary>
public sealed class Timing
{
    TimeSpan duration = TimeSpan.FromMilliseconds(300);
    double iterations = 1d;
    double iterationStart;
    TimeSpan iterationDelay;

    /// <summary>How long a single iteration lasts. Must be positive.</summary>
    public TimeSpan Duration
    {
        get => duration;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            duration = value;
        }
    }

    /// <summary>Delay before the first iteration begins. Negative values seek into the timeline,
    /// starting it partway through — the same trick CSS uses for staggered entrances.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>Delay applied after the final iteration before the timeline reports as finished.</summary>
    public TimeSpan EndDelay { get; set; }

    /// <summary>
    /// Pause between consecutive iterations, during which the end pose of the iteration that just
    /// finished is held. Not applied before the first iteration (that is <see cref="Delay"/>) or
    /// after the last (that is <see cref="EndDelay"/>).
    /// </summary>
    public TimeSpan IterationDelay
    {
        get => iterationDelay;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            iterationDelay = value;
        }
    }

    /// <summary>
    /// How many times to repeat. Fractional values are allowed and truncate the final pass;
    /// <see cref="double.PositiveInfinity"/> loops forever.
    /// </summary>
    public double Iterations
    {
        get => iterations;
        set
        {
            if (double.IsNaN(value) || value < 0d)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Iterations must be zero or greater.");
            iterations = value;
        }
    }

    /// <summary>
    /// Offset into the first iteration, in iterations. 0.5 starts halfway through the first pass
    /// and, under <see cref="PlaybackDirection.Alternate"/>, also shifts which passes run backwards.
    /// </summary>
    public double IterationStart
    {
        get => iterationStart;
        set
        {
            if (double.IsNaN(value) || value < 0d)
                throw new ArgumentOutOfRangeException(nameof(value), value, "IterationStart must be zero or greater.");
            iterationStart = value;
        }
    }

    /// <summary>Which way each iteration runs.</summary>
    public PlaybackDirection Direction { get; set; } = PlaybackDirection.Normal;

    /// <summary>What happens to targets outside the active window.</summary>
    public FillMode Fill { get; set; } = FillMode.None;

    /// <summary>
    /// Easing applied across a whole iteration, on top of any per-segment easing on the keyframes.
    /// Defaults to linear so per-segment curves are the only thing shaping motion unless asked otherwise.
    /// </summary>
    public EasingFunction Easing { get; set; } = Easings.Linear;

    /// <summary>Total time from the start of the first iteration to the end of the last,
    /// excluding <see cref="Delay"/> and <see cref="EndDelay"/>.</summary>
    public TimeSpan ActiveDuration => double.IsPositiveInfinity(Iterations)
        ? TimeSpan.MaxValue
        : Duration * Iterations + IterationDelay * Math.Max(0d, Math.Ceiling(Iterations) - 1d);

    /// <summary>
    /// Total wall-clock time from being started to reporting finished. Infinite for looping timelines.
    /// </summary>
    public TimeSpan TotalDuration => double.IsPositiveInfinity(Iterations)
        ? TimeSpan.MaxValue
        : Delay + ActiveDuration + EndDelay;

    /// <summary>
    /// Resolves a wall-clock offset (measured from the moment the timeline started, before
    /// <see cref="Delay"/>) into eased, direction-adjusted progress.
    /// </summary>
    /// <param name="time">Elapsed time since the timeline started.</param>
    public TimelineSample Sample(TimeSpan time)
    {
        var active = time - Delay;
        var activeDuration = ActiveDuration;
        var infinite = double.IsPositiveInfinity(Iterations);

        // A zero-iteration timeline has no active window at all; it can only ever fill.
        if (Iterations == 0d)
        {
            var fillsAtAll = Fill != FillMode.None;
            return fillsAtAll
                ? new TimelineSample(true, ApplyDirection(0d, 0), 0, active >= TimeSpan.Zero)
                : TimelineSample.Inactive with { IsFinished = active >= TimeSpan.Zero };
        }

        // --- Before the active window (still in the start delay) -------------------------
        if (active < TimeSpan.Zero)
        {
            if (!Fill.HasFlag(FillMode.Backwards))
                return TimelineSample.Inactive;

            // Hold the value the timeline will have at its very first instant, which under
            // Reverse/AlternateReverse is the *end* of the keyframe list, not the start.
            var (startProgress, startIteration) = ResolveIterationPosition(TimeSpan.Zero, activeDuration, infinite);
            return new TimelineSample(true, ApplyDirection(startProgress, startIteration), startIteration, false);
        }

        // --- After the active window -----------------------------------------------------
        if (!infinite && active >= activeDuration)
        {
            var finished = time >= TotalDuration;

            if (!Fill.HasFlag(FillMode.Forwards))
                return TimelineSample.Inactive with { IsFinished = finished };

            var (endProgress, endIteration) = ResolveIterationPosition(activeDuration, activeDuration, infinite);
            return new TimelineSample(true, ApplyDirection(endProgress, endIteration), endIteration, finished);
        }

        // --- Inside the active window ----------------------------------------------------
        var (progress, iteration) = ResolveIterationPosition(active, activeDuration, infinite);
        return new TimelineSample(true, ApplyDirection(progress, iteration), iteration, false);
    }

    /// <summary>
    /// Splits an offset within the active window into (progress through the current iteration,
    /// iteration index), before direction or easing is applied.
    /// </summary>
    (double Progress, int Iteration) ResolveIterationPosition(TimeSpan active, TimeSpan activeDuration, bool infinite)
    {
        // Iteration delays are wall-clock gaps between passes. Fold them out first, so what is left
        // is time spent actually inside an iteration; a sample inside a gap holds the end of the
        // pass that just finished.
        var inGap = false;
        var iterationTime = active;

        if (IterationDelay > TimeSpan.Zero && active > TimeSpan.Zero)
        {
            var cycle = Duration + IterationDelay;
            var cycles = Math.Floor(active / cycle);
            var within = active - cycle * cycles;

            if (within > Duration)
            {
                inGap = true;
                within = Duration;
            }

            iterationTime = Duration * cycles + within;
        }

        var overallProgress = iterationTime / Duration + IterationStart;

        // Landing exactly on an iteration boundary is ambiguous: it is simultaneously the end of
        // one pass and the start of the next. The end of the *final* pass must read as progress 1
        // of the last iteration, otherwise a Forwards fill would snap back to the starting value.
        var atActiveEnd = inGap || (!infinite && active >= activeDuration);

        if (atActiveEnd && overallProgress > 0d)
        {
            var lastIteration = (int)Math.Min(Math.Ceiling(overallProgress) - 1d, int.MaxValue);
            var tail = overallProgress - lastIteration;
            return (Math.Clamp(tail, 0d, 1d), Math.Max(0, lastIteration));
        }

        var index = (int)Math.Min(Math.Floor(overallProgress), int.MaxValue);
        return (overallProgress - index, Math.Max(0, index));
    }

    /// <summary>Applies playback direction, then the iteration-level easing curve.</summary>
    double ApplyDirection(double progress, int iteration)
    {
        var reversed = Direction switch
        {
            PlaybackDirection.Normal => false,
            PlaybackDirection.Reverse => true,
            PlaybackDirection.Alternate => (iteration & 1) == 1,
            PlaybackDirection.AlternateReverse => (iteration & 1) == 0,
            _ => false
        };

        var directed = reversed ? 1d - progress : progress;

        // Easing is applied after direction so an ease-out curve decelerates into whichever end
        // the iteration is actually travelling toward.
        return (Easing ?? Easings.Linear)(directed);
    }

    /// <summary>Creates an independent copy. Easing delegates are shared, which is safe because
    /// they are required to be pure.</summary>
    public Timing Clone() => new()
    {
        duration = duration,
        Delay = Delay,
        EndDelay = EndDelay,
        iterationDelay = iterationDelay,
        iterations = iterations,
        iterationStart = iterationStart,
        Direction = Direction,
        Fill = Fill,
        Easing = Easing
    };
}
