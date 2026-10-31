using Shiny.Controls.BarcodeReader.Tests;
using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Camera.Barcode;
using Shouldly;
using Xunit;
using ZxFormat = ZXing.BarcodeFormat;
using BarcodeFormat = Shiny.Controls.Camera.BarcodeFormat;

namespace Shiny.Maui.Controls.Camera.Tests;

public class BarcodeAnalyzerLongRangeTests
{
    sealed class SceneFrame(SyntheticBarcode.Scene scene, int rotation = 0) : CameraFrame
    {
        public override int Width => scene.Width;
        public override int Height => scene.Height;
        public override int Rotation => rotation;
        public override bool IsMirrored => false;
        public override CameraFrameFormat Format => CameraFrameFormat.Grayscale8;
        protected override byte[] MaterializeLuminance() => scene.Pixels;
    }

    [Fact]
    public async Task Reads_a_small_rotated_code_and_reports_it_upright()
    {
        // 1.3 px per module, 20° off, defocused: the case a whole-frame decoder does not see
        var scene = SyntheticBarcode.Render("5901234123457", ZxFormat.EAN_13, 1.3f, angleDegrees: 20, blur: 0.4f, noise: 3, contrast: 120, width: 1280, height: 720, centerX: 900, centerY: 200);
        var analyzer = new BarcodeAnalyzer { Mode = BarcodeScanMode.LongRange, FullFrameInterval = 0 };
        BarcodesDetectedEventArgs? args = null;
        analyzer.BarcodesDetected += (_, e) => args = e;
        analyzer.Arm(); // results are gated until armed (CameraView.Scan())

        // sensor buffer rotated 90°: what is at the top right of the buffer is at the bottom right upright
        var boxes = await analyzer.AnalyzeAsync(new SceneFrame(scene, rotation: 90), default);

        args.ShouldNotBeNull();
        var code = args.First;
        code.Value.ShouldBe("5901234123457");
        code.Format.ShouldBe(BarcodeFormat.Ean13);
        var expected = CoordinateTransform.ApplyOrientation(new Microsoft.Maui.Graphics.PointF(900f / 1280, 200f / 720), 90, false);
        code.BoundingBox.Center.X.ShouldBe(expected.X, 0.05f);
        code.BoundingBox.Center.Y.ShouldBe(expected.Y, 0.05f);
        boxes.ShouldNotBeNull().ShouldContain(b => b.Text == "5901234123457");
    }

    [Fact]
    public async Task Delivers_once_while_the_code_stays_and_again_after_it_returns()
    {
        var code = SyntheticBarcode.Render("SHINY-0042", ZxFormat.CODE_128, 2f, width: 640, height: 360);
        var empty = SyntheticBarcode.Render("SHINY-0042", ZxFormat.CODE_128, 2f, width: 640, height: 360, centerX: 5000);
        // continuous scanning: stay armed after each delivery
        var analyzer = new BarcodeAnalyzer { Mode = BarcodeScanMode.LongRange, FullFrameInterval = 0, OnDetected = _ => Task.FromResult(true) };
        var count = 0;
        analyzer.BarcodesDetected += (_, _) => count++;
        analyzer.Arm();

        for (var i = 0; i < 4; i++)
            await analyzer.AnalyzeAsync(new SceneFrame(code), default);
        count.ShouldBe(1);

        // gone for longer than the tracker lingers
        for (var i = 0; i < 3; i++)
        {
            await Task.Delay(200);
            await analyzer.AnalyzeAsync(new SceneFrame(empty), default);
        }
        await analyzer.AnalyzeAsync(new SceneFrame(code), default);
        count.ShouldBe(2);
    }

    [Fact]
    public async Task Leaves_2d_candidates_to_the_native_crop_pass()
    {
        // a QR code too small for a whole-frame pass, found by the locator and read from an enlarged crop
        var scene = SyntheticBarcode.Render("https://shinylib.net/lr", ZxFormat.QR_CODE, 2.2f, width: 1280, height: 720, centerX: 700, centerY: 380);
        var analyzer = new BarcodeAnalyzer { Mode = BarcodeScanMode.LongRange, FullFrameInterval = 0 };
        BarcodesDetectedEventArgs? args = null;
        analyzer.BarcodesDetected += (_, e) => args = e;
        analyzer.Arm(); // results are gated until armed (CameraView.Scan())

        await analyzer.AnalyzeAsync(new SceneFrame(scene), default);

        args.ShouldNotBeNull().First.Value.ShouldBe("https://shinylib.net/lr");
    }

    [Fact]
    public async Task Auto_zoom_pulls_in_on_a_code_too_small_to_read()
    {
        // 0.8 px per module: located, never read
        var scene = SyntheticBarcode.Render("5901234123457", ZxFormat.EAN_13, 0.8f, angleDegrees: 3, width: 1280, height: 720);
        var view = new CameraView { MaxZoom = 10 };
        var analyzer = new BarcodeAnalyzer { Mode = BarcodeScanMode.LongRange, AutoZoom = true, FullFrameInterval = 0, MaxNativeCropsPerFrame = 0 };
        view.Analyzer = analyzer;

        for (var i = 0; i < 20; i++)
        {
            await analyzer.AnalyzeAsync(new SceneFrame(scene), default);
            await Task.Delay(20);
        }

        view.Zoom.ShouldBeGreaterThan(1.5);
        view.Zoom.ShouldBeLessThanOrEqualTo(4.0);
    }

    [Fact]
    public async Task Auto_zoom_off_leaves_the_camera_alone()
    {
        var scene = SyntheticBarcode.Render("5901234123457", ZxFormat.EAN_13, 0.8f, angleDegrees: 3, width: 1280, height: 720);
        var view = new CameraView { MaxZoom = 10 };
        var analyzer = new BarcodeAnalyzer { Mode = BarcodeScanMode.LongRange, FullFrameInterval = 0, MaxNativeCropsPerFrame = 0 };
        view.Analyzer = analyzer;

        for (var i = 0; i < 20; i++)
        {
            await analyzer.AnalyzeAsync(new SceneFrame(scene), default);
            await Task.Delay(20);
        }
        view.Zoom.ShouldBe(1.0);
    }

    [Fact]
    public void Long_range_raises_an_untouched_analysis_resolution()
    {
        var view = new CameraView();
        view.Analyzer = new BarcodeAnalyzer { Mode = BarcodeScanMode.LongRange };
        view.AnalysisResolution.ShouldBe(AnalysisResolution.FullHd);
    }

    [Fact]
    public void Long_range_keeps_a_chosen_analysis_resolution_and_standard_touches_nothing()
    {
        var chosen = new CameraView { AnalysisResolution = AnalysisResolution.Hd };
        chosen.Analyzer = new BarcodeAnalyzer { Mode = BarcodeScanMode.LongRange };
        chosen.AnalysisResolution.ShouldBe(AnalysisResolution.Hd);

        var standard = new CameraView();
        var analyzer = new BarcodeAnalyzer();
        standard.Analyzer = analyzer;
        standard.AnalysisResolution.ShouldBe(AnalysisResolution.Default);

        // switching mode later still applies
        analyzer.Mode = BarcodeScanMode.LongRange;
        standard.AnalysisResolution.ShouldBe(AnalysisResolution.FullHd);
    }
}
