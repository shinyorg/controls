using System.ComponentModel;
using Microsoft.Maui.Graphics;

namespace Shiny.Blazor.Controls.Analysis;

/// <summary>
/// An overlay box as the JS detectors report it. Public, with plain properties, because it crosses the JS interop
/// boundary: the trimmer only keeps what a declared parameter type names.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class AnalysisBoxDto
{
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }
    public string? StrokeColor { get; set; }
    public string? Text { get; set; }
    public string? TextColor { get; set; }

    public OverlayBox ToOverlayBox() => new(new RectF(this.X, this.Y, this.W, this.H), ParseColor(this.StrokeColor), this.Text, ParseColor(this.TextColor));

    static Color? ParseColor(string? hex) => String.IsNullOrEmpty(hex) ? null : Color.FromArgb(hex);
}

/// <summary>A barcode as the browser's <c>BarcodeDetector</c> reports it. Public for trim-safe interop.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class AnalysisBarcodeDto
{
    public string Format { get; set; } = String.Empty;
    public string Value { get; set; } = String.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }

    public DetectedBarcode ToDetectedBarcode() => new(this.Value, BrowserBarcodeFormats.Parse(this.Format), new RectF(this.X, this.Y, this.W, this.H));
}

/// <summary>Maps the browser <c>BarcodeDetector</c> format names onto <see cref="BarcodeFormat"/>.</summary>
public static class BrowserBarcodeFormats
{
    /// <summary>The <see cref="BarcodeFormat"/> for a <c>BarcodeDetector</c> format string.</summary>
    public static BarcodeFormat Parse(string? format) => format switch
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
