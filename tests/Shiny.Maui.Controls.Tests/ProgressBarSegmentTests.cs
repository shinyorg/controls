using Microsoft.Maui.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The segmented (stepped) bar. One continuous fill width is spread across the steps, so these
/// assert how much of each step is lit rather than any pixel width.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ProgressBarSegmentTests
{
    public ProgressBarSegmentTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }

    sealed class TestProgressBar : ProgressBar
    {
        public void LayoutAt(double width) => this.OnSizeAllocated(width, 8);
    }

    static TestProgressBar Bar(int segments, double maximum = 100)
    {
        var bar = new TestProgressBar { AnimateProgress = false, Segments = segments, Maximum = maximum };
        bar.LayoutAt(200);
        return bar;
    }


    [Fact]
    public void TheBarIsContinuousByDefault()
        => new TestProgressBar().SegmentFills.ShouldBeEmpty();


    [Fact]
    public void OneSegmentIsStillTheContinuousBar()
        => Bar(1).SegmentFills.ShouldBeEmpty();


    [Fact]
    public void AStepValueLightsWholeSegments()
    {
        var bar = Bar(11, maximum: 11);

        bar.Value = 8;

        bar.SegmentFills.ShouldBe([1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0]);
    }


    [Fact]
    public void AValueBetweenStepsLightsTheCurrentStepPartially()
    {
        var bar = Bar(4);

        bar.Value = 60;

        bar.SegmentFills[0].ShouldBe(1);
        bar.SegmentFills[1].ShouldBe(1);
        bar.SegmentFills[2].ShouldBe(0.4, 0.0001);
        bar.SegmentFills[3].ShouldBe(0);
    }


    [Fact]
    public void ChangingTheSegmentCountRedistributesTheFill()
    {
        var bar = Bar(4);
        bar.Value = 50;

        bar.Segments = 2;

        bar.SegmentFills.ShouldBe([1, 0]);
    }


    [Fact]
    public void TurningSegmentsOffRestoresTheContinuousBar()
    {
        var bar = Bar(4);
        bar.Value = 50;

        bar.Segments = 0;

        bar.SegmentFills.ShouldBeEmpty();
        bar.CurrentFillWidth.ShouldBe(100);
    }
}
