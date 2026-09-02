using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shiny.Maui.Controls.Camera;
using Shiny.Maui.Controls.Camera.Media;
#if ANDROID || IOS || MACCATALYST || WINDOWS || MACOS
using Microsoft.Maui.Hosting;
#endif

namespace Shiny;

public static class CameraMauiAppBuilderExtensions
{
    /// <summary>
    /// Register the Shiny <see cref="CameraView"/> handler and the <see cref="IMediaService"/> that drives
    /// the modal camera. Call alongside <c>UseShinyControls()</c> in your MAUI program. Analyzer packages
    /// (Barcode/Face/Motion/OCR/Documents) are assigned to the single <see cref="CameraView.Analyzer"/>
    /// property at the call site — no separate registration needed.
    /// </summary>
    /// <param name="builder">The app builder.</param>
    /// <param name="configureMedia">
    /// Service-wide media defaults — the compression rate, maximum dimension and output format every
    /// <see cref="IMediaService"/> call inherits, plus a hook applied to every modal's options.
    /// </param>
    public static MauiAppBuilder UseShinyCamera(this MauiAppBuilder builder, Action<MediaServiceOptions>? configureMedia = null)
    {
#if ANDROID || IOS || MACCATALYST || WINDOWS || MACOS
        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<CameraView, CameraViewHandler>();
        });
#endif
        var options = new MediaServiceOptions();
        configureMedia?.Invoke(options);

        // TryAdd so an app that wants its own IMediaService (a fake in tests, a wrapper that logs) can
        // register it before or after this call and win either way
        builder.Services.TryAddSingleton(options);
        builder.Services.TryAddSingleton<IMediaService, MediaService>();
        return builder;
    }
}
