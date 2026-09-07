namespace Shiny.Controls.Diagramming;

/// <summary>
/// Something wrong with the graph that the engine worked around rather than threw on.
/// </summary>
/// <remarks>
/// Real diagram data routinely contains a dangling connection, a duplicate id, or a parent chain that
/// loops. A control that threw on one would be unusable against it, and one that silently dropped it
/// would be worse - so the diagram still draws and <see cref="DiagramModel.Issues"/> says what was
/// wrong. Nothing else will tell you: read it after a rebuild.
/// </remarks>
/// <param name="Kind">What sort of problem it is.</param>
/// <param name="Id">The node or connection id it concerns.</param>
/// <param name="Message">A description, in English, for a log or a debug overlay.</param>
public sealed record DiagramValidationIssue(DiagramIssueKind Kind, string Id, string Message)
{
    /// <summary>The issue's kind, id and message, for logs and debugger display.</summary>
    public override string ToString() => $"{this.Kind}: {this.Message}";
}
