namespace Shiny.Maui.Controls.Analysis;

/// <summary>What kind of source an analysis host feeds its analyzers.</summary>
public enum AnalysisSourceKind
{
    /// <summary>A live camera (<c>CameraView</c>).</summary>
    Camera,

    /// <summary>Played media — a file, a stream (<c>MediaElement</c>).</summary>
    Media
}

/// <summary>
/// A control that runs analyzers — <c>CameraView</c> and <c>MediaElement</c>. Handed to a <see cref="FrameAnalyzer"/>
/// or <see cref="AudioAnalyzer"/> as its <c>Host</c>, so an analyzer can tell what it is looking at without
/// referencing either control.
/// </summary>
public interface IAnalysisHost
{
    /// <summary>Whether the frames and audio come from a camera or from played media.</summary>
    AnalysisSourceKind SourceKind { get; }

    /// <summary>
    /// <c>true</c> when the source cannot be sought: a camera, or a live stream. A transcript or an index built over
    /// a live source can only grow; over seekable media it can be revisited.
    /// </summary>
    bool IsLive { get; }
}

/// <summary>
/// A host whose analyzers may steer what the source captures — the camera's zoom and the size of its analysis
/// frames. Implemented by <c>CameraView</c>; a <c>MediaElement</c> cannot change what is in its video, so it does
/// not implement this and analyzers that would steer simply don't.
/// </summary>
/// <remarks>Read and write these on the UI thread.</remarks>
public interface IZoomableAnalysisHost : IAnalysisHost
{
    /// <summary>The current zoom factor.</summary>
    double Zoom { get; set; }

    /// <summary>The smallest zoom factor the source supports.</summary>
    double MinZoom { get; }

    /// <summary>The largest zoom factor the source supports.</summary>
    double MaxZoom { get; }

    /// <summary>The size of the frames handed to analyzers.</summary>
    AnalysisResolution AnalysisResolution { get; set; }
}
