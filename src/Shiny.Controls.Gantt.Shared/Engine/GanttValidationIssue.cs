namespace Shiny.Controls.Gantt;

/// <summary>
/// Something the engine found wrong with a plan, or with an edit to it.
/// </summary>
/// <remarks>
/// Issues are reported rather than thrown. A plan loaded from a real project database routinely
/// contains a dangling dependency or a task that misses its deadline, and a control that threw on
/// one would be unusable against real data; a control that silently dropped it would be worse. The
/// consumer decides what to surface, and <see cref="IsBlocking"/> says which ones stopped an edit
/// from being applied.
/// </remarks>
/// <param name="Code">What is wrong.</param>
/// <param name="Message">A human-readable description, in English, suitable for a tooltip or log.</param>
/// <param name="TaskId">The task involved, when there is one.</param>
/// <param name="RelatedTaskId">The other end of a dependency or hierarchy problem.</param>
/// <param name="IsBlocking">True when this prevented an edit from being applied at all.</param>
public readonly record struct GanttValidationIssue(
    GanttValidationCode Code,
    string Message,
    string? TaskId = null,
    string? RelatedTaskId = null,
    bool IsBlocking = false
)
{
    /// <summary>The issue in one line, for logs and debugger display.</summary>
    public override string ToString() =>
        $"{this.Code}{(this.TaskId is null ? "" : $" [{this.TaskId}]")}: {this.Message}";
}
