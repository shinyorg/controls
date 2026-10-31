using Shiny.Maui.Controls.Camera.Barcode;

namespace Sample.Features.Camera;

/// <summary>
/// <see cref="BarcodeAnalyzer"/> in <see cref="BarcodeScanMode.LongRange"/> scanning continuously,
/// with auto-zoom pulling in on codes that are found but too small to read.
/// </summary>
public partial class LongRangeBarcodePage : ContentPage
{
    public LongRangeBarcodePage()
    {
        this.InitializeComponent();
        SampleSourceCode.Attach(this);

        // keep scanning after each read; the analyzer only delivers when the set of codes in view changes
        this.Analyzer.OnDetected = this.OnDetected;
        this.Camera.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(this.Camera.Zoom))
                this.ZoomLabel.Text = $"Zoom {this.Camera.Zoom:0.0}x";
        };
    }

    Task<bool> OnDetected(BarcodesDetectedEventArgs args)
    {
        this.Results.Text = string.Join(Environment.NewLine, args.Barcodes.Select(b => $"{b.Format}: {b.Value}"));
        return Task.FromResult(true);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        this.Camera.IsActive = true;
        this.Camera.Scan();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        this.Camera.IsActive = false;
    }
}
