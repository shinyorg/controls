using System.Runtime.InteropServices;
using AudioToolbox;
using AVFoundation;
using CoreAnimation;
using CoreMedia;
using CoreVideo;
using Foundation;
using MediaToolbox;

namespace Shiny.Maui.Controls.Media;

// Frame and audio taps for analysis. Frames come from an AVPlayerItemVideoOutput polled on a timer; audio from an
// MTAudioProcessingTap on the item's audio mix.
partial class AppleMediaPlayerBackend
{
    // analysis frames are pulled no faster than this; an analyzer that wants fewer says so through WantsFrame
    static readonly TimeSpan FramePollInterval = TimeSpan.FromMilliseconds(33);

    IMediaAnalysisSink? analysisSink;
    AVAsset? asset;
    AVPlayerItemVideoOutput? videoOutput;
    Timer? frameTimer;
    int framePolling;
    int frameRotation;

    MTAudioProcessingTap? tap;
    AudioStreamBasicDescription tapFormat;
    float[] tapBuffer = [];
    float[] tapChannel = [];

    public void SetAnalysisSink(IMediaAnalysisSink? sink)
    {
        this.analysisSink = sink;
        this.SyncAnalysis();
    }

    public IAsyncEnumerable<AudioFrame>? DecodeAudio(MediaSource source, AudioAnalysisFormat format, CancellationToken ct)
    {
        var url = AppleMediaSourceResolver.Resolve(source);
        // AVAssetReader reads files, not streams
        if (url is null || !url.IsFileUrl)
            return null;

        return AppleAudioDecoder.DecodeAsync(url, format, ct);
    }

    void OnItemCreated(AVPlayerItem item, AVAsset asset)
    {
        this.asset = asset;
        this.frameRotation = 0;
        this.analysisSink?.OnDiscontinuity(TimeSpan.Zero);
        this.SyncAnalysis();
    }

    // Tracks are only known once the item is ready on a remote source: read the video rotation and retry a tap that
    // found no audio track at open.
    void OnItemReady(AVPlayerItem item)
    {
        this.frameRotation = ReadRotation(this.asset);
        this.SyncAnalysis();
    }

    void SyncAnalysis()
    {
        var sink = this.analysisSink;
        var current = this.item;

        if (sink is { FramesEnabled: true } && current is not null)
            this.EnsureVideoOutput(current);
        else
            this.RemoveVideoOutput();

        // the tap stays on the item once attached — it costs nothing while the sink does not want audio, and
        // removing an audio mix mid-play glitches the output
        if (sink is { AudioEnabled: true } && current is not null && this.tap is null)
            this.AttachTap(current);
    }


    // ── frames ───────────────────────────────────────────────────────────────────────────────────

    void EnsureVideoOutput(AVPlayerItem current)
    {
        if (this.videoOutput is not null)
            return;

        // BGRA so the frame reads in place for Vision and for the managed luminance path alike
        var attributes = new CVPixelBufferAttributes { PixelFormatType = CVPixelFormatType.CV32BGRA };
        this.videoOutput = new AVPlayerItemVideoOutput(attributes);
        current.AddOutput(this.videoOutput);
        this.frameTimer = new Timer(_ => this.PollFrame(), null, FramePollInterval, FramePollInterval);
    }

    void RemoveVideoOutput()
    {
        this.frameTimer?.Dispose();
        this.frameTimer = null;

        if (this.videoOutput is null)
            return;

        this.item?.RemoveOutput(this.videoOutput);
        this.videoOutput.Dispose();
        this.videoOutput = null;
    }

    void PollFrame()
    {
        // a slow WantsFrame/copy must not stack timer callbacks
        if (Interlocked.Exchange(ref this.framePolling, 1) == 1)
            return;

        try
        {
            var output = this.videoOutput;
            var sink = this.analysisSink;
            if (output is null || sink is null)
                return;

            var itemTime = output.GetItemTime(CAAnimation.CurrentMediaTime());
            // only a frame the player has not already handed over — a paused video is analyzed once, not 30x a second
            if (!output.HasNewPixelBufferForItemTime(itemTime) || !sink.WantsFrame())
                return;

            var displayTime = CMTime.Invalid;
            var buffer = output.CopyPixelBuffer(itemTime, ref displayTime);
            if (buffer is null)
                return;

            var seconds = displayTime.IsInvalid ? itemTime.Seconds : displayTime.Seconds;
            var timestamp = Double.IsNaN(seconds) || Double.IsInfinity(seconds) ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);
            sink.OnFrame(AppleVideoFrame.Own(buffer, this.frameRotation, timestamp));
        }
        catch
        {
            // the output can be torn down under a callback already in flight
        }
        finally
        {
            Interlocked.Exchange(ref this.framePolling, 0);
        }
    }

    // The output hands buffers in the track's stored orientation; a phone-shot portrait clip is stored landscape with
    // a 90° preferred transform, which is the rotation analyzers apply to get it upright.
    static int ReadRotation(AVAsset? asset)
    {
        if (asset is null)
            return 0;

#pragma warning disable CA1422, CS0618 // the synchronous track read is fine once the item is ready
        var track = asset.TracksWithMediaType(AVMediaTypes.Video.GetConstant()!).FirstOrDefault();
#pragma warning restore CA1422, CS0618
        if (track is null)
            return 0;

        var t = track.PreferredTransform;
        var degrees = (int)Math.Round(Math.Atan2(t.B, t.A) * 180d / Math.PI);
        return ((degrees % 360) + 360) % 360;
    }


    // ── audio ────────────────────────────────────────────────────────────────────────────────────

    void AttachTap(AVPlayerItem current)
    {
        var source = this.asset;
        if (source is null)
            return;

#pragma warning disable CA1422, CS0618
        var track = source.TracksWithMediaType(AVMediaTypes.Audio.GetConstant()!).FirstOrDefault();
#pragma warning restore CA1422, CS0618
        Console.Error.WriteLine($"[analysis] AttachTap track={(track is null ? "none" : track.TrackID.ToString())}"); // TRACE
        if (track is null)
        {
            // Not loaded yet (a remote file before it is ready — OnItemReady retries), or no audio track to tap at
            // all: an HLS stream exposes none, so its audio cannot be analyzed on Apple.
            return;
        }

        var callbacks = new MTAudioProcessingTapCallbacks(this.TapProcess)
        {
            Prepare = this.TapPrepare
        };
        this.tap = new MTAudioProcessingTap(callbacks, MTAudioProcessingTapCreationFlags.PostEffects);

        var parameters = AVMutableAudioMixInputParameters.FromTrack(track);
        parameters.AudioTapProcessor = this.tap;
        var mix = AVMutableAudioMix.Create();
        mix.InputParameters = [parameters];
        current.AudioMix = mix;
    }

    void TapPrepare(MTAudioProcessingTap tap, nint maxFrames, ref AudioStreamBasicDescription format)
        => this.tapFormat = format;

    int tapFaulted;
    int tapTraced; // TRACE
    int tapFirst; // TRACE

    // Runs on Core Audio's real-time thread: copy, interleave, hand over, return. Nothing may escape it — an
    // exception thrown back into MediaToolbox aborts the process.
    void TapProcess(MTAudioProcessingTap tap, nint numberFrames, MTAudioProcessingTapFlags flags, AudioBuffers bufferList, out nint numberFramesOut, out MTAudioProcessingTapFlags flagsOut)
    {
        numberFramesOut = 0;
        flagsOut = 0;
        try
        {
            this.ProcessTap(tap, numberFrames, bufferList, out numberFramesOut, out flagsOut);
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref this.tapFaulted, 1) == 0)
                Console.Error.WriteLine("[Shiny.MediaElement] audio analysis tap failed: " + ex);
        }
    }

    void ProcessTap(MTAudioProcessingTap tap, nint numberFrames, AudioBuffers bufferList, out nint numberFramesOut, out MTAudioProcessingTapFlags flagsOut)
    {
        var status = tap.GetSourceAudio(numberFrames, bufferList, out flagsOut, out _, out numberFramesOut);
        var sink = this.analysisSink;
        if (Interlocked.Exchange(ref this.tapFirst, 1) == 0) Console.Error.WriteLine($"[analysis] tap first call status={status} sink={sink is not null} wants={sink?.WantsAudio()} fmt={this.tapFormat.FormatFlags}/{this.tapFormat.BitsPerChannel}"); // TRACE
        if (status != MTAudioProcessingTapError.None || sink is null || !sink.WantsAudio())
            return;

        // the tap is float32; anything else (never seen in practice) is skipped rather than misread
        if ((this.tapFormat.FormatFlags & AudioFormatFlags.IsFloat) == 0 || this.tapFormat.BitsPerChannel != 32)
            return;

        var frames = (int)numberFramesOut;
        var nonInterleaved = (this.tapFormat.FormatFlags & AudioFormatFlags.IsNonInterleaved) != 0;
        var channels = nonInterleaved ? bufferList.Count : (int)this.tapFormat.ChannelsPerFrame;
        if (frames <= 0 || channels <= 0)
            return;

        var needed = frames * channels;
        if (this.tapBuffer.Length < needed)
            this.tapBuffer = new float[needed];

        if (nonInterleaved)
        {
            // one AudioBuffer per channel -> interleave
            if (this.tapChannel.Length < frames)
                this.tapChannel = new float[frames];

            for (var ch = 0; ch < channels; ch++)
            {
                var buffer = bufferList[ch];
                var available = Math.Min(frames, buffer.DataByteSize / 4);
                Marshal.Copy(buffer.Data, this.tapChannel, 0, available);
                for (var f = 0; f < available; f++)
                    this.tapBuffer[f * channels + ch] = this.tapChannel[f];
            }
        }
        else
        {
            var buffer = bufferList[0];
            Marshal.Copy(buffer.Data, this.tapBuffer, 0, Math.Min(needed, buffer.DataByteSize / 4));
        }

        if (Interlocked.Exchange(ref this.tapTraced, 1) == 0) Console.Error.WriteLine($"[analysis] tap audio {frames}x{channels} @{this.tapFormat.SampleRate} flags={this.tapFormat.FormatFlags} bits={this.tapFormat.BitsPerChannel}"); // TRACE
        sink.OnAudio(this.tapBuffer.AsSpan(0, needed), (int)this.tapFormat.SampleRate, channels);
    }

    void TeardownAnalysis()
    {
        this.RemoveVideoOutput();

        if (this.tap is not null)
        {
            if (this.item is not null)
                this.item.AudioMix = null;
            this.tap.Dispose();
            this.tap = null;
        }
        this.asset = null;
    }
}
