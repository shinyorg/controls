using Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;
using Shiny.Maui.Controls.Media;

namespace Shiny.Maui.Controls.Media.Gtk;

/// <summary>
/// Hosts the <c>GtkPicture</c> that a <see cref="MediaSurface"/> renders into on the GTK4 head.
/// </summary>
/// <remarks>
/// A separate handler type from the one in the main package because the GTK backend has no target
/// framework of its own — it lives on the plain <c>net11.0</c> build, where the main package
/// deliberately registers nothing.
/// </remarks>
public class GtkMediaSurfaceHandler : GtkViewHandler<MediaSurface, global::Gtk.Picture>
{
    public static IPropertyMapper<MediaSurface, GtkMediaSurfaceHandler> Mapper =
        new PropertyMapper<MediaSurface, GtkMediaSurfaceHandler>(ViewMapper);

    public static CommandMapper<MediaSurface, GtkMediaSurfaceHandler> CommandMapper =
        new(ViewCommandMapper);

    public GtkMediaSurfaceHandler() : base(Mapper, CommandMapper)
    {
    }

    protected override global::Gtk.Picture CreatePlatformView()
    {
        var picture = global::Gtk.Picture.New();
        picture.SetCanShrink(true);
        return picture;
    }

    protected override void ConnectHandler(global::Gtk.Picture platformView)
    {
        base.ConnectHandler(platformView);
        this.VirtualView.AttachOutput(platformView);
    }

    protected override void DisconnectHandler(global::Gtk.Picture platformView)
    {
        (((IElementHandler)this).VirtualView as MediaSurface)?.DetachOutput();
        platformView.SetPaintable(null);
        base.DisconnectHandler(platformView);
    }
}
