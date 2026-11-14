using System.Globalization;
using Shiny.Speech;
using Shiny.Speech.Cloud;

namespace Shiny.Maui.Controls.SpeechAddins;

/// <summary>Which recognizer a <see cref="SpeechCaptionAnalyzer"/> uses.</summary>
public enum SpeechCaptionEngine
{
    /// <summary>
    /// <see cref="SpeechCaptionAnalyzer.Provider"/> when one is set; otherwise the platform's own recognizer where
    /// it can take pushed audio (Apple; Android 13+); otherwise an <see cref="ISpeechToTextProvider"/> registered in
    /// the app's services. Default.
    /// </summary>
    Auto,

    /// <summary>The platform recognizer only — Apple's <c>SFSpeechRecognizer</c>, Android's <c>SpeechRecognizer</c>.</summary>
    Native,

    /// <summary>An <see cref="ISpeechToTextProvider"/> only (cloud services, Whisper).</summary>
    Provider
}

/// <summary>
/// Live captions from what a <c>MediaElement</c> plays or a <c>CameraView</c> hears — speech-to-text as an audio
/// analyzer. Pair it with a <see cref="CaptionView"/> over the video.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two modes, chosen for you.</b> When a <c>MediaElement</c> opens a source the platform can decode (a local file,
/// a bundled asset, a progressive download on Android), the analyzer transcribes the <i>whole source</i> ahead of
/// playback — usually many times faster than real time — and the captions it produces carry the time each phrase is
/// spoken, so a <see cref="CaptionView"/> bound to the player's position shows them in step with the picture
/// (<see cref="IsTimed"/>). Anything else — a live stream, an HLS stream on Apple, a camera — is captioned live, a
/// phrase at a time, a second or two after it is spoken.
/// </para>
/// <para>
/// <b>Engines.</b> On Apple the on-device <c>SFSpeechRecognizer</c> is used by default (it reports word timings; needs
/// <c>NSSpeechRecognitionUsageDescription</c> and the user's speech permission, and the simulator has no on-device
/// model). On Android 13+ the platform <c>SpeechRecognizer</c> is fed the audio directly. Elsewhere, or when
/// <see cref="Provider"/> is set, any Shiny.Speech <see cref="ISpeechToTextProvider"/> is used (Azure, OpenAI,
/// ElevenLabs, Whisper) — the audio is cut into utterances at pauses and each is recognized on its own, so cues still
/// get real start and end times.
/// </para>
/// </remarks>
/// <example>
/// <code language="xaml">
/// &lt;Grid&gt;
///     &lt;media:MediaElement x:Name="Player" Source="talk.mp4"&gt;
///         &lt;media:MediaElement.AudioAnalyzer&gt;
///             &lt;shiny:SpeechCaptionAnalyzer x:Name="Captions" /&gt;
///         &lt;/media:MediaElement.AudioAnalyzer&gt;
///     &lt;/media:MediaElement&gt;
///     &lt;media:CaptionView Source="{x:Reference Captions}"
///                       Position="{Binding Position, Source={x:Reference Player}}"
///                       VerticalOptions="End" Margin="16,0,16,72" /&gt;
/// &lt;/Grid&gt;
/// </code>
/// </example>
public class SpeechCaptionAnalyzer : AudioAnalyzer, ISourceAudioAnalyzer, ICaptionSource, ICaptionSink
{
    readonly object gate = new();
    List<CaptionCue> cues = [];
    IReadOnlyList<CaptionCue> snapshot = [];
    string? partial;
    bool isTimed;
    ICaptionEngine? engine;
    Task<ICaptionEngine?>? engineTask;

    /// <summary>The speech provider to use (cloud, Whisper). Null picks per <see cref="Engine"/>.</summary>
    public static readonly BindableProperty ProviderProperty = BindableProperty.Create(
        nameof(Provider), typeof(ISpeechToTextProvider), typeof(SpeechCaptionAnalyzer), null, propertyChanged: (b, _, _) => ((SpeechCaptionAnalyzer)b).ResetEngine());

    /// <summary>Which recognizer to use. Default <see cref="SpeechCaptionEngine.Auto"/>.</summary>
    public static readonly BindableProperty EngineProperty = BindableProperty.Create(
        nameof(Engine), typeof(SpeechCaptionEngine), typeof(SpeechCaptionAnalyzer), SpeechCaptionEngine.Auto, propertyChanged: (b, _, _) => ((SpeechCaptionAnalyzer)b).ResetEngine());

    /// <summary>The spoken language. Null uses the device's.</summary>
    public static readonly BindableProperty CultureProperty = BindableProperty.Create(
        nameof(Culture), typeof(CultureInfo), typeof(SpeechCaptionAnalyzer), null, propertyChanged: (b, _, _) => ((SpeechCaptionAnalyzer)b).ResetEngine());

    /// <summary>Keep recognition on the device where the platform can (Apple, Android 12+). Default <c>true</c>.</summary>
    public static readonly BindableProperty PreferOnDeviceProperty = BindableProperty.Create(
        nameof(PreferOnDevice), typeof(bool), typeof(SpeechCaptionAnalyzer), true, propertyChanged: (b, _, _) => ((SpeechCaptionAnalyzer)b).ResetEngine());

    /// <summary>
    /// Transcribe a whole decodable source ahead of playback instead of captioning live. Default <c>true</c>; turn it off
    /// to caption only what is actually played.
    /// </summary>
    public static readonly BindableProperty AnalyzeWholeSourceProperty = BindableProperty.Create(
        nameof(AnalyzeWholeSource), typeof(bool), typeof(SpeechCaptionAnalyzer), true);

    /// <summary>Live captions kept before the oldest are dropped. Default 500.</summary>
    public static readonly BindableProperty MaxLiveCuesProperty = BindableProperty.Create(
        nameof(MaxLiveCues), typeof(int), typeof(SpeechCaptionAnalyzer), 500);

    static readonly BindablePropertyKey TranscriptPropertyKey = BindableProperty.CreateReadOnly(
        nameof(Transcript), typeof(string), typeof(SpeechCaptionAnalyzer), String.Empty);

    /// <summary>The whole transcript so far, as one string (read-only, bindable).</summary>
    public static readonly BindableProperty TranscriptProperty = TranscriptPropertyKey.BindableProperty;

    /// <inheritdoc cref="ProviderProperty"/>
    public ISpeechToTextProvider? Provider
    {
        get => (ISpeechToTextProvider?)this.GetValue(ProviderProperty);
        set => this.SetValue(ProviderProperty, value);
    }

    /// <inheritdoc cref="EngineProperty"/>
    public SpeechCaptionEngine Engine
    {
        get => (SpeechCaptionEngine)this.GetValue(EngineProperty);
        set => this.SetValue(EngineProperty, value);
    }

    /// <inheritdoc cref="CultureProperty"/>
    public CultureInfo? Culture
    {
        get => (CultureInfo?)this.GetValue(CultureProperty);
        set => this.SetValue(CultureProperty, value);
    }

    /// <inheritdoc cref="PreferOnDeviceProperty"/>
    public bool PreferOnDevice
    {
        get => (bool)this.GetValue(PreferOnDeviceProperty);
        set => this.SetValue(PreferOnDeviceProperty, value);
    }

    /// <inheritdoc cref="AnalyzeWholeSourceProperty"/>
    public bool AnalyzeWholeSource
    {
        get => (bool)this.GetValue(AnalyzeWholeSourceProperty);
        set => this.SetValue(AnalyzeWholeSourceProperty, value);
    }

    /// <inheritdoc cref="MaxLiveCuesProperty"/>
    public int MaxLiveCues
    {
        get => (int)this.GetValue(MaxLiveCuesProperty);
        set => this.SetValue(MaxLiveCuesProperty, value);
    }

    /// <inheritdoc cref="TranscriptProperty"/>
    public string Transcript
    {
        get => (string)this.GetValue(TranscriptProperty);
        private set => this.SetValue(TranscriptPropertyKey, value);
    }

    /// <summary>Raised on the UI thread for each committed caption.</summary>
    public event EventHandler<CaptionCue>? CaptionCommitted;

    /// <summary>Raised on the UI thread when recognition is unavailable or fails.</summary>
    public event EventHandler<string>? RecognitionFailed;

    /// <inheritdoc/>
    public event EventHandler? CaptionsChanged;

    /// <inheritdoc/>
    public override string Id => "speech-captions";

    /// <inheritdoc/>
    public override AudioAnalysisFormat Format => AudioAnalysisFormat.Speech;

    /// <inheritdoc/>
    public IReadOnlyList<CaptionCue> Cues => this.snapshot;

    /// <inheritdoc/>
    public string? PartialText => this.partial;

    /// <inheritdoc/>
    public bool IsTimed => this.isTimed;

    bool ISourceAudioAnalyzer.WantsSource => this.AnalyzeWholeSource;

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
        this.OnChanged();
    }

    /// <inheritdoc/>
    public override async ValueTask AnalyzeAsync(AudioFrame frame, CancellationToken ct)
    {
        if (this.isTimed)
            this.SetTimed(false);

        var current = await this.GetEngineAsync().ConfigureAwait(false);
        if (current is not null)
            await current.PushAsync(frame, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask AnalyzeSourceAsync(IAsyncEnumerable<AudioFrame> audio, CancellationToken ct)
    {
        Console.Error.WriteLine("[analysis] AnalyzeSourceAsync start"); // TRACE
        this.Clear();
        this.SetTimed(true);

        var current = await this.GetEngineAsync().ConfigureAwait(false);
        if (current is not null)
            await current.TranscribeAsync(audio, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override void OnSeek(TimeSpan position)
    {
        this.engine?.Reset();
        lock (this.gate)
            this.partial = null;

        // live captions from before the jump describe audio that is no longer playing
        if (!this.isTimed)
            this.Clear();
        else
            this.OnChanged();
    }

    /// <inheritdoc/>
    protected override void OnSourceChanged()
    {
        this.engine?.Reset();
        this.SetTimed(false);
        this.Clear();
    }

    /// <inheritdoc/>
    protected override void OnDetached()
    {
        base.OnDetached();
        this.ResetEngine();
    }

    void SetTimed(bool timed)
    {
        this.isTimed = timed;
        this.OnChanged();
    }

    Task<ICaptionEngine?> GetEngineAsync()
    {
        lock (this.gate)
            return this.engineTask ??= this.CreateEngineAsync();
    }

    void ResetEngine()
    {
        lock (this.gate)
        {
            this.engine?.Dispose();
            this.engine = null;
            this.engineTask = null;
        }
    }

    async Task<ICaptionEngine?> CreateEngineAsync()
    {
        // bindables are read on the UI thread
        var (mode, provider, culture, onDevice) = await MainThread.InvokeOnMainThreadAsync(
            () => (this.Engine, this.Provider, this.Culture, this.PreferOnDevice)).ConfigureAwait(false);

        ICaptionEngine? created = null;
        if (mode != SpeechCaptionEngine.Provider && !(mode == SpeechCaptionEngine.Auto && provider is not null))
            created = await CreateNativeAsync(culture, onDevice, this).ConfigureAwait(false);

        if (created is null && mode != SpeechCaptionEngine.Native)
        {
            provider ??= SpeechServices.Resolve<ISpeechToTextProvider>();
            if (provider is not null)
                created = new ProviderCaptionEngine(provider, new SpeechRecognitionOptions { Culture = culture, PreferOnDevice = onDevice }, this);
        }

        if (created is null)
        {
            ((ICaptionSink)this).Fail(mode == SpeechCaptionEngine.Native
                ? "No native speech recognizer can take pushed audio here (or speech permission was refused)."
                : "No speech recognizer is available: the platform recognizer cannot take pushed audio here and no ISpeechToTextProvider is set or registered.", null);
        }

        Console.Error.WriteLine($"[analysis] caption engine={created?.GetType().Name ?? "none"} mode={mode}"); // TRACE
        lock (this.gate)
            this.engine = created;
        return created;
    }

    static async Task<ICaptionEngine?> CreateNativeAsync(CultureInfo? culture, bool onDevice, ICaptionSink sink)
    {
#if IOS || MACCATALYST || MACOS
        return await AppleCaptionEngine.TryCreateAsync(culture, onDevice, sink).ConfigureAwait(false);
#elif ANDROID
        await Task.CompletedTask;
        return AndroidCaptionEngine.TryCreate(culture, onDevice, sink);
#else
        await Task.CompletedTask;
        return null;
#endif
    }

    void ICaptionSink.Partial(string? text)
    {
        lock (this.gate)
        {
            if (this.partial == text)
                return;
            this.partial = String.IsNullOrWhiteSpace(text) ? null : text;
        }
        this.OnChanged();
    }

    void ICaptionSink.Commit(CaptionCue cue)
    {
        Console.Error.WriteLine($"[analysis] cue {cue.Start}-{cue.End} {cue.Text}"); // TRACE
        lock (this.gate)
        {
            var next = new List<CaptionCue>(this.cues);
            // engines commit in order, but a slow final can land after the next utterance's — keep time order
            var index = next.Count;
            while (index > 0 && next[index - 1].Start > cue.Start)
                index--;
            next.Insert(index, cue);

            if (!this.isTimed && next.Count > this.MaxLiveCuesSafe)
                next.RemoveRange(0, next.Count - this.MaxLiveCuesSafe);

            this.cues = next;
            this.snapshot = next.ToArray();
        }

        this.OnChanged();
        this.Raise(() => this.CaptionCommitted?.Invoke(this, cue));
    }

    void ICaptionSink.Fail(string message, Exception? exception)
    {
        Console.Error.WriteLine($"[analysis] caption FAIL {message} {exception}"); // TRACE
        this.Raise(() => this.RecognitionFailed?.Invoke(this, message));
    }

    // read off the UI thread: the bindable's last value, cached on change
    int MaxLiveCuesSafe => this.maxLiveCues;
    int maxLiveCues = 500;

    /// <inheritdoc/>
    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == MaxLiveCuesProperty.PropertyName)
            this.maxLiveCues = Math.Max(1, this.MaxLiveCues);
    }

    void OnChanged()
    {
        this.CaptionsChanged?.Invoke(this, EventArgs.Empty);
        var text = String.Join(" ", this.snapshot.Select(c => c.Text));
        this.Raise(() => this.Transcript = text);
    }
}
