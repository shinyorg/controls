#if !ANDROID && !IOS && !MACCATALYST
using Microsoft.Extensions.DependencyInjection;

namespace Shiny.Maui.Controls.Desktop.Notch;

/// <summary>
/// Lists the displays and creates a notch window for any one of them, for a notch on more than one display
/// at once. The <see cref="INotchWindowService"/> registered on its own is the primary display's.
/// </summary>
/// <remarks>
/// Each notch works out its own display's geometry, so with a MacBook beside an external monitor the
/// built-in display's notch grows out of the camera while the monitor's is a notch with no cut-out.
/// </remarks>
public interface INotchWindowFactory
{
    /// <summary>The displays connected now, the primary first.</summary>
    IReadOnlyList<NotchDisplay> GetDisplays();

    /// <summary>
    /// A new notch window on the display with this <see cref="NotchDisplay.Id"/>, or on the primary display
    /// for null. A display that isn't connected falls back to the primary, so watch for display changes and
    /// dispose the notches of displays that have gone. The caller owns the notch and disposes it.
    /// </summary>
    INotchWindowService Create(string? displayId);
}

sealed class DesktopNotchWindowFactory(NotchWindowOptions options, IServiceProvider services) : INotchWindowFactory
{
    public IReadOnlyList<NotchDisplay> GetDisplays() => NotchWindowPlatform.GetDisplays();

    public INotchWindowService Create(string? displayId) => new DesktopNotchWindowService(
        options,
        services.GetService<Microsoft.Extensions.Logging.ILogger<DesktopNotchWindowService>>(),
        displayId
    );
}
#endif
