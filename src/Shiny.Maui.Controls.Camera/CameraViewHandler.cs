#if ANDROID || IOS || MACCATALYST || WINDOWS || MACOS
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Shiny.Maui.Controls.Camera.Internal;

namespace Shiny.Maui.Controls.Camera;

public partial class CameraViewHandler
{
    internal readonly CameraPipeline Pipeline = new();

    /// <summary>
    /// The bound <see cref="CameraView"/>, or <c>null</c> once the handler has been disconnected.
    /// </summary>
    /// <remarks>
    /// <b>Use this, not <c>VirtualView?.</c>, from anything that can outlive the handler.</b>
    /// <c>ViewHandler&lt;TVirtualView, TPlatformView&gt;.VirtualView</c> <i>throws</i>
    /// <c>InvalidOperationException("VirtualView cannot be null here")</c> when disconnected — it does not
    /// return null — so the null-conditional in <c>VirtualView?.X</c> never gets a chance: the exception
    /// comes out of the property getter first. In a callback dispatched to the UI thread that surfaces as an
    /// unhandled exception and the app aborts (SIGABRT on iOS). Reported by a consumer navigating away from
    /// a page while the camera was still delivering frames.
    /// </remarks>
    internal CameraView? MaybeVirtualView => ((IElementHandler)this).VirtualView as CameraView;

    // Wire overlay delivery (marshaled to the UI thread) and bind the active analyzer to the control's
    // single Analyzer property. Call from each platform's ConnectHandler.
    private protected void InitPipeline()
    {
        this.Pipeline.OnOverlays = (boxes, window, w, h) =>
            this.MaybeVirtualView?.Dispatcher.Dispatch(
                () => this.MaybeVirtualView?.OnOverlaysChanged(boxes, window, w, h));

        // the active analyzer changed (assignment or an IsEnabled toggle); platforms that bind use cases
        // up-front (Android) re-evaluate, others no-op
        this.Pipeline.OnActiveChanged = () => this.OnAnalyzersSynced();

        // the analyzer raises its typed events on the UI thread via this dispatcher
        this.Pipeline.SetDispatcher(a => this.MaybeVirtualView?.Dispatcher.Dispatch(a));

        this.SyncAnalyzer();
    }

    private protected void TeardownPipeline()
    {
        this.Pipeline.SetAnalyzer(null);
    }

    void SyncAnalyzer() => this.Pipeline.SetAnalyzer(this.MaybeVirtualView?.Analyzer);

    public static IPropertyMapper<CameraView, CameraViewHandler> Mapper =
        new PropertyMapper<CameraView, CameraViewHandler>(ViewHandler.ViewMapper)
        {
            [nameof(CameraView.Facing)] = MapFacing,
            [nameof(CameraView.CameraId)] = MapFacing,
            [nameof(CameraView.IsActive)] = MapIsActive,
            [nameof(CameraView.IsTorchOn)] = MapTorch,
            [nameof(CameraView.FlashMode)] = MapFlashMode,
            [nameof(CameraView.Zoom)] = MapZoom,
            [nameof(CameraView.ScaleMode)] = MapScaleMode,
            [nameof(CameraView.ShowDetectionOverlay)] = MapOverlay,
            [nameof(CameraView.Overlays)] = MapOverlay,
            [nameof(CameraView.ScanWindow)] = MapOverlay,
            [nameof(CameraView.Analyzer)] = MapAnalyzer,
            [nameof(CameraView.Filter)] = MapEffects,
            [nameof(CameraView.AnalyzerSeesEffects)] = MapEffects,
            [nameof(CameraView.Effects)] = MapEffects,
            [nameof(CameraView.PhotoQuality)] = MapPhotoQuality,
            [nameof(CameraView.VideoQuality)] = MapVideoQuality,
            [nameof(CameraView.VideoBitrate)] = MapVideoQuality,
            [nameof(CameraView.VideoFrameRate)] = MapVideoQuality,
            [nameof(CameraView.Orientation)] = MapOrientation,
            [nameof(CameraView.CaptureFormat)] = MapCaptureFormat,
        };

    static void MapAnalyzer(CameraViewHandler handler, CameraView view) => handler.SyncAnalyzer();

    public static CommandMapper<CameraView, CameraViewHandler> CommandMapper =
        new(ViewHandler.ViewCommandMapper);

    public CameraViewHandler() : base(Mapper, CommandMapper)
    {
    }

    // Implemented only where binding the analyzer use case is decided up-front (Android); elsewhere elided.
    partial void OnAnalyzersSynced();

    static partial void MapFacing(CameraViewHandler handler, CameraView view);
    static partial void MapIsActive(CameraViewHandler handler, CameraView view);
    static partial void MapTorch(CameraViewHandler handler, CameraView view);
    static partial void MapFlashMode(CameraViewHandler handler, CameraView view);
    static partial void MapZoom(CameraViewHandler handler, CameraView view);
    static partial void MapScaleMode(CameraViewHandler handler, CameraView view);
    static partial void MapOverlay(CameraViewHandler handler, CameraView view);
    // Backs both Filter and Effects — they resolve into one CameraEffectChain, so one mapper reapplies the
    // whole chain rather than two that each try to own part of it.
    static partial void MapEffects(CameraViewHandler handler, CameraView view);

    // Backs VideoQuality, VideoBitrate and VideoFrameRate — all three are fixed when the session is
    // configured, so one mapper reapplies the set of them rather than three that each reconfigure.
    static partial void MapVideoQuality(CameraViewHandler handler, CameraView view);

    // Implemented on Android only. Apple and Windows read PhotoQuality when the shutter fires, so there is
    // nothing to apply ahead of time; CameraX fixes the capture mode when ImageCapture is built, so Android
    // has to rebind its use cases. PhotoJpegQuality is deliberately absent from the mapper for the same
    // reason — every platform reads it at capture time.
    static partial void MapPhotoQuality(CameraViewHandler handler, CameraView view);

    // Implemented on Android and Apple only; macOS and Windows have no rotating display behind the capture
    // device, so the partial is elided there rather than being a no-op body on each.
    static partial void MapOrientation(CameraViewHandler handler, CameraView view);

    // Apple only: it is the one platform whose capture output is asked for a pixel format at all. Android's
    // CameraX and Windows' MediaCapture each deliver what they deliver.
    static partial void MapCaptureFormat(CameraViewHandler handler, CameraView view);
}
#endif
