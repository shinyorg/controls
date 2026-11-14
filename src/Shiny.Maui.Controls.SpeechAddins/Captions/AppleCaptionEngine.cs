#if IOS || MACCATALYST || MACOS
using System.Runtime.InteropServices;
using AVFoundation;
using Foundation;
using Speech;

namespace Shiny.Maui.Controls.SpeechAddins;

/// <summary>
/// On-device captions with Apple's <see cref="SFSpeechRecognizer"/>, fed the analyzed audio as buffers — no
/// microphone, no Swift bridge. Each utterance (cut at pauses) is its own recognition request, so results arrive
/// final soon after the speaker pauses, and the recognizer's segment timestamps give every word its time.
/// </summary>
/// <remarks>
/// Needs <c>NSSpeechRecognitionUsageDescription</c> in Info.plist and the user's speech-recognition permission (no
/// microphone permission — the audio is the app's own). The simulator has no on-device model; with
/// <c>PreferOnDevice</c> off Apple may use its servers instead.
/// </remarks>
sealed class AppleCaptionEngine : ICaptionEngine
{
    // a request that has heard only silence this long is restarted rather than left to grow
    static readonly TimeSpan MaxRequestLength = TimeSpan.FromSeconds(50);

    readonly SFSpeechRecognizer recognizer;
    readonly bool onDevice;
    readonly ICaptionSink sink;
    readonly AVAudioFormat format = new(16000, 1);
    readonly SpeechSegmenter segmenter = new();
    readonly List<Task> pending = [];

    SFSpeechAudioBufferRecognitionRequest? request;
    SFSpeechRecognitionTask? task;
    TimeSpan requestStart;
    TimeSpan requestLength;
    bool collecting; // whole-source mode waits for every request's final result

    AppleCaptionEngine(SFSpeechRecognizer recognizer, bool onDevice, ICaptionSink sink)
    {
        this.recognizer = recognizer;
        this.onDevice = onDevice;
        this.sink = sink;
    }

    /// <summary>Create the engine, asking for permission if needed; <c>null</c> when recognition is unavailable or refused.</summary>
    public static async Task<AppleCaptionEngine?> TryCreateAsync(System.Globalization.CultureInfo? culture, bool preferOnDevice, ICaptionSink sink)
    {
        var status = SFSpeechRecognizer.AuthorizationStatus;
        if (status == SFSpeechRecognizerAuthorizationStatus.NotDetermined)
        {
            var tcs = new TaskCompletionSource<SFSpeechRecognizerAuthorizationStatus>();
            SFSpeechRecognizer.RequestAuthorization(s => tcs.TrySetResult(s));
            status = await tcs.Task.ConfigureAwait(false);
        }
        Console.Error.WriteLine($"[analysis] speech auth={status}"); // TRACE
        if (status != SFSpeechRecognizerAuthorizationStatus.Authorized)
            return null;

        var locale = culture is null ? NSLocale.CurrentLocale : new NSLocale(culture.Name.Replace('-', '_'));
        var recognizer = new SFSpeechRecognizer(locale);
        Console.Error.WriteLine($"[analysis] recognizer available={recognizer.Available} onDevice={recognizer.SupportsOnDeviceRecognition} locale={recognizer.Locale?.Identifier}"); // TRACE
        if (!recognizer.Available)
        {
            recognizer.Dispose();
            return null;
        }

        return new AppleCaptionEngine(recognizer, preferOnDevice && recognizer.SupportsOnDeviceRecognition, sink);
    }

    public Task PushAsync(AudioFrame frame, CancellationToken ct)
    {
        this.Feed(frame);
        return Task.CompletedTask;
    }

    public void Reset()
    {
        this.task?.Cancel();
        this.task = null;
        this.request = null;
        this.segmenter.Complete();
        this.sink.Partial(null);
    }

    public async Task TranscribeAsync(IAsyncEnumerable<AudioFrame> audio, CancellationToken ct)
    {
        this.collecting = true;
        lock (this.pending)
            this.pending.Clear();

        await foreach (var frame in audio.WithCancellation(ct).ConfigureAwait(false))
            this.Feed(frame);

        this.segmenter.Complete();
        this.Finish();

        Task[] waiting;
        lock (this.pending)
            waiting = this.pending.ToArray();
        try
        {
            await Task.WhenAll(waiting).WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            this.collecting = false;
        }
    }

    void Feed(AudioFrame frame)
    {
        if (frame.IsDiscontinuity)
            this.Reset();

        if (this.request is null)
            this.Start(frame.Position);

        using var buffer = this.ToBuffer(frame);
        this.request!.Append(buffer);
        this.requestLength += frame.Duration;

        // a completed utterance ends this request; the next frame opens a new one
        if (this.segmenter.Push(frame).Count > 0 || this.requestLength >= MaxRequestLength)
            this.Finish();
    }

    void Start(TimeSpan position)
    {
        this.requestStart = position;
        this.requestLength = TimeSpan.Zero;
        this.request = new SFSpeechAudioBufferRecognitionRequest
        {
            ShouldReportPartialResults = true,
            RequiresOnDeviceRecognition = this.onDevice
        };
        if (OperatingSystem.IsIOSVersionAtLeast(16) || OperatingSystem.IsMacCatalystVersionAtLeast(16) || OperatingSystem.IsMacOSVersionAtLeast(13))
            this.request.AddsPunctuation = true;

        var start = this.requestStart;
        var done = new TaskCompletionSource();
        if (this.collecting)
        {
            lock (this.pending)
                this.pending.Add(done.Task);
        }

        this.task = this.recognizer.GetRecognitionTask(this.request, (result, error) =>
        {
            if (result is not null)
            {
                if (result.Final)
                {
                    this.Commit(result, start);
                    done.TrySetResult();
                }
                else
                {
                    this.sink.Partial(result.BestTranscription.FormattedString);
                }
            }

            if (error is not null)
            {
                Console.Error.WriteLine($"[analysis] request error {error.Domain}/{error.Code} {error.LocalizedDescription}"); // TRACE
                // "no speech detected" (and a cancelled request) are normal ends of an utterance, not failures
                if (error.Code is not (1110 or 203 or 216 or 301))
                    this.sink.Fail("Speech recognition failed: " + error.LocalizedDescription);
                this.sink.Partial(null);
                done.TrySetResult();
            }
        });
    }

    void Finish()
    {
        Console.Error.WriteLine($"[analysis] request end start={this.requestStart} len={this.requestLength}"); // TRACE
        this.request?.EndAudio();
        this.request = null;
        this.task = null;
    }

    void Commit(SFSpeechRecognitionResult result, TimeSpan start)
    {
        this.sink.Partial(null);
        var transcription = result.BestTranscription;
        var text = transcription.FormattedString?.Trim();
        if (String.IsNullOrEmpty(text))
            return;

        var segments = transcription.Segments;
        var words = new List<CaptionWord>(segments.Length);
        foreach (var s in segments)
        {
            var from = start + TimeSpan.FromSeconds(s.Timestamp);
            words.Add(new CaptionWord(from, from + TimeSpan.FromSeconds(s.Duration), s.Substring));
        }

        var cueStart = words.Count > 0 ? words[0].Start : start;
        var cueEnd = words.Count > 0 ? words[^1].End : start + this.requestLength;
        if (cueEnd <= cueStart)
            cueEnd = cueStart + TimeSpan.FromSeconds(1);
        this.sink.Commit(new CaptionCue(cueStart, cueEnd, text, words));
    }

    AVAudioPcmBuffer ToBuffer(AudioFrame frame)
    {
        var count = frame.FrameCount;
        var buffer = new AVAudioPcmBuffer(this.format, (uint)Math.Max(1, count)) { FrameLength = (uint)count };
        var channel = Marshal.ReadIntPtr(buffer.FloatChannelData);
        Marshal.Copy(frame.Samples.ToArray(), 0, channel, count);
        return buffer;
    }

    public void Dispose()
    {
        this.task?.Cancel();
        this.recognizer.Dispose();
        this.format.Dispose();
    }
}
#endif
