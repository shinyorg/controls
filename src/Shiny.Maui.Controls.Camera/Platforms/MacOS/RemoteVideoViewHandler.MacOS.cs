using AppKit;
using CoreAnimation;
using CoreGraphics;
using Microsoft.Maui.Handlers;

namespace Shiny.Maui.Controls.Camera;

/// <summary>
/// The AppKit view behind a <see cref="RemoteVideoView"/> on macOS: a layer-backed, clipping view whose layer
/// hosts the frame layer.
/// </summary>
/// <remarks>
/// Sized from <see cref="RemoteVideoViewHandler.PlatformArrange"/> as well as its own layout, for the same reason
/// as the camera's host view: the AppKit backend leaves a platform view's bounds at zero, and a frame layer
/// fitted to zero bounds draws nothing.
/// </remarks>
public sealed class RemoteVideoPlatformView : NSView
{
    internal RemoteVideoPlatformView()
    {
        this.WantsLayer = true;
        this.Layer ??= new CALayer();
        this.Layer.BackgroundColor = new CGColor(0, 0, 0);
        this.Layer.MasksToBounds = true; // AspectFill crops here
        this.Content = new RemoteVideoLayer(this.Layer, yAxisUp: !this.IsFlipped);
    }

    internal RemoteVideoLayer Content { get; }

    public override void Layout()
    {
        base.Layout();
        this.Content.Layout();
    }
}


public partial class RemoteVideoViewHandler : ViewHandler<RemoteVideoView, RemoteVideoPlatformView>
{
    AppleRemoteVideoRenderer? renderer;

    protected override RemoteVideoPlatformView CreatePlatformView() => new();

    protected override void ConnectHandler(RemoteVideoPlatformView platformView)
    {
        base.ConnectHandler(platformView);
        this.renderer = new AppleRemoteVideoRenderer(platformView.Content, () => this.MaybeVirtualView?.OnDecoderLostSync());
        this.VirtualView.SetRenderer(this.renderer);
    }

    protected override void DisconnectHandler(RemoteVideoPlatformView platformView)
    {
        this.MaybeVirtualView?.SetRenderer(null);
        this.renderer?.Dispose();
        this.renderer = null;
        platformView.Content.Clear();
        base.DisconnectHandler(platformView);
    }

    /// <inheritdoc/>
    public override void PlatformArrange(Rect frame)
    {
        base.PlatformArrange(frame);
        // The whole frame, not just the size: MAUI's generic ViewHandler arranges nothing on net11.0-macos, so a
        // view that only sets its size stays at (0,0) - stacked on its siblings at the top of the page. The AppKit
        // backend's containers are flipped, so MAUI's top-left rectangle is the frame as-is.
        if (this.PlatformView is { } host && frame is { Width: > 0, Height: > 0 })
        {
            var wanted = new CGRect(frame.X, frame.Y, frame.Width, frame.Height);
            if (!Near(host.Frame, wanted))
                host.Frame = wanted;
        }
        this.PlatformView?.Content.Layout();
    }


    static bool Near(CGRect a, CGRect b)
        => Math.Abs(a.X - b.X) < 0.5 && Math.Abs(a.Y - b.Y) < 0.5
           && Math.Abs(a.Width - b.Width) < 0.5 && Math.Abs(a.Height - b.Height) < 0.5;

    static partial void MapAspect(RemoteVideoViewHandler handler, RemoteVideoView view)
    {
        handler.PlatformView.Content.Aspect = view.Aspect;
        handler.PlatformView.Content.Layout();
    }
}
