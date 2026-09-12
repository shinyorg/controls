namespace Shiny.Maui.Controls;

/// <summary>
/// How many chips in a <see cref="ChipGroup"/> may be selected at once.
/// </summary>
/// <remarks>
/// Unlike <see cref="ButtonGroupSelectionMode"/>, whose default is <c>None</c> because a button group is
/// a row of <em>actions</em> first, selection is the reason a chip group exists — so the default here is
/// <see cref="Single"/>. <see cref="None"/> is still worth having: it turns the group into a row of
/// filters or actions that report a tap and carry no state of their own.
/// </remarks>
public enum ChipSelectionMode
{
    /// <summary>No selection. Chips report taps and nothing else.</summary>
    None,

    /// <summary>One chip at a time — a choice chip.</summary>
    Single,

    /// <summary>Any number at once — a filter chip.</summary>
    Multiple
}
