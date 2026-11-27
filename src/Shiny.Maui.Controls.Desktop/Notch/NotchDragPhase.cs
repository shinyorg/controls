#if !ANDROID && !IOS && !MACCATALYST
namespace Shiny.Maui.Controls.Desktop.Notch;

/// <summary>What the pointer did, as the platform reports it for dragging the notch (see <see cref="INotchWindowService.IsDraggable"/>).</summary>
enum NotchDragPhase
{
    /// <summary>The button went down on the notch window.</summary>
    Pressed,
    /// <summary>The pointer moved with the button still down.</summary>
    Moved,
    /// <summary>The button came up.</summary>
    Released
}
#endif
