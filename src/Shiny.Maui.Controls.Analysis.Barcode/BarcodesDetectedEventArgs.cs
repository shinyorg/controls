
namespace Shiny.Maui.Controls.Analysis.Barcode;

/// <summary>
/// Carries every barcode decoded in a single frame to <see cref="BarcodeAnalyzer.BarcodesDetected"/>
/// subscribers — a frame can hold several codes, so detection is always delivered as a set. For the common
/// single-code flow, <see cref="First"/> is the first (and usually only) code.
/// </summary>
/// <param name="barcodes">The decoded barcodes in the frame (never empty when raised).</param>
public class BarcodesDetectedEventArgs(IReadOnlyList<DetectedBarcode> barcodes) : EventArgs
{
    /// <summary>All barcodes decoded in this frame, in normalized upright image space.</summary>
    public IReadOnlyList<DetectedBarcode> Barcodes { get; } = barcodes;

    /// <summary>The first decoded barcode — convenient when you only expect one.</summary>
    public DetectedBarcode First => this.Barcodes[0];
}
