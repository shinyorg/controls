namespace Shiny.Maui.Controls;

/// <summary>
/// Hooks each platform's navigation stack so <see cref="PageTransition"/> is honoured. The hooks
/// live entirely on the handler side, which is why no navigation API (MAUI's or a library's) has to
/// change for a page's transition to apply.
/// </summary>
static partial class PageTransitionInstaller
{
    internal static void Install(MauiAppBuilder builder)
    {
        if (PageTransition.IsEnabled)
            return;

        PageTransition.IsEnabled = true;

        // Managed transitions cover the heads without a native hook (Windows, macOS AppKit, Mac
        // Catalyst, Linux GTK4) and the per-call overloads on Android's NavigationPage and modals.
        // iOS doesn't need it: every navigation there has a native hook.
#if !IOS
        ManagedPageTransitions.Install();
#endif
        InstallPlatform(builder);
    }

    static partial void InstallPlatform(MauiAppBuilder builder);
}
