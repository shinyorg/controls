using Microsoft.Extensions.Logging;
using Sample.Features.Diagrams;
using Sample.Features.Docking;
using Sample.Features.FloatingPanel;
using Sample.Features.Scheduler;
using Sample.Features.TableView;
using Microsoft.Maui.Platforms.MacOS.Essentials;
using Microsoft.Maui.Platforms.MacOS.Hosting;
using Shiny;
using Shiny.Maui.Controls.QuickEntry;
using Shiny.Maui.Controls.FloorPlan;
using Shiny.Maui.Controls.Office;
using Shiny.Maui.Controls.Scheduler;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent;
#endif

namespace Sample.MacOS;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiAppMacOS<App>()
            .AddMacOSEssentials()
            // Registers SkiaSharp plus, on this head, the AppKit canvas SkiaSharp does not ship.
            .UseShinyOffice()
            .UseShinyFloorPlan()
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
            .UseFileDrop()
            .UseDesktopQuickEntry()
            .UseShinyMediaElement()
            .UseShinyDocking()
            .AddDockPanel<SolutionExplorerPanel>("solution-explorer", "Solution Explorer", "\U0001F4C1")
            .AddDockPanel<OutputPanel>("output", "Output", "\U0001F5A5\uFE0F")
            .AddDockPanel<PropertiesPanel>("properties", "Properties", "\U0001F527")
            .AddDockPanel<EditorPanel>("editor", icon: "\U0001F4C4")
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");

            });

        // The speech engine behind the prompt's read-aloud tool. The Catalyst head gets this from
        // Sample's MauiProgram; this head has its own and needs it too.
        builder.Services.AddSpeechServices();
        builder.Services.AddSingleton<global::Sample.AppSettings>();

        // Pages and services the shared feature pages resolve from DI. The Catalyst head registers
        // these in Sample's MauiProgram; this head has its own and needs the same set - a page whose
        // constructor cannot be resolved renders blank *and* wedges Shell navigation for the rest of
        // the session on the AppKit head, with nothing logged.
        builder.Services.AddTransient<MusicBrowsePage>();
        builder.Services.AddTransient<MusicLibraryPage>();
        builder.Services.AddTransient<StylingPage>();
        builder.Services.AddTransient<BasicFlowchartPage>();
        builder.Services.AddTransient<DirectionsPage>();
        builder.Services.AddTransient<ThemesPage>();
        builder.Services.AddTransient<SubgraphsPage>();
        builder.Services.AddTransient<InteractiveEditorPage>();
        builder.Services.AddSingleton<ISchedulerEventProvider, SampleSchedulerProvider>();

#if DEBUG
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddDebug();
        builder.AddMauiDevFlowAgent();
#endif

        return builder.Build();
    }
}
