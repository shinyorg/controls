using System.Globalization;
using Shiny.Blazor.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// Stop points and the vertical orientation. Everything asserted here is decided in the component
/// rather than the renderer — which mark the value comes to rest on, and which axis a style is written
/// against — so the slider can be driven directly.
/// </summary>
public class SliderMarkTests
{
    static (Slider Slider, ISliderMarkHost Host) Build(
        SliderOrientation orientation = SliderOrientation.Horizontal,
        params SliderMark[] marks
    )
    {
        var slider = new Slider
        {
            Orientation = orientation,
            Minimum = 0,
            Maximum = 100
        };

        var host = (ISliderMarkHost)slider;
        foreach (var mark in marks)
            host.RegisterMark(mark);

        return (slider, host);
    }


    // ---------------------------------------------------------------------------------------------
    // Snapping
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheValueComesToRestOnTheNearestMark()
    {
        var (slider, _) = Build(SliderOrientation.Horizontal,
            new SliderMark { Value = 0 },
            new SliderMark { Value = 40 },
            new SliderMark { Value = 90 });

        await slider.OnDragUpdate(0.55);

        slider.Value.ShouldBe(40);
    }


    [Fact]
    public async Task SnappingOverridesTheStep()
    {
        // Step would land on 33; the mark is what the thumb must come to rest on.
        var (slider, _) = Build(SliderOrientation.Horizontal, new SliderMark { Value = 37.5 });
        slider.Step = 1;

        await slider.OnDragUpdate(0.33);

        slider.Value.ShouldBe(37.5);
    }


    [Fact]
    public async Task MarksAreJustReferencePointsWhenSnappingIsOff()
    {
        var (slider, _) = Build(SliderOrientation.Horizontal, new SliderMark { Value = 90 });
        slider.SnapToMarks = false;
        slider.Step = 1;

        await slider.OnDragUpdate(0.33);

        slider.Value.ShouldBe(33);
    }


    [Fact]
    public async Task AHiddenMarkIsNotASnapTarget()
    {
        var (slider, _) = Build(SliderOrientation.Horizontal,
            new SliderMark { Value = 10, IsVisible = false },
            new SliderMark { Value = 80 });

        await slider.OnDragUpdate(0.15);

        slider.Value.ShouldBe(80);
    }


    [Fact]
    public async Task SnappingDoesNothingUntilThereAreMarks()
    {
        var (slider, _) = Build();
        slider.Step = 5;

        await slider.OnDragUpdate(0.33);

        slider.Value.ShouldBe(35);
    }


    // ---------------------------------------------------------------------------------------------
    // Registration
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AHiddenMarkIsNotDrawn()
    {
        var (slider, _) = Build(SliderOrientation.Horizontal,
            new SliderMark { Value = 10, IsVisible = false },
            new SliderMark { Value = 80 });

        slider.VisibleMarks.Count().ShouldBe(1);
    }


    [Fact]
    public void AMarkThatGoesAwayStopsBeingDrawn()
    {
        var mark = new SliderMark { Value = 10 };
        var (slider, host) = Build(SliderOrientation.Horizontal, mark);

        host.UnregisterMark(mark);

        slider.VisibleMarks.ShouldBeEmpty();
    }


    [Fact]
    public void RegisteringTheSameMarkTwiceDrawsItOnce()
    {
        var mark = new SliderMark { Value = 10 };
        var (slider, host) = Build(SliderOrientation.Horizontal, mark);

        host.RegisterMark(mark);

        slider.VisibleMarks.Count().ShouldBe(1);
    }


    // ---------------------------------------------------------------------------------------------
    // Styles
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void HorizontalPositionsAlongTheLeftEdge()
    {
        var (slider, _) = Build(SliderOrientation.Horizontal, new SliderMark { Value = 25 });
        slider.Value = 25;

        slider.OrientationClass.ShouldBe("shiny-gs-horizontal");
        slider.ThumbStyle.ShouldStartWith("left: 25%;");
        slider.MarkerStyle(slider.VisibleMarks.First(), SliderMarkShape.Dot).ShouldStartWith("left: 25%;");
        slider.MarkLabelStyle(slider.VisibleMarks.First(), SliderMarkShape.Dot).ShouldStartWith("left: 25%;");
    }


    [Fact]
    public void VerticalPositionsFromTheBottomSoTheMinimumIsAtTheFloor()
    {
        var (slider, _) = Build(SliderOrientation.Vertical, new SliderMark { Value = 25 });
        slider.Value = 25;

        slider.OrientationClass.ShouldBe("shiny-gs-vertical");
        slider.ThumbStyle.ShouldStartWith("bottom: 25%;");
        slider.MarkerStyle(slider.VisibleMarks.First(), SliderMarkShape.Dot).ShouldStartWith("bottom: 25%;");
        slider.MarkLabelStyle(slider.VisibleMarks.First(), SliderMarkShape.Dot).ShouldStartWith("bottom: 25%;");
    }


    [Fact]
    public void TheVerticalTrackIsGivenALengthBecauseItHasNoWidthToStretchInto()
    {
        var (slider, _) = Build(SliderOrientation.Vertical);
        slider.VerticalLength = 180;

        slider.TrackStyle.ShouldContain("height: 180px");
    }


    [Fact]
    public void AMarkPaintsItsOwnColourOverTheSliderDefault()
    {
        var (slider, _) = Build(SliderOrientation.Horizontal, new SliderMark { Value = 50, Color = "#663399" });
        slider.MarkColor = "#888888";

        slider.MarkerStyle(slider.VisibleMarks.First(), SliderMarkShape.Dot).ShouldContain("background: #663399");
    }


    [Fact]
    public void TheLabelBandHoldsNothingButTheThumbOverhangUntilThereIsACaptionToPutInIt()
    {
        // With no caption the band still has to hold the half of the thumb hanging past the track, or a
        // tall thumb spills onto whatever is laid out under the slider.
        var (bare, _) = Build(SliderOrientation.Horizontal, new SliderMark { Value = 50 });
        bare.RootStyle.ShouldContain("--shiny-gs-label-band: 8px");

        var (labelled, _) = Build(SliderOrientation.Horizontal, new SliderMark { Value = 50, Text = "Half" });
        labelled.RootStyle.ShouldNotContain("--shiny-gs-label-band: 8px");
    }


    [Fact]
    public void ABubbleIsAPaintedLabelRatherThanSomethingOnTheTrack()
    {
        // Snapping parks the thumb on a mark by definition, so anything drawn on the track at a mark's
        // value spends its life underneath the thumb. Only the dot goes on the track.
        var (slider, _) = Build(SliderOrientation.Horizontal,
            new SliderMark { Value = 50, Text = "Pro", Color = "#663399", Shape = SliderMarkShape.Bubble });
        var mark = slider.VisibleMarks.First();

        slider.MarkLabelStyle(mark, SliderMarkShape.Bubble).ShouldContain("background: #663399");
        slider.RootStyle.ShouldNotContain("--shiny-gs-label-band: 8px");
    }


    [Fact]
    public void TheEndLabelsAreSlidInsideTheTrackInsteadOfHangingOffIt()
    {
        var (slider, _) = Build(SliderOrientation.Horizontal,
            new SliderMark { Value = 0, Text = "Min" },
            new SliderMark { Value = 100, Text = "Max" });

        var marks = slider.VisibleMarks.ToList();
        slider.MarkLabelStyle(marks[0], SliderMarkShape.Dot).ShouldContain("translateX(0%)");
        slider.MarkLabelStyle(marks[1], SliderMarkShape.Dot).ShouldContain("translateX(-100%)");
    }


    [Fact]
    public void EveryNumberInAStyleIsWrittenInvariantly()
    {
        // A comma decimal separator is a silently invalid CSS declaration, and the browser drops the
        // whole rule — so the slider simply renders in the wrong place under a European culture.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var (slider, _) = Build();
            slider.TrackHeight = 8.5;
            slider.Value = 12.5;

            slider.TrackStyle.ShouldContain("height: 8.5px");
            slider.ThumbStyle.ShouldStartWith("left: 12.5%;");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
