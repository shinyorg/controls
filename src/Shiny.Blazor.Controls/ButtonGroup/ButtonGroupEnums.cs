namespace Shiny.Blazor.Controls;

/// <summary>
/// Whether a <see cref="ButtonGroup"/> carries selection state, and how much of it.
/// </summary>
/// <remarks>
/// Mirrors the MAUI <c>ButtonGroupSelectionMode</c> exactly, and is declared separately for the same
/// reason <see cref="ButtonType"/> is: a host-neutral package for a handful of enums would put a second
/// assembly into every WASM payload to save nothing.
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
