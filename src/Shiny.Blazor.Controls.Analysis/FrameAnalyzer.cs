using Microsoft.Maui.Graphics;

namespace Shiny.Blazor.Controls.Analysis;

/// <summary>
/// Base for the single in-browser frame analyzer assigned to a <c>CameraView</c>'s or a <c>MediaElement</c>'s
/// <c>Analyzer</c> — the same descriptor works on either. Mirrors the MAUI <c>FrameAnalyzer</c> shape (one analyzer
/// at a time, a <see cref="ScanWindow"/>, a box toggle) within what the browser can do: <see cref="BarcodeAnalyzer"/>
/// (<c>BarcodeDetector</c>) and <see cref="DocumentAnalyzer"/> run in JS; <see cref="FaceAnalyzer"/> is a placeholder
/// that reports "not supported". The browser has no OCR engine, so there is no OCR analyzer.
/// </summary>
public abstract class FrameAnalyzer
{
    /// <summary>Stable kind discriminator passed to the JS module (e.g. <c>"barcode"</c>).</summary>
    public abstract string Kind { get; }

    /// <summary>
    /// Restricts both detection and the overlay to a normalized rectangle (0..1) in upright video space; null
    /// scans the whole frame. Only detections centered inside it are reported and drawn, and the overlay dims
    /// outside it and frames a viewfinder reticle.
    /// </summary>
    public RectF? ScanWindow { get; set; }

    /// <summary>Whether this analyzer's bounding boxes are drawn. Default <c>true</c>.</summary>
    public bool ShowBoundingBox { get; set; } = true;

    // flat [x, y, w, h] (or null) — primitives only, so it's trim-safe across JS interop
    internal float[]? ScanWindowArray()
        => this.ScanWindow is { } w ? [w.X, w.Y, w.Width, w.Height] : null;
}

/// <summary>
/// Decodes 1D/2D barcodes and QR codes in-browser via the native <c>BarcodeDetector</c> (Chromium/Android;
/// a no-op with an error on Firefox/Safari). The host raises its <c>BarcodesDetected</c> with every code in view. Set <see cref="FrameAnalyzer.ScanWindow"/> to restrict scanning to a region.
/// </summary>
public class BarcodeAnalyzer : FrameAnalyzer
{
    /// <inheritdoc/>
    public override string Kind => "barcode";
}

/// <summary>Placeholder face analyzer — no in-browser engine is wired yet; assigning it reports "not supported".</summary>
public class FaceAnalyzer : FrameAnalyzer
{
    /// <inheritdoc/>
    public override string Kind => "face";
}

/// <summary>
/// Detects when a document is <i>present</i> in the frame (a cheap in-browser luminance/edge heuristic — no
/// OCR), so the (slow, paid) AI extraction only runs on a real document. Pair it with the camera's
/// <c>RequestDocumentImageAsync</c> to get the cropped JPEG of the next steadily-present
/// document, then send that to a Microsoft.Extensions.AI <c>IChatClient</c> — see the
/// <c>Shiny.Blazor.Controls.Camera.Ai</c> package's <c>AiDocumentScanner</c>. Set
/// <see cref="FrameAnalyzer.ScanWindow"/> to require the document inside an aim region.
/// </summary>
public class DocumentAnalyzer : FrameAnalyzer
{
    /// <inheritdoc/>
    public override string Kind => "document";
}
