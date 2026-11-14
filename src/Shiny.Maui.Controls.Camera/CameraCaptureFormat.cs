namespace Shiny.Maui.Controls.Camera;

/// <summary>
/// The pixel format the capture pipeline is asked to deliver frames in. Set via
/// <see cref="CameraView.CaptureFormat"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a cost, not a preference, and the cost is paid the whole time the preview is up.</b> Apple's
/// camera does not natively produce BGRA — it produces biplanar YCbCr. Asking an
/// <c>AVCaptureVideoDataOutput</c> for <c>32BGRA</c> makes the capture pipeline colour-convert every frame
/// at frame rate, whether or not anything is recording, whether or not an analyzer is attached, and the
/// hardware encoder then converts it back to YUV to write the file. A burned-in recording pays that
/// conversion twice per frame; the raw <c>AVCaptureMovieFileOutput</c> path pays it zero times.
/// </para>
/// <para>
/// ⚠️ <b>The reason this is opt-in rather than the default is that BGRA is what a CPU can draw on.</b> An
/// overlay composited through <see cref="ICompositedVideoOverlayRenderer"/> never touches the CPU and is
/// unaffected. An <c>IDrawEffect</c>, or any overlay that falls back to <c>DrawOverlay</c>, needs a
/// <c>CGBitmapContext</c> — which cannot be built over a biplanar buffer, so the recorder converts to a
/// scratch surface and back, per frame. That is slower than simply capturing BGRA in the first place.
/// </para>
/// <para>Android and Windows ignore this: neither delivers a choice worth making here.</para>
/// </remarks>
public enum CameraCaptureFormat
{
    /// <summary>
    /// 32-bit BGRA. Works with every path including CPU-drawn overlays, and is what every release before
    /// this one did.
    /// </summary>
    Bgra32,

    /// <summary>
    /// The camera's own biplanar YCbCr (NV12) where the device offers it, falling back to
    /// <see cref="Bgra32"/> where it does not.
    /// </summary>
    /// <remarks>
    /// Best paired with <see cref="ICompositedVideoOverlayRenderer"/> and no draw effects. Luminance reads
    /// on <see cref="VideoFrame"/> get cheaper — the first plane already <i>is</i> luminance — while
    /// <c>ToCGImage</c> becomes a GPU conversion paid only on frames an analyzer actually takes.
    /// </remarks>
    Yuv420
}
