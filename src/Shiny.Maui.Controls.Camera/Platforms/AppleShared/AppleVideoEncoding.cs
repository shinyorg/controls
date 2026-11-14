using CoreGraphics;
using CoreImage;
using CoreMedia;
using CoreVideo;
using Shiny.Maui.Controls.Camera.Video;
using VideoToolbox;

namespace Shiny.Maui.Controls.Camera;

public partial class VideoEncoder
{
    // VideoToolbox has had a hardware H.264 encoder on every device and Mac these OS versions run on, and
    // Core Image writes JPEG everywhere.
    private static partial bool PlatformIsSupported(VideoCodec codec) => codec is VideoCodec.Jpeg or VideoCodec.H264;

    private static partial IVideoEncoderSession? CreatePlatformSession(
        VideoEncoderSettings settings, int width, int height, Action<EncodedVideoFrame> sink)
        => settings.Codec switch
        {
            VideoCodec.Jpeg => new AppleJpegEncoderSession(settings, sink),
            VideoCodec.H264 => AppleH264EncoderSession.Create(settings, width, height, sink),
            _ => null
        };
}


/// <summary>The Core Image context and colour space every live-video encode and decode on Apple shares.</summary>
/// <remarks>
/// One context for the process: a <c>CIContext</c> is expensive to build and safe to share across threads, and
/// a per-frame one would be a new Metal command queue fifteen times a second. The software fallback is for
/// simulators without a Metal device.
/// </remarks>
static class AppleVideoContext
{
    static CIContext? shared;
    static CGColorSpace? colorSpace;

    public static CIContext Shared => shared ??= Metal.MTLDevice.SystemDefault is { } device
        ? CIContext.FromMetalDevice(device)
        : new CIContext();

    public static CGColorSpace ColorSpace => colorSpace ??= CGColorSpace.CreateDeviceRGB();

    /// <summary>
    /// The capture buffer as a Core Image image scaled to <paramref name="width"/> x <paramref name="height"/>.
    /// Nothing is rendered yet: the scale is folded into whichever render consumes the image, on the GPU.
    /// </summary>
    public static CIImage Scaled(CVPixelBuffer source, int width, int height)
    {
        using var image = new CIImage(source);
        var sx = (double)width / source.Width;
        var sy = (double)height / source.Height;
        return image.ImageByApplyingTransform(CGAffineTransform.MakeScale((nfloat)sx, (nfloat)sy));
    }

    // Rotation and mirroring ride along in VideoToolbox's per-frame refcon, so a frame's orientation comes back
    // with its own output rather than with whatever the camera reports by the time the callback runs.
    public static IntPtr PackOrientation(int rotation, bool mirrored)
        => (IntPtr)((VideoScaling.NormalizeRotation(rotation) / 90) | (mirrored ? 4 : 0));

    public static (int Rotation, bool Mirrored) UnpackOrientation(IntPtr packed)
    {
        var value = (long)packed;
        return ((int)(value & 3) * 90, (value & 4) != 0);
    }
}


/// <summary>
/// JPEG through Core Image: scale and compress in one GPU render, straight off the capture buffer.
/// </summary>
sealed class AppleJpegEncoderSession(VideoEncoderSettings settings, Action<EncodedVideoFrame> sink) : IVideoEncoderSession
{
    double quality = settings.JpegQuality;

    public bool CanEncode(VideoEncoderSettings next, int width, int height)
    {
        if (next.Codec != VideoCodec.Jpeg)
            return false;

        // any size, any quality: every frame stands alone
        this.quality = next.JpegQuality;
        return true;
    }

    public ValueTask EncodeAsync(VideoFrame frame, int width, int height, TimeSpan timestamp, bool forceKeyFrame, CancellationToken ct)
    {
        if (frame is not AppleVideoFrame apple)
            return default;

        using var scaled = AppleVideoContext.Scaled(apple.PixelBuffer, width, height);
        var options = new CIImageRepresentationOptions { LossyCompressionQuality = (float)this.quality };
        using var data = AppleVideoContext.Shared.GetJpegRepresentation(scaled, AppleVideoContext.ColorSpace, options);
        if (data is null)
            return default;

        sink(new EncodedVideoFrame(
            VideoCodec.Jpeg, data.ToArray(), width, height, frame.Rotation, frame.IsMirrored, true, timestamp));
        return default;
    }

    public void Dispose() { }
}


/// <summary>
/// H.264 through a real-time <see cref="VTCompressionSession"/>: baseline profile, no frame reordering, so every
/// frame comes out as soon as it is encoded and in the order it went in.
/// </summary>
/// <remarks>
/// <para>
/// Each camera buffer is scaled into one of the session's own pixel buffers with Core Image before it is
/// submitted, rather than handed over as it is. That is a small GPU copy, and it is required: VideoToolbox reads
/// a submitted buffer asynchronously, and with a recording running the capture delegate draws the burn-in
/// overlay back into the capture buffer straight after the analyzer returns.
/// </para>
/// <para>
/// VideoToolbox emits AVCC (length-prefixed NAL units, parameter sets in the format description). The output
/// callback converts each frame to Annex B and, for a key frame, writes the SPS and PPS in front of it, so the
/// receiver can start from any key frame.
/// </para>
/// </remarks>
sealed class AppleH264EncoderSession : IVideoEncoderSession
{
    readonly VTCompressionSession session;
    readonly VideoEncoderSettings settings;
    readonly int width;
    readonly int height;
    readonly Action<EncodedVideoFrame> sink;
    volatile bool disposed;

    AppleH264EncoderSession(VideoEncoderSettings settings, int width, int height, Action<EncodedVideoFrame> sink, Func<AppleH264EncoderSession, VTCompressionSession?> create)
    {
        this.settings = settings;
        this.width = width;
        this.height = height;
        this.sink = sink;
        this.session = create(this) ?? throw new InvalidOperationException("VideoToolbox could not create an H.264 encoder.");
    }

    public static AppleH264EncoderSession? Create(VideoEncoderSettings settings, int width, int height, Action<EncodedVideoFrame> sink)
    {
        try
        {
            return new AppleH264EncoderSession(settings, width, height, sink, self =>
            {
                var created = VTCompressionSession.Create(
                    width,
                    height,
                    CMVideoCodecType.H264,
                    self.OnEncoded,
                    null,
                    new CVPixelBufferAttributes(CVPixelFormatType.CV32BGRA, width, height));

                if (created is null)
                    return null;

                var fps = settings.FrameRate > 0 ? settings.FrameRate : 30;
                var properties = new VTCompressionProperties
                {
                    RealTime = true,
                    ProfileLevel = VTProfileLevel.H264BaselineAutoLevel,
                    AllowFrameReordering = false,
                    AverageBitRate = settings.Bitrate,
                    ExpectedFrameRate = fps,
                    MaxKeyFrameIntervalDuration = settings.KeyFrameInterval.TotalSeconds,
                    // the same interval in frames, so a camera running slower than FrameRate still gets one;
                    // 1 (every frame a key frame) for a zero interval
                    MaxKeyFrameInterval = Math.Max(1, (int)Math.Round(settings.KeyFrameInterval.TotalSeconds * fps))
                };
                created.SetCompressionProperties(properties);
                created.PrepareToEncodeFrames();
                return created;
            });
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool CanEncode(VideoEncoderSettings next, int width, int height)
        => width == this.width
            && height == this.height
            && next with { JpegQuality = 0 } == this.settings with { JpegQuality = 0 };

    public ValueTask EncodeAsync(VideoFrame frame, int width, int height, TimeSpan timestamp, bool forceKeyFrame, CancellationToken ct)
    {
        if (frame is not AppleVideoFrame apple)
            return default;

        var target = this.session.GetPixelBufferPool()?.CreatePixelBuffer()
            ?? new CVPixelBuffer(width, height, CVPixelFormatType.CV32BGRA);
        try
        {
            using (var scaled = AppleVideoContext.Scaled(apple.PixelBuffer, width, height))
                AppleVideoContext.Shared.Render(scaled, target, new CGRect(0, 0, width, height), AppleVideoContext.ColorSpace);

            using var options = forceKeyFrame ? new VTEncodeFrameOptions { ForceKeyFrame = true }.Dictionary : null;
            var status = this.session.EncodeFrame(
                target,
                new CMTime(timestamp.Ticks / 10, 1_000_000),
                CMTime.Invalid,
                options,
                AppleVideoContext.PackOrientation(frame.Rotation, frame.IsMirrored),
                out _);

            if (status != VTStatus.Ok)
                throw new InvalidOperationException($"VideoToolbox refused a frame: {status}.");
        }
        finally
        {
            // VideoToolbox retains what it still needs
            target.Dispose();
        }
        return default;
    }

    // VideoToolbox's output thread, once per submitted frame, in order.
    void OnEncoded(IntPtr refcon, VTStatus status, VTEncodeInfoFlags flags, CMSampleBuffer? buffer)
    {
        try
        {
            if (this.disposed || status != VTStatus.Ok || buffer is null || (flags & VTEncodeInfoFlags.FrameDropped) != 0)
                return;

            var attachments = buffer.GetSampleAttachments(false);
            var isKey = attachments is not { Length: > 0 } || attachments[0]?.NotSync != true;

            if (buffer.GetVideoFormatDescription() is not { } format)
                return;

            var first = format.GetH264ParameterSet(0, out var count, out var lengthSize, out var error);
            if (error != CMFormatDescriptionError.None || first is null)
                return;

            List<byte[]>? parameterSets = null;
            if (isKey)
            {
                parameterSets = [first];
                for (nuint i = 1; i < count; i++)
                {
                    if (format.GetH264ParameterSet(i, out _, out _, out _) is { } ps)
                        parameterSets.Add(ps);
                }
            }

            using var block = buffer.GetDataBuffer();
            if (block is null)
                return;

            var length = (int)block.DataLength;
            var avcc = new byte[length];
            unsafe
            {
                fixed (byte* p = avcc)
                {
                    if (block.CopyDataBytes(0, (nuint)length, (IntPtr)p) != CMBlockBufferError.None)
                        return;
                }
            }

            var annexB = H264AnnexB.FromLengthPrefixed(avcc, lengthSize, parameterSets);
            var (rotation, mirrored) = AppleVideoContext.UnpackOrientation(refcon);
            var pts = buffer.PresentationTimeStamp;
            var timestamp = pts.IsInvalid ? TimeSpan.Zero : TimeSpan.FromSeconds(pts.Seconds);

            this.sink(new EncodedVideoFrame(
                VideoCodec.H264, annexB, this.width, this.height, rotation, mirrored, isKey, timestamp));
        }
        catch (Exception)
        {
            // a native callback: nothing may unwind into VideoToolbox
        }
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        this.session.Dispose();
    }
}
