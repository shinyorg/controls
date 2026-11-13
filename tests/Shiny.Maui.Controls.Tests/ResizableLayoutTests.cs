using Microsoft.Maui.Controls;
using Shiny.Controls.Resizable;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>The resize arithmetic both hosts share, and the MAUI group that drives it.</summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ResizableLayoutTests
{
    public ResizableLayoutTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }

    static ResizablePanelLimits Free => new();

    static void ShouldSumTo100(double[] sizes) => sizes.Sum().ShouldBe(100, 0.001);


    // ---------------------------------------------------------------------------------------------
    // Initial / Validate
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void PanelsWithoutADefaultShareWhatIsLeft()
    {
        var sizes = ResizableLayout.Initial([25, null, null], [Free, Free, Free]);

        sizes.ShouldBe([25, 37.5, 37.5], tolerance: 0.001);
    }

    [Fact]
    public void InitialSizesAreHeldToTheirLimits()
    {
        var sizes = ResizableLayout.Initial([5, null], [new(MinSize: 20), Free]);

        sizes[0].ShouldBe(20, 0.001);
        ShouldSumTo100(sizes);
    }

    [Fact]
    public void SavedSizesThatDoNotAddUpAreScaledBack()
    {
        var sizes = ResizableLayout.Validate([30, 30], [Free, Free]);

        sizes.ShouldBe([50, 50], tolerance: 0.001);
    }

    [Fact]
    public void AWrongCountFallsBackToEqualShares()
    {
        var sizes = ResizableLayout.Validate([50], [Free, Free, Free, Free]);

        ShouldSumTo100(sizes);
        sizes.Length.ShouldBe(4);
    }


    // ---------------------------------------------------------------------------------------------
    // Resize
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void DraggingMovesSpaceBetweenTheTwoNeighbours()
    {
        var sizes = ResizableLayout.Resize([50, 50], [Free, Free], 0, 10);

        sizes.ShouldBe([60, 40], tolerance: 0.001);
    }

    [Fact]
    public void ADragStopsAtTheShrinkingPanelsMinimum()
    {
        var sizes = ResizableLayout.Resize([50, 50], [Free, new(MinSize: 30)], 0, 40);

        sizes.ShouldBe([70, 30], tolerance: 0.001);
    }

    [Fact]
    public void ADragStopsAtTheGrowingPanelsMaximum()
    {
        var sizes = ResizableLayout.Resize([50, 50], [new(MaxSize: 55), Free], 0, 40);

        sizes.ShouldBe([55, 45], tolerance: 0.001);
    }

    [Fact]
    public void ALongDragPushesThroughToTheNextPanel()
    {
        var sizes = ResizableLayout.Resize([20, 40, 40], [Free, new(MinSize: 30), Free], 0, 30);

        sizes.ShouldBe([50, 30, 20], tolerance: 0.001);
    }

    [Fact]
    public void ANegativeDragGrowsThePanelAfterTheHandle()
    {
        var sizes = ResizableLayout.Resize([50, 50], [Free, Free], 0, -20);

        sizes.ShouldBe([30, 70], tolerance: 0.001);
    }

    [Fact]
    public void ACollapsiblePanelHoldsAtItsMinimumUntilPulledPastHalfway()
    {
        var limits = new ResizablePanelLimits[] { new(MinSize: 20, Collapsible: true), Free };

        // 10 past the minimum would be 30 of travel; 25 leaves it 5 short of halfway to closed.
        ResizableLayout.Resize([30, 70], limits, 0, -15).ShouldBe([20, 80], tolerance: 0.001);
        ResizableLayout.Resize([30, 70], limits, 0, -25).ShouldBe([0, 100], tolerance: 0.001);
    }

    [Fact]
    public void ACollapsedPanelOpensToItsMinimumOrNotAtAll()
    {
        var limits = new ResizablePanelLimits[] { new(MinSize: 20, Collapsible: true, CollapsedSize: 4), Free };

        ResizableLayout.Resize([4, 96], limits, 0, 5).ShouldBe([4, 96], tolerance: 0.001);
        ResizableLayout.Resize([4, 96], limits, 0, 9).ShouldBe([20, 80], tolerance: 0.001);
    }

    [Fact]
    public void AResizeIsComputedFromTheStartSoItCanBeUndoneByDraggingBack()
    {
        var start = new double[] { 50, 50 };
        var limits = new ResizablePanelLimits[] { Free, new(MinSize: 30) };

        ResizableLayout.Resize(start, limits, 0, 40);
        ResizableLayout.Resize(start, limits, 0, 0).ShouldBe(start);
    }


    // ---------------------------------------------------------------------------------------------
    // Collapse / Expand
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CollapseGivesTheSpaceToTheNextPanel()
    {
        var limits = new ResizablePanelLimits[] { new(MinSize: 15, Collapsible: true), Free, Free };

        var sizes = ResizableLayout.Collapse([25, 50, 25], limits, 0);

        sizes.ShouldBe([0, 75, 25], tolerance: 0.001);
    }

    [Fact]
    public void CollapsingTheLastPanelGivesItsSpaceToThePanelBefore()
    {
        var limits = new ResizablePanelLimits[] { Free, new(MinSize: 15, Collapsible: true) };

        ResizableLayout.Collapse([70, 30], limits, 1).ShouldBe([100, 0], tolerance: 0.001);
    }

    [Fact]
    public void ExpandRestoresTheRememberedSize()
    {
        var limits = new ResizablePanelLimits[] { new(MinSize: 15, Collapsible: true), Free };

        ResizableLayout.Expand([0, 100], limits, 0, 35).ShouldBe([35, 65], tolerance: 0.001);
        ResizableLayout.Expand([0, 100], limits, 0).ShouldBe([15, 85], tolerance: 0.001);
    }


    // ---------------------------------------------------------------------------------------------
    // Group
    // ---------------------------------------------------------------------------------------------

    // No handler in a unit test, so the layout manager is driven directly.
    static void Layout(ResizablePanelGroup group, double x, double y, double width, double height)
    {
        var layout = (Microsoft.Maui.ILayout)group;
        layout.CrossPlatformMeasure(width, height);
        layout.CrossPlatformArrange(new Microsoft.Maui.Graphics.Rect(x, y, width, height));
    }

    static (ResizablePanelGroup Group, ResizablePanel First, ResizableHandle Handle, ResizablePanel Second) TwoPanels()
    {
        var first = new ResizablePanel { DefaultSize = 30, MinSize = 10, Collapsible = true };
        var handle = new ResizableHandle();
        var second = new ResizablePanel();
        var group = new ResizablePanelGroup { Children = { first, handle, second } };
        return (group, first, handle, second);
    }

    [Fact]
    public void TheGroupDividesItsWidthAfterTheHandleLine()
    {
        var (group, first, handle, second) = TwoPanels();

        Layout(group, 0, 0, 401, 200);

        first.Bounds.Width.ShouldBe(120, 0.01);
        second.Bounds.X.ShouldBe(121, 0.01);
        second.Bounds.Width.ShouldBe(280, 0.01);
        // the touch target straddles the line
        handle.Bounds.Width.ShouldBe(ResizableHandle.HitSize);
        (handle.Bounds.X + handle.Bounds.Width / 2).ShouldBe(120.5, 0.01);
    }

    [Fact]
    public void ADragSettlesIntoSizesAndOneLayoutChanged()
    {
        var (group, first, handle, _) = TwoPanels();
        Layout(group, 0, 0, 401, 200);
        var raised = 0;
        group.LayoutChanged += (_, _) => raised++;

        group.BeginDrag(handle);
        group.Drag(handle, 40);
        group.Drag(handle, 80);
        raised.ShouldBe(0);
        group.EndDrag(handle);

        raised.ShouldBe(1);
        group.Sizes!.ShouldBe([50, 50], tolerance: 0.001);
        first.Size.ShouldBe(50, 0.001);
    }

    [Fact]
    public void SettingIsCollapsedCollapsesAndRaisesTheEvent()
    {
        var (group, first, _, second) = TwoPanels();
        Layout(group, 0, 0, 400, 200);
        var collapsed = false;
        first.Collapsed += (_, _) => collapsed = true;

        first.IsCollapsed = true;

        collapsed.ShouldBeTrue();
        first.Size.ShouldBe(0, 0.001);
        second.Size.ShouldBe(100, 0.001);

        first.IsCollapsed = false;
        first.Size.ShouldBe(30, 0.001);
    }

    [Fact]
    public void BoundSizesWinOverDefaults()
    {
        var (group, first, _, _) = TwoPanels();
        group.Sizes = [60, 40];

        Layout(group, 0, 0, 400, 200);

        first.Size.ShouldBe(60, 0.001);
    }

    [Fact]
    public void DraggingAPanelShutUpdatesIsCollapsed()
    {
        var (group, first, handle, _) = TwoPanels();
        Layout(group, 0, 0, 400, 200);

        group.BeginDrag(handle);
        group.Drag(handle, -110);   // 30% -> past halfway below the 10% minimum
        group.EndDrag(handle);

        first.IsCollapsed.ShouldBeTrue();
    }
}
