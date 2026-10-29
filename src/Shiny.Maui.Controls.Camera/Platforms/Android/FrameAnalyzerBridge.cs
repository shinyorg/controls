using AndroidX.Camera.Core;

namespace Shiny.Maui.Controls.Camera;

// CameraX ImageAnalysis.Analyzer that wraps each ImageProxy into an AndroidCameraFrame and feeds the
// shared pipeline. The pipeline owns the frame and closes the proxy once all analyzers finish.
sealed class FrameAnalyzerBridge(CameraViewHandler handler) : Java.Lang.Object, ImageAnalysis.IAnalyzer
{
    public void Analyze(IImageProxy image)
    {
        // WantsFrame, not HasAnalyzer: closing the proxy immediately hands the buffer straight back to
        // CameraX rather than holding it open through a pass the analyzer's cadence was going to skip.
        if (!handler.Pipeline.WantsFrame())
        {
            image.Close();
            return;
        }

        var mirrored = handler.MaybeVirtualView?.Facing == CameraFacing.Front;
        handler.Pipeline.Process(new AndroidCameraFrame(image, mirrored), default);
    }

    // The other Analyzer members are Java default methods. The generated Java wrapper still overrides them and
    // calls into .NET, where the binding's default body has nothing left to call - binding ImageAnalysis then
    // throws AbstractMethodError (getDefaultTargetResolution) and the preview stays black. These return what
    // the Java defaults return.
    public global::Android.Util.Size? DefaultTargetResolution => null;

    public int TargetCoordinateSystem => ImageAnalysis.CoordinateSystemOriginal;

    public void UpdateTransform(global::Android.Graphics.Matrix? matrix)
    {
    }
}
