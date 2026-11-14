using System.Globalization;
using Shiny.Speech;
using Shiny.Speech.Cloud;

namespace Shiny.Blazor.Controls.SpeechAddins;

/// <summary>
/// Captions from what a Blazor <c>MediaElement</c> plays or a <c>CameraView</c> hears — speech-to-text as an audio
/// analyzer. Pair it with a <c>CaptionView</c> laid over the player.
/// </summary>
/// <remarks>
/// <para>
/// The browser's own speech recognition listens only to the microphone, so captions are recognized with a Shiny.Speech
/// <see cref="ISpeechToTextProvider"/> (Azure, OpenAI, ElevenLabs, Whisper on a server). The audio is cut into
/// utterances at pauses and each is recognized on its own, so every caption gets the utterance's real start and end.
/// </para>
/// <para>
/// When the <c>MediaElement</c> can fetch and decode the whole source, it is transcribed ahead of playback and the
/// captions line up with the picture (<see cref="IsTimed"/>); otherwise it is captioned live.
/// </para>
/// </remarks>
public class SpeechCaptionAnalyzer(ISpeechToTextProvider provider) : ISourceAudioAnalyzer, ICaptionSource
{
    readonly object gate = new();
    readonly SpeechSegmenter live = new();
    List<CaptionCue> cues = [];
    IReadOnlyList<CaptionCue> snapshot = [];
    string? partial;
    bool isTimed;

    /// <summary>The spoken language. Null lets the provider decide.</summary>
    public CultureInfo? Culture { get; set; }

    /// <summary>Transcribe a whole decodable source ahead of playback instead of captioning live. Default <c>true</c>.</summary>
    public bool AnalyzeWholeSource { get; set; } = true;

    /// <summary>Live captions kept before the oldest are dropped. Default 500.</summary>
    public int MaxLiveCues { get; set; } = 500;

    /// <summary>Raised for each committed caption (off the UI thread).</summary>
    public event EventHandler<CaptionCue>? CaptionCommitted;

    /// <summary>Raised when the provider fails to recognize an utterance (off the UI thread).</summary>
    public event EventHandler<string>? RecognitionFailed;

    /// <inheritdoc/>
    public event EventHandler? CaptionsChanged;

    /// <inheritdoc/>
    public string Id => "speech-captions";

    /// <inheritdoc/>
    public AudioAnalysisFormat Format => AudioAnalysisFormat.Speech;

    /// <inheritdoc/>
    public bool WantsSource => this.AnalyzeWholeSource;

    /// <inheritdoc/>
    public IReadOnlyList<CaptionCue> Cues => this.snapshot;

    /// <inheritdoc/>
    public string? PartialText => this.partial;

    /// <inheritdoc/>
    public bool IsTimed => this.isTimed;

    /// <summary>The whole transcript so far.</summary>
    public string Transcript => String.Join(" ", this.snapshot.Select(c => c.Text));

    /// <summary>The captions so far as WebVTT.</summary>
    public string ToWebVtt() => this.snapshot.ToWebVtt();

    /// <summary>Forget every caption.</summary>
    public void Clear()
    {
        lock (this.gate)
        {
            this.cues = [];
            this.snapshot = [];
            this.partial = null;
        }
        this.CaptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public async ValueTask AnalyzeAsync(AudioFrame frame, CancellationToken ct)
    {
        if (this.isTimed)
        {
            this.isTimed = false;
            this.CaptionsChanged?.Invoke(this, EventArgs.Empty);
        }

        foreach (var segment in this.live.Push(frame))
            await this.RecognizeAsync(segment, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask AnalyzeSourceAsync(IAsyncEnumerable<AudioFrame> audio, CancellationToken ct)
    {
        this.Clear();
        this.isTimed = true;
        this.CaptionsChanged?.Invoke(this, EventArgs.Empty);

        var whole = new SpeechSegmenter();
        await foreach (var frame in audio.WithCancellation(ct).ConfigureAwait(false))
        {
            foreach (var segment in whole.Push(frame))
                await this.RecognizeAsync(segment, ct).ConfigureAwait(false);
        }
        foreach (var segment in whole.Complete())
            await this.RecognizeAsync(segment, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void OnSeek(TimeSpan position)
    {
        this.live.Complete();
        if (this.isTimed)
        {
            lock (this.gate)
                this.partial = null;
            this.CaptionsChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            this.Clear();
        }
    }

    /// <inheritdoc/>
    public void OnSourceChanged()
    {
        this.live.Complete();
        this.isTimed = false;
        this.Clear();
    }

    async Task RecognizeAsync(SpeechSegment segment, CancellationToken ct)
    {
        string? text = null;
        try
        {
            using var stream = new MemoryStream(segment.Audio.ToPcm16Bytes());
            var options = new SpeechRecognitionOptions { Culture = this.Culture };
            var finals = new List<string>();
            string? last = null;
            await foreach (var result in provider.RecognizeAsync(stream, options, ct).ConfigureAwait(false))
            {
                last = result.Text;
                if (result.IsFinal && !String.IsNullOrWhiteSpace(result.Text))
                {
                    finals.Add(result.Text.Trim());
                }
                else
                {
                    lock (this.gate)
                        this.partial = result.Text;
                    this.CaptionsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            text = finals.Count > 0 ? String.Join(" ", finals) : last?.Trim();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            this.RecognitionFailed?.Invoke(this, "The speech provider could not recognize an utterance: " + ex.Message);
        }

        lock (this.gate)
        {
            this.partial = null;
            if (!String.IsNullOrWhiteSpace(text))
            {
                var next = new List<CaptionCue>(this.cues) { new(segment.Start, segment.End, text) };
                if (!this.isTimed && next.Count > this.MaxLiveCues)
                    next.RemoveRange(0, next.Count - this.MaxLiveCues);
                this.cues = next;
                this.snapshot = next.ToArray();
            }
        }

        this.CaptionsChanged?.Invoke(this, EventArgs.Empty);
        if (!String.IsNullOrWhiteSpace(text))
            this.CaptionCommitted?.Invoke(this, this.snapshot[^1]);
    }
}
