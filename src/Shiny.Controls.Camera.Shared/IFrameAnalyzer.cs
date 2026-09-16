namespace Shiny.Controls.Camera;

/// <summary>
/// A pluggable frame analyzer. Implementations inspect a <see cref="CameraFrame"/> and return the styled
/// <see cref="OverlayBox"/>es they want drawn over the preview (in normalized upright image space), while
/// surfacing their semantic result through their own strongly-typed event (e.g. a barcode analyzer's
/// decoded value). The pipeline runs each analyzer with a max-in-flight of one and drops frames while it
/// is busy, so an analyzer may take as long as a frame interval without backing up the camera.
/// Implementations must be allocation-light and must not retain the frame past the returned task.
/// </summary>
/// <remarks>
/// Prefer deriving from <c>FrameAnalyzer</c> (in <c>Shiny.Maui.Controls.Camera</c>), which implements this
/// interface, fires typed events/commands on the UI thread, and adds a <c>ShowBoundingBox</c> toggle.
/// </remarks>
public interface IFrameAnalyzer
{
    /// <summary>Stable identifier used to key/replace this analyzer's boxes in the overlay.</summary>
    string Id { get; }

    /// <summary>
    /// Whether this analyzer wants the frame that is about to be delivered. Called on the capture thread,
    /// once per frame, <b>before the platform materializes anything</b> — return <c>false</c> and the frame
    /// is skipped without being wrapped at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is where a rate limit belongs, and returning early from
    /// <see cref="AnalyzeAsync"/> is not the same thing.</b> By the time <c>AnalyzeAsync</c> runs, the
    /// platform has already built a <see cref="CameraFrame"/> for the buffer — which on Apple means a
    /// full-frame pixel copy (8.3 MB at 1080p). An analyzer that runs five passes a second but returns
    /// early from the other twenty-five was still paying for thirty frames a second; declaring the
    /// cadence here means it pays for five.
    /// </para>
    /// <para>
    /// Must be cheap and must not block: it runs on the capture callback, ahead of the encoder. Read a
    /// cached deadline, not a setting.
    /// </para>
    /// <para>
    /// The default returns <c>true</c> — every frame — which is the behaviour that existed before this
    /// member, so an analyzer written against an earlier version is unaffected.
    /// </para>
    /// </remarks>
    bool WantsFrame() => true;

    /// <summary>
    /// Analyze a single frame and return the boxes to draw for this analyzer. The returned set
    /// <b>replaces</b> this analyzer's previous boxes and persists across subsequent frames until it is
    /// next replaced; return <c>null</c> to <b>clear</b> them (nothing is currently seen). Raise any
    /// semantic result through the analyzer's own typed event before returning. Honor <paramref name="ct"/>
    /// for cooperative cancellation when the camera stops.
    /// </summary>
    ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeAsync(CameraFrame frame, CancellationToken ct);

    /// <summary>
    /// The analyzer has gone live: it was assigned to a running camera pipeline (and is enabled). Frames
    /// may arrive from now on. Default no-op.
    /// </summary>
    /// <remarks>
    /// Paired strictly with <see cref="OnDetached"/> — the two alternate, starting with this one, however many
    /// times the analyzer is attached, detached, disabled or re-enabled. Called on whichever thread changed the
    /// assignment (usually the UI thread); must be cheap and must not block. Prefer creating native resources
    /// lazily on the first <see cref="AnalyzeAsync"/> rather than here, so an analyzer that is attached but
    /// never sees a frame never pays for them.
    /// </remarks>
    void OnAttached() { }

    /// <summary>
    /// The analyzer is no longer live — removed from the camera (<c>Analyzer</c> reassigned or cleared),
    /// disabled via <c>IsEnabled = false</c>, or its camera handler disconnected. <b>Release native
    /// resources here</b> (e.g. close an Android ML Kit detector client); the analyzer may be attached again
    /// later, so re-create them lazily on the next <see cref="AnalyzeAsync"/>. Default no-op.
    /// </summary>
    /// <remarks>
    /// Never runs while an <see cref="AnalyzeAsync"/> pass is in flight: when the analyzer is detached
    /// mid-pass the call is deferred until that pass completes (on the analysis thread), and skipped
    /// altogether if the analyzer is re-attached before it does. So an implementation can close a client
    /// without racing its own in-flight use of it. Must be cheap and must not block, and must tolerate being
    /// called when nothing was ever created.
    /// </remarks>
    void OnDetached() { }
}
