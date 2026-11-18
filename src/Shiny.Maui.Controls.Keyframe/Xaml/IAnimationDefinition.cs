namespace Shiny.Maui.Controls.Keyframe;

/// <summary>
/// Something that describes an animation and can be built against a view: <see cref="Keyframes"/>
/// and <see cref="Choreography"/>. Definitions are plain descriptions, so one in a
/// <c>ResourceDictionary</c> can be played on any number of views at once.
/// </summary>
public interface IAnimationDefinition
{
    /// <summary>Whether playback starts as soon as the element it is attached to loads.</summary>
    bool AutoPlay { get; }

    /// <summary>Playback rate. 1 is real time; negative values run backwards.</summary>
    double Speed { get; }

    /// <summary>What happens under the OS reduced-motion setting.</summary>
    ReducedMotionBehavior ReducedMotion { get; }

    /// <summary>Builds a runnable animation bound to a view.</summary>
    /// <param name="target">The element the animation drives by default.</param>
    /// <param name="resolveTarget">Resolves an <c>x:Name</c> to an element.</param>
    IAnimationNode Build(VisualElement target, Func<string, VisualElement?>? resolveTarget = null);
}
