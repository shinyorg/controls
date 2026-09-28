using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Shell;
using Shiny.Controls.Office.Skia;
using Shiny.Controls.Office.Spreadsheet.View;
using Shouldly;
using Xunit;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// Fixes from the Office editors' device pass that live in the shared engine: the opening zoom on a
/// narrow screen, the status bar's range matching each editor's clamp, and the backstage's template
/// pictures.
/// </summary>
public class OfficeDeviceLeftoverTests
{
    // ---- opening zoom ---------------------------------------------------------------------------

    [Fact]
    public void APageThatFitsOpensAtOneHundredPercent()
        => OfficeZoomModel.Default.OpeningZoom(816, 1200).ShouldBe(1);

    [Fact]
    public void APageWiderThanAPhoneOpensAtPageWidth()
    {
        // A Letter page (8.5in at 96dpi) on a 390pt iPhone: fit, less the 24pt gutter each side.
        var zoom = OfficeZoomModel.Default.OpeningZoom(816, 390);

        zoom.ShouldBe(OfficeZoomModel.Default.Fit(816, 390));
        zoom.ShouldBeLessThan(0.5);
        (816 * zoom).ShouldBeLessThanOrEqualTo(390 - 48);
    }

    [Fact]
    public void TheOpeningZoomNeverZoomsIn()
        => OfficeZoomModel.Default.OpeningZoom(400, 3000).ShouldBe(1);

    [Theory]
    [InlineData(0, 390)]
    [InlineData(816, 0)]
    public void WithoutASizeTheOpeningZoomIsOneHundredPercent(double page, double viewport)
        => OfficeZoomModel.Default.OpeningZoom(page, viewport).ShouldBe(1);

    [Fact]
    public void TheOpeningZoomStaysInsideTheEditorsRange()
    {
        var word = new OfficeZoomModel(DocumentController.MinimumZoom, DocumentController.MaximumZoom, 1.0);

        word.OpeningZoom(816, 100).ShouldBe(DocumentController.MinimumZoom);
    }

    // ---- zoom ranges ----------------------------------------------------------------------------

    [Fact]
    public void EachEditorsZoomRangeIsWhatItsControllerClampsTo()
    {
        DocumentController.MinimumZoom.ShouldBe(0.25);
        DocumentController.MaximumZoom.ShouldBe(4.0);
        SpreadsheetController.MinZoom.ShouldBe(0.1);
        SpreadsheetController.MaxZoom.ShouldBe(4.0);
        SlideController.MinimumZoom.ShouldBe(0.1);
        SlideController.MaximumZoom.ShouldBe(4.0);
    }

    // ---- template thumbnails --------------------------------------------------------------------

    static void ShouldBePictured(IReadOnlyList<OfficeTemplate> pictured, IReadOnlyList<OfficeTemplate> source)
    {
        pictured.Select(x => x.Id).ShouldBe(source.Select(x => x.Id));

        foreach (var template in pictured)
        {
            if (template.IsBlank)
            {
                template.Thumbnail.ShouldBeNull();
                continue;
            }

            template.Thumbnail.ShouldNotBeNull();
            template.Thumbnail!.ShouldStartWith("data:image/png;base64,");

            var png = Convert.FromBase64String(template.Thumbnail["data:image/png;base64,".Length..]);
            png.Length.ShouldBeGreaterThan(500);
            png[1..4].ShouldBe("PNG"u8.ToArray());

            var original = source.Single(x => x.Id == template.Id);
            template.Name.ShouldBe(original.Name);
            template.Description.ShouldBe(original.Description);
            template.Category.ShouldBe(original.Category);
            template.Open.ShouldBeSameAs(original.Open);
        }
    }

    [Fact]
    public void WordTemplatesGetAPictureOfTheirFirstPage()
        => ShouldBePictured(OfficeTemplateThumbnails.Word(), WordTemplates.All);

    [Fact]
    public void SpreadsheetTemplatesGetAPictureOfTheirFirstSheet()
        => ShouldBePictured(OfficeTemplateThumbnails.Spreadsheet(), SpreadsheetTemplates.All);

    [Fact]
    public void SlideTemplatesGetAPictureOfTheirFirstSlide()
        => ShouldBePictured(OfficeTemplateThumbnails.Slides(), SlideTemplates.All);

    [Fact]
    public void ATemplateThatFailsToDrawKeepsItsPlainTile()
    {
        var template = new OfficeTemplate("broken", "Broken");

        var result = OfficeTemplateThumbnails.With([template], _ => throw new InvalidOperationException("no"));

        result.ShouldHaveSingleItem().ShouldBeSameAs(template);
    }

    [Fact]
    public void AHostsOwnPictureIsLeftAlone()
    {
        var template = new OfficeTemplate("mine", "Mine") { Thumbnail = "https://example.com/a.png" };

        var result = OfficeTemplateThumbnails.With([template], _ => throw new InvalidOperationException("should not draw"));

        result.ShouldHaveSingleItem().Thumbnail.ShouldBe("https://example.com/a.png");
    }
}
