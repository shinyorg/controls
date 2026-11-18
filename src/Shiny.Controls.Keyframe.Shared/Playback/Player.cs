namespace Shiny.Controls.Keyframe;

/// <summary>
/// Drives an <see cref="IAnimationNode"/> from a clock, and owns everything stateful about
/// playback: position, rate, pause, seek.
/// </summary>
/// <remarks>
/// Keeping playback state here rather than on <see cref="Timeline"/> is what allows the same
/// timeline to be played by several players at once — the model stays a pure description, and the
/// player is the only thing that remembers where you are.
/// </remarks>
public sealed class Player : IDisposable
{
    readonly IAnimationNode node;
    readonly IClock clock;
    readonly object gate = new();

    TaskCompletionSource<bool>? completion;
    bool subscribed;
    bool disposed;
    double rate = 1d;

    /// <summary>Creates a player for a node.</summary>
    /// <param name="node">The timeline or storyboard to drive.</param>
    /// <param name="clock">The frame source.</param>
    public Player(IAnimationNode node, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(clock);

        this.node = node;
        this.clock = clock;
    }

    /// <summary>Current lifecycle state.</summary>
    public PlaybackState State { get; private set; } = PlaybackState.Idle;

    /// <summary>The node's whole length, delays included; <see cref="TimeSpan.MaxValue"/> when it loops forever.</summary>
    public TimeSpan Duration => node.TotalDuration;

    /// <summary>Current position within the node.</summary>
    public TimeSpan Position { get; private set; }

    /// <summary>
    /// Playback speed. 1 is real time, 2 is double speed, and negative values run backwards from
    /// the current position — which works exactly because evaluation never accumulates state.
    /// </summary>
    public double Rate
    {
        get => rate;
        set
        {
            if (double.IsNaN(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Rate must be a number.");
            rate = value;
        }
    }

    /// <summary>
    /// When true, <see cref="Stop()"/> puts every target back to the value it had when playback began.
    /// </summary>
    public bool RestoreOnStop { get; set; }

    /// <summary>
    /// What <see cref="Play"/> does when the user has asked for reduced motion. The default,
    /// <see cref="ReducedMotionBehavior.Auto"/>, jumps straight to the end state when
    /// <see cref="MotionPolicy.IsReducedMotion"/> says so. Seeking is direct manipulation and is
    /// never affected: a scrubbed player follows the finger whatever the setting.
    /// </summary>
    public ReducedMotionBehavior ReducedMotion { get; set; }

    /// <summary>Raised when playback reaches the end (or, at a negative rate, the beginning).</summary>
    public event EventHandler? Finished;

    /// <summary>
    /// Starts from the beginning, capturing baselines so implicit keyframes resolve against
    /// wherever the targets currently sit.
    /// </summary>
    public void Play()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        node.CaptureBaselines();
        Position = TimeSpan.Zero;

        if (MotionPolicy.ShouldSkip(ReducedMotion))
        {
            SkipToEnd();
            return;
        }

        State = PlaybackState.Running;

        node.Evaluate(Position);
        Subscribe();
        clock.Start();
    }

    /// <summary>
    /// The reduced-motion path: land on the end state without animating. A looping animation has no
    /// end, so it shows its opening pose and stays there — the resting state, for every decorative
    /// loop worth honouring the setting for.
    /// </summary>
    void SkipToEnd()
    {
        var total = node.TotalDuration;
        Position = total == TimeSpan.MaxValue || rate < 0d ? TimeSpan.Zero : total;
        node.Evaluate(Position);
        CompleteRun();
    }

    /// <summary>Resumes from the current position without recapturing baselines.</summary>
    public void Resume()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (State is PlaybackState.Running)
            return;

        State = PlaybackState.Running;
        Subscribe();
        clock.Start();
    }

    /// <summary>
    /// Holds the current position and stops consuming frames.
    /// </summary>
    /// <remarks>
    /// Detaching from the clock rather than merely ignoring it matters on a shared frame source:
    /// it lets the platform stop producing frames entirely once every animation on the window is
    /// paused, instead of running the display link to deliver ticks nobody acts on.
    /// </remarks>
    public void Pause()
    {
        if (State is not PlaybackState.Running)
            return;

        State = PlaybackState.Paused;
        Unsubscribe();
    }

    /// <summary>Stops and resets to the beginning, optionally restoring the captured baselines.</summary>
    public void Stop() => Stop(RestoreOnStop ? StopBehavior.Restore : StopBehavior.Hold);

    /// <summary>Stops and resets to the beginning, doing the given thing with the values written.</summary>
    public void Stop(StopBehavior behavior)
    {
        Unsubscribe();
        State = PlaybackState.Idle;
        Position = TimeSpan.Zero;

        if (behavior == StopBehavior.Restore)
            node.RestoreBaselines();
        else if (behavior == StopBehavior.Release)
            node.Release();

        completion?.TrySetResult(false);
        completion = null;
    }

    /// <summary>Jumps straight to the end and applies the final state.</summary>
    public void Finish()
    {
        var total = node.TotalDuration;
        if (total == TimeSpan.MaxValue)
            throw new InvalidOperationException(
                "Cannot finish an infinitely repeating animation. Stop it, or give it a finite iteration count.");

        Seek(total);
        CompleteRun();
    }

    /// <summary>Moves to an absolute position and applies that state immediately.</summary>
    public void Seek(TimeSpan position)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        Position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        node.Evaluate(Position);
    }

    /// <summary>
    /// Moves to a normalised position, 0 to 1, across the node's whole duration. This is the
    /// gesture-driven scrubbing entry point.
    /// </summary>
    public void SeekProgress(double progress)
    {
        var total = node.TotalDuration;
        if (total == TimeSpan.MaxValue)
            throw new InvalidOperationException(
                "Cannot seek by progress on an infinitely repeating animation; there is no end to measure against.");

        Seek(total * Math.Clamp(progress, 0d, 1d));
    }

    /// <summary>Plays from the start and completes when the animation finishes or is stopped.</summary>
    /// <param name="cancellationToken">Stops playback when signalled.</param>
    /// <returns>True if the animation ran to completion; false if it was stopped or cancelled.</returns>
    public Task<bool> PlayAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        completion?.TrySetResult(false);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        completion = tcs;

        if (cancellationToken.CanBeCanceled)
        {
            var registration = cancellationToken.Register(static state =>
            {
                var player = (Player)state!;
                player.Stop();
            }, this);

            // Release the registration once the run settles, however it settles.
            tcs.Task.ContinueWith(
                static (_, reg) => ((CancellationTokenRegistration)reg!).Dispose(),
                registration,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        Play();
        return tcs.Task;
    }

    void OnTick(TimeSpan delta)
    {
        if (State is not PlaybackState.Running)
            return;

        // Scaling the delta rather than the position keeps rate changes mid-flight smooth: the
        // animation carries on from where it is instead of jumping to a rescaled position.
        var scaled = TimeSpan.FromTicks((long)(delta.Ticks * rate));
        var next = Position + scaled;

        if (next < TimeSpan.Zero)
        {
            // Ran off the front while playing in reverse.
            Position = TimeSpan.Zero;
            node.Evaluate(Position);
            CompleteRun();
            return;
        }

        Position = next;

        if (node.Evaluate(Position) && rate > 0d)
            CompleteRun();
    }

    void CompleteRun()
    {
        Unsubscribe();
        State = PlaybackState.Finished;

        completion?.TrySetResult(true);
        completion = null;

        Finished?.Invoke(this, EventArgs.Empty);
    }

    void Subscribe()
    {
        lock (gate)
        {
            if (subscribed)
                return;

            clock.Tick += OnTick;
            subscribed = true;
        }
    }

    void Unsubscribe()
    {
        lock (gate)
        {
            if (!subscribed)
                return;

            clock.Tick -= OnTick;
            subscribed = false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        Unsubscribe();
        completion?.TrySetResult(false);
        completion = null;
    }
}
