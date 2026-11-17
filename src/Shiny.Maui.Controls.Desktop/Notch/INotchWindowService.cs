#if !ANDROID && !IOS && !MACCATALYST
namespace Shiny.Maui.Controls.Desktop.Notch;

/// <summary>
/// Puts a <see cref="NotchView"/> at the top-centre of the screen, in a borderless window above every
/// other application, and morphs it to fit whatever content you give it. On a MacBook it lives in the
/// camera notch and grows out of it.
/// </summary>
/// <remarks>
/// <para>
/// On a Mac whose display has a notch, the empty notch is exactly the size of the hardware cut-out, so
/// it disappears into it; give it content and it extends down out of the notch. Everywhere else — a Mac
/// display without one, Windows, Linux — it is a floating island centred at the top of the screen.
/// </para>
/// <para>
/// macOS AppKit (<c>net11.0-macos</c>), Windows and Linux (GTK4, the <c>net11.0</c> asset) only. The
/// type does not exist on Android, iOS or Mac Catalyst, so shared code guards it with
/// <c>#if !ANDROID &amp;&amp; !IOS &amp;&amp; !MACCATALYST</c>; put an in-app <see cref="NotchView"/> on the page
/// there instead. Under a Linux Wayland session the compositor decides where the window goes and how
/// it stacks.
/// </para>
/// <para>
/// The window never takes keyboard focus, so the content is for glancing and clicking — status, media
/// controls, a timer, a progress readout — not for typing.
/// </para>
/// </remarks>
public interface INotchWindowService
{
    /// <summary>True when the screen the notch is shown on has a camera notch. Only ever true on macOS.</summary>
    bool HasHardwareNotch { get; }

    /// <summary>
    /// The camera cut-out, in points (<see cref="Size.Zero"/> without one). Content that should sit
    /// <em>beside</em> the camera rather than below it — a "live activity" peeking out either side — lays
    /// itself out around this width at this height.
    /// </summary>
    Size HardwareNotchSize { get; }

    /// <summary>Whether the mouse pointer is over the notch window right now.</summary>
    bool IsPointerOver { get; }

    /// <summary>
    /// The pointer moved onto the notch, whether or not your app is active, so a notch that expands on
    /// hover can be built on it. No accessibility permission is needed on any platform.
    /// </summary>
    event EventHandler? PointerEntered;

    /// <summary>The pointer left the notch (or the notch was hidden while the pointer was over it).</summary>
    event EventHandler? PointerExited;

    /// <summary>Whether the notch window is on screen.</summary>
    bool IsVisible { get; }

    /// <summary>The content currently in the notch.</summary>
    View? Content { get; }

    /// <summary>Raised when the notch surface (not a button inside the content) is clicked.</summary>
    event EventHandler? Tapped;

    /// <summary>Shows the notch, optionally with new content. Showing with no content shows the empty notch.</summary>
    /// <param name="content">The content, or null to keep what is there.</param>
    /// <param name="configure">Styles the notch for this content only. See <see cref="SetContentAsync"/>.</param>
    Task ShowAsync(View? content = null, Action<NotchView>? configure = null);

    /// <summary>
    /// Replaces the content and morphs to fit it. Content that only changes its own size needs no call —
    /// the notch follows it.
    /// </summary>
    /// <param name="content">The new content, or null for the empty notch.</param>
    /// <param name="configure">
    /// Styles the notch for this content only — padding, minimum height, shape — after the service's own
    /// geometry and <see cref="NotchWindowOptions.Configure"/>, so it wins. Each content gets a fresh
    /// notch, so nothing set here carries over to the next.
    /// </param>
    Task SetContentAsync(View? content, Action<NotchView>? configure = null);

    /// <summary>Takes the notch off screen. The window is kept, so the next show is instant.</summary>
    void Hide();
}
#endif
