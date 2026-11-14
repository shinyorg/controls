using System.Threading.Channels;

namespace Shiny.Blazor.Controls.Analysis;

/// <summary>
/// Runs one <see cref="IAudioAnalyzer"/> for a Blazor host: audio chunks pushed up from the AudioWorklet tap, seeks and
/// source changes are queued and handed to the analyzer one at a time, in order, off the interop call.
/// </summary>
/// <remarks>
/// The browser tap delivers mono at the analyzer's <see cref="IAudioAnalyzer.Format"/> rate (48 kHz for
/// <see cref="AudioAnalysisFormat.Native"/>), with positions read from the media element's clock. The queue is
/// bounded; an analyzer that falls behind loses the oldest audio and is told so with
/// <see cref="AudioFrame.IsDiscontinuity"/>.
/// </remarks>
public sealed class AudioAnalysisSession : IAsyncDisposable
{
    const int Capacity = 512;

    sealed record SeekMarker(TimeSpan Position);
    sealed record SourceChangedMarker;
    sealed record SourceJob(ISourceAudioAnalyzer Analyzer, ChannelReader<AudioFrame> Audio, CancellationTokenSource Cts);

    readonly Channel<object> queue;
    readonly CancellationTokenSource cts = new();
    readonly Task consumer;
    Channel<AudioFrame>? source;
    CancellationTokenSource? sourceCts;
    int sourceFrames;
    int dropped;
    bool nextIsDiscontinuity = true;

    /// <summary>Starts a session; the analyzer's <see cref="IAudioAnalyzer.OnAttached"/> runs now.</summary>
    public AudioAnalysisSession(IAudioAnalyzer analyzer)
    {
        this.Analyzer = analyzer;
        this.queue = Channel.CreateBounded<object>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            _ => Interlocked.Exchange(ref this.dropped, 1));

        Guard(analyzer.OnAttached);
        this.consumer = this.ConsumeAsync(this.cts.Token);
    }

    /// <summary>The analyzer this session feeds.</summary>
    public IAudioAnalyzer Analyzer { get; }

    /// <summary>The sample rate to ask the browser tap for.</summary>
    public int SampleRate => this.Analyzer.Format.SampleRate > 0 ? this.Analyzer.Format.SampleRate : 48000;

    /// <summary>Whether a whole-source analysis is standing in for live audio.</summary>
    public bool IsSourceMode => this.source is not null;

    /// <summary>Queue a chunk of little-endian float32 mono samples from the tap.</summary>
    public void Push(byte[] float32, int sampleRate, double positionSeconds)
    {
        if (this.IsSourceMode || float32.Length < 4 || !this.Analyzer.WantsAudio())
            return;

        var samples = new float[float32.Length / 4];
        Buffer.BlockCopy(float32, 0, samples, 0, samples.Length * 4);
        var frame = new AudioFrame(samples, sampleRate, 1, TimeSpan.FromSeconds(positionSeconds), this.nextIsDiscontinuity);
        this.nextIsDiscontinuity = false;
        this.queue.Writer.TryWrite(frame);
    }

    /// <summary>The media seeked to <paramref name="positionSeconds"/>.</summary>
    public void Seek(double positionSeconds)
    {
        this.nextIsDiscontinuity = true;
        this.queue.Writer.TryWrite(new SeekMarker(TimeSpan.FromSeconds(positionSeconds)));
    }

    /// <summary>A new source opened: ends any whole-source analysis and queues <see cref="IAudioAnalyzer.OnSourceChanged"/>.</summary>
    public void SourceChanged()
    {
        this.EndSource(cancel: true);
        this.nextIsDiscontinuity = true;
        this.queue.Writer.TryWrite(new SourceChangedMarker());
    }

    /// <summary>
    /// Start a whole-source analysis when the analyzer takes sources. Returns false when it does not; otherwise feed
    /// <see cref="PushSource"/> and finish with <see cref="CompleteSource"/>.
    /// </summary>
    public bool TryBeginSource()
    {
        if (this.Analyzer is not ISourceAudioAnalyzer analyzer || !analyzer.WantsSource)
            return false;

        this.EndSource(cancel: true);
        this.source = Channel.CreateUnbounded<AudioFrame>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        this.sourceCts = new CancellationTokenSource();
        this.sourceFrames = 0;
        this.queue.Writer.TryWrite(new SourceJob(analyzer, this.source.Reader, this.sourceCts));
        return true;
    }

    /// <summary>Add a decoded chunk (little-endian float32 mono) to the whole-source analysis.</summary>
    public void PushSource(byte[] float32, int sampleRate, double positionSeconds)
    {
        if (this.source is null || float32.Length < 4)
            return;

        var samples = new float[float32.Length / 4];
        Buffer.BlockCopy(float32, 0, samples, 0, samples.Length * 4);
        this.source.Writer.TryWrite(new AudioFrame(samples, sampleRate, 1, TimeSpan.FromSeconds(positionSeconds)));
        this.sourceFrames++;
    }

    /// <summary>The decode finished; with nothing decoded, live audio resumes.</summary>
    public void CompleteSource(bool ok)
    {
        var decodedNothing = !ok && this.sourceFrames == 0;
        this.source?.Writer.TryComplete();
        if (decodedNothing)
            this.EndSource(cancel: true);
        else
            this.source = null; // the job keeps reading what was written; live audio resumes once it is done
    }

    void EndSource(bool cancel)
    {
        this.source?.Writer.TryComplete();
        this.source = null;
        if (cancel)
            this.sourceCts?.Cancel();
        this.sourceCts = null;
    }

    async Task ConsumeAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var item in this.queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    switch (item)
                    {
                        case AudioFrame frame:
                            if (Interlocked.Exchange(ref this.dropped, 0) == 1 && !frame.IsDiscontinuity)
                                frame = new AudioFrame(frame.Samples, frame.SampleRate, frame.Channels, frame.Position, true);
                            await this.Analyzer.AnalyzeAsync(frame, ct).ConfigureAwait(false);
                            break;

                        case SeekMarker seek:
                            this.Analyzer.OnSeek(seek.Position);
                            break;

                        case SourceChangedMarker:
                            this.Analyzer.OnSourceChanged();
                            break;

                        case SourceJob job when !job.Cts.IsCancellationRequested:
                            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, job.Cts.Token))
                                await job.Analyzer.AnalyzeSourceAsync(job.Audio.ReadAllAsync(linked.Token), linked.Token).ConfigureAwait(false);
                            break;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    // a misbehaving analyzer must not tear down the session
                }
            }
        }
        catch (OperationCanceledException)
        {
            // disposed
        }
    }

    static void Guard(Action hook)
    {
        try
        {
            hook();
        }
        catch
        {
            // user code
        }
    }

    /// <summary>Stops the session; the analyzer's <see cref="IAudioAnalyzer.OnDetached"/> runs once the queue has stopped.</summary>
    public async ValueTask DisposeAsync()
    {
        this.EndSource(cancel: true);
        this.queue.Writer.TryComplete();
        this.cts.Cancel();
        try
        {
            await this.consumer.ConfigureAwait(false);
        }
        catch
        {
            // already reported
        }
        this.cts.Dispose();
        Guard(this.Analyzer.OnDetached);
    }
}
