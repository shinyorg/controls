using Shiny.Maui.Controls.QuickEntry;
namespace Shiny.Maui.Controls.Desktop.QuickEntry;

/// <summary>
/// MacCatalyst cannot host the quick entry popup.
/// </summary>
/// <remarks>
/// A Catalyst app's windows are <c>UIWindowScene</c>s, and UIKit offers no way to make one
/// borderless, floating above other applications, or able to take focus without activating the
/// app — which is the entire premise of the popup. Bridging to AppKit (as the tray icon does)
/// gets a native panel on screen but not the MAUI view hierarchy inside it, since the Catalyst
/// handlers all build UIKit views. Run the AppKit head (<c>net11.0-macos</c>) for this feature.
/// </remarks>
static class QuickEntryPlatform
{
    public static bool IsSupported => false;

    public static void BeginInvokeOnMainThread(Action action)
        => Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(action);

    public static void Initialize(IDesktopQuickEntryHost host, object platformWindow, QuickEntryOptions options) { }

    public static void Show(object platformWindow, QuickEntryOptions options, double width, double height) { }

    public static void Hide(object platformWindow) { }

    public static void Resize(object platformWindow, QuickEntryOptions options, double width, double height) { }

    public static void Teardown(object platformWindow) { }

    public static void PolishEntry(object? platformView) { }
}
