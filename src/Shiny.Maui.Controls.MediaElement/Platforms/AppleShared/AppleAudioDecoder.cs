using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AudioToolbox;
using AVFoundation;
using CoreMedia;
using Foundation;

namespace Shiny.Maui.Controls.Media;

/// <summary>
/// Decodes a local file's audio with <see cref="AVAssetReader"/>, as fast as it can read — the whole-source path for
/// an <see cref="ISourceAudioAnalyzer"/>. AVFoundation reads the audio track straight out of an .mp4/.mov/.m4a, so
/// there is no extraction step.
/// </summary>
static class AppleAudioDecoder
{
    public static async IAsyncEnumerable<AudioFrame> DecodeAsync(NSUrl url, AudioAnalysisFormat format, [EnumeratorCancellation] CancellationToken ct)
    {
        // get off the caller's thread before the synchronous reader work begins
        await Task.Yield();

        using var asset = AVAsset.FromUrl(url);
#pragma warning disable CA1422, CS0618
        var track = asset.TracksWithMediaType(AVMediaTypes.Audio.GetConstant()!).FirstOrDefault();
#pragma warning restore CA1422, CS0618
        Console.Error.WriteLine($"[analysis] decoder {url} track={(track is null ? "none" : "ok")}"); // TRACE
        if (track is null)
            yield break;

        using var reader = AVAssetReader.FromAsset(asset, out var error);
        if (reader is null || error is not null)
            yield break;

        // interleaved float32 at the track's own rate and channels; AudioFormatConverter does the rest
        var settings = new AudioSettings
        {
            Format = AudioFormatType.LinearPCM,
            LinearPcmFloat = true,
            LinearPcmBitDepth = 32,
            LinearPcmNonInterleaved = false,
            LinearPcmBigEndian = false
        };
        using var output = new AVAssetReaderTrackOutput(track, settings) { AlwaysCopiesSampleData = false };
        if (!reader.CanAddOutput(output))
            yield break;

        reader.AddOutput(output);
        if (!reader.StartReading())
        {
            Console.Error.WriteLine($"[analysis] decoder StartReading failed {reader.Error?.LocalizedDescription}"); // TRACE
            yield break;
        }

        AudioFormatConverter? converter = null;
        long produced = 0;
        var scratch = Array.Empty<float>();

        while (!ct.IsCancellationRequested)
        {
            using var sample = output.CopyNextSampleBuffer();
            if (sample is null)
                break;

            var description = sample.GetAudioFormatDescription();
            var basic = description?.AudioStreamBasicDescription;
            if (basic is not { } asbd || asbd.SampleRate <= 0)
                continue;

            var channels = (int)Math.Max(1, asbd.ChannelsPerFrame);
            using var block = sample.GetDataBuffer();
            if (block is null)
                continue;

            var byteCount = (int)block.DataLength;
            var count = byteCount / 4;
            if (count == 0)
                continue;

            if (scratch.Length < count)
                scratch = new float[count];

            var pin = GCHandle.Alloc(scratch, GCHandleType.Pinned);
            CMBlockBufferError copied;
            try
            {
                copied = block.CopyDataBytes(0, (nuint)byteCount, pin.AddrOfPinnedObject());
            }
            finally
            {
                pin.Free();
            }
            if (copied != CMBlockBufferError.None)
                continue;

            converter ??= new AudioFormatConverter((int)asbd.SampleRate, channels, format);
            var converted = converter.Convert(scratch.AsSpan(0, count - count % channels));
            if (converted.Length == 0)
                continue;

            var position = TimeSpan.FromSeconds((double)produced / converter.OutputRate);
            produced += converted.Length / converter.OutputChannels;
            yield return new AudioFrame(converted, converter.OutputRate, converter.OutputChannels, position);
        }

        Console.Error.WriteLine($"[analysis] decoder done frames={produced} status={reader.Status} {reader.Error?.LocalizedDescription}"); // TRACE
        if (reader.Status == AVAssetReaderStatus.Reading)
            reader.CancelReading();
    }
}
