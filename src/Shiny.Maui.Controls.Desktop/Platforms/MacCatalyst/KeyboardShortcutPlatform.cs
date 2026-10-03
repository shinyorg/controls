using MauiWindow = Microsoft.Maui.Controls.Window;

namespace Shiny.Maui.Controls.Desktop.KeyboardShortcuts;

/// <summary>
/// Mac Catalyst key source for keyboard shortcuts: the same GameController-based source the core
/// package uses on iOS, linked in here because core has no Catalyst target.
/// </summary>
static class KeyboardShortcutPlatform
{
    public static IDisposable? Attach(MauiWindow window, object platformWindow)
        => GameControllerKeySource.Attach(window, platformWindow);
}
