namespace Shiny.Blazor.Controls;

/// <summary>
/// How a page arrives when it is navigated to, or leaves when the user goes back. The same names as
/// the MAUI package, so one design reads the same on both hosts.
/// </summary>
public enum PageTransitionType
{
    /// <summary>Not set here - fall back to the next level (the page's Push for a back navigation, then the host's default, then no animation).</summary>
    Default,
    /// <summary>No animation.</summary>
    None,
    /// <summary>The incoming page fades in over the outgoing one.</summary>
    Fade,
    /// <summary>Material "fade through": the outgoing page fades out, then the incoming one fades in while growing slightly.</summary>
    FadeThrough,
    /// <summary>The incoming page slides in from <see cref="PageTransitionDirection"/> while the outgoing one drifts a little the other way.</summary>
    Slide,
    /// <summary>Both pages move together, edge to edge.</summary>
    Push,
    /// <summary>The incoming page slides in over the outgoing one, which stays put.</summary>
    Cover,
    /// <summary>The outgoing page slides away and uncovers the incoming one.</summary>
    Reveal,
    /// <summary>Material shared axis on X: both pages shift a little horizontally while crossfading.</summary>
    SharedAxisX,
    /// <summary>Material shared axis on Y: both pages shift a little vertically while crossfading.</summary>
    SharedAxisY,
    /// <summary>Material shared axis on Z: the incoming page grows in while the outgoing one grows past and fades.</summary>
    SharedAxisZ,
    /// <summary>The incoming page grows in from slightly smaller while fading in.</summary>
    Zoom,
    /// <summary>The incoming page grows out of a circle from the point last clicked or tapped, and shrinks back into it on the way back.</summary>
    CircularReveal,
    /// <summary>The page turns over in 3D.</summary>
    Flip,
    /// <summary>The iOS 18 zoom on MAUI; on the web it is <see cref="Zoom"/>, with any hero flown.</summary>
    NativeZoom
}


/// <summary>The edge a page comes in from. Going back runs the transition in reverse, toward the same edge.</summary>
public enum PageTransitionDirection
{
    /// <summary>The transition's natural direction: from the right, or from the bottom for <see cref="PageTransitionType.SharedAxisY"/>.</summary>
    Default,
    FromRight,
    FromLeft,
    FromBottom,
    FromTop
}


/// <summary>How an <see cref="Entrance"/>'s children animate in.</summary>
public enum PageEntranceAnimation
{
    None,
    Fade,
    FadeUp,
    SlideIn,
    Scale
}


/// <summary>What a <see cref="PageTransitionHost"/> does when the user prefers reduced motion.</summary>
public enum PageTransitionReducedMotion
{
    /// <summary>Navigate without animation. The default.</summary>
    Instant,

    /// <summary>A short crossfade instead of the page's transition, and no hero flights: a change of page is still visible, nothing moves.</summary>
    Crossfade,

    /// <summary>Run the transitions anyway.</summary>
    Play
}
