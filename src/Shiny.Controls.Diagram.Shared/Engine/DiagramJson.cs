using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shiny.Controls.Diagramming;

/// <summary>A diagram in the shape it is saved and loaded in.</summary>
/// <remarks>
/// Deliberately a flat pair of lists rather than the nested model: nesting a node inside its parent
/// makes a saved file that cannot express a node with a parent it also has a connection to, and it
/// makes every diff of a saved diagram a reindented mess. The <see cref="DiagramNodeDto.ParentId"/>
/// link carries the hierarchy, which is the shape the model reassembles from anyway.
/// </remarks>
public sealed class DiagramDocument
{
    /// <summary>The nodes.</summary>
    public List<DiagramNodeDto> Nodes { get; set; } = [];

    /// <summary>The connections.</summary>
    public List<DiagramConnectionDto> Connections { get; set; } = [];
}


/// <summary>A node, as saved.</summary>
public sealed class DiagramNodeDto
{
    /// <summary>Stable identity.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The parent's id, when the node is part of a hierarchy.</summary>
    public string? ParentId { get; set; }

    /// <summary>The label.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The outline.</summary>
    public DiagramNodeShape Shape { get; set; }

    /// <summary>Left edge.</summary>
    public double X { get; set; }

    /// <summary>Top edge.</summary>
    public double Y { get; set; }

    /// <summary>Width. Zero means the control's default.</summary>
    public double Width { get; set; }

    /// <summary>Height. Zero means the control's default.</summary>
    public double Height { get; set; }

    /// <summary>Whether the saved position should survive a re-layout.</summary>
    public bool IsPinned { get; set; }

    /// <summary>Fill colour override, as a hex string.</summary>
    public string? Fill { get; set; }

    /// <summary>Outline colour override, as a hex string.</summary>
    public string? Stroke { get; set; }

    /// <summary>Label colour override, as a hex string.</summary>
    public string? TextColor { get; set; }
}


/// <summary>A connection, as saved.</summary>
public sealed class DiagramConnectionDto
{
    /// <summary>Stable identity.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The node the line leaves.</summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>The node the line arrives at.</summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>Which side the line leaves from.</summary>
    public DiagramPort SourcePort { get; set; }

    /// <summary>Which side the line arrives at.</summary>
    public DiagramPort TargetPort { get; set; }

    /// <summary>The label on the line.</summary>
    public string? Text { get; set; }

    /// <summary>Router override, when the connection does not take the diagram's.</summary>
    public DiagramConnectionRouter? Router { get; set; }

    /// <summary>What is drawn where the line leaves the source.</summary>
    public DiagramConnectionCap StartCap { get; set; }

    /// <summary>What is drawn where the line meets the target.</summary>
    public DiagramConnectionCap EndCap { get; set; } = DiagramConnectionCap.FilledArrow;

    /// <summary>Whether the line is solid, dashed or dotted.</summary>
    public DiagramConnectionStroke StrokeStyle { get; set; }

    /// <summary>Line colour override, as a hex string.</summary>
    public string? Stroke { get; set; }
}


/// <summary>
/// Serialization context for the diagram document.
/// </summary>
/// <remarks>
/// Source-generated rather than reflection-based, because both hosts are trimmed and one of them is
/// published to WebAssembly. Reflection-based <c>JsonSerializer</c> calls survive a debug run and
/// then fail only in a trimmed Release publish, which is the worst possible place to find out.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true
)]
[JsonSerializable(typeof(DiagramDocument))]
public partial class DiagramJsonContext : JsonSerializerContext;


/// <summary>
/// Saves a diagram to JSON and loads one back.
/// </summary>
/// <remarks>
/// Round-trips what a diagram <i>is</i> - the graph, the shapes, hand-placed positions - and not what
/// the engine computed from it. Routes, depths and selection are all derived on the next
/// <see cref="DiagramModel.Rebuild"/>, so saving them would only create the possibility of a file
/// disagreeing with itself.
/// </remarks>
public static class DiagramJson
{
    /// <summary>Serializes a graph.</summary>
    /// <param name="nodes">The nodes to save.</param>
    /// <param name="connections">The connections to save.</param>
    public static string Save(IEnumerable<DiagramNode> nodes, IEnumerable<DiagramConnection> connections)
    {
        var document = new DiagramDocument();

        foreach (var node in nodes)
        {
            document.Nodes.Add(new DiagramNodeDto
            {
                Id = node.Id,
                ParentId = node.ParentId,
                Text = node.Text,
                Shape = node.Shape,
                X = node.X,
                Y = node.Y,
                Width = node.Width,
                Height = node.Height,
                IsPinned = node.IsPinned,
                Fill = node.Fill,
                Stroke = node.Stroke,
                TextColor = node.TextColor
            });
        }

        foreach (var connection in connections)
        {
            document.Connections.Add(new DiagramConnectionDto
            {
                Id = connection.Id,
                SourceId = connection.SourceId,
                TargetId = connection.TargetId,
                SourcePort = connection.SourcePort,
                TargetPort = connection.TargetPort,
                Text = connection.Text,
                Router = connection.Router,
                StartCap = connection.StartCap,
                EndCap = connection.EndCap,
                StrokeStyle = connection.StrokeStyle,
                Stroke = connection.Stroke
            });
        }

        return JsonSerializer.Serialize(document, DiagramJsonContext.Default.DiagramDocument);
    }

    /// <summary>
    /// Deserializes a graph.
    /// </summary>
    /// <param name="json">The document to read.</param>
    /// <returns>
    /// The nodes and connections. Parent links are left as <see cref="DiagramNode.ParentId"/> values
    /// for <see cref="DiagramModel.Rebuild"/> to resolve, so a file whose parent ids do not all
    /// resolve still loads and reports the problem rather than failing to parse.
    /// </returns>
    /// <exception cref="JsonException">The document is not valid JSON.</exception>
    public static (List<DiagramNode> Nodes, List<DiagramConnection> Connections) Load(string json)
    {
        var document = JsonSerializer.Deserialize(json, DiagramJsonContext.Default.DiagramDocument)
            ?? new DiagramDocument();

        var nodes = new List<DiagramNode>(document.Nodes.Count);
        var connections = new List<DiagramConnection>(document.Connections.Count);

        foreach (var dto in document.Nodes)
        {
            nodes.Add(new DiagramNode
            {
                Id = dto.Id,
                ParentId = dto.ParentId,
                Text = dto.Text,
                Shape = dto.Shape,
                X = dto.X,
                Y = dto.Y,
                Width = dto.Width,
                Height = dto.Height,
                IsPinned = dto.IsPinned,
                Fill = dto.Fill,
                Stroke = dto.Stroke,
                TextColor = dto.TextColor
            });
        }

        foreach (var dto in document.Connections)
        {
            connections.Add(new DiagramConnection
            {
                Id = dto.Id,
                SourceId = dto.SourceId,
                TargetId = dto.TargetId,
                SourcePort = dto.SourcePort,
                TargetPort = dto.TargetPort,
                Text = dto.Text,
                Router = dto.Router,
                StartCap = dto.StartCap,
                EndCap = dto.EndCap,
                StrokeStyle = dto.StrokeStyle,
                Stroke = dto.Stroke
            });
        }

        return (nodes, connections);
    }
}
