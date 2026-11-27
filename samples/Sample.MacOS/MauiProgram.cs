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
            .UseOfficeDocumentStore()
            .UseShinyFloorPlan()
            .UseShinyControls(cfg => cfg
                .AddDefaultMauiControlFeedback()
                .ConfigureQuickEntry(o =>
                {
                    o.HotKey = OperatingSystem.IsMacOS() ? "Cmd+Opt+Space" : "Ctrl+Alt+Space";
                    o.Placement = QuickEntryPlacement.TopCenter;
                    o.ScreenGlow = ScreenGlowTrigger.WhileBusy;
                }))
            .UseShinyShell(x => x
                .AddGeneratedMaps()
                // the standalone header-peek page shares MinimizedSheetViewModel; its [ShellMap] only covers MinimizedSheetPage
                .Add<Sample.Features.FloatingPanel.MinimizedSheetStandalonePage, Sample.Features.FloatingPanel.MinimizedSheetViewModel>(registerRoute: false)
            )
            .UseShinyDesktop(cfg => cfg
                .UseQuickEntryWindow()
                .UseFileDrop()
                .AddDockPanel<SolutionExplorerPanel>("solution-explorer", "Solution Explorer", "\U0001F4C1")
                .AddDockPanel<OutputPanel>("output", "Output", "\U0001F5A5\uFE0F")
                .AddDockPanel<PropertiesPanel>("properties", "Properties", "\U0001F527")
                .AddDockPanel<EditorPanel>("editor", icon: "\U0001F4C4")
            )
            .UseShinyMediaElement()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");

            });

        // The speech engine behind the prompt's read-aloud tool. The Catalyst head gets this from
        // Sample's MauiProgram; this head has its own and needs it too.
        builder.Services.AddSpeechServices();
        builder.Services.AddSingleton<global::Sample.AppSettings>();
        builder.Services.AddSingleton<global::Sample.Features.Chat.InMemoryChatSessionProvider>();

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
        // TODO: drop the option once the DevFlow agents move to 0.1.0-preview.15+. They are held at
        // preview.14 (whose preview.15 needs Microsoft.Extensions.* 11.0.1, not yet on nuget.org), and the
        // preview.14 agent's file logging reads FileSystem.CacheDirectory while the builder is still being
        // configured. On MAUI 11 the AppKit Essentials only reach that static after Build(), so it throws.
        builder.AddMauiDevFlowAgent(o => o.EnableFileLogging = false);
#endif

        return builder.Build();
    }
}
