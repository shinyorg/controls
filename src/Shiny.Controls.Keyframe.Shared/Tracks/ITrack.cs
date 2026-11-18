namespace Shiny.Controls.Keyframe;

/// <summary>
/// A single animated property. Non-generic so a timeline can hold tracks of mixed value types.
/// </summary>
public interface ITrack
{
    /// <summary>Optional label, used for diagnostics and for addressing a track after the fact.</summary>
    string? Name { get; }

    /// <summary>
    /// Whether this track currently has a live target. Tracks hold their targets weakly, so a
    /// looping animation cannot keep a disposed page alive; once collected the track goes inert.
    /// </summary>
    bool IsAlive { get; }

    /// <summary>
    /// Reads the target's present value and stores it as the resolution for any implicit keyframes.
    /// Called once when playback starts, so an interrupted animation continues from where it is.
    /// </summary>
    void CaptureBaseline();

    /// <summary>Writes the value for the given progress to the target.</summary>
    /// <param name="progress">Eased progress through the iteration. May fall outside [0,1] when an
    /// overshooting curve is applied at timeline level, in which case the track extrapolates from
    /// the nearest segment.</param>
    void Apply(double progress);

    /// <summary>Restores the value captured by <see cref="CaptureBaseline"/>.</summary>
    void RestoreBaseline();

    /// <summary>
    /// Withdraws the track's effect so the target shows its own value again — what CSS does when an
    /// animation without a forwards fill ends, or is cancelled. Hosts that write through a layer
    /// above the property's own value (MAUI's animation layer, a browser's animation stack) drop that
    /// layer here; a track that writes the property directly has nothing to withdraw, so the default
    /// does nothing and the last value written stays.
    /// </summary>
    void Release() { }
}
