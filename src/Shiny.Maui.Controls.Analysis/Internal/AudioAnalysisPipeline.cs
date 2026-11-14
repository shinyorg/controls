using System.ComponentModel;
using System.Threading.Channels;

namespace Shiny.Maui.Controls.Analysis.Internal;

/// <summary>
/// Feeds the single active <see cref="IAudioAnalyzer"/> from a host's audio — a player's processing tap or a
/// camera's microphone. Called on the platform's audio thread with raw interleaved floats, it converts once to the
/// analyzer's <see cref="IAudioAnalyzer.Format"/>, stamps each buffer with a derived media position, and queues it;
/// a consumer task drains the queue into the analyzer one item at a time, in order.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Positions are counted, not read.</b> Neither the Apple tap nor the Android audio sink gives a usable
/// timestamp, so the pipeline anchors to the position the host reports at each <see cref="Discontinuity"/> (a seek,
/// a new source) and adds the samples it has seen since.
/// </para>
/// <para>
/// <b>One queue, one order.</b> Audio, seeks, source changes and whole-source jobs all go through the same queue, so
/// an analyzer never sees a new source's transcript job before the previous source's <c>OnSourceChanged</c>, or a
/// seek land in the middle of a frame.
/// </para>
/// <para>
/// The queue is bounded (<see cref="Capacity"/> items). When an analyzer falls that far behind, the oldest audio is
/// dropped and the next frame it reads carries <see cref="AudioFrame.IsDiscontinuity"/> — losing a stretch of audio
/// is better than an analyzer that drifts further behind the picture without limit.
/// </para>
/// </remarks>
sealed class AudioAnalysisPipeline
{
    /// <summary>How many queued items may wait for the analyzer before the oldest are dropped.</summary>
    public const int Capacity = 512;

    sealed record SeekMarker(TimeSpan Position);
    sealed record SourceChangedMarker;
    sealed record SourceJob(ISourceAudioAnalyzer Analyzer, IAsyncEnumerable<AudioFrame> Audio, CancellationTokenSource Cts);

    readonly object gate = new();
    IAudioAnalyzer? analyzer;
    IAudioAnalyzer? attached;
    Action<Action>? dispatcher;
    volatile bool enabled;
    volatile bool sourceMode;

    Channel<object>? channel;
    CancellationTokenSource? consumerCts;
    CancellationTokenSource? sourceCts;
    int dropped;

    // a seek requested from any thread, applied by the audio thread at the top of its next Push
    int pendingSeek;
    long pendingSeekTicks;

    // audio-thread state: only touched from Push
    AudioFormatConverter? converter;
    int convertInRate;
    int convertInChannels;
    AudioAnalysisFormat convertTarget;
    TimeSpan anchor;
    long producedFrames;
    bool nextIsDiscontinuity;

    /// <summary>Invoked when the active analyzer may have changed (assignment or an <c>IsEnabled</c> toggle), so the host can start or stop its tap.</summary>
    public Action? OnActiveChanged;

    /// <summary>Whether an enabled analyzer is attached.</summary>
    public bool HasAnalyzer => this.enabled;

    /// <summary>The active analyzer, if any.</summary>
    public IAudioAnalyzer? Analyzer => this.analyzer;

    /// <summary>
    /// Whether the next buffer is worth converting. Called on the audio thread before anything is copied; false while
    /// no analyzer is enabled, while a whole-source analysis is standing in for live audio, or when the analyzer says so.
    /// </summary>
    public bool WantsAudio()
    {
        if (!this.enabled || this.sourceMode)
            return false;

        var current = this.analyzer;
        if (current is null)
            return false;

        try
        {
            return current.WantsAudio();
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Dispatcher the analyzer uses to raise its typed events on the UI thread.</summary>
    public void SetDispatcher(Action<Action>? post)
    {
        lock (this.gate)
        {
            this.dispatcher = post;
            (this.analyzer as AudioAnalyzer)?.SetDispatcher(post);
        }
    }

    public void SetAnalyzer(IAudioAnalyzer? next)
    {
        lock (this.gate)
        {
            if (ReferenceEquals(this.analyzer, next))
                return;

            if (this.analyzer is AudioAnalyzer old)
            {
                old.SetDispatcher(null);
                old.PropertyChanged -= this.OnAnalyzerPropertyChanged;
            }

            this.CancelSource();
            this.analyzer = next;

            if (next is AudioAnalyzer aa)
            {
                aa.SetDispatcher(this.dispatcher);
                aa.PropertyChanged += this.OnAnalyzerPropertyChanged;
            }

            this.Recompute();
        }
        this.OnActiveChanged?.Invoke();
    }

    /// <summary>
    /// Push one buffer of interleaved float samples from the audio thread. The span is copied (converted) before this
    /// returns, so the caller may reuse its buffer at once.
    /// </summary>
    public void Push(ReadOnlySpan<float> interleaved, int sampleRate, int channels)
    {
        if (!this.WantsAudio() || sampleRate <= 0 || channels <= 0 || interleaved.Length < channels)
            return;

        var queue = this.channel;
        var current = this.analyzer;
        if (queue is null || current is null)
            return;

        if (Interlocked.Exchange(ref this.pendingSeek, 0) == 1)
        {
            this.converter?.Reset();
            this.anchor = TimeSpan.FromTicks(Interlocked.Read(ref this.pendingSeekTicks));
            this.producedFrames = 0;
            this.nextIsDiscontinuity = true;
        }

        var target = current.Format;
        if (this.converter is null || this.convertInRate != sampleRate || this.convertInChannels != channels || this.convertTarget != target)
        {
            this.converter = new AudioFormatConverter(sampleRate, channels, target);
            this.convertInRate = sampleRate;
            this.convertInChannels = channels;
            this.convertTarget = target;
        }

        var samples = this.converter.Convert(interleaved);
        if (samples.Length == 0)
            return;

        var position = this.anchor + TimeSpan.FromSeconds((double)this.producedFrames / this.converter.OutputRate);
        this.producedFrames += samples.Length / this.converter.OutputChannels;

        if (this.producedFrames == samples.Length / this.converter.OutputChannels) Console.Error.WriteLine($"[analysis] pipeline first push {sampleRate}x{channels} -> {this.converter.OutputRate}"); // TRACE
        var frame = new AudioFrame(samples, this.converter.OutputRate, this.converter.OutputChannels, position, this.nextIsDiscontinuity);
        this.nextIsDiscontinuity = false;
        queue.Writer.TryWrite(frame);
    }

    /// <summary>
    /// The audio jumped to <paramref name="position"/> (a seek). Safe from any thread: the analyzer's
    /// <see cref="IAudioAnalyzer.OnSeek"/> is queued at once (so captions clear even while paused), and the audio
    /// thread re-anchors its position count and flags its next frame when it next pushes.
    /// </summary>
    public void Discontinuity(TimeSpan position)
    {
        Interlocked.Exchange(ref this.pendingSeekTicks, position.Ticks);
        Interlocked.Exchange(ref this.pendingSeek, 1);
        this.channel?.Writer.TryWrite(new SeekMarker(position));
    }

    /// <summary>
    /// A new source opened. Stops any whole-source analysis of the old one, queues the analyzer's
    /// <see cref="IAudioAnalyzer.OnSourceChanged"/>, and restarts the position count at zero.
    /// </summary>
    public void SourceChanged()
    {
        lock (this.gate)
            this.CancelSource();

        Interlocked.Exchange(ref this.pendingSeekTicks, 0);
        Interlocked.Exchange(ref this.pendingSeek, 1);
        this.channel?.Writer.TryWrite(new SourceChangedMarker());
    }

    /// <summary>
    /// Offer the active analyzer the current source ahead of playback. When it is an <see cref="ISourceAudioAnalyzer"/>
    /// that wants sources, <paramref name="decode"/> is asked for the source's audio in the analyzer's format; if it
    /// returns a stream, the job is queued and live audio is not delivered for this source (until the job turns out to
    /// yield nothing, when live delivery resumes). Returns whether the source was taken.
    /// </summary>
    public bool TryAnalyzeSource(Func<AudioAnalysisFormat, IAsyncEnumerable<AudioFrame>?> decode)
    {
        IAudioAnalyzer? current;
        lock (this.gate)
        {
            this.CancelSource();
            current = this.enabled ? this.analyzer : null;
        }

        if (current is not ISourceAudioAnalyzer source || this.channel is not { } queue)
            return false;

        bool wants;
        try
        {
            wants = source.WantsSource;
        }
        catch
        {
            wants = false;
        }
        if (!wants)
            return false;

        IAsyncEnumerable<AudioFrame>? audio;
        try
        {
            audio = decode(source.Format);
        }
        catch
        {
            audio = null;
        }
        if (audio is null)
            return false;

        var cts = new CancellationTokenSource();
        lock (this.gate)
        {
            this.sourceCts = cts;
            this.sourceMode = true;
        }
        queue.Writer.TryWrite(new SourceJob(source, audio, cts));
        return true;
    }

    // Call under gate.
    void CancelSource()
    {
        this.sourceMode = false;
        if (this.sourceCts is null)
            return;
        this.sourceCts.Cancel();
        this.sourceCts = null;
    }

    // Refresh the enabled flag and start/stop the consumer + lifecycle hold. Call under gate.
    void Recompute()
    {
        var aa = this.analyzer as AudioAnalyzer;
        var nowEnabled = this.analyzer is not null && (aa?.IsEnabled ?? true);
        this.enabled = nowEnabled;

        var target = nowEnabled ? this.analyzer : null;
        if (ReferenceEquals(this.attached, target))
            return;

        this.StopConsumer();
        if (target is not null)
            AnalyzerLifecycle.Attach(target);
        if (this.attached is not null)
            AnalyzerLifecycle.Detach(this.attached);
        this.attached = target;

        if (target is not null)
            this.StartConsumer(target);
    }

    void StartConsumer(IAudioAnalyzer target)
    {
        this.channel = Channel.CreateBounded<object>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            },
            _ => Interlocked.Exchange(ref this.dropped, 1)
        );
        this.consumerCts = new CancellationTokenSource();
        this.nextIsDiscontinuity = true;
        _ = this.ConsumeAsync(target, this.channel.Reader, this.consumerCts.Token);
    }

    void StopConsumer()
    {
        this.channel?.Writer.TryComplete();
        this.channel = null;
        this.consumerCts?.Cancel();
        this.consumerCts?.Dispose();
        this.consumerCts = null;
    }

    async Task ConsumeAsync(IAudioAnalyzer target, ChannelReader<object> reader, CancellationToken ct)
    {
        try
        {
            while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (reader.TryRead(out var item))
                {
                    if (!AnalyzerLifecycle.TryBeginPass(target))
                        return;

                    try
                    {
                        await this.HandleAsync(target, item, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch
                    {
                        // a misbehaving analyzer must not tear down the pipeline
                    }
                    finally
                    {
                        AnalyzerLifecycle.EndPass(target);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // stopped
        }
    }

    async Task HandleAsync(IAudioAnalyzer target, object item, CancellationToken ct)
    {
        switch (item)
        {
            case AudioFrame frame:
                if (Interlocked.Exchange(ref this.dropped, 0) == 1 && !frame.IsDiscontinuity)
                    frame = new AudioFrame(frame.Samples, frame.SampleRate, frame.Channels, frame.Position, true);
                await target.AnalyzeAsync(frame, ct).ConfigureAwait(false);
                break;

            case SeekMarker seek:
                target.OnSeek(seek.Position);
                break;

            case SourceChangedMarker:
                target.OnSourceChanged();
                break;

            case SourceJob job when !job.Cts.IsCancellationRequested:
                Console.Error.WriteLine("[analysis] pipeline running source job"); // TRACE
                await this.RunSourceAsync(job, ct).ConfigureAwait(false);
                break;
        }
    }

    async Task RunSourceAsync(SourceJob job, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, job.Cts.Token);
        var counted = new CountingAudio(job.Audio);
        try
        {
            await job.Analyzer.AnalyzeSourceAsync(counted, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // the source changed or the analyzer left
        }

        // A decoder that could not actually read the source (a compressed asset, an unreachable URL) yields nothing;
        // hand the analyzer live audio instead of leaving it with silence.
        if (counted.Frames == 0 && !job.Cts.IsCancellationRequested)
        {
            lock (this.gate)
            {
                if (ReferenceEquals(this.sourceCts, job.Cts))
                    this.CancelSource();
            }
        }
    }

    sealed class CountingAudio(IAsyncEnumerable<AudioFrame> inner) : IAsyncEnumerable<AudioFrame>
    {
        public int Frames;

        public async IAsyncEnumerator<AudioFrame> GetAsyncEnumerator(CancellationToken ct = default)
        {
            await foreach (var frame in inner.WithCancellation(ct).ConfigureAwait(false))
            {
                Interlocked.Increment(ref this.Frames);
                yield return frame;
            }
        }
    }

    void OnAnalyzerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != AudioAnalyzer.IsEnabledProperty.PropertyName)
            return;

        lock (this.gate)
        {
            if (!(this.analyzer is AudioAnalyzer { IsEnabled: true }))
                this.CancelSource();
            this.Recompute();
        }
        this.OnActiveChanged?.Invoke();
    }
}
