using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.MacOS.Handlers;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// Renders an <c>SKCanvasView</c> on the macOS AppKit head, where SkiaSharp itself has no handler.
/// </summary>
/// <remarks>
/// <para>
/// <c>SkiaSharp.Views.Maui.Core</c> multi-targets iOS, Mac Catalyst, Android and Windows but not
/// <c>net10.0-macos</c>, so AppKit resolves its plain <c>net10.0</c> build. That build's
/// <c>SKCanvasViewHandler</c> compiles but its <c>CreatePlatformView()</c> throws
/// <see cref="NotImplementedException"/> — which the macOS <c>ShellHandler</c> swallows, leaving the
/// page blank with nothing in the log beyond "The method or operation is not implemented."
/// </para>
/// <para>
/// Registered by <see cref="MauiAppBuilderExtensions.UseShinyOffice"/> <em>after</em>
/// <c>UseSkiaSharp()</c>, so it replaces the stock registration rather than competing with it.
/// Nothing here is Office-specific: any <c>SKCanvasView</c> in the app renders once this is in.
/// </para>
/// </remarks>
public class MacOSSKCanvasViewHandler : MacOSViewHandler<ISKCanvasView, SkiaCanvasNSView>
{
    /// <summary>
    /// Chained onto <see cref="ViewHandler.ViewMapper"/> rather than onto the stock
    /// <c>SKCanvasViewHandler</c>'s: the macOS platform package installs its AppKit implementations of
    /// background, clip, transform and the rest into that shared mapper, so inheriting it is what
    /// makes an ordinary MAUI property work on this view.
    /// </summary>
    public static readonly IPropertyMapper<ISKCanvasView, MacOSSKCanvasViewHandler> Mapper =
        new PropertyMapper<ISKCanvasView, MacOSSKCanvasViewHandler>(ViewHandler.ViewMapper)
        {
            [nameof(ISKCanvasView.IgnorePixelScaling)] = MapIgnorePixelScaling,
            [nameof(ISKCanvasView.EnableTouchEvents)] = MapEnableTouchEvents
        };

    public static readonly CommandMapper<ISKCanvasView, MacOSSKCanvasViewHandler> CommandMapper =
        new(ViewHandler.ViewCommandMapper)
        {
            [nameof(ISKCanvasView.InvalidateSurface)] = MapInvalidateSurface
        };

    public MacOSSKCanvasViewHandler() : base(Mapper, CommandMapper)
    {
    }

    public MacOSSKCanvasViewHandler(IPropertyMapper? mapper, CommandMapper? commands)
        : base(mapper ?? Mapper, commands ?? CommandMapper)
    {
    }

    protected override SkiaCanvasNSView CreatePlatformView() => new();

    protected override void ConnectHandler(SkiaCanvasNSView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.PaintSurface += this.OnPaintSurface;
        platformView.Touch += this.OnTouch;
        platformView.CanvasSizeChanged += this.OnCanvasSizeChanged;
    }

    protected override void DisconnectHandler(SkiaCanvasNSView platformView)
    {
        platformView.PaintSurface -= this.OnPaintSurface;
        platformView.Touch -= this.OnTouch;
        platformView.CanvasSizeChanged -= this.OnCanvasSizeChanged;

        // Release the backing store now rather than whenever the NSView happens to be finalized.
        platformView.ReleaseSurface();
        base.DisconnectHandler(platformView);
    }

    static void MapIgnorePixelScaling(MacOSSKCanvasViewHandler handler, ISKCanvasView view)
    {
        handler.PlatformView.IgnorePixelScaling = view.IgnorePixelScaling;
        handler.PlatformView.NeedsDisplay = true;
    }

    static void MapEnableTouchEvents(MacOSSKCanvasViewHandler handler, ISKCanvasView view)
        => handler.PlatformView.EnableTouchEvents = view.EnableTouchEvents;

    static void MapInvalidateSurface(MacOSSKCanvasViewHandler handler, ISKCanvasView view, object? args)
        => handler.PlatformView.NeedsDisplay = true;

    void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        => this.VirtualView?.OnPaintSurface(e);

    void OnTouch(object? sender, SKTouchEventArgs e)
        => this.VirtualView?.OnTouch(e);

    void OnCanvasSizeChanged(object? sender, SKSizeI size)
        => this.VirtualView?.OnCanvasSizeChanged(size);
}
