using System.ComponentModel;

namespace Shiny.Maui.Controls;

/// <summary>
/// A tag is about to be committed. Set <see cref="CancelEventArgs.Cancel"/> to refuse it.
/// </summary>
/// <remarks>
/// This is the validation seam. It fires after the text has been trimmed and after the duplicate and
/// <see cref="TagEntry.MaxTags"/> rules have had their say, so a handler only ever sees a tag that would
/// otherwise have been added — and cancelling leaves the typed text where it is, so the user can correct
/// it rather than retype it.
/// </remarks>
public class TagAddingEventArgs(string tag) : CancelEventArgs
{
    /// <summary>The tag as it would be committed.</summary>
    public string Tag { get; } = tag;
}


/// <summary>A tag was committed or removed.</summary>
public class TagEventArgs(string tag, int index) : EventArgs
{
    /// <summary>The tag.</summary>
    public string Tag { get; } = tag;

    /// <summary>Where it sat in the list — the index it was added at, or the one it was removed from.</summary>
    public int Index { get; } = index;
}
