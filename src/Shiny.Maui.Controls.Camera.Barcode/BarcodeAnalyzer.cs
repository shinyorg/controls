using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;

namespace Shiny.Maui.Controls.Camera.Barcode;

/// <summary>
/// Decodes 1D/2D barcodes and QR codes from each frame using the native scanner — Apple Vision on
/// iOS/macOS, Android MLKit, and the managed ZXing.Net decoder on Windows. Raises <see cref="BarcodesDetected"/>
/// once per frame with <b>every</b> code in view (a frame can hold several), and draws a box (captioned with
/// the value) around each; clears them when no code is in view. Set <see cref="FrameAnalyzer.ScanWindow"/> to
/// restrict scanning to a region — only codes centered inside it are reported and drawn, and the overlay frames
/// it as a viewfinder.
/// </summary>
/// <remarks>
/// Set <see cref="Mode"/> to <see cref="BarcodeScanMode.LongRange"/> for codes far from the camera — across an
/// aisle, on a high shelf, at an angle, slightly out of focus — and <see cref="AutoZoom"/> to have the camera
/// zoom in on a code it has found but cannot yet read.
/// </remarks>
public class BarcodeAnalyzer : FrameAnalyzer
{
    readonly BarcodeScanner scanner = new();
    readonly LongRangePipeline longRange = new();

    // the camera's zoom, refreshed on the UI thread each frame; one frame stale is close enough to steer by
    double cameraZoom = 1, cameraMinZoom = 1, cameraMaxZoom = 1;

    /// <inheritdoc/>
    public override string Id => "shiny.camera.barcode";

    /// <summary>
    /// Restrict to specific symbologies (null = all supported). Filters the native scanner (and, in
    /// <see cref="BarcodeScanMode.LongRange"/>, the managed decoder — a 1D-only list there also skips the
    /// native crop pass altogether). Settable in XAML as a comma-separated list — e.g.
    /// <c>Formats="QrCode,Ean13,Code128"</c>.
    /// </summary>
    [System.ComponentModel.TypeConverter(typeof(BarcodeFormatCollectionTypeConverter))]
    public IList<BarcodeFormat>? Formats
    {
        get => this.scanner.Formats;
        set
        {
            this.scanner.Formats = value;
            this.longRange.SetFormats(value);
        }
    }

    /// <summary>
    /// How frames are read. Default <see cref="BarcodeScanMode.Standard"/>. In
    /// <see cref="BarcodeScanMode.LongRange"/> the analyzer also raises its camera's
    /// <see cref="CameraView.AnalysisResolution"/> to <see cref="AnalysisResolution.FullHd"/> when it is still
    /// <see cref="AnalysisResolution.Default"/> — Android otherwise analyzes at 640x480, too coarse for any
    /// code at a distance.
    /// </summary>
    public static readonly BindableProperty ModeProperty = BindableProperty.Create(
        nameof(Mode), typeof(BarcodeScanMode), typeof(BarcodeAnalyzer), BarcodeScanMode.Standard,
        propertyChanged: (b, _, _) => ((BarcodeAnalyzer)b).OnModeChanged());

    /// <inheritdoc cref="ModeProperty"/>
    public BarcodeScanMode Mode
    {
        get => (BarcodeScanMode)this.GetValue(ModeProperty);
        set => this.SetValue(ModeProperty, value);
    }

    /// <summary>
    /// <see cref="BarcodeScanMode.LongRange"/> only: zoom the camera in on a code that has been found but is too
    /// small to read, and give the zoom back once nothing has been in view for a moment. Stands aside the
    /// moment anything else moves <see cref="CameraView.Zoom"/> (a pinch, a slider). Default <c>false</c>.
    /// </summary>
    public static readonly BindableProperty AutoZoomProperty = BindableProperty.Create(
        nameof(AutoZoom), typeof(bool), typeof(BarcodeAnalyzer), false);

    /// <inheritdoc cref="AutoZoomProperty"/>
    public bool AutoZoom
    {
        get => (bool)this.GetValue(AutoZoomProperty);
        set => this.SetValue(AutoZoomProperty, value);
    }

    /// <summary>The furthest <see cref="AutoZoom"/> goes, as a multiple of the zoom the user had. Default 4.</summary>
    public double MaxAutoZoom
    {
        get => this.longRange.Zoom.MaxMultiplier;
        set => this.longRange.Zoom.MaxMultiplier = Math.Max(1, value);
    }

    /// <summary>
    /// <see cref="BarcodeScanMode.LongRange"/> only: run the native scanner over the whole frame every this many
    /// frames (0 = never). Catches codes too close for the locator to frame and symbologies it does not
    /// handle. Default 3.
    /// </summary>
    public int FullFrameInterval { get; set; } = 3;

    /// <summary>
    /// <see cref="BarcodeScanMode.LongRange"/> only: the most candidates handed to the native scanner per
    /// frame, cut out and enlarged, least recently tried first. Default 2.
    /// </summary>
    public int MaxNativeCropsPerFrame { get; set; } = 2;

    /// <summary>
    /// <see cref="BarcodeScanMode.LongRange"/> only: frames a reading with no check digit (Code 39, ITF shorter
    /// than 14) must be seen in before it is reported. Checksummed symbologies report on the first frame.
    /// Default 2.
    /// </summary>
    public int UncheckedConfirmations
    {
        get => this.longRange.Tracker.RequiredUncheckedConfirmations;
        set => this.longRange.Tracker.RequiredUncheckedConfirmations = Math.Max(1, value);
    }

    /// <summary>
    /// <see cref="BarcodeScanMode.LongRange"/> only: draw a faint <see cref="CandidateColor"/> box around every
    /// code found but not yet read — feedback that the scanner has seen it and is working on it. Default
    /// <c>true</c>.
    /// </summary>
    public bool ShowCandidates { get; set; } = true;

    /// <summary>Box outline + caption color. Default a green accent.</summary>
    public Color BoxColor { get; set; } = Color.FromArgb("#22C55E");

    /// <summary>Outline color for a code found but not yet read (<see cref="ShowCandidates"/>). Default a translucent amber.</summary>
    public Color CandidateColor { get; set; } = Color.FromArgb("#B3F59E0B");

    /// <summary>Command invoked (with the <see cref="BarcodesDetectedEventArgs"/>) when barcodes are decoded.</summary>
    public static readonly BindableProperty BarcodesDetectedCommandProperty = BindableProperty.Create(
        nameof(BarcodesDetectedCommand), typeof(ICommand), typeof(BarcodeAnalyzer));

    /// <inheritdoc cref="BarcodesDetectedCommandProperty"/>
    public ICommand? BarcodesDetectedCommand
    {
        get => (ICommand?)this.GetValue(BarcodesDetectedCommandProperty);
        set => this.SetValue(BarcodesDetectedCommandProperty, value);
    }

    /// <summary>
    /// Optional selector deciding the boxes to draw for a decode; return <c>null</c> for no overlay. When
    /// unset the analyzer draws one <see cref="BoxColor"/> box captioned with the value, per barcode.
    /// </summary>
    public Func<BarcodesDetectedEventArgs, IReadOnlyList<OverlayBox>?>? OverlayProvider { get; set; }

    /// <summary>
    /// Continuation invoked (on the UI thread) with the decoded barcodes while the analyzer is armed; return
    /// <c>true</c> to keep scanning (stay armed), <c>false</c> to stop until the next <see cref="CameraView.Scan"/>.
    /// When unset, delivery is single-shot (one set per arm). Bindable so it can target a VM method in XAML.
    /// </summary>
    public static readonly BindableProperty OnDetectedProperty = BindableProperty.Create(
        nameof(OnDetected), typeof(Func<BarcodesDetectedEventArgs, Task<bool>>), typeof(BarcodeAnalyzer));

    /// <inheritdoc cref="OnDetectedProperty"/>
    public Func<BarcodesDetectedEventArgs, Task<bool>>? OnDetected
    {
        get => (Func<BarcodesDetectedEventArgs, Task<bool>>?)this.GetValue(OnDetectedProperty);
        set => this.SetValue(OnDetectedProperty, value);
    }

    /// <summary>Raised on the UI thread once per frame with every barcode decoded, while the analyzer is armed.</summary>
    public event EventHandler<BarcodesDetectedEventArgs>? BarcodesDetected;

    // the set of values delivered last — re-deliver only when the set of codes in view changes, not 30x/sec
    // while the same codes linger
    HashSet<string> lastDelivered = new();

    /// <inheritdoc/>
    /// <remarks>Closes the native scanner client (Android ML Kit); it is re-created on the next frame.</remarks>
    protected override void OnDetached()
    {
        base.OnDetached();
        this.scanner.ReleaseNativeResources();
        this.longRange.Reset();
        this.lastDelivered = new();
    }

    /// <inheritdoc/>
    protected override void OnHostChanged()
    {
        base.OnHostChanged();
        this.RaiseAnalysisResolution();
    }

    void OnModeChanged()
    {
        this.longRange.Reset();
        this.RaiseAnalysisResolution();
    }

    // LongRange needs the detail; Android's default 640x480 analysis frame does not have it. Only an
    // untouched Default is raised — an app that chose a size keeps it.
    void RaiseAnalysisResolution()
    {
        if (this.Mode == BarcodeScanMode.LongRange && this.Host is { AnalysisResolution: AnalysisResolution.Default } host)
            host.AnalysisResolution = AnalysisResolution.FullHd;
    }

    /// <inheritdoc/>
    public override async ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeAsync(CameraFrame frame, CancellationToken ct)
    {
        if (this.Mode == BarcodeScanMode.LongRange)
            return await this.AnalyzeLongRangeAsync(frame, ct).ConfigureAwait(false);

        this.scanner.ScanWindow = this.ScanWindow;
        var codes = await this.scanner.ScanAsync(frame, ct).ConfigureAwait(false);

        // restrict to the scan window (where the native engine didn't already clip to it)
        if (this.ScanWindow is not null)
            codes = codes.Where(c => this.InScanWindow(c.BoundingBox)).ToList();

        return this.Report(codes, null);
    }

    async ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeLongRangeAsync(CameraFrame frame, CancellationToken ct)
    {
        var result = await this.longRange.AnalyzeAsync(frame, this.scanner, this.ScanWindow, this.FullFrameInterval, this.MaxNativeCropsPerFrame, ct).ConfigureAwait(false);
        this.SteerZoom(result.Now, result.Aspect);

        var confirmed = result.Confirmed;
        var unread = this.ShowCandidates ? result.Unread : null;
        if (this.ScanWindow is not null)
        {
            confirmed = confirmed.Where(c => this.InScanWindow(c.BoundingBox)).ToList();
            unread = unread?.Where(this.InScanWindow).ToList();
        }
        return this.Report(confirmed, unread);
    }

    IReadOnlyList<OverlayBox>? Report(List<DetectedBarcode> codes, List<RectF>? unread)
    {
        if (codes.Count == 0)
        {
            this.lastDelivered = new(); // codes left the window -> allow re-delivery when one returns
            if (unread is not { Count: > 0 } || !this.ShowBoundingBox)
                return null; // nothing in view -> clear this analyzer's boxes
            return unread.Select(r => new OverlayBox(r, this.CandidateColor)).ToArray();
        }

        var args = new BarcodesDetectedEventArgs(codes);
        var current = new HashSet<string>(codes.Select(c => c.Value));
        if (!current.SetEquals(this.lastDelivered))
        {
            this.Deliver(args, () => this.BarcodesDetected?.Invoke(this, args), this.BarcodesDetectedCommand, this.OnDetected);
            this.lastDelivered = current;
        }

        return this.ResolveOverlay(args, this.OverlayProvider, () =>
        {
            var boxes = new List<OverlayBox>(codes.Count + (unread?.Count ?? 0));
            foreach (var c in codes)
                boxes.Add(new OverlayBox(c.BoundingBox, this.BoxColor, c.Value, this.BoxColor));
            if (unread != null)
            {
                foreach (var r in unread)
                    boxes.Add(new OverlayBox(r, this.CandidateColor));
            }
            return boxes;
        });
    }

    void SteerZoom(TimeSpan now, float aspect)
    {
        if (this.Host is not { } host)
            return;

        double? next = null;
        if (this.AutoZoom)
            next = this.longRange.Zoom.Update(this.longRange.Tracker.Tracks, this.cameraZoom, this.cameraMinZoom, this.cameraMaxZoom, aspect, now);

        // read and write the camera's zoom on the UI thread
        this.Raise(() =>
        {
            if (next is { } z)
                host.Zoom = z;
            this.cameraZoom = host.Zoom;
            this.cameraMinZoom = host.MinZoom;
            this.cameraMaxZoom = host.MaxZoom;
        });
    }
}
