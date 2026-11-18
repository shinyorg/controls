namespace Shiny.Controls.Keyframe;

/// <summary>
/// A set of tracks sharing one timing configuration — the unit that corresponds to a CSS
/// <c>@keyframes</c> rule applied to an element.
/// </summary>
public sealed class Timeline : IAnimationNode
{
    readonly List<ITrack> tracks = [];

    /// <summary>Creates an empty timeline with default timing.</summary>
    public Timeline() : this(new Timing()) { }

    /// <summary>Creates an empty timeline with the given timing.</summary>
    public Timeline(Timing timing)
    {
        ArgumentNullException.ThrowIfNull(timing);
        Timing = timing;
    }

    /// <summary>Optional label, used for diagnostics.</summary>
    public string? Name { get; set; }

    /// <summary>Duration, repetition, direction and fill behaviour.</summary>
    public Timing Timing { get; }

    /// <summary>The animated properties.</summary>
    public IReadOnlyList<ITrack> Tracks => tracks;

    /// <inheritdoc />
    public TimeSpan TotalDuration => Timing.TotalDuration;

    /// <summary>Raised the first time an evaluation lands past the end of the active duration.</summary>
    public event EventHandler? Finished;

    /// <summary>Raised when evaluation crosses into a new iteration.</summary>
    public event EventHandler<int>? IterationChanged;

    int lastIteration = -1;
    bool finishedRaised;
    bool applied;

    /// <summary>Adds a track.</summary>
    public Timeline Add(ITrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        tracks.Add(track);
        return this;
    }

    /// <summary>Adds several tracks.</summary>
    public Timeline AddRange(IEnumerable<ITrack> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var track in items)
            Add(track);
        return this;
    }

    /// <summary>Removes a track.</summary>
    public bool Remove(ITrack track) => tracks.Remove(track);

    /// <summary>
    /// Drops any track whose target has been collected. Worth calling on long-lived timelines that
    /// are reused across many targets; short-lived ones will never accumulate enough to matter.
    /// </summary>
    public int PruneDeadTracks() => tracks.RemoveAll(t => !t.IsAlive);

    /// <inheritdoc />
    public void CaptureBaselines()
    {
        // Reset the boundary trackers too — capturing baselines is what "starting" means, and a
        // replayed timeline must be able to raise Finished a second time.
        lastIteration = -1;
        finishedRaised = false;
        applied = false;

        foreach (var track in tracks)
            track.CaptureBaseline();
    }

    /// <inheritdoc />
    public void RestoreBaselines()
    {
        foreach (var track in tracks)
            track.RestoreBaseline();
    }

    /// <inheritdoc />
    public void Release()
    {
        applied = false;
        foreach (var track in tracks)
            track.Release();
    }

    /// <inheritdoc />
    public bool Evaluate(TimeSpan time)
    {
        var sample = Timing.Sample(time);

        if (sample.ShouldApply)
        {
            applied = true;
            foreach (var track in tracks)
                track.Apply(sample.Progress);
        }
        else if (applied)
        {
            // Leaving the active window without a fill: withdraw, so the targets show their own
            // values again rather than freezing on whatever the last frame happened to write.
            Release();
        }

        if (sample.Iteration != lastIteration)
        {
            // Suppress the notification for the very first sample; nothing has "changed" yet.
            var isFirst = lastIteration < 0;
            lastIteration = sample.Iteration;
            if (!isFirst)
                IterationChanged?.Invoke(this, sample.Iteration);
        }

        if (sample.IsFinished && !finishedRaised)
        {
            finishedRaised = true;
            Finished?.Invoke(this, EventArgs.Empty);
        }

        return sample.IsFinished;
    }

    /// <summary>
    /// Evaluates by normalised position rather than absolute time, where 0 is the start of the
    /// first iteration and 1 the end of the last. Convenient for scrubbing from a slider or a pan
    /// gesture. Throws for infinitely repeating timelines, which have no meaningful end to scrub to.
    /// </summary>
    /// <param name="progress">Position within the active duration, 0 to 1.</param>
    public void Scrub(double progress)
    {
        if (double.IsPositiveInfinity(Timing.Iterations))
            throw new InvalidOperationException(
                "Cannot scrub a timeline that repeats infinitely; it has no end to scrub toward. " +
                "Set a finite Iterations value, or evaluate by absolute time instead.");

        var clamped = Math.Clamp(progress, 0d, 1d);
        Evaluate(Timing.Delay + Timing.ActiveDuration * clamped);
    }
}
