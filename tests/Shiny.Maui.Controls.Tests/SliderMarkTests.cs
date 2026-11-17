using Microsoft.Maui.Controls;
using Microsoft.Maui.Layouts;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// Stop points and the vertical orientation. Nothing here goes through a real layout pass — headless
/// MAUI never arranges anything — so the slider is told what size it was given and the placement it
/// computed is read back off the absolute layout.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class SliderMarkTests
{
    public SliderMarkTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }


    static Slider Build(SliderOrientation orientation = SliderOrientation.Horizontal, params SliderMark[] marks)
    {
        var slider = new Slider
        {
            Orientation = orientation,
            Minimum = 0,
            Maximum = 100,
            ThumbSize = 20
        };

        foreach (var mark in marks)
            slider.Marks.Add(mark);

        slider.SetLayoutSize(220, 220);
        return slider;
    }


    // ---------------------------------------------------------------------------------------------
    // Orientation
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void HorizontalRunsLeftToRight()
    {
        var slider = Build();

        slider.CenterFor(0).ShouldBeLessThan(slider.CenterFor(1));
    }


    [Fact]
    public void VerticalRunsBottomToTop()
    {
        var slider = Build(SliderOrientation.Vertical);

        // The minimum sits at the largest coordinate, which is the bottom of the box.
        slider.CenterFor(0).ShouldBeGreaterThan(slider.CenterFor(1));
    }


    [Theory]
    [InlineData(SliderOrientation.Horizontal)]
    [InlineData(SliderOrientation.Vertical)]
    public void APositionRoundTripsBackToItsFraction(SliderOrientation orientation)
    {
        var slider = Build(orientation);

        foreach (var percent in new[] { 0d, 0.25, 0.5, 0.75, 1d })
            slider.PercentForCenter(slider.CenterFor(percent)).ShouldBe(percent, 0.0001);
    }


    [Fact]
    public void TheThumbTravelsTheWholeTrackWithoutLeavingIt()
    {
        var slider = Build();

        slider.Value = slider.Minimum;
        slider.SetLayoutSize(220, 220);
        slider.ThumbBounds.X.ShouldBe(0, 0.0001);

        slider.Value = slider.Maximum;
        slider.ThumbBounds.Right.ShouldBe(220, 0.0001);
    }


    [Fact]
    public void TheVerticalThumbEndsAtTheTopOnMaximum()
    {
        var slider = Build(SliderOrientation.Vertical);

        slider.Value = slider.Maximum;
        slider.ThumbBounds.Y.ShouldBe(0, 0.0001);

        slider.Value = slider.Minimum;
        slider.ThumbBounds.Bottom.ShouldBe(220, 0.0001);
    }


    // ---------------------------------------------------------------------------------------------
    // Snapping
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheValueComesToRestOnTheNearestMark()
    {
        var slider = Build(SliderOrientation.Horizontal,
            new SliderMark { Value = 0 },
            new SliderMark { Value = 40 },
            new SliderMark { Value = 90 });

        slider.SetValueFromPercent(0.55);

        slider.Value.ShouldBe(40);
    }


    [Fact]
    public void SnappingOverridesTheStep()
    {
        // Step would land on 33; the mark is what the thumb must come to rest on.
        var slider = Build(SliderOrientation.Horizontal, new SliderMark { Value = 37.5 });
        slider.Step = 1;

        slider.SetValueFromPercent(0.33);

        slider.Value.ShouldBe(37.5);
    }


    [Fact]
    public void MarksAreJustReferencePointsWhenSnappingIsOff()
    {
        var slider = Build(SliderOrientation.Horizontal, new SliderMark { Value = 90 });
        slider.SnapToMarks = false;
        slider.Step = 1;

        slider.SetValueFromPercent(0.33);

        slider.Value.ShouldBe(33);
    }


    [Fact]
    public void AHiddenMarkIsNotASnapTarget()
    {
        var slider = Build(SliderOrientation.Horizontal,
            new SliderMark { Value = 10, IsVisible = false },
            new SliderMark { Value = 80 });

        slider.SetValueFromPercent(0.15);

        slider.Value.ShouldBe(80);
    }


    [Fact]
    public void SnappingDoesNothingUntilThereAreMarks()
    {
        var slider = Build();
        slider.Step = 5;

        slider.SetValueFromPercent(0.33);

        slider.Value.ShouldBe(35);
    }


    // ---------------------------------------------------------------------------------------------
    // Drawing
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AMarkIsDrawnAsSoonAsItIsAddedRatherThanAtTheFirstLayout()
    {
        // net11.0-macos never realizes a child added after the page has laid out, so the views have to
        // exist by the time XAML has finished populating the collection.
        var slider = new Slider();
        slider.Marks.Add(new SliderMark { Value = 50, Text = "Half" });

        slider.DrawnMarks.Count.ShouldBe(1);
    }


    [Fact]
    public void AHiddenMarkDrawsNothing()
    {
        var slider = Build(SliderOrientation.Horizontal,
            new SliderMark { Value = 10, IsVisible = false },
            new SliderMark { Value = 80 });

        slider.DrawnMarks.Count.ShouldBe(1);
        slider.DrawnMarks[0].Mark.Value.ShouldBe(80);
    }


    [Fact]
    public void HidingAMarkAfterTheFactRemovesIt()
    {
        var mark = new SliderMark { Value = 10 };
        var slider = Build(SliderOrientation.Horizontal, mark);
        slider.DrawnMarks.Count.ShouldBe(1);

        mark.IsVisible = false;

        slider.DrawnMarks.ShouldBeEmpty();
    }


    [Fact]
    public void AMarkSitsWhereTheThumbSitsForTheSameValue()
    {
        var slider = Build(SliderOrientation.Horizontal, new SliderMark { Value = 70 });
        slider.Value = 70;
        slider.SetLayoutSize(220, 220);

        var thumb = slider.ThumbBounds;
        var marker = AbsoluteLayout.GetLayoutBounds(slider.DrawnMarks[0].Marker);

        marker.Center.X.ShouldBe(thumb.Center.X, 0.0001);
    }


    [Fact]
    public void MarksAreOrderedUpTheTrackWhenVertical()
    {
        var slider = Build(SliderOrientation.Vertical,
            new SliderMark { Value = 0 },
            new SliderMark { Value = 100 });

        var low = AbsoluteLayout.GetLayoutBounds(slider.DrawnMarks[0].Marker);
        var high = AbsoluteLayout.GetLayoutBounds(slider.DrawnMarks[1].Marker);

        high.Center.Y.ShouldBeLessThan(low.Center.Y);
    }


    [Fact]
    public void ADotCarriesItsTextAsAPlainCaption()
    {
        var slider = Build(SliderOrientation.Horizontal, new SliderMark { Value = 50, Text = "Half" });

        var caption = slider.DrawnMarks[0].Caption.ShouldBeOfType<Label>();
        caption.Text.ShouldBe("Half");
    }


    [Fact]
    public void ABubbleIsTheSameCaptionInAPill()
    {
        var slider = Build(SliderOrientation.Horizontal,
            new SliderMark { Value = 50, Text = "Pro", Shape = SliderMarkShape.Bubble });

        var pill = slider.DrawnMarks[0].Caption.ShouldBeOfType<Border>();
        ((Label)pill.Content!).Text.ShouldBe("Pro");
    }


    [Fact]
    public void ABubbleStaysOffTheTrackSoTheThumbCannotCoverIt()
    {
        // Snapping parks the thumb on a mark by definition, so anything drawn on the track at a mark's
        // value spends its life underneath the thumb. Only the dot goes on the track.
        var slider = Build(SliderOrientation.Horizontal,
            new SliderMark { Value = 50, Text = "Pro", Shape = SliderMarkShape.Bubble });
        slider.Value = 50;
        slider.SetLayoutSize(220, 220);

        var thumb = slider.ThumbBounds;
        var pill = AbsoluteLayout.GetLayoutBounds(slider.DrawnMarks[0].Caption!);

        pill.Y.ShouldBeGreaterThanOrEqualTo(thumb.Bottom);
    }


    [Fact]
    public void CaptionsCanBeTurnedOffWithoutLosingTheMark()
    {
        var slider = Build(SliderOrientation.Horizontal, new SliderMark { Value = 50, Text = "Half" });
        slider.ShowMarkLabels = false;

        slider.DrawnMarks.Count.ShouldBe(1);
        slider.DrawnMarks[0].Caption.ShouldBeNull();
    }


    [Fact]
    public void AMarkPaintsItsOwnColourOverTheSliderDefault()
    {
        var slider = Build(SliderOrientation.Horizontal,
            new SliderMark { Value = 50, Color = Colors.Purple });
        slider.MarkColor = Colors.Gray;

        ((Border)slider.DrawnMarks[0].Marker).BackgroundColor.ShouldBe(Colors.Purple);
    }


    [Fact]
    public void TheThumbIsDrawnOverTheMarks()
    {
        // Appending the marks would paint a tick straight through the thumb, and snapping parks the
        // thumb on a mark by definition.
        var slider = Build(SliderOrientation.Horizontal,
            new SliderMark { Value = 50, Text = "Half", Shape = SliderMarkShape.Line });

        slider.IndexOf(slider.DrawnMarks[0].Marker)
            .ShouldBeLessThan(slider.IndexOf(slider.ThumbView));
    }


    [Fact]
    public void MarksDoNotSwallowTheDrag()
    {
        // Everything drawn on the track is decoration: the pan and tap recognizers live on the layout,
        // so a mark that is hit-testable silently kills the drag that starts on top of it.
        var slider = Build(SliderOrientation.Horizontal, new SliderMark { Value = 50, Text = "Half" });

        var visual = slider.DrawnMarks[0];
        visual.Marker.InputTransparent.ShouldBeTrue();
        visual.Caption!.InputTransparent.ShouldBeTrue();
    }


    // ---------------------------------------------------------------------------------------------
    // MarksSource
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ABoundSourceIsDrawnAlongsideTheDeclaredMarks()
    {
        var slider = Build(SliderOrientation.Horizontal, new SliderMark { Value = 0, Text = "Start" });

        slider.MarksSource = new[] { new SliderMark { Value = 50, Text = "Half" }, new SliderMark { Value = 100 } };

        slider.DrawnMarks.Select(m => m.Mark.Value).ShouldBe(new[] { 0d, 50, 100 });
    }


    [Fact]
    public void NumbersInTheSourceBecomeUnlabelledMarks()
    {
        var slider = Build();

        slider.MarksSource = new List<int> { 25, 75 };

        slider.DrawnMarks.Select(m => m.Mark.Value).ShouldBe(new[] { 25d, 75 });
        slider.DrawnMarks.ShouldAllBe(m => m.Caption == null);
    }


    [Fact]
    public void SourceMarksAreSnapTargets()
    {
        var slider = Build();
        slider.MarksSource = new[] { 20d, 70d };

        slider.SetValueFromPercent(0.6);

        slider.Value.ShouldBe(70);
    }


    [Fact]
    public void AnObservableSourceIsFollowedAsItChanges()
    {
        var source = new System.Collections.ObjectModel.ObservableCollection<SliderMark> { new() { Value = 10 } };
        var slider = Build();
        slider.MarksSource = source;

        source.Add(new SliderMark { Value = 60 });
        slider.DrawnMarks.Count.ShouldBe(2);

        source.RemoveAt(0);
        slider.DrawnMarks.Select(m => m.Mark.Value).ShouldBe(new[] { 60d });

        source.Clear();
        slider.DrawnMarks.ShouldBeEmpty();
    }


    [Fact]
    public void EditingASourceMarkRedrawsIt()
    {
        var mark = new SliderMark { Value = 10 };
        var slider = Build();
        slider.MarksSource = new[] { mark };

        mark.IsVisible = false;

        slider.DrawnMarks.ShouldBeEmpty();
    }


    [Fact]
    public void ReplacingTheSourceLetsGoOfTheOldOne()
    {
        var old = new System.Collections.ObjectModel.ObservableCollection<SliderMark> { new() { Value = 10 } };
        var oldMark = old[0];
        var slider = Build();
        slider.MarksSource = old;

        slider.MarksSource = new[] { 90d };
        old.Add(new SliderMark { Value = 40 });
        oldMark.Value = 20;

        slider.DrawnMarks.Select(m => m.Mark.Value).ShouldBe(new[] { 90d });
    }


    [Fact]
    public void ClearingTheSourceLeavesTheDeclaredMarks()
    {
        var slider = Build(SliderOrientation.Horizontal, new SliderMark { Value = 30 });
        slider.MarksSource = new[] { 60d };

        slider.MarksSource = null;

        slider.DrawnMarks.Select(m => m.Mark.Value).ShouldBe(new[] { 30d });
    }
}
