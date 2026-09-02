using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;
using Sample.Features.Diagrams;
using Sample.Features.Docking;
using Sample.Features.FloatingPanel;
using Sample.Features.Flyout;
using Sample.Features.Scheduler;
using Sample.Features.TableView;
using Shiny;
using Shiny.Maui.Controls.Office;
using Shiny.Maui.Controls.QuickEntry;
using Shiny.Maui.Controls.Scheduler;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent;
#endif

namespace Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .AddAudio()
            // Required by Shiny.Maui.Controls.Office: the spreadsheet grid paints onto a Skia surface.
            // UseShinyOffice registers SkiaSharp and, on the AppKit head, the canvas SkiaSharp omits.
            .UseShinyOffice()
            .UseShinyControls(cfg =>
            {
                cfg.SetCustomFeedback<MyCustomFeedbackService>(); // haptic is installed by default, but we want more fun
                cfg.AddDefaultMauiControlFeedback();

                // Deliberately tighter than the default of four so the ShinyImage page can actually
                // show the queued state - with a normal budget the thirty-image grid never has
                // enough cells waiting to see one.
                cfg.ConfigureImages(o => o.MaxConcurrentDownloads = 2);

                cfg.ConfigureQuickEntry(o =>
                {
                    o.HotKey = OperatingSystem.IsMacOS() ? "Cmd+Opt+Space" : "Ctrl+Alt+Space";
                    o.Placement = QuickEntryPlacement.TopCenter;
                    o.ScreenGlow = ScreenGlowTrigger.WhileBusy;
                });

            })
            .UseShinyCamera(media =>
            {
                // house style for every IMediaService photo — call sites override per call when they need to
                media.CompressionQuality = 85;
                media.MaxDimension = 2048;
            })
            .UseShinyMediaElement()
            .UseTrayIcon()
            .UseFileDrop()
            .UseDesktopQuickEntry()
            .UseShinyDocking()
            .AddDockPanel<SolutionExplorerPanel>("solution-explorer", "Solution Explorer", "📁")
            .AddDockPanel<OutputPanel>("output", "Output", "🖥️")
            .AddDockPanel<PropertiesPanel>("properties", "Properties", "🔧")
            .AddDockPanel<EditorPanel>("editor", icon: "📄")
            .UseShinyShell(x => x
                .AddGeneratedMaps()
                .Add<MinimizedSheetStandalonePage, MinimizedSheetViewModel>(registerRoute: false)
            )
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if IOS
        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<Shell, Sample.Platforms.iOS.SolidTabBarRenderer>();
        });
#endif
        builder.Services.AddSpeechServices();
        builder.Services.AddSingleton<AppSettings>();
        builder.Services.AddSingleton<Sample.Features.Chat.InMemoryChatSessionProvider>();

        builder.Services.AddTransient<MusicBrowsePage>();
        builder.Services.AddTransient<MusicLibraryPage>();
        builder.Services.AddTransient<StylingPage>();
        builder.Services.AddTransient<BasicFlowchartPage>();
        builder.Services.AddTransient<DirectionsPage>();
        builder.Services.AddTransient<ThemesPage>();
        builder.Services.AddTransient<SubgraphsPage>();
        builder.Services.AddTransient<InteractiveEditorPage>();
        builder.Services.AddTransient<FlyoutDrawerPage>();
        builder.Services.AddSingleton<ISchedulerEventProvider, SampleSchedulerProvider>();

        // shared app-session list of documents lifted by the camera's document analyzers
        builder.Services.AddSingleton<Sample.Features.Camera.DocumentSessionStore>();

        // the "AI Document" camera detector ships frames to an IChatClient; the sample registers an offline
        // stand-in so it runs without keys. Swap in a real vision client (Azure OpenAI / OpenAI / Ollama) here.
        builder.Services.AddSingleton<Microsoft.Extensions.AI.IChatClient, Sample.Features.Camera.SampleVisionChatClient>();
        // offline stand-in for an image-to-image model, so the "AI stylize" capture effect demos without a key
        builder.Services.AddSingleton<Microsoft.Extensions.AI.IImageGenerator, Sample.Features.Camera.SampleImageGenerator>();

#if DEBUG
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddDebug();
        builder.AddMauiDevFlowAgent();
#endif

        var app = builder.Build();
        return app;
    }
}
