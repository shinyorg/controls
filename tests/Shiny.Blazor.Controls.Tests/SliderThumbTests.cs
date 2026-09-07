using System.Globalization;
using Shiny.Blazor.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// The thumb box and the space reserved around it. Everything asserted here is decided in the component
/// rather than the renderer — the declarations it writes inline — so the sliders can be driven directly.
/// </summary>
public class SliderThumbTests
{
    // ---------------------------------------------------------------------------------------------
    // Slider
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheThumbIsSquareAtThumbSizeUntilItIsToldOtherwise()
    {
        var slider = new Slider { ThumbSize = 28 };

        slider.ThumbStyle.ShouldContain("width: 28px;");
        slider.ThumbStyle.ShouldContain("height: 28px;");
    }


    [Fact]
    public void AThumbCanBeWiderThanItIsTall()
    {
        var slider = new Slider { ThumbWidth = 60, ThumbHeight = 24 };

        slider.ThumbStyle.ShouldContain("width: 60px;");
        slider.ThumbStyle.ShouldContain("height: 24px;");
    }


    [Fact]
    public void TheThumbIsFullyRoundedUntilACornerRadiusSaysOtherwise()
    {
        // Half the shorter side: a circle while the thumb is square, a pill once it is not.
        new Slider { ThumbWidth = 60, ThumbHeight = 24 }.ThumbStyle.ShouldContain("border-radius: 12px;");
        new Slider { ThumbCornerRadius = "4px" }.ThumbStyle.ShouldContain("border-radius: 4px;");
    }


    [Fact]
    public void ThePaddingIsOnlyWrittenWhenThereIsOneToWrite()
    {
        new Slider().ThumbStyle.ShouldNotContain("padding");
        new Slider { ThumbPadding = "2px 6px" }.ThumbStyle.ShouldContain("padding: 2px 6px;");
    }


    [Fact]
    public void TheBandsClearTheThumbRatherThanTheTrack()
    {
        // The bands are what keeps the thumb off the tooltip and off whatever follows the slider, so a
        // tall custom thumb has to be what they are measured against — not the square ThumbSize.
        var slider = new Slider { TrackHeight = 8, ThumbSize = 24, ThumbHeight = 48 };

        slider.RootStyle.ShouldContain("--shiny-gs-label-band: 20px");
    }


    [Fact]
    public void EveryNumberInAThumbStyleIsWrittenInvariantly()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            new Slider { ThumbWidth = 60.5, ThumbHeight = 24 }.ThumbStyle.ShouldContain("width: 60.5px;");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }


    // ---------------------------------------------------------------------------------------------
    // RangeSlider
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BothRangeThumbsUseTheSameBox()
    {
        var slider = new RangeSlider { ThumbWidth = 44, ThumbHeight = 20 };

        var style = slider.ThumbStyle(25, "#663399");
        style.ShouldContain("width: 44px;");
        style.ShouldContain("height: 20px;");
        style.ShouldContain("border-radius: 10px;");
    }


    [Fact]
    public void TheRangeSliderReservesTheHalfOfTheThumbHangingPastTheTrack()
    {
        new RangeSlider { TrackHeight = 8, ThumbHeight = 48 }
            .RootStyle.ShouldContain("--shiny-rs-thumb-overhang: 20px;");
    }


    [Fact]
    public void EveryNumberInARangeStyleIsWrittenInvariantly()
    {
        // A comma decimal separator is a silently invalid CSS declaration, and the browser drops the
        // whole rule — so the thumbs simply render in the wrong place under a European culture.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            new RangeSlider().ThumbStyle(12.5, "#663399").ShouldStartWith("left: 12.5%;");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
