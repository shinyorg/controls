using Shiny.Maui.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The rule deciding how far along a timeline is.
/// </summary>
/// <remarks>
/// Asserted against the shared <see cref="TimelineNode.StateFor"/> rather than against rendered
/// views: the states are what the rail's colours and the marker's ring are read from, and they are
/// the half of this control that can be wrong without anything throwing. The layout — a rail that
/// stretches to its row — is a thing to look at, not a thing to assert.
/// </remarks>
public class TimelineTests
{
    [Theory]
    [InlineData(0, TimelineNodeState.Complete)]
    [InlineData(1, TimelineNodeState.Complete)]
    [InlineData(2, TimelineNodeState.Current)]
    [InlineData(3, TimelineNodeState.Pending)]
    [InlineData(4, TimelineNodeState.Pending)]
    public void NodesBeforeTheActiveOneAreCompleteAndAfterItArePending(int index, TimelineNodeState expected)
        => TimelineNode.StateFor(index, activeIndex: 2, allActive: false).ShouldBe(expected);

    [Fact]
    public void TheDefaultActiveIndexLeavesEverythingPending()
    {
        // -1 rather than 0, so a timeline handed no position does not silently claim the first entry
        // has happened.
        foreach (var index in Enumerable.Range(0, 5))
            TimelineNode.StateFor(index, activeIndex: -1, allActive: false).ShouldBe(TimelineNodeState.Pending);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2)]
    public void AllActiveWinsWhateverTheIndexSays(int activeIndex)
    {
        // Switching it on must not need the index moved to the end, nor moved back to switch it off.
        foreach (var index in Enumerable.Range(0, 5))
            TimelineNode.StateFor(index, activeIndex, allActive: true).ShouldBe(TimelineNodeState.Complete);
    }

    [Fact]
    public void EverythingAtOrBehindTheActivePositionCountsAsActive()
    {
        // IsActive is what the rail's filled segments and the marker's fill both key off, so the two
        // cannot drift apart.
        Node(1, 2).IsActive.ShouldBeTrue();
        Node(2, 2).IsActive.ShouldBeTrue();
        Node(3, 2).IsActive.ShouldBeFalse();

        static TimelineNode Node(int index, int active)
            => new(new object(), index, TimelineNode.StateFor(index, active, false), index == 0, false);
    }

    [Fact]
    public void TheControlDefaultsToNothingActiveAndALeftHandRail()
    {
        var timeline = new TimelineView();

        timeline.ActiveIndex.ShouldBe(-1);
        timeline.AllActive.ShouldBeFalse();
        timeline.RailPosition.ShouldBe(TimelineRailPosition.Left);

        // Scrolls itself unless told otherwise: a timeline is normally taller than what holds it.
        timeline.IsScrollable.ShouldBeTrue();
    }

    [Fact]
    public void ItemsSourceIsRealisedIntoOneRowPerItem()
    {
        var timeline = new TimelineView { ItemsSource = new[] { "one", "two", "three" } };

        Rows(timeline).ShouldBe(3);
    }

    [Fact]
    public void AnObservableCollectionRebuildsTheRows()
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<string> { "one", "two" };
        var timeline = new TimelineView { ItemsSource = items };

        Rows(timeline).ShouldBe(2);

        items.Add("three");
        Rows(timeline).ShouldBe(3);

        items.RemoveAt(0);
        Rows(timeline).ShouldBe(2);
    }

    /// <summary>The rows the control actually built, reached through its own visual tree.</summary>
    static int Rows(TimelineView timeline)
    {
        var scroller = (ScrollView)timeline.Content!;
        return ((VerticalStackLayout)scroller.Content!).Count;
    }
}
