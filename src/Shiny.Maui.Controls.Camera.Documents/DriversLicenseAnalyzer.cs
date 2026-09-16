using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Camera.Barcode;

namespace Shiny.Maui.Controls.Camera.Documents;

/// <summary>
/// Recognizes a North American driver's license by reading the <b>PDF417</b> barcode on its back (native
/// scanner — Apple Vision / Android MLKit) and parsing the <b>AAMVA</b> record into a strongly-typed
/// <see cref="DriversLicense"/>. Raises <see cref="DocumentDetected"/> and draws a box (captioned with the
/// license number) around the barcode; clears it when no license is in view.
/// </summary>
public class DriversLicenseAnalyzer : FrameAnalyzer
{
    readonly BarcodeScanner scanner = new() { Formats = [BarcodeFormat.Pdf417] };

    /// <inheritdoc/>
    public override string Id => "shiny.camera.driverslicense";

    /// <summary>Box outline + caption color. Default a blue accent.</summary>
    public Color BoxColor { get; set; } = Color.FromArgb("#3B82F6");

    /// <summary>Command invoked (with the <see cref="DocumentDetectedEventArgs{DriversLicense}"/>) on a recognition.</summary>
    public static readonly BindableProperty DocumentDetectedCommandProperty = BindableProperty.Create(
        nameof(DocumentDetectedCommand), typeof(ICommand), typeof(DriversLicenseAnalyzer));

    /// <inheritdoc cref="DocumentDetectedCommandProperty"/>
    public ICommand? DocumentDetectedCommand
    {
        get => (ICommand?)this.GetValue(DocumentDetectedCommandProperty);
        set => this.SetValue(DocumentDetectedCommandProperty, value);
    }

    /// <summary>
    /// Optional selector deciding the boxes to draw for the recognized license; return <c>null</c> for no
    /// overlay. When unset the analyzer draws a <see cref="BoxColor"/> box captioned with the license number.
    /// </summary>
    public Func<DriversLicense, IReadOnlyList<OverlayBox>?>? OverlayProvider { get; set; }

    /// <summary>
    /// Continuation invoked (on the UI thread) with the recognized license while the analyzer is armed; return
    /// <c>true</c> to keep scanning (stay armed), <c>false</c> to stop until the next <see cref="CameraView.Scan"/>.
    /// When unset, delivery is single-shot. Bindable so it can target a VM method in XAML.
    /// </summary>
    public static readonly BindableProperty OnDetectedProperty = BindableProperty.Create(
        nameof(OnDetected), typeof(Func<DocumentDetectedEventArgs<DriversLicense>, Task<bool>>), typeof(DriversLicenseAnalyzer));

    /// <inheritdoc cref="OnDetectedProperty"/>
    public Func<DocumentDetectedEventArgs<DriversLicense>, Task<bool>>? OnDetected
    {
        get => (Func<DocumentDetectedEventArgs<DriversLicense>, Task<bool>>?)this.GetValue(OnDetectedProperty);
        set => this.SetValue(OnDetectedProperty, value);
    }

    /// <summary>Raised on the UI thread when a driver's license is recognized in a frame, while the analyzer is armed.</summary>
    public event EventHandler<DocumentDetectedEventArgs<DriversLicense>>? DocumentDetected;

    string? lastDelivered;

    /// <inheritdoc/>
    /// <remarks>Closes the native barcode scanner client (Android ML Kit); it is re-created on the next frame.</remarks>
    protected override void OnDetached()
    {
        base.OnDetached();
        this.scanner.ReleaseNativeResources();
    }

    /// <inheritdoc/>
    public override async ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeAsync(CameraFrame frame, CancellationToken ct)
    {
        var codes = await this.scanner.ScanAsync(frame, ct).ConfigureAwait(false);

        foreach (var code in codes)
        {
            if (!AamvaParser.TryParse(code.Value, out var license))
                continue;

            // don't re-deliver the same license while it lingers in view; boxes still draw every frame
            if (this.lastDelivered != code.Value)
            {
                this.lastDelivered = code.Value;
                var args = new DocumentDetectedEventArgs<DriversLicense>(license);
                this.Deliver(args, () => this.DocumentDetected?.Invoke(this, args), this.DocumentDetectedCommand, this.OnDetected);
            }

            return this.ResolveOverlay(license, this.OverlayProvider,
                () => new[] { new OverlayBox(code.BoundingBox, this.BoxColor, license.Number ?? "License", this.BoxColor) });
        }
        this.lastDelivered = null; // no license in view -> allow re-delivery when one returns
        return null; // no license barcode in view -> clear
    }
}
