using Shiny.Speech;
using Shiny.Speech.Cloud;

namespace Shiny.Maui.Controls.SpeechAddins;

/// <summary>
/// Captions through any <see cref="ISpeechToTextProvider"/> — Azure, OpenAI, ElevenLabs, Whisper on Linux, your own.
/// Providers report text without timings, so the audio is cut into utterances at pauses first and each utterance is
/// recognized on its own: the cue takes the utterance's start and end, which is what keeps captions in step.
/// </summary>
sealed class ProviderCaptionEngine(ISpeechToTextProvider provider, SpeechRecognitionOptions? options, ICaptionSink sink) : ICaptionEngine
{
    readonly SpeechSegmenter segmenter = new();

    public async Task PushAsync(AudioFrame frame, CancellationToken ct)
    {
        foreach (var segment in this.segmenter.Push(frame))
            await this.RecognizeAsync(segment, ct).ConfigureAwait(false);
    }

    public void Reset() => this.segmenter.Complete(); // drop the utterance in progress unrecognized

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
        string? text = null;
        try
        {
            using var stream = new MemoryStream(segment.Audio.ToPcm16Bytes());
            var finals = new List<string>();
            string? last = null;
            await foreach (var result in provider.RecognizeAsync(stream, options, ct).ConfigureAwait(false))
            {
                last = result.Text;
                if (result.IsFinal && !String.IsNullOrWhiteSpace(result.Text))
                    finals.Add(result.Text.Trim());
                else
                    sink.Partial(result.Text);
            }
            text = finals.Count > 0 ? String.Join(" ", finals) : last?.Trim();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sink.Fail("The speech provider could not recognize an utterance: " + ex.Message, ex);
        }

        sink.Partial(null);
        if (!String.IsNullOrWhiteSpace(text))
            sink.Commit(new CaptionCue(segment.Start, segment.End, text));
    }

    public void Dispose() { }
}
