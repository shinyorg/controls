using System.Runtime.InteropServices;
using CoreGraphics;
using CoreImage;
using CoreMedia;
using CoreVideo;

namespace Shiny.Maui.Controls.Camera;

/// <summary>
/// A <see cref="CameraFrame"/> over an <c>AVCaptureVideoDataOutput</c> buffer, in one of two modes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Borrowed</b> (<see cref="Borrow"/>) holds the capture's own <see cref="CMSampleBuffer"/> open and
/// reads its pixels in place — no copy at all, which is what <c>AndroidCameraFrame</c> does with its
/// <c>ImageProxy</c>. <b>Copied</b> (<see cref="Copy"/>) takes a managed BGRA snapshot the way this class
/// always did.
/// </para>
/// <para>
/// ⚠️ <b>The mode is not a preference, it is a correctness question, and getting it wrong corrupts an
/// analysis rather than slowing it.</b> <c>AppleVideoOverlayRecorder.Composite</c> renders pixel effects
/// and draws the burn-in overlay <i>back into the very buffer the capture delivered</i>. A borrowed frame
/// handed to an async analyzer would then be read on one thread while the encoder mutates it on another —
/// the analyzer would see the HUD burned across the scene it is reading, torn halfway. So a frame is
/// borrowed only while <b>nothing is going to write to that buffer</b>; with a recorder attached it must be
/// copied. <c>VideoFrameDelegate</c> owns that decision because it is the only thing that knows.
/// </para>
/// <para>
/// <b>Even the copy is now lazy.</b> Every in-tree Apple analyzer (OCR, barcode, face, documents) consumes
/// <see cref="ToCGImage"/>, which builds straight off the pixels — so on the common path nothing
/// materializes <see cref="Bgra"/> at all, in either mode.
/// </para>
/// <para>
/// <b>Two pixel formats, and only one of them is a conversion.</b> The capture output may deliver BGRA or
/// biplanar YCbCr (NV12) — see <see cref="CameraView.CaptureFormat"/> for why anyone would ask for the
/// latter. A <b>borrowed</b> frame reports whichever it was handed and reads it in place: NV12's first
/// plane <i>is</i> luminance, so <see cref="SampleLuminance"/> and <see cref="MaterializeLuminance"/> get
/// cheaper rather than dearer, and <see cref="ToCGImage"/> converts on the GPU only when an analyzer
/// actually asks for one. A <b>copied</b> frame is always BGRA: the copy exists so the frame can outlive a
/// buffer the recorder is writing into, and converting once at that point keeps every consumer downstream
/// on one format.
/// </para>
/// <para>
/// Holding a capture buffer open is bounded and deliberate: the pipeline runs one analysis at a time and
/// <see cref="Internal.CameraPipeline.WantsFrame"/> refuses a frame while a pass is in flight, so at most
/// one buffer is ever out of the pool. <c>AVCaptureVideoDataOutput</c> drops late frames rather than
/// queueing them, which is the behaviour we want if a pass ever overruns.
/// </para>
/// </remarks>
public sealed class AppleCameraFrame : CameraFrame
{
    readonly CMSampleBuffer? owned;
    readonly CVPixelBuffer pixelBuffer;
    readonly bool ownsPixelBuffer;
    byte[]? bgra;

    AppleCameraFrame(
        CMSampleBuffer? owned,
        CVPixelBuffer pixelBuffer,
        byte[]? bgra,
        int rotation,
        bool mirrored,
        bool ownsPixelBuffer = false
    )
    {
        this.owned = owned;
        this.pixelBuffer = pixelBuffer;
        this.bgra = bgra;
        this.ownsPixelBuffer = ownsPixelBuffer;
        this.Width = (int)pixelBuffer.Width;
        this.Height = (int)pixelBuffer.Height;
        this.Rotation = rotation;
        this.IsMirrored = mirrored;
        this.IsBiplanar = IsBiplanarFormat(pixelBuffer.PixelFormatType);
        this.videoRangeLuma = pixelBuffer.PixelFormatType == CVPixelFormatType.CV420YpCbCr8BiPlanarVideoRange;
    }

    /// <summary>
    /// Whether the pixels are biplanar YCbCr rather than BGRA. Not the same question as
    /// <see cref="Format"/> answering <see cref="CameraFrameFormat.Yuv420"/> — it is where every read below
    /// branches.
    /// </summary>
    bool IsBiplanar { get; }

    /// <summary>
    /// ⚠️ Video-range luma is 16–235, not 0–255, and reading it as though it were full range makes every
    /// frame look darker than it is — which for an ambient-light consumer is a wrong answer rather than a
    /// slightly-off one.
    /// </summary>
    readonly bool videoRangeLuma;

    /// <summary>Whether a capture buffer is biplanar YCbCr rather than something a CPU can draw on.</summary>
    internal static bool IsBiplanarFormat(CVPixelFormatType format) =>
        format is CVPixelFormatType.CV420YpCbCr8BiPlanarFullRange
            or CVPixelFormatType.CV420YpCbCr8BiPlanarVideoRange;

    byte ExpandLuma(byte value) => this.videoRangeLuma
        ? (byte)Math.Clamp((value - 16) * 255 / 219, 0, 255)
        : value;

    /// <summary>
    /// Hold the capture buffer open and read it in place. The frame takes ownership of
    /// <paramref name="sampleBuffer"/> and disposes it when the last reference is released — the caller
    /// must not.
    /// </summary>
    /// <remarks>
    /// ⚠️ Only safe while nothing will write to this buffer for the frame's lifetime. See the class remarks.
    /// </remarks>
    public static AppleCameraFrame Borrow(CMSampleBuffer sampleBuffer, CVPixelBuffer pixelBuffer, int rotation, bool mirrored)
        => new(sampleBuffer, pixelBuffer, bgra: null, rotation, mirrored);

    /// <summary>
    /// Take a managed BGRA snapshot, so the frame outlives the capture buffer and is unaffected by anything
    /// written to it afterwards. The caller keeps ownership of its buffers.
    /// </summary>
    /// <remarks>
    /// A biplanar capture buffer is converted to BGRA on the GPU first, once, and the frame then behaves
    /// exactly like any other copied frame. That conversion is the price of a snapshot that has to survive
    /// the recorder writing into the original — and it replaces a full-frame CPU copy rather than adding to
    /// one.
    /// </remarks>
    /// <summary>
    /// Wraps a buffer this library owns and will reuse, without copying it.
    /// </summary>
    /// <remarks>
    /// For the filtered frame handed to an analyzer - see <see cref="FilteredFrameBuffer"/>. There
    /// is no CMSampleBuffer behind it and nothing to release: the buffer belongs to the delegate
    /// that rendered it, and is not touched again until the analysis has returned.
    /// </remarks>
    internal static AppleCameraFrame Wrap(CVPixelBuffer pixelBuffer, int rotation, bool mirrored)
        => new(owned: null, pixelBuffer, bgra: null, rotation, mirrored);

    public static AppleCameraFrame Copy(CVPixelBuffer pixelBuffer, int rotation, bool mirrored)
    {
        if (!IsBiplanarFormat(pixelBuffer.PixelFormatType))
            return new(owned: null, pixelBuffer, CopyBgra(pixelBuffer), rotation, mirrored);

        if (Convert(pixelBuffer) is { } converted)
            return new(owned: null, converted, bgra: null, rotation, mirrored, ownsPixelBuffer: true);

        // Nothing to snapshot and no way to convert: hand back a frame over the original rather than
        // nothing at all. It is the borrowed frame's contract without the borrow, which is only reachable
        // if Core Image itself failed — at which point the analysis is the lesser problem.
        return new(owned: null, pixelBuffer, bgra: null, rotation, mirrored);
    }

    /// <summary>Renders a biplanar buffer into a BGRA one of the same size, on the GPU.</summary>
    static CVPixelBuffer? Convert(CVPixelBuffer source)
    {
        try
        {
            var attributes = new CVPixelBufferAttributes
            {
                PixelFormatType = CVPixelFormatType.CV32BGRA,
                Width = (int)source.Width,
                Height = (int)source.Height
            };

            var destination = new CVPixelBuffer(
                (nint)source.Width, (nint)source.Height, CVPixelFormatType.CV32BGRA, attributes);

            using var image = new CIImage(source);
            using var cs = CGColorSpace.CreateDeviceRGB();
            SharedContext.Render(image, destination, image.Extent, cs);
            return destination;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// One Metal-backed context for every conversion a frame needs, built on first use.
    /// </summary>
    /// <remarks>
    /// Static because a <c>CIContext</c> is expensive to build and entirely thread-safe to share, and a
    /// per-frame one would be a new Metal command queue thirty times a second. The software fallback is for
    /// simulators with no Metal device, where correctness matters and speed does not.
    /// </remarks>
    static CIContext SharedContext => sharedContext ??= Metal.MTLDevice.SystemDefault is { } device
        ? CIContext.FromMetalDevice(device)
        : new CIContext();

    static CIContext? sharedContext;

    public override int Width { get; }
    public override int Height { get; }
    public override int Rotation { get; }
    public override bool IsMirrored { get; }
    public override CameraFrameFormat Format =>
        this.IsBiplanar ? CameraFrameFormat.Yuv420 : CameraFrameFormat.Bgra32;

    /// <summary>
    /// The capture buffer itself, for analyzers that can consume one directly. Valid until the frame is
    /// disposed.
    /// </summary>
    public CVPixelBuffer PixelBuffer => this.pixelBuffer;

    /// <summary>
    /// The raw BGRA pixels (4 bytes/px, row-packed at <see cref="CameraFrame.Width"/>).
    /// </summary>
    /// <remarks>
    /// <b>Materialized on first read, not up front.</b> At 1080p this is an 8.3 MB array and every one of
    /// them lands on the Large Object Heap, so it is worth not allocating for the analyzers that never ask.
    /// Prefer <see cref="ToCGImage"/> or <see cref="PixelBuffer"/>.
    /// </remarks>
    public byte[] Bgra => this.bgra ??= this.IsBiplanar
        ? BgraFromBiplanar(this.pixelBuffer)
        : CopyBgra(this.pixelBuffer);

    /// <summary>
    /// BGRA out of a biplanar buffer, by way of one GPU conversion into a scratch buffer.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The expensive read on this class, and the reason every in-tree analyzer avoids it.</b> A
    /// biplanar frame carries 1.5 bytes per pixel and this hands back 4, converted — so it is a colour
    /// conversion plus an 8.3 MB Large Object Heap allocation at 1080p. Prefer <see cref="ToCGImage"/>,
    /// <see cref="PixelBuffer"/>, or the luminance reads, all of which stay on the native planes.
    /// </remarks>
    static byte[] BgraFromBiplanar(CVPixelBuffer source)
    {
        using var converted = Convert(source) ?? throw new InvalidOperationException(
            "Could not convert a biplanar capture buffer to BGRA.");

        return CopyBgra(converted);
    }

    static byte[] CopyBgra(CVPixelBuffer pixelBuffer)
    {
        pixelBuffer.Lock(CVPixelBufferLock.ReadOnly);
        try
        {
            int w = (int)pixelBuffer.Width, h = (int)pixelBuffer.Height;
            var bytesPerRow = (int)pixelBuffer.BytesPerRow;
            var bgra = new byte[w * h * 4];
            var baseAddr = pixelBuffer.BaseAddress;
            for (var row = 0; row < h; row++)
                Marshal.Copy(baseAddr + row * bytesPerRow, bgra, row * w * 4, w * 4);

            return bgra;
        }
        finally
        {
            pixelBuffer.Unlock(CVPixelBufferLock.ReadOnly);
        }
    }

    /// <summary>Build a <see cref="CGImage"/> for Vision / Core Image analyzers.</summary>
    /// <remarks>
    /// Built straight off the capture buffer, so the common analyzer path copies the frame exactly once —
    /// into the <see cref="CGImage"/> — instead of into a managed array and then into a bitmap context
    /// wrapped around that array. A frame that has already materialized <see cref="Bgra"/> is built from
    /// it rather than re-locking the buffer.
    /// </remarks>
    public CGImage? ToCGImage()
    {
        using var cs = CGColorSpace.CreateDeviceRGB();
        const CGBitmapFlags flags = (CGBitmapFlags)CGImageAlphaInfo.NoneSkipFirst | CGBitmapFlags.ByteOrder32Little;

        if (this.bgra is { } snapshot)
        {
            using var fromSnapshot = new CGBitmapContext(
                snapshot, this.Width, this.Height, 8, this.Width * 4, cs, flags);
            return fromSnapshot.ToImage();
        }

        // Biplanar: Core Image reads the two planes and does the colour conversion on the GPU. There is no
        // CPU path worth writing here — a hand-rolled YCbCr→RGB in managed code would be slower than the
        // conversion the capture pipeline was doing before we stopped asking it to.
        if (this.IsBiplanar)
        {
            using var image = new CIImage(this.pixelBuffer);
            return SharedContext.CreateCGImage(image, image.Extent);
        }

        this.pixelBuffer.Lock(CVPixelBufferLock.ReadOnly);
        try
        {
            using var ctx = new CGBitmapContext(
                this.pixelBuffer.BaseAddress, this.Width, this.Height, 8,
                (int)this.pixelBuffer.BytesPerRow, cs, flags);
            return ctx.ToImage();
        }
        finally
        {
            this.pixelBuffer.Unlock(CVPixelBufferLock.ReadOnly);
        }
    }

    /// <summary>
    /// Rec.601 luma, read straight from the capture buffer where the frame is borrowed.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Do not "optimize" this into a sub-sampled plane.</b> The contract is a full
    /// <see cref="CameraFrame.Width"/> x <see cref="CameraFrame.Height"/> plane and motion analyzers index
    /// into it by pixel. A consumer that only wants an average should stride over the result instead.
    /// </remarks>
    protected override byte[] MaterializeLuminance()
    {
        int w = this.Width, h = this.Height;
        var lum = new byte[w * h];

        if (this.bgra is { } snapshot)
        {
            Luma(snapshot, lum, w, h, w * 4);
            return lum;
        }

        // The whole plane, already luminance and already full resolution — a row-wise copy instead of two
        // million multiply-adds. This is the read that gets *faster* on a biplanar capture.
        if (this.IsBiplanar)
        {
            this.pixelBuffer.Lock(CVPixelBufferLock.ReadOnly);
            try
            {
                var stride = (int)this.pixelBuffer.GetBytesPerRowOfPlane(0);
                var plane = this.pixelBuffer.GetBaseAddress(0);
                for (var y = 0; y < h; y++)
                    Marshal.Copy(plane + y * stride, lum, y * w, w);

                if (this.videoRangeLuma)
                {
                    for (var i = 0; i < lum.Length; i++)
                        lum[i] = this.ExpandLuma(lum[i]);
                }

                return lum;
            }
            finally
            {
                this.pixelBuffer.Unlock(CVPixelBufferLock.ReadOnly);
            }
        }

        this.pixelBuffer.Lock(CVPixelBufferLock.ReadOnly);
        try
        {
            var bytesPerRow = (int)this.pixelBuffer.BytesPerRow;
            var row = new byte[bytesPerRow];
            var baseAddr = this.pixelBuffer.BaseAddress;

            // One row at a time rather than the whole plane: a reusable row buffer keeps this off the Large
            // Object Heap, where an 8 MB frame-sized array would land on every sample.
            for (var y = 0; y < h; y++)
            {
                Marshal.Copy(baseAddr + y * bytesPerRow, row, 0, bytesPerRow);
                Luma(row, lum, w, 1, bytesPerRow, destOffset: y * w);
            }
            return lum;
        }
        finally
        {
            this.pixelBuffer.Unlock(CVPixelBufferLock.ReadOnly);
        }
    }

    /// <summary>
    /// Reads only the sampled pixels out of the capture buffer — a thousand reads instead of two million,
    /// and no plane allocated at all.
    /// </summary>
    public override void SampleLuminance(Span<byte> destination, int columns, int rows)
    {
        if (this.bgra is { } snapshot)
        {
            SampleFrom(snapshot, destination, columns, rows, this.Width, this.Height, this.Width * 4);
            return;
        }

        if (this.IsBiplanar)
        {
            this.pixelBuffer.Lock(CVPixelBufferLock.ReadOnly);
            try
            {
                var stride = (int)this.pixelBuffer.GetBytesPerRowOfPlane(0);
                var plane = this.pixelBuffer.GetBaseAddress(0);
                var one = new byte[1];

                // One byte per sample instead of four, and no arithmetic at all: this plane is the answer.
                for (var r = 0; r < rows; r++)
                {
                    var y = SampleCoordinate(r, rows, this.Height);
                    for (var c = 0; c < columns; c++)
                    {
                        Marshal.Copy(plane + y * stride + SampleCoordinate(c, columns, this.Width), one, 0, 1);
                        destination[r * columns + c] = this.ExpandLuma(one[0]);
                    }
                }
            }
            finally
            {
                this.pixelBuffer.Unlock(CVPixelBufferLock.ReadOnly);
            }
            return;
        }

        this.pixelBuffer.Lock(CVPixelBufferLock.ReadOnly);
        try
        {
            var bytesPerRow = (int)this.pixelBuffer.BytesPerRow;
            var baseAddr = this.pixelBuffer.BaseAddress;
            var px = new byte[4];

            for (var r = 0; r < rows; r++)
            {
                var y = SampleCoordinate(r, rows, this.Height);
                for (var c = 0; c < columns; c++)
                {
                    var x = SampleCoordinate(c, columns, this.Width);
                    Marshal.Copy(baseAddr + y * bytesPerRow + x * 4, px, 0, 4);
                    destination[r * columns + c] = (byte)((px[2] * 77 + px[1] * 150 + px[0] * 29) >> 8);
                }
            }
        }
        finally
        {
            this.pixelBuffer.Unlock(CVPixelBufferLock.ReadOnly);
        }
    }

    static void SampleFrom(byte[] src, Span<byte> dest, int columns, int rows, int w, int h, int stride)
    {
        for (var r = 0; r < rows; r++)
        {
            var y = SampleCoordinate(r, rows, h);
            for (var c = 0; c < columns; c++)
            {
                var i = y * stride + SampleCoordinate(c, columns, w) * 4;
                dest[r * columns + c] = (byte)((src[i + 2] * 77 + src[i + 1] * 150 + src[i] * 29) >> 8);
            }
        }
    }

    static void Luma(byte[] src, byte[] dest, int w, int h, int srcStride, int destOffset = 0)
    {
        for (var y = 0; y < h; y++)
        {
            var s = y * srcStride;
            var d = destOffset + y * w;
            for (var x = 0; x < w; x++)
            {
                var b = src[s + x * 4];
                var g = src[s + x * 4 + 1];
                var r = src[s + x * 4 + 2];
                dest[d + x] = (byte)((r * 77 + g * 150 + b * 29) >> 8);
            }
        }
    }

    protected override void ReleaseNative()
    {
        this.bgra = null;

        // A converted frame owns the buffer it rendered into, and nothing else does.
        if (this.ownsPixelBuffer)
        {
            this.pixelBuffer.Dispose();
            return;
        }

        // Only a borrowed frame owns anything else. A copied one was handed buffers the delegate still owns
        // and disposes itself, so releasing them here would be a double free.
        if (this.owned is null)
            return;

        this.pixelBuffer.Dispose();
        this.owned.Dispose();
    }
}
