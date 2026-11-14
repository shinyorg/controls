using System.Runtime.CompilerServices;

namespace Shiny.Maui.Controls.Analysis.Internal;

/// <summary>
/// Drives <c>OnAttached</c> / <c>OnDetached</c> for an analyzer — an <see cref="IFrameAnalyzer"/> or an
/// <see cref="IAudioAnalyzer"/>; one instance may even be both.
/// </summary>
/// <remarks>
/// <para>
/// The state is keyed on the <b>analyzer</b>, not the pipeline, because the two things that decide when a
/// native client may be closed are not owned by one pipeline: an analyzer can be attached to more than one
/// host (a camera and a player), and a pass started by a pipeline that has since let go of it can still be running.
/// </para>
/// <para>
/// An analyzer is <i>live</i> while at least one pipeline holds it attached-and-enabled <b>or</b> a pass is in
/// flight. <c>OnAttached</c> fires on the transition into live, <c>OnDetached</c> on the transition out, so the
/// two strictly alternate and <c>OnDetached</c> can never close a client out from under a running pass. A
/// detach that lands mid-pass is deferred to the end of that pass; a re-attach before then cancels it.
/// </para>
/// <para>
/// Hooks run under the per-analyzer lock — that is what guarantees the ordering. They are user code, so they
/// are guarded: a throwing hook must not take the host down.
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

    // keyed per (analyzer, kind): an object that implements both analyzer interfaces is attached to the frame
    // pipeline and the audio pipeline independently, and each pairs its own hooks
    static readonly ConditionalWeakTable<object, State> frameStates = new();
    static readonly ConditionalWeakTable<object, State> audioStates = new();

    public static void Attach(IFrameAnalyzer analyzer) => Attach(frameStates, analyzer, analyzer.OnAttached);
    public static void Detach(IFrameAnalyzer analyzer) => Detach(frameStates, analyzer, analyzer.OnDetached);
    public static bool TryBeginPass(IFrameAnalyzer analyzer) => TryBeginPass(frameStates, analyzer);
    public static void EndPass(IFrameAnalyzer analyzer) => EndPass(frameStates, analyzer, analyzer.OnDetached);

    public static void Attach(IAudioAnalyzer analyzer) => Attach(audioStates, analyzer, analyzer.OnAttached);
    public static void Detach(IAudioAnalyzer analyzer) => Detach(audioStates, analyzer, analyzer.OnDetached);
    public static bool TryBeginPass(IAudioAnalyzer analyzer) => TryBeginPass(audioStates, analyzer);
    public static void EndPass(IAudioAnalyzer analyzer) => EndPass(audioStates, analyzer, analyzer.OnDetached);

    /// <summary>A pipeline now holds <paramref name="analyzer"/> attached and enabled.</summary>
    static void Attach(ConditionalWeakTable<object, State> states, object analyzer, Action onAttached)
    {
        var state = states.GetValue(analyzer, _ => new State());
        lock (state)
        {
            state.Attachments++;
            if (state.Live)
                return; // still live from a pass in flight (or another camera) — nothing was released

            state.Live = true;
            Invoke(onAttached);
        }
    }

    /// <summary>A pipeline let go of <paramref name="analyzer"/> (removed, disabled, or torn down).</summary>
    static void Detach(ConditionalWeakTable<object, State> states, object analyzer, Action onDetached)
    {
        var state = states.GetValue(analyzer, _ => new State());
        lock (state)
        {
            if (state.Attachments == 0)
                return; // unbalanced — never let a stray detach drive the count negative

            state.Attachments--;
            ReleaseIfIdle(state, onDetached);
        }
    }

    /// <summary>
    /// Reserve a pass. False when no pipeline holds the analyzer — a frame raced a detach through a stale
    /// runner — so the caller drops the frame rather than lazily re-creating a client nothing will release.
    /// </summary>
    static bool TryBeginPass(ConditionalWeakTable<object, State> states, object analyzer)
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
    static void EndPass(ConditionalWeakTable<object, State> states, object analyzer, Action onDetached)
    {
        var state = states.GetValue(analyzer, _ => new State());
        lock (state)
        {
            if (state.InFlight > 0)
                state.InFlight--;
            ReleaseIfIdle(state, onDetached);
        }
    }

    static void ReleaseIfIdle(State state, Action onDetached)
    {
        if (!state.Live || state.Attachments > 0 || state.InFlight > 0)
            return;

        state.Live = false;
        Invoke(onDetached);
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
