namespace Shiny.Controls.BarcodeReader;

/// <summary>Symbologies the engine knows about. The engine decodes the linear ones itself; the rest go to a native decoder.</summary>
public enum Symbology
{
    /// <summary>Not one of the named symbologies.</summary>
    Unknown,
    /// <summary>EAN-13.</summary>
    Ean13,
    /// <summary>UPC-A (an EAN-13 with a leading zero).</summary>
    UpcA,
    /// <summary>EAN-8.</summary>
    Ean8,
    /// <summary>UPC-E.</summary>
    UpcE,
    /// <summary>Code 128 and GS1-128.</summary>
    Code128,
    /// <summary>Code 39.</summary>
    Code39,
    /// <summary>Interleaved 2 of 5, including ITF-14.</summary>
    Itf,
    // read by the native decoder on a cropped candidate, never by the engine
    /// <summary>QR code.</summary>
    QrCode,
    /// <summary>Data Matrix.</summary>
    DataMatrix,
    /// <summary>Aztec.</summary>
    Aztec,
    /// <summary>PDF417.</summary>
    Pdf417,
    /// <summary>Code 93.</summary>
    Code93,
    /// <summary>Codabar.</summary>
    Codabar
}

/// <summary>The shape of texture a candidate was found by.</summary>
public enum CandidateKind
{
    /// <summary>Parallel bars — a 1D code (or a PDF417, which is made of them).</summary>
    Linear,
    /// <summary>Busy texture with no dominant direction — a 2D code, or text.</summary>
    Matrix
}

/// <summary>A region the locator thinks holds a barcode, in pixels of the luminance plane.</summary>
/// <param name="Kind">What texture found it.</param>
/// <param name="Box">The region; for <see cref="CandidateKind.Linear"/> its angle runs across the bars.</param>
/// <param name="Score">Relative strength — texture energy times how well it fits the kind.</param>
public sealed record BarcodeCandidate(CandidateKind Kind, OrientedBox Box, float Score)
{
    /// <summary>Set when the engine decoded it.</summary>
    public BarcodeRead? Read { get; internal set; }

    /// <summary>
    /// The average element (bar or space) width measured across the candidate, pixels, or 0 when it was never
    /// sampled. Below about 1 px a linear code is too small to read and needs zooming in on.
    /// </summary>
    public float ElementWidth { get; internal set; }
}

/// <summary>A decoded barcode.</summary>
/// <param name="Text">The payload.</param>
/// <param name="Symbology">The symbology.</param>
/// <param name="Box">Where it was read, in pixels.</param>
/// <param name="Votes">How many scanlines agreed on it (1 for a fused or native read).</param>
/// <param name="IsChecksummed">The symbology carries a check digit that was verified.</param>
public sealed record BarcodeRead(string Text, Symbology Symbology, OrientedBox Box, int Votes, bool IsChecksummed);
