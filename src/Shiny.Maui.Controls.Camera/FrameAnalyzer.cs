using System.Windows.Input;
using Microsoft.Maui.Graphics;

namespace Shiny.Controls.Camera;

/// <summary>
/// Base class for frame analyzers. Implements <see cref="IFrameAnalyzer"/>, is a <see cref="BindableObject"/>
/// (so its <c>Command</c>s bind in XAML and inherit the <see cref="CameraView"/>'s <see cref="BindableObject.BindingContext"/>),
/// and marshals each analyzer's strongly-typed events/commands onto the UI thread. The camera pipeline injects
/// a dispatcher when the analyzer is attached to a running camera; used standalone (or in tests) events are
/// raised inline.
/// </summary>
public abstract class FrameAnalyzer : BindableObject, IFrameAnalyzer
{
    Action<Action>? dispatcher;
    volatile bool isArmed;

    /// <summary>
    /// Whether this analyzer runs at all. Default <c>true</c>. Set <c>false</c> (e.g. bind it to a switch in
    /// XAML) to turn the analyzer off without clearing <see cref="CameraView.Analyzer"/> — its command/event
    /// bindings stay wired and its internal state is preserved, so toggling it back on resumes instantly. A
    /// disabled analyzer is skipped by the pipeline and its overlay boxes are cleared, so the camera behaves as
    /// if it had none (e.g. on Android, video recording is allowed).
    /// </summary>
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(
        nameof(IsEnabled), typeof(bool), typeof(FrameAnalyzer), true);

    /// <inheritdoc cref="IsEnabledProperty"/>
    public bool IsEnabled
    {
        get => (bool)this.GetValue(IsEnabledProperty);
        set => this.SetValue(IsEnabledProperty, value);
    }

    /// <summary>
    /// Whether this analyzer's bounding boxes are drawn. Default <c>true</c>. Set <c>false</c> (e.g. in XAML)
    /// to run the analyzer purely for its event/command without drawing any box. Distinct from
    /// <see cref="IsEnabled"/>, which stops the analyzer running entirely.
    /// </summary>
    public static readonly BindableProperty ShowBoundingBoxProperty = BindableProperty.Create(
        nameof(ShowBoundingBox), typeof(bool), typeof(FrameAnalyzer), true);

    /// <inheritdoc cref="ShowBoundingBoxProperty"/>
    public bool ShowBoundingBox
    {
        get => (bool)this.GetValue(ShowBoundingBoxProperty);
        set => this.SetValue(ShowBoundingBoxProperty, value);
    }

    /// <summary>
    /// Restricts both detection and the overlay to a normalized rectangle (0..1) in upright image space; null
    /// (default) scans the whole frame. Analyzers that honor it only report detections whose center falls inside
    /// the window (and, where the native engine supports it — e.g. Apple Vision's <c>regionOfInterest</c> — skip
    /// the rest of the frame for a real perf win). The built-in camera overlay dims everything outside the window
    /// and frames a viewfinder reticle, so it doubles as an aim guide (e.g. a tight band for single-barcode scans).
    /// </summary>
    public static readonly BindableProperty ScanWindowProperty = BindableProperty.Create(
        nameof(ScanWindow), typeof(RectF?), typeof(FrameAnalyzer), null);

    /// <inheritdoc cref="ScanWindowProperty"/>
    public RectF? ScanWindow
    {
        get => (RectF?)this.GetValue(ScanWindowProperty);
        set => this.SetValue(ScanWindowProperty, value);
    }

    /// <summary>
    /// True when <paramref name="rect"/> should be reported given the current <see cref="ScanWindow"/>: always
    /// true with no window, else true when the rect's center falls inside the window. Use to post-filter
    /// detections on platforms whose native engine can't clip to a region.
    /// </summary>
    protected bool InScanWindow(RectF rect)
    {
        if (this.ScanWindow is not { } w)
            return true;
        var cx = rect.X + rect.Width / 2f;
        var cy = rect.Y + rect.Height / 2f;
        return cx >= w.X && cx <= w.X + w.Width && cy >= w.Y && cy <= w.Y + w.Height;
    }

    /// <summary>
    /// Whether this analyzer is currently <i>armed</i> to deliver a result. An analyzer always runs and draws
    /// its bounding boxes; it only raises its typed event / command (and invokes <c>OnDetected</c>) while armed.
    /// Arming is a one-shot gate: the next confirmed detection consumes it, then the analyzer goes quiet until
    /// re-armed — unless an <c>OnDetected</c> handler returns <c>true</c> to keep scanning. Arm via
    /// <see cref="CameraView.Scan"/> / <see cref="CameraView.ScanCommand"/>.
    /// </summary>
    public bool IsArmed => this.isArmed;

    volatile object? liveResult;

    /// <summary>
    /// The analyzer's most recent <i>ungated</i> result — what it currently sees, updated every frame,
    /// independent of <see cref="IsArmed"/>.
    /// </summary>
    /// <remarks>
    /// This is the channel <see cref="IDrawEffect"/>s read (via <c>CameraEffectContext.AnalyzerResult</c>) so an
    /// effect can anchor to something the camera is tracking. It exists <b>because</b> the typed event is gated:
    /// a face mask has to follow the face on every frame, not once per <c>Scan()</c>. Not every analyzer
    /// publishes one — it is <c>null</c> unless the analyzer calls <see cref="PublishLive"/>.
    /// </remarks>
    public object? LiveResult => this.liveResult;

    /// <summary>
    /// Publish the analyzer's current ungated result for draw effects to read. Call from the analysis thread
    /// on every frame, passing <c>null</c> when nothing is seen. The value must be immutable (or a fresh
    /// snapshot) — it is read from render threads without locking.
    /// </summary>
    protected void PublishLive(object? result) => this.liveResult = result;

    /// <summary>Arm this analyzer so the next confirmed detection is delivered. Called by <see cref="CameraView.Scan"/>.</summary>
    internal void Arm() => this.isArmed = true;

    /// <summary>Disarm this analyzer so detections are no longer delivered (boxes keep drawing).</summary>
    internal void Disarm() => this.isArmed = false;

    /// <inheritdoc/>
    public abstract string Id { get; }

    /// <inheritdoc/>
    public abstract ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeAsync(CameraFrame frame, CancellationToken ct);

    /// <inheritdoc cref="IFrameAnalyzer.WantsFrame"/>
    /// <remarks>
    /// Override this to declare a cadence — it is what stops the platform building a frame the analyzer is
    /// only going to skip. An analyzer that genuinely wants every frame leaves it alone.
    /// </remarks>
    public virtual bool WantsFrame() => true;

    void IFrameAnalyzer.OnAttached() => this.OnAttached();
    void IFrameAnalyzer.OnDetached() => this.OnDetached();

    /// <inheritdoc cref="IFrameAnalyzer.OnAttached"/>
    protected virtual void OnAttached() { }

    /// <inheritdoc cref="IFrameAnalyzer.OnDetached"/>
    /// <remarks>
    /// Override to release native resources (always call <c>base.OnDetached()</c>). The built-in analyzers
    /// close their Android ML Kit detector clients here and re-create them lazily on the next frame, so an
    /// analyzer declared in XAML can be removed, disabled, re-enabled and re-attached freely. Never runs while
    /// <see cref="AnalyzeAsync"/> is in flight — a detach that lands mid-pass is deferred until it completes.
    /// </remarks>
    protected virtual void OnDetached() { }

    /// <summary>
    /// Set by the camera pipeline so <see cref="Raise"/>/<see cref="Emit"/> post to the UI thread. Pass
    /// <c>null</c> to detach (then they run inline on the analysis thread).
    /// </summary>
    internal void SetDispatcher(Action<Action>? post) => this.dispatcher = post;

    /// <summary>Run an action on the UI thread when attached to a camera, or inline otherwise.</summary>
    protected void Raise(Action action)
    {
        var d = this.dispatcher;
        if (d is null)
            action();
        else
            d(action);
    }

    /// <summary>
    /// Deliver a <i>confirmed</i> detection to the consumer — but only while the analyzer is armed. Call this
    /// (on the analysis thread) at the point the analyzer commits to a result. The arm is consumed synchronously
    /// so a burst of frames showing the same thing yields exactly one delivery. On the UI thread it then raises
    /// the analyzer's typed event, invokes its bound <paramref name="command"/>, and awaits
    /// <paramref name="onDetected"/>: a <c>true</c> result re-arms (keep scanning), <c>false</c> — or no handler
    /// at all (single-shot) — leaves the analyzer disarmed until <see cref="CameraView.Scan"/> is called again.
    /// Bounding boxes are returned from <c>AnalyzeAsync</c> independently and are never gated.
    /// </summary>
    /// <param name="args">The detection payload — passed to the event, the command, and <paramref name="onDetected"/>.</param>
    /// <param name="raiseEvent">Invokes the analyzer's typed event.</param>
    /// <param name="command">The analyzer's bound command, invoked when it can execute (with <paramref name="args"/>).</param>
    /// <param name="onDetected">Optional continuation deciding whether to keep scanning. Return <c>true</c> to stay armed.</param>
    protected void Deliver<TArgs>(TArgs args, Action raiseEvent, ICommand? command, Func<TArgs, Task<bool>>? onDetected)
    {
        // gate + consume the arm synchronously: lingering frames of the same detection won't re-deliver until
        // the handler (or a fresh Scan()) re-arms
        if (!this.isArmed)
            return;
        this.isArmed = false;

        this.Raise(async () =>
        {
            raiseEvent();
            if (command is not null && command.CanExecute(args))
                command.Execute(args);

            if (onDetected is null)
                return; // single-shot: stay disarmed until the next Scan()

            try
            {
                if (await onDetected(args))
                    this.isArmed = true; // keep scanning
            }
            catch
            {
                // a faulting consumer handler must not tear down the dispatcher; stay disarmed (safe stop)
            }
        });
    }

    /// <summary>
    /// Resolve the boxes to draw for a detection: nothing when <see cref="ShowBoundingBox"/> is off, else the
    /// analyzer's <c>OverlayProvider</c> result when one is supplied (return <c>null</c> from it for no box),
    /// else the analyzer's default boxes.
    /// </summary>
    protected IReadOnlyList<OverlayBox>? ResolveOverlay<TArgs>(
        TArgs args,
        Func<TArgs, IReadOnlyList<OverlayBox>?>? provider,
        Func<IReadOnlyList<OverlayBox>?> defaultBoxes)
    {
        if (!this.ShowBoundingBox)
            return null;
        return provider is not null ? provider(args) : defaultBoxes();
    }
}
