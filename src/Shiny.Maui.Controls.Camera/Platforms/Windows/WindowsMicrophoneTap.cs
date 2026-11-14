using System.Runtime.InteropServices;
using Windows.Media.Audio;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Media.Render;
using WinAudioFrame = Windows.Media.AudioFrame;

namespace Shiny.Maui.Controls.Camera;

/// <summary>
/// Reads the microphone with an <see cref="AudioGraph"/> for the camera's audio analyzer (MediaCapture runs video-only
/// here). 16 kHz mono, the speech rate.
/// </summary>
sealed class WindowsMicrophoneTap
{
    const uint SampleRate = 16000;

    AudioGraph? graph;
    AudioDeviceInputNode? input;
    AudioFrameOutputNode? output;
    AudioAnalysisPipeline? pipeline;
    float[] samples = [];

    public bool IsRunning => this.graph is not null;

    public async Task<bool> StartAsync(AudioAnalysisPipeline pipeline, Action<string> onError)
    {
        if (this.graph is not null)
            return true;

        var encoding = AudioEncodingProperties.CreatePcm(SampleRate, 1, 16);
        var created = await AudioGraph.CreateAsync(new AudioGraphSettings(AudioRenderCategory.Speech) { EncodingProperties = encoding });
        if (created.Status != AudioGraphCreationStatus.Success)
        {
            onError("The microphone could not be opened for audio analysis: " + created.Status);
            return false;
        }

        var inputResult = await created.Graph.CreateDeviceInputNodeAsync(MediaCategory.Speech, encoding);
        if (inputResult.Status != AudioDeviceNodeCreationStatus.Success)
        {
            created.Graph.Dispose();
            onError("The microphone could not be opened for audio analysis: " + inputResult.Status);
            return false;
        }

        this.pipeline = pipeline;
        this.graph = created.Graph;
        this.input = inputResult.DeviceInputNode;
        this.output = this.graph.CreateFrameOutputNode(encoding);
        this.input.AddOutgoingConnection(this.output);
        this.graph.QuantumStarted += this.OnQuantum;
        this.graph.Start();
        return true;
    }

    void OnQuantum(AudioGraph sender, object args)
    {
        if (this.output is not { } node || this.pipeline is not { } target || !target.WantsAudio())
            return;

        using var frame = node.GetFrame();
        this.Read(frame, target);
    }

    void Read(WinAudioFrame frame, AudioAnalysisPipeline target)
    {
        using var buffer = frame.LockBuffer(Windows.Media.AudioBufferAccessMode.Read);
        using var reference = buffer.CreateReference();
        int count;
        unsafe
        {
            byte* data = null;
            uint capacity = 0;
            ((IMemoryBufferByteAccess)reference).GetBuffer(&data, &capacity);
            count = (int)Math.Min(capacity, buffer.Length) / 2;
            if (count <= 0 || data == null)
                return;

            if (this.samples.Length < count)
                this.samples = new float[count];
            var pcm = (short*)data;
            for (var i = 0; i < count; i++)
                this.samples[i] = pcm[i] / 32768f;
        }
        target.Push(this.samples.AsSpan(0, count), (int)SampleRate, 1);
    }

    public void Stop()
    {
        if (this.graph is null)
            return;

        this.graph.QuantumStarted -= this.OnQuantum;
        this.graph.Stop();
        this.input?.Dispose();
        this.output?.Dispose();
        this.graph.Dispose();
        this.graph = null;
        this.input = null;
        this.output = null;
        this.pipeline = null;
    }

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(byte** buffer, uint* capacity);
    }
}
