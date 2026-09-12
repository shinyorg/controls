using System.ComponentModel;

namespace Shiny.Maui.Controls;

/// <summary>A <see cref="ChipGroup"/>'s selection moved.</summary>
public class ChipSelectionChangedEventArgs(IReadOnlyList<object> selectedItems) : EventArgs
{
    /// <summary>Everything selected, in the order the items appear in the source. Empty when nothing is.</summary>
    public IReadOnlyList<object> SelectedItems { get; } = selectedItems;

    /// <summary>The first selected item, or <c>null</c> when nothing is selected.</summary>
    public object? SelectedItem => this.SelectedItems.Count == 0 ? null : this.SelectedItems[0];
}


/// <summary>A chip was tapped.</summary>
/// <remarks>
/// Raised for every tap, including one that changes nothing — a re-tap of the selected chip while
/// <see cref="ChipGroup.AllowDeselect"/> is off, or any tap at all in
/// <see cref="ChipSelectionMode.None"/>. That is the point of it: a chip that acts rather than selects
/// has no selection change to listen for.
/// </remarks>
public class ChipTappedEventArgs(object item, int index, bool isSelected) : EventArgs
{
    /// <summary>The item the chip stands for.</summary>
    public object Item { get; } = item;

    /// <summary>Where it sits in the source.</summary>
    public int Index { get; } = index;

    /// <summary>Whether the chip is selected now that the tap has been dealt with.</summary>
    public bool IsSelected { get; } = isSelected;
}


/// <summary>
/// A chip's remove affordance was tapped. Set <see cref="CancelEventArgs.Cancel"/> to keep the chip.
/// </summary>
/// <remarks>
/// This fires <em>before</em> the item leaves the source, which is what makes a confirmation prompt or a
/// server round-trip possible: cancel it, do the work, and remove the item from the collection yourself.
/// </remarks>
public class ChipRemovingEventArgs(object item, int index) : CancelEventArgs
{
    /// <summary>The item the chip stands for.</summary>
    public object Item { get; } = item;

    /// <summary>Where it sits in the source.</summary>
    public int Index { get; } = index;
}


/// <summary>A chip was removed.</summary>
public class ChipRemovedEventArgs(object item, int index) : EventArgs
{
    /// <summary>The item the chip stood for.</summary>
    public object Item { get; } = item;

    /// <summary>Where it sat in the source.</summary>
    public int Index { get; } = index;
}
