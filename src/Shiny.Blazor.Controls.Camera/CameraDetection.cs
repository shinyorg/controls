using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;

namespace Shiny.Blazor.Controls.Camera;

/// <summary>
/// Flat, primitives-only DTO marshaled from the camera JS module into <c>[JSInvokable]</c> callbacks for a
/// styled overlay box. Kept separate from <see cref="OverlayBox"/> (which uses <see cref="RectF"/> /
/// <see cref="Color"/>) so JS interop never (de)serializes a struct — see the repo rule against
/// anonymous/complex interop types. Coordinates are normalized 0..1 in upright video space; colors are hex.
/// </summary>
public class CameraOverlayBox
{
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }
    public string? StrokeColor { get; set; }
    public string? Text { get; set; }
    public string? TextColor { get; set; }

    /// <summary>Project to the shared <see cref="OverlayBox"/> record.</summary>
    public OverlayBox ToOverlayBox() => new(
        new RectF(this.X, this.Y, this.W, this.H),
        Parse(this.StrokeColor),
        this.Text,
        Parse(this.TextColor)
    );

    static Color? Parse(string? hex) => string.IsNullOrEmpty(hex) ? null : Color.FromArgb(hex);
}


/// <summary>
/// The JPEG of a detected document plus its normalized bounds, returned from
/// <see cref="CameraView.RequestDocumentImageAsync"/>. The bytes are the (padded) document crop, ready to send
/// to a vision model.
/// </summary>
/// <param name="Jpeg">The cropped document image as JPEG bytes.</param>
/// <param name="Bounds">The document's normalized bounds (0..1) in upright video space.</param>
public record CameraDocumentImage(byte[] Jpeg, RectF Bounds);


/// <summary>Flat DTO for the typed barcode callback marshaled from the JS <c>BarcodeDetector</c>.</summary>
public class CameraBarcode
{
    /// <summary>The barcode symbology reported by the browser (e.g. "qr_code", "ean_13").</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>The decoded payload text.</summary>
    public string Value { get; set; } = string.Empty;

    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }

    /// <summary>
    /// Project to the shared <see cref="DetectedBarcode"/> the MAUI scanner returns, mapping the browser's format
    /// names (<c>qr_code</c>, <c>ean_13</c>, …) onto <see cref="BarcodeFormat"/>.
    /// </summary>
    public DetectedBarcode ToDetectedBarcode()
        => new(this.Value, ParseFormat(this.Format), new RectF(this.X, this.Y, this.W, this.H));

    internal static BarcodeFormat ParseFormat(string? format) => format switch
    {
        "qr_code" => BarcodeFormat.QrCode,
        "aztec" => BarcodeFormat.Aztec,
        "data_matrix" => BarcodeFormat.DataMatrix,
        "pdf417" => BarcodeFormat.Pdf417,
        "code_128" => BarcodeFormat.Code128,
        "code_39" => BarcodeFormat.Code39,
        "code_93" => BarcodeFormat.Code93,
        "codabar" => BarcodeFormat.Codabar,
        "ean_8" => BarcodeFormat.Ean8,
        "ean_13" => BarcodeFormat.Ean13,
        "upc_a" => BarcodeFormat.UpcA,
        "upc_e" => BarcodeFormat.UpcE,
        "itf" => BarcodeFormat.Itf,
        _ => BarcodeFormat.Unknown
    };
}
