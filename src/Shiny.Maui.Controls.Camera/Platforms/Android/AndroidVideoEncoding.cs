using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Android.Graphics;
using Android.Media;
using Android.OS;
using AndroidX.Camera.Core;
using Shiny.Maui.Controls.Camera.Video;

namespace Shiny.Maui.Controls.Camera;

public partial class VideoEncoder
{
    private static partial bool PlatformIsSupported(VideoCodec codec) => codec switch
    {
        VideoCodec.Jpeg => true,
        VideoCodec.H264 => AndroidVideoCodecs.HasAvcEncoder,
        _ => false
    };

    private static partial IVideoEncoderSession? CreatePlatformSession(
        VideoEncoderSettings settings, int width, int height, Action<EncodedVideoFrame> sink)
        => settings.Codec switch
        {
            VideoCodec.Jpeg => new AndroidJpegEncoderSession(settings, sink),
            VideoCodec.H264 when AndroidVideoCodecs.HasAvcEncoder => AndroidH264EncoderSession.Create(settings, width, height, sink),
            _ => null
        };
}


/// <summary>What this device's MediaCodec list offers for <c>video/avc</c>, looked up once.</summary>
static class AndroidVideoCodecs
{
    public const string Avc = "video/avc";

    static readonly Lazy<(bool Encoder, bool Decoder)> avc = new(() =>
    {
        bool encoder = false, decoder = false;
        try
        {
            foreach (var info in new MediaCodecList(MediaCodecListKind.RegularCodecs).GetCodecInfos() ?? [])
            {
                if (!(info.GetSupportedTypes() ?? []).Any(t => string.Equals(t, Avc, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (info.IsEncoder)
                    encoder = true;
                else
                    decoder = true;
            }
        }
        catch (Exception)
        {
            // a codec list that won't enumerate is a device with no usable codec
        }
        return (encoder, decoder);
    });

    public static bool HasAvcEncoder => avc.Value.Encoder;
    public static bool HasAvcDecoder => avc.Value.Decoder;
}


/// <summary>
/// The planes of a CameraX <see cref="IImageProxy"/> read in place: YUV_420_888 is always three direct buffers,
/// so their native addresses go straight to <see cref="YuvScaler"/> with no copy into managed memory.
/// </summary>
static class AndroidYuv
{
    /// <summary>Scale the camera frame's three planes into a destination laid out as <paramref name="y"/>, <paramref name="u"/>, <paramref name="v"/>.</summary>
    public static unsafe void Scale(IImageProxy source, YuvPlane y, YuvPlane u, YuvPlane v, int width, int height)
    {
        var planes = source.GetPlanes() ?? throw new InvalidOperationException("The camera frame has no planes.");
        if (planes.Length < 3)
            throw new InvalidOperationException("The camera frame is not YUV_420_888.");

        int sw = source.Width, sh = source.Height;
        int scw = (sw + 1) / 2, sch = (sh + 1) / 2;
        int dcw = width / 2, dch = height / 2;

        YuvScaler.ScalePlane(Plane(planes[0]), sw, sh, y, width, height);
        YuvScaler.ScalePlane(Plane(planes[1]), scw, sch, u, dcw, dch);
        YuvScaler.ScalePlane(Plane(planes[2]), scw, sch, v, dcw, dch);
    }

    static unsafe YuvPlane Plane(IImageProxyPlaneProxy plane)
        => new((byte*)Address(plane.Buffer), plane.RowStride, plane.PixelStride);

    public static unsafe YuvPlane Plane(Android.Media.Image.Plane plane)
        => new((byte*)Address(plane.Buffer), plane.RowStride, plane.PixelStride);

    static IntPtr Address(Java.Nio.ByteBuffer? buffer)
    {
        var address = buffer?.GetDirectBufferAddress() ?? IntPtr.Zero;
        return address == IntPtr.Zero
            ? throw new InvalidOperationException("An image plane is not a direct buffer.")
            : address;
    }
}


/// <summary>
/// JPEG on Android: scale the camera frame's planes into one NV21 buffer and let <see cref="YuvImage"/>
/// compress it. No RGB bitmap is ever built.
/// </summary>
sealed class AndroidJpegEncoderSession(VideoEncoderSettings settings, Action<EncodedVideoFrame> sink) : IVideoEncoderSession
{
    double quality = settings.JpegQuality;
    byte[]? nv21;
    readonly MemoryStream output = new();

    public bool CanEncode(VideoEncoderSettings next, int width, int height)
    {
        if (next.Codec != VideoCodec.Jpeg)
            return false;

        this.quality = next.JpegQuality;
        return true;
    }

    public ValueTask EncodeAsync(VideoFrame frame, int width, int height, TimeSpan timestamp, bool forceKeyFrame, CancellationToken ct)
    {
        if (frame is not AndroidCameraFrame android)
            return default;

        var size = width * height + 2 * (width / 2) * (height / 2);
        if (this.nv21 is null || this.nv21.Length != size)
            this.nv21 = new byte[size];

        unsafe
        {
            fixed (byte* p = this.nv21)
            {
                // NV21: the Y plane, then V and U interleaved, V first
                var chroma = p + width * height;
                AndroidYuv.Scale(
                    android.Proxy,
                    new YuvPlane(p, width, 1),
                    new YuvPlane(chroma + 1, width / 2 * 2, 2),
                    new YuvPlane(chroma, width / 2 * 2, 2),
                    width,
                    height);
            }
        }

        // YuvImage needs even dimensions, which VideoScaling.Fit guarantees
        using var image = new YuvImage(this.nv21, ImageFormatType.Nv21, width, height, null);
        this.output.SetLength(0);
        using (var rect = new Android.Graphics.Rect(0, 0, width, height))
        {
            if (!image.CompressToJpeg(rect, (int)Math.Round(this.quality * 100), this.output))
                return default;
        }

        sink(new EncodedVideoFrame(
            VideoCodec.Jpeg, this.output.ToArray(), width, height, frame.Rotation, frame.IsMirrored, true, timestamp));
        return default;
    }

    public void Dispose() => this.output.Dispose();
}


/// <summary>
/// H.264 through a MediaCodec <c>video/avc</c> encoder in ByteBuffer mode: each camera frame is scaled straight
/// into the encoder's input <see cref="Android.Media.Image"/>, and a dedicated thread drains the output so a frame is
/// delivered as soon as it's encoded rather than on the next camera frame.
/// </summary>
/// <remarks>
/// <para>
/// Input is the flexible YUV 4:2:0 format through <see cref="MediaCodec.GetInputImage"/>, which hands back
/// planes with their own strides, so the same scaler that reads CameraX's planes writes the encoder's, whatever
/// the vendor's layout (I420, NV12, padded rows).
/// </para>
/// <para>
/// MediaCodec already speaks Annex B. It emits the SPS and PPS once, as a codec-config buffer; from API 29 the
/// encoder is also asked to repeat them ahead of every sync frame, and on older devices (or encoders that
/// ignore the request) the saved config is put in front of each key frame here.
/// </para>
/// </remarks>
sealed class AndroidH264EncoderSession : IVideoEncoderSession
{
    readonly MediaCodec codec;
    readonly VideoEncoderSettings settings;
    readonly int width;
    readonly int height;
    readonly Action<EncodedVideoFrame> sink;
    readonly ConcurrentDictionary<long, (int Rotation, bool Mirrored)> pending = new();
    readonly Thread drain;
    volatile bool running = true;
    byte[]? config;

    AndroidH264EncoderSession(MediaCodec codec, VideoEncoderSettings settings, int width, int height, Action<EncodedVideoFrame> sink)
    {
        this.codec = codec;
        this.settings = settings;
        this.width = width;
        this.height = height;
        this.sink = sink;
        this.drain = new Thread(this.Drain) { IsBackground = true, Name = "shiny.camera.video.encoder" };
        this.drain.Start();
    }

    public static AndroidH264EncoderSession? Create(VideoEncoderSettings settings, int width, int height, Action<EncodedVideoFrame> sink)
    {
        MediaCodec? codec = null;
        try
        {
            codec = MediaCodec.CreateEncoderByType(AndroidVideoCodecs.Avc);
            var fps = settings.FrameRate > 0 ? settings.FrameRate : 30;

            using var format = MediaFormat.CreateVideoFormat(AndroidVideoCodecs.Avc, width, height);
            format.SetInteger(MediaFormat.KeyColorFormat, (int)MediaCodecCapabilities.Formatyuv420flexible);
            format.SetInteger(MediaFormat.KeyBitRate, settings.Bitrate);
            format.SetInteger(MediaFormat.KeyFrameRate, Math.Max(1, (int)Math.Round(fps)));
            format.SetInteger(MediaFormat.KeyProfile, (int)MediaCodecProfileType.Avcprofilebaseline);

            // seconds between sync frames; 0 makes every frame one. Whole seconds before API 25, where the
            // VideoEncoder's own backstop covers a shorter interval.
            if (OperatingSystem.IsAndroidVersionAtLeast(25))
                format.SetFloat(MediaFormat.KeyIFrameInterval, (float)settings.KeyFrameInterval.TotalSeconds);
            else
                format.SetInteger(MediaFormat.KeyIFrameInterval, settings.KeyFrameInterval <= TimeSpan.Zero ? 0 : Math.Max(1, (int)Math.Round(settings.KeyFrameInterval.TotalSeconds)));

            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                format.SetInteger(MediaFormat.KeyPrependHeaderToSyncFrames, 1);

            // constant bitrate where the encoder offers it: a call's transport has a fixed budget, and a VBR
            // spike on a scene change is exactly the frame that arrives late
            var capabilities = codec.CodecInfo?.GetCapabilitiesForType(AndroidVideoCodecs.Avc)?.EncoderCapabilities;
            if (capabilities?.IsBitrateModeSupported(BitrateMode.Cbr) == true)
                format.SetInteger(MediaFormat.KeyBitrateMode, (int)BitrateMode.Cbr);

            codec.Configure(format, null, null, MediaCodecConfigFlags.Encode);
            codec.Start();
            return new AndroidH264EncoderSession(codec, settings, width, height, sink);
        }
        catch (Exception)
        {
            codec?.Release();
            return null;
        }
    }

    public bool CanEncode(VideoEncoderSettings next, int width, int height)
        => width == this.width
            && height == this.height
            && next with { JpegQuality = 0 } == this.settings with { JpegQuality = 0 };

    public ValueTask EncodeAsync(VideoFrame frame, int width, int height, TimeSpan timestamp, bool forceKeyFrame, CancellationToken ct)
    {
        if (frame is not AndroidCameraFrame android)
            return default;

        // a short wait at most: an encoder that has no free input buffer is behind, and this frame is the one
        // to drop rather than the capture thread
        var index = this.codec.DequeueInputBuffer(5_000);
        if (index < 0)
            return default;

        var queued = false;
        try
        {
            using (var image = this.codec.GetInputImage(index) ?? throw new InvalidOperationException("The encoder has no input image."))
            {
                var planes = image.GetPlanes() ?? throw new InvalidOperationException("The encoder input has no planes.");
                AndroidYuv.Scale(
                    android.Proxy,
                    AndroidYuv.Plane(planes[0]),
                    AndroidYuv.Plane(planes[1]),
                    AndroidYuv.Plane(planes[2]),
                    width,
                    height);
            }

            if (forceKeyFrame)
            {
                using var request = new Bundle();
                request.PutInt(MediaCodec.ParameterKeyRequestSyncFrame, 0);
                this.codec.SetParameters(request);
            }

            var pts = timestamp.Ticks / 10;
            this.pending[pts] = (frame.Rotation, frame.IsMirrored);
            this.codec.QueueInputBuffer(index, 0, width * height * 3 / 2, pts, MediaCodecBufferFlags.None);
            queued = true;
        }
        finally
        {
            // an input buffer that was dequeued must go back, or the encoder eventually runs dry
            if (!queued)
                this.codec.QueueInputBuffer(index, 0, 0, 0, MediaCodecBufferFlags.None);
        }
        return default;
    }

    void Drain()
    {
        using var info = new MediaCodec.BufferInfo();
        while (this.running)
        {
            try
            {
                var index = this.codec.DequeueOutputBuffer(info, 50_000);
                if (index < 0)
                    continue; // try-again-later, format or buffers changed

                try
                {
                    this.Deliver(index, info);
                }
                finally
                {
                    this.codec.ReleaseOutputBuffer(index, false);
                }
            }
            catch (Exception)
            {
                // the codec was stopped under us (Dispose) or failed; either way this loop is done
                if (!this.running)
                    return;
            }
        }
    }

    void Deliver(int index, MediaCodec.BufferInfo info)
    {
        if (info.Size <= 0)
            return;

        using var buffer = this.codec.GetOutputBuffer(index);
        var address = buffer?.GetDirectBufferAddress() ?? IntPtr.Zero;
        if (address == IntPtr.Zero)
            return;

        var data = new byte[info.Size];
        Marshal.Copy(address + info.Offset, data, 0, info.Size);

        if ((info.Flags & MediaCodecBufferFlags.CodecConfig) != 0)
        {
            this.config = data; // the SPS and PPS, in Annex B
            return;
        }

        var isKey = (info.Flags & MediaCodecBufferFlags.KeyFrame) != 0;
        if (isKey)
            data = H264AnnexB.EnsureParameterSets(data, this.config);

        this.pending.TryRemove(info.PresentationTimeUs, out var orientation);
        this.sink(new EncodedVideoFrame(
            VideoCodec.H264,
            data,
            this.width,
            this.height,
            orientation.Rotation,
            orientation.Mirrored,
            isKey,
            TimeSpan.FromTicks(info.PresentationTimeUs * 10)));
    }

    public void Dispose()
    {
        if (!this.running)
            return;

        this.running = false;
        this.drain.Join(TimeSpan.FromMilliseconds(500));
        try
        {
            this.codec.Stop();
        }
        catch (Exception)
        {
            // already stopped or failed
        }
        this.codec.Release();
        this.codec.Dispose();
    }
}
