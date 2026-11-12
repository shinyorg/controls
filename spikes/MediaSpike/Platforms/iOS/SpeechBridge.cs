using System.Runtime.InteropServices;

namespace MediaSpike;

/// <summary>SPIKE: [DllImport] over the Swift ShinySpeechBridge.framework (SpeechAnalyzer / SpeechTranscriber).</summary>
public static unsafe class SpeechBridge
{
    const string Lib = "@rpath/ShinySpeechBridge.framework/ShinySpeechBridge";

    [DllImport(Lib)] static extern int ssb_is_available();
    [DllImport(Lib)] static extern void ssb_set_log(delegate* unmanaged<IntPtr, IntPtr, void> cb);

    static SpeechBridge() => ssb_set_log(&OnLog);

    [UnmanagedCallersOnly]
    static void OnLog(IntPtr ctx, IntPtr msg) => Spike.Log("bridge: " + Marshal.PtrToStringUTF8(msg));

    [DllImport(Lib)]
    static extern void ssb_transcribe_file(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string locale,
        IntPtr ctx,
        delegate* unmanaged<IntPtr, double, double, int, IntPtr, IntPtr, void> onSegment,
        delegate* unmanaged<IntPtr, IntPtr, void> onDone);

    [DllImport(Lib)]
    static extern IntPtr ssb_live_start(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string locale,
        IntPtr ctx,
        delegate* unmanaged<IntPtr, double, double, int, IntPtr, IntPtr, void> onSegment,
        delegate* unmanaged<IntPtr, IntPtr, void> onDone);

    [DllImport(Lib)] static extern int ssb_live_push(IntPtr handle, float* samples, int frames, int channels, double sampleRate);
    [DllImport(Lib)] static extern void ssb_live_finish(IntPtr handle);

    /// <summary>1 SpeechTranscriber, 2 DictationTranscriber only, -1 below iOS 26.</summary>
    public static int IsAvailable() => ssb_is_available();

    public record Seg(double Start, double End, bool IsFinal, string Text, string WordsJson);

    public sealed class Session
    {
        GCHandle self;
        IntPtr live;

        public event Action<Seg>? Segment;
        public event Action<string?>? Done;

        public void TranscribeFile(string path, string locale)
        {
            this.self = GCHandle.Alloc(this);
            ssb_transcribe_file(path, locale, GCHandle.ToIntPtr(this.self), &OnSegment, &OnDone);
        }

        public void StartLive(string locale)
        {
            this.self = GCHandle.Alloc(this);
            this.live = ssb_live_start(locale, GCHandle.ToIntPtr(this.self), &OnSegment, &OnDone);
        }

        public bool Push(float[] planar, int frames, int channels, double sampleRate)
        {
            if (this.live == IntPtr.Zero)
                return false;
            fixed (float* p = planar)
                return ssb_live_push(this.live, p, frames, channels, sampleRate) == 1;
        }

        public void FinishLive()
        {
            var h = Interlocked.Exchange(ref this.live, IntPtr.Zero);
            if (h != IntPtr.Zero)
                ssb_live_finish(h);
        }

        [UnmanagedCallersOnly]
        static void OnSegment(IntPtr ctx, double start, double end, int isFinal, IntPtr text, IntPtr words)
        {
            var s = (Session)GCHandle.FromIntPtr(ctx).Target!;
            s.Segment?.Invoke(new Seg(start, end, isFinal == 1, Marshal.PtrToStringUTF8(text)!, Marshal.PtrToStringUTF8(words)!));
        }

        [UnmanagedCallersOnly]
        static void OnDone(IntPtr ctx, IntPtr error)
        {
            var handle = GCHandle.FromIntPtr(ctx);
            var s = (Session)handle.Target!;
            s.Done?.Invoke(error == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(error));
            handle.Free();
        }
    }
}
