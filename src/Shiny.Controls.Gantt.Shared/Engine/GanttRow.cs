namespace Shiny.Controls.Gantt;

/// <summary>
/// A task placed at a row index, which is what the two hosts actually lay out against.
/// </summary>
/// <remarks>
/// Rows exist separately from tasks because the visible sequence is not the task list: collapsed
/// summaries hide their subtrees, and both hosts need a stable index to position a bar vertically and
/// to route a dependency arrow between two rows. Recomputing that from the tree inside a layout pass
/// would be O(n) per bar.
/// </remarks>
/// <param name="Task">The task on this row.</param>
/// <param name="Index">Zero-based position among the visible rows.</param>
/// <param name="Depth">Indentation level; zero for a root.</param>
/// <param name="HasChildren">Whether the row can be expanded or collapsed.</param>
/// <param name="IsExpanded">Whether it currently is.</param>
public readonly record struct GanttRow(
    GanttTask Task,
    int Index,
    int Depth,
    bool HasChildren,
    bool IsExpanded
);
