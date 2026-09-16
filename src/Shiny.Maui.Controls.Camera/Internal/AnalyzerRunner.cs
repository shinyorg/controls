namespace Shiny.Maui.Controls.Camera.Internal;

/// <summary>
/// Wraps one <see cref="IFrameAnalyzer"/> with a max-in-flight of one. A frame submitted while the
/// analyzer is still working is dropped for this analyzer, giving each analyzer independent backpressure
/// (a slow OCR pass never stalls a fast barcode pass). On accept it retains the shared frame and disposes
/// it when the analysis completes. Forwards every completed result — including <c>null</c> (clear) — to the
/// pipeline.
/// </summary>
sealed class AnalyzerRunner(IFrameAnalyzer analyzer, Action<string, IReadOnlyList<OverlayBox>?> onResult)
{
    int busy;

    public string Id => analyzer.Id;

    /// <summary>The wrapped analyzer (so the pipeline can read its <c>IsEnabled</c> / observe its changes).</summary>
    public IFrameAnalyzer Analyzer => analyzer;

    /// <summary>Cached enabled state, refreshed by the pipeline so frame dispatch never reads a bindable off-thread.</summary>
    public volatile bool Enabled = true;

    /// <summary>
    /// Whether a pass is in flight. Read by the pipeline <b>before</b> the platform materializes a frame, so
    /// a frame that <see cref="TrySubmit"/> would only drop is never built in the first place — on Apple
    /// that is a full-frame copy saved for every frame that lands during a slow pass.
    /// </summary>
    public bool Busy => Volatile.Read(ref this.busy) != 0;

    /// <summary>Submit a frame. Returns false (frame untouched) when the analyzer is busy.</summary>
    public bool TrySubmit(CameraFrame frame, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref this.busy, 1, 0) != 0)
            return false;

        // a frame that raced a detach through this (now stale) runner is dropped: running it would lazily
        // re-create a native client after OnDetached released it, and nothing would release it again
        if (!AnalyzerLifecycle.TryBeginPass(analyzer))
        {
            Interlocked.Exchange(ref this.busy, 0);
            return false;
        }

        frame.Retain();
        _ = this.RunAsync(frame, ct);
        return true;
    }

    async Task RunAsync(CameraFrame frame, CancellationToken ct)
    {
        try
        {
            var result = await analyzer.AnalyzeAsync(frame, ct).ConfigureAwait(false);
            onResult(analyzer.Id, result);
        }
        catch
        {
            // a misbehaving analyzer must not tear down the pipeline
        }
        finally
        {
            frame.Dispose();
            Interlocked.Exchange(ref this.busy, 0);
            // runs a detach that was deferred because it landed mid-pass
            AnalyzerLifecycle.EndPass(analyzer);
        }
    }
}
