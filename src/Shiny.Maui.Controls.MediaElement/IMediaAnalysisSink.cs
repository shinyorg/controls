namespace Shiny.Maui.Controls.Media;

/// <summary>
/// Where a backend sends what it plays for analysis. <see cref="MediaElement"/> hands one to its backend through
/// <see cref="IMediaPlayerBackend.SetAnalysisSink"/> and calls it again whenever an analyzer is assigned, removed,
/// enabled or disabled — the backend reads <see cref="FramesEnabled"/>/<see cref="AudioEnabled"/> each time and adds
/// or removes its taps to match.
/// </summary>
/// <remarks>
/// Implement this only when writing your own <see cref="IMediaPlayerBackend"/> consumer; the control supplies it.
/// Every member may be called off the UI thread and must be cheap.
/// </remarks>
public interface IMediaAnalysisSink
{
    /// <summary>Whether a frame analyzer is attached and enabled — attach a frame tap only while this is true.</summary>
    bool FramesEnabled { get; }

    /// <summary>Whether an audio analyzer is attached and enabled — attach an audio tap only while this is true.</summary>
    bool AudioEnabled { get; }

    /// <summary>Whether the next frame is worth building (the analyzer is free and wants one). Ask before copying pixels.</summary>
    bool WantsFrame();

    /// <summary>Deliver a frame; the sink takes ownership and disposes it. Set its timestamp to the media position.</summary>
    void OnFrame(VideoFrame frame);

    /// <summary>Whether the next audio buffer is worth delivering. Ask before copying samples.</summary>
    bool WantsAudio();

    /// <summary>Deliver interleaved float samples; they are copied before this returns, so the buffer can be reused.</summary>
    void OnAudio(ReadOnlySpan<float> interleaved, int sampleRate, int channels);

    /// <summary>Playback jumped to <paramref name="position"/> — a seek, a loop back to the start, a new source.</summary>
    void OnDiscontinuity(TimeSpan position);
}
