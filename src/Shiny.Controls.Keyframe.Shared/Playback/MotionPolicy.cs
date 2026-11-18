namespace Shiny.Controls.Keyframe;

/// <summary>How an animation responds when the user has asked the operating system for less motion.</summary>
public enum ReducedMotionBehavior
{
    /// <summary>Follow the system setting: animate normally, or jump straight to the end state when
    /// reduced motion is on. The default.</summary>
    Auto,

    /// <summary>Always animate, even under reduced motion. For motion that carries information — a
    /// progress sweep, a value changing — rather than decoration.</summary>
    Play,

    /// <summary>Never animate: always jump straight to the end state.</summary>
    Instant
}

/// <summary>
/// Where the engine asks whether reduced motion is on. Each host wires <see cref="IsReducedMotion"/>
/// to the platform setting at startup — iOS Reduce Motion, Android's animator duration scale,
/// Windows' animation setting, <c>prefers-reduced-motion</c> in a browser — and tests can replace it.
/// </summary>
public static class MotionPolicy
{
    static Func<bool> isReducedMotion = static () => false;

    /// <summary>
    /// Reports whether the user has asked for less motion. Read once per playback start, never per
    /// frame, so a cheap but not free platform query is fine here.
    /// </summary>
    public static Func<bool> IsReducedMotion
    {
        get => isReducedMotion;
        set => isReducedMotion = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Whether an animation with the given behaviour should skip to its end state right now.</summary>
    public static bool ShouldSkip(ReducedMotionBehavior behavior) => behavior switch
    {
        ReducedMotionBehavior.Play => false,
        ReducedMotionBehavior.Instant => true,
        _ => SafeIsReduced()
    };

    static bool SafeIsReduced()
    {
        try
        {
            return isReducedMotion();
        }
        catch
        {
            // A platform query that throws must not take the animation down with it; animate.
            return false;
        }
    }
}
