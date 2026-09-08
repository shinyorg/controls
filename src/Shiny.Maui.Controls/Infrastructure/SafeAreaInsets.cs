namespace Shiny.Maui.Controls.Infrastructure;

/// <summary>
/// The window's own safe-area insets, in device-independent units.
/// </summary>
/// <remarks>
/// <para><see cref="SafeAreaEdges"/> is the right tool almost everywhere and this does not replace
/// it — but it does not reach a view laid out inside a chain of <c>Auto</c>-sized rows, which is
/// exactly the shape of a bar docked against a screen edge. Declared there, the inset arrives as
/// zero and the bar's contents sit under the home indicator with nothing to say why. Reading the
/// window is the answer that always works.</para>
/// <para>Zero on Windows, GTK4 and macOS AppKit, none of which has an inset to report.</para>
/// </remarks>
static partial class SafeAreaInsets
{
    /// <summary>The inset along the bottom edge — the home indicator, or Android's gesture bar.</summary>
    public static double Bottom => PlatformBottom();

    /// <summary>The inset along the top edge — the status bar, notch or Dynamic Island.</summary>
    public static double Top => PlatformTop();
}
