namespace Shiny.Maui.Controls.SpeechAddins;

/// <summary>Where an engine reports what it recognized.</summary>
interface ICaptionSink
{
    /// <summary>The utterance in progress changed (<c>null</c> clears it).</summary>
    void Partial(string? text);

    /// <summary>An utterance is final.</summary>
    void Commit(CaptionCue cue);

    /// <summary>Recognition failed in a way the app should hear about.</summary>
    void Fail(string message, Exception? exception = null);
}

/// <summary>A speech recognizer that turns 16 kHz mono audio into caption cues.</summary>
interface ICaptionEngine : IDisposable
{
    /// <summary>Feed live audio, in order. May await recognition; the host queues audio meanwhile.</summary>
    Task PushAsync(AudioFrame frame, CancellationToken ct);

    /// <summary>The audio jumped or the source changed: abandon the utterance in progress.</summary>
    void Reset();

    /// <summary>Transcribe a whole source, committing cues as they are recognized; returns when it is done.</summary>
    Task TranscribeAsync(IAsyncEnumerable<AudioFrame> audio, CancellationToken ct);
}
