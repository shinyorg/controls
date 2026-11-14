using Microsoft.Maui.Graphics;

namespace Shiny.Controls.Analysis;

/// <summary>The symbology of a scanned barcode. Mirrors the symbologies the native scanners can read.</summary>
public enum BarcodeFormat
{
    /// <summary>Unrecognized / not one of the named symbologies.</summary>
    Unknown,
    QrCode,
    Aztec,
    DataMatrix,
    Pdf417,
    Code128,
    Code39,
    Code93,
    Codabar,
    Ean8,
    Ean13,
    UpcA,
    UpcE,
    Itf
}

/// <summary>A barcode read from a frame by the native scanner, in normalized upright image space.</summary>
/// <param name="Value">The decoded payload text.</param>
/// <param name="Format">The barcode symbology.</param>
/// <param name="BoundingBox">Normalized bounds (0..1) of the barcode in upright image space.</param>
public record DetectedBarcode(string Value, BarcodeFormat Format, RectF BoundingBox);
