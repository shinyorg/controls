using AVFoundation;
using CoreMedia;
using Foundation;

namespace Shiny.Maui.Controls.Camera;

// Forwards audio CMSampleBuffers from an AVCaptureAudioDataOutput to the overlay recorder's AVAssetWriter and/or the
// camera's audio analyzer. The recorder half is only used on the AVAssetWriter recording path (burn-in overlays,
// effects, or an attached audio analyzer); otherwise AVCaptureMovieFileOutput handles audio itself.
sealed class AppleAudioDelegate : AVCaptureAudioDataOutputSampleBufferDelegate
{
    public volatile AppleVideoOverlayRecorder? Recorder;

    /// <summary>The camera's audio analysis pipeline, while an audio analyzer is attached.</summary>
    public volatile AudioAnalysisPipeline? Analysis;

    float[] samples = [];
    byte[] scratch = [];

    public override void DidOutputSampleBuffer(AVCaptureOutput captureOutput, CMSampleBuffer sampleBuffer, AVCaptureConnection connection)
    {
        try
        {
            if (this.Analysis is { } analysis && analysis.WantsAudio()
                && AppleAudioSamples.TryRead(sampleBuffer, ref this.samples, ref this.scratch, out var count, out var rate, out var channels))
            {
                analysis.Push(this.samples.AsSpan(0, count), rate, channels);
            }

            this.Recorder?.AppendAudio(sampleBuffer);
        }
        catch
        {
            // native callback — never let a managed exception escape into ObjC
        }
        finally
        {
            sampleBuffer.Dispose();
        }
    }
}
