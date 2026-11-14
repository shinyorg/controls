using Microsoft.Maui.Handlers;

namespace Shiny.Maui.Controls.Media;

// macOS AppKit head (dotnet/maui-labs). AVFoundation itself is solid here; the MAUI host is preview
// quality, so layout edge cases may need on-device tuning.
public partial class MediaSurfaceHandler : ViewHandler<MediaSurface, MediaPlayerNSView>
{
    protected override MediaPlayerNSView CreatePlatformView() => new();

    // MAUI arranges nothing for a custom view on net10.0-macos; without this the view stays 0x0 — see MediaPlayerNSView
    public override void PlatformArrange(Rect frame)
    {
        base.PlatformArrange(frame);
        this.PlatformView?.ApplyArrangedFrame(frame);
    }

    protected override void ConnectHandler(MediaPlayerNSView platformView)
    {
        base.ConnectHandler(platformView);
        this.VirtualView.AttachOutput(platformView);
    }

    protected override void DisconnectHandler(MediaPlayerNSView platformView)
    {
        this.MaybeVirtualView?.DetachOutput();
        base.DisconnectHandler(platformView);
    }
}
