using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.Infrastructure;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The control's own behaviour: that a line declared in markup relocates itself onto the page edge,
/// that the escape hatch leaves it alone, and that the passthroughs actually reach the inner bar.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ProgressLineTests
{
    public ProgressLineTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }

    static ContentPage PageWith(ProgressLine line)
    {
        var stack = new VerticalStackLayout { Children = { new Label(), line } };
        return new ContentPage { Content = stack };
    }


    [Fact]
    public void ADeclaredLineMovesItselfOntoThePageEdge()
    {
        var line = new ProgressLine();
        var page = PageWith(line);

        // The test dispatcher runs dispatched work inline, so the deferred dock has already happened.
        var layer = line.Parent.ShouldBeOfType<PageOverlay.ProgressLineLayer>();
        PageOverlay.GetOrCreateRoot(page).Children.ShouldContain(v => ReferenceEquals(v, layer));
    }


    [Fact]
    public void TheLineLeavesTheLayoutItWasDeclaredIn()
    {
        var line = new ProgressLine();
        var page = PageWith(line);
        var stack = (VerticalStackLayout)PageOverlay.ContentOf(PageOverlay.GetOrCreateRoot(page))!;

        stack.Children.ShouldNotContain(line);
    }


    [Fact]
    public void DockFalseLeavesTheLineWhereItWasDeclared()
    {
        var line = new ProgressLine { Dock = false };
        var stack = new VerticalStackLayout { Children = { line } };
        _ = new ContentPage { Content = stack };

        stack.Children.ShouldContain(line);
    }


    [Fact]
    public void TopIsTheDefaultEdge()
    {
        var line = new ProgressLine();
        _ = PageWith(line);

        line.Position.ShouldBe(ProgressLinePosition.Top);
        line.VerticalOptions.ShouldBe(LayoutOptions.Start);
    }


    [Fact]
    public void BottomAlignsToTheOtherEdge()
    {
        var line = new ProgressLine { Position = ProgressLinePosition.Bottom };
        _ = PageWith(line);

        line.VerticalOptions.ShouldBe(LayoutOptions.End);
    }


    [Fact]
    public void TheOffsetLandsOnTheEdgeBeingDockedTo()
    {
        var line = new ProgressLine
        {
            Position = ProgressLinePosition.Bottom,
            Offset = new Thickness(4, 0, 8, 12)
        };
        _ = PageWith(line);

        line.Margin.ShouldBe(new Thickness(4, 0, 8, 12));
    }


    /// <summary>
    /// The line spans a whole edge of the page. Leaving it hit-testable would swallow every tap along
    /// that edge for as long as it was up.
    /// </summary>
    [Fact]
    public void TheLineNeverEatsATap()
        => new ProgressLine().InputTransparent.ShouldBeTrue();


    [Fact]
    public void TheLineHeightDrivesTheBar()
    {
        var line = new ProgressLine { LineHeight = 6 };
        _ = PageWith(line);

        line.Bar.TrackHeight.ShouldBe(6);
        line.HeightRequest.ShouldBe(6);
    }


    [Fact]
    public void TheColourReachesTheInnerBar()
    {
        var line = new ProgressLine { BarColor = Colors.Orange };
        _ = PageWith(line);

        line.Bar.BarColor.ShouldBe(Colors.Orange);
    }


    /// <summary>
    /// A rail drawn across the whole window reads as a border that has appeared for no reason, so the
    /// line starts with no track at all — the opposite of <see cref="ProgressBar"/>'s default.
    /// </summary>
    [Fact]
    public void TheTrackIsInvisibleByDefault()
        => new ProgressLine().TrackColor.ShouldBe(Colors.Transparent);


    [Fact]
    public void ValueFlowsThroughToTheBar()
    {
        var line = new ProgressLine();
        _ = PageWith(line);

        line.Value = 42;

        line.Bar.Value.ShouldBe(42);
    }


    [Fact]
    public void ChangingTheLineHeightResizesTheControlAndTheBar()
    {
        var line = new ProgressLine();
        _ = PageWith(line);

        line.LineHeight = 8;

        line.HeightRequest.ShouldBe(8);
        line.Bar.TrackHeight.ShouldBe(8);
    }


    /// <summary>
    /// Turning docking on after construction has to run the move, not just re-measure in place.
    /// </summary>
    [Fact]
    public void TurningDockOnLaterStillMovesTheLine()
    {
        var line = new ProgressLine { Dock = false };
        var stack = new VerticalStackLayout { Children = { line } };
        _ = new ContentPage { Content = stack };

        line.Dock = true;

        line.Parent.ShouldBeOfType<PageOverlay.ProgressLineLayer>();
    }


    /// <summary>
    /// The shape XAML builds for a line declared as a direct child of the page: every direct child is
    /// assigned to <c>Content</c> in turn, so the line is the content for a moment and the layout
    /// declared after it replaces it. Docking is deferred here the way it is on a device - the shared
    /// test dispatcher runs dispatched work inline, which is exactly what hid this: by the time a real
    /// deferred dock ran, the line had no parent and never entered the tree.
    /// </summary>
    [Fact]
    public void ALineDeclaredBeforeThePageContentDocksWhenDockingIsDeferred()
    {
        var queue = new QueuedDispatcherProvider();
        DispatcherProvider.SetCurrent(queue);
        try
        {
            var line = new ProgressLine();
            var scroll = new ScrollView();
            var page = new ContentPage { Content = line };
            page.Content = scroll;

            queue.Dispatcher.RunAll();

            AssertDockedOn(line, page, scroll);
        }
        finally
        {
            TestDispatcherProvider.Install();
        }
    }


    [Fact]
    public void ADockedLineFollowsThePageWhenItsContentIsReplaced()
    {
        var line = new ProgressLine();
        var page = PageWith(line);
        var replacement = new Grid();

        page.Content = replacement;

        AssertDockedOn(line, page, replacement);
    }


    [Fact]
    public void ALineRemovedOnPurposeStaysRemoved()
    {
        var line = new ProgressLine();
        var page = PageWith(line);
        ((Layout)line.Parent).Children.Remove(line);

        page.Content = new Grid();

        line.Parent.ShouldBeNull();
    }


    static void AssertDockedOn(ProgressLine line, ContentPage page, View content)
    {
        var root = page.Content.ShouldBeOfType<PageOverlay.ShinyOverlayRoot>();
        PageOverlay.ContentOf(root).ShouldBeSameAs(content);
        line.Parent.ShouldBeOfType<PageOverlay.ProgressLineLayer>().Parent.ShouldBeSameAs(root);
    }


    sealed class QueuedDispatcherProvider : IDispatcherProvider
    {
        public QueuedDispatcher Dispatcher { get; } = new();

        public IDispatcher? GetForCurrentThread() => this.Dispatcher;
    }


    sealed class QueuedDispatcher : IDispatcher
    {
        readonly Queue<Action> queue = new();

        public bool IsDispatchRequired => false;

        public bool Dispatch(Action action)
        {
            this.queue.Enqueue(action);
            return true;
        }

        public bool DispatchDelayed(TimeSpan delay, Action action) => this.Dispatch(action);

        public IDispatcherTimer CreateTimer() => throw new NotSupportedException();

        public void RunAll()
        {
            while (this.queue.TryDequeue(out var action))
                action();
        }
    }


    [Fact]
    public void TheGradientFallsBackToTheThemeRatherThanAPinnedBlue()
    {
        var line = new ProgressLine();

        line.GradientStartColor.ShouldBeNull();
        line.GradientEndColor.ShouldBeNull();
    }
}
