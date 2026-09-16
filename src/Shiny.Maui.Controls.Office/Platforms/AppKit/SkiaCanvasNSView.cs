using System.Runtime.InteropServices;
using AppKit;
using CoreGraphics;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// An <see cref="NSView"/> that hands out a Skia surface to draw into — the AppKit half of
/// <see cref="MacOSSKCanvasViewHandler"/>, and the reason the Office controls render on
/// <c>net10.0-macos</c> at all.
/// </summary>
/// <remarks>
/// <para>
/// <c>SkiaSharp.Views.Maui</c> ships no <c>net10.0-macos</c> asset, so the AppKit head resolves its
/// plain <c>net10.0</c> build — where <c>SKCanvasViewHandler.CreatePlatformView()</c> throws
/// <see cref="NotImplementedException"/>. Every Office control is an <c>SKCanvasView</c>, so all five
/// of them came up blank on that head. This is the missing platform view.
/// </para>
/// <para>
/// It renders CPU-side rather than through Metal: an <see cref="SKSurface"/> over a raw pixel buffer,
/// blitted into the view's <see cref="CGContext"/> as a <see cref="CGImage"/>. A document or a
/// spreadsheet repaints on scroll and on keystrokes rather than every frame, so the copy is not worth
/// a GPU context — and a raster surface behaves identically to the one the iOS and Android heads hand
/// the painter, which is what keeps the rendering shared.
/// </para>
/// </remarks>
public sealed class SkiaCanvasNSView : NSView
{
    // The buffer is unmanaged and long-lived rather than a byte[] per frame: CGDataProvider wraps it
    // without copying, and CoreGraphics may hold the provider past the end of DrawRect. Reusing one
    // allocation also keeps a scroll from churning multi-megabyte arrays through the GC.
    IntPtr pixels;
    int byteCount;
    SKSurface? surface;
    SKImageInfo info;

    public SkiaCanvasNSView()
    {
        this.WantsLayer = true;
    }

    /// <summary>Raised with a surface to paint, in device pixels.</summary>
    public event EventHandler<SKPaintSurfaceEventArgs>? PaintSurface;

    /// <summary>Raised for mouse and wheel input, with locations in device pixels.</summary>
    public event EventHandler<SKTouchEventArgs>? Touch;

    /// <summary>Raised when the pixel size of the surface changes.</summary>
    public event EventHandler<SKSizeI>? CanvasSizeChanged;

    /// <summary>When false the surface is sized in device pixels; when true, in layout units.</summary>
    public bool IgnorePixelScaling { get; set; }

    /// <summary>Gates <see cref="Touch"/>. Mouse events are ignored entirely while false.</summary>
    public bool EnableTouchEvents { get; set; }

    /// <summary>The current surface size, in pixels.</summary>
    public SKSizeI CanvasSize => this.info.Size;

    /// <summary>
    /// Skia's origin is top-left and AppKit's is bottom-left. Flipping the view rather than
    /// transforming every paint keeps hit-testing, mouse coordinates and the painter in one
    /// coordinate space.
    /// </summary>
    public override bool IsFlipped => true;

    public override void DrawRect(CGRect dirtyRect)
    {
        base.DrawRect(dirtyRect);

        var context = NSGraphicsContext.CurrentContext?.CGContext;
        if (context is null)
            return;

        var scale = this.IgnorePixelScaling ? 1.0 : (this.Window?.BackingScaleFactor ?? 1.0);
        var width = (int)Math.Round(this.Bounds.Width * scale);
        var height = (int)Math.Round(this.Bounds.Height * scale);
        if (width <= 0 || height <= 0)
            return;

        if (!this.EnsureSurface(width, height))
            return;

        var canvas = this.surface!.Canvas;
        canvas.Clear(SKColors.Transparent);
        this.PaintSurface?.Invoke(this, new SKPaintSurfaceEventArgs(this.surface, this.info));
        canvas.Flush();

        using var provider = new CGDataProvider(this.pixels, this.byteCount, ownBuffer: false);
        using var colorSpace = CGColorSpace.CreateDeviceRGB();
        using var image = new CGImage(
            this.info.Width,
            this.info.Height,
            bitsPerComponent: 8,
            bitsPerPixel: 32,
            bytesPerRow: this.info.RowBytes,
            colorSpace,
            CGBitmapFlags.PremultipliedLast | CGBitmapFlags.ByteOrder32Big,
            provider,
            decode: null,
            shouldInterpolate: false,
            CGColorRenderingIntent.Default
        );

        // DrawImage places the image bottom-up, which in a flipped view lands upside down. Undo the
        // flip for the blit alone, so the rest of the view keeps Skia's coordinate space.
        context.SaveState();
        context.TranslateCTM(0, this.Bounds.Height);
        context.ScaleCTM(1, -1);
        context.DrawImage(new CGRect(0, 0, this.Bounds.Width, this.Bounds.Height), image);
        context.RestoreState();
    }

    bool EnsureSurface(int width, int height)
    {
        if (this.surface is not null && this.info.Width == width && this.info.Height == height)
            return true;

        this.ReleaseSurface();

        // Rgba8888 premultiplied is what CGBitmapFlags.PremultipliedLast | ByteOrder32Big describes,
        // so the blit is a straight reinterpret rather than a channel swizzle.
        this.info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        this.byteCount = this.info.BytesSize;
        this.pixels = Marshal.AllocHGlobal(this.byteCount);
        this.surface = SKSurface.Create(this.info, this.pixels, this.info.RowBytes);

        if (this.surface is null)
        {
            this.ReleaseSurface();
            return false;
        }

        this.CanvasSizeChanged?.Invoke(this, this.info.Size);
        return true;
    }

    /// <summary>
    /// Frees the surface and its unmanaged pixel buffer. The next paint allocates them again, so this
    /// is safe to call on a view that may come back on screen.
    /// </summary>
    internal void ReleaseSurface()
    {
        this.surface?.Dispose();
        this.surface = null;

        if (this.pixels != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(this.pixels);
            this.pixels = IntPtr.Zero;
        }

        this.byteCount = 0;
    }

    // ---------------------------------------------------------------------------------------------
    // Input
    // ---------------------------------------------------------------------------------------------

    public override void MouseDown(NSEvent theEvent) => this.RaiseTouch(theEvent, SKTouchAction.Pressed, SKMouseButton.Left, inContact: true);

    public override void MouseDragged(NSEvent theEvent) => this.RaiseTouch(theEvent, SKTouchAction.Moved, SKMouseButton.Left, inContact: true);

    public override void MouseUp(NSEvent theEvent) => this.RaiseTouch(theEvent, SKTouchAction.Released, SKMouseButton.Left, inContact: false);

    public override void RightMouseDown(NSEvent theEvent) => this.RaiseTouch(theEvent, SKTouchAction.Pressed, SKMouseButton.Right, inContact: true);

    public override void RightMouseDragged(NSEvent theEvent) => this.RaiseTouch(theEvent, SKTouchAction.Moved, SKMouseButton.Right, inContact: true);

    public override void RightMouseUp(NSEvent theEvent) => this.RaiseTouch(theEvent, SKTouchAction.Released, SKMouseButton.Right, inContact: false);

    public override void ScrollWheel(NSEvent theEvent)
    {
        if (!this.EnableTouchEvents)
        {
            base.ScrollWheel(theEvent);
            return;
        }

        // A trackpad reports point deltas and a wheel reports line deltas. Scaling the line case to
        // roughly a point-per-notch keeps one turn of a wheel and one flick of a trackpad in the same
        // ballpark, which is all the consumers of WheelDelta need.
        var delta = theEvent.HasPreciseScrollingDeltas
            ? theEvent.ScrollingDeltaY
            : theEvent.ScrollingDeltaY * LinesToPoints;

        this.RaiseTouch(theEvent, SKTouchAction.WheelChanged, SKMouseButton.Unknown, inContact: false, (int)Math.Round(delta));
    }

    const double LinesToPoints = 16;

    void RaiseTouch(NSEvent theEvent, SKTouchAction action, SKMouseButton button, bool inContact, int wheelDelta = 0)
    {
        if (!this.EnableTouchEvents)
            return;

        var point = this.ConvertPointFromView(theEvent.LocationInWindow, null);
        var scale = this.IgnorePixelScaling ? 1.0 : (this.Window?.BackingScaleFactor ?? 1.0);
        var location = new SKPoint((float)(point.X * scale), (float)(point.Y * scale));

        // One pointer, always: AppKit has no multi-touch for a view like this, and the Office
        // controllers key their drags on the id.
        var args = new SKTouchEventArgs(0, action, button, SKTouchDeviceType.Mouse, location, inContact, wheelDelta);
        this.Touch?.Invoke(this, args);
    }

    protected override void Dispose(bool disposing)
    {
        // The pixel buffer is AllocHGlobal memory - window-sized, tens of megabytes on a Retina display -
        // that no finalizer knows about. It used to be freed only on an explicit Dispose(true), which
        // nothing calls for a view MAUI discards, so every collected canvas leaked its whole buffer.
        // On the finalizer path the SKSurface has its own finalizer; only the raw buffer is ours to free.
        if (disposing)
        {
            this.ReleaseSurface();
        }
        else if (this.pixels != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(this.pixels);
            this.pixels = IntPtr.Zero;
        }

        base.Dispose(disposing);
    }
}
