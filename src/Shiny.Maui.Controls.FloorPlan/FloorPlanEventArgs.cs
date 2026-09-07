using Shiny.Controls.FloorPlan;

namespace Shiny.Maui.Controls.FloorPlan;

/// <summary>One element - what was tapped, or what was added.</summary>
public class FloorPlanElementEventArgs : EventArgs
{
    public FloorPlanElementEventArgs(FloorPlanElement element) => this.Element = element;

    public FloorPlanElement Element { get; }
}

/// <summary>A set of elements - what was removed.</summary>
public class FloorPlanElementsEventArgs : EventArgs
{
    public FloorPlanElementsEventArgs(IReadOnlyList<FloorPlanElement> elements) => this.Elements = elements;

    public IReadOnlyList<FloorPlanElement> Elements { get; }
}

/// <summary>The selection, after it changed.</summary>
public class FloorPlanSelectionChangedEventArgs : EventArgs
{
    public FloorPlanSelectionChangedEventArgs(IReadOnlyList<FloorPlanElement> selection) => this.Selection = selection;

    public IReadOnlyList<FloorPlanElement> Selection { get; }

    /// <summary>The one selected element, or null when nothing or several things are selected.</summary>
    public FloorPlanElement? Element => this.Selection.Count == 1 ? this.Selection[0] : null;
}
