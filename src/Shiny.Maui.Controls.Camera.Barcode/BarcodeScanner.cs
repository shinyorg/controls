using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;

namespace Shiny.Maui.Controls.Camera.Barcode;

/// <summary>
/// Reusable native barcode scanner used by <see cref="BarcodeAnalyzer"/> and the document analyzers (the
/// driver's-license PDF417 read). Scans a frame with the platform engine — Apple Vision
/// (<c>VNDetectBarcodesRequest</c>) on iOS/macOS, Android MLKit (<c>BarcodeScanning</c>) — returning the
/// codes in normalized upright image space. Windows (and bare net10.0) has no camera-based native scanner, so
/// there the frame's luminance plane is decoded with the managed ZXing.Net reader instead.
/// </summary>
public partial class BarcodeScanner
{
    /// <summary>Restrict scanning to specific symbologies (null = all supported). Filters the native scanner.</summary>
    public IList<BarcodeFormat>? Formats { get; set; }

    /// <summary>
    /// Optional region of interest in normalized upright space (0..1); null scans the whole frame. Honored
    /// natively where possible — Apple Vision's <c>regionOfInterest</c> on iOS/macOS, and a Y-plane crop fed to
    /// MLKit on Android, and a luminance crop fed to ZXing on Windows — so the engine only decodes that band
    /// (faster, and it can't pick up codes outside it).
    /// </summary>
    public RectF? ScanWindow { get; set; }

    /// <summary>Scan the frame for barcodes. Returns an empty list when none are found / unsupported.</summary>
    public partial Task<List<DetectedBarcode>> ScanAsync(CameraFrame frame, CancellationToken ct);

    /// <summary>
    /// Scan an 8-bit greyscale image (<paramref name="width"/> x <paramref name="height"/>, rows packed) with the
    /// same engine. Boxes come back normalized to that image, as it lies — no rotation or mirroring is applied.
    /// <see cref="ScanWindow"/> is ignored; crop before calling.
    /// </summary>
    /// <remarks>
    /// For handing a decoder one region at a size it can read. The native engines shrink a whole frame before
    /// they look at it, so a code a long way off arrives with its modules thinner than a pixel; cut out and
    /// enlarged first, the same code reads. The long-range analyzer feeds its candidates through here.
    /// </remarks>
    public partial Task<List<DetectedBarcode>> ScanLuminanceAsync(byte[] luminance, int width, int height, CancellationToken ct);

    /// <summary>
    /// Release the native scanner client — on Android, closes the ML Kit <c>BarcodeScanner</c>, which holds
    /// native detector memory until closed. The scanner stays usable: the next <see cref="ScanAsync"/> creates
    /// a fresh client. A no-op elsewhere (Apple Vision requests are per scan; ZXing readers are per scan).
    /// Do not call while a <see cref="ScanAsync"/> is in flight. The built-in analyzers call this from
    /// <see cref="FrameAnalyzer"/>'s <c>OnDetached</c>, which the camera pipeline already defers past any
    /// in-flight pass.
    /// </summary>
    public void ReleaseNativeResources() => this.ReleasePlatform();

    partial void ReleasePlatform();
}
