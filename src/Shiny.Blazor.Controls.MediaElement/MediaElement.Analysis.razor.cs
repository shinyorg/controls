using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Media;

namespace Shiny.Blazor.Controls.Media;

// Frame and audio analysis over what the element plays — the same analyzers the Blazor CameraView runs, plus any
// IAudioAnalyzer (a speech caption analyzer).
public partial class MediaElement
{
    const DynamicallyAccessedMemberTypes JsonSerialized = DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor;

    ElementReference overlayEl;
    IJSObjectReference? frameModule;
    IJSObjectReference? audioModule;
    FrameAnalyzer? appliedAnalyzer;
    bool appliedShowOverlay = true;
    IAudioAnalyzer? appliedAudioAnalyzer;
    AudioAnalysisSession? audioSession;
    MediaAspect appliedAnalysisAspect = (MediaAspect)(-1);
    bool frameStartPending;

    /// <summary>
    /// The frame analyzer run over the video as it plays (null = none) — the same descriptors a Blazor
    /// <c>CameraView</c> takes (<see cref="BarcodeAnalyzer"/>, <see cref="DocumentAnalyzer"/>). A paused video is
    /// analyzed once; a seek clears the boxes.
    /// </summary>
    /// <remarks>
    /// Frames of cross-origin media cannot be read without CORS: serve the media with
    /// <c>Access-Control-Allow-Origin</c> and set <see cref="CrossOrigin"/> to <c>"anonymous"</c>.
    /// </remarks>
    [Parameter] public FrameAnalyzer? Analyzer { get; set; }

    /// <summary>
    /// The audio analyzer fed the audio this element plays (null = none), through a Web Audio worklet. An
    /// <see cref="ISourceAudioAnalyzer"/> is handed the whole source, decoded ahead of playback, when it can be fetched.
    /// </summary>
    /// <remarks>
    /// ⚠️ Routing an element through Web Audio is one-way for its lifetime — it keeps playing normally, but through
    /// an <c>AudioContext</c>. Cross-origin media without CORS reaches the analyzer as silence.
    /// </remarks>
    [Parameter] public IAudioAnalyzer? AudioAnalyzer { get; set; }

    /// <summary>Whether the frame analyzer's boxes and scan window are drawn over the video. Default <c>true</c>.</summary>
    [Parameter] public bool ShowAnalysisOverlay { get; set; } = true;

    /// <summary>The element's <c>crossorigin</c> attribute — <c>"anonymous"</c> lets an analyzer read CORS-enabled remote media.</summary>
    [Parameter] public string? CrossOrigin { get; set; }

    /// <summary>Content laid over the player — a <see cref="CaptionView"/>, say. Positioned against the player.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Raised with the frame analyzer's overlay boxes whenever they change.</summary>
    [Parameter] public EventCallback<IReadOnlyList<OverlayBox>> OverlaysChanged { get; set; }

    /// <summary>Raised with the codes a <see cref="BarcodeAnalyzer"/> read, once per <see cref="ScanAsync"/>.</summary>
    [Parameter] public EventCallback<IReadOnlyList<DetectedBarcode>> BarcodesDetected { get; set; }

    /// <summary>Raised with a <see cref="DocumentAnalyzer"/>'s document bounds (normalized), once per <see cref="ScanAsync"/>.</summary>
    [Parameter] public EventCallback<RectF> DocumentDetected { get; set; }

    /// <summary>Raised when an analyzer cannot run (no <c>BarcodeDetector</c>, cross-origin frames).</summary>
    [Parameter] public EventCallback<string> OnAnalysisError { get; set; }

    /// <summary>
    /// Arm the frame analyzer: it keeps drawing boxes but delivers its next result once (through
    /// <see cref="BarcodesDetected"/> / <see cref="DocumentDetected"/>). Call again to keep scanning.
    /// </summary>
    public async Task ScanAsync()
    {
        if (this.frameModule is not null && this.appliedAnalyzer is not null)
            await this.frameModule.InvokeVoidAsync("arm", this.mediaEl).ConfigureAwait(true);
    }

    /// <summary>Disarm the frame analyzer. Boxes keep drawing.</summary>
    public async Task StopScanningAsync()
    {
        if (this.frameModule is not null)
            await this.frameModule.InvokeVoidAsync("disarm", this.mediaEl).ConfigureAwait(true);
    }

    async Task ApplyAnalysisAsync()
    {
        if (this.module is null || this.selfRef is null)
            return;

        var analyzerChanged = !ReferenceEquals(this.appliedAnalyzer, this.Analyzer) || this.appliedShowOverlay != this.ShowAnalysisOverlay;
        if (analyzerChanged)
        {
            this.appliedAnalyzer = this.Analyzer;
            this.appliedShowOverlay = this.ShowAnalysisOverlay;
            if (this.Analyzer is null)
            {
                if (this.frameModule is not null)
                    await this.frameModule.InvokeVoidAsync("stop", this.mediaEl).ConfigureAwait(true);
            }
            else
            {
                // the overlay canvas renders with the analyzer: start the loop once it is in the DOM
                this.frameStartPending = true;
                this.StateHasChanged();
            }
        }
        else if (this.frameModule is not null && this.appliedAnalyzer is not null && this.appliedAnalysisAspect != this.Aspect)
        {
            this.appliedAnalysisAspect = this.Aspect;
            await this.frameModule.InvokeVoidAsync("setFit", this.mediaEl, ToObjectFit(this.Aspect)).ConfigureAwait(true);
        }

        if (!ReferenceEquals(this.appliedAudioAnalyzer, this.AudioAnalyzer))
        {
            this.appliedAudioAnalyzer = this.AudioAnalyzer;
            await this.StopAudioAsync().ConfigureAwait(true);
            if (this.AudioAnalyzer is not null)
            {
                this.audioSession = new AudioAnalysisSession(this.AudioAnalyzer);
                this.audioModule ??= await this.JS.InvokeAsync<IJSObjectReference>("import", "./_content/Shiny.Blazor.Controls.Analysis/audio-analysis.js").ConfigureAwait(true);
                await this.audioModule.InvokeVoidAsync("startMedia", this.mediaEl, this.selfRef, this.audioSession.SampleRate).ConfigureAwait(true);
                if (this.appliedSource is not null)
                    await this.BeginSourceAnalysisAsync().ConfigureAwait(true);
            }
        }
    }

    async Task StartPendingFrameAnalysisAsync()
    {
        if (!this.frameStartPending || this.Analyzer is not { } analyzer || this.selfRef is null)
            return;

        this.frameStartPending = false;
        this.frameModule ??= await this.JS.InvokeAsync<IJSObjectReference>("import", "./_content/Shiny.Blazor.Controls.Analysis/media-analysis.js").ConfigureAwait(true);
        await this.frameModule.InvokeVoidAsync(
            "start", this.mediaEl, this.ShowAnalysisOverlay ? this.overlayEl : null, this.selfRef, analyzer.Kind,
            this.ShowAnalysisOverlay, analyzer.ShowBoundingBox, analyzer.ScanWindowArray(), ToObjectFit(this.Aspect)
        ).ConfigureAwait(true);
        this.appliedAnalysisAspect = this.Aspect;
    }

    async Task OnSourceChangedForAnalysisAsync()
    {
        if (this.audioSession is null)
            return;

        this.audioSession.SourceChanged();
        await this.BeginSourceAnalysisAsync().ConfigureAwait(true);
    }

    async Task BeginSourceAnalysisAsync()
    {
        if (this.audioSession is null || this.audioModule is null || this.selfRef is null || String.IsNullOrEmpty(this.Source))
            return;

        if (this.audioSession.TryBeginSource())
            await this.audioModule.InvokeVoidAsync("decodeSource", this.Source, this.selfRef, this.audioSession.SampleRate).ConfigureAwait(true);
    }

    async Task StopAudioAsync()
    {
        if (this.audioModule is not null)
            await this.audioModule.InvokeVoidAsync("stopMedia", this.mediaEl).ConfigureAwait(true);

        if (this.audioSession is not null)
        {
            await this.audioSession.DisposeAsync().ConfigureAwait(true);
            this.audioSession = null;
        }
    }

    async Task DisposeAnalysisAsync()
    {
        try
        {
            if (this.frameModule is not null)
            {
                await this.frameModule.InvokeVoidAsync("stop", this.mediaEl).ConfigureAwait(true);
                await this.frameModule.DisposeAsync().ConfigureAwait(true);
            }
            await this.StopAudioAsync().ConfigureAwait(true);
            if (this.audioModule is not null)
                await this.audioModule.DisposeAsync().ConfigureAwait(true);
        }
        catch (JSDisconnectedException)
        {
            // the circuit is gone (Blazor Server); there is nothing left to stop
        }
        this.frameModule = null;
        this.audioModule = null;
    }


    // ── JS callbacks ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Invoked from JS with the frame analyzer's current boxes.</summary>
    [JSInvokable]
    [DynamicDependency(JsonSerialized, typeof(AnalysisBoxDto))]
    public Task OnAnalysisOverlays(AnalysisBoxDto[] boxes, double position)
        => this.OverlaysChanged.InvokeAsync(boxes.Select(b => b.ToOverlayBox()).ToArray());

    /// <summary>Invoked from JS with the codes read while armed.</summary>
    [JSInvokable]
    [DynamicDependency(JsonSerialized, typeof(AnalysisBarcodeDto))]
    public Task OnAnalysisBarcodes(AnalysisBarcodeDto[] barcodes, double position)
        => this.BarcodesDetected.InvokeAsync(barcodes.Select(b => b.ToDetectedBarcode()).ToArray());

    /// <summary>Invoked from JS with a document's bounds while armed.</summary>
    [JSInvokable]
    public Task OnAnalysisDocument(float[] box, double position)
        => box is { Length: 4 }
            ? this.DocumentDetected.InvokeAsync(new RectF(box[0], box[1], box[2], box[3]))
            : Task.CompletedTask;

    /// <summary>Invoked from JS when the picture jumped (a seek).</summary>
    [JSInvokable]
    public Task OnAnalysisDiscontinuity() => this.OverlaysChanged.InvokeAsync(Array.Empty<OverlayBox>());

    // Js suffix: the matching [Parameter] EventCallback owns the plain name
    /// <summary>Invoked from JS when an analyzer cannot run.</summary>
    [JSInvokable]
    public Task OnAnalysisErrorJs(string message) => this.OnAnalysisError.InvokeAsync(message);

    /// <summary>Invoked from the audio worklet with ~100 ms of float32 mono samples.</summary>
    [JSInvokable]
    public void OnAudio(byte[] samples, int sampleRate, double position) => this.audioSession?.Push(samples, sampleRate, position);

    /// <summary>Invoked from JS when the element seeks.</summary>
    [JSInvokable]
    public void OnAudioSeek(double position) => this.audioSession?.Seek(position);

    /// <summary>Invoked from JS with a decoded chunk of the whole source.</summary>
    [JSInvokable]
    public void OnSourceAudio(byte[] samples, int sampleRate, double position) => this.audioSession?.PushSource(samples, sampleRate, position);

    /// <summary>Invoked from JS when the whole-source decode finished.</summary>
    [JSInvokable]
    public void OnSourceAudioEnd(bool ok) => this.audioSession?.CompleteSource(ok);
}
