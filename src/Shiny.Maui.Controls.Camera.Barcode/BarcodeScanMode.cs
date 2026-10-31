namespace Shiny.Maui.Controls.Camera.Barcode;

/// <summary>How <see cref="BarcodeAnalyzer"/> reads a frame.</summary>
public enum BarcodeScanMode
{
    /// <summary>
    /// The native scanner over the whole frame, every frame — Apple Vision, Android ML Kit, ZXing.Net on
    /// Windows. The quickest read of a code that fills a good part of the frame.
    /// </summary>
    Standard,

    /// <summary>
    /// For codes far from the camera, small in frame, rotated or a little soft. Codes are located in the
    /// full-resolution frame before anything is decoded; 1D codes (EAN/UPC, Code 128, Code 39, ITF) are read
    /// by a blur-tolerant managed decoder down to about a pixel per module; 2D codes, and anything the
    /// managed decoder cannot read, go to the native scanner cut out and enlarged; codes are tracked across
    /// frames; and <see cref="BarcodeAnalyzer.AutoZoom"/> can pull the camera in on a code too small to
    /// read. The native scanner still runs over the whole frame every
    /// <see cref="BarcodeAnalyzer.FullFrameInterval"/> frames, so a close code is still found — a frame or two
    /// later than in <see cref="Standard"/>.
    /// </summary>
    LongRange
}
