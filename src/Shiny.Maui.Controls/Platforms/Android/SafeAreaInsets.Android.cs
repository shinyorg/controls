using AndroidX.Core.View;
using Microsoft.Maui.Platform;

namespace Shiny.Maui.Controls.Infrastructure;

static partial class SafeAreaInsets
{
    static double PlatformBottom() => Edge(insets => insets.Bottom);

    static double PlatformTop() => Edge(insets => insets.Top);

    /// <summary>
    /// System bars and the display cutout together — the same union
    /// <see cref="SafeAreaRegions.Container"/> means, and deliberately not the IME.
    /// </summary>
    static double Edge(Func<AndroidX.Core.Graphics.Insets, int> pick)
    {
        var decor = Platform.CurrentActivity?.Window?.DecorView;
        if (decor?.Context?.Resources?.DisplayMetrics is not { } metrics)
            return 0;

        var insets = ViewCompat.GetRootWindowInsets(decor)
            ?.GetInsets(WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.DisplayCutout());

        // Pixels, and every MAUI layout value is device-independent - a raw inset would be roughly
        // three times too large on a typical phone.
        return insets is null ? 0 : pick(insets) / metrics.Density;
    }
}
