using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Camera.Barcode;
using Shiny.Maui.Controls.Camera.Documents;
using Shiny.Maui.Controls.Camera.Media;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Camera.Tests;

/// <summary>
/// The <see cref="IMediaService"/> contract that does not need a window: how options resolve, how the scan
/// request is wired, and what the typed <c>Scan…</c> extensions build.
/// </summary>
public class MediaServiceOptionTests
{
    [Fact]
    public void Encoding_options_default_to_unset_so_the_service_defaults_apply()
    {
        // These are nullable on purpose. If they carried their own literal defaults there would be no way to
        // tell "the caller wants 92" from "the caller said nothing", and a service-wide
        // `CompressionQuality = 70` would silently never apply.
        var photo = new PhotoCaptureOptions();
        photo.CompressionQuality.ShouldBeNull();
        photo.MaxDimension.ShouldBeNull();
        photo.OutputFormat.ShouldBeNull();

        var pick = new MediaPickOptions();
        pick.CompressionQuality.ShouldBeNull();
        pick.MaxDimension.ShouldBeNull();
        pick.OutputFormat.ShouldBeNull();
    }


    [Fact]
    public void Service_defaults_are_a_sensible_jpeg()
    {
        var options = new MediaServiceOptions();

        options.CompressionQuality.ShouldBe(92);
        options.MaxDimension.ShouldBe(0);
        options.OutputFormat.ShouldBe(MediaImageFormat.Jpeg);
    }


    [Fact]
    public void Duplicate_filter_argument_wins_over_the_options_value()
    {
        // The extensions take `filterDuplicates` as a named argument because that is what a caller reaches
        // for; the options object is the long form. When both are supplied the argument has to win, or the
        // shorter spelling would be the one that silently does nothing.
        var options = new MediaScanOptions { FilterDuplicates = true };

        options.WithDuplicateFilter(false).FilterDuplicates.ShouldBeFalse();
        options.WithDuplicateFilter(true).FilterDuplicates.ShouldBeTrue();
    }


    [Fact]
    public void Duplicate_filter_creates_options_when_none_were_supplied()
    {
        MediaScanOptions? none = null;

        var resolved = none.WithDuplicateFilter(false);

        resolved.ShouldNotBeNull();
        resolved.FilterDuplicates.ShouldBeFalse();
    }


    [Fact]
    public void Scan_defaults_filter_duplicates_and_show_their_count()
    {
        var options = new MediaScanOptions();

        options.FilterDuplicates.ShouldBeTrue();
        options.ShowResultCount.ShouldBeTrue();
        options.ShowDoneButton.ShouldBeTrue();
        options.ShowBoundingBox.ShouldBeTrue();
        options.MaxResults.ShouldBeNull();
        options.Timeout.ShouldBeNull();
    }


    [Fact]
    public void Effect_choices_start_with_none_and_cover_grades_and_spatial_looks()
    {
        var choices = MediaEffectChoices.Default;

        choices[0].Name.ShouldBe("None");
        choices[0].Filter.ShouldBe(CameraFilter.None);
        choices[0].Effect.ShouldBeNull();

        // every colour grade, and no duplicate of None
        foreach (var filter in Enum.GetValues<CameraFilter>().Where(f => f != CameraFilter.None))
            choices.ShouldContain(c => c.Filter == filter && c.Effect == null);

        // spatial looks ride on top of an un-graded frame
        choices.ShouldContain(c => c.Name == "Comic" && c.Effect != null);
        choices.ShouldContain(c => c.Name == "Blur" && c.Effect != null);
    }


    [Fact]
    public void Barcode_request_emits_every_code_in_a_frame_and_stays_armed()
    {
        // A frame can hold several codes. The subscription has to fan them out as individual results —
        // yielding only the first would silently drop codes from a shelf full of them.
        var analyzer = new BarcodeAnalyzer();
        var emitted = new List<DetectedBarcode>();

        var request = new MediaScanRequest<DetectedBarcode>
        {
            Analyzer = analyzer,
            Subscribe = emit => analyzer.OnDetected = args =>
            {
                foreach (var code in args.Barcodes)
                    emit(code);

                return Task.FromResult(true);
            },
            DuplicateKey = b => $"{b.Format}|{b.Value}"
        };

        request.Subscribe(emitted.Add);
        var stayArmed = analyzer.OnDetected!(new BarcodesDetectedEventArgs(
        [
            new DetectedBarcode("A", BarcodeFormat.QrCode, RectF.Zero),
            new DetectedBarcode("B", BarcodeFormat.Ean13, RectF.Zero)
        ])).Result;

        emitted.Select(e => e.Value).ShouldBe(["A", "B"]);
        stayArmed.ShouldBeTrue(); // the service decides when to stop, never the analyzer
    }


    [Fact]
    public void Barcode_duplicate_key_separates_symbologies()
    {
        // The same digits as an EAN-13 and as a QR code are two different scans, so the key has to carry the
        // symbology — keying on the value alone would swallow the second one.
        var request = BuildBarcodeRequest();

        var qr = request.DuplicateKey!(new DetectedBarcode("12345", BarcodeFormat.QrCode, RectF.Zero));
        var ean = request.DuplicateKey!(new DetectedBarcode("12345", BarcodeFormat.Ean13, RectF.Zero));

        qr.ShouldNotBe(ean);
    }


    [Fact]
    public void Document_request_emits_the_accumulated_document()
    {
        var analyzer = new CreditCardAnalyzer();
        CreditCard? emitted = null;

        var request = new MediaScanRequest<CreditCard>
        {
            Analyzer = analyzer,
            Subscribe = emit => analyzer.OnDetected = args =>
            {
                emit(args.Document);
                return Task.FromResult(true);
            }
        };

        var card = new CreditCard(CreditCardType.Visa, "4111111111111111", null, null, null, null, null, []);
        request.Subscribe(c => emitted = c);
        analyzer.OnDetected!(new DocumentDetectedEventArgs<CreditCard>(card)).Wait();

        emitted.ShouldBe(card);
    }


    static MediaScanRequest<DetectedBarcode> BuildBarcodeRequest()
    {
        var analyzer = new BarcodeAnalyzer();
        return new MediaScanRequest<DetectedBarcode>
        {
            Analyzer = analyzer,
            Subscribe = _ => { },
            DuplicateKey = b => $"{b.Format}|{b.Value}"
        };
    }
}
