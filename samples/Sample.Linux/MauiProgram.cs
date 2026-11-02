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
            // the docking page's panels, as in the shared and macOS heads; without them every panel is "Unknown panel type"
            .UseShinyDesktop(cfg => cfg
                .UseQuickEntryWindow()
                .AddDockPanel<global::Sample.Features.Docking.SolutionExplorerPanel>("solution-explorer", "Solution Explorer", "\U0001F4C1")
                .AddDockPanel<global::Sample.Features.Docking.OutputPanel>("output", "Output", "\U0001F5A5\uFE0F")
                .AddDockPanel<global::Sample.Features.Docking.PropertiesPanel>("properties", "Properties", "\U0001F527")
                .AddDockPanel<global::Sample.Features.Docking.EditorPanel>("editor", icon: "\U0001F4C4")
            )
            .UseShinyMediaElementGtk()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");

            });

        builder.Services.AddSingleton<global::Sample.AppSettings>();
        builder.Services.AddSingleton<global::Shiny.Maui.Controls.Scheduler.ISchedulerEventProvider, global::Sample.Features.Scheduler.SampleSchedulerProvider>();

#if DEBUG
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddDebug();
        builder.AddMauiDevFlowAgent();
#endif

        return builder.Build();
    }
}
