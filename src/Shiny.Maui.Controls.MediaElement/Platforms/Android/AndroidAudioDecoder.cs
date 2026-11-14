using System.Runtime.CompilerServices;
using Android.Content;
using Android.Media;
using Java.Nio;

namespace Shiny.Maui.Controls.Media;

/// <summary>
/// Decodes a source's audio with <see cref="MediaExtractor"/> + <see cref="MediaCodec"/>, as fast as the codec runs
/// — the whole-source path for an <see cref="ISourceAudioAnalyzer"/>. Reads files, bundled assets and progressive
/// HTTP(S) downloads; adaptive streams (HLS/DASH) are not readable this way and return nothing.
/// </summary>
static class AndroidAudioDecoder
{
    const long TimeoutUs = 10_000;

    public static IAsyncEnumerable<AudioFrame>? DecodeAsync(Context context, string uri, AudioAnalysisFormat format, CancellationToken ct)
    {
        var lower = uri.ToLowerInvariant();
        if (lower.Contains(".m3u8") || lower.Contains(".mpd"))
            return null;

        return Decode(context, uri, format, ct);
    }

    static async IAsyncEnumerable<AudioFrame> Decode(Context context, string uri, AudioAnalysisFormat format, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();

        using var extractor = new MediaExtractor();
        if (!Open(context, extractor, uri))
            yield break;

        var track = -1;
        MediaFormat? trackFormat = null;
        for (var i = 0; i < extractor.TrackCount; i++)
        {
            var f = extractor.GetTrackFormat(i);
            if (f.GetString(MediaFormat.KeyMime)?.StartsWith("audio/", StringComparison.Ordinal) == true)
            {
                track = i;
                trackFormat = f;
                break;
            }
        }
        if (track < 0 || trackFormat is null)
            yield break;

        extractor.SelectTrack(track);
        var mime = trackFormat.GetString(MediaFormat.KeyMime)!;
        using var codec = MediaCodec.CreateDecoderByType(mime);
        codec.Configure(trackFormat, null, null, MediaCodecConfigFlags.None);
        codec.Start();

        var rate = trackFormat.GetInteger(MediaFormat.KeySampleRate);
        var channels = trackFormat.GetInteger(MediaFormat.KeyChannelCount);
        var isFloat = false;
        AudioFormatConverter? converter = null;
        long produced = 0;
        var info = new MediaCodec.BufferInfo();
        var inputDone = false;
        byte[] bytes = [];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!inputDone)
                {
                    var inIndex = codec.DequeueInputBuffer(TimeoutUs);
                    if (inIndex >= 0)
                    {
                        var input = codec.GetInputBuffer(inIndex)!;
                        var size = extractor.ReadSampleData(input, 0);
                        if (size < 0)
                        {
                            codec.QueueInputBuffer(inIndex, 0, 0, 0, MediaCodecBufferFlags.EndOfStream);
                            inputDone = true;
                        }
                        else
                        {
                            codec.QueueInputBuffer(inIndex, 0, size, extractor.SampleTime, MediaCodecBufferFlags.None);
                            extractor.Advance();
                        }
                    }
                }

                var outIndex = codec.DequeueOutputBuffer(info, TimeoutUs);
                if (outIndex == (int)MediaCodecInfoState.OutputFormatChanged)
                {
                    var outFormat = codec.OutputFormat;
                    rate = outFormat.GetInteger(MediaFormat.KeySampleRate);
                    channels = outFormat.GetInteger(MediaFormat.KeyChannelCount);
                    isFloat = outFormat.ContainsKey(MediaFormat.KeyPcmEncoding)
                        && outFormat.GetInteger(MediaFormat.KeyPcmEncoding) == (int)Encoding.PcmFloat;
                    converter = null;
                    continue;
                }
                if (outIndex < 0)
                    continue;

                var endOfStream = (info.Flags & MediaCodecBufferFlags.EndOfStream) != 0;
                if (info.Size > 0)
                {
                    var output = codec.GetOutputBuffer(outIndex)!;
                    output.Position(info.Offset);
                    if (bytes.Length < info.Size)
                        bytes = new byte[info.Size];
                    output.Get(bytes, 0, info.Size);

                    var samples = ToFloats(bytes, info.Size, isFloat);
                    converter ??= new AudioFormatConverter(rate, channels, format);
                    var converted = converter.Convert(samples.AsSpan(0, samples.Length - samples.Length % channels));
                    codec.ReleaseOutputBuffer(outIndex, false);

                    if (converted.Length > 0)
                    {
                        var position = TimeSpan.FromSeconds((double)produced / converter.OutputRate);
                        produced += converted.Length / converter.OutputChannels;
                        yield return new AudioFrame(converted, converter.OutputRate, converter.OutputChannels, position);
                    }
                }
                else
                {
                    codec.ReleaseOutputBuffer(outIndex, false);
                }

                if (endOfStream)
                    break;
            }
        }
        finally
        {
            codec.Stop();
            codec.Release();
        }
    }

    static bool Open(Context context, MediaExtractor extractor, string uri)
    {
        try
        {
            if (uri.StartsWith("asset:///", StringComparison.Ordinal))
            {
                using var fd = context.Assets!.OpenFd(uri["asset:///".Length..]);
                extractor.SetDataSource(fd.FileDescriptor!, fd.StartOffset, fd.Length);
                return true;
            }

            if (uri.StartsWith("file://", StringComparison.Ordinal))
            {
                extractor.SetDataSource(uri["file://".Length..]);
                return true;
            }

            extractor.SetDataSource(uri, new Dictionary<string, string>());
            return true;
        }
        catch
        {
            // compressed assets cannot be opened as a file descriptor; neither can an unreachable URL
            return false;
        }
    }

    static float[] ToFloats(byte[] bytes, int length, bool isFloat)
    {
        if (isFloat)
        {
            var floats = new float[length / 4];
            System.Buffer.BlockCopy(bytes, 0, floats, 0, floats.Length * 4);
            return floats;
        }

        var samples = new float[length / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (short)(bytes[i * 2] | (bytes[i * 2 + 1] << 8)) / 32768f;
        return samples;
    }
}
