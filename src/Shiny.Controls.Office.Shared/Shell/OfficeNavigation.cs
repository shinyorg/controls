namespace Shiny.Controls.Office.Shell;

/// <summary>One heading in the document, as the navigation pane lists it.</summary>
/// <param name="Id">What the editor navigates by — a paragraph index, a bookmark.</param>
/// <param name="Level">Outline level, 1 for Heading 1.</param>
/// <param name="Text">The heading's text.</param>
/// <param name="Page">The 1-based page it is on, when known.</param>
public sealed record OfficeHeading(string Id, int Level, string Text, int? Page = null);


/// <summary>A heading with the headings under it — one node of the pane's tree.</summary>
public sealed class OfficeHeadingNode
{
    internal OfficeHeadingNode(OfficeHeading heading, int depth)
    {
        this.Heading = heading;
        this.Depth = depth;
    }

    public OfficeHeading Heading { get; }

    /// <summary>How deep it is drawn — not the outline level: a Heading 3 straight under a Heading 1 is depth 1.</summary>
    public int Depth { get; }

    public List<OfficeHeadingNode> Children { get; } = [];

    public bool HasChildren => this.Children.Count > 0;
}


/// <summary>A search hit the navigation pane's Results tab lists.</summary>
/// <param name="Id">What the editor navigates by.</param>
/// <param name="Before">Text before the hit, for context.</param>
/// <param name="Match">The matched text, drawn bold.</param>
/// <param name="After">Text after the hit.</param>
/// <param name="Page">The page it is on, when known.</param>
public sealed record OfficeSearchResult(string Id, string Before, string Match, string After, int? Page = null);


/// <summary>The navigation pane's tabs.</summary>
public enum OfficeNavigationTab
{
    Headings,
    Pages,
    Results
}


/// <summary>Builds and filters the navigation pane's heading tree.</summary>
public static class OfficeHeadingTree
{
    /// <summary>
    /// Nests a flat, document-order list of headings. A heading goes under the nearest earlier heading
    /// with a lower level, so a skipped level (1 then 3) still nests rather than starting a new root.
    /// </summary>
    public static IReadOnlyList<OfficeHeadingNode> Build(IEnumerable<OfficeHeading>? headings)
    {
        var roots = new List<OfficeHeadingNode>();
        var stack = new Stack<OfficeHeadingNode>();

        foreach (var heading in headings ?? [])
        {
            while (stack.Count > 0 && stack.Peek().Heading.Level >= heading.Level)
                stack.Pop();

            var node = new OfficeHeadingNode(heading, stack.Count);

            if (stack.Count == 0)
                roots.Add(node);
            else
                stack.Peek().Children.Add(node);

            stack.Push(node);
        }

        return roots;
    }


    /// <summary>
    /// The tree flattened to the rows a pane draws, skipping the children of any node whose id is in
    /// <paramref name="collapsed"/>.
    /// </summary>
    public static IReadOnlyList<OfficeHeadingNode> Flatten(IReadOnlyList<OfficeHeadingNode> roots, ISet<string>? collapsed = null)
    {
        var rows = new List<OfficeHeadingNode>();

        void Walk(IEnumerable<OfficeHeadingNode> nodes)
        {
            foreach (var node in nodes)
            {
                rows.Add(node);
                if (collapsed?.Contains(node.Heading.Id) != true)
                    Walk(node.Children);
            }
        }

        Walk(roots);
        return rows;
    }


    /// <summary>
    /// The heading the caret is under — the last one, in document order, for which
    /// <paramref name="isAtOrBeforeCaret"/> holds. The pane highlights it as you move through the document.
    /// </summary>
    public static OfficeHeading? Current(IReadOnlyList<OfficeHeading> headings, Func<OfficeHeading, bool> isAtOrBeforeCaret)
    {
        OfficeHeading? current = null;
        foreach (var heading in headings)
        {
            if (!isAtOrBeforeCaret(heading))
                break;

            current = heading;
        }

        return current;
    }


    /// <summary>Headings whose text contains <paramref name="query"/>, in document order.</summary>
    public static IReadOnlyList<OfficeHeading> Filter(IEnumerable<OfficeHeading>? headings, string? query)
    {
        var list = headings?.ToList() ?? [];
        return string.IsNullOrWhiteSpace(query)
            ? list
            : list.Where(x => x.Text.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
    }


    /// <summary>"3 results" / "1 result" / "No results" — the line above the Results tab.</summary>
    public static string ResultCount(int count) => count switch
    {
        0 => "No results",
        1 => "1 result",
        _ => $"{count} results"
    };
}


/// <summary>
/// Which parts of the shell fit at a width. Below <see cref="CompactWidth"/> the title bar's search
/// collapses to an icon, the rulers and side panes hide and the ribbon goes simplified.
/// </summary>
public readonly record struct OfficeShellLayout(
    bool IsCompact,
    bool ShowSearchBox,
    bool ShowDocumentName,
    bool ShowRulers,
    bool ShowSidePanes,
    bool SimplifiedRibbon,
    bool ShowZoomSlider)
{
    /// <summary>The breakpoint the brief sets: 600px.</summary>
    public const double CompactWidth = 600;

    /// <summary>Below this the zoom slider gives way to just − / % / +.</summary>
    public const double NarrowWidth = 900;

    public static OfficeShellLayout For(double width)
    {
        if (width <= 0 || double.IsNaN(width))
            width = 1024;   // unmeasured: assume a desktop rather than flash the phone layout

        var compact = width < CompactWidth;
        var narrow = width < NarrowWidth;

        return new OfficeShellLayout(
            IsCompact: compact,
            ShowSearchBox: !compact,
            ShowDocumentName: width >= 400,
            ShowRulers: !compact,
            ShowSidePanes: !compact,
            SimplifiedRibbon: compact,
            ShowZoomSlider: !narrow);
    }
}
