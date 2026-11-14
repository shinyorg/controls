using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Analysis.Barcode;
using Shouldly;
using Xunit;
using ZXing;
using ZxFormat = ZXing.BarcodeFormat;
using BarcodeFormat = Shiny.Controls.Analysis.BarcodeFormat;

namespace Shiny.Maui.Controls.Camera.Tests;

// The net10.0 target shares the Windows ZXing decoder, so these cover the Windows scan path.
public class BarcodeScannerTests
{
    const int W = 640;
    const int H = 480;

    [Fact]
    public async Task Decodes_a_qr_code_from_the_luminance_plane()
    {
        var frame = Frame((ZxFormat.QR_CODE, "shiny-pair-1234", 80, 80, 200, 200));

        var codes = await new BarcodeScanner().ScanAsync(frame, default);

        var code = codes.ShouldHaveSingleItem();
        code.Value.ShouldBe("shiny-pair-1234");
        code.Format.ShouldBe(BarcodeFormat.QrCode);
        // the box lands where the code was drawn (normalized), within the quiet zone
        code.BoundingBox.Center.X.ShouldBe(180f / W, 0.05f);
        code.BoundingBox.Center.Y.ShouldBe(180f / H, 0.05f);
    }

    [Fact]
    public async Task Reports_every_code_in_the_frame()
    {
        var frame = Frame(
            (ZxFormat.QR_CODE, "left", 20, 120, 220, 220),
            (ZxFormat.QR_CODE, "right", 380, 120, 220, 220)
        );

        var codes = await new BarcodeScanner().ScanAsync(frame, default);

        codes.Select(c => c.Value).OrderBy(v => v).ShouldBe(["left", "right"]);
    }

    [Fact]
    public async Task Formats_filter_out_other_symbologies()
    {
        var frame = Frame((ZxFormat.QR_CODE, "qr", 80, 80, 200, 200));

        var codes = await new BarcodeScanner { Formats = [BarcodeFormat.Ean13] }.ScanAsync(frame, default);

        codes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Scan_window_excludes_codes_outside_it()
    {
        var frame = Frame((ZxFormat.QR_CODE, "left", 20, 120, 220, 220));

        var outside = await new BarcodeScanner { ScanWindow = new RectF(0.5f, 0f, 0.5f, 1f) }.ScanAsync(frame, default);
        outside.ShouldBeEmpty();

        var inside = await new BarcodeScanner { ScanWindow = new RectF(0f, 0f, 0.5f, 1f) }.ScanAsync(frame, default);
        var code = inside.ShouldHaveSingleItem();
        code.Value.ShouldBe("left");
        // boxes come back in full-frame space, not crop space
        code.BoundingBox.Center.X.ShouldBe(130f / W, 0.05f);
    }

    [Fact]
    public async Task Linear_codes_get_a_drawable_box()
    {
        var frame = Frame((ZxFormat.CODE_128, "SHINY-128", 120, 180, 400, 120));

        var codes = await new BarcodeScanner().ScanAsync(frame, default);

        var code = codes.ShouldHaveSingleItem();
        code.Value.ShouldBe("SHINY-128");
        code.Format.ShouldBe(BarcodeFormat.Code128);
        code.BoundingBox.Height.ShouldBeGreaterThan(0f);
    }

    [Fact]
    public async Task Mirrored_frames_report_mirror_corrected_boxes()
    {
        var frame = Frame((ZxFormat.QR_CODE, "selfie", 20, 120, 220, 220));
        frame.Mirrored = true;

        var code = (await new BarcodeScanner().ScanAsync(frame, default)).ShouldHaveSingleItem();

        // drawn on the left of the sensor -> shown on the right of a mirrored preview
        code.BoundingBox.Center.X.ShouldBe(1f - 130f / W, 0.05f);
    }

    // white canvas with each code rendered (black on white) into its own rectangle
    static LumFrame Frame(params (ZxFormat Format, string Value, int X, int Y, int Width, int Height)[] codes)
    {
        var lum = new byte[W * H];
        Array.Fill(lum, (byte)255);

        var writer = new MultiFormatWriter();
        foreach (var c in codes)
        {
            var matrix = writer.encode(c.Value, c.Format, c.Width, c.Height);
            for (var y = 0; y < matrix.Height; y++)
            for (var x = 0; x < matrix.Width; x++)
            {
                if (matrix[x, y])
                    lum[(c.Y + y) * W + c.X + x] = 0;
            }
        }
        return new LumFrame(lum);
    }

    sealed class LumFrame(byte[] luminance) : VideoFrame
    {
        public bool Mirrored { get; set; }
        public override int Width => W;
        public override int Height => H;
        public override int Rotation => 0;
        public override bool IsMirrored => this.Mirrored;
        public override VideoFrameFormat Format => VideoFrameFormat.Grayscale8;
        protected override byte[] MaterializeLuminance() => luminance;
    }
}
