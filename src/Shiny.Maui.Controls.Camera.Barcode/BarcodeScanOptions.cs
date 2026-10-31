using Shiny.Maui.Controls.Camera.Media;

namespace Shiny.Maui.Controls.Camera.Barcode;

/// <summary>
/// <see cref="MediaScanOptions"/> plus the barcode analyzer's own settings, for the
/// <see cref="MediaServiceBarcodeExtensions"/> scan verbs. Pass it wherever they take a
/// <see cref="MediaScanOptions"/>.
/// </summary>
/// <example>
/// <code>
/// var code = await media.ScanBarcodeAsync(options: new BarcodeScanOptions { Mode = BarcodeScanMode.LongRange, AutoZoom = true });
/// </code>
/// </example>
public class BarcodeScanOptions : MediaScanOptions
{
    /// <inheritdoc cref="BarcodeAnalyzer.Mode"/>
    public BarcodeScanMode Mode { get; set; } = BarcodeScanMode.Standard;

    /// <inheritdoc cref="BarcodeAnalyzer.AutoZoom"/>
    public bool AutoZoom { get; set; }
}
