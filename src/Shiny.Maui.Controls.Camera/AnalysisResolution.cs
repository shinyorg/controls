namespace Shiny.Maui.Controls.Camera;

/// <summary>The size of the frames handed to <see cref="CameraView.Analyzer"/>, where the platform lets it differ from the preview.</summary>
/// <remarks>
/// <para>
/// Android only. CameraX sizes its analysis stream separately from the preview and, left alone, picks
/// 640x480 — fine for a QR code held up to the lens and hopeless for a shelf label across an aisle, where a
/// code's bars are already down to a pixel or two at 1080p. Apple analyzes the session's own frames, which
/// <see cref="CameraView.VideoQuality"/> sizes (1080p by default); Windows and macOS likewise.
/// </para>
/// <para>
/// Bigger frames cost every analyzer more per frame — the luminance copy alone is 2 MB at 1080p — so raise it
/// for analyzers that need the detail (long-range barcode, OCR at a distance), not as a default.
/// </para>
/// </remarks>
public enum AnalysisResolution
{
    /// <summary>Whatever the platform picks — 640x480 on Android.</summary>
    Default,

    /// <summary>About 1280x720, the closest the camera offers.</summary>
    Hd,

    /// <summary>About 1920x1080, the closest the camera offers.</summary>
    FullHd
}
