#if !ANDROID && !IOS && !MACCATALYST
namespace Shiny.Maui.Controls.Desktop.Notch;

/// <summary>A display a notch can be shown on (see <see cref="INotchWindowFactory"/>).</summary>
/// <param name="Id">
/// Identifies the display across launches, for saving which displays the user picked: on macOS its vendor,
/// model and serial number; on Windows its device name (<c>\\.\DISPLAY1</c>); on Linux its connector (<c>HDMI-1</c>).
/// </param>
/// <param name="Name">What to call it in a list of displays, such as "Built-in Retina Display" or "DELL U2720Q".</param>
/// <param name="IsPrimary">The primary display: the one with the menu bar on macOS, the main display elsewhere.</param>
/// <param name="HardwareNotchSize">This display's camera cut-out, in points; <see cref="Size.Zero"/> without one.</param>
public sealed record NotchDisplay(string Id, string Name, bool IsPrimary, Size HardwareNotchSize)
{
    /// <summary>The display has a camera notch. Only ever true on macOS.</summary>
    public bool HasHardwareNotch => this.HardwareNotchSize.Width > 0;
}
#endif
