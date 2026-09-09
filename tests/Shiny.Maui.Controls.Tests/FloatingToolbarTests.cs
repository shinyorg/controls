using Microsoft.Maui.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// <see cref="FloatingToolbar"/> and the strip it draws. There is no renderer here, so anchoring and
/// the overlay are out of scope; what is in scope is the item model, the overflow split, and the
/// targeting — including the shared-bar case, where getting "which control am I acting on" wrong is
/// silent and ends up deleting the wrong row.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class FloatingToolbarTests
{
    public FloatingToolbarTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }


    static ShinyToolbarItem Item(string text) => new() { Text = text };

    /// <summary>The strip puts the handler on Command precisely so a test can raise it.</summary>
    static void Press(View itemView)
        => itemView.GestureRecognizers.OfType<TapGestureRecognizer>().Single().Command!.Execute(null);

    static IReadOnlyList<View> Cells(FloatingToolbarStrip strip)
        => ((StackLayout)strip.Content!).Children.Cast<View>().ToList();


    [Fact]
    public void StripLaysOutAlongTheOrientation()
    {
        var strip = new FloatingToolbarStrip { Orientation = ToolbarOrientation.Vertical };
        strip.Build([Item("A"), Item("B")], 0, null);

        ((StackLayout)strip.Content!).Orientation.ShouldBe(StackOrientation.Vertical);
        Cells(strip).Count.ShouldBe(2);

        strip.Orientation = ToolbarOrientation.Horizontal;
        ((StackLayout)strip.Content!).Orientation.ShouldBe(StackOrientation.Horizontal);
    }


    [Fact]
    public void HiddenItemsAreNotDrawn()
    {
        var strip = new FloatingToolbarStrip();
        strip.Build([Item("A"), new ShinyToolbarItem { Text = "B", IsVisible = false }, Item("C")], 0, null);

        strip.Rendered.Select(i => i.Text).ShouldBe(["A", "C"]);
    }


    [Fact]
    public void TheOverflowCapCountsTheOverflowButton()
    {
        var strip = new FloatingToolbarStrip();
        var overflow = new ShinyToolbarItem { Text = "More" };

        strip.Build([Item("A"), Item("B"), Item("C"), Item("D"), Item("E")], 3, overflow);

        // Three visible means two real items and the "⋯" — putting three real ones plus the overflow
        // cell on the bar would take it straight back over the width that caused the overflow.
        strip.Rendered.Select(i => i.Text).ShouldBe(["A", "B", "More"]);
        overflow.Children.Select(i => i.Text).ShouldBe(["C", "D", "E"]);
    }


    [Fact]
    public void NothingOverflowsWhenItAllFits()
    {
        var strip = new FloatingToolbarStrip();
        var overflow = new ShinyToolbarItem { Text = "More" };

        strip.Build([Item("A"), Item("B")], 5, overflow);

        strip.Rendered.Select(i => i.Text).ShouldBe(["A", "B"]);
        overflow.Children.ShouldBeEmpty();
    }


    [Fact]
    public void AnItemWithChildrenAsksForAMenuRatherThanActing()
    {
        var strip = new FloatingToolbarStrip();
        var parent = Item("Format");
        parent.Children.Add(Item("Bold"));

        var invoked = 0;
        ShinyToolbarItem? menuFor = null;
        strip.ItemInvoked += (_, _) => invoked++;
        strip.MenuRequested += (_, e) => menuFor = e.Item;

        strip.Build([parent], 0, null);
        Press(Cells(strip)[0]);

        invoked.ShouldBe(0);
        menuFor.ShouldBe(parent);
    }


    [Fact]
    public void ALeafInvokesRatherThanOpeningAMenu()
    {
        var strip = new FloatingToolbarStrip();
        ShinyToolbarItem? clicked = null;
        strip.ItemInvoked += (_, e) => clicked = e.Item;

        var item = Item("Copy");
        strip.Build([item], 0, null);
        Press(Cells(strip)[0]);

        clicked.ShouldBe(item);
    }


    [Fact]
    public void ADisabledItemTakesNoGesture()
    {
        var strip = new FloatingToolbarStrip();
        strip.Build([new ShinyToolbarItem { Text = "Nope", IsEnabled = false }], 0, null);

        Cells(strip)[0].GestureRecognizers.ShouldBeEmpty();
    }


    [Fact]
    public void AttachToRegistersAndUnregistersTheView()
    {
        var bar = new FloatingToolbar();
        var row = new Border();

        FloatingToolbar.SetAttachTo(row, bar);
        FloatingToolbar.GetAttachTo(row).ShouldBe(bar);

        // Setting it to null has to take the trigger back off, or a recycled cell keeps opening a
        // bar for a row it no longer represents.
        FloatingToolbar.SetAttachTo(row, null);
        FloatingToolbar.GetAttachTo(row).ShouldBeNull();
    }


    [Fact]
    public void ShowForRecordsWhichTargetTheBarIsActingOn()
    {
        var bar = new FloatingToolbar();
        var row = new Border { BindingContext = "row-42" };

        bar.ShowFor(row);

        bar.CurrentTarget.ShouldBe(row);
        bar.IsOpen.ShouldBeTrue();
    }


    [Fact]
    public void ItemsIsUsableWithoutBeingAssigned()
    {
        // A defaultValueCreator would never fire propertyChanged, so the collection is built in the
        // constructor - which also means it is there to add to straight away.
        var bar = new FloatingToolbar();

        bar.Items.ShouldNotBeNull();
        bar.Items.Add(Item("A"));
        bar.Items.Count.ShouldBe(1);
    }


    [Fact]
    public void DefaultsAreTheUnsurprisingOnes()
    {
        var bar = new FloatingToolbar();

        bar.Orientation.ShouldBe(ToolbarOrientation.Horizontal);
        bar.Trigger.ShouldBe(TooltipTrigger.Tap);
        bar.OverflowEnabled.ShouldBeTrue();
        bar.DismissOnItemClick.ShouldBeTrue();
        bar.DismissOnTapOutside.ShouldBeTrue();

        // The grace period is what makes a hover toolbar reachable at all, so it is on by default.
        bar.HideDelay.ShouldBeGreaterThan(0);

        // The same enum the tooltip animates with, so the two read as one family.
        bar.Animation.ShouldBe(TooltipAnimation.Scale);
        bar.AnimationLength.ShouldBeGreaterThan(0u);
    }


    [Fact]
    public void AnimationCanBeTurnedOffTwoWays()
    {
        // Both matter: None keeps a duration but skips the motion, and a zero length snaps whatever
        // the animation is set to - which is also the only path open to a headless view, since MAUI
        // resolves an IAnimationManager off a handler and throws without one.
        new FloatingToolbar { Animation = TooltipAnimation.None }.Animation.ShouldBe(TooltipAnimation.None);
        new FloatingToolbar { AnimationLength = 0 }.AnimationLength.ShouldBe(0u);
    }
}
