using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls.Camera;

// The microphone, fed to an IAudioAnalyzer (a speech caption analyzer) — independent of the video stream, so it runs
// whether or not the preview is started.
public partial class CameraView
{
    readonly string audioKey = $"shiny-camera-audio-{Guid.NewGuid():N}";
    IJSObjectReference? audioModule;
    IAudioAnalyzer? appliedAudioAnalyzer;
    AudioAnalysisSession? audioSession;

    /// <summary>
    /// The audio analyzer fed the microphone (null = none) — the same analyzers a <c>MediaElement</c> takes. The
    /// microphone is opened (asking permission) while one is assigned and closed when it is cleared. Positions are
    /// time since the microphone opened.
    /// </summary>
    [Parameter] public IAudioAnalyzer? AudioAnalyzer { get; set; }

    async Task ApplyAudioAnalyzerAsync()
    {
        if (ReferenceEquals(this.appliedAudioAnalyzer, this.AudioAnalyzer) || this.selfRef is null)
            return;

        this.appliedAudioAnalyzer = this.AudioAnalyzer;
        await this.StopAudioAsync();
        if (this.AudioAnalyzer is null)
            return;

        this.audioSession = new AudioAnalysisSession(this.AudioAnalyzer);
        this.audioModule ??= await this.JS.InvokeAsync<IJSObjectReference>("import", "./_content/Shiny.Blazor.Controls.Analysis/audio-analysis.js");
        var opened = await this.audioModule.InvokeAsync<bool>("startMicrophone", this.audioKey, this.selfRef, this.audioSession.SampleRate);
        if (!opened)
            await this.OnError.InvokeAsync("Microphone access was denied, so the audio analyzer receives no audio.");
    }

    async Task StopAudioAsync()
    {
        if (this.audioModule is not null)
        {
            try
            {
                await this.audioModule.InvokeVoidAsync("stopStream", this.audioKey);
            }
            catch (JSDisconnectedException)
            {
                // circuit gone
            }
        }

        if (this.audioSession is not null)
        {
            await this.audioSession.DisposeAsync();
            this.audioSession = null;
        }
    }

    /// <summary>Invoked from the audio worklet with ~100 ms of float32 mono samples.</summary>
    [JSInvokable]
    public void OnAudio(byte[] samples, int sampleRate, double position) => this.audioSession?.Push(samples, sampleRate, position);
}
