namespace Shiny.Controls.FloorPlan;

public enum FloorPlanEditorMode
{
    /// <summary>Drag to pan, tap to report an element. Nothing can be moved or created.</summary>
    View,

    /// <summary>The active tool has the pointer, and selections carry resize handles.</summary>
    Edit
}

/// <summary>
/// What the editor is doing right now - as opposed to the document, which is what it is doing it to.
/// </summary>
/// <remarks>
/// Selection changes go through the methods rather than through the list so that
/// <see cref="SelectionChanged"/> actually fires. A public mutable list would have made the event a
/// lie the first time a tool added to it directly.
/// </remarks>
public class FloorPlanEditorState
{
    readonly List<FloorPlanElement> selected = new();

    FloorPlanElement? hovered;

    public IReadOnlyList<FloorPlanElement> SelectedElements => this.selected;

    /// <summary>The single selected element, or null when nothing or several things are selected.</summary>
    public FloorPlanElement? SelectedElement => this.selected.Count == 1 ? this.selected[0] : null;

    public FloorPlanElement? HoveredElement
    {
        get => this.hovered;
        set
        {
            if (ReferenceEquals(this.hovered, value))
                return;

            this.hovered = value;
            this.HoverChanged?.Invoke(value);
        }
    }

    public IFloorPlanTool? ActiveTool { get; set; }

    public bool IsGridVisible { get; set; } = true;

    public bool SnapToGrid { get; set; } = true;

    public FloorPlanEditorMode Mode { get; set; } = FloorPlanEditorMode.Edit;

    /// <summary>Raised whenever the selected set changes, with the new set.</summary>
    public event Action<IReadOnlyList<FloorPlanElement>>? SelectionChanged;

    /// <summary>Raised when the pointer moves on or off an element.</summary>
    public event Action<FloorPlanElement?>? HoverChanged;

    public bool IsSelected(FloorPlanElement element) => this.selected.Contains(element);

    public void ClearSelection()
    {
        if (this.selected.Count == 0)
            return;

        this.selected.Clear();
        this.RaiseSelectionChanged();
    }

    /// <summary>
    /// Selects <paramref name="element"/>, replacing the current selection unless
    /// <paramref name="addToSelection"/> is set.
    /// </summary>
    /// <remarks>
    /// A locked element is not selectable. That is checked here rather than in each tool so that
    /// rubber-banding over a locked backdrop does not quietly pick it up along with everything else.
    /// </remarks>
    public void Select(FloorPlanElement element, bool addToSelection = false)
    {
        if (element.IsLocked)
            return;

        var changed = false;

        if (!addToSelection && (this.selected.Count != 1 || !ReferenceEquals(this.selected[0], element)))
        {
            this.selected.Clear();
            changed = true;
        }

        if (!this.selected.Contains(element))
        {
            this.selected.Add(element);
            changed = true;
        }

        if (changed)
            this.RaiseSelectionChanged();
    }

    public void Deselect(FloorPlanElement element)
    {
        if (this.selected.Remove(element))
            this.RaiseSelectionChanged();
    }

    public void ToggleSelection(FloorPlanElement element)
    {
        if (this.selected.Remove(element))
            this.RaiseSelectionChanged();
        else
            this.Select(element, addToSelection: true);
    }

    /// <summary>Replaces the selection wholesale, raising the event once.</summary>
    public void SetSelection(IEnumerable<FloorPlanElement> elements)
    {
        var next = elements.Where(x => !x.IsLocked).ToList();
        if (next.Count == this.selected.Count && next.SequenceEqual(this.selected))
            return;

        this.selected.Clear();
        this.selected.AddRange(next);
        this.RaiseSelectionChanged();
    }

    void RaiseSelectionChanged() => this.SelectionChanged?.Invoke(this.selected);
}
