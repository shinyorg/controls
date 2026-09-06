using Microsoft.Maui.Handlers;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using WGrid = Microsoft.UI.Xaml.Controls.Grid;
using WImage = Microsoft.UI.Xaml.Controls.Image;
using WImagingSource = Microsoft.UI.Xaml.Media.Imaging.SoftwareBitmapSource;

namespace Shiny.Maui.Controls.Camera;

// Windows (WinUI 3). CaptureElement does not exist in WinUI 3, so preview and analysis are both driven
// from one MediaFrameReader: each frame is shown via a SoftwareBitmapSource and (when analyzers are
// present) wrapped into a WindowsCameraFrame for the pipeline.
public partial class CameraViewHandler : ViewHandler<CameraView, WGrid>, ICameraViewController
{
    MediaCapture? capture;
    MediaFrameReader? reader;
    WImage? previewImage;
    WImagingSource? previewSource;
    SoftwareBitmap? latest;
    LowLagMediaRecording? recording;
    bool starting;
    readonly object latestGate = new();

    protected override WGrid CreatePlatformView()
    {
        this.previewSource = new WImagingSource();
        this.previewImage = new WImage
        {
            Source = this.previewSource,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
        };
        var grid = new WGrid();
        grid.Children.Add(this.previewImage);
        return grid;
    }

    protected override void ConnectHandler(WGrid platformView)
    {
        base.ConnectHandler(platformView);
        this.InitPipeline();
        if (this.VirtualView.IsActive)
            _ = this.StartAsync();
    }

    protected override void DisconnectHandler(WGrid platformView)
    {
        this.TeardownPipeline();
        _ = this.StopAsync();
        base.DisconnectHandler(platformView);
    }


    public async Task<bool> RequestPermissionAsync(CancellationToken ct = default)
    {
        var status = await MainThread.InvokeOnMainThreadAsync(
            () => Permissions.RequestAsync<Permissions.Camera>());
        return status == PermissionStatus.Granted;
    }


    public async Task<IReadOnlyList<CameraInfo>> GetAvailableCamerasAsync(CancellationToken ct = default)
    {
        var devices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(
            Windows.Devices.Enumeration.DeviceClass.VideoCapture);

        var list = new List<CameraInfo>();
        foreach (var d in devices)
        {
            var facing = d.EnclosureLocation?.Panel switch
            {
                Windows.Devices.Enumeration.Panel.Front => CameraFacing.Front,
                Windows.Devices.Enumeration.Panel.Back => CameraFacing.Back,
                _ => CameraFacing.External
            };
            list.Add(new CameraInfo(d.Id, d.Name, facing, d.IsDefault));
        }
        return list;
    }


    public async Task StartAsync(CancellationToken ct = default)
    {
        if (this.capture != null || this.starting)
            return;

        this.starting = true;
        // Built locally and only published to the field once it is fully initialized. MediaCapture throws
        // 0xC00D36B6 ("needs to be initialized") from VideoDeviceController until InitializeAsync completes,
        // and the property mappers below run synchronously while this is still in flight - publishing early
        // means MapTorch/MapZoom can hit that window. On Windows a throw there escapes Shell's page-creation
        // path and wedges Shell navigation for the rest of the session.
        MediaCapture? pending = null;
        try
        {
            pending = new MediaCapture();
            var settings = new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu
            };
            if (!string.IsNullOrEmpty(this.VirtualView.CameraId))
                settings.VideoDeviceId = this.VirtualView.CameraId;
            await pending.InitializeAsync(settings);

            var source = pending.FrameSources.Values
                .FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color);
            if (source == null)
            {
                pending.Dispose();
                this.MaybeVirtualView?.OnCameraError("No color camera source found");
                return;
            }

            this.reader = await pending.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            this.reader.FrameArrived += this.OnFrameArrived;
            await this.reader.StartAsync();

            this.capture = pending;
            pending = null;

            // the mappers that need a device controller were no-ops while we were starting up; apply them now
            MapTorch(this, this.VirtualView);
            MapZoom(this, this.VirtualView);
        }
        catch (Exception ex)
        {
            pending?.Dispose();
            this.MaybeVirtualView?.OnCameraError("Failed to start camera", ex);
        }
        finally
        {
            this.starting = false;
        }
    }


    public async Task StopAsync(CancellationToken ct = default)
    {
        try
        {
            if (this.reader != null)
            {
                this.reader.FrameArrived -= this.OnFrameArrived;
                await this.reader.StopAsync();
                this.reader.Dispose();
                this.reader = null;
            }
            this.capture?.Dispose();
            this.capture = null;
            lock (this.latestGate)
            {
                this.latest?.Dispose();
                this.latest = null;
            }
        }
        catch { /* tearing down */ }
    }


    void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        using var frame = sender.TryAcquireLatestFrame();
        var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
        if (bitmap == null)
            return;

        // pipeline copy (synchronous), then keep a copy for preview + still capture. WantsFrame rather than
        // HasAnalyzer so the copy is skipped outright on a frame the analyzer's cadence would discard.
        if (this.Pipeline.WantsFrame())
            this.Pipeline.Process(new WindowsCameraFrame(bitmap, mirrored: false), default);

        var display = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        lock (this.latestGate)
        {
            this.latest?.Dispose();
            this.latest = SoftwareBitmap.Copy(display);
        }

        this.previewImage?.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (this.previewSource != null)
                    await this.previewSource.SetBitmapAsync(display);
            }
            catch { /* frame raced with teardown */ }
        });
    }


    public async Task<CameraPhoto> CapturePhotoAsync(CancellationToken ct = default)
    {
        if (this.capture == null)
            throw new InvalidOperationException("Camera is not running");

        // PhotoQuality.Session keeps the original behaviour — a copy of the frame already on screen. It is
        // the only rung that costs nothing, and it is preview-sized by definition. The other two go to the
        // device's photo stream, which on a webcam is frequently several times the preview resolution and on
        // a DSLR-class UVC device is not remotely comparable.
        if (this.VirtualView.PhotoQuality == PhotoQuality.Session)
            return await this.CaptureFromPreviewAsync();

        var props = ImageEncodingProperties.CreateJpeg();
        var best = this.LargestPhotoResolution();
        if (best != null)
        {
            props.Width = best.Width;
            props.Height = best.Height;
        }

        using var stream = new InMemoryRandomAccessStream();
        await this.capture.CapturePhotoToStreamAsync(props, stream);
        stream.Seek(0);

        // No effects: hand back exactly what the device encoded. Decoding and re-encoding it would cost a
        // generation of JPEG for nothing, and this is the path a plain shutter press takes.
        if (this.VirtualView.EffectChain.IsEmpty)
        {
            var decoder = await BitmapDecoder.CreateAsync(stream);
            return new CameraPhoto(await ReadAllAsync(stream), (int)decoder.PixelWidth, (int)decoder.PixelHeight);
        }

        var source = await BitmapDecoder.CreateAsync(stream);
        var captured = await source.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        return await this.EncodeAsync(captured);
    }


    async Task<CameraPhoto> CaptureFromPreviewAsync()
    {
        SoftwareBitmap? snapshot;
        lock (this.latestGate)
            snapshot = this.latest == null ? null : SoftwareBitmap.Copy(this.latest);

        if (snapshot == null)
            throw new InvalidOperationException("Camera is not running");

        return await this.EncodeAsync(snapshot);
    }


    /// <summary>
    /// Applies the effect chain and encodes to JPEG at <see cref="CameraView.PhotoJpegQuality"/>. Takes
    /// ownership of <paramref name="bitmap"/>.
    /// </summary>
    async Task<CameraPhoto> EncodeAsync(SoftwareBitmap bitmap)
    {
        // Windows can't filter the live preview, but it can filter what gets saved — so the chain is applied
        // here rather than nowhere at all.
        var filtered = WindowsCameraFilters.Apply(bitmap, this.VirtualView.EffectChain);
        if (!ReferenceEquals(filtered, bitmap))
        {
            bitmap.Dispose();
            bitmap = filtered;
        }

        // ImageQuality has to go in at CreateAsync — it is an encoder option, not a property that can be set
        // on the encoder afterwards, so there is no way to apply it once encoding has been set up. Without it
        // the encoder picks its own unspecified default and PhotoJpegQuality is silently ignored here.
        var options = new BitmapPropertySet
        {
            { "ImageQuality", new BitmapTypedValue(this.VirtualView.EncoderJpegQuality, Windows.Foundation.PropertyType.Single) }
        };

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, options);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();

        var result = new CameraPhoto(await ReadAllAsync(stream), bitmap.PixelWidth, bitmap.PixelHeight);
        bitmap.Dispose();
        return result;
    }


    /// <summary>The photo stream's largest advertised resolution, or null when the device advertises none.</summary>
    ImageEncodingProperties? LargestPhotoResolution()
    {
        if (this.capture == null)
            return null;

        ImageEncodingProperties? best = null;
        foreach (var candidate in this.capture.VideoDeviceController.GetAvailableMediaStreamProperties(MediaStreamType.Photo))
        {
            if (candidate is not ImageEncodingProperties image)
                continue;

            if (best == null || (long)image.Width * image.Height > (long)best.Width * best.Height)
                best = image;
        }
        return best;
    }


    static async Task<byte[]> ReadAllAsync(IRandomAccessStream stream)
    {
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }


    public async Task StartVideoRecordingAsync(VideoRecordingOptions options, CancellationToken ct = default)
    {
        if (this.capture == null)
            throw new InvalidOperationException("Camera is not running");

        // Burn-in overlays aren't wired on Windows yet: LowLagMediaRecording records straight from the capture
        // device and never sees our composited MediaFrameReader frames, so the overlay would not reach the file.
        // The plan's owned-encode path (IBasicVideoEffect or a MediaStreamSource + Win2D encode) is gated on a
        // Windows-host spike (see the risk register); until then we fail fast rather than silently drop the
        // overlay. The raw-feed recording path (Overlay == null) is fully supported.
        // TODO: implement Windows burn-in recording (Win2D compositing over MediaFrameReader frames).
        if (options.Overlay != null)
            throw new PlatformNotSupportedException(
                "Burn-in video overlays are not yet supported on Windows. Record without VideoRecordingOptions.Overlay, " +
                "or use the on-preview CameraOverlayView.");

        var path = options.FilePath ?? Path.Combine(Path.GetTempPath(), $"shiny-{Guid.NewGuid():N}.mp4");
        var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path));
        var storageFile = await folder.CreateFileAsync(Path.GetFileName(path), Windows.Storage.CreationCollisionOption.ReplaceExisting);

        var profile = this.BuildEncodingProfile();
        this.recording = await this.capture.PrepareLowLagRecordToStorageFileAsync(profile, storageFile);
        await this.recording.StartAsync();
        this.recordingPath = storageFile.Path;
    }

    /// <summary>
    /// The encoding profile for the requested quality, bitrate and frame rate.
    /// </summary>
    /// <remarks>
    /// Unlike the other platforms, Windows builds this per recording rather than when the session is
    /// configured, so <see cref="CameraView.VideoQuality"/> and friends need no rebind here and simply take
    /// effect on the next <c>StartVideoRecordingAsync</c>. <c>MediaEncodingProfile</c> also carries its own
    /// bitrate and frame rate, so both are honoured on the raw-feed path — there is no burn-in path on
    /// Windows to keep in step (see the overlay guard above).
    /// </remarks>
    MediaEncodingProfile BuildEncodingProfile()
    {
        var profile = MediaEncodingProfile.CreateMp4(this.VirtualView.VideoQuality switch
        {
            VideoQuality.Lowest => VideoEncodingQuality.Qvga,
            VideoQuality.Low => VideoEncodingQuality.Vga,
            VideoQuality.Medium => VideoEncodingQuality.HD720p,
            VideoQuality.UltraHigh => VideoEncodingQuality.Uhd2160p,
            VideoQuality.Highest => VideoEncodingQuality.Uhd2160p,
            _ => VideoEncodingQuality.HD1080p
        });

        if (profile.Video == null)
            return profile;

        if (this.VirtualView.VideoBitrate is > 0 and var bitrate)
            profile.Video.Bitrate = (uint)bitrate;

        if (this.VirtualView.VideoFrameRate is > 0 and var fps)
        {
            profile.Video.FrameRate.Numerator = (uint)fps;
            profile.Video.FrameRate.Denominator = 1;
        }

        return profile;
    }

    string? recordingPath;

    public async Task<CameraVideo> StopVideoRecordingAsync(CancellationToken ct = default)
    {
        if (this.recording == null)
            throw new InvalidOperationException("Not recording");

        await this.recording.StopAsync();
        await this.recording.FinishAsync();
        this.recording = null;
        return new CameraVideo(this.recordingPath ?? string.Empty);
    }


    static partial void MapFacing(CameraViewHandler handler, CameraView view) => _ = handler.RestartAsync();

    async Task RestartAsync()
    {
        await this.StopAsync();
        await this.StartAsync();
    }

    static partial void MapIsActive(CameraViewHandler handler, CameraView view)
    {
        if (view.IsActive)
            _ = handler.StartAsync();
        else
            _ = handler.StopAsync();
    }

    // The device controller is only reachable on a started camera, and it throws rather than returning null once
    // the device goes away. A mapper that throws escapes Shell's page-creation path and leaves Shell navigation
    // dead for the session, so swallow it and let the camera stay at its current setting.
    static Windows.Media.Devices.VideoDeviceController? DeviceController(CameraViewHandler handler)
    {
        if (handler.capture == null)
            return null;
        try
        {
            return handler.capture.VideoDeviceController;
        }
        catch
        {
            return null;
        }
    }

    static partial void MapTorch(CameraViewHandler handler, CameraView view)
    {
        try
        {
            if (DeviceController(handler)?.TorchControl is { Supported: true } torch)
                torch.Enabled = view.IsTorchOn;
        }
        catch { /* device dropped or does not support torch */ }
    }

    static partial void MapFlashMode(CameraViewHandler handler, CameraView view) { }

    static partial void MapZoom(CameraViewHandler handler, CameraView view)
    {
        try
        {
            if (DeviceController(handler)?.ZoomControl is { Supported: true } zoom)
            {
                var clamped = Math.Clamp((float)view.Zoom, zoom.Min, zoom.Max);
                zoom.Value = clamped;
                handler.MaybeVirtualView?.OnZoomRangeChanged(zoom.Min, zoom.Max);
            }
        }
        catch { /* device dropped or does not support zoom */ }
    }

    static partial void MapScaleMode(CameraViewHandler handler, CameraView view)
    {
        if (handler.previewImage != null)
            handler.previewImage.Stretch = view.ScaleMode == PreviewScaleMode.AspectFit
                ? Microsoft.UI.Xaml.Media.Stretch.Uniform
                : Microsoft.UI.Xaml.Media.Stretch.UniformToFill;
    }

    // Collapsed rather than hidden: the preview image is one child of a Grid that also carries the managed
    // overlay, so taking it out of layout costs nothing the overlay needs. MediaCapture keeps running, and a
    // recording in flight is written from the same capture regardless of what is on screen.
    static partial void MapShowPreview(CameraViewHandler handler, CameraView view)
    {
        if (handler.previewImage != null)
            handler.previewImage.Visibility = view.ShowPreview
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    static partial void MapOverlay(CameraViewHandler handler, CameraView view) { /* drawn by managed overlay */ }

    // Nothing to do for the live preview: MediaCapture has no cheap effect hook (it would take an
    // IBasicVideoEffect + Win2D pipeline), so effects here are applied to captured stills only. That is
    // reported as EffectSupport.StillOnly rather than pretending the effect took.
    static partial void MapEffects(CameraViewHandler handler, CameraView view) { }

    // Nothing to do: the MediaEncodingProfile is built per recording, so the new value is picked up by the
    // next StartVideoRecordingAsync without touching the running session.
    static partial void MapVideoQuality(CameraViewHandler handler, CameraView view) { }

    // MediaCapture is asked for a photo resolution per capture rather than at configuration time, so
    // there is nothing to rebind when the rung changes.
    static partial void MapPhotoQuality(CameraViewHandler handler, CameraView view) { /* applied at capture time */ }

    // Nothing to do: no rotating display behind the capture device on desktop Windows.
    static partial void MapOrientation(CameraViewHandler handler, CameraView view) { }

    // The capture format is an Apple concern — its data output is the only one asked for a pixel format.
    static partial void MapCaptureFormat(CameraViewHandler handler, CameraView view) { }
}
