using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;

namespace Shiny.Maui.Controls.Camera.Barcode;

/// <summary>
/// Decodes 1D/2D barcodes and QR codes from each frame using the native scanner — Apple Vision on
/// iOS/macOS and Android MLKit (a no-op on Windows and bare net10.0). Raises <see cref="BarcodesDetected"/>
/// once per frame with <b>every</b> code in view (a frame can hold several), and draws a box (captioned with
/// the value) around each; clears them when no code is in view. Set <see cref="FrameAnalyzer.ScanWindow"/> to
/// restrict scanning to a region — only codes centered inside it are reported and drawn, and the overlay frames
/// it as a viewfinder.
/// </summary>
public class BarcodeAnalyzer : FrameAnalyzer
{
    readonly BarcodeScanner scanner = new();

    /// <inheritdoc/>
    public override string Id => "shiny.camera.barcode";

    /// <summary>
    /// Restrict to specific symbologies (null = all supported). Filters the native scanner. Settable in XAML
    /// as a comma-separated list — e.g. <c>Formats="QrCode,Ean13,Code128"</c>.
    /// </summary>
    [System.ComponentModel.TypeConverter(typeof(BarcodeFormatCollectionTypeConverter))]
    public IList<BarcodeFormat>? Formats
    {
        get => this.scanner.Formats;
        set => this.scanner.Formats = value;
    }

    /// <summary>Box outline + caption color. Default a green accent.</summary>
    public Color BoxColor { get; set; } = Color.FromArgb("#22C55E");

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
    }

    /// <inheritdoc/>
    public override async ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeAsync(CameraFrame frame, CancellationToken ct)
    {
        this.scanner.ScanWindow = this.ScanWindow;
        var codes = await this.scanner.ScanAsync(frame, ct).ConfigureAwait(false);

        // restrict to the scan window (where the native engine didn't already clip to it)
        if (this.ScanWindow is not null)
            codes = codes.Where(c => this.InScanWindow(c.BoundingBox)).ToList();

        if (codes.Count == 0)
        {
            this.lastDelivered = new(); // codes left the window -> allow re-delivery when one returns
            return null; // nothing in view -> clear this analyzer's boxes
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
            var boxes = new OverlayBox[codes.Count];
            for (var i = 0; i < codes.Count; i++)
                boxes[i] = new OverlayBox(codes[i].BoundingBox, this.BoxColor, codes[i].Value, this.BoxColor);
            return boxes;
        });
    }
}
