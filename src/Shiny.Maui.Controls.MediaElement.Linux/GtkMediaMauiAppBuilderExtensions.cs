using Microsoft.Maui.Hosting;
using Shiny.Maui.Controls.Media;
using Shiny.Maui.Controls.Media.Gtk;

namespace Shiny;

public static class GtkMediaMauiAppBuilderExtensions
{
    /// <summary>
    /// Register the Shiny <see cref="MediaElement"/> for the GTK4 Linux head. Call this <b>instead of</b>
    /// <c>UseShinyMediaElement()</c>, which is a no-op on the plain <c>net11.0</c> target the GTK host
    /// builds against.
    /// </summary>
    /// <remarks>
    /// Decoding comes from GStreamer through GTK's media backend, so the machine needs
    /// <c>gtk4-media-gstreamer</c> (Fedora/Arch) or <c>libgtk-4-media-gstreamer</c> (Debian/Ubuntu) plus
    /// the codec plugins for whatever you play. Without it the control lays out fine and reports a load
    /// error through <c>MediaFailed</c>.
    /// </remarks>
    public static MauiAppBuilder UseShinyMediaElementGtk(this MauiAppBuilder builder)
    {
        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<MediaSurface, GtkMediaSurfaceHandler>();
        });

        MediaPlayerBackends.Factory = () => new GtkMediaPlayerBackend();
        return builder;
    }
}
