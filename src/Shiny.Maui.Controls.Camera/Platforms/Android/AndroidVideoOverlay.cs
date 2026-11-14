using Android.Content;
using Android.Runtime;
using VideoFrame = AndroidX.Camera.Effects.Frame;
using MauiGraphics = Microsoft.Maui.Graphics;
using PlatformCanvas = Microsoft.Maui.Graphics.Platform.PlatformCanvas;

namespace Shiny.Maui.Controls.Camera;

// The OverlayEffect OnDrawListener (an androidx.arch.core.util.Function<Frame, Boolean>). Invoked per encoded
// frame on the overlay HandlerThread; wraps the frame's transparent android.graphics.Canvas with a MAUI
// Graphics PlatformCanvas and calls the user's IVideoOverlayRenderer. Drawing is in output-buffer pixel space
// (frame.Size); the frame's SensorToBufferTransform is available if per-device coordinate mapping is needed.
sealed class OverlayDrawListener : Java.Lang.Object, AndroidX.Arch.Core.Util.IFunction
{
    readonly IVideoOverlayRenderer overlay;
    readonly Context context;
    readonly CameraFacing facing;
    long startNanos;
    long frameIndex;

    public OverlayDrawListener(Context context, CameraFacing facing, IVideoOverlayRenderer overlay)
    {
        this.context = context;
        this.facing = facing;
        this.overlay = overlay;
    }

    public Java.Lang.Object Apply(Java.Lang.Object? p0)
    {
        try
        {
            if (p0.JavaCast<VideoFrame>() is not { } frame)
                return Java.Lang.Boolean.True!;

            var size = frame.Size;
            int w = size?.Width ?? 0, h = size?.Height ?? 0;
            if (w == 0 || h == 0)
                return Java.Lang.Boolean.True!;

            var ts = frame.TimestampNanos;
            if (this.startNanos == 0)
                this.startNanos = ts;
            var elapsed = TimeSpan.FromTicks(Math.Max(0, ts - this.startNanos) / 100); // 100ns per tick

            var overlayContext = new VideoOverlayContext(elapsed, this.frameIndex++, w, h, this.facing);
            var canvas = new PlatformCanvas(this.context) { Canvas = frame.OverlayCanvas, DisplayScale = 1 };

            // A renderer that caches its own output describes itself as images, and drawing those is both
            // cheaper here and the same code path the caller uses on Apple. Unlike Apple this is not a
            // change of pipeline — OverlayCanvas is already hardware-composited, so what it saves is the
            // renderer's own per-frame drawing, not a CPU blend. Null means "draw me the ordinary way".
            if (this.overlay is ICompositedVideoOverlayRenderer composited
                && composited.GetLayers(overlayContext) is { } layers)
            {
                foreach (var layer in layers)
                {
                    canvas.DrawImage(
                        layer.Image,
                        layer.Destination.X,
                        layer.Destination.Y,
                        layer.Destination.Width,
                        layer.Destination.Height);
                }
                return Java.Lang.Boolean.True!;
            }

            this.overlay.DrawOverlay(canvas, new MauiGraphics.RectF(0, 0, w, h), overlayContext);
        }
        catch
        {
            // never let a managed exception escape into the native draw callback
        }
        return Java.Lang.Boolean.True!;
    }
}


// androidx.core.util.Consumer<Throwable> for OverlayEffect construction/runtime errors. Note the binding
// surfaces java.lang.Throwable as a System.Exception (Java.Lang.Throwable : Exception), not a Java.Lang.Object
// subclass, so we read its description via ToString() rather than pattern-matching the incoming peer.
sealed class OverlayErrorConsumer : Java.Lang.Object, AndroidX.Core.Util.IConsumer
{
    readonly Action<string> onError;
    public OverlayErrorConsumer(Action<string> onError) => this.onError = onError;

    public void Accept(Java.Lang.Object? t)
        => this.onError(t?.ToString() ?? "unknown video overlay error");
}
