using System.Runtime.InteropServices;
using AudioToolbox;
using CoreMedia;

namespace Shiny.Maui.Controls.Camera;

/// <summary>Reads the linear PCM in a capture audio sample buffer as interleaved floats, for an audio analyzer.</summary>
static class AppleAudioSamples
{
    /// <summary>
    /// Copy <paramref name="buffer"/>'s samples into <paramref name="samples"/> (grown as needed) as interleaved
    /// floats. Handles float32 and int16, interleaved or planar — what capture outputs deliver. False for anything else.
    /// </summary>
    public static bool TryRead(CMSampleBuffer buffer, ref float[] samples, ref byte[] scratch, out int count, out int sampleRate, out int channels)
    {
        count = 0;
        sampleRate = 0;
        channels = 0;

        if (buffer.GetAudioFormatDescription()?.AudioStreamBasicDescription is not { } asbd || asbd.SampleRate <= 0)
            return false;

        var isFloat = (asbd.FormatFlags & AudioFormatFlags.IsFloat) != 0;
        var bits = (int)asbd.BitsPerChannel;
        if (!(isFloat && bits == 32) && !(!isFloat && bits == 16))
            return false;

        using var block = buffer.GetDataBuffer();
        if (block is null)
            return false;

        var length = (int)block.DataLength;
        if (length <= 0)
            return false;

        if (scratch.Length < length)
            scratch = new byte[length];

        var pin = GCHandle.Alloc(scratch, GCHandleType.Pinned);
        try
        {
            if (block.CopyDataBytes(0, (nuint)length, pin.AddrOfPinnedObject()) != CMBlockBufferError.None)
                return false;
        }
        finally
        {
            pin.Free();
        }

        channels = (int)Math.Max(1, asbd.ChannelsPerFrame);
        sampleRate = (int)asbd.SampleRate;
        var bytesPerSample = bits / 8;
        count = length / bytesPerSample;
        count -= count % channels;
        if (samples.Length < count)
            samples = new float[count];

        var planar = (asbd.FormatFlags & AudioFormatFlags.IsNonInterleaved) != 0 && channels > 1;
        var frames = count / channels;
        for (var i = 0; i < count; i++)
        {
            // planar data is channel 0's frames, then channel 1's...; write it out interleaved
            var target = planar ? (i % frames) * channels + i / frames : i;
            samples[target] = isFloat
                ? BitConverter.ToSingle(scratch, i * 4)
                : BitConverter.ToInt16(scratch, i * 2) / 32768f;
        }
        return true;
    }
}
