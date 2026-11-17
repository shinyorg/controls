using Shiny.Maui.Controls.Media;
#if ANDROID || IOS || MACCATALYST || WINDOWS || MACOS
using Microsoft.Maui.Hosting;
#endif

namespace Shiny;

public static class MediaMauiAppBuilderExtensions
{
    /// <summary>
    /// Register the Shiny <see cref="MediaElement"/> and <see cref="VideoText"/> handlers and the platform player backend. Call
    /// alongside <c>UseShinyControls()</c> in your MAUI program.
    /// </summary>
    /// <remarks>
    /// On the plain <c>net11.0</c> target — which is what the GTK4 Linux head builds against — this is a
    /// no-op: there is no Linux target framework, so that backend ships separately. Call
    /// <c>UseShinyMediaElementGtk()</c> from <c>Shiny.Maui.Controls.MediaElement.Linux</c> there instead.
    /// </remarks>
    public static MauiAppBuilder UseShinyMediaElement(this MauiAppBuilder builder)
    {
#if ANDROID || IOS || MACCATALYST || WINDOWS || MACOS
        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<MediaSurface, MediaSurfaceHandler>();
#if ANDROID || IOS || MACCATALYST || MACOS
            handlers.AddHandler<VideoTextSurface, VideoTextHandler>();
#endif
        });

#if IOS || MACCATALYST || MACOS
        MediaPlayerBackends.Factory = () => new AppleMediaPlayerBackend();
#elif ANDROID
        MediaPlayerBackends.Factory = () => new AndroidMediaPlayerBackend();
#elif WINDOWS
        MediaPlayerBackends.Factory = () => new WindowsMediaPlayerBackend();
#endif
#endif
        return builder;
    }
}
