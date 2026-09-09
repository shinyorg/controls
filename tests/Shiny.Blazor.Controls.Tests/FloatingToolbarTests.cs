using Shiny.Blazor.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// <see cref="FloatingToolbar"/>'s .NET half. Placement and the triggers live in JS and there is no
/// browser here, so what these cover is the overflow split — where getting the cap's meaning wrong
/// silently hides actions behind a button that itself did not fit.
/// </summary>
public class FloatingToolbarTests
{
    static ToolbarItem Item(string text) => new() { Text = text };

    /// <summary>The measured fit is JS's job; here it is set directly to drive the split.</summary>
    static FloatingToolbar WithFit(FloatingToolbar bar, int fits)
    {
        typeof(FloatingToolbar)
            .GetField("fitCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(bar, fits);

        return bar;
    }

    static IReadOnlyList<string?> Visible(FloatingToolbar bar)
    {
        var value = typeof(FloatingToolbar)
            .GetProperty("VisibleItems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(bar);

        return value is IEnumerable<ToolbarItem> items
            ? items.Select(i => i.Text).ToList()
            : [];
    }

    static bool HasOverflow(FloatingToolbar bar)
        => (bool)typeof(FloatingToolbar)
            .GetProperty("HasOverflow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(bar)!;

    static ToolbarItem Overflow(FloatingToolbar bar)
        => (ToolbarItem)typeof(FloatingToolbar)
            .GetProperty("OverflowItem", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(bar)!;


    [Fact]
    public void DefaultsAreTheUnsurprisingOnes()
    {
        var bar = new FloatingToolbar();

        bar.Orientation.ShouldBe(ToolbarOrientation.Horizontal);
        bar.Trigger.ShouldBe(TooltipTrigger.Click);
        bar.Placement.ShouldBe(TooltipPlacement.Top);
        bar.OverflowEnabled.ShouldBeTrue();
        bar.DismissOnItemClick.ShouldBeTrue();

        // The grace period is what lets the pointer cross from the target onto the bar, so a hover
        // toolbar is reachable at all. Zero here would make hover mode useless.
        bar.HideDelay.ShouldBeGreaterThan(0);

        // The same enum the tooltip animates with, so the two read as one family.
        bar.Animation.ShouldBe(TooltipAnimation.Scale);
        bar.AnimationLength.ShouldBeGreaterThan(0);
    }


    [Fact]
    public void EverythingIsDrawnBeforeAnythingHasBeenMeasured()
    {
        // fitCount is zero until JS reports back, and the measure pass needs the cells rendered in
        // order to have something to measure.
        var bar = new FloatingToolbar { Items = [Item("A"), Item("B"), Item("C")] };

        HasOverflow(bar).ShouldBeFalse();
        Visible(bar).ShouldBe(["A", "B", "C"]);
    }


    [Fact]
    public void TheCapCountsTheOverflowButton()
    {
        var bar = WithFit(new FloatingToolbar { Items = [Item("A"), Item("B"), Item("C"), Item("D")] }, 3);

        HasOverflow(bar).ShouldBeTrue();
        // Two real items and the "⋯" — three real ones plus the overflow cell would put the bar
        // straight back over the width that caused the overflow.
        Visible(bar).ShouldBe(["A", "B"]);
        Overflow(bar).Children!.Select(i => i.Text).ShouldBe(["C", "D"]);
    }


    [Fact]
    public void AnExplicitCapBeatsTheMeasuredOne()
    {
        var bar = WithFit(
            new FloatingToolbar { Items = [Item("A"), Item("B"), Item("C"), Item("D")], MaxVisibleItems = 2 },
            99
        );

        HasOverflow(bar).ShouldBeTrue();
        Visible(bar).ShouldBe(["A"]);
    }


    [Fact]
    public void NothingOverflowsWhenItAllFits()
    {
        var bar = WithFit(new FloatingToolbar { Items = [Item("A"), Item("B")] }, 5);

        HasOverflow(bar).ShouldBeFalse();
        Visible(bar).ShouldBe(["A", "B"]);
    }


    [Fact]
    public void OverflowCanBeTurnedOffEntirely()
    {
        var bar = WithFit(
            new FloatingToolbar { Items = [Item("A"), Item("B"), Item("C")], OverflowEnabled = false },
            1
        );

        HasOverflow(bar).ShouldBeFalse();
        Visible(bar).ShouldBe(["A", "B", "C"]);
    }


    [Fact]
    public void ASeparatorCountsTowardsTheSplit()
    {
        // The measure counts every cell including separators, so the item count it is compared
        // against has to include them too - or a bar with one separator always looks overflowing.
        var bar = WithFit(
            new FloatingToolbar { Items = [Item("A"), new ToolbarItem { IsSeparator = true }, Item("B")] },
            3
        );

        HasOverflow(bar).ShouldBeFalse();
    }
}
