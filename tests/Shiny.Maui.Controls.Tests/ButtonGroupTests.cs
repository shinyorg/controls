using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// Cover for the two halves of a segmented control: the geometry that makes separate buttons read as one
/// control, and the optional selection layer that must leave a group of plain actions completely alone.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ButtonGroupTests
{
    public ButtonGroupTests()
    {
        TestDispatcherProvider.Install();
        TestDispatcherProvider.Instance.Timers.Clear();

        // A fresh Application per test, not `Application.Current ?? new` - Application.Current is
        // process-wide and an implicit style installed by one test would leak into the rest.
        _ = new Application();
        Application.Current!.Resources.MergedDictionaries.Add(new Themes.BasicLightTheme());
    }


    static ButtonGroup Group(int buttons, StackOrientation orientation = StackOrientation.Horizontal)
    {
        var group = new ButtonGroup { Orientation = orientation };

        for (var i = 0; i < buttons; i++)
            group.Children.Add(new ShinyButton { Text = $"Segment {i}" });

        return group;
    }

    static CornerRadius Corners(ShinyButton button)
    {
        var border = (Border)button.Content!;
        return ((RoundRectangle)border.StrokeShape!).CornerRadius;
    }

    static ShinyButton Segment(ButtonGroup group, int index) => group.Segments[index];


    // ---------------------------------------------------------------------------------------------
    // Geometry
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void HorizontalGroup_RoundsOnlyTheOuterCorners()
    {
        var group = Group(3);
        group.CornerRadius = 10;

        Corners(Segment(group, 0)).ShouldBe(new CornerRadius(10, 0, 10, 0));
        Corners(Segment(group, 1)).ShouldBe(new CornerRadius(0));
        Corners(Segment(group, 2)).ShouldBe(new CornerRadius(0, 10, 0, 10));
    }


    [Fact]
    public void VerticalGroup_RoundsTheTopAndBottom()
    {
        var group = Group(3, StackOrientation.Vertical);
        group.CornerRadius = 10;

        Corners(Segment(group, 0)).ShouldBe(new CornerRadius(10, 10, 0, 0));
        Corners(Segment(group, 1)).ShouldBe(new CornerRadius(0));
        Corners(Segment(group, 2)).ShouldBe(new CornerRadius(0, 0, 10, 10));
    }


    [Fact]
    public void TurningTheGroup_RecomputesTheCorners()
    {
        var group = Group(2);
        group.CornerRadius = 10;

        group.Orientation = StackOrientation.Vertical;

        Corners(Segment(group, 0)).ShouldBe(new CornerRadius(10, 10, 0, 0));
        Corners(Segment(group, 1)).ShouldBe(new CornerRadius(0, 0, 10, 10));
    }


    /// <summary>
    /// A lone segment is still a button. Squaring nothing is not the same as pinning all four corners to
    /// the group's radius: the button has to be handed back its own, which may be a theme token.
    /// </summary>
    [Fact]
    public void ALoneSegment_KeepsItsOwnRounding()
    {
        var group = Group(1);
        var button = Segment(group, 0);

        button.CornerRadius = 22;
        Corners(button).ShouldBe(new CornerRadius(22));
    }


    [Fact]
    public void RemovingASegment_HandsItBackItsOwnRounding()
    {
        var group = Group(2);
        group.CornerRadius = 10;

        var last = Segment(group, 1);
        last.CornerRadius = 22;
        Corners(last).ShouldBe(new CornerRadius(0, 10, 0, 10));

        group.Children.Remove(last);
        Corners(last).ShouldBe(new CornerRadius(22));
    }


    [Fact]
    public void OutlinedSegments_CollapseTheirSharedEdge()
    {
        var group = Group(3);
        group.BorderThickness = 1;

        foreach (var segment in group.Segments)
            segment.Appearance = ButtonAppearance.Outlined;

        Segment(group, 0).Margin.Left.ShouldBe(0);
        Segment(group, 1).Margin.Left.ShouldBe(-1);
        Segment(group, 2).Margin.Left.ShouldBe(-1);
    }


    /// <summary>
    /// A filled segment has no edge to double up, and pulling it into its neighbour would only overlap
    /// the two fills - visible as a seam wherever the colours differ.
    /// </summary>
    [Fact]
    public void FilledSegments_AreNotPulledTogether()
    {
        var group = Group(2);
        group.BorderThickness = 1;

        Segment(group, 1).Margin.Left.ShouldBe(0);
    }


    [Fact]
    public void CollapseBordersOff_LeavesTheEdgesAlone()
    {
        var group = Group(2);
        group.BorderThickness = 1;
        group.CollapseBorders = false;

        foreach (var segment in group.Segments)
            segment.Appearance = ButtonAppearance.Outlined;

        Segment(group, 1).Margin.Left.ShouldBe(0);
    }


    [Fact]
    public void ATextSegment_TakesTheOuterCorner()
    {
        var group = new ButtonGroup { CornerRadius = 10 };
        var text = new ButtonGroupText { Text = "USD" };
        group.Children.Add(text);
        group.Children.Add(new ShinyButton { Text = "+" });

        var border = (Border)text.Content!;
        ((RoundRectangle)border.StrokeShape!).CornerRadius.ShouldBe(new CornerRadius(10, 0, 10, 0));
    }


    [Fact]
    public void ASeparator_IsTurnedWithTheGroup()
    {
        var group = new ButtonGroup();
        var separator = new ButtonGroupSeparator();
        group.Children.Add(new ShinyButton { Text = "Follow" });
        group.Children.Add(separator);
        group.Children.Add(new ShinyButton { Text = "More" });

        separator.WidthRequest.ShouldBe(1);

        group.Orientation = StackOrientation.Vertical;
        separator.HeightRequest.ShouldBe(1);
    }


    /// <summary>
    /// A group of groups is a cluster of controls rather than one control, so the inner groups keep their
    /// own merged edges and are spaced apart instead of being joined.
    /// </summary>
    [Fact]
    public void NestedGroups_AreSpacedRatherThanJoined()
    {
        var outer = new ButtonGroup { ClusterSpacing = 8 };
        var first = Group(2);
        var second = Group(3);

        outer.Children.Add(first);
        outer.Children.Add(second);

        first.Margin.Left.ShouldBe(0);
        second.Margin.Left.ShouldBe(8);

        // And the inner groups still merged their own segments.
        Corners(Segment(second, 1)).ShouldBe(new CornerRadius(0));
    }


    // ---------------------------------------------------------------------------------------------
    // Selection
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void WithoutASelectionMode_NothingIsSelectedAndNoAppearanceIsTouched()
    {
        var group = Group(3);
        var button = Segment(group, 1);

        button.Invoke();

        group.SelectedIndex.ShouldBe(-1);
        group.SelectedIndexes.ShouldBeEmpty();
        button.IsSet(ShinyButton.AppearanceProperty).ShouldBeFalse();
    }


    [Fact]
    public void SingleSelection_MovesToTheTappedSegment()
    {
        var group = Group(3);
        group.SelectionMode = ButtonGroupSelectionMode.Single;

        Segment(group, 2).Invoke();
        group.SelectedIndex.ShouldBe(2);

        Segment(group, 0).Invoke();
        group.SelectedIndex.ShouldBe(0);
        group.SelectedIndexes.ShouldBe([0]);
    }


    [Fact]
    public void SingleSelection_IgnoresATapOnTheSelectedSegment()
    {
        var group = Group(3);
        group.SelectionMode = ButtonGroupSelectionMode.Single;

        Segment(group, 1).Invoke();
        Segment(group, 1).Invoke();

        group.SelectedIndex.ShouldBe(1);
    }


    [Fact]
    public void AllowDeselect_LetsTheSelectedSegmentClearItself()
    {
        var group = Group(3);
        group.SelectionMode = ButtonGroupSelectionMode.Single;
        group.AllowDeselect = true;

        Segment(group, 1).Invoke();
        Segment(group, 1).Invoke();

        group.SelectedIndex.ShouldBe(-1);
    }


    [Fact]
    public void MultipleSelection_Toggles()
    {
        var group = Group(3);
        group.SelectionMode = ButtonGroupSelectionMode.Multiple;

        Segment(group, 0).Invoke();
        Segment(group, 2).Invoke();
        group.SelectedIndexes.ShouldBe([0, 2]);

        Segment(group, 0).Invoke();
        group.SelectedIndexes.ShouldBe([2]);
        group.SelectedIndex.ShouldBe(2);
    }


    [Fact]
    public void SelectedIndex_DrivesTheSelectionFromAViewModel()
    {
        var group = Group(3);
        group.SelectionMode = ButtonGroupSelectionMode.Single;

        group.SelectedIndex = 2;

        group.SelectedIndexes.ShouldBe([2]);
        Segment(group, 2).Appearance.ShouldBe(ButtonAppearance.Filled);
        Segment(group, 0).Appearance.ShouldBe(ButtonAppearance.Outlined);
    }


    /// <summary>
    /// The bug a segmented picker found on a device: a segment going from Outlined to Filled kept the
    /// transparent background the outlined pass had written as a *local* value, which outranks the
    /// dynamic resource the filled pass sets. On screen the selected segment was an invisible gap with
    /// white text on white - nothing threw, and the headless tree looked perfect.
    /// </summary>
    [Fact]
    public void SelectingASegment_ActuallyPaintsItsFill()
    {
        var group = Group(3);
        group.SelectionMode = ButtonGroupSelectionMode.Single;

        var button = Segment(group, 1);
        var border = (Border)button.Content!;

        button.Appearance.ShouldBe(ButtonAppearance.Outlined);
        border.BackgroundColor.ShouldBe(Colors.Transparent);

        button.Invoke();

        button.Appearance.ShouldBe(ButtonAppearance.Filled);
        border.BackgroundColor.ShouldNotBe(Colors.Transparent);
    }


    [Fact]
    public void SelectionChanged_ReportsEveryMove()
    {
        var group = Group(3);
        group.SelectionMode = ButtonGroupSelectionMode.Single;

        var seen = new List<int>();
        group.SelectionChanged += (_, e) => seen.Add(e.SelectedIndex);

        Segment(group, 1).Invoke();
        Segment(group, 2).Invoke();

        seen.ShouldBe([1, 2]);
    }


    /// <summary>
    /// A segment that asked for an appearance of its own keeps it while unselected — one odd segment in a
    /// picker stays odd rather than being flattened to the group's default.
    /// </summary>
    [Fact]
    public void AnAuthorSetAppearance_SurvivesAsTheUnselectedLook()
    {
        var group = Group(3);
        Segment(group, 1).Appearance = ButtonAppearance.Tonal;
        group.SelectionMode = ButtonGroupSelectionMode.Single;

        Segment(group, 0).Invoke();

        Segment(group, 0).Appearance.ShouldBe(ButtonAppearance.Filled);
        Segment(group, 1).Appearance.ShouldBe(ButtonAppearance.Tonal);
        Segment(group, 2).Appearance.ShouldBe(ButtonAppearance.Outlined);
    }


    [Fact]
    public void LeavingSelectionMode_HandsTheAppearancesBack()
    {
        var group = Group(2);
        Segment(group, 0).Appearance = ButtonAppearance.Tonal;
        group.SelectionMode = ButtonGroupSelectionMode.Single;

        Segment(group, 1).Invoke();
        Segment(group, 1).Appearance.ShouldBe(ButtonAppearance.Filled);

        group.SelectionMode = ButtonGroupSelectionMode.None;

        Segment(group, 0).Appearance.ShouldBe(ButtonAppearance.Tonal);
        Segment(group, 1).IsSet(ShinyButton.AppearanceProperty).ShouldBeFalse();
    }


    /// <summary>
    /// Text segments and separators are chrome. Counting them would make the indexes a view model sees
    /// depend on where a divider happens to sit.
    /// </summary>
    [Fact]
    public void OnlyButtonsAreSelectable()
    {
        var group = new ButtonGroup { SelectionMode = ButtonGroupSelectionMode.Single };
        group.Children.Add(new ButtonGroupText { Text = "USD" });
        group.Children.Add(new ShinyButton { Text = "Buy" });
        group.Children.Add(new ButtonGroupSeparator());
        group.Children.Add(new ShinyButton { Text = "Sell" });

        group.Segments.Count.ShouldBe(2);

        group.Segments[1].Invoke();
        group.SelectedIndex.ShouldBe(1);
    }


    [Fact]
    public void RemovingTheSelectedSegment_DropsItFromTheSelection()
    {
        var group = Group(3);
        group.SelectionMode = ButtonGroupSelectionMode.Single;

        var last = Segment(group, 2);
        last.Invoke();
        group.SelectedIndex.ShouldBe(2);

        group.Children.Remove(last);
        group.SelectedIndex.ShouldBe(-1);
    }
}
