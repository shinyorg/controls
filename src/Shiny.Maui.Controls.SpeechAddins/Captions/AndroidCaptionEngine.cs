#if ANDROID
using Android.Content;
using Android.OS;
using Android.Speech;
using Microsoft.Maui.ApplicationModel;

namespace Shiny.Maui.Controls.SpeechAddins;

/// <summary>
/// Captions with Android's own <see cref="SpeechRecognizer"/>, fed the analyzed audio through
/// <c>EXTRA_AUDIO_SOURCE</c> instead of the microphone (Android 13+, a recognition service that supports it — Google's
/// does). Each utterance, cut at pauses, is one recognition session, so the cue takes the utterance's times.
/// </summary>
/// <remarks>
/// The platform recognizer reports no word timings and no partials from a pushed audio source, so captions appear
/// once each utterance is recognized. No microphone permission is involved — the audio is the app's own.
/// </remarks>
sealed class AndroidCaptionEngine : ICaptionEngine
{
    readonly Context context;
    readonly string? language;
    readonly bool preferOnDevice;
    readonly ICaptionSink sink;
    readonly SpeechSegmenter segmenter = new();

    AndroidCaptionEngine(Context context, string? language, bool preferOnDevice, ICaptionSink sink)
    {
        this.context = context;
        this.language = language;
        this.preferOnDevice = preferOnDevice;
        this.sink = sink;
    }

    /// <summary>Create the engine, or <c>null</c> below Android 13 or without a recognition service.</summary>
    public static AndroidCaptionEngine? TryCreate(System.Globalization.CultureInfo? culture, bool preferOnDevice, ICaptionSink sink)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return null;

        var context = Android.App.Application.Context;
        if (!SpeechRecognizer.IsRecognitionAvailable(context))
            return null;

        return new AndroidCaptionEngine(context, culture?.Name, preferOnDevice, sink);
    }

    public async Task PushAsync(AudioFrame frame, CancellationToken ct)
    {
        foreach (var segment in this.segmenter.Push(frame))
            await this.RecognizeAsync(segment, ct).ConfigureAwait(false);
    }

    public void Reset() => this.segmenter.Complete();

    public async Task TranscribeAsync(IAsyncEnumerable<AudioFrame> audio, CancellationToken ct)
    {
        var whole = new SpeechSegmenter();
        await foreach (var frame in audio.WithCancellation(ct).ConfigureAwait(false))
        {
            foreach (var segment in whole.Push(frame))
                await this.RecognizeAsync(segment, ct).ConfigureAwait(false);
        }
        foreach (var segment in whole.Complete())
            await this.RecognizeAsync(segment, ct).ConfigureAwait(false);
    }

    async Task RecognizeAsync(SpeechSegment segment, CancellationToken ct)
    {
        var pcm = segment.Audio.ToPcm16Bytes();
        var pipe = ParcelFileDescriptor.CreatePipe()!;
        var read = pipe[0];
        var write = pipe[1];
        var listener = new Listener();
        SpeechRecognizer? recognizer = null;

        try
        {
            // SpeechRecognizer must be created and driven on the main thread
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                recognizer = this.preferOnDevice && OperatingSystem.IsAndroidVersionAtLeast(31) && SpeechRecognizer.IsOnDeviceRecognitionAvailable(this.context)
                    ? SpeechRecognizer.CreateOnDeviceSpeechRecognizer(this.context)
                    : SpeechRecognizer.CreateSpeechRecognizer(this.context);
                recognizer!.SetRecognitionListener(listener);

                var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
                intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
                if (this.language is not null)
                    intent.PutExtra(RecognizerIntent.ExtraLanguage, this.language);
                intent.PutExtra(RecognizerIntent.ExtraAudioSource, read);
                intent.PutExtra(RecognizerIntent.ExtraAudioSourceEncoding, (int)Android.Media.Encoding.Pcm16bit);
                intent.PutExtra(RecognizerIntent.ExtraAudioSourceChannelCount, 1);
                intent.PutExtra(RecognizerIntent.ExtraAudioSourceSamplingRate, 16000);
                recognizer.StartListening(intent);
            }).ConfigureAwait(false);

            // the pipe holds ~64 KB; the write blocks until the recognizer reads, so it runs off the analysis thread
            await Task.Run(() =>
            {
                using var stream = new ParcelFileDescriptor.AutoCloseOutputStream(write);
                stream.Write(pcm, 0, pcm.Length);
            }, ct).ConfigureAwait(false);

            var text = await listener.Result.Task.WaitAsync(TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
            if (!String.IsNullOrWhiteSpace(text))
                this.sink.Commit(new CaptionCue(segment.Start, segment.End, text.Trim()));
        }
        catch (System.OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            this.sink.Fail("Android speech recognition could not recognize an utterance: " + ex.Message, ex);
        }
        finally
        {
            read.Close();
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                recognizer?.Destroy();
                recognizer?.Dispose();
            }).ConfigureAwait(false);
            listener.Dispose();
        }
    }

    public void Dispose() { }

    // android.speech.RecognitionListener is a framework interface, so its default methods are not desugared away
    sealed class Listener : Java.Lang.Object, IRecognitionListener
    {
        public TaskCompletionSource<string?> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void OnResults(Bundle? results)
        {
            var matches = results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition);
            this.Result.TrySetResult(matches is { Count: > 0 } ? matches[0] : null);
        }

        public void OnError(SpeechRecognizerError error)
        {
            // no match / speech timeout just mean the utterance held nothing recognizable
            if (error is SpeechRecognizerError.NoMatch or SpeechRecognizerError.SpeechTimeout)
                this.Result.TrySetResult(null);
            else
                this.Result.TrySetException(new InvalidOperationException("SpeechRecognizer error " + error));
        }

        public void OnBeginningOfSpeech() { }
        public void OnBufferReceived(byte[]? buffer) { }
        public void OnEndOfSpeech() { }
        public void OnEvent(int eventType, Bundle? @params) { }
        public void OnPartialResults(Bundle? partialResults) { }
        public void OnReadyForSpeech(Bundle? @params) { }
        public void OnRmsChanged(float rmsdB) { }
    }
}
#endif
