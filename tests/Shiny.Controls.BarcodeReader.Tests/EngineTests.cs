using Shiny.Controls.BarcodeReader.Linear;
using ZxFormat = ZXing.BarcodeFormat;

namespace Shiny.Controls.BarcodeReader.Tests;

public class EngineTests
{
    static BarcodeRead? Scan(SyntheticBarcode.Scene scene, out List<BarcodeCandidate> candidates)
    {
        var engine = new BarcodeReaderEngine();
        candidates = engine.Scan(new LumaImage(scene.Pixels, scene.Width, scene.Height));
        return candidates.Select(c => c.Read).FirstOrDefault(r => r != null);
    }

    [Theory]
    [InlineData("5901234123457", ZxFormat.EAN_13, Symbology.Ean13, "5901234123457")]
    [InlineData("036000291452", ZxFormat.UPC_A, Symbology.UpcA, "036000291452")]
    [InlineData("96385074", ZxFormat.EAN_8, Symbology.Ean8, "96385074")]
    [InlineData("01234565", ZxFormat.UPC_E, Symbology.UpcE, "01234565")]
    [InlineData("Shiny-128 test", ZxFormat.CODE_128, Symbology.Code128, "Shiny-128 test")]
    [InlineData("12345678901234", ZxFormat.CODE_128, Symbology.Code128, "12345678901234")]
    [InlineData("SHINY-39", ZxFormat.CODE_39, Symbology.Code39, "SHINY-39")]
    [InlineData("12345670", ZxFormat.ITF, Symbology.Itf, "12345670")]
    public void Reads_every_linear_symbology(string text, ZxFormat format, Symbology expected, string value)
    {
        var scene = SyntheticBarcode.Render(text, format, module: 2.5f, width: 640, height: 360);
        var read = Scan(scene, out _);
        read.ShouldNotBeNull();
        read.Symbology.ShouldBe(expected);
        read.Text.ShouldBe(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(160)]
    [InlineData(180)]
    public void Reads_at_any_rotation(float angle)
    {
        var scene = SyntheticBarcode.Render("5901234123457", ZxFormat.EAN_13, module: 2.2f, angleDegrees: angle, width: 640, height: 480);
        Scan(scene, out _).ShouldNotBeNull().Text.ShouldBe("5901234123457");
    }

    [Fact]
    public void Reads_a_soft_distant_ean13()
    {
        // ~1.3 px per module, defocused and noisy — the far-away case the native engines drop
        var scene = SyntheticBarcode.Render("5901234123457", ZxFormat.EAN_13, module: 1.3f, angleDegrees: 6, blur: 0.6f, noise: 6, contrast: 120, width: 960, height: 540);
        var read = Scan(scene, out _);
        read.ShouldNotBeNull();
        read.Text.ShouldBe("5901234123457");
    }

    [Fact]
    public void Reads_a_soft_distant_code128()
    {
        var scene = SyntheticBarcode.Render("SHINY0042", ZxFormat.CODE_128, module: 1.4f, angleDegrees: 8, blur: 0.5f, noise: 5, contrast: 140, width: 960, height: 540);
        Scan(scene, out _).ShouldNotBeNull().Text.ShouldBe("SHINY0042");
    }

    [Fact]
    public void Locates_a_code_off_center()
    {
        var scene = SyntheticBarcode.Render("5901234123457", ZxFormat.EAN_13, module: 1.6f, width: 1280, height: 720, centerX: 300, centerY: 200);
        Scan(scene, out var candidates).ShouldNotBeNull().Text.ShouldBe("5901234123457");
        var box = candidates.First(c => c.Read != null).Read!.Box;
        box.CenterX.ShouldBe(300, 25);
        box.CenterY.ShouldBe(200, 25);
    }

    [Fact]
    public void Locates_a_matrix_code_but_leaves_it_for_the_native_decoder()
    {
        var scene = SyntheticBarcode.Render("https://shinylib.net", ZxFormat.QR_CODE, module: 4f, width: 640, height: 480);
        var read = Scan(scene, out var candidates);
        read.ShouldBeNull();
        candidates.ShouldContain(c => c.Kind == CandidateKind.Matrix);

        var matrix = candidates.First(c => c.Kind == CandidateKind.Matrix);
        matrix.Box.CenterX.ShouldBe(320, 40);
        matrix.Box.CenterY.ShouldBe(240, 40);
    }

    [Fact]
    public void Finds_nothing_in_noise_or_a_plain_edge()
    {
        var w = 640;
        var h = 360;
        var px = new byte[w * h];
        var rng = new Random(3);
        for (var i = 0; i < px.Length; i++)
            px[i] = (byte)((i % w < w / 2 ? 60 : 200) + rng.Next(-4, 5));

        var engine = new BarcodeReaderEngine();
        engine.Scan(new LumaImage(px, w, h)).Where(c => c.Kind == CandidateKind.Linear).ShouldBeEmpty();
    }

    [Fact]
    public void Respects_the_format_filter()
    {
        var scene = SyntheticBarcode.Render("5901234123457", ZxFormat.EAN_13, module: 2.5f, width: 640, height: 360);
        var engine = new BarcodeReaderEngine { Formats = FormatSet.Of([Symbology.Code128]) };
        engine.Scan(new LumaImage(scene.Pixels, scene.Width, scene.Height)).ShouldAllBe(c => c.Read == null);
    }

    [Fact]
    public void Measures_element_width_for_auto_zoom()
    {
        var scene = SyntheticBarcode.Render("5901234123457", ZxFormat.EAN_13, module: 2f, width: 640, height: 360);
        Scan(scene, out var candidates);
        var c = candidates.First(c => c.Kind == CandidateKind.Linear);
        // EAN elements average 95/59 modules
        c.ElementWidth.ShouldBe(2f * 95 / 59, 0.6f);
    }

    [Fact]
    public void Plans_an_enlarging_crop_for_a_small_candidate()
    {
        var c = new BarcodeCandidate(CandidateKind.Matrix, new OrientedBox(500, 300, 0, 30, 30), 1);
        var plan = BarcodeReaderEngine.PlanCrop(c, 1920, 1080);
        plan.Scale.ShouldBeGreaterThan(3f);
        plan.Source.X.ShouldBeLessThan(470);
        plan.Source.Right.ShouldBeGreaterThan(530);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(34)]
    [InlineData(90)]
    public void Reads_a_square_on_code_whose_digits_blur_alike(float angle)
    {
        // 1/7 and 2/8 differ only in bar widths; held square to the sensor every line blurs them the same
        // way, so this needs the check-digit tie-break rather than more votes
        var scene = SyntheticBarcode.Render("5901234123457", ZxFormat.EAN_13, module: 1.3f, angleDegrees: angle, blur: 0.4f, noise: 3, contrast: 120, width: 400, height: 260);
        var engine = new BarcodeReaderEngine();
        var box = new OrientedBox(scene.CenterX, scene.CenterY, scene.Angle, scene.HalfLength, scene.HalfHeight);
        engine.ReadLinear(new LumaImage(scene.Pixels, scene.Width, scene.Height), box).ShouldNotBeNull().Text.ShouldBe("5901234123457");
    }

    [Fact]
    public void Pattern_tables_are_well_formed()
    {
        Code128Decoder.Patterns.Length.ShouldBe(107);
        Code128Decoder.Patterns.ShouldAllBe(p => p.Sum() == 11 && p.Length == 6);
        Code128Decoder.Patterns.Select(p => string.Concat(p)).Distinct().Count().ShouldBe(107);
        EanDecoder.LG.ShouldAllBe(p => p.Sum() == 7);
    }
}
