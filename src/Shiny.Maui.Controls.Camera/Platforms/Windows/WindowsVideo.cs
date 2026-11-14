using Microsoft.Maui.Handlers;
using Shiny.Maui.Controls.Camera.Video;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using RectangleGeometry = Microsoft.UI.Xaml.Media.RectangleGeometry;
using RotateTransform = Microsoft.UI.Xaml.Media.RotateTransform;
using ScaleTransform = Microsoft.UI.Xaml.Media.ScaleTransform;
using TransformGroup = Microsoft.UI.Xaml.Media.TransformGroup;
using WBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using WPoint = Windows.Foundation.Point;
using WRect = Windows.Foundation.Rect;
using WStretch = Microsoft.UI.Xaml.Media.Stretch;
using WCanvas = Microsoft.UI.Xaml.Controls.Canvas;
using WGrid = Microsoft.UI.Xaml.Controls.Grid;
using WImage = Microsoft.UI.Xaml.Controls.Image;
using WImagingSource = Microsoft.UI.Xaml.Media.Imaging.SoftwareBitmapSource;

namespace Shiny.Maui.Controls.Camera;

// Windows: JPEG only, both ways. Media Foundation's H.264 encoder and decoder are reachable from WinRT only
// through MediaTranscoder / MediaStreamSource, which are file- and pipeline-shaped rather than frame-shaped, so
// this platform reports H.264 as unsupported and calls negotiate JPEG.
public partial class VideoEncoder
{
    private static partial bool PlatformIsSupported(VideoCodec codec) => codec == VideoCodec.Jpeg;

    private static partial IVideoEncoderSession? CreatePlatformSession(
        VideoEncoderSettings settings, int width, int height, Action<EncodedVideoFrame> sink)
        => settings.Codec == VideoCodec.Jpeg ? new WindowsJpegEncoderSession(settings, sink) : null;
}


public partial class RemoteVideoView
{
    private static partial bool PlatformCanDecode(VideoCodec codec) => codec == VideoCodec.Jpeg;
}


/// <summary>
/// JPEG through WIC's <see cref="BitmapEncoder"/>, scaling in the same pass. The frame is a managed BGRA copy
/// already (see <see cref="WindowsVideoFrame"/>), so it stays valid across the encoder's awaits.
/// </summary>
sealed class WindowsJpegEncoderSession(VideoEncoderSettings settings, Action<EncodedVideoFrame> sink) : IVideoEncoderSession
{
    double quality = settings.JpegQuality;

    public bool CanEncode(VideoEncoderSettings next, int width, int height)
    {
        if (next.Codec != VideoCodec.Jpeg)
            return false;

        this.quality = next.JpegQuality;
        return true;
    }

    public async ValueTask EncodeAsync(VideoFrame frame, int width, int height, TimeSpan timestamp, bool forceKeyFrame, CancellationToken ct)
    {
        if (frame is not WindowsVideoFrame windows)
            return;

        using var stream = new InMemoryRandomAccessStream();
        var properties = new BitmapPropertySet
        {
            { "ImageQuality", new BitmapTypedValue((float)this.quality, Windows.Foundation.PropertyType.Single) }
        };
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, properties);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            (uint)windows.Width,
            (uint)windows.Height,
            96,
            96,
            windows.Bgra);
        encoder.BitmapTransform.ScaledWidth = (uint)width;
        encoder.BitmapTransform.ScaledHeight = (uint)height;
        encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Linear;
        await encoder.FlushAsync();

        var bytes = new byte[stream.Size];
        using (var reader = new DataReader(stream.GetInputStreamAt(0)))
        {
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);
        }

        sink(new EncodedVideoFrame(
            VideoCodec.Jpeg, bytes, width, height, frame.Rotation, frame.IsMirrored, true, timestamp));
    }

    public void Dispose() { }
}


/// <summary>
/// The WinUI view behind a <see cref="RemoteVideoView"/>: a black, clipped grid holding a canvas, so the image
/// can be placed at its own pixel size and turned upright by a render transform without the grid measuring it.
/// </summary>
public sealed class RemoteVideoPlatformView : WGrid
{
    readonly WCanvas canvas = new();
    readonly WImage image = new() { Stretch = WStretch.Fill };
    readonly WImagingSource source = new();
    EncodedVideoFrame? shown;

    internal RemoteVideoPlatformView()
    {
        this.Background = new WBrush(Microsoft.UI.Colors.Black);
        this.image.Source = this.source;
        this.image.RenderTransformOrigin = new WPoint(0.5, 0.5);
        this.canvas.Children.Add(this.image);
        this.Children.Add(this.canvas);
        this.SizeChanged += (_, _) => this.Fit();
    }

    internal Aspect Aspect { get; set; } = Aspect.AspectFit;

    internal async Task ShowAsync(SoftwareBitmap bitmap, EncodedVideoFrame frame)
    {
        try
        {
            await this.source.SetBitmapAsync(bitmap);
            this.shown = frame with { Width = bitmap.PixelWidth, Height = bitmap.PixelHeight };
            this.image.Opacity = 1;
            this.Fit();
        }
        catch
        {
            // the frame raced the view's teardown
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    internal void ClearImage()
    {
        this.shown = null;
        this.image.Opacity = 0;
    }

    internal void Fit()
    {
        double vw = this.ActualWidth, vh = this.ActualHeight;
        this.Clip = new RectangleGeometry { Rect = new WRect(0, 0, Math.Max(0, vw), Math.Max(0, vh)) };
        if (this.shown is not { } frame || vw <= 0 || vh <= 0)
            return;

        var (sx, sy, rotation) = VideoScaling.Layout(frame.Width, frame.Height, frame.Rotation, vw, vh, this.Aspect);
        this.image.Width = frame.Width;
        this.image.Height = frame.Height;
        WCanvas.SetLeft(this.image, (vw - frame.Width) / 2);
        WCanvas.SetTop(this.image, (vh - frame.Height) / 2);

        // rotate first, then mirror and scale in upright space - a TransformGroup applies its children in order
        var group = new TransformGroup();
        group.Children.Add(new RotateTransform { Angle = rotation });
        group.Children.Add(new ScaleTransform { ScaleX = frame.IsMirrored ? -sx : sx, ScaleY = sy });
        this.image.RenderTransform = group;
    }
}


/// <summary>
/// The Windows <see cref="IRemoteVideoRenderer"/>: decodes JPEG with WIC on a worker, newest frame first, and
/// shows it through a <see cref="WImagingSource"/>. WIC's decoder is asynchronous only, so <see cref="Render"/>
/// queues rather than decodes; frames that arrive while one is decoding replace each other, which costs
/// nothing for a codec where every frame stands alone.
/// </summary>
sealed class WindowsRemoteVideoRenderer(RemoteVideoPlatformView view) : IRemoteVideoRenderer
{
    EncodedVideoFrame? pending;
    int working;
    volatile bool disposed;

    public void Render(EncodedVideoFrame frame)
    {
        if (frame.Codec != VideoCodec.Jpeg || this.disposed)
            return;

        Interlocked.Exchange(ref this.pending, frame);
        if (Interlocked.Exchange(ref this.working, 1) == 0)
            _ = Task.Run(this.DecodeLoop);
    }

    async Task DecodeLoop()
    {
        while (true)
        {
            while (!this.disposed && Interlocked.Exchange(ref this.pending, null) is { } frame)
            {
                try
                {
                    using var stream = new InMemoryRandomAccessStream();
                    using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                    {
                        writer.WriteBytes(frame.Data.ToArray());
                        await writer.StoreAsync();
                        await writer.FlushAsync();
                    }

                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                    if (!view.DispatcherQueue.TryEnqueue(() => _ = view.ShowAsync(bitmap, frame)))
                        bitmap.Dispose();
                }
                catch
                {
                    // one bad frame; the next stands alone
                }
            }

            Interlocked.Exchange(ref this.working, 0);

            // a frame that landed between the last take and the release would otherwise sit unrendered
            if (this.disposed || Volatile.Read(ref this.pending) is null || Interlocked.Exchange(ref this.working, 1) == 1)
                return;
        }
    }

    public void Clear()
    {
        Interlocked.Exchange(ref this.pending, null);
        view.DispatcherQueue.TryEnqueue(view.ClearImage);
    }

    public void Dispose() => this.disposed = true;
}


public partial class RemoteVideoViewHandler : ViewHandler<RemoteVideoView, RemoteVideoPlatformView>
{
    WindowsRemoteVideoRenderer? renderer;

    protected override RemoteVideoPlatformView CreatePlatformView() => new();

    protected override void ConnectHandler(RemoteVideoPlatformView platformView)
    {
        base.ConnectHandler(platformView);
        this.renderer = new WindowsRemoteVideoRenderer(platformView);
        this.VirtualView.SetRenderer(this.renderer);
    }

    protected override void DisconnectHandler(RemoteVideoPlatformView platformView)
    {
        this.MaybeVirtualView?.SetRenderer(null);
        this.renderer?.Dispose();
        this.renderer = null;
        platformView.ClearImage();
        base.DisconnectHandler(platformView);
    }

    static partial void MapAspect(RemoteVideoViewHandler handler, RemoteVideoView view)
    {
        handler.PlatformView.Aspect = view.Aspect;
        handler.PlatformView.Fit();
    }
}
