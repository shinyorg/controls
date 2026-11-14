using Android.Media;
using Microsoft.Maui.ApplicationModel;

namespace Shiny.Maui.Controls.Camera;

/// <summary>
/// Reads the microphone with <see cref="AudioRecord"/> on its own thread for the camera's audio analyzer — CameraX
/// owns the audio of a recording and has no tap of its own. 16 kHz mono, the speech rate, so the analysis pipeline
/// rarely has to convert.
/// </summary>
sealed class AndroidMicrophoneTap
{
    const int SampleRate = 16000;
    const int ChunkFrames = 1600; // 100 ms

    AudioRecord? record;
    Thread? thread;
    volatile bool running;

    public bool IsRunning => this.running;

    public async Task<bool> StartAsync(AudioAnalysisPipeline pipeline, Action<string> onError)
    {
        if (this.running)
            return true;

        var status = await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.Microphone>()).ConfigureAwait(false);
        if (status != PermissionStatus.Granted)
        {
            onError("Microphone access was denied, so the audio analyzer receives no audio.");
            return false;
        }

        var minimum = AudioRecord.GetMinBufferSize(SampleRate, ChannelIn.Mono, Encoding.Pcm16bit);
        var record = new AudioRecord(AudioSource.VoiceRecognition, SampleRate, ChannelIn.Mono, Encoding.Pcm16bit, Math.Max(minimum, ChunkFrames * 4));
        if (record.State != State.Initialized)
        {
            record.Release();
            onError("The microphone could not be opened for audio analysis (it may be in use).");
            return false;
        }

        this.record = record;
        this.running = true;
        record.StartRecording();
        this.thread = new Thread(() => this.Read(record, pipeline)) { IsBackground = true, Name = "shiny.camera.audio" };
        this.thread.Start();
        return true;
    }

    void Read(AudioRecord record, AudioAnalysisPipeline pipeline)
    {
        var pcm = new short[ChunkFrames];
        var floats = new float[ChunkFrames];
        while (this.running)
        {
            var read = record.Read(pcm, 0, pcm.Length);
            if (read <= 0)
            {
                if (read < 0)
                    break;
                continue;
            }

            if (!pipeline.WantsAudio())
                continue;

            for (var i = 0; i < read; i++)
                floats[i] = pcm[i] / 32768f;
            pipeline.Push(floats.AsSpan(0, read), SampleRate, 1);
        }
    }

    public void Stop()
    {
        if (!this.running)
            return;

        this.running = false;
        try
        {
            this.record?.Stop();
        }
        catch
        {
            // already stopped
        }
        this.thread?.Join(500);
        this.record?.Release();
        this.record?.Dispose();
        this.record = null;
        this.thread = null;
    }
}
