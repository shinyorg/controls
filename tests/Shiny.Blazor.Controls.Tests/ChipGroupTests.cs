using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// Everything the group decides — what a click does to the selection, what the cap refuses, what a
/// removal takes out of the source — is a method on the component rather than logic inside an event
/// handler, which is what lets it be driven here without a renderer.
/// </summary>
public class ChipGroupTests
{
    record Category(string Name, int Id);


    static ChipGroup<string> Group(params string[] items)
        => new() { ItemsSource = new List<string>(items) };


    /// <summary>
    /// Adopts the selection parameters the way a lifecycle pass does. Setting them through
    /// <c>SetParametersAsync</c> is not an option: that ends in a <c>StateHasChanged</c>, and a
    /// component with no render handle throws on one.
    /// </summary>
    static T Bound<T>(T group) where T : notnull
    {
        ((dynamic)group).TakeSelectionParameters();
        return group;
    }


    // ---------------------------------------------------------------------------------------------
    // Items
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void EachItemIsAnItem()
    {
        var group = Group("design", "engineering", "sales");

        group.Items.ShouldBe(["design", "engineering", "sales"]);
    }


    [Fact]
    public void ADisplaySelectorNamesWhatTheChipShows()
    {
        var group = new ChipGroup<Category>
        {
            ItemsSource = [new("Design", 1), new("Engineering", 2)],
            DisplaySelector = x => x.Name
        };

        group.Items.Select(group.DisplayFor).ShouldBe(["Design", "Engineering"]);
    }


    // ---------------------------------------------------------------------------------------------
    // Single selection
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task AClickSelectsTheChip()
    {
        var group = Group("design", "engineering");

        await group.TapAsync("engineering");

        group.SelectedItem.ShouldBe("engineering");
        group.IsSelected("engineering").ShouldBeTrue();
        group.IsSelected("design").ShouldBeFalse();
    }


    [Fact]
    public async Task ASecondChipReplacesTheFirst()
    {
        var group = Group("design", "engineering");

        await group.TapAsync("design");
        await group.TapAsync("engineering");

        group.SelectedItem.ShouldBe("engineering");
        group.Current.Count.ShouldBe(1);
    }


    [Fact]
    public async Task ReClickingTheAnswerKeepsItUnlessDeselectIsAllowed()
    {
        var group = Group("design", "engineering");

        await group.TapAsync("design");
        await group.TapAsync("design");
        group.SelectedItem.ShouldBe("design");

        group.AllowDeselect = true;
        await group.TapAsync("design");
        group.SelectedItem.ShouldBeNull();
    }


    [Fact]
    public async Task SelectionModeNoneSelectsNothing()
    {
        var group = Group("design", "engineering");
        group.SelectionMode = ChipSelectionMode.None;

        await group.TapAsync("engineering");

        group.SelectedItem.ShouldBeNull();
        group.Current.ShouldBeEmpty();
    }


    // ---------------------------------------------------------------------------------------------
    // Multiple selection
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task MultipleModeTogglesEachChip()
    {
        var group = Group("design", "engineering", "sales");
        group.SelectionMode = ChipSelectionMode.Multiple;

        await group.TapAsync("design");
        await group.TapAsync("sales");
        group.Current.Count.ShouldBe(2);

        await group.TapAsync("design");
        group.Current.ShouldBe(["sales"]);
    }


    [Fact]
    public async Task TheSelectionIsReportedInSourceOrder()
    {
        var group = Group("design", "engineering", "sales");
        group.SelectionMode = ChipSelectionMode.Multiple;

        await group.TapAsync("sales");
        await group.TapAsync("design");

        group.Current.ShouldBe(["design", "sales"]);
        group.SelectedItem.ShouldBe("design");
    }


    [Fact]
    public async Task TheCapRefusesTheNewChipRatherThanDroppingAnOldOne()
    {
        var group = Group("design", "engineering", "sales");
        group.SelectionMode = ChipSelectionMode.Multiple;
        group.MaxSelectionCount = 2;

        await group.TapAsync("design");
        await group.TapAsync("engineering");
        await group.TapAsync("sales");

        group.Current.ShouldBe(["design", "engineering"]);
    }


    // ---------------------------------------------------------------------------------------------
    // Binding
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ABoundSelectedItemIsAdopted()
    {
        var group = Group("design", "engineering");
        group.SelectedItem = "engineering";

        Bound(group);

        group.IsSelected("engineering").ShouldBeTrue();
    }


    [Fact]
    public void ABoundSelectedItemTheSourceDoesNotOfferIsDropped()
    {
        var group = Group("design", "engineering");
        group.SelectedItem = "marketing";

        Bound(group);

        group.Current.ShouldBeEmpty();
    }


    [Fact]
    public void ABoundSelectedItemsListIsAdopted()
    {
        var group = Group("design", "engineering", "sales");
        group.SelectionMode = ChipSelectionMode.Multiple;
        group.SelectedItems = ["design", "sales"];

        Bound(group);

        group.Current.ShouldBe(["design", "sales"]);
        group.SelectedItem.ShouldBe("design");
    }


    [Fact]
    public async Task AParentReSupplyingTheSameSelectionDoesNotUndoAClick()
    {
        var group = Group("design", "engineering");
        group.SelectedItem = "design";
        Bound(group);

        await group.TapAsync("engineering");

        // Exactly what a re-render does: the parameters are handed back and the pass runs again.
        Bound(group);

        group.SelectedItem.ShouldBe("engineering");
    }


    [Fact]
    public async Task SelectionChangedCarriesTheWholeSelection()
    {
        var group = Group("design", "engineering");
        group.SelectionMode = ChipSelectionMode.Multiple;

        IReadOnlyList<string>? seen = null;
        group.SelectionChanged = Microsoft.AspNetCore.Components.EventCallback.Factory
            .Create<IReadOnlyList<string>>(this, x => seen = x);

        await group.TapAsync("design");
        await group.TapAsync("engineering");

        seen.ShouldBe(["design", "engineering"]);
    }


    // ---------------------------------------------------------------------------------------------
    // Removal
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task RemovingAChipTakesTheItemOutOfTheSource()
    {
        var source = new List<string> { "design", "engineering" };
        var group = new ChipGroup<string> { ItemsSource = source, AllowRemove = true };

        (await group.RemoveAsync("design")).ShouldBeTrue();

        source.ShouldBe(["engineering"]);
        group.Items.ShouldBe(["engineering"]);
    }


    [Fact]
    public async Task RefusingTheRemovalKeepsTheChip()
    {
        var source = new List<string> { "design" };
        var group = new ChipGroup<string>
        {
            ItemsSource = source,
            AllowRemove = true,
            ChipRemoving = _ => false
        };

        (await group.RemoveAsync("design")).ShouldBeFalse();
        source.ShouldBe(["design"]);
    }


    [Fact]
    public async Task RemovingTheSelectedChipClearsTheSelection()
    {
        var source = new List<string> { "design", "engineering" };
        var group = new ChipGroup<string> { ItemsSource = source, AllowRemove = true };

        await group.TapAsync("design");
        await group.RemoveAsync("design");

        group.SelectedItem.ShouldBeNull();
    }


    // ---------------------------------------------------------------------------------------------
    // Read-only
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ReadOnlyBlocksSelectionAndRemoval()
    {
        var source = new List<string> { "design", "engineering" };
        var group = new ChipGroup<string> { ItemsSource = source, AllowRemove = true, ReadOnly = true };

        await group.TapAsync("design");
        await group.RemoveAsync("design");

        group.SelectedItem.ShouldBeNull();
        source.Count.ShouldBe(2);
    }


    // ---------------------------------------------------------------------------------------------
    // The API
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task SelectAndDeselectDriveTheSameSelection()
    {
        var group = Group("design", "engineering");

        (await group.SelectAsync("engineering")).ShouldBeTrue();
        group.SelectedItem.ShouldBe("engineering");

        (await group.DeselectAsync("engineering")).ShouldBeTrue();
        group.SelectedItem.ShouldBeNull();

        (await group.SelectAsync("marketing")).ShouldBeFalse();
    }


    [Fact]
    public async Task ClearSelectionEmptiesEverything()
    {
        var group = Group("design", "engineering");
        group.SelectionMode = ChipSelectionMode.Multiple;

        await group.TapAsync("design");
        await group.TapAsync("engineering");
        await group.ClearSelectionAsync();

        group.Current.ShouldBeEmpty();
        group.SelectedItem.ShouldBeNull();
    }
}
