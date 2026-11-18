namespace Shiny.Controls.Keyframe;

/// <summary>
/// Anything that can be positioned in time and evaluated at an instant. Implemented by both
/// <see cref="Timeline"/> and <see cref="Storyboard"/>, which is what lets storyboards nest.
/// </summary>
/// <remarks>
/// The contract is deliberately narrow: given an absolute offset from this node's own start,
/// write the correct state to the targets. No notion of "previous frame", no accumulation. Every
/// seek, scrub and reversal in the library falls out of that one property.
/// </remarks>
public interface IAnimationNode
{
    /// <summary>
    /// Wall-clock time from start to finish, including delays. <see cref="TimeSpan.MaxValue"/>
    /// for anything that loops forever.
    /// </summary>
    TimeSpan TotalDuration { get; }

    /// <summary>Reads current values from every target, resolving implicit keyframes.</summary>
    void CaptureBaselines();

    /// <summary>Writes the state for the given offset to every target.</summary>
    /// <param name="time">Offset from this node's start.</param>
    /// <returns>True if the node has run past its end.</returns>
    bool Evaluate(TimeSpan time);

    /// <summary>Restores every target to the values captured by <see cref="CaptureBaselines"/>.</summary>
    void RestoreBaselines();

    /// <summary>Withdraws every track's effect so targets show their own values again. See
    /// <see cref="ITrack.Release"/>.</summary>
    void Release() { }
}
