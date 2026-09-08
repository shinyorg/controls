using UIKit;

namespace Shiny.Maui.Controls.Infrastructure;

static partial class SafeAreaInsets
{
    static double PlatformBottom() => Window?.SafeAreaInsets.Bottom ?? 0;

    static double PlatformTop() => Window?.SafeAreaInsets.Top ?? 0;

    /// <summary>
    /// The key window, falling back to any window on a foreground scene.
    /// </summary>
    /// <remarks>
    /// Not simply the first window found: an app showing a modal, an alert or the keyboard has more
    /// than one, and the ones that are not on screen report insets of zero — which is the same answer
    /// as "this device has no home indicator" and impossible to tell apart after the fact.
    /// </remarks>
    static UIWindow? Window
    {
        get
        {
            var windows = UIApplication.SharedApplication?.ConnectedScenes
                .OfType<UIWindowScene>()
                .SelectMany(scene => scene.Windows)
                .ToList();

            if (windows is null || windows.Count == 0)
                return null;

            return windows.FirstOrDefault(w => w.IsKeyWindow) ?? windows[0];
        }
    }
}
