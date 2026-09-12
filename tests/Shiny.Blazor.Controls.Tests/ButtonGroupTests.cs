using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// The Blazor group's joining is CSS, so what is left to assert is the selection layer and the class
/// list that drives the stylesheet — both decided in the component rather than the renderer, so the
/// group can be driven directly.
/// </summary>
public class ButtonGroupTests
{
    static (ButtonGroup Group, ShinyButton[] Segments) Build(
        int count,
        ButtonGroupSelectionMode mode = ButtonGroupSelectionMode.None)
    {
        var group = new ButtonGroup { SelectionMode = mode };
        var segments = new ShinyButton[count];

        for (var i = 0; i < count; i++)
        {
            segments[i] = new ShinyButton { Text = $"Segment {i}" };
            group.Register(segments[i]);
        }

        return (group, segments);
    }


    // ---------------------------------------------------------------------------------------------
    // Selection
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task WithoutASelectionMode_AClickChangesNothing()
    {
        var (group, segments) = Build(3);

        await group.NotifyClickedAsync(segments[1]);

        group.SelectedIndex.ShouldBe(-1);
        group.AppearanceFor(segments[1], appearanceSupplied: false).ShouldBeNull();
    }


    [Fact]
    public async Task SingleSelection_MovesToTheClickedSegment()
    {
        var (group, segments) = Build(3, ButtonGroupSelectionMode.Single);

        await group.NotifyClickedAsync(segments[2]);
        group.SelectedIndex.ShouldBe(2);

        await group.NotifyClickedAsync(segments[0]);
        group.SelectedIndex.ShouldBe(0);
        group.SelectedIndexes.ShouldBe([0]);
    }


    [Fact]
    public async Task SingleSelection_IgnoresAClickOnTheSelectedSegment()
    {
        var (group, segments) = Build(3, ButtonGroupSelectionMode.Single);

        await group.NotifyClickedAsync(segments[1]);
        await group.NotifyClickedAsync(segments[1]);

        group.SelectedIndex.ShouldBe(1);
    }


    [Fact]
    public async Task AllowDeselect_LetsTheSelectedSegmentClearItself()
    {
        var (group, segments) = Build(3, ButtonGroupSelectionMode.Single);
        group.AllowDeselect = true;

        await group.NotifyClickedAsync(segments[1]);
        await group.NotifyClickedAsync(segments[1]);

        group.SelectedIndex.ShouldBe(-1);
        group.SelectedIndexes.ShouldBeEmpty();
    }


    [Fact]
    public async Task MultipleSelection_Toggles()
    {
        var (group, segments) = Build(3, ButtonGroupSelectionMode.Multiple);

        await group.NotifyClickedAsync(segments[0]);
        await group.NotifyClickedAsync(segments[2]);
        group.SelectedIndexes.ShouldBe([0, 2]);

        await group.NotifyClickedAsync(segments[0]);
        group.SelectedIndexes.ShouldBe([2]);
        group.SelectedIndex.ShouldBe(2);
    }


    [Fact]
    public async Task SelectAndDeselect_DriveTheSelectionDirectly()
    {
        var (group, _) = Build(3, ButtonGroupSelectionMode.Multiple);

        await group.SelectAsync(0);
        await group.SelectAsync(2);
        group.SelectedIndexes.ShouldBe([0, 2]);

        await group.DeselectAsync(0);
        group.SelectedIndexes.ShouldBe([2]);

        await group.ClearSelectionAsync();
        group.SelectedIndexes.ShouldBeEmpty();
    }


    // ---------------------------------------------------------------------------------------------
    // Appearance
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheGroupPaintsTheSelectedAndUnselectedSegments()
    {
        var (group, segments) = Build(2, ButtonGroupSelectionMode.Single);

        await group.NotifyClickedAsync(segments[0]);

        group.AppearanceFor(segments[0], false).ShouldBe(ButtonAppearance.Filled);
        group.AppearanceFor(segments[1], false).ShouldBe(ButtonAppearance.Outlined);
    }


    /// <summary>
    /// A segment that asked for an appearance of its own keeps it while unselected — one odd segment in
    /// a picker stays odd. The flag is the only way to know: the parameter's default is a real value, so
    /// a segment that never mentioned an appearance is otherwise indistinguishable from one that asked
    /// for <c>Filled</c>.
    /// </summary>
    [Fact]
    public async Task AnAuthorSetAppearance_SurvivesAsTheUnselectedLook()
    {
        var (group, segments) = Build(2, ButtonGroupSelectionMode.Single);
        segments[1].Appearance = ButtonAppearance.Tonal;

        await group.NotifyClickedAsync(segments[0]);

        group.AppearanceFor(segments[1], appearanceSupplied: true).ShouldBe(ButtonAppearance.Tonal);
        group.AppearanceFor(segments[1], appearanceSupplied: false).ShouldBe(ButtonAppearance.Outlined);
    }


    [Fact]
    public async Task IsSelected_ReportsThePressedState()
    {
        var (group, segments) = Build(2, ButtonGroupSelectionMode.Single);

        await group.NotifyClickedAsync(segments[1]);

        group.IsSelected(segments[0]).ShouldBeFalse();
        group.IsSelected(segments[1]).ShouldBeTrue();
    }


    // ---------------------------------------------------------------------------------------------
    // Reporting
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task SelectionChanged_ReportsEveryMove()
    {
        var (group, segments) = Build(3, ButtonGroupSelectionMode.Single);

        var seen = new List<int>();
        group.SelectionChanged = Microsoft.AspNetCore.Components.EventCallback.Factory
            .Create<IReadOnlyList<int>>(new object(), x => seen.Add(x.Count == 0 ? -1 : x[0]));

        await group.NotifyClickedAsync(segments[1]);
        await group.NotifyClickedAsync(segments[2]);

        seen.ShouldBe([1, 2]);
    }


    [Fact]
    public async Task UnregisteringASegment_TakesItOutOfTheIndexing()
    {
        var (group, segments) = Build(3, ButtonGroupSelectionMode.Single);

        group.Unregister(segments[0]);

        group.IndexOf(segments[1]).ShouldBe(0);
        await group.NotifyClickedAsync(segments[1]);
        group.SelectedIndex.ShouldBe(0);
    }


    /// <summary>
    /// Registration is idempotent: a segment whose parameters are set again must not join the group a
    /// second time, or every re-render would shift the indexes a view model is bound to.
    /// </summary>
    [Fact]
    public void RegisteringTwice_DoesNotDuplicateASegment()
    {
        var (group, segments) = Build(1);

        group.Register(segments[0]);

        group.IndexOf(segments[0]).ShouldBe(0);
    }
}
