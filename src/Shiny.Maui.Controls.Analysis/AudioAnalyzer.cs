namespace Shiny.Maui.Controls.Analysis;

/// <summary>
/// Base class for audio analyzers — the audio counterpart to <see cref="FrameAnalyzer"/>. Implements
/// <see cref="IAudioAnalyzer"/>, is a <see cref="BindableObject"/> (so its commands bind in XAML and inherit the host's
/// <see cref="BindableObject.BindingContext"/>), and marshals typed events onto the UI thread with <see cref="Raise"/>.
/// Assign one to <c>MediaElement.AudioAnalyzer</c> (the audio it plays) or <c>CameraView.AudioAnalyzer</c> (the
/// microphone it records).
/// </summary>
public abstract class AudioAnalyzer : BindableObject, IAudioAnalyzer
{
    Action<Action>? dispatcher;

    /// <summary>
    /// Whether this analyzer runs at all. Default <c>true</c>. Set <c>false</c> to pause it without removing it from
    /// its host; while disabled no audio is converted or queued for it, and its host may stop capturing audio
    /// altogether when nothing else needs it.
    /// </summary>
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(
        nameof(IsEnabled), typeof(bool), typeof(AudioAnalyzer), true);

    /// <inheritdoc cref="IsEnabledProperty"/>
    public bool IsEnabled
    {
        get => (bool)this.GetValue(IsEnabledProperty);
        set => this.SetValue(IsEnabledProperty, value);
    }

    /// <inheritdoc/>
    public abstract string Id { get; }

    /// <inheritdoc/>
    public virtual AudioAnalysisFormat Format => AudioAnalysisFormat.Speech;

    /// <inheritdoc/>
    public virtual bool WantsAudio() => true;

    /// <inheritdoc/>
    public abstract ValueTask AnalyzeAsync(AudioFrame frame, CancellationToken ct);

    void IAudioAnalyzer.OnSeek(TimeSpan position) => this.OnSeek(position);
    void IAudioAnalyzer.OnSourceChanged() => this.OnSourceChanged();
    void IAudioAnalyzer.OnAttached() => this.OnAttached();
    void IAudioAnalyzer.OnDetached() => this.OnDetached();

    /// <inheritdoc cref="IAudioAnalyzer.OnSeek"/>
    protected virtual void OnSeek(TimeSpan position) { }

    /// <inheritdoc cref="IAudioAnalyzer.OnSourceChanged"/>
    protected virtual void OnSourceChanged() { }

    /// <inheritdoc cref="IAudioAnalyzer.OnAttached"/>
    protected virtual void OnAttached() { }

    /// <inheritdoc cref="IAudioAnalyzer.OnDetached"/>
    protected virtual void OnDetached() { }

    /// <summary>The control this analyzer is assigned to (a <c>MediaElement</c> or a <c>CameraView</c>), or null.</summary>
    protected internal IAnalysisHost? Host
    {
        get => this.host;
        internal set
        {
            if (ReferenceEquals(this.host, value))
                return;
            this.host = value;
            this.OnHostChanged();
        }
    }
    IAnalysisHost? host;

    /// <summary><see cref="Host"/> changed. Runs on the thread that assigned it (normally the UI thread). Default no-op.</summary>
    protected virtual void OnHostChanged() { }

    /// <summary>Set by the host's pipeline so <see cref="Raise"/> posts to the UI thread; <c>null</c> runs inline.</summary>
    internal void SetDispatcher(Action<Action>? post) => this.dispatcher = post;

    /// <summary>Run an action on the UI thread when attached to a host, or inline otherwise.</summary>
    protected void Raise(Action action)
    {
        var d = this.dispatcher;
        if (d is null)
            action();
        else
            d(action);
    }
}
