using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// Custom thumb content, and the thumb box it goes in. Nothing here goes through a real layout pass —
/// headless MAUI never arranges anything — so the slider is told what size it was given and the
/// placement it computed is read back off the absolute layout.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class SliderThumbTests
{
    public SliderThumbTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }


    static Border ThumbOf(Slider slider) => (Border)slider.ThumbView;


    // ---------------------------------------------------------------------------------------------
    // The thumb box
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheThumbIsSquareAtThumbSizeUntilItIsToldOtherwise()
    {
        var slider = new Slider { ThumbSize = 28 };
        slider.SetLayoutSize(200, 60);

        slider.ThumbBounds.Width.ShouldBe(28);
        slider.ThumbBounds.Height.ShouldBe(28);
    }


    [Fact]
    public void AWideThumbEatsIntoTheTravelOnItsOwnAxisOnly()
    {
        var slider = new Slider { ThumbSize = 24, ThumbWidth = 60, Value = 100 };
        slider.SetLayoutSize(200, 80);

        slider.ThumbBounds.Width.ShouldBe(60);
        slider.ThumbBounds.Height.ShouldBe(24);

        // The travel is the track less one thumb, so the maximum parks the thumb flush with the far end
        // rather than half of it hanging off.
        slider.ThumbBounds.X.ShouldBe(140);
        (slider.ThumbBounds.X + slider.ThumbBounds.Width).ShouldBe(200);
    }


    [Fact]
    public void AVerticalSliderReadsTheHeightAsTheTravellingAxis()
    {
        var slider = new Slider
        {
            Orientation = SliderOrientation.Vertical,
            ThumbSize = 24,
            ThumbWidth = 60,
            ThumbHeight = 40,
            Value = 0
        };
        slider.SetLayoutSize(120, 200);

        slider.ThumbAlong.ShouldBe(40);
        slider.ThumbAcross.ShouldBe(60);

        // The minimum sits at the bottom, so the thumb's lower edge is flush with the floor.
        (slider.ThumbBounds.Y + slider.ThumbBounds.Height).ShouldBe(200);
    }


    [Fact]
    public void TheTrackBandGrowsToClearAThumbTallerThanTheTrack()
    {
        var slider = new Slider { ShowTooltip = false, TrackHeight = 8, ThumbSize = 24, ThumbHeight = 48 };
        slider.SetLayoutSize(200, 48);

        // The whole thumb is inside the box the slider asked for, not overhanging it.
        slider.ThumbBounds.Y.ShouldBe(0);
        slider.ThumbBounds.Height.ShouldBe(48);
    }


    [Fact]
    public void TheThumbIsFullyRoundedUntilACornerRadiusSaysOtherwise()
    {
        var pill = new Slider { ThumbWidth = 60, ThumbHeight = 24 };
        pill.SetLayoutSize(200, 60);
        ((RoundRectangle)ThumbOf(pill).StrokeShape).CornerRadius.TopLeft.ShouldBe(12);

        var squared = new Slider { ThumbWidth = 60, ThumbHeight = 24, ThumbCornerRadius = 4 };
        squared.SetLayoutSize(200, 60);
        ((RoundRectangle)ThumbOf(squared).StrokeShape).CornerRadius.TopLeft.ShouldBe(4);
    }


    // ---------------------------------------------------------------------------------------------
    // Template content
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheTemplateIsRealizedIntoTheThumb()
    {
        var slider = new Slider { ThumbTemplate = new DataTemplate(() => new Label { Text = "°" }) };
        slider.SetLayoutSize(200, 60);

        ThumbOf(slider).Content.ShouldBeOfType<Label>().Text.ShouldBe("°");
    }


    [Fact]
    public void TheContentIsBuiltOnceAndReboundAsTheThumbMoves()
    {
        var built = 0;
        var slider = new Slider
        {
            ThumbTemplate = new DataTemplate(() =>
            {
                built++;
                return new Label();
            })
        };
        slider.SetLayoutSize(200, 60);

        slider.Value = 40;
        slider.Value = 70;

        // Rebuilding per draw would throw the content away on every pixel of a drag.
        built.ShouldBe(1);
        ThumbOf(slider).Content!.BindingContext.ShouldBe(70.0);
    }


    [Fact]
    public void TheContentDoesNotSwallowTheDrag()
    {
        var slider = new Slider { ThumbTemplate = new DataTemplate(() => new Label()) };
        slider.SetLayoutSize(200, 60);

        // The pan and tap live on the root layout, and Border does not pass InputTransparent down.
        ThumbOf(slider).Content.ShouldBeOfType<Label>().InputTransparent.ShouldBeTrue();
    }


    [Fact]
    public void ANewTemplateReplacesTheContentThatIsThere()
    {
        var slider = new Slider { ThumbTemplate = new DataTemplate(() => new Label { Text = "first" }) };
        slider.SetLayoutSize(200, 60);

        slider.ThumbTemplate = new DataTemplate(() => new Label { Text = "second" });

        ThumbOf(slider).Content.ShouldBeOfType<Label>().Text.ShouldBe("second");
    }
}
