namespace Shiny.Controls.FloorPlan.Tests;

public class SelectionTests
{
    [Fact]
    public void Select_RaisesOnce()
    {
        var state = new FloorPlanEditorState();
        var raised = 0;
        state.SelectionChanged += _ => raised++;

        var room = Plan.Room(0, 0);
        state.Select(room);
        state.Select(room);

        raised.ShouldBe(1);
    }


    [Fact]
    public void Select_ReplacesUnlessAdding()
    {
        var state = new FloorPlanEditorState();
        var a = Plan.Room(0, 0);
        var b = Plan.Room(200, 0);

        state.Select(a);
        state.Select(b);
        state.SelectedElements.ShouldBe([b]);

        state.Select(a, addToSelection: true);
        state.SelectedElements.Count.ShouldBe(2);
    }


    [Fact]
    public void SelectedElement_IsNullForAMultipleSelection()
    {
        var state = new FloorPlanEditorState();
        state.Select(Plan.Room(0, 0));
        state.Select(Plan.Room(200, 0), addToSelection: true);

        state.SelectedElement.ShouldBeNull();
    }


    [Fact]
    public void ToggleSelection_AddsThenRemoves()
    {
        var state = new FloorPlanEditorState();
        var room = Plan.Room(0, 0);

        state.ToggleSelection(room);
        state.IsSelected(room).ShouldBeTrue();

        state.ToggleSelection(room);
        state.IsSelected(room).ShouldBeFalse();
    }


    /// <summary>
    /// Locking is checked in the state rather than in each tool, so that a rubber band dragged over a
    /// locked backdrop does not quietly pick it up along with everything else.
    /// </summary>
    [Fact]
    public void Select_RefusesALockedElement()
    {
        var state = new FloorPlanEditorState();
        var locked = Plan.Room(0, 0);
        locked.IsLocked = true;

        state.Select(locked);

        state.SelectedElements.ShouldBeEmpty();
    }


    [Fact]
    public void SetSelection_RaisesOnceAndDropsLockedElements()
    {
        var state = new FloorPlanEditorState();
        var raised = 0;
        state.SelectionChanged += _ => raised++;

        var open = Plan.Room(0, 0);
        var locked = Plan.Room(200, 0);
        locked.IsLocked = true;

        state.SetSelection([open, locked]);

        raised.ShouldBe(1);
        state.SelectedElements.ShouldBe([open]);
    }


    [Fact]
    public void SetSelection_IsQuietWhenNothingChanged()
    {
        var state = new FloorPlanEditorState();
        var room = Plan.Room(0, 0);
        state.Select(room);

        var raised = 0;
        state.SelectionChanged += _ => raised++;
        state.SetSelection([room]);

        raised.ShouldBe(0);
    }


    [Fact]
    public void HoverChanged_OnlyFiresOnAChange()
    {
        var state = new FloorPlanEditorState();
        var raised = 0;
        state.HoverChanged += _ => raised++;

        var room = Plan.Room(0, 0);
        state.HoveredElement = room;
        state.HoveredElement = room;
        state.HoveredElement = null;

        raised.ShouldBe(2);
    }
}
