using System.Runtime.CompilerServices;

namespace Shiny.Maui.Controls.Camera.Internal;

/// <summary>
/// Drives <see cref="IFrameAnalyzer.OnAttached"/> / <see cref="IFrameAnalyzer.OnDetached"/> for an analyzer.
/// </summary>
/// <remarks>
/// <para>
/// The state is keyed on the <b>analyzer</b>, not the pipeline, because the two things that decide when a
/// native client may be closed are not owned by one pipeline: an analyzer can be attached to more than one
/// camera, and a pass started by a pipeline that has since let go of it can still be running.
/// </para>
/// <para>
/// An analyzer is <i>live</i> while at least one pipeline holds it attached-and-enabled <b>or</b> a pass is in
/// flight. <c>OnAttached</c> fires on the transition into live, <c>OnDetached</c> on the transition out, so the
/// two strictly alternate and <c>OnDetached</c> can never close a client out from under a running pass. A
/// detach that lands mid-pass is deferred to the end of that pass; a re-attach before then cancels it.
/// </para>
/// <para>
/// Hooks run under the per-analyzer lock — that is what guarantees the ordering. They are user code, so they
/// are guarded: a throwing hook must not take the camera down.
/// </para>
/// </remarks>
static class AnalyzerLifecycle
{
    sealed class State
    {
        public int Attachments;
        public int InFlight;
        public bool Live;
    }

    static readonly ConditionalWeakTable<IFrameAnalyzer, State> states = new();

    /// <summary>A pipeline now holds <paramref name="analyzer"/> attached and enabled.</summary>
    public static void Attach(IFrameAnalyzer analyzer)
    {
        var state = states.GetValue(analyzer, _ => new State());
        lock (state)
        {
            state.Attachments++;
            if (state.Live)
                return; // still live from a pass in flight (or another camera) — nothing was released

            state.Live = true;
            Invoke(analyzer.OnAttached);
        }
    }

    /// <summary>A pipeline let go of <paramref name="analyzer"/> (removed, disabled, or torn down).</summary>
    public static void Detach(IFrameAnalyzer analyzer)
    {
        var state = states.GetValue(analyzer, _ => new State());
        lock (state)
        {
            if (state.Attachments == 0)
                return; // unbalanced — never let a stray detach drive the count negative

            state.Attachments--;
            ReleaseIfIdle(analyzer, state);
        }
    }

    /// <summary>
    /// Reserve a pass. False when no pipeline holds the analyzer — a frame raced a detach through a stale
    /// runner — so the caller drops the frame rather than lazily re-creating a client nothing will release.
    /// </summary>
    public static bool TryBeginPass(IFrameAnalyzer analyzer)
    {
        var state = states.GetValue(analyzer, _ => new State());
        lock (state)
        {
            if (state.Attachments == 0)
                return false;

            state.InFlight++;
            return true;
        }
    }

    /// <summary>Release a pass reserved by <see cref="TryBeginPass"/>; runs a deferred detach.</summary>
    public static void EndPass(IFrameAnalyzer analyzer)
    {
        var state = states.GetValue(analyzer, _ => new State());
        lock (state)
        {
            if (state.InFlight > 0)
                state.InFlight--;
            ReleaseIfIdle(analyzer, state);
        }
    }

    static void ReleaseIfIdle(IFrameAnalyzer analyzer, State state)
    {
        if (!state.Live || state.Attachments > 0 || state.InFlight > 0)
            return;

        state.Live = false;
        Invoke(analyzer.OnDetached);
    }

    static void Invoke(Action hook)
    {
        try
        {
            hook();
        }
        catch
        {
            // a misbehaving analyzer must not tear down the pipeline
        }
    }
}
