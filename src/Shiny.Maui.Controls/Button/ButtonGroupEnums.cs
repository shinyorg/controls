namespace Shiny.Maui.Controls;

/// <summary>
/// Whether a <see cref="ButtonGroup"/> carries selection state, and how much of it.
/// </summary>
/// <remarks>
/// The default is <see cref="None"/>, which is the whole point of the enum: a button group is a way of
/// presenting related <em>actions</em> as one control, and an action has no selected state. Turning
/// selection on is opt-in, and turns the same markup into a segmented picker.
/// </remarks>
public enum ButtonGroupSelectionMode
{
    /// <summary>No selection. The group joins its segments visually and nothing else.</summary>
    None,

    /// <summary>One segment at a time, like a segmented picker.</summary>
    Single,

    /// <summary>Any number of segments at once, like a formatting toolbar's bold/italic/underline.</summary>
    Multiple
}
