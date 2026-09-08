using AndroidX.Core.View;
using Microsoft.Maui.Platform;

namespace Shiny.Maui.Controls;

public partial class ShinyNavigationPage
{
    /// <summary>
    /// Fills the status bar with the bar's colour and flips its icons to whichever reads on it.
    /// </summary>
    /// <remarks>
    /// <para>Two halves with very different lifespans. The icons are
    /// <c>WindowInsetsControllerCompat</c>, which works from API 23 and is the whole story on a
    /// current device. The <em>colour</em> is <c>Window.SetStatusBarColor</c>, which Android 15
    /// (API 35) turned into a no-op when it made edge-to-edge mandatory — from there on what shows
    /// behind the clock is whatever the app draws under the inset, which is the bar's own background
    /// once <see cref="RespectSafeArea"/> has extended it. It is still called below because an app
    /// built against API 35 still runs on Android 14 and earlier, where it is the only thing that
    /// colours the strip.</para>
    /// <para>Read as: the two paths agree. Below 15 the system fills the strip with the colour; from
    /// 15 the bar itself paints into it. Either way the status bar wears the bar's background.</para>
    /// </remarks>
    partial void ApplyStatusBar(Color? background, StatusBarStyle style)
    {
        // The current activity, not this page's handler context. A page presented modally - which is
        // how a NavigationPage is shown from a Shell app - has no handler chain that reaches an
        // Activity, so the lookup came back null and the status bar was silently left alone. There
        // is one Activity in a MAUI Android app and it owns the only Window there is.
        var window = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window
            ?? this.Handler?.MauiContext?.Context?.GetActivity()?.Window;

        if (window?.DecorView is not { } decor)
            return;

        // Deprecated from API 35 and a no-op there, not an error - so it is skipped rather than
        // swallowed, which keeps the call off the hot path on a current device.
        if (background is not null && !OperatingSystem.IsAndroidVersionAtLeast(35))
        {
#pragma warning disable CA1422 // pre-35 only, and guarded above
            window.SetStatusBarColor(background.ToPlatform());
#pragma warning restore CA1422
        }

        // KNOWN LIMITATION - the icon appearance does not currently take on Android.
        //
        // Everything below runs: the style resolves correctly, the window is found, and both the
        // AndroidX and the platform write execute (confirmed with a temporary probe on an API 36
        // emulator). The system still reports LIGHT_STATUS_BARS afterwards - `adb shell dumpsys
        // window | grep mLastAppearance` - so something re-asserts the theme's appearance.
        //
        // What has been ruled out: it is not a race (a write triggered long after the page settled
        // loses too), it is not the activity lookup, and it is not the AndroidX wrapper (the direct
        // API behaves the same). The *background* half is unaffected and works - what shows behind
        // the clock is the bar painting through the top inset.
        //
        // Posted rather than applied inline so it lands after MAUI's own presentation work, which
        // costs nothing and is what a fix would want anyway.
        var light = style == StatusBarStyle.DarkContent;
        decor.Post(() =>
        {
            // "Light status bars" is Android's name for a light *background*, so the icons it draws
            // are dark. It is therefore the opposite of the style being asked for, and reading it
            // the other way round is a bug that shows up only as an invisible clock.
            var mask = (int)Android.Views.WindowInsetsControllerAppearance.LightStatusBars;

            if (OperatingSystem.IsAndroidVersionAtLeast(30) && window.InsetsController is { } platform)
            {
                platform.SetSystemBarsAppearance(light ? mask : 0, mask);
            }
            else if (WindowCompat.GetInsetsController(window, decor) is { } controller)
            {
                // Nullable in the binding: the controller does not exist before the window is
                // attached, and this can run from a Refresh that beat the activity to it.
                controller.AppearanceLightStatusBars = light;
            }
        });
    }
}
