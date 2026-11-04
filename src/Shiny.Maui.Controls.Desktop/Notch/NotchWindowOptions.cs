#if !ANDROID && !IOS && !MACCATALYST
namespace Shiny.Maui.Controls.Desktop.Notch;

/// <summary>Settings for <see cref="INotchWindowService"/>. Set them with <c>UseShinyDesktop(cfg =&gt; cfg.ConfigureNotch(...))</c>.</summary>
public sealed class NotchWindowOptions
{
    /// <summary>
    /// Grow out of the camera notch when the screen has one (macOS). When false — or when there is no
    /// notch, which is always the case on Windows and Linux — the notch is a floating island at the top
    /// of the screen instead.
    /// </summary>
    public bool UseHardwareNotch { get; set; } = true;

    /// <summary>
    /// Gap between the top of the usable screen area and a floating island, in points: below the menu
    /// bar on macOS, below a top-docked taskbar or panel on Windows and Linux. Unused when hugging the
    /// hardware notch. Set it to 0 with <see cref="NotchShape.Notch"/> (through <see cref="Configure"/>)
    /// for a notch that hangs from the top edge.
    /// </summary>
    public double TopMargin { get; set; } = 8;

    /// <summary>
    /// macOS, on a display without a notch (or with <see cref="UseHardwareNotch"/> off): measure
    /// <see cref="TopMargin"/> from the screen's top edge rather than from the bottom of the menu bar,
    /// so the island sits over the menu bar the way the hardware notch does. With a 0 margin and
    /// <see cref="NotchShape.Notch"/>, an external monitor gets a notch flush with its top edge.
    /// Ignored on Windows and Linux.
    /// </summary>
    public bool CoverMenuBar { get; set; }

    /// <summary>The widest the notch grows, in points.</summary>
    public double MaxWidth { get; set; } = 640;

    /// <summary>The tallest the notch grows, in points.</summary>
    public double MaxHeight { get; set; } = 480;

    /// <summary>How long the window takes to reshape, in milliseconds. 0 snaps.</summary>
    public double MorphDuration { get; set; } = 300;

    /// <summary>
    /// Keep the notch on every Space and over full-screen apps (macOS), or on every workspace (Linux,
    /// X11). Windows has no public API for this, so it is ignored there.
    /// </summary>
    public bool JoinAllSpaces { get; set; } = true;

    /// <summary>
    /// Styles each <see cref="NotchView"/> the service creates — colour, padding, corner radius. Runs
    /// after the service has applied its own geometry, so anything set here wins.
    /// </summary>
    public Action<NotchView>? Configure { get; set; }
}
#endif
