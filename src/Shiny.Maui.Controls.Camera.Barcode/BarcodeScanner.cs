using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;

namespace Shiny.Maui.Controls.Camera.Barcode;

/// <summary>
/// Reusable native barcode scanner used by <see cref="BarcodeAnalyzer"/> and the document analyzers (the
/// driver's-license PDF417 read). Scans a frame with the platform engine — Apple Vision
/// (<c>VNDetectBarcodesRequest</c>) on iOS/macOS, Android MLKit (<c>BarcodeScanning</c>) — returning the
/// codes in normalized upright image space. A no-op on Windows and bare net10.0 (no native scanner).
/// </summary>
public partial class BarcodeScanner
{
    /// <summary>Restrict scanning to specific symbologies (null = all supported). Filters the native scanner.</summary>
    public IList<BarcodeFormat>? Formats { get; set; }

    /// <summary>
    /// Optional region of interest in normalized upright space (0..1); null scans the whole frame. Honored
    /// natively where possible — Apple Vision's <c>regionOfInterest</c> on iOS/macOS, and a Y-plane crop fed to
    /// MLKit on Android — so the engine only decodes that band (faster, and it can't pick up codes outside it).
    /// A no-op on Windows / bare net10.0 (no native scanner).
    /// </summary>
    public RectF? ScanWindow { get; set; }

    /// <summary>Scan the frame for barcodes. Returns an empty list when none are found / unsupported.</summary>
    public partial Task<List<DetectedBarcode>> ScanAsync(CameraFrame frame, CancellationToken ct);

    /// <summary>
    /// Release the native scanner client — on Android, closes the ML Kit <c>BarcodeScanner</c>, which holds
    /// native detector memory until closed. The scanner stays usable: the next <see cref="ScanAsync"/> creates
    /// a fresh client. A no-op elsewhere (Apple Vision requests are per scan; Windows / net10.0 have no client).
    /// Do not call while a <see cref="ScanAsync"/> is in flight. The built-in analyzers call this from
    /// <see cref="FrameAnalyzer"/>'s <c>OnDetached</c>, which the camera pipeline already defers past any
    /// in-flight pass.
    /// </summary>
    public void ReleaseNativeResources() => this.ReleasePlatform();

    partial void ReleasePlatform();
}
