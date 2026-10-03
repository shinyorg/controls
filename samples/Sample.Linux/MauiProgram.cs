using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Shiny;
using Shiny.Maui.Controls.QuickEntry;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent.Gtk;
#endif

namespace Sample.Linux;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiAppLinuxGtk4<App>()
            .AddLinuxGtk4Essentials()
            .UseShinyControls(cfg => cfg
                .AddDefaultMauiControlFeedback()
                .ConfigureQuickEntry(o =>
                {
                    o.HotKey = OperatingSystem.IsMacOS() ? "Cmd+Opt+Space" : "Ctrl+Alt+Space";
                    o.Placement = QuickEntryPlacement.TopCenter;
                    o.ScreenGlow = ScreenGlowTrigger.WhileBusy;
                }))
            .UseShinyShell(x => x.AddGeneratedMaps())
            .UseTrayIcon()
            .UseDesktopQuickEntry()
            // AppKit, GTK4 and Catalyst key sources for keyboard shortcuts (Windows/iOS/Android need nothing)
            .UseDesktopKeyboardShortcuts()
            .UseShinyMediaElementGtk()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");

            });

        builder.Services.AddSingleton<global::Sample.AppSettings>();

#if DEBUG
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddDebug();
        builder.AddMauiDevFlowAgent();
#endif

        return builder.Build();
    }
}
