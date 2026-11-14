using System.Windows.Input;

namespace Shiny.Maui.Controls.Media;

// Frame and audio analysis: the same analyzers a CameraView runs (OCR, barcodes, faces, motion, captions), fed from
// what this element plays.
[ContentProperty(nameof(Analyzer))]
public partial class MediaElement : IAnalysisHost
{
    readonly FrameAnalysisPipeline framePipeline = new();
    readonly AudioAnalysisPipeline audioPipeline = new();
    MediaAnalysisSink? analysisSink;
    GraphicsView? overlayView;
    readonly AnalysisOverlayDrawable overlayDrawable = new();

    /// <summary>
    /// The frame analyzer run against the video as it plays (null = none) — any <see cref="FrameAnalyzer"/> from the
    /// analysis packages (OCR, barcodes, faces, motion, documents, AI) or your own <see cref="IFrameAnalyzer"/>. The
    /// same analyzer works on a <c>CameraView</c>. Assign or swap it at any time. It is the content property, so it can
    /// be declared inline in XAML.
    /// </summary>
    /// <remarks>
    /// Frames come from the decoded video at analysis size, so boxes line up with the picture whatever the
    /// <see cref="Aspect"/>. Needs <see cref="MediaPlaybackCapabilities.FrameAnalysis"/>; protected (DRM) content yields
    /// no frames. A paused video is analyzed once, not repeatedly, and a seek clears the boxes and resets any analyzer
    /// that tracks across frames.
    /// </remarks>
    public static readonly BindableProperty AnalyzerProperty = BindableProperty.Create(
        nameof(Analyzer), typeof(IFrameAnalyzer), typeof(MediaElement), propertyChanged: OnAnalyzerChanged);

    /// <inheritdoc cref="AnalyzerProperty"/>
    public IFrameAnalyzer? Analyzer
    {
        get => (IFrameAnalyzer?)this.GetValue(AnalyzerProperty);
        set => this.SetValue(AnalyzerProperty, value);
    }

    /// <summary>
    /// The audio analyzer fed the audio this element plays (null = none) — a <c>SpeechCaptionAnalyzer</c> for live
    /// captions, or your own <see cref="IAudioAnalyzer"/>. An analyzer that implements <see cref="ISourceAudioAnalyzer"/>
    /// is given a whole local source ahead of playback instead, where the platform can decode it.
    /// </summary>
    /// <remarks>
    /// Needs <see cref="MediaPlaybackCapabilities.AudioAnalysis"/>. Apple cannot tap HLS streams. Audio is delivered as
    /// it is played, so a paused element delivers nothing; muting does not stop it.
    /// </remarks>
    public static readonly BindableProperty AudioAnalyzerProperty = BindableProperty.Create(
        nameof(AudioAnalyzer), typeof(IAudioAnalyzer), typeof(MediaElement), propertyChanged: OnAudioAnalyzerChanged);

    /// <inheritdoc cref="AudioAnalyzerProperty"/>
    public IAudioAnalyzer? AudioAnalyzer
    {
        get => (IAudioAnalyzer?)this.GetValue(AudioAnalyzerProperty);
        set => this.SetValue(AudioAnalyzerProperty, value);
    }

    /// <summary>
    /// Whether the frame analyzer's boxes and scan window are drawn over the video. Default <c>true</c>. Turn it off to
    /// run an analyzer purely for its events, or to draw the boxes yourself from <see cref="OverlaysChanged"/>.
    /// </summary>
    public static readonly BindableProperty ShowAnalysisOverlayProperty = BindableProperty.Create(
        nameof(ShowAnalysisOverlay), typeof(bool), typeof(MediaElement), true,
        propertyChanged: (b, _, _) => ((MediaElement)b).ApplyOverlayVisibility());

    /// <inheritdoc cref="ShowAnalysisOverlayProperty"/>
    public bool ShowAnalysisOverlay
    {
        get => (bool)this.GetValue(ShowAnalysisOverlayProperty);
        set => this.SetValue(ShowAnalysisOverlayProperty, value);
    }

    /// <summary>
    /// Raised on the UI thread whenever the frame analyzer's overlay boxes change — a presentation channel for
    /// drawing. Subscribe to the analyzer's own typed event for its results (the recognized text, the decoded code).
    /// </summary>
    public event EventHandler<AnalysisOverlaysChangedEventArgs>? OverlaysChanged;

    /// <summary>
    /// Arm the frame analyzer for one scan: it keeps drawing boxes but stays silent until its next confirmed
    /// detection, which it delivers once. Its <c>OnDetected</c> returning <c>true</c> keeps it armed. The same
    /// arming a <c>CameraView</c> does.
    /// </summary>
    public void Scan()
    {
        if (this.Analyzer is FrameAnalyzer { IsEnabled: true } analyzer)
            analyzer.Arm();
    }

    /// <summary>Command form of <see cref="Scan"/>.</summary>
    public ICommand ScanCommand => this.scanCommand ??= new Command(this.Scan);
    ICommand? scanCommand;

    /// <summary>Disarm the frame analyzer. Boxes keep drawing; results stop until the next <see cref="Scan"/>.</summary>
    public void StopScanning()
    {
        if (this.Analyzer is FrameAnalyzer analyzer)
            analyzer.Disarm();
    }

    AnalysisSourceKind IAnalysisHost.SourceKind => AnalysisSourceKind.Media;

    // a stream with no known length is live — there is nothing to seek back to
    bool IAnalysisHost.IsLive => this.Duration <= TimeSpan.Zero && this.CurrentState is MediaElementState.Playing or MediaElementState.Buffering;


    // ── wiring ───────────────────────────────────────────────────────────────────────────────────

    void BuildAnalysisOverlay()
    {
        this.overlayView = new GraphicsView
        {
            Drawable = this.overlayDrawable,
            InputTransparent = true,
            IsVisible = false
        };
        this.rootGrid.Add(this.overlayView);
        this.ApplyOverlayScaling();

        // the owner's pipelines feed the fullscreen mirror's overlay; a mirror runs no analysis of its own
        if (this.mirrorOwner is not null)
        {
            this.ShowAnalysisOverlay = this.mirrorOwner.ShowAnalysisOverlay;
            this.mirrorOwner.OverlaysChanged += this.OnMirrorOwnerOverlays;
            return;
        }

        this.framePipeline.OnOverlays = this.OnPipelineOverlays;
        this.framePipeline.OnActiveChanged = this.ApplyAnalysisSink;
        this.audioPipeline.OnActiveChanged = this.OnAudioActiveChanged;
    }

    static void OnAnalyzerChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var self = (MediaElement)bindable;
        if (oldValue is FrameAnalyzer previous && ReferenceEquals(previous.Host, self))
            previous.Host = null;
        if (newValue is FrameAnalyzer next)
            next.Host = self;
        if (newValue is BindableObject bo)
            SetInheritedBindingContext(bo, self.BindingContext);

        if (self.mirrorOwner is null && self.Handler is not null)
            self.framePipeline.SetAnalyzer(newValue as IFrameAnalyzer);
    }

    static void OnAudioAnalyzerChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var self = (MediaElement)bindable;
        if (oldValue is AudioAnalyzer previous && ReferenceEquals(previous.Host, self))
            previous.Host = null;
        if (newValue is AudioAnalyzer next)
            next.Host = self;
        if (newValue is BindableObject bo)
            SetInheritedBindingContext(bo, self.BindingContext);

        if (self.mirrorOwner is not null || self.Handler is null)
            return;

        self.audioWasActive = false; // a different analyzer: it has seen nothing of this source yet
        self.audioPipeline.SetAnalyzer(newValue as IAudioAnalyzer);
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (this.Analyzer is BindableObject frame)
            SetInheritedBindingContext(frame, this.BindingContext);
        if (this.AudioAnalyzer is BindableObject audio)
            SetInheritedBindingContext(audio, this.BindingContext);
    }

    // The handler connected and the player exists: start the pipelines and hand the backend its sink.
    void ConnectAnalysis()
    {
        if (this.mirrorOwner is not null || this.backend is null)
            return;

        Action<Action> post = action => this.Dispatcher.Dispatch(action);
        this.framePipeline.SetDispatcher(post);
        this.audioPipeline.SetDispatcher(post);
        this.framePipeline.SetAnalyzer(this.Analyzer);
        this.audioPipeline.SetAnalyzer(this.AudioAnalyzer);
        this.ApplyAnalysisSink();
    }

    // The handler is going away: let go of the analyzers (they get OnDetached) and the backend's taps.
    void DisconnectAnalysis()
    {
        if (this.mirrorOwner is not null)
        {
            this.mirrorOwner.OverlaysChanged -= this.OnMirrorOwnerOverlays;
            return;
        }

        this.backend?.SetAnalysisSink(null);
        this.framePipeline.SetAnalyzer(null);
        this.audioPipeline.SetAnalyzer(null);
        this.framePipeline.SetDispatcher(null);
        this.audioPipeline.SetDispatcher(null);
    }

    // An audio analyzer that becomes active on a source already open (assigned late, or IsEnabled switched on) is
    // offered the whole source then, exactly as it would have been at open.
    bool audioWasActive;

    void OnAudioActiveChanged()
    {
        if (this.Dispatcher.IsDispatchRequired)
        {
            this.Dispatcher.Dispatch(this.OnAudioActiveChanged);
            return;
        }

        this.ApplyAnalysisSink();
        var active = this.audioPipeline.HasAnalyzer;
        if (active && !this.audioWasActive && this.CurrentState is not (MediaElementState.None or MediaElementState.Failed))
            this.TryAnalyzeSourceAudio(this.Source);
        this.audioWasActive = active;
    }

    void ApplyAnalysisSink()
    {
        if (this.mirrorOwner is not null || this.backend is null)
            return;

        var any = this.framePipeline.HasAnalyzer || this.audioPipeline.HasAnalyzer;
        if (!any)
        {
            this.backend.SetAnalysisSink(null);
            return;
        }

        this.analysisSink ??= new MediaAnalysisSink(this.framePipeline, this.audioPipeline);
        Console.Error.WriteLine($"[analysis] sink frames={this.framePipeline.HasAnalyzer} audio={this.audioPipeline.HasAnalyzer}"); // TRACE
        this.backend.SetAnalysisSink(this.analysisSink);
    }

    // A new source (or a new analyzer on the current one): offer the whole source to an ISourceAudioAnalyzer.
    void TryAnalyzeSourceAudio(MediaSource? source)
    {
        if (source is null || this.backend is not { } player)
            return;

        if ((player.Capabilities & MediaPlaybackCapabilities.SourceAudioAnalysis) == 0)
            return;

        var taken = this.audioPipeline.TryAnalyzeSource(format => player.DecodeAudio(source, format, CancellationToken.None));
        Console.Error.WriteLine($"[analysis] TryAnalyzeSource({source}) taken={taken}"); // TRACE
    }

    void OnSourceOpenedForAnalysis(MediaSource? source)
    {
        if (this.mirrorOwner is not null)
            return;

        this.framePipeline.SignalDiscontinuity();
        this.audioPipeline.SourceChanged();
        this.TryAnalyzeSourceAudio(source);
    }


    // ── overlay ──────────────────────────────────────────────────────────────────────────────────

    void OnPipelineOverlays(IReadOnlyList<OverlayBox> boxes, RectF? window, int width, int height)
        => this.Dispatcher.Dispatch(() =>
        {
            var args = new AnalysisOverlaysChangedEventArgs(boxes, window, width, height);
            this.DrawOverlays(args);
            this.OverlaysChanged?.Invoke(this, args);
        });

    void OnMirrorOwnerOverlays(object? sender, AnalysisOverlaysChangedEventArgs e) => this.DrawOverlays(e);

    void DrawOverlays(AnalysisOverlaysChangedEventArgs e)
    {
        this.overlayDrawable.Boxes = e.Overlays;
        this.overlayDrawable.ScanWindow = e.ScanWindow;
        if (e.ImageWidth > 0 && e.ImageHeight > 0)
            this.overlayDrawable.ImageAspect = (float)e.ImageWidth / e.ImageHeight;

        this.ApplyOverlayVisibility();
        this.overlayView?.Invalidate();
    }

    void ApplyOverlayVisibility()
    {
        if (this.overlayView is null)
            return;

        var hasContent = this.overlayDrawable.Boxes.Count > 0 || this.overlayDrawable.ScanWindow is not null;
        this.overlayView.IsVisible = this.ShowAnalysisOverlay && hasContent;
    }

    // The overlay maps boxes the way the video is scaled, so the two stay registered.
    void ApplyOverlayScaling()
    {
        this.overlayDrawable.Stretch = this.Aspect == MediaAspect.Fill;
        this.overlayDrawable.ScaleMode = this.Aspect == MediaAspect.AspectFill
            ? PreviewScaleMode.AspectFill
            : PreviewScaleMode.AspectFit;
        this.overlayView?.Invalidate();
    }


    /// <summary>Adapts the backend-facing sink onto the two pipelines.</summary>
    sealed class MediaAnalysisSink(FrameAnalysisPipeline frames, AudioAnalysisPipeline audio) : IMediaAnalysisSink
    {
        readonly CancellationTokenSource cts = new();

        public bool FramesEnabled => frames.HasAnalyzer;
        public bool AudioEnabled => audio.HasAnalyzer;
        public bool WantsFrame() => frames.WantsFrame();
        public void OnFrame(VideoFrame frame) => frames.Process(frame, this.cts.Token);
        public bool WantsAudio() => audio.WantsAudio();
        public void OnAudio(ReadOnlySpan<float> interleaved, int sampleRate, int channels) => audio.Push(interleaved, sampleRate, channels);

        public void OnDiscontinuity(TimeSpan position)
        {
            frames.SignalDiscontinuity();
            audio.Discontinuity(position);
        }
    }
}
