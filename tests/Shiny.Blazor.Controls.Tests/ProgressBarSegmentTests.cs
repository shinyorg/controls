using Shiny.Blazor.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>How much of each step the segmented bar lights, decided in the component.</summary>
public class ProgressBarSegmentTests
{
    [Fact]
    public void AStepValueLightsWholeSegments()
    {
        var bar = new ProgressBar { Segments = 11, Maximum = 11, Value = 8 };

        Enumerable.Range(0, 11).Select(bar.SegmentFill)
            .ShouldBe([1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0]);
    }


    [Fact]
    public void AValueBetweenStepsLightsTheCurrentStepPartially()
    {
        var bar = new ProgressBar { Segments = 4, Value = 60 };

        bar.SegmentFill(1).ShouldBe(1);
        bar.SegmentFill(2).ShouldBe(0.4, 0.0001);
        bar.SegmentFill(3).ShouldBe(0);
    }


    [Fact]
    public void ValuesOutsideTheRangeClamp()
    {
        new ProgressBar { Segments = 4, Value = 250 }.SegmentFill(3).ShouldBe(1);
        new ProgressBar { Segments = 4, Value = -10 }.SegmentFill(0).ShouldBe(0);
    }
}
