namespace Shiny.Blazor.Controls.Keyframe;

/// <summary>How a <see cref="Choreography"/> places its children in time.</summary>
public enum ChoreographyMode
{
    /// <summary>All start together (plus <see cref="Choreography.Stagger"/> per child).</summary>
    Parallel,

    /// <summary>Each starts when the one before it finishes (plus <see cref="Choreography.Stagger"/> as a gap).</summary>
    Sequence
}

/// <summary>What a <see cref="Choreography"/>'s Keyframes children animate.</summary>
public enum ChoreographyApplyTo
{
    /// <summary>Each Keyframes child animates its own element (or target). The storyboard case.</summary>
    Self,

    /// <summary>
    /// The Keyframes children are a template, applied to every direct child element of the
    /// Choreography in turn, staggered. The staggered list entrance.
    /// </summary>
    Children
}

/// <summary>Which elements an <see cref="ImplicitTransitions"/> transitions.</summary>
public enum ImplicitTransitionTarget
{
    /// <summary>The wrapper element itself: change its Style or Class and it animates.</summary>
    Self,

    /// <summary>Every direct child element, whatever changes them.</summary>
    Children
}

/// <summary>How a <see cref="TransitionGroup{TItem}"/> animates its items.</summary>
public enum ItemTransitionMode
{
    /// <summary>Managed animation: Appear, Add, Remove (kept in the page until it has played) and FLIP moves.</summary>
    Keyframes,

    /// <summary>No managed animation; items carry the <c>shiny-transition-item</c> class for your own CSS (<c>@starting-style</c>, say).</summary>
    Platform,

    /// <summary>Never animate. Items appear and disappear at once - the off switch.</summary>
    None
}

/// <summary>A ready-made enter animation for list items. Removal plays it backwards.</summary>
public enum ItemTransition
{
    None,
    Fade,
    FadeUp,
    FadeDown,
    SlideLeft,
    SlideRight,
    Scale,
    Pop
}
