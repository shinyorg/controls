namespace Shiny.Blazor.Controls;

/// <summary>
/// One command on a ribbon, as a command search sees it — the label, where it lives, its shortcut and
/// a way to run it without the button being on screen.
/// </summary>
/// <remarks>
/// Returned by <see cref="Ribbon.GetCommands"/>. The Blazor ribbon only renders the showing tab's
/// items, so the index is built from what has been rendered: every tab the user has opened, plus the
/// quick access row. A host that wants every command findable from the first keystroke adds the rest
/// to its own search list.
/// </remarks>
public sealed class RibbonCommandInfo
{
    internal RibbonCommandInfo(string key, string label, Func<Task> invoke)
    {
        this.Key = key;
        this.Label = label;
        this.InvokeAsync = invoke;
    }

    /// <summary>Tab, group and label joined — stable across renders, which is what de-duplicates the index.</summary>
    public string Key { get; }

    /// <summary>The item's label, or its tooltip when it has no label (an icon-only button).</summary>
    public string Label { get; }

    public string? Tooltip { get; init; }

    public string? Description { get; init; }

    public string? Shortcut { get; init; }

    /// <summary>The item's icon string, as given to it.</summary>
    public string? Icon { get; init; }

    /// <summary>The tab's title, or null for the quick access row.</summary>
    public string? TabTitle { get; init; }

    /// <summary>The tab's key, so a host can switch to it.</summary>
    public string? TabKey { get; init; }

    /// <summary>The group's title.</summary>
    public string? GroupTitle { get; init; }

    /// <summary>Whether the item was enabled when it last rendered.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>"Home › Font" — where the command lives, for a search result's second column.</summary>
    public string? Category
        => this.TabTitle is null
            ? this.GroupTitle
            : this.GroupTitle is null ? this.TabTitle : $"{this.TabTitle} › {this.GroupTitle}";

    /// <summary>Runs the command exactly as pressing its button would.</summary>
    public Func<Task> InvokeAsync { get; }
}
